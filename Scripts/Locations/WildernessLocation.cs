using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UsurperRemake.Systems;
using UsurperRemake.BBS;

/// <summary>
/// The Wilderness — explore 4 themed regions beyond the city gates.
/// Each expedition is a self-contained encounter (combat, foraging, ruins, traveler, shrine).
/// Limited to 4 explorations per day.
/// </summary>
public class WildernessLocation : BaseLocation
{
    private static readonly Random _random = new();

    protected override void DisplayLocation()
    {
        if (IsBBSSession) { DisplayLocationBBS(); return; }

        terminal.ClearScreen();

        terminal.SetColor("bright_green");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                         THE  WILDERNESS                             ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("Beyond the city gates, untamed lands stretch in every direction.");
        terminal.WriteLine("Danger and discovery await those bold enough to venture forth.");
        terminal.WriteLine("");

        int remaining = GameConfig.WildernessMaxDailyExplorations - currentPlayer.WildernessExplorationsToday;
        terminal.SetColor(remaining > 0 ? "bright_yellow" : "red");
        terminal.WriteLine($"Expeditions remaining: {remaining}/{GameConfig.WildernessMaxDailyExplorations}");
        terminal.WriteLine("");

        // Show regions
        foreach (var region in WildernessData.Regions)
        {
            bool canAccess = currentPlayer.Level >= region.MinLevel;
            terminal.SetColor(canAccess ? region.ThemeColor : "darkgray");
            string levelReq = region.MinLevel > 1 ? $" (Level {region.MinLevel}+)" : " (Any level)";
            string lockIcon = canAccess ? "" : " [LOCKED]";
            terminal.WriteLine($"  [{region.DirectionKey}] {region.Name,-24} - {region.Direction}{levelReq}{lockIcon}");
        }

        terminal.WriteLine("");

        // Discoveries
        int discoveryCount = currentPlayer.WildernessDiscoveries.Count;
        if (discoveryCount > 0)
        {
            int revisitsLeft = GameConfig.WildernessMaxDailyRevisits - currentPlayer.WildernessRevisitsToday;
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"  [D] Discoveries ({discoveryCount} found, {revisitsLeft} revisits left)");
        }

        terminal.SetColor("gray");
        terminal.WriteLine("  [R] Return to Main Street");
        terminal.WriteLine("");

