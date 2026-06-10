using UsurperRemake.Utils;
using UsurperRemake.Systems;
using UsurperRemake.Locations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

public class WorldSimulator
{
    // Singleton instance for easy access
    private static WorldSimulator? _instance;
    public static WorldSimulator? Instance => _instance;

    private bool isRunning = false;
    public bool IsRunning => isRunning;
    private Random random = Random.Shared;

    /// <summary>
    /// Multiplier for NPC XP gains. Default 1.0 for normal mode.
    /// Set to less than 1.0 for 24/7 world sim mode to slow NPC progression.
    /// </summary>
    // v0.63.2: bumped 1.0 -> 5.0 to match the DoorMode default. See DoorMode.cs
    // for the rationale -- 14-day live telemetry showed the throttled XP
    // economy was starving NPC progression past Lv 30. The DoorMode flag
    // chain (`--npc-xp`) overrides this default in MUD mode; the static
    // value only matters when WorldSimService.NpcXpMultiplier is never set.
    public static float NpcXpMultiplier { get; set; } = 5.0f;

    /// <summary>
    /// SQL backend for online mode. Set by WorldSimService so NPC marketplace
    /// operations can use the shared auction_listings table.
    /// </summary>
    public static SqlSaveBackend? SqlBackend { get; set; }

    // Get the active NPC list from NPCSpawnSystem instead of caching
    // This ensures we always use the current list even after save/load
    private List<NPC> npcs => UsurperRemake.Systems.NPCSpawnSystem.Instance?.ActiveNPCs ?? new List<NPC>();

    private const float SIMULATION_INTERVAL = 30.0f; // seconds between simulation steps (reduced for faster respawns)
    private const int MAX_TEAM_SIZE = 5; // Maximum members per team (from Pascal)
    private const int NPC_RESPAWN_TICKS = 20; // Respawn dead NPCs after 20 simulation ticks (~10 min)

    // Track dead NPCs for respawn
    private Dictionary<string, int> deadNPCRespawnTimers = new();

    /// <summary>
    /// Set by WorldSimService when a player's team controls city turf but the player is offline.
    /// The world sim can't see the player, so this flag prevents NPC teams from overriding player turf.
    /// </summary>
    public string? PlayerTurfTeam { get; set; } = null;

    /// <summary>
    /// Tracks team names that belong to players. Used by world sim to protect player teams
    /// from NPC AI dissolution, betrayal, and unauthorized recruitment.
    /// Thread-safe because world sim runs on a background thread.
    /// In MUD mode, multiple players may have teams simultaneously.
    /// </summary>
    private static readonly HashSet<string> _playerTeamNames = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _teamLock = new();

    public static void RegisterPlayerTeam(string teamName)
    {
        if (string.IsNullOrEmpty(teamName)) return;
        lock (_teamLock) { _playerTeamNames.Add(teamName); }
    }

    public static void UnregisterPlayerTeam(string teamName)
    {
        if (string.IsNullOrEmpty(teamName)) return;
        lock (_teamLock) { _playerTeamNames.Remove(teamName); }
    }

    public static bool IsPlayerTeam(string? teamName)
    {
        if (string.IsNullOrEmpty(teamName)) return false;
        lock (_teamLock) { return _playerTeamNames.Contains(teamName); }
    }

