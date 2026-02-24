using Godot;
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
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔═════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine($"║{"THE DORMITORY".PadLeft((77 + 13) / 2).PadRight(77)}║");
        terminal.WriteLine("╚═════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        // Atmosphere
        terminal.SetColor("white");
        terminal.Write("Rows of creaky wooden bunks line the walls. The warm, stale air is thick ");
        terminal.WriteLine("with");
        terminal.Write("the smell of sweat and cheap ale. ");
        terminal.SetColor("gray");
        terminal.WriteLine("A few snoring lumps stir under thin blankets.");
        terminal.WriteLine("");

        ShowNPCsInLocation();

        // Menu
        terminal.SetColor("cyan");
        terminal.WriteLine("What would you like to do?");
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
            terminal.Write("ist sleepers       ");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("E");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write("xamine          ");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("G");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine($"o to sleep ({GameConfig.DormitorySleepCost}g, logout)");

            // Row 2
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("K");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write("ill a sleeper      ");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("W");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write("ake guests      ");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("S");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine("tatus");

            // Row 3
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("R");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine("eturn to Anchor Road");
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
            terminal.Write("ist sleepers       ");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("E");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write("xamine          ");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("G");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine("o to sleep");

            // Row 2
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("W");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write("ake the guests     ");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("S");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write("tatus           ");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("R");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine("eturn");
        }
        terminal.WriteLine("");

        if (DoorMode.IsOnlineMode)
        {
            terminal.SetColor("red");
            terminal.WriteLine("  WARNING: Sleeping here leaves you vulnerable to attack!");
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
                await NavigateToLocation(GameLocation.AnchorRoad);
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
        terminal.WriteLine("Sleeping Guests");
        terminal.SetColor("cyan");

        // Show NPC sleepers
        if (sleepers.Count == 0 && !DoorMode.IsOnlineMode)
        {
            terminal.WriteLine("No one is asleep right now.");
        }
        else
        {
            int idx = 1;
            foreach (var npc in sleepers)
            {
                terminal.WriteLine($"{idx,3}. {npc.Name2} (Lvl {npc.Level})");
                idx++;
            }

            // Show sleeping NPCs from world sim
            if (DoorMode.IsOnlineMode)
            {
                var dormNPCs = WorldSimulator.GetSleepingNPCsAt("dormitory");
                if (dormNPCs.Count > 0)
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine("\n  Sleeping NPCs:");
                    foreach (var npcName in dormNPCs)
                    {
                        terminal.WriteLine($"  {idx,3}. {npcName} [SLEEPING NPC]", "yellow");
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
                        terminal.WriteLine("\n  Vulnerable Players:");
                        foreach (var s in dormSleepers)
                        {
                            terminal.WriteLine($"  {idx,3}. {s.Username} [SLEEPING]", "red");
                            idx++;
                        }
                    }
                }
            }
        }
        terminal.WriteLine("\nPress Enter...");
        await terminal.WaitForKeyPress();
    }

    private async Task ExamineSleeper()
    {
        RefreshSleepers();
        if (sleepers.Count == 0)
        {
            terminal.WriteLine("Nobody to examine.", "gray");
            await Task.Delay(1500);
            return;
        }
        var input = await terminal.GetInput("Enter sleeper number or name: ");
        NPC? npc = null;
        if (int.TryParse(input, out int num) && num >= 1 && num <= sleepers.Count)
            npc = sleepers[num - 1];
        else
            npc = sleepers.FirstOrDefault(n => n.Name2.Equals(input, StringComparison.OrdinalIgnoreCase));

        if (npc == null)
        {
            terminal.WriteLine("No such sleeper.", "red");
            await Task.Delay(1500);
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(npc.Name2);
        terminal.SetColor("yellow");
        terminal.WriteLine(new string('═', npc.Name2.Length));
        terminal.SetColor("white");
        terminal.WriteLine(npc.GetDisplayInfo());
        terminal.WriteLine("\nPress Enter...");
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
        var confirm = await terminal.GetInput("Stay here for the night? (y/N): ");
        if (!confirm.Equals("Y", StringComparison.OrdinalIgnoreCase))
            return;

        terminal.ClearScreen();
        terminal.SetColor("white");
        terminal.WriteLine("You claim a free bunk and drift into uneasy sleep...");
        await Task.Delay(1500);

        currentPlayer.HP = currentPlayer.MaxHP;
        currentPlayer.Mana = currentPlayer.MaxMana;
        currentPlayer.Stamina = Math.Max(currentPlayer.Stamina, currentPlayer.Constitution * 2);

        if (!UsurperRemake.BBS.DoorMode.IsOnlineMode)
        {
            await DailySystemManager.Instance.ForceDailyReset();
        }

        terminal.WriteLine("You awaken refreshed, ready for a new day of adventure.", "green");
        await Task.Delay(1500);

        await NavigateToLocation(GameLocation.AnchorRoad);
    }

    private async Task GoToSleepOnline()
    {
        int cost = GameConfig.DormitorySleepCost;
        terminal.SetColor("yellow");
        terminal.WriteLine($"\n  Cost: {cost}g for a bunk bed.");
        terminal.SetColor("red");
        terminal.WriteLine("  WARNING: You are vulnerable to attack while sleeping here!");
        terminal.SetColor("white");

        var confirm = await terminal.GetInput($"Sleep here for the night and log out? (y/N): ");
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
            terminal.WriteLine($"  ({shortfall:N0}g withdrawn from your bank account)", "gray");
        }
        else
        {
            terminal.WriteLine("You can't afford even this flea-ridden bunk.", "red");
            terminal.WriteLine($"  (Checked gold on hand: {currentPlayer.Gold:N0} and bank: {currentPlayer.BankGold:N0})", "gray");
            await Task.Delay(1500);
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("white");
        terminal.WriteLine("You claim a bunk and pull the thin blanket over yourself...");
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
        terminal.WriteLine("You drift into uneasy sleep... (logging out)");
        terminal.SetColor("red");
        terminal.WriteLine("Anyone could slit your throat in the night.");
        await Task.Delay(2000);

        throw new LocationExitException(GameLocation.NoWhere);
    }

    private async Task AttackSleeper()
    {
        var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
        if (backend == null)
        {
            terminal.WriteLine("Not available.", "gray");
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
            terminal.WriteLine("No vulnerable sleepers in the dormitory.", "gray");
            await Task.Delay(1500);
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine("Dormitory — Vulnerable Sleepers");
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
            string lvlStr = npc != null ? $" (Lvl {npc.Level})" : "";
            terminal.WriteLine($"  {targets.Count + 1}. {npcName}{lvlStr} [SLEEPING NPC]", "yellow");
            targets.Add((npcName, true));
        }
        foreach (var s in dormPlayerSleepers)
        {
            // Level filter: can only attack players within ±5 levels
            var targetSave = await backend.ReadGameData(s.Username);
            int targetLevel = targetSave?.Player?.Level ?? 1;
            if (Math.Abs(targetLevel - attackerLevel) > 5)
                continue;

            terminal.WriteLine($"  {targets.Count + 1}. {s.Username} (Lvl {targetLevel}) [SLEEPING PLAYER]", "red");
            targets.Add((s.Username, false));
        }

        terminal.SetColor("white");
        var input = await terminal.GetInput("\nWho do you attack? (number or name, blank to cancel): ");
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
            terminal.WriteLine("No such sleeper.", "red");
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
            terminal.WriteLine("They are no longer here.", "gray");
            await Task.Delay(1000);
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine($"\n  You creep toward {npcName}'s bunk, weapon drawn...\n");
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
                terminal.WriteLine($"You rifle through their belongings and steal {stolenGold:N0} gold!", "yellow");
            }

            terminal.SetColor("dark_red");
            terminal.WriteLine($"\nYou leave {npcName}'s body in the bunk.");

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
                terminal.WriteLine($"Your standing with {UsurperRemake.Systems.FactionSystem.Factions[npc.NPCFaction.Value].Name} has plummeted! (-200)");
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
            terminal.WriteLine($"{npcName} fought you off even half-asleep!");
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
            terminal.WriteLine("Could not load their data.", "red");
            await Task.Delay(1000);
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine($"\n  You creep toward {target.Username}'s bunk, weapon drawn...\n");
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
                terminal.WriteLine($"You rifle through their belongings and steal {stolenGold:N0} gold!", "yellow");
            }

            // Steal 1 random item
            string stolenItemName = await StealRandomItem(backend, target.Username, victimSave);
            if (stolenItemName != null)
                terminal.WriteLine($"You also take their {stolenItemName}!", "yellow");

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
            terminal.WriteLine($"\nYou leave {target.Username}'s lifeless body in the bunk.");
            await Task.Delay(2000);
        }
        else
        {
            terminal.SetColor("cyan");
            terminal.WriteLine($"{target.Username} fought you off even in their sleep!");
            await Task.Delay(2000);
        }
        await terminal.WaitForKeyPress();
    }

    private async Task<string?> StealRandomItem(SqlSaveBackend backend, string username, SaveGameData saveData)
    {
        try
        {
            var playerData = saveData.Player;
            if (playerData == null) return null;

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

            if (stealable.Count == 0) return null;

            // Pick a random item
            var (index, name) = stealable[rng.Next(stealable.Count)];
            var stolenEquip = playerData.DynamicEquipment![index];

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

            return name;
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("DORMITORY", $"Failed to steal item from {username}: {ex.Message}");
            return null;
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
            terminal.WriteLine("You feel too righteous to cause such mischief today.", "yellow");
            await Task.Delay(1500);
            return;
        }

        RefreshSleepers();
        if (sleepers.Count == 0)
        {
            terminal.WriteLine("There is no one to disturb.", "gray");
            await Task.Delay(1500);
            return;
        }

        terminal.WriteLine("You let out a thunderous shout!", "yellow");
        await Task.Delay(1000);
        currentPlayer.Darkness += 10;
        currentPlayer.DarkNr -= 1;

        var angry = sleepers.OrderBy(_ => rng.Next()).Take(rng.Next(1, Math.Min(3, sleepers.Count))).ToList();
        var combatEngine = new CombatEngine(terminal);

        foreach (var npc in angry)
        {
            if (!currentPlayer.IsAlive) break;
            terminal.WriteLine($"{npc.Name2} wakes up furious and attacks you!", "red");
            await Task.Delay(1000);

            var result = await combatEngine.PlayerVsPlayer(currentPlayer, npc);
            if (!currentPlayer.IsAlive)
            {
                terminal.WriteLine("You were knocked out!", "red");
                break;
            }
            else
            {
                terminal.WriteLine($"You subdued {npc.Name2}.", "green");
                npc.HP = Math.Max(1, npc.HP - 10);
            }
        }
        await terminal.WaitForKeyPress();
    }
    #endregion
}
