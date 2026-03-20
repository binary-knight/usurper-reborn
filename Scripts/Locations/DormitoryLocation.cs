using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using UsurperRemake.BBS;
using UsurperRemake.Systems;

namespace UsurperRemake.Locations;

/// <summary>
/// Dormitory – rest & recovery hub (Pascal DORM.PAS minimal port).
/// In online mode, sleeping here logs the player out and leaves them
/// vulnerable to NPC and player attacks while offline.
/// </summary>
public class DormitoryLocation : BaseLocation
{
    private List<NPC> sleepers = new();
    private readonly Random rng = new();

    public DormitoryLocation() : base(GameLocation.Dormitory,
        "Dormitory",
        "Rows of squeaky wooden bunks line the walls; weary adventurers snore under thin blankets.")
    {
    }

    protected override void SetupLocation()
    {
        PossibleExits.Add(GameLocation.AnchorRoad);
        PossibleExits.Add(GameLocation.MainStreet);
    }

    protected override void DisplayLocation()
    {
        terminal.ClearScreen();

        // Header
        WriteBoxHeader(Loc.Get("dormitory.header"), "bright_cyan");
        terminal.WriteLine("");

        // Atmosphere
        terminal.SetColor("white");
        terminal.Write(Loc.Get("dormitory.desc1"));
        terminal.WriteLine(Loc.Get("dormitory.desc2"));
        terminal.Write(Loc.Get("dormitory.desc3"));
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("dormitory.desc4"));
        terminal.WriteLine("");

        ShowNPCsInLocation();

