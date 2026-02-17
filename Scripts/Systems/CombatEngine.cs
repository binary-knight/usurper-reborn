using UsurperRemake.Utils;
using UsurperRemake.Data;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using UsurperRemake.BBS;
using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

/// <summary>
/// Combat Engine - Pascal-compatible combat system
/// Based on PLVSMON.PAS, MURDER.PAS, VARIOUS.PAS, and PLCOMP.PAS
/// </summary>
public partial class CombatEngine
{
    private TerminalEmulator terminal;
    private Random random = new Random();

    // Combat state
    private bool globalBegged = false;
    private bool globalEscape = false;
    private bool globalNoBeg = false;

    // Ability cooldowns - reset each combat
    private Dictionary<string, int> abilityCooldowns = new();

    // Current player reference for combat speed setting
    private Character currentPlayer;

    // Current teammates for healing/support actions
    private List<Character> currentTeammates;

    // Boss combat context - set by OldGodBossSystem when routing boss fights through CombatEngine
    public BossCombatContext? BossContext { get; set; }

    /// <summary>True when the current combat is against Manwe (for Void Key artifact doubling)</summary>
    public static bool IsManweBattle { get; set; }

    // Combat tip system - shows helpful hints occasionally
    private int combatTipCounter = 0;
    private static readonly string[] CombatTips = new string[]
    {
        "TIP: Press [SPD] to toggle combat speed (Instant/Fast/Normal).",
        "TIP: Press [AUTO] or type 'auto' for automatic attacks against weak enemies.",
        "TIP: [P]ower Attack deals 50% more damage but has lower accuracy.",
        "TIP: [E]xact Strike has higher accuracy - good against armored foes.",
        "TIP: [D]efend reduces incoming damage by 50% - useful against strong monsters.",
        "TIP: [T]aunt lowers enemy defense, making them easier to hit.",
        "TIP: [I]disarm can remove a monster's weapon, reducing their damage.",
        "TIP: Spellcasters can press [S] to cast spells using Mana.",
        "TIP: Check [L]hide to attempt to escape or reposition.",
        "TIP: Use healing potions [H] mid-combat if your HP gets low."
    };
    // Class-specific tips shown only to the relevant class
    private static readonly Dictionary<CharacterClass, string[]> ClassSpecificTips = new()
    {
        { CharacterClass.Barbarian, new[] { "TIP: Barbarians can [G]rage for increased combat power!" } },
        { CharacterClass.Ranger, new[] { "TIP: Rangers can use [V]ranged attacks from a distance." } },
    };

    /// <summary>
    /// Gets the appropriate delay time based on player's combat speed setting
    /// </summary>
    private int GetCombatDelay(int baseDelayMs)
    {
        if (currentPlayer == null) return baseDelayMs;

        return currentPlayer.CombatSpeed switch
        {
            CombatSpeed.Instant => 0,
            CombatSpeed.Fast => baseDelayMs / 2,
            _ => baseDelayMs  // Normal
        };
    }

    public CombatEngine(TerminalEmulator? term = null)
    {
        terminal = term;
    }

    /// <summary>
    /// Show a combat tip occasionally to help players learn tactics
    /// </summary>
    private void ShowCombatTipIfNeeded(Character player)
    {
        // Show tip every 5th combat round, less often if player has Fast/Instant speed
        combatTipCounter++;
        int tipFrequency = player.CombatSpeed == CombatSpeed.Normal ? 5 : 10;

        if (combatTipCounter >= tipFrequency)
        {
            combatTipCounter = 0;
            // 30% chance to show a class-specific tip if one exists
            string tip;
            if (random.Next(100) < 30 && ClassSpecificTips.TryGetValue(player.Class, out var classTips))
                tip = classTips[random.Next(classTips.Length)];
            else
                tip = CombatTips[random.Next(CombatTips.Length)];
            terminal.SetColor("dark_gray");
            terminal.WriteLine($"  {tip}");
            terminal.WriteLine("");
        }
    }

    /// <summary>
    /// Player vs Monster combat - LEGACY method for backward compatibility
    /// Redirects to new PlayerVsMonsters method with single-monster list
    /// Based on Player_vs_Monsters procedure from PLVSMON.PAS
    /// </summary>
    public async Task<CombatResult> PlayerVsMonster(Character player, Monster monster, List<Character>? teammates = null, bool offerMonkEncounter = true)
    {
        // Redirect to new multi-monster method with single monster
        return await PlayerVsMonsters(player, new List<Monster> { monster }, teammates, offerMonkEncounter);
    }
    
    /// <summary>
    /// Player vs Player combat
    /// Based on PLVSPLC.PAS and MURDER.PAS
    /// </summary>
    public async Task<CombatResult> PlayerVsPlayer(Character attacker, Character defender)
    {
        // Wizard godmode: save HP/Mana before combat to restore after
        bool isGodMode = UsurperRemake.Server.SessionContext.IsActive
            && (UsurperRemake.Server.SessionContext.Current?.WizardGodMode ?? false);
        long godModeHP = attacker.HP;
        long godModeMana = attacker.Mana;

        // Store player reference for combat speed setting
        currentPlayer = attacker;

        // Reset temporary combat flags (matches PlayerVsMonsters initialization)
        attacker.IsRaging = false;
        defender.IsRaging = false;
        attacker.TempAttackBonus = 0;
        attacker.TempAttackBonusDuration = 0;
        attacker.TempDefenseBonus = 0;
        attacker.TempDefenseBonusDuration = 0;
        attacker.DodgeNextAttack = false;
        attacker.HasBloodlust = false;
        attacker.HasStatusImmunity = false;
        attacker.StatusImmunityDuration = 0;
        abilityCooldowns.Clear();

        // Initialize combat stamina for abilities
        attacker.InitializeCombatStamina();

        // Reset per-combat faction flags
        attacker.DivineFavorTriggeredThisCombat = false;
        if (attacker.PoisonCoatingCombats > 0) attacker.PoisonCoatingCombats--;

        // Ensure abilities are learned based on current level (fixes abilities not showing in quickbar)
        if (!ClassAbilitySystem.IsSpellcaster(attacker.Class))
        {
            ClassAbilitySystem.GetAvailableAbilities(attacker);
        }

        // Initialize combat state
        globalBegged = false;
        globalEscape = false;

        var result = new CombatResult
        {
            Player = attacker,
            Opponent = defender,
            CombatLog = new List<string>()
        };

        // PvP combat introduction
        await ShowPvPIntroduction(attacker, defender, result);
        
        // Main PvP combat loop
        
        while (attacker.IsAlive && defender.IsAlive && !globalEscape)
        {
            // Attacker's turn
            if (attacker.IsAlive && defender.IsAlive)
            {
                var attackerAction = await GetPlayerAction(attacker, null, result, defender);
                await ProcessPlayerVsPlayerAction(attackerAction, attacker, defender, result);
            }
            
            // Defender's turn (if AI controlled)
            if (defender.IsAlive && defender.AI == CharacterAI.Computer)
            {
                await ProcessComputerPlayerAction(defender, attacker, result);
            }
            
            // Check for combat end conditions
            if (!attacker.IsAlive || !defender.IsAlive || globalEscape)
                break;
        }
        
        await DeterminePvPOutcome(result);

        // Wizard godmode: ensure HP/Mana fully restored after PvP combat
        if (isGodMode)
        {
            attacker.HP = godModeHP;
            attacker.Mana = godModeMana;
        }

        return result;
    }

    /// <summary>
    /// Player vs Multiple Monsters - simultaneous turn-based combat
    /// NEW METHOD for group encounters where all monsters fight at once
    /// </summary>
    public async Task<CombatResult> PlayerVsMonsters(
        Character player,
        List<Monster> monsters,
        List<Character>? teammates = null,
        bool offerMonkEncounter = true,
        bool isAmbush = false)
    {
        // Wizard godmode: save HP/Mana before combat to restore after
        bool isGodMode = UsurperRemake.Server.SessionContext.IsActive
            && (UsurperRemake.Server.SessionContext.Current?.WizardGodMode ?? false);
        long godModeHP = player.HP;
        long godModeMana = player.Mana;

        // Store player reference for combat speed setting
        currentPlayer = player;

        // Store teammates for healing/support actions
        currentTeammates = teammates ?? new List<Character>();

        // Reset temporary flags per battle
        player.IsRaging = false;
        player.TempAttackBonus = 0;
        player.TempAttackBonusDuration = 0;
        player.TempDefenseBonus = 0;
        player.TempDefenseBonusDuration = 0;
        player.DodgeNextAttack = false;
        player.HasBloodlust = false;
        player.HasStatusImmunity = false;
        player.StatusImmunityDuration = 0;
        abilityCooldowns.Clear();

        // Initialize combat stamina for player and teammates
        player.InitializeCombatStamina();

        // Reset per-combat faction flags
        player.DivineFavorTriggeredThisCombat = false;
        // Decrement poison coating (per combat, not per round)
        if (player.PoisonCoatingCombats > 0) player.PoisonCoatingCombats--;

        // Ensure abilities are learned based on current level (fixes abilities not showing)
        if (!ClassAbilitySystem.IsSpellcaster(player.Class))
        {
            ClassAbilitySystem.GetAvailableAbilities(player);
        }

        var result = new CombatResult
        {
            Player = player,
            Monsters = new List<Monster>(monsters), // Copy list
            Teammates = teammates ?? new List<Character>(),
            CombatLog = new List<string>()
        };

        // Initialize combat state
        globalBegged = false;
        globalEscape = false;

        // MUD mode: broadcast combat start to room
        if (monsters.Count == 1)
            UsurperRemake.Server.RoomRegistry.BroadcastAction($"{player.DisplayName} draws their weapon and attacks {monsters[0].Name}!");
        else
            UsurperRemake.Server.RoomRegistry.BroadcastAction($"{player.DisplayName} draws their weapon and engages {monsters.Count} enemies!");

        // Show combat introduction
        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        // Screen reader friendly header
        if (player is Player p && p.ScreenReaderMode)
            terminal.WriteLine("--- COMBAT ---");
        else
            terminal.WriteLine("═══ COMBAT ═══");
        terminal.WriteLine("");

        if (monsters.Count == 1)
        {
            var monster = monsters[0];
            if (!string.IsNullOrEmpty(monster.Phrase))
            {
                terminal.SetColor("yellow");
                if (monster.CanSpeak)
                    terminal.WriteLine($"The {monster.Name} says: \"{monster.Phrase}\"");
                else
                    terminal.WriteLine($"The {monster.Name} {monster.Phrase}");
                terminal.WriteLine("");
            }
            terminal.SetColor("white");
            terminal.WriteLine($"You are facing: {monster.GetDisplayInfo()}");

            // Show monster silhouette for single monster (skip for screen readers and BBS mode)
            if (player is Player pp3 && !pp3.ScreenReaderMode && !DoorMode.IsInDoorMode)
            {
                var art = MonsterArtDatabase.GetArtForFamily(monster.FamilyName);
                if (art != null)
                {
                    terminal.WriteLine("");
                    ANSIArt.DisplayArt(terminal, art);
                }
            }
        }
        else
        {
            // Show first monster's silhouette if 3 or fewer (skip for screen readers and BBS mode)
            if (monsters.Count <= 3 && player is Player pp4 && !pp4.ScreenReaderMode && !DoorMode.IsInDoorMode)
            {
                var art = MonsterArtDatabase.GetArtForFamily(monsters[0].FamilyName);
                if (art != null)
                    ANSIArt.DisplayArt(terminal, art);
            }
            terminal.WriteLine("");
        }

        if (result.Teammates.Count > 0)
        {
            terminal.SetColor("white");
            terminal.WriteLine("Fighting alongside you:");
            foreach (var teammate in result.Teammates)
            {
                if (teammate.IsAlive)
                {
                    teammate.InitializeCombatStamina(); // Initialize teammate stamina
                    terminal.WriteLine($"  - {teammate.DisplayName} (Level {teammate.Level})");
                }
            }

            // Show team combat hint for first time fighting with teammates
            HintSystem.Instance.TryShowHint(HintSystem.HINT_TEAM_COMBAT, terminal, player.HintsShown);
        }

        terminal.WriteLine("");

        // Show first combat hint for new players
        HintSystem.Instance.TryShowHint(HintSystem.HINT_FIRST_COMBAT, terminal, player.HintsShown);

        await Task.Delay(GetCombatDelay(2000));

        result.CombatLog.Add($"Combat begins against {monsters.Count} monster(s)!");

        // Log combat start (use max monster level as proxy for floor depth)
        var monsterNames = monsters.Select(m => $"{m.Name}(Lv{m.Level})").ToArray();
        int floorEstimate = monsters.Max(m => m.Level);
        DebugLogger.Instance.LogCombatStart(player.Name, player.Level, monsterNames, floorEstimate);

        // Ambush: monsters get a free attack round before the player can act
        if (isAmbush && player.IsAlive && monsters.Any(m => m.IsAlive))
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine("*** AMBUSH! The monsters strike before you can react! ***");
            terminal.WriteLine("");
            result.CombatLog.Add("Ambush! Monsters attack first!");

            foreach (var ambushMonster in monsters.Where(m => m.IsAlive))
            {
                if (!player.IsAlive) break;
                await ProcessMonsterAction(ambushMonster, player, result);
            }

            if (!player.IsAlive)
            {
                terminal.SetColor("bright_red");
                terminal.WriteLine("You were slain in the ambush!");
            }

            await Task.Delay(GetCombatDelay(1500));
        }

        // Main combat loop
        int roundNumber = 0;
        bool autoCombat = false; // Auto-combat toggle

        while (player.IsAlive && monsters.Any(m => m.IsAlive) && !globalEscape)
        {
            roundNumber++;

            // BBS 80x25 — clear screen at start of each round so combat fits one page
            if (DoorMode.IsInDoorMode)
                terminal.ClearScreen();

            // Display combat status at start of each round
            DisplayCombatStatus(monsters, player);

            // Boss phase transition check
            if (BossContext != null)
            {
                var bossMonster = monsters.FirstOrDefault(m => m.IsBoss && m.IsAlive);
                if (bossMonster != null)
                {
                    int newPhase = BossContext.CheckPhase(bossMonster.HP, bossMonster.MaxHP);
                    if (newPhase > BossContext.CurrentPhase)
                    {
                        await PlayBossPhaseTransition(BossContext, bossMonster, newPhase);
                        BossContext.CurrentPhase = newPhase;

                        // Maelketh spawns minions on phase 2
                        if (BossContext.GodType == OldGodType.Maelketh && newPhase == 2)
                        {
                            var soldiers = CreateSpectralSoldiers(2, bossMonster.Level);
                            monsters.AddRange(soldiers);
                            result.Monsters.AddRange(soldiers);
                        }
                    }

                    // Maelketh summons more soldiers every 3 rounds in phase 2+
                    if (BossContext.GodType == OldGodType.Maelketh && BossContext.CurrentPhase >= 2)
                    {
                        BossContext.RoundsSinceLastSummon++;
                        if (BossContext.RoundsSinceLastSummon >= 3)
                        {
                            BossContext.RoundsSinceLastSummon = 0;
                            int count = 1 + random.Next(2);
                            var soldiers = CreateSpectralSoldiers(count, bossMonster.Level);
                            monsters.AddRange(soldiers);
                            result.Monsters.AddRange(soldiers);
                        }
                    }
                }
            }

            // Process status effects for player and display messages
            var statusMessages = player.ProcessStatusEffects();
            if (statusMessages.Count > 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine("─── Status Effects ───");
                DisplayStatusEffectMessages(statusMessages);
                terminal.WriteLine("");
            }

            // Process plague/disease damage from world events and character conditions
            await ProcessPlagueDamage(player, result);

            // Troll racial passive regeneration
            if (player.Race == CharacterRace.Troll && player.HP < player.MaxHP && player.IsAlive)
            {
                int regenAmount = Math.Min(1 + (int)(player.Level / 20), 3);

                // Drug ConstitutionBonus boosts Troll regeneration (e.g., Ironhide: +25 CON = +2 regen)
                var conDrugEffects = DrugSystem.GetDrugEffects(player);
                if (conDrugEffects.ConstitutionBonus > 0)
                    regenAmount += conDrugEffects.ConstitutionBonus / 10;

                long actualRegen = Math.Min(regenAmount, player.MaxHP - player.HP);
                player.HP += actualRegen;
                terminal.SetColor("bright_green");
                terminal.WriteLine($"[Troll Regeneration] You regenerate {actualRegen} HP!");
            }

            // Drug HPDrain: some drugs cost HP each round (e.g., Haste: -5 HP/round)
            {
                var drugEffects = DrugSystem.GetDrugEffects(player);
                if (drugEffects.HPDrain > 0 && player.IsAlive)
                {
                    long drainAmount = Math.Min(drugEffects.HPDrain, player.HP - 1); // Don't kill from drain (min 1 HP)
                    if (drainAmount > 0)
                    {
                        player.HP -= drainAmount;
                        terminal.SetColor("dark_red");
                        terminal.WriteLine($"[Drug Side Effect] The drugs drain {drainAmount} HP from your body!");
                    }
                }
            }

            // Check if player died from status effects or plague
            if (!player.IsAlive)
            {
                terminal.SetColor("bright_red");
                terminal.WriteLine("You succumb to your wounds!");
                break;
            }

            // === PLAYER TURN ===
            if (player.IsAlive && monsters.Any(m => m.IsAlive))
            {
                CombatAction playerAction;

                if (autoCombat)
                {
                    // Auto-combat: automatically attack random living monster
                    terminal.SetColor("bright_cyan");
                    terminal.WriteLine("[AUTO-COMBAT] Press any key to stop...");
                    terminal.WriteLine("");

                    // Check for key press to stop auto-combat (poll during delay)
                    int delayMs = GetCombatDelay(500);
                    int elapsed = 0;
                    bool stopRequested = false;
                    while (elapsed < delayMs)
                    {
                        if (terminal.IsInputAvailable())
                        {
                            terminal.FlushPendingInput();
                            stopRequested = true;
                            break;
                        }
                        await Task.Delay(50); // Check every 50ms
                        elapsed += 50;
                    }

                    if (stopRequested)
                    {
                        autoCombat = false;
                        terminal.SetColor("bright_yellow");
                        terminal.WriteLine("[AUTO-COMBAT] Stopped. You take manual control.");
                        terminal.WriteLine("");
                        var (action, enableAuto) = await GetPlayerActionMultiMonster(player, monsters, result);
                        playerAction = action;
                        if (enableAuto) autoCombat = true;
                    }
                    // Smart auto-combat: use potion if HP below 50% (preemptive to survive burst)
                    else if (player.HP < player.MaxHP * 0.5 && player.Healing > 0)
                    {
                        terminal.SetColor("bright_green");
                        terminal.WriteLine("[AUTO-COMBAT] HP low - using healing potion...");
                        playerAction = new CombatAction
                        {
                            Type = CombatActionType.QuickHeal
                        };
                    }
                    else
                    {
                        playerAction = new CombatAction
                        {
                            Type = CombatActionType.Attack,
                            TargetIndex = null // Random target
                        };
                    }
                }
                else
                {
                    var (action, enableAuto) = await GetPlayerActionMultiMonster(player, monsters, result);
                    playerAction = action;
                    if (enableAuto) autoCombat = true;
                }

                await ProcessPlayerActionMultiMonster(playerAction, player, monsters, result);
            }

            // Check if all monsters defeated
            if (!monsters.Any(m => m.IsAlive))
                break;

            // Check if player died during their own action (e.g., spell backfired)
            if (!player.IsAlive)
                break;

            // === TEAMMATES' TURNS ===
            foreach (var teammate in result.Teammates.Where(t => t.IsAlive))
            {
                if (monsters.Any(m => m.IsAlive))
                {
                    await ProcessTeammateActionMultiMonster(teammate, monsters, result);
                }
            }

            // Check if all monsters defeated
            if (!monsters.Any(m => m.IsAlive))
                break;

            // === ALL MONSTERS' TURNS ===
            var livingMonsters = monsters.Where(m => m.IsAlive).ToList();
            foreach (var monster in livingMonsters)
            {
                if (!player.IsAlive)
                    break; // Stop if player died

                int attacks = 1;
                if (BossContext != null && monster.IsBoss)
                    attacks = BossContext.AttacksPerRound;

                for (int atk = 0; atk < attacks; atk++)
                {
                    if (!player.IsAlive || !monster.IsAlive) break;

                    // Boss confused check (from dialogue modifiers)
                    if (BossContext != null && monster.IsBoss && BossContext.BossConfused && random.NextDouble() < 0.25)
                    {
                        terminal.WriteLine("");
                        terminal.SetColor("cyan");
                        terminal.WriteLine($"{monster.Name} hesitates, confused by internal contradictions!");
                        await Task.Delay(GetCombatDelay(500));
                        continue;
                    }

                    terminal.WriteLine("");
                    terminal.SetColor("red");

                    // Show boss ability name for boss attacks
                    if (BossContext != null && monster.IsBoss)
                    {
                        string abilityName = SelectBossAbility(BossContext);
                        terminal.WriteLine($"{monster.Name} uses {abilityName}!");
                    }
                    else
                    {
                        terminal.WriteLine($"{monster.Name} attacks!");
                    }

                    await ProcessMonsterAction(monster, player, result);
                    await Task.Delay(GetCombatDelay(800));
                }
            }

            // Check for player death
            if (!player.IsAlive)
                break;

            // Process end-of-round effects: decrement ability cooldowns and buff durations
            ProcessEndOfRoundAbilityEffects(player);

            // Short pause between rounds
            await Task.Delay(GetCombatDelay(1000));
        }

        // Determine combat outcome
        if (globalEscape)
        {
            result.Outcome = CombatOutcome.PlayerEscaped;
            UsurperRemake.Server.RoomRegistry.BroadcastAction($"{player.DisplayName} fled from combat!");
            terminal.SetColor("yellow");
            terminal.WriteLine("You escaped from combat!");

            // Show NPC teammate reactions to fleeing
            if (result.Teammates != null && result.Teammates.Count > 0)
            {
                foreach (var teammate in result.Teammates)
                {
                    if (teammate is NPC npc && npc.IsAlive)
                    {
                        string reaction = npc.GetReaction(result.Player as Player, "combat_flee");
                        if (!string.IsNullOrEmpty(reaction))
                        {
                            terminal.SetColor("cyan");
                            terminal.WriteLine($"  {npc.Name2}: \"{reaction}\"");
                        }
                    }
                }
            }

            // Track flee telemetry for multi-monster
            int maxMonsterLevel = monsters.Any() ? monsters.Max(m => m.Level) : 0;
            TelemetrySystem.Instance.TrackCombat(
                "fled",
                player.Level,
                maxMonsterLevel,
                monsters.Count,
                result.TotalDamageDealt,
                result.TotalDamageTaken,
                monsters.FirstOrDefault()?.Name,
                monsters.Any(m => m.IsBoss),
                roundNumber,
                player.Class.ToString()
            );

            // Calculate partial exp/gold from defeated monsters
            if (result.DefeatedMonsters.Count > 0)
            {
                await HandlePartialVictory(result, offerMonkEncounter);
            }
        }
        else if (!player.IsAlive)
        {
            // Wizard godmode: restore HP/Mana so wizard cannot die
            if (isGodMode)
            {
                player.HP = godModeHP;
                player.Mana = godModeMana;
                result.Outcome = CombatOutcome.Victory;
                await HandleVictoryMultiMonster(result, offerMonkEncounter);
            }
            else
            {
                // Faith Divine Favor: chance to survive lethal damage
                bool divineSaved = false;
                if (!player.DivineFavorTriggeredThisCombat)
                {
                    float divineFavorChance = FactionSystem.Instance?.GetDivineFavorChance() ?? 0f;
                    if (divineFavorChance > 0 && random.NextDouble() < divineFavorChance)
                    {
                        player.DivineFavorTriggeredThisCombat = true;
                        player.HP = Math.Max(1, player.MaxHP / 10);
                        terminal.SetColor("bright_yellow");
                        terminal.WriteLine("");
                        terminal.WriteLine("  A golden light flares around you!");
                        terminal.WriteLine("  The gods arent done with you yet.");
                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"  You are restored to {player.HP} HP!");
                        terminal.WriteLine("");
                        await Task.Delay(GetCombatDelay(2000));
                        divineSaved = true;
                    }
                }

                if (!divineSaved)
                {
                    result.Outcome = CombatOutcome.PlayerDied;
                    UsurperRemake.Server.RoomRegistry.BroadcastAction($"{player.DisplayName} has fallen in combat!");
                    await HandlePlayerDeath(result);
                }
            }
        }
        else if (!monsters.Any(m => m.IsAlive))
        {
            result.Outcome = CombatOutcome.Victory;
            await HandleVictoryMultiMonster(result, offerMonkEncounter);
        }

        // Wizard godmode: ensure HP/Mana fully restored after combat
        if (isGodMode)
        {
            player.HP = godModeHP;
            player.Mana = godModeMana;
        }

        return result;
    }

    /// <summary>
    /// Show combat introduction - Pascal style
    /// </summary>
    private async Task ShowCombatIntroduction(Character player, Monster monster, CombatResult result)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        // Screen reader friendly header
        if (player is Player p && p.ScreenReaderMode)
            terminal.WriteLine("--- COMBAT ---");
        else
            terminal.WriteLine("═══ COMBAT ═══");
        terminal.WriteLine("");
        
        // Monster appearance
        if (!string.IsNullOrEmpty(monster.Phrase))
        {
            terminal.SetColor("yellow");
            if (monster.CanSpeak)
                terminal.WriteLine($"The {monster.Name} says: \"{monster.Phrase}\"");
            else
                terminal.WriteLine($"The {monster.Name} {monster.Phrase}");
            terminal.WriteLine("");
        }
        
        terminal.SetColor("white");
        terminal.WriteLine($"You are facing: {monster.GetDisplayInfo()}");
        terminal.WriteLine("");

        // Show monster silhouette (skip for screen readers and BBS mode)
        if (player is Player pp2 && !pp2.ScreenReaderMode && !DoorMode.IsInDoorMode)
        {
            var art = MonsterArtDatabase.GetArtForFamily(monster.FamilyName);
            if (art != null)
                ANSIArt.DisplayArt(terminal, art);
        }

        if (result.Teammates.Count > 0)
        {
            terminal.WriteLine("Fighting alongside you:");
            foreach (var teammate in result.Teammates)
            {
                if (teammate.IsAlive)
                {
                    terminal.WriteLine($"  - {teammate.DisplayName} (Level {teammate.Level})");
                }
            }
        }

        terminal.WriteLine("");
        await Task.Delay(GetCombatDelay(2000));

        result.CombatLog.Add($"Combat begins against {monster.Name}!");
    }

    /// <summary>
    /// Get player action - Pascal-compatible menu
    /// Based on shared_menu from PLCOMP.PAS
    /// </summary>
    private async Task<CombatAction> GetPlayerAction(Character player, Monster monster, CombatResult result, Character? pvpOpponent = null)
    {
        bool isPvP = pvpOpponent != null && monster == null;

        while (true) // Loop until valid action chosen
        {
            // Apply status ticks before player chooses action
            player.ProcessStatusEffects();

            // Check if player can act due to status effects
            if (!player.CanAct())
            {
                var preventingStatus = player.ActiveStatuses.Keys.FirstOrDefault(s => s.PreventsAction());
                terminal.SetColor("yellow");
                terminal.WriteLine($"You are {preventingStatus.ToString().ToLower()} and cannot act!");
                await Task.Delay(GetCombatDelay(1500));
                return new CombatAction { Type = CombatActionType.None };
            }

            // If stunned, skip turn
            if (player.HasStatus(StatusEffect.Stunned))
            {
                terminal.SetColor("gray");
                terminal.WriteLine("You are stunned and cannot act this round!");
                await Task.Delay(GetCombatDelay(800));
                return new CombatAction { Type = CombatActionType.Status };
            }

            // Display combat menu
            if (DoorMode.IsInDoorMode)
            {
                ShowCombatMenuBBS(player, monster, pvpOpponent, isPvP);
            }
            else if (player.ScreenReaderMode)
            {
                ShowCombatMenuScreenReader(player, monster, pvpOpponent, isPvP);
            }
            else
            {
                ShowCombatMenuStandard(player, monster, pvpOpponent, isPvP);
            }

            // Show combat tip occasionally (skip in BBS mode to save lines)
            if (!DoorMode.IsInDoorMode)
                ShowCombatTipIfNeeded(player);

            terminal.SetColor("white");
            terminal.Write("Choose action: ");

            var choice = await terminal.GetInput("");
            var upperChoice = choice.Trim().ToUpper();

            // Handle combat speed toggle
            if (upperChoice == "SPD")
            {
                player.CombatSpeed = player.CombatSpeed switch
                {
                    CombatSpeed.Normal => CombatSpeed.Fast,
                    CombatSpeed.Fast => CombatSpeed.Instant,
                    _ => CombatSpeed.Normal
                };
                terminal.WriteLine($"Combat speed set to: {player.CombatSpeed}", "cyan");
                await Task.Delay(500);
                continue; // Show menu again
            }

            // Handle quickbar slots [1]-[9] for spells and abilities
            if (upperChoice.Length == 1 && upperChoice[0] >= '1' && upperChoice[0] <= '9')
            {
                var qbResult = await HandleQuickbarActionSingleMonster(player, upperChoice, monster);
                if (qbResult != null)
                    return qbResult;
                continue; // Invalid/cancelled, show menu again
            }

            // Parse and validate action
            var action = ParseCombatAction(upperChoice, player);

            // Block certain actions in PvP
            if (isPvP)
            {
                if (action.Type == CombatActionType.PowerAttack ||
                    action.Type == CombatActionType.PreciseStrike ||
                    action.Type == CombatActionType.Backstab ||
                    action.Type == CombatActionType.Smite ||
                    action.Type == CombatActionType.FightToDeath ||
                    action.Type == CombatActionType.BegForMercy)
                {
                    terminal.WriteLine("That action is not available in PvP combat.", "yellow");
                    await Task.Delay(800);
                    continue; // Show menu again
                }
            }

            return action;
        }
    }

    /// <summary>
    /// Display combat menu in screen reader friendly format (no box-drawing characters)
    /// </summary>
    private void ShowCombatMenuScreenReader(Character player, Monster? monster, Character? pvpOpponent, bool isPvP)
    {
        terminal.WriteLine("");
        terminal.WriteLine("Combat Menu");
        terminal.WriteLine("");

        // HP Status
        terminal.WriteLine($"Your HP: {player.HP} of {player.MaxHP}");
        if (monster != null)
        {
            terminal.WriteLine($"Enemy: {monster.Name}, HP: {monster.HP}");
        }
        else if (pvpOpponent != null)
        {
            terminal.WriteLine($"Opponent: {pvpOpponent.DisplayName}, HP: {pvpOpponent.HP} of {pvpOpponent.MaxHP}");
        }

        // Status effects
        if (player.ActiveStatuses.Count > 0 || player.IsRaging)
        {
            var list = new List<string>();
            foreach (var kv in player.ActiveStatuses)
            {
                var label = kv.Key.ToString();
                if (kv.Value > 0) label += $" {kv.Value} turns";
                list.Add(label);
            }
            if (player.IsRaging && !list.Any(s => s.StartsWith("Raging")))
                list.Add("Raging");
            terminal.WriteLine($"Status: {string.Join(", ", list)}");
        }
        terminal.WriteLine("");

        // Basic actions
        terminal.WriteLine("Actions:");
        terminal.WriteLine("  A - Attack");
        terminal.WriteLine("  D - Defend, reduces damage by 50 percent");

        // Quickbar slots
        var quickbarActions = GetQuickbarActions(player);
        if (quickbarActions.Count > 0)
        {
            terminal.WriteLine("");
            terminal.WriteLine("Quickbar:");
            foreach (var (qKey, slotId, displayName, available) in quickbarActions)
            {
                if (available)
                    terminal.WriteLine($"  {qKey} - {displayName}");
                else
                    terminal.WriteLine($"  {qKey} - {displayName} (unavailable)");
            }
        }
        terminal.WriteLine("");

        // Healing
        if (player.Healing > 0)
            terminal.WriteLine($"  H - Heal, Potions: {player.Healing} of {player.MaxPotions}");
        else
            terminal.WriteLine("  H - Heal, No Potions");

        // Class-specific
        if (player.Class == CharacterClass.Barbarian && !player.IsRaging)
            terminal.WriteLine("  G - Rage, Berserker fury");
        if (player.Class == CharacterClass.Ranger)
            terminal.WriteLine("  V - Ranged Attack");

        // Tactical options (monster combat only)
        if (monster != null)
        {
            terminal.WriteLine("  P - Power Attack, plus 50 percent damage, lower accuracy");
            terminal.WriteLine("  E - Precise Strike, higher accuracy");
        }

        terminal.WriteLine("  I - Disarm, remove weapon bonus");
        terminal.WriteLine("  T - Taunt, lower enemy defense");
        terminal.WriteLine("  L - Hide, attempt to slip away");

        // Retreat/Flee
        if (monster != null)
        {
            terminal.WriteLine("  R - Retreat, attempt to flee");
            terminal.WriteLine("  M - Beg for Mercy");
            terminal.WriteLine("  F - Fight to Death, no retreat");
        }
        else if (isPvP)
        {
            terminal.WriteLine("  R - Flee Combat");
        }

        // Utility
        terminal.WriteLine("  S - View Status");
        string speedLabel = player.CombatSpeed switch
        {
            CombatSpeed.Instant => "Instant",
            CombatSpeed.Fast => "Fast",
            _ => "Normal"
        };
        terminal.WriteLine($"  SPD - Combat Speed, currently {speedLabel}");
        terminal.WriteLine("");
    }

    /// <summary>
    /// Compact combat action menu for BBS 80x25 terminals (single monster combat).
    /// Fits on 2-3 lines. Quickbar skills shown as "[1-9]Skills" shortcut.
    /// </summary>
    private void ShowCombatMenuBBS(Character player, Monster? monster, Character? pvpOpponent, bool isPvP)
    {
        // Row 1: Core actions
        terminal.SetColor("bright_green");
        terminal.Write(" [A]");
        terminal.SetColor("green");
        terminal.Write("ttack ");
        terminal.SetColor("bright_cyan");
        terminal.Write("[D]");
        terminal.SetColor("cyan");
        terminal.Write("efend ");

        // Potions (healing and/or mana)
        if (player.Healing > 0 || player.ManaPotions > 0)
        {
            terminal.SetColor("bright_magenta");
            terminal.Write("[H]");
            terminal.SetColor("magenta");
            string potLabel = "";
            if (player.Healing > 0) potLabel += $"HP:{player.Healing}";
            if (player.ManaPotions > 0) potLabel += (potLabel.Length > 0 ? "/" : "") + $"MP:{player.ManaPotions}";
            terminal.Write($"Pot({potLabel}) ");
        }

        // Quickbar skills summary
        var quickbarActions = GetQuickbarActions(player);
        if (quickbarActions.Count > 0)
        {
            var available = quickbarActions.Where(q => q.available).ToList();
            if (available.Count > 0)
            {
                terminal.SetColor("bright_yellow");
                terminal.Write("[1-9]");
                terminal.SetColor("yellow");
                terminal.Write($"Skills({available.Count}) ");
            }
        }

        if (monster != null)
        {
            terminal.SetColor("yellow");
            terminal.Write("[R]");
            terminal.SetColor("white");
            terminal.Write("etreat ");
        }
        else if (isPvP)
        {
            terminal.SetColor("yellow");
            terminal.Write("[R]");
            terminal.SetColor("white");
            terminal.Write("Flee ");
        }
        terminal.WriteLine("");

        // Row 2: Tactical + utility
        if (monster != null)
        {
            terminal.SetColor("yellow");
            terminal.Write(" [P]");
            terminal.SetColor("darkgray");
            terminal.Write("ower ");
            terminal.SetColor("yellow");
            terminal.Write("[E]");
            terminal.SetColor("darkgray");
            terminal.Write("xact ");
        }
        terminal.SetColor("cyan");
        terminal.Write("[I]");
        terminal.SetColor("darkgray");
        terminal.Write("Disarm ");
        terminal.SetColor("cyan");
        terminal.Write("[T]");
        terminal.SetColor("darkgray");
        terminal.Write("aunt ");

        string speedLabel = player.CombatSpeed switch
        {
            CombatSpeed.Instant => "Inst",
            CombatSpeed.Fast => "Fast",
            _ => "Nrml"
        };
        terminal.SetColor("gray");
        terminal.Write("[AUTO] ");
        terminal.Write($"[SPD]{speedLabel} ");
        terminal.Write("[S]tats");
        terminal.WriteLine("");
    }

    /// <summary>
    /// Compact dungeon combat action menu for BBS 80x25 terminals (multi-monster combat).
    /// Fits on 2-3 lines. Quickbar skills shown as "[1-9]Skills" shortcut.
    /// </summary>
    private void ShowDungeonCombatMenuBBS(Character player, bool hasInjuredTeammates, bool canHealAlly, List<(string key, string name, bool available)> classInfo)
    {
        // Row 1: Core actions
        terminal.SetColor("bright_green");
        terminal.Write(" [A]");
        terminal.SetColor("green");
        terminal.Write("ttack ");
        terminal.SetColor("bright_cyan");
        terminal.Write("[D]");
        terminal.SetColor("cyan");
        terminal.Write("efend ");

        // Potions (healing and/or mana)
        if (player.Healing > 0 || player.ManaPotions > 0)
        {
            terminal.SetColor("bright_magenta");
            terminal.Write("[I]");
            terminal.SetColor("magenta");
            string potLabel = "";
            if (player.Healing > 0) potLabel += $"HP:{player.Healing}";
            if (player.ManaPotions > 0) potLabel += (potLabel.Length > 0 ? "/" : "") + $"MP:{player.ManaPotions}";
            terminal.Write($"Pot({potLabel}) ");
        }

        // Heal ally
        if (hasInjuredTeammates && canHealAlly)
        {
            terminal.SetColor("bright_green");
            terminal.Write("[H]");
            terminal.SetColor("green");
            terminal.Write("ealAlly ");
        }

        // Quickbar skills summary
        if (classInfo.Count > 0)
        {
            var available = classInfo.Where(c => c.available).ToList();
            if (available.Count > 0)
            {
                terminal.SetColor("bright_yellow");
                terminal.Write("[1-9]");
                terminal.SetColor("yellow");
                terminal.Write($"Skills({available.Count}) ");
            }
        }

        terminal.SetColor("yellow");
        terminal.Write("[R]");
        terminal.SetColor("white");
        terminal.Write("etreat");
        terminal.WriteLine("");

        // Row 2: Utility
        terminal.SetColor("gray");
        terminal.Write(" [AUTO] ");
        string speedLabel = player.CombatSpeed switch
        {
            CombatSpeed.Instant => "Inst",
            CombatSpeed.Fast => "Fast",
            _ => "Nrml"
        };
        terminal.Write($"[SPD]{speedLabel}");

        // Boss save option
        if (BossContext?.CanSave == true)
        {
            terminal.SetColor("bright_magenta");
            terminal.Write(" [V]");
            terminal.SetColor("magenta");
            terminal.Write("Save");
        }
        terminal.WriteLine("");
    }

    /// <summary>
    /// Display combat menu with box-drawing characters (standard visual mode)
    /// </summary>
    private void ShowCombatMenuStandard(Character player, Monster? monster, Character? pvpOpponent, bool isPvP)
    {
        // Combat header with HP display
        terminal.SetColor("bright_white");
        terminal.WriteLine("╔═══════════════════════════════════════╗");
        terminal.WriteLine("║           CHOOSE YOUR ACTION          ║");
        terminal.WriteLine("╠═══════════════════════════════════════╣");

        // HP Status line
        terminal.SetColor("cyan");
        string hpStatus = $"Your HP: {player.HP}/{player.MaxHP}";
        if (monster != null)
        {
            hpStatus += $"  │  {monster.Name}: {monster.HP}";
        }
        else if (pvpOpponent != null)
        {
            hpStatus += $"  │  {pvpOpponent.DisplayName}: {pvpOpponent.HP}/{pvpOpponent.MaxHP}";
        }
        terminal.WriteLine($"║ {hpStatus,-37} ║");

        // Show status effects if any
        if (player.ActiveStatuses.Count > 0 || player.IsRaging)
        {
            var list = new List<string>();
            foreach (var kv in player.ActiveStatuses)
            {
                var label = kv.Key.ToString();
                if (kv.Value > 0) label += $"({kv.Value})";
                list.Add(label);
            }
            if (player.IsRaging && !list.Any(s => s.StartsWith("Raging")))
                list.Add("Raging");

            terminal.SetColor("yellow");
            string statusStr = string.Join(", ", list);
            if (statusStr.Length > 35) statusStr = statusStr.Substring(0, 32) + "...";
            terminal.WriteLine($"║ Status: {statusStr,-29} ║");
        }

        terminal.SetColor("bright_white");
        terminal.WriteLine("╠═══════════════════════════════════════╣");

        // === BASIC ACTIONS ===
        terminal.SetColor("bright_green");
        terminal.Write("║ [A] ");
        terminal.SetColor("green");
        terminal.WriteLine("Attack                             ║");

        terminal.SetColor("bright_cyan");
        terminal.Write("║ [D] ");
        terminal.SetColor("cyan");
        terminal.WriteLine("Defend (reduce damage 50%)         ║");

        // === QUICKBAR SLOTS [1]-[9] ===
        var quickbarActions = GetQuickbarActions(player);
        if (quickbarActions.Count > 0)
        {
            terminal.SetColor("bright_white");
            terminal.WriteLine("╠═══════════════════════════════════════╣");
            foreach (var (qKey, slotId, displayName, available) in quickbarActions)
            {
                if (available)
                {
                    terminal.SetColor("bright_yellow");
                    terminal.Write($"║ [{qKey}] ");
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"{displayName,-33}║");
                }
                else
                {
                    terminal.SetColor("darkgray");
                    terminal.WriteLine($"║ [{qKey}] {displayName,-33}║");
                }
            }
        }

        // === HEALING OPTIONS ===
        if (player.Healing > 0)
        {
            terminal.SetColor("bright_magenta");
            terminal.Write("║ [H] ");
            terminal.SetColor("magenta");
            terminal.WriteLine($"Heal (Potions: {player.Healing}/{player.MaxPotions})              ║");
        }
        else
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine("║ [H] Heal (No Potions)                  ║");
        }

        // === CLASS-SPECIFIC ABILITIES ===
        if (player.Class == CharacterClass.Barbarian && !player.IsRaging)
        {
            terminal.SetColor("bright_red");
            terminal.Write("║ [G] ");
            terminal.SetColor("red");
            terminal.WriteLine("Rage (Berserker fury!)             ║");
        }

        if (player.Class == CharacterClass.Ranger)
        {
            terminal.SetColor("bright_yellow");
            terminal.Write("║ [V] ");
            terminal.SetColor("yellow");
            terminal.WriteLine("Ranged Attack                      ║");
        }

        // === TACTICAL OPTIONS (monster combat only) ===
        if (monster != null)
        {
            terminal.SetColor("bright_yellow");
            terminal.Write("║ [P] ");
            terminal.SetColor("yellow");
            terminal.WriteLine("Power Attack (+50% dmg, -accuracy) ║");

            terminal.SetColor("bright_yellow");
            terminal.Write("║ [E] ");
            terminal.SetColor("yellow");
            terminal.WriteLine("Precise Strike (+accuracy)         ║");
        }

        // Tactical options for both combat types
        terminal.SetColor("bright_cyan");
        terminal.Write("║ [I] ");
        terminal.SetColor("cyan");
        terminal.WriteLine("Disarm (remove weapon bonus)       ║");

        terminal.SetColor("bright_cyan");
        terminal.Write("║ [T] ");
        terminal.SetColor("cyan");
        terminal.WriteLine("Taunt (lower enemy defense)        ║");

        terminal.SetColor("gray");
        terminal.Write("║ [L] ");
        terminal.SetColor("darkgray");
        terminal.WriteLine("Hide (attempt to slip away)        ║");

        // === RETREAT/FLEE OPTIONS ===
        if (monster != null)
        {
            terminal.SetColor("yellow");
            terminal.Write("║ [R] ");
            terminal.SetColor("white");
            terminal.WriteLine("Retreat (attempt to flee)          ║");

            terminal.SetColor("gray");
            terminal.Write("║ [M] ");
            terminal.SetColor("darkgray");
            terminal.WriteLine("Beg for Mercy                      ║");

            terminal.SetColor("bright_red");
            terminal.Write("║ [F] ");
            terminal.SetColor("red");
            terminal.WriteLine("Fight to Death (no retreat)        ║");
        }
        else if (isPvP)
        {
            // PvP-specific: Flee instead of retreat
            terminal.SetColor("yellow");
            terminal.Write("║ [R] ");
            terminal.SetColor("white");
            terminal.WriteLine("Flee Combat                        ║");
        }

        // === UTILITY ===
        terminal.SetColor("gray");
        terminal.Write("║ [S] ");
        terminal.SetColor("darkgray");
        terminal.WriteLine("View Status                        ║");

        // Combat speed option
        string speedLabel = player.CombatSpeed switch
        {
            CombatSpeed.Instant => "Instant",
            CombatSpeed.Fast => "Fast",
            _ => "Normal"
        };
        terminal.SetColor("gray");
        terminal.Write("║ [SPD] ");
        terminal.SetColor("darkgray");
        terminal.WriteLine($"Combat Speed: {speedLabel,-18}║");

        terminal.SetColor("bright_white");
        terminal.WriteLine("╚═══════════════════════════════════════╝");
        terminal.WriteLine("");
    }

    /// <summary>
    /// Display dungeon combat menu in screen reader friendly format (no box-drawing characters)
    /// </summary>
    private void ShowDungeonCombatMenuScreenReader(Character player, bool hasInjuredTeammates, bool canHealAlly, List<(string key, string name, bool available)> classInfo)
    {
        terminal.WriteLine("");
        terminal.WriteLine("Dungeon Combat Menu");
        terminal.WriteLine("");

        // Basic actions
        terminal.WriteLine("Actions:");
        terminal.WriteLine("  A - Attack");
        terminal.WriteLine("  D - Defend, reduces damage by 50 percent");

        // Item option
        if (player.Healing > 0 || player.ManaPotions > 0)
        {
            string potionInfo = "";
            if (player.Healing > 0) potionInfo += $"HP:{player.Healing}";
            if (player.ManaPotions > 0) potionInfo += (potionInfo.Length > 0 ? " " : "") + $"MP:{player.ManaPotions}";
            terminal.WriteLine($"  I - Use Item, Potions: {potionInfo}");
        }
        else
            terminal.WriteLine("  I - Use Item, No Potions");

        // Heal Ally
        if (hasInjuredTeammates)
        {
            if (canHealAlly)
                terminal.WriteLine("  H - Heal Ally");
            else
                terminal.WriteLine("  H - Heal Ally, No means to heal");
        }

        // Quickbar slots
        if (classInfo.Count > 0)
        {
            terminal.WriteLine("");
            terminal.WriteLine("Quickbar:");
            foreach (var (key, name, available) in classInfo)
            {
                if (available)
                    terminal.WriteLine($"  {key} - {name}");
                else
                    terminal.WriteLine($"  {key} - {name} (unavailable)");
            }
        }

        // Boss Save option (only when fighting saveable Old God with artifact)
        if (BossContext?.CanSave == true)
            terminal.WriteLine("  V - Attempt to Save, uses Soulweaver's Loom");

        // Retreat and auto
        terminal.WriteLine("  R - Retreat, attempt to flee");
        terminal.WriteLine("  AUTO - Auto-Combat Mode");

        // Combat speed
        string speedLabel = player.CombatSpeed switch
        {
            CombatSpeed.Instant => "Instant",
            CombatSpeed.Fast => "Fast",
            _ => "Normal"
        };
        terminal.WriteLine($"  SPD - Combat Speed, currently {speedLabel}");
        terminal.WriteLine("");
    }

    /// <summary>
    /// Display dungeon combat menu with box-drawing characters (standard visual mode)
    /// </summary>
    private void ShowDungeonCombatMenuStandard(Character player, bool hasInjuredTeammates, bool canHealAlly, List<(string key, string name, bool available)> classInfo)
    {
        terminal.SetColor("bright_white");
        terminal.WriteLine("╔═══════════════════════════════════════╗");
        terminal.WriteLine("║           CHOOSE YOUR ACTION          ║");
        terminal.WriteLine("╠═══════════════════════════════════════╣");

        // Basic actions
        terminal.SetColor("bright_green");
        terminal.Write("║ [A] ");
        terminal.SetColor("green");
        terminal.WriteLine("Attack                             ║");

        terminal.SetColor("bright_cyan");
        terminal.Write("║ [D] ");
        terminal.SetColor("cyan");
        terminal.WriteLine("Defend (reduce damage 50%)         ║");

        // Item option (show potion count)
        if (player.Healing > 0)
        {
            terminal.SetColor("bright_magenta");
            terminal.Write("║ [I] ");
            terminal.SetColor("magenta");
            terminal.WriteLine($"Use Item (Potions: {player.Healing}/{player.MaxPotions})         ║");
        }
        else
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine("║ [I] Use Item (No Potions)              ║");
        }

        // Heal Ally option
        if (hasInjuredTeammates)
        {
            if (canHealAlly)
            {
                terminal.SetColor("bright_green");
                terminal.Write("║ [H] ");
                terminal.SetColor("green");
                terminal.WriteLine("Heal Ally                          ║");
            }
            else
            {
                terminal.SetColor("darkgray");
                terminal.WriteLine("║ [H] Heal Ally (No means to heal)      ║");
            }
        }

        // Quickbar slots [1]-[9]
        if (classInfo.Count > 0)
        {
            terminal.SetColor("bright_white");
            terminal.WriteLine("╠═══════════════════════════════════════╣");
            foreach (var (key, name, available) in classInfo)
            {
                if (available)
                {
                    terminal.SetColor("bright_yellow");
                    terminal.Write($"║ [{key}] ");
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"{name,-33}║");
                }
                else
                {
                    terminal.SetColor("darkgray");
                    terminal.WriteLine($"║ [{key}] {name,-33}║");
                }
            }
        }

        // Boss Save option (only when fighting saveable Old God with artifact)
        if (BossContext?.CanSave == true)
        {
            terminal.SetColor("bright_magenta");
            terminal.Write("║ [V] ");
            terminal.SetColor("magenta");
            terminal.WriteLine("Attempt to Save (Soulweaver's Loom)║");
        }

        // Retreat and auto
        terminal.SetColor("yellow");
        terminal.Write("║ [R] ");
        terminal.SetColor("white");
        terminal.WriteLine("Retreat (attempt to flee)          ║");

        terminal.SetColor("bright_cyan");
        terminal.Write("║ [AUTO] ");
        terminal.SetColor("cyan");
        terminal.WriteLine("Auto-Combat Mode                ║");

        // Combat speed option
        string speedLabel = player.CombatSpeed switch
        {
            CombatSpeed.Instant => "Instant",
            CombatSpeed.Fast => "Fast",
            _ => "Normal"
        };
        terminal.SetColor("gray");
        terminal.Write("║ [SPD]  ");
        terminal.SetColor("darkgray");
        terminal.WriteLine($"Combat Speed: {speedLabel,-18}║");

        terminal.SetColor("bright_white");
        terminal.WriteLine("╚═══════════════════════════════════════╝");
        terminal.WriteLine("");
    }

    /// <summary>
    /// Parse combat action from input
    /// </summary>
    private CombatAction ParseCombatAction(string choice, Character player)
    {
        return choice switch
        {
            "A" => new CombatAction { Type = CombatActionType.Attack },
            "V" => new CombatAction { Type = CombatActionType.RangedAttack },
            "D" => new CombatAction { Type = CombatActionType.Defend },
            "H" => new CombatAction { Type = CombatActionType.Heal },
            "Q" => new CombatAction { Type = CombatActionType.QuickHeal },
            "F" => new CombatAction { Type = CombatActionType.FightToDeath },
            "S" => new CombatAction { Type = CombatActionType.Status },
            "M" => new CombatAction { Type = CombatActionType.BegForMercy },
            "P" => new CombatAction { Type = CombatActionType.PowerAttack },
            "E" => new CombatAction { Type = CombatActionType.PreciseStrike },
            "R" => new CombatAction { Type = CombatActionType.Retreat },
            "G" when player.Class == CharacterClass.Barbarian && !player.IsRaging => new CombatAction { Type = CombatActionType.Rage },
            "I" => new CombatAction { Type = CombatActionType.Disarm },
            "T" => new CombatAction { Type = CombatActionType.Taunt },
            "L" => new CombatAction { Type = CombatActionType.Hide },
            _ => new CombatAction { Type = CombatActionType.Attack } // Default to attack
        };
    }
    
    /// <summary>
    /// Process player action - Pascal combat mechanics
    /// </summary>
    private async Task ProcessPlayerAction(CombatAction action, Character player, Monster monster, CombatResult result)
    {
        player.UsedItem = false;
        player.Casted = false;
        
        switch (action.Type)
        {
            case CombatActionType.Attack:
                await ExecuteAttack(player, monster, result);
                break;
                
            case CombatActionType.Defend:
                await ExecuteDefend(player, result);
                break;
                
            case CombatActionType.Heal:
                await ExecuteHeal(player, result, false);
                break;
                
            case CombatActionType.QuickHeal:
                await ExecuteHeal(player, result, true);
                break;
                
            case CombatActionType.FightToDeath:
                await ExecuteFightToDeath(player, monster, result);
                break;
                
            case CombatActionType.Status:
                await ShowCombatStatus(player, result);
                break;
                
            case CombatActionType.BegForMercy:
                await ExecuteBegForMercy(player, monster, result);
                break;
                
            case CombatActionType.UseItem:
                await ExecuteUseItem(player, result);
                break;
                
            case CombatActionType.CastSpell:
                await ExecuteCastSpell(player, monster, result);
                break;

            case CombatActionType.UseAbility:
            case CombatActionType.ClassAbility:
                await ExecuteUseAbility(player, monster, result);
                break;

            case CombatActionType.SoulStrike:
                await ExecuteSoulStrike(player, monster, result);
                break;
                
            case CombatActionType.Backstab:
                await ExecuteBackstab(player, monster, result);
                break;
                
            case CombatActionType.PowerAttack:
                await ExecutePowerAttack(player, monster, result);
                break;
                
            case CombatActionType.PreciseStrike:
                await ExecutePreciseStrike(player, monster, result);
                break;
                
            case CombatActionType.Retreat:
                await ExecuteRetreat(player, monster, result);
                break;
                
            case CombatActionType.Rage:
                await ExecuteRage(player, result);
                break;
                
            case CombatActionType.Smite:
                await ExecuteSmite(player, monster, result);
                break;
                
            case CombatActionType.Disarm:
                await ExecuteDisarm(player, monster, result);
                break;
                
            case CombatActionType.Taunt:
                await ExecuteTaunt(player, monster, result);
                break;
                
            case CombatActionType.Hide:
                await ExecuteHide(player, result);
                break;
                
            case CombatActionType.RangedAttack:
                await ExecuteRangedAttack(player, monster, result);
                break;
        }
    }
    
    /// <summary>
    /// Execute attack - Pascal normal_attack calculation
    /// Based on normal_attack function from VARIOUS.PAS
    /// </summary>
    private async Task ExecuteAttack(Character attacker, Monster target, CombatResult result)
    {
        int swings = GetAttackCount(attacker);
        int baseSwings = 1 + attacker.GetClassCombatModifiers().ExtraAttacks;

        for (int s = 0; s < swings && target.HP > 0; s++)
        {
            // Determine if this is an off-hand attack for dual-wielding
            // Off-hand attacks are the extra attacks from dual-wielding
            bool isOffHandAttack = attacker.IsDualWielding && s >= baseSwings;
            await ExecuteSingleAttack(attacker, target, result, s > 0, isOffHandAttack);
        }
    }

    private async Task ExecuteSingleAttack(Character attacker, Monster target, CombatResult result, bool isExtra, bool isOffHandAttack = false)
    {
        // === D20 ROLL SYSTEM FOR HIT DETERMINATION ===
        // Calculate monster AC based on level and defense
        int monsterAC = 10 + (target.Level / 3) + (int)(target.Defence / 15);

        // Apply modifiers that affect hit chance
        if (attacker.IsRaging)
            monsterAC += 4; // Rage lowers accuracy
        if (attacker.HasStatus(StatusEffect.PowerStance))
            monsterAC += 2; // Power stance is less accurate
        if (attacker.HasStatus(StatusEffect.Blessed))
            monsterAC -= 2; // Blessing helps accuracy
        if (attacker.HasStatus(StatusEffect.RoyalBlessing))
            monsterAC -= 2; // Royal blessing from the king helps accuracy
        if (attacker.Blind || attacker.HasStatus(StatusEffect.Blinded))
            monsterAC += 6; // Blindness severely reduces accuracy

        // Roll to hit using D20 system
        var attackRoll = TrainingSystem.RollAttack(attacker, monsterAC, false, null, random);

        // Show the roll result
        terminal.SetColor("dark_gray");
        terminal.WriteLine($"[Roll: {attackRoll.NaturalRoll} + {attackRoll.Modifier} = {attackRoll.Total} vs AC {monsterAC}]");

        // Show off-hand attack message
        if (isOffHandAttack)
        {
            terminal.SetColor("cyan");
            terminal.WriteLine("Off-hand strike!");
        }

        // Check for hit
        if (!attackRoll.Success && !attackRoll.IsCriticalSuccess)
        {
            // Sunforged Blade: attacks cannot miss (except critical failures)
            if (!attackRoll.IsCriticalFailure && ArtifactSystem.Instance.HasSunforgedBlade())
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("The Sunforged Blade guides your strike true!");
                // Fall through to damage calculation as a normal hit
            }
            else
            {
                // Miss!
                if (attackRoll.IsCriticalFailure)
                {
                    terminal.SetColor("dark_red");
                    terminal.WriteLine("CRITICAL MISS! You stumble badly!");
                }
                else
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine($"You missed the {target.Name}!");
                }
                result.CombatLog.Add($"Player misses {target.Name} (roll: {attackRoll.NaturalRoll})");

                // Still have chance to improve basic attack skill from attempting
                if (TrainingSystem.TryImproveFromUse(attacker, "basic_attack", random))
                {
                    var newLevel = TrainingSystem.GetSkillProficiency(attacker, "basic_attack");
                    terminal.WriteLine($"Your combat experience grows! Basic Attack is now {TrainingSystem.GetProficiencyName(newLevel)}!", "bright_yellow");
                }

                await Task.Delay(GetCombatDelay(1500));
                return;
            }
        }

        // === HIT! Calculate damage ===
        // Base damage = Strength + Strength bonus + (Level * 2) + WeapPow
        long attackPower = attacker.Strength;

        // Add Strength-based damage bonus from StatEffectsSystem
        attackPower += StatEffectsSystem.GetStrengthDamageBonus(attacker.Strength);

        // Level-based scaling - CRITICAL for high level balance
        attackPower += attacker.Level * 2;

        // Apply class/status modifiers
        if (attacker.IsRaging)
            attackPower = (long)(attackPower * 1.5); // Rage gives 50% bonus (balanced from 75%)

        if (attacker.HasStatus(StatusEffect.PowerStance))
            attackPower = (long)(attackPower * 1.5);

        if (attacker.HasStatus(StatusEffect.Blessed))
            attackPower += attacker.Level / 5 + 2;
        if (attacker.HasStatus(StatusEffect.RoyalBlessing))
            attackPower = (long)(attackPower * 1.10); // 10% damage bonus from king's blessing
        if (attacker.HasStatus(StatusEffect.Weakened))
            attackPower = Math.Max(1, attackPower - attacker.Level / 10 - 4);

        // Add weapon power with level scaling
        if (attacker.WeapPow > 0)
        {
            long weaponBonus = attacker.WeapPow + (attacker.Level / 10);
            attackPower += weaponBonus + random.Next(0, (int)Math.Min(int.MaxValue, weaponBonus + 1));
        }

        // Random attack variation - scales with level
        int variationMax = Math.Max(21, attacker.Level / 2);
        attackPower += random.Next(1, variationMax);

        // Apply weapon configuration damage modifier (2H bonus, dual-wield off-hand penalty)
        double damageModifier = GetWeaponConfigDamageModifier(attacker, isOffHandAttack);
        attackPower = (long)(attackPower * damageModifier);

        // Apply proficiency effect multiplier for basic attacks
        var basicProficiency = TrainingSystem.GetSkillProficiency(attacker, "basic_attack");
        float proficiencyMultiplier = TrainingSystem.GetEffectMultiplier(basicProficiency);
        attackPower = (long)(attackPower * proficiencyMultiplier);

        // Apply roll quality multiplier (critical hits, great hits, etc.)
        float rollMultiplier = attackRoll.GetDamageMultiplier();

        // Additional Dexterity-based critical hit chance (on top of natural 20)
        bool dexCrit = !attackRoll.IsCriticalSuccess && StatEffectsSystem.RollCriticalHit(attacker);
        if (dexCrit)
        {
            // Apply Dexterity-based crit multiplier
            rollMultiplier = StatEffectsSystem.GetCriticalDamageMultiplier(attacker.Dexterity, attacker.GetEquipmentCritDamageBonus());
        }

        attackPower = (long)(attackPower * rollMultiplier);

        // Apply difficulty modifier to player damage
        attackPower = DifficultySystem.ApplyPlayerDamageMultiplier(attackPower);

        // Sunforged Blade artifact: +100% damage vs undead and demons (+200% during Manwe fight)
        if (ArtifactSystem.Instance.HasSunforgedBlade() &&
            (target.MonsterClass == MonsterClass.Undead || target.MonsterClass == MonsterClass.Demon || target.Undead > 0))
        {
            float holyMult = IsManweBattle && ArtifactSystem.Instance.HasVoidKey() ? 3.0f : 2.0f;
            attackPower = (long)(attackPower * holyMult);
            terminal.WriteLine("The Sunforged Blade blazes with holy fire!", "bright_yellow");
        }

        // Apply grief effects - grief stage can modify damage dealt
        var griefEffects = GriefSystem.Instance.GetCurrentEffects();
        if (griefEffects.DamageModifier != 0 || griefEffects.CombatModifier != 0 || griefEffects.AllStatModifier != 0)
        {
            // Damage modifier: positive = more damage (Anger stage), negative = less damage
            // Combat modifier: general combat effectiveness (Denial/Bargaining)
            // AllStatModifier: affects everything (Depression)
            float totalGriefMod = 1.0f + griefEffects.DamageModifier + griefEffects.CombatModifier + griefEffects.AllStatModifier;
            attackPower = (long)(attackPower * totalGriefMod);

            // Show grief effect message for significant modifiers
            if (griefEffects.DamageModifier > 0.1f)
            {
                terminal.WriteLine("  (Rage fuels your strikes)", "dark_red");
            }
            else if (griefEffects.AllStatModifier < -0.1f)
            {
                terminal.WriteLine("  (Grief weighs on your arm)", "dark_gray");
            }
        }

        // Apply divine blessing bonus damage
        int divineBonusDamage = DivineBlessingSystem.Instance.CalculateBonusDamage(attacker, target, (int)attackPower);
        if (divineBonusDamage > 0)
        {
            attackPower += divineBonusDamage;
        }

        // Apply divine critical hit bonus
        int divineCritBonus = DivineBlessingSystem.Instance.GetCriticalHitBonus(attacker);
        if (divineCritBonus > 0 && !attackRoll.IsCriticalSuccess && !dexCrit)
        {
            // Extra chance for divine crit based on god's blessing
            if (random.Next(100) < divineCritBonus)
            {
                attackPower = (long)(attackPower * 1.5f);
                terminal.WriteLine($"Divine fury guides your strike!", "bright_magenta");
            }
        }

        // Poison Coating bonus damage (Black Market consumable)
        if (attacker.PoisonCoatingCombats > 0)
        {
            long poisonBonus = (long)(attackPower * GameConfig.PoisonCoatingDamageBonus);
            attackPower += poisonBonus;
        }

        // Show critical hit message
        if (attackRoll.IsCriticalSuccess)
        {
            terminal.WriteLine("CRITICAL HIT!", "bright_red");
            await Task.Delay(GetCombatDelay(500));
        }
        else if (dexCrit)
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"Precision strike! ({StatEffectsSystem.GetCriticalHitChance(attacker.Dexterity, attacker.GetEquipmentCritChanceBonus())}% crit chance)");
            await Task.Delay(GetCombatDelay(300));
        }
        else if (rollMultiplier >= 1.5f)
        {
            terminal.WriteLine("Devastating blow!", "bright_yellow");
        }
        else if (rollMultiplier >= 1.25f)
        {
            terminal.WriteLine("Solid hit!", "yellow");
        }

        // Store punch for display
        attacker.Punch = attackPower;

        terminal.SetColor("green");
        terminal.WriteLine($"You hit the {target.Name} for {attackPower} damage!");

        // Calculate defense absorption
        long defense = target.Defence + random.Next(0, (int)Math.Max(1, target.Defence / 8));

        if (target.ArmPow > 0)
        {
            // Guard against integer overflow when ArmPow is very large
            int armPowMax = (int)Math.Min(target.ArmPow, int.MaxValue - 1);
            defense += random.Next(0, armPowMax + 1);
        }

        // Armor piercing enchantment reduces effective defense
        int armorPiercePct = attacker.GetEquipmentArmorPiercing();
        if (armorPiercePct > 0)
        {
            long pierced = defense * armorPiercePct / 100;
            defense = Math.Max(0, defense - pierced);
        }

        long actualDamage = Math.Max(1, attackPower - defense);

        if (defense > 0 && defense < attackPower)
        {
            terminal.SetColor("cyan");
            if (armorPiercePct > 0)
                terminal.WriteLine($"{target.Name}'s armor absorbed {defense} points! (Pierced {armorPiercePct}%)");
            else
                terminal.WriteLine($"{target.Name}'s armor absorbed {defense} points!");
        }

        // Apply damage
        target.HP = Math.Max(0, target.HP - actualDamage);

        // Track statistics - damage dealt
        bool wasCritical = attackRoll.IsCriticalSuccess || dexCrit;
        attacker.Statistics.RecordDamageDealt(actualDamage, wasCritical);

        // Track for telemetry
        result.TotalDamageDealt += actualDamage;

        terminal.SetColor("red");
        terminal.WriteLine($"{target.Name} takes {actualDamage} damage!");

        result.CombatLog.Add($"Player attacks {target.Name} for {actualDamage} damage (roll: {attackRoll.NaturalRoll})");

        // Apply divine lifesteal
        int lifesteal = DivineBlessingSystem.Instance.CalculateLifesteal(attacker, (int)actualDamage);
        if (lifesteal > 0)
        {
            attacker.HP = Math.Min(attacker.MaxHP, attacker.HP + lifesteal);
            terminal.WriteLine($"Dark power drains {lifesteal} life from your enemy!", "dark_magenta");
        }

        // Equipment lifesteal (Lifedrinker enchant)
        int equipLifeSteal = attacker.GetEquipmentLifeSteal();
        if (equipLifeSteal > 0 && actualDamage > 0)
        {
            long stolen = Math.Max(1, actualDamage * equipLifeSteal / 100);
            attacker.HP = Math.Min(attacker.MaxHP, attacker.HP + stolen);
            terminal.WriteLine($"Your weapon drains {stolen} life! (Lifedrinker)", "dark_green");
        }

        // Elemental enchant procs (v0.30.9)
        CheckElementalEnchantProcs(attacker, target, actualDamage, result);

        // Sunforged Blade: heals lowest-HP ally for 10% of damage dealt
        if (actualDamage > 0 && ArtifactSystem.Instance.HasSunforgedBlade() && currentTeammates != null && currentTeammates.Count > 0)
        {
            var injuredAlly = currentTeammates
                .Where(t => t.IsAlive && t.HP < t.MaxHP)
                .OrderBy(t => (double)t.HP / t.MaxHP)
                .FirstOrDefault();
            if (injuredAlly != null)
            {
                long allyHeal = Math.Max(1, actualDamage / 10);
                long oldHP = injuredAlly.HP;
                injuredAlly.HP = Math.Min(injuredAlly.MaxHP, injuredAlly.HP + allyHeal);
                long actualHeal = injuredAlly.HP - oldHP;
                if (actualHeal > 0)
                {
                    terminal.WriteLine($"Radiant light heals {injuredAlly.DisplayName} for {actualHeal} HP!", "bright_yellow");
                }
            }
        }

        // Chance to improve basic attack skill from successful use
        if (TrainingSystem.TryImproveFromUse(attacker, "basic_attack", random))
        {
            var newLevel = TrainingSystem.GetSkillProficiency(attacker, "basic_attack");
            terminal.WriteLine($"Your combat experience grows! Basic Attack is now {TrainingSystem.GetProficiencyName(newLevel)}!", "bright_yellow");
        }

        await Task.Delay(GetCombatDelay(1500));
    }
    
    /// <summary>
    /// Execute heal action
    /// </summary>
    private async Task ExecuteHeal(Character player, CombatResult result, bool quick)
    {
        bool hasHealing = player.Healing > 0 && player.HP < player.MaxHP;
        bool hasMana = player.ManaPotions > 0 && player.Mana < player.MaxMana;

        // If player has mana potions, route through the item submenu for choice
        if (hasMana && !quick)
        {
            await ExecuteUseItem(player, result);
            return;
        }

        if (player.HP >= player.MaxHP)
        {
            terminal.WriteLine("You are already at full health!", "yellow");
            await Task.Delay(GetCombatDelay(1000));
            return;
        }

        if (player.Healing <= 0)
        {
            terminal.WriteLine("You have no healing potions!", "red");
            await Task.Delay(GetCombatDelay(1000));
            return;
        }

        if (quick)
        {
            // Quick heal uses one potion
            player.Healing--;
            long healAmount = 30 + player.Level * 5 + random.Next(10, 30);
            healAmount = Math.Min(healAmount, player.MaxHP - player.HP);
            player.HP += healAmount;
            player.Statistics?.RecordPotionUsed(healAmount);
            terminal.WriteLine($"You quickly quaff a healing potion for {healAmount} HP!", "green");
            result.CombatLog.Add($"Player heals for {healAmount} HP");
        }
        else
        {
            // Regular heal - ask how many potions to use for full control
            long missingHP = player.MaxHP - player.HP;
            long avgHealPerPotion = 50 + player.Level * 5;  // Average heal: 30 + level*5 + avg(10-30)
            int potionsToFullHeal = (int)Math.Ceiling((double)missingHP / avgHealPerPotion);
            potionsToFullHeal = Math.Min(potionsToFullHeal, (int)player.Healing);

            terminal.WriteLine($"You have {player.Healing} healing potions.", "cyan");
            terminal.WriteLine($"Missing {missingHP} HP. (~{potionsToFullHeal} potions to full)", "cyan");
            var input = await terminal.GetInput($"How many potions? (1-{player.Healing}, F=full, Enter=1): ");

            int potionsToUse = 1;
            if (input.Trim().Equals("F", StringComparison.OrdinalIgnoreCase))
            {
                // Use enough potions to heal to full
                potionsToUse = potionsToFullHeal;
            }
            else if (!string.IsNullOrWhiteSpace(input) && int.TryParse(input, out int parsed))
            {
                potionsToUse = Math.Clamp(parsed, 1, (int)player.Healing);
            }

            long totalHeal = 0;
            for (int i = 0; i < potionsToUse && player.HP < player.MaxHP; i++)
            {
                player.Healing--;
                long healAmount = 30 + player.Level * 5 + random.Next(10, 30);
                healAmount = Math.Min(healAmount, player.MaxHP - player.HP);
                player.HP += healAmount;
                totalHeal += healAmount;
            }

            player.Statistics?.RecordPotionUsed(totalHeal);
            terminal.WriteLine($"You use {potionsToUse} potion(s) and heal {totalHeal} HP!", "bright_green");
            result.CombatLog.Add($"Player heals for {totalHeal} HP using {potionsToUse} potions");
        }

        await Task.Delay(GetCombatDelay(1000));
    }
    
    /// <summary>
    /// Execute backstab (Assassin special ability)
    /// Based on Pascal backstab mechanics
    /// </summary>
    private async Task ExecuteBackstab(Character player, Monster target, CombatResult result)
    {
        if (target == null)
        {
            terminal.WriteLine("Backstab has no effect in this combat!", "yellow");
            await Task.Delay(GetCombatDelay(500));
            return;
        }

        terminal.SetColor("bright_yellow");
        terminal.WriteLine("You attempt to backstab!");
        await Task.Delay(GetCombatDelay(1000));
        
        // Backstab calculation (Pascal-compatible)
        long backstabPower = player.Strength + player.WeapPow;
        backstabPower = (long)(backstabPower * GameConfig.BackstabMultiplier); // 3x damage

        // Backstab success chance based on dexterity
        // Formula: DEX * 2, but capped at 75% to prevent guaranteed success exploit
        // Drug DexterityBonus adds to effective DEX (e.g., QuickSilver: +20)
        var drugEffects = DrugSystem.GetDrugEffects(player);
        long effectiveDex = player.Dexterity + drugEffects.DexterityBonus;
        int successChance = Math.Min(75, (int)(effectiveDex * 2));
        if (random.Next(100) < successChance)
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine($"BACKSTAB! You strike from the shadows for {backstabPower} damage!");

            target.HP = Math.Max(0, target.HP - backstabPower);
            player.Statistics.RecordDamageDealt(backstabPower, true); // Backstab counts as critical
            result.TotalDamageDealt += backstabPower; // Track for telemetry
            result.CombatLog.Add($"Player backstabs {target.Name} for {backstabPower} damage");
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("Your backstab attempt fails!");
            result.CombatLog.Add($"Player backstab fails against {target.Name}");
        }
        
        await Task.Delay(GetCombatDelay(2000));
    }
    
    /// <summary>
    /// Execute Soul Strike (Paladin special ability)
    /// Based on Soul_Effect from VARIOUS.PAS
    /// </summary>
    private async Task ExecuteSoulStrike(Character player, Monster target, CombatResult result)
    {
        terminal.SetColor("bright_white");
        terminal.WriteLine("You channel divine power for a Soul Strike!");
        await Task.Delay(GetCombatDelay(1000));
        
        // Soul Strike power based on chivalry and level
        long soulPower = (player.Chivalry / 10) + (player.Level * 5);
        
        if (soulPower > 0)
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"Divine energy strikes for {soulPower} damage!");

            target.HP = Math.Max(0, target.HP - soulPower);
            player.Statistics.RecordDamageDealt(soulPower, false);
            result.TotalDamageDealt += soulPower; // Track for telemetry
            result.CombatLog.Add($"Player Soul Strike hits {target.Name} for {soulPower} damage");
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("Your soul lacks the purity for this attack!");
            result.CombatLog.Add($"Player Soul Strike fails - insufficient chivalry");
        }
        
        await Task.Delay(GetCombatDelay(2000));
    }
    
    /// <summary>
    /// Execute retreat - Pascal retreat mechanics
    /// Based on Retreat function from PLVSMON.PAS
    /// </summary>
    private async Task ExecuteRetreat(Character player, Monster monster, CombatResult result)
    {
        // Check if fleeing is allowed on current difficulty
        if (!DifficultySystem.CanFlee())
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine("NIGHTMARE MODE: There is no escape. Fight or die!");
            await Task.Delay(GetCombatDelay(1500));
            return;
        }

        terminal.SetColor("yellow");
        terminal.WriteLine("You attempt to flee from combat!");
        await Task.Delay(GetCombatDelay(1000));

        // IMPROVED ESCAPE FORMULA:
        // Base 40% + Dexterity bonus (each 10 dex = +5%) + Level bonus (each 10 levels = +3%)
        // Rangers/Assassins get +15% bonus
        // Maximum 75% chance to escape (prevents guaranteed escapes)
        int escapeChance = 40;
        escapeChance += (int)(player.Dexterity / 2); // Dex contributes significantly
        escapeChance += player.Level / 3; // Level helps too

        // Class bonuses for agile classes
        if (player.Class == CharacterClass.Ranger || player.Class == CharacterClass.Assassin)
            escapeChance += 15;
        if (player.Class == CharacterClass.Jester || player.Class == CharacterClass.Bard)
            escapeChance += 10;

        // Shadows faction escape bonus
        escapeChance += FactionSystem.Instance?.GetEscapeChanceBonus() ?? 0;

        // Cap at 75% to prevent guaranteed escapes
        escapeChance = Math.Min(75, escapeChance);

        // Smoke bomb: guaranteed escape from non-boss combat
        if (player.SmokeBombs > 0 && !monster.IsBoss)
        {
            player.SmokeBombs--;
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("You throw a smoke bomb! The monster chokes on the haze!");
            terminal.SetColor("green");
            terminal.WriteLine("You escape in the confusion!");
            globalEscape = true;
            result.Outcome = CombatOutcome.PlayerEscaped;
            result.CombatLog.Add("Player escaped using smoke bomb");
            player.Statistics.TotalCombatsFled++;
            await Task.Delay(GetCombatDelay(1500));
            return;
        }

        terminal.SetColor("gray");
        terminal.WriteLine($"(Escape chance: {escapeChance}%)");

        if (random.Next(100) < escapeChance)
        {
            terminal.SetColor("green");
            terminal.WriteLine("You have escaped battle!");
            globalEscape = true;
            result.Outcome = CombatOutcome.PlayerEscaped;
            result.CombatLog.Add("Player successfully retreated");
            player.Statistics.TotalCombatsFled++;
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine("The monster won't let you escape!");

            // Reduced damage for failed escape - scales moderately with monster level
            // At worst, lose 5-15% of max HP
            long maxEscapeDamage = Math.Max(10, player.MaxHP / 10);
            long escapeDamage = random.Next((int)Math.Min(int.MaxValue, maxEscapeDamage / 2), (int)Math.Min(int.MaxValue, maxEscapeDamage));

            terminal.WriteLine($"As you turn to flee, you take {escapeDamage} damage!");

            player.HP = Math.Max(1, player.HP - escapeDamage); // Never kills - just reduces to 1 HP
            result.CombatLog.Add($"Player retreat fails, takes {escapeDamage} damage");

            // Failed escape doesn't kill, but warns player
            if (player.HP <= player.MaxHP / 10)
            {
                terminal.SetColor("bright_red");
                terminal.WriteLine("WARNING: You are critically wounded!");
            }
        }

        await Task.Delay(GetCombatDelay(2000));
    }
    
    /// <summary>
    /// Execute beg for mercy
    /// </summary>
    private async Task ExecuteBegForMercy(Character player, Monster monster, CombatResult result)
    {
        if (globalNoBeg)
        {
            terminal.WriteLine("The monster shows no mercy!", "red");
            await Task.Delay(GetCombatDelay(1500));
            return;
        }
        
        terminal.SetColor("yellow");
        terminal.WriteLine("You beg for mercy!");
        
        // Mercy chance based on charisma
        // Formula: CHA * 2, but capped at 75% to prevent guaranteed escape exploit
        int mercyChance = Math.Min(75, (int)(player.Charisma * 2));
        if (random.Next(100) < mercyChance && !globalBegged)
        {
            terminal.SetColor("green");
            terminal.WriteLine("The monster takes pity on you and lets you live!");
            globalEscape = true;
            globalBegged = true;
            result.Outcome = CombatOutcome.PlayerEscaped;
            result.CombatLog.Add("Player successfully begged for mercy");
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine("Your pleas fall on deaf ears!");
            result.CombatLog.Add("Player begging fails");
        }
        
        await Task.Delay(GetCombatDelay(2000));
    }
    
    /// <summary>
    /// Process monster action - Pascal AI
    /// Based on monster behavior from PLVSMON.PAS
    /// </summary>
    private async Task ProcessMonsterAction(Monster monster, Character player, CombatResult result)
    {
        if (!monster.IsAlive) return;

        terminal.SetColor("red");

        // Tick monster statuses
        if (monster.PoisonRounds > 0)
        {
            int dmg = random.Next(1, 5); // 1d4
            monster.HP = Math.Max(0, monster.HP - dmg);
            monster.PoisonRounds--;
            terminal.WriteLine($"Poison burns {monster.Name} for {dmg} damage!", "dark_green");
            if (monster.PoisonRounds == 0) monster.Poisoned = false;
        }

        if (monster.StunRounds > 0)
        {
            monster.StunRounds--;
            terminal.WriteLine($"{monster.Name} is stunned and cannot act!", "cyan");
            await Task.Delay(GetCombatDelay(600));
            return; // Skip action
        }

        // Check if monster is stunned from ability effects
        if (monster.Stunned)
        {
            monster.Stunned = false; // One-round stun
            terminal.WriteLine($"{monster.Name} is stunned and cannot act!", "cyan");
            await Task.Delay(GetCombatDelay(600));
            return;
        }

        // Check if monster is charmed (may skip attack)
        if (monster.Charmed)
        {
            if (random.Next(100) < 50) // 50% chance to skip
            {
                terminal.WriteLine($"{monster.Name} hesitates, charmed by your presence!", "magenta");
                monster.Charmed = false;
                await Task.Delay(GetCombatDelay(600));
                return;
            }
            monster.Charmed = false;
        }

        // Check if monster is sleeping (from Sleep spell or ability)
        if (monster.IsSleeping)
        {
            terminal.WriteLine($"{monster.Name} is fast asleep!", "cyan");
            monster.SleepDuration--;
            if (monster.SleepDuration <= 0)
            {
                monster.IsSleeping = false;
                terminal.WriteLine($"{monster.Name} wakes up!", "yellow");
            }
            await Task.Delay(GetCombatDelay(600));
            return;
        }

        // Check if monster is feared (from Fear spell or Intimidating Roar)
        if (monster.IsFeared)
        {
            terminal.WriteLine($"{monster.Name} cowers in fear and cannot attack!", "yellow");
            monster.FearDuration--;
            if (monster.FearDuration <= 0)
            {
                monster.IsFeared = false;
                terminal.WriteLine($"{monster.Name} shakes off the fear!", "yellow");
            }
            await Task.Delay(GetCombatDelay(600));
            return;
        }

        // Check if monster is stunned (modern stun from spells, distinct from legacy Stunned)
        if (monster.IsStunned)
        {
            terminal.WriteLine($"{monster.Name} is stunned and cannot act!", "bright_yellow");
            monster.StunDuration--;
            if (monster.StunDuration <= 0)
            {
                monster.IsStunned = false;
            }
            await Task.Delay(GetCombatDelay(600));
            return;
        }

        // Check if monster is frozen (from Frost Bomb)
        if (monster.IsFrozen)
        {
            terminal.WriteLine($"{monster.Name} is frozen solid and cannot act!", "bright_cyan");
            monster.FrozenDuration--;
            if (monster.FrozenDuration <= 0)
            {
                monster.IsFrozen = false;
                terminal.WriteLine($"The ice around {monster.Name} shatters!", "cyan");
            }
            await Task.Delay(GetCombatDelay(600));
            return;
        }

        // Check if monster is confused (from Deadly Joke)
        if (monster.IsConfused)
        {
            monster.ConfusedDuration--;
            if (random.Next(100) < 50)
            {
                terminal.WriteLine($"{monster.Name} stumbles around in confusion!", "magenta");
                if (monster.ConfusedDuration <= 0) monster.IsConfused = false;
                await Task.Delay(GetCombatDelay(600));
                return;
            }
            else if (random.Next(100) < 25)
            {
                long selfDmg = Math.Max(1, monster.Strength / 3);
                monster.HP -= selfDmg;
                terminal.SetColor("magenta");
                terminal.WriteLine($"{monster.Name} hits itself in confusion for {selfDmg} damage!");
                if (monster.HP <= 0)
                {
                    monster.HP = 0;
                    terminal.SetColor("bright_green");
                    terminal.WriteLine($"{monster.Name} defeats itself!");
                }
                if (monster.ConfusedDuration <= 0) monster.IsConfused = false;
                await Task.Delay(GetCombatDelay(600));
                return;
            }
            if (monster.ConfusedDuration <= 0) monster.IsConfused = false;
            // Falls through to normal action
        }

        // Check if monster is slowed (from Slow spell)
        if (monster.IsSlowed)
        {
            monster.SlowDuration--;
            if (random.Next(100) < 50)
            {
                terminal.WriteLine($"{monster.Name} moves sluggishly and wastes its turn!", "gray");
                if (monster.SlowDuration <= 0) monster.IsSlowed = false;
                await Task.Delay(GetCombatDelay(600));
                return;
            }
            if (monster.SlowDuration <= 0) monster.IsSlowed = false;
        }

        // Tick down marked duration
        if (monster.IsMarked)
        {
            monster.MarkedDuration--;
            if (monster.MarkedDuration <= 0)
            {
                monster.IsMarked = false;
                terminal.WriteLine($"The mark on {monster.Name} fades.", "gray");
            }
        }

        // Check if player will dodge the next attack
        if (player.DodgeNextAttack)
        {
            player.DodgeNextAttack = false;
            terminal.WriteLine($"You deftly dodge {monster.Name}'s attack!", "bright_cyan");
            await Task.Delay(GetCombatDelay(600));
            return;
        }

        // === SMART MONSTER TARGETING ===
        // Monsters intelligently choose targets based on threat, class roles, and positioning
        var aliveTeammates = result.Teammates?.Where(t => t.IsAlive).ToList();
        if (aliveTeammates != null && aliveTeammates.Count > 0)
        {
            var targetChoice = SelectMonsterTarget(player, aliveTeammates, monster, random);
            if (targetChoice != null && targetChoice != player)
            {
                await MonsterAttacksCompanion(monster, targetChoice, result);
                return;
            }
            // If targetChoice is player, fall through to normal player attack
        }

        // === MONSTER SPECIAL ABILITIES ===
        // Chance for monster to use a special ability instead of normal attack
        bool usedSpecialAbility = await TryMonsterSpecialAbility(monster, player, result);
        if (usedSpecialAbility)
        {
            await Task.Delay(GetCombatDelay(800));
            return; // Special ability replaced normal attack
        }

        // === D20 ROLL SYSTEM FOR MONSTER ATTACK ===
        // Roll monster attack against player AC
        var monsterRoll = TrainingSystem.RollMonsterAttack(monster, player, random);

        // Show the roll result
        terminal.SetColor("dark_gray");
        terminal.WriteLine($"[{monster.Name} rolls: {monsterRoll.NaturalRoll} + {monsterRoll.Modifier} = {monsterRoll.Total} vs AC {monsterRoll.TargetDC}]");

        // Blur / duplicate miss chance (20%) - additional miss chance on top of D20
        if (player.HasStatus(StatusEffect.Blur) && monsterRoll.Success)
        {
            if (random.Next(100) < 20)
            {
                var missMessage = CombatMessages.GetMonsterAttackMessage(monster.Name, monster.MonsterColor, 0, player.MaxHP);
                terminal.WriteLine(missMessage);
                terminal.WriteLine($"The attack strikes only illusory images!", "gray");
                result.CombatLog.Add($"{monster.Name} misses due to blur");
                await Task.Delay(GetCombatDelay(800));
                return;
            }
        }

        // Agility-based dodge chance (from StatEffectsSystem)
        if (monsterRoll.Success && StatEffectsSystem.RollDodge(player))
        {
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"You nimbly dodge {monster.Name}'s attack! ({StatEffectsSystem.GetDodgeChance(player.Agility)}% dodge)");
            result.CombatLog.Add($"Player dodges {monster.Name}'s attack");
            await Task.Delay(GetCombatDelay(800));
            return;
        }

        // Shadow Crown artifact: 30% chance to dodge any attack (60% during Manwe fight)
        if (monsterRoll.Success && ArtifactSystem.Instance.HasShadowCrown())
        {
            int shadowDodge = IsManweBattle && ArtifactSystem.Instance.HasVoidKey() ? 60 : 30;
            if (random.Next(100) < shadowDodge)
            {
                terminal.SetColor("dark_magenta");
                terminal.WriteLine($"You slip through the shadows, evading {monster.Name}'s attack!");
                result.CombatLog.Add($"Player shadow-dodges {monster.Name}'s attack (Shadow Crown)");
                await Task.Delay(GetCombatDelay(800));
                return;
            }
        }

        // Check for miss
        if (!monsterRoll.Success && !monsterRoll.IsCriticalSuccess)
        {
            if (monsterRoll.IsCriticalFailure)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine($"The {monster.Name} stumbles and misses badly!");
            }
            else
            {
                terminal.WriteLine($"The {monster.Name} attacks but misses!");
            }
            result.CombatLog.Add($"{monster.Name} misses player (roll: {monsterRoll.NaturalRoll})");
            await Task.Delay(GetCombatDelay(1500));
            return;
        }

        // === HIT! Calculate damage ===
        long monsterAttack = monster.GetAttackPower();

        // Add random variation
        int variationMax = monster.Level <= 3 ? 6 : 10;
        monsterAttack += random.Next(0, variationMax);

        // Apply roll quality multiplier
        float rollMultiplier = monsterRoll.GetDamageMultiplier();
        monsterAttack = (long)(monsterAttack * rollMultiplier);

        // Apply difficulty modifier to monster damage
        monsterAttack = DifficultySystem.ApplyMonsterDamageMultiplier(monsterAttack);

        // Show critical hit message
        if (monsterRoll.IsCriticalSuccess)
        {
            terminal.WriteLine($"CRITICAL HIT from {monster.Name}!", "bright_red");
        }

        // Use colored combat message
        var attackMessage = CombatMessages.GetMonsterAttackMessage(monster.Name, monster.MonsterColor, monsterAttack, player.MaxHP);
        terminal.WriteLine(attackMessage);

        // Check for shield block (20% chance to double shield AC)
        var (blocked, blockBonus) = TryShieldBlock(player);
        if (blocked)
        {
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"You raise your shield and block the attack!");
        }

        // Player defense
        long playerDefense = player.Defence + random.Next(0, (int)Math.Max(1, player.Defence / 8));
        playerDefense += player.MagicACBonus;

        if (player.ArmPow > 0)
        {
            // Guard against integer overflow when ArmPow is very large
            int armPowMax = (int)Math.Min(player.ArmPow, int.MaxValue - 1);
            playerDefense += random.Next(0, armPowMax + 1);
        }

        playerDefense += blockBonus;

        double defenseModifier = GetWeaponConfigDefenseModifier(player);
        playerDefense = (long)(playerDefense * defenseModifier);

        if (player.HasStatus(StatusEffect.Blessed))
            playerDefense += 2;
        if (player.HasStatus(StatusEffect.RoyalBlessing))
            playerDefense = (long)(playerDefense * 1.10); // 10% defense bonus from king's blessing
        if (player.IsRaging)
            playerDefense = Math.Max(0, playerDefense - 4);

        // Apply temporary defense bonus from abilities
        playerDefense += player.TempDefenseBonus;

        // Apply grief effects to defense - grief stage can modify defense
        var griefDefenseEffects = GriefSystem.Instance.GetCurrentEffects();
        if (griefDefenseEffects.DefenseModifier != 0 || griefDefenseEffects.AllStatModifier != 0)
        {
            // Defense modifier: positive = more defense, negative = less defense (Anger stage)
            // AllStatModifier: affects everything (Depression)
            float totalGriefDefMod = 1.0f + griefDefenseEffects.DefenseModifier + griefDefenseEffects.AllStatModifier;
            playerDefense = (long)(playerDefense * totalGriefDefMod);
        }

        // Apply monster distraction (reduces monster accuracy effectively increasing defense)
        if (monster.Distracted)
        {
            playerDefense += 15; // Distraction gives effective +15 defense
            monster.Distracted = false;
        }

        long actualDamage = Math.Max(1, monsterAttack - playerDefense);

        // Worldstone artifact: 50% damage reduction (75% during Manwe fight)
        if (ArtifactSystem.Instance.HasWorldstone())
        {
            float wsReduction = IsManweBattle && ArtifactSystem.Instance.HasVoidKey() ? 0.75f : 0.50f;
            long reduced = (long)(actualDamage * wsReduction);
            if (reduced > 0)
            {
                actualDamage = Math.Max(1, actualDamage - reduced);
                terminal.WriteLine($"The Worldstone absorbs the impact! (-{reduced})", "dark_green");
            }
        }

        // Defending halves damage
        if (player.IsDefending)
        {
            actualDamage = (long)Math.Ceiling(actualDamage / 2.0);
        }

        // Apply divine damage reduction
        int divineReduction = DivineBlessingSystem.Instance.CalculateDamageReduction(player, (int)actualDamage);
        if (divineReduction > 0)
        {
            actualDamage = Math.Max(1, actualDamage - divineReduction);
            terminal.WriteLine($"Divine protection absorbs {divineReduction} damage!", "bright_cyan");
        }

        // Check for divine intervention (save from lethal hit)
        bool wouldDie = player.HP - actualDamage <= 0;
        if (wouldDie && DivineBlessingSystem.Instance.CheckDivineIntervention(player, (int)actualDamage))
        {
            var blessing = DivineBlessingSystem.Instance.GetBlessings(player);
            terminal.WriteLine($"{blessing.GodName} intervenes!", "bright_magenta");
            terminal.WriteLine("Divine light surrounds you, turning death aside!", "bright_white");
            actualDamage = player.HP - 1; // Survive with 1 HP
            wouldDie = false;
        }

        // Check for companion sacrifice (if player would still die)
        if (wouldDie && result.Teammates != null)
        {
            var sacrificeResult = await CheckCompanionSacrifice(player, (int)actualDamage, result);
            if (sacrificeResult.SacrificeOccurred)
            {
                // Companion took the damage instead
                actualDamage = 0;
            }
        }

        // Invulnerable: divine shield blocks all damage
        if (player.HasStatus(StatusEffect.Invulnerable))
        {
            terminal.WriteLine("The attack cannot penetrate your divine shield!", "bright_white");
            result.CombatLog.Add($"{monster.Name}'s attack blocked by Invulnerable");
            await Task.Delay(GetCombatDelay(600));
            return;
        }

        // Apply damage
        player.HP = Math.Max(0, player.HP - actualDamage);

        // Track statistics - damage taken
        player.Statistics.RecordDamageTaken(actualDamage);

        // Track for telemetry
        result.TotalDamageTaken += actualDamage;

        terminal.SetColor("red");
        if (blocked && actualDamage < monsterAttack / 2)
        {
            terminal.WriteLine($"Your shield absorbs most of the blow! You take only {actualDamage} damage!", "bright_cyan");
        }
        else if (player.IsDefending)
        {
            terminal.WriteLine($"You brace for impact and only take {actualDamage} damage!", "bright_cyan");
        }
        else
        {
            terminal.WriteLine($"{player.DisplayName} takes {actualDamage} damage!");
        }

        result.CombatLog.Add($"{monster.Name} attacks player for {actualDamage} damage (roll: {monsterRoll.NaturalRoll})");

        // Scales of Law artifact: reflect 15% of damage taken (30% during Manwe fight)
        if (actualDamage > 0 && monster.IsAlive && ArtifactSystem.Instance.HasScalesOfLaw())
        {
            float reflectPct = IsManweBattle && ArtifactSystem.Instance.HasVoidKey() ? 0.30f : 0.15f;
            long reflectedDamage = Math.Max(1, (long)(actualDamage * reflectPct));
            monster.HP -= reflectedDamage;
            terminal.WriteLine($"The Scales of Law reflect {reflectedDamage} damage back at {monster.Name}!", "gray");
            if (monster.HP <= 0)
            {
                terminal.WriteLine($"{monster.Name} is destroyed by karmic justice!", "bright_white");
            }
        }

        // Equipment thorns: reflect damage based on Thorns enchantment
        int thornsPct = player.GetEquipmentThorns();
        if (thornsPct > 0 && actualDamage > 0 && monster.IsAlive)
        {
            long thornsDamage = Math.Max(1, actualDamage * thornsPct / 100);
            monster.HP -= thornsDamage;
            terminal.SetColor("yellow");
            terminal.WriteLine($"Thorns reflect {thornsDamage} damage back at {monster.Name}! (Retaliation)");
            if (monster.HP <= 0)
            {
                terminal.WriteLine($"{monster.Name} falls to your thorns!", "bright_yellow");
            }
        }

        // Note: Defend status is now cleared at end of round in ProcessEndOfRoundAbilityEffects
        // so it protects against ALL monster attacks in the round, not just the first one

        await Task.Delay(GetCombatDelay(2000));
    }

    /// <summary>
    /// Try to use a monster special ability instead of normal attack
    /// Returns true if ability was used (skip normal attack), false otherwise
    /// </summary>
    private async Task<bool> TryMonsterSpecialAbility(Monster monster, Character player, CombatResult result)
    {
        // No abilities? Normal attack
        if (monster.SpecialAbilities == null || monster.SpecialAbilities.Count == 0)
            return false;

        // Base 30% chance to use special ability (scales with monster level)
        int abilityChance = 30 + (monster.Level / 5);
        if (random.Next(100) >= abilityChance)
            return false;

        // Pick a random ability
        string abilityName = monster.SpecialAbilities[random.Next(monster.SpecialAbilities.Count)];

        // Try to parse as AbilityType
        if (!Enum.TryParse<MonsterAbilities.AbilityType>(abilityName, true, out var abilityType))
            return false; // Unknown ability name

        // Execute the ability
        var abilityResult = MonsterAbilities.ExecuteAbility(abilityType, monster, player);

        // Display ability message
        if (!string.IsNullOrEmpty(abilityResult.Message))
        {
            terminal.SetColor(abilityResult.MessageColor ?? "red");
            terminal.WriteLine(abilityResult.Message);
        }

        // Apply direct damage
        if (abilityResult.DirectDamage > 0)
        {
            if (player.HasStatus(StatusEffect.Invulnerable))
            {
                terminal.WriteLine("Your divine shield absorbs the ability!", "bright_white");
                result.CombatLog.Add($"{monster.Name}'s {abilityName} blocked by Invulnerable");
            }
            else
            {
                long actualDamage = Math.Max(1, abilityResult.DirectDamage - (player.Defence / 3));

                // Drug MagicResistBonus reduces monster ability damage (e.g., ThirdEye: +25%)
                var drugEffects = DrugSystem.GetDrugEffects(player);
                if (drugEffects.MagicResistBonus > 0)
                    actualDamage = Math.Max(1, (long)(actualDamage * (1.0 - drugEffects.MagicResistBonus / 100.0)));

                player.HP -= actualDamage;
                terminal.WriteLine($"You take {actualDamage} damage!", "red");
                result.CombatLog.Add($"{monster.Name} uses {abilityName} for {actualDamage} damage");
            }
        }

        // Apply mana drain
        if (abilityResult.ManaDrain > 0)
        {
            player.Mana = Math.Max(0, player.Mana - abilityResult.ManaDrain);
            result.CombatLog.Add($"{monster.Name} drains {abilityResult.ManaDrain} mana");
        }

        // Apply status effects
        if (abilityResult.InflictStatus != StatusEffect.None && abilityResult.StatusChance > 0)
        {
            if (player.HasStatusImmunity)
            {
                terminal.WriteLine($"Your iron will resists {abilityResult.InflictStatus}!", "bright_white");
                result.CombatLog.Add($"Player resists {abilityResult.InflictStatus} (status immunity)");
            }
            else if (random.Next(100) < abilityResult.StatusChance)
            {
                player.ApplyStatus(abilityResult.InflictStatus, abilityResult.StatusDuration);
                terminal.WriteLine($"You are afflicted with {abilityResult.InflictStatus}!", "yellow");
                result.CombatLog.Add($"Player afflicted with {abilityResult.InflictStatus}");
            }
        }

        // Apply life steal
        if (abilityResult.LifeStealPercent > 0 && abilityResult.DamageMultiplier > 0)
        {
            if (player.HasStatus(StatusEffect.Invulnerable))
            {
                terminal.WriteLine("Your divine shield deflects the life drain!", "bright_white");
                result.CombatLog.Add($"{monster.Name}'s life drain blocked by Invulnerable");
            }
            else
            {
                // Do a regular attack with life steal
                long damage = (long)(monster.GetAttackPower() * abilityResult.DamageMultiplier);
                damage = Math.Max(1, damage - player.Defence);
                player.HP -= damage;
                long healAmount = damage * abilityResult.LifeStealPercent / 100;
                monster.HP = Math.Min(monster.MaxHP, monster.HP + healAmount);
                terminal.WriteLine($"You take {damage} damage! {monster.Name} heals {healAmount}!", "magenta");
                result.CombatLog.Add($"{monster.Name} life drains for {damage} damage, heals {healAmount}");
            }
        }

        // Apply damage multiplier attacks (non-life steal)
        if (abilityResult.DamageMultiplier > 0 && abilityResult.LifeStealPercent == 0 && abilityResult.DirectDamage == 0)
        {
            if (player.HasStatus(StatusEffect.Invulnerable))
            {
                terminal.WriteLine("Your divine shield absorbs the blow!", "bright_white");
                result.CombatLog.Add($"{monster.Name}'s {abilityName} blocked by Invulnerable");
            }
            else
            {
                long damage = (long)(monster.GetAttackPower() * abilityResult.DamageMultiplier);
                damage = Math.Max(1, damage - player.Defence);
                player.HP -= damage;
                terminal.WriteLine($"You take {damage} damage!", "red");
                result.CombatLog.Add($"{monster.Name} uses {abilityName} for {damage} damage");
            }
        }

        return abilityResult.SkipNormalAttack || abilityResult.DirectDamage > 0 || abilityResult.ManaDrain > 0
               || abilityResult.LifeStealPercent > 0 || (abilityResult.DamageMultiplier > 0 && abilityResult.LifeStealPercent == 0);
    }

    /// <summary>
    /// Process teammate action (AI-controlled)
    /// </summary>
    private async Task ProcessTeammateAction(Character teammate, Monster monster, CombatResult result)
    {
        if (!teammate.IsAlive || !monster.IsAlive) return;
        
        // Simple AI: attack if healthy, heal if low HP
        if (teammate.HP < teammate.MaxHP / 3 && teammate.HP < teammate.MaxHP)
        {
            // Heal
            long healAmount = Math.Min(15, teammate.MaxHP - teammate.HP);
            teammate.HP += healAmount;
            terminal.WriteLine($"{teammate.DisplayName} heals for {healAmount} HP.", "green");
            result.CombatLog.Add($"{teammate.DisplayName} heals for {healAmount} HP");
        }
        else
        {
            // Attack
            long attackPower = teammate.Strength + teammate.WeapPow + random.Next(1, 16);
            long defense = monster.GetDefensePower();
            long damage = Math.Max(1, attackPower - defense);
            
            monster.HP = Math.Max(0, monster.HP - damage);
            terminal.WriteLine($"{teammate.DisplayName} attacks {monster.Name} for {damage} damage!", "cyan");
            result.CombatLog.Add($"{teammate.DisplayName} attacks {monster.Name} for {damage} damage");
        }
        
        await Task.Delay(GetCombatDelay(1000));
    }
    
    /// <summary>
    /// Determine combat outcome and apply rewards/penalties
    /// </summary>
    private async Task DetermineCombatOutcome(CombatResult result)
    {
        if (globalEscape)
        {
            result.Outcome = CombatOutcome.PlayerEscaped;
            terminal.WriteLine("You have fled from combat.", "yellow");

            // Track flee telemetry
            TelemetrySystem.Instance.TrackCombat(
                "fled",
                result.Player.Level,
                result.Monster.Level,
                1,
                result.TotalDamageDealt,
                result.TotalDamageTaken,
                result.Monster.Name,
                result.Monster.IsBoss,
                0, // Round count not tracked in single combat
                result.Player.Class.ToString()
            );
        }
        else if (!result.Player.IsAlive)
        {
            // Faith Divine Favor: chance to survive lethal damage
            bool divineSaved = false;
            if (!result.Player.DivineFavorTriggeredThisCombat)
            {
                float divineFavorChance = FactionSystem.Instance?.GetDivineFavorChance() ?? 0f;
                if (divineFavorChance > 0 && random.NextDouble() < divineFavorChance)
                {
                    result.Player.DivineFavorTriggeredThisCombat = true;
                    result.Player.HP = Math.Max(1, result.Player.MaxHP / 10);
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine("");
                    terminal.WriteLine("  A golden light flares around you!");
                    terminal.WriteLine("  The gods arent done with you yet.");
                    terminal.SetColor("bright_green");
                    terminal.WriteLine($"  You are restored to {result.Player.HP} HP!");
                    terminal.WriteLine("");
                    await Task.Delay(GetCombatDelay(2000));
                    divineSaved = true;
                }
            }

            if (!divineSaved)
            {
                result.Outcome = CombatOutcome.PlayerDied;
                await HandlePlayerDeath(result);
            }
        }
        else if (!result.Monster.IsAlive)
        {
            result.Outcome = CombatOutcome.Victory;
            await HandleVictory(result);
        }

        await Task.Delay(GetCombatDelay(2000));
    }
    
    /// <summary>
    /// Handle player victory - Pascal rewards
    /// </summary>
    private async Task HandleVictory(CombatResult result)
    {
        // Check if this was a boss fight for dramatic art display
        // ONLY actual floor bosses count - not mini-bosses, champions, or high-level monsters
        bool isBoss = result.Monster.IsBoss;

        if (isBoss)
        {
            terminal.ClearScreen();
            await UsurperRemake.UI.ANSIArt.DisplayArtAnimated(terminal, UsurperRemake.UI.ANSIArt.BossVictory, 40);
            terminal.WriteLine("");
            await Task.Delay(GetCombatDelay(1000));
        }

        terminal.SetColor("bright_green");
        terminal.WriteLine($"You have slain the {result.Monster.Name}!");
        terminal.WriteLine("");

        // MUD mode: broadcast victory to room
        if (isBoss)
            UsurperRemake.Server.RoomRegistry.BroadcastAction($"{result.Player.DisplayName} has defeated the mighty {result.Monster.Name}!");
        else
            UsurperRemake.Server.RoomRegistry.BroadcastAction($"{result.Player.DisplayName} defeated {result.Monster.Name}.");

        QuestSystem.OnMonsterKilled(result.Player, result.Monster.Name, isBoss);

        // Calculate rewards (Pascal-compatible) with world event and difficulty modifiers
        long baseExpReward = result.Monster.GetExperienceReward();
        long baseGoldReward = result.Monster.GetGoldReward();

        // Apply world event modifiers
        long expReward = WorldEventSystem.Instance.GetAdjustedXP(baseExpReward);
        long goldReward = WorldEventSystem.Instance.GetAdjustedGold(baseGoldReward);

        // Apply difficulty modifiers
        float xpMult = DifficultySystem.GetExperienceMultiplier(DifficultySystem.CurrentDifficulty);
        float goldMult = DifficultySystem.GetGoldMultiplier(DifficultySystem.CurrentDifficulty);
        expReward = (long)(expReward * xpMult);
        goldReward = (long)(goldReward * goldMult);

        // Spouse XP bonus - 10% if married and spouse is alive
        long spouseBonus = 0;
        if (RomanceTracker.Instance.IsMarried && RomanceTracker.Instance.PrimarySpouse != null)
        {
            var spouseNpc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == RomanceTracker.Instance.PrimarySpouse.NPCId);
            if (spouseNpc != null && spouseNpc.IsAlive)
            {
                spouseBonus = expReward / 10; // 10% bonus
                expReward += spouseBonus;
            }
        }

        // Divine blessing XP bonus
        int divineXPBonus = DivineBlessingSystem.Instance.GetXPBonus(result.Player);
        long divineXPAmount = 0;
        if (divineXPBonus > 0)
        {
            divineXPAmount = (long)(expReward * divineXPBonus / 100f);
            expReward += divineXPAmount;
        }

        // Child XP bonus - having children motivates the parent to fight harder
        float childXPMult = FamilySystem.Instance?.GetChildXPMultiplier(result.Player) ?? 1.0f;
        if (childXPMult > 1.0f)
        {
            expReward = (long)(expReward * childXPMult);
        }

        // Team bonus - 15% extra XP and gold for having teammates
        long teamXPBonus = 0;
        long teamGoldBonus = 0;
        if (result.Teammates != null && result.Teammates.Count > 0)
        {
            teamXPBonus = (long)(expReward * 0.15);
            teamGoldBonus = (long)(goldReward * 0.15);
            expReward += teamXPBonus;
            goldReward += teamGoldBonus;
        }

        // Team balance XP penalty - reduced XP when carried by high-level teammates
        float teamXPMult = TeamBalanceSystem.Instance.CalculateXPMultiplier(result.Player, result.Teammates);
        long preTeamBalanceXP = expReward;
        if (teamXPMult < 1.0f)
        {
            expReward = (long)(expReward * teamXPMult);
        }

        result.Player.Experience += expReward;
        result.Player.Gold += goldReward;
        result.Player.MKills++;

        // Award experience to active companions (50% of player's XP)
        CompanionSystem.Instance?.AwardCompanionExperience(expReward, terminal);

        // Award experience to NPC teammates (spouses/lovers) - 50% of player's XP
        AwardTeammateExperience(result.Teammates, expReward, terminal);

        // Track statistics
        result.Player.Statistics.RecordMonsterKill(expReward, goldReward, isBoss, result.Monster.IsUnique);
        result.Player.Statistics.RecordGoldChange(result.Player.Gold);

        // Track telemetry for combat victory
        TelemetrySystem.Instance.TrackCombat(
            "victory",
            result.Player.Level,
            result.Monster?.Level ?? 0,
            1,
            result.TotalDamageDealt,
            result.TotalDamageTaken,
            result.Monster?.Name,
            isBoss,
            0, // Round count not tracked in single combat
            result.Player.Class.ToString()
        );

        // Track boss kill milestone
        if (isBoss)
        {
            TelemetrySystem.Instance.TrackMilestone(
                $"boss_defeated_{result.Monster.Name.Replace(" ", "_").ToLower()}",
                result.Player.Level,
                result.Player.Class.ToString()
            );
        }

        // Track archetype (Hero for combat, with bonus for bosses and rare monsters)
        ArchetypeTracker.Instance.RecordMonsterKill(result.Monster.Level, result.Monster.IsUnique);
        if (isBoss)
        {
            ArchetypeTracker.Instance.RecordBossDefeat(result.Monster.Name, result.Monster.Level);

            // Online news: boss kill
            if (OnlineStateManager.IsActive)
            {
                var bossKillerName = result.Player.Name2 ?? result.Player.Name1;
                _ = OnlineStateManager.Instance!.AddNews(
                    $"{bossKillerName} defeated the boss {result.Monster.Name}!", "combat");
            }
        }

        // Track gold collection for quests
        QuestSystem.OnGoldCollected(result.Player, goldReward);

        terminal.SetColor("green");
        terminal.WriteLine($"You gain {expReward} experience!");
        terminal.WriteLine($"You find {goldReward} gold!");

        // Show bonus from world events if any
        if (expReward > baseExpReward + spouseBonus || goldReward > baseGoldReward)
        {
            terminal.SetColor("bright_cyan");
            if (expReward > baseExpReward + spouseBonus)
                terminal.WriteLine($"  (World event bonus: +{expReward - baseExpReward - spouseBonus} XP)");
            if (goldReward > baseGoldReward)
                terminal.WriteLine($"  (World event bonus: +{goldReward - baseGoldReward} gold)");
        }

        // Show spouse bonus if applicable
        if (spouseBonus > 0)
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"  (Spouse love bonus: +{spouseBonus} XP) <3");
        }

        // Show divine blessing bonus if applicable
        if (divineXPAmount > 0)
        {
            var blessing = DivineBlessingSystem.Instance.GetBlessings(result.Player);
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"  ({blessing.GodName}'s favor: +{divineXPAmount} XP)");
        }

        // Show team balance XP penalty if applicable
        if (teamXPMult < 1.0f)
        {
            long xpLost = preTeamBalanceXP - expReward;
            terminal.SetColor("yellow");
            terminal.WriteLine($"  (High-level ally penalty: -{xpLost} XP, {(int)(teamXPMult * 100)}% rate)");
        }

        // Show team bonus if applicable
        if (teamXPBonus > 0 || teamGoldBonus > 0)
        {
            terminal.SetColor("cyan");
            terminal.WriteLine($"  (Team bonus: +{teamXPBonus} XP, +{teamGoldBonus} gold)");
        }

        // Offer weapon pickup
        if (result.Monster.GrabWeap && !string.IsNullOrEmpty(result.Monster.Weapon))
        {
            terminal.WriteLine($"Do you want to pick up the {result.Monster.Weapon}? (Y/N)", "yellow");
            var input = await terminal.GetInput("> ");
            if (input.Trim().ToUpper().StartsWith("Y"))
            {
                Item lootItem;
                var baseWeapon = ItemManager.GetClassicWeapon((int)result.Monster.WeapNr);
                if (baseWeapon != null)
                {
                    lootItem = new Item
                    {
                        Name = baseWeapon.Name,
                        Type = ObjType.Weapon,
                        Value = baseWeapon.Value,
                        Attack = (int)baseWeapon.Power
                    };
                }
                else
                {
                    lootItem = new Item
                    {
                        Name = result.Monster.Weapon,
                        Type = ObjType.Weapon,
                        Value = 0,
                        Attack = (int)result.Monster.WeapPow
                    };
                }

                result.Player.Inventory.Add(lootItem);
                terminal.WriteLine($"You picked up {lootItem.Name}.", "bright_green");
                result.ItemsFound.Add(lootItem.Name);
            }
        }

        // Offer armor pickup
        if (result.Monster.GrabArm && !string.IsNullOrEmpty(result.Monster.Armor))
        {
            terminal.WriteLine($"Do you want to take the {result.Monster.Armor}? (Y/N)", "yellow");
            var input = await terminal.GetInput("> ");
            if (input.Trim().ToUpper().StartsWith("Y"))
            {
                Item lootItem;
                var baseArmor = ItemManager.GetClassicArmor((int)result.Monster.ArmNr);
                if (baseArmor != null)
                {
                    lootItem = new Item
                    {
                        Name = baseArmor.Name,
                        Type = ObjType.Body,
                        Value = baseArmor.Value,
                        Armor = (int)baseArmor.Power
                    };
                }
                else
                {
                    lootItem = new Item
                    {
                        Name = result.Monster.Armor,
                        Type = ObjType.Body,
                        Value = 0,
                        Armor = (int)result.Monster.ArmPow
                    };
                }

                result.Player.Inventory.Add(lootItem);
                terminal.WriteLine($"You picked up {lootItem.Name}.", "bright_green");
                result.ItemsFound.Add(lootItem.Name);
            }
        }

        result.CombatLog.Add($"Victory! Gained {expReward} exp and {goldReward} gold");

        // Check and award achievements based on combat result
        // Use TotalDamageTaken from combat result to accurately track damage taken during THIS combat
        bool tookDamage = result.TotalDamageTaken > 0;
        double hpPercent = (double)result.Player.HP / result.Player.MaxHP;
        AchievementSystem.CheckCombatAchievements(result.Player, tookDamage, hpPercent);
        AchievementSystem.CheckAchievements(result.Player);
        await AchievementSystem.ShowPendingNotifications(terminal);

        // Check for dungeon loot drops using new LootGenerator system
        // Single-monster combat still needs equipment drops!
        if (result.DefeatedMonsters == null || result.DefeatedMonsters.Count == 0)
        {
            // Add the monster to DefeatedMonsters for loot check
            result.DefeatedMonsters = new List<Monster> { result.Monster };
        }
        await CheckForEquipmentDrop(result);

        // Soulweaver's Loom: heal 25% HP after each battle (50% during Manwe fight)
        ApplySoulweaverPostBattleHeal(result.Player);

        // Auto-heal with potions after combat, then offer to buy replacements
        AutoHealWithPotions(result.Player);
        AutoRestoreManaWithPotions(result.Player);

        // Monk potion purchase option - Pascal PLVSMON.PAS monk encounter
        await OfferMonkPotionPurchase(result.Player);

        // Auto-save after combat victory
        await SaveSystem.Instance.AutoSave(result.Player);
    }

    /// <summary>
    /// Offer potion purchase from monk - Pascal PLVSMON.PAS monk system
    /// </summary>
    public async Task OfferMonkPotionPurchase(Character player)
    {
        bool canBuyHealing = player.Healing < player.MaxPotions;
        bool canBuyMana = SpellSystem.HasSpells(player) && player.ManaPotions < player.MaxManaPotions;

        // Don't bother the player if they're already at max for everything
        if (!canBuyHealing && !canBuyMana)
        {
            return;
        }

        // Monk only appears ~25% of the time (like original Usurper PLVSMON.PAS)
        if (random.Next(100) >= 25)
        {
            return;
        }

        terminal.WriteLine("");
        terminal.WriteLine("A wandering monk approaches you...", "cyan");

        // Calculate costs (scales with level)
        int healCostPerPotion = 50 + (player.Level * 10);
        int manaCostPerPotion = Math.Max(75, player.Level * 3);

        // Show what's available
        if (canBuyHealing && canBuyMana)
            terminal.WriteLine($"\"I have potions for body and mind, traveler.\"", "white");
        else if (canBuyHealing)
            terminal.WriteLine($"\"Would you like to buy healing potions?\"", "white");
        else
            terminal.WriteLine($"\"I sense your arcane reserves are low. Need mana potions?\"", "white");
        terminal.WriteLine("");

        // --- Healing potions ---
        if (canBuyHealing)
        {
            terminal.WriteLine($"[H]ealing Potions - {healCostPerPotion}g each ({player.Healing}/{player.MaxPotions})", "green");
        }

        // --- Mana potions ---
        if (canBuyMana)
        {
            terminal.WriteLine($"[M]ana Potions - {manaCostPerPotion}g each ({player.ManaPotions}/{player.MaxManaPotions})", "blue");
        }

        terminal.WriteLine($"[N]o thanks", "gray");
        terminal.WriteLine($"Your gold: {player.Gold:N0}", "yellow");
        terminal.WriteLine("");

        string choice;
        while (true)
        {
            terminal.Write("Choice: ");
            var response = await terminal.GetInput("");
            choice = response.Trim().ToUpper();

            if (choice == "N" || (choice == "H" && canBuyHealing) || (choice == "M" && canBuyMana))
                break;

            terminal.WriteLine("Please choose a valid option.", "gray");
        }

        if (choice == "H")
        {
            await MonkBuyPotionType(player, "healing", healCostPerPotion,
                (int)player.Healing, player.MaxPotions,
                bought => { player.Healing += bought; },
                cost => { player.Statistics?.RecordPurchase(cost); player.Statistics?.RecordGoldSpent(cost); });
        }
        else if (choice == "M")
        {
            await MonkBuyPotionType(player, "mana", manaCostPerPotion,
                (int)player.ManaPotions, player.MaxManaPotions,
                bought => { player.ManaPotions += bought; },
                cost => { player.Statistics?.RecordPurchase(cost); player.Statistics?.RecordGoldSpent(cost); });
        }
        else
        {
            terminal.WriteLine("The monk nods and continues on his way.", "gray");
            await Task.Delay(GetCombatDelay(1000));
            return;
        }

        terminal.WriteLine("");
        terminal.WriteLine("The monk bows and departs.", "gray");
        await Task.Delay(GetCombatDelay(2000));
    }

    private async Task MonkBuyPotionType(Character player, string potionType, int costPerPotion,
        int currentCount, int maxCount, Action<int> applyPurchase, Action<long> trackStats)
    {
        int roomForPotions = maxCount - currentCount;
        int maxAffordable = (int)(player.Gold / costPerPotion);
        int maxCanBuy = Math.Min(roomForPotions, maxAffordable);

        if (maxCanBuy <= 0)
        {
            if (roomForPotions <= 0)
                terminal.WriteLine($"You already have the maximum number of {potionType} potions!", "yellow");
            else
                terminal.WriteLine("You don't have enough gold!", "red");
            await Task.Delay(GetCombatDelay(1500));
            return;
        }

        terminal.WriteLine($"How many {potionType} potions? (Max: {maxCanBuy})", "cyan");
        var amountInput = await terminal.GetInput("> ");

        if (!int.TryParse(amountInput.Trim(), out int amount) || amount < 1)
        {
            terminal.WriteLine("Cancelled.", "gray");
            await Task.Delay(GetCombatDelay(1000));
            return;
        }

        if (amount > maxCanBuy)
        {
            terminal.WriteLine($"You can only buy {maxCanBuy}!", "yellow");
            amount = maxCanBuy;
        }

        long totalCost = amount * costPerPotion;
        player.Gold -= totalCost;
        applyPurchase(amount);
        trackStats(totalCost);

        string color = potionType == "mana" ? "blue" : "green";
        terminal.WriteLine("");
        terminal.WriteLine($"You purchase {amount} {potionType} potion{(amount > 1 ? "s" : "")} for {totalCost:N0} gold.", color);
        terminal.WriteLine($"Gold remaining: {player.Gold:N0}", "yellow");
    }

    /// <summary>
    /// Check for elemental enchant procs on player's equipped weapon (single-monster combat).
    /// </summary>
    private void CheckElementalEnchantProcs(Character attacker, Monster target, long damage, CombatResult result)
    {
        var weapon = attacker.GetEquipment(EquipmentSlot.MainHand);
        if (weapon == null) return;

        if (weapon.HasFireEnchant && random.NextDouble() < GameConfig.FireEnchantProcChance)
        {
            long fireDamage = Math.Max(1, (long)(damage * GameConfig.FireEnchantDamageMultiplier));
            target.HP = Math.Max(0, target.HP - fireDamage);
            terminal.SetColor("bright_red");
            terminal.WriteLine($"Flames erupt from your weapon! {fireDamage} fire damage! (Phoenix Fire)");
            result.TotalDamageDealt += fireDamage;
            attacker.Statistics?.RecordDamageDealt(fireDamage, false);
        }

        if (weapon.HasFrostEnchant && random.NextDouble() < GameConfig.FrostEnchantProcChance)
        {
            target.Defence = Math.Max(0, target.Defence - GameConfig.FrostEnchantAgiReduction);
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"Frost spreads from the impact! {target.Name}'s defence reduced! (Frostbite)");
        }

        if (weapon.HasLightningEnchant && random.NextDouble() < GameConfig.LightningEnchantProcChance)
        {
            long lightningDamage = Math.Max(1, (long)(damage * GameConfig.LightningEnchantDamageMultiplier));
            target.HP = Math.Max(0, target.HP - lightningDamage);
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"Lightning arcs from your strike! {lightningDamage} shock damage! {target.Name} is stunned! (Thunderstrike)");
            result.TotalDamageDealt += lightningDamage;
            attacker.Statistics?.RecordDamageDealt(lightningDamage, false);
        }

        if (weapon.HasPoisonEnchant && random.NextDouble() < GameConfig.PoisonEnchantProcChance)
        {
            int poisonValue = weapon.PoisonDamage > 0 ? weapon.PoisonDamage : (int)(damage * 0.10);
            long poisonDamage = Math.Max(1, poisonValue);
            target.HP = Math.Max(0, target.HP - poisonDamage);
            terminal.SetColor("green");
            terminal.WriteLine($"Venom seeps into the wound! {poisonDamage} poison damage! (Venomstrike)");
            result.TotalDamageDealt += poisonDamage;
            attacker.Statistics?.RecordDamageDealt(poisonDamage, false);
        }

        if (weapon.HasHolyEnchant && random.NextDouble() < GameConfig.HolyEnchantProcChance)
        {
            bool isUndead = target.Name.Contains("Skeleton") || target.Name.Contains("Zombie") ||
                            target.Name.Contains("Ghost") || target.Name.Contains("Lich") ||
                            target.Name.Contains("Wraith") || target.Name.Contains("Vampire") ||
                            target.Name.Contains("Undead") || target.Name.Contains("Revenant");
            float holyMult = isUndead ? GameConfig.HolyEnchantDamageMultiplier * 2 : GameConfig.HolyEnchantDamageMultiplier;
            long holyDamage = Math.Max(1, (long)(damage * holyMult));
            target.HP = Math.Max(0, target.HP - holyDamage);
            terminal.SetColor("bright_white");
            terminal.WriteLine(isUndead
                ? $"Holy light sears the undead! {holyDamage} radiant damage! (Smite)"
                : $"Divine light strikes true! {holyDamage} holy damage! (Radiance)");
            result.TotalDamageDealt += holyDamage;
            attacker.Statistics?.RecordDamageDealt(holyDamage, false);
        }

        if (weapon.HasShadowEnchant && random.NextDouble() < GameConfig.ShadowEnchantProcChance)
        {
            long shadowDamage = Math.Max(1, (long)(damage * GameConfig.ShadowEnchantDamageMultiplier));
            target.HP = Math.Max(0, target.HP - shadowDamage);
            terminal.SetColor("dark_magenta");
            terminal.WriteLine($"Shadows coil from the blade! {shadowDamage} shadow damage! (Darkstrike)");
            result.TotalDamageDealt += shadowDamage;
            attacker.Statistics?.RecordDamageDealt(shadowDamage, false);
        }

        // Mana steal proc
        int manaStealPct = attacker.GetEquipmentManaSteal();
        if (manaStealPct > 0 && damage > 0)
        {
            long manaRestored = Math.Max(1, damage * manaStealPct / 100);
            attacker.Mana = Math.Min(attacker.MaxMana, attacker.Mana + (int)manaRestored);
            terminal.SetColor("blue");
            terminal.WriteLine($"Your weapon siphons {manaRestored} mana! (Siphon)");
        }
    }

    /// <summary>
    /// Check for elemental enchant procs in multi-monster combat.
    /// </summary>
    private void CheckElementalEnchantProcsMonster(Character attacker, Monster target, long damage)
    {
        var weapon = attacker.GetEquipment(EquipmentSlot.MainHand);
        if (weapon == null) return;

        if (weapon.HasFireEnchant && random.NextDouble() < GameConfig.FireEnchantProcChance)
        {
            long fireDamage = Math.Max(1, (long)(damage * GameConfig.FireEnchantDamageMultiplier));
            target.HP -= fireDamage;
            terminal.SetColor("bright_red");
            terminal.WriteLine($"  Flames erupt! {fireDamage} fire damage to {target.Name}! (Phoenix Fire)");
            attacker.Statistics?.RecordDamageDealt(fireDamage, false);
        }

        if (weapon.HasFrostEnchant && random.NextDouble() < GameConfig.FrostEnchantProcChance)
        {
            target.Defence = Math.Max(0, target.Defence - GameConfig.FrostEnchantAgiReduction);
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"  Frost spreads! {target.Name}'s defence reduced! (Frostbite)");
        }

        if (weapon.HasLightningEnchant && random.NextDouble() < GameConfig.LightningEnchantProcChance)
        {
            long lightningDamage = Math.Max(1, (long)(damage * GameConfig.LightningEnchantDamageMultiplier));
            target.HP -= lightningDamage;
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"  Lightning arcs! {lightningDamage} shock damage to {target.Name}! Stunned! (Thunderstrike)");
            attacker.Statistics?.RecordDamageDealt(lightningDamage, false);
        }

        if (weapon.HasPoisonEnchant && random.NextDouble() < GameConfig.PoisonEnchantProcChance)
        {
            int poisonValue = weapon.PoisonDamage > 0 ? weapon.PoisonDamage : (int)(damage * 0.10);
            long poisonDamage = Math.Max(1, poisonValue);
            target.HP -= poisonDamage;
            terminal.SetColor("green");
            terminal.WriteLine($"  Venom seeps in! {poisonDamage} poison damage to {target.Name}! (Venomstrike)");
            attacker.Statistics?.RecordDamageDealt(poisonDamage, false);
        }

        if (weapon.HasHolyEnchant && random.NextDouble() < GameConfig.HolyEnchantProcChance)
        {
            bool isUndead = target.Name.Contains("Skeleton") || target.Name.Contains("Zombie") ||
                            target.Name.Contains("Ghost") || target.Name.Contains("Lich") ||
                            target.Name.Contains("Wraith") || target.Name.Contains("Vampire") ||
                            target.Name.Contains("Undead") || target.Name.Contains("Revenant");
            float holyMult = isUndead ? GameConfig.HolyEnchantDamageMultiplier * 2 : GameConfig.HolyEnchantDamageMultiplier;
            long holyDamage = Math.Max(1, (long)(damage * holyMult));
            target.HP -= holyDamage;
            terminal.SetColor("bright_white");
            terminal.WriteLine(isUndead
                ? $"  Holy light sears! {holyDamage} radiant damage to {target.Name}! (Smite)"
                : $"  Divine strike! {holyDamage} holy damage to {target.Name}! (Radiance)");
            attacker.Statistics?.RecordDamageDealt(holyDamage, false);
        }

        if (weapon.HasShadowEnchant && random.NextDouble() < GameConfig.ShadowEnchantProcChance)
        {
            long shadowDamage = Math.Max(1, (long)(damage * GameConfig.ShadowEnchantDamageMultiplier));
            target.HP -= shadowDamage;
            terminal.SetColor("dark_magenta");
            terminal.WriteLine($"  Shadows coil! {shadowDamage} shadow damage to {target.Name}! (Darkstrike)");
            attacker.Statistics?.RecordDamageDealt(shadowDamage, false);
        }

        // Mana steal proc
        int manaStealPct = attacker.GetEquipmentManaSteal();
        if (manaStealPct > 0 && damage > 0)
        {
            long manaRestored = Math.Max(1, damage * manaStealPct / 100);
            attacker.Mana = Math.Min(attacker.MaxMana, attacker.Mana + (int)manaRestored);
            terminal.SetColor("blue");
            terminal.WriteLine($"  Weapon siphons {manaRestored} mana! (Siphon)");
        }
    }

    /// <summary>
    /// Automatically use healing potions to restore HP after combat.
    /// Fires before the monk purchase so players heal first, then buy replacements.
    /// </summary>
    /// <summary>
    /// Soulweaver's Loom artifact: heal 25% HP after each battle (50% during Manwe fight)
    /// </summary>
    private void ApplySoulweaverPostBattleHeal(Character player)
    {
        if (!ArtifactSystem.Instance.HasSoulweaversLoom()) return;
        if (player.HP >= player.MaxHP) return;

        float healPct = IsManweBattle && ArtifactSystem.Instance.HasVoidKey() ? 0.50f : 0.25f;
        long healAmount = (long)(player.MaxHP * healPct);
        long oldHP = player.HP;
        player.HP = Math.Min(player.MaxHP, player.HP + healAmount);
        long actualHeal = player.HP - oldHP;
        if (actualHeal > 0)
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"The Soulweaver's Loom mends your wounds... (+{actualHeal} HP)");
        }
    }

    private void AutoHealWithPotions(Character player)
    {
        if (player.Healing <= 0 || player.HP >= player.MaxHP)
            return;

        long totalHealed = 0;
        int potionsUsed = 0;
        while (player.Healing > 0 && player.HP < player.MaxHP)
        {
            long healAmount = 30 + player.Level * 5 + random.Next(10, 30);
            healAmount = Math.Min(healAmount, player.MaxHP - player.HP);
            player.HP += healAmount;
            totalHealed += healAmount;
            player.Healing--;
            potionsUsed++;
            player.Statistics?.RecordPotionUsed((int)healAmount);
        }

        if (potionsUsed > 0)
        {
            terminal.WriteLine("");
            terminal.WriteLine($"You drink {potionsUsed} potion{(potionsUsed > 1 ? "s" : "")} and recover {totalHealed:N0} HP. ({player.HP}/{player.MaxHP} HP, {player.Healing} potions left)", "green");
        }
    }

    /// <summary>
    /// Automatically use mana potions to restore mana after combat.
    /// Only triggers for spellcasters who have mana potions and are below full mana.
    /// </summary>
    private void AutoRestoreManaWithPotions(Character player)
    {
        if (player.ManaPotions <= 0 || player.Mana >= player.MaxMana)
            return;

        // Only auto-use for characters who have spells
        if (!SpellSystem.HasSpells(player))
            return;

        long manaPerPotion = 30 + player.Level * 5;
        long manaNeeded = player.MaxMana - player.Mana;
        int potionsNeeded = (int)Math.Ceiling((double)manaNeeded / manaPerPotion);
        int potionsUsed = (int)Math.Min(potionsNeeded, player.ManaPotions);

        long totalRestored = 0;
        for (int i = 0; i < potionsUsed; i++)
        {
            long restoreAmount = Math.Min(manaPerPotion, player.MaxMana - player.Mana);
            player.Mana += restoreAmount;
            totalRestored += restoreAmount;
            player.ManaPotions--;
            player.Statistics?.RecordManaPotionUsed(restoreAmount);
        }

        if (potionsUsed > 0)
        {
            terminal.WriteLine($"You drink {potionsUsed} mana potion{(potionsUsed > 1 ? "s" : "")} and recover {totalRestored:N0} MP. ({player.Mana}/{player.MaxMana} MP, {player.ManaPotions} mana potions left)", "blue");
        }
    }

    /// <summary>
    /// Check for equipment drops after combat victory
    /// Uses the new LootGenerator for exciting, level-scaled drops
    /// </summary>
    private async Task CheckForEquipmentDrop(CombatResult result)
    {
        if (result.DefeatedMonsters == null || result.DefeatedMonsters.Count == 0)
            return;

        // Calculate drop chance based on number and level of defeated monsters
        // Base 15% per monster, higher level monsters have better drop rates
        foreach (var monster in result.DefeatedMonsters)
        {
            Item? loot = null;

            // Mini-bosses (Champions) ALWAYS drop equipment
            if (monster.IsMiniBoss)
            {
                loot = LootGenerator.GenerateMiniBossLoot(monster.Level, result.Player.Class);
                if (loot != null)
                {
                    // Cap MinLevel to player's level — if you killed it, you earned it
                    if (loot.MinLevel > result.Player.Level)
                        loot.MinLevel = result.Player.Level;
                    terminal.WriteLine("");
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine("The Champion drops valuable equipment!");
                    await DisplayEquipmentDrop(loot, monster, result.Player);
                }
                continue; // Skip normal drop logic for mini-bosses
            }

            // Bosses ALWAYS drop high-quality equipment
            if (monster.IsBoss)
            {
                loot = LootGenerator.GenerateBossLoot(monster.Level, result.Player.Class);
                if (loot != null)
                {
                    // Cap MinLevel to player's level — if you killed it, you earned it
                    if (loot.MinLevel > result.Player.Level)
                        loot.MinLevel = result.Player.Level;
                    terminal.WriteLine("");
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine("The Boss drops powerful equipment!");
                    await DisplayEquipmentDrop(loot, monster, result.Player);
                }
                continue; // Skip normal drop logic for bosses
            }

            // Regular monsters - normal drop chance
            // Drop chance: 15% base + 0.5% per monster level, capped at 40%
            double dropChance = 0.15 + (monster.Level * 0.005);
            dropChance = Math.Min(0.40, dropChance);

            // Named monsters (Lords, Chiefs, Kings) have better drop chance
            if (monster.Name.Contains("Boss") || monster.Name.Contains("Chief") ||
                monster.Name.Contains("Lord") || monster.Name.Contains("King"))
            {
                dropChance = 0.60;
            }

            if (random.NextDouble() < dropChance)
            {
                // Generate loot using the new LootGenerator system
                loot = LootGenerator.GenerateDungeonLoot(monster.Level, result.Player.Class);

                if (loot != null)
                {
                    // Cap MinLevel to player's level — if you killed it, you earned it
                    if (loot.MinLevel > result.Player.Level)
                        loot.MinLevel = result.Player.Level;
                    // Display the drop with excitement!
                    await DisplayEquipmentDrop(loot, monster, result.Player);
                }
            }
        }
    }

    /// <summary>
    /// Display equipment drop with appropriate fanfare
    /// </summary>
    private async Task DisplayEquipmentDrop(Item lootItem, Monster monster, Character player)
    {
        var rarity = LootGenerator.GetItemRarity(lootItem);
        string rarityColor = LootGenerator.GetRarityColor(rarity);

        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");

        // More dramatic display for higher rarity
        switch (rarity)
        {
            case LootGenerator.ItemRarity.Legendary:
            case LootGenerator.ItemRarity.Artifact:
                terminal.WriteLine("╔════════════════════════════════════════════════════════╗");
                terminal.WriteLine("║           *** LEGENDARY DROP! ***                ║");
                terminal.WriteLine("╚════════════════════════════════════════════════════════╝");
                break;
            case LootGenerator.ItemRarity.Epic:
                terminal.WriteLine("═══════════════════════════════════════");
                terminal.WriteLine("      ** EPIC DROP! **");
                terminal.WriteLine("═══════════════════════════════════════");
                break;
            case LootGenerator.ItemRarity.Rare:
                terminal.WriteLine("═════════════════════════════");
                terminal.WriteLine("     * RARE DROP! *");
                terminal.WriteLine("═════════════════════════════");
                break;
            default:
                terminal.WriteLine("─────────────────────────────");
                terminal.WriteLine("       ITEM FOUND!");
                terminal.WriteLine("─────────────────────────────");
                break;
        }

        terminal.WriteLine("");

        if (lootItem.IsIdentified)
        {
            // Identified - show full details
            terminal.SetColor(rarityColor);
            terminal.WriteLine($"  {lootItem.Name}");
            terminal.SetColor("white");

            if (lootItem.Type == global::ObjType.Weapon)
                terminal.WriteLine($"  Attack Power: +{lootItem.Attack}");
            else if (lootItem.Type == global::ObjType.Fingers || lootItem.Type == global::ObjType.Neck)
            {
                // Accessories don't have armor — show item type instead
                string itemTypeName = lootItem.Type == global::ObjType.Fingers ? "Ring" : "Necklace";
                terminal.WriteLine($"  Type: {itemTypeName}");
            }
            else
                terminal.WriteLine($"  Armor Power: +{lootItem.Armor}");

            var bonuses = new List<string>();
            if (lootItem.Strength != 0) bonuses.Add($"Str {lootItem.Strength:+#;-#;0}");
            if (lootItem.Dexterity != 0) bonuses.Add($"Dex {lootItem.Dexterity:+#;-#;0}");
            if (lootItem.Wisdom != 0) bonuses.Add($"Wis {lootItem.Wisdom:+#;-#;0}");
            if (lootItem.HP != 0) bonuses.Add($"HP {lootItem.HP:+#;-#;0}");
            if (lootItem.Mana != 0) bonuses.Add($"Mana {lootItem.Mana:+#;-#;0}");
            if (lootItem.Defence != 0) bonuses.Add($"Def {lootItem.Defence:+#;-#;0}");

            if (bonuses.Count > 0)
            {
                terminal.SetColor("cyan");
                terminal.WriteLine($"  Bonuses: {string.Join(", ", bonuses)}");
            }

            terminal.SetColor("yellow");
            terminal.WriteLine($"  Value: {lootItem.Value:N0} gold");

            if (lootItem.IsCursed)
            {
                terminal.SetColor("red");
                terminal.WriteLine("");
                terminal.WriteLine("  WARNING: This item is CURSED!");
                terminal.WriteLine("  Visit the Magic Shop to remove the curse.");
            }

            await ShowEquipmentComparison(lootItem, player);
        }
        else
        {
            // Unidentified - show only type hint, no stats
            string unidName = LootGenerator.GetUnidentifiedName(lootItem);
            terminal.SetColor("magenta");
            terminal.WriteLine($"  {unidName}");
            terminal.SetColor("gray");
            terminal.WriteLine("  The item's properties are unknown.");
            terminal.WriteLine("  Take it to the Magic Shop to have it identified.");
        }

        terminal.WriteLine("");

        // Ask player what to do
        terminal.SetColor("white");
        terminal.WriteLine($"You found this on the {monster.Name}'s corpse.");
        terminal.WriteLine("");
        if (lootItem.IsIdentified)
        {
            terminal.WriteLine("(E)quip immediately");
        }
        terminal.WriteLine("(T)ake to inventory");
        terminal.WriteLine("(L)eave it");
        terminal.WriteLine("");

        terminal.Write("Your choice: ");
        var choice = await terminal.GetKeyInput();

        switch (choice.ToUpper())
        {
            case "E":
                // Block equipping unidentified items
                if (!lootItem.IsIdentified)
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine("You can't equip an unidentified item. Taking it to your inventory instead.");
                    player.Inventory.Add(lootItem);
                    string unidDisplayName = LootGenerator.GetUnidentifiedName(lootItem);
                    terminal.SetColor("cyan");
                    terminal.WriteLine($"Added {unidDisplayName} to your inventory.");
                    break;
                }
                // Convert Item to Equipment and equip properly
                Equipment equipment;
                if (lootItem.Type == global::ObjType.Weapon)
                {
                    // Look up in equipment database to get correct handedness and weapon type
                    var knownEquip = EquipmentDatabase.GetByName(lootItem.Name);
                    var lootHandedness = knownEquip?.Handedness ?? WeaponHandedness.OneHanded;
                    var lootWeaponType = knownEquip?.WeaponType ?? WeaponType.Sword;

                    equipment = Equipment.CreateWeapon(
                        id: 10000 + random.Next(10000),
                        name: lootItem.Name,
                        handedness: lootHandedness,
                        weaponType: lootWeaponType,
                        power: lootItem.Attack,
                        value: lootItem.Value,
                        rarity: ConvertRarityToEquipmentRarity(LootGenerator.GetItemRarity(lootItem))
                    );
                }
                else
                {
                    // Determine the correct slot based on item type
                    EquipmentSlot itemSlot = lootItem.Type switch
                    {
                        global::ObjType.Shield => EquipmentSlot.OffHand,
                        global::ObjType.Body => EquipmentSlot.Body,
                        global::ObjType.Head => EquipmentSlot.Head,
                        global::ObjType.Arms => EquipmentSlot.Arms,
                        global::ObjType.Hands => EquipmentSlot.Hands,
                        global::ObjType.Legs => EquipmentSlot.Legs,
                        global::ObjType.Feet => EquipmentSlot.Feet,
                        global::ObjType.Waist => EquipmentSlot.Waist,
                        global::ObjType.Neck => EquipmentSlot.Neck,
                        global::ObjType.Face => EquipmentSlot.Face,
                        global::ObjType.Fingers => EquipmentSlot.LFinger,
                        global::ObjType.Abody => EquipmentSlot.Cloak,
                        _ => EquipmentSlot.Body
                    };

                    // Use CreateAccessory for rings and necklaces, CreateArmor for everything else
                    if (lootItem.Type == global::ObjType.Fingers || lootItem.Type == global::ObjType.Neck)
                    {
                        equipment = Equipment.CreateAccessory(
                            id: 10000 + random.Next(10000),
                            name: lootItem.Name,
                            slot: itemSlot,
                            value: lootItem.Value,
                            rarity: ConvertRarityToEquipmentRarity(LootGenerator.GetItemRarity(lootItem))
                        );
                    }
                    else
                    {
                        equipment = Equipment.CreateArmor(
                            id: 10000 + random.Next(10000),
                            name: lootItem.Name,
                            slot: itemSlot,
                            armorType: ArmorType.Chain,
                            ac: lootItem.Armor,
                            value: lootItem.Value,
                            rarity: ConvertRarityToEquipmentRarity(LootGenerator.GetItemRarity(lootItem))
                        );
                    }
                }

                // Transfer the already-capped MinLevel from the loot Item
                // (Equipment.Create* methods hardcode MinLevel by rarity, but LootGenerator
                // already capped it to the player's level — honor that cap)
                equipment.MinLevel = lootItem.MinLevel;

                // Apply bonus stats to equipment
                if (lootItem.Strength != 0) equipment = equipment.WithStrength(lootItem.Strength);
                if (lootItem.Dexterity != 0) equipment = equipment.WithDexterity(lootItem.Dexterity);
                if (lootItem.Wisdom != 0) equipment = equipment.WithWisdom(lootItem.Wisdom);
                if (lootItem.HP != 0) equipment = equipment.WithMaxHP(lootItem.HP);
                if (lootItem.Mana != 0) equipment = equipment.WithMaxMana(lootItem.Mana);
                if (lootItem.Defence != 0) equipment = equipment.WithDefence(lootItem.Defence);
                if (lootItem.IsCursed) equipment.IsCursed = true;

                // Transfer loot enchantment effects to Equipment properties (v0.40.5)
                if (lootItem.LootEffects != null && lootItem.LootEffects.Count > 0)
                {
                    foreach (var (effectType, value) in lootItem.LootEffects)
                    {
                        var effect = (LootGenerator.SpecialEffect)effectType;
                        switch (effect)
                        {
                            case LootGenerator.SpecialEffect.FireDamage:
                                equipment.HasFireEnchant = true;
                                break;
                            case LootGenerator.SpecialEffect.IceDamage:
                                equipment.HasFrostEnchant = true;
                                break;
                            case LootGenerator.SpecialEffect.LightningDamage:
                                equipment.HasLightningEnchant = true;
                                break;
                            case LootGenerator.SpecialEffect.PoisonDamage:
                                equipment.HasPoisonEnchant = true;
                                equipment.PoisonDamage = Math.Max(equipment.PoisonDamage, value);
                                break;
                            case LootGenerator.SpecialEffect.HolyDamage:
                                equipment.HasHolyEnchant = true;
                                break;
                            case LootGenerator.SpecialEffect.ShadowDamage:
                                equipment.HasShadowEnchant = true;
                                break;
                            case LootGenerator.SpecialEffect.LifeSteal:
                                equipment.LifeSteal = Math.Max(equipment.LifeSteal, Math.Max(5, value / 2));
                                break;
                            case LootGenerator.SpecialEffect.ManaSteal:
                                equipment.ManaSteal = Math.Max(equipment.ManaSteal, Math.Max(5, value / 2));
                                break;
                            case LootGenerator.SpecialEffect.CriticalStrike:
                                equipment.CriticalChanceBonus = Math.Max(equipment.CriticalChanceBonus, value);
                                break;
                            case LootGenerator.SpecialEffect.CriticalDamage:
                                equipment.CriticalDamageBonus = Math.Max(equipment.CriticalDamageBonus, value);
                                break;
                            case LootGenerator.SpecialEffect.ArmorPiercing:
                                equipment.ArmorPiercing = Math.Max(equipment.ArmorPiercing, value);
                                break;
                            case LootGenerator.SpecialEffect.Thorns:
                                equipment.Thorns = Math.Max(equipment.Thorns, value);
                                break;
                            case LootGenerator.SpecialEffect.Regeneration:
                                equipment.HPRegen = Math.Max(equipment.HPRegen, value);
                                break;
                            case LootGenerator.SpecialEffect.ManaRegen:
                                equipment.ManaRegen = Math.Max(equipment.ManaRegen, value);
                                break;
                            case LootGenerator.SpecialEffect.MagicResist:
                                equipment.MagicResistance = Math.Max(equipment.MagicResistance, value);
                                break;
                        }
                    }
                }

                // Enforce power-based MinLevel floor (prevents low-level players
                // from equipping absurdly powerful gear regardless of source)
                equipment.EnforceMinLevelFromPower();

                // Register the equipment in the database so it can be looked up later
                EquipmentDatabase.RegisterDynamic(equipment);

                // For one-handed weapons, ask which slot to use
                EquipmentSlot? targetSlot = null;
                if (Character.RequiresSlotSelection(equipment))
                {
                    targetSlot = await PromptForWeaponSlot(player);
                    if (targetSlot == null)
                    {
                        // Player cancelled - add to inventory instead
                        player.Inventory.Add(lootItem);
                        terminal.SetColor("cyan");
                        terminal.WriteLine($"Added {lootItem.Name} to your inventory.");
                        break;
                    }
                }

                // For rings, prompt if both finger slots are full
                if (equipment.Slot == EquipmentSlot.LFinger || equipment.Slot == EquipmentSlot.RFinger)
                {
                    var leftRing = player.GetEquipment(EquipmentSlot.LFinger);
                    var rightRing = player.GetEquipment(EquipmentSlot.RFinger);

                    if (leftRing != null && rightRing != null)
                    {
                        targetSlot = await PromptForRingSlot(player);
                        if (targetSlot == null)
                        {
                            // Player chose inventory
                            player.Inventory.Add(lootItem);
                            terminal.SetColor("cyan");
                            terminal.WriteLine($"Added {lootItem.Name} to your inventory.");
                            break;
                        }
                    }
                }

                // Try to equip the item
                if (player.EquipItem(equipment, targetSlot, out string equipMsg))
                {
                    if (lootItem.IsCursed)
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine("");
                        terminal.WriteLine("You equip the item... and feel a dark presence bind to you!");
                        terminal.WriteLine("The curse takes hold! You cannot remove this item normally.");
                    }
                    else
                    {
                        terminal.SetColor("green");
                        terminal.WriteLine(equipMsg);
                    }
                }
                else
                {
                    // Equip failed - add to inventory instead
                    player.Inventory.Add(lootItem);
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"Could not equip: {equipMsg}");
                    terminal.WriteLine($"Added {lootItem.Name} to your inventory.");
                }
                break;

            case "T":
                // Add to inventory
                player.Inventory.Add(lootItem);
                terminal.SetColor("cyan");
                string invName = lootItem.IsIdentified ? lootItem.Name : LootGenerator.GetUnidentifiedName(lootItem);
                terminal.WriteLine($"Added {invName} to your inventory.");
                break;

            default:
                terminal.SetColor("gray");
                terminal.WriteLine("You leave the item behind.");
                break;
        }

        await Task.Delay(GetCombatDelay(1500));
    }

    /// <summary>
    /// Convert LootGenerator rarity to Equipment rarity
    /// </summary>
    private EquipmentRarity ConvertRarityToEquipmentRarity(LootGenerator.ItemRarity rarity)
    {
        return rarity switch
        {
            LootGenerator.ItemRarity.Common => EquipmentRarity.Common,
            LootGenerator.ItemRarity.Uncommon => EquipmentRarity.Uncommon,
            LootGenerator.ItemRarity.Rare => EquipmentRarity.Rare,
            LootGenerator.ItemRarity.Epic => EquipmentRarity.Epic,
            LootGenerator.ItemRarity.Legendary => EquipmentRarity.Legendary,
            LootGenerator.ItemRarity.Artifact => EquipmentRarity.Artifact,
            _ => EquipmentRarity.Common
        };
    }

    /// <summary>
    /// Show comparison between dropped item and currently equipped item
    /// </summary>
    private async Task ShowEquipmentComparison(Item lootItem, Character player)
    {
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine("  ─────────────────────────────────────");
        // Determine which slot this item would go in
        EquipmentSlot targetSlot = lootItem.Type switch
        {
            global::ObjType.Weapon => EquipmentSlot.MainHand,
            global::ObjType.Shield => EquipmentSlot.OffHand,
            global::ObjType.Body => EquipmentSlot.Body,
            global::ObjType.Head => EquipmentSlot.Head,
            global::ObjType.Arms => EquipmentSlot.Arms,
            global::ObjType.Hands => EquipmentSlot.Hands,
            global::ObjType.Legs => EquipmentSlot.Legs,
            global::ObjType.Feet => EquipmentSlot.Feet,
            global::ObjType.Waist => EquipmentSlot.Waist,
            global::ObjType.Neck => EquipmentSlot.Neck,
            global::ObjType.Face => EquipmentSlot.Face,
            global::ObjType.Fingers => EquipmentSlot.LFinger,
            global::ObjType.Abody => EquipmentSlot.Cloak,
            _ => EquipmentSlot.Body
        };

        // Show slot name in comparison header
        string slotDisplayName = targetSlot switch
        {
            EquipmentSlot.MainHand => "Main Hand",
            EquipmentSlot.OffHand => "Off Hand",
            EquipmentSlot.LFinger => "Ring",
            EquipmentSlot.RFinger => "Ring",
            EquipmentSlot.Neck => "Necklace",
            _ => targetSlot.ToString()
        };
        terminal.SetColor("white");
        terminal.WriteLine($"  COMPARISON ({slotDisplayName} slot):");

        // Get currently equipped item
        var currentEquip = player.GetEquipment(targetSlot);

        if (currentEquip == null)
        {
            terminal.SetColor("green");
            terminal.WriteLine($"  Slot is empty - this would be an upgrade!");
        }
        else
        {
            terminal.SetColor("cyan");
            terminal.WriteLine($"  Currently Equipped: {currentEquip.Name}");

            // Compare primary stat (Attack for weapons, Armor for armor, stat value for accessories)
            if (lootItem.Type == global::ObjType.Weapon)
            {
                int currentPower = currentEquip.WeaponPower;
                int newPower = lootItem.Attack;
                int diff = newPower - currentPower;

                terminal.SetColor("white");
                terminal.Write($"  Attack: {currentPower} -> {newPower} ");
                if (diff > 0)
                {
                    terminal.SetColor("bright_green");
                    terminal.WriteLine($"(+{diff} UPGRADE)");
                }
                else if (diff < 0)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine($"({diff} downgrade)");
                }
                else
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine("(same)");
                }
            }
            else if (lootItem.Type == global::ObjType.Fingers || lootItem.Type == global::ObjType.Neck)
            {
                // Accessories: compare total stat value instead of armor
                int currentStatTotal = currentEquip.StrengthBonus + currentEquip.DexterityBonus +
                    currentEquip.WisdomBonus + currentEquip.MaxHPBonus + currentEquip.MaxManaBonus +
                    currentEquip.DefenceBonus + currentEquip.AgilityBonus + currentEquip.ConstitutionBonus +
                    currentEquip.IntelligenceBonus + currentEquip.CharismaBonus;
                int newStatTotal = lootItem.Strength + lootItem.Dexterity + lootItem.Wisdom +
                    lootItem.HP + lootItem.Mana + lootItem.Defence;
                int diff = newStatTotal - currentStatTotal;

                terminal.SetColor("white");
                terminal.Write($"  Stat Total: {currentStatTotal} -> {newStatTotal} ");
                if (diff > 0)
                {
                    terminal.SetColor("bright_green");
                    terminal.WriteLine($"(+{diff} UPGRADE)");
                }
                else if (diff < 0)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine($"({diff} downgrade)");
                }
                else
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine("(same)");
                }
            }
            else
            {
                int currentAC = currentEquip.ArmorClass;
                int newAC = lootItem.Armor;
                int diff = newAC - currentAC;

                terminal.SetColor("white");
                terminal.Write($"  Armor: {currentAC} -> {newAC} ");
                if (diff > 0)
                {
                    terminal.SetColor("bright_green");
                    terminal.WriteLine($"(+{diff} UPGRADE)");
                }
                else if (diff < 0)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine($"({diff} downgrade)");
                }
                else
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine("(same)");
                }
            }

            // Compare bonus stats if either item has them
            var currentBonuses = new List<string>();
            var newBonuses = new List<string>();

            // Current item bonuses
            if (currentEquip.StrengthBonus != 0) currentBonuses.Add($"Str {currentEquip.StrengthBonus:+#;-#;0}");
            if (currentEquip.DexterityBonus != 0) currentBonuses.Add($"Dex {currentEquip.DexterityBonus:+#;-#;0}");
            if (currentEquip.WisdomBonus != 0) currentBonuses.Add($"Wis {currentEquip.WisdomBonus:+#;-#;0}");
            if (currentEquip.MaxHPBonus != 0) currentBonuses.Add($"HP {currentEquip.MaxHPBonus:+#;-#;0}");
            if (currentEquip.MaxManaBonus != 0) currentBonuses.Add($"Mana {currentEquip.MaxManaBonus:+#;-#;0}");
            if (currentEquip.DefenceBonus != 0) currentBonuses.Add($"Def {currentEquip.DefenceBonus:+#;-#;0}");

            // New item bonuses
            if (lootItem.Strength != 0) newBonuses.Add($"Str {lootItem.Strength:+#;-#;0}");
            if (lootItem.Dexterity != 0) newBonuses.Add($"Dex {lootItem.Dexterity:+#;-#;0}");
            if (lootItem.Wisdom != 0) newBonuses.Add($"Wis {lootItem.Wisdom:+#;-#;0}");
            if (lootItem.HP != 0) newBonuses.Add($"HP {lootItem.HP:+#;-#;0}");
            if (lootItem.Mana != 0) newBonuses.Add($"Mana {lootItem.Mana:+#;-#;0}");
            if (lootItem.Defence != 0) newBonuses.Add($"Def {lootItem.Defence:+#;-#;0}");

            if (currentBonuses.Count > 0 || newBonuses.Count > 0)
            {
                terminal.SetColor("gray");
                if (currentBonuses.Count > 0)
                    terminal.WriteLine($"  Current bonuses: {string.Join(", ", currentBonuses)}");
                else
                    terminal.WriteLine("  Current bonuses: (none)");

                if (newBonuses.Count > 0)
                    terminal.WriteLine($"  New bonuses: {string.Join(", ", newBonuses)}");
                else
                    terminal.WriteLine("  New bonuses: (none)");
            }
        }

        terminal.SetColor("gray");
        terminal.WriteLine("  ─────────────────────────────────────");

        await Task.CompletedTask; // Keep async signature for consistency
    }

    /// <summary>
    /// Prompt player to choose which hand to equip a one-handed weapon in
    /// </summary>
    private async Task<EquipmentSlot?> PromptForWeaponSlot(Character player)
    {
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine("This is a one-handed weapon. Where would you like to equip it?");
        terminal.WriteLine("");

        // Show current equipment in both slots
        var mainHandItem = player.GetEquipment(EquipmentSlot.MainHand);
        var offHandItem = player.GetEquipment(EquipmentSlot.OffHand);

        terminal.SetColor("white");
        terminal.Write("  (M) Main Hand: ");
        if (mainHandItem != null)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(mainHandItem.Name);
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("Empty");
        }

        terminal.SetColor("white");
        terminal.Write("  (O) Off-Hand:  ");
        if (offHandItem != null)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(offHandItem.Name);
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("Empty");
        }

        terminal.SetColor("white");
        terminal.WriteLine("  (C) Cancel - Add to inventory instead");
        terminal.WriteLine("");

        terminal.Write("Your choice: ");
        var slotChoice = await terminal.GetKeyInput();

        return slotChoice.ToUpper() switch
        {
            "M" => EquipmentSlot.MainHand,
            "O" => EquipmentSlot.OffHand,
            _ => null // Cancel
        };
    }

    private async Task<EquipmentSlot?> PromptForRingSlot(Character player)
    {
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine("Both ring slots are occupied. Which ring would you like to replace?");
        terminal.WriteLine("");

        var leftRing = player.GetEquipment(EquipmentSlot.LFinger);
        var rightRing = player.GetEquipment(EquipmentSlot.RFinger);

        terminal.SetColor("white");
        terminal.Write("  (L) Left Finger:  ");
        if (leftRing != null)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(leftRing.Name);
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("Empty");
        }

        terminal.SetColor("white");
        terminal.Write("  (R) Right Finger: ");
        if (rightRing != null)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(rightRing.Name);
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("Empty");
        }

        terminal.SetColor("white");
        terminal.WriteLine("  (I) Add to inventory instead");
        terminal.WriteLine("");

        terminal.Write("Your choice: ");
        var slotChoice = await terminal.GetKeyInput();

        return slotChoice.ToUpper() switch
        {
            "L" => EquipmentSlot.LFinger,
            "R" => EquipmentSlot.RFinger,
            _ => null // Inventory
        };
    }

    // ==================== MULTI-MONSTER COMBAT HELPER METHODS ====================

    /// <summary>
    /// Display current combat status with all monsters and status effects
    /// </summary>
    private void DisplayCombatStatus(List<Monster> monsters, Character player)
    {
        // Check for screen reader mode
        if (player is Player p && p.ScreenReaderMode)
        {
            DisplayCombatStatusScreenReader(monsters, player);
            return;
        }

        // BBS 80x25 — compact status that leaves room for action menu on same page
        if (DoorMode.IsInDoorMode)
        {
            DisplayCombatStatusBBS(monsters, player);
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                    COMBAT STATUS                         ║");
        terminal.WriteLine("╠══════════════════════════════════════════════════════════╣");

        // Show all living monsters with status effects
        int monsterNum = 0;
        for (int i = 0; i < monsters.Count; i++)
        {
            var monster = monsters[i];
            if (!monster.IsAlive) continue;
            monsterNum++;

            // Calculate HP bar (guard against division by zero)
            double hpPercent = Math.Max(0, Math.Min(1.0, (double)monster.HP / Math.Max(1, monster.MaxHP)));
            int barLength = 12;
            int filledBars = Math.Max(0, Math.Min(barLength, (int)(hpPercent * barLength)));
            int emptyBars = barLength - filledBars;
            string hpBar = new string('█', filledBars) + new string('░', emptyBars);

            terminal.SetColor("yellow");
            terminal.Write($"║ [{i + 1}] ");
            terminal.SetColor(monster.IsBoss ? "bright_red" : "white");
            // Truncate long names to fit the status box (18 chars)
            string displayName = monster.Name;
            if (displayName.Length > 18)
            {
                // For bosses with titles like "Maelketh, The Broken Blade", use just the first name
                int commaIdx = displayName.IndexOf(',');
                if (commaIdx > 0 && commaIdx <= 18)
                    displayName = displayName[..commaIdx];
                else
                    displayName = displayName[..18];
            }
            terminal.Write($"{displayName,-18} ");
            terminal.SetColor(hpPercent > 0.5 ? "green" : hpPercent > 0.25 ? "yellow" : "red");
            terminal.Write($"{hpBar} ");
            terminal.SetColor("white");
            terminal.Write($"{monster.HP,5}/{monster.MaxHP,-5}");

            // Show boss phase indicator
            if (BossContext != null && monster.IsBoss)
            {
                terminal.SetColor("bright_magenta");
                terminal.Write($" [Phase {BossContext.CurrentPhase}]");
            }

            // Show monster status effects
            var monsterStatuses = GetMonsterStatusString(monster);
            if (!string.IsNullOrEmpty(monsterStatuses))
            {
                terminal.SetColor("darkgray");
                terminal.WriteLine($" {monsterStatuses}");
            }
            else
            {
                terminal.WriteLine("");
            }
        }

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╠══════════════════════════════════════════════════════════╣");

        // Show player status with enhanced display (guard against division by zero)
        double playerHpPercent = Math.Max(0, Math.Min(1.0, (double)player.HP / Math.Max(1, player.MaxHP)));
        double playerMpPercent = player.MaxMana > 0 ? Math.Max(0, Math.Min(1.0, (double)player.Mana / player.MaxMana)) : 0;

        int hpBarLen = 15;
        int mpBarLen = 10;

        int playerFilledBars = Math.Max(0, Math.Min(hpBarLen, (int)(playerHpPercent * hpBarLen)));
        int playerEmptyBars = hpBarLen - playerFilledBars;
        string playerHpBar = new string('█', playerFilledBars) + new string('░', playerEmptyBars);

        int manaFilledBars = Math.Max(0, Math.Min(mpBarLen, (int)(playerMpPercent * mpBarLen)));
        int manaEmptyBars = mpBarLen - manaFilledBars;
        string manaBar = new string('█', manaFilledBars) + new string('░', manaEmptyBars);

        terminal.SetColor("bright_cyan");
        terminal.Write($"║ ");
        terminal.SetColor("bright_white");
        terminal.Write($"{(player.Name2 ?? player.Name1),-18} ");

        // HP bar
        terminal.SetColor(playerHpPercent > 0.5 ? "bright_green" : playerHpPercent > 0.25 ? "yellow" : "bright_red");
        terminal.Write($"HP:{playerHpBar} ");
        terminal.SetColor("white");
        terminal.Write($"{player.HP,5}/{player.MaxHP,-5}");
        terminal.WriteLine("");

        // Mana and resources line
        terminal.SetColor("bright_cyan");
        terminal.Write($"║ ");
        terminal.SetColor("bright_blue");
        terminal.Write($"MP:{manaBar} ");
        terminal.SetColor("cyan");
        terminal.Write($"{player.Mana,4}/{player.MaxMana,-4}  ");
        terminal.SetColor("bright_magenta");
        terminal.Write($"Potions: ");
        terminal.SetColor("white");
        terminal.WriteLine($"{player.Healing}/{player.MaxPotions}");

        // Stamina bar for combat abilities
        double staminaPercent = player.MaxCombatStamina > 0 ? Math.Max(0, Math.Min(1.0, (double)player.CurrentCombatStamina / player.MaxCombatStamina)) : 0;
        int staminaBarLen = 10;
        int staminaFilledBars = Math.Max(0, Math.Min(staminaBarLen, (int)(staminaPercent * staminaBarLen)));
        int staminaEmptyBars = staminaBarLen - staminaFilledBars;
        string staminaBar = new string('█', staminaFilledBars) + new string('░', staminaEmptyBars);

        terminal.SetColor("bright_cyan");
        terminal.Write($"║ ");
        terminal.SetColor("bright_yellow");
        terminal.Write($"ST:{staminaBar} ");
        terminal.SetColor("yellow");
        terminal.WriteLine($"{player.CurrentCombatStamina,4}/{player.MaxCombatStamina,-4}");

        // Status effects line for player
        if (player.ActiveStatuses.Count > 0)
        {
            terminal.SetColor("bright_cyan");
            terminal.Write($"║ ");
            terminal.SetColor("gray");
            terminal.Write("Status: ");
            DisplayPlayerStatusEffects(player);
            terminal.WriteLine("");
        }

        // Combat stats line
        terminal.SetColor("bright_cyan");
        terminal.Write($"║ ");
        terminal.SetColor("gray");
        terminal.Write($"ATK: ");
        terminal.SetColor("bright_yellow");
        terminal.Write($"{player.Strength + player.WeapPow,-5} ");
        terminal.SetColor("gray");
        terminal.Write($"DEF: ");
        terminal.SetColor("bright_cyan");
        terminal.Write($"{player.Defence + player.ArmPow + player.MagicACBonus,-5} ");

        // Show damage absorption if active
        if (player.DamageAbsorptionPool > 0)
        {
            terminal.SetColor("gray");
            terminal.Write($"Shield: ");
            terminal.SetColor("bright_magenta");
            terminal.Write($"{player.DamageAbsorptionPool}");
        }
        terminal.WriteLine("");

        // Show teammate status if we have teammates
        if (currentTeammates != null && currentTeammates.Count > 0)
        {
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("╠══════════════════════════════════════════════════════════╣");
            terminal.SetColor("gray");
            terminal.WriteLine("║                     ALLIES                               ║");

            foreach (var teammate in currentTeammates)
            {
                if (!teammate.IsAlive) continue;

                // Calculate HP bar for teammate
                double tmHpPercent = Math.Max(0, Math.Min(1.0, (double)teammate.HP / teammate.MaxHP));
                int tmBarLen = 10;
                int tmFilledBars = Math.Max(0, Math.Min(tmBarLen, (int)(tmHpPercent * tmBarLen)));
                int tmEmptyBars = tmBarLen - tmFilledBars;
                string tmHpBar = new string('█', tmFilledBars) + new string('░', tmEmptyBars);

                terminal.SetColor("bright_cyan");
                terminal.Write($"║ ");
                terminal.SetColor("white");
                terminal.Write($"{teammate.DisplayName,-16} ");
                terminal.SetColor(tmHpPercent > 0.5 ? "green" : tmHpPercent > 0.25 ? "yellow" : "red");
                terminal.Write($"HP:{tmHpBar} ");
                terminal.SetColor("white");
                terminal.Write($"{teammate.HP,4}/{teammate.MaxHP,-4} ");

                // Show potions for non-healers, mana for healers
                bool isHealer = teammate.Class == CharacterClass.Cleric || teammate.Class == CharacterClass.Paladin;
                if (isHealer && teammate.MaxMana > 0)
                {
                    terminal.SetColor("cyan");
                    terminal.Write($"MP:{teammate.Mana}/{teammate.MaxMana}");
                }
                else if (teammate.Healing > 0)
                {
                    terminal.SetColor("magenta");
                    terminal.Write($"Pot:{teammate.Healing}");
                }
                terminal.WriteLine("");
            }
        }

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");
    }

    /// <summary>
    /// Compact combat status for BBS 80x25 terminals.
    /// Displays monsters and player on minimal lines to leave room for the action menu.
    /// </summary>
    private void DisplayCombatStatusBBS(List<Monster> monsters, Character player)
    {
        // Line 1: Header
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══════════════════════════════ COMBAT ═══════════════════════════════════════");

        // Lines 2+: Monsters (two per line to save space)
        var aliveMonsters = monsters.Where(m => m.IsAlive).ToList();
        for (int i = 0; i < aliveMonsters.Count; i += 2)
        {
            var m1 = aliveMonsters[i];
            double hp1Pct = (double)m1.HP / Math.Max(1, m1.MaxHP);
            int filled1 = (int)(hp1Pct * 8);
            string bar1 = new string('█', filled1) + new string('░', 8 - filled1);
            string col1 = hp1Pct > 0.5 ? "green" : hp1Pct > 0.25 ? "yellow" : "red";

            terminal.SetColor("yellow");
            terminal.Write($" [{monsters.IndexOf(m1) + 1}]");
            terminal.SetColor("white");
            string name1 = m1.Name.Length > 14 ? m1.Name[..14] : m1.Name;
            terminal.Write($"{name1,-14} ");
            terminal.SetColor(col1);
            terminal.Write($"{bar1} ");
            terminal.SetColor("white");
            terminal.Write($"{m1.HP,4}/{m1.MaxHP,-4}");

            if (i + 1 < aliveMonsters.Count)
            {
                var m2 = aliveMonsters[i + 1];
                double hp2Pct = (double)m2.HP / Math.Max(1, m2.MaxHP);
                int filled2 = (int)(hp2Pct * 8);
                string bar2 = new string('█', filled2) + new string('░', 8 - filled2);
                string col2 = hp2Pct > 0.5 ? "green" : hp2Pct > 0.25 ? "yellow" : "red";

                terminal.Write("  ");
                terminal.SetColor("yellow");
                terminal.Write($"[{monsters.IndexOf(m2) + 1}]");
                terminal.SetColor("white");
                string name2 = m2.Name.Length > 14 ? m2.Name[..14] : m2.Name;
                terminal.Write($"{name2,-14} ");
                terminal.SetColor(col2);
                terminal.Write($"{bar2} ");
                terminal.SetColor("white");
                terminal.Write($"{m2.HP,4}/{m2.MaxHP,-4}");
            }
            terminal.WriteLine("");
        }

        // Separator
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("──────────────────────────────────────────────────────────────────────────────");

        // Player line 1: Name + HP + MP + Potions
        double playerHpPct = (double)player.HP / Math.Max(1, player.MaxHP);
        int pFilled = (int)(playerHpPct * 10);
        string pBar = new string('█', pFilled) + new string('░', 10 - pFilled);
        string pCol = playerHpPct > 0.5 ? "bright_green" : playerHpPct > 0.25 ? "yellow" : "bright_red";

        terminal.SetColor("bright_white");
        terminal.Write($" {(player.Name2 ?? player.Name1)}  ");
        terminal.SetColor("gray");
        terminal.Write("HP:");
        terminal.SetColor(pCol);
        terminal.Write($"{pBar} {player.HP}/{player.MaxHP}");
        if (player.MaxMana > 0)
        {
            terminal.SetColor("gray");
            terminal.Write("  MP:");
            terminal.SetColor("bright_blue");
            terminal.Write($"{player.Mana}/{player.MaxMana}");
        }
        terminal.SetColor("gray");
        terminal.Write("  Pot:");
        terminal.SetColor("magenta");
        terminal.Write($"{player.Healing}/{player.MaxPotions}");
        terminal.WriteLine("");

        // Player line 2: ST + ATK + DEF + status effects
        terminal.SetColor("gray");
        terminal.Write($" ST:");
        terminal.SetColor("yellow");
        terminal.Write($"{player.CurrentCombatStamina}/{player.MaxCombatStamina}");
        terminal.SetColor("gray");
        terminal.Write($"  ATK:");
        terminal.SetColor("bright_yellow");
        terminal.Write($"{player.Strength + player.WeapPow}");
        terminal.SetColor("gray");
        terminal.Write($"  DEF:");
        terminal.SetColor("bright_cyan");
        terminal.Write($"{player.Defence + player.ArmPow + player.MagicACBonus}");
        if (player.DamageAbsorptionPool > 0)
        {
            terminal.SetColor("gray");
            terminal.Write($"  Shield:");
            terminal.SetColor("bright_magenta");
            terminal.Write($"{player.DamageAbsorptionPool}");
        }
        // Inline status effects
        if (player.ActiveStatuses.Count > 0 || player.IsRaging)
        {
            var statuses = new List<string>();
            foreach (var kv in player.ActiveStatuses)
                statuses.Add(kv.Value > 0 ? $"{kv.Key}({kv.Value})" : kv.Key.ToString());
            if (player.IsRaging && !statuses.Any(s => s.StartsWith("Raging")))
                statuses.Add("Raging");
            terminal.SetColor("gray");
            terminal.Write("  ");
            terminal.SetColor("yellow");
            terminal.Write(string.Join(",", statuses));
        }
        terminal.WriteLine("");

        // Show teammate status (one line each, compact)
        if (currentTeammates != null && currentTeammates.Count > 0)
        {
            var aliveTeammates = currentTeammates.Where(t => t.IsAlive).ToList();
            if (aliveTeammates.Count > 0)
            {
                terminal.SetColor("gray");
                terminal.Write(" Allies: ");
                for (int i = 0; i < aliveTeammates.Count; i++)
                {
                    var tm = aliveTeammates[i];
                    double tmPct = (double)tm.HP / Math.Max(1, tm.MaxHP);
                    string tmCol = tmPct > 0.5 ? "green" : tmPct > 0.25 ? "yellow" : "red";
                    if (i > 0) terminal.Write("  ");
                    terminal.SetColor("white");
                    string tmName = tm.DisplayName.Length > 10 ? tm.DisplayName[..10] : tm.DisplayName;
                    terminal.Write($"{tmName} ");
                    terminal.SetColor(tmCol);
                    terminal.Write($"{tm.HP}/{tm.MaxHP}");
                }
                terminal.WriteLine("");
            }
        }

        // Boss phase indicator
        if (BossContext != null)
        {
            var bossMonster = aliveMonsters.FirstOrDefault(m => m.IsBoss);
            if (bossMonster != null)
            {
                terminal.SetColor("bright_magenta");
                terminal.WriteLine($" Boss Phase {BossContext.CurrentPhase}");
            }
        }

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("──────────────────────────────────────────────────────────────────────────────");
    }

    /// <summary>
    /// Screen reader friendly version of combat status display.
    /// Uses plain text instead of box-drawing characters and visual bars.
    /// </summary>
    private void DisplayCombatStatusScreenReader(List<Monster> monsters, Character player)
    {
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("--- COMBAT STATUS ---");
        terminal.WriteLine("");

        // Show all living monsters
        terminal.SetColor("yellow");
        terminal.WriteLine("Enemies:");
        int monsterNum = 0;
        for (int i = 0; i < monsters.Count; i++)
        {
            var monster = monsters[i];
            if (!monster.IsAlive) continue;
            monsterNum++;

            double hpPercent = Math.Max(0, Math.Min(100, (double)monster.HP / Math.Max(1, monster.MaxHP) * 100));
            string hpStatus = hpPercent > 75 ? "healthy" : hpPercent > 50 ? "wounded" : hpPercent > 25 ? "badly hurt" : "near death";

            terminal.SetColor(monster.IsBoss ? "bright_red" : "white");
            terminal.Write($"  {i + 1}. {monster.Name}");
            terminal.SetColor("gray");
            terminal.Write($" - HP: {monster.HP} of {monster.MaxHP} ({(int)hpPercent} percent, {hpStatus})");

            var monsterStatuses = GetMonsterStatusString(monster);
            if (!string.IsNullOrEmpty(monsterStatuses))
            {
                terminal.Write($" [{monsterStatuses}]");
            }
            terminal.WriteLine("");
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_white");
        terminal.WriteLine("Your Status:");

        // Player HP
        double playerHpPercent = Math.Max(0, Math.Min(100, (double)player.HP / Math.Max(1, player.MaxHP) * 100));
        string playerHpStatus = playerHpPercent > 75 ? "healthy" : playerHpPercent > 50 ? "wounded" : playerHpPercent > 25 ? "badly hurt" : "critical";
        terminal.SetColor(playerHpPercent > 50 ? "green" : playerHpPercent > 25 ? "yellow" : "red");
        terminal.WriteLine($"  HP: {player.HP} of {player.MaxHP} ({(int)playerHpPercent} percent, {playerHpStatus})");

        // Player MP
        if (player.MaxMana > 0)
        {
            double mpPercent = Math.Max(0, Math.Min(100, (double)player.Mana / player.MaxMana * 100));
            terminal.SetColor("cyan");
            terminal.WriteLine($"  MP: {player.Mana} of {player.MaxMana} ({(int)mpPercent} percent)");
        }

        // Stamina
        if (player.MaxCombatStamina > 0)
        {
            double stPercent = Math.Max(0, Math.Min(100, (double)player.CurrentCombatStamina / player.MaxCombatStamina * 100));
            terminal.SetColor("yellow");
            terminal.WriteLine($"  Stamina: {player.CurrentCombatStamina} of {player.MaxCombatStamina} ({(int)stPercent} percent)");
        }

        // Potions
        terminal.SetColor("magenta");
        terminal.WriteLine($"  Potions: {player.Healing} of {player.MaxPotions}");

        // Combat stats
        terminal.SetColor("gray");
        terminal.WriteLine($"  Attack: {player.Strength + player.WeapPow}, Defense: {player.Defence + player.ArmPow + player.MagicACBonus}");

        // Damage absorption
        if (player.DamageAbsorptionPool > 0)
        {
            terminal.SetColor("magenta");
            terminal.WriteLine($"  Magic Shield: {player.DamageAbsorptionPool} damage remaining");
        }

        // Status effects
        if (player.ActiveStatuses.Count > 0)
        {
            terminal.SetColor("gray");
            terminal.Write("  Status effects: ");
            DisplayPlayerStatusEffects(player);
            terminal.WriteLine("");
        }

        // Teammates
        if (currentTeammates != null && currentTeammates.Count > 0)
        {
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine("Allies:");

            foreach (var teammate in currentTeammates)
            {
                if (!teammate.IsAlive) continue;

                double tmHpPercent = Math.Max(0, Math.Min(100, (double)teammate.HP / teammate.MaxHP * 100));
                string tmStatus = tmHpPercent > 75 ? "healthy" : tmHpPercent > 50 ? "wounded" : tmHpPercent > 25 ? "badly hurt" : "critical";

                terminal.SetColor("white");
                terminal.Write($"  {teammate.DisplayName}");
                terminal.SetColor(tmHpPercent > 50 ? "green" : tmHpPercent > 25 ? "yellow" : "red");
                terminal.Write($" - HP: {teammate.HP} of {teammate.MaxHP} ({(int)tmHpPercent} percent, {tmStatus})");

                bool isHealer = teammate.Class == CharacterClass.Cleric || teammate.Class == CharacterClass.Paladin;
                if (isHealer && teammate.MaxMana > 0)
                {
                    terminal.SetColor("cyan");
                    terminal.Write($", MP: {teammate.Mana} of {teammate.MaxMana}");
                }
                else if (teammate.Healing > 0)
                {
                    terminal.SetColor("magenta");
                    terminal.Write($", Potions: {teammate.Healing}");
                }
                terminal.WriteLine("");
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("---------------------");
        terminal.WriteLine("");
    }

    /// <summary>
    /// Get status string for a monster
    /// </summary>
    private string GetMonsterStatusString(Monster monster)
    {
        var statuses = new List<string>();

        if (monster.PoisonRounds > 0) statuses.Add($"PSN({monster.PoisonRounds})");
        if (monster.StunRounds > 0) statuses.Add($"STN({monster.StunRounds})");
        if (monster.WeakenRounds > 0) statuses.Add($"WEK({monster.WeakenRounds})");
        if (monster.IsBoss) statuses.Add("[BOSS]");

        return string.Join(" ", statuses);
    }

    /// <summary>
    /// Display player status effects with colors
    /// </summary>
    private void DisplayPlayerStatusEffects(Character player)
    {
        bool first = true;
        foreach (var kvp in player.ActiveStatuses)
        {
            if (!first) terminal.Write(" ");
            first = false;

            string color = kvp.Key.GetDisplayColor();
            string shortName = kvp.Key.GetShortName();

            terminal.SetColor(color);
            terminal.Write($"{shortName}({kvp.Value})");
        }
    }

    /// <summary>
    /// Display and process status effect tick messages
    /// </summary>
    private void DisplayStatusEffectMessages(List<(string message, string color)> messages)
    {
        foreach (var (message, color) in messages)
        {
            terminal.SetColor(color);
            terminal.WriteLine($"  » {message}");
        }
    }

    /// <summary>
    /// Get target selection from player
    /// </summary>
    private async Task<int?> GetTargetSelection(List<Monster> monsters, bool allowRandom = true)
    {
        var livingMonsters = monsters.Where(m => m.IsAlive).ToList();

        if (livingMonsters.Count == 1)
        {
            // Only one target, auto-select
            return monsters.IndexOf(livingMonsters[0]);
        }

        terminal.SetColor("yellow");
        if (allowRandom)
        {
            terminal.Write($"Target which monster? (1-{monsters.Count}, or ENTER for random): ");
        }
        else
        {
            terminal.Write($"Target which monster? (1-{monsters.Count}): ");
        }

        var input = await terminal.GetInput("");

        if (string.IsNullOrWhiteSpace(input) && allowRandom)
        {
            // Random target
            return null;
        }

        if (int.TryParse(input.Trim(), out int targetNum) && targetNum >= 1 && targetNum <= monsters.Count)
        {
            int index = targetNum - 1;
            if (monsters[index].IsAlive)
            {
                return index;
            }
            else
            {
                terminal.WriteLine("That monster is already dead!", "red");
                await Task.Delay(GetCombatDelay(1000));
                return await GetTargetSelection(monsters, allowRandom);
            }
        }

        terminal.WriteLine("Invalid target!", "red");
        await Task.Delay(GetCombatDelay(1000));
        return await GetTargetSelection(monsters, allowRandom);
    }

    /// <summary>
    /// Get random living monster from list
    /// </summary>
    private Monster GetRandomLivingMonster(List<Monster> monsters)
    {
        var living = monsters.Where(m => m.IsAlive).ToList();
        if (living.Count == 0) return null;
        return living[random.Next(living.Count)];
    }

    /// <summary>
    /// Apply damage to all living monsters (AoE) - damage is split among targets
    /// </summary>
    private async Task ApplyAoEDamage(List<Monster> monsters, long totalDamage, CombatResult result, string damageSource = "AoE attack")
    {
        var livingMonsters = monsters.Where(m => m.IsAlive).ToList();
        if (livingMonsters.Count == 0) return;

        // Split damage among all living monsters
        long damagePerMonster = totalDamage / livingMonsters.Count;

        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"{damageSource} hits all enemies for {damagePerMonster} damage each!");
        terminal.WriteLine("");

        foreach (var monster in livingMonsters)
        {
            long actualDamage = Math.Max(1, damagePerMonster - monster.ArmPow);

            // Marked targets take 30% bonus damage
            if (monster.IsMarked)
                actualDamage = (long)(actualDamage * 1.3);

            // Wake sleeping monsters on damage
            if (monster.IsSleeping)
            {
                monster.IsSleeping = false;
                monster.SleepDuration = 0;
                terminal.WriteLine($"{monster.Name} wakes up from the attack!", "yellow");
            }

            monster.HP -= actualDamage;

            // Track damage dealt statistics
            result.Player?.Statistics.RecordDamageDealt(actualDamage, false);

            terminal.SetColor("yellow");
            terminal.Write($"{monster.Name}: ");
            terminal.SetColor("red");
            terminal.WriteLine($"-{actualDamage} HP");

            // Elemental enchant procs (v0.30.9)
            if (result.Player != null)
                CheckElementalEnchantProcsMonster(result.Player, monster, actualDamage);

            // Equipment lifesteal (Lifedrinker enchant)
            if (result.Player != null && actualDamage > 0)
            {
                int equipLS = result.Player.GetEquipmentLifeSteal();
                if (equipLS > 0)
                {
                    long stolen = Math.Max(1, actualDamage * equipLS / 100);
                    result.Player.HP = Math.Min(result.Player.MaxHP, result.Player.HP + stolen);
                    terminal.WriteLine($"  Your weapon drains {stolen} life! (Lifedrinker)", "dark_green");
                }
            }

            if (monster.HP <= 0)
            {
                monster.HP = 0;
                terminal.SetColor("bright_red");
                terminal.WriteLine($"  {monster.Name} has been defeated!");
                result.DefeatedMonsters.Add(monster);

                // Bloodlust heal-on-kill
                if (result.Player != null && result.Player.HasBloodlust)
                {
                    long bloodlustHeal = Math.Min(result.Player.Level * 2 + 20, result.Player.MaxHP - result.Player.HP);
                    if (bloodlustHeal > 0)
                    {
                        result.Player.HP += bloodlustHeal;
                        terminal.SetColor("bright_red");
                        terminal.WriteLine($"  BLOODLUST! +{bloodlustHeal} HP!");
                    }
                }
            }

            result.CombatLog.Add($"{monster.Name} took {actualDamage} damage from {damageSource}");
        }

        await Task.Delay(GetCombatDelay(1500));
    }

    /// <summary>
    /// Apply damage to single monster and track if defeated
    /// </summary>
    private async Task ApplySingleMonsterDamage(Monster target, long damage, CombatResult result, string damageSource = "attack", Character? attacker = null)
    {
        if (target == null || !target.IsAlive) return;

        // Armor piercing enchantment reduces effective monster armor
        long effectiveArmor = target.ArmPow;
        if (attacker != null)
        {
            int armorPiercePct = attacker.GetEquipmentArmorPiercing();
            if (armorPiercePct > 0)
            {
                effectiveArmor = Math.Max(0, effectiveArmor - effectiveArmor * armorPiercePct / 100);
            }
        }
        long actualDamage = Math.Max(1, damage - effectiveArmor);

        // Marked target takes 30% bonus damage
        if (target.IsMarked)
        {
            long markedBonus = (long)(actualDamage * 0.3);
            actualDamage += markedBonus;
            terminal.SetColor("bright_red");
            terminal.WriteLine($"[MARKED] +{markedBonus} bonus damage!");
        }

        // Apply alignment bonus damage if attacker is provided
        if (attacker != null)
        {
            var (bonusDamage, bonusDesc) = GetAlignmentBonusDamage(attacker, target, actualDamage);
            if (bonusDamage > 0)
            {
                actualDamage += bonusDamage;
            }
            if (!string.IsNullOrEmpty(bonusDesc))
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(bonusDesc);
            }
        }

        // Wake sleeping monsters on damage
        if (target.IsSleeping)
        {
            target.IsSleeping = false;
            target.SleepDuration = 0;
            terminal.WriteLine($"{target.Name} wakes up from the attack!", "yellow");
        }

        target.HP -= actualDamage;

        // Track damage dealt statistics (only for player attacks)
        if (attacker == currentPlayer || attacker == result.Player)
        {
            result.Player?.Statistics.RecordDamageDealt(actualDamage, false);
            result.TotalDamageDealt += actualDamage; // Track for telemetry
        }

        // Use new colored combat messages - different message for player vs allies
        string attackMessage;
        if (attacker != null && attacker != currentPlayer && attacker.IsCompanion)
        {
            // Companion/ally attack
            attackMessage = CombatMessages.GetAllyAttackMessage(attacker.DisplayName, target.Name, actualDamage, target.MaxHP);
        }
        else if (attacker != null && attacker != currentPlayer && attacker is NPC)
        {
            // NPC teammate attack
            attackMessage = CombatMessages.GetAllyAttackMessage(attacker.DisplayName, target.Name, actualDamage, target.MaxHP);
        }
        else
        {
            // Player attack
            attackMessage = CombatMessages.GetPlayerAttackMessage(target.Name, actualDamage, target.MaxHP);
        }
        terminal.WriteLine(attackMessage);

        if (target.HP <= 0)
        {
            target.HP = 0;

            // Use new colored death message
            var deathMessage = CombatMessages.GetDeathMessage(target.Name, target.MonsterColor);
            terminal.WriteLine(deathMessage);

            result.DefeatedMonsters.Add(target);

            // Bloodlust heal-on-kill
            if (result.Player != null && result.Player.HasBloodlust)
            {
                long bloodlustHeal = Math.Min(result.Player.Level * 2 + 20, result.Player.MaxHP - result.Player.HP);
                if (bloodlustHeal > 0)
                {
                    result.Player.HP += bloodlustHeal;
                    terminal.SetColor("bright_red");
                    terminal.WriteLine($"BLOODLUST! You feast on the kill and recover {bloodlustHeal} HP!");
                }
            }

            await Task.Delay(GetCombatDelay(800));
        }

        result.CombatLog.Add($"{target.Name} took {actualDamage} damage from {damageSource}");
    }

    // ==================== MULTI-MONSTER ACTION PROCESSING ====================

    /// <summary>
    /// Show spell list and let player choose a spell
    /// Returns spell index or -1 if cancelled
    /// </summary>
    private async Task<int> ShowSpellListAndChoose(Character player)
    {
        var availableSpells = SpellSystem.GetAvailableSpells(player);
        if (availableSpells.Count == 0)
        {
            terminal.WriteLine("You don't know any spells yet!", "red");
            await Task.Delay(GetCombatDelay(1500));
            return -1;
        }

        terminal.WriteLine("");
        terminal.SetColor("magenta");
        terminal.WriteLine("=== Available Spells ===");
        terminal.WriteLine("");

        for (int i = 0; i < availableSpells.Count; i++)
        {
            var spell = availableSpells[i];
            int manaCost = SpellSystem.CalculateManaCost(spell, player);
            bool canCast = player.Mana >= manaCost;

            if (canCast)
            {
                terminal.SetColor("white");
                terminal.Write($"[{i + 1}] ");
                terminal.SetColor("cyan");
                terminal.Write($"{spell.Name}");
                terminal.SetColor("gray");
                terminal.Write($" (Lv{spell.Level})");
                terminal.SetColor("yellow");
                terminal.WriteLine($" - {manaCost} mana");
            }
            else
            {
                terminal.SetColor("darkgray");
                terminal.WriteLine($"[{i + 1}] {spell.Name} (Lv{spell.Level}) - {manaCost} mana (Not enough mana)");
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.Write($"Choose spell (1-{availableSpells.Count}, or 0 to cancel): ");

        var input = await terminal.GetInput("");

        if (int.TryParse(input.Trim(), out int choice))
        {
            if (choice == 0)
            {
                return -1; // Cancelled
            }

            if (choice >= 1 && choice <= availableSpells.Count)
            {
                var selectedSpell = availableSpells[choice - 1];
                int manaCost = SpellSystem.CalculateManaCost(selectedSpell, player);

                if (player.Mana < manaCost)
                {
                    terminal.WriteLine("Not enough mana!", "red");
                    await Task.Delay(GetCombatDelay(1500));
                    return -1;
                }

                return selectedSpell.Level; // Return spell level/index
            }
        }

        terminal.WriteLine("Invalid choice!", "red");
        await Task.Delay(GetCombatDelay(1000));
        return -1;
    }

    /// <summary>
    /// Get player action for multi-monster combat with target selection
    /// </summary>
    private async Task<(CombatAction action, bool enableAutoCombat)> GetPlayerActionMultiMonster(Character player, List<Monster> monsters, CombatResult result)
    {
        while (true)  // Loop until valid action is chosen
        {
            // Check if player can act due to status effects
            if (!player.CanAct())
            {
                var preventingStatus = player.ActiveStatuses.Keys.FirstOrDefault(s => s.PreventsAction());
                terminal.SetColor("yellow");
                terminal.WriteLine($"You are {preventingStatus.ToString().ToLower()} and cannot act!");
                await Task.Delay(GetCombatDelay(1500));
                return (new CombatAction { Type = CombatActionType.None }, false);
            }

            // Show action menu (screen reader compatible or standard)
            bool hasInjuredTeammates = currentTeammates?.Any(t => t.IsAlive && t.HP < t.MaxHP) ?? false;
            bool canHealAlly = hasInjuredTeammates && (player.Healing > 0 || (ClassAbilitySystem.IsSpellcaster(player.Class) && player.Mana > 0));
            var classInfo = GetClassSpecificActions(player);

            if (DoorMode.IsInDoorMode)
            {
                ShowDungeonCombatMenuBBS(player, hasInjuredTeammates, canHealAlly, classInfo);
            }
            else if (player.ScreenReaderMode)
            {
                ShowDungeonCombatMenuScreenReader(player, hasInjuredTeammates, canHealAlly, classInfo);
            }
            else
            {
                ShowDungeonCombatMenuStandard(player, hasInjuredTeammates, canHealAlly, classInfo);
            }

            // Show combat tip occasionally (skip in BBS mode to save lines)
            if (!DoorMode.IsInDoorMode)
                ShowCombatTipIfNeeded(player);

            terminal.SetColor("white");
            terminal.Write("Choose action: ");

            var input = await terminal.GetInput("");
            var action = new CombatAction();

            switch (input.Trim().ToUpper())
            {
                case "A":
                    action.Type = CombatActionType.Attack;
                    // Get target selection
                    action.TargetIndex = await GetTargetSelection(monsters, allowRandom: true);
                    return (action, false);

                case "D":
                    action.Type = CombatActionType.Defend;
                    return (action, false);

                case "I":
                    // Check if player can use any potions
                    bool hasHealPots = player.Healing > 0 && player.HP < player.MaxHP;
                    bool hasManaPots = player.ManaPotions > 0 && player.Mana < player.MaxMana;
                    if (!hasHealPots && !hasManaPots)
                    {
                        terminal.WriteLine("You have no usable potions!", "yellow");
                        await Task.Delay(GetCombatDelay(800));
                        continue;
                    }
                    action.Type = CombatActionType.UseItem;
                    return (action, false);

                case "H":
                    // Heal ally - give potion or cast heal spell on teammate
                    var healAllyResult = await HandleHealAlly(player, monsters);
                    if (healAllyResult != null)
                    {
                        return (healAllyResult, false);
                    }
                    // Player cancelled - loop back
                    continue;

                case "V":
                    if (BossContext?.CanSave == true)
                    {
                        action.Type = CombatActionType.BossSave;
                        return (action, false);
                    }
                    terminal.WriteLine("Invalid action, please try again", "yellow");
                    await Task.Delay(GetCombatDelay(1000));
                    continue;

                case "R":
                    action.Type = CombatActionType.Retreat;
                    return (action, false);

                case "AUTO":
                    // Enable auto-combat mode
                    terminal.SetColor("bright_cyan");
                    terminal.WriteLine("");
                    terminal.WriteLine("Auto-combat enabled! You will automatically attack each round.");
                    terminal.WriteLine("Combat will pause if you take manual control.");
                    terminal.WriteLine("");
                    await Task.Delay(GetCombatDelay(1500));

                    // Return an attack action for this round AND enable auto-combat
                    action.Type = CombatActionType.Attack;
                    action.TargetIndex = null; // Random target
                    return (action, true);

                case "SPD":
                    // Cycle combat speed: Normal -> Fast -> Instant -> Normal
                    player.CombatSpeed = player.CombatSpeed switch
                    {
                        CombatSpeed.Normal => CombatSpeed.Fast,
                        CombatSpeed.Fast => CombatSpeed.Instant,
                        _ => CombatSpeed.Normal
                    };
                    string newSpeedName = player.CombatSpeed switch
                    {
                        CombatSpeed.Instant => "Instant (no delays)",
                        CombatSpeed.Fast => "Fast (50% delays)",
                        _ => "Normal (full delays)"
                    };
                    terminal.SetColor("gray");
                    terminal.WriteLine($"Combat speed set to: {newSpeedName}");
                    terminal.WriteLine("");
                    await Task.Delay(GetCombatDelay(500));
                    continue; // Show menu again

                // Quickbar slots (1-9) - spells and abilities
                case "1":
                case "2":
                case "3":
                case "4":
                case "5":
                case "6":
                case "7":
                case "8":
                case "9":
                    var qbAction = await HandleQuickbarAction(player, input.Trim(), monsters);
                    if (qbAction.HasValue)
                    {
                        action.Type = qbAction.Value.type;
                        action.TargetIndex = qbAction.Value.target;
                        action.AbilityId = qbAction.Value.abilityId;
                        action.SpellIndex = qbAction.Value.spellIndex;
                        action.AllyTargetIndex = qbAction.Value.allyTarget;
                        action.TargetAllMonsters = qbAction.Value.targetAll;
                        return (action, false);
                    }
                    continue; // Invalid or cancelled, show menu again

                default:
                    terminal.WriteLine("Invalid action, please try again", "yellow");
                    await Task.Delay(GetCombatDelay(1000));
                    terminal.WriteLine("");
                    continue;  // Loop back to ask again
            }
        }
    }

    /// <summary>
    /// Process player action in multi-monster combat
    /// </summary>
    private async Task ProcessPlayerActionMultiMonster(CombatAction action, Character player, List<Monster> monsters, CombatResult result)
    {
        switch (action.Type)
        {
            case CombatActionType.Attack:
                Monster target = null;
                if (action.TargetIndex.HasValue)
                {
                    target = monsters[action.TargetIndex.Value];
                }
                else
                {
                    // Random target
                    target = GetRandomLivingMonster(monsters);
                }

                if (target != null && target.IsAlive)
                {
                    // Get attack count (includes dual-wield bonus)
                    int swings = GetAttackCount(player);
                    int baseSwings = 1 + player.GetClassCombatModifiers().ExtraAttacks;

                    for (int s = 0; s < swings && target.IsAlive; s++)
                    {
                        bool isOffHandAttack = player.IsDualWielding && s >= baseSwings;

                        terminal.WriteLine("");
                        terminal.SetColor("bright_green");
                        if (isOffHandAttack)
                        {
                            terminal.WriteLine($"Off-hand strike at {target.Name}!");
                        }
                        else
                        {
                            terminal.WriteLine($"You attack {target.Name}!");
                        }
                        await Task.Delay(GetCombatDelay(500));

                        // Calculate player attack damage
                        long attackPower = player.Strength + player.WeapPow + random.Next(1, 16);

                        // Apply temporary attack bonus from abilities
                        attackPower += player.TempAttackBonus;

                        // Apply weapon configuration damage modifier
                        double damageModifier = GetWeaponConfigDamageModifier(player, isOffHandAttack);
                        attackPower = (long)(attackPower * damageModifier);

                        long damage = Math.Max(1, attackPower);
                        await ApplySingleMonsterDamage(target, damage, result, isOffHandAttack ? "off-hand strike" : "your attack", player);
                    }
                }
                break;

            case CombatActionType.Defend:
                terminal.WriteLine("");
                terminal.SetColor("cyan");
                terminal.WriteLine("You take a defensive stance!");
                // Add defending status manually (Character doesn't have AddStatusEffect)
                if (!player.ActiveStatuses.ContainsKey(StatusEffect.Defending))
                {
                    player.ActiveStatuses[StatusEffect.Defending] = 1;
                }
                await Task.Delay(GetCombatDelay(1000));
                break;

            case CombatActionType.CastSpell:
                await ExecuteSpellMultiMonster(player, monsters, action, result);
                break;

            case CombatActionType.Heal:
                await ExecuteHeal(player, result, false);
                break;

            case CombatActionType.QuickHeal:
                await ExecuteHeal(player, result, true);
                break;

            case CombatActionType.UseItem:
                await ExecuteUseItem(player, result);
                break;

            case CombatActionType.Retreat:
                // Check if fleeing is allowed on current difficulty
                if (!DifficultySystem.CanFlee())
                {
                    terminal.WriteLine("");
                    terminal.SetColor("bright_red");
                    terminal.WriteLine("NIGHTMARE MODE: There is no escape. Fight or die!");
                    await Task.Delay(GetCombatDelay(1500));
                    break;
                }

                // Smoke bomb: guaranteed escape from non-boss combat
                if (player.SmokeBombs > 0 && BossContext == null)
                {
                    player.SmokeBombs--;
                    terminal.WriteLine("");
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine("You throw a smoke bomb and vanish into the haze!");
                    globalEscape = true;
                    await Task.Delay(GetCombatDelay(1500));
                    break;
                }

                // Boss fights have flat 20% flee chance; normal combat is dex-based
                int retreatChance;
                if (BossContext != null)
                    retreatChance = 20;
                else
                    retreatChance = (int)(player.Dexterity / 2);

                // Shadows faction escape bonus
                retreatChance += FactionSystem.Instance?.GetEscapeChanceBonus() ?? 0;
                retreatChance = Math.Min(75, retreatChance);

                int retreatRoll = random.Next(1, 101);

                terminal.WriteLine("");
                if (retreatRoll <= retreatChance)
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine("You successfully retreat from combat!");
                    globalEscape = true;
                }
                else
                {
                    terminal.SetColor("red");
                    if (BossContext != null)
                    {
                        var bossName = monsters.FirstOrDefault(m => m.IsBoss)?.Name ?? "The Old God";
                        terminal.WriteLine($"{bossName} blocks your escape!");
                    }
                    else
                    {
                        terminal.WriteLine("You failed to retreat!");
                    }
                }
                await Task.Delay(GetCombatDelay(1500));
                break;

            case CombatActionType.Backstab:
                await ExecuteBackstabMultiMonster(player, monsters, action.TargetIndex, result);
                break;

            case CombatActionType.PowerAttack:
                await ExecutePowerAttackMultiMonster(player, monsters, action.TargetIndex, result);
                break;

            case CombatActionType.PreciseStrike:
                await ExecutePreciseStrikeMultiMonster(player, monsters, action.TargetIndex, result);
                break;

            case CombatActionType.Rage:
                await ExecuteRageMultiMonster(player, result);
                break;

            case CombatActionType.Hide:
                await ExecuteHideMultiMonster(player, result);
                break;

            case CombatActionType.SoulStrike:
                await ExecuteSoulStrikeMultiMonster(player, monsters, action.TargetIndex, result);
                break;

            case CombatActionType.Smite:
                await ExecuteSmiteMultiMonster(player, monsters, action.TargetIndex, result);
                break;

            case CombatActionType.RangedAttack:
                await ExecuteRangedAttackMultiMonster(player, monsters, action.TargetIndex, result);
                break;

            case CombatActionType.Disarm:
                await ExecuteDisarmMultiMonster(player, monsters, action.TargetIndex, result);
                break;

            case CombatActionType.Taunt:
                await ExecuteTauntMultiMonster(player, monsters, action.TargetIndex, result);
                break;

            case CombatActionType.UseAbility:
            case CombatActionType.ClassAbility:
                await ExecuteUseAbilityMultiMonster(player, monsters, action, result);
                break;

            case CombatActionType.BossSave:
                if (BossContext != null)
                {
                    var bossTarget = monsters.FirstOrDefault(m => m.IsBoss && m.IsAlive);
                    if (bossTarget != null && await AttemptBossSave(player, bossTarget, result))
                    {
                        BossContext.BossSaved = true;
                        bossTarget.HP = 0; // End combat peacefully
                    }
                }
                break;

            case CombatActionType.None:
                // Player couldn't act (stunned, etc.) - already handled in GetPlayerActionMultiMonster
                break;
        }
    }

    // ==================== CLASS-SPECIFIC ABILITY IMPLEMENTATIONS ====================

    private async Task ExecuteBackstabMultiMonster(Character player, List<Monster> monsters, int? targetIndex, CombatResult result)
    {
        var target = targetIndex.HasValue ? monsters[targetIndex.Value] : GetRandomLivingMonster(monsters);
        if (target == null || !target.IsAlive) return;

        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"You slip into the shadows and strike at {target.Name}!");
        await Task.Delay(GetCombatDelay(500));

        // Backstab: 3x damage, dexterity-based success
        // Drug DexterityBonus adds to effective DEX (e.g., QuickSilver: +20)
        var drugEffects = DrugSystem.GetDrugEffects(player);
        long effectiveDex = player.Dexterity + drugEffects.DexterityBonus;
        int successChance = Math.Min(95, 50 + (int)(effectiveDex / 2));
        if (random.Next(100) < successChance)
        {
            long backstabDamage = (player.Strength + player.WeapPow) * 3;
            backstabDamage += random.Next(1, 20);
            backstabDamage = DifficultySystem.ApplyPlayerDamageMultiplier(backstabDamage);

            terminal.SetColor("bright_red");
            terminal.WriteLine($"CRITICAL BACKSTAB! You deal {backstabDamage} damage!");

            await ApplySingleMonsterDamage(target, backstabDamage, result, "backstab", player);
        }
        else
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("Your backstab attempt fails - the enemy noticed you!");
            await Task.Delay(GetCombatDelay(1000));
        }
    }

    private async Task ExecutePowerAttackMultiMonster(Character player, List<Monster> monsters, int? targetIndex, CombatResult result)
    {
        var target = targetIndex.HasValue ? monsters[targetIndex.Value] : GetRandomLivingMonster(monsters);
        if (target == null || !target.IsAlive) return;

        terminal.WriteLine("");
        terminal.SetColor("bright_red");
        terminal.WriteLine($"You wind up for a powerful strike at {target.Name}!");
        await Task.Delay(GetCombatDelay(500));

        // Power Attack: +50% damage
        long powerDamage = (long)((player.Strength + player.WeapPow) * 1.5);
        powerDamage += random.Next(5, 25);
        powerDamage = DifficultySystem.ApplyPlayerDamageMultiplier(powerDamage);

        terminal.SetColor("bright_red");
        terminal.WriteLine($"POWER ATTACK! You deal {powerDamage} damage!");

        await ApplySingleMonsterDamage(target, powerDamage, result, "power attack", player);
    }

    private async Task ExecutePreciseStrikeMultiMonster(Character player, List<Monster> monsters, int? targetIndex, CombatResult result)
    {
        var target = targetIndex.HasValue ? monsters[targetIndex.Value] : GetRandomLivingMonster(monsters);
        if (target == null || !target.IsAlive) return;

        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"You carefully aim at {target.Name}'s weak point!");
        await Task.Delay(GetCombatDelay(500));

        // Precise Strike: normal damage but ignores 50% armor
        long damage = player.Strength + player.WeapPow + random.Next(1, 15);
        damage = DifficultySystem.ApplyPlayerDamageMultiplier(damage);

        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"PRECISE STRIKE! You deal {damage} damage (armor-piercing)!");

        // Apply damage directly, bypassing some defense
        long actualDamage = Math.Max(1, damage - (target.ArmPow / 2));
        target.HP = Math.Max(0, target.HP - actualDamage);

        terminal.SetColor("white");
        terminal.WriteLine($"{target.Name} takes {actualDamage} damage!");

        if (target.HP <= 0)
        {
            target.HP = 0;
            var deathMessage = CombatMessages.GetDeathMessage(target.Name, target.MonsterColor);
            terminal.WriteLine(deathMessage);
            result.DefeatedMonsters.Add(target);
        }
        await Task.Delay(GetCombatDelay(800));
    }

    private async Task ExecuteRageMultiMonster(Character player, CombatResult result)
    {
        terminal.WriteLine("");
        terminal.SetColor("bright_red");
        terminal.WriteLine("RAAAAAGE! You enter a berserker fury!");
        terminal.WriteLine("Damage doubled, but defense reduced!");

        player.IsRaging = true;
        player.ApplyStatus(StatusEffect.Raging, 5); // Lasts 5 rounds

        await Task.Delay(GetCombatDelay(1500));
    }

    private async Task ExecuteHideMultiMonster(Character player, CombatResult result)
    {
        terminal.WriteLine("");

        // Hide success based on dexterity
        int hideChance = Math.Min(90, 40 + (int)(player.Dexterity / 2));
        if (random.Next(100) < hideChance)
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine("You slip into the shadows, hidden from view...");
            terminal.WriteLine("Your next attack will deal bonus damage!");

            player.ApplyStatus(StatusEffect.Hidden, 2);
        }
        else
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("You try to hide but the enemies spot you!");
        }

        await Task.Delay(GetCombatDelay(1000));
    }

    private async Task ExecuteSoulStrikeMultiMonster(Character player, List<Monster> monsters, int? targetIndex, CombatResult result)
    {
        var target = targetIndex.HasValue ? monsters[targetIndex.Value] : GetRandomLivingMonster(monsters);
        if (target == null || !target.IsAlive) return;

        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"You channel holy power against {target.Name}!");
        await Task.Delay(GetCombatDelay(500));

        // Soul Strike: Chivalry-based holy damage
        long holyDamage = (player.Chivalry / 10) + (player.Level * 5) + random.Next(10, 30);
        holyDamage = DifficultySystem.ApplyPlayerDamageMultiplier(holyDamage);

        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"SOUL STRIKE! Holy fire deals {holyDamage} damage!");

        await ApplySingleMonsterDamage(target, holyDamage, result, "soul strike", player);
    }

    private async Task ExecuteSmiteMultiMonster(Character player, List<Monster> monsters, int? targetIndex, CombatResult result)
    {
        var target = targetIndex.HasValue ? monsters[targetIndex.Value] : GetRandomLivingMonster(monsters);
        if (target == null || !target.IsAlive) return;

        terminal.WriteLine("");
        terminal.SetColor("bright_white");
        terminal.WriteLine($"You call upon divine wrath against {target.Name}!");
        await Task.Delay(GetCombatDelay(500));

        // Smite: 150% damage + level bonus
        long smiteDamage = (long)((player.Strength + player.WeapPow) * 1.5) + player.Level;
        smiteDamage += random.Next(10, 25);
        smiteDamage = DifficultySystem.ApplyPlayerDamageMultiplier(smiteDamage);

        terminal.SetColor("bright_white");
        terminal.WriteLine($"SMITE! Divine power deals {smiteDamage} damage!");

        await ApplySingleMonsterDamage(target, smiteDamage, result, "smite", player);
    }

    private async Task ExecuteRangedAttackMultiMonster(Character player, List<Monster> monsters, int? targetIndex, CombatResult result)
    {
        var target = targetIndex.HasValue ? monsters[targetIndex.Value] : GetRandomLivingMonster(monsters);
        if (target == null || !target.IsAlive) return;

        terminal.WriteLine("");
        terminal.SetColor("green");
        terminal.WriteLine($"You fire at {target.Name} from a distance!");
        await Task.Delay(GetCombatDelay(500));

        // Ranged: Dexterity-based damage
        long rangedDamage = (player.Dexterity / 2) + random.Next(5, 15);
        rangedDamage = DifficultySystem.ApplyPlayerDamageMultiplier(rangedDamage);

        terminal.SetColor("green");
        terminal.WriteLine($"Your arrow strikes for {rangedDamage} damage!");

        await ApplySingleMonsterDamage(target, rangedDamage, result, "ranged attack", player);
    }

    private async Task ExecuteDisarmMultiMonster(Character player, List<Monster> monsters, int? targetIndex, CombatResult result)
    {
        var target = targetIndex.HasValue ? monsters[targetIndex.Value] : GetRandomLivingMonster(monsters);
        if (target == null || !target.IsAlive) return;

        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine($"You attempt to disarm {target.Name}!");
        await Task.Delay(GetCombatDelay(500));

        // Disarm: Dexterity vs monster strength
        int disarmChance = Math.Max(10, 50 + (int)(player.Dexterity - target.Strength) / 2);
        if (random.Next(100) < disarmChance)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine($"SUCCESS! {target.Name}'s weapon power is reduced!");
            target.WeapPow = Math.Max(0, target.WeapPow - 5);
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine("Your disarm attempt fails!");
        }

        await Task.Delay(GetCombatDelay(1000));
    }

    private async Task ExecuteTauntMultiMonster(Character player, List<Monster> monsters, int? targetIndex, CombatResult result)
    {
        var target = targetIndex.HasValue ? monsters[targetIndex.Value] : GetRandomLivingMonster(monsters);
        if (target == null || !target.IsAlive) return;

        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine($"You taunt {target.Name} mercilessly!");
        await Task.Delay(GetCombatDelay(500));

        // Taunt: Lower enemy defense
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"{target.Name} becomes enraged and lowers their guard!");
        target.Defence = Math.Max(0, target.Defence - 3);
        target.ArmPow = Math.Max(0, target.ArmPow - 2);

        await Task.Delay(GetCombatDelay(1000));
    }

    /// <summary>
    /// Execute class ability in multi-monster combat
    /// This is the main entry point for learned abilities from ClassAbilitySystem
    /// </summary>
    private async Task ExecuteUseAbilityMultiMonster(Character player, List<Monster> monsters, CombatAction action, CombatResult result)
    {
        // If we have a specific ability ID, use it directly
        if (!string.IsNullOrEmpty(action.AbilityId))
        {
            var ability = ClassAbilitySystem.GetAbility(action.AbilityId);
            if (ability == null)
            {
                terminal.WriteLine("Unknown ability!", "red");
                await Task.Delay(GetCombatDelay(1000));
                return;
            }

            // Check stamina
            if (!player.HasEnoughStamina(ability.StaminaCost))
            {
                terminal.WriteLine($"Not enough stamina! Need {ability.StaminaCost}, have {player.CurrentCombatStamina}.", "red");
                await Task.Delay(GetCombatDelay(1000));
                return;
            }

            // Check cooldown
            if (abilityCooldowns.TryGetValue(action.AbilityId, out int cd) && cd > 0)
            {
                terminal.WriteLine($"{ability.Name} is on cooldown for {cd} more rounds!", "red");
                await Task.Delay(GetCombatDelay(1000));
                return;
            }

            // Spend stamina
            player.SpendStamina(ability.StaminaCost);

            // Execute the ability
            var abilityResult = ClassAbilitySystem.UseAbility(player, action.AbilityId, random);

            // Display ability use
            terminal.WriteLine("");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"» {player.Name2} uses {ability.Name}! (-{ability.StaminaCost} stamina)");
            terminal.SetColor("gray");
            terminal.WriteLine($"  (Stamina: {player.CurrentCombatStamina}/{player.MaxCombatStamina})");
            await Task.Delay(GetCombatDelay(500));

            // Get target for damage abilities
            Monster target = null;
            if (action.TargetIndex.HasValue && action.TargetIndex.Value < monsters.Count)
            {
                target = monsters[action.TargetIndex.Value];
            }
            else
            {
                target = GetRandomLivingMonster(monsters);
            }

            // Apply ability effects
            await ApplyAbilityEffectsMultiMonster(player, target, monsters, abilityResult, result);

            // Set cooldown
            if (abilityResult.CooldownApplied > 0)
            {
                abilityCooldowns[action.AbilityId] = abilityResult.CooldownApplied;
                terminal.SetColor("gray");
                terminal.WriteLine($"  ({ability.Name} cooldown: {abilityResult.CooldownApplied} rounds)");
            }

            // Display training improvement message if ability proficiency increased
            if (abilityResult.SkillImproved && !string.IsNullOrEmpty(abilityResult.NewProficiencyLevel))
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"  Your {ability.Name} proficiency improved to {abilityResult.NewProficiencyLevel}!");
                await Task.Delay(GetCombatDelay(800));
            }

            // Log the action
            result.CombatLog.Add($"{player.DisplayName} uses {ability.Name}");

            await Task.Delay(GetCombatDelay(800));
        }
        else
        {
            // No specific ability - show menu (fallback to old behavior)
            await ShowAbilityMenuAndExecute(player, monsters, result);
        }
    }

    /// <summary>
    /// Apply ability effects in multi-monster combat
    /// </summary>
    private async Task ApplyAbilityEffectsMultiMonster(Character player, Monster target, List<Monster> monsters, ClassAbilityResult abilityResult, CombatResult result)
    {
        var ability = abilityResult.AbilityUsed;
        if (ability == null) return;

        // Determine if this is the player or an AI teammate for message formatting
        bool isPlayer = (player == currentPlayer);
        string actorName = isPlayer ? "You" : player.DisplayName;

        // Apply damage
        if (abilityResult.Damage > 0 && target != null && target.IsAlive)
        {
            long actualDamage = abilityResult.Damage;

            // Handle special damage effects
            if (abilityResult.SpecialEffect == "execute" && target.HP < target.MaxHP * 0.3)
            {
                actualDamage *= 2;
                terminal.SetColor("bright_red");
                terminal.WriteLine("EXECUTION! Double damage to wounded enemy!");
            }
            else if (abilityResult.SpecialEffect == "last_stand" && player.HP < player.MaxHP * 0.25)
            {
                actualDamage = (long)(actualDamage * 1.5);
                terminal.SetColor("bright_red");
                terminal.WriteLine(isPlayer
                    ? "LAST STAND! Desperation fuels your attack!"
                    : $"LAST STAND! Desperation fuels {actorName}'s attack!");
            }
            else if (abilityResult.SpecialEffect == "armor_pierce")
            {
                terminal.SetColor("green");
                terminal.WriteLine("The attack ignores armor!");
            }
            else if (abilityResult.SpecialEffect == "backstab")
            {
                actualDamage = (long)(actualDamage * 1.5);
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("Critical strike from the shadows!");
            }
            else if (abilityResult.SpecialEffect == "aoe")
            {
                // AoE abilities hit all monsters
                terminal.SetColor("bright_red");
                terminal.WriteLine("The attack strikes all enemies!");
                await ApplyAoEDamage(monsters, actualDamage, result, ability.Name);
                // Skip single target damage since we did AoE
                actualDamage = 0;
            }

            // Apply single target damage (unless AoE)
            if (actualDamage > 0)
            {
                // Apply defense unless armor_pierce
                if (abilityResult.SpecialEffect != "armor_pierce")
                {
                    long defense = target.Defence / 2; // Abilities partially bypass defense
                    actualDamage = Math.Max(1, actualDamage - defense);
                }

                target.HP -= actualDamage;
                result.TotalDamageDealt += actualDamage;

                // Track damage dealt statistics
                result.Player?.Statistics.RecordDamageDealt(actualDamage, false);

                terminal.SetColor("bright_red");
                terminal.WriteLine(isPlayer
                    ? $"You deal {actualDamage} damage to {target.Name}!"
                    : $"{actorName} deals {actualDamage} damage to {target.Name}!");

                if (target.HP <= 0)
                {
                    target.HP = 0;
                    terminal.SetColor("bright_green");
                    terminal.WriteLine($"{target.Name} is slain!");
                    result.DefeatedMonsters.Add(target);
                }
            }
        }

        // Apply healing
        if (abilityResult.Healing > 0)
        {
            long actualHealing = Math.Min(abilityResult.Healing, player.MaxHP - player.HP);
            player.HP += actualHealing;

            terminal.SetColor("bright_green");
            terminal.WriteLine(isPlayer
                ? $"You recover {actualHealing} HP!"
                : $"{actorName} recovers {actualHealing} HP!");
        }

        // Apply buffs
        if (abilityResult.AttackBonus > 0)
        {
            player.TempAttackBonus = abilityResult.AttackBonus;
            player.TempAttackBonusDuration = abilityResult.Duration;
            terminal.SetColor("cyan");
            terminal.WriteLine(isPlayer
                ? $"Attack increased by {abilityResult.AttackBonus} for {abilityResult.Duration} rounds!"
                : $"{actorName}'s attack increased by {abilityResult.AttackBonus} for {abilityResult.Duration} rounds!");
        }

        if (abilityResult.DefenseBonus > 0)
        {
            player.TempDefenseBonus = abilityResult.DefenseBonus;
            player.TempDefenseBonusDuration = abilityResult.Duration;
            terminal.SetColor("cyan");
            terminal.WriteLine(isPlayer
                ? $"Defense increased by {abilityResult.DefenseBonus} for {abilityResult.Duration} rounds!"
                : $"{actorName}'s defense increased by {abilityResult.DefenseBonus} for {abilityResult.Duration} rounds!");
        }
        else if (abilityResult.DefenseBonus < 0)
        {
            // Rage reduces defense
            player.TempDefenseBonus = abilityResult.DefenseBonus;
            player.TempDefenseBonusDuration = abilityResult.Duration;
            terminal.SetColor("yellow");
            terminal.WriteLine(isPlayer
                ? $"Defense reduced by {-abilityResult.DefenseBonus} (rage)!"
                : $"{actorName}'s defense reduced by {-abilityResult.DefenseBonus} (rage)!");
        }

        // Handle special effects
        switch (abilityResult.SpecialEffect)
        {
            case "escape":
                terminal.SetColor("magenta");
                terminal.WriteLine(isPlayer
                    ? "You vanish in a puff of smoke!"
                    : $"{actorName} vanishes in a puff of smoke!");
                if (isPlayer) globalEscape = true;
                break;

            case "stun":
                if (target != null && target.IsAlive && random.Next(100) < 60)
                {
                    target.Stunned = true;
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"{target.Name} is stunned!");
                }
                break;

            case "poison":
                if (target != null && target.IsAlive)
                {
                    target.Poisoned = true;
                    terminal.SetColor("green");
                    terminal.WriteLine($"{target.Name} is poisoned!");
                }
                break;

            case "distract":
                if (target != null && target.IsAlive)
                {
                    target.Distracted = true;
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"{target.Name} is distracted and will have reduced accuracy!");
                }
                break;

            case "charm":
                if (target != null && target.IsAlive && random.Next(100) < 40)
                {
                    target.Charmed = true;
                    terminal.SetColor("magenta");
                    terminal.WriteLine($"{target.Name} is charmed and may hesitate to attack!");
                }
                break;

            case "smoke":
                terminal.SetColor("gray");
                terminal.WriteLine(isPlayer
                    ? "A cloud of smoke obscures you from attack!"
                    : $"A cloud of smoke obscures {actorName} from attack!");
                player.TempDefenseBonus += 40;
                player.TempDefenseBonusDuration = Math.Max(player.TempDefenseBonusDuration, 2);
                break;

            case "rage":
                player.IsRaging = true;
                terminal.SetColor("bright_red");
                terminal.WriteLine(isPlayer
                    ? "BERSERKER RAGE! You enter a blood fury!"
                    : $"BERSERKER RAGE! {actorName} enters a blood fury!");
                break;

            case "dodge_next":
                player.DodgeNextAttack = true;
                terminal.SetColor("cyan");
                terminal.WriteLine(isPlayer
                    ? "You prepare to dodge the next attack!"
                    : $"{actorName} prepares to dodge the next attack!");
                break;

            case "inspire":
                if (result.Teammates != null && result.Teammates.Count > 0)
                {
                    foreach (var teammate in result.Teammates.Where(t => t.IsAlive))
                    {
                        teammate.TempAttackBonus += 15;
                        teammate.TempAttackBonusDuration = Math.Max(teammate.TempAttackBonusDuration, 3);
                    }
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine(isPlayer
                        ? "Your allies are inspired! (+15 Attack for 3 rounds)"
                        : $"{actorName}'s melody inspires the party! (+15 Attack for 3 rounds)");
                }
                else
                {
                    player.TempAttackBonus += 10;
                    player.TempAttackBonusDuration = Math.Max(player.TempAttackBonusDuration, 3);
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine(isPlayer
                        ? "The melody steels your resolve! (+10 Attack for 3 rounds)"
                        : $"The melody steels {actorName}'s resolve! (+10 Attack for 3 rounds)");
                }
                break;

            case "resist_all":
                player.HasStatusImmunity = true;
                player.StatusImmunityDuration = abilityResult.Duration > 0 ? abilityResult.Duration : 3;
                terminal.SetColor("bright_white");
                terminal.WriteLine(isPlayer
                    ? "Your will becomes unbreakable! Status effects cannot touch you!"
                    : $"{actorName}'s will becomes unbreakable! Status effects cannot touch them!");
                break;

            // === DAMAGE ENHANCEMENT EFFECTS ===

            case "reckless":
                player.TempDefenseBonus -= 20;
                player.TempDefenseBonusDuration = Math.Max(player.TempDefenseBonusDuration, 1);
                terminal.SetColor("bright_red");
                terminal.WriteLine(isPlayer
                    ? "Reckless swing! You leave yourself exposed!"
                    : $"Reckless swing! {actorName} leaves themselves exposed!");
                break;

            case "desperate":
                if (player.HP < player.MaxHP * 0.4 && target != null && target.IsAlive)
                {
                    long desperateDmg = (long)(abilityResult.Damage * 0.5);
                    desperateDmg = Math.Max(1, desperateDmg - target.Defence / 4);
                    target.HP -= desperateDmg;
                    result.TotalDamageDealt += desperateDmg;
                    result.Player?.Statistics.RecordDamageDealt(desperateDmg, false);
                    terminal.SetColor("bright_red");
                    terminal.WriteLine($"DESPERATION! {desperateDmg} bonus damage!");
                    if (target.HP <= 0)
                    {
                        target.HP = 0;
                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"{target.Name} is slain!");
                        if (!result.DefeatedMonsters.Contains(target))
                            result.DefeatedMonsters.Add(target);
                    }
                }
                break;

            case "fire":
                if (target != null && target.IsAlive)
                {
                    bool fireVulnerable = target.MonsterClass == MonsterClass.Plant || target.MonsterClass == MonsterClass.Beast;
                    if (fireVulnerable)
                    {
                        long fireBonusDmg = abilityResult.Damage / 2;
                        target.HP -= fireBonusDmg;
                        result.TotalDamageDealt += fireBonusDmg;
                        result.Player?.Statistics.RecordDamageDealt(fireBonusDmg, false);
                        terminal.SetColor("bright_red");
                        terminal.WriteLine($"The fire burns intensely! {fireBonusDmg} bonus damage!");
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine("The bomb explodes in a burst of flame!");
                    }
                    target.Poisoned = true;
                    target.PoisonRounds = Math.Max(target.PoisonRounds, 2);
                }
                break;

            case "holy":
                if (target != null && target.IsAlive &&
                    (target.MonsterClass == MonsterClass.Undead || target.MonsterClass == MonsterClass.Demon || target.Undead > 0))
                {
                    long holyBonusDmg = abilityResult.Damage;
                    target.HP -= holyBonusDmg;
                    result.TotalDamageDealt += holyBonusDmg;
                    result.Player?.Statistics.RecordDamageDealt(holyBonusDmg, false);
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine($"HOLY SMITE! The undead burns with divine fire! +{holyBonusDmg} damage!");
                    if (target.HP <= 0)
                    {
                        target.HP = 0;
                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"{target.Name} is purified!");
                        if (!result.DefeatedMonsters.Contains(target))
                            result.DefeatedMonsters.Add(target);
                    }
                }
                else
                {
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine(isPlayer
                        ? "Divine energy radiates from your weapon!"
                        : $"Divine energy radiates from {actorName}'s weapon!");
                }
                break;

            case "holy_avenger":
                if (target != null && target.IsAlive &&
                    (target.MonsterClass == MonsterClass.Undead || target.MonsterClass == MonsterClass.Demon || target.Undead > 0))
                {
                    long avengerBonusDmg = (long)(abilityResult.Damage * 0.75);
                    target.HP -= avengerBonusDmg;
                    result.TotalDamageDealt += avengerBonusDmg;
                    result.Player?.Statistics.RecordDamageDealt(avengerBonusDmg, false);
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine($"DIVINE VENGEANCE! +{avengerBonusDmg} holy damage!");
                    if (target.HP <= 0)
                    {
                        target.HP = 0;
                        if (!result.DefeatedMonsters.Contains(target))
                            result.DefeatedMonsters.Add(target);
                    }
                }
                {
                    long avengerHeal = Math.Min(player.Level * 2, player.MaxHP - player.HP);
                    if (avengerHeal > 0)
                    {
                        player.HP += avengerHeal;
                        terminal.SetColor("bright_green");
                        terminal.WriteLine(isPlayer
                            ? $"Divine energy heals you for {avengerHeal} HP!"
                            : $"Divine energy heals {actorName} for {avengerHeal} HP!");
                    }
                }
                break;

            case "critical":
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(isPlayer
                    ? "You strike a vital point!"
                    : $"{actorName} strikes a vital point!");
                break;

            case "fury":
                player.IsRaging = true;
                if (!player.ActiveStatuses.ContainsKey(StatusEffect.Raging))
                    player.ActiveStatuses[StatusEffect.Raging] = abilityResult.Duration > 0 ? abilityResult.Duration : 3;
                terminal.SetColor("bright_red");
                terminal.WriteLine(isPlayer
                    ? "THE WAR GOD'S FURY BURNS WITHIN YOU!"
                    : $"THE WAR GOD'S FURY BURNS WITHIN {actorName.ToUpper()}!");
                break;

            case "champion":
                {
                    long champHeal = Math.Min(player.Level * 3, player.MaxHP - player.HP);
                    if (champHeal > 0)
                    {
                        player.HP += champHeal;
                        terminal.SetColor("bright_green");
                        terminal.WriteLine(isPlayer
                            ? $"The Champion's spirit heals you for {champHeal} HP!"
                            : $"The Champion's spirit heals {actorName} for {champHeal} HP!");
                    }
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine("A strike worthy of legends!");
                }
                break;

            // === AOE EFFECTS ===

            case "aoe_holy":
                {
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine("Divine judgment rains down upon all enemies!");
                    var livingMonsHoly = monsters.Where(m => m.IsAlive).ToList();
                    foreach (var m in livingMonsHoly)
                    {
                        long holyAoeDmg = abilityResult.Damage / Math.Max(1, livingMonsHoly.Count);
                        if (m.MonsterClass == MonsterClass.Undead || m.MonsterClass == MonsterClass.Demon || m.Undead > 0)
                            holyAoeDmg = (long)(holyAoeDmg * 1.5);
                        long actualHolyDmg = Math.Max(1, holyAoeDmg - m.ArmPow / 2);
                        if (m.IsSleeping) { m.IsSleeping = false; m.SleepDuration = 0; }
                        m.HP -= actualHolyDmg;
                        result.Player?.Statistics.RecordDamageDealt(actualHolyDmg, false);
                        result.TotalDamageDealt += actualHolyDmg;
                        bool isUndead = m.MonsterClass == MonsterClass.Undead || m.Undead > 0;
                        terminal.SetColor("yellow");
                        terminal.Write($"  {m.Name}: ");
                        terminal.SetColor("bright_yellow");
                        terminal.WriteLine($"-{actualHolyDmg} HP{(isUndead ? " (HOLY!)" : "")}");
                        if (m.HP <= 0)
                        {
                            m.HP = 0;
                            terminal.SetColor("bright_green");
                            terminal.WriteLine($"  {m.Name} is purified!");
                            if (!result.DefeatedMonsters.Contains(m))
                                result.DefeatedMonsters.Add(m);
                        }
                    }
                }
                break;

            // === CROWD CONTROL EFFECTS ===

            case "fear":
                if (target != null && target.IsAlive)
                {
                    int fearChance = target.IsBoss ? 35 : 70;
                    if (random.Next(100) < fearChance)
                    {
                        target.IsFeared = true;
                        target.FearDuration = abilityResult.Duration > 0 ? abilityResult.Duration : 3;
                        terminal.SetColor("bright_red");
                        terminal.WriteLine(isPlayer
                            ? $"{target.Name} is terrified by your roar!"
                            : $"{target.Name} is terrified by {actorName}'s roar!");
                    }
                    else
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine(isPlayer
                            ? $"{target.Name} resists your intimidation!"
                            : $"{target.Name} resists {actorName}'s intimidation!");
                    }
                }
                break;

            case "confusion":
                if (target != null && target.IsAlive)
                {
                    int confuseChance = target.IsBoss ? 30 : 65;
                    if (random.Next(100) < confuseChance)
                    {
                        target.IsConfused = true;
                        target.ConfusedDuration = 2;
                        terminal.SetColor("magenta");
                        terminal.WriteLine($"{target.Name} is bewildered by the joke!");
                    }
                    else
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine($"{target.Name} doesn't get the joke.");
                    }
                }
                break;

            case "freeze":
                if (target != null && target.IsAlive)
                {
                    int freezeChance = target.IsBoss ? 40 : 75;
                    if (random.Next(100) < freezeChance)
                    {
                        target.IsFrozen = true;
                        target.FrozenDuration = abilityResult.Duration > 0 ? abilityResult.Duration : 2;
                        terminal.SetColor("bright_cyan");
                        terminal.WriteLine($"{target.Name} is frozen solid!");
                    }
                    else
                    {
                        terminal.SetColor("cyan");
                        terminal.WriteLine($"The frost bites {target.Name} but doesn't freeze it!");
                    }
                }
                break;

            // === KILL/MULTI-HIT EFFECTS ===

            case "instant_kill":
                if (target != null && target.IsAlive && target.HP < target.MaxHP * 0.25 && !target.IsBoss)
                {
                    if (random.Next(100) < 50)
                    {
                        long killDmg = target.HP;
                        target.HP = 0;
                        result.TotalDamageDealt += killDmg;
                        result.Player?.Statistics.RecordDamageDealt(killDmg, false);
                        terminal.SetColor("bright_red");
                        terminal.WriteLine($"ASSASSINATED! {target.Name} falls instantly!");
                        if (!result.DefeatedMonsters.Contains(target))
                            result.DefeatedMonsters.Add(target);
                    }
                    else
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine("The killing blow narrowly misses the vital spot!");
                    }
                }
                break;

            case "multi_hit":
                if (target != null && target.IsAlive)
                {
                    int extraHits = random.Next(2, 5);
                    terminal.SetColor("bright_red");
                    terminal.WriteLine($"FRENZY! {extraHits} additional strikes!");
                    for (int i = 0; i < extraHits && target.IsAlive; i++)
                    {
                        long frenzyDmg = Math.Max(1, abilityResult.Damage / 3 - target.Defence / 4);
                        frenzyDmg = (long)(frenzyDmg * (0.8 + random.NextDouble() * 0.4));
                        target.HP -= frenzyDmg;
                        result.TotalDamageDealt += frenzyDmg;
                        result.Player?.Statistics.RecordDamageDealt(frenzyDmg, false);
                        terminal.SetColor("red");
                        terminal.WriteLine($"  Strike {i + 1}: {frenzyDmg} damage!");
                        if (target.HP <= 0)
                        {
                            target.HP = 0;
                            terminal.SetColor("bright_green");
                            terminal.WriteLine($"  {target.Name} is torn apart!");
                            if (!result.DefeatedMonsters.Contains(target))
                                result.DefeatedMonsters.Add(target);
                        }
                    }
                }
                break;

            case "execute_all":
                {
                    terminal.SetColor("bright_red");
                    terminal.WriteLine("DEATH BLOSSOM! Blades strike in every direction!");
                    var livingMonsDB = monsters.Where(m => m.IsAlive).ToList();
                    foreach (var m in livingMonsDB)
                    {
                        long blossomDmg = Math.Max(1, abilityResult.Damage / Math.Max(1, livingMonsDB.Count) - m.Defence / 3);
                        if (m.HP < m.MaxHP * 0.3)
                        {
                            blossomDmg *= 2;
                            terminal.SetColor("bright_red");
                            terminal.Write($"  EXECUTE {m.Name}: ");
                        }
                        else
                        {
                            terminal.SetColor("red");
                            terminal.Write($"  {m.Name}: ");
                        }
                        if (m.IsSleeping) { m.IsSleeping = false; m.SleepDuration = 0; }
                        m.HP -= blossomDmg;
                        result.TotalDamageDealt += blossomDmg;
                        result.Player?.Statistics.RecordDamageDealt(blossomDmg, false);
                        terminal.SetColor("red");
                        terminal.WriteLine($"-{blossomDmg} HP");
                        if (m.HP <= 0)
                        {
                            m.HP = 0;
                            terminal.SetColor("bright_green");
                            terminal.WriteLine($"  {m.Name} is slain!");
                            if (!result.DefeatedMonsters.Contains(m))
                                result.DefeatedMonsters.Add(m);
                        }
                    }
                }
                break;

            // === BUFF/UTILITY EFFECTS ===

            case "evasion":
                player.DodgeNextAttack = true;
                player.TempDefenseBonus += 50;
                player.TempDefenseBonusDuration = Math.Max(player.TempDefenseBonusDuration, 2);
                terminal.SetColor("magenta");
                terminal.WriteLine(isPlayer
                    ? "You melt into the shadows, nearly impossible to hit!"
                    : $"{actorName} melts into the shadows, nearly impossible to hit!");
                break;

            case "stealth":
                player.DodgeNextAttack = true;
                terminal.SetColor("green");
                terminal.WriteLine(isPlayer
                    ? "You blend perfectly with your surroundings!"
                    : $"{actorName} blends perfectly with their surroundings!");
                break;

            case "marked":
                if (target != null && target.IsAlive)
                {
                    target.IsMarked = true;
                    target.MarkedDuration = abilityResult.Duration > 0 ? abilityResult.Duration : 4;
                    terminal.SetColor("bright_red");
                    terminal.WriteLine($"{target.Name} is marked! All attacks deal 30% bonus damage!");
                }
                break;

            case "bloodlust":
                player.HasBloodlust = true;
                terminal.SetColor("bright_red");
                terminal.WriteLine(isPlayer
                    ? "BLOODLUST! Each kill will heal you!"
                    : $"BLOODLUST! Each kill will heal {actorName}!");
                break;

            case "immunity":
                player.HasStatusImmunity = true;
                player.StatusImmunityDuration = abilityResult.Duration > 0 ? abilityResult.Duration : 4;
                {
                    var toRemove = player.ActiveStatuses.Keys.Where(s => s.IsNegative()).ToList();
                    foreach (var s in toRemove) player.ActiveStatuses.Remove(s);
                }
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(isPlayer
                    ? "UNSTOPPABLE! You shrug off all afflictions!"
                    : $"UNSTOPPABLE! {actorName} shrugs off all afflictions!");
                break;

            case "invulnerable":
                if (!player.ActiveStatuses.ContainsKey(StatusEffect.Invulnerable))
                    player.ActiveStatuses[StatusEffect.Invulnerable] = abilityResult.Duration > 0 ? abilityResult.Duration : 2;
                terminal.SetColor("bright_white");
                terminal.WriteLine(isPlayer
                    ? "A divine shield of pure light surrounds you!"
                    : $"A divine shield of pure light surrounds {actorName}!");
                break;

            case "cleanse":
                {
                    var cleansable = player.ActiveStatuses.Keys.Where(s => s.IsNegative()).ToList();
                    foreach (var s in cleansable) player.ActiveStatuses.Remove(s);
                    terminal.SetColor("bright_white");
                    if (cleansable.Count > 0)
                        terminal.WriteLine($"Cleansing light purges {cleansable.Count} affliction(s)!");
                    else
                        terminal.WriteLine(isPlayer
                            ? "Holy light washes over you!"
                            : $"Holy light washes over {actorName}!");
                }
                break;

            case "vanish":
                player.DodgeNextAttack = true;
                if (!player.ActiveStatuses.ContainsKey(StatusEffect.Hidden))
                    player.ActiveStatuses[StatusEffect.Hidden] = 1;
                terminal.SetColor("magenta");
                terminal.WriteLine(isPlayer
                    ? "You vanish completely! Your next attack will strike from the shadows!"
                    : $"{actorName} vanishes completely! Their next attack will strike from the shadows!");
                break;

            case "shadow":
                if (!player.ActiveStatuses.ContainsKey(StatusEffect.Hidden))
                    player.ActiveStatuses[StatusEffect.Hidden] = 2;
                terminal.SetColor("magenta");
                terminal.WriteLine(isPlayer
                    ? "Shadows embrace you. Noctura's power flows through you!"
                    : $"Shadows embrace {actorName}. Noctura's power flows through them!");
                break;

            case "transmute":
                {
                    var negToRemove = player.ActiveStatuses.Keys.Where(s => s.IsNegative()).ToList();
                    foreach (var s in negToRemove) player.ActiveStatuses.Remove(s);
                    if (!player.ActiveStatuses.ContainsKey(StatusEffect.Regenerating))
                        player.ActiveStatuses[StatusEffect.Regenerating] = abilityResult.Duration > 0 ? abilityResult.Duration : 5;
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine("Alchemical energy transforms your body! All ailments purged!");
                }
                break;

            case "legend":
                if (!player.ActiveStatuses.ContainsKey(StatusEffect.Regenerating))
                    player.ActiveStatuses[StatusEffect.Regenerating] = abilityResult.Duration > 0 ? abilityResult.Duration : 5;
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("You become the legend! Songs of power flow through you!");
                break;

            case "legendary":
                if (target != null && target.IsAlive)
                {
                    target.IsStunned = true;
                    target.StunDuration = 1;
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine($"LEGENDARY SHOT! {target.Name} staggers from the devastating hit!");
                }
                break;

            case "avatar":
                player.IsRaging = true;
                player.HasStatusImmunity = true;
                player.StatusImmunityDuration = abilityResult.Duration > 0 ? abilityResult.Duration : 5;
                if (!player.ActiveStatuses.ContainsKey(StatusEffect.Raging))
                    player.ActiveStatuses[StatusEffect.Raging] = abilityResult.Duration > 0 ? abilityResult.Duration : 5;
                terminal.SetColor("bright_red");
                terminal.WriteLine("YOU BECOME DESTRUCTION INCARNATE! NOTHING CAN STOP YOU!");
                break;

            case "avatar_light":
                if (!player.ActiveStatuses.ContainsKey(StatusEffect.Invulnerable))
                    player.ActiveStatuses[StatusEffect.Invulnerable] = 1;
                player.HasStatusImmunity = true;
                player.StatusImmunityDuration = abilityResult.Duration > 0 ? abilityResult.Duration : 5;
                terminal.SetColor("bright_white");
                terminal.WriteLine("DIVINE LIGHT FLOODS YOUR BEING! YOU ARE THE AVATAR OF LIGHT!");
                break;

            case "guaranteed_hit":
                terminal.SetColor("cyan");
                terminal.WriteLine("Your aim is true!");
                break;
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Show ability selection menu and execute chosen ability (fallback for UseAbility without ID)
    /// </summary>
    private async Task ShowAbilityMenuAndExecute(Character player, List<Monster> monsters, CombatResult result)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("═══ COMBAT ABILITIES ═══");
        terminal.WriteLine("");

        var availableAbilities = ClassAbilitySystem.GetAvailableAbilities(player);

        if (availableAbilities.Count == 0)
        {
            terminal.WriteLine("You haven't learned any abilities yet!", "red");
            terminal.WriteLine("Train at the Level Master to unlock abilities as you level up.", "yellow");
            await Task.Delay(GetCombatDelay(2000));
            return;
        }

        terminal.SetColor("cyan");
        terminal.WriteLine($"Combat Stamina: {player.CurrentCombatStamina}/{player.MaxCombatStamina}");
        terminal.WriteLine("");
        terminal.WriteLine("Available Abilities:", "white");
        terminal.WriteLine("");

        int displayIndex = 1;
        var selectableAbilities = new List<ClassAbilitySystem.ClassAbility>();

        foreach (var ability in availableAbilities)
        {
            bool canUse = ClassAbilitySystem.CanUseAbility(player, ability.Id, abilityCooldowns);
            bool hasStamina = player.HasEnoughStamina(ability.StaminaCost);
            bool onCooldown = abilityCooldowns.TryGetValue(ability.Id, out int cooldownLeft) && cooldownLeft > 0;

            string statusText = "";
            string color = "white";

            if (onCooldown)
            {
                statusText = $" [Cooldown: {cooldownLeft} rounds]";
                color = "dark_gray";
            }
            else if (!hasStamina)
            {
                statusText = $" [Need {ability.StaminaCost} stamina]";
                color = "dark_gray";
            }

            terminal.SetColor(color);
            terminal.WriteLine($"  {displayIndex}. {ability.Name} - {ability.StaminaCost} stamina{statusText}");
            terminal.SetColor("gray");
            terminal.WriteLine($"     {ability.Description}");

            selectableAbilities.Add(ability);
            displayIndex++;
        }

        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.Write("Enter ability number (0 to cancel): ");
        string input = terminal.GetInputSync();

        if (!int.TryParse(input, out int choice) || choice < 1 || choice > selectableAbilities.Count)
        {
            terminal.WriteLine("Cancelled.", "gray");
            await Task.Delay(GetCombatDelay(500));
            return;
        }

        var selectedAbility = selectableAbilities[choice - 1];

        // Execute the selected ability
        var abilityAction = new CombatAction
        {
            Type = CombatActionType.ClassAbility,
            AbilityId = selectedAbility.Id,
            TargetIndex = null // Will pick random target
        };

        await ExecuteUseAbilityMultiMonster(player, monsters, abilityAction, result);
    }

    /// <summary>
    /// Execute spell in multi-monster combat (handles AoE and single target)
    /// </summary>
    private async Task ExecuteSpellMultiMonster(Character player, List<Monster> monsters, CombatAction action, CombatResult result)
    {
        var spellInfo = SpellSystem.GetSpellInfo(player.Class, action.SpellIndex);
        if (spellInfo == null)
        {
            terminal.WriteLine("Invalid spell!", "red");
            return;
        }

        // Check mana cost
        int manaCost = SpellSystem.CalculateManaCost(spellInfo, player);
        if (player.Mana < manaCost)
        {
            terminal.WriteLine("Not enough mana!", "red");
            await Task.Delay(GetCombatDelay(1000));
            return;
        }

        // Use SpellSystem.CastSpell for proper spell execution with all effects
        var spellResult = SpellSystem.CastSpell(player, spellInfo.Level, null);

        terminal.WriteLine("");
        terminal.SetColor("magenta");
        terminal.WriteLine($"You cast {spellInfo.Name}!");
        terminal.WriteLine(spellResult.Message);
        await Task.Delay(GetCombatDelay(1000));

        // Only apply effects if spell succeeded (not fumbled/failed)
        if (!spellResult.Success)
        {
            result.CombatLog.Add($"{player.DisplayName}'s spell fizzles.");
            return;
        }

        // Handle buff/heal spells - apply effects to caster or targeted ally
        if (spellInfo.SpellType == "Buff" || spellInfo.SpellType == "Heal")
        {
            // Check if healing an ally
            if (spellInfo.SpellType == "Heal" && action.AllyTargetIndex.HasValue && currentTeammates != null)
            {
                var injuredAllies = currentTeammates.Where(t => t.IsAlive && t.HP < t.MaxHP).ToList();
                if (action.AllyTargetIndex.Value < injuredAllies.Count)
                {
                    var allyTarget = injuredAllies[action.AllyTargetIndex.Value];
                    if (spellResult.Healing > 0)
                    {
                        long oldHP = allyTarget.HP;
                        allyTarget.HP = Math.Min(allyTarget.MaxHP, allyTarget.HP + spellResult.Healing);
                        long actualHeal = allyTarget.HP - oldHP;

                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"{allyTarget.DisplayName} recovers {actualHeal} HP!");

                        // Sync companion HP
                        if (allyTarget.IsCompanion && allyTarget.CompanionId.HasValue)
                        {
                            CompanionSystem.Instance.SyncCompanionHP(allyTarget);
                        }
                    }
                    result.CombatLog.Add($"{player.DisplayName} casts {spellInfo.Name} on {allyTarget.DisplayName}.");
                }
                else
                {
                    // Invalid target, heal self instead
                    ApplySpellEffects(player, null, spellResult);
                    result.CombatLog.Add($"{player.DisplayName} casts {spellInfo.Name}.");
                }
            }
            else
            {
                // Self-targeting
                ApplySpellEffects(player, null, spellResult);
                result.CombatLog.Add($"{player.DisplayName} casts {spellInfo.Name}.");
            }
        }
        // Handle AoE attack spells
        else if (action.TargetAllMonsters && spellInfo.IsMultiTarget)
        {
            // Use the spell's calculated damage
            long totalDamage = spellResult.Damage;
            if (totalDamage <= 0)
            {
                // Fallback if spell didn't set damage
                totalDamage = spellInfo.Level * 50 + (player.Intelligence / 2);
            }
            totalDamage = DifficultySystem.ApplyPlayerDamageMultiplier(totalDamage);
            await ApplyAoEDamage(monsters, totalDamage, result, spellInfo.Name);

            // Apply spell special effects to each surviving monster
            if (!string.IsNullOrEmpty(spellResult.SpecialEffect) && spellResult.SpecialEffect != "fizzle" && spellResult.SpecialEffect != "fail")
            {
                foreach (var m in monsters.Where(m => m.IsAlive))
                {
                    HandleSpecialSpellEffectOnMonster(m, spellResult.SpecialEffect, spellResult.Duration, player, spellResult.Damage, result);
                }
            }
        }
        // Handle single target attack/debuff spells
        else
        {
            Monster target = null;
            if (action.TargetIndex.HasValue && action.TargetIndex.Value < monsters.Count)
            {
                target = monsters[action.TargetIndex.Value];
            }
            else
            {
                target = GetRandomLivingMonster(monsters);
            }

            if (target != null && target.IsAlive)
            {
                // Use the spell's calculated damage
                long damage = spellResult.Damage;
                if (damage <= 0 && spellInfo.SpellType == "Attack")
                {
                    // Fallback if spell didn't set damage
                    damage = spellInfo.Level * 50 + (player.Intelligence / 2);
                }

                if (damage > 0)
                {
                    damage = DifficultySystem.ApplyPlayerDamageMultiplier(damage);
                    await ApplySingleMonsterDamage(target, damage, result, spellInfo.Name, player);
                }

                // Handle debuff special effects
                if (!string.IsNullOrEmpty(spellResult.SpecialEffect) && spellResult.SpecialEffect != "fizzle" && spellResult.SpecialEffect != "fail")
                {
                    HandleSpecialSpellEffectOnMonster(target, spellResult.SpecialEffect, spellResult.Duration, player, spellResult.Damage, result);
                }
            }
        }

        // Display training improvement message if spell proficiency increased
        if (spellResult.SkillImproved && !string.IsNullOrEmpty(spellResult.NewProficiencyLevel))
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"  Your {spellInfo.Name} proficiency improved to {spellResult.NewProficiencyLevel}!");
            await Task.Delay(GetCombatDelay(800));
        }
    }

    /// <summary>
    /// Apply special spell effects to a monster (sleep, fear, stun, poison, freeze, holy, drain, etc.)
    /// </summary>
    private void HandleSpecialSpellEffectOnMonster(Monster target, string effect, int duration, Character player, long spellDamage, CombatResult result)
    {
        switch (effect.ToLower())
        {
            case "sleep":
                target.IsSleeping = true;
                target.SleepDuration = duration > 0 ? duration : 3;
                terminal.WriteLine($"{target.Name} falls asleep!", "cyan");
                break;

            case "fear":
                target.IsFeared = true;
                target.FearDuration = duration > 0 ? duration : 3;
                terminal.WriteLine($"{target.Name} cowers in fear!", "yellow");
                break;

            case "stun":
            case "lightning":
                target.IsStunned = true;
                target.StunDuration = duration > 0 ? duration : 1;
                terminal.WriteLine($"{target.Name} is stunned!", "bright_yellow");
                break;

            case "slow":
                target.IsSlowed = true;
                target.SlowDuration = duration > 0 ? duration : 3;
                terminal.WriteLine($"{target.Name} is slowed!", "gray");
                break;

            case "poison":
                target.Poisoned = true;
                target.PoisonRounds = duration > 0 ? duration : 5;
                terminal.WriteLine($"{target.Name} is poisoned!", "dark_green");
                break;

            case "freeze":
                target.IsFrozen = true;
                target.FrozenDuration = duration > 0 ? duration : 2;
                terminal.WriteLine($"{target.Name} is frozen solid!", "bright_cyan");
                break;

            case "frost":
                target.IsSlowed = true;
                target.SlowDuration = duration > 0 ? duration : 2;
                terminal.WriteLine($"{target.Name} is chilled to the bone!", "cyan");
                break;

            case "web":
                target.IsStunned = true;
                target.StunDuration = duration > 0 ? duration : 2;
                terminal.WriteLine($"{target.Name} is entangled in sticky webs!", "white");
                break;

            case "confusion":
                target.IsConfused = true;
                target.ConfusedDuration = duration > 0 ? duration : 3;
                terminal.WriteLine($"{target.Name} stumbles around in confusion!", "magenta");
                break;

            case "mass_confusion":
                target.IsConfused = true;
                target.ConfusedDuration = duration > 0 ? duration : 3;
                terminal.WriteLine($"{target.Name} is overwhelmed by psychic chaos!", "magenta");
                break;

            case "dominate":
                target.Charmed = true;
                target.IsFriendly = true;
                terminal.WriteLine($"{target.Name}'s will is broken — it serves you now!", "bright_magenta");
                break;

            case "holy":
                // Bonus damage vs Undead and Demons
                if (target.MonsterClass == MonsterClass.Undead || target.MonsterClass == MonsterClass.Demon)
                {
                    long holyBonus = (long)(spellDamage * 0.5);
                    target.HP -= holyBonus;
                    terminal.WriteLine($"Holy light sears {target.Name} for {holyBonus} bonus damage!", "bright_yellow");
                    result.CombatLog.Add($"Holy bonus: {holyBonus} vs {target.MonsterClass}");
                }
                break;

            case "fire":
                // Burn DoT — reuse poison mechanics for fire damage over time
                target.Poisoned = true;
                target.PoisonRounds = duration > 0 ? duration : 2;
                terminal.WriteLine($"{target.Name} catches fire!", "red");
                break;

            case "drain":
                // Heal caster for 50% of spell damage
                if (spellDamage > 0)
                {
                    long healAmount = spellDamage / 2;
                    long oldHP = player.HP;
                    player.HP = Math.Min(player.MaxHP, player.HP + healAmount);
                    long actualHeal = player.HP - oldHP;
                    if (actualHeal > 0)
                    {
                        terminal.WriteLine($"You drain {actualHeal} life force from {target.Name}!", "bright_green");
                    }
                }
                break;

            case "death":
                // Instant kill chance if target below 20% HP (never on bosses)
                if (!target.IsBoss && target.HP < target.MaxHP * 0.20)
                {
                    if (random.Next(100) < 30)
                    {
                        terminal.WriteLine($"The death magic claims {target.Name}'s remaining life force!", "dark_red");
                        target.HP = 0;
                        result.CombatLog.Add($"Death spell instant kill on {target.Name}");
                    }
                    else
                    {
                        terminal.WriteLine($"{target.Name} resists the killing blow!", "gray");
                    }
                }
                break;

            case "disintegrate":
                // Reduce target defense by 25% for rest of combat
                int defReduction = (int)(target.ArmPow * 0.25);
                target.ArmPow = Math.Max(0, target.ArmPow - defReduction);
                terminal.WriteLine($"{target.Name}'s armor disintegrates! (-{defReduction} defense)", "bright_red");
                break;

            case "psychic":
                // 25% chance to confuse target for 1 round
                if (random.Next(100) < 25)
                {
                    target.IsConfused = true;
                    target.ConfusedDuration = 1;
                    terminal.WriteLine($"The psychic blast rattles {target.Name}'s mind!", "magenta");
                }
                break;

            case "dispel":
                // Clear monster positive states
                target.Charmed = false;
                target.IsFriendly = false;
                target.IsConverted = false;
                terminal.WriteLine($"Dark enchantments are stripped from {target.Name}!", "bright_white");
                break;

            case "convert":
            {
                // Conversion: flee/pacify/join based on CHA vs level
                terminal.WriteLine($"{target.Name} is touched by divine light!", "white");
                int conversionChance = 30 + (int)(player.Charisma / 5) - (target.Level * 2);
                conversionChance = Math.Clamp(conversionChance, 5, 85);

                if (target.MonsterClass == MonsterClass.Undead || target.MonsterClass == MonsterClass.Demon)
                {
                    conversionChance /= 2;
                    terminal.WriteLine("The unholy creature resists the divine light!", "dark_red");
                }

                if (random.Next(100) < conversionChance)
                {
                    int effectRoll = random.Next(100);
                    if (effectRoll < 40)
                    {
                        terminal.WriteLine($"{target.Name} sees the error of its ways and flees!", "bright_cyan");
                        target.HP = 0;
                        target.Fled = true;
                    }
                    else if (effectRoll < 70)
                    {
                        terminal.WriteLine($"{target.Name} is pacified by the holy light!", "bright_green");
                        target.IsStunned = true;
                        target.StunDuration = 3 + random.Next(1, 4);
                        target.IsFriendly = true;
                    }
                    else
                    {
                        terminal.WriteLine($"{target.Name} is converted to your cause!", "bright_yellow");
                        target.IsFriendly = true;
                        target.IsConverted = true;
                    }
                }
                else
                {
                    terminal.WriteLine($"{target.Name} resists the conversion attempt!", "gray");
                }
                break;
            }

            case "escape":
                terminal.WriteLine($"{player.DisplayName} vanishes in a whirl of arcane energy!", "magenta");
                globalEscape = true;
                break;
        }
    }

    /// <summary>
    /// Handle player healing an ally - choose between potion or spell, then choose target
    /// Returns the action to execute, or null if cancelled
    /// </summary>
    private async Task<CombatAction?> HandleHealAlly(Character player, List<Monster> monsters)
    {
        // Get all living teammates (show all, even if at full health - player can see status)
        var livingTeammates = currentTeammates?.Where(t => t.IsAlive).ToList() ?? new List<Character>();
        if (livingTeammates.Count == 0)
        {
            terminal.WriteLine("No allies available to heal.", "yellow");
            await Task.Delay(GetCombatDelay(1000));
            return null;
        }

        // Determine what healing options the player has
        bool hasPotion = player.Healing > 0;
        bool isSpellcaster = ClassAbilitySystem.IsSpellcaster(player.Class);
        bool hasHealSpell = isSpellcaster && player.Mana > 0; // Will check for actual heal spells below

        terminal.WriteLine("");
        terminal.SetColor("bright_green");
        terminal.WriteLine("═══ HEAL ALLY ═══");
        terminal.WriteLine("");

        // Show healing options
        terminal.SetColor("white");
        int option = 1;
        List<(int num, string type)> options = new();

        if (hasPotion)
        {
            terminal.WriteLine($"[{option}] Give healing potion(s) ({player.Healing} remaining)");
            options.Add((option, "potion"));
            option++;
        }

        if (hasHealSpell)
        {
            terminal.WriteLine($"[{option}] Cast healing spell");
            options.Add((option, "spell"));
            option++;
        }

        terminal.SetColor("gray");
        terminal.WriteLine("[0] Cancel");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.Write("Choose healing method: ");
        var methodInput = await terminal.GetInput("");

        if (!int.TryParse(methodInput, out int methodChoice) || methodChoice == 0)
        {
            return null;
        }

        var selectedOption = options.FirstOrDefault(o => o.num == methodChoice);
        if (selectedOption == default)
        {
            terminal.WriteLine("Invalid choice.", "red");
            await Task.Delay(GetCombatDelay(500));
            return null;
        }

        // Now select which ally to heal - show ALL teammates
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("Select ally to heal:");
        for (int i = 0; i < livingTeammates.Count; i++)
        {
            var ally = livingTeammates[i];
            int hpPercent = ally.MaxHP > 0 ? (int)(100 * ally.HP / ally.MaxHP) : 100;
            string hpColor = hpPercent < 25 ? "red" : hpPercent < 50 ? "yellow" : hpPercent < 100 ? "bright_green" : "green";
            terminal.SetColor(hpColor);
            string status = hpPercent >= 100 ? " (Full)" : "";
            terminal.WriteLine($"  [{i + 1}] {ally.DisplayName} - HP: {ally.HP}/{ally.MaxHP} ({hpPercent}%){status}");
        }
        terminal.SetColor("gray");
        terminal.WriteLine("  [0] Cancel");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.Write("Choose target: ");
        var targetInput = await terminal.GetInput("");

        if (!int.TryParse(targetInput, out int targetChoice) || targetChoice == 0)
        {
            return null;
        }

        if (targetChoice < 1 || targetChoice > livingTeammates.Count)
        {
            terminal.WriteLine("Invalid target.", "red");
            await Task.Delay(GetCombatDelay(500));
            return null;
        }

        var targetAlly = livingTeammates[targetChoice - 1];

        // Check if target is already at full health
        if (targetAlly.HP >= targetAlly.MaxHP)
        {
            terminal.WriteLine($"{targetAlly.DisplayName} is already at full health!", "yellow");
            await Task.Delay(GetCombatDelay(1000));
            return null;
        }

        // Execute the healing
        if (selectedOption.type == "potion")
        {
            // Calculate how much HP is missing
            long missingHP = targetAlly.MaxHP - targetAlly.HP;
            int healPerPotion = 30 + player.Level * 5 + 20; // Average heal per potion

            // Ask if player wants to fully heal or use 1 potion
            int potionsNeeded = (int)Math.Ceiling((double)missingHP / healPerPotion);
            potionsNeeded = Math.Min(potionsNeeded, (int)player.Healing); // Can't use more than we have

            terminal.WriteLine("");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"{targetAlly.DisplayName} is missing {missingHP} HP.");
            terminal.WriteLine($"Each potion heals approximately {healPerPotion} HP.");
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine($"[1] Use 1 potion");
            if (potionsNeeded > 1)
            {
                terminal.WriteLine($"[F] Fully heal (uses up to {potionsNeeded} potions)");
            }
            terminal.SetColor("gray");
            terminal.WriteLine("[0] Cancel");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.Write("Choice: ");
            var potionChoice = await terminal.GetInput("");

            if (string.IsNullOrEmpty(potionChoice) || potionChoice.ToUpper() == "0")
            {
                return null;
            }

            int potionsToUse = 1;
            if (potionChoice.ToUpper() == "F" && potionsNeeded > 1)
            {
                potionsToUse = potionsNeeded;
            }
            else if (potionChoice != "1")
            {
                terminal.WriteLine("Invalid choice.", "red");
                await Task.Delay(GetCombatDelay(500));
                return null;
            }

            // Apply healing potions
            long totalHeal = 0;
            long oldHP = targetAlly.HP;

            for (int i = 0; i < potionsToUse && targetAlly.HP < targetAlly.MaxHP; i++)
            {
                player.Healing--;
                int healAmount = 30 + player.Level * 5 + random.Next(10, 30);
                targetAlly.HP = Math.Min(targetAlly.MaxHP, targetAlly.HP + healAmount);
            }

            totalHeal = targetAlly.HP - oldHP;

            // Track statistics
            player.Statistics.RecordPotionUsed(totalHeal);

            terminal.WriteLine("");
            terminal.SetColor("bright_green");
            if (potionsToUse == 1)
            {
                terminal.WriteLine($"You give a healing potion to {targetAlly.DisplayName}!");
            }
            else
            {
                terminal.WriteLine($"You give {potionsToUse} healing potions to {targetAlly.DisplayName}!");
            }
            terminal.WriteLine($"{targetAlly.DisplayName} recovers {totalHeal} HP!", "green");

            if (targetAlly.HP >= targetAlly.MaxHP)
            {
                terminal.WriteLine($"{targetAlly.DisplayName} is fully healed!", "bright_green");
            }

            // Sync companion HP if this is a companion
            if (targetAlly.IsCompanion && targetAlly.CompanionId.HasValue)
            {
                CompanionSystem.Instance.SyncCompanionHP(targetAlly);
            }

            await Task.Delay(GetCombatDelay(1000));

            // Return a "no action" since healing used the turn but isn't an attack
            return new CombatAction { Type = CombatActionType.HealAlly };
        }
        else // spell
        {
            // Show heal spells and let player choose
            var healSpells = GetAvailableHealSpells(player);
            if (healSpells.Count == 0)
            {
                terminal.WriteLine("You don't have any healing spells available.", "yellow");
                await Task.Delay(GetCombatDelay(1000));
                return null;
            }

            terminal.WriteLine("");
            terminal.SetColor("bright_blue");
            terminal.WriteLine("Select healing spell:");
            for (int i = 0; i < healSpells.Count; i++)
            {
                var spell = healSpells[i];
                terminal.SetColor("cyan");
                terminal.WriteLine($"  [{i + 1}] {spell.Name} - Mana: {spell.ManaCost}");
            }
            terminal.SetColor("gray");
            terminal.WriteLine("  [0] Cancel");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.Write("Choose spell: ");
            var spellInput = await terminal.GetInput("");

            if (!int.TryParse(spellInput, out int spellChoice) || spellChoice == 0)
            {
                return null;
            }

            if (spellChoice < 1 || spellChoice > healSpells.Count)
            {
                terminal.WriteLine("Invalid spell.", "red");
                await Task.Delay(GetCombatDelay(500));
                return null;
            }

            var selectedSpell = healSpells[spellChoice - 1];

            // Check mana
            if (player.Mana < selectedSpell.ManaCost)
            {
                terminal.WriteLine("Not enough mana!", "red");
                await Task.Delay(GetCombatDelay(1000));
                return null;
            }

            // Cast the heal spell on the ally
            var spellResult = SpellSystem.CastSpell(player, selectedSpell.Level, null);

            terminal.WriteLine("");
            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"You cast {selectedSpell.Name} on {targetAlly.DisplayName}!");
            terminal.WriteLine(spellResult.Message);

            if (spellResult.Success && spellResult.Healing > 0)
            {
                long oldHP = targetAlly.HP;
                targetAlly.HP = Math.Min(targetAlly.MaxHP, targetAlly.HP + spellResult.Healing);
                long actualHeal = targetAlly.HP - oldHP;

                terminal.SetColor("bright_green");
                terminal.WriteLine($"{targetAlly.DisplayName} recovers {actualHeal} HP!");

                // Sync companion HP if this is a companion
                if (targetAlly.IsCompanion && targetAlly.CompanionId.HasValue)
                {
                    CompanionSystem.Instance.SyncCompanionHP(targetAlly);
                }
            }
            else if (!spellResult.Success)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("The spell fails to take effect.");
            }

            await Task.Delay(GetCombatDelay(1000));

            return new CombatAction { Type = CombatActionType.HealAlly };
        }
    }

    /// <summary>
    /// Get list of healing spells available to the player
    /// </summary>
    private List<SpellSystem.SpellInfo> GetAvailableHealSpells(Character player)
    {
        var result = new List<SpellSystem.SpellInfo>();

        // Get spells for the player's class
        var spells = SpellSystem.GetAllSpellsForClass(player.Class);
        if (spells == null || spells.Count == 0) return result;

        foreach (var spell in spells)
        {
            // Check if it's a heal spell and player can cast it
            if (spell.SpellType == "Heal" &&
                player.Level >= SpellSystem.GetLevelRequired(player.Class, spell.Level) &&
                player.Mana >= spell.ManaCost)
            {
                result.Add(spell);
            }
        }

        return result;
    }

    /// <summary>
    /// Prompt player to select heal target (self or ally)
    /// Returns null for self, or index of teammate to heal
    /// </summary>
    private async Task<int?> SelectHealTarget(Character player)
    {
        var injuredAllies = currentTeammates?.Where(t => t.IsAlive && t.HP < t.MaxHP).ToList() ?? new List<Character>();

        terminal.WriteLine("");
        terminal.SetColor("bright_green");
        terminal.WriteLine("Select heal target:");

        // Self option (guard against division by zero)
        int playerHpPercent = player.MaxHP > 0 ? (int)(100 * player.HP / player.MaxHP) : 100;
        string playerHpColor = playerHpPercent < 25 ? "red" : playerHpPercent < 50 ? "yellow" : "green";
        terminal.SetColor(playerHpColor);
        terminal.WriteLine($"  [0] Self - HP: {player.HP}/{player.MaxHP} ({playerHpPercent}%)");

        // Ally options
        for (int i = 0; i < injuredAllies.Count; i++)
        {
            var ally = injuredAllies[i];
            int hpPercent = ally.MaxHP > 0 ? (int)(100 * ally.HP / ally.MaxHP) : 100;
            string hpColor = hpPercent < 25 ? "red" : hpPercent < 50 ? "yellow" : "green";
            terminal.SetColor(hpColor);
            terminal.WriteLine($"  [{i + 1}] {ally.DisplayName} - HP: {ally.HP}/{ally.MaxHP} ({hpPercent}%)");
        }

        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.Write("Target: ");
        var input = await terminal.GetInput("");

        if (!int.TryParse(input, out int choice))
        {
            return null; // Default to self
        }

        if (choice == 0)
        {
            return null; // Self
        }

        if (choice >= 1 && choice <= injuredAllies.Count)
        {
            return choice - 1; // Return index into currentTeammates
        }

        return null; // Invalid, default to self
    }

    /// <summary>
    /// Process teammate action in multi-monster combat with intelligent AI
    /// </summary>
    private async Task ProcessTeammateActionMultiMonster(Character teammate, List<Monster> monsters, CombatResult result)
    {
        // Build list of all party members (player + teammates) for healing decisions
        var allPartyMembers = new List<Character> { currentPlayer };
        if (currentTeammates != null)
        {
            allPartyMembers.AddRange(currentTeammates.Where(t => t.IsAlive));
        }

        // Check if teammate should heal instead of attack
        var healAction = await TryTeammateHealAction(teammate, allPartyMembers, result);
        if (healAction)
        {
            return; // Healing action was taken
        }

        // Check if teammate should cast an offensive spell
        var spellAction = await TryTeammateOffensiveSpell(teammate, monsters, result);
        if (spellAction)
        {
            return; // Spell was cast
        }

        // Check if teammate should use a class ability
        var abilityAction = await TryTeammateClassAbility(teammate, monsters, result);
        if (abilityAction)
        {
            return; // Ability was used
        }

        // Otherwise, attack the weakest monster
        var weakestMonster = monsters
            .Where(m => m.IsAlive)
            .OrderBy(m => m.HP)
            .FirstOrDefault();

        if (weakestMonster != null)
        {
            terminal.WriteLine("");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"{teammate.DisplayName} attacks {weakestMonster.Name}!");
            await Task.Delay(GetCombatDelay(500));

            // Calculate teammate attack damage
            long attackPower = teammate.Strength + teammate.WeapPow + random.Next(1, 16);

            // Apply weapon configuration damage modifier (includes alignment bonuses)
            double damageModifier = GetWeaponConfigDamageModifier(teammate);
            attackPower = (long)(attackPower * damageModifier);

            long damage = Math.Max(1, attackPower);
            damage = DifficultySystem.ApplyPlayerDamageMultiplier(damage);
            await ApplySingleMonsterDamage(weakestMonster, damage, result, $"{teammate.DisplayName}'s attack", teammate);
        }
    }

    /// <summary>
    /// Try to have a teammate perform a healing action. Returns true if healing occurred.
    /// Uses balanced priority - heals whoever has the lowest HP percentage.
    /// </summary>
    private async Task<bool> TryTeammateHealAction(Character teammate, List<Character> allPartyMembers, CombatResult result)
    {
        // Find the most injured party member (lowest HP percentage)
        var mostInjured = allPartyMembers
            .Where(m => m.IsAlive && m.HP < m.MaxHP)
            .OrderBy(m => (double)m.HP / m.MaxHP)
            .FirstOrDefault();

        if (mostInjured == null)
        {
            return false; // No one needs healing
        }

        double injuredPercent = (double)mostInjured.HP / mostInjured.MaxHP;

        // Check if teammate can heal with spells (any class with mana and healing spells)
        bool canHealWithSpells = teammate.Mana > 10 && GetBestHealSpell(teammate) != null;
        bool hasPotion = teammate.Healing > 0;

        // Classes that prioritize healing
        bool isHealerClass = teammate.Class == CharacterClass.Cleric ||
                            teammate.Class == CharacterClass.Paladin ||
                            teammate.Class == CharacterClass.Bard;

        // Healer classes heal more aggressively (below 70% HP)
        // Other classes with heals are more conservative (below 50% HP)
        double healThreshold = isHealerClass ? 0.70 : 0.50;

        // Use heal spell if available and target needs it
        if (canHealWithSpells && injuredPercent < healThreshold)
        {
            return await TeammateHealWithSpell(teammate, mostInjured, result);
        }

        // Use potion if no spells or low mana and target is below 50% HP
        if (hasPotion && injuredPercent < 0.50)
        {
            return await TeammateHealWithPotion(teammate, mostInjured, result);
        }

        // Self-preservation: if the teammate themselves is below 50% HP, use a potion
        double selfPercent = (double)teammate.HP / teammate.MaxHP;
        if (hasPotion && selfPercent < 0.50)
        {
            return await TeammateHealWithPotion(teammate, teammate, result);
        }

        return false;
    }

    /// <summary>
    /// Teammate casts a healing spell on a party member
    /// </summary>
    private async Task<bool> TeammateHealWithSpell(Character teammate, Character target, CombatResult result)
    {
        // Get best healing spell the teammate can cast
        var healSpell = GetBestHealSpell(teammate);
        if (healSpell == null || teammate.Mana < healSpell.ManaCost)
        {
            return false;
        }

        // Cast the spell
        var spellResult = SpellSystem.CastSpell(teammate, healSpell.Level, null);

        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");

        string targetName = target == currentPlayer ? "you" : target.DisplayName;
        terminal.WriteLine($"{teammate.DisplayName} casts {healSpell.Name} on {targetName}!");

        if (spellResult.Success && spellResult.Healing > 0)
        {
            long oldHP = target.HP;
            target.HP = Math.Min(target.MaxHP, target.HP + spellResult.Healing);
            long actualHeal = target.HP - oldHP;

            terminal.SetColor("bright_green");
            string healTarget = target == currentPlayer ? "You recover" : $"{target.DisplayName} recovers";
            terminal.WriteLine($"{healTarget} {actualHeal} HP!");

            // Sync companion HP
            if (target.IsCompanion && target.CompanionId.HasValue)
            {
                CompanionSystem.Instance.SyncCompanionHP(target);
            }

            result.CombatLog.Add($"{teammate.DisplayName} heals {target.DisplayName} for {actualHeal} HP.");
        }
        else
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("The spell fizzles!");
            result.CombatLog.Add($"{teammate.DisplayName}'s healing spell fizzles.");
        }

        await Task.Delay(GetCombatDelay(800));
        return true;
    }

    /// <summary>
    /// Teammate uses a healing potion on a party member
    /// </summary>
    private async Task<bool> TeammateHealWithPotion(Character teammate, Character target, CombatResult result)
    {
        if (teammate.Healing <= 0)
        {
            return false;
        }

        teammate.Healing--;

        // Potion heals a fixed amount plus some randomness (same formula as player potions)
        int healAmount = 30 + teammate.Level * 5 + random.Next(10, 30);
        long oldHP = target.HP;
        target.HP = Math.Min(target.MaxHP, target.HP + healAmount);
        long actualHeal = target.HP - oldHP;

        // Track statistics if this is the player using a potion or being healed
        if (currentPlayer != null)
        {
            currentPlayer.Statistics.RecordPotionUsed(actualHeal);
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");

        string targetName = target == currentPlayer ? "you" : target.DisplayName;
        if (target == teammate)
        {
            terminal.WriteLine($"{teammate.DisplayName} drinks a healing potion!");
        }
        else
        {
            terminal.WriteLine($"{teammate.DisplayName} gives a healing potion to {targetName}!");
        }

        terminal.SetColor("bright_green");
        string healTarget = target == currentPlayer ? "You recover" : $"{target.DisplayName} recovers";
        terminal.WriteLine($"{healTarget} {actualHeal} HP!");

        // Sync companion state (HP and potions)
        if (target.IsCompanion && target.CompanionId.HasValue)
        {
            CompanionSystem.Instance.SyncCompanionHP(target);
        }
        if (teammate.IsCompanion && teammate.CompanionId.HasValue)
        {
            CompanionSystem.Instance.SyncCompanionState(teammate);
        }

        result.CombatLog.Add($"{teammate.DisplayName} uses potion on {target.DisplayName} for {actualHeal} HP.");

        await Task.Delay(GetCombatDelay(800));
        return true;
    }

    /// <summary>
    /// Get the best healing spell the character can cast based on their current mana
    /// </summary>
    private SpellSystem.SpellInfo? GetBestHealSpell(Character caster)
    {
        var spells = SpellSystem.GetAllSpellsForClass(caster.Class);
        if (spells == null || spells.Count == 0) return null;

        return spells
            .Where(s => s.SpellType == "Heal" &&
                        caster.Level >= SpellSystem.GetLevelRequired(caster.Class, s.Level) &&
                        caster.Mana >= s.ManaCost)
            .OrderByDescending(s => s.Level) // Prefer higher level heals
            .FirstOrDefault();
    }

    /// <summary>
    /// Get the best offensive spell the character can cast based on their current mana
    /// Prefers AoE spells when multiple enemies exist, otherwise single-target
    /// </summary>
    private SpellSystem.SpellInfo? GetBestOffensiveSpell(Character caster, bool preferAoE)
    {
        var spells = SpellSystem.GetAllSpellsForClass(caster.Class);
        if (spells == null || spells.Count == 0) return null;

        var availableAttacks = spells
            .Where(s => s.SpellType == "Attack" &&
                        caster.Level >= SpellSystem.GetLevelRequired(caster.Class, s.Level) &&
                        caster.Mana >= SpellSystem.CalculateManaCost(s, caster))
            .ToList();

        if (availableAttacks.Count == 0) return null;

        // If preferring AoE and we have AoE spells, use the best one
        if (preferAoE)
        {
            var aoeSpell = availableAttacks
                .Where(s => s.IsMultiTarget)
                .OrderByDescending(s => s.Level)
                .FirstOrDefault();
            if (aoeSpell != null) return aoeSpell;
        }

        // Otherwise return best single-target attack
        return availableAttacks
            .Where(s => !s.IsMultiTarget)
            .OrderByDescending(s => s.Level)
            .FirstOrDefault() ?? availableAttacks.OrderByDescending(s => s.Level).FirstOrDefault();
    }

    /// <summary>
    /// NPC teammate attempts to cast an offensive spell on enemies
    /// </summary>
    private async Task<bool> TryTeammateOffensiveSpell(Character teammate, List<Monster> monsters, CombatResult result)
    {
        // Only cast spells if teammate is a spell-casting class and has mana
        bool isSpellCaster = teammate.Class == CharacterClass.Magician ||
                            teammate.Class == CharacterClass.Sage ||
                            teammate.Class == CharacterClass.Cleric ||
                            teammate.Class == CharacterClass.Paladin ||
                            teammate.Class == CharacterClass.Bard;

        if (!isSpellCaster || teammate.Mana < 10) return false;

        // Count living monsters to decide if AoE is worth it
        var livingMonsters = monsters.Where(m => m.IsAlive).ToList();
        if (livingMonsters.Count == 0) return false;

        bool preferAoE = livingMonsters.Count >= 3;
        var spell = GetBestOffensiveSpell(teammate, preferAoE);

        if (spell == null) return false;

        // 70% chance to cast spell instead of attacking (don't always spam spells)
        if (random.Next(100) >= 70) return false;

        // Cast the spell
        int manaCost = SpellSystem.CalculateManaCost(spell, teammate);
        teammate.Mana -= manaCost;

        var spellResult = SpellSystem.CastSpell(teammate, spell.Level, null);

        terminal.WriteLine("");
        terminal.SetColor("magenta");
        terminal.WriteLine($"{teammate.DisplayName} casts {spell.Name}!");

        if (!spellResult.Success)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("The spell fizzles!");
            result.CombatLog.Add($"{teammate.DisplayName}'s {spell.Name} fizzles.");
            await Task.Delay(GetCombatDelay(600));
            return true; // Still used their turn
        }

        // Calculate damage
        long damage = spellResult.Damage;
        if (damage <= 0)
        {
            damage = spell.Level * 40 + (teammate.Intelligence / 2);
        }

        // Apply AoE damage
        if (spell.IsMultiTarget && livingMonsters.Count >= 2)
        {
            long damagePerTarget = damage / livingMonsters.Count;
            damagePerTarget = Math.Max(damagePerTarget, damage / 3); // Min 1/3 damage each

            foreach (var monster in livingMonsters)
            {
                long actualDamage = Math.Min(damagePerTarget, monster.HP);
                monster.HP -= (int)actualDamage;

                terminal.SetColor("bright_red");
                terminal.WriteLine($"  {monster.Name} takes {actualDamage} damage!");

                if (monster.HP <= 0)
                {
                    monster.HP = 0;
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"  {monster.Name} is destroyed!");
                    result.DefeatedMonsters.Add(monster);
                }
            }

            // Apply spell special effects to each surviving monster (same as player AoE path)
            if (!string.IsNullOrEmpty(spellResult.SpecialEffect) && spellResult.SpecialEffect != "fizzle" && spellResult.SpecialEffect != "fail")
            {
                foreach (var m in livingMonsters.Where(m => m.IsAlive))
                {
                    HandleSpecialSpellEffectOnMonster(m, spellResult.SpecialEffect, spellResult.Duration, teammate, spellResult.Damage, result);
                }
            }

            result.CombatLog.Add($"{teammate.DisplayName} casts {spell.Name} for {damage} total damage!");
        }
        else
        {
            // Single target - hit weakest monster
            var target = livingMonsters.OrderBy(m => m.HP).FirstOrDefault();
            if (target != null)
            {
                long actualDamage = Math.Min(damage, target.HP);
                target.HP -= (int)actualDamage;

                terminal.SetColor("bright_red");
                terminal.WriteLine($"{target.Name} takes {actualDamage} damage!");

                if (target.HP <= 0)
                {
                    target.HP = 0;
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"{target.Name} is destroyed!");
                    result.DefeatedMonsters.Add(target);
                }

                // Apply spell special effect to target (same as player single-target path)
                if (target.IsAlive && !string.IsNullOrEmpty(spellResult.SpecialEffect) && spellResult.SpecialEffect != "fizzle" && spellResult.SpecialEffect != "fail")
                {
                    HandleSpecialSpellEffectOnMonster(target, spellResult.SpecialEffect, spellResult.Duration, teammate, spellResult.Damage, result);
                }

                result.CombatLog.Add($"{teammate.DisplayName} casts {spell.Name} on {target.Name} for {actualDamage} damage!");
            }
        }

        await Task.Delay(GetCombatDelay(800));
        return true;
    }

    /// <summary>
    /// NPC teammate attempts to use a class ability via the full ClassAbilitySystem.
    /// Uses the same ApplyAbilityEffectsMultiMonster as the player, so all 30+ ability
    /// effects (stun, poison, execute, frenzy, holy, rage, etc.) work for teammates too.
    /// </summary>
    private async Task<bool> TryTeammateClassAbility(Character teammate, List<Monster> monsters, CombatResult result)
    {
        var livingMonsters = monsters.Where(m => m.IsAlive).ToList();
        if (livingMonsters.Count == 0) return false;

        // Get all abilities available to this teammate's class and level
        var availableAbilities = ClassAbilitySystem.GetAvailableAbilities(teammate);
        if (availableAbilities == null || availableAbilities.Count == 0) return false;

        // Filter to abilities the teammate can afford (stamina-wise)
        var affordableAbilities = availableAbilities
            .Where(a => teammate.CurrentCombatStamina >= a.StaminaCost)
            .ToList();
        if (affordableAbilities.Count == 0) return false;

        // 30% chance to use an ability (don't spam every round)
        if (random.Next(100) >= 30) return false;

        // AI: Pick the best ability for the situation
        ClassAbilitySystem.ClassAbility? chosenAbility = null;

        // Prefer attack abilities — pick the strongest one available
        var attackAbilities = affordableAbilities
            .Where(a => a.Type == ClassAbilitySystem.AbilityType.Attack || a.Type == ClassAbilitySystem.AbilityType.Debuff)
            .OrderByDescending(a => a.LevelRequired)
            .ToList();

        // If multiple monsters alive, prefer AoE abilities
        if (livingMonsters.Count >= 3)
        {
            var aoeAbility = attackAbilities.FirstOrDefault(a =>
                a.SpecialEffect == "aoe" || a.SpecialEffect == "whirlwind" ||
                a.SpecialEffect == "fire" || a.SpecialEffect == "holy_aoe");
            if (aoeAbility != null) chosenAbility = aoeAbility;
        }

        // If a monster is low HP, prefer execute-type abilities
        if (chosenAbility == null && livingMonsters.Any(m => m.HP < m.MaxHP * 0.3))
        {
            var executeAbility = attackAbilities.FirstOrDefault(a =>
                a.SpecialEffect == "execute" || a.SpecialEffect == "assassinate");
            if (executeAbility != null) chosenAbility = executeAbility;
        }

        // Otherwise use the strongest attack ability
        if (chosenAbility == null && attackAbilities.Count > 0)
        {
            chosenAbility = attackAbilities[0]; // Highest level required = strongest
        }

        // If no attack abilities, try buff/defense (20% chance)
        if (chosenAbility == null && random.Next(100) < 20)
        {
            chosenAbility = affordableAbilities
                .Where(a => a.Type == ClassAbilitySystem.AbilityType.Buff || a.Type == ClassAbilitySystem.AbilityType.Defense || a.Type == ClassAbilitySystem.AbilityType.Heal)
                .OrderByDescending(a => a.LevelRequired)
                .FirstOrDefault();
        }

        if (chosenAbility == null) return false;

        // Use the ability through the proper system (calculates damage, scaling, effects)
        var abilityResult = ClassAbilitySystem.UseAbility(teammate, chosenAbility.Id, random);
        if (!abilityResult.Success) return false;

        // Display ability usage
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"» {teammate.DisplayName} uses {chosenAbility.Name}! (-{chosenAbility.StaminaCost} stamina)");
        await Task.Delay(GetCombatDelay(500));

        // Get target for the ability
        Monster? target = null;
        if (chosenAbility.Type == ClassAbilitySystem.AbilityType.Attack || chosenAbility.Type == ClassAbilitySystem.AbilityType.Debuff)
        {
            // Target low HP monster for execute abilities, otherwise weakest
            if (chosenAbility.SpecialEffect == "execute" || chosenAbility.SpecialEffect == "assassinate")
            {
                target = livingMonsters.OrderBy(m => (double)m.HP / m.MaxHP).FirstOrDefault();
            }
            else
            {
                target = livingMonsters.OrderBy(m => m.HP).FirstOrDefault();
            }
        }
        else
        {
            // For buff/defense/heal, still need a target for the method signature
            target = livingMonsters.FirstOrDefault();
        }

        if (target == null) return false;

        // Apply ability effects through the full system (same as player path)
        await ApplyAbilityEffectsMultiMonster(teammate, target, livingMonsters, abilityResult, result);

        // Log the action
        result.CombatLog.Add($"{teammate.DisplayName} uses {chosenAbility.Name}");

        await Task.Delay(GetCombatDelay(800));
        return true;
    }

    /// <summary>
    /// Calculate base physical damage for a character
    /// </summary>
    private long CalculateBaseDamage(Character character)
    {
        // Base damage from strength + weapon power
        long damage = character.Strength / 2;

        // Add weapon power if equipped
        if (character.WeapPow > 0)
        {
            damage += character.WeapPow + random.Next(1, 16);
        }
        else
        {
            damage += character.Level * 2; // Unarmed damage scales with level
        }

        // Add additional strength bonus
        damage += character.Strength / 10;

        return Math.Max(1, damage);
    }

    /// <summary>
    /// Check if any companion will sacrifice themselves to save the player
    /// </summary>
    private async Task<(bool SacrificeOccurred, UsurperRemake.Systems.CompanionId? CompanionId)> CheckCompanionSacrifice(
        Character player, int incomingDamage, CombatResult result)
    {
        var companionSystem = UsurperRemake.Systems.CompanionSystem.Instance;

        // Find companions in teammates
        var companionTeammates = result.Teammates?.Where(t => t.IsCompanion && t.CompanionId.HasValue).ToList();
        if (companionTeammates == null || companionTeammates.Count == 0)
            return (false, null);

        // Check if any companion will sacrifice
        var sacrificingCompanion = companionSystem.CheckForSacrifice(player, incomingDamage);
        if (sacrificingCompanion == null)
            return (false, null);

        // Companion sacrifices themselves!
        terminal.WriteLine("");
        await Task.Delay(500);

        terminal.SetColor("bright_red");
        terminal.WriteLine("╔════════════════════════════════════════════════════╗");
        terminal.WriteLine("║              COMPANION SACRIFICE                    ║");
        terminal.WriteLine("╚════════════════════════════════════════════════════╝");
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("bright_white");
        terminal.WriteLine($"As the killing blow descends upon you...");
        await Task.Delay(800);

        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"{sacrificingCompanion.Name} throws themselves in the way!");
        terminal.WriteLine("");
        await Task.Delay(1000);

        // Show their sacrifice dialogue
        string sacrificeLine = sacrificingCompanion.Id switch
        {
            UsurperRemake.Systems.CompanionId.Aldric =>
                "\"NOT THIS TIME!\" Aldric roars, shield raised high.",
            UsurperRemake.Systems.CompanionId.Lyris =>
                "\"I finally understand why I found you...\" Lyris whispers, stepping forward.",
            UsurperRemake.Systems.CompanionId.Mira =>
                "\"Perhaps... this is what I was seeking all along.\" Mira smiles gently.",
            UsurperRemake.Systems.CompanionId.Vex =>
                "\"Heh. Always wanted to go out doing something that mattered.\" Vex grins.",
            _ => $"\"{sacrificingCompanion.Name} leaps to your defense!\""
        };

        terminal.SetColor("yellow");
        terminal.WriteLine(sacrificeLine);
        terminal.WriteLine("");
        await Task.Delay(1500);

        // The companion takes the full damage and dies
        terminal.SetColor("dark_red");
        terminal.WriteLine($"The blow strikes {sacrificingCompanion.Name} instead...");
        await Task.Delay(1000);

        // Remove companion from teammates
        var companionChar = companionTeammates.FirstOrDefault(t => t.CompanionId == sacrificingCompanion.Id);
        if (companionChar != null)
        {
            companionChar.HP = 0;
            result.Teammates?.Remove(companionChar);
        }

        // Kill the companion permanently
        await companionSystem.KillCompanion(
            sacrificingCompanion.Id,
            UsurperRemake.Systems.DeathType.Sacrifice,
            $"Sacrificed themselves to save {player.DisplayName} from a killing blow",
            terminal);

        // Player survives with 1 HP
        player.HP = 1;

        terminal.SetColor("gray");
        terminal.WriteLine("You are alive. But at what cost?");
        terminal.WriteLine("");
        await Task.Delay(2000);

        // Increase loyalty with remaining companions (they witnessed the sacrifice)
        foreach (var remaining in companionSystem.GetActiveCompanions())
        {
            if (remaining != null && !remaining.IsDead)
            {
                companionSystem.ModifyLoyalty(remaining.Id, 20,
                    $"Witnessed {sacrificingCompanion.Name}'s sacrifice");
            }
        }

        return (true, sacrificingCompanion.Id);
    }

    /// <summary>
    /// Smart monster targeting - selects who the monster attacks based on threat, class, and positioning
    /// Returns null to attack player, or a teammate to attack instead
    /// </summary>
    private Character? SelectMonsterTarget(Character player, List<Character> aliveTeammates, Monster monster, Random random)
    {
        // Build a list of all potential targets with weights
        var targetWeights = new List<(Character target, int weight)>();

        // Player base weight - squishy classes get lower weight (less likely to be targeted)
        int playerWeight = GetTargetWeight(player);
        targetWeights.Add((player, playerWeight));

        // Add all alive teammates with their weights
        foreach (var teammate in aliveTeammates)
        {
            int teammateWeight = GetTargetWeight(teammate);
            targetWeights.Add((teammate, teammateWeight));
        }

        // Calculate total weight
        int totalWeight = targetWeights.Sum(tw => tw.weight);
        if (totalWeight <= 0) return null; // Attack player by default

        // Roll to select target
        int roll = random.Next(totalWeight);
        int cumulative = 0;

        foreach (var (target, weight) in targetWeights)
        {
            cumulative += weight;
            if (roll < cumulative)
            {
                return target == player ? null : target;
            }
        }

        return null; // Default to player
    }

    /// <summary>
    /// Calculate target weight based on class role, armor, and threat level
    /// Higher weight = more likely to be targeted
    /// Tanks have high weight (draw aggro), squishies have low weight
    /// </summary>
    private int GetTargetWeight(Character character)
    {
        int baseWeight = 100;

        // Class-based modifiers - tank classes draw more aggro
        // Classes: Alchemist, Assassin, Barbarian, Bard, Cleric, Jester, Magician, Paladin, Ranger, Sage, Warrior
        switch (character.Class)
        {
            // Tank classes - HIGH aggro (frontline fighters)
            case CharacterClass.Paladin:
                baseWeight = 180; // Holy warriors draw attention, heavily armored
                break;
            case CharacterClass.Barbarian:
                baseWeight = 170; // Raging warriors are intimidating and threatening
                break;
            case CharacterClass.Warrior:
                baseWeight = 160; // Primary frontline fighters
                break;

            // Off-tank / Melee classes - MEDIUM-HIGH aggro
            case CharacterClass.Ranger:
                baseWeight = 130; // Can hold their own in combat
                break;
            case CharacterClass.Bard:
                baseWeight = 110; // Versatile but stays somewhat back
                break;
            case CharacterClass.Jester:
                baseWeight = 100; // Unpredictable, average targeting
                break;

            // Support / Squishy classes - LOW aggro (stay in back)
            case CharacterClass.Assassin:
                baseWeight = 70; // Sneaky, hard to target, stays in shadows
                break;
            case CharacterClass.Cleric:
                baseWeight = 80; // Healers stay in the back
                break;
            case CharacterClass.Magician:
                baseWeight = 60; // Squishy casters stay far back
                break;
            case CharacterClass.Sage:
                baseWeight = 65; // Scholars avoid direct combat
                break;
            case CharacterClass.Alchemist:
                baseWeight = 75; // Support class, stays back
                break;

            default:
                baseWeight = 100;
                break;
        }

        // Armor modifier - heavily armored characters draw more attention (they're in front)
        // ArmPow roughly indicates how armored someone is
        if (character.ArmPow > 50)
            baseWeight += 30;
        else if (character.ArmPow > 30)
            baseWeight += 15;
        else if (character.ArmPow < 15)
            baseWeight -= 20; // Lightly armored stay back

        // HP modifier - characters with more max HP are assumed to be more in the fray
        if (character.MaxHP > 500)
            baseWeight += 20;
        else if (character.MaxHP < 100)
            baseWeight -= 20;

        // Low HP modifier - monsters may finish off weakened targets
        double hpPercent = (double)character.HP / Math.Max(1, character.MaxHP);
        if (hpPercent < 0.25)
            baseWeight += 25; // Monsters smell blood
        else if (hpPercent < 0.5)
            baseWeight += 10;

        // Defending characters draw aggro (they're actively blocking)
        if (character.IsDefending)
            baseWeight += 40;

        // Ensure minimum weight of 10
        return Math.Max(10, baseWeight);
    }

    /// <summary>
    /// Handle monster attacking a companion instead of the player
    /// </summary>
    private async Task MonsterAttacksCompanion(Monster monster, Character companion, CombatResult result)
    {
        terminal.SetColor("red");
        terminal.WriteLine($"The {monster.Name} turns its attention to {companion.DisplayName}!");
        await Task.Delay(GetCombatDelay(500));

        // Calculate monster damage
        long monsterAttack = monster.GetAttackPower();
        monsterAttack += random.Next(0, 10);

        // Apply difficulty modifier
        monsterAttack = DifficultySystem.ApplyMonsterDamageMultiplier(monsterAttack);

        // Calculate companion defense
        long companionDefense = companion.Defence + companion.ArmPow / 2;

        long actualDamage = Math.Max(1, monsterAttack - companionDefense);

        // Apply damage to companion
        companion.HP = Math.Max(0, companion.HP - actualDamage);

        terminal.SetColor("yellow");
        terminal.WriteLine($"{companion.DisplayName} takes {actualDamage} damage! ({companion.HP}/{companion.MaxHP} HP)");

        result.CombatLog.Add($"{monster.Name} attacks {companion.DisplayName} for {actualDamage} damage");

        // Check if teammate died
        if (!companion.IsAlive)
        {
            if (companion.IsEcho)
            {
                // Player echo "death" - just dissipates, no permanent effect
                terminal.WriteLine("");
                terminal.SetColor("bright_cyan");
                terminal.WriteLine($"  {companion.DisplayName}'s echo dissipates into mist...");
                terminal.SetColor("gray");
                terminal.WriteLine($"  Their real self is unharmed.");
                result.Teammates?.Remove(companion);
            }
            else if (companion.IsCompanion && companion.CompanionId.HasValue)
            {
                // Story companion death
                await HandleCompanionDeath(companion, monster.Name, result);
            }
            else
            {
                // NPC teammate death (spouse, lover, team member)
                await HandleNpcTeammateDeath(companion, monster.Name, result);
            }
        }

        // Sync companion HP back to CompanionSystem (only for story companions)
        if (companion.IsCompanion)
        {
            var companionSystem = UsurperRemake.Systems.CompanionSystem.Instance;
            companionSystem.SyncCompanionHP(companion);
        }

        await Task.Delay(GetCombatDelay(1000));
    }

    /// <summary>
    /// Handle a companion dying in combat
    /// </summary>
    private async Task HandleCompanionDeath(Character companion, string killerName, CombatResult result)
    {
        if (!companion.CompanionId.HasValue) return;

        var companionSystem = UsurperRemake.Systems.CompanionSystem.Instance;

        terminal.WriteLine("");
        terminal.SetColor("dark_red");
        terminal.WriteLine($"{companion.DisplayName} falls!");
        terminal.WriteLine("");
        await Task.Delay(1000);

        // Remove from teammates
        result.Teammates?.Remove(companion);

        // Kill the companion permanently
        await companionSystem.KillCompanion(
            companion.CompanionId.Value,
            UsurperRemake.Systems.DeathType.Combat,
            $"Slain by {killerName} in combat",
            terminal);

        // Generate death news for the realm
        string location = result.Player?.CurrentLocation ?? "the dungeons";
        NewsSystem.Instance?.WriteDeathNews(companion.DisplayName, killerName, location);
    }

    /// <summary>
    /// Handle an NPC teammate (spouse, lover, team member) dying in combat
    /// </summary>
    private async Task HandleNpcTeammateDeath(Character npc, string killerName, CombatResult result)
    {
        terminal.WriteLine("");
        terminal.SetColor("dark_red");
        terminal.WriteLine("═══════════════════════════════════════════════════════════");
        terminal.WriteLine($"  {npc.DisplayName} has fallen in battle!");
        terminal.WriteLine("═══════════════════════════════════════════════════════════");
        terminal.WriteLine("");
        await Task.Delay(1500);

        // Mark the NPC as permanently dead
        // We need to find the actual NPC in the world and mark it dead
        var npcId = npc.ID ?? "";
        var worldNpc = UsurperRemake.Systems.NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == npcId);
        if (worldNpc != null)
        {
            worldNpc.IsDead = true;
            worldNpc.HP = 0; // Ensure HP is also zero
            DebugLogger.Instance.LogInfo("NPC", $"NPC DIED: {worldNpc.Name} (ID: {npcId}) - marked as permanently dead");

            // Queue for respawn immediately
            WorldSimulator.Instance?.QueueNPCForRespawn(worldNpc.Name);
        }

        // Also mark the combat character reference
        if (npc is NPC npcRef)
        {
            npcRef.IsDead = true;
        }

        // Remove from teammates
        result.Teammates?.Remove(npc);

        // Remove from player's dungeon party if applicable
        GameEngine.Instance?.DungeonPartyNPCIds?.Remove(npcId);

        // Trigger grief system for NPC teammate death
        UsurperRemake.Systems.GriefSystem.Instance.BeginNpcGrief(
            npcId,
            npc.DisplayName,
            UsurperRemake.Systems.DeathType.Combat);

        // Check relationship type and handle accordingly
        var romanceTracker = UsurperRemake.Systems.RomanceTracker.Instance;
        if (romanceTracker != null)
        {
            bool wasSpouse = romanceTracker.IsPlayerMarriedTo(npcId);
            bool wasLover = romanceTracker.IsPlayerInRelationshipWith(npcId);

            if (wasSpouse)
            {
                terminal.SetColor("magenta");
                terminal.WriteLine($"Your beloved spouse {npc.DisplayName} has fallen...");
                terminal.WriteLine($"Perhaps they can still be saved...");
                // Mark the spouse as dead in romance tracker
                romanceTracker.HandleSpouseDeath(npcId);
            }
            else if (wasLover)
            {
                terminal.SetColor("magenta");
                terminal.WriteLine($"Your lover {npc.DisplayName} has fallen...");
                terminal.WriteLine($"Perhaps they can still be saved...");
            }
            else
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"Your loyal teammate {npc.DisplayName} has made the ultimate sacrifice.");
            }
        }

        terminal.WriteLine("");
        await Task.Delay(1000);

        // Generate death news for the realm
        string location = result.Player?.CurrentLocation ?? "the dungeons";
        NewsSystem.Instance?.WriteDeathNews(npc.DisplayName, killerName, location);

        // Ocean Philosophy awakening moment
        if (!OceanPhilosophySystem.Instance.ExperiencedMoments.Contains(AwakeningMoment.FirstCompanionDeath))
        {
            OceanPhilosophySystem.Instance.ExperienceMoment(AwakeningMoment.FirstCompanionDeath);
        }
    }

    /// <summary>
    /// Handle victory over multiple monsters
    /// </summary>
    private async Task HandleVictoryMultiMonster(CombatResult result, bool offerMonkEncounter)
    {
        // MUD mode: broadcast victory
        if (result.DefeatedMonsters.Count > 0)
        {
            var monsterNames = string.Join(", ", result.DefeatedMonsters.Select(m => m.Name).Distinct());
            bool anyBoss = result.DefeatedMonsters.Any(m => m.IsBoss);
            if (anyBoss)
                UsurperRemake.Server.RoomRegistry.BroadcastAction($"{result.Player.DisplayName} has defeated the mighty {monsterNames}!");
            else if (result.DefeatedMonsters.Count == 1)
                UsurperRemake.Server.RoomRegistry.BroadcastAction($"{result.Player.DisplayName} defeated {monsterNames}.");
            else
                UsurperRemake.Server.RoomRegistry.BroadcastAction($"{result.Player.DisplayName} defeated {result.DefeatedMonsters.Count} enemies!");
        }

        terminal.WriteLine("");

        // Use enhanced victory message
        var victoryMessage = CombatMessages.GetVictoryMessage(result.DefeatedMonsters.Count);
        terminal.SetColor("bright_green");
        terminal.WriteLine("═══════════════════════════");
        terminal.WriteLine($"    {victoryMessage}");
        terminal.WriteLine("═══════════════════════════");
        terminal.WriteLine("");

        // Calculate total rewards from all defeated monsters
        long totalExp = 0;
        long totalGold = 0;

        foreach (var monster in result.DefeatedMonsters)
        {
            // Update quest progress for each monster killed
            // ONLY actual floor bosses count for boss-related tracking
            bool isBoss = monster.IsBoss;
            QuestSystem.OnMonsterKilled(result.Player, monster.Name, isBoss);

            // Calculate exp reward based on level difference
            long baseExp = monster.Experience;
            long levelDiff = monster.Level - result.Player.Level;

            // Percentage-based bonus/penalty: 15% per level difference
            // Fighting higher level monsters gives significant bonus (up to 100% extra)
            // Fighting lower level monsters gives penalty (minimum 25% of base)
            double levelMultiplier = 1.0 + (levelDiff * 0.15);
            levelMultiplier = Math.Clamp(levelMultiplier, 0.25, 2.0);
            long expReward = (long)(baseExp * levelMultiplier);
            expReward = Math.Max(10, expReward); // Never less than 10 XP

            // Calculate gold reward
            long goldReward = monster.Gold + random.Next(0, (int)(monster.Gold * 0.5));

            totalExp += expReward;
            totalGold += goldReward;

            // Track monster kill stats
            result.Player.MKills++;
            result.Player.Statistics.RecordMonsterKill(expReward, goldReward, isBoss, monster.IsUnique);
            ArchetypeTracker.Instance.RecordMonsterKill(monster.Level, monster.IsUnique);
            if (isBoss)
            {
                ArchetypeTracker.Instance.RecordBossDefeat(monster.Name, monster.Level);
            }
        }

        // Apply world event modifiers
        long adjustedExp = WorldEventSystem.Instance.GetAdjustedXP(totalExp);
        long adjustedGold = WorldEventSystem.Instance.GetAdjustedGold(totalGold);

        // Apply difficulty modifiers
        float xpMult = DifficultySystem.GetExperienceMultiplier(DifficultySystem.CurrentDifficulty);
        float goldMult = DifficultySystem.GetGoldMultiplier(DifficultySystem.CurrentDifficulty);
        adjustedExp = (long)(adjustedExp * xpMult);
        adjustedGold = (long)(adjustedGold * goldMult);

        // Apply child XP bonus
        float childXPMult = FamilySystem.Instance?.GetChildXPMultiplier(result.Player) ?? 1.0f;
        if (childXPMult > 1.0f)
        {
            adjustedExp = (long)(adjustedExp * childXPMult);
        }

        // Team bonus - 15% extra XP and gold for having teammates
        long teamXPBonus = 0;
        long teamGoldBonus = 0;
        if (result.Teammates != null && result.Teammates.Count > 0)
        {
            teamXPBonus = (long)(adjustedExp * 0.15);
            teamGoldBonus = (long)(adjustedGold * 0.15);
            adjustedExp += teamXPBonus;
            adjustedGold += teamGoldBonus;
        }

        // Team balance XP penalty - reduced XP when carried by high-level teammates
        float teamXPMult = TeamBalanceSystem.Instance.CalculateXPMultiplier(result.Player, result.Teammates);
        long preTeamBalanceExp = adjustedExp;
        if (teamXPMult < 1.0f)
        {
            adjustedExp = (long)(adjustedExp * teamXPMult);
        }

        // Apply rewards
        result.Player.Experience += adjustedExp;
        result.Player.Gold += adjustedGold;
        result.ExperienceGained = adjustedExp;
        result.GoldGained = adjustedGold;

        // Track peak gold
        result.Player.Statistics.RecordGoldChange(result.Player.Gold);

        // Track telemetry for multi-monster combat victory
        bool hasBoss = result.DefeatedMonsters.Any(m => m.IsBoss);
        TelemetrySystem.Instance.TrackCombat(
            "victory",
            result.Player.Level,
            result.DefeatedMonsters.Max(m => m.Level),
            result.DefeatedMonsters.Count,
            result.TotalDamageDealt,
            result.TotalDamageTaken,
            result.DefeatedMonsters.FirstOrDefault()?.Name,
            hasBoss,
            0, // Round count tracked separately in flee tracking
            result.Player.Class.ToString()
        );

        // Award experience to active companions (50% of player's XP)
        CompanionSystem.Instance?.AwardCompanionExperience(adjustedExp, terminal);

        // Award experience to NPC teammates (spouses/lovers) - 50% of player's XP
        AwardTeammateExperience(result.Teammates, adjustedExp, terminal);

        // Track gold collection for quests
        QuestSystem.OnGoldCollected(result.Player, adjustedGold);

        // Display rewards
        terminal.SetColor("yellow");
        terminal.WriteLine($"Defeated {result.DefeatedMonsters.Count} monster(s)!");
        terminal.WriteLine($"Experience gained: {adjustedExp}");

        // Show team balance XP penalty if applicable
        if (teamXPMult < 1.0f)
        {
            long xpLost = preTeamBalanceExp - adjustedExp;
            terminal.SetColor("yellow");
            terminal.WriteLine($"  (High-level ally penalty: -{xpLost} XP, {(int)(teamXPMult * 100)}% rate)");
        }

        // Show team bonus if applicable
        if (teamXPBonus > 0 || teamGoldBonus > 0)
        {
            terminal.SetColor("cyan");
            terminal.WriteLine($"  (Team bonus: +{teamXPBonus} XP, +{teamGoldBonus} gold)");
        }
        terminal.WriteLine($"Gold gained: {adjustedGold:N0}");

        // Show bonus from world events if any
        if (adjustedExp > totalExp || adjustedGold > totalGold)
        {
            terminal.SetColor("bright_cyan");
            if (adjustedExp > totalExp)
                terminal.WriteLine($"  (World event bonus: +{adjustedExp - totalExp} XP)");
            if (adjustedGold > totalGold)
                terminal.WriteLine($"  (World event bonus: +{adjustedGold - totalGold} gold)");
        }
        terminal.WriteLine("");

        // Show NPC teammate reactions after combat victory
        if (result.Teammates != null && result.Teammates.Count > 0)
        {
            foreach (var teammate in result.Teammates)
            {
                if (teammate is NPC npc && npc.IsAlive)
                {
                    string reaction = npc.GetReaction(result.Player as Player, "combat_victory");
                    if (!string.IsNullOrEmpty(reaction))
                    {
                        terminal.SetColor("cyan");
                        terminal.WriteLine($"  {npc.Name2}: \"{reaction}\"");
                    }
                }
            }
            terminal.WriteLine("");
        }

        // Check for equipment drop!
        await CheckForEquipmentDrop(result);

        // Check and award achievements after multi-monster combat
        // Use TotalDamageTaken from combat result to accurately track damage taken during THIS combat
        bool tookDamage = result.TotalDamageTaken > 0;
        double hpPercent = (double)result.Player.HP / result.Player.MaxHP;
        AchievementSystem.CheckCombatAchievements(result.Player, tookDamage, hpPercent);
        AchievementSystem.CheckAchievements(result.Player);
        await AchievementSystem.ShowPendingNotifications(terminal);

        await Task.Delay(GetCombatDelay(2000));

        // Soulweaver's Loom: heal 25% HP after each battle (50% during Manwe fight)
        ApplySoulweaverPostBattleHeal(result.Player);

        // Auto-heal with potions after combat, then offer to buy replacements
        AutoHealWithPotions(result.Player);
        AutoRestoreManaWithPotions(result.Player);

        // Monk encounter ONLY if requested
        if (offerMonkEncounter)
        {
            await OfferMonkPotionPurchase(result.Player);
        }

        result.CombatLog.Add($"Victory! Gained {adjustedExp} exp and {adjustedGold} gold from {result.DefeatedMonsters.Count} monsters");

        // Log combat end
        DebugLogger.Instance.LogCombatEnd("Victory", adjustedExp, adjustedGold, result.CombatLog.Count);

        // Auto-save after combat victory
        await SaveSystem.Instance.AutoSave(result.Player);
    }

    /// <summary>
    /// Handle partial victory (player escaped but defeated some monsters)
    /// </summary>
    private async Task HandlePartialVictory(CombatResult result, bool offerMonkEncounter)
    {
        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine($"You defeated {result.DefeatedMonsters.Count} monster(s) before escaping!");
        terminal.WriteLine("");

        // Calculate partial rewards
        long totalExp = 0;
        long totalGold = 0;

        foreach (var monster in result.DefeatedMonsters)
        {
            long baseExp = monster.Experience / 2; // Half exp for retreat
            long goldReward = monster.Gold;

            totalExp += baseExp;
            totalGold += goldReward;
        }

        // Apply world event modifiers
        long adjustedExp = WorldEventSystem.Instance.GetAdjustedXP(totalExp);
        long adjustedGold = WorldEventSystem.Instance.GetAdjustedGold(totalGold);

        // Apply difficulty modifiers
        float xpMult = DifficultySystem.GetExperienceMultiplier(DifficultySystem.CurrentDifficulty);
        float goldMult = DifficultySystem.GetGoldMultiplier(DifficultySystem.CurrentDifficulty);
        adjustedExp = (long)(adjustedExp * xpMult);
        adjustedGold = (long)(adjustedGold * goldMult);

        // Apply child XP bonus
        float childXPMult = FamilySystem.Instance?.GetChildXPMultiplier(result.Player) ?? 1.0f;
        if (childXPMult > 1.0f)
        {
            adjustedExp = (long)(adjustedExp * childXPMult);
        }

        // Team balance XP penalty - reduced XP when carried by high-level teammates
        float teamXPMult = TeamBalanceSystem.Instance.CalculateXPMultiplier(result.Player, result.Teammates);
        long preTeamBalanceExp = adjustedExp;
        if (teamXPMult < 1.0f)
        {
            adjustedExp = (long)(adjustedExp * teamXPMult);
        }

        result.Player.Experience += adjustedExp;
        result.Player.Gold += adjustedGold;

        // Award experience to active companions (50% of player's XP)
        CompanionSystem.Instance?.AwardCompanionExperience(adjustedExp, terminal);

        // Award experience to NPC teammates (spouses/lovers) - 50% of player's XP
        AwardTeammateExperience(result.Teammates, adjustedExp, terminal);

        terminal.WriteLine($"Experience gained: {adjustedExp}");

        // Show team balance XP penalty if applicable
        if (teamXPMult < 1.0f)
        {
            long xpLost = preTeamBalanceExp - adjustedExp;
            terminal.SetColor("yellow");
            terminal.WriteLine($"  (High-level ally penalty: -{xpLost} XP, {(int)(teamXPMult * 100)}% rate)");
        }
        terminal.WriteLine($"Gold gained: {adjustedGold:N0}");

        // Show bonus from world events if any
        if (adjustedExp > totalExp || adjustedGold > totalGold)
        {
            terminal.SetColor("bright_cyan");
            if (adjustedExp > totalExp)
                terminal.WriteLine($"  (World event bonus: +{adjustedExp - totalExp} XP)");
            if (adjustedGold > totalGold)
                terminal.WriteLine($"  (World event bonus: +{adjustedGold - totalGold} gold)");
        }
        terminal.WriteLine("");

        await Task.Delay(GetCombatDelay(2000));

        // Soulweaver's Loom: heal 25% HP after each battle (50% during Manwe fight)
        ApplySoulweaverPostBattleHeal(result.Player);

        // Auto-restore with potions after partial victory
        AutoHealWithPotions(result.Player);
        AutoRestoreManaWithPotions(result.Player);

        // No monk encounter on escape, even if some monsters were defeated
        // (to avoid the issue where monk appears mid-fight)

        result.CombatLog.Add($"Escaped after defeating {result.DefeatedMonsters.Count} monsters");
    }

    /// <summary>
    /// Handle player death with resurrection options
    /// </summary>
    private async Task HandlePlayerDeath(CombatResult result)
    {
        terminal.ClearScreen();

        // Display dramatic death art
        await UsurperRemake.UI.ANSIArt.DisplayArtAnimated(terminal, UsurperRemake.UI.ANSIArt.Death, 60);
        terminal.WriteLine("");
        await Task.Delay(GetCombatDelay(1000));

        // Show NPC teammate reactions to player death
        if (result.Teammates != null && result.Teammates.Count > 0)
        {
            foreach (var teammate in result.Teammates)
            {
                if (teammate is NPC npc && npc.IsAlive)
                {
                    string reaction = npc.GetReaction(result.Player as Player, "ally_death");
                    if (!string.IsNullOrEmpty(reaction))
                    {
                        terminal.SetColor("cyan");
                        terminal.WriteLine($"  {npc.Name2}: \"{reaction}\"");
                    }
                }
            }
            terminal.WriteLine("");
            await Task.Delay(GetCombatDelay(1500));
        }

        result.Player.HP = 0;
        result.Player.MDefeats++;
        result.CombatLog.Add($"Player killed by {result.Monster?.Name ?? "opponent"}");

        // Log player death (use monster level as proxy for floor depth)
        DebugLogger.Instance.LogPlayerDeath(result.Player.Name, result.Monster?.Name ?? "unknown", result.Monster?.Level ?? 0);

        // Track statistics - death (not from player)
        result.Player.Statistics.RecordDeath(false);

        // Queue Stranger encounter after first death
        if (result.Player.Statistics.TotalMonsterDeaths == 1)
        {
            StrangerEncounterSystem.Instance.QueueScriptedEncounter(ScriptedEncounterType.AfterFirstDeath);
        }
        StrangerEncounterSystem.Instance.RecordGameEvent(StrangerContextEvent.PlayerDied);

        // Track telemetry for player death
        TelemetrySystem.Instance.TrackDeath(
            result.Player.Level,
            result.Monster?.Name ?? "unknown",
            result.Monster?.Level ?? 0 // use monster level as proxy for dungeon depth
        );

        // Present resurrection options
        var resurrectionResult = await PresentResurrectionChoices(result);

        if (resurrectionResult.WasResurrected)
        {
            result.Player.HP = resurrectionResult.RestoredHP;
            result.Outcome = CombatOutcome.PlayerEscaped; // Continue as escaped rather than died
            result.ShouldReturnToTemple = resurrectionResult.ShouldReturnToTemple;
            terminal.SetColor("green");
            terminal.WriteLine("");
            terminal.WriteLine("You gasp as life flows back into your body!");
            terminal.WriteLine($"You have been resurrected with {result.Player.HP} HP!");
            if (resurrectionResult.ShouldReturnToTemple)
            {
                terminal.WriteLine("You awaken at the temple...");
            }
            terminal.WriteLine("");
            result.CombatLog.Add($"Player resurrected via {resurrectionResult.Method}");
        }
        else
        {
            // True death - apply penalties and return to temple
            await ApplyDeathPenalties(result);
            result.ShouldReturnToTemple = true; // Player resurrects at temple after death
        }
    }

    /// <summary>
    /// Present resurrection choices to the player
    /// </summary>
    private async Task<ResurrectionResult> PresentResurrectionChoices(CombatResult result)
    {
        var player = result.Player;
        var choices = new List<ResurrectionChoice>();

        // Option 1: Divine Intervention (if has resurrections)
        if (player.Resurrections > 0)
        {
            choices.Add(new ResurrectionChoice
            {
                Name = "Divine Intervention",
                Description = $"Call upon the gods for resurrection ({player.Resurrections} remaining)",
                Cost = 0,
                HPRestored = (int)(player.MaxHP * 0.5), // 50% HP
                Method = "Divine Intervention",
                UsesResurrection = true,
                RequiresGold = false
            });
        }

        // Option 2: Temple Resurrection (costs gold, returns to temple)
        long templeCost = 500 + (player.Level * 100);
        if (player.Gold >= templeCost || player.BankGold >= templeCost)
        {
            choices.Add(new ResurrectionChoice
            {
                Name = "Temple Resurrection",
                Description = $"Pay the temple {templeCost:N0} gold for resurrection (returns to Temple)",
                Cost = templeCost,
                HPRestored = (int)(player.MaxHP * 0.75), // 75% HP
                Method = "Temple Resurrection",
                UsesResurrection = false,
                RequiresGold = true,
                ReturnsToTemple = true
            });
        }

        // Option 3: Deal with Death (if high enough level and has darkness)
        if (player.Level >= 5 && player.Darkness >= 100)
        {
            choices.Add(new ResurrectionChoice
            {
                Name = "Deal with Death",
                Description = "Bargain with the reaper (costs Darkness, permanent stat loss)",
                Cost = 0,
                HPRestored = (int)(player.MaxHP * 0.25), // 25% HP
                Method = "Dark Bargain",
                UsesResurrection = false,
                RequiresGold = false,
                IsDarkBargain = true
            });
        }

        // Option 4: Accept Death
        choices.Add(new ResurrectionChoice
        {
            Name = "Accept Your Fate",
            Description = "Accept death and face the consequences",
            Cost = 0,
            HPRestored = 0,
            Method = "Death Accepted",
            UsesResurrection = false,
            AcceptsDeath = true
        });

        // Present choices
        terminal.SetColor("yellow");
        terminal.WriteLine("╔════════════════════════════════════════╗");
        terminal.WriteLine("║         THE VEIL BETWEEN WORLDS        ║");
        terminal.WriteLine("╚════════════════════════════════════════╝");
        terminal.WriteLine("");
        terminal.WriteLine("You stand at the threshold between life and death.");
        terminal.WriteLine("Choose your path:");
        terminal.WriteLine("");

        for (int i = 0; i < choices.Count; i++)
        {
            var choice = choices[i];
            terminal.SetColor("cyan");
            terminal.WriteLine($"[{i + 1}] {choice.Name}");
            terminal.SetColor("white");
            terminal.WriteLine($"    {choice.Description}");
            terminal.WriteLine("");
        }

        terminal.SetColor("yellow");
        terminal.Write("Your choice: ");

        // Get player choice
        int selectedIndex = -1;
        while (selectedIndex < 0 || selectedIndex >= choices.Count)
        {
            var input = await terminal.GetCharAsync();
            if (int.TryParse(input.ToString(), out int num) && num >= 1 && num <= choices.Count)
            {
                selectedIndex = num - 1;
            }
        }

        terminal.WriteLine((selectedIndex + 1).ToString());
        terminal.WriteLine("");

        var selectedChoice = choices[selectedIndex];

        // Handle the choice
        if (selectedChoice.AcceptsDeath)
        {
            terminal.SetColor("red");
            terminal.WriteLine("You accept your fate...");
            terminal.WriteLine("The darkness claims you.");
            return new ResurrectionResult { WasResurrected = false };
        }

        if (selectedChoice.UsesResurrection)
        {
            player.Resurrections--;
            player.ResurrectionsUsed++;
            player.LastResurrection = DateTime.Now;
            player.Statistics.RecordResurrection();
            terminal.SetColor("white");
            terminal.WriteLine("A brilliant light pierces the darkness!");
            terminal.WriteLine("The gods have heard your prayers!");
        }
        else if (selectedChoice.RequiresGold)
        {
            // Deduct gold from bank first, then cash
            if (player.BankGold >= selectedChoice.Cost)
            {
                player.BankGold -= selectedChoice.Cost;
            }
            else
            {
                player.Gold -= selectedChoice.Cost;
            }
            terminal.SetColor("white");
            terminal.WriteLine("Temple priests chant sacred words...");
            terminal.WriteLine("Their magic pulls your soul back from the void!");
        }
        else if (selectedChoice.IsDarkBargain)
        {
            // Dark bargain - costs darkness and a permanent stat reduction
            player.Darkness -= 50;
            var random = new Random();
            int statLoss = 1 + random.Next(3);

            // Reduce a random stat permanently
            switch (random.Next(6))
            {
                case 0: player.Strength = Math.Max(1, player.Strength - statLoss); break;
                case 1: player.Defence = Math.Max(1, player.Defence - statLoss); break;
                case 2: player.Stamina = Math.Max(1, player.Stamina - statLoss); break;
                case 3: player.Agility = Math.Max(1, player.Agility - statLoss); break;
                case 4: player.Charisma = Math.Max(1, player.Charisma - statLoss); break;
                case 5: player.MaxHP = Math.Max(10, player.MaxHP - (statLoss * 5)); break;
            }

            terminal.SetColor("magenta");
            terminal.WriteLine("You feel a cold presence...");
            terminal.WriteLine("\"Very well, mortal. But this bargain has a price...\"");
            terminal.WriteLine($"You feel yourself grow weaker... (-{statLoss} to a random stat)");
        }

        return new ResurrectionResult
        {
            WasResurrected = true,
            RestoredHP = selectedChoice.HPRestored,
            Method = selectedChoice.Method,
            ShouldReturnToTemple = selectedChoice.ReturnsToTemple
        };
    }

    /// <summary>
    /// Apply death penalties when player truly dies
    /// </summary>
    private async Task ApplyDeathPenalties(CombatResult result)
    {
        var player = result.Player;
        var random = new Random();

        terminal.SetColor("red");
        terminal.WriteLine("");
        terminal.WriteLine("Death claims you...");
        terminal.WriteLine("");
        await Task.Delay(GetCombatDelay(1000));

        // Experience loss (10-20%)
        long expLoss = (long)(player.Experience * (0.1 + random.NextDouble() * 0.1));
        player.Experience = Math.Max(0, player.Experience - expLoss);
        terminal.WriteLine($"You lose {expLoss:N0} experience points!");

        // Gold loss (drop 50-75%) — Crown Tax Exemption reduces by 20%
        double goldLossRate = 0.5 + random.NextDouble() * 0.25;
        float taxExemption = FactionSystem.Instance?.GetTaxExemptionRate() ?? 0f;
        if (taxExemption > 0) goldLossRate *= (1.0 - taxExemption);
        long goldLoss = (long)(player.Gold * goldLossRate);
        player.Gold = Math.Max(0, player.Gold - goldLoss);
        if (goldLoss > 0)
        {
            terminal.WriteLine($"You drop {goldLoss:N0} gold!");
        }

        // Small chance to lose an item
        if (player.Item != null && player.Item.Count > 0 && random.Next(100) < 20)
        {
            int itemIndex = random.Next(player.Item.Count);
            player.Item.RemoveAt(itemIndex);
            if (player.ItemType != null && player.ItemType.Count > itemIndex)
            {
                player.ItemType.RemoveAt(itemIndex);
            }
            terminal.WriteLine("An item slips from your grasp as you fall!");
        }

        terminal.WriteLine("");
        terminal.WriteLine("You will resurrect at the temple with 1 HP...");
        player.HP = 1; // Resurrect with 1 HP at temple
        player.Mana = 0; // No mana

        // Generate death news for the realm
        string killerName = result.Monster?.Name ?? "unknown forces";
        string location = player.CurrentLocation ?? "the dungeons";
        NewsSystem.Instance?.WriteDeathNews(player.DisplayName, killerName, location);
    }

    /// <summary>
    /// Resurrection choice data structure
    /// </summary>
    private class ResurrectionChoice
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public long Cost { get; set; }
        public int HPRestored { get; set; }
        public string Method { get; set; } = "";
        public bool UsesResurrection { get; set; }
        public bool RequiresGold { get; set; }
        public bool IsDarkBargain { get; set; }
        public bool AcceptsDeath { get; set; }
        public bool ReturnsToTemple { get; set; }
    }

    /// <summary>
    /// Resurrection result
    /// </summary>
    private class ResurrectionResult
    {
        public bool WasResurrected { get; set; }
        public int RestoredHP { get; set; }
        public string Method { get; set; } = "";
        public bool ShouldReturnToTemple { get; set; } = false;
    }
    
    /// <summary>
    /// Show combat status
    /// </summary>
    private async Task ShowCombatStatus(Character player, CombatResult result)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("Combat Status");
        terminal.WriteLine("=============");
        terminal.WriteLine("");
        
        terminal.SetColor("white");
        terminal.WriteLine($"Name: {player.DisplayName}");
        terminal.WriteLine($"HP: {player.HP}/{player.MaxHP}");
        terminal.WriteLine($"Strength: {player.Strength}");
        terminal.WriteLine($"Defence: {player.Defence}");
        terminal.WriteLine($"Weapon Power: {player.WeapPow}");
        terminal.WriteLine($"Armor Power: {player.ArmPow}");
        
        // Surface active status effects here as well
        if (player.ActiveStatuses.Count > 0 || player.IsRaging)
        {
            var effects = new List<string>();
            foreach (var kv in player.ActiveStatuses)
            {
                effects.Add($"{kv.Key} ({kv.Value})");
            }
            if (player.IsRaging && !effects.Any(e => e.StartsWith("Raging")))
                effects.Add("Raging");

            terminal.WriteLine($"Active Effects: {string.Join(", ", effects)}");
        }
        
        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }
    
    /// <summary>
    /// Fight to Death (Berserker) mode - all-out offense, no defense, no mercy
    /// Player attacks continuously with doubled damage until one side dies
    /// Cannot flee, cannot heal, cannot surrender
    /// </summary>
    private async Task ExecuteFightToDeath(Character player, Monster monster, CombatResult result)
    {
        terminal.SetColor("bright_red");
        terminal.WriteLine("╔════════════════════════════════════════╗");
        terminal.WriteLine("║     YOU ENTER A BERSERKER RAGE!        ║");
        terminal.WriteLine("╚════════════════════════════════════════╝");
        terminal.SetColor("red");
        terminal.WriteLine("Your eyes turn red with fury! No retreat, no mercy!");
        terminal.WriteLine("You will fight until death - yours or theirs!");
        terminal.WriteLine("");
        await Task.Delay(GetCombatDelay(1500));

        // Set berserker status
        player.IsRaging = true;
        result.CombatLog.Add("Player enters berserker rage - Fight to Death!");

        int round = 0;
        while (player.HP > 0 && monster.HP > 0)
        {
            round++;
            terminal.SetColor("red");
            terminal.WriteLine($"═══ RAGE ROUND {round} ═══");

            // Player attacks with berserker fury (doubled damage, more attacks)
            int rageAttacks = Math.Max(2, GetAttackCount(player) + 1); // At least 2 attacks, +1 bonus

            for (int i = 0; i < rageAttacks && monster.HP > 0; i++)
            {
                // Berserker attack - base damage * 2, ignore defense partially
                long berserkerPower = player.Strength * 2 + player.WeapPow * 2 + random.Next(1, 31);

                // Apply drug effects (stacks with rage)
                var drugEffects = DrugSystem.GetDrugEffects(player);
                if (drugEffects.DamageBonus > 0)
                    berserkerPower = (long)(berserkerPower * (1.0 + drugEffects.DamageBonus / 100.0));

                // Monster takes reduced defense in berserker attack (player's fury overwhelms)
                long monsterDef = monster.GetDefensePower() / 2;
                long damage = Math.Max(5, berserkerPower - monsterDef);

                // Critical rage hits (25% chance for triple damage)
                bool isCriticalFury = random.Next(100) < 25;
                if (isCriticalFury)
                {
                    damage *= 3;
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine($"  CRITICAL FURY! You strike {monster.Name} for {damage} damage!");
                }
                else
                {
                    terminal.SetColor("bright_red");
                    terminal.WriteLine($"  You savagely attack {monster.Name} for {damage} damage!");
                }

                monster.HP -= damage;
                result.TotalDamageDealt += damage;
                player.Statistics.RecordDamageDealt(damage, isCriticalFury);

                if (monster.HP <= 0)
                {
                    terminal.SetColor("bright_green");
                    terminal.WriteLine("");
                    terminal.WriteLine($"You tear {monster.Name} apart in your fury!");
                    terminal.WriteLine("The blood rage subsides as your enemy falls...");
                    result.Victory = true;
                    result.MonsterKilled = true;
                    break;
                }
            }

            if (monster.HP <= 0) break;

            // Monster counterattack - hits harder against undefended berserker
            terminal.SetColor("dark_red");
            terminal.WriteLine("");
            long monsterAttack = monster.GetAttackPower() + random.Next(1, 16);

            // Berserker has NO defense (ignored in rage)
            long playerDef = random.Next(1, 11); // Minimal defense from pure luck
            long monsterDamage = Math.Max(3, monsterAttack - playerDef);

            // Monster gets bonus damage vs berserker (50% more)
            monsterDamage = (long)(monsterDamage * 1.5);

            terminal.WriteLine($"  {monster.Name} strikes your undefended body for {monsterDamage} damage!");
            player.HP -= monsterDamage;
            result.TotalDamageTaken += monsterDamage;
            player.Statistics.RecordDamageTaken(monsterDamage);

            // Show HP status
            terminal.SetColor("gray");
            terminal.WriteLine($"  Your HP: {player.HP}/{player.MaxHP} | {monster.Name} HP: {monster.HP}/{monster.MaxHP}");

            if (player.HP <= 0)
            {
                terminal.SetColor("dark_red");
                terminal.WriteLine("");
                terminal.WriteLine("Your berserker rage was not enough...");
                terminal.WriteLine("You fall in glorious battle!");
                result.Victory = false;
                result.PlayerDied = true;
                break;
            }

            await Task.Delay(GetCombatDelay(600));
        }

        // End berserker state
        player.IsRaging = false;

        // HP drain after rage (exhaustion)
        if (player.HP > 0)
        {
            long exhaustion = Math.Min(player.HP - 1, player.MaxHP / 10);
            player.HP -= exhaustion;
            terminal.SetColor("gray");
            terminal.WriteLine($"The rage subsides, leaving you exhausted. (-{exhaustion} HP)");
        }

        await Task.Delay(GetCombatDelay(1000));
    }
    
    /// <summary>
    /// Use healing potions during combat - Pascal PLVSMON.PAS potion system
    /// Potions heal to full HP and use only the amount needed
    /// </summary>
    private async Task ExecuteUseItem(Character player, CombatResult result)
    {
        bool hasHealing = player.Healing > 0 && player.HP < player.MaxHP;
        bool hasMana = player.ManaPotions > 0 && player.Mana < player.MaxMana;

        // If player has both types, let them choose
        if (hasHealing && hasMana)
        {
            terminal.WriteLine("Which potion?", "cyan");
            terminal.WriteLine($"  (H) Healing Potion  ({player.Healing}/{player.MaxPotions})", "green");
            terminal.WriteLine($"  (M) Mana Potion     ({player.ManaPotions}/{player.MaxManaPotions})", "blue");
            string choice = await terminal.GetInput("> ");
            if (choice.Equals("M", StringComparison.OrdinalIgnoreCase))
            {
                await ExecuteUseManaPotion(player, result);
                return;
            }
            // Default to healing potion
        }
        else if (!hasHealing && hasMana)
        {
            // Only mana potions available
            await ExecuteUseManaPotion(player, result);
            return;
        }
        else if (!hasHealing && !hasMana)
        {
            terminal.WriteLine("You have no usable potions!", "yellow");
            await Task.Delay(GetCombatDelay(1000));
            return;
        }

        // Healing potion logic
        if (player.Healing <= 0)
        {
            terminal.WriteLine("You have no healing potions!", "yellow");
            await Task.Delay(GetCombatDelay(1000));
            return;
        }
        if (player.HP >= player.MaxHP)
        {
            terminal.WriteLine("You are already at full health!", "yellow");
            await Task.Delay(GetCombatDelay(1000));
            return;
        }

        long hpNeeded = player.MaxHP - player.HP;
        int healPerPotion = 30 + player.Level * 5 + random.Next(10, 30);
        int potionsNeeded = (int)Math.Ceiling((double)hpNeeded / healPerPotion);
        potionsNeeded = Math.Min(potionsNeeded, (int)player.Healing);
        long actualHealing = Math.Min((long)potionsNeeded * healPerPotion, hpNeeded);
        player.HP += actualHealing;
        player.HP = Math.Min(player.HP, player.MaxHP);
        player.Healing -= potionsNeeded;

        terminal.WriteLine($"You drink {potionsNeeded} healing potion{(potionsNeeded > 1 ? "s" : "")} and recover {actualHealing} HP!", "green");
        terminal.WriteLine($"Potions remaining: {player.Healing}/{player.MaxPotions}", "cyan");
        result.CombatLog.Add($"Player used {potionsNeeded} healing potion(s) for {actualHealing} HP");
        await Task.Delay(GetCombatDelay(1500));
    }

    private async Task ExecuteUseManaPotion(Character player, CombatResult result)
    {
        if (player.ManaPotions <= 0)
        {
            terminal.WriteLine("You have no mana potions!", "yellow");
            await Task.Delay(GetCombatDelay(1000));
            return;
        }
        if (player.Mana >= player.MaxMana)
        {
            terminal.WriteLine("Your mana is already full!", "yellow");
            await Task.Delay(GetCombatDelay(1000));
            return;
        }

        long manaRestore = 30 + player.Level * 5;
        long manaNeeded = player.MaxMana - player.Mana;
        long actualRestore = Math.Min(manaRestore, manaNeeded);
        player.Mana += actualRestore;
        player.ManaPotions--;

        terminal.WriteLine($"You drink a mana potion and recover {actualRestore} MP!", "blue");
        terminal.WriteLine($"Mana potions remaining: {player.ManaPotions}/{player.MaxManaPotions}", "cyan");
        result.CombatLog.Add($"Player used mana potion for {actualRestore} MP");
        await Task.Delay(GetCombatDelay(1500));
    }
    
    /// <summary>
    /// Execute spell-casting action. Leverages the rich ProcessSpellCasting helper which already
    /// contains the Pascal-compatible spell selection UI and effect application logic.
    /// </summary>
    private async Task ExecuteCastSpell(Character player, Monster monster, CombatResult result)
    {
        // Prevent double-casting in a single round – mirrors original flag from VARIOUS.PAS
        if (player.Casted)
        {
            terminal.WriteLine("You have already cast a spell this round!", "yellow");
            await Task.Delay(GetCombatDelay(1000));
            return;
        }

        // Delegate to the existing spell-handling UI/logic
        ProcessSpellCasting(player, monster);

        // Mark that the player used their casting action this turn so other systems (AI, etc.)
        // can react accordingly.
        player.Casted = true;

        // Add entry to combat log for post-battle analysis and testing.
        result.CombatLog.Add($"{player.DisplayName} casts a spell.");

        // Small delay to keep pacing consistent with other combat actions.
        await Task.Delay(GetCombatDelay(500));
    }

    /// <summary>
    /// Execute ability usage action for non-caster classes.
    /// Shows ability selection menu and applies the selected ability's effects.
    /// </summary>
    private async Task ExecuteUseAbility(Character player, Monster monster, CombatResult result)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("═══ COMBAT ABILITIES ═══");
        terminal.WriteLine("");

        // Get available abilities for this character
        var availableAbilities = ClassAbilitySystem.GetAvailableAbilities(player);

        if (availableAbilities.Count == 0)
        {
            terminal.WriteLine("You haven't learned any abilities yet!", "red");
            terminal.WriteLine("Train at the Level Master to unlock abilities as you level up.", "yellow");
            await Task.Delay(GetCombatDelay(2000));
            return;
        }

        // Display available abilities with cooldown and stamina info
        terminal.SetColor("cyan");
        terminal.WriteLine($"Combat Stamina: {player.CurrentCombatStamina}/{player.MaxCombatStamina}");
        terminal.WriteLine("");
        terminal.WriteLine("Available Abilities:", "white");
        terminal.WriteLine("");

        int displayIndex = 1;
        var selectableAbilities = new List<ClassAbilitySystem.ClassAbility>();

        foreach (var ability in availableAbilities)
        {
            bool canUse = ClassAbilitySystem.CanUseAbility(player, ability.Id, abilityCooldowns);
            bool hasStamina = player.HasEnoughStamina(ability.StaminaCost);
            bool onCooldown = abilityCooldowns.TryGetValue(ability.Id, out int cooldownLeft) && cooldownLeft > 0;

            string statusText = "";
            string color = "white";

            if (onCooldown)
            {
                statusText = $" [Cooldown: {cooldownLeft} rounds]";
                color = "dark_gray";
            }
            else if (!hasStamina)
            {
                statusText = $" [Need {ability.StaminaCost} stamina, have {player.CurrentCombatStamina}]";
                color = "dark_gray";
            }

            terminal.SetColor(color);
            terminal.WriteLine($"  {displayIndex}. {ability.Name} - {ability.StaminaCost} stamina{statusText}");
            terminal.SetColor("gray");
            terminal.WriteLine($"     {ability.Description}");

            selectableAbilities.Add(ability);
            displayIndex++;
        }

        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.Write("Enter ability number (0 to cancel): ");
        string input = terminal.GetInputSync();

        if (!int.TryParse(input, out int choice) || choice < 1 || choice > selectableAbilities.Count)
        {
            terminal.WriteLine("Cancelled.", "gray");
            await Task.Delay(GetCombatDelay(500));
            return;
        }

        var selectedAbility = selectableAbilities[choice - 1];

        // Verify we can actually use it
        if (!ClassAbilitySystem.CanUseAbility(player, selectedAbility.Id, abilityCooldowns))
        {
            if (abilityCooldowns.TryGetValue(selectedAbility.Id, out int cd) && cd > 0)
            {
                terminal.WriteLine($"{selectedAbility.Name} is on cooldown for {cd} more rounds!", "red");
            }
            await Task.Delay(GetCombatDelay(1500));
            return;
        }

        // Check stamina cost
        if (!player.HasEnoughStamina(selectedAbility.StaminaCost))
        {
            terminal.WriteLine($"Not enough stamina! Need {selectedAbility.StaminaCost}, have {player.CurrentCombatStamina}.", "red");
            await Task.Delay(GetCombatDelay(1500));
            return;
        }

        // Deduct stamina cost
        player.SpendStamina(selectedAbility.StaminaCost);
        terminal.SetColor("cyan");
        terminal.WriteLine($"(-{selectedAbility.StaminaCost} stamina, {player.CurrentCombatStamina}/{player.MaxCombatStamina} remaining)");

        // Execute the ability
        var abilityResult = ClassAbilitySystem.UseAbility(player, selectedAbility.Id, random);

        terminal.WriteLine("");
        terminal.SetColor("bright_magenta");
        terminal.WriteLine(abilityResult.Message);

        // Apply ability effects
        await ApplyAbilityEffects(player, monster, abilityResult, result);

        // Set cooldown
        if (abilityResult.CooldownApplied > 0)
        {
            abilityCooldowns[selectedAbility.Id] = abilityResult.CooldownApplied;
        }

        // Display training improvement message if ability proficiency increased
        if (abilityResult.SkillImproved && !string.IsNullOrEmpty(abilityResult.NewProficiencyLevel))
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"  Your {selectedAbility.Name} proficiency improved to {abilityResult.NewProficiencyLevel}!");
            await Task.Delay(GetCombatDelay(800));
        }

        // Log the action
        result.CombatLog.Add($"{player.DisplayName} uses {selectedAbility.Name}");

        await Task.Delay(GetCombatDelay(1000));
    }

    /// <summary>
    /// Apply the effects of a class ability to combat
    /// </summary>
    private async Task ApplyAbilityEffects(Character player, Monster monster, ClassAbilityResult abilityResult, CombatResult result)
    {
        var ability = abilityResult.AbilityUsed;
        if (ability == null) return;

        // Apply damage
        if (abilityResult.Damage > 0 && monster != null)
        {
            long actualDamage = abilityResult.Damage;

            // Handle special damage effects
            if (abilityResult.SpecialEffect == "execute" && monster.HP < monster.MaxHP * 0.3)
            {
                actualDamage *= 2;
                terminal.WriteLine("EXECUTION! Double damage to wounded enemy!", "bright_red");
            }
            else if (abilityResult.SpecialEffect == "last_stand" && player.HP < player.MaxHP * 0.25)
            {
                actualDamage = (long)(actualDamage * 1.5);
                terminal.WriteLine("LAST STAND! Desperation fuels your attack!", "bright_red");
            }
            else if (abilityResult.SpecialEffect == "armor_pierce")
            {
                // Ignore defense for acid splash
                terminal.WriteLine("The acid ignores armor!", "green");
            }
            else if (abilityResult.SpecialEffect == "backstab")
            {
                // Backstab bonus if monster hasn't attacked yet
                actualDamage = (long)(actualDamage * 1.5);
                terminal.WriteLine("Critical strike from the shadows!", "bright_yellow");
            }

            // Apply defense unless armor_pierce
            if (abilityResult.SpecialEffect != "armor_pierce")
            {
                long defense = monster.Defence / 2; // Abilities partially bypass defense
                actualDamage = Math.Max(1, actualDamage - defense);
            }

            monster.HP -= actualDamage;
            result.TotalDamageDealt += actualDamage;
            player.Statistics.RecordDamageDealt(actualDamage, false);

            terminal.SetColor("bright_red");
            terminal.WriteLine($"You deal {actualDamage} damage to {monster.Name}!");

            if (monster.HP <= 0)
            {
                terminal.WriteLine($"{monster.Name} is slain!", "bright_green");
            }
        }

        // Apply healing
        if (abilityResult.Healing > 0)
        {
            long actualHealing = Math.Min(abilityResult.Healing, player.MaxHP - player.HP);
            player.HP += actualHealing;

            terminal.SetColor("bright_green");
            terminal.WriteLine($"You recover {actualHealing} HP!");
        }

        // Apply buffs (temporary combat bonuses stored on player)
        if (abilityResult.AttackBonus > 0 || abilityResult.DefenseBonus != 0)
        {
            // Store buff info - these will be applied to next attacks/defense
            // For simplicity, we'll add them directly to temp stats
            if (abilityResult.AttackBonus > 0)
            {
                player.TempAttackBonus = abilityResult.AttackBonus;
                player.TempAttackBonusDuration = abilityResult.Duration;
                terminal.WriteLine($"Attack increased by {abilityResult.AttackBonus} for {abilityResult.Duration} rounds!", "cyan");
            }

            if (abilityResult.DefenseBonus > 0)
            {
                player.TempDefenseBonus = abilityResult.DefenseBonus;
                player.TempDefenseBonusDuration = abilityResult.Duration;
                terminal.WriteLine($"Defense increased by {abilityResult.DefenseBonus} for {abilityResult.Duration} rounds!", "cyan");
            }
            else if (abilityResult.DefenseBonus < 0)
            {
                // Rage reduces defense
                player.TempDefenseBonus = abilityResult.DefenseBonus;
                player.TempDefenseBonusDuration = abilityResult.Duration;
                terminal.WriteLine($"Defense reduced by {-abilityResult.DefenseBonus} (rage)!", "yellow");
            }
        }

        // Handle special effects
        switch (abilityResult.SpecialEffect)
        {
            case "escape":
                terminal.WriteLine("You vanish in a puff of smoke!", "magenta");
                globalEscape = true;
                break;

            case "stun":
                if (monster != null && random.Next(100) < 60)
                {
                    monster.Stunned = true;
                    terminal.WriteLine($"{monster.Name} is stunned!", "yellow");
                }
                break;

            case "poison":
                if (monster != null)
                {
                    monster.Poisoned = true;
                    terminal.WriteLine($"{monster.Name} is poisoned!", "green");
                }
                break;

            case "distract":
                if (monster != null)
                {
                    monster.Distracted = true;
                    terminal.WriteLine($"{monster.Name} is distracted and will have reduced accuracy!", "yellow");
                }
                break;

            case "charm":
                if (monster != null && random.Next(100) < 40)
                {
                    monster.Charmed = true;
                    terminal.WriteLine($"{monster.Name} is charmed and may hesitate to attack!", "magenta");
                }
                break;

            case "smoke":
                terminal.WriteLine("A cloud of smoke obscures you from attack!", "gray");
                player.TempDefenseBonus += 40;
                player.TempDefenseBonusDuration = Math.Max(player.TempDefenseBonusDuration, 2);
                break;

            case "rage":
                player.IsRaging = true;
                terminal.WriteLine("BERSERKER RAGE! You enter a blood fury!", "bright_red");
                break;

            case "dodge_next":
                player.DodgeNextAttack = true;
                terminal.WriteLine("You prepare to dodge the next attack!", "cyan");
                break;

            case "inspire":
                player.TempAttackBonus += 10;
                player.TempAttackBonusDuration = Math.Max(player.TempAttackBonusDuration, 3);
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("The melody steels your resolve! (+10 Attack for 3 rounds)");
                break;

            case "resist_all":
                player.HasStatusImmunity = true;
                player.StatusImmunityDuration = abilityResult.Duration > 0 ? abilityResult.Duration : 3;
                terminal.SetColor("bright_white");
                terminal.WriteLine("Your will becomes unbreakable! Status effects cannot touch you!");
                break;

            case "reckless":
                player.TempDefenseBonus -= 20;
                player.TempDefenseBonusDuration = Math.Max(player.TempDefenseBonusDuration, 1);
                terminal.SetColor("bright_red");
                terminal.WriteLine("Reckless swing! You leave yourself exposed!");
                break;

            case "desperate":
                if (player.HP < player.MaxHP * 0.4 && monster != null && monster.IsAlive)
                {
                    long desperateDmg = (long)(abilityResult.Damage * 0.5);
                    desperateDmg = Math.Max(1, desperateDmg - monster.Defence / 4);
                    monster.HP -= desperateDmg;
                    result.TotalDamageDealt += desperateDmg;
                    player.Statistics.RecordDamageDealt(desperateDmg, false);
                    terminal.SetColor("bright_red");
                    terminal.WriteLine($"DESPERATION! {desperateDmg} bonus damage!");
                    if (monster.HP <= 0)
                    {
                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"{monster.Name} is slain!");
                    }
                }
                break;

            case "fire":
                if (monster != null && monster.IsAlive)
                {
                    bool fireVulnerable = monster.MonsterClass == MonsterClass.Plant || monster.MonsterClass == MonsterClass.Beast;
                    if (fireVulnerable)
                    {
                        long fireBonusDmg = abilityResult.Damage / 2;
                        monster.HP -= fireBonusDmg;
                        result.TotalDamageDealt += fireBonusDmg;
                        player.Statistics.RecordDamageDealt(fireBonusDmg, false);
                        terminal.SetColor("bright_red");
                        terminal.WriteLine($"The fire burns intensely! {fireBonusDmg} bonus damage!");
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine("The bomb explodes in a burst of flame!");
                    }
                    monster.Poisoned = true;
                    monster.PoisonRounds = Math.Max(monster.PoisonRounds, 2);
                }
                break;

            case "holy":
                if (monster != null && monster.IsAlive &&
                    (monster.MonsterClass == MonsterClass.Undead || monster.MonsterClass == MonsterClass.Demon || monster.Undead > 0))
                {
                    long holyBonusDmg = abilityResult.Damage;
                    monster.HP -= holyBonusDmg;
                    result.TotalDamageDealt += holyBonusDmg;
                    player.Statistics.RecordDamageDealt(holyBonusDmg, false);
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine($"HOLY SMITE! The undead burns with divine fire! +{holyBonusDmg} damage!");
                }
                else
                {
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine("Divine energy radiates from your weapon!");
                }
                break;

            case "holy_avenger":
                if (monster != null && monster.IsAlive &&
                    (monster.MonsterClass == MonsterClass.Undead || monster.MonsterClass == MonsterClass.Demon || monster.Undead > 0))
                {
                    long avengerBonusDmg = (long)(abilityResult.Damage * 0.75);
                    monster.HP -= avengerBonusDmg;
                    result.TotalDamageDealt += avengerBonusDmg;
                    player.Statistics.RecordDamageDealt(avengerBonusDmg, false);
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine($"DIVINE VENGEANCE! +{avengerBonusDmg} holy damage!");
                }
                {
                    long avengerHeal = Math.Min(player.Level * 2, player.MaxHP - player.HP);
                    if (avengerHeal > 0)
                    {
                        player.HP += avengerHeal;
                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"Divine energy heals you for {avengerHeal} HP!");
                    }
                }
                break;

            case "critical":
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("You strike a vital point!");
                break;

            case "fury":
                player.IsRaging = true;
                if (!player.ActiveStatuses.ContainsKey(StatusEffect.Raging))
                    player.ActiveStatuses[StatusEffect.Raging] = abilityResult.Duration > 0 ? abilityResult.Duration : 3;
                terminal.SetColor("bright_red");
                terminal.WriteLine("THE WAR GOD'S FURY BURNS WITHIN YOU!");
                break;

            case "champion":
                {
                    long champHeal = Math.Min(player.Level * 3, player.MaxHP - player.HP);
                    if (champHeal > 0)
                    {
                        player.HP += champHeal;
                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"The Champion's spirit heals you for {champHeal} HP!");
                    }
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine("A strike worthy of legends!");
                }
                break;

            case "aoe_holy":
                if (monster != null && monster.IsAlive)
                {
                    long holyAoeDmg = abilityResult.Damage;
                    if (monster.MonsterClass == MonsterClass.Undead || monster.MonsterClass == MonsterClass.Demon || monster.Undead > 0)
                        holyAoeDmg = (long)(holyAoeDmg * 1.5);
                    monster.HP -= holyAoeDmg;
                    result.TotalDamageDealt += holyAoeDmg;
                    player.Statistics.RecordDamageDealt(holyAoeDmg, false);
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine($"Divine judgment strikes for {holyAoeDmg} damage!");
                }
                break;

            case "fear":
                if (monster != null && monster.IsAlive)
                {
                    int fearChance = monster.IsBoss ? 35 : 70;
                    if (random.Next(100) < fearChance)
                    {
                        monster.IsFeared = true;
                        monster.FearDuration = abilityResult.Duration > 0 ? abilityResult.Duration : 3;
                        terminal.SetColor("bright_red");
                        terminal.WriteLine($"{monster.Name} is terrified by your roar!");
                    }
                    else
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine($"{monster.Name} resists your intimidation!");
                    }
                }
                break;

            case "confusion":
                if (monster != null && monster.IsAlive)
                {
                    int confuseChance = monster.IsBoss ? 30 : 65;
                    if (random.Next(100) < confuseChance)
                    {
                        monster.IsConfused = true;
                        monster.ConfusedDuration = 2;
                        terminal.SetColor("magenta");
                        terminal.WriteLine($"{monster.Name} is bewildered by the joke!");
                    }
                    else
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine($"{monster.Name} doesn't get the joke.");
                    }
                }
                break;

            case "freeze":
                if (monster != null && monster.IsAlive)
                {
                    int freezeChance = monster.IsBoss ? 40 : 75;
                    if (random.Next(100) < freezeChance)
                    {
                        monster.IsFrozen = true;
                        monster.FrozenDuration = abilityResult.Duration > 0 ? abilityResult.Duration : 2;
                        terminal.SetColor("bright_cyan");
                        terminal.WriteLine($"{monster.Name} is frozen solid!");
                    }
                    else
                    {
                        terminal.SetColor("cyan");
                        terminal.WriteLine($"The frost bites {monster.Name} but doesn't freeze it!");
                    }
                }
                break;

            case "instant_kill":
                if (monster != null && monster.IsAlive && monster.HP < monster.MaxHP * 0.25 && !monster.IsBoss)
                {
                    if (random.Next(100) < 50)
                    {
                        long killDmg = monster.HP;
                        monster.HP = 0;
                        result.TotalDamageDealt += killDmg;
                        player.Statistics.RecordDamageDealt(killDmg, false);
                        terminal.SetColor("bright_red");
                        terminal.WriteLine($"ASSASSINATED! {monster.Name} falls instantly!");
                    }
                    else
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine("The killing blow narrowly misses the vital spot!");
                    }
                }
                break;

            case "multi_hit":
                if (monster != null && monster.IsAlive)
                {
                    int extraHits = random.Next(2, 5);
                    terminal.SetColor("bright_red");
                    terminal.WriteLine($"FRENZY! {extraHits} additional strikes!");
                    for (int i = 0; i < extraHits && monster.IsAlive; i++)
                    {
                        long frenzyDmg = Math.Max(1, abilityResult.Damage / 3 - monster.Defence / 4);
                        frenzyDmg = (long)(frenzyDmg * (0.8 + random.NextDouble() * 0.4));
                        monster.HP -= frenzyDmg;
                        result.TotalDamageDealt += frenzyDmg;
                        player.Statistics.RecordDamageDealt(frenzyDmg, false);
                        terminal.SetColor("red");
                        terminal.WriteLine($"  Strike {i + 1}: {frenzyDmg} damage!");
                        if (monster.HP <= 0)
                        {
                            terminal.SetColor("bright_green");
                            terminal.WriteLine($"  {monster.Name} is torn apart!");
                        }
                    }
                }
                break;

            case "execute_all":
                // In single-monster, behaves like execute
                if (monster != null && monster.IsAlive && monster.HP < monster.MaxHP * 0.3)
                {
                    long executeDmg = abilityResult.Damage;
                    monster.HP -= executeDmg;
                    result.TotalDamageDealt += executeDmg;
                    player.Statistics.RecordDamageDealt(executeDmg, false);
                    terminal.SetColor("bright_red");
                    terminal.WriteLine($"DEATH BLOSSOM EXECUTE! +{executeDmg} damage!");
                }
                break;

            case "evasion":
                player.DodgeNextAttack = true;
                player.TempDefenseBonus += 50;
                player.TempDefenseBonusDuration = Math.Max(player.TempDefenseBonusDuration, 2);
                terminal.SetColor("magenta");
                terminal.WriteLine("You melt into the shadows, nearly impossible to hit!");
                break;

            case "stealth":
                player.DodgeNextAttack = true;
                terminal.SetColor("green");
                terminal.WriteLine("You blend perfectly with your surroundings!");
                break;

            case "marked":
                if (monster != null && monster.IsAlive)
                {
                    monster.IsMarked = true;
                    monster.MarkedDuration = abilityResult.Duration > 0 ? abilityResult.Duration : 4;
                    terminal.SetColor("bright_red");
                    terminal.WriteLine($"{monster.Name} is marked! All attacks deal 30% bonus damage!");
                }
                break;

            case "bloodlust":
                player.HasBloodlust = true;
                terminal.SetColor("bright_red");
                terminal.WriteLine("BLOODLUST! Each kill will heal you!");
                break;

            case "immunity":
                player.HasStatusImmunity = true;
                player.StatusImmunityDuration = abilityResult.Duration > 0 ? abilityResult.Duration : 4;
                {
                    var toRemoveNeg = player.ActiveStatuses.Keys.Where(s => s.IsNegative()).ToList();
                    foreach (var s in toRemoveNeg) player.ActiveStatuses.Remove(s);
                }
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("UNSTOPPABLE! You shrug off all afflictions!");
                break;

            case "invulnerable":
                if (!player.ActiveStatuses.ContainsKey(StatusEffect.Invulnerable))
                    player.ActiveStatuses[StatusEffect.Invulnerable] = abilityResult.Duration > 0 ? abilityResult.Duration : 2;
                terminal.SetColor("bright_white");
                terminal.WriteLine("A divine shield of pure light surrounds you!");
                break;

            case "cleanse":
                {
                    var cleansable = player.ActiveStatuses.Keys.Where(s => s.IsNegative()).ToList();
                    foreach (var s in cleansable) player.ActiveStatuses.Remove(s);
                    terminal.SetColor("bright_white");
                    if (cleansable.Count > 0)
                        terminal.WriteLine($"Cleansing light purges {cleansable.Count} affliction(s)!");
                    else
                        terminal.WriteLine("Holy light washes over you!");
                }
                break;

            case "vanish":
                player.DodgeNextAttack = true;
                if (!player.ActiveStatuses.ContainsKey(StatusEffect.Hidden))
                    player.ActiveStatuses[StatusEffect.Hidden] = 1;
                terminal.SetColor("magenta");
                terminal.WriteLine("You vanish completely! Your next attack will strike from the shadows!");
                break;

            case "shadow":
                if (!player.ActiveStatuses.ContainsKey(StatusEffect.Hidden))
                    player.ActiveStatuses[StatusEffect.Hidden] = 2;
                terminal.SetColor("magenta");
                terminal.WriteLine("Shadows embrace you. Noctura's power flows through you!");
                break;

            case "transmute":
                {
                    var negToRemove = player.ActiveStatuses.Keys.Where(s => s.IsNegative()).ToList();
                    foreach (var s in negToRemove) player.ActiveStatuses.Remove(s);
                    if (!player.ActiveStatuses.ContainsKey(StatusEffect.Regenerating))
                        player.ActiveStatuses[StatusEffect.Regenerating] = abilityResult.Duration > 0 ? abilityResult.Duration : 5;
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine("Alchemical energy transforms your body! All ailments purged!");
                }
                break;

            case "legend":
                if (!player.ActiveStatuses.ContainsKey(StatusEffect.Regenerating))
                    player.ActiveStatuses[StatusEffect.Regenerating] = abilityResult.Duration > 0 ? abilityResult.Duration : 5;
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("You become the legend! Songs of power flow through you!");
                break;

            case "legendary":
                if (monster != null && monster.IsAlive)
                {
                    monster.IsStunned = true;
                    monster.StunDuration = 1;
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine($"LEGENDARY SHOT! {monster.Name} staggers from the devastating hit!");
                }
                break;

            case "avatar":
                player.IsRaging = true;
                player.HasStatusImmunity = true;
                player.StatusImmunityDuration = abilityResult.Duration > 0 ? abilityResult.Duration : 5;
                if (!player.ActiveStatuses.ContainsKey(StatusEffect.Raging))
                    player.ActiveStatuses[StatusEffect.Raging] = abilityResult.Duration > 0 ? abilityResult.Duration : 5;
                terminal.SetColor("bright_red");
                terminal.WriteLine("YOU BECOME DESTRUCTION INCARNATE! NOTHING CAN STOP YOU!");
                break;

            case "avatar_light":
                if (!player.ActiveStatuses.ContainsKey(StatusEffect.Invulnerable))
                    player.ActiveStatuses[StatusEffect.Invulnerable] = 1;
                player.HasStatusImmunity = true;
                player.StatusImmunityDuration = abilityResult.Duration > 0 ? abilityResult.Duration : 5;
                terminal.SetColor("bright_white");
                terminal.WriteLine("DIVINE LIGHT FLOODS YOUR BEING! YOU ARE THE AVATAR OF LIGHT!");
                break;

            case "guaranteed_hit":
                terminal.SetColor("cyan");
                terminal.WriteLine("Your aim is true!");
                break;
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Process end-of-round ability effects: decrement cooldowns and buff durations
    /// </summary>
    private void ProcessEndOfRoundAbilityEffects(Character player)
    {
        // Regenerate combat stamina each round
        int staminaRegen = player.RegenerateCombatStamina();
        if (staminaRegen > 0)
        {
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"You recover {staminaRegen} stamina. (Stamina: {player.CurrentCombatStamina}/{player.MaxCombatStamina})");
        }

        // Regenerate mana for spellcasters each round
        if (SpellSystem.HasSpells(player) && player.Mana < player.MaxMana)
        {
            int manaRegen = StatEffectsSystem.GetManaRegenPerRound(player.Wisdom);
            player.Mana = Math.Min(player.MaxMana, player.Mana + manaRegen);
            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"You recover {manaRegen} mana. (Mana: {player.Mana}/{player.MaxMana})");
        }

        // Equipment HP regeneration enchantment
        int equipHPRegen = player.GetEquipmentHPRegen();
        if (equipHPRegen > 0 && player.HP < player.MaxHP)
        {
            long oldHP = player.HP;
            player.HP = Math.Min(player.MaxHP, player.HP + equipHPRegen);
            long healed = player.HP - oldHP;
            if (healed > 0)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine($"Your equipment regenerates {healed} HP! (Regeneration)");
            }
        }

        // Equipment Mana regeneration enchantment
        int equipManaRegen = player.GetEquipmentManaRegen();
        if (equipManaRegen > 0 && player.Mana < player.MaxMana)
        {
            long oldMana = player.Mana;
            player.Mana = Math.Min(player.MaxMana, player.Mana + equipManaRegen);
            long restored = player.Mana - oldMana;
            if (restored > 0)
            {
                terminal.SetColor("bright_blue");
                terminal.WriteLine($"Your equipment restores {restored} mana! (Arcane Regen)");
            }
        }

        // Decrement ability cooldowns
        var cooldownKeys = abilityCooldowns.Keys.ToList();
        foreach (var key in cooldownKeys)
        {
            abilityCooldowns[key]--;
            if (abilityCooldowns[key] <= 0)
            {
                abilityCooldowns.Remove(key);
            }
        }

        // Decrement temporary attack bonus duration
        if (player.TempAttackBonusDuration > 0)
        {
            player.TempAttackBonusDuration--;
            if (player.TempAttackBonusDuration <= 0)
            {
                player.TempAttackBonus = 0;
            }
        }

        // Decrement temporary defense bonus duration
        if (player.TempDefenseBonusDuration > 0)
        {
            player.TempDefenseBonusDuration--;
            if (player.TempDefenseBonusDuration <= 0)
            {
                player.TempDefenseBonus = 0;
            }
        }

        // Tick down status immunity duration
        if (player.HasStatusImmunity)
        {
            player.StatusImmunityDuration--;
            if (player.StatusImmunityDuration <= 0)
            {
                player.HasStatusImmunity = false;
                terminal.SetColor("gray");
                terminal.WriteLine("Your status immunity fades.");
            }
        }

        // Clear defending status at end of round (after all monsters have attacked)
        // This ensures defend protects against ALL monster attacks in a round
        if (player.IsDefending)
        {
            player.IsDefending = false;
            if (player.HasStatus(StatusEffect.Defending))
                player.ActiveStatuses.Remove(StatusEffect.Defending);
        }
    }

    private async Task ShowPvPIntroduction(Character attacker, Character defender, CombatResult result)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine("═══ PLAYER FIGHT ═══");
        terminal.WriteLine("");
        
        terminal.SetColor("white");
        terminal.WriteLine($"{attacker.DisplayName} confronts {defender.DisplayName}!");
        await Task.Delay(GetCombatDelay(2000));
    }
    
    private async Task ProcessPlayerVsPlayerAction(CombatAction action, Character attacker, Character defender, CombatResult result)
    {
        if (!attacker.IsAlive || !defender.IsAlive) return;

        switch (action.Type)
        {
            case CombatActionType.Attack:
                await ExecutePvPAttack(attacker, defender, result);
                break;

            case CombatActionType.Defend:
                await ExecuteDefend(attacker, result);
                break;

            case CombatActionType.Heal:
                await ExecuteHeal(attacker, result, false);
                break;

            case CombatActionType.QuickHeal:
                await ExecuteHeal(attacker, result, true);
                break;

            case CombatActionType.Status:
                await ShowCombatStatus(attacker, result);
                break;

            case CombatActionType.UseItem:
                // Redirect to proper heal (UseItem is deprecated, use Heal instead)
                await ExecuteHeal(attacker, result, false);
                break;

            case CombatActionType.CastSpell:
                await ExecutePvPSpell(attacker, defender, action, result);
                break;

            case CombatActionType.UseAbility:
            case CombatActionType.ClassAbility:
                await ExecutePvPAbility(attacker, defender, action, result);
                break;

            case CombatActionType.Rage:
                await ExecuteRage(attacker, result);
                break;

            case CombatActionType.RangedAttack:
                // Ranger ranged attack works in PvP as a basic attack with bonus
                await ExecutePvPAttack(attacker, defender, result);
                break;

            case CombatActionType.Retreat:
                // Flee from PvP combat
                int fleeChance = 30 + (int)(attacker.Agility / 2) - (int)(defender.Agility / 4);
                fleeChance = Math.Clamp(fleeChance, 10, 75);
                if (random.Next(100) < fleeChance)
                {
                    terminal.WriteLine("You manage to escape!", "green");
                    globalEscape = true;
                    result.Outcome = CombatOutcome.PlayerEscaped;
                }
                else
                {
                    terminal.WriteLine($"{defender.DisplayName} blocks your escape!", "red");
                }
                await Task.Delay(GetCombatDelay(800));
                break;

            case CombatActionType.Hide:
                await ExecuteHide(attacker, result);
                break;

            case CombatActionType.Taunt:
                terminal.WriteLine($"You taunt {defender.DisplayName}!", "yellow");
                await Task.Delay(GetCombatDelay(500));
                break;

            case CombatActionType.Disarm:
                await ExecutePvPDisarm(attacker, defender, result);
                break;

            // Actions that don't work in PvP
            case CombatActionType.PowerAttack:
            case CombatActionType.PreciseStrike:
            case CombatActionType.Backstab:
            case CombatActionType.Smite:
            case CombatActionType.FightToDeath:
            case CombatActionType.BegForMercy:
                terminal.WriteLine("That action is not available in PvP combat.", "yellow");
                await Task.Delay(GetCombatDelay(500));
                // Default to basic attack
                await ExecutePvPAttack(attacker, defender, result);
                break;

            default:
                // Default to attack
                await ExecutePvPAttack(attacker, defender, result);
                break;
        }
    }

    /// <summary>
    /// Execute a basic PvP attack (Character vs Character)
    /// </summary>
    private async Task ExecutePvPAttack(Character attacker, Character defender, CombatResult result)
    {
        long attackPower = attacker.Strength + attacker.WeapPow + random.Next(1, 16);

        // Apply weapon configuration damage modifier
        double damageModifier = GetWeaponConfigDamageModifier(attacker);
        attackPower = (long)(attackPower * damageModifier);

        // Check for critical hit
        bool isCritical = random.Next(100) < 5 + (attacker.Dexterity / 10);
        if (isCritical)
        {
            attackPower = (long)(attackPower * 1.5);
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("CRITICAL HIT!");
        }

        // Check for shield block on defender
        var (blocked, blockBonus) = TryShieldBlock(defender);
        if (blocked)
        {
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"{defender.DisplayName} raises their shield to block!");
        }

        long defense = defender.Defence + random.Next(0, (int)Math.Max(1, defender.Defence / 8));
        defense += blockBonus;

        // Apply defender's weapon configuration defense modifier
        double defenseModifier = GetWeaponConfigDefenseModifier(defender);
        defense = (long)(defense * defenseModifier);

        // Apply defending bonus if active
        if (defender.HasStatus(StatusEffect.Defending))
        {
            defense = (long)(defense * 1.5);
        }

        long damage = Math.Max(1, attackPower - defense);
        defender.HP = Math.Max(0, defender.HP - damage);

        // Track statistics
        attacker.Statistics?.RecordDamageDealt(damage, isCritical);

        terminal.SetColor(isCritical ? "bright_red" : "red");
        terminal.WriteLine($"You strike {defender.DisplayName} for {damage} damage!");
        terminal.SetColor("cyan");
        terminal.WriteLine($"Your HP: {attacker.HP}/{attacker.MaxHP}");
        terminal.WriteLine($"{defender.DisplayName} HP: {defender.HP}/{defender.MaxHP}");

        result.CombatLog.Add($"{attacker.DisplayName} hits {defender.DisplayName} for {damage}");
        await Task.Delay(GetCombatDelay(800));
    }

    /// <summary>
    /// Execute a spell in PvP combat
    /// </summary>
    private async Task ExecutePvPSpell(Character attacker, Character defender, CombatAction action, CombatResult result)
    {
        if (attacker.Mana <= 0)
        {
            terminal.WriteLine("You have no mana to cast spells!", "red");
            await Task.Delay(GetCombatDelay(500));
            return;
        }

        var spells = SpellSystem.GetAvailableSpells(attacker)
                    .Where(s => SpellSystem.CanCastSpell(attacker, s.Level))
                    .ToList();

        if (spells.Count == 0)
        {
            terminal.WriteLine("You don't know any spells you can cast!", "yellow");
            await Task.Delay(GetCombatDelay(500));
            return;
        }

        SpellSystem.SpellInfo? chosen = null;

        // If spell was triggered from quickbar, use it directly
        if (action.SpellIndex > 0)
        {
            chosen = spells.FirstOrDefault(s => s.Level == action.SpellIndex);
        }

        if (chosen == null)
        {
            // Show spell selection menu
            terminal.WriteLine("Available spells:", "cyan");
            for (int i = 0; i < spells.Count; i++)
            {
                var sp = spells[i];
                terminal.WriteLine($"  [{i + 1}] {sp.Name} (Level {sp.Level}, Cost: {sp.ManaCost})", "white");
            }

            var choice = await terminal.GetInput("Cast which spell? ");
            if (int.TryParse(choice, out int spellIndex) && spellIndex >= 1 && spellIndex <= spells.Count)
            {
                chosen = spells[spellIndex - 1];
            }
            else
            {
                terminal.WriteLine("Spell cancelled.", "gray");
                await Task.Delay(GetCombatDelay(500));
                return;
            }
        }

        var spellResult = SpellSystem.CastSpell(attacker, chosen.Level, defender);
        terminal.WriteLine(spellResult.Message, "magenta");

        // Apply spell effects - damage spells work on Character
        if (spellResult.Success && spellResult.Damage > 0)
        {
            defender.HP = Math.Max(0, defender.HP - spellResult.Damage);
            terminal.WriteLine($"{defender.DisplayName} takes {spellResult.Damage} magical damage!", "bright_magenta");
        }

        // Apply healing spells to self
        if (spellResult.Success && spellResult.Healing > 0)
        {
            attacker.HP = Math.Min(attacker.MaxHP, attacker.HP + spellResult.Healing);
            terminal.SetColor("bright_green");
            terminal.WriteLine($"You recover {spellResult.Healing} HP!");
        }

        result.CombatLog.Add($"{attacker.DisplayName} casts {chosen.Name}");
        await Task.Delay(GetCombatDelay(800));
    }

    /// <summary>
    /// Execute a class ability in PvP combat (Character vs Character)
    /// Handles both quickbar-triggered abilities (action.AbilityId set) and manual ability selection
    /// </summary>
    private async Task ExecutePvPAbility(Character attacker, Character defender, CombatAction action, CombatResult result)
    {
        ClassAbilitySystem.ClassAbility? selectedAbility = null;

        // If ability was triggered from quickbar, use it directly
        if (!string.IsNullOrEmpty(action.AbilityId))
        {
            selectedAbility = ClassAbilitySystem.GetAbility(action.AbilityId);
            if (selectedAbility == null)
            {
                terminal.WriteLine("Unknown ability!", "red");
                await Task.Delay(GetCombatDelay(500));
                return;
            }
        }
        else
        {
            // Manual ability selection menu
            var availableAbilities = ClassAbilitySystem.GetAvailableAbilities(attacker);

            if (availableAbilities.Count == 0)
            {
                terminal.WriteLine("You haven't learned any abilities yet!", "red");
                await Task.Delay(GetCombatDelay(1000));
                return;
            }

            terminal.SetColor("bright_yellow");
            terminal.WriteLine("═══ COMBAT ABILITIES ═══");
            terminal.SetColor("cyan");
            terminal.WriteLine($"Combat Stamina: {attacker.CurrentCombatStamina}/{attacker.MaxCombatStamina}");
            terminal.WriteLine("");

            int displayIndex = 1;
            var selectableAbilities = new List<ClassAbilitySystem.ClassAbility>();

            foreach (var ability in availableAbilities)
            {
                bool canUse = ClassAbilitySystem.CanUseAbility(attacker, ability.Id, abilityCooldowns);
                bool hasStamina = attacker.HasEnoughStamina(ability.StaminaCost);
                bool onCooldown = abilityCooldowns.TryGetValue(ability.Id, out int cooldownLeft) && cooldownLeft > 0;

                string statusText = "";
                string color = "white";

                if (onCooldown)
                {
                    statusText = $" [Cooldown: {cooldownLeft} rounds]";
                    color = "dark_gray";
                }
                else if (!hasStamina)
                {
                    statusText = $" [Need {ability.StaminaCost} stamina, have {attacker.CurrentCombatStamina}]";
                    color = "dark_gray";
                }

                terminal.SetColor(color);
                terminal.WriteLine($"  {displayIndex}. {ability.Name} - {ability.StaminaCost} stamina{statusText}");
                terminal.SetColor("gray");
                terminal.WriteLine($"     {ability.Description}");

                selectableAbilities.Add(ability);
                displayIndex++;
            }

            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.Write("Enter ability number (0 to cancel): ");
            string input = terminal.GetInputSync();

            if (!int.TryParse(input, out int choice) || choice < 1 || choice > selectableAbilities.Count)
            {
                terminal.WriteLine("Cancelled.", "gray");
                await Task.Delay(GetCombatDelay(500));
                return;
            }

            selectedAbility = selectableAbilities[choice - 1];
        }

        if (!ClassAbilitySystem.CanUseAbility(attacker, selectedAbility.Id, abilityCooldowns))
        {
            if (abilityCooldowns.TryGetValue(selectedAbility.Id, out int cd) && cd > 0)
                terminal.WriteLine($"{selectedAbility.Name} is on cooldown for {cd} more rounds!", "red");
            else
                terminal.WriteLine("Cannot use that ability right now!", "red");
            await Task.Delay(GetCombatDelay(1000));
            return;
        }

        if (!attacker.HasEnoughStamina(selectedAbility.StaminaCost))
        {
            terminal.WriteLine($"Not enough stamina! Need {selectedAbility.StaminaCost}, have {attacker.CurrentCombatStamina}.", "red");
            await Task.Delay(GetCombatDelay(1000));
            return;
        }

        // Deduct stamina and execute
        attacker.SpendStamina(selectedAbility.StaminaCost);
        terminal.SetColor("cyan");
        terminal.WriteLine($"(-{selectedAbility.StaminaCost} stamina, {attacker.CurrentCombatStamina}/{attacker.MaxCombatStamina} remaining)");

        var abilityResult = ClassAbilitySystem.UseAbility(attacker, selectedAbility.Id, random);

        terminal.SetColor("bright_magenta");
        terminal.WriteLine(abilityResult.Message);

        // Apply ability effects to PvP target
        if (abilityResult.Damage > 0)
        {
            long actualDamage = abilityResult.Damage;

            // Special effects
            if (abilityResult.SpecialEffect == "execute" && defender.HP < defender.MaxHP * 0.3)
            {
                actualDamage *= 2;
                terminal.WriteLine("EXECUTION! Double damage to wounded enemy!", "bright_red");
            }
            else if (abilityResult.SpecialEffect == "last_stand" && attacker.HP < attacker.MaxHP * 0.25)
            {
                actualDamage = (long)(actualDamage * 1.5);
                terminal.WriteLine("LAST STAND! Desperation fuels your attack!", "bright_red");
            }
            else if (abilityResult.SpecialEffect == "armor_pierce")
            {
                terminal.WriteLine("The acid ignores armor!", "green");
            }
            else if (abilityResult.SpecialEffect == "backstab")
            {
                actualDamage = (long)(actualDamage * 1.5);
                terminal.WriteLine("Critical strike from the shadows!", "bright_yellow");
            }

            // Apply defense (abilities partially bypass defense)
            if (abilityResult.SpecialEffect != "armor_pierce")
            {
                long defense = defender.Defence / 2;
                actualDamage = Math.Max(1, actualDamage - defense);
            }

            defender.HP = Math.Max(0, defender.HP - actualDamage);
            attacker.Statistics?.RecordDamageDealt(actualDamage, false);

            terminal.SetColor("bright_red");
            terminal.WriteLine($"You deal {actualDamage} damage to {defender.DisplayName}!");
        }

        // Apply healing effects (self-heals)
        if (abilityResult.Healing > 0)
        {
            attacker.HP = Math.Min(attacker.MaxHP, attacker.HP + abilityResult.Healing);
            terminal.SetColor("bright_green");
            terminal.WriteLine($"You recover {abilityResult.Healing} HP!");
        }

        // Apply temp attack/defense bonuses to attacker
        if (abilityResult.AttackBonus > 0)
        {
            attacker.TempAttackBonus += abilityResult.AttackBonus;
            attacker.TempAttackBonusDuration = Math.Max(attacker.TempAttackBonusDuration, abilityResult.Duration);
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"Attack power increased by {abilityResult.AttackBonus}!");
        }
        if (abilityResult.DefenseBonus > 0)
        {
            attacker.TempDefenseBonus += abilityResult.DefenseBonus;
            attacker.TempDefenseBonusDuration = Math.Max(attacker.TempDefenseBonusDuration, abilityResult.Duration);
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"Defense increased by {abilityResult.DefenseBonus}!");
        }

        // Set cooldown
        if (abilityResult.CooldownApplied > 0)
        {
            abilityCooldowns[selectedAbility.Id] = abilityResult.CooldownApplied;
        }

        result.CombatLog.Add($"{attacker.DisplayName} uses {selectedAbility.Name}");
        await Task.Delay(GetCombatDelay(1000));
    }

    /// <summary>
    /// Execute a disarm attempt in PvP combat
    /// </summary>
    private async Task ExecutePvPDisarm(Character attacker, Character defender, CombatResult result)
    {
        // Disarm attempt based on dexterity vs defender's strength
        int successChance = 30 + (int)(attacker.Dexterity / 3) - (int)(defender.Strength / 4);
        successChance = Math.Clamp(successChance, 5, 75);

        if (random.Next(100) < successChance)
        {
            // Successful disarm - temporarily reduce weapon power
            long oldWeapPow = defender.WeapPow;
            defender.WeapPow = Math.Max(0, defender.WeapPow / 2);
            terminal.SetColor("bright_green");
            terminal.WriteLine($"You disarm {defender.DisplayName}! Their weapon effectiveness is reduced!");
            result.CombatLog.Add($"{attacker.DisplayName} disarms {defender.DisplayName}");
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"Your disarm attempt fails!");
            result.CombatLog.Add($"{attacker.DisplayName} fails to disarm");
        }

        await Task.Delay(GetCombatDelay(800));
    }
    
    private async Task ProcessComputerPlayerAction(Character computer, Character opponent, CombatResult result)
    {
        // Basic heuristic AI
        if (!computer.IsAlive || !opponent.IsAlive) return;

        // 1. Heal if low
        if (computer.HP < computer.MaxHP / 3 && computer.Healing > 0)
        {
            computer.Healing--;
            long heal = 30 + computer.Level * 5 + random.Next(10, 30);
            heal = Math.Min(heal, computer.MaxHP - computer.HP);
            computer.HP += heal;
            terminal.WriteLine($"{computer.DisplayName} quaffs a potion and heals {heal} HP!", "green");
            result.CombatLog.Add($"{computer.DisplayName} heals {heal}");
            await Task.Delay(GetCombatDelay(800));
            return;
        }

        // 2. Cast spell if mage and enough mana
        if ((computer.Class == CharacterClass.Magician || computer.Class == CharacterClass.Sage || computer.Class == CharacterClass.Cleric) && computer.Mana > 0)
        {
            var spells = SpellSystem.GetAvailableSpells(computer)
                        .Where(s => SpellSystem.CanCastSpell(computer, s.Level) && s.SpellType == "Attack")
                        .ToList();
            if (spells.Count > 0 && random.Next(100) < 40)
            {
                var chosen = spells[random.Next(spells.Count)];
                var spellResult = SpellSystem.CastSpell(computer, chosen.Level, opponent);
                terminal.WriteLine(spellResult.Message, "magenta");
                // For now only self-affecting or damage spells ignored in PvP; skip Monster
                ApplySpellEffects(computer, null, spellResult);
                result.CombatLog.Add($"{computer.DisplayName} casts {chosen.Name}");
                await Task.Delay(GetCombatDelay(1000));
                return;
            }
        }

        // 3. Default attack
        long attackPower = computer.Strength + computer.WeapPow + random.Next(1, 16);

        // Apply weapon configuration damage modifier
        double damageModifier = GetWeaponConfigDamageModifier(computer);
        attackPower = (long)(attackPower * damageModifier);

        // Check for shield block on defender
        var (blocked, blockBonus) = TryShieldBlock(opponent);
        if (blocked)
        {
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"{opponent.DisplayName} raises their shield to block!");
        }

        long defense = opponent.Defence + random.Next(0, (int)Math.Max(1, opponent.Defence / 8));
        defense += blockBonus;

        // Apply defender's weapon configuration defense modifier
        double defenseModifier = GetWeaponConfigDefenseModifier(opponent);
        defense = (long)(defense * defenseModifier);

        long damage = Math.Max(1, attackPower - defense);
        opponent.HP = Math.Max(0, opponent.HP - damage);
        terminal.WriteLine($"{computer.DisplayName} strikes for {damage} damage!", "red");
        result.CombatLog.Add($"{computer.DisplayName} hits {opponent.DisplayName} for {damage}");
        await Task.Delay(GetCombatDelay(800));
    }
    
    private async Task DeterminePvPOutcome(CombatResult result)
    {
        if (!result.Player.IsAlive)
        {
            result.Outcome = CombatOutcome.PlayerDied;
            terminal.WriteLine($"{result.Player.DisplayName} has been defeated!", "red");

            // Track death to another player
            result.Player.Statistics?.RecordDeath(toPlayer: true);

            // Track kill for the opponent if they're a player
            result.Opponent?.Statistics?.RecordPlayerKill();

            // Generate death news for the realm
            string location = result.Player.CurrentLocation ?? "battle";
            NewsSystem.Instance?.WriteDeathNews(result.Player.DisplayName, result.Opponent?.DisplayName ?? "an opponent", location);
        }
        else if (!result.Opponent.IsAlive)
        {
            result.Outcome = CombatOutcome.Victory;
            terminal.WriteLine($"{result.Player.DisplayName} is victorious!", "green");

            // Track PvP kill for the player
            result.Player.Statistics?.RecordPlayerKill();

            // Track death for the opponent
            result.Opponent?.Statistics?.RecordDeath(toPlayer: true);

            // Generate death news for the realm
            string location = result.Opponent?.CurrentLocation ?? "battle";
            NewsSystem.Instance?.WriteDeathNews(result.Opponent?.DisplayName ?? "Unknown", result.Player.DisplayName, location);

            // === REWARD CALCULATION FOR KILLING NPCs ===
            // Calculate XP based on opponent level
            int opponentLevel = result.Opponent?.Level ?? 1;
            int levelDiff = opponentLevel - result.Player.Level;

            // Base XP: 50 * level, with level difference modifier
            long baseXP = 50 * opponentLevel;
            double levelMultiplier = 1.0 + (levelDiff * 0.15);
            levelMultiplier = Math.Clamp(levelMultiplier, 0.25, 2.0);
            long xpReward = (long)(baseXP * levelMultiplier);
            xpReward = Math.Max(10, xpReward);

            // Apply difficulty modifier
            float xpMult = DifficultySystem.GetExperienceMultiplier(DifficultySystem.CurrentDifficulty);
            xpReward = (long)(xpReward * xpMult);

            // Calculate gold reward - take some of opponent's gold + level-based bonus
            long opponentGold = result.Opponent?.Gold ?? 0;
            long goldFromOpponent = (long)(opponentGold * 0.5); // Take half their gold
            long bonusGold = random.Next(10, 30) * opponentLevel; // Level-based bonus
            long goldReward = goldFromOpponent + bonusGold;

            // Apply difficulty modifier
            float goldMult = DifficultySystem.GetGoldMultiplier(DifficultySystem.CurrentDifficulty);
            goldReward = (long)(goldReward * goldMult);

            // Remove gold from opponent
            if (result.Opponent != null)
            {
                result.Opponent.Gold = Math.Max(0, result.Opponent.Gold - goldFromOpponent);
            }

            // Apply rewards to player
            result.Player.Experience += xpReward;
            result.Player.Gold += goldReward;
            result.ExperienceGained = xpReward;
            result.GoldGained = goldReward;

            // Track peak gold
            result.Player.Statistics?.RecordGoldChange(result.Player.Gold);

            // Display rewards
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine($"Experience gained: {xpReward:N0}");
            terminal.WriteLine($"Gold gained: {goldReward:N0}");

            // === BONUS LOOT FROM NPC EQUIPMENT ===
            // Chance to salvage value from opponent's equipment
            long equipmentLootValue = 0;

            if (result.Opponent != null)
            {
                // 30% chance to salvage weapon value
                string opponentWeaponName = result.Opponent.WeaponName;
                if (!string.IsNullOrEmpty(opponentWeaponName) &&
                    opponentWeaponName != "Fist" &&
                    opponentWeaponName != "None" &&
                    random.Next(100) < 30)
                {
                    // Find weapon value and give a portion as loot
                    var weapon = EquipmentDatabase.GetByName(opponentWeaponName);
                    if (weapon != null)
                    {
                        long weaponValue = (long)(weapon.Value * 0.5); // 50% of item value
                        equipmentLootValue += weaponValue;
                        result.ItemsFound.Add($"{opponentWeaponName} (salvaged for {weaponValue:N0}g)");
                    }
                }

                // 25% chance to salvage armor value
                string opponentArmorName = result.Opponent.ArmorName;
                if (!string.IsNullOrEmpty(opponentArmorName) &&
                    opponentArmorName != "None" &&
                    opponentArmorName != "Clothes" &&
                    random.Next(100) < 25)
                {
                    // Find armor value and give a portion as loot
                    var armor = EquipmentDatabase.GetByName(opponentArmorName);
                    if (armor != null)
                    {
                        long armorValue = (long)(armor.Value * 0.5); // 50% of item value
                        equipmentLootValue += armorValue;
                        result.ItemsFound.Add($"{opponentArmorName} (salvaged for {armorValue:N0}g)");
                    }
                }
            }

            // Apply equipment loot value
            if (equipmentLootValue > 0)
            {
                result.Player.Gold += equipmentLootValue;
                result.GoldGained += equipmentLootValue;

                terminal.SetColor("bright_cyan");
                terminal.WriteLine("");
                terminal.WriteLine("Equipment salvaged:");
                foreach (var item in result.ItemsFound)
                {
                    terminal.WriteLine($"  • {item}");
                }
            }
        }

        await Task.Delay(GetCombatDelay(2000));
    }

    /// <summary>
    /// Process spell casting during combat
    /// </summary>
    private void ProcessSpellCasting(Character player, Monster monster)
    {
        terminal.ClearScreen();
        terminal.SetColor("white");
        terminal.WriteLine("═══ Spell Casting ═══");
        
        var availableSpells = SpellSystem.GetAvailableSpells(player);
        if (availableSpells.Count == 0)
        {
            terminal.WriteLine($"{player.DisplayName} doesn't know any spells yet!", "red");
            terminal.PressAnyKey();
            return;
        }
        
        // Display available spells
        terminal.WriteLine("Available Spells:");
        for (int i = 0; i < availableSpells.Count; i++)
        {
            var spell = availableSpells[i];
            var manaCost = SpellSystem.CalculateManaCost(spell, player);
            var canCast = player.Mana >= manaCost && player.Level >= SpellSystem.GetLevelRequired(player.Class, spell.Level);
            var color = canCast ? ConsoleColor.White : ConsoleColor.DarkGray;
            
            terminal.SetColor(color);
            terminal.WriteLine($"{i + 1}. {spell.Name} (Level {spell.Level}) - {manaCost} mana");
            if (!canCast)
            {
                terminal.WriteLine("   (Not enough mana)");
            }
        }
        
        terminal.WriteLine("");
        terminal.WriteLine("Enter spell number (0 to cancel): ", ConsoleColor.Yellow, false);
        string input = terminal.GetInputSync();
        
        if (int.TryParse(input, out int spellChoice) && spellChoice > 0 && spellChoice <= availableSpells.Count)
        {
            var selectedSpell = availableSpells[spellChoice - 1];
            
            if (!SpellSystem.CanCastSpell(player, selectedSpell.Level))
            {
                terminal.WriteLine("You cannot cast this spell right now!", "red");
                terminal.PressAnyKey();
                return;
            }
            
            // Cast the spell – the SpellSystem API expects a Character target. We pass null and
            // handle damage application ourselves against the Monster instance further below.
            var spellResult = SpellSystem.CastSpell(player, selectedSpell.Level, null);

            terminal.WriteLine("");
            terminal.WriteLine(spellResult.Message);

            // Apply spell effects
            ApplySpellEffects(player, monster, spellResult);

            // Display training improvement message if spell proficiency increased
            if (spellResult.SkillImproved && !string.IsNullOrEmpty(spellResult.NewProficiencyLevel))
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"  Your {selectedSpell.Name} proficiency improved to {spellResult.NewProficiencyLevel}!");
            }

            terminal.PressAnyKey();
        }
        else if (spellChoice != 0)
        {
            terminal.WriteLine("Invalid spell selection!", "red");
            terminal.PressAnyKey();
        }
    }
    
    /// <summary>
    /// Apply spell effects to combat
    /// </summary>
    private void ApplySpellEffects(Character caster, Monster target, SpellSystem.SpellResult spellResult)
    {
        // Apply healing to caster
        if (spellResult.Healing > 0)
        {
            long oldHP = caster.HP;
            caster.HP = Math.Min(caster.HP + spellResult.Healing, caster.MaxHP);
            long actualHealing = caster.HP - oldHP;
            terminal.WriteLine($"{caster.DisplayName} heals {actualHealing} hitpoints!", "green");
        }
        
        // Apply damage to target
        if (spellResult.Damage > 0 && target != null)
        {
            target.HP = Math.Max(0, target.HP - spellResult.Damage);
            terminal.WriteLine($"{target.Name} takes {spellResult.Damage} damage!", "red");

            if (target.HP <= 0)
            {
                terminal.WriteLine($"{target.Name} has been slain by magic!", "dark_red");
            }
        }
        
        // Convert buffs into status effects (basic mapping for now)
        if (spellResult.ProtectionBonus > 0)
        {
            int dur = spellResult.Duration > 0 ? spellResult.Duration : 999;
            caster.MagicACBonus = spellResult.ProtectionBonus;
            caster.ApplyStatus(StatusEffect.Blessed, dur);
            terminal.WriteLine($"{caster.DisplayName} is magically protected! (+{spellResult.ProtectionBonus} AC for {dur} rounds)", "blue");
        }

        if (spellResult.AttackBonus > 0)
        {
            // Use PowerStance to represent offensive boost (simplified)
            int dur = spellResult.Duration > 0 ? spellResult.Duration : 3;
            caster.ApplyStatus(StatusEffect.PowerStance, dur);
            terminal.WriteLine($"{caster.DisplayName}'s power surges! (+50% damage for {dur} rounds)", "red");
        }
        
        // Handle special effects
        if (!string.IsNullOrEmpty(spellResult.SpecialEffect))
        {
            HandleSpecialSpellEffect(caster, target, spellResult.SpecialEffect);
        }
    }
    
    /// <summary>
    /// Handle special spell effects
    /// </summary>
    private void HandleSpecialSpellEffect(Character caster, Monster? target, string effect)
    {
        switch (effect.ToLower())
        {
            case "poison":
                if (target != null)
                {
                    target.Poisoned = true;
                    target.PoisonRounds = 5;
                    terminal.WriteLine($"{target.Name} is poisoned!", "dark_green");
                }
                break;
                
            case "sleep":
                if (target != null)
                {
                    target.IsSleeping = true;
                    target.SleepDuration = 3;
                    terminal.WriteLine($"{target.Name} falls into a magical slumber!", "cyan");
                }
                break;

            case "freeze":
                if (target != null)
                {
                    target.IsFrozen = true;
                    target.FrozenDuration = 2;
                    terminal.WriteLine($"{target.Name} is frozen solid!", "bright_cyan");
                }
                break;

            case "fear":
                if (target != null)
                {
                    target.IsFeared = true;
                    target.FearDuration = 3;
                    terminal.WriteLine($"{target.Name} cowers in fear!", "yellow");
                }
                break;
                
            case "escape":
                terminal.WriteLine($"{caster.DisplayName} vanishes in a whirl of arcane energy!", "magenta");
                globalEscape = true;
                break;
                
            case "blur":
            case "fog":
            case "duplicate":
                caster.ApplyStatus(StatusEffect.Blur, 999);
                terminal.WriteLine($"{caster.DisplayName}'s outline shimmers and blurs!", "cyan");
                break;
                
            case "stoneskin":
                caster.DamageAbsorptionPool = 10 * caster.Level;
                caster.ApplyStatus(StatusEffect.Stoneskin, 999);
                terminal.WriteLine($"{caster.DisplayName}'s skin hardens to resilient stone!", "dark_gray");
                break;
                
            case "steal":
                if (target != null)
                {
                    int stealCap = (int)Math.Max(1, Math.Min(target.Gold / 10, int.MaxValue));
                    var goldStolen = random.Next(stealCap);
                    if (goldStolen > 0)
                    {
                        target.Gold -= goldStolen;
                        caster.Gold += goldStolen;
                        terminal.WriteLine($"{caster.DisplayName} steals {goldStolen} gold from {target.Name}!", "yellow");
                    }
                    else
                    {
                        terminal.WriteLine($"The steal attempt finds no gold!", "gray");
                    }
                }
                break;
                
            case "convert":
                if (target != null)
                {
                    terminal.WriteLine($"{target.Name} is touched by divine light!", "white");

                    // Calculate conversion chance based on caster's Charisma and monster's level
                    int conversionChance = 30 + (int)(caster.Charisma / 5) - (target.Level * 2);
                    conversionChance = Math.Clamp(conversionChance, 5, 85); // 5-85% range

                    // Undead and demons are harder to convert
                    if (target.MonsterClass == MonsterClass.Undead || target.MonsterClass == MonsterClass.Demon)
                    {
                        conversionChance /= 2;
                        terminal.WriteLine("The unholy creature resists the divine light!", "dark_red");
                    }

                    if (random.Next(100) < conversionChance)
                    {
                        // Conversion success - determine effect
                        int effectRoll = random.Next(100);

                        if (effectRoll < 40)
                        {
                            // Monster flees in fear/awe
                            terminal.SetColor("bright_cyan");
                            terminal.WriteLine($"{target.Name} sees the error of its ways and flees!");
                            target.HP = 0; // Effectively removed from combat
                            target.Fled = true;
                        }
                        else if (effectRoll < 70)
                        {
                            // Monster becomes pacified (won't attack for several rounds)
                            terminal.SetColor("bright_green");
                            terminal.WriteLine($"{target.Name} is pacified by the holy light!");
                            terminal.WriteLine("It gazes at you with newfound respect...");
                            target.IsStunned = true;
                            target.StunDuration = 3 + random.Next(1, 4); // Stunned (won't attack) for 3-6 rounds
                            target.IsFriendly = true; // Mark as temporarily friendly
                        }
                        else
                        {
                            // Monster joins your side temporarily
                            terminal.SetColor("bright_yellow");
                            terminal.WriteLine($"{target.Name} is converted to your cause!");
                            terminal.WriteLine("It will fight by your side for this battle!");
                            target.IsFriendly = true;
                            target.IsConverted = true;
                            // Note: Combat system needs to handle converted monsters attacking other enemies
                        }
                    }
                    else
                    {
                        // Conversion failed
                        terminal.SetColor("gray");
                        terminal.WriteLine($"{target.Name} resists the conversion attempt!");
                        terminal.WriteLine("The creature's will is too strong...");
                    }
                }
                break;
                
            case "haste":
                caster.ApplyStatus(StatusEffect.Haste, 3);
                break;
                
            case "slow":
                if (target != null)
                {
                    target.IsSlowed = true;
                    target.SlowDuration = 3;
                    terminal.WriteLine($"{target.Name} is slowed!", "gray");
                }
                break;

            case "cure_disease":
                // Purify spell - cleanse disease and minor poisons
                bool curedAnything = false;

                // Clear status effect diseases
                if (caster.ActiveStatuses.ContainsKey(StatusEffect.Diseased))
                {
                    caster.RemoveStatus(StatusEffect.Diseased);
                    curedAnything = true;
                }
                if (caster.ActiveStatuses.ContainsKey(StatusEffect.Poisoned))
                {
                    caster.RemoveStatus(StatusEffect.Poisoned);
                    curedAnything = true;
                }

                // Clear boolean disease flags
                if (caster.Blind) { caster.Blind = false; curedAnything = true; }
                if (caster.Plague) { caster.Plague = false; curedAnything = true; }
                if (caster.Smallpox) { caster.Smallpox = false; curedAnything = true; }
                if (caster.Measles) { caster.Measles = false; curedAnything = true; }
                if (caster.Leprosy) { caster.Leprosy = false; curedAnything = true; }
                if (caster.LoversBane) { caster.LoversBane = false; curedAnything = true; }

                // Clear poison counter
                if (caster.Poison > 0) { caster.Poison = 0; curedAnything = true; }

                if (curedAnything)
                {
                    terminal.WriteLine($"{caster.DisplayName} is purified! Disease and afflictions are purged!", "bright_green");
                }
                else
                {
                    terminal.WriteLine($"{caster.DisplayName} is already healthy - the purifying light finds nothing to cleanse.", "cyan");
                }
                break;

            case "identify":
                terminal.WriteLine($"{caster.DisplayName} examines their belongings carefully...", "bright_white");
                foreach (var itm in caster.Inventory)
                {
                    terminal.WriteLine($" - {itm.Name}  (Type: {itm.Type}, Pow: {itm.Attack}/{itm.Armor})", "white");
                }
                break;

            case "mirror":
            case "invisible":
            case "shadow":
                caster.ApplyStatus(StatusEffect.Blur, 999);
                terminal.WriteLine($"{caster.DisplayName}'s form shimmers and becomes indistinct!", "cyan");
                break;

            case "avatar":
                // Divine Avatar: boost all combat stats
                caster.TempAttackBonus += 55;
                caster.TempDefenseBonus += 55;
                terminal.WriteLine($"{caster.DisplayName} channels divine power! (+55 ATK/DEF)", "bright_yellow");
                break;

            case "wish":
                // Wish: double all combat stats
                caster.TempAttackBonus += (int)caster.Strength;
                caster.TempDefenseBonus += (int)caster.Defense;
                terminal.WriteLine($"{caster.DisplayName}'s wish reshapes reality! All combat stats doubled!", "bright_magenta");
                break;

            case "timestop":
                // Time Stop: dodge + attack/defense bonus for 1 round
                caster.DodgeNextAttack = true;
                caster.TempAttackBonus += 35;
                caster.TempDefenseBonus += 35;
                terminal.WriteLine($"{caster.DisplayName} freezes time itself! (+35 ATK/DEF, dodge next attack)", "bright_cyan");
                break;

            case "mindblank":
                // Mind Blank: full status immunity
                caster.HasStatusImmunity = true;
                caster.StatusImmunityDuration = 999;
                terminal.WriteLine($"{caster.DisplayName}'s mind becomes an impenetrable fortress!", "bright_white");
                break;
        }
    }

    /// <summary>
    /// Execute defend – player braces and gains 50% damage reduction for the next monster hit.
    /// </summary>
    private async Task ExecuteDefend(Character player, CombatResult result)
    {
        player.IsDefending = true;
        player.ApplyStatus(StatusEffect.Defending, 1);
        terminal.WriteLine("You raise your guard, preparing to deflect incoming blows.", "bright_cyan");
        result.CombatLog.Add("Player enters defensive stance (50% damage reduction)");
        await Task.Delay(GetCombatDelay(1000));
    }

    private async Task ExecutePowerAttack(Character attacker, Monster target, CombatResult result)
    {
        if (target == null)
        {
            terminal.WriteLine("Power Attack has no effect in this combat!", "yellow");
            await Task.Delay(GetCombatDelay(500));
            return;
        }

        // Apply PowerStance status so any extra attacks this round follow the same rules
        attacker.ApplyStatus(StatusEffect.PowerStance, 1);

        // Higher damage, lower accuracy – modelled via larger damage multiplier but higher chance of minimal absorption.
        long originalStrength = attacker.Strength;
        long attackPower = (long)(originalStrength * 1.5);

        if (attacker.WeapPow > 0)
        {
            attackPower += (long)(attacker.WeapPow * 1.5) + random.Next(0, (int)attacker.WeapPow + 1);
        }

        attackPower += random.Next(1, 21); // variation

        // Apply weapon configuration damage modifier (2H bonus)
        double damageModifier = GetWeaponConfigDamageModifier(attacker);
        attackPower = (long)(attackPower * damageModifier);

        // Reduce "accuracy": enemy gains extra defense in calculation (25 % boost)
        long defense = target.Defence + random.Next(0, (int)Math.Max(1, target.Defence / 8));
        defense = (long)(defense * 1.25); // built-in accuracy penalty
        if (target.ArmPow > 0)
        {
            // Guard against integer overflow when ArmPow is very large
            int armPowMax = (int)Math.Min(target.ArmPow, int.MaxValue - 1);
            defense += random.Next(0, armPowMax + 1);
        }

        long damage = Math.Max(1, attackPower - defense);
        damage = DifficultySystem.ApplyPlayerDamageMultiplier(damage);

        terminal.SetColor("magenta");
        terminal.WriteLine($"POWER ATTACK! You smash the {target.Name} for {damage} damage!");

        target.HP = Math.Max(0, target.HP - damage);
        result.CombatLog.Add($"Player power-attacks {target.Name} for {damage} dmg (PowerStance)");

        await Task.Delay(GetCombatDelay(1000));
    }

    private async Task ExecutePreciseStrike(Character attacker, Monster target, CombatResult result)
    {
        if (target == null)
        {
            terminal.WriteLine("Precise Strike has no effect in this combat!", "yellow");
            await Task.Delay(GetCombatDelay(500));
            return;
        }

        // Higher accuracy (+25 %) but normal damage.
        long attackPower = attacker.Strength;
        if (attacker.WeapPow > 0)
        {
            attackPower += attacker.WeapPow + random.Next(0, (int)attacker.WeapPow + 1);
        }
        attackPower += random.Next(1, 21);

        // Apply weapon configuration damage modifier (2H bonus)
        double damageModifier = GetWeaponConfigDamageModifier(attacker);
        attackPower = (long)(attackPower * damageModifier);

        // Boost accuracy by 25 % via reducing target defense.
        long defense = target.Defence + random.Next(0, (int)Math.Max(1, target.Defence / 8));
        defense = (long)(defense * 0.75);
        if (target.ArmPow > 0)
        {
            // Guard against integer overflow when ArmPow is very large
            int armPowMax = (int)Math.Min(target.ArmPow, int.MaxValue - 1);
            defense += random.Next(0, armPowMax + 1);
        }

        long damage = Math.Max(1, attackPower - defense);
        damage = DifficultySystem.ApplyPlayerDamageMultiplier(damage);

        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"Precise strike lands for {damage} damage.");

        target.HP = Math.Max(0, target.HP - damage);
        result.CombatLog.Add($"Player precise-strikes {target.Name} for {damage} dmg");

        await Task.Delay(GetCombatDelay(1000));
    }

    private async Task ExecuteRangedAttack(Character attacker, Monster target, CombatResult result)
    {
        if (target == null)
        {
            await Task.Delay(GetCombatDelay(500));
            return;
        }

        // Accuracy heavily Dex-weighted
        long attackScore = attacker.Dexterity + (attacker.Level / 2) + random.Next(1, 21);
        long defenseScore = target.Defence + random.Next(1, 21);

        if (attackScore > defenseScore)
        {
            long damage = attacker.Dexterity / 2 + random.Next(1, 7); // d6 based
            damage = DifficultySystem.ApplyPlayerDamageMultiplier(damage);
            terminal.WriteLine($"You shoot an arrow for {damage} damage!", "bright_green");
            target.HP = Math.Max(0, target.HP - damage);
            result.CombatLog.Add($"Player ranged hits {target.Name} for {damage}");
        }
        else
        {
            terminal.WriteLine("Your missile misses the target.", "gray");
            result.CombatLog.Add("Player ranged misses");
        }

        await Task.Delay(GetCombatDelay(800));
    }

    private async Task ExecuteRage(Character player, CombatResult result)
    {
        player.IsRaging = true;
        terminal.WriteLine("You fly into a bloodthirsty rage!", "bright_red");
        result.CombatLog.Add("Player enters Rage state");
        await Task.Delay(GetCombatDelay(800));
    }

    private async Task ExecuteSmite(Character player, Monster target, CombatResult result)
    {
        if (target == null)
        {
            terminal.WriteLine("Smite has no effect in this combat!", "yellow");
            await Task.Delay(GetCombatDelay(500));
            return;
        }

        if (player.SmiteChargesRemaining <= 0)
        {
            terminal.WriteLine("You are out of smite charges!", "gray");
            await Task.Delay(GetCombatDelay(800));
            return;
        }

        player.SmiteChargesRemaining--;

        // Smite damage: 150 % of normal attack plus level bonus
        long damage = (long)(player.Strength * 1.5) + player.Level;
        if (player.WeapPow > 0)
            damage += (long)(player.WeapPow * 1.5);
        damage += random.Next(1, 21);

        // Apply weapon configuration damage modifier (2H bonus)
        double damageModifier = GetWeaponConfigDamageModifier(player);
        damage = (long)(damage * damageModifier);

        long defense = target.Defence + random.Next(0, (int)Math.Max(1, target.Defence / 8));
        long actual = Math.Max(1, damage - defense);
        actual = DifficultySystem.ApplyPlayerDamageMultiplier(actual);

        terminal.SetColor("yellow");
        terminal.WriteLine($"You SMITE the evil {target.Name} for {actual} holy damage!");

        target.HP = Math.Max(0, target.HP - actual);
        result.CombatLog.Add($"Player smites {target.Name} for {actual} dmg");
        await Task.Delay(GetCombatDelay(1000));
    }

    private async Task ExecuteDisarm(Character player, Monster monster, CombatResult result)
    {
        if (monster == null || string.IsNullOrEmpty(monster.Weapon))
        {
            terminal.WriteLine("Nothing to disarm!", "gray");
            await Task.Delay(GetCombatDelay(600));
            return;
        }

        long attackerScore = player.Dexterity + random.Next(1, 21);
        long defenderScore = (monster.Strength / 2) + random.Next(1, 21);

        if (attackerScore > defenderScore)
        {
            monster.WeapPow = 0;
            monster.Weapon = "";
            monster.WUser = false;
            terminal.WriteLine($"You knock the {monster.Name}'s weapon away!", "yellow");
            result.CombatLog.Add($"{player.DisplayName} disarmed {monster.Name}");
        }
        else
        {
            terminal.WriteLine("Disarm attempt failed!", "gray");
        }
        await Task.Delay(GetCombatDelay(900));
    }

    private async Task ExecuteTaunt(Character player, Monster monster, CombatResult result)
    {
        if (monster == null)
        {
            await Task.Delay(GetCombatDelay(500));
            return;
        }
        terminal.WriteLine($"You taunt {monster.Name}, drawing its ire!", "yellow");
        // Simple effect: lower monster defence for next round
        monster.Defence = Math.Max(0, monster.Defence - 2);
        result.CombatLog.Add($"{player.DisplayName} taunted {monster.Name}");
        await Task.Delay(GetCombatDelay(700));
    }

    private async Task ExecuteHide(Character player, CombatResult result)
    {
        // Dexterity check
        long roll = player.Dexterity + random.Next(1, 21);
        if (roll >= 15)
        {
            player.ApplyStatus(StatusEffect.Hidden, 1);
            terminal.WriteLine("You melt into the shadows, ready to strike!", "dark_gray");
            result.CombatLog.Add("Player hides (next attack gains advantage)");
        }
        else
        {
            terminal.WriteLine("You fail to find cover and remain exposed.", "gray");
        }
        await Task.Delay(GetCombatDelay(800));
    }

    /// <summary>
    /// Award experience to NPC teammates (spouses, lovers, team members)
    /// NPCs get 50% of the player's XP and can level up during combat
    /// </summary>
    private void AwardTeammateExperience(List<Character> teammates, long playerXP, TerminalEmulator terminal)
    {
        if (teammates == null || teammates.Count == 0 || playerXP <= 0) return;

        // Teammates get 50% of player's XP
        long teammateXP = playerXP / 2;
        if (teammateXP <= 0) return;

        // Count eligible teammates first (echoes don't get XP but still count for team bonus)
        int eligibleCount = 0;
        foreach (var t in teammates)
        {
            if (t != null && t.IsAlive && !t.IsCompanion && !t.IsEcho && t.Level < 100)
                eligibleCount++;
        }
        if (eligibleCount == 0) return;

        // Show header for teammate XP
        terminal.SetColor("gray");
        terminal.WriteLine($"Team XP (+{teammateXP} each):");

        foreach (var teammate in teammates)
        {
            if (teammate == null || !teammate.IsAlive) continue;
            if (teammate.IsCompanion) continue; // Companions are handled separately by CompanionSystem
            if (teammate.IsEcho) continue; // Player echoes don't gain XP (loaded from DB)
            if (teammate.Level >= 100) continue; // Max level cap

            // Award XP
            long previousXP = teammate.Experience;
            teammate.Experience += teammateXP;
            long xpNeeded = GetExperienceForLevel(teammate.Level + 1);

            // Show XP gain for all teammates
            terminal.SetColor("cyan");
            terminal.WriteLine($"  {teammate.DisplayName}: {teammate.Experience:N0}/{xpNeeded:N0}");
            terminal.SetColor("white");

            // Check for level up (using same formula as player/NPCs)
            long xpForNextLevel = GetExperienceForLevel(teammate.Level + 1);
            while (teammate.Experience >= xpForNextLevel && teammate.Level < 100)
            {
                teammate.Level++;

                // Apply stat gains on level up
                var random = new Random();
                teammate.BaseMaxHP += 10 + random.Next(5, 15);
                teammate.BaseStrength += random.Next(1, 3);
                teammate.BaseDefence += random.Next(1, 2);

                // Recalculate all stats with equipment bonuses
                teammate.RecalculateStats();

                // Restore HP to full on level up
                teammate.HP = teammate.MaxHP;

                terminal.SetColor("bright_green");
                terminal.WriteLine($"  {teammate.DisplayName} leveled up! (Lv {teammate.Level})");

                // Generate news for spouse/lover level ups
                NewsSystem.Instance?.Newsy(true, $"{teammate.DisplayName} has achieved Level {teammate.Level}!");

                // Calculate next threshold
                xpForNextLevel = GetExperienceForLevel(teammate.Level + 1);
            }
        }
    }

    /// <summary>
    /// XP formula matching the player's curve (level^1.8 * 50)
    /// </summary>
    private static long GetExperienceForLevel(int level)
    {
        if (level <= 1) return 0;
        long exp = 0;
        for (int i = 2; i <= level; i++)
        {
            exp += (long)(Math.Pow(i, 1.8) * 50);
        }
        return exp;
    }

    private int GetAttackCount(Character attacker)
    {
        int attacks = 1;

        // Warrior extra swings
        var mods = attacker.GetClassCombatModifiers();
        attacks += mods.ExtraAttacks;

        // Dual-wield bonus: +1 attack with off-hand weapon
        if (attacker.IsDualWielding)
            attacks += 1;

        // Agility-based extra attack chance (from StatEffectsSystem)
        if (StatEffectsSystem.RollExtraAttack(attacker))
            attacks += 1;

        // Shadow Crown artifact: extra strike per round (+2 during Manwe fight)
        if (ArtifactSystem.Instance.HasShadowCrown())
            attacks += IsManweBattle && ArtifactSystem.Instance.HasVoidKey() ? 2 : 1;

        // Drug-based extra attacks (e.g., Haste drug)
        var drugEffects = DrugSystem.GetDrugEffects(attacker);
        attacks += drugEffects.ExtraAttacks;

        // Speed penalty from drugs
        if (drugEffects.SpeedPenalty > 15)
            attacks = Math.Max(1, attacks - 1);

        // Haste doubles attacks
        if (attacker.HasStatus(StatusEffect.Haste))
            attacks *= 2;

        // Slow halves attacks (rounded down)
        if (attacker.HasStatus(StatusEffect.Slow))
            attacks = Math.Max(1, attacks / 2);

        return attacks;
    }

    /// <summary>
    /// Calculate attack damage modifier based on weapon configuration
    /// Two-Handed: +25% damage bonus
    /// Dual-Wield: Off-hand attack at 50% power (handled in attack count)
    /// Also applies alignment-based attack modifiers
    /// </summary>
    private double GetWeaponConfigDamageModifier(Character attacker, bool isOffHandAttack = false)
    {
        double modifier = 1.0;

        // Two-handed weapons get 25% damage bonus
        if (attacker.IsTwoHanding)
            modifier = 1.25;

        // Off-hand attacks in dual-wield do 50% damage
        if (isOffHandAttack && attacker.IsDualWielding)
            modifier = 0.50;

        // Apply alignment-based attack modifier
        var (attackMod, _) = AlignmentSystem.Instance.GetCombatModifiers(attacker);
        modifier *= attackMod;

        // Apply drug effects
        var drugEffects = DrugSystem.GetDrugEffects(attacker);
        if (drugEffects.DamageBonus > 0)
            modifier *= 1.0 + (drugEffects.DamageBonus / 100.0);
        if (drugEffects.StrengthBonus > 0)
            modifier *= 1.0 + (drugEffects.StrengthBonus / 200.0); // Half effect for strength
        if (drugEffects.AttackBonus > 0)
            modifier *= 1.0 + (drugEffects.AttackBonus / 100.0);

        return modifier;
    }

    /// <summary>
    /// Calculate defense modifier based on weapon configuration
    /// Two-Handed: -15% defense penalty (less defensive stance)
    /// Dual-Wield: -10% defense penalty (less focus on blocking)
    /// Shield: No penalty, plus chance for block
    /// Also applies alignment-based defense modifiers
    /// </summary>
    private double GetWeaponConfigDefenseModifier(Character defender)
    {
        double modifier = 1.0;

        if (defender.IsTwoHanding)
            modifier = 0.85; // 15% penalty
        else if (defender.IsDualWielding)
            modifier = 0.90; // 10% penalty

        // Apply alignment-based defense modifier
        var (_, defenseMod) = AlignmentSystem.Instance.GetCombatModifiers(defender);
        modifier *= defenseMod;

        // Apply drug effects
        var drugEffects = DrugSystem.GetDrugEffects(defender);
        if (drugEffects.DefenseBonus > 0)
            modifier *= 1.0 + (drugEffects.DefenseBonus / 100.0);
        if (drugEffects.ArmorBonus > 0)
            modifier *= 1.0 + (drugEffects.ArmorBonus / 100.0);
        if (drugEffects.DefensePenalty > 0)
            modifier *= 1.0 - (drugEffects.DefensePenalty / 100.0);

        return modifier;
    }

    /// <summary>
    /// Get alignment-specific bonus damage against evil/undead creatures
    /// Holy/Good characters deal extra damage vs evil, Evil characters drain life
    /// </summary>
    private (long bonusDamage, string description) GetAlignmentBonusDamage(Character attacker, Monster target, long baseDamage)
    {
        // Drug DarknessBonus temporarily boosts effective Darkness for alignment combat modifiers
        // (e.g., DemonBlood: +10 Darkness, pushing alignment toward Dark/Evil bonuses)
        var drugEffects = DrugSystem.GetDrugEffects(attacker);
        long savedDarkness = attacker.Darkness;
        if (drugEffects.DarknessBonus > 0)
            attacker.Darkness = Math.Min(1000, attacker.Darkness + drugEffects.DarknessBonus);

        var alignment = AlignmentSystem.Instance.GetAlignment(attacker);

        // Restore original Darkness after alignment check
        attacker.Darkness = savedDarkness;

        bool targetIsEvil = target.Level > 5 && (target.Name.Contains("Demon") || target.Name.Contains("Undead") ||
                            target.Name.Contains("Vampire") || target.Name.Contains("Lich") ||
                            target.Name.Contains("Devil") || target.Name.Contains("Skeleton") ||
                            target.Name.Contains("Zombie") || target.Name.Contains("Ghost"));

        switch (alignment)
        {
            case AlignmentSystem.AlignmentType.Holy:
                if (targetIsEvil)
                {
                    // Holy Smite: +25% damage vs evil/undead
                    long holyBonus = (long)(baseDamage * 0.25);
                    return (holyBonus, "Holy power burns the darkness!");
                }
                break;

            case AlignmentSystem.AlignmentType.Good:
                if (targetIsEvil)
                {
                    // Righteous Fury: +10% damage vs evil
                    long goodBonus = (long)(baseDamage * 0.10);
                    return (goodBonus, "Righteous fury guides your strike!");
                }
                break;

            case AlignmentSystem.AlignmentType.Evil:
                // Soul Drain: 10% of damage dealt heals the attacker
                long drainAmount = (long)(baseDamage * 0.10);
                attacker.HP = Math.Min(attacker.MaxHP, attacker.HP + drainAmount);
                return (0, $"Dark energy heals you for {drainAmount} HP!");

            case AlignmentSystem.AlignmentType.Dark:
                // Shadow Strike: Chance for fear effect (simulated as bonus damage)
                if (random.Next(100) < 15)
                {
                    long fearBonus = (long)(baseDamage * 0.15);
                    return (fearBonus, "Your dark presence terrifies the enemy!");
                }
                break;
        }

        return (0, "");
    }

    /// <summary>
    /// Check for shield block and return bonus AC if successful
    /// 20% chance to block, which doubles shield AC for that hit
    /// </summary>
    private (bool blocked, int bonusAC) TryShieldBlock(Character defender)
    {
        if (!defender.HasShieldEquipped)
            return (false, 0);

        var shield = defender.GetEquipment(EquipmentSlot.OffHand);
        if (shield == null)
            return (false, 0);

        // 20% chance to block
        if (random.Next(100) < 20)
        {
            // Double the shield's AC bonus when blocking
            return (true, shield.ShieldBonus);
        }

        return (false, 0);
    }

    /// <summary>
    /// Process plague and disease damage during combat
    /// Affected by both WorldEventSystem plague outbreaks and character's personal disease status
    /// </summary>
    private async Task ProcessPlagueDamage(Character player, CombatResult result)
    {
        bool hasDisease = player.Plague || player.Smallpox || player.Measles || player.Leprosy;
        bool worldPlague = WorldEventSystem.Instance.PlaguActive;

        // No damage if no disease
        if (!hasDisease && !worldPlague) return;

        // Calculate damage based on disease type and world plague
        long plagueDamage = 0;
        string diseaseMessage = "";

        if (player.Plague)
        {
            // Plague: 3-5% of max HP per round
            plagueDamage += (long)(player.MaxHP * (0.03 + random.NextDouble() * 0.02));
            diseaseMessage = "The plague ravages your body!";
        }
        else if (player.Leprosy)
        {
            // Leprosy: 2-3% of max HP per round
            plagueDamage += (long)(player.MaxHP * (0.02 + random.NextDouble() * 0.01));
            diseaseMessage = "Leprosy weakens your limbs!";
        }
        else if (player.Smallpox)
        {
            // Smallpox: 1-2% of max HP per round
            plagueDamage += (long)(player.MaxHP * (0.01 + random.NextDouble() * 0.01));
            diseaseMessage = "Smallpox saps your strength!";
        }
        else if (player.Measles)
        {
            // Measles: 1% of max HP per round
            plagueDamage += (long)(player.MaxHP * 0.01);
            diseaseMessage = "Measles makes you feverish!";
        }

        // World plague adds extra damage if active (even to healthy characters)
        if (worldPlague && !hasDisease)
        {
            // Plague in the air: 1% chance to take minor damage per round
            if (random.Next(100) < 10)
            {
                plagueDamage += (long)(player.MaxHP * 0.01);
                diseaseMessage = "The plague in the air sickens you!";

                // Small chance to contract the plague during combat
                if (random.Next(100) < 5)
                {
                    player.Plague = true;
                    terminal.SetColor("bright_red");
                    terminal.WriteLine("You have contracted the plague!");
                    await Task.Delay(GetCombatDelay(1000));
                }
            }
        }
        else if (worldPlague && hasDisease)
        {
            // World plague amplifies personal disease damage by 25%
            plagueDamage = (long)(plagueDamage * 1.25);
        }

        // Apply damage if any
        if (plagueDamage > 0)
        {
            plagueDamage = Math.Max(1, plagueDamage);
            player.HP = Math.Max(0, player.HP - plagueDamage);

            terminal.SetColor("yellow");
            terminal.WriteLine($"  {diseaseMessage} (-{plagueDamage} HP)");
            result.CombatLog.Add($"Disease damage: {plagueDamage}");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Get quickbar actions for combat display and input handling.
    /// Reads from player.Quickbar which contains both spell IDs ("spell:5") and ability IDs ("power_strike").
    /// Returns list of (hotkey, slotId, display name, is available)
    /// </summary>
    private List<(string key, string slotId, string displayName, bool available)> GetQuickbarActions(Character player)
    {
        var actions = new List<(string, string, string, bool)>();
        if (player.Quickbar == null) return actions;

        for (int i = 0; i < Math.Min(9, player.Quickbar.Count); i++)
        {
            var slotId = player.Quickbar[i];
            if (string.IsNullOrEmpty(slotId)) continue;

            var spellLevel = SpellSystem.ParseQuickbarSpellLevel(slotId);
            if (spellLevel.HasValue)
            {
                // Spell slot
                var spell = SpellSystem.GetSpellInfo(player.Class, spellLevel.Value);
                if (spell == null) continue;
                int manaCost = SpellSystem.CalculateManaCost(spell, player);
                bool canCast = player.CanCastSpells() && player.Mana >= manaCost;
                string displayName;
                if (!player.CanCastSpells())
                    displayName = $"{spell.Name} (SILENCED)";
                else
                    displayName = $"{spell.Name} ({manaCost} MP)";
                actions.Add(((i + 1).ToString(), slotId, displayName, canCast));
            }
            else
            {
                // Ability slot
                var ability = ClassAbilitySystem.GetAbility(slotId);
                if (ability == null) continue;
                bool canUse = ClassAbilitySystem.CanUseAbility(player, slotId, abilityCooldowns);
                string displayName;
                if (abilityCooldowns.TryGetValue(slotId, out int cd) && cd > 0)
                    displayName = $"{ability.Name} (CD:{cd})";
                else
                    displayName = $"{ability.Name} ({ability.StaminaCost} ST)";
                actions.Add(((i + 1).ToString(), slotId, displayName, canUse));
            }
        }
        return actions;
    }

    /// <summary>
    /// Get quickbar actions in the old (key, name, available) format for menu display compatibility.
    /// </summary>
    private List<(string key, string name, bool available)> GetClassSpecificActions(Character player)
    {
        return GetQuickbarActions(player).Select(a => (a.key, a.displayName, a.available)).ToList();
    }

    /// <summary>
    /// Handle quickbar action input (1-9) for multi-monster combat.
    /// Reads from player.Quickbar and handles both spells and abilities.
    /// Returns the action type, target index, ability ID, and spell index if valid, null if invalid.
    /// </summary>
    private async Task<(CombatActionType type, int? target, string abilityId, int spellIndex, int? allyTarget, bool targetAll)?> HandleQuickbarAction(Character player, string key, List<Monster> monsters)
    {
        var quickbarActions = GetQuickbarActions(player);
        var matched = quickbarActions.FirstOrDefault(a => a.key == key);

        if (string.IsNullOrEmpty(matched.slotId))
        {
            terminal.WriteLine("That quickbar slot is empty!", "yellow");
            await Task.Delay(GetCombatDelay(1000));
            return null;
        }

        // Check availability
        if (!matched.available)
        {
            var spellLevel = SpellSystem.ParseQuickbarSpellLevel(matched.slotId);
            if (spellLevel.HasValue)
            {
                var spell = SpellSystem.GetSpellInfo(player.Class, spellLevel.Value);
                if (spell != null)
                {
                    int manaCost = SpellSystem.CalculateManaCost(spell, player);
                    if (!player.CanCastSpells())
                        terminal.WriteLine($"{spell.Name} cannot be cast - you are SILENCED!", "red");
                    else
                        terminal.WriteLine($"Not enough mana! Need {manaCost}, have {player.Mana}.", "red");
                }
            }
            else
            {
                var ability = ClassAbilitySystem.GetAbility(matched.slotId);
                if (ability != null)
                {
                    if (player.CurrentCombatStamina < ability.StaminaCost)
                        terminal.WriteLine($"Not enough stamina! Need {ability.StaminaCost}, have {player.CurrentCombatStamina}.", "red");
                    else if (abilityCooldowns.TryGetValue(matched.slotId, out int cd) && cd > 0)
                        terminal.WriteLine($"{ability.Name} is on cooldown for {cd} more rounds!", "red");
                }
            }
            await Task.Delay(GetCombatDelay(1000));
            return null;
        }

        // Handle spell quickbar slot
        var spLevel = SpellSystem.ParseQuickbarSpellLevel(matched.slotId);
        if (spLevel.HasValue)
        {
            var spellInfo = SpellSystem.GetSpellInfo(player.Class, spLevel.Value);
            if (spellInfo == null) return null;

            // Check spell type for targeting
            if (spellInfo.SpellType == "Buff" || spellInfo.SpellType == "Heal")
            {
                int? allyTarget = null;
                if (spellInfo.SpellType == "Heal" && currentTeammates?.Any(t => t.IsAlive && t.HP < t.MaxHP) == true)
                {
                    allyTarget = await SelectHealTarget(player);
                }
                return (CombatActionType.CastSpell, null, "", spLevel.Value, allyTarget, false);
            }
            else if (spellInfo.IsMultiTarget)
            {
                terminal.WriteLine("");
                terminal.Write("Target all monsters? (Y/N): ");
                var targetAllResponse = await terminal.GetInput("");
                bool targetAll = targetAllResponse.Trim().ToUpper() == "Y";
                int? targetIdx = null;
                if (!targetAll)
                {
                    targetIdx = await GetTargetSelection(monsters, allowRandom: false);
                }
                return (CombatActionType.CastSpell, targetIdx, "", spLevel.Value, null, targetAll);
            }
            else
            {
                // Single target attack/debuff spell
                var targetIdx = await GetTargetSelection(monsters, allowRandom: false);
                return (CombatActionType.CastSpell, targetIdx, "", spLevel.Value, null, false);
            }
        }

        // Handle ability quickbar slot
        return (CombatActionType.ClassAbility, null, matched.slotId, 0, null, false);
    }

    /// <summary>
    /// Handle quickbar slot input (1-9) for single-monster combat.
    /// Resolves the quickbar slot to either a spell cast or ability use action.
    /// Returns a CombatAction if valid, null if empty/cancelled/unavailable.
    /// </summary>
    private async Task<CombatAction?> HandleQuickbarActionSingleMonster(Character player, string key, Monster? monster)
    {
        var quickbarActions = GetQuickbarActions(player);
        var matched = quickbarActions.FirstOrDefault(a => a.key == key);

        if (string.IsNullOrEmpty(matched.slotId))
        {
            terminal.WriteLine("That quickbar slot is empty!", "yellow");
            await Task.Delay(GetCombatDelay(1000));
            return null;
        }

        // Check availability
        if (!matched.available)
        {
            var spellLevel = SpellSystem.ParseQuickbarSpellLevel(matched.slotId);
            if (spellLevel.HasValue)
            {
                var spell = SpellSystem.GetSpellInfo(player.Class, spellLevel.Value);
                if (spell != null)
                {
                    int manaCost = SpellSystem.CalculateManaCost(spell, player);
                    if (!player.CanCastSpells())
                        terminal.WriteLine($"{spell.Name} cannot be cast - you are SILENCED!", "red");
                    else
                        terminal.WriteLine($"Not enough mana! Need {manaCost}, have {player.Mana}.", "red");
                }
            }
            else
            {
                var ability = ClassAbilitySystem.GetAbility(matched.slotId);
                if (ability != null)
                {
                    if (player.CurrentCombatStamina < ability.StaminaCost)
                        terminal.WriteLine($"Not enough stamina! Need {ability.StaminaCost}, have {player.CurrentCombatStamina}.", "red");
                    else if (abilityCooldowns.TryGetValue(matched.slotId, out int cd) && cd > 0)
                        terminal.WriteLine($"{ability.Name} is on cooldown for {cd} more rounds!", "red");
                }
            }
            await Task.Delay(GetCombatDelay(1000));
            return null;
        }

        // Handle spell quickbar slot
        var spLevel = SpellSystem.ParseQuickbarSpellLevel(matched.slotId);
        if (spLevel.HasValue)
        {
            return new CombatAction
            {
                Type = CombatActionType.CastSpell,
                SpellIndex = spLevel.Value
            };
        }

        // Handle ability quickbar slot
        return new CombatAction
        {
            Type = CombatActionType.ClassAbility,
            AbilityId = matched.slotId
        };
    }

    /// <summary>
    /// Execute a learned class ability
    /// </summary>
    private async Task ExecuteLearnedAbility(Character player, string abilityId, Monster monster, CombatResult result)
    {
        var ability = ClassAbilitySystem.GetAbility(abilityId);
        if (ability == null) return;

        // Use the ability
        var abilityResult = ClassAbilitySystem.UseAbility(player, abilityId, random);

        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"» {player.Name2} uses {ability.Name}!");
        await Task.Delay(GetCombatDelay(500));

        // Apply effects based on ability type
        if (abilityResult.Damage > 0 && monster != null)
        {
            // Handle special effects
            int actualDamage = abilityResult.Damage;

            if (ability.SpecialEffect == "execute" && monster.HP < monster.MaxHP * 0.3)
            {
                actualDamage *= 2;
                terminal.SetColor("bright_red");
                terminal.WriteLine("EXECUTION! Double damage to wounded enemy!");
            }
            else if (ability.SpecialEffect == "last_stand" && player.HP < player.MaxHP * 0.3)
            {
                actualDamage = (int)(actualDamage * 1.5);
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("Last Stand! Fighting with desperate strength!");
            }

            monster.HP -= actualDamage;
            result.TotalDamageDealt += actualDamage;
            player.Statistics.RecordDamageDealt(actualDamage, false);
            terminal.SetColor("bright_green");
            terminal.WriteLine($"Dealt {actualDamage} damage to {monster.Name}!");
        }

        if (abilityResult.Healing > 0)
        {
            int healed = (int)Math.Min(abilityResult.Healing, player.MaxHP - player.HP);
            player.HP += healed;
            terminal.SetColor("bright_green");
            terminal.WriteLine($"Recovered {healed} HP!");
        }

        if (abilityResult.AttackBonus > 0 || abilityResult.DefenseBonus != 0)
        {
            // Apply as temporary buff
            if (abilityResult.AttackBonus > 0)
            {
                player.TempAttackBonus += abilityResult.AttackBonus;
                player.TempAttackBonusDuration = Math.Max(player.TempAttackBonusDuration, abilityResult.Duration);
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"+{abilityResult.AttackBonus} Attack for {abilityResult.Duration} rounds!");
            }

            if (abilityResult.DefenseBonus != 0)
            {
                player.TempDefenseBonus += abilityResult.DefenseBonus;
                player.TempDefenseBonusDuration = Math.Max(player.TempDefenseBonusDuration, abilityResult.Duration);
                string sign = abilityResult.DefenseBonus >= 0 ? "+" : "";
                terminal.SetColor(abilityResult.DefenseBonus >= 0 ? "bright_cyan" : "yellow");
                terminal.WriteLine($"{sign}{abilityResult.DefenseBonus} Defense for {abilityResult.Duration} rounds!");
            }
        }

        // Set cooldown
        if (abilityResult.CooldownApplied > 0)
        {
            abilityCooldowns[abilityId] = abilityResult.CooldownApplied;
        }

        // Display training improvement message if ability proficiency increased
        if (abilityResult.SkillImproved && !string.IsNullOrEmpty(abilityResult.NewProficiencyLevel))
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"  Your {ability.Name} proficiency improved to {abilityResult.NewProficiencyLevel}!");
        }

        await Task.Delay(GetCombatDelay(800));
    }

    // ==================== BOSS COMBAT HELPERS ====================

    /// <summary>
    /// Play boss phase transition dialogue when HP crosses a threshold
    /// </summary>
    private async Task PlayBossPhaseTransition(BossCombatContext ctx, Monster boss, int newPhase)
    {
        terminal.WriteLine("");
        terminal.SetColor("bright_red");
        terminal.WriteLine($"═══ PHASE {newPhase} ═══");
        terminal.WriteLine("");

        var dialogue = newPhase switch
        {
            2 => ctx.BossData.Phase2Dialogue,
            3 => ctx.BossData.Phase3Dialogue,
            _ => Array.Empty<string>()
        };

        foreach (var line in dialogue)
        {
            terminal.SetColor(ctx.BossData.ThemeColor);
            terminal.WriteLine($"  \"{line}\"");
            await Task.Delay(200);
        }

        if (newPhase == 2)
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine($"  {boss.Name} grows more powerful!");
        }
        else if (newPhase == 3)
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine($"  {boss.Name} unleashes their true form!");
        }

        terminal.WriteLine("");
        await Task.Delay(1500);
    }

    /// <summary>
    /// Create spectral soldier Monster objects for Maelketh's minion summons
    /// </summary>
    private List<Monster> CreateSpectralSoldiers(int count, int bossLevel)
    {
        var soldiers = new List<Monster>();
        for (int i = 0; i < count; i++)
        {
            long soldierHP = 50 + bossLevel * 5;
            var soldier = new Monster
            {
                Name = "Spectral Soldier",
                Level = Math.Max(1, bossLevel - 5),
                HP = soldierHP,
                MaxHP = soldierHP,
                Strength = 10 + bossLevel * 2,
                Defence = (int)(5 + bossLevel),
                WeapPow = 5 + bossLevel,
                ArmPow = 3 + bossLevel / 2,
                MonsterColor = "dark_red",
                FamilyName = "Spectral",
                IsBoss = false,
                IsActive = true,
                CanSpeak = false
            };
            soldiers.Add(soldier);
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_red");
        terminal.WriteLine($"  {count} Spectral Soldiers materialize from the shadows!");
        return soldiers;
    }

    /// <summary>
    /// Select a random boss ability name based on current phase
    /// </summary>
    private string SelectBossAbility(BossCombatContext ctx)
    {
        var abilities = ctx.BossData.Abilities.Where(a => a.Phase <= ctx.CurrentPhase).ToList();
        if (abilities.Count == 0) return "Divine Strike";
        return abilities[random.Next(abilities.Count)].Name;
    }

    /// <summary>
    /// Attempt to save an Old God boss using the Soulweaver's Loom
    /// </summary>
    private async Task<bool> AttemptBossSave(Character player, Monster bossMonster, CombatResult result)
    {
        if (BossContext == null) return false;

        var boss = BossContext.BossData;

        if (!boss.CanBeSaved)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"  {boss.Name} cannot be saved.");
            return false;
        }

        if (!ArtifactSystem.Instance.HasArtifact(ArtifactType.SoulweaversLoom))
        {
            terminal.SetColor("red");
            terminal.WriteLine("  You need the Soulweaver's Loom!");
            return false;
        }

        // Saving requires boss to be below 50% HP
        double hpPercent = (double)bossMonster.HP / Math.Max(1, bossMonster.MaxHP);
        if (hpPercent > 0.5)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"  {boss.Name} is too strong to save yet.");
            terminal.SetColor("gray");
            terminal.WriteLine("  Weaken them first.");
            return false;
        }

        // Requires positive alignment
        if (player.Chivalry - player.Darkness < 200)
        {
            terminal.SetColor("dark_red");
            terminal.WriteLine("  Your heart is too dark to use the Loom.");
            return false;
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("  The Soulweaver's Loom glows with ancient power.");
        await Task.Delay(800);

        terminal.SetColor("white");
        terminal.WriteLine($"  You reach out to {boss.Name}'s corrupted essence...");
        await Task.Delay(1000);

        // Display save dialogue
        if (boss.SaveDialogue != null)
        {
            foreach (var line in boss.SaveDialogue)
            {
                terminal.SetColor("bright_cyan");
                terminal.WriteLine($"  \"{line}\"");
                await Task.Delay(300);
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_green");
        terminal.WriteLine($"  {boss.Name} is cleansed of corruption!");

        return true;
    }
}

/// <summary>
/// Combat action types - Pascal menu options
/// </summary>
public enum CombatActionType
{
    None,           // No action (stunned, etc.)
    Attack,
    Defend,
    Heal,
    QuickHeal,
    FightToDeath,
    Status,
    BegForMercy,
    UseItem,
    CastSpell,
    UseAbility,     // Use a class ability from ClassAbilitySystem
    ClassAbility,   // Execute a learned class ability
    SoulStrike,     // Paladin ability (legacy)
    Backstab,       // Assassin ability (legacy)
    Retreat,
    PowerAttack,
    PreciseStrike,
    Rage,
    Smite,
    Disarm,
    Taunt,
    Hide,
    RangedAttack,
    HealAlly,       // Heal a teammate with potion or spell
    BossSave        // Attempt to save an Old God boss mid-combat
}

/// <summary>
/// Combat action data
/// </summary>
public class CombatAction
{
    public CombatActionType Type { get; set; }
    public int SpellIndex { get; set; }
    public int ItemIndex { get; set; }
    public string TargetId { get; set; } = "";
    public string AbilityId { get; set; } = "";   // For UseAbility action type

    // Multi-monster combat support
    public int? TargetIndex { get; set; }         // Which monster (0-based index) or null for random
    public bool TargetAllMonsters { get; set; }   // True for AoE abilities

    // Ally targeting for heal spells
    public int? AllyTargetIndex { get; set; }     // Which teammate to heal (null = self)
}

/// <summary>
/// Combat result data
/// </summary>
public class CombatResult
{
    public Character Player { get; set; }

    // Multi-monster combat support
    public List<Monster> Monsters { get; set; } = new();
    public List<Monster> DefeatedMonsters { get; set; } = new();

    // Backward compatibility - returns first monster
    public Monster Monster
    {
        get => Monsters?.FirstOrDefault();
        set
        {
            if (Monsters == null) Monsters = new();
            if (value != null && !Monsters.Contains(value))
                Monsters.Add(value);
        }
    }

    public Character Opponent { get; set; }           // For PvP
    public List<Character> Teammates { get; set; } = new();
    public CombatOutcome Outcome { get; set; }
    public List<string> CombatLog { get; set; } = new();
    public long ExperienceGained { get; set; }
    public long GoldGained { get; set; }
    public List<string> ItemsFound { get; set; } = new();

    // Damage tracking for berserker mode
    public long TotalDamageDealt { get; set; }
    public long TotalDamageTaken { get; set; }

    // Simple outcome flags
    public bool Victory { get; set; }
    public bool MonsterKilled { get; set; }
    public bool PlayerDied { get; set; }

    // Resurrection flags
    public bool ShouldReturnToTemple { get; set; }
}

/// <summary>
/// Combat outcomes
/// </summary>
public enum CombatOutcome
{
    Victory,
    PlayerDied,
    PlayerEscaped,
    Stalemate,
    Interrupted
}

/// <summary>
/// Context for Old God boss fights routed through CombatEngine.
/// Set by OldGodBossSystem before calling PlayerVsMonsters.
/// </summary>
public class BossCombatContext
{
    public OldGodBossData BossData { get; set; }
    public OldGodType GodType { get; set; }
    public int CurrentPhase { get; set; } = 1;
    public int AttacksPerRound { get; set; } = 2;
    public bool CanSave { get; set; }
    public bool BossSaved { get; set; }
    public int RoundsSinceLastSummon { get; set; }

    // Dialogue-based combat modifiers (set by OldGodBossSystem)
    public double DamageMultiplier { get; set; } = 1.0;
    public double DefenseMultiplier { get; set; } = 1.0;
    public int BonusDamage { get; set; }
    public int BonusDefense { get; set; }
    public double CriticalChance { get; set; } = 0.05;
    public bool HasRageBoost { get; set; }
    public bool HasInsight { get; set; }
    public double BossDamageMultiplier { get; set; } = 1.0;
    public double BossDefenseMultiplier { get; set; } = 1.0;
    public bool BossConfused { get; set; }
    public bool BossWeakened { get; set; }

    /// <summary>
    /// Determine what phase the boss should be in based on HP percentage
    /// </summary>
    public int CheckPhase(long currentHP, long maxHP)
    {
        double pct = (double)currentHP / Math.Max(1, maxHP);
        if (pct <= 0.20) return 3;
        if (pct <= 0.50) return 2;
        return 1;
    }
}
