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

                // Cooldown: check last spawn time via boss_data_json or just rely on
                // the fact that we only spawn 1/day and bosses last 1 hour + expire
                // The DB already handles "no active boss" check above

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
                    string spawnMsg = $"\n  *** {bossDef.Name}, {bossDef.Title} has appeared! ***\n  Type /boss to join the fight!";
                    MudServer.Instance?.BroadcastToAll(spawnMsg);

                    // Post to news feed
                    if (OnlineStateManager.IsActive)
                    {
                        _ = OnlineStateManager.Instance!.AddNews(
                            $"{bossDef.Name}, {bossDef.Title} has appeared! The realm needs heroes!", "world_boss");
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
                terminal.WriteLine("\n  World Boss is only available in online mode.");
                await Task.Delay(1500);
                return;
            }

            if (player.Level < GameConfig.WorldBossMinLevel)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"\n  You must be at least level {GameConfig.WorldBossMinLevel} to challenge a World Boss.");
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
                terminal.WriteLine("  [A] Attack Boss    [Q] Back");
                terminal.SetColor("white");
                terminal.Write("\n  Choice: ");
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
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            { const string t = "WORLD BOSS"; int l = (78 - t.Length) / 2, r = 78 - t.Length - l; terminal.WriteLine($"║{new string(' ', l)}{t}{new string(' ', r)}║"); }
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine("  No World Boss is active right now.");
            terminal.WriteLine("  A boss will appear when enough adventurers are online.");
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine("  World Bosses spawn automatically when 2+ players are online.");
            terminal.WriteLine("  They last for 1 hour and require teamwork to defeat.");
        }

        private void DrawBossStatusScreen(TerminalEmulator terminal, WorldBossInfo boss,
            WorldBossRuntimeData? bossData, WorldBossDefinition? bossDef)
        {
            string themeColor = bossDef?.ThemeColor ?? "bright_red";
            string bossTitle = bossDef != null ? $"{bossDef.Name}, {bossDef.Title}" : boss.BossName;
            int phase = bossData?.CurrentPhase ?? 1;

            terminal.SetColor("bright_magenta");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            { const string t = "WORLD BOSS"; int l = (78 - t.Length) / 2, r = 78 - t.Length - l; terminal.WriteLine($"║{new string(' ', l)}{t}{new string(' ', r)}║"); }
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            // Boss name and phase
            terminal.SetColor(themeColor);
            terminal.WriteLine($"  {bossTitle}  (Level {boss.BossLevel})");
            if (phase > 1)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"  Phase {phase}/3 — {GetPhaseDescription(phase)}");
            }

            // HP bar
            double hpPercent = boss.MaxHP > 0 ? (double)boss.CurrentHP / boss.MaxHP * 100 : 0;
            int barFilled = Math.Clamp((int)(hpPercent / 5), 0, 20);
            string hpBar = new string('█', barFilled) + new string('░', 20 - barFilled);
            string hpColor = hpPercent > 50 ? "bright_green" : hpPercent > 25 ? "bright_yellow" : "bright_red";

            terminal.SetColor(hpColor);
            terminal.WriteLine($"  HP: [{hpBar}] {boss.CurrentHP:N0} / {boss.MaxHP:N0} ({hpPercent:F1}%)");

            // Time remaining
            var timeLeft = boss.ExpiresAt - DateTime.UtcNow;
            if (timeLeft.TotalSeconds > 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  Time remaining: {(int)timeLeft.TotalMinutes}m {timeLeft.Seconds}s");
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine("  TIME EXPIRED — Boss is despawning!");
            }

            // Boss element/theme
            if (bossDef != null)
            {
                terminal.SetColor("darkgray");
                terminal.WriteLine($"  Element: {bossDef.Element}  |  Attacks/round: {bossDef.AttacksPerRound}");
            }
            terminal.WriteLine("");
        }

        private void DrawLeaderboard(TerminalEmulator terminal, List<WorldBossDamageEntry> leaderboard, string playerName)
        {
            if (leaderboard.Count == 0) return;

            long totalDamage = leaderboard.Sum(e => e.DamageDealt);

            terminal.SetColor("bright_yellow");
            terminal.WriteLine("  ═══ Damage Leaderboard ═══");
            for (int i = 0; i < leaderboard.Count; i++)
            {
                var entry = leaderboard[i];
                double pct = totalDamage > 0 ? (double)entry.DamageDealt / totalDamage * 100 : 0;
                bool isPlayer = entry.PlayerName.Equals(playerName, StringComparison.OrdinalIgnoreCase);

                string color = i == 0 ? "bright_yellow" : i < 3 ? "yellow" : isPlayer ? "bright_cyan" : "white";
                string marker = i == 0 ? " MVP" : "";
                string youTag = isPlayer ? " (YOU)" : "";

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
                terminal.WriteLine($"\n  You were recently defeated! Wait {secsLeft}s before re-entering combat.");
                await Task.Delay(2000);
                return;
            }

            if (player.HP <= 0)
            {
                terminal.SetColor("red");
                terminal.WriteLine("\n  You're too injured to fight! Visit the Healer first.");
                await Task.Delay(1500);
                return;
            }

            // Parse boss runtime data
            var bossData = DeserializeRuntimeData(boss.BossDataJson);
            var bossDef = WorldBossDatabase.GetBossById(bossData?.DefinitionId ?? "");
            if (bossDef == null || bossData == null)
            {
                terminal.SetColor("red");
                terminal.WriteLine("\n  Error: Could not load boss data.");
                await Task.Delay(1500);
                return;
            }

            // Combat state
            var state = new WorldBossCombatState();
            var rng = new Random();

            terminal.ClearScreen();
            terminal.SetColor(bossDef.ThemeColor);
            terminal.WriteLine($"\n  You engage {bossDef.Name}, {bossDef.Title}!");
            terminal.SetColor("gray");
            terminal.WriteLine($"  Phase {bossData.CurrentPhase}/3 — Prepare yourself!\n");
            await Task.Delay(1000);

            while (state.Round < GameConfig.WorldBossMaxRoundsPerSession && player.HP > 0 && !state.Retreated)
            {
                state.Round++;

                // Refresh boss state from DB each round
                var currentBoss = await backend.GetActiveWorldBoss();
                if (currentBoss == null || currentBoss.Status != "active" || currentBoss.CurrentHP <= 0)
                {
                    terminal.SetColor("bright_green");
                    terminal.WriteLine($"\n  *** {bossDef.Name} HAS BEEN DEFEATED! ***");
                    terminal.SetColor("yellow");
                    terminal.WriteLine("  The realm celebrates! Rewards will be distributed.");
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
                int barFilled = Math.Clamp((int)(hpPct / 5), 0, 20);
                string hpBar = new string('█', barFilled) + new string('░', 20 - barFilled);
                string hpColor = hpPct > 50 ? "bright_green" : hpPct > 25 ? "bright_yellow" : "bright_red";

                terminal.SetColor("white");
                terminal.WriteLine($"  ─── Round {state.Round} ───");
                terminal.SetColor(hpColor);
                terminal.WriteLine($"  Boss HP: [{hpBar}] {currentBoss.CurrentHP:N0}/{currentBoss.MaxHP:N0} ({hpPct:F1}%)");
                terminal.SetColor("cyan");
                terminal.WriteLine($"  Your HP: {player.HP}/{player.MaxHP}  Mana: {player.Mana}/{player.MaxMana}  Phase: {bossData.CurrentPhase}");

                // Show player status effects
                if (player.ActiveStatuses.Count > 0)
                {
                    var statusList = player.ActiveStatuses.Select(kv => $"{kv.Key}({kv.Value})");
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"  Status: {string.Join(", ", statusList)}");
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
                    terminal.WriteLine($"  You are {preventingStatus}! You cannot act this round.");
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
                        terminal.WriteLine($"  >> Total damage this round: {roundDamage:N0}");
                        terminal.SetColor("gray");
                        terminal.WriteLine($"  >> Boss HP remaining: {Math.Max(0, remainingHp):N0}");

                        if (remainingHp <= 0)
                        {
                            ActiveBossName = null;
                            terminal.SetColor("bright_green");
                            terminal.WriteLine($"\n  *** {bossDef.Name} HAS BEEN DEFEATED! ***");
                            terminal.SetColor("yellow");
                            terminal.WriteLine("  You dealt the killing blow!");

                            // Broadcast
                            string killMsg = $"\n  *** {bossDef.Name} has been defeated! {player.DisplayName} struck the final blow! ***";
                            MudServer.Instance?.BroadcastToAll(killMsg, playerKey);

                            // News
                            if (OnlineStateManager.IsActive)
                                _ = OnlineStateManager.Instance!.AddNews(
                                    $"{bossDef.Name} has been defeated! {player.DisplayName} struck the final blow!", "world_boss");

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
                    terminal.WriteLine($"  {bossDef.Name}'s overwhelming presence deals {auraDamage:N0} damage!");
                    terminal.SetColor("cyan");
                    terminal.WriteLine($"  Your HP: {player.HP}/{player.MaxHP}");
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
                    terminal.WriteLine($"\n  {bossDef.Name} has struck you down!");
                    terminal.SetColor("gray");
                    terminal.WriteLine($"  You dealt {state.SessionDamage:N0} total damage before falling.");

                    // Revive with 25% HP, apply cooldown
                    player.HP = Math.Max(1, player.MaxHP / 4);
                    _deathCooldowns[playerKey] = DateTime.UtcNow.AddSeconds(GameConfig.WorldBossDeathCooldownSeconds);

                    terminal.SetColor("cyan");
                    terminal.WriteLine($"  Healers drag you to safety. HP restored to {player.HP}/{player.MaxHP}.");
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"  You must wait {GameConfig.WorldBossDeathCooldownSeconds}s before re-entering combat.");
                    break;
                }

                await Task.Delay(300); // Brief pause between rounds
            }

            // Max rounds reached
            if (state.Round >= GameConfig.WorldBossMaxRoundsPerSession && !state.Died && !state.Retreated && player.HP > 0)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"\n  You've reached the maximum of {GameConfig.WorldBossMaxRoundsPerSession} rounds.");
                terminal.WriteLine("  You step back to recover. Re-enter when ready!");
            }

            // Session summary
            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"  ═══ Combat Session Summary ═══");
            terminal.SetColor("white");
            terminal.WriteLine($"  Rounds fought: {state.Round}");
            terminal.WriteLine($"  Damage dealt:  {state.SessionDamage:N0}");
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
            terminal.SetColor("green");
            terminal.WriteLine("╔═══════════════════════════════════════╗");
            terminal.Write("║");
            terminal.SetColor("bright_white");
            terminal.Write("           CHOOSE YOUR ACTION          ");
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

            terminal.SetColor("white");
            terminal.Write("  Action: ");
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
                    terminal.WriteLine($"  You strike {bossDef.Name} for {damage:N0} damage!");
                    break;

                case "C": // Cast spell
                    damage = await ProcessSpellCast(player, terminal, bossDef, bossData, rng);
                    break;

                case "D": // Defend
                    state.DefendingRounds = 2;
                    terminal.SetColor("bright_cyan");
                    terminal.WriteLine("  You raise your guard! Damage reduced for 2 rounds.");
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

                case "R": // Retreat
                    state.Retreated = true;
                    terminal.SetColor("yellow");
                    terminal.WriteLine("  You retreat from combat to recover.");
                    break;

                default:
                    damage = CalculatePlayerDamage(player, bossDef, bossData, rng, state.TempAttackBonus);
                    terminal.SetColor("bright_green");
                    terminal.WriteLine($"  You strike {bossDef.Name} for {damage:N0} damage!");
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
                terminal.WriteLine("  Your power attack misses!");
                return 0;
            }

            long damage = (long)(CalculatePlayerDamage(player, bossDef, bossData, rng, tempAtkBonus) * 1.5);
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"  POWER ATTACK! You crush {bossDef.Name} for {damage:N0} damage!");
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
                terminal.WriteLine($"  CRITICAL! Precise strike hits {bossDef.Name} for {baseDamage:N0} damage!");
            }
            else
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine($"  Precise strike hits {bossDef.Name} for {baseDamage:N0} damage!");
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
                terminal.WriteLine("  You cannot cast spells right now!");
                return 0;
            }

            var availableSpells = SpellSystem.GetAvailableSpells(player);
            if (availableSpells.Count == 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine("  You don't know any spells.");
                return 0;
            }

            terminal.SetColor("bright_cyan");
            terminal.WriteLine("  ─── Available Spells ───");
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
                terminal.WriteLine("  Not enough mana for any spell.");
                return 0;
            }

            terminal.SetColor("white");
            terminal.Write("  Cast which spell (# or Q): ");
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
                        terminal.WriteLine($"  Healed for {result.Healing} HP! ({player.HP}/{player.MaxHP})");
                    }

                    // Protection/attack buffs
                    if (result.ProtectionBonus > 0)
                    {
                        player.ApplyStatus(StatusEffect.Shielded, result.Duration > 0 ? result.Duration : 3);
                        terminal.SetColor("cyan");
                        terminal.WriteLine($"  Protection increased by {result.ProtectionBonus}!");
                    }
                    if (result.AttackBonus > 0)
                    {
                        player.ApplyStatus(StatusEffect.Empowered, result.Duration > 0 ? result.Duration : 3);
                        terminal.SetColor("yellow");
                        terminal.WriteLine($"  Attack power increased by {result.AttackBonus}!");
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
            // Simple potion use — Character.Healing is the potion count
            if (player.Healing > 0)
            {
                long healAmount = (long)(player.MaxHP * 0.3);
                player.HP = Math.Min(player.MaxHP, player.HP + healAmount);
                player.Healing--;

                terminal.SetColor("bright_green");
                terminal.WriteLine($"  You drink a healing potion! +{healAmount} HP ({player.HP}/{player.MaxHP})");
                terminal.SetColor("gray");
                terminal.WriteLine($"  Potions remaining: {player.Healing}");
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine("  You have no healing potions.");
            }
        }

        private async Task<long> ProcessClassAbility(Character player, TerminalEmulator terminal,
            WorldBossDefinition bossDef, WorldBossRuntimeData bossData, Random rng,
            Dictionary<string, int> cooldowns)
        {
            var abilities = ClassAbilitySystem.GetAvailableAbilities(player);
            if (abilities.Count == 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine("  No abilities available.");
                return 0;
            }

            terminal.SetColor("bright_yellow");
            terminal.WriteLine("  ─── Class Abilities ───");
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
                terminal.WriteLine("  All abilities on cooldown.");
                return 0;
            }

            terminal.SetColor("white");
            terminal.Write("  Use which ability (# or Q): ");
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
                            terminal.WriteLine($"  Healed for {result.Healing}! ({player.HP}/{player.MaxHP})");
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
                    terminal.WriteLine($"  {bossDef.Name} strikes you for {bossDmg:N0} damage! (HP: {player.HP}/{player.MaxHP})");
                }
            }
        }

        private async Task ProcessBossAbility(WorldBossAbility ability, WorldBossDefinition bossDef,
            WorldBossRuntimeData bossData, Character player, TerminalEmulator terminal, Random rng,
            int defendingRounds, int playerDefBonus)
        {
            terminal.SetColor(bossDef.ThemeColor);
            terminal.WriteLine($"  {bossDef.Name} uses {ability.Name}!");

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
                terminal.WriteLine($"  {ability.Description} ({abilityDmg:N0} damage!)");
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
                    terminal.WriteLine($"  You are afflicted with {ability.AppliedStatus.Value}! ({ability.StatusDuration} rounds)");
                }
                else
                {
                    terminal.SetColor("cyan");
                    terminal.WriteLine($"  You resist the {ability.AppliedStatus.Value} effect!");
                }
            }

            // Self-heal
            if (ability.SelfHealPercent > 0)
            {
                terminal.SetColor("magenta");
                terminal.WriteLine($"  {bossDef.Name} regenerates from the attack!");
            }

            terminal.SetColor("cyan");
            terminal.WriteLine($"  Your HP: {player.HP}/{player.MaxHP}");
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

            long raw = Math.Max(1, bossStr - playerDef / 2);
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
                terminal.WriteLine($"  *** PHASE {newPhase} — {GetPhaseDescription(newPhase)} ***");

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
                string phaseMsg = $"\n  *** {bossDef.Name} enters Phase {newPhase}! {GetPhaseDescription(newPhase)} ***";
                MudServer.Instance?.BroadcastToAll(phaseMsg);
            }
        }

        private string GetPhaseDescription(int phase) => phase switch
        {
            2 => "The boss grows desperate!",
            3 => "ENRAGED — Full power unleashed!",
            _ => "The battle begins."
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
                        tierName = "MVP";
                    }
                    else if (i < top3Cutoff)
                    {
                        xpMult = GameConfig.WorldBossTop3XPMult;
                        goldMult = GameConfig.WorldBossTop3XPMult;
                        minRarity = LootGenerator.ItemRarity.Epic;
                        tierName = "Top 3";
                    }
                    else if (i < top25Cutoff)
                    {
                        xpMult = GameConfig.WorldBossTop25XPMult;
                        goldMult = GameConfig.WorldBossTop25XPMult;
                        minRarity = LootGenerator.ItemRarity.Rare;
                        tierName = "Top 25%";
                    }
                    else if (i < top50Cutoff)
                    {
                        xpMult = GameConfig.WorldBossTop50XPMult;
                        goldMult = GameConfig.WorldBossTop50XPMult;
                        minRarity = LootGenerator.ItemRarity.Uncommon;
                        tierName = "Top 50%";
                    }
                    else
                    {
                        xpMult = GameConfig.WorldBossBaseXPMult;
                        goldMult = GameConfig.WorldBossBaseXPMult;
                        minRarity = LootGenerator.ItemRarity.Common;
                        tierName = "Contributor";
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

                        // Generate and give loot item
                        var lootItem = LootGenerator.GenerateWorldBossLoot(
                            bossLevel, minRarity, bossDef.Element, killingPlayer.Class);

                        killingTerminal.SetColor("bright_yellow");
                        killingTerminal.WriteLine($"\n  ═══ World Boss Rewards ({tierName}) ═══");
                        killingTerminal.SetColor("white");
                        killingTerminal.WriteLine($"  XP:   +{xpReward:N0}");
                        killingTerminal.WriteLine($"  Gold: +{goldReward:N0}");

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
                                killingTerminal.WriteLine($"  Loot: {lootItem.Name}");
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
                        // Apply rewards via DB for offline/other online players
                        await backend.AddXPToPlayer(entry.PlayerName, xpReward);
                        await backend.AddGoldToPlayer(entry.PlayerName, goldReward);

                        // Send notification message
                        string msg = $"World Boss Defeated! You earned {xpReward:N0} XP and {goldReward:N0} gold " +
                                     $"for your contribution ({tierName}: {entry.DamageDealt:N0} damage).";
                        await backend.SendMessage("System", entry.PlayerName, "world_boss", msg);

                        // Try to deliver loot to online player's session
                        var session = FindOnlineSession(entry.PlayerName);
                        if (session?.Context?.Engine?.CurrentPlayer != null)
                        {
                            var onlinePlayer = session.Context.Engine.CurrentPlayer;
                            var lootItem = LootGenerator.GenerateWorldBossLoot(
                                bossLevel, minRarity, bossDef.Element,
                                onlinePlayer.Class);

                            if (lootItem != null)
                            {
                                onlinePlayer.Inventory.Add(lootItem);
                                onlinePlayer.Experience += xpReward;
                                onlinePlayer.Gold += goldReward;
                                session.EnqueueMessage(
                                    $"\n  *** World Boss Rewards ({tierName}): +{xpReward:N0} XP, +{goldReward:N0} gold, {lootItem.Name} ***");
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
                    string mvpName = leaderboard.Count > 0 ? leaderboard[0].PlayerName : "unknown";
                    _ = OnlineStateManager.Instance!.AddNews(
                        $"{bossDef.Name} defeated! MVP: {mvpName} ({leaderboard[0].DamageDealt:N0} dmg). " +
                        $"{totalContributors} heroes participated.", "world_boss");
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
