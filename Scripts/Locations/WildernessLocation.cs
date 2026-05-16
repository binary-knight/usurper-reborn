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

        // Phase 5: Electron mode emits Wilderness menu state. Pattern B.
        if (GameConfig.ElectronMode)
        {
            EmitElectronEvents();
            return;
        }

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

        terminal.WriteLine("");

        // v0.61.0: Druid's Shrines pilgrimage entry. Outside the expedition flow so
        // a shrine visit doesn't consume an expedition slot. Was originally only
        // wired into the BBS code path; restored to the visual path here so SSH /
        // web / Electron sessions can see and pick it.
        terminal.SetColor("bright_magenta");
        if (currentPlayer.HasActiveShrineAttunement)
        {
            var shrine = UsurperRemake.Data.DruidShrineData.GetById(currentPlayer.AttunedShrineId);
            string shrineName = shrine?.Name ?? currentPlayer.AttunedShrineId;
            // v0.61.3: GetShrineTimeRemainingLabel returns "12.5h" online or
            // "2 days" / "1 day" / "today" in single-player so the same
            // template renders the right unit per game mode.
            terminal.WriteLine(Loc.Get(
                IsScreenReader ? "wilderness.pilgrimage_active_sr" : "wilderness.pilgrimage_active_visual",
                shrineName, currentPlayer.GetShrineTimeRemainingLabel()));
        }
        else
        {
            terminal.WriteLine(Loc.Get(
                IsScreenReader ? "wilderness.pilgrimage_sr" : "wilderness.pilgrimage_visual"));
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
        // v0.61.0: Druid's Shrines pilgrimage entry. Available outside the expedition
        // flow so a shrine visit doesn't consume an expedition slot.
        terminal.SetColor("bright_magenta");
        if (currentPlayer.HasActiveShrineAttunement)
        {
            var shrine = UsurperRemake.Data.DruidShrineData.GetById(currentPlayer.AttunedShrineId);
            terminal.WriteLine(Loc.Get("wilderness.bbs_pilgrimage_active",
                shrine?.Name ?? currentPlayer.AttunedShrineId,
                currentPlayer.GetShrineTimeRemainingLabel()));
        }
        else
        {
            terminal.WriteLine(Loc.Get("wilderness.bbs_pilgrimage"));
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

            case "P":
                await ShowPilgrimageMenu();
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

        // v0.61.0 Beast Taming: separate, additive chance to encounter a tameable beast
        // after the main encounter resolves. Region- and level-gated; beasts already in
        // the player's roster are excluded so each tame is a new addition.
        if (Random.Shared.Next(100) < UsurperRemake.Data.BeastData.BeastEncounterChancePercent)
            await TryBeastEncounter(region);
    }

    /// <summary>
    /// v0.61.0 Beast Taming. Rolled at ~8% per expedition after the normal encounter.
    /// Picks one region-eligible beast the player doesn't already own, presents an
    /// encounter flavor block, gives the player 3 skill-check attempts (CHA + DEX vs
    /// beast difficulty). Success = added to permanent roster. Failure on all 3 = beast
    /// flees and despawns for this run (try again next expedition).
    /// </summary>
    private async Task TryBeastEncounter(WildernessRegion region)
    {
        // Pool: region-matching, level-gated, not already in roster.
        var ownedIds = currentPlayer.PetRoster?.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase)
                       ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var beast = UsurperRemake.Data.BeastData.PickEligibleBeast(
            region.DirectionKey, (int)currentPlayer.Level, Random.Shared, ownedIds);
        if (beast == null) return; // Nothing in this region matches player level / not already owned.

        // Roster full? Show a flavor "saw a beast but you're at capacity" line.
        if (currentPlayer.PetRoster.Count >= UsurperRemake.Data.BeastData.MaxRosterSize)
        {
            terminal.WriteLine("");
            terminal.SetColor("dark_gray");
            terminal.WriteLine(Loc.Get("wilderness.beast_roster_full", beast.Name));
            await terminal.PressAnyKey();
            return;
        }

        // Encounter flavor.
        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("wilderness.beast_encounter_header", beast.Name, beast.Species));
        terminal.SetColor("white");
        foreach (var line in beast.EncounterFlavor.Split('\n'))
            terminal.WriteLine($"  {line}");
        terminal.WriteLine("");
        terminal.SetColor("dark_gray");
        terminal.WriteLine($"  {beast.PassiveDescription}");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("wilderness.beast_tame_prompt"));
        terminal.WriteLine("");
        var choice = await GetChoice();
        if (choice.ToUpper() != "T")
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("wilderness.beast_walk_away", beast.Name));
            await terminal.PressAnyKey();
            return;
        }

        // 3 attempts. Each attempt is (d20 + CHA/4 + DEX/8 + favor-bonus) vs TameDifficulty.
        // CHA contributes more than DEX (taming is mostly a charisma act); DEX matters
        // for handling the creature without spooking it.
        int chaBonus = (int)(currentPlayer.GetEffectiveCharisma() / 4);
        int dexBonus = (int)(currentPlayer.Dexterity / 8);
        for (int attempt = 1; attempt <= UsurperRemake.Data.BeastData.TameAttempts; attempt++)
        {
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("wilderness.beast_attempt_header", attempt, UsurperRemake.Data.BeastData.TameAttempts));
            terminal.SetColor("dark_gray");
            terminal.WriteLine(Loc.Get("wilderness.beast_attempt_calc", chaBonus, dexBonus, beast.TameDifficulty));
            await Task.Delay(1200);

            int roll = Random.Shared.Next(1, 21); // d20
            int total = roll + chaBonus + dexBonus;
            bool success = total >= beast.TameDifficulty;

            terminal.SetColor(success ? "bright_green" : "yellow");
            terminal.WriteLine(Loc.Get("wilderness.beast_attempt_roll", roll, total, beast.TameDifficulty,
                success ? Loc.Get("wilderness.beast_attempt_pass") : Loc.Get("wilderness.beast_attempt_fail")));

            if (success)
            {
                // Tame succeeds. Add to roster.
                var newPet = new UsurperRemake.Data.Pet
                {
                    Id = beast.Id,
                    Name = beast.Name,
                    TamedAtUtc = DateTime.UtcNow,
                    Level = 1,
                    Experience = 0
                };
                currentPlayer.PetRoster.Add(newPet);
                if (string.IsNullOrEmpty(currentPlayer.ActivePetId))
                    currentPlayer.ActivePetId = newPet.Id; // Auto-activate first tame.

                terminal.WriteLine("");
                terminal.SetColor("bright_green");
                foreach (var line in beast.TameSuccessFlavor.Split('\n'))
                    terminal.WriteLine($"  {line}");
                terminal.WriteLine("");
                terminal.SetColor("bright_cyan");
                terminal.WriteLine(Loc.Get("wilderness.beast_tame_success", beast.Name, currentPlayer.PetRoster.Count, UsurperRemake.Data.BeastData.MaxRosterSize));
                if (string.Equals(currentPlayer.ActivePetId, beast.Id, StringComparison.OrdinalIgnoreCase))
                    terminal.WriteLine(Loc.Get("wilderness.beast_auto_active"));
                else
                    terminal.WriteLine(Loc.Get("wilderness.beast_switch_at_home"));

                await terminal.PressAnyKey();
                return;
            }
        }

        // All 3 attempts failed.
        terminal.WriteLine("");
        terminal.SetColor("dark_red");
        terminal.WriteLine(Loc.Get("wilderness.beast_flees", beast.Name));
        await terminal.PressAnyKey();
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

        // v0.61.2 (player report: "Feral Cat takes flight, becoming harder to hit!"):
        // MonsterGenerator picked a random dungeon family/tier with that level's stats,
        // and the line above only overwrote Name -- the underlying abilities (e.g. Angel's
        // Flight), family, MonsterClass, color, and combat dialogue all leaked through.
        // Rewrite the family-specific fields so the creature actually behaves like its
        // name implies. Names not in the lookup default to a plain Beast.
        var profile = WildernessData.GetMonsterProfile(monsterName);
        monster.FamilyName = profile.Family;
        monster.TierName = monsterName;
        monster.AttackType = profile.AttackType;
        monster.MonsterColor = profile.Color;
        monster.CanSpeak = profile.CanSpeak;
        monster.MonsterClass = profile.MonsterClass;
        monster.Undead = profile.MonsterClass == MonsterClass.Undead ? 1 : 0;
        monster.SpecialAbilities.Clear();
        foreach (var ability in profile.Abilities)
            monster.SpecialAbilities.Add(ability);

        // Player report (Lv.30 Elf Sage, post-v0.61.2 deploy): "I fought a black
        // bear crackling with elemental fury and a bandit scout breathing fire."
        // The v0.61.2 fix overrode family / class / abilities / etc. but missed
        // monster.Phrase, which MonsterGenerator had set from the randomly-picked
        // family's intro flavor (Elemental: "crackles with raw elemental fury";
        // Draconic Drake: "roars and breathes a gout of fire!"). Reset Phrase
        // to a family-appropriate default that matches the wilderness family
        // override, mirroring MonsterGenerator.GetMonsterPhrase's defaults.
        monster.Phrase = profile.Family switch
        {
            "Beast" => "snarls and growls menacingly.",
            "Humanoid" => "draws steel and prepares to fight.",
            "Undead" => "The living shall join the dead...",
            "Insectoid" => "clicks and hisses aggressively.",
            "Construct" => "grinds to life with a mechanical whir.",
            "Elemental" => "crackles with raw elemental fury.",
            "Aquatic" => "lets out a deep, gurgling roar.",
            "Draconic" => "roars menacingly!",
            "Plant" => "rustles ominously, branches reaching toward you.",
            "Fey" => "Let's play a game...",
            "Giant" => "Fe Fi Fo Fum!",
            _ => "" // unknown family -> silent intro
        };

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

    /// <summary>
    /// v0.61.0 Druid's Shrines. Pilgrimage menu: list all five shrines and let the
    /// player attune to one. A 24-hour timer enforces the daily cap -- re-attuning
    /// before expiry just replaces the active buff with the new one (a real choice).
    /// Per-shrine favor increments each visit; milestones unlock future encounters.
    /// </summary>
    private async Task ShowPilgrimageMenu()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine(Loc.Get("wilderness.pilgrimage_header"));
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("wilderness.pilgrimage_intro"));
        terminal.WriteLine("");

        if (currentPlayer.HasActiveShrineAttunement)
        {
            var active = UsurperRemake.Data.DruidShrineData.GetById(currentPlayer.AttunedShrineId);
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("wilderness.pilgrimage_active",
                active?.Name ?? currentPlayer.AttunedShrineId,
                currentPlayer.GetShrineTimeRemainingLabel()));
            terminal.SetColor("dark_gray");
            terminal.WriteLine($"  ({active?.PassiveSummary ?? ""})");
            terminal.WriteLine("");
        }

        var shrines = UsurperRemake.Data.DruidShrineData.Shrines;
        for (int i = 0; i < shrines.Length; i++)
        {
            var s = shrines[i];
            int favor = currentPlayer.ShrineFavor.TryGetValue(s.Id, out var f) ? f : 0;
            bool isActive = string.Equals(currentPlayer.AttunedShrineId, s.Id, StringComparison.OrdinalIgnoreCase)
                            && currentPlayer.HasActiveShrineAttunement;
            terminal.SetColor("gray");
            terminal.Write($"  [{i + 1}] ");
            terminal.SetColor(isActive ? "bright_green" : "white");
            terminal.Write($"{s.Name,-38}");
            terminal.SetColor("dark_gray");
            terminal.WriteLine($"  favor: {favor}");
            terminal.SetColor("cyan");
            terminal.WriteLine($"        {s.PassiveSummary}");
        }
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(IsScreenReader ? "  0. Cancel" : "  [0] Cancel");
        terminal.WriteLine("");

        var input = await terminal.GetInput(Loc.Get("wilderness.pilgrimage_select"));
        if (!int.TryParse(input, out int choice) || choice < 1 || choice > shrines.Length)
            return;

        var selected = shrines[choice - 1];

        // Confirm
        terminal.WriteLine("");
        terminal.SetColor("bright_magenta");
        terminal.WriteLine($"  {selected.Name}");
        terminal.SetColor("dark_gray");
        foreach (var line in selected.FlavorDescription.Split('\n'))
            terminal.WriteLine($"  {line}");
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine($"  {selected.PassiveSummary}");
        if (selected.ChivalryShift != 0)
        {
            terminal.SetColor(selected.ChivalryShift > 0 ? "bright_yellow" : "dark_red");
            terminal.WriteLine($"  {Loc.Get("wilderness.pilgrimage_alignment", selected.ChivalryShift > 0 ? $"+{selected.ChivalryShift}" : selected.ChivalryShift.ToString())}");
        }
        terminal.WriteLine("");
        var confirm = await terminal.GetInput(Loc.Get("wilderness.pilgrimage_confirm"));
        if (confirm?.ToUpper() != "Y")
            return;

        // Apply attunement. Two timers set so the right one fires per game mode:
        //   * Online: real-time `AttunedShrineExpiresUtc`. Server runs 24/7 so
        //     wall-clock 24h is the natural reference.
        //   * Single-player: `AttunedShrineExpiresGameDay`. Player report (v0.61.2,
        //     Lv.8 Elf Sage): "buff lasts for real time 24 hours, not in game
        //     time. Is that intentional?" No. Single-player time advances via
        //     sleep / [Z] Wait / dungeon descent, not wall-clock, so the buff
        //     now expires `AttunementHours/24` game days after attunement (so
        //     24h => 1 game day; if AttunementHours rises, the math still
        //     rounds up to whole game days, minimum 1).
        currentPlayer.AttunedShrineId = selected.Id;
        currentPlayer.AttunedShrineExpiresUtc = DateTime.UtcNow.AddHours(UsurperRemake.Data.DruidShrineData.AttunementHours);
        int gameDaysToAdd = System.Math.Max(1, (int)System.Math.Ceiling(UsurperRemake.Data.DruidShrineData.AttunementHours / 24.0));
        int currentGameDay = UsurperRemake.Systems.StoryProgressionSystem.Instance?.CurrentGameDay ?? 1;
        currentPlayer.AttunedShrineExpiresGameDay = currentGameDay + gameDaysToAdd;
        if (!currentPlayer.ShrineFavor.ContainsKey(selected.Id))
            currentPlayer.ShrineFavor[selected.Id] = 0;
        currentPlayer.ShrineFavor[selected.Id]++;

        // Alignment shift via paired-movement system.
        if (selected.ChivalryShift != 0)
        {
            UsurperRemake.Systems.AlignmentSystem.Instance.ChangeAlignment(
                currentPlayer, Math.Abs(selected.ChivalryShift), selected.ChivalryShift > 0,
                $"Pilgrimage to {selected.Name}");
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        foreach (var line in selected.AttunementDescription.Split('\n'))
            terminal.WriteLine($"  {line}");
        terminal.WriteLine("");
        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("wilderness.pilgrimage_attuned",
            selected.Name, UsurperRemake.Data.DruidShrineData.AttunementHours));

        // Milestone check -- crossing 10 visits earns a one-time tangible gift from
        // that god. 25 and 50 visit milestones still print the favor-recognition line
        // but their rewards land in a future polish pass.
        int newFavor = currentPlayer.ShrineFavor[selected.Id];
        if (UsurperRemake.Data.DruidShrineData.FavorMilestones.Contains(newFavor))
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("wilderness.pilgrimage_milestone", newFavor, selected.GodPatron));
            if (newFavor == 10)
                await ApplyShrineMilestone10Reward(selected);
        }

        await terminal.PressAnyKey();
    }

    /// <summary>
    /// v0.61.1: tangible reward when the player crosses 10 visits to a shrine. Each
    /// god gives a permanent stat lift themed to their domain. Bumps BaseMaxHP /
    /// BaseStat fields directly so RecalculateStats() preserves them across loads.
    /// </summary>
    private async Task ApplyShrineMilestone10Reward(UsurperRemake.Data.DruidShrineData.DruidShrine shrine)
    {
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        switch (shrine.Id)
        {
            case "terravok":
                currentPlayer.BaseMaxHP += 25;
                currentPlayer.MaxHP += 25;
                currentPlayer.HP += 25;
                terminal.WriteLine(Loc.Get("wilderness.milestone_terravok_gift"));
                break;
            case "maelketh":
                currentPlayer.BaseStrength += 3;
                terminal.WriteLine(Loc.Get("wilderness.milestone_maelketh_gift"));
                break;
            case "noctura":
                currentPlayer.BaseDexterity += 3;
                terminal.WriteLine(Loc.Get("wilderness.milestone_noctura_gift"));
                break;
            case "aurelion":
                currentPlayer.BaseWisdom += 5;
                terminal.WriteLine(Loc.Get("wilderness.milestone_aurelion_gift"));
                break;
            case "veloura":
                currentPlayer.BaseCharisma += 5;
                terminal.WriteLine(Loc.Get("wilderness.milestone_veloura_gift"));
                break;
        }
        currentPlayer.RecalculateStats();
        await Task.Delay(2000);
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

    /// <summary>
    /// Phase 5: emit Wilderness region picker for the Electron client. Pattern B.
    /// </summary>
    private void EmitElectronEvents()
    {
        var player = GetCurrentPlayer();
        if (player == null) return;

        ElectronBridge.EmitLocation(
            name: Loc.Get("wilderness.header"),
            description: Loc.Get("wilderness.intro_line1"),
            timeOfDay: "");

        bool isManaClass = player is Player p && p.IsManaClass;
        ElectronBridge.EmitStats(
            hp: player.HP, maxHp: player.MaxHP,
            mana: isManaClass ? player.Mana : 0, maxMana: isManaClass ? player.MaxMana : 0,
            stamina: isManaClass ? 0 : player.Stamina, maxStamina: isManaClass ? 0 : player.BaseStamina,
            gold: player.Gold, level: player.Level,
            className: player.ClassName, raceName: player.Race.ToString(),
            playerName: player.DisplayName);

        var menu = new List<ElectronBridge.MenuItemData>();
        foreach (var region in WildernessData.Regions)
        {
            bool canAccess = player.Level >= region.MinLevel;
            menu.Add(new() {
                Key = region.DirectionKey,
                Label = $"{region.Name} ({region.Direction})" + (canAccess ? "" : $" [Lv.{region.MinLevel}]"),
                Category = canAccess ? "explore" : "locked",
                Icon = "wilderness"
            });
        }
        menu.Add(new() { Key = "D", Label = "Visit Discovery", Category = "explore", Icon = "discovery" });
        menu.Add(new() { Key = "R", Label = Loc.Get("ui.return"), Category = "navigate", Icon = "back" });
        ElectronBridge.EmitMenu(menu);
    }
}
