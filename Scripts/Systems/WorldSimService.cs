using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Headless 24/7 world simulator service.
    /// Runs NPC AI, dungeon exploration, leveling, shopping, social dynamics
    /// without any interactive terminal or player session.
    /// State is periodically persisted to the shared SQLite database.
    /// </summary>
    public class WorldSimService
    {
        private readonly SqlSaveBackend sqlBackend;
        private readonly int simIntervalSeconds;
        private readonly float npcXpMultiplier;
        private readonly int saveIntervalMinutes;

        private WorldSimulator? worldSimulator;
        private DateTime lastSaveTime = DateTime.MinValue;
        private DateTime _lastWorldDailyReset = DateTime.MinValue;  // 7 PM ET world-level daily reset
        private long lastNpcVersion = 0;  // Track world_state NPC version to detect player changes
        private long lastRoyalCourtVersion = 0;  // Track royal_court version to detect player changes (treasury, taxes, etc.)
        private string? _lastNpcJsonHash;  // Dirty-check: skip NPC save when nothing changed

        // Heartbeat support for embedded worldsim (database-level leader election)
        private string? _heartbeatOwnerId;

        /// <summary>
        /// Signals when initialization (systems + world state load) is complete.
        /// Used by embedded mode so the player session waits until NPCs are ready.
        /// </summary>
        public TaskCompletionSource<bool> InitializationComplete { get; } = new();

        private readonly JsonSerializerOptions jsonOptions = new()
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            IncludeFields = true
        };

        public WorldSimService(
            SqlSaveBackend backend,
            int simIntervalSeconds = 60,
            float npcXpMultiplier = 0.25f,
            int saveIntervalMinutes = 5,
            string? heartbeatOwnerId = null)
        {
            this.sqlBackend = backend;
            this.simIntervalSeconds = simIntervalSeconds;
            this.npcXpMultiplier = npcXpMultiplier;
            this.saveIntervalMinutes = saveIntervalMinutes;
            this._heartbeatOwnerId = heartbeatOwnerId;
        }

        /// <summary>
        /// Run the world simulator in a loop until cancellation is requested.
        /// </summary>
        public async Task RunAsync(CancellationToken cancellationToken)
        {
            DebugLogger.Instance.LogInfo("WORLDSIM", $"Starting persistent world simulator");
            DebugLogger.Instance.LogInfo("WORLDSIM", $"Sim interval: {simIntervalSeconds}s, NPC XP: {npcXpMultiplier:F2}x, Save interval: {saveIntervalMinutes}min");

            // Phase 1: Initialize minimal systems
            InitializeSystems();

            // Phase 2: Load NPC state from database
            await LoadWorldState();

            // Phase 2.5: Load king/royal court state from world_state (the authoritative source)
            LoadRoyalCourtFromWorldState();

            // Phase 2.6: Load children, marriages, and world events from world_state
            // These must survive world sim restarts (previously lost on restart)
            LoadChildrenState();
            LoadMarriageRegistryState();
            LoadWorldEventsState();

            // Load last world daily reset time from world_state
            LoadLastWorldDailyReset();

            // Load player team names so WorldSimulator can protect them from NPC AI
            await LoadPlayerTeamNames();

            // Track initial versions so we can detect player modifications
            lastNpcVersion = sqlBackend.GetWorldStateVersion(OnlineStateManager.KEY_NPCS);
            lastRoyalCourtVersion = sqlBackend.GetWorldStateVersion("royal_court");
            DebugLogger.Instance.LogInfo("WORLDSIM", $"Initial versions - NPC: {lastNpcVersion}, Royal court: {lastRoyalCourtVersion}");

            // Phase 3: Set the NPC XP multiplier
            WorldSimulator.NpcXpMultiplier = npcXpMultiplier;

            // Phase 4: Run simulation loop
            lastSaveTime = DateTime.UtcNow;

            var aliveCount = NPCSpawnSystem.Instance.ActiveNPCs.Count(n => n.IsAlive && !n.IsDead);
            DebugLogger.Instance.LogInfo("WORLDSIM", $"Simulation running. NPCs: {aliveCount} alive / {NPCSpawnSystem.Instance.ActiveNPCs.Count} total");

            // Signal that initialization is complete (for embedded mode)
            InitializationComplete.TrySetResult(true);

            try
            {
                int tickCount = 0;
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        // Run one simulation tick
                        worldSimulator?.SimulateStep();
                        tickCount++;

                        // Update heartbeat (embedded mode leader election)
                        if (_heartbeatOwnerId != null)
                        {
                            sqlBackend.UpdateWorldSimHeartbeat(_heartbeatOwnerId);
                        }

                        // Check for 7 PM ET world daily reset
                        CheckWorldDailyReset();

                        // Check world boss spawn conditions
                        await WorldBossSystem.Instance.CheckSpawnConditions(sqlBackend);

                        // Log status every 10 ticks
                        if (tickCount % 10 == 0)
                        {
                            var alive = NPCSpawnSystem.Instance.ActiveNPCs.Count(n => n.IsAlive && !n.IsDead);
                            var dead = NPCSpawnSystem.Instance.ActiveNPCs.Count(n => !n.IsAlive || n.IsDead);
                            DebugLogger.Instance.LogDebug("WORLDSIM", $"Tick {tickCount}: {alive} alive, {dead} dead NPCs");
                        }

                        // Check if it's time to persist state
                        if ((DateTime.UtcNow - lastSaveTime).TotalMinutes >= saveIntervalMinutes)
                        {
                            await SaveWorldState();
                            lastSaveTime = DateTime.UtcNow;
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Instance.LogError("WORLDSIM", $"Simulation step error: {ex.Message}\n{ex.StackTrace}");
                    }

                    await Task.Delay(TimeSpan.FromSeconds(simIntervalSeconds), cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown
            }
            finally
            {
                // Graceful shutdown: save state one final time
                DebugLogger.Instance.LogInfo("WORLDSIM", "Shutting down - saving final state...");
                await SaveWorldState();

                // Release worldsim lock (embedded mode)
                if (_heartbeatOwnerId != null)
                {
                    sqlBackend.ReleaseWorldSimLock(_heartbeatOwnerId);
                    DebugLogger.Instance.LogInfo("WORLDSIM", "Released worldsim lock");
                }

                // Clear database callback
                NewsSystem.DatabaseCallback = null;

                // Signal initialization complete in case we're shutting down before init finished
                InitializationComplete.TrySetResult(false);

                DebugLogger.Instance.LogInfo("WORLDSIM", "Final state saved. Goodbye.");
            }
        }

        /// <summary>
        /// Initialize only the systems needed for headless simulation.
        /// Skips: TerminalEmulator, LocationManager, UI systems, auth, player tracking.
        /// </summary>
        private void InitializeSystems()
        {
            DebugLogger.Instance.LogInfo("WORLDSIM", "Initializing minimal systems...");

            // Initialize save system with SQL backend
            SaveSystem.InitializeWithBackend(sqlBackend);

            // Initialize static data systems
            EquipmentDatabase.Initialize();

            // Ensure NPC spawn system singleton exists
            _ = NPCSpawnSystem.Instance;

            // Ensure news system singleton exists (for NPC activity news)
            _ = NewsSystem.Instance;

            // Route NPC news to database for website activity feed ("The Living World")
            NewsSystem.DatabaseCallback = (message) =>
            {
                try
                {
                    _ = sqlBackend.AddNews(message, "npc", null!);
                }
                catch { /* fail silently - don't crash sim for logging */ }
            };
            DebugLogger.Instance.LogInfo("WORLDSIM", "NPC activity feed wired to database");

            // Create WorldSimulator but do NOT start its internal background loop.
            // We drive SimulateStep() directly in our controlled loop.
            worldSimulator = new WorldSimulator();
            worldSimulator.SetActive(true);

            // Give the world sim and challenge system access to the SQL backend
            WorldSimulator.SqlBackend = sqlBackend;
            ChallengeSystem.Instance.SqlBackend = sqlBackend;

            DebugLogger.Instance.LogInfo("WORLDSIM", "Minimal systems initialized for headless simulation");
        }

        /// <summary>
        /// Load NPC state from the shared world_state table.
        /// If no state exists, initialize fresh NPCs from templates.
        /// </summary>
        private async Task LoadWorldState()
        {
            try
            {
                var npcJson = await sqlBackend.LoadWorldState(OnlineStateManager.KEY_NPCS);
                if (!string.IsNullOrEmpty(npcJson))
                {
                    var npcData = JsonSerializer.Deserialize<List<NPCData>>(npcJson, jsonOptions);
                    if (npcData != null && npcData.Count > 0)
                    {
                        RestoreNPCsFromData(npcData);
                        DebugLogger.Instance.LogInfo("WORLDSIM", $"Loaded {npcData.Count} NPCs from database");

                        // Process dead NPCs for respawn
                        worldSimulator?.ProcessDeadNPCsOnLoad();
                        return;
                    }
                }

                // No existing state -- initialize fresh NPCs from templates
                DebugLogger.Instance.LogInfo("WORLDSIM", "No existing NPC state found. Initializing fresh NPCs...");
                await NPCSpawnSystem.Instance.InitializeClassicNPCs();
                DebugLogger.Instance.LogInfo("WORLDSIM", $"Initialized {NPCSpawnSystem.Instance.ActiveNPCs.Count} fresh NPCs");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("WORLDSIM", $"Failed to load world state: {ex.Message}. Initializing fresh.");
                await NPCSpawnSystem.Instance.ForceReinitializeNPCs();
            }
        }

        /// <summary>
        /// Persist current NPC state to the shared database.
        /// Before saving, checks if a player session has modified the NPC data since our last save.
        /// If so, reloads from DB first to pick up player changes (prevents divergence).
        /// </summary>
        private async Task SaveWorldState()
        {
            try
            {
                // Check if NPC data was modified by a player session since our last save.
                // Both game server and world sim write to the same world_state key.
                // The version auto-increments on each write, so if it changed, a player saved.
                long currentNpcVersion = sqlBackend.GetWorldStateVersion(OnlineStateManager.KEY_NPCS);
                if (currentNpcVersion > lastNpcVersion && lastNpcVersion > 0)
                {
                    DebugLogger.Instance.LogInfo("WORLDSIM", $"NPC data modified by game server (v{lastNpcVersion} → v{currentNpcVersion}). Reloading to pick up player changes...");

                    // Capture world-sim-managed state BEFORE reloading from DB.
                    // Player sessions write stale NPC data that may overwrite active pregnancies,
                    // marriage state changes, or other world-sim-driven mutations.
                    var activePregnancies = new Dictionary<string, DateTime>();
                    foreach (var npc in NPCSpawnSystem.Instance.ActiveNPCs)
                    {
                        if (npc.PregnancyDueDate.HasValue)
                        {
                            var key = npc.Name2 ?? npc.Name1;
                            activePregnancies[key] = npc.PregnancyDueDate.Value;
                        }
                    }

                    await LoadWorldState();
                    _lastNpcJsonHash = null; // Invalidate hash so next save always writes after reload+merge

                    // Merge back pregnancies that the player session overwrote with stale data
                    if (activePregnancies.Count > 0)
                    {
                        int restored = 0;
                        foreach (var npc in NPCSpawnSystem.Instance.ActiveNPCs)
                        {
                            var key = npc.Name2 ?? npc.Name1;
                            if (!npc.PregnancyDueDate.HasValue && activePregnancies.TryGetValue(key, out var dueDate))
                            {
                                // Only restore if the due date hasn't already passed
                                // (if it passed, the pregnancy should have been processed before the reload)
                                npc.PregnancyDueDate = dueDate;
                                restored++;
                            }
                        }
                        if (restored > 0)
                        {
                            DebugLogger.Instance.LogInfo("WORLDSIM", $"Restored {restored} pregnancies after player-triggered reload");
                        }
                    }

                    // Reload royal court too (NPC changes often accompany king changes)
                    LoadRoyalCourtFromWorldState();
                    lastRoyalCourtVersion = sqlBackend.GetWorldStateVersion("royal_court");
                }

                // Check if royal court was modified by a player session since our last save.
                // This catches changes that ONLY affect royal_court without changing NPC data:
                // treasury deposits, tax policy changes, guard salary payments, etc.
                long currentRoyalCourtVersion = sqlBackend.GetWorldStateVersion("royal_court");
                if (currentRoyalCourtVersion > lastRoyalCourtVersion && lastRoyalCourtVersion > 0)
                {
                    DebugLogger.Instance.LogInfo("WORLDSIM", $"Royal court modified by player (v{lastRoyalCourtVersion} → v{currentRoyalCourtVersion}). Reloading...");
                    LoadRoyalCourtFromWorldState();
                    lastRoyalCourtVersion = currentRoyalCourtVersion;
                }

                // Save our NPC state (either fresh from reload or accumulated simulation changes)
                // Dirty-check: hash the serialized JSON and skip the DB write if nothing changed.
                // The NPC blob is ~18 MB, so avoiding unnecessary writes saves significant I/O.
                var npcData = OnlineStateManager.SerializeCurrentNPCs();
                var json = JsonSerializer.Serialize(npcData, jsonOptions);

                // Use a fast hash to detect changes (SHA256 of the JSON string)
                string jsonHash;
                using (var sha = System.Security.Cryptography.SHA256.Create())
                {
                    var hashBytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(json));
                    jsonHash = Convert.ToHexString(hashBytes);
                }

                var aliveCount = NPCSpawnSystem.Instance.ActiveNPCs.Count(n => n.IsAlive && !n.IsDead);

                if (jsonHash != _lastNpcJsonHash)
                {
                    await sqlBackend.SaveWorldState(OnlineStateManager.KEY_NPCS, json);
                    _lastNpcJsonHash = jsonHash;

                    // Track our save's version for the next cycle's comparison
                    lastNpcVersion = sqlBackend.GetWorldStateVersion(OnlineStateManager.KEY_NPCS);

                    DebugLogger.Instance.LogInfo("WORLDSIM", $"State saved (v{lastNpcVersion}): {aliveCount} alive NPCs at {DateTime.UtcNow:HH:mm:ss}");
                }
                else
                {
                    DebugLogger.Instance.LogDebug("WORLDSIM", $"NPC state unchanged, skipping write: {aliveCount} alive NPCs at {DateTime.UtcNow:HH:mm:ss}");
                }

                // Save royal court to world_state (authoritative - world sim maintains this)
                await SaveRoyalCourtToWorldState();
                lastRoyalCourtVersion = sqlBackend.GetWorldStateVersion("royal_court");

                // Save economy summary for the dashboard
                await SaveEconomyState();

                // Save children data (dashboard + raw for round-trip)
                await SaveChildrenState();

                // Save NPC marriage registry (survives world sim restart)
                await SaveMarriageRegistryState();

                // Save world events (plagues, festivals, wars, etc.)
                await SaveWorldEventsState();

                // Prune old news across ALL categories (48-hour window, 500 entry global cap)
                await sqlBackend.PruneAllNews(hoursToKeep: 48, maxTotal: 500);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("WORLDSIM", $"Failed to save world state: {ex.Message}");
            }
        }

        /// <summary>
        /// Load royal court state from world_state.
        /// The world_state 'royal_court' key is the single source of truth for king identity,
        /// treasury, tax rates, court members, etc. Both the world sim and player sessions
        /// read/write this key. The world sim is the primary maintainer; player actions
        /// (throne challenges, tax changes) write updates to this key immediately.
        /// </summary>
        private void LoadRoyalCourtFromWorldState()
        {
            try
            {
                var json = sqlBackend.LoadWorldState("royal_court").GetAwaiter().GetResult();
                if (string.IsNullOrEmpty(json)) return;

                var royalCourt = JsonSerializer.Deserialize<RoyalCourtSaveData>(json, jsonOptions);
                if (royalCourt == null || string.IsNullOrEmpty(royalCourt.KingName)) return;

                var king = CastleLocation.GetCurrentKing();

                // Check if king identity changed (player took the throne, or NPC challenge)
                if (king == null || king.Name != royalCourt.KingName)
                {
                    // Create king directly from saved data — don't use SetCurrentKing
                    // which creates a fresh King with default treasury/empty guards
                    king = new King
                    {
                        Name = royalCourt.KingName,
                        AI = (CharacterAI)royalCourt.KingAI,
                        Sex = (CharacterSex)royalCourt.KingSex,
                        IsActive = true
                    };
                    CastleLocation.SetKing(king);
                    DebugLogger.Instance.LogInfo("WORLDSIM", $"King loaded from world_state: {royalCourt.KingName}");
                }

                if (king != null)
                {
                    // Restore financial/political state from world_state (authoritative)
                    king.Treasury = royalCourt.Treasury;
                    king.TaxRate = royalCourt.TaxRate;
                    king.TotalReign = royalCourt.TotalReign;
                    king.KingTaxPercent = royalCourt.KingTaxPercent > 0 ? royalCourt.KingTaxPercent : 5;
                    king.CityTaxPercent = royalCourt.CityTaxPercent > 0 ? royalCourt.CityTaxPercent : 2;

                    // Restore coronation date and tax alignment
                    if (!string.IsNullOrEmpty(royalCourt.CoronationDate))
                    {
                        if (DateTime.TryParse(royalCourt.CoronationDate, null, System.Globalization.DateTimeStyles.RoundtripKind, out var coronation))
                            king.CoronationDate = coronation;
                    }
                    king.TaxAlignment = (GameConfig.TaxAlignment)royalCourt.TaxAlignment;

                    // Restore monarch history
                    if (royalCourt.MonarchHistory != null && royalCourt.MonarchHistory.Count > 0)
                    {
                        var history = royalCourt.MonarchHistory.Select(m => new MonarchRecord
                        {
                            Name = m.Name,
                            Title = m.Title,
                            DaysReigned = m.DaysReigned,
                            CoronationDate = DateTime.TryParse(m.CoronationDate, null, System.Globalization.DateTimeStyles.RoundtripKind, out var cd) ? cd : DateTime.Now,
                            EndReason = m.EndReason
                        }).ToList();
                        CastleLocation.SetMonarchHistory(history);
                    }

                    // Restore court members
                    if (royalCourt.CourtMembers != null)
                    {
                        king.CourtMembers = royalCourt.CourtMembers.Select(m => new CourtMember
                        {
                            Name = m.Name,
                            Faction = (CourtFaction)m.Faction,
                            Influence = m.Influence,
                            LoyaltyToKing = m.LoyaltyToKing,
                            Role = m.Role,
                            IsPlotting = m.IsPlotting
                        }).ToList();
                    }

                    // Restore heirs
                    if (royalCourt.Heirs != null)
                    {
                        king.Heirs = royalCourt.Heirs.Select(h => new RoyalHeir
                        {
                            Name = h.Name,
                            Age = h.Age,
                            ClaimStrength = h.ClaimStrength,
                            ParentName = h.ParentName,
                            Sex = (CharacterSex)h.Sex,
                            IsDesignated = h.IsDesignated
                        }).ToList();
                    }

                    // Restore spouse
                    if (royalCourt.Spouse != null)
                    {
                        king.Spouse = new RoyalSpouse
                        {
                            Name = royalCourt.Spouse.Name,
                            Sex = (CharacterSex)royalCourt.Spouse.Sex,
                            OriginalFaction = (CourtFaction)royalCourt.Spouse.OriginalFaction,
                            Dowry = royalCourt.Spouse.Dowry,
                            Happiness = royalCourt.Spouse.Happiness
                        };
                    }
                    else
                    {
                        king.Spouse = null; // Ensure old spouse doesn't carry over
                    }

                    // Restore plots
                    if (royalCourt.ActivePlots != null)
                    {
                        king.ActivePlots = royalCourt.ActivePlots.Select(p => new CourtIntrigue
                        {
                            PlotType = p.PlotType,
                            Conspirators = p.Conspirators ?? new List<string>(),
                            Target = p.Target,
                            Progress = p.Progress,
                            IsDiscovered = p.IsDiscovered
                        }).ToList();
                    }

                    king.DesignatedHeir = royalCourt.DesignatedHeir;

                    // Restore guards
                    if (royalCourt.Guards != null && royalCourt.Guards.Count > 0)
                    {
                        king.Guards = royalCourt.Guards.Select(g => new RoyalGuard
                        {
                            Name = g.Name,
                            AI = (CharacterAI)g.AI,
                            Sex = (CharacterSex)g.Sex,
                            DailySalary = g.DailySalary,
                            Loyalty = g.Loyalty,
                            IsActive = g.IsActive
                        }).ToList();
                    }

                    // Restore monster guards
                    if (royalCourt.MonsterGuards != null && royalCourt.MonsterGuards.Count > 0)
                    {
                        king.MonsterGuards = royalCourt.MonsterGuards.Select(m => new MonsterGuard
                        {
                            Name = m.Name,
                            Level = m.Level,
                            HP = m.HP,
                            MaxHP = m.MaxHP,
                            Strength = m.Strength,
                            Defence = m.Defence,
                            WeapPow = m.WeapPow,
                            ArmPow = m.ArmPow,
                            MonsterType = m.MonsterType,
                            PurchaseCost = m.PurchaseCost,
                            DailyFeedingCost = m.DailyFeedingCost
                        }).ToList();
                    }

                    // Phase 2 — restore previously unserialized fields
                    if (royalCourt.Prisoners != null && royalCourt.Prisoners.Count > 0)
                    {
                        king.Prisoners = royalCourt.Prisoners.ToDictionary(
                            p => p.CharacterName,
                            p => new PrisonRecord
                            {
                                CharacterName = p.CharacterName,
                                Crime = p.Crime,
                                Sentence = p.Sentence,
                                DaysServed = p.DaysServed,
                                ImprisonmentDate = DateTime.TryParse(p.ImprisonmentDate, out var impDate) ? impDate : DateTime.Now,
                                BailAmount = p.BailAmount
                            });
                    }

                    if (royalCourt.Orphans != null && royalCourt.Orphans.Count > 0)
                    {
                        king.Orphans = royalCourt.Orphans.Select(o => new RoyalOrphan
                        {
                            Name = o.Name,
                            Age = o.Age,
                            Sex = (CharacterSex)o.Sex,
                            ArrivalDate = DateTime.TryParse(o.ArrivalDate, out var arrDate) ? arrDate : DateTime.Now,
                            BackgroundStory = o.BackgroundStory,
                            Happiness = o.Happiness,
                            MotherName = o.MotherName,
                            FatherName = o.FatherName,
                            MotherID = o.MotherID,
                            FatherID = o.FatherID,
                            Race = (CharacterRace)o.Race,
                            BirthDate = DateTime.TryParse(o.BirthDate, out var bd) ? bd : DateTime.Now,
                            Soul = o.Soul,
                            IsRealOrphan = o.IsRealOrphan
                        }).ToList();
                    }

                    king.MagicBudget = royalCourt.MagicBudget;

                    if (royalCourt.EstablishmentStatus != null && royalCourt.EstablishmentStatus.Count > 0)
                        king.EstablishmentStatus = new Dictionary<string, bool>(royalCourt.EstablishmentStatus);

                    if (!string.IsNullOrEmpty(royalCourt.LastProclamation))
                        king.LastProclamation = royalCourt.LastProclamation;

                    if (!string.IsNullOrEmpty(royalCourt.LastProclamationDate) &&
                        DateTime.TryParse(royalCourt.LastProclamationDate, out var procDate))
                        king.LastProclamationDate = procDate;

                    DebugLogger.Instance.LogDebug("WORLDSIM", $"Royal court loaded: King {king.Name}, Treasury {king.Treasury:N0}, Guards {king.Guards.Count}, Monsters {king.MonsterGuards.Count}");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("WORLDSIM", $"Failed to load royal court from world_state: {ex.Message}");
            }
        }

        /// <summary>
        /// Save current royal court state to world_state.
        /// This is the authoritative write - the world sim maintains this data.
        /// </summary>
        private async Task SaveRoyalCourtToWorldState()
        {
            try
            {
                var king = CastleLocation.GetCurrentKing();
                if (king == null) return;

                var data = new RoyalCourtSaveData
                {
                    KingName = king.Name,
                    Treasury = king.Treasury,
                    TaxRate = king.TaxRate,
                    TotalReign = king.TotalReign,
                    KingTaxPercent = king.KingTaxPercent,
                    CityTaxPercent = king.CityTaxPercent,
                    DesignatedHeir = king.DesignatedHeir ?? "",
                    KingAI = (int)king.AI,
                    KingSex = (int)king.Sex,
                    CoronationDate = king.CoronationDate.ToString("o"),
                    TaxAlignment = (int)king.TaxAlignment,
                    MonarchHistory = CastleLocation.GetMonarchHistory()?.Select(m => new MonarchRecordSaveData
                    {
                        Name = m.Name,
                        Title = m.Title,
                        DaysReigned = m.DaysReigned,
                        CoronationDate = m.CoronationDate.ToString("o"),
                        EndReason = m.EndReason
                    }).ToList() ?? new List<MonarchRecordSaveData>(),
                    CourtMembers = king.CourtMembers?.Select(m => new CourtMemberSaveData
                    {
                        Name = m.Name,
                        Faction = (int)m.Faction,
                        Influence = m.Influence,
                        LoyaltyToKing = m.LoyaltyToKing,
                        Role = m.Role,
                        IsPlotting = m.IsPlotting
                    }).ToList() ?? new List<CourtMemberSaveData>(),
                    Heirs = king.Heirs?.Select(h => new RoyalHeirSaveData
                    {
                        Name = h.Name,
                        Age = h.Age,
                        ClaimStrength = h.ClaimStrength,
                        ParentName = h.ParentName,
                        Sex = (int)h.Sex,
                        IsDesignated = h.IsDesignated
                    }).ToList() ?? new List<RoyalHeirSaveData>(),
                    Spouse = king.Spouse != null ? new RoyalSpouseSaveData
                    {
                        Name = king.Spouse.Name,
                        Sex = (int)king.Spouse.Sex,
                        OriginalFaction = (int)king.Spouse.OriginalFaction,
                        Dowry = king.Spouse.Dowry,
                        Happiness = king.Spouse.Happiness
                    } : null,
                    ActivePlots = king.ActivePlots?.Select(p => new CourtIntrigueSaveData
                    {
                        PlotType = p.PlotType,
                        Conspirators = p.Conspirators,
                        Target = p.Target,
                        Progress = p.Progress,
                        IsDiscovered = p.IsDiscovered
                    }).ToList() ?? new List<CourtIntrigueSaveData>(),
                    Guards = king.Guards?.Select(g => new RoyalGuardSaveData
                    {
                        Name = g.Name,
                        AI = (int)g.AI,
                        Sex = (int)g.Sex,
                        DailySalary = g.DailySalary,
                        Loyalty = g.Loyalty,
                        IsActive = g.IsActive
                    }).ToList() ?? new List<RoyalGuardSaveData>(),
                    MonsterGuards = king.MonsterGuards?.Select(m => new MonsterGuardSaveData
                    {
                        Name = m.Name,
                        Level = m.Level,
                        HP = m.HP,
                        MaxHP = m.MaxHP,
                        Strength = m.Strength,
                        Defence = m.Defence,
                        WeapPow = m.WeapPow,
                        ArmPow = m.ArmPow,
                        MonsterType = m.MonsterType,
                        PurchaseCost = m.PurchaseCost,
                        DailyFeedingCost = m.DailyFeedingCost
                    }).ToList() ?? new List<MonsterGuardSaveData>(),

                    // Phase 2 — previously unserialized fields
                    Prisoners = king.Prisoners?.Select(kvp => new PrisonRecordSaveData
                    {
                        CharacterName = kvp.Value.CharacterName,
                        Crime = kvp.Value.Crime,
                        Sentence = kvp.Value.Sentence,
                        DaysServed = kvp.Value.DaysServed,
                        ImprisonmentDate = kvp.Value.ImprisonmentDate.ToString("o"),
                        BailAmount = kvp.Value.BailAmount
                    }).ToList() ?? new List<PrisonRecordSaveData>(),
                    Orphans = king.Orphans?.Select(o => new RoyalOrphanSaveData
                    {
                        Name = o.Name,
                        Age = o.Age,
                        Sex = (int)o.Sex,
                        ArrivalDate = o.ArrivalDate.ToString("o"),
                        BackgroundStory = o.BackgroundStory,
                        Happiness = o.Happiness,
                        MotherName = o.MotherName,
                        FatherName = o.FatherName,
                        MotherID = o.MotherID,
                        FatherID = o.FatherID,
                        Race = (int)o.Race,
                        BirthDate = o.BirthDate.ToString("o"),
                        Soul = o.Soul,
                        IsRealOrphan = o.IsRealOrphan
                    }).ToList() ?? new List<RoyalOrphanSaveData>(),
                    MagicBudget = king.MagicBudget,
                    EstablishmentStatus = king.EstablishmentStatus ?? new Dictionary<string, bool>(),
                    LastProclamation = king.LastProclamation ?? "",
                    LastProclamationDate = king.LastProclamationDate != DateTime.MinValue
                        ? king.LastProclamationDate.ToString("o") : ""
                };

                var json = JsonSerializer.Serialize(data, jsonOptions);
                await sqlBackend.SaveWorldState("royal_court", json);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("WORLDSIM", $"Failed to save royal court to world_state: {ex.Message}");
            }
        }

        /// <summary>
        /// Save economy/tax data to the shared database for the dashboard to read.
        /// The world sim runs headlessly (no player session), so it can't see the player.
        /// If the game server previously wrote a player as city control leader, preserve
        /// that info as long as the controlling team hasn't changed.
        /// </summary>
        private async Task SaveEconomyState()
        {
            try
            {
                var king = CastleLocation.GetCurrentKing();
                var cityInfo = CityControlSystem.Instance.GetCityControlInfo();
                var leader = CityControlSystem.Instance.GetCityControlLeader();

                // World sim can't see the player, so leader.IsPlayer will always be false here.
                // Check if the game server previously wrote economy data with a player leader.
                // If the controlling team matches, preserve the player leader info and member count.
                string leaderName = leader.Name;
                long leaderBank = leader.BankGold;
                bool leaderIsPlayer = leader.IsPlayer;
                int memberCount = cityInfo.MemberCount;
                long totalPower = cityInfo.TotalPower;

                if (!leaderIsPlayer)
                {
                    try
                    {
                        var existingJson = await sqlBackend.LoadWorldState("economy");
                        if (!string.IsNullOrEmpty(existingJson))
                        {
                            var existing = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(existingJson);
                            if (existing != null &&
                                existing.TryGetValue("cityControlLeaderIsPlayer", out var isPlayerEl) &&
                                isPlayerEl.ValueKind == JsonValueKind.True &&
                                existing.TryGetValue("cityControlTeam", out var teamEl) &&
                                !string.IsNullOrEmpty(teamEl.GetString()))
                            {
                                // Game server wrote a player as leader — preserve their turf control
                                // even if the world sim can't see the team (player-only team, player offline)
                                var playerTeamName = teamEl.GetString()!;
                                if (existing.TryGetValue("cityControlLeader", out var nameEl))
                                    leaderName = nameEl.GetString() ?? leaderName;
                                if (existing.TryGetValue("cityControlLeaderBank", out var bankEl))
                                    leaderBank = bankEl.GetInt64();
                                if (existing.TryGetValue("cityControlMembers", out var membersEl))
                                    memberCount = membersEl.GetInt32();
                                if (existing.TryGetValue("cityControlPower", out var powerEl))
                                    totalPower = powerEl.GetInt64();
                                leaderIsPlayer = true;
                                // Override cityInfo.TeamName since the world sim might report "None"
                                // for a player-only team
                                cityInfo = (playerTeamName, memberCount, totalPower);
                            }
                        }
                    }
                    catch { /* ignore parse errors, fall through to NPC-only data */ }
                }

                // Tell the WorldSimulator about player turf control so it doesn't get overwritten
                if (leaderIsPlayer && WorldSimulator.Instance != null)
                    WorldSimulator.Instance.PlayerTurfTeam = cityInfo.TeamName;
                else if (WorldSimulator.Instance != null)
                    WorldSimulator.Instance.PlayerTurfTeam = null;

                var economyData = new Dictionary<string, object?>
                {
                    ["kingName"] = king?.Name ?? "None",
                    ["kingIsActive"] = king?.IsActive ?? false,
                    ["treasury"] = king?.Treasury ?? 0,
                    ["taxRate"] = king?.TaxRate ?? 0,
                    ["kingTaxPercent"] = king?.KingTaxPercent ?? 0,
                    ["cityTaxPercent"] = king?.CityTaxPercent ?? 0,
                    ["dailyTaxRevenue"] = king?.DailyTaxRevenue ?? 0,
                    ["dailyCityTaxRevenue"] = king?.DailyCityTaxRevenue ?? 0,
                    ["dailyIncome"] = king?.CalculateDailyIncome() ?? 0,
                    ["dailyExpenses"] = king?.CalculateDailyExpenses() ?? 0,
                    ["cityControlTeam"] = cityInfo.TeamName,
                    ["cityControlMembers"] = memberCount,
                    ["cityControlPower"] = totalPower,
                    ["cityControlLeader"] = leaderName,
                    ["cityControlLeaderBank"] = leaderBank,
                    ["cityControlLeaderIsPlayer"] = leaderIsPlayer,
                    ["updatedAt"] = DateTime.UtcNow.ToString("o")
                };

                var json = JsonSerializer.Serialize(economyData, jsonOptions);
                await sqlBackend.SaveWorldState("economy", json);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("WORLDSIM", $"Failed to save economy state: {ex.Message}");
            }
        }

        /// <summary>
        /// Save children data to the shared database.
        /// Includes both display-friendly data (for the dashboard) AND raw ChildData (for round-trip deserialization).
        /// The raw data ensures children survive world sim restarts.
        /// </summary>
        private async Task SaveChildrenState()
        {
            try
            {
                var familySystem = FamilySystem.Instance;
                if (familySystem == null) return;

                var children = familySystem.AllChildren.Where(c => !c.Deleted).ToList();

                // Display-friendly data for the dashboard
                var childrenData = children.Select(c => new Dictionary<string, object?>
                {
                    ["name"] = c.Name,
                    ["age"] = c.Age,
                    ["sex"] = c.Sex == CharacterSex.Male ? "Male" : "Female",
                    ["mother"] = c.Mother,
                    ["father"] = c.Father,
                    ["soul"] = c.Soul,
                    ["soulDesc"] = c.GetSoulDescription(),
                    ["health"] = c.Health,
                    ["royal"] = c.Royal,
                    ["kidnapped"] = c.Kidnapped,
                    ["birthDate"] = c.BirthDate.ToString("o"),
                    ["location"] = c.Location == GameConfig.ChildLocationHome ? "Home" :
                                   c.Location == GameConfig.ChildLocationOrphanage ? "Orphanage" : "Unknown"
                }).ToList();

                // Raw ChildData for round-trip serialization (survives world sim restart)
                var childrenRaw = familySystem.SerializeChildren();

                var wrapper = new Dictionary<string, object>
                {
                    ["count"] = childrenData.Count,
                    ["children"] = childrenData,
                    ["childrenRaw"] = childrenRaw,
                    ["updatedAt"] = DateTime.UtcNow.ToString("o")
                };

                var json = JsonSerializer.Serialize(wrapper, jsonOptions);
                await sqlBackend.SaveWorldState("children", json);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("WORLDSIM", $"Failed to save children state: {ex.Message}");
            }
        }

        /// <summary>
        /// Load children from world_state on startup.
        /// Reads the raw ChildData array saved by SaveChildrenState() and populates FamilySystem.
        /// </summary>
        private void LoadChildrenState()
        {
            try
            {
                var json = sqlBackend.LoadWorldState("children").GetAwaiter().GetResult();
                if (string.IsNullOrEmpty(json)) return;

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("childrenRaw", out var rawElement))
                {
                    DebugLogger.Instance.LogInfo("WORLDSIM", "No childrenRaw in world_state (legacy format). Children will start empty.");
                    return;
                }

                var childDataList = JsonSerializer.Deserialize<List<ChildData>>(rawElement.GetRawText(), jsonOptions);
                if (childDataList != null)
                {
                    // Empty list is valid — means all children aged out or were converted to NPCs
                    FamilySystem.Instance.DeserializeChildren(childDataList);
                    DebugLogger.Instance.LogInfo("WORLDSIM", $"Loaded {childDataList.Count} children from world_state");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("WORLDSIM", $"Failed to load children from world_state: {ex.Message}");
            }
        }

        /// <summary>
        /// Save NPCMarriageRegistry to world_state.
        /// Persists NPC-NPC marriages and affair states so they survive world sim restarts.
        /// </summary>
        private async Task SaveMarriageRegistryState()
        {
            try
            {
                var registry = NPCMarriageRegistry.Instance;
                var marriages = registry.GetAllMarriages();
                var affairs = registry.GetAllAffairs();

                var data = new Dictionary<string, object>
                {
                    ["marriages"] = marriages,
                    ["affairs"] = affairs,
                    ["updatedAt"] = DateTime.UtcNow.ToString("o")
                };

                var json = JsonSerializer.Serialize(data, jsonOptions);
                await sqlBackend.SaveWorldState(OnlineStateManager.KEY_MARRIAGES, json);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("WORLDSIM", $"Failed to save marriage registry: {ex.Message}");
            }
        }

        /// <summary>
        /// Load NPCMarriageRegistry from world_state on startup.
        /// Falls back to rebuilding from NPC IsMarried/SpouseName fields if no data exists.
        /// </summary>
        private void LoadMarriageRegistryState()
        {
            try
            {
                var json = sqlBackend.LoadWorldState(OnlineStateManager.KEY_MARRIAGES).GetAwaiter().GetResult();
                if (string.IsNullOrEmpty(json))
                {
                    // No saved data — rebuild from NPC fields as fallback
                    RebuildMarriageRegistryFromNPCs();
                    return;
                }

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Restore marriages
                if (root.TryGetProperty("marriages", out var marriagesEl))
                {
                    var marriageData = JsonSerializer.Deserialize<List<NPCMarriageData>>(marriagesEl.GetRawText(), jsonOptions);
                    NPCMarriageRegistry.Instance.RestoreMarriages(marriageData);
                    DebugLogger.Instance.LogInfo("WORLDSIM", $"Loaded {marriageData?.Count ?? 0} NPC marriages from world_state");
                }

                // Restore affairs
                if (root.TryGetProperty("affairs", out var affairsEl))
                {
                    var affairData = JsonSerializer.Deserialize<List<AffairState>>(affairsEl.GetRawText(), jsonOptions);
                    NPCMarriageRegistry.Instance.RestoreAffairs(affairData);
                    DebugLogger.Instance.LogInfo("WORLDSIM", $"Loaded {affairData?.Count ?? 0} affairs from world_state");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("WORLDSIM", $"Failed to load marriage registry from world_state: {ex.Message}. Rebuilding from NPCs.");
                RebuildMarriageRegistryFromNPCs();
            }
        }

        /// <summary>
        /// Rebuild the marriage registry from NPC IsMarried/SpouseName fields.
        /// This is a lossy fallback — we can reconstruct marriages but not affairs.
        /// </summary>
        private void RebuildMarriageRegistryFromNPCs()
        {
            var npcs = NPCSpawnSystem.Instance.ActiveNPCs;
            int rebuilt = 0;

            foreach (var npc in npcs)
            {
                if (npc.IsMarried && !string.IsNullOrEmpty(npc.SpouseName))
                {
                    var spouse = npcs.FirstOrDefault(n => n.Name == npc.SpouseName);
                    if (spouse != null && !NPCMarriageRegistry.Instance.IsMarriedToNPC(npc.ID))
                    {
                        NPCMarriageRegistry.Instance.RegisterMarriage(npc.ID, spouse.ID, npc.Name, spouse.Name);
                        rebuilt++;
                    }
                }
            }

            if (rebuilt > 0)
                DebugLogger.Instance.LogInfo("WORLDSIM", $"Rebuilt {rebuilt} marriages from NPC fields (fallback)");
        }

        /// <summary>
        /// Save world events to world_state so they survive world sim restarts.
        /// Uses the same WorldEventData format as OnlineStateManager.
        /// </summary>
        private async Task SaveWorldEventsState()
        {
            try
            {
                var activeEvents = WorldEventSystem.Instance.GetActiveEvents();
                var eventDataList = new List<WorldEventData>();

                // Save global state flags
                eventDataList.Add(new WorldEventData
                {
                    Id = "global_state",
                    Type = "GlobalState",
                    Title = "Global State",
                    Description = WorldEventSystem.Instance.CurrentKingDecree,
                    Parameters = new Dictionary<string, object>
                    {
                        ["PlaguActive"] = WorldEventSystem.Instance.PlaguActive,
                        ["WarActive"] = WorldEventSystem.Instance.WarActive,
                        ["FestivalActive"] = WorldEventSystem.Instance.FestivalActive
                    }
                });

                foreach (var evt in activeEvents)
                {
                    var eventData = new WorldEventData
                    {
                        Id = Guid.NewGuid().ToString(),
                        Type = evt.Type.ToString(),
                        Title = evt.Title,
                        Description = evt.Description,
                        StartTime = DateTime.Now.AddDays(-evt.StartDay),
                        EndTime = DateTime.Now.AddDays(evt.DaysRemaining),
                        Parameters = new Dictionary<string, object>
                        {
                            ["DaysRemaining"] = evt.DaysRemaining,
                            ["StartDay"] = evt.StartDay
                        }
                    };

                    // Save effects as Effect_<key> parameters
                    foreach (var effect in evt.Effects)
                    {
                        eventData.Parameters[$"Effect_{effect.Key}"] = effect.Value;
                    }

                    eventDataList.Add(eventData);
                }

                var json = JsonSerializer.Serialize(eventDataList, jsonOptions);
                await sqlBackend.SaveWorldState(OnlineStateManager.KEY_WORLD_EVENTS, json);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("WORLDSIM", $"Failed to save world events: {ex.Message}");
            }
        }

        /// <summary>
        /// Load world events from world_state on startup.
        /// Uses WorldEventSystem.RestoreFromSaveData() which already handles the WorldEventData format.
        /// </summary>
        private void LoadWorldEventsState()
        {
            try
            {
                var json = sqlBackend.LoadWorldState(OnlineStateManager.KEY_WORLD_EVENTS).GetAwaiter().GetResult();
                if (string.IsNullOrEmpty(json)) return;

                var eventDataList = JsonSerializer.Deserialize<List<WorldEventData>>(json, jsonOptions);
                if (eventDataList != null && eventDataList.Count > 0)
                {
                    // Use current day = 0 as placeholder; RestoreFromSaveData reads each event's own StartDay
                    WorldEventSystem.Instance.RestoreFromSaveData(eventDataList, 0);
                    var activeCount = WorldEventSystem.Instance.GetActiveEvents().Count;
                    DebugLogger.Instance.LogInfo("WORLDSIM", $"Loaded {activeCount} active world events from world_state");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("WORLDSIM", $"Failed to load world events: {ex.Message}");
            }
        }

        /// <summary>
        /// Load all player team names from player_teams table so WorldSimulator
        /// can protect them from NPC AI dissolution and unauthorized modification.
        /// Critical in MUD mode where GameEngine.Instance is null on the world sim thread.
        /// </summary>
        private async Task LoadPlayerTeamNames()
        {
            try
            {
                var teams = await sqlBackend.GetPlayerTeams();
                foreach (var team in teams)
                {
                    WorldSimulator.RegisterPlayerTeam(team.TeamName);
                }
                DebugLogger.Instance.LogInfo("WORLDSIM", $"Registered {teams.Count} player team names for protection");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("WORLDSIM", $"Failed to load player team names: {ex.Message}");
            }
        }

        /// <summary>
        /// Restore NPCs from saved data. Follows the same pattern as GameEngine.RestoreNPCs()
        /// but without requiring a GameEngine instance.
        /// </summary>
        private void RestoreNPCsFromData(List<NPCData> npcData)
        {
            // Clear existing NPCs
            NPCSpawnSystem.Instance.ClearAllNPCs();

            NPC? kingNpc = null;

            foreach (var data in npcData)
            {
                var npc = new NPC
                {
                    Id = data.Id,
                    ID = !string.IsNullOrEmpty(data.CharacterID) ? data.CharacterID : $"npc_{data.Name.ToLower().Replace(" ", "_")}",
                    Name1 = data.Name,
                    Name2 = data.Name,
                    Level = data.Level,
                    HP = data.HP,
                    MaxHP = data.MaxHP,
                    BaseMaxHP = data.BaseMaxHP > 0 ? data.BaseMaxHP : data.MaxHP,
                    BaseMaxMana = data.BaseMaxMana > 0 ? data.BaseMaxMana : data.MaxMana,
                    CurrentLocation = data.Location,
                    Experience = data.Experience,
                    Strength = data.Strength,
                    Defence = data.Defence,
                    Agility = data.Agility,
                    Dexterity = data.Dexterity,
                    Mana = data.Mana,
                    MaxMana = data.MaxMana,
                    WeapPow = data.WeapPow,
                    ArmPow = data.ArmPow,
                    BaseStrength = data.BaseStrength > 0 ? data.BaseStrength : data.Strength,
                    BaseDefence = data.BaseDefence > 0 ? data.BaseDefence : data.Defence,
                    BaseDexterity = data.BaseDexterity > 0 ? data.BaseDexterity : data.Dexterity,
                    BaseAgility = data.BaseAgility > 0 ? data.BaseAgility : data.Agility,
                    BaseStamina = data.BaseStamina > 0 ? data.BaseStamina : 50,
                    BaseConstitution = data.BaseConstitution > 0 ? data.BaseConstitution : 10 + data.Level * 2,
                    BaseIntelligence = data.BaseIntelligence > 0 ? data.BaseIntelligence : 10,
                    BaseWisdom = data.BaseWisdom > 0 ? data.BaseWisdom : 10,
                    BaseCharisma = data.BaseCharisma > 0 ? data.BaseCharisma : 10,
                    Class = data.Class,
                    Race = data.Race,
                    Sex = (CharacterSex)data.Sex,
                    Team = data.Team,
                    CTurf = data.IsTeamLeader,
                    IsDead = data.IsDead,

                    // Lifecycle - aging and natural death (with migration for legacy data)
                    Age = data.Age > 0 ? data.Age : new Random().Next(18, 50),
                    BirthDate = data.BirthDate > DateTime.MinValue
                        ? data.BirthDate
                        : DateTime.Now.AddHours(-(data.Age > 0 ? data.Age : new Random().Next(18, 50)) * GameConfig.NpcLifecycleHoursPerYear),
                    IsAgedDeath = data.IsAgedDeath,
                    IsPermaDead = data.IsPermaDead,
                    PregnancyDueDate = data.PregnancyDueDate,

                    IsMarried = data.IsMarried,
                    Married = data.Married,
                    SpouseName = data.SpouseName ?? "",
                    MarriedTimes = data.MarriedTimes,
                    NPCFaction = data.NPCFaction >= 0 ? (Faction)data.NPCFaction : null,
                    Chivalry = data.Chivalry,
                    Darkness = data.Darkness,
                    Gold = data.Gold,
                    BankGold = data.BankGold,
                    AI = CharacterAI.Computer
                };

                // Restore items
                if (data.Items != null && data.Items.Length > 0)
                {
                    npc.Item = new List<int>(data.Items);
                }

                // Restore market inventory
                if (data.MarketInventory != null && data.MarketInventory.Count > 0)
                {
                    if (npc.MarketInventory == null)
                        npc.MarketInventory = new List<global::Item>();

                    foreach (var itemData in data.MarketInventory)
                    {
                        var item = new global::Item
                        {
                            Name = itemData.ItemName,
                            Value = itemData.ItemValue,
                            Type = itemData.ItemType,
                            Attack = itemData.Attack,
                            Armor = itemData.Armor,
                            Strength = itemData.Strength,
                            Defence = itemData.Defence,
                            IsCursed = itemData.IsCursed
                        };
                        npc.MarketInventory.Add(item);
                    }
                }

                // Restore personality if available
                if (data.PersonalityProfile != null)
                {
                    npc.Personality = new PersonalityProfile
                    {
                        Aggression = data.PersonalityProfile.Aggression,
                        Loyalty = data.PersonalityProfile.Loyalty,
                        Intelligence = data.PersonalityProfile.Intelligence,
                        Greed = data.PersonalityProfile.Greed,
                        Sociability = data.PersonalityProfile.Compassion,
                        Courage = data.PersonalityProfile.Courage,
                        Trustworthiness = data.PersonalityProfile.Honesty,
                        Ambition = data.PersonalityProfile.Ambition,
                        Vengefulness = data.PersonalityProfile.Vengefulness,
                        Impulsiveness = data.PersonalityProfile.Impulsiveness,
                        Caution = data.PersonalityProfile.Caution,
                        Mysticism = data.PersonalityProfile.Mysticism,
                        Patience = data.PersonalityProfile.Patience,
                        Archetype = data.Archetype ?? "Balanced",
                        Gender = data.PersonalityProfile.Gender,
                        Orientation = data.PersonalityProfile.Orientation,
                        IntimateStyle = data.PersonalityProfile.IntimateStyle,
                        RelationshipPref = data.PersonalityProfile.RelationshipPref,
                        Romanticism = data.PersonalityProfile.Romanticism,
                        Sensuality = data.PersonalityProfile.Sensuality,
                        Jealousy = data.PersonalityProfile.Jealousy,
                        Commitment = data.PersonalityProfile.Commitment,
                        Adventurousness = data.PersonalityProfile.Adventurousness,
                        Exhibitionism = data.PersonalityProfile.Exhibitionism,
                        Voyeurism = data.PersonalityProfile.Voyeurism,
                        Flirtatiousness = data.PersonalityProfile.Flirtatiousness,
                        Passion = data.PersonalityProfile.Passion,
                        Tenderness = data.PersonalityProfile.Tenderness
                    };

                    // Migration: fill in Intelligence/Patience/Mysticism/Trustworthiness/Caution
                    // if they were never set (legacy data where these traits defaulted to 0)
                    if (npc.Personality.Intelligence <= 0 && npc.Personality.Patience <= 0 &&
                        npc.Personality.Mysticism <= 0 && npc.Personality.Trustworthiness <= 0)
                    {
                        var archetype = (data.Archetype ?? "citizen").ToLower();
                        var rng = new Random(npc.Name.GetHashCode()); // Deterministic per NPC name
                        float Rf() => (float)rng.NextDouble();

                        switch (archetype)
                        {
                            case "thug": case "brawler":
                                npc.Personality.Intelligence = Rf() * 0.4f + 0.1f;
                                npc.Personality.Patience = Rf() * 0.3f + 0.1f;
                                npc.Personality.Mysticism = Rf() * 0.2f;
                                npc.Personality.Trustworthiness = Rf() * 0.3f + 0.1f;
                                npc.Personality.Caution = Rf() * 0.3f + 0.1f;
                                break;
                            case "merchant": case "trader":
                                npc.Personality.Intelligence = Rf() * 0.3f + 0.5f;
                                npc.Personality.Patience = Rf() * 0.3f + 0.5f;
                                npc.Personality.Mysticism = Rf() * 0.3f;
                                npc.Personality.Trustworthiness = Rf() * 0.4f + 0.4f;
                                npc.Personality.Caution = Rf() * 0.3f + 0.5f;
                                break;
                            case "noble": case "aristocrat":
                                npc.Personality.Intelligence = Rf() * 0.3f + 0.5f;
                                npc.Personality.Patience = Rf() * 0.3f + 0.4f;
                                npc.Personality.Mysticism = Rf() * 0.3f + 0.1f;
                                npc.Personality.Trustworthiness = Rf() * 0.4f + 0.3f;
                                npc.Personality.Caution = Rf() * 0.3f + 0.4f;
                                break;
                            case "guard": case "soldier":
                                npc.Personality.Intelligence = Rf() * 0.4f + 0.3f;
                                npc.Personality.Patience = Rf() * 0.3f + 0.5f;
                                npc.Personality.Mysticism = Rf() * 0.2f + 0.1f;
                                npc.Personality.Trustworthiness = Rf() * 0.2f + 0.6f;
                                npc.Personality.Caution = Rf() * 0.3f + 0.4f;
                                break;
                            case "priest": case "cleric":
                                npc.Personality.Intelligence = Rf() * 0.3f + 0.5f;
                                npc.Personality.Patience = Rf() * 0.2f + 0.7f;
                                npc.Personality.Mysticism = Rf() * 0.3f + 0.5f;
                                npc.Personality.Trustworthiness = Rf() * 0.2f + 0.7f;
                                npc.Personality.Caution = Rf() * 0.3f + 0.5f;
                                break;
                            case "mystic": case "mage":
                                npc.Personality.Intelligence = Rf() * 0.2f + 0.8f;
                                npc.Personality.Patience = Rf() * 0.3f + 0.5f;
                                npc.Personality.Mysticism = Rf() * 0.2f + 0.8f;
                                npc.Personality.Trustworthiness = Rf() * 0.5f + 0.2f;
                                npc.Personality.Caution = Rf() * 0.4f + 0.4f;
                                break;
                            case "craftsman": case "artisan":
                                npc.Personality.Intelligence = Rf() * 0.3f + 0.5f;
                                npc.Personality.Patience = Rf() * 0.2f + 0.6f;
                                npc.Personality.Mysticism = Rf() * 0.3f + 0.1f;
                                npc.Personality.Trustworthiness = Rf() * 0.3f + 0.5f;
                                npc.Personality.Caution = Rf() * 0.3f + 0.4f;
                                break;
                            default: // citizen/commoner
                                npc.Personality.Intelligence = Rf() * 0.6f + 0.2f;
                                npc.Personality.Patience = Rf() * 0.6f + 0.2f;
                                npc.Personality.Mysticism = Rf() * 0.4f + 0.1f;
                                npc.Personality.Trustworthiness = Rf() * 0.6f + 0.2f;
                                npc.Personality.Caution = Rf() * 0.6f + 0.2f;
                                break;
                        }
                    }
                }
                npc.Archetype = data.Archetype ?? "citizen";

                // Initialize AI systems (uses restored personality if available)
                npc.EnsureSystemsInitialized();

                // Restore memories, goals, emotional state
                if (npc.Brain != null)
                {
                    if (data.Memories != null)
                    {
                        foreach (var memData in data.Memories)
                        {
                            if (Enum.TryParse<MemoryType>(memData.Type, out var memType))
                            {
                                var memory = new MemoryEvent
                                {
                                    Type = memType,
                                    Description = memData.Description,
                                    InvolvedCharacter = memData.InvolvedCharacter,
                                    Timestamp = memData.Timestamp,
                                    Importance = memData.Importance,
                                    EmotionalImpact = memData.EmotionalImpact
                                };
                                npc.Brain.Memory?.RecordEvent(memory);
                            }
                        }
                    }

                    if (data.CurrentGoals != null)
                    {
                        foreach (var goalData in data.CurrentGoals)
                        {
                            if (Enum.TryParse<global::GoalType>(goalData.Type, out var goalType))
                            {
                                var goal = new global::Goal(goalData.Name, goalType, goalData.Priority)
                                {
                                    Progress = goalData.Progress,
                                    IsActive = goalData.IsActive,
                                    TargetValue = goalData.TargetValue,
                                    CurrentValue = goalData.CurrentValue,
                                    CreatedTime = goalData.CreatedTime
                                };
                                npc.Brain.Goals?.AddGoal(goal);
                            }
                        }
                    }

                    if (data.EmotionalState != null)
                    {
                        if (data.EmotionalState.Happiness > 0)
                            npc.Brain.Emotions?.AddEmotion(EmotionType.Joy, data.EmotionalState.Happiness, 120);
                        if (data.EmotionalState.Anger > 0)
                            npc.Brain.Emotions?.AddEmotion(EmotionType.Anger, data.EmotionalState.Anger, 120);
                        if (data.EmotionalState.Fear > 0)
                            npc.Brain.Emotions?.AddEmotion(EmotionType.Fear, data.EmotionalState.Fear, 120);
                        if (data.EmotionalState.Trust > 0)
                            npc.Brain.Emotions?.AddEmotion(EmotionType.Gratitude, data.EmotionalState.Trust, 120);
                        if (data.EmotionalState.Confidence > 0)
                            npc.Brain.Emotions?.AddEmotion(EmotionType.Confidence, data.EmotionalState.Confidence, 120);
                        if (data.EmotionalState.Sadness > 0)
                            npc.Brain.Emotions?.AddEmotion(EmotionType.Sadness, data.EmotionalState.Sadness, 120);
                        if (data.EmotionalState.Greed > 0)
                            npc.Brain.Emotions?.AddEmotion(EmotionType.Greed, data.EmotionalState.Greed, 120);
                        if (data.EmotionalState.Loneliness > 0)
                            npc.Brain.Emotions?.AddEmotion(EmotionType.Loneliness, data.EmotionalState.Loneliness, 120);
                        if (data.EmotionalState.Envy > 0)
                            npc.Brain.Emotions?.AddEmotion(EmotionType.Envy, data.EmotionalState.Envy, 120);
                        if (data.EmotionalState.Pride > 0)
                            npc.Brain.Emotions?.AddEmotion(EmotionType.Pride, data.EmotionalState.Pride, 120);
                        if (data.EmotionalState.Hope > 0)
                            npc.Brain.Emotions?.AddEmotion(EmotionType.Hope, data.EmotionalState.Hope, 120);
                        if (data.EmotionalState.Peace > 0)
                            npc.Brain.Emotions?.AddEmotion(EmotionType.Peace, data.EmotionalState.Peace, 120);
                    }
                }

                // Fix XP for legacy data
                if (npc.Experience <= 0 && npc.Level > 1)
                {
                    npc.Experience = GetExperienceForLevel(npc.Level);
                }

                // Fix base stats if not set
                if (npc.BaseMaxHP <= 0)
                {
                    npc.BaseMaxHP = npc.MaxHP;
                    npc.BaseStrength = npc.Strength;
                    npc.BaseDefence = npc.Defence;
                    npc.BaseDexterity = npc.Dexterity;
                    npc.BaseAgility = npc.Agility;
                    npc.BaseStamina = npc.Stamina;
                    npc.BaseConstitution = npc.Constitution;
                    npc.BaseIntelligence = npc.Intelligence;
                    npc.BaseWisdom = npc.Wisdom;
                    npc.BaseCharisma = npc.Charisma;
                    npc.BaseMaxMana = npc.MaxMana;
                }

                // Restore dynamic equipment
                if (data.DynamicEquipment != null && data.DynamicEquipment.Count > 0)
                {
                    foreach (var equipData in data.DynamicEquipment)
                    {
                        var equipment = new Equipment
                        {
                            Name = equipData.Name,
                            Description = equipData.Description ?? "",
                            Slot = (EquipmentSlot)equipData.Slot,
                            WeaponPower = equipData.WeaponPower,
                            ArmorClass = equipData.ArmorClass,
                            ShieldBonus = equipData.ShieldBonus,
                            BlockChance = equipData.BlockChance,
                            StrengthBonus = equipData.StrengthBonus,
                            DexterityBonus = equipData.DexterityBonus,
                            ConstitutionBonus = equipData.ConstitutionBonus,
                            IntelligenceBonus = equipData.IntelligenceBonus,
                            WisdomBonus = equipData.WisdomBonus,
                            CharismaBonus = equipData.CharismaBonus,
                            MaxHPBonus = equipData.MaxHPBonus,
                            MaxManaBonus = equipData.MaxManaBonus,
                            DefenceBonus = equipData.DefenceBonus,
                            MinLevel = equipData.MinLevel,
                            Value = equipData.Value,
                            IsCursed = equipData.IsCursed,
                            Rarity = (EquipmentRarity)equipData.Rarity,
                            WeaponType = (WeaponType)equipData.WeaponType,
                            Handedness = (WeaponHandedness)equipData.Handedness,
                            ArmorType = (ArmorType)equipData.ArmorType,
                            StaminaBonus = equipData.StaminaBonus,
                            AgilityBonus = equipData.AgilityBonus,
                            CriticalChanceBonus = equipData.CriticalChanceBonus,
                            CriticalDamageBonus = equipData.CriticalDamageBonus,
                            MagicResistance = equipData.MagicResistance,
                            PoisonDamage = equipData.PoisonDamage,
                            LifeSteal = equipData.LifeSteal,
                            HasFireEnchant = equipData.HasFireEnchant,
                            HasFrostEnchant = equipData.HasFrostEnchant,
                            HasLightningEnchant = equipData.HasLightningEnchant,
                            HasPoisonEnchant = equipData.HasPoisonEnchant,
                            HasHolyEnchant = equipData.HasHolyEnchant,
                            HasShadowEnchant = equipData.HasShadowEnchant,
                            ManaSteal = equipData.ManaSteal,
                            ArmorPiercing = equipData.ArmorPiercing,
                            Thorns = equipData.Thorns,
                            HPRegen = equipData.HPRegen,
                            ManaRegen = equipData.ManaRegen
                        };

                        int newId = EquipmentDatabase.RegisterDynamic(equipment);

                        // Update EquippedItems to use the new dynamic ID
                        if (data.EquippedItems != null)
                        {
                            foreach (var slot in data.EquippedItems.Keys.ToList())
                            {
                                if (data.EquippedItems[slot] == equipData.Id)
                                    data.EquippedItems[slot] = newId;
                            }
                        }
                    }
                }

                // Restore equipped items
                if (data.EquippedItems != null && data.EquippedItems.Count > 0)
                {
                    foreach (var kvp in data.EquippedItems)
                    {
                        npc.EquippedItems[(EquipmentSlot)kvp.Key] = kvp.Value;
                    }
                }

                // Recalculate stats with equipment bonuses
                npc.RecalculateStats();

                // Sanity check HP
                long minHP = 20 + (npc.Level * 10);
                if (npc.MaxHP < minHP)
                {
                    npc.BaseMaxHP = minHP;
                    npc.MaxHP = minHP;
                    if (npc.HP < 0 || npc.HP > npc.MaxHP)
                        npc.HP = npc.IsDead ? 0 : npc.MaxHP;
                }

                NPCSpawnSystem.Instance.AddRestoredNPC(npc);

                if (data.IsKing)
                    kingNpc = npc;
            }

            // Restore king
            if (kingNpc != null)
            {
                global::CastleLocation.SetCurrentKing(kingNpc);
                DebugLogger.Instance.LogInfo("WORLDSIM", $"Restored king: {kingNpc.Name}");
            }

            NPCSpawnSystem.Instance.MarkAsInitialized();

            DebugLogger.Instance.LogInfo("WORLDSIM",
                $"Restored {npcData.Count} NPCs, {npcData.Count(n => n.IsDead)} dead");
        }

        /// <summary>
        /// Load the last world daily reset timestamp from world_state.
        /// </summary>
        private void LoadLastWorldDailyReset()
        {
            try
            {
                var json = sqlBackend.LoadWorldState("last_daily_reset").GetAwaiter().GetResult();
                if (!string.IsNullOrEmpty(json))
                {
                    _lastWorldDailyReset = JsonSerializer.Deserialize<DateTime>(json, jsonOptions);
                    DebugLogger.Instance.LogInfo("WORLDSIM", $"Loaded last world daily reset: {_lastWorldDailyReset:o}");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("WORLDSIM", $"Failed to load last_daily_reset: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if the 7 PM ET boundary has been crossed since the last world daily reset.
        /// If so, process world-level daily events (king, guards, treasury, etc.).
        /// </summary>
        private void CheckWorldDailyReset()
        {
            try
            {
                var resetBoundary = DailySystemManager.GetCurrentResetBoundary();
                if (_lastWorldDailyReset >= resetBoundary)
                    return; // Already processed this cycle

                DebugLogger.Instance.LogInfo("WORLDSIM", $"7 PM ET daily reset triggered (boundary: {resetBoundary:o})");
                ProcessWorldDailyReset();
                _lastWorldDailyReset = resetBoundary;

                // Persist to world_state so it survives server restarts
                var json = JsonSerializer.Serialize(resetBoundary, jsonOptions);
                sqlBackend.SaveWorldState("last_daily_reset", json).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("WORLDSIM", $"World daily reset error: {ex.Message}");
            }
        }

        /// <summary>
        /// Process world-level daily events at the 7 PM ET boundary.
        /// Handles king activities, guard loyalty, treasury, and world events.
        /// Player-specific resets are handled by DailySystemManager.ProcessPlayerDailyEvents().
        /// </summary>
        private void ProcessWorldDailyReset()
        {
            try
            {
                var king = CastleLocation.GetCurrentKing();
                if (king?.IsActive == true)
                {
                    var treasuryBefore = king.Treasury;

                    // King daily activities: treasury income/expenses, TotalReign++, prisoner processing
                    king.ProcessDailyActivities();

                    // Process guard loyalty changes based on treasury health
                    var guardsToRemove = new List<RoyalGuard>();
                    var random = new Random();
                    foreach (var guard in king.Guards)
                    {
                        if (king.Treasury < king.CalculateDailyExpenses())
                        {
                            guard.Loyalty = Math.Max(0, guard.Loyalty - 5);
                        }
                        else
                        {
                            guard.Loyalty = Math.Min(100, guard.Loyalty + 1);
                        }

                        var daysServed = (DateTime.Now - guard.RecruitmentDate).TotalDays;
                        if (daysServed > 30)
                            guard.Loyalty = Math.Min(100, guard.Loyalty + 1);

                        if (guard.Loyalty <= 10)
                        {
                            guardsToRemove.Add(guard);
                            NewsSystem.Instance?.Newsy(true, $"Guard {guard.Name} has deserted the royal service!");
                        }
                        else if (guard.Loyalty <= 25 && random.Next(100) < 10)
                        {
                            guardsToRemove.Add(guard);
                            NewsSystem.Instance?.Newsy(true, $"Disgruntled guard {guard.Name} has abandoned their post!");
                        }
                    }
                    foreach (var deserter in guardsToRemove)
                        king.Guards.Remove(deserter);

                    // Treasury crisis check
                    if (king.Treasury < king.CalculateDailyExpenses())
                    {
                        foreach (var guard in king.Guards)
                            guard.Loyalty = Math.Max(0, guard.Loyalty - 3);

                        var escapedMonsters = new List<MonsterGuard>();
                        foreach (var monster in king.MonsterGuards)
                        {
                            if (random.Next(100) < 10)
                            {
                                escapedMonsters.Add(monster);
                                NewsSystem.Instance?.Newsy(true, $"The unfed {monster.Name} has escaped from the castle moat!");
                            }
                        }
                        foreach (var monster in escapedMonsters)
                            king.MonsterGuards.Remove(monster);

                        if (king.Guards.Count > 0 || king.MonsterGuards.Count > 0)
                            NewsSystem.Instance?.Newsy(false, $"Royal treasury crisis! Guards and monsters go unpaid!");
                    }

                    // Log financial summary
                    var netChange = king.CalculateDailyIncome() - king.CalculateDailyExpenses();
                    if (netChange < 0 && Math.Abs(netChange) > 100)
                        NewsSystem.Instance?.Newsy(false, $"The royal treasury hemorrhages {Math.Abs(netChange)} gold daily!");

                    DebugLogger.Instance.LogInfo("WORLDSIM",
                        $"World daily reset: King {king.Name}, Treasury {treasuryBefore:N0} -> {king.Treasury:N0}, Reign day {king.TotalReign}");
                }

                // Process world events
                WorldEventSystem.Instance.ProcessDailyEvents(0);

                // Process quest maintenance
                QuestSystem.ProcessDailyQuestMaintenance();

                NewsSystem.Instance?.Newsy(false, "A new day dawns in the realm...");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("WORLDSIM", $"ProcessWorldDailyReset error: {ex.Message}");
            }
        }

        /// <summary>
        /// Calculate experience needed for a given level.
        /// Same formula as WorldSimulator.GetExperienceForLevel.
        /// </summary>
        private static long GetExperienceForLevel(int level)
        {
            if (level <= 1) return 0;
            long exp = 0;
            for (int i = 2; i <= level; i++)
            {
                exp += (long)(Math.Pow(i, 2.0) * 50);
            }
            return exp;
        }
    }
}