    /// <summary>
    /// v0.64.0 Brain v2 Slice 4: Tier A triage. NPCs that should route through
    /// the real-combat NPCCombatSimulator (abilities, potions, real damage
    /// formulas) instead of the abstract Math.Max sim. The bar is set to
    /// roughly 50 NPCs in a 200-NPC population: named/notable + player-adjacent.
    /// Tier B (everyone else) stays on the abstract sim -- cheap, fast,
    /// telemetry-comparable. Throughput envelope from the combat-reviewer audit
    /// is ~50-80 real combat runs per 60s tick; 50 NPCs * 12% team_dungeon
    /// weight = ~6 real combats per tick at the busy band, well inside budget.
    /// </summary>
    public static bool IsTierANPC(NPC npc)
    {
        if (npc == null) return false;

        // Kings are story-relevant.
        if (npc.King) return true;

        // NPCs on a player's team get real combat (their performance affects player play).
        if (IsPlayerTeam(npc.Team)) return true;

        // v0.64.0 Brain v2 Slice 7b: Court members (Royal Advisor, Spymaster,
        // Marshal, Royal Chaplain, etc.) get real combat. These NPCs are
        // narratively prominent and players interact with them at Castle. A
        // court Spymaster getting killed by an abstract-sim damage trade in
        // some random dungeon is bad theater; they deserve real combat where
        // their abilities matter.
        try
        {
            var king = CastleLocation.GetCurrentKing();
            if (king?.CourtMembers != null && king.CourtMembers.Count > 0)
            {
                string npcName = npc.Name2 ?? npc.Name1 ?? "";
                if (king.CourtMembers.Any(c => string.Equals(c.Name, npcName, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
        }
        catch
        {
            // CastleLocation.GetCurrentKing can throw during startup / partial
            // restore. Fall through to remaining criteria.
        }

        // High-level NPCs (Lv 30+) get real combat. Most named/notable NPCs
        // climb past this threshold; low-level immigrants stay on abstract sim
        // until they earn the tier promotion.
        if (npc.Level >= 30) return true;

        // Immortals and players (shouldn't see one here, but defensive).
        if (npc is Player) return true;

        return false;
    }

    // NPC Sleep Cycle: tracks which NPCs are currently sleeping and where
    // Key = NPC name, Value = location ("dormitory" or "inn")
    private static readonly Dictionary<string, string> _sleepingNPCs = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _sleepLock = new();
    private int _lastSleepCycleTick = 0;
    private const int SLEEP_CYCLE_INTERVAL = 10; // Every 10 ticks (~5 min), rotate sleepers
    private const int MAX_SLEEPING_NPCS = 8; // Max NPCs sleeping at any time
    private const double NPC_SLEEP_CHANCE = 0.03; // 3% chance per tick for eligible NPC to sleep
    private const int NPC_SLEEP_DURATION_TICKS = 20; // NPCs sleep for ~10 min (20 ticks)
    private readonly Dictionary<string, int> _npcSleepStartTick = new();

    public static List<string> GetSleepingNPCsAt(string location)
    {
        lock (_sleepLock)
        {
            return _sleepingNPCs
                .Where(kvp => kvp.Value.Equals(location, StringComparison.OrdinalIgnoreCase))
                .Select(kvp => kvp.Key)
                .ToList();
        }
    }

    public static void WakeUpNPC(string npcName)
    {
        lock (_sleepLock)
        {
            _sleepingNPCs.Remove(npcName);
        }
    }

    // Gossip system - pool of recent events NPCs can spread as rumors
    private class GossipItem
    {
        public string Text { get; set; } = "";
        public int TimesShared { get; set; }
        public int MaxShares { get; set; } // 2-3 shares before it's old news
    }
    private static readonly List<GossipItem> _gossipPool = new();
    private const int MaxGossipPoolSize = 20;

    // Emotional cascade rate limiting - tick counter per NPC
    private static readonly Dictionary<string, int> _lastCascadeTick = new();
    private static int _currentTick = 0;

    /// <summary>
    /// When true, the simulator is running in catch-up mode (fast-forward).
    /// Skips player reputation spread and reduces logging.
    /// </summary>
    public bool IsCatchUpMode { get; set; }

    // === Online mode rate-limiting (persistent server needs realistic pacing) ===
    // Per-NPC combat count per "sim day" (resets every TICKS_PER_SIM_DAY ticks)
    private readonly Dictionary<string, int> _npcDailyCombats = new();
    // Per-pair escalation cooldown: "npcA|npcB" -> tick when last escalation happened
    private readonly Dictionary<string, int> _pairEscalationCooldown = new();
    // Per-pair tension message cooldown: "npcA|npcB" -> tick when last "tensions rising" message sent
    private readonly Dictionary<string, int> _tensionMessageCooldown = new();
    // Per-NPC last team action tick (join/form/recruit) to prevent team churn
    private readonly Dictionary<string, int> _npcTeamActionCooldown = new();
    // Tick of last daily reset for rate-limiting counters
    private int _lastDailyResetTick = 0;
    // One "sim day" = ~1 hour of real time (120 ticks at 30s each)
    private const int TICKS_PER_SIM_DAY = 120;
    // Max combats per NPC per sim-day in online mode
    private const int MAX_DAILY_COMBATS_ONLINE = 3;
    // Cooldown ticks between escalations for same enemy pair (~5 min)
    private const int PAIR_ESCALATION_COOLDOWN_TICKS = 10;
    // Cooldown ticks between "tensions rising" messages for same pair (~1 hour at 30s intervals)
    private const int TENSION_MESSAGE_COOLDOWN_TICKS = 120;
    // Max tension messages per sim tick in online mode (prevents feed domination)
    private const int MAX_TENSION_MESSAGES_PER_TICK = 2;
    // Cooldown ticks before an NPC can do another team action (~15 min)
    private const int TEAM_ACTION_COOLDOWN_TICKS = 30;

    public WorldSimulator()
    {
        _instance = this;
    }

    /// <summary>
    /// Get a canonical key for an NPC pair (order-independent) for cooldown tracking.
    /// </summary>
    private static string GetPairKey(string id1, string id2) =>
        string.CompareOrdinal(id1, id2) <= 0 ? $"{id1}|{id2}" : $"{id2}|{id1}";

    /// <summary>
    /// Reset daily rate-limiting counters. Called every TICKS_PER_SIM_DAY ticks.
    /// </summary>
    private void ResetDailyCounters()
    {
        _npcDailyCombats.Clear();
        // Prune expired cooldowns (older than their respective thresholds)
        var expiredPairs = _pairEscalationCooldown.Where(kv => _currentTick - kv.Value > PAIR_ESCALATION_COOLDOWN_TICKS).Select(kv => kv.Key).ToList();
        foreach (var key in expiredPairs) _pairEscalationCooldown.Remove(key);
        var expiredTensions = _tensionMessageCooldown.Where(kv => _currentTick - kv.Value > TENSION_MESSAGE_COOLDOWN_TICKS).Select(kv => kv.Key).ToList();
        foreach (var key in expiredTensions) _tensionMessageCooldown.Remove(key);
        var expiredTeam = _npcTeamActionCooldown.Where(kv => _currentTick - kv.Value > TEAM_ACTION_COOLDOWN_TICKS).Select(kv => kv.Key).ToList();
        foreach (var key in expiredTeam) _npcTeamActionCooldown.Remove(key);
        SocialInfluenceSystem.Instance?.ResetDailyCounters();
    }

    /// <summary>
    /// Check if an NPC has hit their daily combat cap (online mode only).
    /// </summary>
    private bool HasHitDailyCombatCap(string npcId)
    {
        if (!UsurperRemake.BBS.DoorMode.IsOnlineMode) return false;
        return _npcDailyCombats.TryGetValue(npcId, out int count) && count >= MAX_DAILY_COMBATS_ONLINE;
    }

    /// <summary>
    /// Record a combat for daily cap tracking.
    /// </summary>
    private void RecordCombat(string npcId)
    {
        if (!_npcDailyCombats.ContainsKey(npcId))
            _npcDailyCombats[npcId] = 0;
        _npcDailyCombats[npcId]++;
    }

    /// <summary>
    /// Add a gossip item to the pool. Sociable NPCs will spread it via news later.
    /// </summary>
    public static void AddGossip(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        // Avoid duplicate gossip
        if (_gossipPool.Any(g => g.Text == text)) return;
        _gossipPool.Add(new GossipItem { Text = text, TimesShared = 0, MaxShares = 2 + Random.Shared.Next(2) });
        while (_gossipPool.Count > MaxGossipPoolSize)
            _gossipPool.RemoveAt(0);
    }

    // Team name generators for NPC-formed teams - Ocean/Manwe themed for lore
    private static readonly string[] TeamNamePrefixes = new[]
    {
        "The Tidal", "The Azure", "The Storm", "The Deep", "The Salt",
        "The Wave", "The Coral", "The Tempest", "The Abyssal", "The Pearl",
        "The Manwe", "The Seafoam", "The Riptide", "The Trident", "The Nautical",
        "The Leviathan", "The Kraken", "The Siren", "The Maritime", "The Oceanic"
    };

    private static readonly string[] TeamNameSuffixes = new[]
    {
        "Tide", "Current", "Mariners", "Sailors", "Corsairs",
        "Navigators", "Voyagers", "Depths", "Wanderers", "Brotherhood",
        "Covenant", "Order", "Guild", "Company", "Alliance",
        "Conclave", "Fellowship", "Syndicate", "Circle", "Legion"
    };

    // Location names that match actual game locations
    private static readonly string[] GameLocations = new[]
    {
        "Main Street", "Dungeon", "Weapon Shop", "Armor Shop", "Magic Shop",
        "Healer", "Inn", "Temple", "Church", "Auction House", "Castle", "Love Street", "Bank"
    };
    
    public void StartSimulation(List<NPC>? worldNPCs = null)
    {
        // Note: The worldNPCs parameter is ignored - we always use NPCSpawnSystem.Instance.ActiveNPCs
        // This ensures the simulator sees the correct NPCs even after save/load
        isRunning = true;

        // Initialize social emergence systems
        if (SocialInfluenceSystem.Instance == null)
            new SocialInfluenceSystem();
        if (CulturalMemeSystem.Instance == null)
            new CulturalMemeSystem();

        // Clean up orphaned marriages where one or both NPCs are permanently dead
        CleanUpDeadNPCMarriages();

        UsurperRemake.Systems.DebugLogger.Instance.LogInfo("WORLD", $"WorldSimulator starting - NPCs available: {npcs?.Count ?? 0}");

        // Start a background task to periodically run simulation steps.
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            UsurperRemake.Systems.DebugLogger.Instance.LogInfo("WORLD", "Background simulation task started");
            while (isRunning)
            {
                try
                {
                    SimulateStep();
                }
                catch (Exception ex)
                {
                    UsurperRemake.Systems.DebugLogger.Instance.LogError("WORLD", $"Simulation error: {ex.Message}\n{ex.StackTrace}");
                }
                await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(SIMULATION_INTERVAL));
            }
            UsurperRemake.Systems.DebugLogger.Instance.LogInfo("WORLD", "Background simulation task stopped");
        });
    }
    
    public void StopSimulation()
    {
        isRunning = false;
        // GD.Print("[WorldSim] Simulation stopped");
    }

    /// <summary>
    /// Mark the simulator as active without starting the internal background loop.
    /// Used by WorldSimService which drives SimulateStep() externally.
    /// </summary>
    public void SetActive(bool active)
    {
        isRunning = active;
    }
    
    public void SimulateStep()
    {
        if (!isRunning || npcs == null) return;

        // Suppress per-tick debug logging during catch-up (would generate 20k+ log lines)
        if (!IsCatchUpMode)
        {
            var aliveCount = npcs.Count(n => n.IsAlive && !n.IsDead);
            var deadCount = npcs.Count(n => !n.IsAlive || n.IsDead);
            UsurperRemake.Systems.DebugLogger.Instance.LogDebug("WORLD", $"SimulateStep: {aliveCount} alive, {deadCount} dead, {deadNPCRespawnTimers.Count} in respawn queue");
        }

        // Handle NPC respawns
        ProcessNPCRespawns();

        // Process child aging (children age and eventually become adult NPCs)
        FamilySystem.Instance?.ProcessDailyAging();

        // Process NPC aging and natural death
        ProcessNPCAging();

        // Process orphan aging in the Royal Orphanage
        ProcessOrphanAging();

        // During catch-up, probability-based systems (pregnancies, divorces, NPC AI combat,
        // relationships) are tuned for 30-second intervals. Running them every tick in a tight
        // loop would cause 100% divorce rates, population explosions, etc.
        // Only run these every 60 ticks during catch-up (~30 min of sim time per invocation).
        bool runVolatileSystems = !IsCatchUpMode || (_currentTick % 60 == 0);

        if (runVolatileSystems)
        {
            // Process NPC immigration (replenish extinct/critical races)
            // Gated during catch-up to prevent population explosion
            ProcessNPCImmigration();

            // Process NPC pregnancies and births (including affairs)
            ProcessNPCPregnancies();

            // Process NPC divorces
            ProcessNPCDivorces();

            // v0.64.0 hygiene: prune permanently-dead NPCs (aged death,
            // permadeath) whose grace window has elapsed. Without this,
            // the npcs list grows unbounded over time as corpses
            // accumulate and never get garbage-collected. Lazy / cheap:
            // walks the list once, removes anything older than the grace
            // window. Runs every 120 ticks (~1 hour at 30-sec ticks) so
            // the cost is negligible.
            if (_currentTick % 120 == 0) PrunePermanentlyDeadNPCs();
        }

        // Process NPC sleep cycle (some NPCs go to sleep, others wake up)
        if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
            ProcessNPCSleepCycle();

        // NPC attacks on sleeping players disabled — punishes casual players
        // who can't play in long sessions. Player-vs-player sleep attacks
        // ([K] Attack Sleeper at Inn/Dormitory) remain available as a PvP choice.
        // if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
        //     ProcessNPCAttacksOnSleepers();

        // NPC AI decisions, activities, and relationships are all probability-based
        // per-tick systems. Gate the entire block during catch-up.
        if (runVolatileSystems)
        {
            var worldState = new WorldState(npcs);

            foreach (var npc in npcs.Where(n => n.IsAlive && n.Brain != null))
            {
                try
                {
                    if (npc.IsAIDriven)
                    {
                        // v0.64.0 Brain v2 Slice 2 + 3: goal-driven utility scorer
                        // cohort. Brain.DecideNextAction is skipped entirely --
                        // its 15-minute cooldown gated goal-state updates too
                        // tightly (29/30 ticks returned Continue without running
                        // UpdateGoals). BrainV2ProcessActivities runs the goal /
                        // emotional / memory updates inline before scoring, so
                        // life-event goal promotion (KilledMyParent -> Avenge,
                        // FamilyMemberBorn -> Protect Family, LostFamilyMember ->
                        // Mourn) fires the same tick the scorer reads goals.
                        BrainV2ProcessActivities(npc, worldState);
                    }
                    else
                    {
                        // Legacy heuristic cohort: existing behavior preserved exactly.
                        // Brain output runs through ExecuteNPCAction's thin handler set
                        // and the weighted-Markov picker runs after.
                        var action = npc.Brain.DecideNextAction(worldState);
                        ExecuteNPCAction(npc, action, worldState);
                        ProcessNPCActivities(npc, worldState);
                    }

                    // Process NPC relationships (marriages, friendships, enemies)
                    EnhancedNPCBehaviors.ProcessNPCRelationships(npc, npcs);
                }
                catch (Exception ex)
                {
                    UsurperRemake.Systems.DebugLogger.Instance.LogError("WORLD", $"Error processing NPC {npc.Name}: {ex.Message}");
                }
            }
        }

        // Spread player reputation through NPC network (skip during catch-up — player wasn't present)
        if (!IsCatchUpMode)
        {
            var currentPlayer = GameEngine.Instance?.CurrentPlayer;
            if (currentPlayer != null)
                SocialInfluenceSystem.Instance?.ProcessPlayerReputationSpread(npcs, currentPlayer.Name, _currentTick);
        }

        // Track dead NPCs for respawn (check both HP <= 0 and IsDead flag)
        // Skip age-dead and perma-dead NPCs - they don't come back
        foreach (var npc in npcs.Where(n => (!n.IsAlive || n.IsDead) && !n.IsAgedDeath && !n.IsPermaDead))
        {
            var respawnKey = npc.Id ?? npc.Name;
            if (!deadNPCRespawnTimers.ContainsKey(respawnKey))
            {
                deadNPCRespawnTimers[respawnKey] = NPC_RESPAWN_TICKS;
                if (!IsCatchUpMode)
                    UsurperRemake.Systems.DebugLogger.Instance.LogDebug("NPC", $"{npc.Name} added to respawn queue ({NPC_RESPAWN_TICKS} ticks)");
            }
        }

        // Tick counter (always incremented, even during catch-up, for rate-limiters)
        _currentTick++;
        // Periodically prune cascade rate-limiter to free memory from dead/removed NPCs
        if (_currentTick % 100 == 0)
            _lastCascadeTick.Clear();
        // Reset daily rate-limiting counters every sim-day (online mode)
        if (UsurperRemake.BBS.DoorMode.IsOnlineMode && _currentTick - _lastDailyResetTick >= TICKS_PER_SIM_DAY)
        {
            _lastDailyResetTick = _currentTick;
            ResetDailyCounters();
        }

        if (runVolatileSystems)
        {
            // Update emotional states from recent memories
            foreach (var npc in npcs.Where(n => n.IsAlive && !n.IsDead && n.EmotionalState != null))
            {
                var recentMems = npc.Brain?.Memory?.AllMemories?.Where(m => m.IsRecent(2)).ToList()
                    ?? new List<MemoryEvent>();
                npc.EmotionalState.Update(recentMems);
            }

            // Process emotional cascades - strong emotions spread to nearby NPCs
            foreach (var npc in npcs.Where(n => n.IsAlive && !n.IsDead && n.EmotionalState != null))
            {
                ProcessEmotionalCascades(npc);
            }

            // Process gossip spreading
            ProcessGossip();

            // Process social emergence systems (v0.42.0)
            SocialInfluenceSystem.Instance?.ProcessOpinionPropagation(npcs, _currentTick);
            SocialInfluenceSystem.Instance?.ProcessFactionRecruitment(npcs, _currentTick);
            SocialInfluenceSystem.Instance?.ProcessRoleAdaptation(npcs, _currentTick);
            CulturalMemeSystem.Instance?.GenerateNewMemes(npcs, _currentTick);
            CulturalMemeSystem.Instance?.ProcessMemeSpreading(npcs, _currentTick);
            CulturalMemeSystem.Instance?.DecayMemes();
        }

        // Process world events (gated during catch-up to avoid ~1000 flavor text entries)
        if (runVolatileSystems)
            ProcessWorldEvents();

        // Update relationships and social dynamics
        if (runVolatileSystems)
            UpdateSocialDynamics();

        // Process NPC settlement (autonomous town-building)
        // Gated during catch-up — gold contributions and construction run per-tick
        if (runVolatileSystems)
            SettlementSystem.Instance?.ProcessTick(npcs.Where(n => n.IsAlive && !n.IsDead).ToList());
    }

    /// <summary>
    /// Check if a dying NPC is the current king. If so, vacate the throne.
    /// </summary>
    private static void CheckKingDeath(NPC npc)
    {
        var king = CastleLocation.GetCurrentKing();
        if (king == null || !king.IsActive) return;

        string npcName = npc.Name2 ?? npc.Name;
        if (king.Name == npcName || king.Name == npc.Name)
        {
            CastleLocation.VacateThrone(npc.IsAgedDeath
                ? "The ruler has died of old age."
                : "The ruler has fallen in battle.");
        }
    }

    /// <summary>
    /// Queue an NPC for respawn immediately (call when NPC dies in combat)
    /// </summary>
    public void QueueNPCForRespawn(string npcName, int ticks = -1)
    {
        if (string.IsNullOrEmpty(npcName)) return;

        int respawnTicks = ticks > 0 ? ticks : NPC_RESPAWN_TICKS;

        if (!deadNPCRespawnTimers.ContainsKey(npcName))
        {
            deadNPCRespawnTimers[npcName] = respawnTicks;
            UsurperRemake.Systems.DebugLogger.Instance.LogInfo("NPC", $"Queued {npcName} for respawn ({respawnTicks} ticks = ~{respawnTicks * SIMULATION_INTERVAL / 60:F1} min)");
        }
    }

    /// <summary>
    /// Clear the respawn queue - call when loading a different save
    /// </summary>
    public void ClearRespawnQueue()
    {
        if (deadNPCRespawnTimers.Count > 0)
        {
            UsurperRemake.Systems.DebugLogger.Instance.LogDebug("NPC", $"Clearing respawn queue ({deadNPCRespawnTimers.Count} entries)");
            deadNPCRespawnTimers.Clear();
        }
    }

    /// <summary>
    /// Roll whether an NPC death becomes permanent. Story NPCs and population-floor-protected NPCs are exempt.
    /// </summary>
    /// <param name="npc">The NPC who died</param>
    /// <param name="baseChance">Base permadeath chance (0.0 to 1.0)</param>
    /// <returns>True if the death should be permanent</returns>
    public bool RollPermadeath(NPC npc, float baseChance)
    {
        if (npc == null) return false;

        // Story NPCs are always exempt
        if (npc.IsStoryNPC) return false;

        // Population floor — don't let the world get too empty
        int aliveCount = npcs.Count(n => n.IsAlive && !n.IsDead && !n.IsAgedDeath && !n.IsPermaDead);
        if (aliveCount < GameConfig.PermadeathPopulationFloor) return false;

        // Race floor — prevent any race from going extinct
        int raceAlive = npcs.Count(n => n.IsAlive && !n.IsDead && !n.IsAgedDeath && !n.IsPermaDead && n.Race == npc.Race);
        if (raceAlive <= GameConfig.PermadeathRaceFloor) return false;

        // Higher-level NPCs are harder to permanently kill
        float levelReduction = npc.Level * GameConfig.PermadeathLevelReduction;
        float finalChance = Math.Max(0.01f, baseChance * (1f - levelReduction));

        return random.NextDouble() < finalChance;
    }

    /// <summary>
    /// v0.64.0 hygiene: garbage-collect permanently-dead NPCs from the
    /// `npcs` list after their grace window has elapsed. Without this,
    /// IsAgedDeath / IsPermaDead corpses accumulate forever in
    /// `NPCSpawnSystem.ActiveNPCs`, eventually pushing the list past the
    /// MaxNPCPopulation cap and silently disabling immigration, births,
    /// and orphan graduation (the bug this method fixes the long tail of).
    ///
    /// Pruning criteria: corpse must be permanently dead (IsAgedDeath or
    /// IsPermaDead) AND either have no DeathDate set (legacy corpses from
    /// before this field was introduced -- pruned on first run) OR have a
    /// DeathDate older than the grace window (7 days, matching the
    /// `/restore` window and the DialogueEnhancer grief memory window).
    ///
    /// The grace window protects recent-death lookups: news feed entries
    /// from the past week can still resolve the deceased's NPC record;
    /// adult children who lost a parent in the past week still find them
    /// for grief / cinematic purposes. After 7 days the corpse is purely
    /// historical and its name lives on in the children's lineage fields
    /// (which are strings on the children's records, independent of
    /// whether the parent's NPC record still exists).
    /// </summary>
    private void PrunePermanentlyDeadNPCs()
    {
        try
        {
            var spawnSystem = UsurperRemake.Systems.NPCSpawnSystem.Instance;
            if (spawnSystem?.ActiveNPCs == null) return;

            var cutoff = DateTime.UtcNow.AddDays(-7);
            var toRemove = spawnSystem.ActiveNPCs
                .Where(n => (n.IsAgedDeath || n.IsPermaDead)
                            && (n.DeathDate == null || n.DeathDate.Value < cutoff))
                .ToList();

            if (toRemove.Count == 0) return;

            foreach (var corpse in toRemove)
            {
                spawnSystem.ActiveNPCs.Remove(corpse);
                // Also clear the respawn timer just in case it lingered.
                deadNPCRespawnTimers.Remove(corpse.Id ?? corpse.Name);
            }

            DebugLogger.Instance.LogInfo("LIFECYCLE",
                $"PrunePermanentlyDeadNPCs: removed {toRemove.Count} aged/permadead corpse(s) " +
                $"from active list. Remaining: {spawnSystem.ActiveNPCs.Count}.");
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("LIFECYCLE",
                $"PrunePermanentlyDeadNPCs failed: {ex.Message}. Corpse pruning will retry next cycle.");
        }
    }

    /// <summary>
    /// Handle an NPC death with permadeath roll. Replaces the old IsDead=true + QueueNPCForRespawn pattern.
    /// </summary>
    /// <param name="npc">The NPC who died</param>
    /// <param name="basePermadeathChance">Base permadeath chance for this death context</param>
    /// <param name="killerName">Who killed them (for news)</param>
    /// <param name="location">Where they died</param>
    /// <returns>True if the death was permanent</returns>
    public bool MarkNPCDead(NPC npc, float basePermadeathChance, string killerName, string location)
    {
        if (npc == null) return false;

        // NPCs engaged with a player (in conversation, dungeon party, etc.) are protected
        // from world sim deaths. They can still "die" temporarily but skip permadeath.
        if (npc.IsInConversation)
        {
            // v0.64.1 spouse-death fix: blocking the death flag is not enough --
            // IsAlive is computed as HP > 0, so leaving HP at 0 still reads as
            // dead everywhere (home resolution, resurrection screen, party
            // filters). Heal like the IsPlayerTeam branch below so a "blocked"
            // death never produces a zombie 0-HP state. Bug report: spouse in
            // dungeon party showed full health in-party, then appeared dead at
            // Home after exit, three separate occurrences.
            npc.HP = Math.Max(1, npc.MaxHP / 4);
            DebugLogger.Instance.LogInfo("WORLDSIM", $"Skipping death for {npc.Name} -- engaged with player (healed to {npc.HP})");
            return false;
        }

        // NPCs on a player's team are protected from world sim kills
        if (IsPlayerTeam(npc.Team))
        {
            // Heal them back up instead of killing — they're an active team member
            npc.HP = Math.Max(1, npc.MaxHP / 4);
            DebugLogger.Instance.LogInfo("WORLDSIM", $"Skipping death for {npc.Name} — on player team '{npc.Team}'");
            return false;
        }

        npc.IsDead = true;
        npc.HP = 0;
        npc.PregnancyDueDate = null;
        npc.PregnancyFatherName = null;
        CheckKingDeath(npc);

        bool permadeath = RollPermadeath(npc, basePermadeathChance);
        if (permadeath)
        {
            // Permadeath is disabled — NPCs now respawn after death.
            // npc.IsPermaDead = true;
            // deadNPCRespawnTimers.Remove(npc.Id ?? npc.Name);

            // Permadeath news and log suppressed because permadeath is disabled.
            // NPCs will respawn, so "will not return" messages would be misleading.
            // NewsSystem.Instance?.Newsy(
            //     $"\u2620 {npc.Name} has been slain by {killerName} and will not return. The realm mourns.");

            // Witnesses record the permanent death
            SocialInfluenceSystem.RecordWitnesses(npcs, location,
                killerName, npc.Name2 ?? npc.Name, WitnessEventType.SawMurder);

            // DebugLogger.Instance.LogInfo("PERMADEATH", $"{npc.Name} permanently killed by {killerName} (chance was {basePermadeathChance:P0})");

            // v0.63.0 slice 4 (audit npc-C7): pre-fix the gate read
            // (npc.IsAgedDeath || npc.IsPermaDead) but `npc.IsPermaDead = true`
            // is commented out at line 550 (permadeath disabled in v0.53.11),
            // so the bereavement was unreachable from any combat-permadeath
            // roll. Since we're inside `if (permadeath)`, the death IS
            // permanent for bookkeeping purposes; drop the flag check so
            // HandleSpouseBereavement fires regardless of whether the flag
            // happens to be live. Aging-death path at line 896 still gates
            // on its own IsAgedDeath check independently.
            if (npc.Married || npc.IsMarried)
            {
                // v0.64.1 audit fix: notify a player spouse BEFORE bereavement
                // clears the deceased's Married/IsMarried/SpouseName flags --
                // the notification's own gate reads those flags, so calling it
                // after bereavement (as the original v0.64.1 code did) made it
                // dead code on every permanent-death path.
                NotifyPlayerSpouseOfDeath(npc, killerName, location);
                HandleSpouseBereavement(npc);
            }

            // Check if this NPC's children are now orphaned (both parents dead)
            CheckForOrphanedChildren(npc);

            // v0.63.0 multi-gen NPC family rivalries. Every living relative of
            // the deceased (children, parents, siblings) records a memory of
            // the loss. If there was a killer, the killer is added to each
            // relative's Enemies list. Children get a parent-specific memory
            // type (KilledMyParent / LostFamilyMember -> parent); other
            // relatives get the generic family types. Surfaces later as: that
            // relative refusing to talk to the killer, refusing to join the
            // killer's team, attacking on sight if Dark.
            RecordFamilyDeath(npc, killerName);
        }
        else
        {
            QueueNPCForRespawn(npc.Id ?? npc.Name);
            NewsSystem.Instance.WriteDeathNews(npc.Name, killerName, location);
        }

        // Death epitaphs disabled: budget review showed they were the single
        // largest LLM cost driver (~676 calls / 123k tokens per 24h, ~73%
        // success rate) for marginal flavor return. WriteDeathNews above
        // already posts the death; the supplemental dramatic line was not
        // worth the spend. LLMMoments.PostDeathEpitaphAsync kept in code +
        // tests for future re-enable; just no call site.

        // v0.64.1 audit fix: the spouse-death notification fires ONLY inside
        // the permadeath branch above (before HandleSpouseBereavement clears
        // the marriage flags). It deliberately does NOT fire on the temp-death
        // respawn branch -- a spouse who respawns in ~10 minutes is not a
        // widowing event, and mailing "your spouse has died" for every world-
        // sim knockdown would be both false and spammy.

        return permadeath;
    }

    /// <summary>
    /// v0.64.1: targeted death notification for a player whose NPC spouse
    /// just died. Online: in-game mail (persists; unread-mail notice fires on
    /// next login). Single-player: PendingNotifications queue (shown at next
    /// location entry). NPC-NPC marriages are handled by the existing
    /// HandleSpouseBereavement path; this is only for NPC-married-to-player.
    /// Best-effort -- failure never breaks the death cascade.
    /// </summary>
    private void NotifyPlayerSpouseOfDeath(NPC npc, string killerName, string location)
    {
        try
        {
            if (npc == null) return;
            if (!(npc.Married || npc.IsMarried)) return;
            if (string.IsNullOrWhiteSpace(npc.SpouseName)) return;

            string spouseName = npc.SpouseName.Trim();

            // v0.64.1 audit fix: the common world-sim case is an NPC-NPC
            // marriage -- SpouseName holds another NPC's display name. NPC
            // names come from finite pools, so a player whose display name
            // collides with an NPC spouse name would receive a false widow
            // mail. If the spouse name matches ANY pool NPC (alive or dead),
            // this is an NPC-NPC marriage: bereavement handles it; skip the
            // player notification entirely.
            var pool = NPCSpawnSystem.Instance?.ActiveNPCs;
            if (pool != null && pool.Any(n =>
                n != null && n != npc &&
                (string.Equals(n.Name2, spouseName, StringComparison.OrdinalIgnoreCase)
                 || string.Equals(n.Name1, spouseName, StringComparison.OrdinalIgnoreCase))))
            {
                return;
            }
            string npcName = npc.Name2 ?? npc.Name1 ?? npc.Name ?? "Your spouse";
            string killer = string.IsNullOrWhiteSpace(killerName) ? "unknown forces" : killerName;
            string place = string.IsNullOrWhiteSpace(location) ? "parts unknown" : location;
            string when = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'");
            string body = Loc.Get("worldsim.spouse_death_notice", npcName, killer, place, when);

            // Online: if the spouse name resolves to a real player, send mail.
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode && SqlBackend != null)
            {
                var username = SqlBackend.ResolvePlayerUsername(spouseName);
                if (!string.IsNullOrEmpty(username))
                {
                    _ = SqlBackend.SendMessage("The Town Crier", username, "death", body);
                    DebugLogger.Instance.LogInfo("WORLDSIM",
                        $"Spouse-death mail sent to player '{username}' for {npcName}");
                }
                return;
            }

            // Single-player: if the current player is the spouse, queue a
            // pending notification.
            var current = GameEngine.Instance?.CurrentPlayer;
            if (current != null &&
                (string.Equals(current.Name2, spouseName, StringComparison.OrdinalIgnoreCase)
                 || string.Equals(current.Name1, spouseName, StringComparison.OrdinalIgnoreCase)
                 || string.Equals(current.DisplayName, spouseName, StringComparison.OrdinalIgnoreCase)))
            {
                GameEngine.PendingNotifications.Enqueue(body);
                DebugLogger.Instance.LogInfo("WORLDSIM",
                    $"Spouse-death notification queued for single-player spouse of {npcName}");
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("WORLDSIM",
                $"Spouse-death notification failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Apply blood price consequences to a player who caused a permanent NPC death.
    /// Only for player-initiated kills (deliberate murder, dark magic). Self-defense carries no blood price.
    /// </summary>
    public static void ApplyBloodPrice(Character player, NPC victim, float weight, bool isDeliberate)
    {
        if (player == null || victim == null) return;

        // Check if the NPC was well-liked by others — extra guilt
        float avgImpression = 0f;
        var activeNPCs = NPCSpawnSystem.Instance?.ActiveNPCs;
        if (activeNPCs != null)
        {
            var impressions = activeNPCs
                .Where(n => n != victim && n.IsAlive && !n.IsDead)
                .Select(n => n.Memory?.GetCharacterImpression(victim.Name2 ?? victim.Name) ?? 0f)
                .ToList();
            if (impressions.Count > 0)
                avgImpression = impressions.Average();
        }

        float totalWeight = weight;
        if (avgImpression > 0.3f)
            totalWeight += GameConfig.MurderWeightLikedNPCBonus;

        player.MurderWeight += totalWeight;

        // Log the victim's name (cap at 20 entries)
        string victimName = victim.DisplayName ?? victim.Name2 ?? victim.Name;
        if (player.PermakillLog.Count < 20)
            player.PermakillLog.Add(victimName);

        // Darkness increase proportional to weight
        player.Darkness += (long)(totalWeight * 10);

        // Reduce all active companion loyalty
        var companionSystem = CompanionSystem.Instance;
        if (companionSystem != null)
        {
            int loyaltyLoss = isDeliberate ? GameConfig.CompanionLossPerMurder : 5;
            foreach (CompanionId id in Enum.GetValues(typeof(CompanionId)))
            {
                var companion = companionSystem.GetCompanion(id);
                if (companion != null && companion.IsActive)
                    companionSystem.ModifyLoyalty(id, -loyaltyLoss, $"witnessed the death of {victimName}");
            }
        }

        DebugLogger.Instance.LogInfo("BLOOD_PRICE",
            $"Player murder weight: {player.MurderWeight:F1} (+{totalWeight:F1}) for {victimName}" +
            (isDeliberate ? " (DELIBERATE)" : " (combat)"));
    }

    /// <summary>
    /// Force immediate processing of dead NPCs - call after loading a save
    /// This ensures dead NPCs start their respawn timers immediately and
    /// respawn NPCs that have been dead for a while (based on save data)
    /// </summary>
    public void ProcessDeadNPCsOnLoad()
    {
        // Clear any stale entries from a previous save
        ClearRespawnQueue();

        if (npcs == null || npcs.Count == 0)
        {
            UsurperRemake.Systems.DebugLogger.Instance.LogWarning("NPC", "ProcessDeadNPCsOnLoad: No NPCs found!");
            return;
        }

        var deadCount = npcs.Count(n => !n.IsAlive || n.IsDead);
        UsurperRemake.Systems.DebugLogger.Instance.LogInfo("NPC", $"ProcessDeadNPCsOnLoad: Found {deadCount} dead NPCs out of {npcs.Count} total");

        // Find all dead NPCs and add them to the respawn queue
        // Skip permanently dead NPCs (aged death and permadeath)
        foreach (var npc in npcs.Where(n => (!n.IsAlive || n.IsDead) && !n.IsAgedDeath && !n.IsPermaDead))
        {
            var respawnKey = npc.Id ?? npc.Name;
            if (!deadNPCRespawnTimers.ContainsKey(respawnKey))
            {
                // NPCs from saves respawn faster - just 2 ticks (~2 min) instead of 10
                deadNPCRespawnTimers[respawnKey] = 2;
                UsurperRemake.Systems.DebugLogger.Instance.LogDebug("NPC", $"Queued {npc.Name} for fast respawn (2 ticks)");
            }
        }

        UsurperRemake.Systems.DebugLogger.Instance.LogInfo("NPC", $"Respawn queue now has {deadNPCRespawnTimers.Count} NPCs");
    }

    /// <summary>
    /// Process NPC respawns - resurrect dead NPCs after a cooldown period
    /// </summary>
    private void ProcessNPCRespawns()
    {
        if (npcs == null) return;

        var toRespawn = new List<string>();

        foreach (var kvp in deadNPCRespawnTimers.ToList())
        {
            deadNPCRespawnTimers[kvp.Key] = kvp.Value - 1;

            if (deadNPCRespawnTimers[kvp.Key] <= 0)
            {
                toRespawn.Add(kvp.Key);
            }
        }

        foreach (var npcName in toRespawn)
        {
            var npc = npcs.FirstOrDefault(n => (n.Id ?? n.Name) == npcName);
            if (npc != null)
            {
                // Age death and permadeath are permanent - they don't come back
                if (npc.IsAgedDeath || npc.IsPermaDead)
                {
                    deadNPCRespawnTimers.Remove(npcName);
                    continue;
                }

                // Respawn the NPC - clear permanent death flag
                npc.IsDead = false;

                // CRITICAL FIX: Ensure base stats are valid before RecalculateStats
                // If base stats are 0 (from old saves or corruption), RecalculateStats
                // will zero out all stats. Fix them based on level and class.
                ValidateAndFixBaseStats(npc);

                // Recalculate stats to fix any corrupted MaxHP values
                npc.RecalculateStats();

                // Ensure MaxHP is at least a reasonable minimum based on level
                long minHP = 20 + (npc.Level * 10);
                if (npc.MaxHP < minHP)
                {
                    npc.BaseMaxHP = minHP;
                    npc.MaxHP = minHP;
                    UsurperRemake.Systems.DebugLogger.Instance.LogWarning("NPC", $"{npc.Name} had invalid MaxHP, reset to {minHP}");
                }

                // Restore full HP
                npc.HP = npc.MaxHP;
                npc.UpdateLocation("Main Street");

                // Lose some gold and XP as death penalty
                npc.Gold = Math.Max(0, npc.Gold / 2);

                NewsSystem.Instance.Newsy(true, $"{npc.Name} has returned from the realm of the dead!");
                UsurperRemake.Systems.DebugLogger.Instance.LogInfo("NPC", $"RESPAWNED: {npc.Name} (HP restored to {npc.HP}, MaxHP={npc.MaxHP}, IsDead={npc.IsDead})");
            }
            else
            {
                UsurperRemake.Systems.DebugLogger.Instance.LogWarning("NPC", $"Could not find NPC to respawn: {npcName}");
            }

            deadNPCRespawnTimers.Remove(npcName);
        }
    }

    /// <summary>
    /// Validate and fix NPC base stats if they are invalid (0 or negative).
    /// This is critical for saves from older versions where base stats weren't saved,
    /// or for corrupted NPCs. Without valid base stats, RecalculateStats() will
    /// zero out all stats causing STR: 0, DEF: -18 type issues.
    /// </summary>
    private void ValidateAndFixBaseStats(NPC npc)
    {
        bool needsFix = false;
        int level = npc.Level > 0 ? npc.Level : 1;

        // Check if base stats are invalid (0 or negative)
        if (npc.BaseStrength <= 0)
        {
            // Calculate reasonable base strength for level and class
            npc.BaseStrength = 10 + (level * 5);
            if (npc.Class == CharacterClass.Warrior || npc.Class == CharacterClass.Barbarian)
                npc.BaseStrength += level * 2;
            needsFix = true;
        }

        if (npc.BaseDefence <= 0)
        {
            npc.BaseDefence = 10 + (level * 3);
            needsFix = true;
        }

        if (npc.BaseAgility <= 0)
        {
            npc.BaseAgility = 10 + (level * 3);
            needsFix = true;
        }

        if (npc.BaseDexterity <= 0)
        {
            npc.BaseDexterity = 10 + (level * 2);
            if (npc.Class == CharacterClass.Assassin)
                npc.BaseDexterity += level * 3;
            needsFix = true;
        }

        if (npc.BaseStamina <= 0)
        {
            npc.BaseStamina = 10 + (level * 4);
            needsFix = true;
        }

        if (npc.BaseConstitution <= 0)
        {
            npc.BaseConstitution = 10 + (level * 2);
            needsFix = true;
        }

        if (npc.BaseIntelligence <= 0)
        {
            npc.BaseIntelligence = 10 + (level * 2);
            if (npc.Class == CharacterClass.Magician)
                npc.BaseIntelligence += level * 3;
            needsFix = true;
        }

        if (npc.BaseWisdom <= 0)
        {
            npc.BaseWisdom = 10 + (level * 2);
            if (npc.Class == CharacterClass.Cleric || npc.Class == CharacterClass.Paladin)
                npc.BaseWisdom += level * 2;
            needsFix = true;
        }

        if (npc.BaseCharisma <= 0)
        {
            npc.BaseCharisma = 10;
            needsFix = true;
        }

        if (npc.BaseMaxHP <= 0)
        {
            // Calculate based on class
            npc.BaseMaxHP = npc.Class switch
            {
                CharacterClass.Warrior or CharacterClass.Barbarian => 100 + (level * 50),
                CharacterClass.Magician => 50 + (level * 25),
                CharacterClass.Cleric or CharacterClass.Paladin => 80 + (level * 40),
                CharacterClass.Assassin => 70 + (level * 35),
                CharacterClass.Sage => 90 + (level * 45),
                _ => 80 + (level * 40)
            };
            needsFix = true;
        }

        if (npc.BaseMaxMana <= 0 && (npc.Class == CharacterClass.Magician ||
            npc.Class == CharacterClass.Cleric || npc.Class == CharacterClass.Paladin ||
            npc.Class == CharacterClass.Sage))
        {
            npc.BaseMaxMana = npc.Class switch
            {
                CharacterClass.Magician => 50 + (level * 30),
                CharacterClass.Cleric or CharacterClass.Paladin => 40 + (level * 20),
                CharacterClass.Sage => 30 + (level * 15),
                _ => 0
            };
            needsFix = true;
        }

        if (needsFix)
        {
            UsurperRemake.Systems.DebugLogger.Instance.LogWarning("NPC",
                $"Fixed corrupted base stats for {npc.Name} (Level {level} {npc.Class}): " +
                $"STR={npc.BaseStrength}, DEF={npc.BaseDefence}, AGI={npc.BaseAgility}");
        }
    }

    /// <summary>
    /// Process NPC aging - update ages from BirthDate and trigger natural death
    /// when an NPC exceeds their race's maximum lifespan.
    /// Age-related death is permanent - the soul has moved on.
    /// </summary>
    private void ProcessNPCAging()
    {
        foreach (var npc in npcs.Where(n => n.IsAlive && !n.IsDead && !n.IsAgedDeath).ToList())
        {
            // Skip story NPCs - they're needed for narrative quests
            if (npc.IsStoryNPC) continue;

            // Skip NPCs without birth dates (shouldn't happen, but safety)
            if (npc.BirthDate <= DateTime.MinValue) continue;

            // Calculate current age from birth date (using accelerated lifecycle rate)
            int previousAge = npc.Age;
            int currentAge = (int)((DateTime.Now - npc.BirthDate).TotalHours / GameConfig.NpcLifecycleHoursPerYear);
            npc.Age = currentAge;

            // Birthday announcement when age increments
            if (currentAge > previousAge && previousAge > 0)
            {
                NewsSystem.Instance?.WriteBirthdayNews(npc.Name2, currentAge, npc.Race.ToString());
            }

            // Check for natural death
            if (GameConfig.RaceLifespan.TryGetValue(npc.Race, out int maxAge))
            {
                if (currentAge >= maxAge)
                {
                    // v0.63.0 slice 4 (audit npc-N7): race-extinction guard.
                    // Pre-fix, aging-death could push a race below the
                    // GameConfig.PermadeathRaceFloor population floor since
                    // the natural-attrition path didn't consult the floor
                    // (only combat-permadeath did via RollPermadeath). When
                    // a race has only the floor count alive, defer this
                    // NPC's aging death by extending their birth date
                    // slightly so the floor holds. Immigration system will
                    // refill the race over time; this just buys us a tick.
                    int raceAliveCount = npcs.Count(n =>
                        n != null && n.IsAlive && !n.IsDead && !n.IsAgedDeath
                        && !n.IsPermaDead && n.Race == npc.Race);
                    if (raceAliveCount <= GameConfig.PermadeathRaceFloor)
                    {
                        // Buy one game-week of life by pushing BirthDate forward.
                        // Immigration tick should add a same-race NPC before
                        // this NPC ages again.
                        npc.BirthDate = npc.BirthDate.AddDays(7);
                        continue; // Don't process death this tick; next NPC.
                    }

                    // Natural death - permanent, no respawn
                    npc.IsDead = true;
                    npc.IsAgedDeath = true;
                    npc.DeathDate = DateTime.UtcNow; // for PrunePermanentlyDeadNPCs grace window
                    npc.HP = 0;
                    npc.PregnancyDueDate = null;
                    npc.PregnancyFatherName = null;
                    CheckKingDeath(npc);

                    // Remove from respawn queue if somehow queued
                    deadNPCRespawnTimers.Remove(npc.Id ?? npc.Name);

                    // Handle marriage - widow the spouse
                    if (npc.Married || npc.IsMarried)
                    {
                        // v0.64.1 audit fix: notify a player spouse BEFORE
                        // bereavement clears the marriage flags (the notify
                        // gate reads them). Aging path doesn't route through
                        // MarkNPCDead, so it needs its own hook.
                        NotifyPlayerSpouseOfDeath(npc, "old age", npc.CurrentLocation ?? "their home");
                        HandleSpouseBereavement(npc);
                    }

                    // v0.61.5: If the deceased was on a player's team, bequeath
                    // their equipment + inventory + gold to the team leader.
                    // The leader may be offline; we queue items to the
                    // pending_inheritance table and deliver on next login.
                    BequeathItemsToTeamLeader(npc);

                    NewsSystem.Instance?.Newsy(
                        $"⚱ {npc.Name2} has passed away peacefully at the age of {currentAge}. The soul moves on...");

                    UsurperRemake.Systems.DebugLogger.Instance.LogInfo("LIFECYCLE",
                        $"{npc.Name2} died of old age at {currentAge} (max {maxAge} for {npc.Race})");

                    // Check if this NPC's children are now orphaned (both parents dead)
                    CheckForOrphanedChildren(npc);
                }
            }
        }
    }

    /// <summary>
    /// Process NPC immigration — generates immigrants to maintain race diversity.
    /// Races below the extinction floor (2) always get immigrants.
    /// Additionally, the most underrepresented race gets 1 immigrant per tick
    /// to gradually rebalance toward equal distribution.
    /// Called once per world sim tick.
    /// </summary>
    private void ProcessNPCImmigration()
    {
        var aliveNPCs = npcs.Where(n => n.IsAlive && !n.IsDead && !n.IsAgedDeath && !n.IsPermaDead).ToList();

        // Hard cap on LIVING NPC population. Pre-fix this checked
        // `npcs.Count` (total list including permadead corpses), which
        // froze immigration in any world that had accumulated more dead
        // NPCs than the cap. Permadead corpses are inert records that
        // shouldn't block new arrivals from replacing them.
        if (aliveNPCs.Count >= GameConfig.MaxNPCPopulation) return;

        bool populationHigh = aliveNPCs.Count >= GameConfig.MaxNPCPopulation - 10; // Slow down near the cap

        // Calculate average level of alive NPCs for immigrant scaling
        int avgLevel = aliveNPCs.Count > 0 ? Math.Max(1, (int)aliveNPCs.Average(n => n.Level)) : 5;

        // Count alive NPCs per race
        int raceCount = 10; // 10 base playable races
        var raceCounts = new Dictionary<CharacterRace, int>();
        for (int i = 0; i < raceCount; i++)
            raceCounts[(CharacterRace)i] = 0;
        foreach (var npc in aliveNPCs)
        {
            if ((int)npc.Race < raceCount)
                raceCounts[npc.Race]++;
        }

        // Phase 1: Extinction prevention — races below 5 get immediate immigrants (always active)
        foreach (var kvp in raceCounts)
        {
            if (kvp.Value >= 5) continue;

            int needed = 5 - kvp.Value;

            for (int i = 0; i < needed; i++)
            {
                var sex = (i % 2 == 0) ? CharacterSex.Male : CharacterSex.Female;
                SpawnImmigrant(kvp.Key, sex, avgLevel);
            }
        }

        // Phase 2: Diversity balancing — only when population is below 80
        if (populationHigh) return;

        int targetPerRace = Math.Max(5, aliveNPCs.Count / raceCount);

        // Spawn 1 immigrant for the most underrepresented race
        // Only triggers if at least one race is below 60% of the target
        int diversityThreshold = (targetPerRace * 3) / 5;
        var mostNeeded = raceCounts
            .Where(kv => kv.Value < diversityThreshold)
            .OrderBy(kv => kv.Value)
            .FirstOrDefault();

        if (mostNeeded.Key != default || raceCounts.Values.Any(v => v == 0))
        {
            // Find the actual most underrepresented race (including those at 0)
            var targetRace = raceCounts.OrderBy(kv => kv.Value).First();
            if (targetRace.Value < diversityThreshold)
            {
                var sex = random.Next(2) == 0 ? CharacterSex.Male : CharacterSex.Female;
                SpawnImmigrant(targetRace.Key, sex, avgLevel);
            }
        }
    }

    /// <summary>
    /// Spawn a single immigrant NPC of the given race and sex.
    /// </summary>
    private void SpawnImmigrant(CharacterRace race, CharacterSex sex, int avgLevel)
    {
        var immigrant = NPCSpawnSystem.Instance?.GenerateImmigrantNPC(race, sex, avgLevel);
        if (immigrant != null)
        {
            NPCSpawnSystem.Instance?.AddRestoredNPC(immigrant);

            // v0.62.1 (article fix): race may be vowel-initial (Elf, Orc) so emit
            // "An Elf traveler" / "A Dwarf traveler" via GetIndefiniteArticle.
            NewsSystem.Instance?.Newsy(
                $"{GameConfig.GetIndefiniteArticle(race.ToString())} {race} traveler named {immigrant.Name2} has arrived in town.");

            UsurperRemake.Systems.DebugLogger.Instance.LogInfo("IMMIGRATION",
                $"Generated immigrant: {immigrant.Name2} ({race} {immigrant.Class} L{immigrant.Level} {sex})");
        }
    }

    /// <summary>
    /// v0.61.5: Bequeath a deceased NPC's belongings to their team leader (player).
    /// Called when an NPC dies of old age while on a player's team. Items, equipment,
    /// and gold are queued in the pending_inheritance table; they are delivered to
    /// the player on their next login. Online-mode only — single-player saves
    /// don't have the cross-player pending-inheritance infrastructure.
    /// </summary>
    private void BequeathItemsToTeamLeader(NPC deceased)
    {
        if (!UsurperRemake.BBS.DoorMode.IsOnlineMode) return;
        if (string.IsNullOrEmpty(deceased.Team)) return;

        try
        {
            var backend = UsurperRemake.Systems.SaveSystem.Instance.Backend as UsurperRemake.Systems.SqlSaveBackend;
            if (backend == null) return;

            // Find the player who created the team. Synchronous-blocking on async
            // is acceptable here -- this fires once per old-age death (rare event)
            // and we're inside the world-sim tick which already runs synchronously.
            var leaderTask = backend.GetTeamLeaderUsername(deceased.Team);
            leaderTask.Wait();
            var leaderUsername = leaderTask.Result;
            if (string.IsNullOrEmpty(leaderUsername)) return;

            int queued = 0;
            var jsonOpts = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                IncludeFields = true
            };

            // Equipped items: walk EquippedItems dict, resolve each ID to an Item,
            // queue. EquipmentDatabase has the full item registry.
            foreach (var kvp in deceased.EquippedItems)
            {
                if (kvp.Value <= 0) continue;
                var eq = global::EquipmentDatabase.GetById(kvp.Value);
                if (eq == null) continue;
                // Convert Equipment to Item for transfer (Item is the inventory shape).
                var item = deceased.ConvertEquipmentToLegacyItem(eq);
                if (item == null) continue;
                string itemJson = System.Text.Json.JsonSerializer.Serialize(item, jsonOpts);
                if (backend.QueueInheritance(leaderUsername, deceased.Name2 ?? deceased.Name1 ?? "Unknown", itemJson, 0))
                    queued++;
            }

            // Inventory items
            if (deceased.Inventory != null)
            {
                foreach (var item in deceased.Inventory)
                {
                    if (item == null) continue;
                    string itemJson = System.Text.Json.JsonSerializer.Serialize(item, jsonOpts);
                    if (backend.QueueInheritance(leaderUsername, deceased.Name2 ?? deceased.Name1 ?? "Unknown", itemJson, 0))
                        queued++;
                }
            }

            // Gold (single row with no item, just gold_amount)
            if (deceased.Gold > 0)
            {
                if (backend.QueueInheritance(leaderUsername, deceased.Name2 ?? deceased.Name1 ?? "Unknown", null, deceased.Gold))
                    queued++;
            }

            if (queued > 0)
            {
                UsurperRemake.Systems.DebugLogger.Instance.LogInfo("LIFECYCLE",
                    $"Bequeathed {queued} items/gold from {deceased.Name2} (team '{deceased.Team}') to leader '{leaderUsername}'.");
            }
        }
        catch (Exception ex)
        {
            UsurperRemake.Systems.DebugLogger.Instance.LogError("LIFECYCLE",
                $"Failed to bequeath items from {deceased.Name2}: {ex.Message}");
        }
    }

    /// <summary>
    /// Handle a spouse's bereavement when their partner dies.
    /// Clears marriage state on the surviving spouse and removes the registry entry.
    /// </summary>
    private void HandleSpouseBereavement(NPC deceased)
    {
        var spouse = npcs.FirstOrDefault(n =>
            n.Name2 == deceased.SpouseName && (n.Married || n.IsMarried) && !n.IsDead);
        if (spouse != null)
        {
            spouse.Married = false;
            spouse.IsMarried = false;
            spouse.SpouseName = "";
            spouse.Brain?.Memory?.AddMemory(
                $"My beloved {deceased.Name2} has passed away...", "bereavement", DateTime.Now);

            UsurperRemake.Systems.DebugLogger.Instance.LogInfo("LIFECYCLE",
                $"{spouse.Name2} is now widowed after {deceased.Name2}'s passing");
        }
        else if (!string.IsNullOrEmpty(deceased.SpouseName) && UsurperRemake.BBS.DoorMode.IsOnlineMode)
        {
            // Spouse not found in NPC list — might be a player
            // Notify the player; login cleanup will clear their marriage flags
            try
            {
                var backend = SaveSystem.Instance?.Backend as SqlSaveBackend;
                if (backend != null)
                {
                    backend.SendMessage("System", deceased.SpouseName, "system",
                        $"Your beloved {deceased.Name2} has passed away. You are now widowed.").GetAwaiter().GetResult();

                    DebugLogger.Instance.LogInfo("LIFECYCLE",
                        $"Player {deceased.SpouseName} will be widowed on login after NPC spouse {deceased.Name2}'s permadeath");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("LIFECYCLE", $"Failed to notify player {deceased.SpouseName} of spouse death: {ex.Message}");
            }
        }

        // Always clear the registry entry, even if the spouse wasn't found
        // (e.g., spouse already dead, name mismatch, or not loaded)
        NPCMarriageRegistry.Instance.EndMarriage(deceased.ID);

        // Clear RomanceTracker spouse record (for player spouse deaths)
        try { RomanceTracker.Instance?.HandleSpouseDeath(deceased.ID); }
        catch { /* RomanceTracker may not be initialized */ }

        // Clear deceased's own marriage flags
        deceased.Married = false;
        deceased.IsMarried = false;
    }

    /// <summary>
    /// Migration cleanup: remove marriages where one or both NPCs are permanently dead.
    /// Runs once on world sim startup to fix existing orphaned marriage data.
    /// </summary>
    private void CleanUpDeadNPCMarriages()
    {
        var registry = NPCMarriageRegistry.Instance;
        if (registry == null) return;

        var allMarriages = registry.GetAllMarriages();
        int cleaned = 0;

        foreach (var marriage in allMarriages)
        {
            var npc1 = npcs.FirstOrDefault(n => n.ID == marriage.Npc1Id);
            var npc2 = npcs.FirstOrDefault(n => n.ID == marriage.Npc2Id);

            bool npc1Dead = npc1 == null || npc1.IsDead || npc1.IsPermaDead;
            bool npc2Dead = npc2 == null || npc2.IsDead || npc2.IsPermaDead;

            if (npc1Dead || npc2Dead)
            {
                // End the marriage in registry
                registry.EndMarriage(marriage.Npc1Id);

                // Clear marriage flags on any surviving NPC
                if (npc1 != null && !npc1Dead)
                {
                    npc1.Married = false;
                    npc1.IsMarried = false;
                    npc1.SpouseName = "";
                }
                if (npc2 != null && !npc2Dead)
                {
                    npc2.Married = false;
                    npc2.IsMarried = false;
                    npc2.SpouseName = "";
                }

                cleaned++;
            }
        }

        if (cleaned > 0)
        {
            UsurperRemake.Systems.DebugLogger.Instance.LogInfo("WORLDSIM",
                $"Cleaned up {cleaned} orphaned marriage(s) involving dead NPCs");
        }

        // Reverse check: NPCs flagged as married but not in the registry
        int flagsCleaned = 0;
        foreach (var npc in npcs.Where(n => (n.Married || n.IsMarried) && n.IsAlive && !n.IsDead))
        {
            if (registry.IsMarriedToNPC(npc.ID) != true)
            {
                // Check if spouse exists and is alive — if not, clear the stale flag
                var spouse = !string.IsNullOrEmpty(npc.SpouseName)
                    ? npcs.FirstOrDefault(n => n.Name2 == npc.SpouseName && n.IsAlive && !n.IsDead)
                    : null;
                if (spouse == null)
                {
                    npc.Married = false;
                    npc.IsMarried = false;
                    npc.SpouseName = "";
                    flagsCleaned++;
                }
            }
        }
        if (flagsCleaned > 0)
        {
            UsurperRemake.Systems.DebugLogger.Instance.LogInfo("WORLDSIM",
                $"Cleared {flagsCleaned} stale marriage flags on NPCs not in registry");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ROYAL ORPHANAGE — Orphan Detection and Aging
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Check if a dead NPC has children whose other parent is also dead.
    /// If so, move those children to the Royal Orphanage.
    /// </summary>
    private void CheckForOrphanedChildren(NPC deadNPC)
    {
        var familySystem = FamilySystem.Instance;
        if (familySystem == null) return;

        string deadName = deadNPC.Name2 ?? deadNPC.Name;
        string deadID = deadNPC.ID ?? "";

        // Find all living children of this NPC
        var childrenOfDead = familySystem.AllChildren
            .Where(c => !c.Deleted &&
                         c.Age < 18 &&
                         c.Location != GameConfig.ChildLocationOrphanage &&
                         ((!string.IsNullOrEmpty(deadID) && (c.MotherID == deadID || c.FatherID == deadID)) ||
                          c.Mother == deadName || c.Father == deadName))
            .ToList();

        if (childrenOfDead.Count == 0) return;

        var king = CastleLocation.GetCurrentKing();

        foreach (var child in childrenOfDead)
        {
            bool otherParentDead = IsParentDeadOrMissing(child, deadID, deadName);
            if (!otherParentDead) continue;

            // Both parents dead/missing — child becomes an orphan
            child.Location = GameConfig.ChildLocationOrphanage;

            // Create RoyalOrphan record on the king's list (if king exists and space available)
            if (king != null && king.Orphans.Count < GameConfig.MaxRoyalOrphans)
            {
                // Check not already added (same child orphaned by two deaths in same tick)
                if (king.Orphans.Any(o => o.Name == child.Name && o.IsRealOrphan)) continue;

                var orphan = new RoyalOrphan
                {
                    Name = child.Name,
                    Age = child.Age,
                    Sex = child.Sex,
                    ArrivalDate = DateTime.Now,
                    BirthDate = child.BirthDate,
                    BackgroundStory = $"Both parents lost. Mother: {child.Mother}, Father: {child.Father}.",
                    Happiness = 30, // Low — just lost family
                    MotherName = child.Mother,
                    FatherName = child.Father,
                    MotherID = child.MotherID,
                    FatherID = child.FatherID,
                    Race = DetermineOrphanRace(child),
                    Soul = child.Soul,
                    IsRealOrphan = true
                };

                king.Orphans.Add(orphan);

                NewsSystem.Instance?.Newsy(
                    $"🏠 Young {child.Name}, child of the late {child.Mother} and {child.Father}, has been taken into the Royal Orphanage.");
            }
            else if (king == null)
            {
                // No king — child is flagged as orphan (Location=Orphanage) but no RoyalOrphan created yet.
                // When a new king is crowned, orphaned children will be picked up.
                DebugLogger.Instance.LogInfo("ORPHANAGE",
                    $"{child.Name} orphaned but no king exists — will be picked up when king crowned");
            }

            DebugLogger.Instance.LogInfo("ORPHANAGE",
                $"{child.Name} orphaned (parents: {child.Mother} & {child.Father})");
        }
    }

    /// <summary>
    /// Check if the other parent of a child (not the one who just died) is also dead or missing.
    /// </summary>
    private bool IsParentDeadOrMissing(Child child, string deadParentID, string deadParentName)
    {
        // Determine which parent is the "other" one
        string otherID;
        string otherName;

        bool deadIsMother = (!string.IsNullOrEmpty(deadParentID) && child.MotherID == deadParentID) ||
                            child.Mother == deadParentName;
        if (deadIsMother)
        {
            otherID = child.FatherID;
            otherName = child.Father;
        }
        else
        {
            otherID = child.MotherID;
            otherName = child.Mother;
        }

        if (string.IsNullOrEmpty(otherID) && string.IsNullOrEmpty(otherName))
            return true; // Unknown parent = effectively dead

        // Check if other parent is a player character (not an NPC).
        // In online mode, CurrentPlayer is null in the world sim context,
        // so we detect player parents by their ID format (NPC IDs start with "npc_").
        // Player characters don't permadeath, so they're always considered alive.
        var player = GameEngine.Instance?.CurrentPlayer;
        if (player != null)
        {
            if (player.DisplayName == otherName || player.Name2 == otherName)
                return false; // Player is alive, child is not orphaned
        }
        else if (!string.IsNullOrEmpty(otherID) && !otherID.StartsWith("npc_"))
        {
            // Parent has a non-NPC ID — this is a player character (always alive)
            return false;
        }

        // Check NPC list (includes dead NPCs since they stay in ActiveNPCs)
        var otherParent = npcs.FirstOrDefault(n =>
            (!string.IsNullOrEmpty(otherID) && n.ID == otherID) ||
            n.Name2 == otherName || n.Name == otherName);

        if (otherParent == null)
            return true; // Parent not found in the game at all

        return otherParent.IsDead;
    }

    /// <summary>
    /// Determine orphan's race from their parents (looks up dead parents in NPC list).
    /// </summary>
    public CharacterRace DetermineOrphanRace(Child child)
    {
        var mother = npcs.FirstOrDefault(n =>
            (!string.IsNullOrEmpty(child.MotherID) && n.ID == child.MotherID) ||
            n.Name2 == child.Mother);
        var father = npcs.FirstOrDefault(n =>
            (!string.IsNullOrEmpty(child.FatherID) && n.ID == child.FatherID) ||
            n.Name2 == child.Father);

        // 50/50 from parents
        if (mother != null && father != null)
            return random.Next(2) == 0 ? mother.Race : father.Race;
        if (mother != null) return mother.Race;
        if (father != null) return father.Race;
        return CharacterRace.Human;
    }

    /// <summary>
    /// Age orphans in the Royal Orphanage. When real orphans reach 18, process their coming-of-age.
    /// </summary>
    private void ProcessOrphanAging()
    {
        var king = CastleLocation.GetCurrentKing();
        if (king == null || king.Orphans.Count == 0) return;

        var comingOfAge = new List<RoyalOrphan>();

        foreach (var orphan in king.Orphans)
        {
            if (orphan.IsRealOrphan)
            {
                // Real orphans age from BirthDate using lifecycle rate (same as NPCs)
                orphan.Age = orphan.ComputedAge;

                if (orphan.Age >= 18)
                    comingOfAge.Add(orphan);
            }
            // Generated orphans: static Age, no aging/graduation
        }

        foreach (var orphan in comingOfAge)
        {
            ProcessOrphanComingOfAge(orphan, king);
        }
    }

    /// <summary>
    /// Process an orphan who has come of age (18+). They graduate from the orphanage
    /// and either become a royal guard or are released as a citizen NPC.
    /// </summary>
    private void ProcessOrphanComingOfAge(RoyalOrphan orphan, King king)
    {
        // Remove from orphanage
        king.Orphans.Remove(orphan);

        // Mark underlying Child as Deleted
        var child = FamilySystem.Instance?.AllChildren
            .FirstOrDefault(c => c.Name == orphan.Name && !c.Deleted &&
                                 c.Location == GameConfig.ChildLocationOrphanage);
        if (child != null)
            child.Deleted = true;

        int roll = random.Next(100);

        if (roll < 30 && king.Guards.Count < King.MaxNPCGuards)
        {
            // 30% chance: become a royal guard (if slots available)
            OrphanBecomesRoyalGuard(orphan, king);
        }
        else
        {
            // 70% chance (or guard slots full): released as citizen NPC
            OrphanBecomesNPC(orphan);
        }
    }

    /// <summary>
    /// Orphan graduates to become a Royal Guard with high loyalty (raised by the crown).
    /// </summary>
    private void OrphanBecomesRoyalGuard(RoyalOrphan orphan, King king)
    {
        var guard = new RoyalGuard
        {
            Name = orphan.Name,
            AI = CharacterAI.Computer,
            Sex = orphan.Sex,
            DailySalary = GameConfig.BaseGuardSalary,
            RecruitmentDate = DateTime.Now,
            Loyalty = 85 // High loyalty — raised by the crown
        };
        king.Guards.Add(guard);

        // Also create the NPC entity so the guard has real combat stats
        OrphanBecomesNPC(orphan);

        string guardPrefix = GameConfig.ScreenReaderMode ? "" : "⚔ ";
        NewsSystem.Instance?.Newsy(
            $"{guardPrefix}{orphan.Name}, raised in the Royal Orphanage, has come of age and joined the Royal Guard!");

        DebugLogger.Instance.LogInfo("ORPHANAGE",
            $"{orphan.Name} came of age and became a Royal Guard");
    }

    /// <summary>
    /// Orphan graduates to become a citizen NPC (parallel of FamilySystem.ConvertChildToNPC).
    ///
    /// v0.63.0 rewrite -- mirrors the slice-1 fixes that ConvertChildToNPC got:
    /// lineage carryover from RoyalOrphan (its MotherName/FatherName/MotherID/
    /// FatherID slots were always populated, just never carried forward to the
    /// graduated NPC), Base* stats set so RecalculateStats doesn't zero them,
    /// missing CON/WIS/DEX/MaxMana added, name-clash disambiguation, orientation
    /// sync, late-joiner diversity nudge, and faction assignment. Pre-fix
    /// orphan-graduates were 0-stat husks that never reconciled their female-Gay
    /// orientation roll to Lesbian and never got a faction.
    /// </summary>
    public void OrphanBecomesNPC(RoyalOrphan orphan)
    {
        // Don't exceed LIVING population cap. Pre-fix this checked
        // `npcs.Count` (total list including permadead), which blocked
        // orphan graduation in worlds with heavy accumulated permadeath.
        int aliveCount = npcs.Count(n => n.IsAlive && !n.IsDead && !n.IsAgedDeath && !n.IsPermaDead);
        if (aliveCount >= GameConfig.MaxNPCPopulation) return;

        string displayName = NPCSpawnSystem.Instance?.DisambiguateNPCName(orphan.Name) ?? orphan.Name;

        int level = 1;
        int strength = 12 + random.Next(5);
        int defence = 12 + random.Next(5);
        int stamina = 10 + random.Next(5);
        int agility = 10 + random.Next(5);
        int dexterity = 10 + random.Next(5);
        int constitution = 10 + random.Next(5);
        int intelligence = 10 + random.Next(5);
        int wisdom = 10 + random.Next(5);
        int charisma = 12 + random.Next(5); // Orphanage gives social skills
        int maxHP = 100;
        var orphanClass = DetermineOrphanClass(orphan.Soul);
        int maxMana = IsCasterClass(orphanClass) ? 50 + intelligence * 2 : 0;

        var npc = new NPC
        {
            ID = $"npc_{displayName.ToLower().Replace(" ", "_")}_{Guid.NewGuid().ToString("N").Substring(0, 8)}",
            Name1 = displayName,
            Name2 = displayName,
            Sex = orphan.Sex,
            Age = 18,
            Race = orphan.Race,
            Class = orphanClass,
            Level = level,
            Experience = GameConfig.GetExperienceForLevel(level),
            HP = maxHP,
            MaxHP = maxHP,
            BaseMaxHP = maxHP,
            MaxMana = maxMana,
            BaseMaxMana = maxMana,
            Strength = strength,
            BaseStrength = strength,
            Defence = defence,
            BaseDefence = defence,
            Stamina = stamina,
            BaseStamina = stamina,
            Agility = agility,
            BaseAgility = agility,
            Dexterity = dexterity,
            BaseDexterity = dexterity,
            Constitution = constitution,
            BaseConstitution = constitution,
            Intelligence = intelligence,
            BaseIntelligence = intelligence,
            Wisdom = wisdom,
            BaseWisdom = wisdom,
            Charisma = charisma,
            BaseCharisma = charisma,
            Gold = 200,
            CurrentLocation = "Main Street",
            AI = CharacterAI.Computer,
            BirthDate = orphan.BirthDate,
            // Lineage carryover from the RoyalOrphan record (v0.63.0 slice 1)
            MotherName = orphan.MotherName ?? "",
            FatherName = orphan.FatherName ?? "",
            MotherID = orphan.MotherID ?? "",
            FatherID = orphan.FatherID ?? "",
            // Orphan history: the recorded names ARE the biological parents
            // (the orphan record is the durable parent reference after the
            // child enters the orphanage).
            OriginalMotherName = orphan.MotherName ?? "",
            OriginalFatherName = orphan.FatherName ?? "",
            SoulAtGraduation = orphan.Soul,
            // True if either recorded parent ID looks like a player slot.
            WasRaisedByPlayer =
                (!string.IsNullOrEmpty(orphan.MotherID) && !orphan.MotherID.StartsWith("npc_", StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrEmpty(orphan.FatherID) && !orphan.FatherID.StartsWith("npc_", StringComparison.OrdinalIgnoreCase)),
            // v0.64.0 Brain v2 Slice 1: graduating orphans get the goal-driven AI.
            IsAIDriven = true
        };

        // Soul-based alignment
        if (orphan.Soul > 200) { npc.Chivalry = 50 + random.Next(50); npc.Darkness = 0; }
        else if (orphan.Soul < -200) { npc.Chivalry = 0; npc.Darkness = 50 + random.Next(50); }
        else { npc.Chivalry = 25; npc.Darkness = 25; }

        // Initialize NPC systems (personality, brain, etc.)
        npc.EnsureSystemsInitialized();
        if (npc.Personality != null)
        {
            npc.Personality.Gender = orphan.Sex == CharacterSex.Female ? GenderIdentity.Female : GenderIdentity.Male;
            // Reconcile the orientation roll against the now-set gender so the
            // population doesn't drift straight (the v0.61.7 fix that the
            // npc-system-reviewer audit flagged as missing from this path).
            npc.Personality.SyncOrientationToGender();
            if (orphan.Soul > 100) { npc.Personality.Aggression = 0.2f; npc.Personality.Tenderness = 0.8f; }
            else if (orphan.Soul < -100) { npc.Personality.Aggression = 0.7f; npc.Personality.Tenderness = 0.2f; }
        }

        // Late-joiner diversity nudge and faction assignment (parity with
        // FamilySystem.ConvertChildToNPC -- orphan-graduates used to skip both).
        NPCSpawnSystem.Instance?.NudgeLateJoinerOrientation(npc);
        npc.NPCFaction = NPCSpawnSystem.Instance?.DetermineFactionForNPC(npc);

        NPCSpawnSystem.Instance?.AddRestoredNPC(npc);

        NewsSystem.Instance?.Newsy(
            $"🎓 {displayName}, raised in the Royal Orphanage, has come of age and joined the realm!");

        DebugLogger.Instance.LogInfo("ORPHANAGE",
            $"{displayName} came of age and became a citizen NPC ({npc.Class})");
    }

    /// <summary>
    /// v0.63.0: record an NPC death in the memory of every living relative
    /// (children, parents, siblings). Each relative records a LostFamilyMember
    /// memory; children additionally record a parent-specific KilledMyParent
    /// memory (preserves the original slice-E2 contract); other relatives get
    /// the generic KilledMyFamily type. If there was a killer, the killer is
    /// added to each relative's Enemies list. Surfaces downstream as the
    /// relative refusing to talk to / join the killer's team, attacking on
    /// sight when alignment is dark. Sparse + idempotent.
    /// </summary>
    private void RecordFamilyDeath(NPC deceased, string killerName)
    {
        try
        {
            if (deceased == null) return;

            // Walk all NPCs once and classify by relation to the deceased.
            string deceasedName = deceased.Name2 ?? deceased.Name1 ?? deceased.Name;
            string deceasedId = deceased.ID ?? "";
            if (string.IsNullOrEmpty(deceasedName)) return;

            // Build the deceased's parent name set up-front so we can detect
            // siblings (any NPC sharing at least one parent name with deceased).
            var deceasedParentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(deceased.MotherName)) deceasedParentNames.Add(deceased.MotherName);
            if (!string.IsNullOrEmpty(deceased.FatherName)) deceasedParentNames.Add(deceased.FatherName);

            foreach (var relative in npcs)
            {
                if (relative == null || relative.IsDead || relative == deceased) continue;

                // Child of deceased? (deceased was their parent)
                bool isChild =
                    (relative.MotherName == deceasedName || relative.FatherName == deceasedName)
                    || (!string.IsNullOrEmpty(deceasedId) && (relative.MotherID == deceasedId || relative.FatherID == deceasedId));

                // Parent of deceased? (deceased was their child)
                bool isParent =
                    (!string.IsNullOrEmpty(deceased.MotherName) && relative.Name2 == deceased.MotherName)
                    || (!string.IsNullOrEmpty(deceased.FatherName) && relative.Name2 == deceased.FatherName);

                // Sibling? (shared at least one parent name with deceased)
                bool isSibling = !isChild && !isParent && deceasedParentNames.Count > 0
                    && ((!string.IsNullOrEmpty(relative.MotherName) && deceasedParentNames.Contains(relative.MotherName))
                        || (!string.IsNullOrEmpty(relative.FatherName) && deceasedParentNames.Contains(relative.FatherName)));

                if (!isChild && !isParent && !isSibling) continue;

                string relationLabel = isChild ? "parent" : isParent ? "child" : "sibling";
                bool hasKiller = !string.IsNullOrEmpty(killerName);

                // Parent-specific memory for children (preserves slice-E2 contract).
                if (isChild && hasKiller)
                {
                    relative.Brain?.Memory?.RecordEvent(new MemoryEvent
                    {
                        Type = MemoryType.KilledMyParent,
                        Description = $"{killerName} killed my parent {deceasedName}.",
                        Importance = 0.95f,
                        EmotionalImpact = -0.9f,
                        Timestamp = DateTime.Now,
                        InvolvedCharacter = killerName,
                    });
                }
                else if (hasKiller)
                {
                    // Generic family-kill memory for parents / siblings.
                    relative.Brain?.Memory?.RecordEvent(new MemoryEvent
                    {
                        Type = MemoryType.KilledMyFamily,
                        Description = $"{killerName} killed my {relationLabel} {deceasedName}.",
                        Importance = 0.9f,
                        EmotionalImpact = -0.85f,
                        Timestamp = DateTime.Now,
                        InvolvedCharacter = killerName,
                    });
                }

                // Loss memory for every relative regardless of killer.
                relative.Brain?.Memory?.RecordEvent(new MemoryEvent
                {
                    Type = MemoryType.LostFamilyMember,
                    Description = $"My {relationLabel} {deceasedName} died.",
                    Importance = isChild ? 0.9f : 0.85f,
                    EmotionalImpact = isChild ? -0.85f : -0.8f,
                    Timestamp = DateTime.Now,
                    InvolvedCharacter = deceasedName,
                });

                // Killer enters the relative's Enemies list.
                if (hasKiller && !relative.Enemies.Contains(killerName))
                {
                    relative.Enemies.Add(killerName);
                }
            }
        }
        catch (Exception ex)
        {
            UsurperRemake.Systems.DebugLogger.Instance?.LogWarning("FAMILY",
                $"RecordFamilyDeath failed: {ex.Message}");
        }
    }

    /// <summary>
    /// v0.63.0: record a new-child event in the memory of every living relative
    /// (parents, siblings, grandparents). Positive valence memory, no behavior
    /// hooks attached. Counterpart to RecordFamilyDeath.
    /// </summary>
    public void RecordFamilyBirth(string childName, string motherName, string fatherName)
    {
        try
        {
            if (string.IsNullOrEmpty(childName)) return;

            var parentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(motherName)) parentNames.Add(motherName);
            if (!string.IsNullOrEmpty(fatherName)) parentNames.Add(fatherName);
            if (parentNames.Count == 0) return;

            foreach (var relative in npcs)
            {
                if (relative == null || relative.IsDead) continue;

                bool isParent = parentNames.Contains(relative.Name2 ?? "");
                bool isSibling = !isParent
                    && ((!string.IsNullOrEmpty(relative.MotherName) && parentNames.Contains(relative.MotherName))
                        || (!string.IsNullOrEmpty(relative.FatherName) && parentNames.Contains(relative.FatherName)));

                if (!isParent && !isSibling) continue;

                string relationLabel = isParent ? "child" : "sibling";

                relative.Brain?.Memory?.RecordEvent(new MemoryEvent
                {
                    Type = MemoryType.FamilyMemberBorn,
                    Description = $"My {relationLabel} {childName} was born.",
                    Importance = isParent ? 0.85f : 0.6f,
                    EmotionalImpact = isParent ? 0.85f : 0.55f,
                    Timestamp = DateTime.Now,
                    InvolvedCharacter = childName,
                });
            }
        }
        catch (Exception ex)
        {
            UsurperRemake.Systems.DebugLogger.Instance?.LogWarning("FAMILY",
                $"RecordFamilyBirth failed: {ex.Message}");
        }
    }

    /// <summary>
    /// True if the class needs a mana pool. Local duplicate of FamilySystem's
    /// helper (kept private to avoid widening the system surface area).
    /// </summary>
    private static bool IsCasterClass(CharacterClass cls)
    {
        return cls == CharacterClass.Magician
            || cls == CharacterClass.Cleric
            || cls == CharacterClass.Sage
            || cls == CharacterClass.Paladin
            || cls == CharacterClass.Bard
            || cls == CharacterClass.MysticShaman;
    }

    /// <summary>
    /// Determine character class for an orphan based on their soul value.
    /// </summary>
    private CharacterClass DetermineOrphanClass(int soul)
    {
        if (soul > 200)
            return random.Next(3) switch { 0 => CharacterClass.Paladin, 1 => CharacterClass.Cleric, _ => CharacterClass.Warrior };
        else if (soul < -200)
            return random.Next(3) switch { 0 => CharacterClass.Assassin, 1 => CharacterClass.Magician, _ => CharacterClass.Barbarian };
        else
        {
            var classes = new[] { CharacterClass.Warrior, CharacterClass.Magician, CharacterClass.Assassin,
                                  CharacterClass.Ranger, CharacterClass.Bard, CharacterClass.Sage };
            return classes[random.Next(classes.Length)];
        }
    }

    /// <summary>
    /// Pick up orphaned children who were flagged while no king existed.
    /// Called when a new king is crowned or when the orphanage is first accessed.
    /// </summary>
    public static void PickUpOrphanedChildren(King king)
    {
        var familySystem = FamilySystem.Instance;
        if (familySystem == null || king == null) return;

        var orphanedChildren = familySystem.AllChildren
            .Where(c => !c.Deleted && c.Location == GameConfig.ChildLocationOrphanage &&
                        !king.Orphans.Any(o => o.Name == c.Name && o.IsRealOrphan))
            .ToList();

        // v0.63.0 slice 4 (audit npc-N4): per-instance NPC list is in scope here
        // and DetermineOrphanRace already exists. Use it instead of hardcoding
        // Human, so late-pickup orphans inherit their actual parental race.
        var inst = WorldSimulator.Instance;

        foreach (var child in orphanedChildren)
        {
            if (king.Orphans.Count >= GameConfig.MaxRoyalOrphans) break;

            var inheritedRace = inst != null
                ? inst.DetermineOrphanRace(child)
                : CharacterRace.Human;

            king.Orphans.Add(new RoyalOrphan
            {
                Name = child.Name,
                Age = child.Age,
                Sex = child.Sex,
                ArrivalDate = DateTime.Now,
                BirthDate = child.BirthDate,
                BackgroundStory = $"Both parents lost. Mother: {child.Mother}, Father: {child.Father}.",
                Happiness = 30,
                MotherName = child.Mother,
                FatherName = child.Father,
                MotherID = child.MotherID,
                FatherID = child.FatherID,
                Race = inheritedRace,
                Soul = child.Soul,
                IsRealOrphan = true
            });
        }

        if (orphanedChildren.Count > 0)
        {
            DebugLogger.Instance.LogInfo("ORPHANAGE",
                $"Picked up {orphanedChildren.Count} orphaned children for new king");
        }
    }

    /// <summary>
    /// Process NPC pregnancies - handle births for pregnant NPCs and
    /// give married female NPCs a chance to become pregnant each tick.
    /// </summary>
    private void ProcessNPCPregnancies()
    {
        // Process existing pregnancies - check for births
        foreach (var npc in npcs.Where(n => n.IsAlive && !n.IsDead && n.PregnancyDueDate.HasValue).ToList())
        {
            if (DateTime.Now >= npc.PregnancyDueDate.Value)
            {
                // Baby is due! Find the father (could be affair partner, not spouse)
                string fatherName = npc.SpouseName;
                if (!string.IsNullOrEmpty(npc.PregnancyFatherName))
                {
                    fatherName = npc.PregnancyFatherName;
                    npc.PregnancyFatherName = null; // Clear after birth
                }

                // Try to find the father - first alive, then even dead (for the child record)
                var father = npcs.FirstOrDefault(n =>
                    n.Name2 == fatherName && n.IsAlive && !n.IsDead);

                // If father is dead/missing, still create the child (use any NPC with that name)
                if (father == null)
                {
                    father = npcs.FirstOrDefault(n => n.Name2 == fatherName);
                }

                if (father != null)
                {
                    FamilySystem.Instance?.CreateNPCChild(npc, father);
                    UsurperRemake.Systems.DebugLogger.Instance.LogInfo("LIFECYCLE",
                        $"Child born to {npc.Name2} and {father.Name2}!");
                }
                else
                {
                    // Father completely gone from the game - create child with mother only
                    UsurperRemake.Systems.DebugLogger.Instance.LogWarning("LIFECYCLE",
                        $"Birth: father '{fatherName}' not found for {npc.Name2}'s pregnancy. Creating child anyway.");
                    FamilySystem.Instance?.CreateNPCChild(npc, npc); // Use mother as both parents
                }
                npc.PregnancyDueDate = null;
            }
        }

        // Calculate dynamic pregnancy rate based on population
        int aliveCount = npcs.Count(n => n.IsAlive && !n.IsDead);
        int childCount = FamilySystem.Instance?.AllChildren.Count(c => !c.Deleted) ?? 0;
        int totalPop = aliveCount + childCount;

        // Hard cap: no new pregnancies when LIVING population is at limit.
        // Pre-fix this checked `npcs.Count` (total list size including
        // permadead corpses). Live audit found a world with 99 alive + 11
        // dead + 134 permadead = 244 records, well past the 200 cap, with
        // pregnancy permanently disabled despite 71 marriages. Permadead
        // corpses are inert records that shouldn't block new life.
        if (aliveCount >= GameConfig.MaxNPCPopulation)
        {
            return;
        }

        // Dynamic rate: higher when underpopulated, lower when overpopulated
        // Values are denominator for random check (higher = less likely per tick)
        int pregnancyDenominator = totalPop < 40  ? 150    // ~0.67% — repopulate quickly
                                 : totalPop < 80  ? 400    // ~0.25% — normal
                                 : totalPop < 120 ? 800    // ~0.125% — slow down
                                 : totalPop < 160 ? 2000   // ~0.05% — very slow
                                 :                  4000;   // ~0.025% — near-zero

        // Eligible females (married or not) may become pregnant
        var eligibleFemales = npcs.Where(n =>
            n.IsAlive && !n.IsDead &&
            n.Sex == CharacterSex.Female &&
            !n.PregnancyDueDate.HasValue &&
            n.Age >= 18 && n.Age <= 45).ToList();

        foreach (var npc in eligibleFemales)
        {
            if (random.Next(pregnancyDenominator) > 0) continue;

            // Try to find a partner for this pregnancy
            NPC? father = null;
            bool isAffair = false;

            // If married, spouse is the default partner
            if (npc.Married || npc.IsMarried)
            {
                father = npcs.FirstOrDefault(n =>
                    n.Name2 == npc.SpouseName && n.IsAlive && !n.IsDead);

                // Flirtatious NPCs may conceive with someone other than their spouse
                var profile = npc.Brain?.Personality;
                if (profile != null && profile.Flirtatiousness > 0.6f && random.Next(100) < 15)
                {
                    // 15% chance for flirtatious married NPCs to have an affair pregnancy
                    var affairPartner = FindAffairPartner(npc);
                    if (affairPartner != null)
                    {
                        father = affairPartner;
                        isAffair = true;
                    }
                }
            }
            else
            {
                // Unmarried NPCs with high flirtatiousness can have casual pregnancies
                var profile = npc.Brain?.Personality;
                if (profile != null && profile.Flirtatiousness > 0.5f)
                {
                    father = FindAffairPartner(npc);
                    if (father != null) isAffair = true;
                }
            }

            if (father == null) continue;

            // Check couple doesn't have too many children already (max 4)
            var existingChildren = FamilySystem.Instance?.GetChildrenOfCouple(npc, father);
            if (existingChildren != null && existingChildren.Count >= 4) continue;

            // Pregnancy! Due in ~9 game months ≈ 7 hours at accelerated rate
            npc.PregnancyDueDate = DateTime.Now.AddHours(7);

            if (isAffair)
            {
                // Track the affair father on the NPC so the child gets the right parentage
                npc.PregnancyFatherName = father.Name2;

                NewsSystem.Instance?.WriteAffairNews(npc.Name2, father.Name2);
            }
            else
            {
                string childPrefix = GameConfig.ScreenReaderMode ? "" : "♥ ";
                NewsSystem.Instance?.Newsy(
                    $"{childPrefix}{npc.Name2} and {father.Name2} are expecting a child!");
            }

            UsurperRemake.Systems.DebugLogger.Instance.LogInfo("LIFECYCLE",
                $"{npc.Name2} is pregnant by {father.Name2} (affair={isAffair})! Due: {npc.PregnancyDueDate.Value:HH:mm}");
        }
    }

    /// <summary>
    /// Find a compatible affair partner for an NPC. Must be attracted to each other,
    /// alive, and of appropriate age.
    /// </summary>
    private NPC? FindAffairPartner(NPC npc)
    {
        var profile = npc.Brain?.Personality;
        if (profile == null) return null;

        // v0.63.0 slice 1: NPC-NPC incest gate for the affair path. Same shape
        // as the marriage filter -- silent, just narrows the candidate pool so
        // a sibling / parent / grandchild never registers as a partner.
        var family = UsurperRemake.Systems.FamilySystem.Instance;

        var candidates = npcs.Where(c =>
            c.ID != npc.ID &&
            c.IsAlive && !c.IsDead &&
            c.Sex != npc.Sex && // Opposite sex for pregnancy
            c.Name2 != npc.SpouseName && // Not the current spouse
            c.Age >= 18 &&
            c.Brain?.Personality != null &&
            profile.IsAttractedTo(c.Brain.Personality.Gender) &&
            c.Brain.Personality.IsAttractedTo(profile.Gender) &&
            (family == null
                || !UsurperRemake.Systems.FamilySystem.IsBlockingRelation(
                       family.GetFamilyRelation(npc, c)))
        ).ToList();

        if (candidates.Count == 0) return null;

        // Prefer flirtatious partners
        var flirty = candidates.Where(c =>
            c.Brain?.Personality?.Flirtatiousness > 0.4f).ToList();

        if (flirty.Count > 0)
            return flirty[random.Next(flirty.Count)];

        return candidates[random.Next(candidates.Count)];
    }

    /// <summary>
    /// Process NPC divorces. Married couples may split based on personality mismatch,
    /// low commitment, or high flirtatiousness. Divorce frees both NPCs to remarry.
    /// </summary>
    private void ProcessNPCDivorces()
    {
        // Only process each couple once (by checking the female or alphabetically first partner)
        var marriedNPCs = npcs.Where(n =>
            n.IsAlive && !n.IsDead &&
            (n.Married || n.IsMarried) &&
            !string.IsNullOrEmpty(n.SpouseName))
            .ToList();

        // Track already-processed couples to avoid double-divorce
        var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var npc in marriedNPCs)
        {
            if (processed.Contains(npc.Name2 ?? npc.Name)) continue;

            var spouse = npcs.FirstOrDefault(n =>
                n.Name2 == npc.SpouseName && n.IsAlive && !n.IsDead);
            if (spouse == null) continue;

            processed.Add(npc.Name2 ?? npc.Name);
            processed.Add(spouse.Name2 ?? spouse.Name);

            // Base divorce chance per tick
            // Online: 0.02% per tick (~2.4% per hour, ~one divorce per ~40 hours per couple)
            // Single-player: 0.3% per tick (unchanged — sessions are short)
            float divorceChance = UsurperRemake.BBS.DoorMode.IsOnlineMode ? 0.0002f : 0.003f;

            var profile1 = npc.Brain?.Personality;
            var profile2 = spouse.Brain?.Personality;

            if (profile1 != null && profile2 != null)
            {
                // Low commitment increases divorce chance
                if (profile1.Commitment < 0.3f) divorceChance += 0.005f;
                if (profile2.Commitment < 0.3f) divorceChance += 0.005f;

                // High flirtatiousness increases divorce chance
                if (profile1.Flirtatiousness > 0.7f) divorceChance += 0.003f;
                if (profile2.Flirtatiousness > 0.7f) divorceChance += 0.003f;

                // Alignment mismatch increases divorce chance
                bool oneGood = npc.Chivalry > npc.Darkness;
                bool otherGood = spouse.Chivalry > spouse.Darkness;
                if (oneGood != otherGood) divorceChance += 0.004f;

                // High commitment in BOTH reduces divorce chance significantly
                if (profile1.Commitment > 0.7f && profile2.Commitment > 0.7f)
                    divorceChance *= 0.2f;
            }

            if (random.NextDouble() >= divorceChance) continue;

            // Divorce!
            npc.Married = false;
            npc.IsMarried = false;
            npc.SpouseName = "";

            spouse.Married = false;
            spouse.IsMarried = false;
            spouse.SpouseName = "";

            // Clear pregnancy only if the father is the divorcing spouse (preserve affair pregnancies)
            if (npc.PregnancyDueDate.HasValue && string.IsNullOrEmpty(npc.PregnancyFatherName))
            {
                npc.PregnancyDueDate = null;
            }
            if (spouse.PregnancyDueDate.HasValue && string.IsNullOrEmpty(spouse.PregnancyFatherName))
            {
                spouse.PregnancyDueDate = null;
            }

            // End marriage in registry
            NPCMarriageRegistry.Instance.EndMarriage(npc.ID);

            // Add memories
            npc.Brain?.Memory?.AddMemory(
                $"I divorced {spouse.Name2}.", "divorce", DateTime.Now);
            spouse.Brain?.Memory?.AddMemory(
                $"I divorced {npc.Name2}.", "divorce", DateTime.Now);

            NewsSystem.Instance?.WriteDivorceNews(npc.Name2, spouse.Name2);

            UsurperRemake.Systems.DebugLogger.Instance.LogInfo("LIFECYCLE",
                $"{npc.Name2} and {spouse.Name2} have divorced!");
        }
    }

    /// <summary>
    /// Process additional NPC activities like dungeon runs, shopping, training
    /// </summary>
    private void ProcessNPCActivities(NPC npc, WorldState world)
    {
        // Player team NPCs don't do autonomous activities (dungeon runs, shopping, etc.)
        // They could die in simulated dungeon combat, causing the "disappeared from team" bug
        if (IsPlayerTeam(npc.Team)) return;

        // Chance per tick to do something interesting
        // Online: 5% (was 15%) — more realistic pacing for persistent server
        // Single-player: 15% (unchanged)
        double activityChance = UsurperRemake.BBS.DoorMode.IsOnlineMode ? 0.05 : 0.15;
        if (random.NextDouble() > activityChance) return;

        var activities = BuildCandidateActivities(npc);
        if (activities.Count == 0) return;

        // Legacy weighted-random pick (heuristic cohort).
        double totalWeight = activities.Sum(a => a.weight);
        double roll = random.NextDouble() * totalWeight;
        double cumulative = 0;
        string selectedAction = "move";
        foreach (var (action, weight) in activities)
        {
            cumulative += weight;
            if (roll <= cumulative)
            {
                selectedAction = action;
                break;
            }
        }

        DispatchVerb(npc, selectedAction);
    }

    /// <summary>
    /// v0.64.0 Brain v2 Slice 2: goal-driven utility scorer cohort path. Same
    /// player-team gate and per-tick activity gate as the legacy picker, but
    /// instead of a weighted-random roll over the candidate set, runs
    /// BrainV2Scorer.PickAction which adds goal alignment / need satisfaction /
    /// recency penalty on top of the picker's modifier chain output, then
    /// picks argmax (with personality-Impulsiveness occasional random override
    /// for variety). DispatchVerb is shared with the legacy path so the same
    /// activity flavor + emotional state + telemetry fire either way.
    /// </summary>

    // v0.64.1: case-insensitive lookup of a living NPC by display or first name.
    // Used by TryTargetSteerToTarget to resolve Goal.TargetCharacter strings
    // (which are populated by family-memory promotion in Slice 3 and by the
    // LLM strategic-goals generator in Slice 12a) into actual NPC references.
    // Returns null if no match or the target is dead/permadead -- caller falls
    // through to normal verb dispatch in those cases.
    internal NPC? FindLivingNPCByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var trimmed = name.Trim();
        foreach (var n in npcs)
        {
            if (n == null || !n.IsAlive || n.IsDead) continue;
            if (string.Equals(n.Name2, trimmed, StringComparison.OrdinalIgnoreCase)) return n;
            if (string.Equals(n.Name1, trimmed, StringComparison.OrdinalIgnoreCase)) return n;
            if (string.Equals(n.Name, trimmed, StringComparison.OrdinalIgnoreCase)) return n;
        }
        return null;
    }

    // v0.64.1 Brain v2 Slice 13: target-aware dispatch. When the NPC's
    // priority goal has a TargetCharacter (LLM-populated for revenge,
    // romance, rivalry, mentorship, etc., or set by family-memory promotion
    // for Avenge goals) and that target is alive, steer the NPC to the
    // target's current location instead of running the normal verb. This
    // turns dashboard goal text like "Crush Lucinda Foxglove" from
    // verb-family bias into literal in-game pursuit -- the NPC will physically
    // show up wherever Lucinda is, with goal-type-keyed activity flavor.
    //
    // Probability scales with goal priority so low-priority strategic threads
    // don't dominate behavior; high-priority Avenge goals will pursue
    // aggressively. Settlement residents respect snap-back.
    //
    // Returns true if steered (caller skips normal verb dispatch).
    private bool TryTargetSteerToTarget(NPC npc)
    {
        var brain = npc.Brain;
        var goal = brain?.Goals?.GetPriorityGoal();
        if (goal == null || string.IsNullOrWhiteSpace(goal.TargetCharacter)) return false;

        // v0.64.1 audit fix: never steer an NPC who is engaged with a player
        // (dungeon party, conversation) -- yanking a partied spouse's location
        // out of "Dungeon" mid-run corrupts the party bookkeeping and fights
        // the engaged-protection work in this same release. Likewise never
        // steer an NPC who is themselves in the Dungeon (their own run's exit
        // bookkeeping expects to find them there).
        if (npc.IsInConversation) return false;
        if (string.Equals(npc.CurrentLocation, "Dungeon", StringComparison.OrdinalIgnoreCase)) return false;

        // Settlers stay put; their snap-back enforcement would undo any move.
        if (SettlementSystem.Instance?.State.SettlerNames.Contains(npc.Name) == true) return false;

        var target = FindLivingNPCByName(goal.TargetCharacter);
        if (target == null) return false;
        if (string.IsNullOrWhiteSpace(target.CurrentLocation)) return false;
        // Don't follow the target into the Dungeon -- the dungeon verb has its
        // own combat / floor-pick semantics that the steer can't replicate, and
        // mass-tracking a target across dungeon floors would be silly.
        if (target.CurrentLocation == "Dungeon") return false;
        // Already there? Nothing to do; fall through to normal verb selection
        // so the NPC actually does something this tick.
        if (string.Equals(npc.CurrentLocation, target.CurrentLocation, StringComparison.OrdinalIgnoreCase))
            return false;

        // Probability scales with goal priority: priority 0.9 = 36% steer,
        // priority 0.5 = 20% steer, priority 0.3 = 12% steer. Tuned so a
        // single tick almost never pursues, but over many ticks the NPC
        // accumulates time near the target.
        double steerChance = Math.Clamp(goal.Priority * 0.4, 0.05, 0.50);
        if (random.NextDouble() > steerChance) return false;

        // Goal-type-keyed flavor + emotional impact.
        string targetName = target.Name2 ?? target.Name1 ?? "their mark";
        string activity;
        EmotionType primaryEmotion;
        float emotionStrength;
        switch (goal.Type)
        {
            case GoalType.Combat:
                activity = $"shadowing {targetName} with hostile intent";
                primaryEmotion = EmotionType.Anger;
                emotionStrength = 0.5f;
                break;
            case GoalType.Social:
                activity = goal.Name.Contains("Reconcile", StringComparison.OrdinalIgnoreCase)
                    || goal.Name.Contains("Protect", StringComparison.OrdinalIgnoreCase)
                    || goal.Name.Contains("Friend", StringComparison.OrdinalIgnoreCase)
                    ? $"seeking out {targetName} to talk"
                    : $"watching {targetName} from across the room";
                primaryEmotion = goal.Name.Contains("Reconcile", StringComparison.OrdinalIgnoreCase)
                    || goal.Name.Contains("Protect", StringComparison.OrdinalIgnoreCase)
                    ? EmotionType.Hope
                    : EmotionType.Envy;
                emotionStrength = 0.4f;
                break;
            case GoalType.Personal:
                activity = $"keeping watch over {targetName}";
                primaryEmotion = EmotionType.Hope;
                emotionStrength = 0.3f;
                break;
            default:
                activity = $"trailing {targetName}";
                primaryEmotion = EmotionType.Confidence;
                emotionStrength = 0.3f;
                break;
        }

        string locationBefore = npc.CurrentLocation;
        // v0.64.1 audit fix: route through UpdateLocation (the single movement
        // chokepoint) instead of a raw field write -- the raw write skipped the
        // LocationManager presence-registry remove/add, leaving ghost entries
        // at the pre-steer location that accumulated per steer. UpdateLocation
        // clears CurrentActivity, so set the flavor AFTER the move.
        npc.UpdateLocation(target.CurrentLocation);
        npc.CurrentActivity = activity;
        npc.EmotionalState?.AddEmotion(primaryEmotion, emotionStrength, 90);

        // Telemetry: log as action=target_steer so dashboard pivots can isolate
        // the new behavior from normal verbs. Outcome captures the goal type
        // for finer rollup ("target_steer/Combat", "target_steer/Social").
        try
        {
            SqlBackend?.LogNPCDecision(
                npc.Name2 ?? npc.Name1 ?? "(unknown)",
                (int)npc.Level,
                npc.Class.ToString(),
                "target_steer",
                locationBefore,
                npc.CurrentLocation,
                $"goal:{goal.Type}",
                0,
                0,
                npc.HP,
                npc.HP,
                npc.IsAIDriven);
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("WORLDSIM",
                $"target_steer telemetry failed for {npc.Name2 ?? npc.Name1}: {ex.Message}");
        }

        return true;
    }

    private void BrainV2ProcessActivities(NPC npc, WorldState world)
    {
        if (IsPlayerTeam(npc.Team)) return;

        double activityChance = UsurperRemake.BBS.DoorMode.IsOnlineMode ? 0.05 : 0.15;
        if (random.NextDouble() > activityChance) return;

        // v0.64.0 Brain v2 Slice 3: tick the brain's goal / emotional / memory
        // state inline before scoring. This replaces the legacy Brain.DecideNextAction
        // cooldown-gated update path so family-memory goal promotion (Avenge,
        // Protect Family, Mourn) and goal completion detection fire the same
        // tick the scorer reads them. Cheap -- only fires when the activity
        // gate already permitted a decision, so ~5% of ticks per NPC online.
        var brain = npc.Brain;
        if (brain != null)
        {
            try
            {
                brain.Emotions?.Update(brain.Memory?.GetRecentEvents() ?? new List<MemoryEvent>());
                brain.Memory?.DecayMemories();
                brain.Goals?.UpdateGoals(npc, world, brain.Memory, brain.Emotions);
            }
            catch (Exception ex)
            {
                UsurperRemake.Systems.DebugLogger.Instance.LogError("BRAINV2",
                    $"Brain state update failed for {npc.Name2 ?? npc.Name}: {ex.Message}");
                // Fall through: scorer will still run on whatever goal state exists.
            }
        }

        // v0.64.1 Brain v2 Slice 13: target-aware pre-pick. If the priority
        // goal has a TargetCharacter and the target NPC is alive and
        // reachable, probabilistically steer to their location instead of
        // running a normal verb this tick. Probability scales with goal
        // priority. See TryTargetSteerToTarget for the policy details. When
        // this fires, skip the rest of the tick -- the steer IS the action.
        if (TryTargetSteerToTarget(npc)) return;

        var activities = BuildCandidateActivities(npc);
        if (activities.Count == 0) return;

        // Brain v2 scorer adds goal / need / recency layers on top of the
        // picker-built candidate weights, then argmaxes (Impulsiveness gates
        // a small random-pick override for behavioral variety).
        string selectedAction = BrainV2Scorer.PickAction(npc, activities, random);

        DispatchVerb(npc, selectedAction);

        // Mark for the recency layer: next tick's scorer will discount this
        // verb based on how long ago it ran.
        npc.Brain?.MarkActivity(selectedAction);
    }

    /// <summary>
    /// v0.64.0 Brain v2 Slice 2: shared candidate-set builder. Walks every
    /// supported verb, applies the gate condition (gold / HP / level / etc),
    /// computes the base weight, then runs the modifier chain (personality,
    /// time-of-day, memory, relationships, neighbor pressure, world events,
    /// memes, emergent role) that mutates the list in place. Both the legacy
    /// weighted-random picker and the Brain v2 scorer consume the output.
    /// </summary>
    private List<(string action, double weight)> BuildCandidateActivities(NPC npc)
    {
        // Weight activities based on NPC state
        var activities = new List<(string action, double weight)>();

        // Dungeon exploration - only if HP is healthy
        if (npc.HP > npc.MaxHP * 0.7 && npc.Level >= 1)
        {
            activities.Add(("dungeon", 0.15));
        }

        // v0.61.3 progression-weight rebalance. First telemetry pass showed
        // shop/train/levelup at <1% of all actions while move/inn/team_recruit
        // dominated at 50%+. NPCs were wandering instead of progressing.
        // Bumped shop, train, and levelup weights so when an NPC has the
        // resources for progression actions, they actually take them.

        // Shopping - if has gold. Was 0.20; bumped to 0.30. Greed bonus
        // separately so the merchant-soul NPCs shop more aggressively.
        if (npc.Gold > 100)
        {
            float shopWeight = 0.30f;
            if (npc.Brain?.Personality != null)
                shopWeight += npc.Brain.Personality.Greed * 0.10f;
            activities.Add(("shop", shopWeight));
        }

        // Training at gym. v0.64.1 tuning: post-deploy telemetry showed train
        // at 42% of all NPC actions -- a runaway. The v0.61.5 0.15 -> 0.25 bump,
        // followed by the later 0.25 -> 0.50 bump (to push NPCs to the gym
        // because previous telemetry showed train at only 1%), plus the
        // compounding personality multipliers (Aggression 1.3x / Intelligence
        // 1.4x / Mysticism 1.2x / Patience-impatient 1.3x) and Brain v2
        // scorer multipliers (Personal-goal 1.6x / Combat-goal 1.3x /
        // need-sat 1.3x for low-level NPCs), stacked to make train dominate
        // every other action. Cutting base 0.50 -> 0.22 and Ambition
        // contribution 0.15 -> 0.08, paired with scorer + personality
        // reductions, should bring train share to ~20% -- still a top
        // activity, no longer eating half the world. Gold gate kept at 20.
        if (npc.Gold > 20)
        {
            float trainWeight = 0.22f;
            if (npc.Brain?.Personality != null)
                trainWeight += npc.Brain.Personality.Ambition * 0.08f;
            activities.Add(("train", trainWeight));
        }

        // Visit level master if eligible. v0.61.3 bumped 0.30 -> 0.80;
        // v0.61.5 telemetry showed only 112 levelups across 174k decisions
        // (0.1%) -- 0.80 was still losing to the sum of all other weights.
        // When an NPC has the XP, leveling up should be near-automatic, so
        // raised to 2.0 to dominate the weighted pick when eligible.
        long expForNextLevel = GameConfig.GetExperienceForLevel(npc.Level + 1);
        if (npc.Experience >= expForNextLevel && npc.Level < 100)
        {
            activities.Add(("levelup", 2.0));
        }

        // Heal if wounded
        if (npc.HP < npc.MaxHP * 0.5)
        {
            activities.Add(("heal", 0.35));
        }

        // Socialize/move around town. Was 0.15; dropped to 0.05 because the
        // first telemetry showed "move" was the #1 action (17% of all events)
        // and it produces nothing -- pure wandering. Still kept as a fallback
        // so NPCs can drift between locations naturally.
        activities.Add(("move", 0.05));

        // Romance/Love Street - based on personality
        if (npc.Gold > 500 && npc.Brain?.Personality != null)
        {
            float romanceWeight = 0.05f; // Base chance
            // Higher romanticism = more likely to seek romance
            romanceWeight += npc.Brain.Personality.Romanticism * 0.10f;
            // Lower commitment = more likely to visit pleasure houses
            romanceWeight += (1f - npc.Brain.Personality.Commitment) * 0.05f;
            activities.Add(("love_street", romanceWeight));
        }

        // Temple worship - based on personality (faith/piety influences this)
        if (npc.Brain?.Personality != null)
        {
            float templeWeight = 0.05f; // Base chance
            // More lawful/good aligned characters visit temple more
            if (npc.Chivalry > npc.Darkness)
                templeWeight += 0.08f;
            // Lower aggression = more peaceful/spiritual
            templeWeight += (1f - npc.Brain.Personality.Aggression) * 0.05f;
            // Higher wisdom = more spiritual
            if (npc.Wisdom > 50)
                templeWeight += 0.05f;
            activities.Add(("temple", templeWeight));
        }

        // Bank visit - more likely if has gold to deposit or needs gold.
        // v0.63.2 Fix F: lowered deposit threshold from 1000 to 500 so NPCs
        // bank earlier in the wealth curve and keep more reserves out of
        // reach of the inn / healer / pickpocket drains.
        if (npc.Gold > 500 || (npc.BankGold > 0 && npc.Gold < 100))
        {
            float bankWeight = 0.15f; // base; was 0.10f, bumped to surface more often
            // Higher greed = more likely to visit bank
            if (npc.Brain?.Personality != null)
                bankWeight += npc.Brain.Personality.Greed * 0.08f;
            // More gold = more reason to deposit
            if (npc.Gold > 5000)
                bankWeight += 0.1f;
            activities.Add(("bank", bankWeight));
        }

        // Spouse going home - if this NPC is married to the player
        var romanceTracker = RomanceTracker.Instance;
        bool isPlayerSpouse = romanceTracker?.Spouses?.Any(s => s.NPCId == npc.ID) == true;
        bool isPlayerLover = romanceTracker?.CurrentLovers?.Any(l => l.NPCId == npc.ID) == true;

        if (isPlayerSpouse || isPlayerLover)
        {
            // Spouses and lovers have a higher chance to go "home" to be with the player
            float homeWeight = isPlayerSpouse ? 0.35f : 0.20f; // Spouses more likely
            // Higher romanticism = more likely to want to be home with partner
            if (npc.Brain?.Personality != null)
            {
                homeWeight += npc.Brain.Personality.Romanticism * 0.15f;
                homeWeight += npc.Brain.Personality.Commitment * 0.10f;
            }
            activities.Add(("go_home", homeWeight));
        }

        // Marketplace visit - if has items to sell or gold to buy
        if (npc.Gold > 200 || npc.MarketInventory.Count > 0)
        {
            float marketWeight = 0.12f; // Base 12% weight
            // Higher greed = more likely to trade
            if (npc.Brain?.Personality != null)
                marketWeight += npc.Brain.Personality.Greed * 0.06f;
            // More inventory = more reason to visit
            if (npc.MarketInventory.Count > 2)
                marketWeight += 0.05f;
            activities.Add(("marketplace", marketWeight));
        }

        // Castle visit - chance to apply for royal guard or seek audience
        // More likely for higher level, honorable NPCs
        if (npc.Level >= 5 && npc.Chivalry > npc.Darkness)
        {
            float castleWeight = 0.05f; // Base 5% weight
            // Higher chivalry = more likely to serve the crown
            castleWeight += Math.Min(0.10f, npc.Chivalry / 1000f);
            // Higher level = more qualified for guard duty
            if (npc.Level >= 10)
                castleWeight += 0.05f;
            activities.Add(("castle", castleWeight));
        }

        // Dark Alley - shady NPCs, thieves, assassins, high darkness characters
        if (npc.Brain?.Personality != null)
        {
            float alleyWeight = 0.03f; // Low base
            // High darkness alignment = drawn to the shadows
            if (npc.Darkness > npc.Chivalry)
                alleyWeight += 0.10f;
            // Assassins and thieves love the alley
            if (npc.Class == CharacterClass.Assassin || npc.Class == CharacterClass.Ranger)
                alleyWeight += 0.08f;
            // Higher greed = seeking black market deals
            alleyWeight += npc.Brain.Personality.Greed * 0.06f;
            // Higher aggression = more comfortable in rough areas
            alleyWeight += npc.Brain.Personality.Aggression * 0.04f;
            if (alleyWeight > 0.05f) // Only add if they'd actually go
                activities.Add(("dark_alley", alleyWeight));
        }

        // Settlement — community-minded NPCs may visit the outskirts settlement
        if (SettlementSystem.Instance?.State.IsEstablished == true)
        {
            float settlementWeight = 0.08f;
            if (npc.Brain?.Personality != null)
            {
                settlementWeight += npc.Brain.Personality.Sociability * 0.06f;
                if (npc.Brain.Personality.Aggression < 0.3f) settlementWeight += 0.04f;
            }
            activities.Add(("settlement", settlementWeight));
        }

        // Inn - rest, socialize, drink, gossip (main social hub).
        // v0.61.3: dropped base 0.15 -> 0.08. Inn was the #2 action overall
        // (17% of all events) in the first telemetry pass, drowning out
        // progression actions. Still has sociability and time-of-day bonuses
        // that let it spike when contextually appropriate (evening, sociable
        // NPC, wounded), so the inn stays the natural social hub without
        // monopolising the action picker.
        {
            float innWeight = 0.08f;
            // Wounded NPCs rest at the inn
            if (npc.HP < npc.MaxHP * 0.7)
                innWeight += 0.10f;
            // Sociable NPCs like the inn
            if (npc.Brain?.Personality != null)
                innWeight += npc.Brain.Personality.Sociability * 0.08f;
            // Evening/night - everyone heads to the inn
            int hour = DateTime.Now.Hour;
            if (hour >= 18 || hour < 6)
                innWeight += 0.10f;
            activities.Add(("inn", innWeight));
        }

        // Team activities. v0.61.3: dropped team_recruit weights since the
        // first telemetry pass showed 1729 recruit attempts producing very
        // few actual recruits -- NPCs were spending too many ticks shopping
        // for teammates that weren't there. Halved both rates.
        if (string.IsNullOrEmpty(npc.Team))
        {
            // Not in a team - consider joining or forming one. Was 0.25 base
            // / 0.35 gang-affinity; now 0.12 / 0.18.
            float teamWeight = 0.12f;
            if (npc.Brain?.Personality?.IsLikelyToJoinGang() == true)
                teamWeight = 0.18f;
            activities.Add(("team_recruit", teamWeight));
        }
        else
        {
            // In a team - team activities. v0.63.2: weight is level-keyed
            // because the 14-day telemetry showed team_dungeon at 35% death
            // rate and 0.14% win rate, almost entirely driven by the Lv 10-19
            // band where naked-equipment NPCs can't beat the formula-driven
            // monster damage. Lv 5-9 are now blocked entirely at Gate 0; Lv
            // 10-29 take a heavy weight cut so they don't run dungeon attempts
            // they can't win; Lv 30+ keep the normal weight since they have
            // accumulated enough Base* stats to survive their floor pick.
            if (npc.HP > npc.MaxHP * 0.6)
            {
                double teamDungeonWeight;
                if (npc.Level < 20) teamDungeonWeight = 0.03;       // was 0.12
                else if (npc.Level < 30) teamDungeonWeight = 0.06;  // was 0.12
                else teamDungeonWeight = 0.12;
                activities.Add(("team_dungeon", teamDungeonWeight));
            }
            // Was 0.15; now 0.06 — once in a team, ongoing recruiting is a
            // background activity, not a primary one.
            activities.Add(("team_recruit", 0.06));
        }

        // v0.64.1 Brain v2 Slice 15: NPC bounty questing. Players visit Quest
        // Hall to claim bounties from the board (kill X monsters, clear Y
        // floors). NPCs never did. This makes them. Base weight modest so
        // questing competes with other goal-driven actions; Aggression /
        // Greed / Courage personality boosts in ApplyPersonalityWeights
        // make combat-leaning NPCs more likely to seek out bounties.
        activities.Add(("quest", 0.08));

        // Apply the modifier chain in place. Each modifier mutates the list,
        // multiplying weights based on personality / time-of-day / memory /
        // relationships / neighbor density / world events / cultural memes /
        // emergent role. Both the legacy weighted-random picker and the Brain
        // v2 scorer consume the same post-modifier output.
        ApplyPersonalityWeights(activities, npc);
        ApplyTimeOfDayWeights(activities);
        ApplyMemoryWeights(activities, npc);
        ApplyRelationshipWeights(activities, npc);
        ApplyNeighborPressure(activities, npc);
        ApplyWorldEventWeights(activities, npc);
        CulturalMemeSystem.Instance?.ApplyMemeWeights(activities, npc);
        SocialInfluenceSystem.ApplyRoleWeights(activities, npc);

        return activities;
    }

    /// <summary>
    /// v0.64.0 Brain v2 Slice 2: shared verb dispatch shared by the legacy
    /// weighted-Markov picker (ProcessNPCActivities) AND the goal-driven
    /// utility scorer (BrainV2Scorer). Takes a verb string from either, runs
    /// the corresponding NPC* action method through TelemetryWrap, stamps
    /// activity flavor and emotional state. Settler snap-back enforced at the
    /// end so settlement residents always return to the Settlement after
    /// doing their activity. Returns nothing -- side effects only.
    /// </summary>
    internal void DispatchVerb(NPC npc, string verb)
    {
        bool isSettler = SettlementSystem.Instance?.State.SettlerNames.Contains(npc.Name) == true;

        switch (verb)
        {
            case "dungeon":
                // NPCExploreDungeon has its own internal telemetry hook with
                // richer outcome strings (won / died / fled / stalemate / aborted_*)
                // so we don't double-log it through TelemetryWrap.
                NPCExploreDungeon(npc);
                // v0.61.2: only stamp the dungeon-flavor activity / emotions if
                // the NPC actually went. Self-preservation gates in
                // NPCExploreDungeon can redirect them to Healer or Main Street
                // and set a different activity; don't overwrite that.
                if (npc.CurrentLocation == "Dungeon")
                {
                    npc.CurrentActivity = "exploring the dungeon depths";
                    npc.EmotionalState?.AddEmotion(EmotionType.Confidence, 0.5f, 90);
                    npc.EmotionalState?.AddEmotion(EmotionType.Fear, 0.3f, 60);
                }
                break;
            case "shop":
                TelemetryWrap(npc, "shop", () => NPCGoShopping(npc));
                npc.CurrentActivity = npc.CurrentLocation == "Weapon Shop"
                    ? "examining a blade on the rack"
                    : "browsing the armor on display";
                npc.EmotionalState?.AddEmotion(EmotionType.Greed, 0.3f, 60);
                break;
            case "train":
                TelemetryWrap(npc, "train", () => NPCTrainAtGym(npc));
                npc.CurrentActivity = "training with the practice dummies";
                npc.EmotionalState?.AddEmotion(EmotionType.Confidence, 0.4f, 90);
                npc.EmotionalState?.AddEmotion(EmotionType.Pride, 0.3f, 60);
                break;
            case "levelup":
                TelemetryWrap(npc, "levelup", () => NPCVisitMaster(npc));
                npc.CurrentActivity = "consulting with the Level Master";
                npc.EmotionalState?.AddEmotion(EmotionType.Pride, 0.6f, 120);
                npc.EmotionalState?.AddEmotion(EmotionType.Joy, 0.5f, 120);
                npc.EmotionalState?.AddEmotion(EmotionType.Hope, 0.4f, 90);
                break;
            case "heal":
                TelemetryWrap(npc, "heal", () => NPCVisitHealer(npc));
                npc.CurrentActivity = "browsing the healing potions";
                npc.EmotionalState?.AddEmotion(EmotionType.Hope, 0.4f, 60);
                npc.EmotionalState?.AddEmotion(EmotionType.Peace, 0.3f, 90);
                break;
            case "move":
                TelemetryWrap(npc, "move", () => MoveNPCToRandomLocation(npc));
                npc.CurrentActivity = "passing through";
                break;
            case "team_recruit":
                TelemetryWrap(npc, "team_recruit", () => NPCTeamRecruitment(npc));
                npc.CurrentActivity = "looking for recruits";
                npc.EmotionalState?.AddEmotion(EmotionType.Hope, 0.3f, 60);
                break;
            case "quest":
                TelemetryWrap(npc, "quest", () => NPCTakeBountyQuest(npc));
                // CurrentActivity stamped inside NPCTakeBountyQuest based on
                // the outcome (claimed / completed / failed / nothing-available).
                break;
            case "team_dungeon":
                TelemetryWrap(npc, "team_dungeon", () => NPCTeamDungeonRun(npc));
                npc.CurrentActivity = "rallying the team for a dungeon run";
                npc.EmotionalState?.AddEmotion(EmotionType.Confidence, 0.5f, 90);
                npc.EmotionalState?.AddEmotion(EmotionType.Confidence, 0.4f, 90);
                break;
            case "love_street":
                TelemetryWrap(npc, "love_street", () => NPCVisitLoveStreet(npc));
                npc.CurrentActivity = "enjoying the evening company";
                npc.EmotionalState?.AddEmotion(EmotionType.Joy, 0.5f, 120);
                npc.EmotionalState?.AddEmotion(EmotionType.Peace, 0.3f, 90);
                break;
            case "temple":
                TelemetryWrap(npc, "temple", () => NPCVisitTemple(npc));
                npc.CurrentActivity = "praying quietly";
                npc.EmotionalState?.AddEmotion(EmotionType.Peace, 0.5f, 120);
                npc.EmotionalState?.AddEmotion(EmotionType.Hope, 0.4f, 90);
                break;
            case "bank":
                TelemetryWrap(npc, "bank", () => NPCVisitBank(npc));
                npc.CurrentActivity = "counting coins at the counter";
                npc.EmotionalState?.AddEmotion(EmotionType.Greed, 0.3f, 60);
                break;
            case "marketplace":
                TelemetryWrap(npc, "marketplace", () => NPCVisitMarketplace(npc));
                npc.CurrentActivity = "haggling with a merchant";
                npc.EmotionalState?.AddEmotion(EmotionType.Greed, 0.4f, 60);
                npc.EmotionalState?.AddEmotion(EmotionType.Joy, 0.2f, 60);
                break;
            case "castle":
                TelemetryWrap(npc, "castle", () => NPCVisitCastle(npc));
                npc.CurrentActivity = "attending to court business";
                npc.EmotionalState?.AddEmotion(EmotionType.Pride, 0.4f, 90);
                npc.EmotionalState?.AddEmotion(EmotionType.Confidence, 0.3f, 60);
                break;
            case "go_home":
                TelemetryWrap(npc, "go_home", () => NPCGoHome(npc));
                npc.CurrentActivity = "heading home for the day";
                npc.EmotionalState?.AddEmotion(EmotionType.Peace, 0.4f, 120);
                npc.EmotionalState?.AddEmotion(EmotionType.Joy, 0.3f, 90);
                break;
            case "dark_alley":
                TelemetryWrap(npc, "dark_alley", () => NPCVisitDarkAlley(npc));
                npc.CurrentActivity = "lurking in the shadows";
                npc.EmotionalState?.AddEmotion(EmotionType.Greed, 0.4f, 90);
                npc.EmotionalState?.AddEmotion(EmotionType.Confidence, 0.3f, 60);
                break;
            case "inn":
                TelemetryWrap(npc, "inn", () => NPCVisitInn(npc));
                npc.CurrentActivity = "having a drink at the bar";
                npc.EmotionalState?.AddEmotion(EmotionType.Joy, 0.3f, 60);
                npc.EmotionalState?.AddEmotion(EmotionType.Peace, 0.3f, 90);
                break;
            case "settlement":
                TelemetryWrap(npc, "settlement", () => { npc.CurrentLocation = "Settlement"; });
                npc.CurrentActivity = "helping build the settlement";
                npc.EmotionalState?.AddEmotion(EmotionType.Joy, 0.3f, 60);
                npc.EmotionalState?.AddEmotion(EmotionType.Hope, 0.3f, 90);
                break;
        }

        // Settlement residents: let them do activities (earn gold, train, etc.)
        // but always snap their location back to Settlement afterward
        if (isSettler && npc.CurrentLocation != "Settlement")
        {
            npc.CurrentLocation = "Settlement";
        }
    }

    /// <summary>
    /// Multiply an activity weight in the list. No-op if activity not present.
    /// </summary>
    private static void MultiplyWeight(List<(string action, double weight)> activities, string action, double multiplier)
    {
        for (int i = 0; i < activities.Count; i++)
        {
            if (activities[i].action == action)
            {
                activities[i] = (action, activities[i].weight * multiplier);
                return;
            }
        }
    }

    /// <summary>
    /// Map a location name (as stored in NPC.CurrentLocation / MemoryEvent.Location)
    /// to the activity action name used in the weight system. Returns null if no mapping.
    /// </summary>
    private static string? MapLocationToActivity(string location)
    {
        return location switch
        {
            "Dungeon" => "dungeon",
            "Weapon Shop" or "Armor Shop" or "Magic Shop" => "shop",
            "Auction House" => "marketplace",
            "Inn" => "inn",
            "Temple" or "Church" => "temple",
            "Castle" => "castle",
            "Love Street" => "love_street",
            "Dark Alley" => "dark_alley",
            "Bank" => "bank",
            "Healer" => "heal",
            "Home" => "go_home",
            "Main Street" => "move",
            "Gym" => "train",
            _ => null
        };
    }

    /// <summary>
    /// Apply personality-driven weight modifiers so NPCs behave according to their traits.
    /// Aggressive NPCs dungeon more, scholarly NPCs train more, sociable NPCs socialize more, etc.
    /// </summary>
    private static void ApplyPersonalityWeights(List<(string action, double weight)> activities, NPC npc)
    {
        var p = npc.Brain?.Personality;
        if (p == null) return;

        if (p.Aggression > 0.6f)
        {
            MultiplyWeight(activities, "dungeon", 1.5);
            MultiplyWeight(activities, "team_dungeon", 1.4);
            MultiplyWeight(activities, "train", 1.15); // v0.64.1 anti-runaway: 1.3 -> 1.15
            MultiplyWeight(activities, "dark_alley", 1.4);
            MultiplyWeight(activities, "shop", 0.7);
            MultiplyWeight(activities, "quest", 1.5); // bounties feed the hunt
        }
        if (p.Sociability > 0.6f)
        {
            MultiplyWeight(activities, "love_street", 1.5);
            MultiplyWeight(activities, "inn", 1.5);
            MultiplyWeight(activities, "move", 1.4); // wander and meet people
            MultiplyWeight(activities, "temple", 0.8);
        }
        if (p.Greed > 0.6f)
        {
            MultiplyWeight(activities, "shop", 1.5);
            MultiplyWeight(activities, "bank", 1.5);
            MultiplyWeight(activities, "marketplace", 1.4);
            MultiplyWeight(activities, "dark_alley", 1.3);
            MultiplyWeight(activities, "dungeon", 1.2);
            MultiplyWeight(activities, "quest", 1.4); // bounties pay coin
        }
        if (p.Intelligence > 0.6f)
        {
            MultiplyWeight(activities, "train", 1.2); // v0.64.1 anti-runaway: 1.4 -> 1.2
            MultiplyWeight(activities, "shop", 1.2);
            MultiplyWeight(activities, "dungeon", 0.8);
        }
        if (p.Mysticism > 0.6f)
        {
            MultiplyWeight(activities, "temple", 1.5);
            MultiplyWeight(activities, "train", 1.1); // v0.64.1 anti-runaway: 1.2 -> 1.1
            MultiplyWeight(activities, "shop", 0.8);
        }
        if (p.Caution > 0.6f)
        {
            MultiplyWeight(activities, "heal", 1.5);
            MultiplyWeight(activities, "bank", 1.3);
            MultiplyWeight(activities, "dungeon", 0.6);
            MultiplyWeight(activities, "team_dungeon", 0.7);
        }
        if (p.Courage > 0.7f)
        {
            MultiplyWeight(activities, "dungeon", 1.4);
            MultiplyWeight(activities, "team_dungeon", 1.3);
            MultiplyWeight(activities, "castle", 1.3);
            MultiplyWeight(activities, "quest", 1.3); // brave NPCs answer the board
        }
        if (p.Patience < 0.3f)
        {
            MultiplyWeight(activities, "move", 1.3); // restless, always moving
            MultiplyWeight(activities, "dungeon", 1.3);
            MultiplyWeight(activities, "temple", 0.5);
        }
        // Sociable and ambitious NPCs are more likely to form/join teams
        if (p.Sociability > 0.5f)
        {
            MultiplyWeight(activities, "team_recruit", 1.4);
        }
        if (p.Ambition > 0.6f)
        {
            MultiplyWeight(activities, "team_recruit", 1.3);
        }
        if (p.Loyalty > 0.6f)
        {
            MultiplyWeight(activities, "team_recruit", 1.2);
        }
    }

    /// <summary>
    /// Apply time-of-day weight modifiers. NPCs train in the morning, shop in the afternoon,
    /// socialize in the evening, and go home at night.
    /// </summary>
    private static void ApplyTimeOfDayWeights(List<(string action, double weight)> activities)
    {
        int hour = DailySystemManager.GetCurrentGameHour();

        if (hour >= 6 && hour < 12) // Morning
        {
            MultiplyWeight(activities, "train", 1.15); // v0.64.1 anti-runaway: 1.3 -> 1.15
            MultiplyWeight(activities, "shop", 1.2);
            MultiplyWeight(activities, "temple", 1.3);
        }
        else if (hour >= 12 && hour < 18) // Afternoon
        {
            MultiplyWeight(activities, "dungeon", 1.3);
            MultiplyWeight(activities, "team_dungeon", 1.3);
            MultiplyWeight(activities, "shop", 1.2);
            MultiplyWeight(activities, "marketplace", 1.2);
        }
        else if (hour >= 18 && hour < 23) // Evening
        {
            MultiplyWeight(activities, "move", 1.5); // socializing
            MultiplyWeight(activities, "love_street", 1.4);
            MultiplyWeight(activities, "inn", 1.5); // evening drinks
            MultiplyWeight(activities, "dark_alley", 1.6); // shady business after dark
            MultiplyWeight(activities, "go_home", 1.3);
        }
        else // Night (23-5)
        {
            MultiplyWeight(activities, "go_home", 2.0);
            MultiplyWeight(activities, "inn", 1.3); // late-night drinkers
            MultiplyWeight(activities, "dark_alley", 1.8); // dark alley thrives at night
            MultiplyWeight(activities, "dungeon", 0.5);
            MultiplyWeight(activities, "team_dungeon", 0.5);
            MultiplyWeight(activities, "shop", 0.3);
            MultiplyWeight(activities, "marketplace", 0.3);
            MultiplyWeight(activities, "heal", 0.5);
        }
    }

    /// <summary>
    /// Apply memory-driven weight modifiers. Processes all recent memories (not just 5)
    /// and accumulates location-specific sentiment from MemoryEvent.Location + EmotionalImpact.
    /// Defeat avoidance scales by severity (importance field).
    /// </summary>
    private static void ApplyMemoryWeights(List<(string action, double weight)> activities, NPC npc)
    {
        var memories = npc.Brain?.Memory?.GetRecentEvents(48); // last 48 hours
        if (memories == null || memories.Count == 0) return;

        // Track cumulative location sentiment: location -> sum of emotional impacts
        var locationSentiment = new Dictionary<string, double>();

        foreach (var mem in memories)
        {
            // --- Global activity modifiers based on memory type ---
            switch (mem.Type)
            {
                case MemoryType.Attacked:
                case MemoryType.Defeated:
                    // Scale dungeon avoidance by severity: importance 0.5 → 0.7x, importance 1.0 → 0.4x
                    double defeatSeverity = 0.7 - (mem.Importance * 0.3);
                    MultiplyWeight(activities, "dungeon", defeatSeverity);
                    MultiplyWeight(activities, "team_dungeon", defeatSeverity + 0.1);
                    MultiplyWeight(activities, "heal", 1.0 + mem.Importance * 0.5);  // 1.0–1.5x
                    MultiplyWeight(activities, "train", 1.1 + mem.Importance * 0.2); // 1.1–1.3x
                    break;

                case MemoryType.Betrayed:
                    MultiplyWeight(activities, "go_home", 1.3);
                    MultiplyWeight(activities, "move", 0.7);
                    MultiplyWeight(activities, "dungeon", 0.8);
                    break;

                case MemoryType.Helped:
                case MemoryType.Saved:
                case MemoryType.Defended:
                    MultiplyWeight(activities, "dungeon", 1.1);
                    MultiplyWeight(activities, "team_dungeon", 1.1);
                    break;

                case MemoryType.Traded:
                case MemoryType.BoughtItem:
                case MemoryType.SoldItem:
                    MultiplyWeight(activities, "shop", 1.15);
                    MultiplyWeight(activities, "marketplace", 1.2);
                    break;

                case MemoryType.SawDeath:
                    MultiplyWeight(activities, "temple", 1.3);
                    MultiplyWeight(activities, "dungeon", 0.7);
                    break;
            }

            // --- Accumulate location-specific sentiment ---
            if (!string.IsNullOrEmpty(mem.Location))
            {
                double sentiment = mem.EmotionalImpact; // -1.0 to 1.0
                if (!locationSentiment.ContainsKey(mem.Location))
                    locationSentiment[mem.Location] = 0;
                locationSentiment[mem.Location] += sentiment;
            }
        }

        // --- Apply location sentiment to matching activities ---
        foreach (var (location, sentiment) in locationSentiment)
        {
            string? activityName = MapLocationToActivity(location);
            if (activityName == null) continue;

            // Clamp sentiment to [-3, 3] to prevent runaway multipliers
            double clamped = Math.Clamp(sentiment, -3.0, 3.0);

            // Convert sentiment to multiplier: -3 → 0.4x, 0 → 1.0x, +3 → 1.6x
            double multiplier = 1.0 + (clamped * 0.2);
            MultiplyWeight(activities, activityName, multiplier);
        }
    }

    /// <summary>
    /// Apply relationship-driven weight modifiers. NPCs prefer locations where friends are
    /// and avoid locations where enemies are. Boosts inn when friends are at the inn;
    /// boosts move when friends are elsewhere.
    /// </summary>
    private void ApplyRelationshipWeights(List<(string action, double weight)> activities, NPC npc)
    {
        // Build a quick map: location -> (friendCount, enemyCount)
        var locationPresence = new Dictionary<string, (int friends, int enemies)>();

        foreach (var other in npcs)
        {
            if (other == npc || !other.IsAlive || other.IsDead) continue;
            if (string.IsNullOrEmpty(other.CurrentLocation)) continue;

            int rel = RelationshipSystem.GetRelationshipLevel(npc, other);
            bool isFriend = rel <= GameConfig.RelationFriendship;  // 40 or below
            bool isEnemy = rel >= GameConfig.RelationEnemy;         // 100 or above

            if (!isFriend && !isEnemy) continue;

            var loc = other.CurrentLocation;
            if (!locationPresence.ContainsKey(loc))
                locationPresence[loc] = (0, 0);

            var current = locationPresence[loc];
            if (isFriend)
                locationPresence[loc] = (current.friends + 1, current.enemies);
            else
                locationPresence[loc] = (current.friends, current.enemies + 1);
        }

        if (locationPresence.Count == 0) return;

        // Friends at the Inn specifically → boost inn activity
        if (locationPresence.TryGetValue("Inn", out var innPresence))
        {
            if (innPresence.friends > 0)
                MultiplyWeight(activities, "inn", 1.0 + innPresence.friends * 0.2); // +20% per friend
            if (innPresence.enemies > 0)
                MultiplyWeight(activities, "inn", Math.Max(0.4, 1.0 - innPresence.enemies * 0.25));
        }

        // Friends elsewhere (not at NPC's current location) → boost move to seek them out
        bool friendsElsewhere = locationPresence.Any(kv =>
            kv.Key != npc.CurrentLocation && kv.Value.friends > 0);
        if (friendsElsewhere)
            MultiplyWeight(activities, "move", 1.3);

        // Boost/penalize specific activities based on friend/enemy presence at that location
        foreach (var (location, presence) in locationPresence)
        {
            string? activity = MapLocationToActivity(location);
            if (activity == null) continue;

            if (presence.friends > 0)
                MultiplyWeight(activities, activity, 1.0 + presence.friends * 0.15); // +15% per friend
            if (presence.enemies > 0)
                MultiplyWeight(activities, activity, Math.Max(0.3, 1.0 - presence.enemies * 0.3)); // -30% per enemy
        }
    }

    /// <summary>
    /// Conway-inspired neighbor pressure. NPCs react to population density at their current location:
    /// isolation drives movement, small groups stabilize, overcrowding drives dispersal,
    /// rival presence drives flight or aggression, safe havens encourage recovery.
    /// </summary>
    private void ApplyNeighborPressure(List<(string action, double weight)> activities, NPC npc)
    {
        if (string.IsNullOrEmpty(npc.CurrentLocation)) return;

        int totalNeighbors = 0;
        int allies = 0;
        int rivals = 0;

        foreach (var other in npcs)
        {
            if (other == npc || !other.IsAlive || other.IsDead) continue;
            if (other.CurrentLocation != npc.CurrentLocation) continue;

            totalNeighbors++;
            int rel = RelationshipSystem.GetRelationshipLevel(npc, other);
            if (rel <= GameConfig.RelationFriendship)
                allies++;
            else if (rel >= GameConfig.RelationEnemy)
                rivals++;
        }

        // Isolation: few neighbors, few allies → seek company
        if (totalNeighbors <= GameConfig.NeighborIsolationMax && allies <= 1)
        {
            MultiplyWeight(activities, "move", 1.5);
        }

        // Stability: healthy ally count → settle in and socialize
        if (allies >= GameConfig.NeighborStabilityMin && allies <= GameConfig.NeighborStabilityMax)
        {
            MultiplyWeight(activities, "inn", 1.3);
            MultiplyWeight(activities, "temple", 1.3);
            MultiplyWeight(activities, "team_recruit", 1.3);
        }

        // Overcrowding: too many NPCs at one location → disperse
        if (totalNeighbors >= GameConfig.NeighborOvercrowdingMin)
        {
            MultiplyWeight(activities, "move", 1.4);
            MultiplyWeight(activities, "inn", 0.7);
            MultiplyWeight(activities, "temple", 0.7);
            MultiplyWeight(activities, "team_recruit", 0.7);
        }

        // Hostile territory: rivals present → flee or fight
        if (rivals >= GameConfig.NeighborRivalThreshold)
        {
            MultiplyWeight(activities, "move", 1.6);
            MultiplyWeight(activities, "dungeon", 1.2);
        }

        // Safe haven: allies present, no rivals → rest and improve
        if (allies >= GameConfig.NeighborStabilityMin && rivals == 0)
        {
            MultiplyWeight(activities, "heal", 1.3);
            MultiplyWeight(activities, "train", 1.2);
        }
    }

    /// <summary>
    /// Apply world event-driven weight modifiers. NPCs react to active wars, plagues,
    /// festivals, and throne changes based on personality and faction.
    /// </summary>
    private static void ApplyWorldEventWeights(List<(string action, double weight)> activities, NPC npc)
    {
        var worldEvents = WorldEventSystem.Instance;
        if (worldEvents == null) return;

        var personality = npc.Brain?.Personality;

        // --- WAR: brave NPCs fight, cautious NPCs hide, Crown faction rallies ---
        if (worldEvents.WarActive)
        {
            if (personality != null && (personality.Aggression > 0.5f || personality.Courage > 0.5f))
            {
                MultiplyWeight(activities, "dungeon", 1.4);
                MultiplyWeight(activities, "team_dungeon", 1.35);
                MultiplyWeight(activities, "train", 1.25);
            }
            if (personality != null && personality.Caution > 0.5f)
            {
                MultiplyWeight(activities, "go_home", 1.3);
                MultiplyWeight(activities, "bank", 1.3);
                MultiplyWeight(activities, "dungeon", 0.6);
            }
            if (npc.NPCFaction == Faction.TheCrown)
            {
                MultiplyWeight(activities, "castle", 1.5);
                MultiplyWeight(activities, "train", 1.2);
            }
        }

        // --- PLAGUE: everyone heals more, cautious avoid dungeon, sociable reduce contact ---
        if (worldEvents.PlaguActive)
        {
            MultiplyWeight(activities, "heal", 1.3);
            MultiplyWeight(activities, "temple", 1.25);
            MultiplyWeight(activities, "marketplace", 0.8);

            if (personality != null && personality.Caution > 0.4f)
            {
                MultiplyWeight(activities, "dungeon", 0.5);
                MultiplyWeight(activities, "team_dungeon", 0.6);
            }
            if (personality != null && personality.Sociability > 0.5f)
            {
                MultiplyWeight(activities, "inn", 0.7);
                MultiplyWeight(activities, "love_street", 0.6);
                MultiplyWeight(activities, "move", 0.75);
            }
        }

        // --- FESTIVAL: everyone celebrates, sociable NPCs party hard ---
        if (worldEvents.FestivalActive)
        {
            MultiplyWeight(activities, "marketplace", 1.2);
            MultiplyWeight(activities, "inn", 1.15);
            MultiplyWeight(activities, "dungeon", 0.8);
            MultiplyWeight(activities, "team_dungeon", 0.85);

            if (personality != null && personality.Sociability > 0.5f)
            {
                MultiplyWeight(activities, "inn", 1.35);
                MultiplyWeight(activities, "move", 1.25);
                MultiplyWeight(activities, "love_street", 1.3);
            }
        }

        // --- THRONE EVENTS: faction-specific reactions ---
        var activeEvents = worldEvents.GetActiveEvents();
        bool throneEvent = activeEvents.Any(e =>
            e.Type == WorldEventSystem.EventType.KingMartialLaw ||
            e.Type == WorldEventSystem.EventType.KingWarDeclaration);

        if (throneEvent)
        {
            if (npc.NPCFaction == Faction.TheCrown)
                MultiplyWeight(activities, "castle", 1.5);
            if (npc.NPCFaction == Faction.TheShadows)
            {
                MultiplyWeight(activities, "dark_alley", 1.4);
                MultiplyWeight(activities, "inn", 1.2);
            }
            if (npc.NPCFaction == Faction.TheFaith)
                MultiplyWeight(activities, "temple", 1.4);
        }
    }

    /// <summary>
    /// NPC attempts to form or join a team, or recruit members
    /// </summary>
    private void NPCTeamRecruitment(NPC npc)
    {
        // Online mode: cooldown on team actions to prevent rapid join/leave/form churn
        if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
        {
            if (_npcTeamActionCooldown.TryGetValue(npc.Id, out int lastTick) &&
                _currentTick - lastTick < TEAM_ACTION_COOLDOWN_TICKS)
                return;
        }

        if (string.IsNullOrEmpty(npc.Team))
        {
            // Try to join an existing team or form a new one
            NPCTryJoinOrFormTeam(npc);
        }
        else
        {
            // Already in a team - try to recruit others
            NPCTryRecruitForTeam(npc);
        }
    }

    /// <summary>
    /// NPC tries to join an existing team or form a new one
    /// </summary>
    private void NPCTryJoinOrFormTeam(NPC npc)
    {
        // Look for existing NPC teams at this location to join
        // Exclude player teams — NPCs shouldn't autonomously join them
        var teamsAtLocation = npcs
            .Where(n => n.IsAlive && !string.IsNullOrEmpty(n.Team) && n.CurrentLocation == npc.CurrentLocation &&
                        !IsPlayerTeam(n.Team))
            .GroupBy(n => n.Team)
            .Where(g => g.Count() < MAX_TEAM_SIZE)
            .ToList();

        // Online: 30% join chance (was 75%) — teams grow more gradually
        float joinChance = UsurperRemake.BBS.DoorMode.IsOnlineMode ? 0.30f : 0.75f;
        if (teamsAtLocation.Any() && random.NextDouble() < joinChance)
        {
            // Try to join an existing team
            var teamGroup = teamsAtLocation[random.Next(teamsAtLocation.Count)];
            var teamLeader = teamGroup.FirstOrDefault();
            if (teamLeader != null)
            {
                // Check compatibility with team leader
                var compatibility = npc.Brain?.Personality?.GetCompatibility(teamLeader.Brain?.Personality) ?? 0.5f;
                if (compatibility > 0.3f)
                {
                    // Join the team!
                    npc.Team = teamLeader.Team;
                    npc.TeamPW = teamLeader.TeamPW;
                    npc.CTurf = teamLeader.CTurf;

                    NewsSystem.Instance.Newsy(true, $"{npc.Name} joined the team '{npc.Team}'!");
                    if (UsurperRemake.BBS.DoorMode.IsOnlineMode) _npcTeamActionCooldown[npc.Id] = _currentTick;
                    return;
                }
            }
        }

        // Form a new team if we have compatible NPCs nearby (or anywhere if no locals found)
        var nearbyUnteamed = npcs
            .Where(n => n.IsAlive && !n.IsDead &&
                   string.IsNullOrEmpty(n.Team) &&
                   n.CurrentLocation == npc.CurrentLocation &&
                   n.Id != npc.Id)
            .ToList();

        // Fall back to any unaffiliated NPC in the realm if nobody at this location
        if (nearbyUnteamed.Count == 0)
        {
            nearbyUnteamed = npcs
                .Where(n => n.IsAlive && !n.IsDead &&
                       string.IsNullOrEmpty(n.Team) &&
                       n.Id != npc.Id &&
                       n.Brain?.Personality?.IsLikelyToJoinGang() == true)
                .ToList();
        }

        // Online: 15% form chance (was 50%) — new teams are rarer events
        float formChance = UsurperRemake.BBS.DoorMode.IsOnlineMode ? 0.15f : 0.50f;
        if (nearbyUnteamed.Count >= 1 && random.NextDouble() < formChance)
        {
            // Form a new team
            var teamName = GenerateTeamName();
            var teamPassword = Guid.NewGuid().ToString().Substring(0, 8);

            npc.Team = teamName;
            npc.TeamPW = teamPassword;
            npc.CTurf = false;
            npc.TeamRec = 0;

            // Add first recruit - try a few candidates for compatibility
            NPC? bestRecruit = null;
            float bestCompat = 0f;
            int tries = Math.Min(nearbyUnteamed.Count, 5);
            for (int t = 0; t < tries; t++)
            {
                var candidate = nearbyUnteamed[random.Next(nearbyUnteamed.Count)];
                var compat = npc.Brain?.Personality?.GetCompatibility(candidate.Brain?.Personality) ?? 0.5f;
                if (compat > bestCompat) { bestCompat = compat; bestRecruit = candidate; }
            }

            if (bestRecruit != null && bestCompat > 0.25f)
            {
                bestRecruit.Team = teamName;
                bestRecruit.TeamPW = teamPassword;
                bestRecruit.CTurf = false;

                NewsSystem.Instance.Newsy(true, $"{npc.Name} formed a new team called '{teamName}' with {bestRecruit.Name}!");
                if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
                {
                    _npcTeamActionCooldown[npc.Id] = _currentTick;
                    _npcTeamActionCooldown[bestRecruit.Id] = _currentTick;
                }
            }
            else
            {
                // No compatible recruit found - undo the team assignment
                npc.Team = "";
                npc.TeamPW = "";
            }
        }
    }

    /// <summary>
    /// NPC tries to recruit others into their team
    /// </summary>
    private void NPCTryRecruitForTeam(NPC npc)
    {
        if (string.IsNullOrEmpty(npc.Team)) return;

        // Don't let NPCs autonomously recruit into the player's team
        if (IsPlayerTeam(npc.Team))
            return;

        // Check current team size
        var teamSize = npcs.Count(n => n.Team == npc.Team && n.IsAlive);
        if (teamSize >= MAX_TEAM_SIZE) return;

        // Find candidates at this location
        var candidates = npcs
            .Where(n => n.IsAlive &&
                   string.IsNullOrEmpty(n.Team) &&
                   n.CurrentLocation == npc.CurrentLocation &&
                   n.Id != npc.Id)
            .ToList();

        if (candidates.Count == 0) return;

        var candidate = candidates[random.Next(candidates.Count)];
        var compatibility = npc.Brain?.Personality?.GetCompatibility(candidate.Brain?.Personality) ?? 0.5f;

        // Base recruitment chance on compatibility, charisma, and candidate's gang-joining tendency
        float recruitChance = compatibility * 0.5f;
        recruitChance += (npc.Charisma / 100f) * 0.2f;
        if (candidate.Brain?.Personality?.IsLikelyToJoinGang() == true)
        {
            recruitChance += 0.2f;
        }

        if (random.NextDouble() < recruitChance)
        {
            candidate.Team = npc.Team;
            candidate.TeamPW = npc.TeamPW;
            candidate.CTurf = npc.CTurf;

            if (random.NextDouble() < 0.3) // 30% chance to announce
            {
                NewsSystem.Instance.Newsy(true, $"{npc.Name} recruited {candidate.Name} into '{npc.Team}'!");
            }
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
            {
                _npcTeamActionCooldown[npc.Id] = _currentTick;
                _npcTeamActionCooldown[candidate.Id] = _currentTick;
            }
        }
    }

    /// <summary>
    /// Generate a random team name
    /// </summary>
    private string GenerateTeamName()
    {
        var prefix = TeamNamePrefixes[random.Next(TeamNamePrefixes.Length)];
        var suffix = TeamNameSuffixes[random.Next(TeamNameSuffixes.Length)];
        return $"{prefix} {suffix}";
    }

    /// <summary>
    /// NPC does a team dungeon run with teammates
    /// </summary>
    private void NPCTeamDungeonRun(NPC npc)
    {
        if (string.IsNullOrEmpty(npc.Team)) return;
        if (npc.IsInConversation) return;

        // v0.61.2 Phase 1 telemetry: capture leader state for log row.
        string locationBefore = npc.CurrentLocation;
        long hpBefore = npc.HP;
        long goldBefore = npc.Gold;
        long xpBefore = npc.Experience;
        void LogTeamDungeonDecision(string outcome)
        {
            SqlBackend?.LogNPCDecision(
                npc.Name2 ?? npc.Name1 ?? "(unknown)",
                (int)npc.Level,
                npc.Class.ToString(),
                "team_dungeon",
                locationBefore,
                npc.CurrentLocation,
                outcome,
                npc.Gold - goldBefore,
                npc.Experience - xpBefore,
                hpBefore,
                npc.HP,
                npc.IsAIDriven);
        }

        // v0.61.3 self-preservation gates for team_dungeon, mirroring the
        // v0.61.2 pattern applied to NPCExploreDungeon. The first telemetry
        // pass (13K rows / 13 hours) showed team_dungeon dying at 71% with
        // ZERO successful flees vs solo dungeon at 10.6% death and 84.8%
        // successful flees. The asymmetry was protection scope: the v0.61.2
        // fix only touched NPCExploreDungeon. Team_dungeon kept the pre-fix
        // 25% flee threshold + "must take 2 rounds of damage first" gate.
        // This block applies the same gates the solo path now uses.
        var leaderPersonality = npc.Brain?.Personality;

        // v0.61.5 Gate 0: Lv 1-4 teams are a meat grinder. Production
        // telemetry across 174k NPC decisions showed Lv 1-4 team_dungeon at
        // 100% death rate (51 of 51) and Lv 5-9 at 84% (895 of 1069). The
        // existing gates work fine at 40+ (18% death) but don't help at the
        // low end because avgLevel - 5 still floors at 1 and low-level
        // monsters punch hard relative to NPCs without gear. Block the
        // attempt entirely; let the leader find a safer action next tick.
        //
        // v0.63.2: 283k decisions over 14 days showed the Lv 8-18 band still
        // dying at 8-10% per action, with the Lv 10-19 cohort accumulating
        // the most team_dungeon deaths. Raise the gate from < 5 to < 10.
        if (npc.Level < 10)
        {
            npc.UpdateLocation("Inn");
            npc.CurrentActivity = "telling the team they aren't ready yet";
            LogTeamDungeonDecision("aborted_underleveled");
            return;
        }

        // Gate 1: wounded leader doesn't lead a dungeon run. v0.61.5 raised
        // 0.7 -> 0.8 -- at 71% HP the leader was walking into the AoE damage
        // spread with too little margin and tipping the whole team into a
        // loss. 80% gives a real buffer.
        if (npc.HP < npc.MaxHP * 0.8)
        {
            npc.UpdateLocation("Healer");
            npc.CurrentActivity = "tending to wounds before any expedition";
            LogTeamDungeonDecision("aborted_wounded");
            return;
        }

        // Gate 2: cautious leaders (low Courage AND low Ambition) don't
        // rally the team for dungeon runs. 90% abort rate; the 10% slip-
        // through preserves the rare bad-judgment case for narrative color.
        if (leaderPersonality != null && leaderPersonality.Courage < 0.3f && leaderPersonality.Ambition < 0.3f
            && random.Next(100) < 90)
        {
            npc.UpdateLocation("Inn");
            npc.CurrentActivity = "talking up a dungeon run that never quite happens";
            LogTeamDungeonDecision("aborted_cautious");
            return;
        }

        // Get all alive team members (skip any engaged with players or below
        // 50% HP). Same 50% threshold the existing code used, just clearer.
        var teamMembers = npcs
            .Where(n => n.Team == npc.Team && n.IsAlive && n.HP > n.MaxHP * 0.5 && !n.IsInConversation)
            .ToList();

        if (teamMembers.Count < 2)
        {
            // Not enough healthy teammates, do solo dungeon run. Solo path has
            // its own telemetry hook so we don't log a row here.
            NPCExploreDungeon(npc);
            return;
        }

        // Move team to dungeon
        foreach (var member in teamMembers)
        {
            member.UpdateLocation("Dungeon");
        }

        // Determine dungeon level. First-day telemetry showed the
        // avgLevel - 2 to avgLevel range still produced 45% deaths and 6.5%
        // completions for teams. v0.61.3 baseline (avgLevel - 5 to avgLevel - 3)
        // worked at high levels (40+ band hit 18% death) but low-level teams
        // were still catastrophically losing (Lv 10-19 band at 76% death rate
        // across 3023 attempts).
        //
        // v0.61.5: split the floor pick by team avg level. Sub-20 teams pick
        // avgLevel - 7 to avgLevel - 5 AND skip the Courage/Ambition push-up
        // (a brave low-level leader is just walking into worse monsters with
        // the same low-level kit). 20+ teams keep the old pick.
        int avgLevel = (int)teamMembers.Average(m => m.Level);
        int dungeonLevel;
        // v0.63.2: even at avgLevel - 7, the Lv 10-19 band died at 8-10% per
        // action across 14 days of telemetry. Drop the low-band floor to
        // avgLevel - 10 to avgLevel - 8 (was -7 to -5), giving naked-gear
        // NPCs a real shot at completion. High-band tuning unchanged --
        // 40+ NPCs already survive at 1-2% death rate per existing gates.
        if (avgLevel < 20)
        {
            dungeonLevel = avgLevel - 10 + random.Next(0, 3);
            // Skip Courage/Ambition bonus -- low-level teams shouldn't
            // overreach. They die when they do.
        }
        else if (avgLevel < 30)
        {
            // Lv 20-29 mid-band: less aggressive than 30+ but more than
            // sub-20. avgLevel - 8 to avgLevel - 6.
            dungeonLevel = avgLevel - 8 + random.Next(0, 3);
        }
        else
        {
            dungeonLevel = avgLevel - 5 + random.Next(0, 3);
            if (leaderPersonality != null)
            {
                if (leaderPersonality.Courage > 0.7f) dungeonLevel += random.Next(0, 2);
                if (leaderPersonality.Ambition > 0.7f) dungeonLevel += random.Next(0, 2);
            }
        }
        dungeonLevel = Math.Clamp(dungeonLevel, 1, 100);

        // Generate monster group (teams fight groups of monsters)
        int monsterCount = Math.Min(teamMembers.Count, random.Next(2, 5));
        var monsters = new List<Monster>();
        for (int i = 0; i < monsterCount; i++)
        {
            monsters.Add(MonsterGenerator.GenerateMonster(dungeonLevel));
        }
        // Snapshot total monster HP for the partial-XP-on-loss calc below.
        long groupStartHP = monsters.Sum(m => m.HP);

        // v0.64.0 Brain v2 Slice 4: Tier A team leaders route the whole team
        // through real combat. The leader's Tier A status governs the whole
        // party so a Tier A king + his 4 Lv 5 immigrant teammates all fight
        // with abilities / potions / real damage (the team is only as
        // sophisticated as its weakest member would otherwise be).
        bool teamWon;
        long totalExp;
        long totalGold;
        if (IsTierANPC(npc))
        {
            // Real-combat path. Leader is the primary; rest are teammates.
            var leader = teamMembers[0];
            var partyAllies = teamMembers.Skip(1).Cast<Character>().ToList();
            var simResult = NPCCombatSimulator.Simulate(leader, monsters, partyAllies, random);
            totalExp = simResult.ExpReward;
            totalGold = simResult.GoldReward;
            teamWon = simResult.Outcome == NPCCombatOutcome.Won;
            // Sync HP/Mana/Stamina back to team members (already mutated via
            // refs inside the simulator). MarkNPCDead for any teammate whose
            // HP hit 0 -- the simulator just sets HP to 0; the world-sim is
            // responsible for the permadeath cascade.
            foreach (var member in teamMembers.Where(m => m.HP <= 0).ToList())
            {
                MarkNPCDead(member, 0.05f, monsters.FirstOrDefault()?.Name ?? "a monster", "the dungeon");
            }
        }
        else
        {
            teamWon = SimulateTeamVsMonsterCombat(teamMembers, monsters, out totalExp, out totalGold);
        }

        if (teamWon)
        {
            // Distribute rewards evenly among surviving team members
            var survivors = teamMembers.Where(m => m.IsAlive).ToList();
            if (survivors.Count > 0)
            {
                long expShare = totalExp / survivors.Count;
                long goldShare = totalGold / survivors.Count;

                // Bonus for team play, throttled by NpcXpMultiplier for world sim mode
                expShare = (long)(expShare * 1.15 * NpcXpMultiplier);

                foreach (var member in survivors)
                {
                    member.GainExperience(expShare);
                    member.GainGold(goldShare);
                }

                // Generate news for notable victories
                if (random.NextDouble() < 0.15 || monsters.Any(m => m.IsBoss))
                {
                    NewsSystem.Instance.Newsy(true, $"Team '{npc.Team}' conquered dungeon level {dungeonLevel}, defeating {monsterCount} monsters!");
                }

                // GD.Print($"[WorldSim] Team '{npc.Team}' won! {survivors.Count} survivors shared {totalExp} XP and {totalGold} gold");

                // Return to town after the fight
                foreach (var member in survivors)
                {
                    member.UpdateLocation(member.HP < member.MaxHP * 0.5 ? "Healer" : "Inn");
                }
            }
            LogTeamDungeonDecision("won");
        }
        else
        {
            // Team lost - check for deaths (permadeath roll per member)
            var dead = teamMembers.Where(m => !m.IsAlive).ToList();
            if (dead.Any())
            {
                var killerName = monsters.FirstOrDefault()?.Name ?? "dungeon monsters";
                foreach (var deadMember in dead)
                {
                    MarkNPCDead(deadMember, GameConfig.PermadeathChanceDungeonTeam, killerName, "the Dungeon");
                }
            }

            // Partial XP for surviving members when the team dealt meaningful
            // damage before the loss. Same threshold as the solo flee path
            // (>=30% of monster group HP). Telemetry showed team-dungeon at
            // 45% deaths / 6.5% completions with zero XP on the other 88%
            // of attempts; surviving members ate damage but got nothing for
            // their effort. Capped at 50% of the win reward to keep losing
            // strictly worse than winning. Basis is the full hypothetical
            // win reward (not the kill-discounted `totalExp`), since the
            // partial-credit signal is damage dealt, not kills landed.
            long groupEndHP = monsters.Sum(m => Math.Max(0L, m.HP));
            long groupDamageDealt = Math.Max(0, groupStartHP - groupEndHP);
            float groupDealtPct = groupStartHP > 0 ? (float)groupDamageDealt / groupStartHP : 0f;
            if (groupDealtPct >= 0.30f)
            {
                var survivors = teamMembers.Where(m => m.IsAlive).ToList();
                if (survivors.Count > 0)
                {
                    long fullReward = monsters.Sum(m => m.GetExperienceReward());
                    float xpScale = Math.Min(0.50f, groupDealtPct * 0.50f);
                    long partialExpShare = (long)(fullReward * xpScale * NpcXpMultiplier / survivors.Count);
                    if (partialExpShare > 0)
                    {
                        foreach (var member in survivors)
                            member.GainExperience(partialExpShare);
                    }
                }
            }

            // Survivors flee
            foreach (var survivor in teamMembers.Where(m => m.IsAlive))
            {
                survivor.UpdateLocation("Main Street");
            }
            // Outcome from the leader's perspective: if leader died, log "died";
            // if leader survived but team lost, log "fled".
            LogTeamDungeonDecision(npc.IsAlive ? "fled" : "died");
        }
    }

    /// <summary>
    /// Simulate team vs monster group combat
    /// </summary>
    private bool SimulateTeamVsMonsterCombat(List<NPC> team, List<Monster> monsters, out long totalExp, out long totalGold)
    {
        totalExp = 0;
        totalGold = 0;
        int rounds = 0;
        const int maxRounds = 40;

        while (team.Any(m => m.IsAlive) && monsters.Any(m => m.IsAlive) && rounds < maxRounds)
        {
            rounds++;

            // v0.61.3: flee gate rewritten to match the v0.61.2 solo path.
            // Old: HP < 25% AND rounds > 2. With 2-4 monsters hitting the team
            // each round, 25% HP was usually one hit from dead by the time the
            // gate triggered, AND the 2-round delay meant the team ate 4-8
            // monster swings before anyone considered fleeing. Telemetry showed
            // this produced a 71% death rate with ZERO successful flees.
            // New: HP < 50% baseline (Courage-scaled like solo: 60% for low-
            // Courage members, 35% for brave), no rounds gate, base flee
            // chance bumped 55%->70% so successful flees actually stick.
            foreach (var member in team.Where(m => m.IsAlive).ToList())
            {
                // v0.63.2 Fix A retune2: flee threshold dropped further to 20%
                // baseline (10% brave, 30% cowardly). At 35% NPCs were still
                // bailing on combats they were trading roughly 1:1 in -- the
                // partial-credit XP path was firing (good) but no full wins.
                // 20% means an NPC will press on through ~80% of their HP
                // before fleeing, which is enough to claim most kills given
                // the new Lv*6 damage floor.
                float fleeThreshold = 0.20f;
                var p = member.Brain?.Personality;
                if (p != null)
                {
                    if (p.Courage < 0.3f) fleeThreshold = 0.30f;
                    else if (p.Courage > 0.7f) fleeThreshold = 0.10f;
                }
                if (member.HP >= member.MaxHP * fleeThreshold) continue;

                int fleeChance = 70 + (int)(member.Agility / 3);
                if (random.Next(100) < Math.Min(95, fleeChance))
                {
                    // Flee — remove from combat but keep alive
                    member.UpdateLocation("Healer");
                }
            }
            // If all team members fled, end combat
            if (!team.Any(m => m.IsAlive && m.Location == "Dungeon")) break;

            // v0.61.3 Cleric sim self-heal (team path). Same shape as solo:
            // Clerics restore 10% MaxHP per round when below 50% HP. Also
            // covers party Clerics helping the team survive longer, which
            // raises completion rate beyond the solo case.
            foreach (var member in team.Where(m => m.IsAlive && m.Location == "Dungeon"))
            {
                if (member.Class == CharacterClass.Cleric && member.HP > 0 && member.HP < member.MaxHP * 0.5)
                {
                    long heal = member.MaxHP / 10;
                    member.HP = Math.Min(member.MaxHP, member.HP + heal);
                }
            }

            // Team attacks monsters
            foreach (var member in team.Where(m => m.IsAlive && m.Location == "Dungeon"))
            {
                var target = monsters.Where(m => m.IsAlive).OrderBy(_ => random.Next()).FirstOrDefault();
                if (target == null) break;

                // Attack calculation with team coordination bonus.
                // v0.63.2 Fix A: Level-scaled damage floor. Pre-fix, naked-gear
                // NPCs (WeapPow=0) facing high-defense monsters did 1 damage
                // per swing -- with 1000+ monster HP, combats ran 40 rounds
                // without a kill and "completed" with 0 XP. Floor at Lv*3 so
                // a Lv 50 NPC does at least 150 per hit regardless of gear.
                // The additive formula still applies when gear+stats outclass
                // the floor (high-Str well-geared NPCs hit harder).
                long damage = Math.Max(member.Level * 6, member.Strength + member.WeapPow - target.Defence);
                damage += random.Next(1, (int)Math.Max(2, member.WeapPow / 3));
                damage = (long)(damage * 1.1); // 10% team coordination bonus

                target.HP -= damage;

                if (!target.IsAlive)
                {
                    totalExp += target.GetExperienceReward();
                    totalGold += target.GetGoldReward();
                }
            }

            // Monsters attack team (only members still in dungeon)
            foreach (var monster in monsters.Where(m => m.IsAlive))
            {
                var target = team.Where(m => m.IsAlive && m.Location == "Dungeon").OrderBy(_ => random.Next()).FirstOrDefault();
                if (target == null) break;

                // Monster attack - slightly reduced against teams (they help each other).
                // v0.63.2: also halved baseline to reflect that world-sim NPCs
                // lack player-grade defensive tools (no potion-quaffing, no
                // mid-fight class abilities, no dodge mechanic). Without this,
                // combat was a flat 1:1 damage trade and NPCs traded lives
                // with monsters at a ~0% win rate.
                long damage = Math.Max(1, monster.Strength + monster.WeapPow - target.Defence - target.ArmPow);
                damage += random.Next(1, (int)Math.Max(2, monster.WeapPow / 3));
                damage = (long)(damage * 0.50); // sim NPCs take half monster damage
                damage = (long)(damage * 0.85); // additional 15% from team support

                // v0.61.3 Warrior sim damage reduction (team path). Stacks
                // multiplicatively with the existing 15% team-support reduction.
                // Net incoming for a Warrior in a team = 0.85 * 0.85 = 0.7225x
                // (~28% reduction), reflecting both team coordination AND the
                // shield/heavy-armor archetype.
                if (target.Class == CharacterClass.Warrior)
                    damage = (long)(damage * 0.85);

                target.TakeDamage(damage);
            }
        }

        return team.Any(m => m.IsAlive) && !monsters.Any(m => m.IsAlive);
    }

    /// <summary>
    /// v0.61.2 Phase 1 NPC AI telemetry: wraps a dispatcher action call with
    /// before/after state capture, derives a simple outcome string from the
    /// state delta, and logs a row to npc_decision_log. Used for every action
    /// type except "dungeon" (NPCExploreDungeon has its own internal logging
    /// with richer outcome strings like "won" / "fled" / "stalemate" that the
    /// generic delta-based classifier can't produce). One row per NPC action
    /// per tick, enough resolution to compute cohort baselines (survival rate,
    /// gold accumulation, action distribution) for AI-vs-heuristic comparison
    /// once the AI subset lands.
    /// </summary>
    private void TelemetryWrap(NPC npc, string action, Action runAction)
    {
        string locationBefore = npc.CurrentLocation;
        long hpBefore = npc.HP;
        long goldBefore = npc.Gold;
        long xpBefore = npc.Experience;
        long levelBefore = npc.Level;

        try
        {
            runAction();
        }
        catch (Exception ex)
        {
            // Never let a telemetry wrapper swallow exceptions; rethrow so the
            // world-sim catch in SimulateTick handles it the same way it would
            // without the wrapper. But log the action so we can correlate.
            DebugLogger.Instance.LogError("WORLDSIM", $"NPC action '{action}' threw for {npc.Name2 ?? npc.Name1}: {ex.Message}");
            // Still try to log the failed-action row before rethrowing.
            try { SqlBackend?.LogNPCDecision(
                npc.Name2 ?? npc.Name1 ?? "(unknown)",
                (int)levelBefore,
                npc.Class.ToString(),
                action,
                locationBefore,
                npc.CurrentLocation,
                "exception",
                npc.Gold - goldBefore,
                npc.Experience - xpBefore,
                hpBefore,
                npc.HP,
                npc.IsAIDriven); } catch { }
            throw;
        }

        // Derive outcome from state delta. Priority order: died > leveled_up >
        // took_damage > healed > earned > spent > completed. Single classifier
        // per row so SQL rollups stay simple. NPCs that aborted an action via
        // an internal early-return get an "abort_*" outcome from the action
        // method itself if it surfaces it; otherwise the generic classifier
        // calls them "completed" since the dispatcher can't see the difference.
        string outcome;
        if (!npc.IsAlive)
            outcome = "died";
        else if (npc.Level > levelBefore)
            outcome = "leveled_up";
        else if (npc.HP < hpBefore)
            outcome = "took_damage";
        else if (npc.HP > hpBefore)
            outcome = "healed";
        else if (npc.Gold > goldBefore)
            outcome = "earned";
        else if (npc.Gold < goldBefore)
            outcome = "spent";
        else
            outcome = "completed";

        SqlBackend?.LogNPCDecision(
            npc.Name2 ?? npc.Name1 ?? "(unknown)",
            (int)levelBefore,
            npc.Class.ToString(),
            action,
            locationBefore,
            npc.CurrentLocation,
            outcome,
            npc.Gold - goldBefore,
            npc.Experience - xpBefore,
            hpBefore,
            npc.HP,
            npc.IsAIDriven);
    }

    /// <summary>
    /// NPC explores the dungeon and fights monsters
    /// </summary>
    private void NPCExploreDungeon(NPC npc)
    {
        // Don't send NPCs on dungeon runs if they're engaged with a player
        if (npc.IsInConversation) return;

        // v0.61.2 Phase 1 NPC AI telemetry: capture before-state once at entry
        // so every outcome path can log a (before, after, outcome) row to
        // npc_decision_log. Local helper centralises the write. is_ai_driven
        // is forwarded from npc.IsAIDriven (v0.64.0 Brain v2 Slice 1) so dungeon
        // outcomes by Brain-driven NPCs land in the AI-cohort telemetry bucket.
        string locationBefore = npc.CurrentLocation;
        long hpBefore = npc.HP;
        long goldBefore = npc.Gold;
        long xpBefore = npc.Experience;
        void LogDungeonDecision(string outcome)
        {
            SqlBackend?.LogNPCDecision(
                npc.Name2 ?? npc.Name1 ?? "(unknown)",
                (int)npc.Level,
                npc.Class.ToString(),
                "dungeon",
                locationBefore,
                npc.CurrentLocation,
                outcome,
                npc.Gold - goldBefore,
                npc.Experience - xpBefore,
                hpBefore,
                npc.HP,
                npc.IsAIDriven);
        }

        // v0.61.2 self-preservation gates. Pre-fix the world feed was full of
        // "NPC slain by Golem at the Dungeon" entries because nothing here
        // checked whether an NPC should be doing this run at all. Three gates
        // now refuse the trip when the survival math is bad. Dispatcher checks
        // npc.CurrentLocation after this returns -- if we redirected the NPC,
        // the dungeon-flavor activity message won't fire.
        var personality = npc.Brain?.Personality;

        // Gate 1: wounded NPCs go to the healer, not the dungeon. 70% HP floor
        // because a fresh fight can easily eat 30% in the first round or two.
        if (npc.HP < npc.MaxHP * 0.7)
        {
            npc.UpdateLocation("Healer");
            npc.CurrentActivity = "tending to wounds";
            LogDungeonDecision("aborted_wounded");
            return;
        }

        // Gate 2: cautious NPCs (low Courage AND low Ambition) are not
        // adventurers and shouldn't act like ones. 90% chance to abort and
        // hang around town instead. The 10% slip-through preserves the
        // occasional bad-decision-by-a-coward edge case for narrative color.
        if (personality != null && personality.Courage < 0.3f && personality.Ambition < 0.3f
            && random.Next(100) < 90)
        {
            npc.UpdateLocation("Main Street");
            npc.CurrentActivity = "lingering near the safety of the gates";
            LogDungeonDecision("aborted_cautious");
            return;
        }

        npc.UpdateLocation("Dungeon");

        // Determine dungeon level based on NPC level. Per the first-day
        // telemetry pass, the v0.61.2 baseline (npc.Level - 2 to npc.Level)
        // was still too aggressive: only ~1% of attempts won, mid-level
        // NPCs got carved through 60-80% HP and fled before completing.
        // The baseline rolls EASIER floors (npc.Level - 5 to npc.Level - 3)
        // so NPCs are comfortably above the floor's monster level and the
        // simulated combat is winnable. Courage/Ambition still push +0..1
        // each for risk-takers. Clamped at 1.
        int dungeonLevel = npc.Level - 5 + random.Next(0, 3); // npc.Level - 5 to npc.Level - 3
        if (personality != null)
        {
            if (personality.Courage > 0.7f) dungeonLevel += random.Next(0, 2);
            if (personality.Ambition > 0.7f) dungeonLevel += random.Next(0, 2);
        }
        dungeonLevel = Math.Clamp(dungeonLevel, 1, 100);

        // Generate a monster
        var monster = MonsterGenerator.GenerateMonster(dungeonLevel);
        long monsterStartHP = monster.HP; // Snapshot for partial-XP-on-flee calc below.

        // v0.64.0 Brain v2 Slice 4: Tier A NPCs route through the real-combat
        // NPCCombatSimulator (abilities, potions, real damage formulas). Tier B
        // stays on the abstract sim below. Both paths share the same outcome
        // schema (won/fled/died/stalemate) so dashboard pivots work uniformly.
        if (IsTierANPC(npc))
        {
            var simResult = NPCCombatSimulator.Simulate(npc, new List<Monster> { monster }, null, random);
            long expGain = (long)(simResult.ExpReward * NpcXpMultiplier);
            long goldGain = simResult.GoldReward;
            if (expGain > 0) npc.GainExperience(expGain);
            if (goldGain > 0) npc.GainGold(goldGain);

            // Loot drop on win (same rate as abstract sim).
            if (simResult.Outcome == NPCCombatOutcome.Won
                && random.NextDouble() < 0.35
                && npc.MarketInventory.Count < npc.MaxMarketInventory)
            {
                var loot = NPCItemGenerator.GenerateDungeonLoot(npc, dungeonLevel);
                npc.MarketInventory.Add(loot);
            }

            // Outcome telemetry uses the existing logger with richer outcome
            // strings so Brain v2 NPC combats land in the same dashboard as
            // the abstract sim (just tagged is_ai_driven=true via the existing
            // LogDungeonDecision helper above).
            string outcome = simResult.Outcome switch
            {
                NPCCombatOutcome.Won => "won",
                NPCCombatOutcome.Fled => "fled",
                NPCCombatOutcome.Died => "died",
                _ => "stalemate",
            };
            // Update location on death/flee to match abstract sim's behavior.
            if (simResult.Outcome == NPCCombatOutcome.Died)
            {
                // 5% permadeath chance from a dungeon-monster kill, matching
                // the abstract sim's typical death pressure. MarkNPCDead
                // respects player-team protection / race-floor / story NPC
                // exemptions internally.
                MarkNPCDead(npc, 0.05f, monster.Name, "the dungeon");
            }
            else
            {
                npc.UpdateLocation(simResult.Outcome == NPCCombatOutcome.Won
                    ? "Main Street" : "Healer");
            }
            LogDungeonDecision(outcome);
            return;
        }

        // Simulate combat (Tier B / abstract sim path)
        int rounds = 0;
        bool npcWon = false;

        bool fled = false;
        // v0.61.2: flee threshold lifted from 30% to 50% baseline. v0.63.2
        // retune2: dropped to 20% baseline (10% brave, 30% cowardly). At 35%
        // NPCs were still bailing on combats they were trading roughly 1:1 in.
        // 20% lets them fight through ~80% HP before fleeing, enough to claim
        // most kills with the new Lv*6 damage floor.
        float fleeThreshold = 0.20f;
        if (personality != null)
        {
            if (personality.Courage < 0.3f) fleeThreshold = 0.30f;
            else if (personality.Courage > 0.7f) fleeThreshold = 0.10f;
        }
        while (npc.IsAlive && monster.IsAlive && rounds < 50)
        {
            rounds++;

            // v0.61.3 Cleric sim self-heal. Telemetry over 12h showed Clerics
            // at 0% wins / 17.9% deaths in solo dungeon -- worst class by far.
            // Root: real combat AI casts Cure Wounds at low HP, but the sim
            // doesn't model spell-casting. Apply a 10% MaxHP heal per round
            // when below 50% HP. Mirrors the real-combat behavior at a coarse
            // grain: Clerics shouldn't die just because the sim is simpler.
            if (npc.Class == CharacterClass.Cleric && npc.HP > 0 && npc.HP < npc.MaxHP * 0.5)
            {
                long heal = npc.MaxHP / 10;
                npc.HP = Math.Min(npc.MaxHP, npc.HP + heal);
            }

            if (npc.HP < npc.MaxHP * fleeThreshold)
            {
                // Higher AGI = better flee chance; base 70%.
                int fleeChance = 70 + (int)(npc.Agility / 3);
                if (random.Next(100) < Math.Min(95, fleeChance))
                {
                    fled = true;
                    break;
                }
            }

            // NPC attacks. v0.63.2 Fix A retune2: floor at Lv*6 (was *3, then *4).
            // Telemetry showed Lv 50 NPCs fleeing at 20% HP after 10+ rounds
            // without finishing the kill -- damage trade was even but combat
            // was too slow. Lv*6 gets a Lv 50 NPC to 300/swing, killing
            // most floor-appropriate monsters in 5 rounds.
            long npcDamage = Math.Max(npc.Level * 6, npc.Strength + npc.WeapPow - monster.Defence);
            npcDamage += random.Next(1, (int)Math.Max(1, npc.WeapPow / 2));
            monster.HP -= npcDamage;

            if (!monster.IsAlive)
            {
                npcWon = true;
                break;
            }

            // Monster attacks. v0.63.2: also halved baseline (same rationale
            // as team path -- sim NPCs lack player-grade defensive tools, so
            // monster damage halved to keep the trade winnable). NPCs missing
            // ArmPow accounted for; v0.63.2 NPCs now spawn with intrinsic
            // gear power so target.Defence already does meaningful work.
            long monsterDamage = Math.Max(1, monster.Strength + monster.WeapPow - npc.Defence - npc.ArmPow);
            monsterDamage += random.Next(1, (int)Math.Max(1, monster.WeapPow / 2));
            monsterDamage = (long)(monsterDamage * 0.50);

            // v0.61.3 Warrior sim damage reduction. Telemetry showed Warriors
            // at 0% wins / 5.5% deaths -- they survive (lots of HP) but never
            // kill. Real Warriors stack BlockChance / ShieldBonus / DefenceBonus
            // from heavy armor and shields; the sim's flat
            // `monster.Strength + WeapPow - npc.Defence` formula ignores those.
            // 15% incoming-damage reduction on Warriors approximates the
            // tank-archetype shield/armor advantage that lets them sustain
            // long enough to actually finish the kill. Barbarian is similarly
            // 0-win but high-death (no defense kit) -- leaving them alone for
            // now; a Rage-style "below 30% HP get attack bonus" is the right
            // shape for them but bigger scope.
            if (npc.Class == CharacterClass.Warrior)
                monsterDamage = (long)(monsterDamage * 0.85);

            npc.TakeDamage(monsterDamage);
        }

        if (npcWon)
        {
            // NPC wins - gain XP and gold (XP throttled by NpcXpMultiplier for world sim mode)
            long expGain = (long)(monster.GetExperienceReward() * NpcXpMultiplier);
            long goldGain = monster.GetGoldReward();

            npc.GainExperience(expGain);
            npc.GainGold(goldGain);

            // 35% chance to find loot if has inventory space
            if (random.NextDouble() < 0.35 && npc.MarketInventory.Count < npc.MaxMarketInventory)
            {
                var loot = NPCItemGenerator.GenerateDungeonLoot(npc, dungeonLevel);
                npc.MarketInventory.Add(loot);
                // GD.Print($"[WorldSim] {npc.Name} found {loot.Name} in the dungeon!");
            }

            // Generate news for notable victories
            if (monster.IsBoss || monster.Level >= npc.Level + 5 || random.NextDouble() < 0.1)
            {
                string newsMsg = monster.IsBoss
                    ? $"{npc.Name} defeated the mighty {monster.Name} in the dungeon depths!"
                    : $"{npc.Name} slew a {monster.Name} (Lv{monster.Level}) and earned {goldGain} gold.";
                NewsSystem.Instance.Newsy(true, newsMsg);
            }

            // GD.Print($"[WorldSim] {npc.Name} defeated {monster.Name}, gained {expGain} XP and {goldGain} gold");

            // Return to town after the fight
            var returnLocations = new[] { "Main Street", "Inn", "Main Street", "Healer" };
            npc.UpdateLocation(npc.HP < npc.MaxHP * 0.5
                ? "Healer"  // Wounded NPCs head to the healer
                : returnLocations[random.Next(returnLocations.Length)]);
            LogDungeonDecision("won");
        }
        else if (!npc.IsAlive)
        {
            // NPC died in solo dungeon crawl — permadeath roll
            MarkNPCDead(npc, GameConfig.PermadeathChanceDungeonSolo, monster.Name, "the Dungeon");
            LogDungeonDecision("died");
        }
        else if (fled)
        {
            // Fled mid-fight — head to healer if wounded.
            // Telemetry showed NPCs fight through 60-80% HP loss before fleeing
            // (they're dealing real damage, just can't finish the kill). With
            // zero XP on flee, the progression loop stalls — NPCs almost never
            // level up. Grant partial XP when the NPC inflicted at least 30% of
            // the monster's HP, scaled to the damage actually dealt. This lets
            // valid effort pay off without turning flee into a no-risk farming
            // strategy: max 50% of the win-reward, and only if the NPC really
            // hurt the monster.
            long damageDealt = Math.Max(0, monsterStartHP - monster.HP);
            float dealtPct = monsterStartHP > 0 ? (float)damageDealt / monsterStartHP : 0f;
            if (dealtPct >= 0.30f)
            {
                float xpScale = Math.Min(0.50f, dealtPct * 0.50f); // 30% dealt -> 15% reward, 90%+ -> 45%
                long partialXP = (long)(monster.GetExperienceReward() * NpcXpMultiplier * xpScale);
                if (partialXP > 0)
                    npc.GainExperience(partialXP);
            }
            npc.UpdateLocation(npc.HP < npc.MaxHP * 0.5 ? "Healer" : "Main Street");
            LogDungeonDecision("fled");
        }
        else
        {
            // 50-round timeout (stalemate) — head to healer if wounded
            npc.UpdateLocation(npc.HP < npc.MaxHP * 0.5 ? "Healer" : "Main Street");
            LogDungeonDecision("stalemate");
        }
    }

    /// <summary>
    /// NPC goes shopping for equipment using the modern equipment system
    /// </summary>
    private void NPCGoShopping(NPC npc)
    {
        EquipmentDatabase.Initialize();

        // Initialize EquippedItems if needed
        if (npc.EquippedItems == null)
            npc.EquippedItems = new Dictionary<EquipmentSlot, int>();

        bool boughtSomething = false;
        string itemBought = "";

        // Determine how much gold the NPC is willing to spend (30-70% of their gold)
        long spendingBudget = (long)(npc.Gold * (0.3 + random.NextDouble() * 0.4));

        // Try to upgrade weapon (50% of the time)
        if (random.NextDouble() < 0.5)
        {
            npc.UpdateLocation("Weapon Shop");

            // Get current weapon power
            int currentWeaponPower = 0;
            if (npc.EquippedItems.TryGetValue(EquipmentSlot.MainHand, out int weaponId))
            {
                var currentWeapon = EquipmentDatabase.GetById(weaponId);
                if (currentWeapon != null)
                    currentWeaponPower = currentWeapon.WeaponPower;
            }

            // Find a better weapon within budget
            var betterWeapon = EquipmentDatabase.GetWeaponsByHandedness(WeaponHandedness.OneHanded)
                .Concat(EquipmentDatabase.GetWeaponsByHandedness(WeaponHandedness.TwoHanded))
                .Where(w => w.Value <= spendingBudget && w.WeaponPower > currentWeaponPower)
                .OrderByDescending(w => w.WeaponPower)
                .FirstOrDefault();

            if (betterWeapon != null)
            {
                var (_, _, weaponTotalWithTax) = CityControlSystem.CalculateTaxedPrice(betterWeapon.Value);
                if (npc.Gold < weaponTotalWithTax) { betterWeapon = null; }
            }

            if (betterWeapon != null)
            {
                var (_, _, weaponTotal) = CityControlSystem.CalculateTaxedPrice(betterWeapon.Value);
                npc.SpendGold((int)weaponTotal);
                CityControlSystem.Instance.ProcessSaleTax(betterWeapon.Value);
                npc.EquippedItems[EquipmentSlot.MainHand] = betterWeapon.Id;

                // If new weapon is two-handed, remove off-hand item
                if (betterWeapon.Handedness == WeaponHandedness.TwoHanded)
                {
                    npc.EquippedItems.Remove(EquipmentSlot.OffHand);
                }

                boughtSomething = true;
                itemBought = betterWeapon.Name;
                npc.RecalculateStats();
            }
        }
        else
        {
            // Try to upgrade armor
            npc.UpdateLocation("Armor Shop");

            // Pick a random armor slot to try to upgrade
            var armorSlots = new[]
            {
                EquipmentSlot.Body, EquipmentSlot.Head, EquipmentSlot.Hands,
                EquipmentSlot.Feet, EquipmentSlot.Legs, EquipmentSlot.Arms
            };
            var targetSlot = armorSlots[random.Next(armorSlots.Length)];

            // Get current armor in that slot
            int currentArmorAC = 0;
            if (npc.EquippedItems.TryGetValue(targetSlot, out int armorId))
            {
                var currentArmor = EquipmentDatabase.GetById(armorId);
                if (currentArmor != null)
                    currentArmorAC = currentArmor.ArmorClass;
            }

            // Find better armor for that slot within budget
            var betterArmor = EquipmentDatabase.GetBySlot(targetSlot)
                .Where(a => a.Value <= spendingBudget && a.ArmorClass > currentArmorAC)
                .OrderByDescending(a => a.ArmorClass)
                .FirstOrDefault();

            if (betterArmor != null)
            {
                var (_, _, armorTotalWithTax) = CityControlSystem.CalculateTaxedPrice(betterArmor.Value);
                if (npc.Gold < armorTotalWithTax) betterArmor = null;
            }

            if (betterArmor != null)
            {
                var (_, _, armorTotal) = CityControlSystem.CalculateTaxedPrice(betterArmor.Value);
                npc.SpendGold((int)armorTotal);
                CityControlSystem.Instance.ProcessSaleTax(betterArmor.Value);
                npc.EquippedItems[targetSlot] = betterArmor.Id;
                boughtSomething = true;
                itemBought = betterArmor.Name;
                npc.RecalculateStats();
            }
        }

        if (boughtSomething && random.NextDouble() < 0.15)
        {
            NewsSystem.Instance.Newsy(true, $"{npc.Name} purchased {itemBought} from the shop.");
        }

        // GD.Print($"[WorldSim] {npc.Name} went shopping" + (boughtSomething ? $" and bought {itemBought}" : " but couldn't afford anything"));
    }

    /// <summary>
    /// NPC trains to improve stats
    /// </summary>
    private void NPCTrainAtGym(NPC npc)
    {
        npc.UpdateLocation("Gym");

        int trainingCost = npc.Level * 10 + 50;
        var (_, _, trainingTotalWithTax) = CityControlSystem.CalculateTaxedPrice(trainingCost);
        if (npc.Gold >= trainingTotalWithTax)
        {
            npc.SpendGold((int)trainingTotalWithTax);
            CityControlSystem.Instance.ProcessSaleTax(trainingCost);

            // Random stat increase - update BOTH current and base stats
            int statChoice = random.Next(4);
            switch (statChoice)
            {
                case 0: npc.BaseStrength++; break;
                case 1: npc.BaseDefence++; break;
                case 2: npc.BaseAgility++; break;
                case 3: npc.BaseMaxHP += 5; break;
            }

            // Recalculate all stats from base values
            npc.RecalculateStats();

            // Restore HP if vitality was trained
            if (statChoice == 3)
            {
                npc.HP = Math.Min(npc.HP + 5, npc.MaxHP);
            }

            // GD.Print($"[WorldSim] {npc.Name} trained at the Gym and gained {statName}");

            // Chance to also improve a skill proficiency
            if (random.NextDouble() < GameConfig.NPCGymProficiencyChance)
            {
                string? skillId = GetRandomNPCTrainingSkill(npc);
                if (skillId != null)
                {
                    TrainingSystem.TryImproveFromUse(npc, skillId, random, GameConfig.NPCProficiencyCap);
                }
            }

            // Occasionally newsworthy
            if (random.NextDouble() < 0.05)
            {
                NewsSystem.Instance.Newsy(true, $"{npc.Name} has been training hard at the Gym!");
            }
        }
    }

    /// <summary>
    /// Pick a random skill for an NPC to train at the Gym.
    /// 50% basic_attack, otherwise a random class ability.
    /// </summary>
    private string? GetRandomNPCTrainingSkill(NPC npc)
    {
        if (random.NextDouble() < 0.5)
            return "basic_attack";

        // Try to pick a class ability the NPC has access to
        try
        {
            var abilities = ClassAbilitySystem.GetAvailableAbilities(npc);
            if (abilities != null && abilities.Count > 0)
            {
                var ability = abilities[random.Next(abilities.Count)];
                return ability.Id;
            }
        }
        catch { /* ClassAbilitySystem not available */ }

        return "basic_attack"; // Fallback
    }

    /// <summary>
    /// NPC visits their master to level up
    /// </summary>
    private void NPCVisitMaster(NPC npc)
    {
        npc.UpdateLocation("Level Master");
        long expNeeded = GameConfig.GetExperienceForLevel(npc.Level + 1);
        if (npc.Experience >= expNeeded && npc.Level < 100)
        {
            npc.Level++;

            // Update base stats on level up (before equipment bonuses)
            npc.BaseMaxHP += 10 + random.Next(5, 15);
            npc.BaseStrength += random.Next(1, 3);
            npc.BaseDefence += random.Next(1, 2);

            // v0.63.2 Fix B: NPCs nudge their gear up on level-up so they keep
            // pace with the dungeons they're meant to clear. Bump the Base
            // values so the gear power survives the next RecalculateStats()
            // call; live WeapPow/ArmPow also bumped for immediate effect.
            npc.BaseWeapPow += 5;
            npc.BaseArmPow += 4;
            npc.WeapPow += 5;
            npc.ArmPow += 4;

            // v0.63.2: grant a class-keyed mana bump on level-up so casters
            // don't stagnate at their initial spawn-time pool. Pre-v0.63.2
            // NPCVisitMaster only incremented HP/STR/DEF; casters who leveled
            // up gained no extra mana, eroding spell viability at high level.
            // Bands roughly match the spawn formula's per-level scaling.
            long manaGain = npc.Class switch
            {
                CharacterClass.Magician or CharacterClass.Sage => 18 + random.Next(0, 8),
                CharacterClass.Cleric or CharacterClass.Paladin => 15 + random.Next(0, 6),
                CharacterClass.Bard => 13 + random.Next(0, 6),
                CharacterClass.MysticShaman => 16 + random.Next(0, 6),
                CharacterClass.Tidesworn or CharacterClass.Wavecaller or CharacterClass.Cyclebreaker
                    or CharacterClass.Abysswarden or CharacterClass.Voidreaver => 16 + random.Next(0, 6),
                _ => 0
            };
            if (manaGain > 0) npc.BaseMaxMana += manaGain;

            // Recalculate all stats with equipment bonuses
            npc.RecalculateStats();

            // Restore HP to full on level up
            npc.HP = npc.MaxHP;

            // This is always newsworthy!
            NewsSystem.Instance?.WriteNPCLevelUpNews(npc.Name, npc.Level, npc.Class.ToString(), npc.Race.ToString());

            // v0.63.0 slice 3b D2: family reflection. If this NPC was raised by a
            // player AND has just crossed a public-milestone level (10 / 20 / 30 /
            // 50), spawn a separate news entry that ties their accomplishment back
            // to the player as parent. Same shape as a regular news post but with
            // alignment-keyed flavor so a virtuous grown child earns the player
            // reputation while an evil one earns the player notoriety. Gated to
            // WasRaisedByPlayer (lineage flag set at graduation) so non-player
            // NPCs don't spam the feed.
            if (npc.WasRaisedByPlayer && IsReflectionMilestone(npc.Level))
            {
                EmitFamilyReflectionNews(npc);
            }
        }
    }

    /// <summary>
    /// v0.63.0 slice 3b D2: which NPC levels trigger a "reflects on the parent"
    /// news entry. Sparse so the feed doesn't get spammed by every adult-child
    /// level-up. Lv 10 (no longer a novice), 20 (slice 3 D5 arc-completion level),
    /// 30 (designer-doc example), 50 (career mark), 75 (legend tier).
    /// </summary>
    private static bool IsReflectionMilestone(int level)
    {
        return level == 10 || level == 20 || level == 30 || level == 50 || level == 75;
    }

    /// <summary>
    /// v0.63.0 slice 3b D2: localized news entry tying an adult NPC's milestone
    /// level-up to their parent name. Tone splits on SoulAtGraduation (the
    /// snapshot taken at coming-of-age, immune to later drift) so a virtuous
    /// child earns "rumors of their kindness reach the parent" flavor and an
    /// evil child earns "rumors of their cruelty" flavor.
    /// </summary>
    private void EmitFamilyReflectionNews(NPC adultChild)
    {
        try
        {
            string parentName = !string.IsNullOrEmpty(adultChild.MotherName)
                ? adultChild.MotherName!
                : (adultChild.FatherName ?? "");
            if (string.IsNullOrEmpty(parentName)) return;

            string toneSlot;
            int soul = adultChild.SoulAtGraduation;
            if (soul > 100) toneSlot = "virtuous";
            else if (soul < -100) toneSlot = "evil";
            else toneSlot = "neutral";

            string headline = UsurperRemake.Systems.Loc.Get(
                $"family.reflection_news_{toneSlot}",
                adultChild.Name2 ?? adultChild.Name1 ?? "their child",
                parentName,
                adultChild.Level);
            NewsSystem.Instance?.Newsy(true, headline);
        }
        catch (Exception ex)
        {
            UsurperRemake.Systems.DebugLogger.Instance?.LogWarning("FAMILY",
                $"EmitFamilyReflectionNews failed: {ex.Message}");
        }
    }

    /// <summary>
    /// NPC visits the healer
    /// </summary>
    private void NPCVisitHealer(NPC npc)
    {
        npc.UpdateLocation("Healer");

        // v0.63.2 Fix D: NPCs heal to full for a nominal cost rather than the
        // player-grade `(MaxHP - HP) * 2` rate. Pre-fix-D a Lv 50 NPC at half
        // HP paid 1,500g + tax to heal -- equivalent to several days of
        // gambling wins -- which combined with the inn drain pushed NPCs
        // toward poverty. Now heal cost is 1g per 10 HP missing (Lv 50 NPC
        // at half HP pays ~75g), capped at a quarter of carried gold so a
        // poor NPC still gets healed. King's tax is bypassed for NPC heals
        // since the cost is already symbolic. Always heals to full.
        if (npc.HP >= npc.MaxHP) return;

        long missing = npc.MaxHP - npc.HP;
        long healCost = Math.Max(1, missing / 10);
        long affordable = Math.Min(healCost, npc.Gold / 4);
        if (affordable > 0) npc.SpendGold(affordable);
        npc.HP = npc.MaxHP;
    }

    // v0.64.1 Brain v2 Slice 15: NPC bounty questing. Players visit Quest
    // Hall to claim bounties from the board (kill X monsters, clear Y
    // floors); NPCs never did. This is the minimal "narrative theater"
    // version: the NPC reads the available board, picks a level-appropriate
    // bounty, rolls success based on level matching, and applies the
    // reward + news on success. Does NOT claim or remove the quest from
    // questDatabase -- players still see the same bounty on the board.
    //
    // Future slice could promote this to real competition (NPC sets Occupier,
    // quest disappears from player board, takes N ticks to complete) but
    // that needs careful interaction with QuestSystem.ClaimQuest's
    // Player-typed signature. Read-only is the safe MVP.
    private void NPCTakeBountyQuest(NPC npc)
    {
        npc.UpdateLocation("Quest Hall");

        List<Quest> available;
        try
        {
            available = QuestSystem.GetBountyBoardQuests(npc) ?? new List<Quest>();
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("WORLDSIM",
                $"NPCTakeBountyQuest GetBountyBoardQuests failed for {npc.Name2 ?? npc.Name}: {ex.Message}");
            available = new List<Quest>();
        }

        if (available.Count == 0)
        {
            // Empty board -- NPC walks in, sees nothing in their level band,
            // walks back out. Mild Sadness so the personality state reads
            // "tried, found nothing" rather than aimless.
            npc.CurrentActivity = "scanning a sparse bounty board";
            npc.EmotionalState?.AddEmotion(EmotionType.Sadness, 0.2f, 30);
            return;
        }

        // Pick a level-appropriate quest -- prefer one closest to NPC level
        // so the success roll is meaningful (skip the trivial cleanup quests
        // and the absurdly over-leveled ones the NPC would always fail).
        var pick = available
            .OrderBy(q => Math.Abs(q.MinLevel - (int)npc.Level))
            .First();

        // Success probability: base 55%, +4% per level the NPC has over the
        // quest's MinLevel (cap +35), -3% per level the quest is over the
        // NPC. Clamped to [0.15, 0.92] so even a flawless match has a 8%
        // surprise failure (board-rumor wasn't what it seemed) and even an
        // outclassed NPC has a slim chance to come back winning.
        int levelDelta = (int)npc.Level - pick.MinLevel;
        double successChance;
        if (levelDelta >= 0)
            successChance = 0.55 + Math.Min(0.35, levelDelta * 0.04);
        else
            successChance = 0.55 + (levelDelta * 0.03);
        successChance = Math.Clamp(successChance, 0.15, 0.92);

        bool succeeded = random.NextDouble() < successChance;
        string questDesc;
        try
        {
            questDesc = pick.GetTargetDescription();
            if (string.IsNullOrWhiteSpace(questDesc)) questDesc = "a bounty";
        }
        catch
        {
            questDesc = "a bounty";
        }

        if (succeeded)
        {
            // Reward scales on the QUEST's intended level (so NPCs hunting
            // big bounties get paid like big bounties).
            long goldReward = 0;
            long xpReward = 0;
            try
            {
                if (pick.RewardType == QuestRewardType.Money)
                    goldReward = pick.CalculateReward(pick.MinLevel);
                else if (pick.RewardType == QuestRewardType.Experience)
                    xpReward = pick.CalculateReward(pick.MinLevel);
                // Direct gold bounty (king-style) bypasses the byte Reward.
                if (pick.BountyGold > 0) goldReward += pick.BountyGold;
            }
            catch { /* malformed quest data, fall through with zero rewards */ }

            // Floor rewards so even a malformed quest pays something for the
            // successful hunt -- otherwise news fires "claimed bounty" for 0g.
            if (goldReward <= 0 && xpReward <= 0)
            {
                goldReward = pick.MinLevel * 100;
            }

            if (goldReward > 0) npc.Gold += goldReward;
            if (xpReward > 0) npc.GainExperience(xpReward);

            npc.CurrentActivity = $"collecting the bounty on {questDesc}";
            npc.EmotionalState?.AddEmotion(EmotionType.Pride, 0.5f, 120);
            npc.EmotionalState?.AddEmotion(EmotionType.Joy, 0.4f, 90);

            try
            {
                NewsSystem.Instance?.Newsy(true,
                    $"{npc.Name2 ?? npc.Name} returned victorious from a hunt for {questDesc}!");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("WORLDSIM",
                    $"NPCTakeBountyQuest news write failed: {ex.Message}");
            }
        }
        else
        {
            // Failed: no penalty, just a beat of disappointment. Could be
            // expanded later (lose face / NPC pays the entry fee) but for
            // MVP a no-cost retry next tick keeps the verb low-friction.
            npc.CurrentActivity = "returning empty-handed from the hunt";
            npc.EmotionalState?.AddEmotion(EmotionType.Sadness, 0.3f, 60);
            npc.EmotionalState?.AddEmotion(EmotionType.Anger, 0.2f, 30);
        }
    }

    // Slice 1's DispatchBrainAction (translator from Brain's narrow ActionType
    // vocabulary to picker verbs) was removed in Slice 2 -- the scorer consumes
    // the picker's full 17-verb candidate set directly, so the translator is
    // dead code. Git history (v0.64.0 Slice 1 commit) has the implementation if
    // it's ever needed again.

    private void ExecuteNPCAction(NPC npc, NPCAction action, WorldState world)
    {
        switch (action.Type)
        {
            case ActionType.Idle:
                // NPC does nothing this turn
                break;

            case ActionType.Explore:
                MoveNPCToRandomLocation(npc);
                break;

            case ActionType.Trade:
                ExecuteTrade(npc, action.Target, world);
                break;

            case ActionType.Socialize:
                ExecuteSocialize(npc, action.Target, world);
                break;

            case ActionType.Attack:
                ExecuteAttack(npc, action.Target, world);
                break;

            case ActionType.Rest:
                ExecuteRest(npc);
                break;
                
            case ActionType.Train:
                ExecuteTrain(npc);
                break;
                
            case ActionType.JoinGang:
                ExecuteJoinGang(npc, action.Target, world);
                break;
                
            case ActionType.SeekRevenge:
                ExecuteSeekRevenge(npc, action.Target, world);
                break;
        }
    }
    
    /// <summary>
    /// Move NPC to a location, biased by relationships. Friends attract, enemies repel.
    /// Falls back to pure random if NPC has no meaningful relationships.
    /// </summary>
    private void MoveNPCToRandomLocation(NPC npc)
    {
        // Settlement residents stay in the settlement — don't move them to town
        if (SettlementSystem.Instance?.State.SettlerNames.Contains(npc.Name) == true)
        {
            npc.CurrentLocation = "Settlement";
            return;
        }

        // Build weighted location list — base weight 1.0 for all locations
        var locationWeights = new List<(string location, double weight)>();
        foreach (var loc in GameLocations)
            locationWeights.Add((loc, 1.0));

        // Adjust weights based on where friends/enemies are
        foreach (var other in npcs)
        {
            if (other == npc || !other.IsAlive || other.IsDead) continue;
            if (string.IsNullOrEmpty(other.CurrentLocation)) continue;

            int rel = RelationshipSystem.GetRelationshipLevel(npc, other);

            for (int i = 0; i < locationWeights.Count; i++)
            {
                if (locationWeights[i].location != other.CurrentLocation) continue;

                if (rel <= GameConfig.RelationFriendship) // friend or better
                {
                    // Closer relationship = stronger pull (married +0.9, friend +0.3)
                    double friendBoost = 0.3 + (GameConfig.RelationFriendship - rel) / 50.0;
                    locationWeights[i] = (locationWeights[i].location,
                        locationWeights[i].weight + friendBoost);
                }
                else if (rel >= GameConfig.RelationEnemy) // enemy or worse
                {
                    double enemyPenalty = (rel - GameConfig.RelationEnemy) / 20.0;
                    double newWeight = locationWeights[i].weight * (0.5 - enemyPenalty * 0.2);
                    locationWeights[i] = (locationWeights[i].location,
                        Math.Max(0.05, newWeight));
                }
                break;
            }
        }

        // Conway density pressure: penalize overcrowded destinations, favor small groups
        var locationDensity = new Dictionary<string, int>();
        foreach (var other in npcs)
        {
            if (other == npc || !other.IsAlive || other.IsDead) continue;
            if (string.IsNullOrEmpty(other.CurrentLocation)) continue;
            if (!locationDensity.ContainsKey(other.CurrentLocation))
                locationDensity[other.CurrentLocation] = 0;
            locationDensity[other.CurrentLocation]++;
        }
        for (int i = 0; i < locationWeights.Count; i++)
        {
            locationDensity.TryGetValue(locationWeights[i].location, out int density);
            if (density >= GameConfig.NeighborOvercrowdingMin)
                locationWeights[i] = (locationWeights[i].location, locationWeights[i].weight * 0.5);
            else if (density >= 1 && density <= 3)
                locationWeights[i] = (locationWeights[i].location, locationWeights[i].weight * 1.3);
        }

        // Zero out current location so NPC always moves somewhere new
        for (int i = 0; i < locationWeights.Count; i++)
        {
            if (locationWeights[i].location == npc.CurrentLocation)
            {
                locationWeights[i] = (locationWeights[i].location, 0.0);
                break;
            }
        }

        // Weighted random selection
        double totalWeight = locationWeights.Sum(lw => lw.weight);
        if (totalWeight <= 0)
        {
            // Fallback: pure random
            var fallback = GameLocations[random.Next(GameLocations.Length)];
            if (fallback != npc.CurrentLocation)
                npc.UpdateLocation(fallback);
            return;
        }

        double roll = random.NextDouble() * totalWeight;
        double cumulative = 0;
        string newLocation = GameLocations[0];

        foreach (var (location, weight) in locationWeights)
        {
            cumulative += weight;
            if (roll <= cumulative)
            {
                newLocation = location;
                break;
            }
        }

        if (newLocation != npc.CurrentLocation)
        {
            npc.UpdateLocation(newLocation);
        }
    }

    /// <summary>
    /// NPC spouse/lover goes home to be with the player
    /// </summary>
    private void NPCGoHome(NPC npc)
    {
        npc.UpdateLocation("Home");
        // GD.Print($"[WorldSim] {npc.Name} went home to be with their partner");

        // Small chance to generate sweet news about it
        if (random.NextDouble() < 0.15)
        {
            var romance = RomanceTracker.Instance;
            bool isSpouse = romance?.Spouses?.Any(s => s.NPCId == npc.ID) == true;

            string[] homeMessages = isSpouse ? new[]
            {
                $"{npc.Name} is spending quality time at home.",
                $"{npc.Name} prepared a warm meal at home.",
                $"{npc.Name} is waiting faithfully at home."
            } : new[]
            {
                $"{npc.Name} stopped by home for a visit.",
                $"{npc.Name} is relaxing at home.",
                $"{npc.Name} came home looking for company."
            };

            NewsSystem.Instance?.Newsy(false, homeMessages[random.Next(homeMessages.Length)]);
        }
    }

    /// <summary>
    /// NPC visits Love Street for romance or paid companionship
    /// </summary>
    private void NPCVisitLoveStreet(NPC npc)
    {
        npc.UpdateLocation("Love Street");
        // GD.Print($"[WorldSim] {npc.Name} visits Love Street");

        var personality = npc.Brain?.Personality;
        if (personality == null) return;

        // Check if NPC has a spouse - might cause jealousy drama
        var spouse = RomanceTracker.Instance?.Spouses?
            .FirstOrDefault(s => s.NPCId == npc.ID);

        // Decide what to do: socialize, seek romance, or use paid services
        float roll = (float)random.NextDouble();

        if (roll < 0.4f)
        {
            // Just socializing/looking for dates
            // GD.Print($"[WorldSim] {npc.Name} is looking for romance at Love Street");
            // Could meet other NPCs here for relationship building
        }
        else if (roll < 0.7f && npc.Gold >= 500)
        {
            // Use paid services
            int cost = random.Next(500, 5000);
            cost = Math.Min(cost, (int)npc.Gold);
            npc.SpendGold(cost);

            // Disease risk based on how cheap they went
            float diseaseChance = cost < 2000 ? 0.25f : cost < 5000 ? 0.15f : 0.05f;
            bool gotDisease = random.NextDouble() < diseaseChance;

            if (gotDisease)
            {
                // NPC contracted a disease
                npc.HP = Math.Max(1, npc.HP / 2);
                NewsSystem.Instance.Newsy(true,
                    $"{npc.Name} caught a nasty disease at Love Street!");
                // GD.Print($"[WorldSim] {npc.Name} contracted a disease at Love Street!");
            }
            else
            {
                // GD.Print($"[WorldSim] {npc.Name} spent {cost} gold at Love Street");
            }

            // Generate news occasionally
            if (random.NextDouble() < 0.3f)
            {
                NewsSystem.Instance.Newsy(true,
                    $"{npc.Name} was seen at Love Street last night.");
            }
        }
        else
        {
            // Looking for actual romance with other NPCs present
            var otherNPCs = npcs
                .Where(n => n.IsAlive && n.ID != npc.ID && n.CurrentLocation == "Love Street")
                .ToList();

            if (otherNPCs.Any())
            {
                var target = otherNPCs[random.Next(otherNPCs.Count)];
                bool isAttracted = personality.IsAttractedTo(
                    target.Sex == CharacterSex.Female ? GenderIdentity.Female : GenderIdentity.Male);

                if (isAttracted)
                {
                    // Improve relationship
                    RelationshipSystem.UpdateRelationship(npc, target, 1, 2, true);
                    // GD.Print($"[WorldSim] {npc.Name} and {target.Name} hit it off at Love Street");
                }
            }
        }
    }

    /// <summary>
    /// NPC visits the Temple to worship, sacrifice, or seek divine guidance
    /// </summary>
    private void NPCVisitTemple(NPC npc)
    {
        npc.UpdateLocation("Temple");
        // GD.Print($"[WorldSim] {npc.Name} visits the Temple");

        var personality = npc.Brain?.Personality;
        float roll = (float)random.NextDouble();

        // Decide what to do at the temple
        if (roll < 0.4f)
        {
            // Prayer and worship
            // GD.Print($"[WorldSim] {npc.Name} prays at the Temple");

            // Small chance of receiving a blessing
            if (random.NextDouble() < 0.1)
            {
                // NPC receives minor blessing
                int statBoost = random.Next(1, 3);
                switch (random.Next(3))
                {
                    case 0:
                        npc.Strength += statBoost;
                        break;
                    case 1:
                        npc.Wisdom += statBoost;
                        break;
                    case 2:
                        npc.HP = Math.Min(npc.HP + npc.MaxHP / 4, npc.MaxHP);
                        break;
                }
                NewsSystem.Instance.Newsy(false, $"{npc.Name} received a divine blessing at the Temple.");
                // GD.Print($"[WorldSim] {npc.Name} received a divine blessing!");
            }
        }
        else if (roll < 0.6f && npc.Gold >= 500)
        {
            // Gold sacrifice to gain favor
            int sacrifice = random.Next(500, Math.Min(5000, (int)npc.Gold));
            npc.SpendGold(sacrifice);

            // Boost chivalry slightly
            npc.Chivalry += sacrifice / 500;

            // GD.Print($"[WorldSim] {npc.Name} sacrificed {sacrifice} gold at the Temple");

            // Occasional news for large sacrifices
            if (sacrifice >= 2000 && random.NextDouble() < 0.3)
            {
                NewsSystem.Instance.Newsy(false, $"{npc.Name} made a generous offering at the Temple.");
            }
        }
        else if (roll < 0.75f && personality != null && personality.Aggression > 0.6f && npc.Darkness > npc.Chivalry)
        {
            // Evil NPCs might desecrate an altar
            // GD.Print($"[WorldSim] {npc.Name} desecrates an altar at the Temple!");

            // Gain darkness, risk divine retribution
            npc.Darkness += random.Next(10, 25);

            // 30% chance of divine punishment
            if (random.NextDouble() < 0.3)
            {
                int damage = random.Next(20, 50 + npc.Level);
                npc.HP = Math.Max(1, npc.HP - damage);
                NewsSystem.Instance.Newsy(true, $"{npc.Name} was struck by divine wrath for desecrating an altar!");
                // GD.Print($"[WorldSim] {npc.Name} was struck by divine wrath!");
            }
            else
            {
                NewsSystem.Instance.Newsy(true, $"{npc.Name} desecrated an altar at the Temple!");
            }
        }
        else
        {
            // Meditation and contemplation - slight HP recovery
            npc.HP = Math.Min(npc.HP + npc.MaxHP / 10, npc.MaxHP);
            // GD.Print($"[WorldSim] {npc.Name} meditates peacefully at the Temple");

            // Meet other NPCs at temple for relationship building
            var otherNPCs = npcs
                .Where(n => n.IsAlive && n.ID != npc.ID && n.CurrentLocation == "Temple")
                .ToList();

            if (otherNPCs.Any() && random.NextDouble() < 0.3)
            {
                var other = otherNPCs[random.Next(otherNPCs.Count)];
                // Spiritual bond - improve relationship
                RelationshipSystem.UpdateRelationship(npc, other, 1, 1, false);
                // GD.Print($"[WorldSim] {npc.Name} and {other.Name} share a moment of spiritual connection");
            }
        }
    }

    /// <summary>
    /// NPC visits the Bank to deposit, withdraw, or apply for guard duty
    /// </summary>
    private void NPCVisitBank(NPC npc)
    {
        npc.UpdateLocation("Bank");
        // GD.Print($"[WorldSim] {npc.Name} visits the Bank");

        var personality = npc.Brain?.Personality;
        float roll = (float)random.NextDouble();

        // Decide what to do at the bank.
        // v0.63.2 Fix F: deposit threshold dropped 1000 -> 500 and deposit
        // percent bumped to 60-90% (was 50-80%) so NPCs stash more aggressively
        // and don't keep large pools exposed to ambient drains.
        if (npc.Gold > 500 && roll < 0.6f)
        {
            double depositPercent = 0.6 + (random.NextDouble() * 0.3);
            long depositAmount = (long)(npc.Gold * depositPercent);

            npc.SpendGold(depositAmount);
            npc.BankGold += depositAmount;

            // GD.Print($"[WorldSim] {npc.Name} deposited {depositAmount} gold at the Bank");

            // Occasional news for large deposits
            if (depositAmount >= 10000 && random.NextDouble() < 0.2)
            {
                NewsSystem.Instance.Newsy(false, $"{npc.Name} made a substantial deposit at the Ironvault Bank.");
            }
        }
        else if (npc.BankGold > 0 && npc.Gold < 100 && roll < 0.7f)
        {
            // Withdraw gold when low on cash
            long withdrawAmount = Math.Min(npc.BankGold, 500 + (npc.Level * 50));
            npc.BankGold -= withdrawAmount;
            npc.GainGold(withdrawAmount);

            // GD.Print($"[WorldSim] {npc.Name} withdrew {withdrawAmount} gold from the Bank");
        }
        else if (!npc.BankGuard && npc.Level >= 5 && npc.Darkness <= 100 && roll < 0.85f)
        {
            // Apply for guard duty (if eligible)
            if (random.NextDouble() < 0.3) // 30% chance to actually apply
            {
                npc.BankGuard = true;
                npc.BankWage = 1000 + (npc.Level * 50);

                NewsSystem.Instance.Newsy(true, $"{npc.Name} has been hired as a guard at the Ironvault Bank!");
                // GD.Print($"[WorldSim] {npc.Name} became a bank guard (wage: {npc.BankWage}/day)");
            }
        }
        else if (npc.BankGuard && random.NextDouble() < 0.05f)
        {
            // Small chance to resign from guard duty
            npc.BankGuard = false;
            npc.BankWage = 0;
            // GD.Print($"[WorldSim] {npc.Name} resigned from bank guard duty");
        }
        else
        {
            // Just checking account or socializing at the bank
            // GD.Print($"[WorldSim] {npc.Name} checked their account at the Bank");

            // Might meet other NPCs at the bank
            var otherNPCs = npcs
                .Where(n => n.IsAlive && n.ID != npc.ID && n.CurrentLocation == "Bank")
                .ToList();

            if (otherNPCs.Any() && random.NextDouble() < 0.2)
            {
                var other = otherNPCs[random.Next(otherNPCs.Count)];
                // Business acquaintance - small relationship boost
                RelationshipSystem.UpdateRelationship(npc, other, 1, 0, false);
                // GD.Print($"[WorldSim] {npc.Name} and {other.Name} chatted at the Bank");
            }
        }
    }

    /// <summary>
    /// NPC visits the Marketplace to list items or browse/buy.
    /// In online mode (SqlBackend set), uses the shared auction_listings table.
    /// In single-player mode, uses the in-memory MarketplaceSystem.
    /// </summary>
    private void NPCVisitMarketplace(NPC npc)
    {
        npc.UpdateLocation("Auction House");

        if (SqlBackend != null)
        {
            // Online mode: use SQL-backed auction house
            NPCVisitMarketplaceSql(npc);
        }
        else
        {
            // Single-player: use in-memory marketplace
            if (npc.MarketInventory.Count > 0 && random.NextDouble() < 0.5)
            {
                var item = npc.MarketInventory[random.Next(npc.MarketInventory.Count)];
                MarketplaceSystem.Instance.NPCListItem(npc, item);
                npc.MarketInventory.Remove(item);
            }

            if (npc.Gold > 500 && random.NextDouble() < 0.25 && MarketplaceSystem.Instance.Listings.Count >= 5)
            {
                MarketplaceSystem.Instance.NPCBrowseAndBuy(npc);
            }
        }

        // Meet other NPCs at marketplace for relationship building
        var otherNPCs = npcs
            .Where(n => n.IsAlive && n.ID != npc.ID && n.CurrentLocation == "Auction House")
            .ToList();

        if (otherNPCs.Any() && random.NextDouble() < 0.2)
        {
            var other = otherNPCs[random.Next(otherNPCs.Count)];
            RelationshipSystem.UpdateRelationship(npc, other, 1, 0, false);
        }
    }

    /// <summary>
    /// Online mode: NPC lists items and browses/buys from the shared auction_listings table.
    /// Fire-and-forget async since SimulateStep is synchronous.
    /// </summary>
    private void NPCVisitMarketplaceSql(NPC npc)
    {
        var backend = SqlBackend!;

        // 50% chance to list an item if has inventory
        if (npc.MarketInventory.Count > 0 && random.NextDouble() < 0.5)
        {
            var item = npc.MarketInventory[random.Next(npc.MarketInventory.Count)];
            long price = MarketplaceSystem.Instance.CalculateNPCPrice(item, npc);
            string itemJson = JsonSerializer.Serialize(item);
            npc.MarketInventory.Remove(item);

            _ = Task.Run(async () =>
            {
                try
                {
                    await backend.CreateAuctionListing(npc.Name, item.Name, itemJson, price, hoursToExpire: 48);
                    NewsSystem.Instance?.Newsy(false, $"{npc.Name} put {item.Name} up for sale at the Auction House.");
                }
                catch (Exception ex)
                {
                    DebugLogger.Instance.LogError("MARKETPLACE", $"NPC list error: {ex.Message}");
                }
            });
        }

        // 25% chance to browse and potentially buy (reduced from 50% to prevent NPCs emptying the marketplace)
        if (npc.Gold > 500 && random.NextDouble() < 0.25)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var listings = await backend.GetActiveAuctionListings(20);
                    if (listings.Count == 0) return;

                    // Don't buy if marketplace is running low — keep items available for players
                    if (listings.Count < 5) return;

                    var affordable = listings
                        .Where(l => l.Price <= npc.Gold * 0.8)
                        .Where(l => !l.Seller.Equals(npc.Name, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (affordable.Count == 0) return;

                    var chosen = affordable[random.Next(affordable.Count)];

                    // Deserialize the item to check if NPC wants it
                    var item = JsonSerializer.Deserialize<Item>(chosen.ItemJson);
                    if (item == null) return;

                    var tempListing = new MarketplaceSystem.MarketListing
                    {
                        Item = item, Seller = chosen.Seller, Price = chosen.Price
                    };
                    if (!MarketplaceSystem.Instance.NPCWantsToBuy(npc, tempListing)
                        && random.NextDouble() >= 0.1) return;

                    // Atomic SQL purchase
                    bool bought = await backend.BuyAuctionListing(chosen.Id, npc.Name);
                    if (!bought) return;

                    npc.Gold -= chosen.Price;

                    // Pay the seller (works for both players and NPCs)
                    await backend.AddGoldToPlayer(chosen.Seller, chosen.Price);

                    // Equip or store the purchased item
                    MarketplaceSystem.Instance.EquipOrStoreItem(npc, item);

                    NewsSystem.Instance?.Newsy(false,
                        $"{npc.Name} bought {item.Name} from {chosen.Seller} at the Auction House.");

                    // Notify seller
                    await backend.SendMessage("Auction House", chosen.Seller, "auction",
                        $"Your {item.Name} sold to {npc.Name} for {chosen.Price:N0} gold!");
                }
                catch (Exception ex)
                {
                    DebugLogger.Instance.LogError("MARKETPLACE", $"NPC browse/buy error: {ex.Message}");
                }
            });
        }
    }

    /// <summary>
    /// NPC visits the Castle to apply for royal guard or seek audience with the king
    /// </summary>
    private void NPCVisitCastle(NPC npc)
    {
        npc.UpdateLocation("Castle");

        // Get current king
        var king = CastleLocation.GetCurrentKing();
        if (king == null || !king.IsActive)
        {
            // No king - just wander the castle grounds
            return;
        }

        // Check if NPC is already a royal guard
        if (king.Guards.Any(g => g.Name == npc.Name))
        {
            // Already a guard - just doing their duty
            return;
        }

        // Check if NPC is already a bank guard (can't serve both)
        if (npc.BankGuard)
        {
            return;
        }

        // Check eligibility: Level 5+, more chivalry than darkness, not on a team
        if (npc.Level >= 5 &&
            npc.Chivalry > npc.Darkness &&
            string.IsNullOrEmpty(npc.Team) &&
            king.Guards.Count < GameConfig.MaxRoyalGuards)
        {
            // Chance to apply for royal guard based on personality and stats
            float applyChance = 0.15f; // Base 15% chance

            // Higher chivalry = more likely to want to serve
            applyChance += Math.Min(0.20f, npc.Chivalry / 500f);

            // Higher level = more confident to apply
            if (npc.Level >= 10) applyChance += 0.10f;
            if (npc.Level >= 20) applyChance += 0.10f;

            // Personality factors
            if (npc.Brain?.Personality != null)
            {
                applyChance += npc.Brain.Personality.Aggression * 0.05f; // Warriors like guard duty
                applyChance -= npc.Brain.Personality.Greed * 0.10f; // Greedy prefer bank guard (better pay)
            }

            if (random.NextDouble() < applyChance)
            {
                // NPC applies and is accepted!
                long salary = GameConfig.BaseGuardSalary + (npc.Level * GameConfig.GuardSalaryPerGuardLevel);

                var guard = new RoyalGuard
                {
                    Name = npc.Name,
                    AI = CharacterAI.Computer,
                    Sex = npc.Sex,
                    DailySalary = salary,
                    RecruitmentDate = DateTime.Now,
                    Loyalty = 80 + random.Next(21), // 80-100 loyalty
                    IsActive = true
                };

                king.Guards.Add(guard);

                // News announcement
                NewsSystem.Instance?.Newsy(true, $"{npc.Name} has joined the Royal Guard!");

                // Chivalry boost for service
                npc.Chivalry += 5;
            }
        }
        else if (npc.Level >= 3 && random.NextDouble() < 0.10)
        {
            // Lower level NPCs might just seek audience or donate
            // Small chance to donate to royal purse
            if (npc.Gold > 500 && npc.Chivalry > 50)
            {
                long donation = Math.Min(npc.Gold / 10, 200 + npc.Level * 10);
                npc.SpendGold(donation);
                king.Treasury += donation;
                npc.Chivalry += (int)Math.Min(5, donation / 50);
            }
        }

        // Meet other NPCs at the castle
        var otherNPCs = npcs
            .Where(n => n.IsAlive && n.ID != npc.ID && n.CurrentLocation == "Castle")
            .ToList();

        if (otherNPCs.Any() && random.NextDouble() < 0.15)
        {
            var other = otherNPCs[random.Next(otherNPCs.Count)];
            RelationshipSystem.UpdateRelationship(npc, other, 1, 0, false);
        }
    }

    /// <summary>
    /// NPC visits the Dark Alley for shady dealings
    /// </summary>
    private void NPCVisitDarkAlley(NPC npc)
    {
        npc.UpdateLocation("Dark Alley");

        // Pickpocket attempt - high greed + high dex NPCs may steal.
        // v0.63.2 Fix E: bumped trigger 15% -> 25% and rewards scaled higher
        // so the alley becomes a real income source instead of pocket change.
        if (npc.Brain?.Personality != null && npc.Brain.Personality.Greed > 0.5f && random.NextDouble() < 0.25)
        {
            var victims = npcs
                .Where(n => n.IsAlive && n.ID != npc.ID && n.CurrentLocation == "Dark Alley" && n.Gold > 100)
                .ToList();

            if (victims.Any())
            {
                var victim = victims[random.Next(victims.Count)];
                long stolen = Math.Min(victim.Gold / 8, 100 + npc.Level * 10);
                if (stolen > 0)
                {
                    victim.SpendGold(stolen);
                    npc.Gold += stolen;
                    npc.Darkness += 2;
                    NewsSystem.Instance?.Newsy(false, $"{npc.Name} pickpocketed {stolen} gold from {victim.Name} in the Dark Alley!");
                    RelationshipSystem.UpdateRelationship(victim, npc, -5, 0, false);
                }
            }
        }

        // v0.63.2 Fix E: standalone "found coin pouch" income for any NPC at
        // the alley regardless of personality. Models the loose-coin discovery
        // + petty-theft-of-strangers that wasn't being represented when there
        // was no other victim NPC in the alley at the same tick. Modest level-
        // scaled payout so the alley produces baseline income even for non-
        // Greed NPCs who'd never pickpocket.
        if (random.NextDouble() < 0.15)
        {
            long found = 20 + npc.Level * 3 + random.Next(npc.Level * 2);
            npc.Gold += found;
        }

        // Fence stolen goods - sell inventory items at shady prices.
        // v0.63.2 Fix E: trigger 20% -> 30%, fence rate 33% -> 45% so the
        // payout for actually fencing something feels worth it.
        if (npc.MarketInventory.Count > 0 && random.NextDouble() < 0.30)
        {
            var item = npc.MarketInventory[random.Next(npc.MarketInventory.Count)];
            long fencePrice = Math.Max(20, (long)(item.Value * 0.45));
            npc.Gold += fencePrice;
            npc.MarketInventory.Remove(item);
            npc.Darkness += 1;
        }

        // v0.63.2: Spot-the-Mark gambling. v0.63.2 Fix E retune: WIS-keyed win
        // chance bumped to 40-75% (was 35-65%) and payout to 1.5-2.5x (was
        // 1.3-2.0x). Net expected value now positive for any NPC -- the alley
        // is a real income lane, not a slow grind down to zero.
        if (npc.Gold > 50 && random.NextDouble() < 0.30)
        {
            float greedFactor = npc.Brain?.Personality?.Greed ?? 0.5f;
            long bet = (long)(npc.Gold * (0.05 + greedFactor * 0.15));
            bet = Math.Clamp(bet, 10, 2000);

            int winChance = 40 + (int)Math.Min(35L, npc.Wisdom / 2);
            if (random.Next(100) < winChance)
            {
                long winnings = (long)(bet * (1.5 + random.NextDouble() * 1.0)); // 1.5-2.5x payout
                npc.Gold += winnings;
            }
            else
            {
                npc.SpendGold(bet);
                npc.Darkness += 1; // each loss is a small darkness tick
            }
        }

        // v0.63.2: Mugging gone wrong. 4% chance of a violent confrontation
        // that costs the NPC HP. Capped at 25% MaxHP per incident -- the sim
        // is supposed to drain NPCs occasionally, not kill them off in town.
        // Cowardly NPCs (low Courage) take less damage (they run sooner).
        if (random.NextDouble() < 0.04)
        {
            float courage = npc.Brain?.Personality?.Courage ?? 0.5f;
            int hpLoss = (int)(npc.MaxHP * (0.05 + random.NextDouble() * 0.20) * (1.2f - courage * 0.5f));
            hpLoss = Math.Clamp(hpLoss, 5, (int)(npc.MaxHP * 0.25));
            npc.HP = Math.Max(1, npc.HP - hpLoss); // floor at 1 -- this is a town shake-down, not a death
        }

        // Meet other shady NPCs
        var otherNPCs = npcs
            .Where(n => n.IsAlive && n.ID != npc.ID && n.CurrentLocation == "Dark Alley")
            .ToList();

        if (otherNPCs.Any() && random.NextDouble() < 0.20)
        {
            var other = otherNPCs[random.Next(otherNPCs.Count)];
            // Shady companionship - small friendship boost
            RelationshipSystem.UpdateRelationship(npc, other, 1, 0, false);
        }
    }

    /// <summary>
    /// NPC visits the Inn for rest, drinks, and socializing
    /// </summary>
    private void NPCVisitInn(NPC npc)
    {
        npc.UpdateLocation("Inn");

        // Heal slightly from resting at the inn
        if (npc.HP < npc.MaxHP)
        {
            int healing = (int)Math.Max(5, npc.MaxHP / 20);
            npc.HP = Math.Min(npc.HP + healing, npc.MaxHP);
        }

        // Pay for drinks. v0.63.2 Fix C: dramatically reduced from earlier
        // v0.63.2 tuning. Telemetry showed sociable NPCs were running 60-100g
        // tabs per inn visit, with inn = 19% of all actions. Over a week per
        // NPC = ~hundreds of gold drained, faster than any income source
        // could replenish. New: 5-15g base tab, max +10g Sociability scaling,
        // and only fires if NPC has > 200g (poor NPCs skip the tab rather
        // than getting deeper underwater).
        if (npc.Gold > 200)
        {
            float socialFactor = npc.Brain?.Personality?.Sociability ?? 0.5f;
            long drinkCost = (long)(5 + random.Next(10) + socialFactor * 10);
            drinkCost = Math.Min(drinkCost, npc.Gold / 20); // cap at 5% of gold
            npc.SpendGold(drinkCost);
        }

        // v0.63.2 Fix C: Drinking too much. 4% chance (was 6%). HP penalty
        // (alcohol poisoning), small extra gold loss. Pre-fix-C the extraTab
        // could hit 200g per event which was inflating the gold drain. Now
        // capped at 30g max and only fires if NPC has > 100g.
        if (npc.Gold > 100 && random.NextDouble() < 0.04)
        {
            int hpLoss = (int)(npc.MaxHP * (0.03 + random.NextDouble() * 0.12));
            npc.HP = Math.Max(1, npc.HP - hpLoss);
            long extraTab = Math.Min(npc.Gold / 20, 10 + random.Next(20));
            npc.SpendGold(extraTab);
        }

        // Socialize - meet other NPCs at the inn
        var otherNPCs = npcs
            .Where(n => n.IsAlive && n.ID != npc.ID && n.CurrentLocation == "Inn")
            .ToList();

        if (otherNPCs.Any() && random.NextDouble() < 0.25)
        {
            var other = otherNPCs[random.Next(otherNPCs.Count)];
            RelationshipSystem.UpdateRelationship(npc, other, 2, 0, false);

            // v0.63.2: Inn brawl. 5% chance a social interaction at the Inn
            // turns into a fistfight. Both NPCs take HP damage; the one with
            // lower Sociability and higher Aggression loses more. Capped at
            // 18% MaxHP so it's a black eye, not a coffin. Generates real
            // outcome variance in inn telemetry (previously: 0 HP changes,
            // 0 gold changes, 0 deaths -- pure null).
            if (random.NextDouble() < 0.05)
            {
                float npcAggro = npc.Brain?.Personality?.Aggression ?? 0.5f;
                float otherAggro = other.Brain?.Personality?.Aggression ?? 0.5f;
                int npcLoss = (int)(npc.MaxHP * (0.04 + random.NextDouble() * 0.14) * (0.5f + otherAggro));
                int otherLoss = (int)(other.MaxHP * (0.04 + random.NextDouble() * 0.14) * (0.5f + npcAggro));
                npc.HP = Math.Max(1, npc.HP - Math.Clamp(npcLoss, 5, (int)(npc.MaxHP * 0.18)));
                other.HP = Math.Max(1, other.HP - Math.Clamp(otherLoss, 5, (int)(other.MaxHP * 0.18)));
                RelationshipSystem.UpdateRelationship(npc, other, -3, 0, false);
                NewsSystem.Instance?.Newsy(false, $"{npc.Name} and {other.Name} traded blows at the Inn before the keeper threw them out.");
            }
            // Gossip at the inn (small chance)
            else if (random.NextDouble() < 0.10)
            {
                NewsSystem.Instance?.Newsy(false, $"{npc.Name} and {other.Name} shared drinks and gossip at the Inn.");
            }
        }
    }

    private void ExecuteTrade(NPC npc, string targetId, WorldState world)
    {
        if (string.IsNullOrEmpty(targetId)) return;
        
        var target = world.GetNPCById(targetId);
        if (target == null || target.CurrentLocation != npc.CurrentLocation) return;
        
        // Simple trade simulation
        var tradeAmount = Random.Shared.Next(10, 101);
        if (npc.CanAfford(tradeAmount) && target.CanAfford(tradeAmount))
        {
            // Exchange some gold (simplified)
            npc.SpendGold(tradeAmount / 2);
            target.GainGold(tradeAmount / 2);
            
            // Record the interaction
            npc.Brain?.RecordInteraction(target, InteractionType.Traded);
            target.Brain?.RecordInteraction(npc, InteractionType.Traded);
            
            // GD.Print($"[WorldSim] {npc.Name} traded with {target.Name}");
        }
    }
    
    private void ExecuteSocialize(NPC npc, string targetId, WorldState world)
    {
        if (string.IsNullOrEmpty(targetId)) return;
        
        var target = world.GetNPCById(targetId);
        if (target == null || target.CurrentLocation != npc.CurrentLocation) return;
        
        // Check compatibility for relationship building
        var compatibility = npc.Brain.Personality.GetCompatibility(target.Brain?.Personality);
        
        if (compatibility > 0.6f)
        {
            // Positive interaction
            npc.Brain?.RecordInteraction(target, InteractionType.Complimented);
            target.Brain?.RecordInteraction(npc, InteractionType.Complimented);
            
            // Chance to become friends or allies
            if ((float)Random.Shared.NextDouble() < compatibility * 0.5f)
            {
                npc.AddRelationship(target.Id, 0);
                target.AddRelationship(npc.Id, 0);
                // GD.Print($"[WorldSim] {npc.Name} and {target.Name} became friends");
            }
        }
        else if (compatibility < 0.3f)
        {
            // Negative interaction
            npc.Brain?.RecordInteraction(target, InteractionType.Insulted);
            target.Brain?.RecordInteraction(npc, InteractionType.Insulted);
            
            // GD.Print($"[WorldSim] {npc.Name} had a negative interaction with {target.Name}");
        }
    }
    
    private void ExecuteAttack(NPC npc, string targetId, WorldState world)
    {
        if (string.IsNullOrEmpty(targetId)) return;
        if (targetId == npc.Id) return; // Guard against self-attack

        var target = world.GetNPCById(targetId);
        if (target == null || target.CurrentLocation != npc.CurrentLocation || !target.IsAlive) return;

        // Don't attack NPCs engaged with a player (in conversation, dungeon party, etc.)
        if (target.IsInConversation || npc.IsInConversation) return;

        // Don't attack NPCs on a player's team, and don't let player team NPCs start brawls
        if (IsPlayerTeam(target.Team) || IsPlayerTeam(npc.Team)) return;

        // Multi-round combat simulation (like a real fight, not a single punch)
        int rounds = 0;
        const int maxRounds = 30;
        long totalDamageToTarget = 0;
        long totalDamageToAttacker = 0;

        while (npc.IsAlive && target.IsAlive && rounds < maxRounds)
        {
            rounds++;

            // Attacker strikes. v0.63.2 Fix A: Lv-scaled damage floor so naked
            // NPCs don't get stuck doing 1 damage per round in NPC-vs-NPC fights.
            long attackDamage = Math.Max(npc.Level * 3, npc.Strength + npc.WeapPow - target.Defence);
            attackDamage += random.Next(1, (int)Math.Max(2, npc.WeapPow / 3));
            target.TakeDamage(attackDamage);
            totalDamageToTarget += attackDamage;

            if (!target.IsAlive) break;

            // Defender strikes back
            long defenderDamage = Math.Max(target.Level * 3, target.Strength + target.WeapPow - npc.Defence);
            defenderDamage += random.Next(1, (int)Math.Max(2, target.WeapPow / 3));
            npc.TakeDamage(defenderDamage);
            totalDamageToAttacker += defenderDamage;
        }

        // Record the attack
        npc.Brain?.RecordInteraction(target, InteractionType.Attacked);
        target.Brain?.RecordInteraction(npc, InteractionType.Attacked);

        // Update relationships - make them enemies
        if (!npc.Enemies.Contains(target.Id))
            npc.Enemies.Add(target.Id);
        if (!target.Enemies.Contains(npc.Id))
            target.Enemies.Add(npc.Id);

        if (!target.IsAlive)
        {
            target.SetState(NPCState.Dead);
            MarkNPCDead(target, GameConfig.PermadeathChanceNPCvsNPC, npc.Name, npc.CurrentLocation ?? "unknown");
            npc.Brain?.Memory?.RecordEvent(new MemoryEvent
            {
                Type = MemoryType.SawDeath,
                InvolvedCharacter = target.Name2 ?? target.Name,
                Description = $"Killed {target.Name} in combat",
                Importance = 0.9f,
                Location = npc.CurrentLocation
            });

            // Victor gains gold from the fallen
            long stolenGold = Math.Max(0, target.Gold / 4);
            if (stolenGold > 0)
            {
                npc.GainGold(stolenGold);
                target.Gold -= stolenGold;
            }

        }
        else if (!npc.IsAlive)
        {
            // Attacker died instead!
            npc.SetState(NPCState.Dead);
            MarkNPCDead(npc, GameConfig.PermadeathChanceNPCvsNPC, target.Name, target.CurrentLocation ?? "unknown");
            target.Brain?.Memory?.RecordEvent(new MemoryEvent
            {
                Type = MemoryType.SawDeath,
                InvolvedCharacter = npc.Name2 ?? npc.Name,
                Description = $"Killed {npc.Name} in self-defense",
                Importance = 0.9f,
                Location = target.CurrentLocation
            });

        }
    }
    
    private void ExecuteRest(NPC npc)
    {
        var healAmount = npc.MaxHP / 4;
        npc.Heal(healAmount);
        npc.ChangeActivity(Activity.Resting, "Recovering health");
        
        // GD.Print($"[WorldSim] {npc.Name} rested and healed {healAmount} HP");
    }
    
    private void ExecuteTrain(NPC npc)
    {
        // Small chance to gain experience from training
        if ((float)Random.Shared.NextDouble() < 0.3f)
        {
            var expGain = (long)(Random.Shared.Next(10, 31) * NpcXpMultiplier);
            npc.GainExperience(expGain);
            // GD.Print($"[WorldSim] {npc.Name} trained and gained {expGain} experience");
        }
        
        npc.ChangeActivity(Activity.Working, "Training and improving skills");
    }
    
    private void ExecuteJoinGang(NPC npc, string targetId, WorldState world)
    {
        if (string.IsNullOrEmpty(targetId) || npc.GangId != null) return;
        
        var gangLeader = world.GetNPCById(targetId);
        if (gangLeader == null || gangLeader.CurrentLocation != npc.CurrentLocation) return;
        
        // Check if gang leader accepts the NPC
        var compatibility = npc.Brain.Personality.GetCompatibility(gangLeader.Brain?.Personality);
        
        if (compatibility > 0.5f && gangLeader.GangMembers.Count < 6) // Max gang size
        {
            npc.GangId = gangLeader.Id;
            gangLeader.GangMembers.Add(npc.Id);
            
            npc.AddRelationship(gangLeader.Id, 0);
            gangLeader.AddRelationship(npc.Id, 0);
            
            npc.Brain?.Memory?.RecordEvent(new MemoryEvent
            {
                Type = MemoryType.JoinedGang,
                InvolvedCharacter = gangLeader.Id,
                Description = $"Joined {gangLeader.Name}'s gang",
                Importance = 0.8f,
                Location = npc.CurrentLocation
            });
            
            // GD.Print($"[WorldSim] {npc.Name} joined {gangLeader.Name}'s gang");
        }
    }
    
    private void ExecuteSeekRevenge(NPC npc, string targetId, WorldState world)
    {
        if (string.IsNullOrEmpty(targetId)) return;
        
        var target = world.GetNPCById(targetId);
        if (target != null && target.CurrentLocation == npc.CurrentLocation)
        {
            // If target is found, attack them
            ExecuteAttack(npc, targetId, world);
        }
        else
        {
            // Move around looking for the target
            MoveNPCToRandomLocation(npc);
            npc.ChangeActivity(Activity.Hunting, $"Seeking revenge against {target?.Name ?? "enemy"}");
        }
    }
    
    /// <summary>
    /// When an NPC has a strong dominant emotion, it affects nearby NPCs at the same location.
    /// Anger spreads fear, fear spreads panic, joy is infectious, sadness resonates with the empathetic.
    /// Rate-limited to once per 5 ticks per NPC to prevent runaway cascades.
    /// </summary>
    private void ProcessEmotionalCascades(NPC npc)
    {
        // Rate limit: only cascade every 5 ticks per NPC
        string npcId = npc.Id ?? npc.Name;
        if (_lastCascadeTick.TryGetValue(npcId, out int lastTick) && (_currentTick - lastTick) < 5)
            return;

        var dominant = npc.EmotionalState.GetDominantEmotion();
        if (dominant == null) return;

        float intensity = npc.EmotionalState.GetEmotionIntensity(dominant.Value);
        if (intensity < 0.7f) return; // Only strong emotions cascade

        // Find other NPCs at the same location
        var nearbyNPCs = npcs.Where(n =>
            n != npc && n.IsAlive && !n.IsDead &&
            n.EmotionalState != null &&
            n.CurrentLocation == npc.CurrentLocation).ToList();

        if (nearbyNPCs.Count == 0) return;

        _lastCascadeTick[npcId] = _currentTick;
        string npcName = npc.Name2 ?? npc.Name;

        foreach (var nearby in nearbyNPCs)
        {
            switch (dominant.Value)
            {
                case EmotionType.Anger:
                    // Aggressive NPCs catch anger, others become fearful
                    if (nearby.Brain?.Personality?.Aggression > 0.5f)
                        nearby.EmotionalState.AddEmotion(EmotionType.Anger, 0.1f, 60);
                    else
                        nearby.EmotionalState.AddEmotion(EmotionType.Fear, 0.2f, 90);
                    break;

                case EmotionType.Fear:
                    // Panic spreads
                    nearby.EmotionalState.AddEmotion(EmotionType.Fear, 0.15f, 60);
                    break;

                case EmotionType.Joy:
                    // Infectious happiness
                    nearby.EmotionalState.AddEmotion(EmotionType.Joy, 0.1f, 120);
                    break;

                case EmotionType.Sadness:
                    // Empathetic NPCs share sadness
                    if (nearby.Brain?.Personality != null &&
                        (nearby.Brain.Personality.Mysticism > 0.5f || nearby.Brain.Personality.Sociability > 0.6f))
                    {
                        nearby.EmotionalState.AddEmotion(EmotionType.Sadness, 0.1f, 90);
                    }
                    break;
            }
        }

        // Generate gossip for particularly dramatic cascades
        if (nearbyNPCs.Count >= 3)
        {
            string emotionWord = dominant.Value switch
            {
                EmotionType.Anger => "rage",
                EmotionType.Fear => "panic",
                EmotionType.Joy => "celebration",
                EmotionType.Sadness => "grief",
                _ => null
            };
            if (emotionWord != null)
                AddGossip($"A wave of {emotionWord} swept through the {npc.CurrentLocation} — started by {npcName}");
        }
    }

    /// <summary>
    /// Sociable NPCs spread gossip from the pool to the news feed.
    /// </summary>
    private void ProcessGossip()
    {
        if (_gossipPool.Count == 0) return;
        if (random.NextDouble() > 0.10) return; // 10% chance per tick

        // Find a sociable NPC at a social location
        var socialLocations = new[] { "Inn", "Love Street", "Main Street", "Auction House", "Temple" };
        var gossiper = npcs
            .Where(n => n.IsAlive && !n.IsDead &&
                        n.Brain?.Personality?.Sociability > 0.4f &&
                        socialLocations.Contains(n.CurrentLocation))
            .OrderBy(_ => random.Next())
            .FirstOrDefault();

        if (gossiper == null) return;

        // Pick a random gossip to share
        var gossip = _gossipPool[random.Next(_gossipPool.Count)];
        string gossiperName = gossiper.Name2 ?? gossiper.Name;

        NewsSystem.Instance?.Newsy($"{gossiperName} is telling anyone who'll listen: \"{gossip.Text}\"");

        gossip.TimesShared++;
        if (gossip.TimesShared >= gossip.MaxShares)
            _gossipPool.Remove(gossip);
    }

    private void ProcessWorldEvents()
    {
        // Random world events that can affect NPCs
        if ((float)Random.Shared.NextDouble() < 0.05f) // 5% chance per simulation step
        {
            GenerateRandomEvent();
        }
    }
    
    private void GenerateRandomEvent()
    {
        var events = new[]
        {
            "A merchant caravan arrives in town",
            "Strange noises are heard from the dungeon",
            "The king makes a royal decree",
            "A festival begins in the town square",
            "Bandits are spotted near the roads",
            "A mysterious stranger appears",
            "The weather turns stormy",
            "A new shop opens in the market"
        };
        
        var randomEvent = events[Random.Shared.Next(0, events.Length)];
        // GD.Print($"[WorldSim] World Event: {randomEvent}");
        
        // Record event in all NPC memories with low importance
        foreach (var npc in npcs.Where(n => n.IsAlive && n.Brain != null))
        {
            npc.Brain.Memory?.RecordEvent(new MemoryEvent
            {
                Type = MemoryType.WitnessedEvent,
                Description = $"Witnessed: {randomEvent}",
                Importance = 0.2f,
                Location = npc.CurrentLocation
            });
        }
    }
    
    private void UpdateSocialDynamics()
    {
        // Check for gang betrayals
        CheckGangBetrayals();

        // Check for new gang formations
        CheckGangFormations();

        // Process rival relationships
        ProcessRivalries();

        // Process team dynamics
        ProcessTeamDynamics();

        // Process throne and city challenges
        ProcessChallenges();

        // Process prisoner activities
        ProcessPrisonerActivities();

        // Process royal court politics
        ProcessRoyalCourtPolitics();
    }

    /// <summary>
    /// Process royal court political activities - guard recruitment, court intrigue
    /// </summary>
    private void ProcessRoyalCourtPolitics()
    {
        try
        {
            var king = CastleLocation.GetCurrentKing();
            if (king == null || !king.IsActive) return;

            // NPC guard recruitment (10% chance per tick if there are openings)
            if (king.Guards.Count < King.MaxNPCGuards && (float)Random.Shared.NextDouble() < 0.10f)
            {
                ProcessNPCGuardRecruitment(king);
            }

            // Court intrigue processing (5% chance per tick)
            if ((float)Random.Shared.NextDouble() < 0.05f)
            {
                ProcessCourtIntrigue(king);
            }

            // Plot progression (all active plots advance)
            foreach (var plot in king.ActivePlots.ToList())
            {
                AdvancePlot(king, plot);
            }
        }
        catch (Exception ex)
            {
                DebugLogger.Instance.Log(DebugLogger.LogLevel.Debug, "SYSTEM", $"Swallowed exception: {ex.Message}");
            }
    }

    /// <summary>
    /// NPCs may apply to become royal guards if positions are available
    /// </summary>
    private void ProcessNPCGuardRecruitment(King king)
    {
        // Find NPCs who might want to become guards:
        // - Not already a guard
        // - Not in prison
        // - Not on a team (guards serve the crown, not teams)
        // - Not already the King
        // - Level 5+ (minimum competency)
        // - High loyalty/lawfulness personality
        var candidates = npcs?
            .Where(n => n.IsAlive &&
                   n.Level >= 5 &&
                   n.DaysInPrison <= 0 &&
                   string.IsNullOrEmpty(n.Team) &&
                   !n.King &&
                   !n.IsStoryNPC &&
                   !king.Guards.Any(g => g.Name == n.Name) &&
                   (n.Brain?.Personality?.Trustworthiness > 0.5f ||
                    n.Brain?.Personality?.Loyalty > 0.6f))
            .OrderByDescending(n => n.Level)
            .Take(3)
            .ToList();

        if (candidates == null || candidates.Count == 0) return;

        // Pick a random candidate
        var applicant = candidates[Random.Shared.Next(0, candidates.Count)];

        // Check if treasury can afford the recruitment cost
        if (king.Treasury < GameConfig.GuardRecruitmentCost)
        {
            // GD.Print($"[WorldSim] {applicant.Name} wanted to join guards but treasury is low");
            return;
        }

        // Add the NPC as a guard
        var guard = new RoyalGuard
        {
            Name = applicant.Name,
            AI = CharacterAI.Computer,
            Sex = applicant.Sex,
            DailySalary = GameConfig.BaseGuardSalary + (applicant.Level * GameConfig.GuardSalaryPerGuardLevel),
            RecruitmentDate = DateTime.Now,
            Loyalty = 70 + Random.Shared.Next(0, 31)  // New recruits have 70-100 loyalty
        };

        king.Guards.Add(guard);
        king.Treasury -= GameConfig.GuardRecruitmentCost;

        NewsSystem.Instance?.Newsy(false, $"{applicant.Name} has joined the Royal Guard!");
        // GD.Print($"[WorldSim] {applicant.Name} recruited as Royal Guard");
    }

    /// <summary>
    /// Process court intrigue - unhappy court members may start plots
    /// </summary>
    private void ProcessCourtIntrigue(King king)
    {
        // Initialize court if empty
        if (king.CourtMembers.Count == 0)
        {
            InitializeCourtMembers(king);
        }

        // Check for new plots starting
        var unhappyMembers = king.CourtMembers
            .Where(c => c.LoyaltyToKing < 40 && !c.IsPlotting)
            .ToList();

        if (unhappyMembers.Count >= 2 && king.ActivePlots.Count < 3)
        {
            // Start a new plot
            var conspirators = unhappyMembers.Take(Random.Shared.Next(2, (Math.Min(4, unhappyMembers.Count)) + 1)).ToList();

            string plotType = Random.Shared.Next(0, 4) switch
            {
                0 => "Assassination",
                1 => "Coup",
                2 => "Scandal",
                _ => "Sabotage"
            };

            var plot = new CourtIntrigue
            {
                PlotType = plotType,
                Conspirators = conspirators.Select(c => c.Name).ToList(),
                Target = king.Name,
                Progress = 10 + Random.Shared.Next(0, 21),
                StartDate = DateTime.Now
            };

            king.ActivePlots.Add(plot);
            foreach (var conspirator in conspirators)
            {
                conspirator.IsPlotting = true;
            }

            // GD.Print($"[WorldSim] New {plotType} plot started by {string.Join(", ", plot.Conspirators)}");
        }
    }

    /// <summary>
    /// Initialize court members for a new king
    /// </summary>
    private void InitializeCourtMembers(King king)
    {
        // Create default court positions
        var roles = new[] { "Royal Advisor", "Court Steward", "Marshal", "Spymaster", "Treasurer" };
        var factions = Enum.GetValues<CourtFaction>().Where(f => f != CourtFaction.None).ToArray();

        foreach (var role in roles)
        {
            var member = new CourtMember
            {
                Name = GenerateCourtMemberName(),
                Role = role,
                Faction = factions[Random.Shared.Next(0, factions.Length)],
                Influence = 40 + Random.Shared.Next(0, 41),
                LoyaltyToKing = 50 + Random.Shared.Next(0, 41),
                JoinedCourt = DateTime.Now
            };
            king.CourtMembers.Add(member);
        }
    }

    /// <summary>
    /// Generate a random court member name
    /// </summary>
    private string GenerateCourtMemberName()
    {
        var firstNames = new[] { "Lord", "Lady", "Sir", "Baron", "Countess", "Duke", "Duchess" };
        var lastNames = new[] { "Blackwood", "Ashford", "Ironside", "Goldstein", "Silverhart",
                                "Ravencroft", "Thornwood", "Nightingale", "Stormwind", "Darkhaven" };

        return $"{firstNames[Random.Shared.Next(0, firstNames.Length)]} {lastNames[Random.Shared.Next(0, lastNames.Length)]}";
    }

    /// <summary>
    /// Advance a plot toward completion
    /// </summary>
    private void AdvancePlot(King king, CourtIntrigue plot)
    {
        if (plot.IsDiscovered) return;

        // Plots advance 5-15% per tick
        plot.Progress += Random.Shared.Next(5, 16);

        // Chance of discovery (higher for larger conspiracies)
        float discoveryChance = 0.02f + (plot.Conspirators.Count * 0.01f);
        if ((float)Random.Shared.NextDouble() < discoveryChance)
        {
            plot.IsDiscovered = true;
            plot.DiscoveredBy = "Royal Spymaster";

            // Conspirators go to prison
            foreach (var conspirator in plot.Conspirators)
            {
                var member = king.CourtMembers.FirstOrDefault(m => m.Name == conspirator);
                if (member != null)
                {
                    member.IsPlotting = false;
                    king.CourtMembers.Remove(member);
                }
            }

            // v0.62.1 (article fix): plot types include "Assassination" / "Espionage"
            // which need "An" not "A". Lowercased so the helper still finds the vowel.
            string plotTypeLc = plot.PlotType.ToLower();
            NewsSystem.Instance?.Newsy(true,
                $"{GameConfig.GetIndefiniteArticle(plotTypeLc)} {plotTypeLc} plot against {king.GetTitle()} {king.Name} was discovered!");

            king.ActivePlots.Remove(plot);
            return;
        }

        // Plot triggers at 100%
        if (plot.Progress >= 100)
        {
            ExecutePlot(king, plot);
        }
    }

    /// <summary>
    /// Execute a completed plot
    /// </summary>
    private void ExecutePlot(King king, CourtIntrigue plot)
    {
        switch (plot.PlotType)
        {
            case "Assassination":
                // King "survives" but is weakened
                king.Treasury /= 2;
                NewsSystem.Instance?.Newsy(true,
                    $"ASSASSINATION ATTEMPT! {king.GetTitle()} {king.Name} narrowly survived an assassination plot!");
                break;

            case "Coup":
                // Treasury stolen, guards desert
                king.Treasury = Math.Max(0, king.Treasury - 10000);
                var deserters = king.Guards.Where(g => g.Loyalty < 50).ToList();
                foreach (var guard in deserters)
                {
                    king.Guards.Remove(guard);
                }
                NewsSystem.Instance?.Newsy(true,
                    $"COUP ATTEMPT! {deserters.Count} guards joined the conspiracy against {king.GetTitle()} {king.Name}!");
                break;

            case "Scandal":
                // King's reputation damaged - harder to collect taxes
                king.TaxRate = Math.Max(0, king.TaxRate - 10);
                NewsSystem.Instance?.Newsy(true,
                    $"SCANDAL! Shocking revelations about {king.GetTitle()} {king.Name} rock the kingdom!");
                break;

            case "Sabotage":
                // Treasury damaged
                king.Treasury = Math.Max(0, king.Treasury - 5000);
                king.MagicBudget = Math.Max(0, king.MagicBudget - 2000);
                NewsSystem.Instance?.Newsy(true,
                    $"SABOTAGE! The royal treasury has been plundered!");
                break;
        }

        // Clear conspirators' plotting status
        foreach (var conspirator in plot.Conspirators)
        {
            var member = king.CourtMembers.FirstOrDefault(m => m.Name == conspirator);
            if (member != null)
            {
                member.IsPlotting = false;
            }
        }

        king.ActivePlots.Remove(plot);
    }

    /// <summary>
    /// Process throne and city control challenges
    /// </summary>
    private void ProcessChallenges()
    {
        try
        {
            ChallengeSystem.Instance.ProcessMaintenanceChallenges();
        }
        catch (Exception ex)
            {
                DebugLogger.Instance.Log(DebugLogger.LogLevel.Debug, "SYSTEM", $"Swallowed exception: {ex.Message}");
            }
    }

    /// <summary>
    /// Process prisoner stat-building activities
    /// </summary>
    private void ProcessPrisonerActivities()
    {
        try
        {
            PrisonActivitySystem.Instance.ProcessAllPrisonerActivities();
        }
        catch (Exception ex)
            {
                DebugLogger.Instance.Log(DebugLogger.LogLevel.Debug, "SYSTEM", $"Swallowed exception: {ex.Message}");
            }
    }

    /// <summary>
    /// Process team-related dynamics: betrayals, team wars, turf control
    /// </summary>
    private void ProcessTeamDynamics()
    {
        // Check for team betrayals (members leaving)
        CheckTeamBetrayals();

        // Check for team vs team conflicts
        CheckTeamWars();

        // Update turf control
        UpdateTurfControl();
    }

    /// <summary>
    /// Check for NPCs leaving their teams
    /// </summary>
    private void CheckTeamBetrayals()
    {
        var teamMembers = npcs.Where(n => !string.IsNullOrEmpty(n.Team) && n.IsAlive).ToList();

        // Dissolve any 1-member NPC-only teams (cleanup for teams that lost members to death/departure)
        // Player teams are protected — the player counts as a member even though they're not in the NPC list
        var soloTeams = teamMembers
            .GroupBy(n => n.Team, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1)
            .ToList();
        foreach (var soloGroup in soloTeams)
        {
            var solo = soloGroup.First();
            // Don't dissolve the player's team — player is a member but not in the NPC list
            if (IsPlayerTeam(solo.Team))
                continue;
            var oldTeam = solo.Team;
            solo.Team = "";
            solo.TeamPW = "";
            solo.CTurf = false;
            solo.TeamRec = 0;
            NewsSystem.Instance.Newsy(true, $"The team '{oldTeam}' has disbanded as {solo.DisplayName} went solo!");
        }

        // Re-fetch after cleanup
        teamMembers = npcs.Where(n => !string.IsNullOrEmpty(n.Team) && n.IsAlive).ToList();

        foreach (var member in teamMembers)
        {
            // Never remove NPCs from the player's team via world simulation
            // Player must manually sack them from Team Corner
            if (IsPlayerTeam(member.Team))
                continue;

            // Low loyalty or betrayal-prone personality
            bool likelyToLeave = member.Brain?.Personality?.IsLikelyToBetray() == true ||
                                 member.Loyalty < 30;

            // Online: 0.1% per tick (~12% per hour). Single-player: 1% per tick (unchanged).
            float abandonChance = UsurperRemake.BBS.DoorMode.IsOnlineMode ? 0.001f : 0.01f;
            if (likelyToLeave && random.NextDouble() < abandonChance)
            {
                string oldTeam = member.Team;

                // Leave the team
                member.Team = "";
                member.TeamPW = "";
                member.CTurf = false;
                member.TeamRec = 0;

                NewsSystem.Instance.Newsy(true, $"{member.Name} abandoned '{oldTeam}'!");
                // GD.Print($"[WorldSim] {member.Name} left team '{oldTeam}'");

                // Check if team is now empty or solo (NPC-only teams)
                var remainingMembers = npcs.Count(n => n.Team == oldTeam && n.IsAlive);
                if (remainingMembers == 0 && !IsPlayerTeam(oldTeam))
                {
                    NewsSystem.Instance.Newsy(true, $"The team '{oldTeam}' has been disbanded!");
                }
                else if (remainingMembers == 1 && !IsPlayerTeam(oldTeam))
                {
                    // Dissolve single-member NPC-only teams — can't be a team of one
                    // Player teams with 1 NPC are fine (player is also a member)
                    var soloMember = npcs.FirstOrDefault(n => n.Team == oldTeam && n.IsAlive);
                    if (soloMember != null)
                    {
                        soloMember.Team = "";
                        soloMember.TeamPW = "";
                        soloMember.CTurf = false;
                        soloMember.TeamRec = 0;
                        NewsSystem.Instance.Newsy(true, $"The team '{oldTeam}' has disbanded as {soloMember.DisplayName} went solo!");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Check for team vs team conflicts
    /// </summary>
    private void CheckTeamWars()
    {
        // Get all active teams
        var teams = npcs
            .Where(n => !string.IsNullOrEmpty(n.Team) && n.IsAlive)
            .GroupBy(n => n.Team)
            .Where(g => g.Count() >= 2) // Only teams with 2+ members
            .ToList();

        if (teams.Count < 2) return;

        // Small chance for team war
        if (random.NextDouble() > 0.02) return; // 2% chance per tick

        // Pick two random teams at the same location
        var team1Group = teams[random.Next(teams.Count)];
        var team1Location = team1Group.First().CurrentLocation;

        var teamsAtSameLocation = teams
            .Where(t => t.Key != team1Group.Key && t.Any(n => n.CurrentLocation == team1Location))
            .ToList();

        if (!teamsAtSameLocation.Any()) return;

        var team2Group = teamsAtSameLocation[random.Next(teamsAtSameLocation.Count)];

        // Team war!
        var team1 = team1Group.Where(n => n.CurrentLocation == team1Location && n.IsAlive).ToList();
        var team2 = team2Group.Where(n => n.CurrentLocation == team1Location && n.IsAlive).ToList();

        if (team1.Count == 0 || team2.Count == 0) return;

        string team1Name = team1Group.Key;
        string team2Name = team2Group.Key;

        NewsSystem.Instance.Newsy(true, $"Team War! '{team1Name}' clashes with '{team2Name}' at {team1Location}!");
        // GD.Print($"[WorldSim] Team war between '{team1Name}' and '{team2Name}'");

        // Simulate team battle
        bool team1Won = SimulateTeamVsTeamCombat(team1, team2);

        if (team1Won)
        {
            NewsSystem.Instance.Newsy(true, $"'{team1Name}' emerged victorious against '{team2Name}'!");
        }
        else
        {
            NewsSystem.Instance.Newsy(true, $"'{team2Name}' emerged victorious against '{team1Name}'!");
        }
    }

    /// <summary>
    /// Simulate team vs team combat
    /// </summary>
    private bool SimulateTeamVsTeamCombat(List<NPC> team1, List<NPC> team2)
    {
        int rounds = 0;
        const int maxRounds = 30;

        while (team1.Any(m => m.IsAlive) && team2.Any(m => m.IsAlive) && rounds < maxRounds)
        {
            rounds++;

            // Team 1 attacks team 2
            foreach (var attacker in team1.Where(m => m.IsAlive))
            {
                var target = team2.Where(m => m.IsAlive).OrderBy(_ => random.Next()).FirstOrDefault();
                if (target == null) break;

                long damage = Math.Max(1, attacker.Strength + attacker.WeapPow - target.Defence - target.ArmPow);
                damage += random.Next(1, (int)Math.Max(2, attacker.WeapPow / 4));
                target.TakeDamage(damage);

                if (!target.IsAlive)
                {
                    MarkNPCDead(target, GameConfig.PermadeathChanceTeamWar, attacker.Name, target.CurrentLocation ?? "battle");
                }
            }

            // Team 2 attacks team 1
            foreach (var attacker in team2.Where(m => m.IsAlive))
            {
                var target = team1.Where(m => m.IsAlive).OrderBy(_ => random.Next()).FirstOrDefault();
                if (target == null) break;

                long damage = Math.Max(1, attacker.Strength + attacker.WeapPow - target.Defence - target.ArmPow);
                damage += random.Next(1, (int)Math.Max(2, attacker.WeapPow / 4));
                target.TakeDamage(damage);

                if (!target.IsAlive)
                {
                    MarkNPCDead(target, GameConfig.PermadeathChanceTeamWar, attacker.Name, target.CurrentLocation ?? "battle");
                }
            }
        }

        // Determine winner by survivors
        int team1Alive = team1.Count(m => m.IsAlive);
        int team2Alive = team2.Count(m => m.IsAlive);

        if (team1Alive > team2Alive) return true;
        if (team2Alive > team1Alive) return false;

        // Tiebreaker: total remaining HP
        long team1HP = team1.Where(m => m.IsAlive).Sum(m => m.HP);
        long team2HP = team2.Where(m => m.IsAlive).Sum(m => m.HP);

        return team1HP >= team2HP;
    }

    /// <summary>
    /// Update turf control - strongest team can claim turf
    /// </summary>
    private void UpdateTurfControl()
    {
        // Get current turf controller (NPC-side)
        var currentController = npcs.FirstOrDefault(n => n.CTurf && !string.IsNullOrEmpty(n.Team));

        // Check if a player's team controls turf (set by WorldSimService from economy data).
        // The player may have a team with no NPC members — the world sim can't see the player
        // but should respect their city control until another team actively takes it.
        bool playerTeamControlsTurf = !string.IsNullOrEmpty(PlayerTurfTeam);

        // Get all NPC teams with their power
        var teams = npcs
            .Where(n => !string.IsNullOrEmpty(n.Team) && n.IsAlive)
            .GroupBy(n => n.Team)
            .Select(g => new
            {
                TeamName = g.Key,
                Power = g.Sum(m => m.Level + m.Strength + m.Defence),
                Members = g.ToList(),
                HasTurf = g.Any(m => m.CTurf)
            })
            .OrderByDescending(t => t.Power)
            .ToList();

        if (teams.Count == 0) return;

        var strongestTeam = teams.First();

        // If no one controls turf (and no player team holds it), small chance for strongest team to claim
        if (currentController == null && !playerTeamControlsTurf &&
            strongestTeam.Power > 100 && random.NextDouble() < 0.02)
        {
            foreach (var member in strongestTeam.Members)
            {
                member.CTurf = true;
                member.TeamRec = 0;
            }

            NewsSystem.Instance.Newsy(true, $"'{strongestTeam.TeamName}' has taken control of the town!");
            // GD.Print($"[WorldSim] Team '{strongestTeam.TeamName}' took control of turf");
        }
    }
    
    private void CheckGangBetrayals()
    {
        var gangMembers = npcs.Where(npc => !string.IsNullOrEmpty(npc.GangId)).ToList();
        
        foreach (var member in gangMembers)
        {
            if (member.Brain?.Personality?.IsLikelyToBetray() == true && (float)Random.Shared.NextDouble() < 0.02f) // 2% chance
            {
                var gangLeader = npcs.FirstOrDefault(npc => npc.Id == member.GangId);
                if (gangLeader != null)
                {
                    // Betray the gang
                    member.GangId = null;
                    gangLeader.GangMembers.Remove(member.Id);
                    
                    member.AddRelationship(gangLeader.Id, 0);
                    gangLeader.AddRelationship(member.Id, 0);
                    
                    member.Brain?.RecordInteraction(gangLeader, InteractionType.Betrayed);
                    gangLeader.Brain?.RecordInteraction(member, InteractionType.Betrayed);
                    
                    // GD.Print($"[WorldSim] {member.Name} betrayed {gangLeader.Name}'s gang!");
                }
            }
        }
    }
    
    private void CheckGangFormations()
    {
        var potentialLeaders = npcs.Where(npc => 
            npc.IsAlive && 
            string.IsNullOrEmpty(npc.GangId) && 
            npc.GangMembers.Count == 0 &&
            npc.Brain?.Personality?.IsLikelyToJoinGang() == true &&
            npc.Brain.Personality.Ambition > 0.7f).ToList();
        
        foreach (var leader in potentialLeaders)
        {
            if ((float)Random.Shared.NextDouble() < 0.01f) // 1% chance to form new gang
            {
                // Look for potential gang members in the same location
                var sameLocation = npcs.Where(npc => 
                    npc.CurrentLocation == leader.CurrentLocation &&
                    npc.Id != leader.Id &&
                    string.IsNullOrEmpty(npc.GangId) &&
                    npc.Brain?.Personality?.IsLikelyToJoinGang() == true).ToList();
                
                if (sameLocation.Count >= 2)
                {
                    var newMember = sameLocation[Random.Shared.Next(0, sameLocation.Count)];
                    newMember.GangId = leader.Id;
                    leader.GangMembers.Add(newMember.Id);
                    
                    // GD.Print($"[WorldSim] {leader.Name} formed a new gang with {newMember.Name}");
                }
            }
        }
    }
    
    private int _tensionMessagesThisTick = 0;

    private void ProcessRivalries()
    {
        bool isOnline = UsurperRemake.BBS.DoorMode.IsOnlineMode;
        _tensionMessagesThisTick = 0;

        // --- Seed new rivalries ---
        // Online: 2% chance (was 8%) — rivalries develop slowly over hours, not minutes
        // Single-player: 8% chance (unchanged)
        float newRivalryChance = isOnline ? 0.02f : 0.08f;

        var aliveNpcs = npcs.Where(npc => npc.IsAlive && !npc.IsDead).ToList();
        foreach (var npc in aliveNpcs)
        {
            var personality = npc.Brain?.Personality;
            if (personality == null || personality.Aggression < 0.5f) continue;
            if ((float)Random.Shared.NextDouble() > newRivalryChance) continue;

            // Online mode: cap enemy list size to prevent runaway escalation
            if (isOnline && npc.Enemies.Count >= 5) continue;

            // Find a potential rival at the same location
            var potentialRivals = aliveNpcs.Where(other =>
                other.Id != npc.Id &&
                !npc.Enemies.Contains(other.Id) &&
                other.CurrentLocation == npc.CurrentLocation &&
                (!npc.IsMarried || npc.SpouseName != (other.Name2 ?? other.Name)) // Don't rival your spouse
            ).ToList();

            if (potentialRivals.Count == 0) continue;

            // Prefer rivals with opposing alignment or faction
            NPC? rival = null;
            foreach (var candidate in potentialRivals)
            {
                bool opposingAlignment = (npc.Chivalry > 200 && candidate.Darkness > 200) ||
                                          (npc.Darkness > 200 && candidate.Chivalry > 200);
                bool opposingFaction = npc.NPCFaction.HasValue && candidate.NPCFaction.HasValue &&
                                       npc.NPCFaction != candidate.NPCFaction;
                bool personalityClash = personality.Aggression > 0.7f &&
                                        (candidate.Brain?.Personality?.Aggression ?? 0) > 0.6f;

                if (opposingAlignment || opposingFaction || personalityClash)
                {
                    rival = candidate;
                    break;
                }
            }

            // Fallback: pick a random rival (less likely)
            float fallbackChance = isOnline ? 0.01f : 0.03f;
            if (rival == null && (float)Random.Shared.NextDouble() < fallbackChance)
                rival = potentialRivals[random.Next(potentialRivals.Count)];

            if (rival != null)
            {
                npc.Enemies.Add(rival.Id);
                rival.Enemies.Add(npc.Id);

                // Record the enmity in memory
                string npcName = npc.Name2 ?? npc.Name;
                string rivalName = rival.Name2 ?? rival.Name;
                npc.Brain?.Memory?.RecordEvent(new MemoryEvent
                {
                    Type = MemoryType.MadeEnemy,
                    InvolvedCharacter = rivalName,
                    Description = $"Developed a rivalry with {rivalName}",
                    Importance = 0.6f,
                    Location = npc.CurrentLocation
                });
                rival.Brain?.Memory?.RecordEvent(new MemoryEvent
                {
                    Type = MemoryType.MadeEnemy,
                    InvolvedCharacter = npcName,
                    Description = $"Developed a rivalry with {npcName}",
                    Importance = 0.6f,
                    Location = rival.CurrentLocation
                });

                // In online mode, use cooldown and per-tick cap to prevent feed domination
                string pairKey = GetPairKey(npc.Id, rival.Id);
                bool cooldownActive = false;
                if (isOnline && _tensionMessageCooldown.TryGetValue(pairKey, out int lastTensionTick))
                    cooldownActive = (_currentTick - lastTensionTick) < TENSION_MESSAGE_COOLDOWN_TICKS;

                if (!cooldownActive && (!isOnline || _tensionMessagesThisTick < MAX_TENSION_MESSAGES_PER_TICK))
                {
                    NewsSystem.Instance?.Newsy(false, $"Tensions are rising between {npcName} and {rivalName} at the {npc.CurrentLocation}.");
                    if (isOnline)
                    {
                        _tensionMessageCooldown[pairKey] = _currentTick;
                        _tensionMessagesThisTick++;
                    }
                }
            }
        }

        // --- Escalate existing rivalries between enemies ---
        // Online: 3% chance (was 12%) with per-pair cooldown and daily combat cap
        // Single-player: 12% chance (unchanged)
        float escalationChance = isOnline ? 0.03f : 0.12f;

        var enemies = npcs.Where(npc => npc.IsAlive && !npc.IsDead && npc.Enemies.Count > 0).ToList();
        var processedPairs = new HashSet<string>(); // Prevent A→B and B→A both firing in same tick

        foreach (var npc in enemies)
        {
            // Player team NPCs don't participate in world sim brawls
            if (IsPlayerTeam(npc.Team)) continue;

            // Clean corrupt self-references from enemy list
            npc.Enemies.RemoveAll(eid => eid == npc.Id);
            if (npc.Enemies.Count == 0) continue;

            // Online: skip NPC if they've hit their daily combat cap
            if (isOnline && HasHitDailyCombatCap(npc.Id)) continue;

            foreach (var enemyId in npc.Enemies.ToList()) // ToList to avoid modification during iteration
            {
                var enemy = npcs.FirstOrDefault(n => n.Id == enemyId);
                if (enemy == null || enemy.Id == npc.Id) continue; // skip self (corrupt enemy list)
                if (!enemy.IsAlive || enemy.IsDead) continue;
                if (IsPlayerTeam(enemy.Team)) continue; // Don't target player team members
                if ((float)Random.Shared.NextDouble() >= escalationChance) continue;

                // Skip if this pair already had a conflict this tick (prevents duplicate news)
                string pairId = string.Compare(npc.Id, enemyId, StringComparison.Ordinal) < 0
                    ? $"{npc.Id}:{enemyId}" : $"{enemyId}:{npc.Id}";
                if (!processedPairs.Add(pairId)) continue;

                // Must be at same location to escalate
                if (npc.CurrentLocation != enemy.CurrentLocation) continue;

                // Online: check per-pair cooldown
                if (isOnline)
                {
                    string pairKey = GetPairKey(npc.Id, enemyId);
                    if (_pairEscalationCooldown.TryGetValue(pairKey, out int lastTick) &&
                        _currentTick - lastTick < PAIR_ESCALATION_COOLDOWN_TICKS)
                        continue;
                    _pairEscalationCooldown[pairKey] = _currentTick;

                    // Also check enemy's daily combat cap
                    if (HasHitDailyCombatCap(enemyId)) continue;
                }

                // Pick conflict type based on instigator's personality
                var personality2 = npc.Brain?.Personality;
                var world = new WorldState(npcs);

                if (personality2 != null && personality2.Greed > 0.5f && personality2.Intelligence > 0.5f)
                {
                    ExecuteTheft(npc, enemy);
                }
                else if (personality2 != null && personality2.Courage > 0.6f && personality2.Aggression < 0.6f)
                {
                    ExecuteChallenge(npc, enemy);
                }
                else
                {
                    // Default: brawl (existing combat)
                    ExecuteAttack(npc, enemyId, world);
                    // Witnesses observe the brawl
                    SocialInfluenceSystem.RecordWitnesses(npcs, npc.CurrentLocation,
                        npc.Name2 ?? npc.Name, enemy.Name2 ?? enemy.Name, WitnessEventType.SawBrawl);
                    AddGossip($"{npc.Name2 ?? npc.Name} got into a brawl with {enemy.Name2 ?? enemy.Name} at the {npc.CurrentLocation}");
                }

                // Record combat for daily cap
                if (isOnline)
                {
                    RecordCombat(npc.Id);
                    RecordCombat(enemyId);
                }
            }
        }

        // --- Online mode: periodic enemy reconciliation ---
        // Every sim-day, enemies have a chance to bury the hatchet
        if (isOnline && _currentTick % TICKS_PER_SIM_DAY == 0)
        {
            ProcessReconciliation(aliveNpcs);
        }
    }

    /// <summary>
    /// Enemies gradually reconcile over time. Low-aggression NPCs drop old rivalries.
    /// Each enemy pair has a 10-20% chance per sim-day to reconcile (based on personality).
    /// </summary>
    private void ProcessReconciliation(List<NPC> aliveNpcs)
    {
        foreach (var npc in aliveNpcs)
        {
            if (npc.Enemies.Count == 0) continue;
            var personality = npc.Brain?.Personality;
            // Base reconciliation chance: 15%. Low aggression increases it, high aggression decreases it.
            float baseChance = 0.15f;
            if (personality != null)
            {
                baseChance += (1f - personality.Aggression) * 0.10f; // Peaceful NPCs reconcile more
                baseChance -= personality.Aggression * 0.05f;        // Aggressive NPCs hold grudges
            }
            baseChance = Math.Clamp(baseChance, 0.05f, 0.30f);

            foreach (var enemyId in npc.Enemies.ToList())
            {
                if ((float)Random.Shared.NextDouble() < baseChance)
                {
                    npc.Enemies.Remove(enemyId);
                    var enemy = npcs.FirstOrDefault(n => n.Id == enemyId);
                    if (enemy != null)
                    {
                        enemy.Enemies.Remove(npc.Id);
                    }
                    // No news for reconciliation — it happens quietly
                }
            }
        }
    }

    /// <summary>
    /// Cunning/greedy NPC steals gold from a rival.
    /// </summary>
    private void ExecuteTheft(NPC thief, NPC victim)
    {
        // Guard against self-theft (corrupt enemy list)
        if (thief.Id == victim.Id) return;
        if (victim.Gold <= 0) return;

        // Steal 5-15% of victim's gold
        long stolenAmount = Math.Max(1, (long)(victim.Gold * (0.05 + random.NextDouble() * 0.10)));
        victim.Gold -= stolenAmount;
        thief.Gold += stolenAmount;

        // Record memories
        thief.Brain?.Memory?.RecordEvent(new MemoryEvent
        {
            Type = MemoryType.GainedGold,
            InvolvedCharacter = victim.Name2 ?? victim.Name,
            Description = $"Stole {stolenAmount} gold from {victim.Name2 ?? victim.Name}",
            Importance = 0.7f,
            Location = thief.CurrentLocation
        });
        victim.Brain?.Memory?.RecordEvent(new MemoryEvent
        {
            Type = MemoryType.Attacked,
            InvolvedCharacter = thief.Name2 ?? thief.Name,
            Description = $"{thief.Name2 ?? thief.Name} stole {stolenAmount} gold from me",
            Importance = 0.8f,
            Location = victim.CurrentLocation
        });

        // Theft creates enmity
        if (!victim.Enemies.Contains(thief.Id))
            victim.Enemies.Add(thief.Id);
        if (!thief.Enemies.Contains(victim.Id))
            thief.Enemies.Add(victim.Id);

        // Victim becomes angry
        victim.EmotionalState?.AddEmotion(EmotionType.Anger, 0.6f, 180);

        // Generate news
        string thiefName = thief.Name2 ?? thief.Name;
        string victimName = victim.Name2 ?? victim.Name;
        // Witnesses observe the theft
        SocialInfluenceSystem.RecordWitnesses(npcs, thief.CurrentLocation, thiefName, victimName, WitnessEventType.SawTheft);
        NewsSystem.Instance?.Newsy($"{thiefName} was caught pickpocketing {victimName} at the {thief.CurrentLocation}! {stolenAmount} gold went missing.");
        AddGossip($"{thiefName} stole from {victimName}");

        UsurperRemake.Systems.DebugLogger.Instance.LogInfo("WORLD", $"{thiefName} stole {stolenAmount}g from {victimName}");
    }

    /// <summary>
    /// Noble/proud NPC publicly challenges a rival. Winner gains confidence, loser loses face.
    /// </summary>
    private void ExecuteChallenge(NPC challenger, NPC target)
    {
        if (challenger.Id == target.Id) return; // Guard against self-challenge
        string challengerName = challenger.Name2 ?? challenger.Name;
        string targetName = target.Name2 ?? target.Name;

        // Compare overall combat power with some randomness
        long challengerPower = challenger.GetAttackPower() + challenger.Level + random.Next(1, 20);
        long targetPower = target.GetAttackPower() + target.Level + random.Next(1, 20);

        bool challengerWins = challengerPower > targetPower;
        var winner = challengerWins ? challenger : target;
        var loser = challengerWins ? target : challenger;
        string winnerName = winner.Name2 ?? winner.Name;
        string loserName = loser.Name2 ?? loser.Name;

        // Winner gains confidence, loser gains sadness
        winner.EmotionalState?.AddEmotion(EmotionType.Confidence, 0.5f, 300);
        winner.EmotionalState?.AddEmotion(EmotionType.Pride, 0.4f, 240);
        loser.EmotionalState?.AddEmotion(EmotionType.Sadness, 0.4f, 180);

        // Record memories — use names (not IDs) so impression system can track them
        winner.Brain?.Memory?.RecordEvent(new MemoryEvent
        {
            Type = MemoryType.Defeated,
            InvolvedCharacter = loserName,
            Description = $"Won a public challenge against {loserName}",
            Importance = 0.7f,
            EmotionalImpact = 0.5f,
            Location = challenger.CurrentLocation
        });
        loser.Brain?.Memory?.RecordEvent(new MemoryEvent
        {
            Type = MemoryType.Defeated,
            InvolvedCharacter = winnerName,
            Description = $"Lost a public challenge to {winnerName}",
            Importance = 0.6f,
            EmotionalImpact = -0.5f,
            Location = challenger.CurrentLocation
        });

        // Challenges deepen rivalries
        if (!winner.Enemies.Contains(loser.Id))
            winner.Enemies.Add(loser.Id);
        if (!loser.Enemies.Contains(winner.Id))
            loser.Enemies.Add(winner.Id);

        // Witnesses observe the challenge (suppress auto-news — we generate our own below)
        SocialInfluenceSystem.RecordWitnesses(npcs, challenger.CurrentLocation, challengerName, targetName, WitnessEventType.SawChallenge, suppressNews: true);

        // Generate news
        NewsSystem.Instance?.Newsy($"{challengerName} publicly challenged {targetName} at the {challenger.CurrentLocation}! {winnerName} emerged victorious.");
        AddGossip($"{winnerName} bested {loserName} in a public challenge");

        UsurperRemake.Systems.DebugLogger.Instance.LogInfo("WORLD", $"Challenge: {challengerName} vs {targetName} - {winnerName} won");
    }
    
    public string GetSimulationStatus()
    {
        if (!isRunning) return "Simulation stopped";

        var aliveNPCs = npcs.Count(npc => npc.IsAlive);
        var gangs = npcs.Where(npc => npc.GangMembers.Count > 0).Count();
        var teams = npcs.Where(n => !string.IsNullOrEmpty(n.Team)).Select(n => n.Team).Distinct().Count();
        var turfController = npcs.FirstOrDefault(n => n.CTurf && !string.IsNullOrEmpty(n.Team))?.Team ?? "None";
        var totalRelationships = npcs.Sum(npc => npc.KnownCharacters.Count);

        return $"Active NPCs: {aliveNPCs}, Teams: {teams}, Turf: {turfController}, Gangs: {gangs}, Relationships: {totalRelationships}";
    }

    /// <summary>
    /// Get list of all active teams with their members
    /// </summary>
    public List<TeamInfo> GetActiveTeams()
    {
        if (npcs == null) return new List<TeamInfo>();

        return npcs
            .Where(n => !string.IsNullOrEmpty(n.Team) && n.IsAlive)
            .GroupBy(n => n.Team)
            .Select(g => new TeamInfo
            {
                TeamName = g.Key,
                MemberCount = g.Count(),
                TotalPower = g.Sum(m => m.Level + m.Strength + m.Defence),
                AverageLevel = (int)g.Average(m => m.Level),
                ControlsTurf = g.Any(m => m.CTurf),
                Members = g.Select(m => m.Name).ToList()
            })
            .OrderByDescending(t => t.TotalPower)
            .ToList();
    }

    /// <summary>
    /// Get teammates for a player's team
    /// </summary>
    public List<NPC> GetPlayerTeammates(string teamName)
    {
        if (npcs == null || string.IsNullOrEmpty(teamName))
            return new List<NPC>();

        return npcs.Where(n => n.Team == teamName && n.IsAlive).ToList();
    }

    // =====================================================================
    // NPC Sleep Cycle System
    // =====================================================================

    private void ProcessNPCSleepCycle()
    {
        try
        {
            _currentTick++; // reuse existing tick counter

            // Wake up NPCs whose sleep duration has expired
            List<string> toWake;
            lock (_sleepLock)
            {
                toWake = _npcSleepStartTick
                    .Where(kvp => _currentTick - kvp.Value >= NPC_SLEEP_DURATION_TICKS)
                    .Select(kvp => kvp.Key)
                    .ToList();
            }
            foreach (var name in toWake)
            {
                lock (_sleepLock)
                {
                    _sleepingNPCs.Remove(name);
                    _npcSleepStartTick.Remove(name);
                }
            }

            // Only rotate sleepers every SLEEP_CYCLE_INTERVAL ticks
            if (_currentTick - _lastSleepCycleTick < SLEEP_CYCLE_INTERVAL) return;
            _lastSleepCycleTick = _currentTick;

            int currentSleeping;
            lock (_sleepLock) { currentSleeping = _sleepingNPCs.Count; }
            if (currentSleeping >= MAX_SLEEPING_NPCS) return;

            var rng = Random.Shared;

            // Pick eligible NPCs to go to sleep
            foreach (var npc in npcs.Where(n => n.IsAlive && !n.IsDead && !n.IsStoryNPC && n.Level >= 3))
            {
                lock (_sleepLock)
                {
                    if (_sleepingNPCs.Count >= MAX_SLEEPING_NPCS) break;
                    if (_sleepingNPCs.ContainsKey(npc.Name2)) continue;
                }

                if (rng.NextDouble() >= NPC_SLEEP_CHANCE) continue;

                // Wealthy/high-level NPCs prefer the inn; poorer ones use dormitory
                string location;
                if (npc.Gold > 500 && npc.Level >= 10 && rng.NextDouble() < 0.6)
                    location = "inn";
                else
                    location = "dormitory";

                lock (_sleepLock)
                {
                    _sleepingNPCs[npc.Name2] = location;
                    _npcSleepStartTick[npc.Name2] = _currentTick;
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SLEEP_CYCLE", $"ProcessNPCSleepCycle failed: {ex.Message}");
        }
    }

    // =====================================================================
    // Sleeping Player Attack System
    // =====================================================================

    private void ProcessNPCAttacksOnSleepers()
    {
        if (SqlBackend == null) return;

        try
        {
            var sleepers = SqlBackend.GetSleepingPlayers().GetAwaiter().GetResult();
            if (sleepers.Count == 0) return;

            foreach (var sleeper in sleepers)
            {
                if (sleeper.IsDead) continue;

                // Roll attack chance per tick — inn sleepers are much safer than dormitory
                float attackChance = sleeper.InnDefenseBoost
                    ? GameConfig.InnSleeperAttackChancePerTick
                    : GameConfig.SleeperAttackChancePerTick;
                if (random.NextDouble() >= attackChance) continue;

                // Pick a random aggressive NPC — only Dark or Evil alignment NPCs will attack sleepers
                // (Good/Holy/Neutral NPCs don't murder people in their sleep)
                // Also skip NPCs on the same team, or who are spouse/lover of the sleeping player
                // Level filter: NPC must be within ±5 levels of sleeper to prevent overleveled NPCs
                // from trivially killing lower-level players through all their guards
                var alignmentSystem = new UsurperRemake.Systems.AlignmentSystem();
                string sleeperTeam = SqlBackend.GetPlayerTeamName(sleeper.Username);
                string sleeperName = sleeper.Username;
                int sleeperLevel = GetSleeperLevel(sleeper.Username);
                int minAttackerLevel = Math.Max(GameConfig.MinNPCLevelForSleeperAttack, sleeperLevel - 5);
                int maxAttackerLevel = sleeperLevel + 5;
                var eligibleNPCs = npcs
                    .Where(n => n.IsAlive && !n.IsDead && n.Level >= minAttackerLevel && n.Level <= maxAttackerLevel && !n.IsStoryNPC
                        && (alignmentSystem.GetAlignment(n) == UsurperRemake.Systems.AlignmentSystem.AlignmentType.Dark
                         || alignmentSystem.GetAlignment(n) == UsurperRemake.Systems.AlignmentSystem.AlignmentType.Evil)
                        && (string.IsNullOrEmpty(sleeperTeam) || !sleeperTeam.Equals(n.Team, StringComparison.OrdinalIgnoreCase))
                        && !n.SpouseName.Equals(sleeperName, StringComparison.OrdinalIgnoreCase)
                        && !RelationshipSystem.IsMarriedOrLover(n.Name2, sleeperName))
                    .ToList();

                if (eligibleNPCs.Count == 0) continue;

                var attackerNPC = eligibleNPCs[random.Next(eligibleNPCs.Count)];

                DebugLogger.Instance.LogInfo("SLEEP", $"NPC {attackerNPC.Name2} (Lvl {attackerNPC.Level}) attacks sleeping player {sleeper.Username}");

                // Create attacker character from NPC stats
                var attacker = new Character
                {
                    Name2 = attackerNPC.Name2,
                    Level = attackerNPC.Level,
                    HP = attackerNPC.HP,
                    MaxHP = attackerNPC.MaxHP,
                    Strength = attackerNPC.Strength,
                    Defence = attackerNPC.Defence,
                    Agility = attackerNPC.Agility,
                    WeapPow = attackerNPC.WeapPow,
                    ArmPow = attackerNPC.ArmPow,
                    Dexterity = attackerNPC.Dexterity,
                    Constitution = attackerNPC.Constitution,
                    AI = CharacterAI.Computer
                };

                var attackLog = new List<string>();
                bool attackRepelled = false;

                // Fight through guards (gauntlet)
                var guards = ParseGuards(sleeper.GuardsJson);
                if (guards.Count > 0)
                {
                    for (int i = 0; i < guards.Count; i++)
                    {
                        var guard = guards[i];
                        var guardChar = HeadlessCombatResolver.CreateGuardCharacter(
                            guard.Type, guard.Hp, sleeperLevel, random);

                        var guardResult = HeadlessCombatResolver.Resolve(attacker, guardChar, random);

                        if (guardResult.Outcome == HeadlessCombatResolver.HeadlessOutcome.DefenderWins ||
                            guardResult.Outcome == HeadlessCombatResolver.HeadlessOutcome.Draw)
                        {
                            // Guard repelled the attacker
                            guard.Hp = Math.Max(0, guard.Hp - (int)guardResult.DamageToDefender);
                            attackLog.Add($"Your {guard.Name} fought off {attackerNPC.Name2}!");
                            attackRepelled = true;
                            break;
                        }
                        else
                        {
                            // Guard defeated
                            attackLog.Add($"Your {guard.Name} was defeated by {attackerNPC.Name2}.");
                            guards.RemoveAt(i);
                            i--;
                            // Attacker HP carries over (already modified in-place)
                        }
                    }

                    // Update guards in DB
                    var updatedGuardsJson = SerializeGuards(guards);
                    SqlBackend.UpdateSleeperGuards(sleeper.Username, updatedGuardsJson).GetAwaiter().GetResult();
                }

                if (attackRepelled)
                {
                    // Log the repelled attack
                    var logEntry = JsonSerializer.Serialize(new
                    {
                        attacker = attackerNPC.Name2,
                        type = "npc",
                        result = "repelled",
                        details = attackLog
                    });
                    SqlBackend.AppendSleepAttackLog(sleeper.Username, logEntry).GetAwaiter().GetResult();
                    continue;
                }

                // Guards defeated (or none) — now attack the sleeping player
                var saveData = SqlBackend.ReadGameData(sleeper.Username).GetAwaiter().GetResult();
                if (saveData?.Player == null) continue;

                var sleeperChar = PlayerCharacterLoader.CreateFromSaveData(saveData.Player, sleeper.Username);

                // Apply Inn defense boost
                if (sleeper.InnDefenseBoost)
                {
                    sleeperChar.Strength = (long)(sleeperChar.Strength * (1.0 + GameConfig.InnDefenseBoost));
                    sleeperChar.Defence = (long)(sleeperChar.Defence * (1.0 + GameConfig.InnDefenseBoost));
                    sleeperChar.WeapPow = (long)(sleeperChar.WeapPow * (1.0 + GameConfig.InnDefenseBoost));
                    sleeperChar.ArmPow = (long)(sleeperChar.ArmPow * (1.0 + GameConfig.InnDefenseBoost));
                }

                var combatResult = HeadlessCombatResolver.Resolve(attacker, sleeperChar, random);

                if (combatResult.Outcome == HeadlessCombatResolver.HeadlessOutcome.AttackerWins)
                {
                    // NPC won — steal gold, steal item, apply XP loss
                    long goldOnHand = saveData.Player.Gold;
                    long stolenGold = (long)(goldOnHand * GameConfig.SleeperGoldTheftPercent);

                    if (stolenGold > 0)
                        SqlBackend.DeductGoldFromPlayer(sleeper.Username, stolenGold).GetAwaiter().GetResult();

                    // Steal 1 random item (from DynamicEquipment)
                    string? stolenItemName = null;
                    if (saveData.Player.DynamicEquipment != null && saveData.Player.DynamicEquipment.Count > 0)
                    {
                        int idx = random.Next(saveData.Player.DynamicEquipment.Count);
                        var stolenItem = saveData.Player.DynamicEquipment[idx];
                        stolenItemName = stolenItem.Name;

                        // Remove from equipped slots
                        if (saveData.Player.EquippedItems != null)
                        {
                            var slotToRemove = saveData.Player.EquippedItems
                                .Where(kvp => kvp.Value == stolenItem.Id)
                                .Select(kvp => kvp.Key)
                                .FirstOrDefault(-1);
                            if (slotToRemove >= 0)
                                saveData.Player.EquippedItems.Remove(slotToRemove);
                        }
                        saveData.Player.DynamicEquipment.RemoveAt(idx);
                    }

                    // Apply XP loss
                    long xpLoss = (long)(saveData.Player.Experience * GameConfig.SleeperXPLossPercent / 100.0);
                    saveData.Player.Experience = Math.Max(0, saveData.Player.Experience - xpLoss);

                    // Write modified save
                    SqlBackend.WriteGameData(sleeper.Username, saveData).GetAwaiter().GetResult();

                    // Mark dead
                    SqlBackend.MarkSleepingPlayerDead(sleeper.Username).GetAwaiter().GetResult();

                    attackLog.Add($"{attackerNPC.Name2} attacked you in your sleep and killed you!");
                    if (stolenGold > 0) attackLog.Add($"They stole {stolenGold:N0} gold.");
                    if (stolenItemName != null) attackLog.Add($"They took your {stolenItemName}.");
                    if (xpLoss > 0) attackLog.Add($"You lost {xpLoss:N0} experience.");

                    // Log + message
                    var logEntry = JsonSerializer.Serialize(new
                    {
                        attacker = attackerNPC.Name2,
                        type = "npc",
                        result = "killed",
                        goldStolen = stolenGold,
                        itemStolen = stolenItemName ?? "nothing",
                        xpLost = xpLoss,
                        details = attackLog
                    });
                    SqlBackend.AppendSleepAttackLog(sleeper.Username, logEntry).GetAwaiter().GetResult();
                    SqlBackend.SendMessage(attackerNPC.Name2, sleeper.Username, "sleep_attack",
                        $"{attackerNPC.Name2} murdered you in your sleep! Lost {stolenGold:N0} gold{(stolenItemName != null ? $" and {stolenItemName}" : "")}.").GetAwaiter().GetResult();

                    DebugLogger.Instance.LogInfo("SLEEP", $"NPC {attackerNPC.Name2} killed sleeping {sleeper.Username}, stole {stolenGold}g + {stolenItemName ?? "nothing"}");
                }
                else
                {
                    // Sleeping player fought off the attacker
                    attackerNPC.HP = Math.Max(1, attackerNPC.HP - combatResult.DamageToAttacker);
                    attackLog.Add($"You fought off {attackerNPC.Name2} in your sleep!");

                    var logEntry = JsonSerializer.Serialize(new
                    {
                        attacker = attackerNPC.Name2,
                        type = "npc",
                        result = "repelled",
                        details = attackLog
                    });
                    SqlBackend.AppendSleepAttackLog(sleeper.Username, logEntry).GetAwaiter().GetResult();
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SLEEP", $"ProcessNPCAttacksOnSleepers failed: {ex.Message}");
        }
    }

    private int GetSleeperLevel(string username)
    {
        try
        {
            var saveData = SqlBackend?.ReadGameData(username).GetAwaiter().GetResult();
            return saveData?.Player?.Level ?? 10;
        }
        catch { return 10; }
    }

    private record GuardData(string Type, string Name, int Hp, int MaxHp)
    {
        public int Hp { get; set; } = Hp;
    }

    private List<GuardData> ParseGuards(string json)
    {
        try
        {
            if (string.IsNullOrEmpty(json) || json == "[]") return new List<GuardData>();
            using var doc = JsonDocument.Parse(json);
            var guards = new List<GuardData>();
            foreach (var elem in doc.RootElement.EnumerateArray())
            {
                guards.Add(new GuardData(
                    elem.GetProperty("type").GetString() ?? "rookie_npc",
                    GetGuardName(elem.GetProperty("type").GetString() ?? "rookie_npc"),
                    elem.GetProperty("hp").GetInt32(),
                    elem.GetProperty("maxHp").GetInt32()
                ));
            }
            return guards;
        }
        catch { return new List<GuardData>(); }
    }

    private string SerializeGuards(List<GuardData> guards)
    {
        var list = guards.Select(g => new { type = g.Type, hp = g.Hp, maxHp = g.MaxHp }).ToList();
        return JsonSerializer.Serialize(list);
    }

    private static string GetGuardName(string type) => type switch
    {
        "rookie_npc" => "Rookie Guard",
        "veteran_npc" => "Veteran Guard",
        "elite_npc" => "Elite Guard",
        "hound" => "Guard Hound",
        "troll" => "Guard Troll",
        "drake" => "Guard Drake",
        _ => "Guard"
    };
}

/// <summary>
/// Team information for display and queries
/// </summary>
public class TeamInfo
{
    public string TeamName { get; set; } = "";
    public int MemberCount { get; set; }
    public long TotalPower { get; set; }
    public int AverageLevel { get; set; }
    public bool ControlsTurf { get; set; }
    public List<string> Members { get; set; } = new();
} 