        // Menu
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("dormitory.what_to_do"));
        terminal.WriteLine("");

        if (DoorMode.IsOnlineMode)
        {
            // Row 1
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("L");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("dormitory.list_sleepers"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("E");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("dormitory.examine_menu"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("G");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dormitory.go_sleep_online", GameConfig.DormitorySleepCost));

            // Row 2
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("K");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("dormitory.kill_sleeper"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("W");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("dormitory.wake_guests"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("S");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dormitory.status_menu"));

            // Row 3
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("R");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dormitory.return_anchor"));
        }
        else
        {
            // Row 1
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("L");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("dormitory.list_sleepers"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("E");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("dormitory.examine_menu"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("G");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dormitory.go_sleep"));

            // Row 2
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("W");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("dormitory.wake_guests_sp"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("S");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("dormitory.status_menu_sp"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("R");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dormitory.return_short"));
        }
        terminal.WriteLine("");

        if (DoorMode.IsOnlineMode)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("dormitory.sleep_warning"));
            terminal.WriteLine("");
        }

        ShowStatusLine();
    }

    protected override async Task<bool> ProcessChoice(string choice)
    {
        var (handled, shouldExit) = await TryProcessGlobalCommand(choice);
        if (handled) return shouldExit;

        if (string.IsNullOrWhiteSpace(choice)) return false;
        char ch = char.ToUpperInvariant(choice.Trim()[0]);

        switch (ch)
        {
            case 'L':
                await ListSleepers();
                return false;
            case 'E':
                await ExamineSleeper();
                return false;
            case 'G':
                await GoToSleep();
                return true;
            case 'K':
                if (DoorMode.IsOnlineMode)
                    await AttackSleeper();
                return false;
            case 'W':
                await WakeGuests();
                return false;
            case 'S':
                await ShowStatus();
                return false;
            case 'R':
                await NavigateToLocation(GameLocation.MainStreet);
                return true;
            default:
                return false;
        }
    }

    #region Helper Methods

    private void RefreshSleepers()
    {
        sleepers = LocationManager.Instance.GetNPCsInLocation(GameLocation.Dormitory)
                    .Where(n => n.IsAlive)
                    .ToList();

        // Populate with wanderers if empty
        if (sleepers.Count < 4)
        {
            foreach (var npc in GameEngine.Instance.GetNPCsInLocation(GameLocation.MainStreet))
            {
                if (sleepers.Count >= 8) break;
                if (rng.NextDouble() < 0.05)
                {
                    LocationManager.Instance.RemoveNPCFromLocation(GameLocation.MainStreet, npc);
                    LocationManager.Instance.AddNPCToLocation(GameLocation.Dormitory, npc);
                    npc.UpdateLocation("dormitory");
                    sleepers.Add(npc);
                }
            }
        }
    }

    private async Task ListSleepers()
    {
        RefreshSleepers();
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("dormitory.sleeping_guests"));
        terminal.SetColor("cyan");

        // Show NPC sleepers
        if (sleepers.Count == 0 && !DoorMode.IsOnlineMode)
        {
            terminal.WriteLine(Loc.Get("dormitory.no_sleepers"));
        }
        else
        {
            int idx = 1;
            foreach (var npc in sleepers)
            {
                terminal.WriteLine($"{idx,3}. {npc.Name2} ({Loc.Get("common.lvl")} {npc.Level})");
                idx++;
            }

            // Show sleeping NPCs from world sim
            if (DoorMode.IsOnlineMode)
            {
                var dormNPCs = WorldSimulator.GetSleepingNPCsAt("dormitory");
                if (dormNPCs.Count > 0)
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"\n{Loc.Get("dormitory.sleeping_npcs")}");
                    foreach (var npcName in dormNPCs)
                    {
                        terminal.WriteLine($"  {idx,3}. {npcName} {Loc.Get("dormitory.sleeping_npc_tag")}", "yellow");
                        idx++;
                    }
                }

                // Show offline player sleepers at dormitory
                var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
                if (backend != null)
                {
                    var offlineSleepers = await backend.GetSleepingPlayers();
                    var dormSleepers = offlineSleepers
                        .Where(s => s.SleepLocation == "dormitory" && !s.IsDead)
                        .ToList();
                    if (dormSleepers.Count > 0)
                    {
                        terminal.SetColor("dark_red");
                        terminal.WriteLine($"\n{Loc.Get("dormitory.vulnerable_players")}");
                        foreach (var s in dormSleepers)
                        {
                            terminal.WriteLine($"  {idx,3}. {s.Username} {Loc.Get("dormitory.sleeping_tag")}", "red");
                            idx++;
                        }
                    }
                }
            }
        }
        terminal.WriteLine(Loc.Get("dormitory.press_enter"));
        await terminal.WaitForKeyPress();
    }

    private async Task ExamineSleeper()
    {
        RefreshSleepers();
        if (sleepers.Count == 0)
        {
            terminal.WriteLine(Loc.Get("dormitory.nobody_examine"), "gray");
            await Task.Delay(1500);
            return;
        }
        var input = await terminal.GetInput(Loc.Get("dormitory.enter_sleeper"));
        NPC? npc = null;
        if (int.TryParse(input, out int num) && num >= 1 && num <= sleepers.Count)
            npc = sleepers[num - 1];
        else
            npc = sleepers.FirstOrDefault(n => n.Name2.Equals(input, StringComparison.OrdinalIgnoreCase));

        if (npc == null)
        {
            terminal.WriteLine(Loc.Get("dormitory.no_such_sleeper"), "red");
            await Task.Delay(1500);
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(npc.Name2);
        if (!IsScreenReader)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(new string('═', npc.Name2.Length));
        }
        terminal.SetColor("white");
        terminal.WriteLine(npc.GetDisplayInfo());
        terminal.WriteLine(Loc.Get("dormitory.press_enter"));
        await terminal.WaitForKeyPress();
    }

    private async Task GoToSleep()
    {
        if (DoorMode.IsOnlineMode)
        {
            await GoToSleepOnline();
            return;
        }

        // Single-player: classic behavior
        var confirm = await terminal.GetInput(Loc.Get("dormitory.stay_night"));
        if (!confirm.Equals("Y", StringComparison.OrdinalIgnoreCase))
            return;

        terminal.ClearScreen();
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("dormitory.claim_bunk"));
        await Task.Delay(1500);

        currentPlayer.HP = currentPlayer.MaxHP;
        currentPlayer.Mana = currentPlayer.MaxMana;
        currentPlayer.Stamina = Math.Max(currentPlayer.Stamina, currentPlayer.Constitution * 2);

        if (!UsurperRemake.BBS.DoorMode.IsOnlineMode)
        {
            await DailySystemManager.Instance.ForceDailyReset();
        }

        terminal.WriteLine(Loc.Get("dormitory.awaken_refreshed"), "green");
        await terminal.PressAnyKey();

        await NavigateToLocation(GameLocation.MainStreet);
    }

    private async Task GoToSleepOnline()
    {
        int cost = GameConfig.DormitorySleepCost;
        terminal.SetColor("yellow");
        terminal.WriteLine($"\n{Loc.Get("dormitory.cost_bunk", cost)}");
        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("dormitory.sleep_vulnerable"));
        terminal.SetColor("white");

        var confirm = await terminal.GetInput(Loc.Get("dormitory.sleep_logout"));
        if (!confirm.Equals("Y", StringComparison.OrdinalIgnoreCase))
            return;

        if (currentPlayer.Gold >= cost)
        {
            currentPlayer.Gold -= cost;
        }
        else if (currentPlayer.Gold + currentPlayer.BankGold >= cost)
        {
            long shortfall = cost - currentPlayer.Gold;
            currentPlayer.Gold = 0;
            currentPlayer.BankGold -= shortfall;
            terminal.WriteLine(Loc.Get("dormitory.bank_withdrawn", shortfall.ToString("N0")), "gray");
        }
        else
        {
            terminal.WriteLine(Loc.Get("dormitory.cant_afford"), "red");
            terminal.WriteLine(Loc.Get("dormitory.checked_gold", currentPlayer.Gold.ToString("N0"), currentPlayer.BankGold.ToString("N0")), "gray");
            await Task.Delay(1500);
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("dormitory.claim_thin_blanket"));
        await Task.Delay(1000);

        // Restore HP/Mana/Stamina
        currentPlayer.HP = currentPlayer.MaxHP;
        currentPlayer.Mana = currentPlayer.MaxMana;
        currentPlayer.Stamina = Math.Max(currentPlayer.Stamina, currentPlayer.Constitution * 2);

        if (!UsurperRemake.BBS.DoorMode.IsOnlineMode)
        {
            await DailySystemManager.Instance.ForceDailyReset();
        }

        // Save game
        await GameEngine.Instance.SaveCurrentGame();

        // Register as sleeping (vulnerable, no guards)
        var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
        if (backend != null)
        {
            var username = DoorMode.OnlineUsername ?? currentPlayer.Name2;
            await backend.RegisterSleepingPlayer(username, "dormitory", "[]", 0);
        }

        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("dormitory.drift_uneasy"));
        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("dormitory.slit_throat"));
        await Task.Delay(2000);

        throw new LocationExitException(GameLocation.NoWhere);
    }

    private async Task AttackSleeper()
    {
        var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
        if (backend == null)
        {
            terminal.WriteLine(Loc.Get("dormitory.not_available"), "gray");
            await Task.Delay(1000);
            return;
        }

        // Gather targets: sleeping NPCs at dormitory + offline players at dormitory
        var sleepingNPCNames = WorldSimulator.GetSleepingNPCsAt("dormitory");
        var offlineSleepers = await backend.GetSleepingPlayers();
        var dormPlayerSleepers = offlineSleepers
            .Where(s => s.SleepLocation == "dormitory" && !s.IsDead)
            .Where(s => !s.Username.Equals(DoorMode.OnlineUsername ?? "", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (sleepingNPCNames.Count == 0 && dormPlayerSleepers.Count == 0)
        {
            terminal.WriteLine(Loc.Get("dormitory.no_vulnerable"), "gray");
            await Task.Delay(1500);
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine(Loc.Get("dormitory.vulnerable_title"));
        terminal.WriteLine("");

        // Build combined target list (skip NPCs on the player's team or spouse/lover)
        // Level filter: can only attack sleepers within ±5 levels
        string playerTeam = currentPlayer.Team ?? "";
        string playerName = currentPlayer.Name2 ?? currentPlayer.Name1 ?? "";
        int attackerLevel = currentPlayer.Level;
        var targets = new List<(string name, bool isNPC)>();
        foreach (var npcName in sleepingNPCNames)
        {
            var npc = NPCSpawnSystem.Instance.GetNPCByName(npcName);
            // Don't allow attacking your own team members
            if (npc != null && !string.IsNullOrEmpty(playerTeam) &&
                playerTeam.Equals(npc.Team, StringComparison.OrdinalIgnoreCase))
                continue;
            // Don't allow attacking your spouse or lover
            if (npc != null && (npc.SpouseName.Equals(playerName, StringComparison.OrdinalIgnoreCase)
                || RelationshipSystem.IsMarriedOrLover(npcName, playerName)))
                continue;
            if (npc != null && Math.Abs(npc.Level - attackerLevel) > 5)
                continue;
            string lvlStr = npc != null ? $" ({Loc.Get("common.lvl")} {npc.Level})" : "";
            terminal.WriteLine($"  {targets.Count + 1}. {npcName}{lvlStr} {Loc.Get("dormitory.sleeping_npc_tag")}", "yellow");
            targets.Add((npcName, true));
        }
        foreach (var s in dormPlayerSleepers)
        {
            // Skip accounts that have no character data (registered but never finished creation)
            var targetSave = await backend.ReadGameData(s.Username);
            if (targetSave?.Player == null)
                continue;

            // Level filter: can only attack players within ±5 levels
            int targetLevel = targetSave.Player.Level;
            if (Math.Abs(targetLevel - attackerLevel) > 5)
                continue;

            terminal.WriteLine($"  {targets.Count + 1}. {s.Username} ({Loc.Get("common.lvl")} {targetLevel}) {Loc.Get("dormitory.sleeping_player_tag")}", "red");
            targets.Add((s.Username, false));
        }

        terminal.SetColor("white");
        var input = await terminal.GetInput($"\n{Loc.Get("dormitory.who_attack")}");
        if (string.IsNullOrWhiteSpace(input)) return;

        (string name, bool isNPC) chosen = default;
        if (int.TryParse(input, out int idx) && idx >= 1 && idx <= targets.Count)
            chosen = targets[idx - 1];
        else
        {
            var match = targets.FirstOrDefault(t => t.name.Equals(input, StringComparison.OrdinalIgnoreCase));
            if (match.name != null)
                chosen = match;
        }

        if (chosen.name == null)
        {
            terminal.WriteLine(Loc.Get("dormitory.no_such_sleeper"), "red");
            await Task.Delay(1000);
            return;
        }

        if (chosen.isNPC)
        {
            await AttackSleepingNPC(chosen.name);
        }
        else
        {
            await AttackSleepingPlayer(backend, chosen.name);
        }
    }

    private async Task AttackSleepingNPC(string npcName)
    {
        var npc = NPCSpawnSystem.Instance.GetNPCByName(npcName);
        if (npc == null || !npc.IsAlive || npc.IsDead)
        {
            terminal.WriteLine(Loc.Get("dormitory.no_longer_here"), "gray");
            await Task.Delay(1000);
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine($"\n  {Loc.Get("dormitory.creep_toward_npc", npcName)}\n");
        await Task.Delay(1500);

        // Darkness penalty for attacking a sleeping NPC
        currentPlayer.Darkness += 25;

        // Combat — NPC is sleeping so they fight at a disadvantage (reduced stats)
        long origStr = npc.Strength;
        long origDef = npc.Defence;
        npc.Strength = (long)(npc.Strength * 0.7); // 30% weaker while sleeping
        npc.Defence = (long)(npc.Defence * 0.7);

        var combatEngine = new CombatEngine(terminal);
        var result = await combatEngine.PlayerVsPlayer(currentPlayer, npc);

        // Restore NPC stats (if they survived)
        npc.Strength = origStr;
        npc.Defence = origDef;

        if (result.Outcome == CombatOutcome.Victory)
        {
            // Steal some gold
            long stolenGold = (long)(npc.Gold * GameConfig.SleeperGoldTheftPercent);
            if (stolenGold > 0)
            {
                currentPlayer.Gold += stolenGold;
                npc.Gold -= stolenGold;
                terminal.WriteLine(Loc.Get("dormitory.steal_gold", stolenGold), "yellow");
            }

            terminal.SetColor("dark_red");
            terminal.WriteLine($"\n{Loc.Get("dormitory.leave_body", npcName)}");

            // Record murder memory on the NPC (they'll come for revenge when they respawn)
            npc.Memory?.RecordEvent(new MemoryEvent
            {
                Type = MemoryType.Murdered,
                Description = $"Murdered in my sleep by {currentPlayer.Name2}",
                InvolvedCharacter = currentPlayer.Name2,
                Importance = 1.0f,
                EmotionalImpact = -1.0f,
                Location = "Dormitory"
            });

            // Faction standing penalty
            if (npc.NPCFaction.HasValue)
            {
                var factionSystem = UsurperRemake.Systems.FactionSystem.Instance;
                factionSystem?.ModifyReputation(npc.NPCFaction.Value, -200);
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("dormitory.faction_plummeted", UsurperRemake.Systems.FactionSystem.Factions[npc.NPCFaction.Value].Name));
            }

            // Witness memories for other NPCs at this location
            foreach (var witness in LocationManager.Instance.GetNPCsInLocation(GameLocation.Dormitory)
                .Where(n => n.IsAlive && n.Name2 != npcName))
            {
                witness.Memory?.RecordEvent(new MemoryEvent
                {
                    Type = MemoryType.SawDeath,
                    Description = $"Witnessed {currentPlayer.Name2} murder {npcName} in their sleep",
                    InvolvedCharacter = currentPlayer.Name2,
                    Importance = 0.8f,
                    EmotionalImpact = -0.6f,
                    Location = "Dormitory"
                });
            }

            // Remove NPC from sleeping list
            WorldSimulator.WakeUpNPC(npcName);

            // Post news
            try { OnlineStateManager.Instance?.AddNews($"{currentPlayer.Name2} murdered {npcName} in their sleep at the Dormitory!", "combat"); } catch { }

            await Task.Delay(2000);
        }
        else
        {
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("dormitory.fought_off_npc", npcName));
            WorldSimulator.WakeUpNPC(npcName);
            await Task.Delay(2000);
        }
        await terminal.WaitForKeyPress();
    }

    private async Task AttackSleepingPlayer(SqlSaveBackend backend, string targetUsername)
    {
        var target = (await backend.GetSleepingPlayers())
            .FirstOrDefault(s => s.Username.Equals(targetUsername, StringComparison.OrdinalIgnoreCase));
        if (target == null) return;

        // Load the victim's save data
        var victimSave = await backend.ReadGameData(target.Username);
        if (victimSave?.Player == null)
        {
            terminal.WriteLine(Loc.Get("dormitory.could_not_load"), "red");
            await Task.Delay(1000);
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine($"\n  {Loc.Get("dormitory.creep_toward_player", target.Username)}\n");
        await Task.Delay(1500);

        // No guards in dormitory — fight the sleeper directly
        var victim = PlayerCharacterLoader.CreateFromSaveData(victimSave.Player, target.Username);
        long victimGold = victim.Gold;
        victim.Gold = 0; // prevent CombatEngine from applying its own gold steal

        // Backstab bonus: darkness for attacking a sleeper
        currentPlayer.Darkness += 25;

        var combatEngine = new CombatEngine(terminal);
        var result = await combatEngine.PlayerVsPlayer(currentPlayer, victim);

        if (result.Outcome == CombatOutcome.Victory)
        {
            // Steal 50% of their gold
            long stolenGold = (long)(victimGold * GameConfig.SleeperGoldTheftPercent);
            if (stolenGold > 0)
            {
                currentPlayer.Gold += stolenGold;
                await backend.DeductGoldFromPlayer(target.Username, stolenGold);
                terminal.WriteLine(Loc.Get("dormitory.steal_gold", stolenGold), "yellow");
            }

            // Steal 1 random item
            var (stolenItemName, stolenEquipment) = await StealRandomItem(backend, target.Username, victimSave);
            if (stolenItemName != null && stolenEquipment != null)
            {
                // Add the stolen equipment to the attacker's inventory
                EquipmentDatabase.RegisterDynamic(stolenEquipment);
                var legacyItem = currentPlayer.ConvertEquipmentToLegacyItem(stolenEquipment);
                currentPlayer.Inventory.Add(legacyItem);
                terminal.WriteLine(Loc.Get("dormitory.also_take_item", stolenItemName), "yellow");
            }

            // Apply XP loss to victim
            long xpLoss = (long)(victimSave.Player.Experience * GameConfig.SleeperXPLossPercent / 100.0);
            if (xpLoss > 0)
                await DeductXPFromPlayer(backend, target.Username, xpLoss);

            // Mark victim as dead
            await backend.MarkSleepingPlayerDead(target.Username);

            // Log the attack
            var logEntry = JsonSerializer.Serialize(new
            {
                attacker = currentPlayer.Name2,
                type = "player",
                result = "attacker_won",
                gold_stolen = stolenGold,
                item_stolen = stolenItemName ?? (object)null!,
                xp_lost = xpLoss
            });
            await backend.AppendSleepAttackLog(target.Username, logEntry);

            // Send message to victim
            await backend.SendMessage(currentPlayer.Name2, target.Username, "sleep_attack",
                $"{currentPlayer.Name2} murdered you in your sleep! They stole {stolenGold:N0} gold{(stolenItemName != null ? $" and your {stolenItemName}" : "")}.");

            terminal.SetColor("dark_red");
            terminal.WriteLine($"\n{Loc.Get("dormitory.leave_lifeless", target.Username)}");
            await Task.Delay(2000);
        }
        else
        {
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("dormitory.fought_off_player", target.Username));
            await Task.Delay(2000);
        }
        await terminal.WaitForKeyPress();
    }

    private async Task<(string? name, Equipment? equipment)> StealRandomItem(SqlSaveBackend backend, string username, SaveGameData saveData)
    {
        try
        {
            var playerData = saveData.Player;
            if (playerData == null) return (null, null);

            // Collect stealable dynamic equipment (these have names)
            var stealable = new List<(int index, string name)>();
            if (playerData.DynamicEquipment != null)
            {
                for (int i = 0; i < playerData.DynamicEquipment.Count; i++)
                {
                    var eq = playerData.DynamicEquipment[i];
                    if (eq != null && !string.IsNullOrEmpty(eq.Name))
                        stealable.Add((i, eq.Name));
                }
            }

            if (stealable.Count == 0) return (null, null);

            // Pick a random item
            var (index, name) = stealable[rng.Next(stealable.Count)];
            var stolenEquip = playerData.DynamicEquipment![index];

            // Build an Equipment object from the save data before removing it
            var equipment = new Equipment
            {
                Id = stolenEquip.Id,
                Name = stolenEquip.Name ?? name,
                Slot = (EquipmentSlot)(stolenEquip.Slot),
                Handedness = (WeaponHandedness)(stolenEquip.Handedness),
                WeaponType = (WeaponType)(stolenEquip.WeaponType),
                WeaponPower = stolenEquip.WeaponPower,
                ArmorClass = stolenEquip.ArmorClass,
                ShieldBonus = stolenEquip.ShieldBonus,
                BlockChance = stolenEquip.BlockChance,
                DefenceBonus = stolenEquip.DefenceBonus,
                StrengthBonus = stolenEquip.StrengthBonus,
                DexterityBonus = stolenEquip.DexterityBonus,
                AgilityBonus = stolenEquip.AgilityBonus,
                ConstitutionBonus = stolenEquip.ConstitutionBonus,
                IntelligenceBonus = stolenEquip.IntelligenceBonus,
                WisdomBonus = stolenEquip.WisdomBonus,
                CharismaBonus = stolenEquip.CharismaBonus,
                MaxHPBonus = stolenEquip.MaxHPBonus,
                MaxManaBonus = stolenEquip.MaxManaBonus,
                Value = stolenEquip.Value,
                IsIdentified = true,
                MinLevel = stolenEquip.MinLevel
            };

            // Also remove from equipped slots if this item is equipped
            if (playerData.EquippedItems != null)
            {
                var slotToRemove = playerData.EquippedItems
                    .Where(kvp => kvp.Value == stolenEquip.Id)
                    .Select(kvp => kvp.Key)
                    .FirstOrDefault(-1);
                if (slotToRemove >= 0)
                    playerData.EquippedItems.Remove(slotToRemove);
            }

            playerData.DynamicEquipment.RemoveAt(index);

            // Write modified save back
            await backend.WriteGameData(username, saveData);

            return (name, equipment);
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("DORMITORY", $"Failed to steal item from {username}: {ex.Message}");
            return (null, null);
        }
    }

    private async Task DeductXPFromPlayer(SqlSaveBackend backend, string username, long xpLoss)
    {
        try
        {
            var saveData = await backend.ReadGameData(username);
            if (saveData?.Player == null) return;
            saveData.Player.Experience = Math.Max(0, saveData.Player.Experience - xpLoss);
            await backend.WriteGameData(username, saveData);
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("DORMITORY", $"Failed to deduct XP from {username}: {ex.Message}");
        }
    }

    private async Task WakeGuests()
    {
        if (currentPlayer.DarkNr <= 0)
        {
            terminal.WriteLine(Loc.Get("dormitory.too_righteous"), "yellow");
            await Task.Delay(1500);
            return;
        }

        RefreshSleepers();
        if (sleepers.Count == 0)
        {
            terminal.WriteLine(Loc.Get("dormitory.no_one_disturb"), "gray");
            await Task.Delay(1500);
            return;
        }

        terminal.WriteLine(Loc.Get("dormitory.thunderous_shout"), "yellow");
        await Task.Delay(1000);
        currentPlayer.Darkness += 10;
        currentPlayer.DarkNr -= 1;

        var angry = sleepers.OrderBy(_ => rng.Next()).Take(rng.Next(1, Math.Min(3, sleepers.Count))).ToList();
        var combatEngine = new CombatEngine(terminal);

        foreach (var npc in angry)
        {
            if (!currentPlayer.IsAlive) break;
            terminal.WriteLine(Loc.Get("dormitory.wakes_furious", npc.Name2), "red");
            await Task.Delay(1000);

            var result = await combatEngine.PlayerVsPlayer(currentPlayer, npc);
            if (!currentPlayer.IsAlive)
            {
                terminal.WriteLine(Loc.Get("dormitory.knocked_out"), "red");
                break;
            }
            else
            {
                terminal.WriteLine(Loc.Get("dormitory.subdued", npc.Name2), "green");
                npc.HP = Math.Max(1, npc.HP - 10);
            }
        }
        await terminal.WaitForKeyPress();
    }
    #endregion
}
