using UsurperRemake.Data;
using UsurperRemake.Server;
using UsurperRemake.BBS;
using UsurperRemake.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// World Boss System — shared HP pool raid bosses for online mode.
    /// Each player runs their own combat loop; damage is recorded atomically to a shared DB pool.
    /// Boss spawns once per day via WorldSimService tick, lasts 1 hour, requires multiple players.
    /// </summary>
    public class WorldBossSystem
    {
        private static WorldBossSystem? _instance;
        public static WorldBossSystem Instance => _instance ??= new WorldBossSystem();

        private readonly Random _rng = new();

        // v1.1.4: cooldowns and re-entry live on the player's world_boss_damage row, never in memory.

        /// <summary>
        /// The key every world boss row uses: the login name (Name1), lowercased, the same key the
        /// session table, the mail, and the inheritance queue use. The display name changes with a
        /// marriage and differs for alts; it is stored beside the row for showing, never for finding.
        /// </summary>
        internal static string RowKey(Character player) =>
            (string.IsNullOrEmpty(player.Name1) ? player.Name2 : player.Name1).ToLowerInvariant();

        /// <summary>The save that must follow a delivery; tests replace it to observe the call.</summary>
        internal Func<Character, Task<bool>> SaveHook = p => SaveSystem.Instance.AutoSave(p, force: true);

        /// <summary>v1.1.10: queues an item that does not fit the pack; tests replace it to make the write fail.</summary>
        internal Func<SqlSaveBackend, string, string, string, bool> QueueHook = (db, key, source, json) => db.QueueInheritance(key, source, json);

        /// <summary>Last known active boss name for notification display. Set on spawn, cleared on death/despawn.</summary>
        public volatile string? ActiveBossName;

        // ═══════════════════════════════════════════════════════════════════════════
        // The tick (v1.1.4): the boss's clock and the only writer of boss state.
        // Schedule, spawn, window end, Rally, phase, notices, the town snapshot.
        // DOCS/WORLD_BOSS_PLAN.md rulings 1 and 3.
        // ═══════════════════════════════════════════════════════════════════════════

        public const string ScheduleKey = "world_boss_schedule";

        /// <summary>What the town line and /boss show; refreshed by the tick, read by every session.</summary>
        public sealed class WorldBossSnapshot
        {
            public bool Active;
            public int BossId;
            public string BossName = "";
            public string BossTitle = "";
            public int Level;
            public double HpPercent;
            public int Engaged;
            public int Nights;
            public DateTime? ExpiresUtc;
            public string NextBossName = "";
            public string NextBossTitle = "";
            public int NextLevel;
            public DateTime? NextSpawnUtc;
        }

        public volatile WorldBossSnapshot Snapshot = new();

        private readonly JsonSerializerOptions _scheduleJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private async Task<WorldBossSchedule?> LoadSchedule(SqlSaveBackend backend)
        {
            try
            {
                var json = await backend.LoadWorldState(ScheduleKey);
                return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<WorldBossSchedule>(json, _scheduleJson);
            }
            catch { return null; }
        }

        private Task SaveSchedule(SqlSaveBackend backend, WorldBossSchedule schedule) =>
            backend.SaveWorldState(ScheduleKey, JsonSerializer.Serialize(schedule, _scheduleJson));

        /// <summary>
        /// Called every 30 s from WorldSimService. Order matters: end a passed window first, then
        /// make sure a schedule exists, spawn when its hour has come, then the live upkeep.
        /// </summary>
        public async Task Tick(SqlSaveBackend backend)
        {
            try
            {
                var now = DateTime.UtcNow;
                var active = await backend.GetActiveWorldBossAnyTime();
                if (active != null && active.ExpiresAt <= now)
                {
                    await EndWindow(backend, active);
                    active = null;
                }

                var schedule = await LoadSchedule(backend);
                if (schedule == null || schedule.SpawnedBossId != 0 && active == null && await SpawnedBossIsOver(backend, schedule))
                {
                    schedule = await MakeNextSchedule(backend, schedule);
                }

                if (active == null && schedule.SpawnedBossId == 0 && now >= schedule.SpawnUtc.AddHours(schedule.WindowHours))
                {
                    // The whole window passed with the server down: no late spawn after every notice said
                    // one hour; the schedule rolls forward and the countdown tells the truth in the morning.
                    backend.LogWorldBossEvent(0, "missed", "", $"def={schedule.DefinitionId} spawn={schedule.SpawnUtc:u}");
                    DebugLogger.Instance.LogWarning("WORLD_BOSS", $"Missed the {schedule.SpawnUtc:u} window for {schedule.DefinitionId}; rescheduling");
                    schedule = await MakeNextSchedule(backend, schedule);
                }
                else if (active == null && schedule.SpawnedBossId == 0 && now >= schedule.SpawnUtc)
                {
                    active = await SpawnScheduled(backend, schedule);
                }

                if (schedule.SpawnedBossId == 0 && !schedule.NoticedHourBefore && now >= schedule.SpawnUtc.AddHours(-GameConfig.WorldBossNoticeHoursBefore))
                {
                    schedule.NoticedHourBefore = true;
                    await SaveSchedule(backend, schedule);
                    NoticeHourBefore(backend, schedule);
                }

                if (active != null)
                {
                    await LiveUpkeep(backend, active);
                }

                await SettleUnsettled(backend);
                RefreshSnapshot(backend, active, schedule);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("WORLD_BOSS", $"Tick failed: {ex.Message}");
            }
        }

        private async Task<bool> SpawnedBossIsOver(SqlSaveBackend backend, WorldBossSchedule schedule)
        {
            var boss = await backend.GetWorldBossById(schedule.SpawnedBossId);
            return boss == null || boss.Status != "active";
        }

        /// <summary>The next 8 PM Eastern. A withdrawn boss returns; otherwise the cohort's median level picks one.</summary>
        private async Task<WorldBossSchedule> MakeNextSchedule(SqlSaveBackend backend, WorldBossSchedule? previous)
        {
            var now = DateTime.UtcNow;
            var spawnUtc = WorldBossMath.NextSpawnUtc(now);
            if (previous != null && previous.SpawnUtc >= spawnUtc)
                spawnUtc = WorldBossMath.SpawnUtcFor(previous.SpawnUtc, 1); // never the same hour twice
            int median = backend.GetActivePlayersMedianLevel(GameConfig.WorldBossActiveDays);
            var schedule = new WorldBossSchedule { SpawnUtc = spawnUtc, MedianLevel = median, WindowHours = GameConfig.WorldBossWindowHours };

            var withdrawn = await backend.GetWithdrawnWorldBoss();
            if (withdrawn != null && withdrawn.Nights < GameConfig.WorldBossMaxNights)
            {
                schedule.CarriedBossId = withdrawn.Id;
                schedule.DefinitionId = withdrawn.DefinitionId;
                schedule.BossLevel = withdrawn.BossLevel;
            }
            else
            {
                var def = WorldBossMath.PickBoss(median, WorldBossDatabase.GetAllBosses(), _rng);
                schedule.DefinitionId = def.Id;
                schedule.BossLevel = WorldBossMath.BossLevelFor(def, median);
            }
            await SaveSchedule(backend, schedule);
            await NoticeScheduled(backend, schedule);
            schedule.NoticedAtReset = true;
            await SaveSchedule(backend, schedule);
            DebugLogger.Instance.LogInfo("WORLD_BOSS", $"Scheduled {schedule.DefinitionId} (Lv{schedule.BossLevel}, median {median}) for {schedule.SpawnUtc:u}{(schedule.CarriedBossId != 0 ? " (returning)" : "")}");
            return schedule;
        }

        private async Task<WorldBossInfo?> SpawnScheduled(SqlSaveBackend backend, WorldBossSchedule schedule)
        {
            var bossDef = WorldBossDatabase.GetBossById(schedule.DefinitionId) ?? WorldBossMath.PickBoss(schedule.MedianLevel, WorldBossDatabase.GetAllBosses(), _rng);
            int onlineCount = backend.GetOnlinePlayerCount();
            WorldBossInfo? boss = null;

            if (schedule.CarriedBossId != 0)
            {
                if (await backend.ReactivateWorldBoss(schedule.CarriedBossId, GameConfig.WorldBossNightRegenFraction, schedule.WindowHours))
                {
                    boss = await backend.GetWorldBossById(schedule.CarriedBossId);
                    if (boss != null)
                    {
                        backend.LogWorldBossEvent(boss.Id, "return", "", $"night={boss.Nights} hp={boss.CurrentHP} online={onlineCount}");
                        int pct = (int)(100.0 * boss.CurrentHP / Math.Max(1, boss.MaxHP));
                        MudServer.Instance?.BroadcastLocalized(lang =>
                            $"\n  *** {Loc.GetIn(lang, "world_boss.returns_broadcast", bossDef.Name, boss.Nights, pct)} ***\n  {Loc.GetIn(lang, "world_boss.type_boss_to_join")}");
                        if (OnlineStateManager.IsActive)
                            _ = OnlineStateManager.Instance!.AddNews(Loc.Get("world_boss.returns_broadcast", bossDef.Name, boss.Nights, pct), "world_boss");
                        DiscordBridge.QueueSystemEvent(Loc.GetIn("en", "world_boss.returns_broadcast", bossDef.Name, boss.Nights, pct));
                    }
                }
            }

            if (boss == null)
            {
                int bossLevel = schedule.BossLevel > 0 ? schedule.BossLevel : WorldBossMath.BossLevelFor(bossDef, schedule.MedianLevel);
                var bossData = new WorldBossRuntimeData
                {
                    DefinitionId = bossDef.Id,
                    CurrentPhase = 1,
                    ScaledLevel = bossLevel,
                    ScaledStrength = bossDef.BaseStrength + (bossLevel - bossDef.BaseLevel) * 3,
                    ScaledDefence = bossDef.BaseDefence + (bossLevel - bossDef.BaseLevel) * 2,
                    ScaledAgility = bossDef.BaseAgility + (bossLevel - bossDef.BaseLevel),
                    AttacksPerRound = bossDef.AttacksPerRound
                };
                long maxHp = WorldBossMath.MaxHP(bossLevel, bossData.ScaledDefence);
                int id = await backend.SpawnScheduledWorldBoss(bossDef.Name, bossLevel, maxHp, schedule.WindowHours,
                    JsonSerializer.Serialize(bossData), bossDef.Id, schedule.SpawnUtc, schedule.MedianLevel, onlineCount);
                if (id <= 0) return null;
                boss = await backend.GetWorldBossById(id);
                backend.LogWorldBossEvent(id, "spawn", "", $"level={bossLevel} hp={maxHp} online={onlineCount} median={schedule.MedianLevel}");
                DebugLogger.Instance.LogInfo("WORLD_BOSS", $"Spawned {bossDef.Name} (Lv{bossLevel}, HP:{maxHp:N0}) with {onlineCount} players online");
                MudServer.Instance?.BroadcastLocalized(lang =>
                    $"\n  *** {Loc.GetIn(lang, "world_boss.spawn_broadcast", bossDef.Name, bossDef.Title)} ***\n  {Loc.GetIn(lang, "world_boss.type_boss_to_join")}");
                if (OnlineStateManager.IsActive)
                    _ = OnlineStateManager.Instance!.AddNews(Loc.Get("world_boss.spawn_news", bossDef.Name, bossDef.Title), "world_boss");
                DiscordBridge.QueueSystemEvent(Loc.GetIn("en", "world_boss.spawn_news", bossDef.Name, bossDef.Title));
            }

            if (boss != null)
            {
                ActiveBossName = bossDef.Name;
                schedule.SpawnedBossId = boss.Id;
                await SaveSchedule(backend, schedule);
            }
            return boss;
        }

        /// <summary>The window passed with the boss alive: it withdraws, or leaves after the last night.</summary>
        private async Task EndWindow(SqlSaveBackend backend, WorldBossInfo boss)
        {
            // v1.1.5: a telegraph live at the window's end expires with it; the withdraw clears it
            if (boss.TelegraphLive)
                backend.LogWorldBossEvent(boss.Id, "telegraph_expired", "", $"{boss.TelegraphId}|window_end", boss.TelegraphSeq);
            // Pay the night first (idempotent through the night bits; damage on an expired row is already
            // refused), then withdraw. A crash between the two leaves the boss active-and-expired and
            // the next tick runs EndWindow again.
            await PayWithdrawal(backend, boss);
            if (!await backend.WithdrawWorldBoss(boss.Id)) return;
            var bossDef = WorldBossDatabase.GetBossById(boss.DefinitionId);
            string name = bossDef?.Name ?? boss.BossName;
            int pct = (int)(100.0 * boss.CurrentHP / Math.Max(1, boss.MaxHP));
            ActiveBossName = null;
            backend.LogWorldBossEvent(boss.Id, "withdraw", "", $"night={boss.Nights} hp={boss.CurrentHP} pct={pct}");

            bool leaves = boss.Nights >= GameConfig.WorldBossMaxNights;
            if (leaves)
            {
                await backend.MarkWorldBossLeft(boss.Id);
                await backend.MarkWorldBossSettled(boss.Id);
                backend.LogWorldBossEvent(boss.Id, "left", "", $"nights={boss.Nights} hp={boss.CurrentHP}");
                var board = await backend.GetWorldBossDamageLeaderboard(boss.Id, 10);
                string stood = board.Count == 0 ? Loc.Get("world_boss.nobody") : string.Join(", ", board.Select(e => e.ShownName));
                MudServer.Instance?.BroadcastLocalized(lang => $"\n  *** {Loc.GetIn(lang, "world_boss.left_news", name, boss.Nights, stood)} ***");
                if (OnlineStateManager.IsActive)
                    _ = OnlineStateManager.Instance!.AddNews(Loc.Get("world_boss.left_news", name, boss.Nights, stood), "world_boss");
                DiscordBridge.QueueSystemEvent(Loc.GetIn("en", "world_boss.left_news", name, boss.Nights, stood));
            }
            else
            {
                MudServer.Instance?.BroadcastLocalized(lang => $"\n  *** {Loc.GetIn(lang, "world_boss.withdrew_news", name, pct, boss.Nights)} ***");
                if (OnlineStateManager.IsActive)
                    _ = OnlineStateManager.Instance!.AddNews(Loc.Get("world_boss.withdrew_news", name, pct, boss.Nights), "world_boss");
                DiscordBridge.QueueSystemEvent(Loc.GetIn("en", "world_boss.withdrew_news", name, pct, boss.Nights));
            }
        }

        /// <summary>Rally regeneration, the monotonic phase, the peak engaged count.</summary>
        private async Task LiveUpkeep(SqlSaveBackend backend, WorldBossInfo boss)
        {
            long regen = Math.Max(1, (long)(boss.MaxHP * GameConfig.WorldBossRallyRegenPerTick));
            if (await backend.RallyRegenWorldBoss(boss.Id, regen, GameConfig.WorldBossRallyIdleMinutes))
                backend.LogWorldBossEvent(boss.Id, "rally", "", $"regen={regen}");

            double hpPct = boss.MaxHP > 0 ? (double)boss.CurrentHP / boss.MaxHP : 1.0;
            int target = hpPct <= GameConfig.WorldBossPhase3Threshold ? 3 : hpPct <= GameConfig.WorldBossPhase2Threshold ? 2 : 1;
            if (target > boss.Phase && await backend.RaiseWorldBossPhase(boss.Id, target))
            {
                var bossDef = WorldBossDatabase.GetBossById(boss.DefinitionId);
                string name = bossDef?.Name ?? boss.BossName;
                backend.LogWorldBossEvent(boss.Id, "phase", "", $"phase={target} hp={boss.CurrentHP}");
                MudServer.Instance?.BroadcastLocalized(lang =>
                    $"\n  *** {Loc.GetIn(lang, "world_boss.phase_change_broadcast", name, target, GetPhaseDescriptionIn(lang, target))} ***");
                DiscordBridge.QueueSystemEvent(Loc.GetIn("en", "world_boss.phase_change_broadcast", name, target, GetPhaseDescriptionIn("en", target)));
            }

            int engaged = backend.GetWorldBossEngagedCount(boss.Id, GameConfig.WorldBossEngagedMinutes);
            if (engaged > 0) await backend.UpdateWorldBossPeakEngaged(boss.Id, engaged);

            await TelegraphUpkeep(backend, boss, engaged);
            await FocusUpkeep(backend, boss);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // v1.1.5 (milestone B): telegraphs and focus, owned by the tick
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>The fixed cycle for a phase: every ability the phase can use, in definition order.</summary>
        internal static List<WorldBossAbility> TelegraphCycle(WorldBossDefinition bossDef, int phase)
        {
            var list = new List<WorldBossAbility>();
            if (bossDef.Phase1Abilities != null) list.AddRange(bossDef.Phase1Abilities);
            if (phase >= 2 && bossDef.Phase2Abilities != null) list.AddRange(bossDef.Phase2Abilities);
            if (phase >= 3 && bossDef.Phase3Abilities != null) list.AddRange(bossDef.Phase3Abilities);
            return list;
        }

        /// <summary>The ability after the current telegraph in the phase's cycle; the first when there is none or the phase changed it.</summary>
        internal static WorldBossAbility? NextTelegraph(WorldBossDefinition bossDef, int phase, string currentId)
        {
            var cycle = TelegraphCycle(bossDef, phase);
            if (cycle.Count == 0) return null;
            int i = cycle.FindIndex(a => a.Name == currentId);
            return cycle[(i + 1) % cycle.Count];
        }

        internal static WorldBossAbility? FindAbility(WorldBossDefinition bossDef, string id) =>
            TelegraphCycle(bossDef, 3).FirstOrDefault(a => a.Name == id);

        /// <summary>
        /// Resolve the live telegraph once it has landed (a channel short of its interrupts lands on
        /// everyone; one that met them breaks and staggers the boss; a heal that lands heals the pool),
        /// then, one gap later, issue the next from the phase's cycle. Nothing is issued while nobody
        /// is engaged. Every step is a guarded write, so two ticks cannot double-resolve or double-issue.
        /// </summary>
        private async Task TelegraphUpkeep(SqlSaveBackend backend, WorldBossInfo boss, int engaged)
        {
            var bossDef = WorldBossDatabase.GetBossById(boss.DefinitionId);
            if (bossDef == null) return;
            var now = DateTime.UtcNow;

            if (boss.TelegraphLive)
            {
                if (boss.TelegraphLandsAt.HasValue && boss.TelegraphLandsAt.Value <= now)
                {
                    var ability = FindAbility(bossDef, boss.TelegraphId);
                    bool channel = ability?.IsChannel ?? boss.InterruptsNeeded > 0;
                    string outcome = channel && boss.InterruptsDone >= boss.InterruptsNeeded && boss.InterruptsNeeded > 0 ? "broken" : "landed";
                    if (await backend.ResolveWorldBossTelegraph(boss.Id, boss.TelegraphSeq, outcome, GameConfig.WorldBossStaggerSeconds))
                    {
                        string kind = channel ? "channel" : "strike";
                        backend.LogWorldBossEvent(boss.Id, "telegraph_resolved", "",
                            $"{boss.TelegraphId}|{kind}|{outcome}|{boss.InterruptsDone}/{boss.InterruptsNeeded}|engaged={engaged}", boss.TelegraphSeq);
                        if (outcome == "landed" && ability != null && ability.IsHeal)
                        {
                            long heal = Math.Max(1, (long)(boss.MaxHP * ability.SelfHealPercent));
                            if (await backend.HealWorldBoss(boss.Id, heal))
                                backend.LogWorldBossEvent(boss.Id, "heal", "", $"amount={heal}", boss.TelegraphSeq);
                        }
                        if (outcome == "broken")
                        {
                            string name = bossDef.Name;
                            TellFighters(backend, boss.Id, null, lang => Loc.GetIn(lang, "world_boss.stagger_broadcast", name));
                        }
                    }
                }
                return;
            }

            if (engaged <= 0) return;
            bool gapPassed = !boss.TelegraphLandsAt.HasValue || boss.TelegraphLandsAt.Value.AddSeconds(GameConfig.WorldBossTelegraphGapSeconds) <= now;
            if (!gapPassed) return;

            var next = NextTelegraph(bossDef, Math.Max(1, boss.Phase), boss.TelegraphId);
            if (next == null) return;
            long seq = boss.TelegraphSeq + 1;
            int needed = next.IsChannel ? Math.Min(GameConfig.WorldBossInterruptsMax, Math.Max(1, engaged)) : 0;
            if (await backend.IssueWorldBossTelegraph(boss.Id, next.Name, seq, GameConfig.WorldBossTelegraphLandSeconds, needed))
                backend.LogWorldBossEvent(boss.Id, "telegraph_issued", "", $"{next.Name}|{(next.IsChannel ? "channel" : "strike")}|needed={needed}|engaged={engaged}", seq);
        }

        /// <summary>Every focus window the tick re-picks the boss's focus from window damage, unless a Challenge holds it.</summary>
        private async Task FocusUpkeep(SqlSaveBackend backend, WorldBossInfo boss)
        {
            var focus = await backend.RefreshWorldBossFocus(boss.Id, GameConfig.WorldBossFocusWindowSeconds, GameConfig.WorldBossEngagedMinutes);
            if (focus != null && focus != boss.FocusPlayer)
                backend.LogWorldBossEvent(boss.Id, "focus", focus, "by=damage");
        }

        /// <summary>A line to every engaged fighter's session except one, in their own language, printed at their next prompt.</summary>
        internal static void TellFighters(SqlSaveBackend backend, int bossId, string? exceptKey, Func<string, string> line)
        {
            var server = MudServer.Instance;
            if (server == null) return;
            foreach (var (key, _) in backend.GetWorldBossEngagedNames(bossId, GameConfig.WorldBossEngagedMinutes))
            {
                if (key == exceptKey) continue;
                if (!server.ActiveSessions.TryGetValue(key, out var session) || session == null) continue;
                string lang = session.Context?.Language ?? "en";
                session.EnqueueMessage($"  {line(lang)}");
            }
        }

        /// <summary>
        /// The tick finishes what a crash or a killer's session left: kill rewards for a defeated
        /// boss, withdrawal pay for a withdrawn one (idempotent through the night bits), and the
        /// settled flag for one that left.
        /// </summary>
        private async Task SettleUnsettled(SqlSaveBackend backend)
        {
            foreach (int id in backend.GetUnsettledWorldBossIds())
            {
                var boss = await backend.GetWorldBossById(id);
                if (boss == null) continue;
                switch (boss.Status)
                {
                    case "defeated": await SettleKill(backend, boss); break;
                    case "left": await PayWithdrawal(backend, boss); await backend.MarkWorldBossSettled(id); break;
                }
            }
        }

        private static long BudgetFor(WorldBossInfo boss)
        {
            long scaledDef = 0;
            try
            {
                var data = JsonSerializer.Deserialize<WorldBossRuntimeData>(boss.BossDataJson);
                scaledDef = data?.ScaledDefence ?? 0;
            }
            catch { }
            return WorldBossMath.PerPlayerBudget(boss.BossLevel, scaledDef);
        }

        /// <summary>
        /// Kill rewards (ruling 4): one frozen row per qualified human, by effort at their own level,
        /// with the together bonus, items by score, Legendary only for the MVP of three or more.
        /// Insert-or-ignore rows, then the settled flag; running twice writes nothing new.
        /// </summary>
        public async Task SettleKill(SqlSaveBackend backend, WorldBossInfo boss)
        {
            var bossDef = WorldBossDatabase.GetBossById(boss.DefinitionId);
            string name = bossDef?.Name ?? boss.BossName;
            var board = (await backend.GetWorldBossDamageLeaderboard(boss.Id, 200)).Where(e => !e.IsNpc).ToList();
            long budget = BudgetFor(boss);
            var scored = board.Select(e => (entry: e, score: WorldBossMath.Score(e.DamageDealt, budget)))
                              .Where(x => WorldBossMath.Qualified(x.score)).ToList();
            int contributors = scored.Count;
            string mvpKey = board.Count > 0 ? board[0].PlayerName : "";
            foreach (var (entry, score) in scored)
            {
                bool mvp = entry.PlayerName == mvpKey;
                int level = Math.Max(1, entry.PlayerLevel);
                var rarity = WorldBossMath.TierFor(score, mvp, contributors);
                await backend.InsertWorldBossReward(new WorldBossReward
                {
                    BossId = boss.Id, BossName = name, PlayerName = entry.PlayerName, Night = boss.Nights, Kind = "kill",
                    Xp = WorldBossMath.KillXP(level, score, contributors), Gold = WorldBossMath.KillGold(level, score, contributors),
                    Fame = GameConfig.WorldBossFameQualified + (mvp ? GameConfig.WorldBossFameMvpExtra : 0),
                    Rarity = (int)rarity, Marks = 1 + (int)rarity, Score = score, Mvp = mvp, DamageDealt = entry.DamageDealt,
                });
            }
            if (await backend.MarkWorldBossSettled(boss.Id))
            {
                backend.LogWorldBossEvent(boss.Id, "settle", "", $"outcome=kill qualified={contributors} of {board.Count} budget={budget}");
                if (board.Count > 0)
                {
                    string news = Loc.Get("world_boss.defeat_news", name, board[0].ShownName, $"{board[0].DamageDealt:N0}", board.Count);
                    if (OnlineStateManager.IsActive) await OnlineStateManager.Instance!.AddNews(news, "world_boss");
                    DiscordBridge.QueueSystemEvent(Loc.GetIn("en", "world_boss.defeat_news", name, board[0].ShownName, $"{board[0].DamageDealt:N0}", board.Count));
                }
                // The realm celebrates: +10 percent XP for a day through the world-event bonus path.
                try { WorldEventSystem.Instance.ForceEvent(WorldEventSystem.EventType.WorldBossVictory, DateTime.UtcNow.DayOfYear); }
                catch (Exception ex) { DebugLogger.Instance.LogError("WORLD_BOSS", $"Victory event failed: {ex.Message}"); }
                NotifyOnlineRewards(scored.Select(x => x.entry.PlayerName));
            }
        }

        /// <summary>Withdrawal pay: a quarter of the night's share, once per player per night through the bit.</summary>
        public async Task PayWithdrawal(SqlSaveBackend backend, WorldBossInfo boss)
        {
            var bossDef = WorldBossDatabase.GetBossById(boss.DefinitionId);
            string name = bossDef?.Name ?? boss.BossName;
            var board = (await backend.GetWorldBossDamageLeaderboard(boss.Id, 200)).Where(e => !e.IsNpc && e.NightDamage > 0).ToList();
            long budget = BudgetFor(boss);
            var scored = board.Select(e => (entry: e, score: WorldBossMath.Score(e.NightDamage, budget)))
                              .Where(x => WorldBossMath.Qualified(x.score)).ToList();
            int contributors = scored.Count;
            var paid = new List<string>();
            foreach (var (entry, score) in scored)
            {
                if (!await backend.ClaimWorldBossNightPay(boss.Id, entry.PlayerName, boss.Nights)) continue;
                int level = Math.Max(1, entry.PlayerLevel);
                await backend.InsertWorldBossReward(new WorldBossReward
                {
                    BossId = boss.Id, BossName = name, PlayerName = entry.PlayerName, Night = boss.Nights, Kind = "withdraw",
                    Xp = WorldBossMath.WithdrawalXP(level, score, contributors), Gold = WorldBossMath.WithdrawalGold(level, score, contributors),
                    Fame = 5, Rarity = 0, Marks = 0, Score = score, Mvp = false, DamageDealt = entry.NightDamage,
                });
                paid.Add(entry.PlayerName);
            }
            if (paid.Count > 0)
            {
                backend.LogWorldBossEvent(boss.Id, "withdraw_pay", "", $"night={boss.Nights} paid={paid.Count}");
                NotifyOnlineRewards(paid);
            }
        }

        /// <summary>Tell online players rewards are waiting; their own session delivers them.</summary>
        private static void NotifyOnlineRewards(IEnumerable<string> playerKeys)
        {
            if (MudServer.Instance == null) return;
            foreach (var key in playerKeys)
            {
                if (MudServer.Instance.ActiveSessions.TryGetValue(key.ToLowerInvariant(), out var session))
                    session.EnqueueMessage($"\n  *** {Loc.GetIn(session.Context?.Language ?? "en", "world_boss.rewards_waiting")} ***");
            }
        }

        /// <summary>
        /// Delivery by the owning session, at login and on the /boss screen: XP, gold, fame, statistics,
        /// achievements, and the item (rolled at the player's level and class with the frozen rarity;
        /// a full pack sends it to the inheritance queue). Each row flips delivered once.
        /// </summary>
        public async Task DeliverWorldBossRewards(Character player, SqlSaveBackend backend, TerminalEmulator terminal)
        {
            string key = RowKey(player);
            var rows = backend.GetUndeliveredWorldBossRewards(key);
            if (rows.Count == 0) return;
            // v1.1.11: the team's current Training level; a teammate may have upgraded since it was read (review)
            TeamHQBonus.RefreshLevels(player, backend);
            bool headerShown = false;
            foreach (var r in rows)
            {
                if (!await backend.MarkWorldBossRewardDelivered(r.Id)) continue;
                if (!headerShown)
                {
                    headerShown = true;
                    terminal.WriteLine("");
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine(GameConfig.ScreenReaderMode ? $"  {Loc.Get("world_boss.rewards_delivered_header")}" : $"  ═══ {Loc.Get("world_boss.rewards_delivered_header")} ═══");
                }
                // v1.1.11: Team HQ Training at delivery, where the player and their levels are loaded (after the settlement cap).
                long xp = TeamHQBonus.ApplyXP(player, r.Xp);
                player.Experience += xp;
                player.Gold += r.Gold;
                player.Fame += r.Fame;
                terminal.SetColor("white");
                terminal.WriteLine(r.Kind == "kill"
                    ? $"  {Loc.Get("world_boss.reward_kind_kill", r.BossName, r.Night, (int)(r.Score * 100), r.Mvp ? Loc.Get("world_boss.tier_mvp") : Loc.Get("world_boss.tier_contributor"))}"
                    : $"  {Loc.Get("world_boss.reward_kind_withdraw", r.BossName, r.Night)}");
                terminal.WriteLine($"  {Loc.Get("world_boss.reward_xp", $"{xp:N0}")}  {Loc.Get("world_boss.reward_gold", $"{r.Gold:N0}")}  {Loc.Get("world_boss.reward_fame", r.Fame)}");

                var boss = await backend.GetWorldBossById(r.BossId);
                var bossDef = boss != null ? WorldBossDatabase.GetBossById(boss.DefinitionId) : null;
                if (r.Kind == "kill")
                {
                    player.Statistics.RecordWorldBossKill(bossDef?.Id ?? r.BossName, r.DamageDealt, r.Mvp);
                    AchievementSystem.TryUnlock(player, "world_boss_first");
                    if (player.Statistics.UniqueWorldBossTypes.Count >= 5) AchievementSystem.TryUnlock(player, "world_boss_5_unique");
                    if (player.Statistics.WorldBossesKilled >= 25) AchievementSystem.TryUnlock(player, "world_boss_25_total");
                    if (r.Mvp) AchievementSystem.TryUnlock(player, "world_boss_mvp");

                    var item = LootGenerator.GenerateWorldBossLoot(Math.Max(1, player.Level), (LootGenerator.ItemRarity)r.Rarity, bossDef?.Element ?? "", player.Class);
                    if (item != null)
                    {
                        if ((player.Inventory?.Count ?? 0) < 50)
                        {
                            player.Inventory!.Add(item);
                            terminal.SetColor((LootGenerator.ItemRarity)r.Rarity >= LootGenerator.ItemRarity.Legendary ? "bright_yellow" : (LootGenerator.ItemRarity)r.Rarity >= LootGenerator.ItemRarity.Epic ? "bright_magenta" : "bright_cyan");
                            terminal.WriteLine($"  {Loc.Get("world_boss.reward_loot")}: {LootGenerator.GetUnidentifiedName(item)}");
                        }
                        else
                        {
                            var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, IncludeFields = true };
                            if (QueueHook(backend, key, r.BossName, JsonSerializer.Serialize(item, opts)))
                            {
                                terminal.SetColor("gray");
                                terminal.WriteLine($"  {Loc.Get("world_boss.reward_item_queued", LootGenerator.GetUnidentifiedName(item))}");
                            }
                            else
                            {
                                // v1.1.10: the reward row is already marked delivered, so a failed queue
                                // write used to lose the item. It goes in the pack past the soft limit
                                // instead, and the save below keeps it.
                                player.Inventory!.Add(item);
                                DebugLogger.Instance.LogError("WORLD_BOSS", $"Inheritance queue write failed for {key}; {item.Name} put in the pack past the limit instead");
                                terminal.SetColor("bright_cyan");
                                terminal.WriteLine($"  {Loc.Get("world_boss.reward_loot")}: {LootGenerator.GetUnidentifiedName(item)}");
                            }
                        }
                    }
                }
                backend.LogWorldBossEvent(r.BossId, "deliver", key, $"kind={r.Kind} xp={r.Xp} gold={r.Gold} rarity={r.Rarity}");
            }
            if (headerShown)
            {
                terminal.WriteLine("");
                // The flag flipped first (no double pay); the save must follow in the same method, unthrottled,
                // or a dropped connection loses a reward the ledger says was delivered.
                try { await SaveHook(player); }
                catch (Exception ex) { DebugLogger.Instance.LogError("WORLD_BOSS", $"Save after reward delivery failed: {ex.Message}"); }
            }
        }

        private async Task NoticeScheduled(SqlSaveBackend backend, WorldBossSchedule schedule)
        {
            var bossDef = WorldBossDatabase.GetBossById(schedule.DefinitionId);
            if (bossDef == null) return;
            string key = schedule.CarriedBossId != 0 ? "world_boss.notice_returns" : "world_boss.notice_scheduled";
            try
            {
                if (OnlineStateManager.IsActive)
                    await OnlineStateManager.Instance!.AddNews(Loc.Get(key, bossDef.Name, bossDef.Title, schedule.BossLevel, Loc.Get("world_boss.spawn_hour_text")), "world_boss");
                DiscordBridge.QueueSystemEvent(Loc.GetIn("en", key, bossDef.Name, bossDef.Title, schedule.BossLevel, Loc.GetIn("en", "world_boss.spawn_hour_text")));
                MudServer.Instance?.BroadcastLocalized(lang => $"\n  {Loc.GetIn(lang, key, bossDef.Name, bossDef.Title, schedule.BossLevel, Loc.GetIn(lang, "world_boss.spawn_hour_text"))}");
                // Awaited one by one: hundreds of fire-and-forget inserts would contend with the tick's own writes.
                foreach (var (username, language) in backend.GetRecentActivePlayers(GameConfig.WorldBossActiveDays))
                {
                    string lang = string.IsNullOrEmpty(language) ? "en" : language;
                    await backend.SendMessage("System", username, "world_boss", Loc.GetIn(lang, key, bossDef.Name, bossDef.Title, schedule.BossLevel, Loc.GetIn(lang, "world_boss.spawn_hour_text")));
                }
            }
            catch (Exception ex) { DebugLogger.Instance.LogError("WORLD_BOSS", $"Notice failed: {ex.Message}"); }
        }

        private void NoticeHourBefore(SqlSaveBackend backend, WorldBossSchedule schedule)
        {
            var bossDef = WorldBossDatabase.GetBossById(schedule.DefinitionId);
            if (bossDef == null) return;
            MudServer.Instance?.BroadcastLocalized(lang => $"\n  *** {Loc.GetIn(lang, "world_boss.notice_hour", bossDef.Name, bossDef.Title)} ***");
            DiscordBridge.QueueSystemEvent(Loc.GetIn("en", "world_boss.notice_hour", bossDef.Name, bossDef.Title));
        }

        private void RefreshSnapshot(SqlSaveBackend backend, WorldBossInfo? active, WorldBossSchedule schedule)
        {
            var snap = new WorldBossSnapshot();
            if (active != null && active.Status == "active")
            {
                var def = WorldBossDatabase.GetBossById(active.DefinitionId);
                snap.Active = true;
                snap.BossId = active.Id;
                snap.BossName = def?.Name ?? active.BossName;
                snap.BossTitle = def?.Title ?? "";
                snap.Level = active.BossLevel;
                snap.HpPercent = active.MaxHP > 0 ? 100.0 * active.CurrentHP / active.MaxHP : 0;
                snap.Engaged = backend.GetWorldBossEngagedCount(active.Id, GameConfig.WorldBossEngagedMinutes);
                snap.Nights = active.Nights;
                snap.ExpiresUtc = active.ExpiresAt;
                ActiveBossName = snap.BossName;
            }
            else
            {
                ActiveBossName = null;
            }
            if (schedule.SpawnedBossId == 0)
            {
                var next = WorldBossDatabase.GetBossById(schedule.DefinitionId);
                snap.NextBossName = next?.Name ?? "";
                snap.NextBossTitle = next?.Title ?? "";
                snap.NextLevel = schedule.BossLevel;
                snap.NextSpawnUtc = schedule.SpawnUtc;
            }
            Snapshot = snap;
        }

        /// <summary>"2h 14m" or "14m", localized.</summary>
        public static string FormatSpan(TimeSpan span)
        {
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;
            int h = (int)span.TotalHours, m = span.Minutes;
            return h > 0 ? Loc.Get("world_boss.span_hm", h, m) : Loc.Get("world_boss.span_m", Math.Max(1, m));
        }

        /// <summary>The one status line for the town screen, or null when there is nothing to say.</summary>
        public string? TownLine()
        {
            var snap = Snapshot;
            var now = DateTime.UtcNow;
            if (snap.Active)
                return Loc.Get("world_boss.town_live", snap.BossName, (int)snap.HpPercent, snap.Engaged, FormatSpan((snap.ExpiresUtc ?? now) - now));
            if (snap.NextSpawnUtc.HasValue && !string.IsNullOrEmpty(snap.NextBossName))
                return Loc.Get("world_boss.town_countdown", snap.NextBossName, FormatSpan(snap.NextSpawnUtc.Value - now));
            return null;
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // UI — Boss status screen, leaderboard, enter combat
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Main world boss UI — shows boss status, leaderboard, and combat entry.
        /// Called from MainStreetLocation and /boss command.
        /// </summary>
        public async Task ShowWorldBossUI(Character player, TerminalEmulator terminal)
        {
            var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
            if (backend == null)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"\n  {Loc.Get("world_boss.online_only")}");
                await Task.Delay(1500);
                return;
            }

            if (player.Level < GameConfig.WorldBossMinLevel)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"\n  {Loc.Get("world_boss.min_level", GameConfig.WorldBossMinLevel)}");
                await Task.Delay(1500);
                return;
            }

            // v1.1.4: rewards from a boss settled while this player was away, or by another session
            await DeliverWorldBossRewards(player, backend, terminal);

            // v1.1.10: an item that waited for room in the pack arrives here too, not only at the next
            // login, so a player who has made room does not have to log out for it.
            if (await GameEngine.DeliverPendingInheritance(player, terminal, backend) > 0)
            {
                try { await SaveHook(player); }
                catch (Exception ex) { DebugLogger.Instance.LogError("WORLD_BOSS", $"Save after inheritance delivery failed: {ex.Message}"); }
            }

            while (true)
            {
                terminal.ClearScreen();

                var boss = await backend.GetActiveWorldBoss();

                if (boss == null || boss.Status != "active")
                {
                    DrawNoBossScreen(terminal);
                    await terminal.PressAnyKey();
                    break;
                }

                // Parse runtime data
                var bossData = DeserializeRuntimeData(boss.BossDataJson);
                var bossDef = WorldBossDatabase.GetBossById(bossData?.DefinitionId ?? "");

                DrawBossStatusScreen(terminal, boss, bossData, bossDef);

                // Show damage leaderboard
                var leaderboard = await backend.GetWorldBossDamageLeaderboard(boss.Id, 10);
                DrawLeaderboard(terminal, leaderboard, RowKey(player));

                // Menu
                terminal.SetColor("cyan");
                terminal.WriteLine($"  [A] {Loc.Get("world_boss.attack_boss")}    [Q] {Loc.Get("engine.back")}");
                terminal.SetColor("white");
                terminal.Write($"\n  {Loc.Get("ui.your_choice")}");
                string input = (await terminal.ReadLineAsync())?.Trim().ToUpper() ?? "";

                // v0.61.6: the menu label used to render as `[Q] [B]ack` (Loc.Get("ui.back")
                // is the BBS-convention string `[B]ack`), which read as a malformed double
                // hotkey next to the plain `[A] Attack Boss` label. Now uses the plain-word
                // `engine.back` label so it renders `[Q] Back`. Q is the hotkey; B is still
                // accepted as a defensive alias for players used to the old `[B]ack` label.
                if (input == "Q" || input == "B" || input == "") break;

                if (input == "A")
                {
                    await RunWorldBossCombat(player, terminal, backend, boss);
                }
            }
        }

        private void DrawNoBossScreen(TerminalEmulator terminal)
        {
            UIHelper.WriteBoxHeader(terminal, Loc.Get("world_boss.header"), "bright_magenta");
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("world_boss.no_active")}");
            var snap = Snapshot;
            if (snap.NextSpawnUtc.HasValue && !string.IsNullOrEmpty(snap.NextBossName))
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"  {Loc.Get("world_boss.next_boss", snap.NextBossName, snap.NextBossTitle, snap.NextLevel, FormatSpan(snap.NextSpawnUtc.Value - DateTime.UtcNow))}");
            }
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine($"  {Loc.Get("world_boss.spawn_info", Loc.Get("world_boss.spawn_hour_text"))}");
            terminal.WriteLine($"  {Loc.Get("world_boss.duration_info", GameConfig.WorldBossWindowHours, GameConfig.WorldBossMaxNights)}");
        }

        private void DrawBossStatusScreen(TerminalEmulator terminal, WorldBossInfo boss,
            WorldBossRuntimeData? bossData, WorldBossDefinition? bossDef)
        {
            string themeColor = bossDef?.ThemeColor ?? "bright_red";
            string bossTitle = bossDef != null ? $"{bossDef.Name}, {bossDef.Title}" : boss.BossName;
            int phase = bossData?.CurrentPhase ?? 1;

            UIHelper.WriteBoxHeader(terminal, Loc.Get("world_boss.header"), "bright_magenta");
            terminal.WriteLine("");

            // Boss name and phase
            terminal.SetColor(themeColor);
            terminal.WriteLine($"  {bossTitle}  ({Loc.Get("ui.level")} {boss.BossLevel})");
            if (phase > 1)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"  {Loc.Get("world_boss.phase_label", phase, 3)} — {GetPhaseDescription(phase)}");
            }
            if (boss.Nights > 1)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {Loc.Get("world_boss.night_label", boss.Nights, GameConfig.WorldBossMaxNights)}");
            }

            // HP bar
            double hpPercent = boss.MaxHP > 0 ? (double)boss.CurrentHP / boss.MaxHP * 100 : 0;
            string hpColor = hpPercent > 50 ? "bright_green" : hpPercent > 25 ? "bright_yellow" : "bright_red";

            terminal.SetColor(hpColor);
            if (GameConfig.ScreenReaderMode)
            {
                terminal.WriteLine($"  HP: {boss.CurrentHP:N0} / {boss.MaxHP:N0} ({hpPercent:F1}%)");
            }
            else
            {
                int barFilled = Math.Clamp((int)(hpPercent / 5), 0, 20);
                string hpBar = new string('█', barFilled) + new string('░', 20 - barFilled);
                terminal.WriteLine($"  HP: [{hpBar}] {boss.CurrentHP:N0} / {boss.MaxHP:N0} ({hpPercent:F1}%)");
            }

            // Time remaining
            var timeLeft = boss.ExpiresAt - DateTime.UtcNow;
            if (timeLeft.TotalSeconds > 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  {Loc.Get("world_boss.time_remaining", (int)timeLeft.TotalMinutes, timeLeft.Seconds)}");
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine($"  {Loc.Get("world_boss.time_expired")}");
            }

            // Boss element/theme
            if (bossDef != null)
            {
                terminal.SetColor("darkgray");
                terminal.WriteLine($"  {Loc.Get("world_boss.element")}: {bossDef.Element}  |  {Loc.Get("world_boss.attacks_per_round")}: {bossDef.AttacksPerRound}");
            }
            terminal.WriteLine("");
        }

        private void DrawLeaderboard(TerminalEmulator terminal, List<WorldBossDamageEntry> leaderboard, string playerKey)
        {
            if (leaderboard.Count == 0) return;

            long totalDamage = leaderboard.Sum(e => e.DamageDealt);

            terminal.SetColor("bright_yellow");
            terminal.WriteLine(GameConfig.ScreenReaderMode ? $"  {Loc.Get("world_boss.damage_leaderboard")}" : $"  ═══ {Loc.Get("world_boss.damage_leaderboard")} ═══");
            for (int i = 0; i < leaderboard.Count; i++)
            {
                var entry = leaderboard[i];
                double pct = totalDamage > 0 ? (double)entry.DamageDealt / totalDamage * 100 : 0;
                bool isPlayer = entry.PlayerName.Equals(playerKey, StringComparison.OrdinalIgnoreCase);

                string color = i == 0 ? "bright_yellow" : i < 3 ? "yellow" : isPlayer ? "bright_cyan" : "white";
                string marker = i == 0 ? $" {Loc.Get("world_boss.mvp_tag")}" : "";
                string youTag = isPlayer ? $" ({Loc.Get("world_boss.you_tag")})" : "";

                terminal.SetColor(color);
                terminal.WriteLine($"  {i + 1,2}. {entry.ShownName,-18} {entry.DamageDealt,10:N0} dmg  {pct,5:F1}%{marker}{youTag}");
            }
            terminal.WriteLine("");
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Combat Loop — Full round-by-round combat with shared HP pool
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Full interactive combat loop against the world boss.
        /// Each player runs their own loop; damage recorded atomically to shared DB.
        /// </summary>
        private async Task RunWorldBossCombat(Character player, TerminalEmulator terminal,
            SqlSaveBackend backend, WorldBossInfo boss)
        {
            // v1.1.11: read the team's HQ levels when the fight starts.
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
                TeamHQBonus.RefreshLevels(player);
            string playerKey = RowKey(player);
            // v1.1.4: the re-entry cooldown is on the player's row (two minutes after a retreat or the
            // fifty-round rest, five after a fall). There is no lock: retreat, fall, and rest all re-enter.
            int cooldownLeft = backend.GetWorldBossCooldownSeconds(boss.Id, playerKey);
            if (cooldownLeft > 0)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"\n  {Loc.Get("world_boss.death_cooldown", cooldownLeft)}");
                await Task.Delay(2000);
                return;
            }

            if (player.HP <= 0)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"\n  {Loc.Get("world_boss.too_injured")}");
                await Task.Delay(1500);
                return;
            }

            // Parse boss runtime data
            var bossData = DeserializeRuntimeData(boss.BossDataJson);
            var bossDef = WorldBossDatabase.GetBossById(bossData?.DefinitionId ?? "");
            if (bossDef == null || bossData == null)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"\n  {Loc.Get("world_boss.error_load")}");
                await Task.Delay(1500);
                return;
            }

            // Combat state
            var state = new WorldBossCombatState();
            var rng = Random.Shared;

            // v1.1.4 (council decision 5): the boss meets a player below its level at theirs. r is
            // frozen for the session; the session's copy of the scaled stats carries it, and the
            // player's native damage is divided by r before the row write. At or above the boss's
            // level r is 1 and the per-round cap on applied damage bounds the top.
            state.Ratio = WorldBossMath.Ratio(player.Level, boss.BossLevel);
            state.RoundCap = WorldBossMath.RoundCap(boss.MaxHP);
            state.BossId = boss.Id;
            state.BossMaxHP = boss.MaxHP;
            bossData.ScaledStrength = Math.Max(1, (long)Math.Round(bossData.ScaledStrength * state.Ratio));
            bossData.ScaledDefence = Math.Max(0, (long)Math.Round(bossData.ScaledDefence * state.Ratio));
            bossData.CurrentPhase = Math.Max(1, boss.Phase);

            // v1.1.5: the player's row knows which telegraphs are theirs. Entry starts at the live
            // telegraph, or the next one; anything that landed while they were away is not applied
            // (leaving costs the re-entry cooldown, which is the price of the dodge).
            var atEntry = await backend.GetWorldBossById(boss.Id) ?? boss;   // the caller's row may be stale
            long entrySeq = atEntry.TelegraphLive ? atEntry.TelegraphSeq : atEntry.TelegraphSeq + 1;
            // The one telegraph that was live when they last left still lands on them (ruling 2: leaving
            // before a landing and returning after still takes it); nothing issued in between does.
            var before = backend.GetWorldBossPlayerTelegraphState(boss.Id, playerKey);
            var carried = backend.GetResolvedWorldBossTelegraphs(boss.Id, before.LastResolvedSeq)
                .Where(t => t.Seq >= before.EngagedSinceSeq && t.Seq <= before.EngagedUntilSeq && t.Seq <= atEntry.LastResolvedSeq).ToList();
            await backend.EnsureWorldBossPlayerRow(boss.Id, playerKey, player.Level, player.DisplayName, entrySeq);

            // Reset transient combat buffs so leftover buffs from a previous fight (dungeon, etc.)
            // don't carry into the world boss, and so ability/spell buffs applied this fight start clean.
            player.TempAttackBonus = 0;
            player.TempAttackBonusDuration = 0;
            player.TempDefenseBonus = 0;
            player.TempDefenseBonusDuration = 0;
            player.TempDamageReductionPercent = 0;
            player.TempDamageReductionDuration = 0;
            player.HasStatusImmunity = false;
            player.StatusImmunityDuration = 0;

            terminal.ClearScreen();
            terminal.SetColor(bossDef.ThemeColor);
            terminal.WriteLine($"\n  {Loc.Get("world_boss.engage", bossDef.Name, bossDef.Title)}");

            // Dramatic spawn narration (localized) when the player first engages, mirroring the
            // Old God intro-dialogue beat.
            foreach (var line in bossDef.LocSpawn())
                terminal.WriteLine($"  {line}");

            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("world_boss.phase_label", bossData.CurrentPhase, 3)} -- {Loc.Get("world_boss.prepare_yourself")}");
            if (state.Ratio < 1.0)
                terminal.WriteLine($"  {Loc.Get("world_boss.meets_at_level")}");
            terminal.WriteLine("");
            await Task.Delay(1000);

            // Non-lethal "downed" outcome shared by the status-DoT path and the boss-damage
            // path. A world boss fight never consumes a resurrection: dropping to 0 HP ends the
            // session with the healers dragging the player to safety (revive to 25% HP plus a
            // re-entry cooldown). issue #110: this previously lived only on the boss-damage path,
            // so a death from a burn/poison DoT tick (processed at the top of the round) fell
            // through to the normal death flow and triggered a real death / divine intervention.
            void ApplyHealersSafety()
            {
                state.Died = true;
                terminal.SetColor("bright_red");
                terminal.WriteLine($"\n  {Loc.Get("world_boss.struck_you_down", bossDef.Name)}");
                terminal.SetColor("gray");
                terminal.WriteLine($"  {Loc.Get("world_boss.total_damage_before_fall", $"{state.SessionDamage:N0}")}");

                // Revive with 25% HP; the cooldown goes on the row when the session is recorded.
                player.HP = Math.Max(1, player.MaxHP / 4);

                terminal.SetColor("cyan");
                terminal.WriteLine($"  {Loc.Get("world_boss.healers_safety", player.HP, player.MaxHP)}");
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {Loc.Get("world_boss.must_wait", GameConfig.WorldBossFallCooldownSeconds)}");
            }

            while (state.Round < GameConfig.WorldBossMaxRoundsPerSession && player.HP > 0 && !state.Retreated)
            {
                state.Round++;

                // Refresh boss state from DB each round
                var currentBoss = await backend.GetActiveWorldBoss();
                if (currentBoss == null || currentBoss.Status != "active" || currentBoss.CurrentHP <= 0)
                {
                    terminal.SetColor("bright_green");
                    terminal.WriteLine($"\n  *** {Loc.Get("world_boss.has_been_defeated", bossDef.Name)} ***");
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"  {Loc.Get("world_boss.realm_celebrates")}");
                    break;
                }

                // v1.1.4: the tick owns the phase; the loop shows the change when it sees it
                if (currentBoss.Phase > bossData.CurrentPhase)
                {
                    bossData.CurrentPhase = currentBoss.Phase;
                    await ShowPhaseChange(currentBoss.Phase, bossDef, terminal);
                }

                // ─── v1.1.5: what landed since this player's last round (once per seq, by the row) ───
                foreach (var landed in carried) ApplyLandedTelegraph(landed, before, bossDef, player, terminal, rng);
                carried.Clear();
                var mine = backend.GetWorldBossPlayerTelegraphState(currentBoss.Id, playerKey);
                foreach (var landed in backend.GetResolvedWorldBossTelegraphs(currentBoss.Id, mine.LastResolvedSeq))
                {
                    if (landed.Seq < mine.EngagedSinceSeq || landed.Seq > currentBoss.LastResolvedSeq) continue;
                    if (!await backend.AdvanceWorldBossPlayerResolved(currentBoss.Id, playerKey, landed.Seq)) continue;
                    ApplyLandedTelegraph(landed, mine, bossDef, player, terminal, rng);
                }
                if (player.HP <= 0) { ApplyHealersSafety(); break; }

                // ─── Round header: boss, you, who is here, the telegraph, the menu (under 20 rows) ───
                var roster = backend.GetWorldBossEngagedNames(currentBoss.Id, GameConfig.WorldBossEngagedMinutes);
                int engagedNow = Math.Max(roster.Count, 1);
                bool focused = engagedNow <= 1 || currentBoss.FocusPlayer == playerKey;
                DrawRoundHeader(terminal, state, currentBoss, bossDef, bossData, player, roster, playerKey, focused);

                // ─── Process player status effects (DoT, duration tick-down) ───
                var statusMessages = player.ProcessStatusEffects();
                foreach (var (msg, color) in statusMessages)
                {
                    terminal.SetColor(color);
                    terminal.WriteLine($"  {msg}");
                }

                // Check if player can act after status processing. issue #110: a DoT tick
                // (burn/poison) that drops the player to 0 must take the same non-lethal
                // "healers dragged you to safety" outcome as boss-dealt damage below, not fall
                // through to the normal death flow (which consumed a resurrection).
                if (player.HP <= 0) { ApplyHealersSafety(); break; }

                bool canAct = player.CanAct();
                if (!canAct)
                {
                    var preventingStatus = player.ActiveStatuses.Keys.FirstOrDefault(s => s.PreventsAction());
                    terminal.SetColor("red");
                    terminal.WriteLine($"  {Loc.Get("world_boss.cannot_act", preventingStatus)}");
                }
                else
                {
                    // ─── Player action menu ───
                    var liveAbility = currentBoss.TelegraphLive ? FindAbility(bossDef, currentBoss.TelegraphId) : null;
                    ShowWorldBossActionMenu(terminal, player, liveAbility);
                    string input = (await terminal.ReadLineAsync())?.Trim().ToUpper() ?? "A";

                    // v1.1.5: answers and Challenge cost the round and write only their own column
                    if (input == "B" || input == "T" || input == "F")
                    {
                        await ProcessAnswer(input, player, terminal, backend, currentBoss, liveAbility, bossDef, playerKey, mine);
                        input = "";
                    }

                    long roundDamage = input == "" ? 0 : await ProcessPlayerAction(input, player, terminal, bossDef, bossData,
                        rng, state);

                    if (state.Retreated) break;

                    if (roundDamage > 0)
                    {
                        // Record damage atomically to shared HP pool. wasKillingBlow is true only
                        // for the single caller whose conditional status-flip won the race; other
                        // concurrent callers who bring remainingHp to 0 in the same round see
                        // wasKillingBlow == false (v0.57.9 fix for duplicate kill-credit bug).
                        // v1.1.11: Team HQ Armory, after every modifier and before the ratio and the cap.
                        roundDamage = TeamHQBonus.ApplyAttack(player, roundDamage);
                        long cap = WorldBossMath.RoundCap(state.BossMaxHP, currentBoss.Staggered);
                        long toApply = WorldBossMath.Applied(roundDamage, state.Ratio, cap);
                        var (remainingHp, wasKillingBlow, applied) = await backend.RecordWorldBossDamage(
                            currentBoss.Id, playerKey, toApply, player.Level, player.DisplayName);
                        if (applied <= 0)
                        {
                            // Not credited: the row is no longer active inside its window.
                            var latest = await backend.GetWorldBossById(currentBoss.Id);
                            terminal.SetColor("yellow");
                            if (latest?.Status == "defeated")
                            {
                                ActiveBossName = null;
                                state.Killed = true;
                                terminal.WriteLine($"\n  *** {Loc.Get("world_boss.already_defeated", bossDef.Name)} ***");
                            }
                            else
                            {
                                terminal.WriteLine($"\n  {Loc.Get("world_boss.window_closed", bossDef.Name)}");
                            }
                            break;
                        }
                        state.SessionDamage += applied;

                        terminal.SetColor("bright_green");
                        terminal.Write($"  >> {Loc.Get("world_boss.total_round_damage", $"{applied:N0}")}");
                        if (toApply >= cap && roundDamage / Math.Max(0.01, state.Ratio) > cap)
                        {
                            terminal.SetColor("yellow");
                            terminal.Write($"  {Loc.Get("world_boss.round_cap_hit", $"{cap:N0}")}");
                        }
                        terminal.SetColor("gray");
                        terminal.WriteLine($"  {Loc.Get("world_boss.boss_hp_remaining", $"{Math.Max(0, remainingHp):N0}")}");

                        if (wasKillingBlow)
                        {
                            ActiveBossName = null;
                            state.Killed = true;
                            terminal.SetColor("bright_green");
                            terminal.WriteLine($"\n  *** {Loc.Get("world_boss.has_been_defeated", bossDef.Name)} ***");
                            terminal.SetColor("yellow");
                            terminal.WriteLine($"  {Loc.Get("world_boss.killing_blow")}");

                            // Localized defeat narration for the player who landed the kill.
                            terminal.SetColor(bossDef.ThemeColor);
                            foreach (var line in bossDef.LocDefeat())
                                terminal.WriteLine($"  {line}");

                            // Broadcast — only the killer's session sends this
                            // Per-recipient language (built in the killer's session, but each player
                            // should read it in their own language, not the killer's).
                            MudServer.Instance?.BroadcastLocalized(lang =>
                                $"\n  *** {Loc.GetIn(lang, "world_boss.defeat_broadcast", bossDef.Name, player.DisplayName)} ***",
                                playerKey);

                            // News — also only the killer posts this
                            if (OnlineStateManager.IsActive)
                                _ = OnlineStateManager.Instance!.AddNews(
                                    Loc.Get("world_boss.defeat_broadcast", bossDef.Name, player.DisplayName), "world_boss");

                            // v1.1.4: settle writes the frozen reward rows (the tick would too, after a
                            // crash); this session delivers its own, and online players are told.
                            var defeated = await backend.GetWorldBossById(currentBoss.Id);
                            if (defeated != null) await SettleKill(backend, defeated);
                            await DeliverWorldBossRewards(player, backend, terminal);
                            break;
                        }
                        else if (remainingHp <= 0)
                        {
                            // Boss died this round but another player landed the killing blow.
                            // Exit cleanly with no broadcast, no duplicate news, no duplicate
                            // reward distribution. Our damage was already recorded on the
                            // leaderboard above; the killer's settle (or the tick's) writes our
                            // reward row and this session delivers it on the next /boss visit.
                            ActiveBossName = null;
                            state.Killed = true;
                            terminal.SetColor("yellow");
                            terminal.WriteLine($"\n  *** {Loc.Get("world_boss.already_defeated", bossDef.Name)} ***");
                            break;
                        }
                    }
                }

                // ─── Boss actions ───
                if (player.HP > 0 && !state.Retreated)
                {
                    ProcessBossActions(bossDef, bossData, player, terminal, rng, state, focused);
                }

                // Decrement defend counter
                if (state.DefendingRounds > 0) state.DefendingRounds--;

                // Tick buff durations (attack/defense/damage-reduction/status-immunity) so ability
                // and spell buffs expire on schedule, mirroring the main combat engine.
                if (player.TempAttackBonusDuration > 0 && --player.TempAttackBonusDuration <= 0)
                    player.TempAttackBonus = 0;
                if (player.TempDefenseBonusDuration > 0 && --player.TempDefenseBonusDuration <= 0)
                    player.TempDefenseBonus = 0;
                if (player.TempDamageReductionDuration > 0 && --player.TempDamageReductionDuration <= 0)
                    player.TempDamageReductionPercent = 0;
                if (player.StatusImmunityDuration > 0 && --player.StatusImmunityDuration <= 0)
                    player.HasStatusImmunity = false;

                // Tick ability cooldowns
                foreach (var key in state.AbilityCooldowns.Keys.ToList())
                {
                    state.AbilityCooldowns[key]--;
                    if (state.AbilityCooldowns[key] <= 0)
                        state.AbilityCooldowns.Remove(key);
                }

                // Player death check (boss-dealt damage). Same non-lethal outcome as the
                // status-DoT path above, via the shared helper (issue #110).
                if (player.HP <= 0)
                {
                    ApplyHealersSafety();
                    break;
                }

                await Task.Delay(300); // Brief pause between rounds
            }

            // Max rounds reached
            if (state.Round >= GameConfig.WorldBossMaxRoundsPerSession && !state.Died && !state.Retreated && player.HP > 0)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"\n  {Loc.Get("world_boss.max_rounds", GameConfig.WorldBossMaxRoundsPerSession)}");
                terminal.WriteLine($"  {Loc.Get("world_boss.step_back")}");
            }

            // v1.1.4: the session on the player's row: counts and the re-entry cooldown
            int cooldownSeconds = state.Killed ? 0 : state.Died ? GameConfig.WorldBossFallCooldownSeconds : GameConfig.WorldBossRetreatCooldownSeconds;
            await backend.RecordWorldBossSession(boss.Id, playerKey, player.Level, state.Round, state.Died, cooldownSeconds, player.DisplayName);
            backend.LogWorldBossEvent(boss.Id, "session_end", playerKey,
                $"reason={(state.Killed ? "kill" : state.Died ? "fall" : state.Retreated ? "retreat" : "rest")} rounds={state.Round} damage={state.SessionDamage} level={player.Level} r={state.Ratio:F2}");

            // Session summary
            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(GameConfig.ScreenReaderMode ? $"  {Loc.Get("world_boss.combat_summary")}" : $"  ═══ {Loc.Get("world_boss.combat_summary")} ═══");
            terminal.SetColor("white");
            terminal.WriteLine($"  {Loc.Get("world_boss.rounds_fought", state.Round)}");
            terminal.WriteLine($"  {Loc.Get("world_boss.damage_dealt", $"{state.SessionDamage:N0}")}");
            terminal.WriteLine("");

            // Autosave
            await SaveSystem.Instance.AutoSave(player as Player);
            await terminal.PressAnyKey();
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Player Action Processing
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// v1.1.5: the round header in under twenty rows: the boss line, your line, who is fighting,
        /// the telegraph with its answer and cost. Plain lines in screen-reader mode.
        /// </summary>
        private void DrawRoundHeader(TerminalEmulator terminal, WorldBossCombatState state, WorldBossInfo boss,
            WorldBossDefinition bossDef, WorldBossRuntimeData bossData, Character player,
            List<(string key, string display)> roster, string playerKey, bool focused)
        {
            double hpPct = boss.MaxHP > 0 ? (double)boss.CurrentHP / boss.MaxHP * 100 : 0;
            string hpColor = hpPct > 50 ? "bright_green" : hpPct > 25 ? "bright_yellow" : "bright_red";
            string stagger = boss.Staggered ? $"  {Loc.Get("world_boss.staggered_header", (int)Math.Ceiling((boss.StaggerUntil!.Value - DateTime.UtcNow).TotalSeconds))}" : "";

            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.Write(GameConfig.ScreenReaderMode ? $"  {Loc.Get("world_boss.round", state.Round)}  " : $"  ─── {Loc.Get("world_boss.round", state.Round)} ───  ");
            terminal.SetColor(hpColor);
            if (GameConfig.ScreenReaderMode)
                terminal.Write($"{Loc.Get("world_boss.boss_hp_label")}: {boss.CurrentHP:N0}/{boss.MaxHP:N0} ({hpPct:F1}%)");
            else
            {
                int barFilled = Math.Clamp((int)(hpPct / 5), 0, 20);
                string hpBar = new string('█', barFilled) + new string('░', 20 - barFilled);
                terminal.Write($"[{hpBar}] {hpPct:F1}%");
            }
            terminal.SetColor("gray");
            terminal.Write($"  {Loc.Get("world_boss.phase_short_label")} {bossData.CurrentPhase}");
            if (stagger != "") { terminal.SetColor("bright_yellow"); terminal.Write(stagger); }
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.Write($"  {Loc.Get("world_boss.your_hp_label")}: {player.HP}/{player.MaxHP}  {Loc.Get("world_boss.mana_label")}: {player.Mana}/{player.MaxMana}");
            if (player.ActiveStatuses.Count > 0)
            {
                terminal.SetColor("yellow");
                terminal.Write($"  {Loc.Get("world_boss.status_label")}: {string.Join(", ", player.ActiveStatuses.Select(kv => $"{kv.Key}({kv.Value})"))}");
            }
            terminal.WriteLine("");

            // Who is here: grouped players first, then the rest by recency; five names and a count
            var group = GroupSystem.Instance?.GetGroupFor(playerKey);
            var grouped = new HashSet<string>(group?.MemberUsernames.Select(u => u.ToLowerInvariant()) ?? Enumerable.Empty<string>());
            var others = roster.Where(r => r.key != playerKey)
                .OrderByDescending(r => grouped.Contains(r.key))
                .Select(r => string.IsNullOrEmpty(r.display) ? r.key : r.display).ToList();
            terminal.SetColor("gray");
            if (others.Count == 0)
                terminal.WriteLine($"  {Loc.Get("world_boss.fighting_alone")}");
            else
            {
                string names = string.Join(", ", others.Take(GameConfig.WorldBossRosterLines));
                if (others.Count > GameConfig.WorldBossRosterLines) names += $" {Loc.Get("world_boss.and_more", others.Count - GameConfig.WorldBossRosterLines)}";
                terminal.WriteLine($"  {Loc.Get("world_boss.fighting_now", names)}");
            }
            terminal.SetColor(focused ? "bright_red" : "gray");
            terminal.WriteLine($"  {(focused ? Loc.Get("world_boss.focus_you") : Loc.Get("world_boss.focus_other", FocusName(boss, roster)))}");

            // The telegraph, its answer and its cost
            if (boss.TelegraphLive)
            {
                var ability = FindAbility(bossDef, boss.TelegraphId);
                string name = ability != null ? bossDef.LocAbilityName(ability) : boss.TelegraphId;
                int secs = boss.TelegraphLandsAt.HasValue ? (int)Math.Ceiling((boss.TelegraphLandsAt.Value - DateTime.UtcNow).TotalSeconds) : 0;
                string when = secs > 0 ? Loc.Get("world_boss.lands_in", secs) : Loc.Get("world_boss.telegraph_landing_now");
                terminal.SetColor("bright_magenta");
                if (ability?.IsChannel ?? boss.InterruptsNeeded > 0)
                    terminal.WriteLine($"  {Loc.Get("world_boss.telegraph_channel", bossDef.Name, name, boss.InterruptsDone, boss.InterruptsNeeded, when)}");
                else
                    terminal.WriteLine($"  {Loc.Get("world_boss.telegraph_strike", bossDef.Name, name, (int)(GameConfig.WorldBossTelegraphUnansweredPercent * 100), when)}");
            }
        }

        private static string FocusName(WorldBossInfo boss, List<(string key, string display)> roster)
        {
            var hit = roster.FirstOrDefault(r => r.key == boss.FocusPlayer);
            return string.IsNullOrEmpty(hit.display) ? (string.IsNullOrEmpty(boss.FocusPlayer) ? "-" : boss.FocusPlayer) : hit.display;
        }

        /// <summary>v1.1.5: one line replaces the seven-row box; the answer key appears only while a telegraph is live.</summary>
        private void ShowWorldBossActionMenu(TerminalEmulator terminal, Character player, WorldBossAbility? live)
        {
            var abilities = ClassAbilitySystem.GetAvailableAbilities(player);
            string answer = live == null ? "" : live.IsChannel ? $" {Loc.Get("world_boss.menu_interrupt")}" : $" {Loc.Get("world_boss.menu_brace")}";
            terminal.SetColor("bright_white");
            if (GameConfig.ScreenReaderMode)
            {
                terminal.WriteLine($"  {Loc.Get("world_boss.menu_line_sr", abilities.Count > 0 ? Loc.Get("world_boss.menu_ability_sr") : "")}{answer}");
            }
            else
            {
                terminal.WriteLine($"  {Loc.Get("world_boss.menu_line", abilities.Count > 0 ? Loc.Get("world_boss.menu_ability") : "")}{answer}");
            }
            terminal.SetColor("white");
            terminal.Write($"  {Loc.Get("world_boss.action_prompt")}");
        }

        /// <summary>
        /// v1.1.5: Brace, Interrupt, Challenge. Each costs the round. Interrupt is one transaction on
        /// the shared counter and the player's row (both or neither), so nobody counts twice.
        /// </summary>
        private async Task ProcessAnswer(string input, Character player, TerminalEmulator terminal, SqlSaveBackend backend,
            WorldBossInfo boss, WorldBossAbility? live, WorldBossDefinition bossDef, string playerKey, WorldBossPlayerTelegraphState mine)
        {
            string shown = player.DisplayName;
            if (input == "F")
            {
                if (await backend.TryChallengeWorldBoss(boss.Id, playerKey, GameConfig.WorldBossChallengeHoldSeconds))
                {
                    terminal.SetColor("bright_red");
                    terminal.WriteLine($"  {Loc.Get("world_boss.you_challenge", bossDef.Name)}");
                    backend.LogWorldBossEvent(boss.Id, "focus", playerKey, "by=challenge");
                    TellFighters(backend, boss.Id, playerKey, lang => Loc.GetIn(lang, "world_boss.fighter_challenges", shown, bossDef.Name));
                }
                else
                {
                    terminal.SetColor("yellow");
                    var latest = await backend.GetWorldBossById(boss.Id);
                    terminal.WriteLine($"  {Loc.Get("world_boss.challenge_held", latest?.FocusPlayer ?? "")}");
                }
                return;
            }

            if (live == null || !boss.TelegraphLive)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  {Loc.Get("world_boss.nothing_to_answer")}");
                return;
            }
            if (mine.AnsweredSeq == boss.TelegraphSeq)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  {Loc.Get("world_boss.already_answered")}");
                return;
            }
            string name = bossDef.LocAbilityName(live);
            if (input == "T")
            {
                if (!live.IsChannel)
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine($"  {Loc.Get("world_boss.nothing_to_interrupt", name)}");
                    return;
                }
                if (await backend.InterruptWorldBoss(boss.Id, boss.TelegraphSeq, playerKey))
                {
                    var latest = await backend.GetWorldBossById(boss.Id);
                    int done = latest?.InterruptsDone ?? boss.InterruptsDone + 1, needed = latest?.InterruptsNeeded ?? boss.InterruptsNeeded;
                    terminal.SetColor("bright_cyan");
                    terminal.WriteLine($"  {Loc.Get("world_boss.you_interrupt", name, done, needed)}");
                    backend.LogWorldBossEvent(boss.Id, "interrupt", playerKey, $"{done}/{needed}", boss.TelegraphSeq);
                    TellFighters(backend, boss.Id, playerKey, lang => Loc.GetIn(lang, "world_boss.fighter_interrupts", shown, done, needed));
                }
                else
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"  {Loc.Get("world_boss.interrupt_late")}");
                }
                return;
            }

            // Brace: personal, no roll, no shared write
            if (await backend.RecordWorldBossAnswer(boss.Id, playerKey, boss.TelegraphSeq, "brace"))
            {
                terminal.SetColor("bright_cyan");
                terminal.WriteLine($"  {Loc.Get("world_boss.you_brace", name)}");
                backend.LogWorldBossEvent(boss.Id, "answer", playerKey, "brace", boss.TelegraphSeq);
                TellFighters(backend, boss.Id, playerKey, lang => Loc.GetIn(lang, "world_boss.fighter_braces", shown));
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  {Loc.Get("world_boss.already_answered")}");
            }
        }

        /// <summary>
        /// v1.1.5: a telegraph landed on this player. Strike: a tenth of max HP if braced, else three
        /// tenths and the status. Channel landed: three tenths on everyone, halved for the braced,
        /// with the status; a heal channel only heals (the tick did that). Broken: nothing but the
        /// stagger. Statuses that stop the player acting are not applied (a player who cannot act
        /// cannot answer the next one).
        /// </summary>
        internal void ApplyLandedTelegraph(WorldBossTelegraphOutcome landed, WorldBossPlayerTelegraphState mine,
            WorldBossDefinition bossDef, Character player, TerminalEmulator terminal, Random rng)
        {
            var ability = FindAbility(bossDef, landed.Id);
            string name = ability != null ? bossDef.LocAbilityName(ability) : landed.Id;
            bool answered = mine.AnsweredSeq == landed.Seq && mine.AnswerKind != "";
            if (landed.Outcome == "broken")
            {
                terminal.SetColor("bright_cyan");
                terminal.WriteLine($"  {Loc.Get("world_boss.channel_broken", bossDef.Name, name)}");
                return;
            }
            if (ability != null && ability.IsHeal)
            {
                terminal.SetColor("magenta");
                terminal.WriteLine($"  {Loc.Get("world_boss.heal_lands", name, bossDef.Name)}");
                return;
            }
            double pct = landed.Kind == "channel"
                ? (answered ? GameConfig.WorldBossTelegraphUnansweredPercent / 2 : GameConfig.WorldBossTelegraphUnansweredPercent)
                : (answered ? GameConfig.WorldBossTelegraphAnsweredPercent : GameConfig.WorldBossTelegraphUnansweredPercent);
            long dmg = Math.Max(1, (long)(player.MaxHP * pct));
            dmg = Math.Max(1, TeamHQBonus.ApplyDefense(player, dmg));   // v1.1.11: Team HQ Barracks on a telegraphed hit too
            player.HP = Math.Max(0, player.HP - dmg);
            terminal.SetColor(answered ? "yellow" : "bright_red");
            terminal.WriteLine(landed.Kind == "channel"
                ? $"  {Loc.Get("world_boss.channel_lands", name, $"{dmg:N0}", player.HP, player.MaxHP)}"
                : answered ? $"  {Loc.Get("world_boss.strike_lands_braced", name, $"{dmg:N0}", player.HP, player.MaxHP)}"
                           : $"  {Loc.Get("world_boss.strike_lands_full", name, $"{dmg:N0}", player.HP, player.MaxHP)}");

            if (!answered && ability?.AppliedStatus is { } status && status != StatusEffect.None && !status.PreventsAction())
            {
                if (player.HasStatusImmunity && player.StatusImmunityDuration > 0 && !ability.IsUnavoidable)
                {
                    terminal.SetColor("bright_white");
                    terminal.WriteLine($"  {Loc.Get("world_boss.resist_effect", status)}");
                    return;
                }
                double resistChance = 30.0 + player.Level * 0.5;
                if (ability.IsUnavoidable || rng.NextDouble() * 100 >= resistChance)
                {
                    player.ApplyStatus(status, ability.StatusDuration);
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"  {Loc.Get("world_boss.afflicted_with", status, ability.StatusDuration)}");
                }
            }
        }

        private async Task<long> ProcessPlayerAction(string input, Character player, TerminalEmulator terminal,
            WorldBossDefinition bossDef, WorldBossRuntimeData bossData, Random rng,
            WorldBossCombatState state)
        {
            long damage = 0;

            switch (input)
            {
                case "A": // Standard attack
                    damage = CalculatePlayerDamage(player, bossDef, bossData, rng);
                    terminal.SetColor("bright_green");
                    terminal.WriteLine($"  {Loc.Get("world_boss.you_strike", bossDef.Name, $"{damage:N0}")}");
                    break;

                case "C": // Cast spell
                    damage = await ProcessSpellCast(player, terminal, bossDef, bossData, rng);
                    break;

                case "D": // Defend
                    state.DefendingRounds = 2;
                    terminal.SetColor("bright_cyan");
                    terminal.WriteLine($"  {Loc.Get("world_boss.raise_guard")}");
                    break;

                case "I": // Use item (potion)
                    await ProcessUseItem(player, terminal);
                    break;

                case "P": // Power attack (high damage, lower accuracy)
                    damage = CalculatePowerAttackDamage(player, bossDef, bossData, rng, terminal);
                    break;

                case "E": // Precise strike (higher crit chance)
                    damage = CalculatePreciseStrikeDamage(player, bossDef, bossData, rng, terminal);
                    break;

                case "L": // Class ability
                    damage = await ProcessClassAbility(player, terminal, bossDef, bossData, rng, state.AbilityCooldowns);
                    break;

                // Quickbar shortcuts (1-9) — treat as class ability selection
                case "1": case "2": case "3": case "4": case "5":
                case "6": case "7": case "8": case "9":
                    damage = await ProcessClassAbility(player, terminal, bossDef, bossData, rng, state.AbilityCooldowns);
                    break;

                case "R": // Retreat
                    state.Retreated = true;
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"  {Loc.Get("world_boss.retreat")}");
                    break;

                default:
                    damage = CalculatePlayerDamage(player, bossDef, bossData, rng);
                    terminal.SetColor("bright_green");
                    terminal.WriteLine($"  {Loc.Get("world_boss.you_strike", bossDef.Name, $"{damage:N0}")}");
                    break;
            }

            return damage;
        }

        private long CalculatePlayerDamage(Character player, WorldBossDefinition bossDef,
            WorldBossRuntimeData bossData, Random rng, bool allowCrit = true)
        {
            // Active attack buff (Battle Cry / Focus / spell buffs) — only while its duration holds.
            long atkBonus = player.TempAttackBonusDuration > 0 ? player.TempAttackBonus : 0;

            // Standard damage formula: STR + WeapPow + bonuses - boss DEF/2
            long baseDamage = player.Strength + player.WeapPow + atkBonus;
            long defense = bossData.ScaledDefence / 2;
            long raw = Math.Max(1, baseDamage - defense);

            // Apply variance (70-130%)
            double variance = 0.7 + rng.NextDouble() * 0.6;
            long damage = (long)(raw * variance);

            // Real critical-hit chance (DEX + equipment crit bonus), matching the main combat engine
            // so gear and DEX investment actually pay off here instead of the old flat 5-50% curve.
            int critChance = StatEffectsSystem.GetCriticalHitChance(player.Dexterity, player.GetEquipmentCritChanceBonus());
            if (allowCrit && rng.Next(100) < critChance)
                damage = (long)(damage * 1.5);

            // BossSlayer bonus: +10% damage if any equipped item has BossSlayer effect
            if (HasSpecialEffect(player, LootGenerator.SpecialEffect.BossSlayer))
                damage = (long)(damage * 1.1);

            return Math.Max(1, damage);
        }

        private long CalculatePowerAttackDamage(Character player, WorldBossDefinition bossDef,
            WorldBossRuntimeData bossData, Random rng, TerminalEmulator terminal)
        {
            // 75% hit chance, but 1.5x damage
            if (rng.NextDouble() > 0.75)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  {Loc.Get("world_boss.power_attack_miss")}");
                return 0;
            }

            long damage = (long)(CalculatePlayerDamage(player, bossDef, bossData, rng) * 1.5);
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"  {Loc.Get("world_boss.power_attack_hit", bossDef.Name, $"{damage:N0}")}");
            return damage;
        }

        private long CalculatePreciseStrikeDamage(Character player, WorldBossDefinition bossDef,
            WorldBossRuntimeData bossData, Random rng, TerminalEmulator terminal)
        {
            // Always hits, higher crit chance (double normal), but 80% base damage.
            // v1.1.4: one crit roll, the doubled one below; the base used to roll the ordinary crit too.
            long baseDamage = (long)(CalculatePlayerDamage(player, bossDef, bossData, rng, allowCrit: false) * 0.8);

            // Extra crit check — double the real DEX/equipment crit chance, capped at 95%
            int critChance = Math.Min(95, StatEffectsSystem.GetCriticalHitChance(player.Dexterity, player.GetEquipmentCritChanceBonus()) * 2);
            if (rng.Next(100) < critChance)
            {
                baseDamage = (long)(baseDamage * 1.8);
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"  {Loc.Get("world_boss.precise_crit", bossDef.Name, $"{baseDamage:N0}")}");
            }
            else
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine($"  {Loc.Get("world_boss.precise_hit", bossDef.Name, $"{baseDamage:N0}")}");
            }
            return baseDamage;
        }

        private async Task<long> ProcessSpellCast(Character player, TerminalEmulator terminal,
            WorldBossDefinition bossDef, WorldBossRuntimeData bossData, Random rng)
        {
            // Show available spells
            if (!player.CanCastSpells())
            {
                terminal.SetColor("red");
                terminal.WriteLine($"  {Loc.Get("world_boss.cannot_cast")}");
                return 0;
            }

            // Check weapon requirement before listing spells
            if (!SpellSystem.HasRequiredSpellWeapon(player))
            {
                var required = SpellSystem.GetSpellWeaponRequirement(player.Class);
                terminal.SetColor("red");
                terminal.WriteLine($"  {Loc.Get("world_boss.need_weapon_spell", required)}");
                return 0;
            }

            var availableSpells = SpellSystem.GetAvailableSpells(player);
            if (availableSpells.Count == 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  {Loc.Get("world_boss.no_spells")}");
                return 0;
            }

            terminal.SetColor("bright_cyan");
            terminal.WriteLine(GameConfig.ScreenReaderMode ? $"  {Loc.Get("world_boss.available_spells")}" : $"  ─── {Loc.Get("world_boss.available_spells")} ───");
            var castableSpells = new List<SpellSystem.SpellInfo>();
            foreach (var spell in availableSpells)
            {
                if (SpellSystem.CanCastSpell(player, spell.Level))
                {
                    terminal.SetColor("cyan");
                    terminal.WriteLine($"  [{castableSpells.Count + 1}] {spell.Name} (Mana: {spell.ManaCost})");
                    castableSpells.Add(spell);
                }
            }

            if (castableSpells.Count == 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  {Loc.Get("world_boss.not_enough_mana")}");
                return 0;
            }

            terminal.SetColor("white");
            terminal.Write($"  {Loc.Get("world_boss.cast_which_spell")}");
            string spellInput = (await terminal.ReadLineAsync())?.Trim().ToUpper() ?? "";

            if (spellInput == "Q" || spellInput == "") return 0;

            if (int.TryParse(spellInput, out int spellIdx) && spellIdx >= 1 && spellIdx <= castableSpells.Count)
            {
                var selectedSpell = castableSpells[spellIdx - 1];
                var result = SpellSystem.CastSpell(player, selectedSpell.Level);

                if (result.Success)
                {
                    terminal.SetColor("bright_cyan");
                    terminal.WriteLine($"  {result.Message}");

                    long spellDamage = result.Damage;

                    // Healing spells heal the player instead
                    if (result.Healing > 0)
                    {
                        player.HP = Math.Min(player.MaxHP, player.HP + result.Healing);
                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"  {Loc.Get("world_boss.healed_for", result.Healing, player.HP, player.MaxHP)}");
                    }

                    // Protection/attack buffs — apply to the player's Temp* fields so the
                    // world-boss damage/defense math actually reads them (the old Shielded/Empowered
                    // statuses were cosmetic here; nothing in this loop consulted them).
                    int spellDur = result.Duration > 0 ? result.Duration : 3;
                    if (result.ProtectionBonus > 0)
                    {
                        player.TempDefenseBonus = Math.Max(player.TempDefenseBonus, result.ProtectionBonus);
                        player.TempDefenseBonusDuration = Math.Max(player.TempDefenseBonusDuration, spellDur);
                        terminal.SetColor("cyan");
                        terminal.WriteLine($"  {Loc.Get("world_boss.protection_increased", result.ProtectionBonus)}");
                    }
                    if (result.AttackBonus > 0)
                    {
                        player.TempAttackBonus = Math.Max(player.TempAttackBonus, result.AttackBonus);
                        player.TempAttackBonusDuration = Math.Max(player.TempAttackBonusDuration, spellDur);
                        terminal.SetColor("yellow");
                        terminal.WriteLine($"  {Loc.Get("world_boss.attack_increased", result.AttackBonus)}");
                    }

                    return Math.Max(0, spellDamage);
                }
                else
                {
                    terminal.SetColor("red");
                    terminal.WriteLine($"  {result.Message}");
                    return 0;
                }
            }

            return 0;
        }

        private async Task ProcessUseItem(Character player, TerminalEmulator terminal)
        {
            bool hasHealing = player.Healing > 0 && player.HP < player.MaxHP;
            bool hasMana = player.ManaPotions > 0 && player.Mana < player.MaxMana;

            if (!hasHealing && !hasMana)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  {Loc.Get("ui.no_healing_potions")}");
                return;
            }

            bool useMana = false;
            // If player has both types, let them choose
            if (hasHealing && hasMana)
            {
                terminal.WriteLine($"  {Loc.Get("combat.which_potion")}", "cyan");
                terminal.WriteLine($"    (H) {Loc.Get("world_boss.healing_potion")}  ({player.Healing}/{player.MaxPotions})", "green");
                terminal.WriteLine($"    (M) {Loc.Get("world_boss.mana_potion")}     ({player.ManaPotions}/{player.MaxManaPotions})", "blue");
                string choice = await terminal.GetInput("  > ");
                if (choice.Equals("M", StringComparison.OrdinalIgnoreCase)) useMana = true;
                // anything else defaults to healing
            }
            else if (hasMana)
            {
                useMana = true;
            }

            if (useMana)
                await DrinkManaPotions(player, terminal);
            else
                await DrinkHealingPotions(player, terminal);
        }

        /// <summary>
        /// Prompt for a quantity (number, or F = drink until full) and drink that many healing potions
        /// in a single action. The old loop drank exactly one per turn and handed the boss a free round
        /// for every sip — this is the "can only use 1 potion" report.
        /// </summary>
        private async Task DrinkHealingPotions(Character player, TerminalEmulator terminal)
        {
            int available = (int)Math.Min(player.Healing, int.MaxValue);
            terminal.SetColor("white");
            string input = (await terminal.GetInput($"  {Loc.Get("world_boss.potion_qty_heal", available)} "))?.Trim() ?? "";
            if (input.Equals("Q", StringComparison.OrdinalIgnoreCase) || input == "") return;

            int qty;
            if (input.Equals("F", StringComparison.OrdinalIgnoreCase))
                qty = available;
            else if (!int.TryParse(input, out qty) || qty < 1)
                qty = 1;
            qty = Math.Min(qty, available);

            long perPotion = (long)(player.MaxHP * 0.3);
            perPotion = PotionBonus.ApplyOwnerBonuses(player, perPotion); // v1.1.11: Infirmary, before the cap
            long totalHealed = 0;
            int drank = 0;
            for (int i = 0; i < qty && player.Healing > 0 && player.HP < player.MaxHP; i++)
            {
                long before = player.HP;
                player.HP = Math.Min(player.MaxHP, player.HP + perPotion);
                totalHealed += player.HP - before;
                player.Healing--;
                drank++;
            }

            if (drank == 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  {Loc.Get("ui.no_healing_potions")}");
                return;
            }

            terminal.SetColor("bright_green");
            terminal.WriteLine($"  {Loc.Get("world_boss.drank_heal_multi", drank, totalHealed, player.HP, player.MaxHP)}");
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("world_boss.potions_remaining", player.Healing)}");
        }

        private async Task DrinkManaPotions(Character player, TerminalEmulator terminal)
        {
            int available = (int)Math.Min(player.ManaPotions, int.MaxValue);
            terminal.SetColor("white");
            string input = (await terminal.GetInput($"  {Loc.Get("world_boss.potion_qty_mana", available)} "))?.Trim() ?? "";
            if (input.Equals("Q", StringComparison.OrdinalIgnoreCase) || input == "") return;

            int qty;
            if (input.Equals("F", StringComparison.OrdinalIgnoreCase))
                qty = available;
            else if (!int.TryParse(input, out qty) || qty < 1)
                qty = 1;
            qty = Math.Min(qty, available);

            long perPotion = 30 + player.Level * 5;
            long totalRestored = 0;
            int drank = 0;
            for (int i = 0; i < qty && player.ManaPotions > 0 && player.Mana < player.MaxMana; i++)
            {
                long before = player.Mana;
                player.Mana = Math.Min(player.MaxMana, player.Mana + perPotion);
                totalRestored += player.Mana - before;
                player.ManaPotions--;
                drank++;
            }

            if (drank == 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  {Loc.Get("world_boss.no_spells")}");
                return;
            }

            terminal.SetColor("bright_blue");
            terminal.WriteLine($"  {Loc.Get("world_boss.drank_mana_multi", drank, totalRestored, player.Mana, player.MaxMana)}");
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("world_boss.mana_potions_remaining", player.ManaPotions, player.MaxManaPotions)}");
        }

        private async Task<long> ProcessClassAbility(Character player, TerminalEmulator terminal,
            WorldBossDefinition bossDef, WorldBossRuntimeData bossData, Random rng,
            Dictionary<string, int> cooldowns)
        {
            var abilities = ClassAbilitySystem.GetAvailableAbilities(player);
            if (abilities.Count == 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  {Loc.Get("world_boss.no_abilities")}");
                return 0;
            }

            terminal.SetColor("bright_yellow");
            terminal.WriteLine(GameConfig.ScreenReaderMode ? $"  {Loc.Get("world_boss.class_abilities")}" : $"  ─── {Loc.Get("world_boss.class_abilities")} ───");
            var usable = new List<(int idx, ClassAbilitySystem.ClassAbility ability)>();
            for (int i = 0; i < abilities.Count; i++)
            {
                var ab = abilities[i];
                bool onCooldown = cooldowns.ContainsKey(ab.Id);
                bool canUse = ClassAbilitySystem.CanUseAbility(player, ab.Id, cooldowns);

                string cdStr = onCooldown ? $" (CD: {cooldowns[ab.Id]})" : "";
                terminal.SetColor(canUse ? "cyan" : "darkgray");
                terminal.WriteLine($"  [{i + 1}] {ab.Name}{cdStr}");
                if (canUse) usable.Add((i + 1, ab));
            }

            if (usable.Count == 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  {Loc.Get("world_boss.all_on_cooldown")}");
                return 0;
            }

            terminal.SetColor("white");
            terminal.Write($"  {Loc.Get("world_boss.use_which_ability")}");
            string abilityInput = (await terminal.ReadLineAsync())?.Trim().ToUpper() ?? "";

            if (abilityInput == "Q" || abilityInput == "") return 0;

            if (int.TryParse(abilityInput, out int abIdx))
            {
                var selected = usable.FirstOrDefault(u => u.idx == abIdx);
                if (selected.ability != null)
                {
                    var result = ClassAbilitySystem.UseAbility(player, selected.ability.Id, rng);
                    if (result.Success)
                    {
                        terminal.SetColor("bright_yellow");
                        terminal.WriteLine($"  {result.Message}");

                        if (result.CooldownApplied > 0)
                            cooldowns[selected.ability.Id] = result.CooldownApplied;

                        // Healing
                        if (result.Healing > 0)
                        {
                            player.HP = Math.Min(player.MaxHP, player.HP + result.Healing);
                            terminal.SetColor("bright_green");
                            terminal.WriteLine($"  {Loc.Get("world_boss.healed_for", result.Healing, player.HP, player.MaxHP)}");
                        }

                        // Apply buff effects to the player's own Temp* fields (the same mechanism the
                        // main combat engine uses), then the world-boss damage/defense math reads them.
                        // Without this, Battle Cry / Focus / Shield Wall / Iron Will printed flavor and
                        // did nothing — they were the "abilities don't work" report.
                        ApplyAbilityBuffsToPlayer(player, result, terminal);

                        return Math.Max(0, result.Damage);
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine($"  {result.Message}");
                    }
                }
            }

            return 0;
        }

        /// <summary>
        /// Apply a class ability's buff/defensive effects to the player's own Temp* fields, the same
        /// way CombatEngine does, so the world-boss damage and boss-attack math actually read them.
        /// Covers AttackBonus / DefenseBonus / Duration generically (Battle Cry, Focus, Shield Wall,
        /// Iron Will's +50 DEF, etc.) plus the headline SpecialEffect buffs the player can use here.
        /// </summary>
        private void ApplyAbilityBuffsToPlayer(Character player, ClassAbilityResult result, TerminalEmulator terminal)
        {
            int dur = result.Duration > 0 ? result.Duration : 1;

            if (result.AttackBonus > 0)
            {
                player.TempAttackBonus = Math.Max(player.TempAttackBonus, result.AttackBonus);
                player.TempAttackBonusDuration = Math.Max(player.TempAttackBonusDuration, dur);
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {Loc.Get("world_boss.buff_attack_dur", result.AttackBonus, player.TempAttackBonusDuration)}");
            }

            if (result.DefenseBonus > 0)
            {
                player.TempDefenseBonus = Math.Max(player.TempDefenseBonus, result.DefenseBonus);
                player.TempDefenseBonusDuration = Math.Max(player.TempDefenseBonusDuration, dur);
                terminal.SetColor("cyan");
                terminal.WriteLine($"  {Loc.Get("world_boss.buff_defense_dur", result.DefenseBonus, player.TempDefenseBonusDuration)}");
            }

            switch (result.SpecialEffect)
            {
                case "shield_wall_formation": // 30% incoming damage reduction
                    player.TempDamageReductionPercent = Math.Max(player.TempDamageReductionPercent, 30);
                    player.TempDamageReductionDuration = Math.Max(player.TempDamageReductionDuration, result.Duration > 0 ? result.Duration : 3);
                    terminal.SetColor("bright_cyan");
                    terminal.WriteLine($"  {Loc.Get("world_boss.buff_damage_reduction", 30, player.TempDamageReductionDuration)}");
                    break;

                case "resist_all":   // Iron Will — immune to debuffs for the duration
                case "immunity":
                    player.HasStatusImmunity = true;
                    player.StatusImmunityDuration = Math.Max(player.StatusImmunityDuration, result.Duration > 0 ? result.Duration : 3);
                    terminal.SetColor("bright_white");
                    terminal.WriteLine($"  {Loc.Get("world_boss.buff_status_immunity", player.StatusImmunityDuration)}");
                    break;
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Boss AI — Ability selection and attacks
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// v1.1.5: the boss's own action each round is a basic attack, twice in its last phase; the
        /// abilities are telegraphs now. A basic attack hits the focused player at one and a half
        /// times, everyone else at half. Defend still doubles defence against it.
        /// </summary>
        private void ProcessBossActions(WorldBossDefinition bossDef, WorldBossRuntimeData bossData,
            Character player, TerminalEmulator terminal, Random rng, WorldBossCombatState state, bool focused)
        {
            int defendingRounds = state.DefendingRounds;
            int attacks = bossData.CurrentPhase >= 3 ? 2 : 1;
            double mult = focused ? GameConfig.WorldBossFocusMultiplier : GameConfig.WorldBossOffFocusMultiplier;
            for (int i = 0; i < attacks && player.HP > 0; i++)
            {
                long beforeBarracks = Math.Max(1, (long)(CalculateBossBasicDamage(bossData, player, rng, defendingRounds) * mult));
                // v1.1.11: Team HQ Barracks last; the 20%-of-STR minimum holds where it held before.
                long bossDmg = Math.Max(Math.Min(beforeBarracks, BossMinimumDamage(bossData)), TeamHQBonus.ApplyDefense(player, beforeBarracks));
                player.HP = Math.Max(0, player.HP - bossDmg);
                terminal.SetColor("bright_red");
                terminal.WriteLine($"  {Loc.Get(focused ? "world_boss.boss_strikes_focused" : "world_boss.boss_strikes", bossDef.Name, $"{bossDmg:N0}", player.HP, player.MaxHP)}");
            }
        }

        // Defense never takes a boss basic attack below 20% of the boss's strength.
        private static long BossMinimumDamage(WorldBossRuntimeData bossData) => Math.Max(1, bossData.ScaledStrength / 5);

        private long CalculateBossBasicDamage(WorldBossRuntimeData bossData, Character player, Random rng,
            int defendingRounds)
        {
            long bossStr = bossData.ScaledStrength;

            // Active defense buff (Shield Wall / Iron Will / spell protection) — only while it holds.
            long defBonus = player.TempDefenseBonusDuration > 0 ? player.TempDefenseBonus : 0;
            long playerDef = player.Defence + player.ArmPow + defBonus;

            // Defending doubles effective defense
            if (defendingRounds > 0)
                playerDef *= 2;

            // TitanResolve: +5% max HP effective defense
            if (HasSpecialEffect(player, LootGenerator.SpecialEffect.TitanResolve))
                playerDef += (long)(player.MaxHP * 0.05);

            // Defense can reduce damage but never below 20% of boss strength
            // World bosses are meant to be dangerous — pure defense stacking shouldn't trivialize them
            long raw = Math.Max(1, bossStr - playerDef / 2);
            raw = Math.Max(raw, BossMinimumDamage(bossData));
            double variance = 0.7 + rng.NextDouble() * 0.6;
            long final = Math.Max(1, (long)(raw * variance));

            // Shield Wall Formation flat % damage reduction (applies after the defense curve).
            if (player.TempDamageReductionDuration > 0 && player.TempDamageReductionPercent > 0)
                final = Math.Max(1, final - final * player.TempDamageReductionPercent / 100);

            return final;
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Phase Transitions
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>v1.1.4: the tick raised the phase; this session shows it once.</summary>
        private async Task ShowPhaseChange(int newPhase, WorldBossDefinition bossDef, TerminalEmulator terminal)
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("");
            terminal.WriteLine($"  *** {Loc.Get("world_boss.phase_label", newPhase, 3)} — {GetPhaseDescription(newPhase)} ***");
            string[]? dialogue = newPhase == 2 ? bossDef.LocPhase2() : bossDef.LocPhase3();
            if (dialogue != null)
            {
                terminal.SetColor(bossDef.ThemeColor);
                foreach (var line in dialogue)
                {
                    terminal.WriteLine($"  {line}");
                    await Task.Delay(800);
                }
            }
            terminal.WriteLine("");
        }

        private string GetPhaseDescription(int phase) => phase switch
        {
            2 => Loc.Get("world_boss.phase_2_desc"),
            3 => Loc.Get("world_boss.phase_3_desc"),
            _ => Loc.Get("world_boss.phase_1_desc")
        };

        // Language-explicit variant for per-recipient broadcasts.
        private string GetPhaseDescriptionIn(string lang, int phase) => phase switch
        {
            2 => Loc.GetIn(lang, "world_boss.phase_2_desc"),
            3 => Loc.GetIn(lang, "world_boss.phase_3_desc"),
            _ => Loc.GetIn(lang, "world_boss.phase_1_desc")
        };

        // ═══════════════════════════════════════════════════════════════════════════
        // Helper Methods
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>v1.1.4: equipped slots only; a boss-slayer blade in the bag used to count.</summary>
        public static bool HasSpecialEffect(Character player, LootGenerator.SpecialEffect effect)
        {
            foreach (var kvp in player.EquippedItems)
            {
                if (kvp.Value <= 0) continue;
                var eq = EquipmentDatabase.GetById(kvp.Value);
                if (eq == null) continue;
                if (effect == LootGenerator.SpecialEffect.BossSlayer && eq.HasBossSlayer) return true;
                if (effect == LootGenerator.SpecialEffect.TitanResolve && eq.HasTitanResolve) return true;
            }
            return false;
        }

        private WorldBossRuntimeData? DeserializeRuntimeData(string json)
        {
            try
            {
                if (string.IsNullOrEmpty(json) || json == "{}") return null;
                return JsonSerializer.Deserialize<WorldBossRuntimeData>(json);
            }
            catch
            {
                return null;
            }
        }

    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Runtime data structure — serialized as JSON in boss_data_json column
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Mutable combat state passed between async methods (avoids ref params in async).
    /// </summary>
    public class WorldBossCombatState
    {
        public int Round { get; set; }
        public long SessionDamage { get; set; }
        public bool Retreated { get; set; }
        public bool Died { get; set; }
        public Dictionary<string, int> AbilityCooldowns { get; } = new();
        public int DefendingRounds { get; set; }
        // v1.1.4
        public bool Killed { get; set; }
        public double Ratio { get; set; } = 1.0;
        public long RoundCap { get; set; } = long.MaxValue;
        public int BossId { get; set; }
        public long BossMaxHP { get; set; }
    }

    public class WorldBossRuntimeData
    {
        public string DefinitionId { get; set; } = "";
        public int CurrentPhase { get; set; } = 1;
        public int ScaledLevel { get; set; }
        public long ScaledStrength { get; set; }
        public long ScaledDefence { get; set; }
        public long ScaledAgility { get; set; }
        public int AttacksPerRound { get; set; } = 2;
    }
}
