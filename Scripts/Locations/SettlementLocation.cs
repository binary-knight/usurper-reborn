using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UsurperRemake.Systems;
using UsurperRemake.BBS;

/// <summary>
/// The Outskirts — an NPC-built autonomous settlement beyond the city gates.
/// NPCs migrate here based on personality traits, pool resources, and build structures.
/// Players can visit, contribute, and use services from completed buildings.
/// </summary>
public class SettlementLocation : BaseLocation
{

    /// <summary>
    /// Immediately persist settlement state to world_state in online mode.
    /// Prevents data loss if server restarts before the next world sim save cycle.
    /// </summary>
    private void PersistSettlementIfOnline()
    {
        if (!DoorMode.IsOnlineMode || OnlineStateManager.Instance == null) return;
        _ = OnlineStateManager.Instance.SaveSettlementToWorldState();
    }

    protected override void DisplayLocation()
    {
        if (IsBBSSession) { DisplayLocationBBS(); return; }

        terminal.ClearScreen();
        var state = SettlementSystem.Instance.State;
        int settlers = state.SettlerNames.Count;

        // Header
        WriteBoxHeader(Loc.Get("settlement.header"), "bright_yellow", 70);
        terminal.WriteLine("");

        // Dynamic description based on settlement size
        terminal.SetColor("white");
        if (settlers < 8)
        {
            terminal.WriteLine(Loc.Get("settlement.desc_small_1"));
            terminal.WriteLine(Loc.Get("settlement.desc_small_2"));
        }
        else if (settlers < 12)
        {
            terminal.WriteLine(Loc.Get("settlement.desc_medium_1"));
            terminal.WriteLine(Loc.Get("settlement.desc_medium_2"));
        }
        else
        {
            terminal.WriteLine(Loc.Get("settlement.desc_large_1"));
            terminal.WriteLine(Loc.Get("settlement.desc_large_2"));
        }
        terminal.WriteLine("");

        // Settlement stats
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("settlement.stats", settlers, GameConfig.SettlementMaxNPCs, $"{state.CommunalTreasury:N0}"));
        terminal.WriteLine("");

