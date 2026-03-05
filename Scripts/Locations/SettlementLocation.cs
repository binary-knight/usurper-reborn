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
    private static readonly Random _random = new();

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
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                        THE  OUTSKIRTS                               ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        // Dynamic description based on settlement size
        terminal.SetColor("white");
        if (settlers < 8)
        {
            terminal.WriteLine("A handful of rough shelters cluster in a clearing beyond the gates.");
            terminal.WriteLine("Cookfire smoke rises lazily. The settlers eye you with cautious hope.");
        }
        else if (settlers < 12)
        {
            terminal.WriteLine("A growing community has taken root here. Rough-hewn buildings line");
            terminal.WriteLine("muddy paths. The sound of hammers and conversation fills the air.");
        }
        else
        {
            terminal.WriteLine("A bustling frontier settlement stretches before you. What began as");
            terminal.WriteLine("a few tents is becoming a proper town, alive with purpose.");
        }
        terminal.WriteLine("");

        // Settlement stats
        terminal.SetColor("cyan");
        terminal.WriteLine($"  Settlers: {settlers}/{GameConfig.SettlementMaxNPCs}    Treasury: {state.CommunalTreasury:N0} gold");
        terminal.WriteLine("");

        // Buildings status (compact)
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("  Buildings:");
        foreach (var kvp in state.Buildings.OrderByDescending(b => (int)b.Value.Tier))
        {
            var b = kvp.Value;
            string name = SettlementSystem.GetBuildingDisplayName(kvp.Key);
            bool isActive = state.ActiveBuilding == kvp.Key;
            // Show "In Progress" instead of "Not Started" when actively being built
            string tier = (isActive && b.Tier == BuildingTier.None && b.ResourcePool > 0)
                ? "In Progress"
                : SettlementSystem.GetTierDisplayName(b.Tier);
            string color = b.Tier switch
            {
                BuildingTier.Upgraded => "bright_green",
                BuildingTier.Built => "green",
                BuildingTier.Foundation => "yellow",
                _ => isActive ? "cyan" : "darkgray"
            };

            string active = isActive ? " [BUILDING...]" : "";
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
            terminal.Write("  Settlers: ");
            terminal.SetColor("white");
            var names = state.SettlerNames.Take(8).ToList();
            terminal.Write(string.Join(", ", names));
            if (settlers > 8) terminal.Write($" (+{settlers - 8} more)");
            terminal.WriteLine("");
            terminal.WriteLine("");
        }

        // Show NPC-proposed buildings
        var proposedBuilt = state.ProposedBuildings.Where(b => b.Value.Tier > BuildingTier.None).ToList();
        if (proposedBuilt.Count > 0)
        {
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("  Settler-Built:");
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
                string active = state.ActiveProposedBuildingId == kvp.Key ? " [BUILDING...]" : "";
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
                terminal.WriteLine($"  ** {state.CurrentProposal.ProposerName} proposes: {propTemplate.Name} **");
                terminal.SetColor("gray");
                int dispFor = state.CurrentProposal.SupportVotes + Math.Max(0, state.CurrentProposal.PlayerVoteWeight);
                int dispAgainst = state.CurrentProposal.OpposeVotes + Math.Max(0, -state.CurrentProposal.PlayerVoteWeight);
                terminal.WriteLine($"     Votes: {dispFor} for / {dispAgainst} against ({state.CurrentProposal.TicksRemaining} ticks left)");
                terminal.WriteLine("");
            }
        }

        // Menu
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("  [V] View Building Details");
        terminal.WriteLine("  [C] Contribute Gold");
        terminal.WriteLine("  [P] Proposals");

        var services = SettlementSystem.Instance.GetAvailableServices();
        var proposedServices = SettlementSystem.Instance.GetProposedServices();
        if (services.Count > 0 || proposedServices.Count > 0)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine("  [S] Settlement Services");
        }

        terminal.SetColor("gray");
        terminal.WriteLine("  [R] Return to Main Street");
        terminal.WriteLine("");

        ShowStatusLine();
    }

    private void DisplayLocationBBS()
    {
        terminal.ClearScreen();
        var state = SettlementSystem.Instance.State;
        int settlers = state.SettlerNames.Count;

        terminal.SetColor("bright_yellow");
        terminal.WriteLine("=== THE OUTSKIRTS ===");
        terminal.SetColor("white");
        terminal.WriteLine($"Settlers: {settlers}/{GameConfig.SettlementMaxNPCs}  Treasury: {state.CommunalTreasury:N0}g");

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
                terminal.WriteLine($"  Building: {SettlementSystem.GetBuildingDisplayName(state.ActiveBuilding.Value)} ({ab.ResourcePool:N0}/{cost:N0})");
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
                terminal.WriteLine($"  Proposal: {pt.Name} ({bFor}Y/{bAgainst}N)");
            }
        }

        terminal.SetColor("white");
        terminal.WriteLine("[V]Details [C]Contribute [P]Proposals [S]Services [R]Return");
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
                terminal.WriteLine("You head back through the gates to Main Street.", "gray");
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
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("╔══════════════════════════════════════╗");
        terminal.WriteLine("║          BUILDING STATUS             ║");
        terminal.WriteLine("╚══════════════════════════════════════╝");
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
                    terminal.WriteLine($"    Progress: [{"".PadRight(filled, '#').PadRight(20, '.')}] {bs.ResourcePool:N0}/{cost:N0} gold ({pct * 100:F0}%)");
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
        terminal.WriteLine($"Your gold: {currentPlayer.Gold:N0}");

        if (currentPlayer.Gold <= 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine("You have no gold to contribute.");
            await terminal.PressAnyKey();
            return;
        }

        string input = await terminal.GetInput("Amount to contribute (or 0 to cancel): ");
        if (!long.TryParse(input, out long amount) || amount <= 0)
        {
            terminal.WriteLine("Cancelled.", "gray");
            return;
        }

        if (amount > currentPlayer.Gold)
        {
            terminal.SetColor("red");
            terminal.WriteLine("You don't have that much gold!");
            await terminal.PressAnyKey();
            return;
        }

        currentPlayer.Gold -= amount;
        SettlementSystem.Instance.ContributeGold(currentPlayer.Name, amount);

        terminal.SetColor("bright_green");
        terminal.WriteLine($"You contribute {amount:N0} gold to the settlement.");
        terminal.SetColor("white");

        long totalContrib = SettlementSystem.Instance.State.PlayerContributions
            .GetValueOrDefault(currentPlayer.Name, 0);
        terminal.WriteLine($"Your total contributions: {totalContrib:N0} gold");

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
            terminal.WriteLine("No services available yet. The settlement needs more buildings!");
            await terminal.PressAnyKey();
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_green");
        terminal.WriteLine("Available Services:");
        terminal.SetColor("white");
        foreach (var (key, label, _) in services)
        {
            terminal.WriteLine($"  [{key}] {label}");
        }
        if (proposedServices.Count > 0)
        {
            terminal.SetColor("bright_cyan");
            foreach (var (key, label, _) in proposedServices)
            {
                terminal.WriteLine($"  [{key}] {label}");
            }
        }
        terminal.SetColor("gray");
        terminal.WriteLine("  [0] Cancel");
        terminal.WriteLine("");

        string input = await terminal.GetInput("Your choice: ");
        input = input.Trim();
        if (input == "0") { terminal.WriteLine("Cancelled.", "gray"); return; }

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

        terminal.WriteLine("Cancelled.", "gray");
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
            terminal.WriteLine("You already have a settlement buff active!");
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("bright_green");
        terminal.WriteLine("");
        terminal.WriteLine("The settlers share a hearty meal with you by the tavern fire.");
        terminal.WriteLine("You feel inspired and ready for battle.");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"  Gained: +{GameConfig.SettlementXPBonus * 100:F0}% XP for {GameConfig.SettlementBuffDuration} combats!");

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
            terminal.WriteLine("The shrine's healing power is spent for today. Return tomorrow.");
            await terminal.PressAnyKey();
            return;
        }

        if (currentPlayer.HP >= currentPlayer.MaxHP)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("You are already at full health.");
            await terminal.PressAnyKey();
            return;
        }

        int healAmount = (int)(currentPlayer.MaxHP * GameConfig.SettlementHealPercent);
        currentPlayer.HP = Math.Min(currentPlayer.MaxHP, currentPlayer.HP + healAmount);
        currentPlayer.Statistics?.RecordHealthRestored(healAmount);
        currentPlayer.SettlementShrineUsedToday = true;

        terminal.SetColor("bright_green");
        terminal.WriteLine("");
        terminal.WriteLine("The shrine tender guides you to a quiet alcove.");
        terminal.WriteLine("Warmth spreads through you as your wounds mend.");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"  Healed {healAmount} HP! (HP: {currentPlayer.HP}/{currentPlayer.MaxHP})");

        await terminal.PressAnyKey();
    }

    private async Task UsePalisadeService()
    {
        if (currentPlayer.HasSettlementBuff)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("You already have a settlement buff active!");
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("bright_green");
        terminal.WriteLine("");
        terminal.WriteLine("The settlement guards share their patrol techniques with you.");
        terminal.WriteLine("You feel more aware of threats and better prepared to defend.");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"  Gained: +{GameConfig.SettlementDefenseBonus * 100:F0}% defense for {GameConfig.SettlementBuffDuration} combats!");

        currentPlayer.SettlementBuffType = (int)SettlementBuffType.DefenseBonus;
        currentPlayer.SettlementBuffCombats = GameConfig.SettlementBuffDuration;
        currentPlayer.SettlementBuffValue = GameConfig.SettlementDefenseBonus;

        await terminal.PressAnyKey();
    }

    private async Task UseWorkshopService()
    {
        // Find first unidentified item in inventory
        var unidentified = currentPlayer.Inventory?.FirstOrDefault(i => i != null && !i.IsIdentified);
        if (unidentified == null)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("You have no unidentified items.");
            await terminal.PressAnyKey();
            return;
        }

        unidentified.IsIdentified = true;
        terminal.SetColor("bright_green");
        terminal.WriteLine("");
        terminal.WriteLine("The settlement craftsman examines your item carefully...");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"  Identified: {unidentified.Name}!");

        await terminal.PressAnyKey();
    }

    private async Task UseWatchtowerService()
    {
        terminal.SetColor("cyan");
        terminal.WriteLine("");
        string input = await terminal.GetInput("Which dungeon floor to scout? (1-100): ");
        if (!int.TryParse(input, out int floor) || floor < 1 || floor > 100)
        {
            terminal.WriteLine("Cancelled.", "gray");
            return;
        }

        terminal.SetColor("bright_green");
        terminal.WriteLine("");
        terminal.WriteLine($"The watchtower scouts report on Floor {floor}:");
        terminal.SetColor("white");

        // Generate some useful info about the floor
        var sampleMonster = MonsterGenerator.GenerateMonster(floor);
        if (sampleMonster != null)
        {
            terminal.WriteLine($"  Creatures sighted: Level ~{sampleMonster.Level} monsters");
            terminal.WriteLine($"  Example: {sampleMonster.Name}");
        }

        // Check for special floors
        var specialFloors = new Dictionary<int, string>
        {
            { 15, "An ancient seal radiates power here." },
            { 25, "The presence of Maelketh lingers." },
            { 30, "A seal awaits a worthy claimant." },
            { 40, "Veloura's domain stretches here." },
            { 45, "A seal shimmers in the darkness." },
            { 55, "Thorgrim guards this depth." },
            { 60, "An ancient seal hums with energy." },
            { 70, "Noctura's shadows dominate." },
            { 80, "A seal of tremendous power waits." },
            { 85, "Aurelion's radiance blinds." },
            { 95, "Terravok shakes the earth." },
            { 99, "The final seal beckons." },
            { 100, "Manwe, the Eternal, awaits." }
        };

        if (specialFloors.TryGetValue(floor, out string hint))
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"  Special: {hint}");
        }

        await terminal.PressAnyKey();
    }

    private async Task UseCouncilHallService()
    {
        var state = SettlementSystem.Instance.State;

        if (currentPlayer.SettlementGoldClaimedToday)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("You've already claimed your share today. Come back tomorrow.");
            await terminal.PressAnyKey();
            return;
        }

        if (state.CommunalTreasury <= 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("The communal treasury is empty.");
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
        terminal.WriteLine("The council elder counts out your share from the treasury.");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"  Received: {share:N0} gold!");
        terminal.SetColor("gray");
        terminal.WriteLine($"  Remaining treasury: {state.CommunalTreasury:N0} gold");

        PersistSettlementIfOnline();
        await terminal.PressAnyKey();
    }

    private async Task UseMarketService()
    {
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("");
        terminal.WriteLine("The settlers display their wares on rough wooden tables.");
        terminal.SetColor("gray");
        terminal.WriteLine("(Market stall trading coming in a future update!)");
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
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔══════════════════════════════════════╗");
        terminal.WriteLine("║         SETTLEMENT PROPOSALS         ║");
        terminal.WriteLine("╚══════════════════════════════════════╝");
        terminal.WriteLine("");

        if (proposal != null)
        {
            var template = SettlementSystem.Instance.GetProposalTemplate(proposal.BuildingId);
            if (template != null)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"  Active Proposal: {template.Name}");
                terminal.SetColor("white");
                terminal.WriteLine($"  Proposed by: {proposal.ProposerName}");
                terminal.SetColor("gray");
                terminal.WriteLine($"  \"{template.Description}\"");
                terminal.SetColor("white");
                terminal.WriteLine($"  Effect: {template.EffectDescription}");
                terminal.WriteLine("");
                terminal.SetColor("cyan");

                int totalFor = proposal.SupportVotes + Math.Max(0, proposal.PlayerVoteWeight);
                int totalAgainst = proposal.OpposeVotes + Math.Max(0, -proposal.PlayerVoteWeight);
                terminal.WriteLine($"  Support: {totalFor}    Oppose: {totalAgainst}    (Resolves in {proposal.TicksRemaining} ticks)");
                terminal.WriteLine("");

                terminal.SetColor("bright_green");
                terminal.WriteLine($"  [E] Endorse ({GameConfig.SettlementEndorsementCost:N0} gold, +2 support)");
                terminal.SetColor("red");
                terminal.WriteLine($"  [O] Oppose (+2 against)");
            }
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  No active proposals. The settlers are focused on other work.");
        }

        // Show NPC-proposed buildings
        var proposed = state.ProposedBuildings.Where(b => b.Value.Tier > BuildingTier.None).ToList();
        if (proposed.Count > 0)
        {
            terminal.WriteLine("");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("  Settler-Built Structures:");
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
        terminal.WriteLine("  [0] Back");
        terminal.WriteLine("");

        string input = await terminal.GetInput("Your choice: ");
        input = input.Trim().ToUpper();

        if (input == "E" && proposal != null)
        {
            string voterName = currentPlayer.Name;
            int myVote = proposal.PlayerVotes.GetValueOrDefault(voterName, 0);
            if (myVote != 0)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("You've already cast your vote on this proposal.");
            }
            else if (currentPlayer.Gold < GameConfig.SettlementEndorsementCost)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"You need {GameConfig.SettlementEndorsementCost:N0} gold to endorse a proposal.");
            }
            else
            {
                currentPlayer.Gold -= GameConfig.SettlementEndorsementCost;
                SettlementSystem.Instance.VoteOnProposal(voterName, 2);
                currentPlayer.Statistics?.RecordGoldSpent(GameConfig.SettlementEndorsementCost);
                terminal.SetColor("bright_green");
                terminal.WriteLine("You endorse the proposal! Your influence sways the settlers.");
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
                terminal.WriteLine("You've already cast your vote on this proposal.");
            }
            else
            {
                SettlementSystem.Instance.VoteOnProposal(voterName, -2);
                terminal.SetColor("yellow");
                terminal.WriteLine("You speak against the proposal. The settlers reconsider.");
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
                terminal.WriteLine("This service is not yet available.");
                await terminal.PressAnyKey();
                break;
        }
    }

    private async Task UseArenaService()
    {
        if (currentPlayer.HasSettlementBuff)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("You already have a settlement buff active!");
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("bright_green");
        terminal.WriteLine("");
        terminal.WriteLine("You spar with the arena fighters, learning their aggressive techniques.");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"  Gained: +{GameConfig.SettlementDamageBonus * 100:F0}% damage for {GameConfig.SettlementBuffDuration} combats!");

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
            terminal.WriteLine("You already have a settlement buff active!");
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("bright_green");
        terminal.WriteLine("");
        terminal.WriteLine("The thieves share their secrets of finding hidden treasures.");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"  Gained: +{GameConfig.SettlementGoldBonus * 100:F0}% gold find for {GameConfig.SettlementBuffDuration} combats!");

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
            terminal.WriteLine("The mystic circle's energy is depleted for today. Return tomorrow.");
            await terminal.PressAnyKey();
            return;
        }

        currentPlayer.SettlementCircleUsedToday = true;

        if (currentPlayer.IsManaClass)
        {
            if (currentPlayer.Mana >= currentPlayer.MaxMana)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("Your mana is already full.");
                await terminal.PressAnyKey();
                return;
            }

            long restoreAmount = (long)(currentPlayer.MaxMana * GameConfig.SettlementManaRestorePercent);
            currentPlayer.Mana = Math.Min(currentPlayer.MaxMana, currentPlayer.Mana + restoreAmount);

            terminal.SetColor("bright_green");
            terminal.WriteLine("");
            terminal.WriteLine("The mystic circle hums with power. Arcane energy flows into you.");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"  Restored {restoreAmount} MP! (MP: {currentPlayer.Mana}/{currentPlayer.MaxMana})");
        }
        else
        {
            // Non-casters get HP restoration instead
            if (currentPlayer.HP >= currentPlayer.MaxHP)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("You are already at full health.");
                await terminal.PressAnyKey();
                return;
            }

            long restoreAmount = (long)(currentPlayer.MaxHP * GameConfig.SettlementManaRestorePercent);
            currentPlayer.HP = Math.Min(currentPlayer.MaxHP, currentPlayer.HP + restoreAmount);

            terminal.SetColor("bright_green");
            terminal.WriteLine("");
            terminal.WriteLine("The mystic circle pulses with restorative energy, mending your wounds.");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"  Restored {restoreAmount} HP! (HP: {currentPlayer.HP}/{currentPlayer.MaxHP})");
        }

        await terminal.PressAnyKey();
    }

    private async Task UsePrisonService()
    {
        if (currentPlayer.HasSettlementBuff)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("You already have a settlement buff active!");
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("bright_green");
        terminal.WriteLine("");
        terminal.WriteLine("The jailers teach you how to spot and avoid traps.");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"  Gained: -{GameConfig.SettlementTrapResist * 100:F0}% trap damage for {GameConfig.SettlementBuffDuration} combats!");

        currentPlayer.SettlementBuffType = (int)SettlementBuffType.TrapResist;
        currentPlayer.SettlementBuffCombats = GameConfig.SettlementBuffDuration;
        currentPlayer.SettlementBuffValue = GameConfig.SettlementTrapResist;

        await terminal.PressAnyKey();
    }

    private async Task UseScoutsLodgeService()
    {
        terminal.SetColor("cyan");
        terminal.WriteLine("");
        string input = await terminal.GetInput("Which dungeon floor to scout? (scouts will check 3 floors): ");
        if (!int.TryParse(input, out int startFloor) || startFloor < 1 || startFloor > 98)
        {
            terminal.WriteLine("Cancelled.", "gray");
            return;
        }

        terminal.SetColor("bright_green");
        terminal.WriteLine("");
        terminal.WriteLine("The scouts fan out and report back:");

        for (int f = startFloor; f <= Math.Min(startFloor + 2, 100); f++)
        {
            terminal.SetColor("white");
            var monster = MonsterGenerator.GenerateMonster(f);
            if (monster != null)
            {
                terminal.WriteLine($"  Floor {f}: Level ~{monster.Level} ({monster.Name})");
            }
        }

        await terminal.PressAnyKey();
    }

    private async Task UseLibraryService()
    {
        if (currentPlayer.HasSettlementBuff)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("You already have a settlement buff active!");
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("bright_green");
        terminal.WriteLine("");
        terminal.WriteLine("You spend time studying ancient tomes in the library.");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"  Gained: +{GameConfig.SettlementLibraryXPBonus * 100:F0}% XP for {GameConfig.SettlementBuffDuration} combats!");

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
            terminal.WriteLine("The herbalist has already given you a herb today. Come back tomorrow.");
            await terminal.PressAnyKey();
            return;
        }

        // Give a random herb
        var herbTypes = Enum.GetValues(typeof(HerbType)).Cast<HerbType>().Where(h => h != HerbType.None).ToArray();
        var herb = herbTypes[_random.Next(herbTypes.Length)];

        // Check if player can carry more
        int currentCount = currentPlayer.GetHerbCount(herb);
        int maxCarry = GameConfig.HerbMaxCarry[(int)herb];

        if (currentCount >= maxCarry)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"You're already carrying the maximum number of {HerbData.GetName(herb)}.");
            await terminal.PressAnyKey();
            return;
        }

        // Add herb
        currentPlayer.AddHerb(herb);
        currentPlayer.SettlementHerbClaimedToday = true;

        terminal.SetColor("bright_green");
        terminal.WriteLine("");
        terminal.WriteLine("The herbalist rummages through dried bundles and hands you something.");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"  Received: {HerbData.GetName(herb)}!");
        terminal.SetColor("gray");
        terminal.WriteLine($"  ({HerbData.GetDescription(herb)})");

        await terminal.PressAnyKey();
    }

    private async Task UseGamblingHallService()
    {
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("");
        terminal.WriteLine("The Gambling Hall buzzes with dice rolls and cheers.");
        terminal.SetColor("white");
        terminal.WriteLine($"Your gold: {currentPlayer.Gold:N0}  (Max bet: {GameConfig.SettlementGambleMaxBet:N0})");
        terminal.WriteLine("");

        string input = await terminal.GetInput("How much gold to wager? (0 to leave): ");
        if (!long.TryParse(input, out long bet) || bet <= 0)
        {
            terminal.WriteLine("You walk away from the tables.", "gray");
            return;
        }

        bet = Math.Min(bet, GameConfig.SettlementGambleMaxBet);
        if (bet > currentPlayer.Gold)
        {
            terminal.SetColor("red");
            terminal.WriteLine("You don't have that much gold!");
            await terminal.PressAnyKey();
            return;
        }

        currentPlayer.Gold -= bet;
        bool win = _random.NextDouble() < 0.5;

        if (win)
        {
            long winnings = bet * 2;
            currentPlayer.Gold += winnings;
            terminal.SetColor("bright_green");
            terminal.WriteLine($"The dice favor you! You win {winnings:N0} gold!");
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine($"Bad luck! You lose {bet:N0} gold.");
        }

        terminal.SetColor("gray");
        terminal.WriteLine($"Gold remaining: {currentPlayer.Gold:N0}");
        await terminal.PressAnyKey();
    }

    private async Task UseOracleService()
    {
        var hints = new[]
        {
            "The deeper you go, the stronger the rewards... and the dangers.",
            "Ancient seals wait on floors 15, 30, 45, 60, 80, and 99.",
            "Old Gods guard floors 25, 40, 55, 70, 85, 95, and 100.",
            "Companions found in the Inn have their own quests and stories.",
            "The King's favor can be won... or taken by force.",
            "Some weapons carry enchantments that reveal themselves in battle.",
            "The settlement grows stronger with each building completed.",
            "NPC settlers build what their hearts desire. Different settlers, different towns.",
            "Herbs gathered from your garden can turn the tide of battle.",
            "There are multiple endings to discover. Will you become a god?",
        };

        string hint = hints[_random.Next(hints.Length)];

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("");
        terminal.WriteLine("The veiled oracle speaks in a distant voice:");
        terminal.SetColor("white");
        terminal.WriteLine($"  \"{hint}\"");
        terminal.WriteLine("");

        await terminal.PressAnyKey();
    }
}
