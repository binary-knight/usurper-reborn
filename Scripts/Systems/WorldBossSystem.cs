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

        // Per-player death cooldown tracking (username -> UTC time when they can re-enter)
        private readonly Dictionary<string, DateTime> _deathCooldowns = new();

        /// <summary>Last known active boss name for notification display. Set on spawn, cleared on death/despawn.</summary>
        public volatile string? ActiveBossName;

        // ═══════════════════════════════════════════════════════════════════════════
        // Spawn System — Called from WorldSimService tick loop
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Check if conditions are met to spawn a new world boss.
        /// Called every 30s from WorldSimService tick loop.
        /// </summary>
        public async Task CheckSpawnConditions(SqlSaveBackend backend)
        {
            try
            {
                // Expire any old bosses first
                await backend.ExpireWorldBosses();

                // Don't spawn if one is already active
                var activeBoss = await backend.GetActiveWorldBoss();
                if (activeBoss != null) return;

                // Clear notification if boss despawned
                ActiveBossName = null;

                // Check minimum player count
                int onlineCount = backend.GetOnlinePlayerCount();
                if (onlineCount < GameConfig.WorldBossMinPlayersToSpawn) return;

                // Cooldown: don't spawn if a boss was defeated/expired recently
                var lastBossTime = backend.GetLastWorldBossEndTime();
                if (lastBossTime.HasValue)
                {
                    double hoursSinceLast = (DateTime.UtcNow - lastBossTime.Value).TotalHours;
                    if (hoursSinceLast < GameConfig.WorldBossSpawnCooldownHours)
                    {
                        return;
                    }
                }

                // Pick a random boss
                var bossDef = WorldBossDatabase.GetRandomBoss();
                if (bossDef == null) return;

                // Scale HP based on online player count
                int avgLevel = backend.GetAverageOnlineLevel();
                int bossLevel = Math.Max(bossDef.BaseLevel, avgLevel);
                long scaledHP = (long)(bossDef.BaseHP * (1.0 + GameConfig.WorldBossHPScalePerPlayer * onlineCount));

                // Create boss data JSON for phase tracking
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
                string dataJson = JsonSerializer.Serialize(bossData);

                // Spawn it
                int bossId = await backend.SpawnWorldBoss(
                    bossDef.Name, bossLevel, scaledHP,
                    GameConfig.WorldBossDurationHours, dataJson);

                if (bossId > 0)
                {
                    ActiveBossName = bossDef.Name;
                    DebugLogger.Instance.LogInfo("WORLD_BOSS", $"Spawned {bossDef.Name} (Lv{bossLevel}, HP:{scaledHP:N0}) with {onlineCount} players online");

                    // Broadcast spawn to all online players
                    string spawnMsg = $"\n  *** {Loc.Get("world_boss.spawn_broadcast", bossDef.Name, bossDef.Title)} ***\n  {Loc.Get("world_boss.type_boss_to_join")}";
                    MudServer.Instance?.BroadcastToAll(spawnMsg);

                    // Post to news feed
                    if (OnlineStateManager.IsActive)
                    {
                        _ = OnlineStateManager.Instance!.AddNews(
                            Loc.Get("world_boss.spawn_news", bossDef.Name, bossDef.Title), "world_boss");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("WORLD_BOSS", $"Spawn check failed: {ex.Message}");
            }
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

            while (true)
            {
                terminal.ClearScreen();

                // Expire old bosses
                await backend.ExpireWorldBosses();
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
                DrawLeaderboard(terminal, leaderboard, player.DisplayName);

                // Menu
                terminal.SetColor("cyan");
                terminal.WriteLine($"  [A] {Loc.Get("world_boss.attack_boss")}    [Q] {Loc.Get("ui.back")}");
                terminal.SetColor("white");
                terminal.Write($"\n  {Loc.Get("ui.your_choice")}");
                string input = (await terminal.ReadLineAsync())?.Trim().ToUpper() ?? "";

                if (input == "Q" || input == "") break;

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
            terminal.WriteLine($"  {Loc.Get("world_boss.appear_when_enough")}");
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine($"  {Loc.Get("world_boss.spawn_info")}");
            terminal.WriteLine($"  {Loc.Get("world_boss.duration_info")}");
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

        private void DrawLeaderboard(TerminalEmulator terminal, List<WorldBossDamageEntry> leaderboard, string playerName)
        {
            if (leaderboard.Count == 0) return;

            long totalDamage = leaderboard.Sum(e => e.DamageDealt);

            terminal.SetColor("bright_yellow");
            terminal.WriteLine(GameConfig.ScreenReaderMode ? $"  {Loc.Get("world_boss.damage_leaderboard")}" : $"  ═══ {Loc.Get("world_boss.damage_leaderboard")} ═══");
            for (int i = 0; i < leaderboard.Count; i++)
            {
                var entry = leaderboard[i];
                double pct = totalDamage > 0 ? (double)entry.DamageDealt / totalDamage * 100 : 0;
                bool isPlayer = entry.PlayerName.Equals(playerName, StringComparison.OrdinalIgnoreCase);

                string color = i == 0 ? "bright_yellow" : i < 3 ? "yellow" : isPlayer ? "bright_cyan" : "white";
                string marker = i == 0 ? $" {Loc.Get("world_boss.mvp_tag")}" : "";
                string youTag = isPlayer ? $" ({Loc.Get("world_boss.you_tag")})" : "";

                terminal.SetColor(color);
                terminal.WriteLine($"  {i + 1,2}. {entry.PlayerName,-18} {entry.DamageDealt,10:N0} dmg  {pct,5:F1}%{marker}{youTag}");
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
            // Check death cooldown
            string playerKey = player.DisplayName.ToLowerInvariant();
            if (_deathCooldowns.TryGetValue(playerKey, out var cooldownEnd) && DateTime.UtcNow < cooldownEnd)
            {
                int secsLeft = (int)(cooldownEnd - DateTime.UtcNow).TotalSeconds;
                terminal.SetColor("red");
                terminal.WriteLine($"\n  {Loc.Get("world_boss.death_cooldown", secsLeft)}");
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

            terminal.ClearScreen();
            terminal.SetColor(bossDef.ThemeColor);
            terminal.WriteLine($"\n  {Loc.Get("world_boss.engage", bossDef.Name, bossDef.Title)}");
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("world_boss.phase_label", bossData.CurrentPhase, 3)} — {Loc.Get("world_boss.prepare_yourself")}\n");
            await Task.Delay(1000);

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

                // Update phase from DB (another player may have triggered a phase change)
                var latestData = DeserializeRuntimeData(currentBoss.BossDataJson);
                if (latestData != null)
                    bossData.CurrentPhase = latestData.CurrentPhase;

                // Check for phase transitions
                await CheckPhaseTransition(currentBoss, bossData, bossDef, backend, terminal);

                // ─── Round header ───
                double hpPct = currentBoss.MaxHP > 0 ? (double)currentBoss.CurrentHP / currentBoss.MaxHP * 100 : 0;
                string hpColor = hpPct > 50 ? "bright_green" : hpPct > 25 ? "bright_yellow" : "bright_red";

                terminal.SetColor("white");
                if (GameConfig.ScreenReaderMode)
                    terminal.WriteLine($"  {Loc.Get("world_boss.round", state.Round)}");
                else
                    terminal.WriteLine($"  ─── {Loc.Get("world_boss.round", state.Round)} ───");
                terminal.SetColor(hpColor);
                if (GameConfig.ScreenReaderMode)
                    terminal.WriteLine($"  {Loc.Get("world_boss.boss_hp_label")}: {currentBoss.CurrentHP:N0}/{currentBoss.MaxHP:N0} ({hpPct:F1}%)");
                else
                {
                    int barFilled = Math.Clamp((int)(hpPct / 5), 0, 20);
                    string hpBar = new string('█', barFilled) + new string('░', 20 - barFilled);
                    terminal.WriteLine($"  {Loc.Get("world_boss.boss_hp_label")}: [{hpBar}] {currentBoss.CurrentHP:N0}/{currentBoss.MaxHP:N0} ({hpPct:F1}%)");
                }
                terminal.SetColor("cyan");
                terminal.WriteLine($"  {Loc.Get("world_boss.your_hp_label")}: {player.HP}/{player.MaxHP}  {Loc.Get("world_boss.mana_label")}: {player.Mana}/{player.MaxMana}  {Loc.Get("world_boss.phase_short_label")}: {bossData.CurrentPhase}");

                // Show player status effects
                if (player.ActiveStatuses.Count > 0)
                {
                    var statusList = player.ActiveStatuses.Select(kv => $"{kv.Key}({kv.Value})");
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"  {Loc.Get("world_boss.status_label")}: {string.Join(", ", statusList)}");
                }
                terminal.WriteLine("");

                // ─── Process player status effects (DoT, duration tick-down) ───
                var statusMessages = player.ProcessStatusEffects();
                foreach (var (msg, color) in statusMessages)
                {
                    terminal.SetColor(color);
                    terminal.WriteLine($"  {msg}");
                }

                // Check if player can act after status processing
                if (player.HP <= 0) break;

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
                    ShowWorldBossActionMenu(terminal, player, state.AbilityCooldowns);
                    string input = (await terminal.ReadLineAsync())?.Trim().ToUpper() ?? "A";

                    long roundDamage = await ProcessPlayerAction(input, player, terminal, bossDef, bossData,
                        rng, state);

                    if (state.Retreated) break;

                    if (roundDamage > 0)
                    {
                        // Record damage atomically to shared HP pool
                        long remainingHp = await backend.RecordWorldBossDamage(
                            currentBoss.Id, playerKey, roundDamage);
                        state.SessionDamage += roundDamage;

                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"  >> {Loc.Get("world_boss.total_round_damage", $"{roundDamage:N0}")}");
                        terminal.SetColor("gray");
                        terminal.WriteLine($"  >> {Loc.Get("world_boss.boss_hp_remaining", $"{Math.Max(0, remainingHp):N0}")}");

                        if (remainingHp <= 0)
                        {
                            ActiveBossName = null;
                            terminal.SetColor("bright_green");
                            terminal.WriteLine($"\n  *** {Loc.Get("world_boss.has_been_defeated", bossDef.Name)} ***");
                            terminal.SetColor("yellow");
                            terminal.WriteLine($"  {Loc.Get("world_boss.killing_blow")}");

                            // Broadcast
                            string killMsg = $"\n  *** {Loc.Get("world_boss.defeat_broadcast", bossDef.Name, player.DisplayName)} ***";
                            MudServer.Instance?.BroadcastToAll(killMsg, playerKey);

                            // News
                            if (OnlineStateManager.IsActive)
                                _ = OnlineStateManager.Instance!.AddNews(
                                    Loc.Get("world_boss.defeat_broadcast", bossDef.Name, player.DisplayName), "world_boss");

                            // Distribute rewards to all contributors
                            await DistributeWorldBossRewards(currentBoss.Id, bossDef, currentBoss.MaxHP,
                                currentBoss.BossLevel, backend, player, terminal);
                            break;
                        }
                    }
                }

                // ─── Boss actions ───
                if (player.HP > 0 && !state.Retreated)
                {
                    await ProcessBossActions(bossDef, bossData, player, terminal, rng,
                        state.DefendingRounds, state.TempDefenseBonus);
                }

                // ─── Presence aura (unavoidable damage each round) ───
                if (player.HP > 0 && !state.Retreated)
                {
                    float auraMult = bossData.CurrentPhase switch
                    {
                        2 => GameConfig.WorldBossAuraPhase2Mult,
                        3 => GameConfig.WorldBossAuraPhase3Mult,
                        _ => 1.0f
                    };
                    float auraPercent = bossDef.AuraBaseDamagePercent * auraMult;
                    long auraDamage = Math.Max(1, (long)(player.MaxHP * auraPercent));

                    // Defending reduces aura damage by 50%
                    if (state.DefendingRounds > 0)
                        auraDamage = auraDamage / 2;

                    player.HP = Math.Max(0, player.HP - auraDamage);

                    terminal.SetColor("magenta");
                    terminal.WriteLine($"  {Loc.Get("world_boss.aura_damage", bossDef.Name, $"{auraDamage:N0}")}");
                    terminal.SetColor("cyan");
                    terminal.WriteLine($"  {Loc.Get("world_boss.your_hp_label")}: {player.HP}/{player.MaxHP}");
                }

                // Decrement defend counter
                if (state.DefendingRounds > 0) state.DefendingRounds--;

                // Tick ability cooldowns
                foreach (var key in state.AbilityCooldowns.Keys.ToList())
                {
                    state.AbilityCooldowns[key]--;
                    if (state.AbilityCooldowns[key] <= 0)
                        state.AbilityCooldowns.Remove(key);
                }

                // Player death check
                if (player.HP <= 0)
                {
                    state.Died = true;
                    terminal.SetColor("bright_red");
                    terminal.WriteLine($"\n  {Loc.Get("world_boss.struck_you_down", bossDef.Name)}");
                    terminal.SetColor("gray");
                    terminal.WriteLine($"  {Loc.Get("world_boss.total_damage_before_fall", $"{state.SessionDamage:N0}")}");

                    // Revive with 25% HP, apply cooldown
                    player.HP = Math.Max(1, player.MaxHP / 4);
                    _deathCooldowns[playerKey] = DateTime.UtcNow.AddSeconds(GameConfig.WorldBossDeathCooldownSeconds);

                    terminal.SetColor("cyan");
                    terminal.WriteLine($"  {Loc.Get("world_boss.healers_safety", player.HP, player.MaxHP)}");
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"  {Loc.Get("world_boss.must_wait", GameConfig.WorldBossDeathCooldownSeconds)}");
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

        private void ShowWorldBossActionMenu(TerminalEmulator terminal, Character player,
            Dictionary<string, int> cooldowns)
        {
            if (GameConfig.ScreenReaderMode)
            {
                terminal.SetColor("bright_white");
                terminal.WriteLine($"  {Loc.Get("world_boss.choose_action")}");
                terminal.SetColor("cyan");
                terminal.WriteLine($"  A. {Loc.Get("world_boss.action_attack")}");
                terminal.WriteLine($"  C. {Loc.Get("world_boss.action_cast")}");
                terminal.WriteLine($"  D. {Loc.Get("world_boss.action_defend")}");
                terminal.WriteLine($"  I. {Loc.Get("world_boss.action_item")}");
                terminal.WriteLine($"  P. {Loc.Get("world_boss.action_power")}");
                terminal.WriteLine($"  E. {Loc.Get("world_boss.action_precise")}");
                var abilities = ClassAbilitySystem.GetAvailableAbilities(player);
                if (abilities.Count > 0)
                    terminal.WriteLine($"  L. {Loc.Get("world_boss.action_ability")}  (or 1-9 for quickbar)");
                terminal.WriteLine($"  R. {Loc.Get("world_boss.action_retreat")}");
            }
            else
            {
                terminal.SetColor("green");
                terminal.WriteLine("╔═══════════════════════════════════════╗");
                terminal.Write("║");
                terminal.SetColor("bright_white");
                terminal.Write($"           {Loc.Get("world_boss.choose_action")}          ");
                terminal.SetColor("green");
                terminal.WriteLine("║");
                terminal.WriteLine("╠═══════════════════════════════════════╣");

                // Basic actions
                terminal.Write("║ ");
                terminal.SetColor("bright_white"); terminal.Write("[A]");
                terminal.SetColor("cyan"); terminal.Write("ttack  ");
                terminal.SetColor("bright_white"); terminal.Write("[C]");
                terminal.SetColor("cyan"); terminal.Write("ast Spell  ");
                terminal.SetColor("bright_white"); terminal.Write("[D]");
                terminal.SetColor("cyan"); terminal.Write("efend     ");
                terminal.SetColor("green"); terminal.WriteLine("║");

                terminal.Write("║ ");
                terminal.SetColor("bright_white"); terminal.Write("[I]");
                terminal.SetColor("cyan"); terminal.Write("tem    ");
                terminal.SetColor("bright_white"); terminal.Write("[P]");
                terminal.SetColor("cyan"); terminal.Write("ower Attack ");
                terminal.SetColor("bright_white"); terminal.Write("[E]");
                terminal.SetColor("cyan"); terminal.Write("Precise   ");
                terminal.SetColor("green"); terminal.WriteLine("║");

                // Class abilities
                var abilities = ClassAbilitySystem.GetAvailableAbilities(player);
                if (abilities.Count > 0)
                {
                    terminal.Write("║ ");
                    terminal.SetColor("bright_white"); terminal.Write("[L]");
                    terminal.SetColor("cyan"); terminal.Write("Ability ");
                    terminal.SetColor("bright_white"); terminal.Write("[R]");
                    terminal.SetColor("cyan"); terminal.Write("etreat                   ");
                    terminal.SetColor("green"); terminal.WriteLine("║");
                }
                else
                {
                    terminal.Write("║ ");
                    terminal.SetColor("bright_white"); terminal.Write("[R]");
                    terminal.SetColor("cyan"); terminal.Write("etreat                              ");
                    terminal.SetColor("green"); terminal.WriteLine("║");
                }

                terminal.SetColor("green");
                terminal.WriteLine("╚═══════════════════════════════════════╝");
            }

            terminal.SetColor("white");
            terminal.Write($"  {Loc.Get("world_boss.action_prompt")}");
        }

        private async Task<long> ProcessPlayerAction(string input, Character player, TerminalEmulator terminal,
            WorldBossDefinition bossDef, WorldBossRuntimeData bossData, Random rng,
            WorldBossCombatState state)
        {
            long damage = 0;

            switch (input)
            {
                case "A": // Standard attack
                    damage = CalculatePlayerDamage(player, bossDef, bossData, rng, state.TempAttackBonus);
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
                    damage = CalculatePowerAttackDamage(player, bossDef, bossData, rng, state.TempAttackBonus, terminal);
                    break;

                case "E": // Precise strike (higher crit chance)
                    damage = CalculatePreciseStrikeDamage(player, bossDef, bossData, rng, state.TempAttackBonus, terminal);
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
                    damage = CalculatePlayerDamage(player, bossDef, bossData, rng, state.TempAttackBonus);
                    terminal.SetColor("bright_green");
                    terminal.WriteLine($"  {Loc.Get("world_boss.you_strike", bossDef.Name, $"{damage:N0}")}");
                    break;
            }

            return damage;
        }

        private long CalculatePlayerDamage(Character player, WorldBossDefinition bossDef,
            WorldBossRuntimeData bossData, Random rng, int tempAtkBonus)
        {
            // Standard damage formula: STR + WeapPow + bonuses - boss DEF/2
            long baseDamage = player.Strength + player.WeapPow + tempAtkBonus;
            long defense = bossData.ScaledDefence / 2;
            long raw = Math.Max(1, baseDamage - defense);

            // Apply variance (70-130%)
            double variance = 0.7 + rng.NextDouble() * 0.6;
            long damage = (long)(raw * variance);

            // Critical hit check (5-50% chance based on DEX)
            double critChance = Math.Clamp(5.0 + player.Dexterity * 0.5, 5.0, 50.0);
            if (rng.NextDouble() * 100 < critChance)
            {
                damage = (long)(damage * 1.5);
            }

            // BossSlayer bonus: +10% damage if any equipped item has BossSlayer effect
            if (HasSpecialEffect(player, LootGenerator.SpecialEffect.BossSlayer))
                damage = (long)(damage * 1.1);

            return Math.Max(1, damage);
        }

        private long CalculatePowerAttackDamage(Character player, WorldBossDefinition bossDef,
            WorldBossRuntimeData bossData, Random rng, int tempAtkBonus, TerminalEmulator terminal)
        {
            // 75% hit chance, but 1.5x damage
            if (rng.NextDouble() > 0.75)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  {Loc.Get("world_boss.power_attack_miss")}");
                return 0;
            }

            long damage = (long)(CalculatePlayerDamage(player, bossDef, bossData, rng, tempAtkBonus) * 1.5);
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"  {Loc.Get("world_boss.power_attack_hit", bossDef.Name, $"{damage:N0}")}");
            return damage;
        }

        private long CalculatePreciseStrikeDamage(Character player, WorldBossDefinition bossDef,
            WorldBossRuntimeData bossData, Random rng, int tempAtkBonus, TerminalEmulator terminal)
        {
            // Always hits, higher crit chance (double normal), but 80% base damage
            long baseDamage = (long)(CalculatePlayerDamage(player, bossDef, bossData, rng, tempAtkBonus) * 0.8);

            // Extra crit check
            double critChance = Math.Clamp(10.0 + player.Dexterity * 1.0, 10.0, 75.0);
            if (rng.NextDouble() * 100 < critChance)
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

                    // Protection/attack buffs
                    if (result.ProtectionBonus > 0)
                    {
                        player.ApplyStatus(StatusEffect.Shielded, result.Duration > 0 ? result.Duration : 3);
                        terminal.SetColor("cyan");
                        terminal.WriteLine($"  {Loc.Get("world_boss.protection_increased", result.ProtectionBonus)}");
                    }
                    if (result.AttackBonus > 0)
                    {
                        player.ApplyStatus(StatusEffect.Empowered, result.Duration > 0 ? result.Duration : 3);
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

            // If player has both types, let them choose
            if (hasHealing && hasMana)
            {
                terminal.WriteLine($"  {Loc.Get("combat.which_potion")}", "cyan");
                terminal.WriteLine($"    (H) {Loc.Get("world_boss.healing_potion")}  ({player.Healing}/{player.MaxPotions})", "green");
                terminal.WriteLine($"    (M) {Loc.Get("world_boss.mana_potion")}     ({player.ManaPotions}/{player.MaxManaPotions})", "blue");
                string choice = await terminal.GetInput("  > ");
                if (choice.Equals("M", StringComparison.OrdinalIgnoreCase))
                {
                    UseManaPotion(player, terminal);
                    return;
                }
                // Default to healing potion
            }
            else if (!hasHealing && hasMana)
            {
                UseManaPotion(player, terminal);
                return;
            }

            // Healing potion
            long healAmount = (long)(player.MaxHP * 0.3);
            player.HP = Math.Min(player.MaxHP, player.HP + healAmount);
            player.Healing--;

            terminal.SetColor("bright_green");
            terminal.WriteLine($"  {Loc.Get("world_boss.drink_potion", healAmount, player.HP, player.MaxHP)}");
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("world_boss.potions_remaining", player.Healing)}");
        }

        private void UseManaPotion(Character player, TerminalEmulator terminal)
        {
            long manaRestore = 30 + player.Level * 5;
            long manaNeeded = player.MaxMana - player.Mana;
            long actualRestore = Math.Min(manaRestore, manaNeeded);
            player.Mana += actualRestore;
            player.ManaPotions--;

            terminal.SetColor("bright_blue");
            terminal.WriteLine($"  {Loc.Get("world_boss.mana_potion_used", actualRestore)}");
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

        // ═══════════════════════════════════════════════════════════════════════════
        // Boss AI — Ability selection and attacks
        // ═══════════════════════════════════════════════════════════════════════════

        private async Task ProcessBossActions(WorldBossDefinition bossDef, WorldBossRuntimeData bossData,
            Character player, TerminalEmulator terminal, Random rng,
            int defendingRounds, int playerDefBonus)
        {
            // Boss gets multiple attacks per round
            int attacks = bossData.AttacksPerRound;
            if (bossData.CurrentPhase >= 3) attacks++; // Extra attack in phase 3

            for (int i = 0; i < attacks && player.HP > 0; i++)
            {
                // Select ability for this attack
                var abilities = GetPhaseAbilities(bossDef, bossData.CurrentPhase);
                WorldBossAbility? ability = null;

                if (abilities.Count > 0)
                {
                    // 60% chance to use an ability, 40% basic attack
                    if (rng.NextDouble() < 0.6)
                        ability = abilities[rng.Next(abilities.Count)];
                }

                if (ability != null)
                {
                    await ProcessBossAbility(ability, bossDef, bossData, player, terminal, rng,
                        defendingRounds, playerDefBonus);
                }
                else
                {
                    // Basic attack
                    long bossDmg = CalculateBossBasicDamage(bossData, player, rng, defendingRounds, playerDefBonus);
                    player.HP = Math.Max(0, player.HP - bossDmg);

                    terminal.SetColor("bright_red");
                    terminal.WriteLine($"  {Loc.Get("world_boss.boss_strikes", bossDef.Name, $"{bossDmg:N0}", player.HP, player.MaxHP)}");
                }
            }
        }

        private async Task ProcessBossAbility(WorldBossAbility ability, WorldBossDefinition bossDef,
            WorldBossRuntimeData bossData, Character player, TerminalEmulator terminal, Random rng,
            int defendingRounds, int playerDefBonus)
        {
            terminal.SetColor(bossDef.ThemeColor);
            terminal.WriteLine($"  {Loc.Get("world_boss.boss_uses_ability", bossDef.Name, ability.Name)}");

            // Calculate ability damage
            long baseDmg = CalculateBossBasicDamage(bossData, player, rng, defendingRounds, playerDefBonus);
            long abilityDmg = (long)(baseDmg * ability.DamageMultiplier);

            // Unavoidable abilities bypass defense
            if (ability.IsUnavoidable)
            {
                abilityDmg = (long)(bossData.ScaledStrength * ability.DamageMultiplier * (0.8 + rng.NextDouble() * 0.4));
                if (defendingRounds > 0) abilityDmg = abilityDmg * 3 / 4; // Defending still helps a bit
            }

            if (abilityDmg > 0)
            {
                player.HP = Math.Max(0, player.HP - abilityDmg);
                terminal.SetColor("bright_red");
                terminal.WriteLine($"  {ability.Description} ({abilityDmg:N0} {Loc.Get("world_boss.damage_suffix")})");
            }

            // Apply status effect
            if (ability.AppliedStatus.HasValue && ability.AppliedStatus.Value != StatusEffect.None)
            {
                // Status resist check: 30% base resist, +0.5% per player level
                double resistChance = 30.0 + player.Level * 0.5;
                if (ability.IsUnavoidable || rng.NextDouble() * 100 >= resistChance)
                {
                    player.ApplyStatus(ability.AppliedStatus.Value, ability.StatusDuration);
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"  {Loc.Get("world_boss.afflicted_with", ability.AppliedStatus.Value, ability.StatusDuration)}");
                }
                else
                {
                    terminal.SetColor("cyan");
                    terminal.WriteLine($"  {Loc.Get("world_boss.resist_effect", ability.AppliedStatus.Value)}");
                }
            }

            // Self-heal
            if (ability.SelfHealPercent > 0)
            {
                terminal.SetColor("magenta");
                terminal.WriteLine($"  {Loc.Get("world_boss.boss_regenerates", bossDef.Name)}");
            }

            terminal.SetColor("cyan");
            terminal.WriteLine($"  {Loc.Get("world_boss.your_hp_label")}: {player.HP}/{player.MaxHP}");
        }

        private long CalculateBossBasicDamage(WorldBossRuntimeData bossData, Character player, Random rng,
            int defendingRounds, int playerDefBonus)
        {
            long bossStr = bossData.ScaledStrength;
            long playerDef = player.Defence + player.ArmPow + playerDefBonus;

            // Defending doubles effective defense
            if (defendingRounds > 0)
                playerDef *= 2;

            // TitanResolve: +5% max HP effective defense
            if (HasSpecialEffect(player, LootGenerator.SpecialEffect.TitanResolve))
                playerDef += (long)(player.MaxHP * 0.05);

            // Defense can reduce damage but never below 20% of boss strength
            // World bosses are meant to be dangerous — pure defense stacking shouldn't trivialize them
            long raw = Math.Max(1, bossStr - playerDef / 2);
            long minDamage = Math.Max(1, bossStr / 5);
            raw = Math.Max(raw, minDamage);
            double variance = 0.7 + rng.NextDouble() * 0.6;

            return Math.Max(1, (long)(raw * variance));
        }

        private List<WorldBossAbility> GetPhaseAbilities(WorldBossDefinition bossDef, int phase)
        {
            var abilities = new List<WorldBossAbility>();
            // All phases include earlier abilities
            if (bossDef.Phase1Abilities != null) abilities.AddRange(bossDef.Phase1Abilities);
            if (phase >= 2 && bossDef.Phase2Abilities != null) abilities.AddRange(bossDef.Phase2Abilities);
            if (phase >= 3 && bossDef.Phase3Abilities != null) abilities.AddRange(bossDef.Phase3Abilities);
            return abilities;
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Phase Transitions
        // ═══════════════════════════════════════════════════════════════════════════

        private async Task CheckPhaseTransition(WorldBossInfo boss, WorldBossRuntimeData bossData,
            WorldBossDefinition bossDef, SqlSaveBackend backend, TerminalEmulator terminal)
        {
            if (boss.MaxHP <= 0) return;

            double hpPercent = (double)boss.CurrentHP / boss.MaxHP;
            int newPhase = bossData.CurrentPhase;

            if (hpPercent <= GameConfig.WorldBossPhase3Threshold && bossData.CurrentPhase < 3)
                newPhase = 3;
            else if (hpPercent <= GameConfig.WorldBossPhase2Threshold && bossData.CurrentPhase < 2)
                newPhase = 2;

            if (newPhase != bossData.CurrentPhase)
            {
                bossData.CurrentPhase = newPhase;

                // Save phase to DB for other players
                string dataJson = JsonSerializer.Serialize(bossData);
                await backend.UpdateWorldBossData(boss.Id, dataJson);

                // Display phase transition
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("");
                terminal.WriteLine($"  *** {Loc.Get("world_boss.phase_label", newPhase, 3)} — {GetPhaseDescription(newPhase)} ***");

                string[]? dialogue = newPhase == 2 ? bossDef.Phase2Dialogue : bossDef.Phase3Dialogue;
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

                // Broadcast phase change
                string phaseMsg = $"\n  *** {Loc.Get("world_boss.phase_change_broadcast", bossDef.Name, newPhase, GetPhaseDescription(newPhase))} ***";
                MudServer.Instance?.BroadcastToAll(phaseMsg);
            }
        }

        private string GetPhaseDescription(int phase) => phase switch
        {
            2 => Loc.Get("world_boss.phase_2_desc"),
            3 => Loc.Get("world_boss.phase_3_desc"),
            _ => Loc.Get("world_boss.phase_1_desc")
        };

        // ═══════════════════════════════════════════════════════════════════════════
        // Reward Distribution — Contribution-based rewards for all participants
        // ═══════════════════════════════════════════════════════════════════════════

        private async Task DistributeWorldBossRewards(int bossId, WorldBossDefinition bossDef,
            long bossMaxHP, int bossLevel, SqlSaveBackend backend,
            Character killingPlayer, TerminalEmulator killingTerminal)
        {
            try
            {
                var leaderboard = await backend.GetWorldBossDamageLeaderboard(bossId, 100);
                if (leaderboard.Count == 0) return;

                long totalDamage = leaderboard.Sum(e => e.DamageDealt);
                int totalContributors = leaderboard.Count;

                // Calculate reward tiers
                int top3Cutoff = Math.Min(3, totalContributors);
                int top25Cutoff = Math.Max(top3Cutoff, totalContributors / 4);
                int top50Cutoff = Math.Max(top25Cutoff, totalContributors / 2);

                for (int i = 0; i < leaderboard.Count; i++)
                {
                    var entry = leaderboard[i];
                    double contribution = totalDamage > 0 ? (double)entry.DamageDealt / totalDamage : 0;

                    // Determine reward tier
                    float xpMult, goldMult;
                    LootGenerator.ItemRarity minRarity;
                    string tierName;

                    if (i == 0)
                    {
                        xpMult = GameConfig.WorldBossMVPXPMult;
                        goldMult = GameConfig.WorldBossMVPXPMult;
                        minRarity = LootGenerator.ItemRarity.Legendary;
                        tierName = Loc.Get("world_boss.tier_mvp");
                    }
                    else if (i < top3Cutoff)
                    {
                        xpMult = GameConfig.WorldBossTop3XPMult;
                        goldMult = GameConfig.WorldBossTop3XPMult;
                        minRarity = LootGenerator.ItemRarity.Epic;
                        tierName = Loc.Get("world_boss.tier_top3");
                    }
                    else if (i < top25Cutoff)
                    {
                        xpMult = GameConfig.WorldBossTop25XPMult;
                        goldMult = GameConfig.WorldBossTop25XPMult;
                        minRarity = LootGenerator.ItemRarity.Rare;
                        tierName = Loc.Get("world_boss.tier_top25");
                    }
                    else if (i < top50Cutoff)
                    {
                        xpMult = GameConfig.WorldBossTop50XPMult;
                        goldMult = GameConfig.WorldBossTop50XPMult;
                        minRarity = LootGenerator.ItemRarity.Uncommon;
                        tierName = Loc.Get("world_boss.tier_top50");
                    }
                    else
                    {
                        xpMult = GameConfig.WorldBossBaseXPMult;
                        goldMult = GameConfig.WorldBossBaseXPMult;
                        minRarity = LootGenerator.ItemRarity.Common;
                        tierName = Loc.Get("world_boss.tier_contributor");
                    }

                    // Calculate rewards
                    long baseXP = GameConfig.WorldBossBaseXPPerLevel * bossLevel;
                    long baseGold = GameConfig.WorldBossBaseGoldPerLevel * bossLevel;
                    long xpReward = (long)(baseXP * xpMult);
                    long goldReward = (long)(baseGold * goldMult);

                    // Check if this is the killing player (they're still online, apply directly)
                    bool isKillingPlayer = entry.PlayerName.Equals(
                        killingPlayer.DisplayName, StringComparison.OrdinalIgnoreCase);

                    if (isKillingPlayer)
                    {
                        // Apply directly to the killing player
                        killingPlayer.Experience += xpReward;
                        killingPlayer.Gold += goldReward;
                        killingPlayer.Fame += 25; // Fame from world boss kill

                        // Generate and give loot item
                        var lootItem = LootGenerator.GenerateWorldBossLoot(
                            bossLevel, minRarity, bossDef.Element, killingPlayer.Class);

                        killingTerminal.SetColor("bright_yellow");
                        killingTerminal.WriteLine($"\n  ═══ {Loc.Get("world_boss.rewards_header", tierName)} ═══");
                        killingTerminal.SetColor("white");
                        killingTerminal.WriteLine($"  {Loc.Get("world_boss.reward_xp", $"{xpReward:N0}")}");
                        killingTerminal.WriteLine($"  {Loc.Get("world_boss.reward_gold", $"{goldReward:N0}")}");

                        if (lootItem != null)
                        {
                            if (killingPlayer is Player kp)
                            {
                                kp.Inventory.Add(lootItem);
                                // Color based on item value as a proxy for rarity
                                bool hasLegendary = lootItem.LootEffects.Any(e => e.EffectType == (int)LootGenerator.SpecialEffect.BossSlayer);
                                bool hasEpic = lootItem.LootEffects.Any(e => e.EffectType == (int)LootGenerator.SpecialEffect.TitanResolve);
                                string rarityColor = hasLegendary ? "bright_yellow" : hasEpic ? "bright_magenta" : "bright_cyan";
                                killingTerminal.SetColor(rarityColor);
                                killingTerminal.WriteLine($"  {Loc.Get("world_boss.reward_loot")}: {LootGenerator.GetUnidentifiedName(lootItem)}");
                            }
                        }

                        // Record stats
                        if (killingPlayer is Player kpStats)
                        {
                            kpStats.Statistics.RecordWorldBossKill(bossDef.Id, entry.DamageDealt, i == 0);
                            // Check all world boss achievements
                            AchievementSystem.TryUnlock(killingPlayer, "world_boss_first");
                            if (kpStats.Statistics.UniqueWorldBossTypes.Count >= 5)
                                AchievementSystem.TryUnlock(killingPlayer, "world_boss_5_unique");
                            if (kpStats.Statistics.WorldBossesKilled >= 25)
                                AchievementSystem.TryUnlock(killingPlayer, "world_boss_25_total");
                            if (i == 0)
                                AchievementSystem.TryUnlock(killingPlayer, "world_boss_mvp");
                        }
                    }
                    else
                    {
                        // Check if this player is online — if so, apply in-memory to avoid
                        // race condition with SQL add (session save would double-count)
                        var session = FindOnlineSession(entry.PlayerName);
                        bool isOnline = session?.Context?.Engine?.CurrentPlayer != null;

                        if (!isOnline)
                        {
                            // Offline player: apply via SQL (will be loaded on next login)
                            await backend.AddXPToPlayer(entry.PlayerName, xpReward);
                            await backend.AddGoldToPlayer(entry.PlayerName, goldReward);
                        }

                        // Send notification message
                        string msg = Loc.Get("world_boss.reward_message", $"{xpReward:N0}", $"{goldReward:N0}", tierName, $"{entry.DamageDealt:N0}");
                        await backend.SendMessage("System", entry.PlayerName, "world_boss", msg);

                        // Deliver rewards and loot to online player's session
                        if (isOnline)
                        {
                            var onlinePlayer = session!.Context!.Engine!.CurrentPlayer!;

                            // Apply rewards in-memory only (saved with next session save)
                            onlinePlayer.Experience += xpReward;
                            onlinePlayer.Gold += goldReward;
                            onlinePlayer.Fame += 15; // Fame from world boss participation

                            var lootItem = LootGenerator.GenerateWorldBossLoot(
                                bossLevel, minRarity, bossDef.Element,
                                onlinePlayer.Class);

                            if (lootItem != null)
                            {
                                onlinePlayer.Inventory.Add(lootItem);
                                session.EnqueueMessage(
                                    $"\n  *** {Loc.Get("world_boss.rewards_notification_loot", tierName, $"{xpReward:N0}", $"{goldReward:N0}", LootGenerator.GetUnidentifiedName(lootItem))} ***");
                            }
                            else
                            {
                                session.EnqueueMessage(
                                    $"\n  *** {Loc.Get("world_boss.rewards_notification", tierName, $"{xpReward:N0}", $"{goldReward:N0}")} ***");
                            }

                            // Record stats for online players
                            if (onlinePlayer is Player opStats)
                            {
                                opStats.Statistics.RecordWorldBossKill(bossDef.Id, entry.DamageDealt, i == 0);
                                AchievementSystem.TryUnlock(onlinePlayer, "world_boss_first");
                                if (opStats.Statistics.UniqueWorldBossTypes.Count >= 5)
                                    AchievementSystem.TryUnlock(onlinePlayer, "world_boss_5_unique");
                                if (opStats.Statistics.WorldBossesKilled >= 25)
                                    AchievementSystem.TryUnlock(onlinePlayer, "world_boss_25_total");
                                if (i == 0)
                                    AchievementSystem.TryUnlock(onlinePlayer, "world_boss_mvp");
                            }
                        }
                    }
                }

                // Post summary to news
                if (OnlineStateManager.IsActive)
                {
                    string mvpName = leaderboard.Count > 0 ? leaderboard[0].PlayerName : Loc.Get("world_boss.unknown");
                    _ = OnlineStateManager.Instance!.AddNews(
                        Loc.Get("world_boss.defeat_news", bossDef.Name, mvpName, $"{leaderboard[0].DamageDealt:N0}", totalContributors), "world_boss");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("WORLD_BOSS", $"Reward distribution failed: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Helper Methods
        // ═══════════════════════════════════════════════════════════════════════════

        private PlayerSession? FindOnlineSession(string playerName)
        {
            if (MudServer.Instance == null) return null;
            var key = playerName.ToLowerInvariant();
            MudServer.Instance.ActiveSessions.TryGetValue(key, out var session);
            return session;
        }

        public static bool HasSpecialEffect(Character player, LootGenerator.SpecialEffect effect)
        {
            int effectId = (int)effect;

            // Check all items in inventory with the effect — equipped items will have
            // the effect applied through CombatEngine's equipment processing, but for
            // world boss combat we check LootEffects directly on inventory items
            foreach (var item in player.Inventory)
            {
                if (item.LootEffects != null && item.LootEffects.Any(e => e.EffectType == effectId))
                    return true;
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
        public int TempDefenseBonus { get; set; }
        public int TempAttackBonus { get; set; }
        public int DefendingRounds { get; set; }
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