        // Buildings status (compact)
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("settlement.buildings_label"));
        foreach (var kvp in state.Buildings.OrderByDescending(b => (int)b.Value.Tier))
        {
            var b = kvp.Value;
            string name = SettlementSystem.GetBuildingDisplayName(kvp.Key);
            bool isActive = state.ActiveBuilding == kvp.Key;
            // Show "In Progress" instead of "Not Started" when actively being built
            string tier = (isActive && b.Tier == BuildingTier.None && b.ResourcePool > 0)
                ? Loc.Get("settlement.in_progress")
                : SettlementSystem.GetTierDisplayName(b.Tier);
            string color = b.Tier switch
            {
                BuildingTier.Upgraded => "bright_green",
                BuildingTier.Built => "green",
                BuildingTier.Foundation => "yellow",
                _ => isActive ? "cyan" : "darkgray"
            };

            string active = isActive ? Loc.Get("settlement.building_active") : "";
            terminal.SetColor(color);
            terminal.Write($"    {name,-16} ");
            terminal.SetColor("white");
            terminal.Write($"{tier,-12}");

            // Show progress bar for active building
            if (state.ActiveBuilding == kvp.Key)
            {
                int nextTier = (int)b.Tier + 1;
                if (nextTier <= (int)BuildingTier.Upgraded)
                {
                    long cost = GameConfig.SettlementBuildingCosts[nextTier];
                    float pct = cost > 0 ? Math.Min(1f, (float)b.ResourcePool / cost) : 0f;
                    int filled = (int)(pct * 15);
                    terminal.SetColor("bright_cyan");
                    if (IsScreenReader)
                        terminal.Write(Loc.Get("settlement.pct_complete", $"{pct * 100:F0}"));
                    else
                        terminal.Write($" [{"".PadRight(filled, '#').PadRight(15, '.')}] {pct * 100:F0}%");
                }
            }
            terminal.SetColor("yellow");
            terminal.Write(active);
            terminal.WriteLine("");
        }
        terminal.WriteLine("");

        // Settlers present
        terminal.SetColor("gray");
        if (settlers > 0)
        {
            terminal.Write(Loc.Get("settlement.settlers_label"));
            terminal.SetColor("white");
            var names = state.SettlerNames.Take(8).ToList();
            terminal.Write(string.Join(", ", names));
            if (settlers > 8) terminal.Write(Loc.Get("settlement.settlers_more", settlers - 8));
            terminal.WriteLine("");
            terminal.WriteLine("");
        }

        // Show NPC-proposed buildings
        var proposedBuilt = state.ProposedBuildings.Where(b => b.Value.Tier > BuildingTier.None).ToList();
        if (proposedBuilt.Count > 0)
        {
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("settlement.settler_built_label"));
            foreach (var kvp in proposedBuilt.OrderByDescending(b => (int)b.Value.Tier))
            {
                var template = SettlementSystem.Instance.GetProposalTemplate(kvp.Key);
                string name = template?.Name ?? kvp.Key;
                string tier = SettlementSystem.GetTierDisplayName(kvp.Value.Tier);
                string color = kvp.Value.Tier switch
                {
                    BuildingTier.Upgraded => "bright_green",
                    BuildingTier.Built => "green",
                    _ => "yellow"
                };
                string active = state.ActiveProposedBuildingId == kvp.Key ? Loc.Get("settlement.building_active") : "";
                terminal.SetColor(color);
                terminal.Write($"    {name,-16} ");
                terminal.SetColor("white");
                terminal.Write($"{tier,-12}");
                if (state.ActiveProposedBuildingId == kvp.Key)
                {
                    int nextTier = (int)kvp.Value.Tier + 1;
                    if (nextTier <= (int)BuildingTier.Upgraded)
                    {
                        long cost = GameConfig.SettlementBuildingCosts[nextTier];
                        float pct = cost > 0 ? Math.Min(1f, (float)kvp.Value.ResourcePool / cost) : 0f;
                        int filled = (int)(pct * 15);
                        terminal.SetColor("bright_cyan");
                        if (IsScreenReader)
                            terminal.Write(Loc.Get("settlement.pct_complete", $"{pct * 100:F0}"));
                        else
                            terminal.Write($" [{"".PadRight(filled, '#').PadRight(15, '.')}] {pct * 100:F0}%");
                    }
                }
                terminal.SetColor("yellow");
                terminal.Write(active);
                terminal.WriteLine("");
            }
            terminal.WriteLine("");
        }

        // Active proposal notification
        if (state.CurrentProposal != null)
        {
            var propTemplate = SettlementSystem.Instance.GetProposalTemplate(state.CurrentProposal.BuildingId);
            if (propTemplate != null)
            {
                terminal.SetColor("bright_cyan");
                terminal.WriteLine(Loc.Get("settlement.proposal_active", state.CurrentProposal.ProposerName, propTemplate.Name));
                terminal.SetColor("gray");
                int dispFor = state.CurrentProposal.SupportVotes + Math.Max(0, state.CurrentProposal.PlayerVoteWeight);
                int dispAgainst = state.CurrentProposal.OpposeVotes + Math.Max(0, -state.CurrentProposal.PlayerVoteWeight);
                terminal.WriteLine(Loc.Get("settlement.proposal_votes", dispFor, dispAgainst, state.CurrentProposal.TicksRemaining));
                terminal.WriteLine("");
            }
        }

        // Menu
        terminal.SetColor("bright_yellow");
        if (IsScreenReader)
        {
            terminal.WriteLine(Loc.Get("settlement.menu_view_sr"));
            terminal.WriteLine(Loc.Get("settlement.menu_contribute_sr"));
            terminal.WriteLine(Loc.Get("settlement.menu_proposals_sr"));
        }
        else
        {
            terminal.WriteLine(Loc.Get("settlement.menu_view"));
            terminal.WriteLine(Loc.Get("settlement.menu_contribute"));
            terminal.WriteLine(Loc.Get("settlement.menu_proposals"));
        }

        var services = SettlementSystem.Instance.GetAvailableServices();
        var proposedServices = SettlementSystem.Instance.GetProposedServices();
        if (services.Count > 0 || proposedServices.Count > 0)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine(IsScreenReader ? Loc.Get("settlement.menu_services_sr") : Loc.Get("settlement.menu_services"));
        }

        terminal.SetColor("gray");
        terminal.WriteLine(IsScreenReader ? Loc.Get("settlement.menu_return_sr") : Loc.Get("settlement.menu_return"));
        terminal.WriteLine("");

        ShowStatusLine();
    }

    private void DisplayLocationBBS()
    {
        terminal.ClearScreen();
        var state = SettlementSystem.Instance.State;
        int settlers = state.SettlerNames.Count;

        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("settlement.bbs_header"));
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("settlement.bbs_stats", settlers, GameConfig.SettlementMaxNPCs, $"{state.CommunalTreasury:N0}"));

        // Compact building list
        foreach (var kvp in state.Buildings.Where(b => b.Value.Tier > BuildingTier.None || state.ActiveBuilding == b.Key))
        {
            string name = SettlementSystem.GetBuildingDisplayName(kvp.Key);
            string tier = SettlementSystem.GetTierDisplayName(kvp.Value.Tier);
            string active = state.ActiveBuilding == kvp.Key ? "*" : "";
            terminal.WriteLine($"  {name}: {tier}{active}");
        }

        if (state.ActiveBuilding != null)
        {
            var ab = state.Buildings[state.ActiveBuilding.Value];
            int nextTier = (int)ab.Tier + 1;
            if (nextTier <= (int)BuildingTier.Upgraded)
            {
                long cost = GameConfig.SettlementBuildingCosts[nextTier];
                terminal.SetColor("cyan");
                terminal.WriteLine(Loc.Get("settlement.bbs_building", SettlementSystem.GetBuildingDisplayName(state.ActiveBuilding.Value), $"{ab.ResourcePool:N0}", $"{cost:N0}"));
            }
        }

        // NPC-proposed buildings (compact)
        foreach (var kvp in state.ProposedBuildings.Where(b => b.Value.Tier > BuildingTier.None))
        {
            var tmpl = SettlementSystem.Instance.GetProposalTemplate(kvp.Key);
            string nm = tmpl?.Name ?? kvp.Key;
            string tr = SettlementSystem.GetTierDisplayName(kvp.Value.Tier);
            string act = state.ActiveProposedBuildingId == kvp.Key ? "*" : "";
            terminal.WriteLine($"  {nm}: {tr}{act}");
        }

        if (state.CurrentProposal != null)
        {
            var pt = SettlementSystem.Instance.GetProposalTemplate(state.CurrentProposal.BuildingId);
            if (pt != null)
            {
                terminal.SetColor("cyan");
                int bFor = state.CurrentProposal.SupportVotes + Math.Max(0, state.CurrentProposal.PlayerVoteWeight);
                int bAgainst = state.CurrentProposal.OpposeVotes + Math.Max(0, -state.CurrentProposal.PlayerVoteWeight);
                terminal.WriteLine(Loc.Get("settlement.bbs_proposal", pt.Name, bFor, bAgainst));
            }
        }

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("settlement.bbs_menu"));
        terminal.WriteLine("");
        ShowStatusLine();
    }

    protected override async Task<bool> ProcessChoice(string choice)
    {
        string upper = choice.ToUpper().Trim();

        switch (upper)
        {
            case "V":
                await ShowBuildingDetails();
                return false;

            case "C":
                await ContributeGold();
                return false;

            case "S":
                await ShowServices();
                return false;

            case "P":
                await ShowProposals();
                return false;

            case "R":
            case "Q":
                terminal.WriteLine(Loc.Get("settlement.return_to_main"), "gray");
                await Task.Delay(1000);
                throw new LocationExitException(GameLocation.MainStreet);

            default:
                var (handled, shouldExit) = await TryProcessGlobalCommand(choice);
                if (handled) return shouldExit;
                return false;
        }
    }

    protected override string GetMudPromptName() => "Settlement";

    // ═══════════════════════════════════════════════════════════════
    // BUILDING DETAILS
    // ═══════════════════════════════════════════════════════════════

    private async Task ShowBuildingDetails()
    {
        var state = SettlementSystem.Instance.State;

        terminal.WriteLine("");
        WriteBoxHeader(Loc.Get("settlement.building_status"), "bright_yellow", 38);
        terminal.WriteLine("");

        foreach (SettlementBuilding building in Enum.GetValues(typeof(SettlementBuilding)))
        {
            var bs = state.Buildings[building];
            string name = SettlementSystem.GetBuildingDisplayName(building);
            string desc = SettlementSystem.GetBuildingDescription(building);
            string tier = SettlementSystem.GetTierDisplayName(bs.Tier);
            bool isActive = state.ActiveBuilding == building;

            terminal.SetColor(bs.Tier > BuildingTier.None ? "bright_green" : "gray");
            terminal.WriteLine($"  {name} — {tier}");
            terminal.SetColor("white");
            terminal.WriteLine($"    {desc}");

            if (isActive)
            {
                int nextTier = (int)bs.Tier + 1;
                if (nextTier <= (int)BuildingTier.Upgraded)
                {
                    long cost = GameConfig.SettlementBuildingCosts[nextTier];
                    float pct = cost > 0 ? Math.Min(1f, (float)bs.ResourcePool / cost) : 0f;
                    int filled = (int)(pct * 20);
                    terminal.SetColor("bright_cyan");
                    terminal.WriteLine(Loc.Get("settlement.building_progress", "".PadRight(filled, '#').PadRight(20, '.'), $"{bs.ResourcePool:N0}", $"{cost:N0}", $"{pct * 100:F0}"));
                }
            }

            terminal.WriteLine("");
        }

        await terminal.PressAnyKey();
    }

    // ═══════════════════════════════════════════════════════════════
    // CONTRIBUTE GOLD
    // ═══════════════════════════════════════════════════════════════

    private async Task ContributeGold()
    {
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("settlement.your_gold", $"{currentPlayer.Gold:N0}"));

        if (currentPlayer.Gold <= 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("ui.no_gold_to_contribute"));
            await terminal.PressAnyKey();
            return;
        }

        string input = await terminal.GetInput(Loc.Get("settlement.contribute_prompt"));
        if (!long.TryParse(input, out long amount) || amount <= 0)
        {
            terminal.WriteLine(Loc.Get("ui.cancelled"), "gray");
            return;
        }

        if (amount > currentPlayer.Gold)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("settlement.not_that_much_gold"));
            await terminal.PressAnyKey();
            return;
        }

        currentPlayer.Gold -= amount;
        SettlementSystem.Instance.ContributeGold(currentPlayer.Name, amount);

        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("settlement.contribute_success", $"{amount:N0}"));
        terminal.SetColor("white");

        long totalContrib = SettlementSystem.Instance.State.PlayerContributions
            .GetValueOrDefault(currentPlayer.Name, 0);
        terminal.WriteLine(Loc.Get("settlement.total_contributions", $"{totalContrib:N0}"));

        currentPlayer.Statistics?.RecordGoldSpent(amount);
        PersistSettlementIfOnline();
        await terminal.PressAnyKey();
    }

    // ═══════════════════════════════════════════════════════════════
    // SERVICES
    // ═══════════════════════════════════════════════════════════════

    private async Task ShowServices()
    {
        var services = SettlementSystem.Instance.GetAvailableServices();
        var proposedServices = SettlementSystem.Instance.GetProposedServices();

        if (services.Count == 0 && proposedServices.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("settlement.no_services"));
            await terminal.PressAnyKey();
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("settlement.available_services"));
        terminal.SetColor("white");
        foreach (var (key, label, _) in services)
        {
            terminal.WriteLine(IsScreenReader ? $"  {key}. {label}" : $"  [{key}] {label}");
        }
        if (proposedServices.Count > 0)
        {
            terminal.SetColor("bright_cyan");
            foreach (var (key, label, _) in proposedServices)
            {
                terminal.WriteLine(IsScreenReader ? $"  {key}. {label}" : $"  [{key}] {label}");
            }
        }
        terminal.SetColor("gray");
        terminal.WriteLine(IsScreenReader ? Loc.Get("settlement.cancel_sr") : Loc.Get("settlement.cancel"));
        terminal.WriteLine("");

        string input = await GetChoice();
        input = input.Trim();
        if (input == "0") { terminal.WriteLine(Loc.Get("ui.cancelled"), "gray"); return; }

        // Check core services
        var service = services.FirstOrDefault(s => s.key == input);
        if (service.key != null)
        {
            await UseService(service.building);
            return;
        }

        // Check proposed building services
        var proposed = proposedServices.FirstOrDefault(s => s.key == input);
        if (proposed.key != null)
        {
            await UseProposedService(proposed.buildingId);
            return;
        }

        terminal.WriteLine(Loc.Get("ui.cancelled"), "gray");
    }

    private async Task UseService(SettlementBuilding building)
    {
        switch (building)
        {
            case SettlementBuilding.Tavern:
                await UseTavernService();
                break;
            case SettlementBuilding.Shrine:
                await UseShrineService();
                break;
            case SettlementBuilding.Palisade:
                await UsePalisadeService();
                break;
            case SettlementBuilding.Workshop:
                await UseWorkshopService();
                break;
            case SettlementBuilding.Watchtower:
                await UseWatchtowerService();
                break;
            case SettlementBuilding.CouncilHall:
                await UseCouncilHallService();
                break;
            case SettlementBuilding.MarketStall:
                await UseMarketService();
                break;
        }
    }

    private async Task UseTavernService()
    {
        if (currentPlayer.HasSettlementBuff)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("ui.settlement_buff_active"));
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("bright_green");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("settlement.tavern_desc_1"));
        terminal.WriteLine(Loc.Get("settlement.tavern_desc_2"));
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("settlement.gained_xp_buff", $"{GameConfig.SettlementXPBonus * 100:F0}", GameConfig.SettlementBuffDuration));

        currentPlayer.SettlementBuffType = (int)SettlementBuffType.XPBonus;
        currentPlayer.SettlementBuffCombats = GameConfig.SettlementBuffDuration;
        currentPlayer.SettlementBuffValue = GameConfig.SettlementXPBonus;

        await terminal.PressAnyKey();
    }

    private async Task UseShrineService()
    {
        if (currentPlayer.SettlementShrineUsedToday)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("settlement.shrine_spent"));
            await terminal.PressAnyKey();
            return;
        }

        if (currentPlayer.HP >= currentPlayer.MaxHP)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("settlement.already_full_hp"));
            await terminal.PressAnyKey();
            return;
        }

        int healAmount = (int)(currentPlayer.MaxHP * GameConfig.SettlementHealPercent);
        currentPlayer.HP = Math.Min(currentPlayer.MaxHP, currentPlayer.HP + healAmount);
        currentPlayer.Statistics?.RecordHealthRestored(healAmount);
        currentPlayer.SettlementShrineUsedToday = true;

        terminal.SetColor("bright_green");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("settlement.shrine_desc_1"));
        terminal.WriteLine(Loc.Get("settlement.shrine_desc_2"));
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("settlement.healed_hp", healAmount, currentPlayer.HP, currentPlayer.MaxHP));

        await terminal.PressAnyKey();
    }

    private async Task UsePalisadeService()
    {
        if (currentPlayer.HasSettlementBuff)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("ui.settlement_buff_active"));
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("bright_green");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("settlement.palisade_desc_1"));
        terminal.WriteLine(Loc.Get("settlement.palisade_desc_2"));
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("settlement.gained_defense_buff", $"{GameConfig.SettlementDefenseBonus * 100:F0}", GameConfig.SettlementBuffDuration));

        currentPlayer.SettlementBuffType = (int)SettlementBuffType.DefenseBonus;
        currentPlayer.SettlementBuffCombats = GameConfig.SettlementBuffDuration;
        currentPlayer.SettlementBuffValue = GameConfig.SettlementDefenseBonus;

        await terminal.PressAnyKey();
    }

    private async Task UseWorkshopService()
    {
        if (currentPlayer.SettlementWorkshopUsedToday)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("  The smiths have already sharpened your weapon today. Return tomorrow.");
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("bright_green");
        terminal.WriteLine("");
        terminal.WriteLine("  The settlement smiths heat your blade in the forge and hone it to a razor edge.");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("  Your weapon has been sharpened! +20% damage for 10 combats.");

        currentPlayer.WorkshopBuffCombats = 10;
        currentPlayer.SettlementWorkshopUsedToday = true;

        await terminal.PressAnyKey();
    }

    private async Task UseWatchtowerService()
    {
        terminal.SetColor("cyan");
        terminal.WriteLine("");
        string input = await terminal.GetInput("Which dungeon floor to reveal? (1-100): ");
        if (!int.TryParse(input, out int floor) || floor < 1 || floor > 100)
        {
            terminal.WriteLine(Loc.Get("ui.cancelled"), "gray");
            return;
        }

        // Generate the floor deterministically (same seed = same layout)
        var dungeonFloor = DungeonGenerator.GenerateFloor(floor);

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("");
        terminal.WriteLine($"  ╔══ WATCHTOWER SCOUT REPORT: Floor {floor} ══╗");
        terminal.SetColor("white");

        // Special floor warnings
        int[] oldGodFloors = { 25, 40, 55, 70, 85, 95, 100 };
        int[] sealFloors   = { 15, 30, 45, 60, 80, 99 };
        if (oldGodFloors.Contains(floor))
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine($"  ⚠ WARNING: An Old God dwells on this floor. Do not enter unprepared.");
            terminal.SetColor("white");
        }
        else if (sealFloors.Contains(floor))
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"  ★ SEAL FLOOR: An ancient seal awaits collection.");
            terminal.SetColor("white");
        }

        terminal.SetColor("gray");
        terminal.WriteLine($"  Theme: {dungeonFloor.Theme}  |  Danger: {dungeonFloor.DangerLevel}/10  |  Rooms: {dungeonFloor.Rooms.Count}");
        terminal.WriteLine("");

        // Room-by-room breakdown
        int roomNum = 0;
        foreach (var room in dungeonFloor.Rooms)
        {
            roomNum++;
            string roomIcon = room.Type switch
            {
                RoomType.BossAntechamber => "[BOSS]",
                RoomType.SecretVault     => "[SECRET]",
                RoomType.Shrine          => "[SHRINE]",
                RoomType.MeditationChamber => "[MEDITATE]",
                RoomType.LoreLibrary     => "[LORE]",
                RoomType.ArenaRoom       => "[ARENA]",
                RoomType.MerchantDen     => "[MERCHANT]",
                RoomType.Settlement      => "[OUTPOST]",
                _                        => ""
            };

            // Build tags
            var tags = new System.Text.StringBuilder();
            if (room.HasMonsters)   tags.Append(" Monsters");
            if (room.HasTrap)       tags.Append(" Trap");
            if (room.HasTreasure)   tags.Append(" Treasure");
            if (room.HasEvent)      tags.Append(" Event");
            if (room.IsBossRoom)    tags.Append(" [BOSS ROOM]");
            if (room.IsSecretRoom)  tags.Append(" [SECRET]");
            if (room.HasStairsDown) tags.Append(" ↓Stairs");

            string tagStr = tags.Length > 0 ? " —" + tags : "";

            // Color by danger
            if (room.IsBossRoom)
                terminal.SetColor("bright_red");
            else if (room.HasMonsters && room.DangerRating >= 7)
                terminal.SetColor("red");
            else if (room.IsSecretRoom || room.HasTreasure)
                terminal.SetColor("bright_yellow");
            else if (room.Type == RoomType.Shrine || room.Type == RoomType.MeditationChamber)
                terminal.SetColor("bright_cyan");
            else if (room.Type == RoomType.MerchantDen)
                terminal.SetColor("bright_green");
            else
                terminal.SetColor("white");

            string label = roomIcon.Length > 0 ? $" {roomIcon}" : "";
            terminal.WriteLine($"  {roomNum,2}. {room.Name}{label}{tagStr}");

            // Sample monster if present (Monsters list is empty at gen-time; generate a sample)
            if (room.HasMonsters)
            {
                terminal.SetColor("gray");
                if (room.Monsters.Count > 0)
                {
                    var m = room.Monsters[0];
                    string plural = room.Monsters.Count > 1 ? $" (+{room.Monsters.Count - 1} more)" : "";
                    terminal.WriteLine($"      → {m.Name} (Lv {m.Level}){plural}");
                }
                else
                {
                    var sample = MonsterGenerator.GenerateMonster(floor);
                    if (sample != null)
                        terminal.WriteLine($"      → {sample.Name} (Lv {sample.Level})");
                }
            }
        }

        terminal.SetColor("gray");
        terminal.WriteLine("");
        terminal.WriteLine("  (Scout report reflects floor layout at time of observation)");

        await terminal.PressAnyKey();
    }

    private async Task UseCouncilHallService()
    {
        var state = SettlementSystem.Instance.State;

        if (currentPlayer.SettlementGoldClaimedToday)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("settlement.council_claimed_today"));
            await terminal.PressAnyKey();
            return;
        }

        if (state.CommunalTreasury <= 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("settlement.treasury_empty"));
            await terminal.PressAnyKey();
            return;
        }

        // Player gets a share based on their contribution ratio
        long totalContrib = state.PlayerContributions.Values.Sum();
        long playerContrib = state.PlayerContributions.GetValueOrDefault(currentPlayer.Name, 0);

        long share;
        if (totalContrib <= 0 || playerContrib <= 0)
        {
            // Minimum share for visitors who haven't contributed
            share = Math.Min(state.CommunalTreasury, 50);
        }
        else
        {
            float ratio = Math.Min(1f, (float)playerContrib / totalContrib);
            share = (long)(state.CommunalTreasury * ratio * 0.1); // 10% of their proportional share
            share = Math.Max(50, share);
            share = Math.Min(share, state.CommunalTreasury);
        }

        state.CommunalTreasury -= share;
        currentPlayer.Gold += share;
        currentPlayer.SettlementGoldClaimedToday = true;

        terminal.SetColor("bright_green");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("settlement.council_elder"));
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("settlement.received_gold", $"{share:N0}"));
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("settlement.remaining_treasury", $"{state.CommunalTreasury:N0}"));

        PersistSettlementIfOnline();
        await terminal.PressAnyKey();
    }

    private async Task UseMarketService()
    {
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("settlement.market_desc"));
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("settlement.market_coming_soon"));
        await terminal.PressAnyKey();
    }

    // ═══════════════════════════════════════════════════════════════
    // PROPOSALS
    // ═══════════════════════════════════════════════════════════════

    private async Task ShowProposals()
    {
        var state = SettlementSystem.Instance.State;
        var proposal = state.CurrentProposal;

        terminal.WriteLine("");
        WriteBoxHeader(Loc.Get("settlement.proposals"), "bright_cyan", 38);
        terminal.WriteLine("");

        if (proposal != null)
        {
            var template = SettlementSystem.Instance.GetProposalTemplate(proposal.BuildingId);
            if (template != null)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("settlement.active_proposal", template.Name));
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("settlement.proposed_by", proposal.ProposerName));
                terminal.SetColor("gray");
                terminal.WriteLine($"  \"{template.Description}\"");
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("settlement.proposal_effect", template.EffectDescription));
                terminal.WriteLine("");
                terminal.SetColor("cyan");

                int totalFor = proposal.SupportVotes + Math.Max(0, proposal.PlayerVoteWeight);
                int totalAgainst = proposal.OpposeVotes + Math.Max(0, -proposal.PlayerVoteWeight);
                terminal.WriteLine(Loc.Get("settlement.proposal_support_oppose", totalFor, totalAgainst, proposal.TicksRemaining));
                terminal.WriteLine("");

                terminal.SetColor("bright_green");
                terminal.WriteLine(IsScreenReader
                    ? Loc.Get("settlement.endorse_sr", $"{GameConfig.SettlementEndorsementCost:N0}")
                    : Loc.Get("settlement.endorse", $"{GameConfig.SettlementEndorsementCost:N0}"));
                terminal.SetColor("red");
                terminal.WriteLine(IsScreenReader
                    ? Loc.Get("settlement.oppose_sr")
                    : Loc.Get("settlement.oppose"));
            }
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("settlement.no_active_proposals"));
        }

        // Show NPC-proposed buildings
        var proposed = state.ProposedBuildings.Where(b => b.Value.Tier > BuildingTier.None).ToList();
        if (proposed.Count > 0)
        {
            terminal.WriteLine("");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("settlement.settler_built_structures"));
            foreach (var kvp in proposed.OrderByDescending(b => (int)b.Value.Tier))
            {
                var tmpl = SettlementSystem.Instance.GetProposalTemplate(kvp.Key);
                string name = tmpl?.Name ?? kvp.Key;
                string tier = SettlementSystem.GetTierDisplayName(kvp.Value.Tier);
                terminal.SetColor(kvp.Value.Tier >= BuildingTier.Built ? "bright_green" : "yellow");
                terminal.WriteLine($"    {name,-20} {tier}");
                if (tmpl != null)
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine($"      {tmpl.EffectDescription}");
                }
            }
        }

        terminal.SetColor("gray");
        terminal.WriteLine("");
        terminal.WriteLine(IsScreenReader ? Loc.Get("settlement.back_sr") : Loc.Get("settlement.back"));
        terminal.WriteLine("");

        string input = await GetChoice();
        input = input.Trim().ToUpper();

        if (input == "E" && proposal != null)
        {
            string voterName = currentPlayer.Name;
            int myVote = proposal.PlayerVotes.GetValueOrDefault(voterName, 0);
            if (myVote != 0)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("settlement.already_voted"));
            }
            else if (currentPlayer.Gold < GameConfig.SettlementEndorsementCost)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("settlement.need_gold_endorse", $"{GameConfig.SettlementEndorsementCost:N0}"));
            }
            else
            {
                currentPlayer.Gold -= GameConfig.SettlementEndorsementCost;
                SettlementSystem.Instance.VoteOnProposal(voterName, 2);
                currentPlayer.Statistics?.RecordGoldSpent(GameConfig.SettlementEndorsementCost);
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("settlement.endorse_success"));
                PersistSettlementIfOnline();
            }
            await terminal.PressAnyKey();
        }
        else if (input == "O" && proposal != null)
        {
            string voterName = currentPlayer.Name;
            int myVote = proposal.PlayerVotes.GetValueOrDefault(voterName, 0);
            if (myVote != 0)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("settlement.already_voted"));
            }
            else
            {
                SettlementSystem.Instance.VoteOnProposal(voterName, -2);
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("settlement.oppose_success"));
                PersistSettlementIfOnline();
            }
            await terminal.PressAnyKey();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // NPC-PROPOSED BUILDING SERVICES
    // ═══════════════════════════════════════════════════════════════

    private async Task UseProposedService(string buildingId)
    {
        switch (buildingId)
        {
            case "arena":
                await UseArenaService();
                break;
            case "thieves_den":
                await UseThievesDenService();
                break;
            case "mystic_circle":
                await UseMysticCircleService();
                break;
            case "prison":
                await UsePrisonService();
                break;
            case "scouts_lodge":
                await UseScoutsLodgeService();
                break;
            case "library":
                await UseLibraryService();
                break;
            case "herbalist_hut":
                await UseHerbalistHutService();
                break;
            case "gambling_hall":
                await UseGamblingHallService();
                break;
            case "oracles_sanctum":
                await UseOracleService();
                break;
            default:
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("settlement.service_not_available"));
                await terminal.PressAnyKey();
                break;
        }
    }

    private async Task UseArenaService()
    {
        if (currentPlayer.HasSettlementBuff)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("ui.settlement_buff_active"));
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("bright_green");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("settlement.arena_desc"));
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("settlement.gained_damage_buff", $"{GameConfig.SettlementDamageBonus * 100:F0}", GameConfig.SettlementBuffDuration));

        currentPlayer.SettlementBuffType = (int)SettlementBuffType.DamageBonus;
        currentPlayer.SettlementBuffCombats = GameConfig.SettlementBuffDuration;
        currentPlayer.SettlementBuffValue = GameConfig.SettlementDamageBonus;

        await terminal.PressAnyKey();
    }

    private async Task UseThievesDenService()
    {
        if (currentPlayer.HasSettlementBuff)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("ui.settlement_buff_active"));
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("bright_green");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("settlement.thieves_desc"));
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("settlement.gained_gold_buff", $"{GameConfig.SettlementGoldBonus * 100:F0}", GameConfig.SettlementBuffDuration));

        currentPlayer.SettlementBuffType = (int)SettlementBuffType.GoldBonus;
        currentPlayer.SettlementBuffCombats = GameConfig.SettlementBuffDuration;
        currentPlayer.SettlementBuffValue = GameConfig.SettlementGoldBonus;

        await terminal.PressAnyKey();
    }

    private async Task UseMysticCircleService()
    {
        if (currentPlayer.SettlementCircleUsedToday)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("settlement.mystic_depleted"));
            await terminal.PressAnyKey();
            return;
        }

        currentPlayer.SettlementCircleUsedToday = true;

        if (currentPlayer.IsManaClass)
        {
            if (currentPlayer.Mana >= currentPlayer.MaxMana)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("settlement.mana_already_full"));
                await terminal.PressAnyKey();
                return;
            }

            long restoreAmount = (long)(currentPlayer.MaxMana * GameConfig.SettlementManaRestorePercent);
            currentPlayer.Mana = Math.Min(currentPlayer.MaxMana, currentPlayer.Mana + restoreAmount);

            terminal.SetColor("bright_green");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("settlement.mystic_mana_desc"));
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("settlement.restored_mp", restoreAmount, currentPlayer.Mana, currentPlayer.MaxMana));
        }
        else
        {
            // Non-casters get HP restoration instead
            if (currentPlayer.HP >= currentPlayer.MaxHP)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("settlement.already_full_hp"));
                await terminal.PressAnyKey();
                return;
            }

            long restoreAmount = (long)(currentPlayer.MaxHP * GameConfig.SettlementManaRestorePercent);
            currentPlayer.HP = Math.Min(currentPlayer.MaxHP, currentPlayer.HP + restoreAmount);

            terminal.SetColor("bright_green");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("settlement.mystic_hp_desc"));
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("settlement.restored_hp", restoreAmount, currentPlayer.HP, currentPlayer.MaxHP));
        }

        await terminal.PressAnyKey();
    }

    private async Task UsePrisonService()
    {
        if (currentPlayer.HasSettlementBuff)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("ui.settlement_buff_active"));
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("bright_green");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("settlement.prison_desc"));
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("settlement.gained_trap_resist", $"{GameConfig.SettlementTrapResist * 100:F0}", GameConfig.SettlementBuffDuration));

        currentPlayer.SettlementBuffType = (int)SettlementBuffType.TrapResist;
        currentPlayer.SettlementBuffCombats = GameConfig.SettlementBuffDuration;
        currentPlayer.SettlementBuffValue = GameConfig.SettlementTrapResist;

        await terminal.PressAnyKey();
    }

    private async Task UseScoutsLodgeService()
    {
        terminal.SetColor("cyan");
        terminal.WriteLine("");
        string input = await terminal.GetInput(Loc.Get("settlement.scouts_prompt"));
        if (!int.TryParse(input, out int startFloor) || startFloor < 1 || startFloor > 98)
        {
            terminal.WriteLine(Loc.Get("ui.cancelled"), "gray");
            return;
        }

        terminal.SetColor("bright_green");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("settlement.scouts_report"));

        for (int f = startFloor; f <= Math.Min(startFloor + 2, 100); f++)
        {
            terminal.SetColor("white");
            var monster = MonsterGenerator.GenerateMonster(f);
            if (monster != null)
            {
                terminal.WriteLine(Loc.Get("settlement.scouts_floor", f, monster.Level, monster.Name));
            }
        }

        await terminal.PressAnyKey();
    }

    private async Task UseLibraryService()
    {
        if (currentPlayer.HasSettlementBuff)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("ui.settlement_buff_active"));
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("bright_green");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("settlement.library_desc"));
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("settlement.gained_library_xp", $"{GameConfig.SettlementLibraryXPBonus * 100:F0}", GameConfig.SettlementBuffDuration));

        currentPlayer.SettlementBuffType = (int)SettlementBuffType.LibraryXP;
        currentPlayer.SettlementBuffCombats = GameConfig.SettlementBuffDuration;
        currentPlayer.SettlementBuffValue = GameConfig.SettlementLibraryXPBonus;

        await terminal.PressAnyKey();
    }

    private async Task UseHerbalistHutService()
    {
        if (currentPlayer.SettlementHerbClaimedToday)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("settlement.herbalist_claimed"));
            await terminal.PressAnyKey();
            return;
        }

        // Give a random herb
        var herbTypes = Enum.GetValues(typeof(HerbType)).Cast<HerbType>().Where(h => h != HerbType.None).ToArray();
        var herb = herbTypes[Random.Shared.Next(herbTypes.Length)];

        // Check if player can carry more
        int currentCount = currentPlayer.GetHerbCount(herb);
        int maxCarry = GameConfig.HerbMaxCarry[(int)herb];

        if (currentCount >= maxCarry)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("settlement.herbalist_max_carry", HerbData.GetName(herb)));
            await terminal.PressAnyKey();
            return;
        }

        // Add herb
        currentPlayer.AddHerb(herb);
        currentPlayer.SettlementHerbClaimedToday = true;

        terminal.SetColor("bright_green");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("settlement.herbalist_desc"));
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("settlement.herbalist_received", HerbData.GetName(herb)));
        terminal.SetColor("gray");
        terminal.WriteLine($"  ({HerbData.GetDescription(herb)})");

        await terminal.PressAnyKey();
    }

    private async Task UseGamblingHallService()
    {
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("settlement.gambling_header"));
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("settlement.gambling_gold_info", $"{currentPlayer.Gold:N0}", $"{GameConfig.SettlementGambleMaxBet:N0}"));
        terminal.WriteLine("");

        string input = await terminal.GetInput(Loc.Get("settlement.gambling_prompt"));
        if (!long.TryParse(input, out long bet) || bet <= 0)
        {
            terminal.WriteLine(Loc.Get("settlement.gambling_walk_away"), "gray");
            return;
        }

        bet = Math.Min(bet, GameConfig.SettlementGambleMaxBet);
        if (bet > currentPlayer.Gold)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("settlement.not_that_much_gold"));
            await terminal.PressAnyKey();
            return;
        }

        currentPlayer.Gold -= bet;
        bool win = Random.Shared.NextDouble() < 0.5;

        if (win)
        {
            long winnings = bet * 2;
            currentPlayer.Gold += winnings;
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("settlement.gambling_win", $"{winnings:N0}"));
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("settlement.gambling_lose", $"{bet:N0}"));
        }

        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("settlement.gambling_remaining", $"{currentPlayer.Gold:N0}"));
        await terminal.PressAnyKey();
    }

    private async Task UseOracleService()
    {
        var hints = new[]
        {
            Loc.Get("settlement.oracle_hint_0"),
            Loc.Get("settlement.oracle_hint_1"),
            Loc.Get("settlement.oracle_hint_2"),
            Loc.Get("settlement.oracle_hint_3"),
            Loc.Get("settlement.oracle_hint_4"),
            Loc.Get("settlement.oracle_hint_5"),
            Loc.Get("settlement.oracle_hint_6"),
            Loc.Get("settlement.oracle_hint_7"),
            Loc.Get("settlement.oracle_hint_8"),
            Loc.Get("settlement.oracle_hint_9"),
        };

        string hint = hints[Random.Shared.Next(hints.Length)];

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("settlement.oracle_intro"));
        terminal.SetColor("white");
        terminal.WriteLine($"  \"{hint}\"");
        terminal.WriteLine("");

        await terminal.PressAnyKey();
    }
}