        ShowStatusLine();
    }

    private void DisplayLocationBBS()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_green");
        terminal.WriteLine("=== THE WILDERNESS ===");
        terminal.WriteLine("");

        int remaining = GameConfig.WildernessMaxDailyExplorations - currentPlayer.WildernessExplorationsToday;
        terminal.SetColor(remaining > 0 ? "bright_yellow" : "red");
        terminal.WriteLine($"Expeditions: {remaining}/{GameConfig.WildernessMaxDailyExplorations}");

        foreach (var region in WildernessData.Regions)
        {
            bool canAccess = currentPlayer.Level >= region.MinLevel;
            terminal.SetColor(canAccess ? "white" : "darkgray");
            terminal.WriteLine($"[{region.DirectionKey}] {region.Name} {(canAccess ? "" : "[LOCKED]")}");
        }

        if (currentPlayer.WildernessDiscoveries.Count > 0)
        {
            int revisitsLeft = GameConfig.WildernessMaxDailyRevisits - currentPlayer.WildernessRevisitsToday;
            terminal.WriteLine($"[D] Discoveries ({currentPlayer.WildernessDiscoveries.Count}, {revisitsLeft} revisits)");
        }
        terminal.WriteLine("[R] Return");
        terminal.WriteLine("");
        ShowStatusLine();
    }

    protected override async Task<bool> ProcessChoice(string choice)
    {
        string upper = choice.ToUpper().Trim();

        // Check region keys
        var region = WildernessData.GetRegionByKey(upper);
        if (region != null)
        {
            await ExploreRegion(region);
            return false;
        }

        switch (upper)
        {
            case "D":
                await ShowDiscoveries();
                return false;

            case "R":
            case "Q":
                terminal.WriteLine("You return through the city gates.", "gray");
                await Task.Delay(1500);
                throw new LocationExitException(GameLocation.MainStreet);

            default:
                var (handled, shouldExit) = await TryProcessGlobalCommand(choice);
                if (handled) return shouldExit;
                return false;
        }
    }

    protected override string GetMudPromptName() => "Wilderness";

    // ═══════════════════════════════════════════════════════════════
    // EXPLORATION
    // ═══════════════════════════════════════════════════════════════

    private async Task ExploreRegion(WildernessRegion region)
    {
        // Level check
        if (currentPlayer.Level < region.MinLevel)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"The {region.Name} is too dangerous for you. (Requires level {region.MinLevel})");
            await Task.Delay(2000);
            return;
        }

        // Daily limit check
        if (currentPlayer.WildernessExplorationsToday >= GameConfig.WildernessMaxDailyExplorations)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("You're too tired for another expedition today. Rest and try tomorrow.");
            await Task.Delay(2000);
            return;
        }

        // Consume expedition
        currentPlayer.WildernessExplorationsToday++;

        // Advance game time and fatigue
        if (!DoorMode.IsOnlineMode)
        {
            currentPlayer.GameTimeMinutes += GameConfig.WildernessTimeCostMinutes;
            currentPlayer.Fatigue = Math.Min(100, currentPlayer.Fatigue + GameConfig.WildernessFatigueCost);
        }

        // Show travel text
        terminal.ClearScreen();
        terminal.SetColor(region.ThemeColor);
        terminal.WriteLine($"═══ {region.Name} ═══");
        terminal.WriteLine("");
        terminal.SetColor("white");
        foreach (var line in region.Description.Split('\n'))
            terminal.WriteLine(line);
        terminal.WriteLine("");
        await Task.Delay(2000);

        // Roll encounter type: 40% combat, 25% foraging, 15% ruins, 10% traveler, 10% shrine
        int roll = _random.Next(100);
        if (roll < 40)
            await CombatEncounter(region);
        else if (roll < 65)
            await ForagingEncounter(region);
        else if (roll < 80)
            await RuinsEncounter(region);
        else if (roll < 90)
            await TravelerEncounter(region);
        else
            await ShrineEncounter(region);

        // Chance to discover something new (10% per trip)
        if (_random.Next(100) < 10)
            await CheckForDiscovery(region);
    }

    // ═══════════════════════════════════════════════════════════════
    // ENCOUNTER TYPES
    // ═══════════════════════════════════════════════════════════════

    private async Task CombatEncounter(WildernessRegion region)
    {
        string monsterName = region.MonsterNames[_random.Next(region.MonsterNames.Length)];

        terminal.SetColor("red");
        terminal.WriteLine($"A {monsterName} emerges from the {region.Name.ToLower()}!");
        terminal.WriteLine("");
        await Task.Delay(1500);

        // Generate monster scaled to player level (capped by region difficulty)
        int monsterLevel = Math.Max(region.MinLevel, currentPlayer.Level - 2 + _random.Next(5));
        var monster = MonsterGenerator.GenerateMonster(monsterLevel);
        monster.Name = monsterName;

        // Run full combat
        var combatEngine = new CombatEngine(terminal);
        var teammates = new List<Character>();

        // Add companions if any
        var companionChars = CompanionSystem.Instance?.GetCompanionsAsCharacters();
        if (companionChars != null)
            teammates.AddRange(companionChars.Where(c => c.IsAlive));

        var result = await combatEngine.PlayerVsMonster(currentPlayer, monster, teammates);

        if (result.Outcome == CombatOutcome.Victory)
        {
            // Bonus wilderness gold
            long bonusGold = (long)(_random.Next(10, 30) * (1 + region.MinLevel / 10.0));
            currentPlayer.Gold += bonusGold;
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"You search the area and find {bonusGold} additional gold.");
        }

        await terminal.PressAnyKey();
    }

    private async Task ForagingEncounter(WildernessRegion region)
    {
        var result = region.ForagingResults[_random.Next(region.ForagingResults.Length)];

        terminal.SetColor("green");
        terminal.WriteLine("You search the area carefully...");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine(result.text);
        terminal.WriteLine("");

        ApplyForagingResult(result.effect, region);

        await terminal.PressAnyKey();
    }

    private void ApplyForagingResult(string effect, WildernessRegion region)
    {
        int levelScale = Math.Max(1, region.MinLevel / 5);

        switch (effect)
        {
            case "herb_healing":
                if (currentPlayer.HerbHealing < GameConfig.HerbMaxCarry[(int)HerbType.HealingHerb])
                {
                    currentPlayer.HerbHealing++;
                    terminal.SetColor("green");
                    terminal.WriteLine("Found: Healing Herb (+1)");
                }
                else
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine("Your herb pouch is full.");
                }
                break;
            case "herb_ironbark":
                if (currentPlayer.HerbIronbark < GameConfig.HerbMaxCarry[(int)HerbType.IronbarkRoot])
                {
                    currentPlayer.HerbIronbark++;
                    terminal.SetColor("green");
                    terminal.WriteLine("Found: Ironbark Root (+1)");
                }
                else
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine("Your herb pouch is full.");
                }
                break;
            case "herb_firebloom":
                if (currentPlayer.HerbFirebloom < GameConfig.HerbMaxCarry[(int)HerbType.FirebloomPetal])
                {
                    currentPlayer.HerbFirebloom++;
                    terminal.SetColor("green");
                    terminal.WriteLine("Found: Firebloom Petal (+1)");
                }
                else
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine("Your herb pouch is full.");
                }
                break;
            case "herb_starbloom":
                if (currentPlayer.HerbStarbloom < GameConfig.HerbMaxCarry[(int)HerbType.StarbloomEssence])
                {
                    currentPlayer.HerbStarbloom++;
                    terminal.SetColor("green");
                    terminal.WriteLine("Found: Starbloom Essence (+1)");
                }
                else
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine("Your herb pouch is full.");
                }
                break;
            case "herb_swift":
                if (currentPlayer.HerbSwiftthistle < GameConfig.HerbMaxCarry[(int)HerbType.Swiftthistle])
                {
                    currentPlayer.HerbSwiftthistle++;
                    terminal.SetColor("green");
                    terminal.WriteLine("Found: Swiftthistle (+1)");
                }
                else
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine("Your herb pouch is full.");
                }
                break;
            case "heal_small":
                long healSmall = Math.Min(currentPlayer.MaxHP / 10, currentPlayer.MaxHP - currentPlayer.HP);
                if (healSmall > 0)
                {
                    currentPlayer.HP += healSmall;
                    terminal.SetColor("green");
                    terminal.WriteLine($"You eat the foraged food. (+{healSmall} HP)");
                }
                else
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine("A pleasant snack, but you're already healthy.");
                }
                break;
            case "heal_medium":
                long healMed = Math.Min(currentPlayer.MaxHP / 5, currentPlayer.MaxHP - currentPlayer.HP);
                if (healMed > 0)
                {
                    currentPlayer.HP += healMed;
                    terminal.SetColor("green");
                    terminal.WriteLine($"The medicinal plant restores your health. (+{healMed} HP)");
                }
                break;
            case "gold_small":
                long goldS = 20 * levelScale + _random.Next(20);
                currentPlayer.Gold += goldS;
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"Worth {goldS} gold!");
                break;
            case "gold_medium":
                long goldM = 50 * levelScale + _random.Next(50);
                currentPlayer.Gold += goldM;
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"Worth {goldM} gold!");
                break;
            case "gold_large":
                long goldL = 100 * levelScale + _random.Next(100);
                currentPlayer.Gold += goldL;
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"Worth {goldL} gold!");
                break;
            case "nothing":
                terminal.SetColor("gray");
                terminal.WriteLine("Better luck next time.");
                break;
        }
    }

    private async Task RuinsEncounter(WildernessRegion region)
    {
        string ruins = region.RuinsEncounters[_random.Next(region.RuinsEncounters.Length)];

        terminal.SetColor("cyan");
        terminal.WriteLine("You discover ruins in the wilderness...");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine(ruins);
        terminal.WriteLine("");

        terminal.SetColor("bright_yellow");
        terminal.WriteLine("[S] Search for treasure   [L] Leave it alone");
        terminal.WriteLine("");

        var choice = await terminal.GetInput("Your choice: ");

        if (choice.ToUpper() == "S")
        {
            // 60% treasure, 20% trap, 20% nothing
            int roll = _random.Next(100);
            if (roll < 60)
            {
                long gold = 30 + (long)(currentPlayer.Level * 3) + _random.Next(50);
                currentPlayer.Gold += gold;
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"You find {gold} gold hidden in the ruins!");

                // Small chance of a healing potion
                if (_random.Next(100) < 30)
                {
                    currentPlayer.Healing = Math.Min(currentPlayer.Healing + 1, currentPlayer.MaxPotions);
                    terminal.SetColor("green");
                    terminal.WriteLine("You also find a dusty healing potion!");
                }
            }
            else if (roll < 80)
            {
                long damage = Math.Max(1, currentPlayer.MaxHP / 10);
                currentPlayer.HP = Math.Max(1, currentPlayer.HP - damage);
                terminal.SetColor("red");
                terminal.WriteLine($"A hidden trap springs! You take {damage} damage!");
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine("The ruins have already been picked clean.");
            }
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("You move on, leaving the ruins undisturbed.");
        }

        await terminal.PressAnyKey();
    }

    private async Task TravelerEncounter(WildernessRegion region)
    {
        var traveler = region.TravelerEncounters[_random.Next(region.TravelerEncounters.Length)];

        terminal.SetColor("cyan");
        terminal.WriteLine(traveler.text);
        terminal.WriteLine("");

        terminal.SetColor("bright_yellow");
        terminal.WriteLine("[T] Talk   [L] Leave");
        terminal.WriteLine("");

        var choice = await terminal.GetInput("Your choice: ");

        if (choice.ToUpper() == "T")
        {
            // Travelers offer random benefits
            int roll = _random.Next(100);
            if (roll < 40)
            {
                // Trade offer
                long cost = 20 + _random.Next(30);
                terminal.SetColor("white");
                terminal.WriteLine($"The {traveler.name} offers to sell you a healing potion for {cost} gold.");
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("[Y] Buy   [N] Decline");
                var buy = await terminal.GetInput("Your choice: ");
                if (buy.ToUpper() == "Y" && currentPlayer.Gold >= cost)
                {
                    currentPlayer.Gold -= cost;
                    currentPlayer.Healing = Math.Min(currentPlayer.Healing + 1, currentPlayer.MaxPotions);
                    terminal.SetColor("green");
                    terminal.WriteLine("Purchased!");
                }
                else if (buy.ToUpper() == "Y")
                {
                    terminal.SetColor("red");
                    terminal.WriteLine("You don't have enough gold.");
                }
            }
            else if (roll < 70)
            {
                // Lore / hint
                terminal.SetColor("white");
                terminal.WriteLine($"The {traveler.name} shares wisdom of the wilds.");
                terminal.SetColor("cyan");
                terminal.WriteLine("\"The deeper dungeon levels hold treasures beyond imagining,");
                terminal.WriteLine(" but the Old Gods sleep there. Tread carefully.\"");
            }
            else
            {
                // Small healing
                long heal = currentPlayer.MaxHP / 8;
                currentPlayer.HP = Math.Min(currentPlayer.MaxHP, currentPlayer.HP + heal);
                terminal.SetColor("green");
                terminal.WriteLine($"The {traveler.name} shares food and drink. (+{heal} HP)");
            }
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("You nod and continue on your way.");
        }

        await terminal.PressAnyKey();
    }

    private async Task ShrineEncounter(WildernessRegion region)
    {
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("You discover a weathered shrine at the roadside.");
        terminal.WriteLine("Ancient symbols glow faintly in the stone.");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("bright_yellow");
        terminal.WriteLine("[P] Pray   [L] Leave");
        terminal.WriteLine("");

        var choice = await terminal.GetInput("Your choice: ");

        if (choice.ToUpper() == "P")
        {
            int roll = _random.Next(100);
            if (roll < 30)
            {
                // Heal
                long heal = currentPlayer.MaxHP / 4;
                currentPlayer.HP = Math.Min(currentPlayer.MaxHP, currentPlayer.HP + heal);
                long mana = currentPlayer.MaxMana / 4;
                currentPlayer.Mana = Math.Min(currentPlayer.MaxMana, currentPlayer.Mana + mana);
                terminal.SetColor("bright_green");
                terminal.WriteLine("A warm light envelops you. Your wounds close and your mind clears.");
                if (heal > 0) terminal.WriteLine($"  +{heal} HP");
                if (mana > 0) terminal.WriteLine($"  +{mana} Mana");
            }
            else if (roll < 55)
            {
                // Small stat buff
                int stat = _random.Next(3);
                terminal.SetColor("bright_cyan");
                if (stat == 0)
                {
                    currentPlayer.Strength += 1;
                    terminal.WriteLine("The shrine blesses you with strength. (+1 STR)");
                }
                else if (stat == 1)
                {
                    currentPlayer.Dexterity += 1;
                    terminal.WriteLine("The shrine blesses you with agility. (+1 DEX)");
                }
                else
                {
                    currentPlayer.Wisdom += 1;
                    terminal.WriteLine("The shrine blesses you with wisdom. (+1 WIS)");
                }
            }
            else if (roll < 75)
            {
                // XP
                long xp = 10 + currentPlayer.Level * 5;
                currentPlayer.Experience += xp;
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"Ancient knowledge flows into you. (+{xp} XP)");
            }
            else
            {
                // Nothing special
                terminal.SetColor("gray");
                terminal.WriteLine("You feel a moment of peace, but nothing else happens.");
            }
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("You pass the shrine by.");
        }

        await terminal.PressAnyKey();
    }

    // ═══════════════════════════════════════════════════════════════
    // DISCOVERIES
    // ═══════════════════════════════════════════════════════════════

    private async Task CheckForDiscovery(WildernessRegion region)
    {
        // Find an undiscovered location in this region
        var undiscovered = region.Discoveries
            .Where(d => !currentPlayer.WildernessDiscoveries.Contains(d.Id) && currentPlayer.Level >= d.MinLevel)
            .ToArray();

        if (undiscovered.Length == 0) return;

        var discovery = undiscovered[_random.Next(undiscovered.Length)];
        currentPlayer.WildernessDiscoveries.Add(discovery.Id);

        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("★ DISCOVERY ★");
        terminal.SetColor("white");
        terminal.WriteLine($"You've found: {discovery.Name}");
        terminal.SetColor("gray");
        terminal.WriteLine(discovery.Description);
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("(You can revisit this location from the Discoveries menu)");

        NewsSystem.Instance?.Newsy($"☆ {currentPlayer.Name} discovered {discovery.Name} in the {region.Name}!");

        await Task.Delay(3000);
    }

    private async Task ShowDiscoveries()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══ Your Discoveries ═══");
        terminal.WriteLine("");

        if (currentPlayer.WildernessDiscoveries.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("You haven't discovered anything yet.");
            terminal.WriteLine("Explore the wilderness to find hidden locations.");
            await terminal.PressAnyKey();
            return;
        }

        var allDiscoveries = WildernessData.Regions
            .SelectMany(r => r.Discoveries.Select(d => (region: r, discovery: d)))
            .Where(x => currentPlayer.WildernessDiscoveries.Contains(x.discovery.Id))
            .ToList();

        for (int i = 0; i < allDiscoveries.Count; i++)
        {
            var (region, discovery) = allDiscoveries[i];
            terminal.SetColor(region.ThemeColor);
            terminal.Write($"  [{i + 1}] ");
            terminal.SetColor("white");
            terminal.Write($"{discovery.Name}");
            terminal.SetColor("gray");
            terminal.WriteLine($" ({region.Name})");
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine("[0] Return");
        terminal.WriteLine("");

        var choice = await terminal.GetInput("Visit: ");

        if (int.TryParse(choice, out int idx) && idx >= 1 && idx <= allDiscoveries.Count)
        {
            var (region, discovery) = allDiscoveries[idx - 1];

            // Visiting a discovery costs a revisit (separate from expeditions)
            if (currentPlayer.WildernessRevisitsToday >= GameConfig.WildernessMaxDailyRevisits)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("You've already revisited enough discoveries for today.");
                await Task.Delay(2000);
                return;
            }

            currentPlayer.WildernessRevisitsToday++;

            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"═══ {discovery.Name} ═══");
            terminal.SetColor("gray");
            terminal.WriteLine(discovery.Description);
            terminal.WriteLine("");
            await Task.Delay(1500);

            // Run encounter based on discovery type
            switch (discovery.EncounterType)
            {
                case "combat":
                    await CombatEncounter(region);
                    break;
                case "shrine":
                    await ShrineEncounter(region);
                    break;
                case "ruins":
                    await RuinsEncounter(region);
                    break;
                case "traveler":
                    await TravelerEncounter(region);
                    break;
            }
        }
    }
}
