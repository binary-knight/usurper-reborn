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

    protected override void DisplayLocation()
    {
        if (IsBBSSession) { DisplayLocationBBS(); return; }

        terminal.ClearScreen();

        WriteBoxHeader(Loc.Get("wilderness.header"), "bright_green");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("wilderness.intro_line1"));
        terminal.WriteLine(Loc.Get("wilderness.intro_line2"));
        terminal.WriteLine("");

        int remaining = GameConfig.WildernessMaxDailyExplorations - currentPlayer.WildernessExplorationsToday;
        terminal.SetColor(remaining > 0 ? "bright_yellow" : "red");
        terminal.WriteLine(Loc.Get("wilderness.expeditions_remaining", remaining, GameConfig.WildernessMaxDailyExplorations));
        terminal.WriteLine("");

        // Show regions
        foreach (var region in WildernessData.Regions)
        {
            bool canAccess = currentPlayer.Level >= region.MinLevel;
            terminal.SetColor(canAccess ? region.ThemeColor : "darkgray");
            string levelReq = region.MinLevel > 1 ? Loc.Get("wilderness.level_req", region.MinLevel) : Loc.Get("wilderness.any_level");
            string lockIcon = canAccess ? "" : (IsScreenReader ? Loc.Get("wilderness.locked") : Loc.Get("wilderness.locked_bracket"));
            terminal.WriteLine(IsScreenReader
                ? $"  {region.DirectionKey}. {region.Name,-24} - {region.Direction}{levelReq}{lockIcon}"
                : $"  [{region.DirectionKey}] {region.Name,-24} - {region.Direction}{levelReq}{lockIcon}");
        }

        terminal.WriteLine("");

        // Discoveries
        int discoveryCount = currentPlayer.WildernessDiscoveries.Count;
        if (discoveryCount > 0)
        {
            int revisitsLeft = GameConfig.WildernessMaxDailyRevisits - currentPlayer.WildernessRevisitsToday;
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(IsScreenReader
                ? Loc.Get("wilderness.discoveries_sr", discoveryCount, revisitsLeft)
                : Loc.Get("wilderness.discoveries_visual", discoveryCount, revisitsLeft));
        }

        terminal.SetColor("gray");
        terminal.WriteLine(IsScreenReader ? Loc.Get("wilderness.return_sr") : Loc.Get("wilderness.return_visual"));
        terminal.WriteLine("");

        ShowStatusLine();
    }

    private void DisplayLocationBBS()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("wilderness.bbs_header"));
        terminal.WriteLine("");

        int remaining = GameConfig.WildernessMaxDailyExplorations - currentPlayer.WildernessExplorationsToday;
        terminal.SetColor(remaining > 0 ? "bright_yellow" : "red");
        terminal.WriteLine(Loc.Get("wilderness.bbs_expeditions", remaining, GameConfig.WildernessMaxDailyExplorations));

        foreach (var region in WildernessData.Regions)
        {
            bool canAccess = currentPlayer.Level >= region.MinLevel;
            terminal.SetColor(canAccess ? "white" : "darkgray");
            terminal.WriteLine($"[{region.DirectionKey}] {region.Name} {(canAccess ? "" : "[LOCKED]")}");
        }

        if (currentPlayer.WildernessDiscoveries.Count > 0)
        {
            int revisitsLeft = GameConfig.WildernessMaxDailyRevisits - currentPlayer.WildernessRevisitsToday;
            terminal.WriteLine(Loc.Get("wilderness.bbs_discoveries", currentPlayer.WildernessDiscoveries.Count, revisitsLeft));
        }
        terminal.WriteLine(Loc.Get("wilderness.bbs_return"));
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
                terminal.WriteLine(Loc.Get("wilderness.return_city"), "gray");
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
            terminal.WriteLine(Loc.Get("wilderness.region_too_dangerous", region.Name, region.MinLevel));
            await Task.Delay(2000);
            return;
        }

        // Daily limit check
        if (currentPlayer.WildernessExplorationsToday >= GameConfig.WildernessMaxDailyExplorations)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("wilderness.too_tired"));
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
        if (IsScreenReader)
            terminal.WriteLine(region.Name);
        else
            terminal.WriteLine($"═══ {region.Name} ═══");
        terminal.WriteLine("");
        terminal.SetColor("white");
        foreach (var line in region.Description.Split('\n'))
            terminal.WriteLine(line);
        terminal.WriteLine("");
        await Task.Delay(2000);

        // Roll encounter type: 40% combat, 25% foraging, 15% ruins, 10% traveler, 10% shrine
        int roll = Random.Shared.Next(100);
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
        if (Random.Shared.Next(100) < 10)
            await CheckForDiscovery(region);
    }

    // ═══════════════════════════════════════════════════════════════
    // ENCOUNTER TYPES
    // ═══════════════════════════════════════════════════════════════

    private async Task CombatEncounter(WildernessRegion region)
    {
        string monsterName = region.MonsterNames[Random.Shared.Next(region.MonsterNames.Length)];

        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("wilderness.monster_emerges", monsterName, region.Name.ToLower()));
        terminal.WriteLine("");
        await Task.Delay(1500);

        // Generate monster scaled to player level (capped by region difficulty)
        int monsterLevel = Math.Max(region.MinLevel, currentPlayer.Level - 2 + Random.Shared.Next(5));
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
            long bonusGold = (long)(Random.Shared.Next(10, 30) * (1 + region.MinLevel / 10.0));
            currentPlayer.Gold += bonusGold;
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("wilderness.bonus_gold", bonusGold));
        }

        await terminal.PressAnyKey();
    }

    private async Task ForagingEncounter(WildernessRegion region)
    {
        var result = region.ForagingResults[Random.Shared.Next(region.ForagingResults.Length)];

        terminal.SetColor("green");
        terminal.WriteLine(Loc.Get("wilderness.search_area"));
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
                    terminal.WriteLine(Loc.Get("wilderness.found_healing_herb"));
                }
                else
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("wilderness.herb_pouch_full"));
                }
                break;
            case "herb_ironbark":
                if (currentPlayer.HerbIronbark < GameConfig.HerbMaxCarry[(int)HerbType.IronbarkRoot])
                {
                    currentPlayer.HerbIronbark++;
                    terminal.SetColor("green");
                    terminal.WriteLine(Loc.Get("wilderness.found_ironbark"));
                }
                else
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("wilderness.herb_pouch_full"));
                }
                break;
            case "herb_firebloom":
                if (currentPlayer.HerbFirebloom < GameConfig.HerbMaxCarry[(int)HerbType.FirebloomPetal])
                {
                    currentPlayer.HerbFirebloom++;
                    terminal.SetColor("green");
                    terminal.WriteLine(Loc.Get("wilderness.found_firebloom"));
                }
                else
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("wilderness.herb_pouch_full"));
                }
                break;
            case "herb_starbloom":
                if (currentPlayer.HerbStarbloom < GameConfig.HerbMaxCarry[(int)HerbType.StarbloomEssence])
                {
                    currentPlayer.HerbStarbloom++;
                    terminal.SetColor("green");
                    terminal.WriteLine(Loc.Get("wilderness.found_starbloom"));
                }
                else
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("wilderness.herb_pouch_full"));
                }
                break;
            case "herb_swift":
                if (currentPlayer.HerbSwiftthistle < GameConfig.HerbMaxCarry[(int)HerbType.Swiftthistle])
                {
                    currentPlayer.HerbSwiftthistle++;
                    terminal.SetColor("green");
                    terminal.WriteLine(Loc.Get("wilderness.found_swiftthistle"));
                }
                else
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("wilderness.herb_pouch_full"));
                }
                break;
            case "heal_small":
                long healSmall = Math.Min(currentPlayer.MaxHP / 10, currentPlayer.MaxHP - currentPlayer.HP);
                if (healSmall > 0)
                {
                    currentPlayer.HP += healSmall;
                    terminal.SetColor("green");
                    terminal.WriteLine(Loc.Get("wilderness.eat_foraged_food", healSmall));
                }
                else
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("wilderness.already_healthy"));
                }
                break;
            case "heal_medium":
                long healMed = Math.Min(currentPlayer.MaxHP / 5, currentPlayer.MaxHP - currentPlayer.HP);
                if (healMed > 0)
                {
                    currentPlayer.HP += healMed;
                    terminal.SetColor("green");
                    terminal.WriteLine(Loc.Get("wilderness.medicinal_plant", healMed));
                }
                break;
            case "gold_small":
                long goldS = 20 * levelScale + Random.Shared.Next(20);
                currentPlayer.Gold += goldS;
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("wilderness.worth_gold", goldS));
                break;
            case "gold_medium":
                long goldM = 50 * levelScale + Random.Shared.Next(50);
                currentPlayer.Gold += goldM;
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("wilderness.worth_gold", goldM));
                break;
            case "gold_large":
                long goldL = 100 * levelScale + Random.Shared.Next(100);
                currentPlayer.Gold += goldL;
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("wilderness.worth_gold", goldL));
                break;
            case "nothing":
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("wilderness.better_luck"));
                break;
        }
    }

    private async Task RuinsEncounter(WildernessRegion region)
    {
        string ruins = region.RuinsEncounters[Random.Shared.Next(region.RuinsEncounters.Length)];

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("wilderness.discover_ruins"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine(ruins);
        terminal.WriteLine("");

        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("wilderness.ruins_search_or_leave"));
        terminal.WriteLine("");

        var choice = await GetChoice();

        if (choice.ToUpper() == "S")
        {
            // 60% treasure, 20% trap, 20% nothing
            int roll = Random.Shared.Next(100);
            if (roll < 60)
            {
                long gold = 30 + (long)(currentPlayer.Level * 3) + Random.Shared.Next(50);
                currentPlayer.Gold += gold;
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("wilderness.ruins_gold_found", gold));

                // Small chance of a healing potion
                if (Random.Shared.Next(100) < 30)
                {
                    currentPlayer.Healing = Math.Min(currentPlayer.Healing + 1, currentPlayer.MaxPotions);
                    terminal.SetColor("green");
                    terminal.WriteLine(Loc.Get("wilderness.ruins_potion_found"));
                }
            }
            else if (roll < 80)
            {
                long damage = Math.Max(1, currentPlayer.MaxHP / 10);
                currentPlayer.HP = Math.Max(1, currentPlayer.HP - damage);
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("wilderness.ruins_trap", damage));
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("wilderness.ruins_picked_clean"));
            }
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("wilderness.ruins_leave"));
        }

        await terminal.PressAnyKey();
    }

    private async Task TravelerEncounter(WildernessRegion region)
    {
        var traveler = region.TravelerEncounters[Random.Shared.Next(region.TravelerEncounters.Length)];

        terminal.SetColor("cyan");
        terminal.WriteLine(traveler.text);
        terminal.WriteLine("");

        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("wilderness.traveler_talk_or_leave"));
        terminal.WriteLine("");

        var choice = await GetChoice();

        if (choice.ToUpper() == "T")
        {
            // Travelers offer random benefits
            int roll = Random.Shared.Next(100);
            if (roll < 40)
            {
                // Trade offer
                long cost = 20 + Random.Shared.Next(30);
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("wilderness.traveler_sell_potion", traveler.name, cost));
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("wilderness.traveler_buy_or_decline"));
                var buy = await GetChoice();
                if (buy.ToUpper() == "Y" && currentPlayer.Gold >= cost)
                {
                    currentPlayer.Gold -= cost;
                    currentPlayer.Healing = Math.Min(currentPlayer.Healing + 1, currentPlayer.MaxPotions);
                    terminal.SetColor("green");
                    terminal.WriteLine(Loc.Get("wilderness.traveler_purchased"));
                }
                else if (buy.ToUpper() == "Y")
                {
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("ui.not_enough_gold_plain"));
                }
            }
            else if (roll < 70)
            {
                // Lore / hint
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("wilderness.traveler_shares_wisdom", traveler.name));
                terminal.SetColor("cyan");
                terminal.WriteLine(Loc.Get("wilderness.traveler_lore_line1"));
                terminal.WriteLine(Loc.Get("wilderness.traveler_lore_line2"));
            }
            else
            {
                // Small healing
                long heal = currentPlayer.MaxHP / 8;
                currentPlayer.HP = Math.Min(currentPlayer.MaxHP, currentPlayer.HP + heal);
                terminal.SetColor("green");
                terminal.WriteLine(Loc.Get("wilderness.traveler_shares_food", traveler.name, heal));
            }
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("wilderness.traveler_nod_leave"));
        }

        await terminal.PressAnyKey();
    }

    private async Task ShrineEncounter(WildernessRegion region)
    {
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("wilderness.shrine_discover"));
        terminal.WriteLine(Loc.Get("wilderness.shrine_symbols"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("wilderness.shrine_pray_or_leave"));
        terminal.WriteLine("");

        var choice = await GetChoice();

        if (choice.ToUpper() == "P")
        {
            int roll = Random.Shared.Next(100);
            if (roll < 30)
            {
                // Heal
                long heal = currentPlayer.MaxHP / 4;
                currentPlayer.HP = Math.Min(currentPlayer.MaxHP, currentPlayer.HP + heal);
                long mana = currentPlayer.MaxMana / 4;
                currentPlayer.Mana = Math.Min(currentPlayer.MaxMana, currentPlayer.Mana + mana);
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("wilderness.shrine_warm_light"));
                if (heal > 0) terminal.WriteLine(Loc.Get("wilderness.shrine_hp", heal));
                if (mana > 0) terminal.WriteLine(Loc.Get("wilderness.shrine_mana", mana));
            }
            else if (roll < 55)
            {
                // Small stat buff
                int stat = Random.Shared.Next(3);
                terminal.SetColor("bright_cyan");
                if (stat == 0)
                {
                    currentPlayer.Strength += 1;
                    terminal.WriteLine(Loc.Get("wilderness.shrine_str"));
                }
                else if (stat == 1)
                {
                    currentPlayer.Dexterity += 1;
                    terminal.WriteLine(Loc.Get("wilderness.shrine_dex"));
                }
                else
                {
                    currentPlayer.Wisdom += 1;
                    terminal.WriteLine(Loc.Get("wilderness.shrine_wis"));
                }
            }
            else if (roll < 75)
            {
                // XP
                long xp = 10 + currentPlayer.Level * 5;
                currentPlayer.Experience += xp;
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("wilderness.shrine_xp", xp));
            }
            else
            {
                // Nothing special
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("wilderness.shrine_peace"));
            }
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("wilderness.shrine_pass"));
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

        var discovery = undiscovered[Random.Shared.Next(undiscovered.Length)];
        currentPlayer.WildernessDiscoveries.Add(discovery.Id);

        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("wilderness.discovery_star"));
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("wilderness.discovery_found", discovery.Name));
        terminal.SetColor("gray");
        terminal.WriteLine(discovery.Description);
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("wilderness.discovery_revisit_hint"));

        NewsSystem.Instance?.Newsy($"☆ {currentPlayer.Name} discovered {discovery.Name} in the {region.Name}!");

        await Task.Delay(3000);
    }

    private async Task ShowDiscoveries()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        if (IsScreenReader)
            terminal.WriteLine(Loc.Get("wilderness.discoveries_title"));
        else
            terminal.WriteLine(Loc.Get("wilderness.discoveries_title_visual"));
        terminal.WriteLine("");

        if (currentPlayer.WildernessDiscoveries.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("wilderness.no_discoveries"));
            terminal.WriteLine(Loc.Get("wilderness.no_discoveries_hint"));
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
            terminal.Write(IsScreenReader ? $"  {i + 1}. " : $"  [{i + 1}] ");
            terminal.SetColor("white");
            terminal.Write($"{discovery.Name}");
            terminal.SetColor("gray");
            terminal.WriteLine($" ({region.Name})");
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(IsScreenReader ? Loc.Get("wilderness.discoveries_return_sr") : Loc.Get("wilderness.discoveries_return_visual"));
        terminal.WriteLine("");

        var choice = await terminal.GetInput(Loc.Get("wilderness.discoveries_visit_prompt"));

        if (int.TryParse(choice, out int idx) && idx >= 1 && idx <= allDiscoveries.Count)
        {
            var (region, discovery) = allDiscoveries[idx - 1];

            // Visiting a discovery costs a revisit (separate from expeditions)
            if (currentPlayer.WildernessRevisitsToday >= GameConfig.WildernessMaxDailyRevisits)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("wilderness.revisits_exhausted"));
                await Task.Delay(2000);
                return;
            }

            currentPlayer.WildernessRevisitsToday++;

            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            if (IsScreenReader)
                terminal.WriteLine(discovery.Name);
            else
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
