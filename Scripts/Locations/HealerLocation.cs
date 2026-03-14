using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UsurperRemake.Systems;
using UsurperRemake.BBS;

/// <summary>
/// The Golden Bow, Healing Hut - run by Jadu The Fat
/// Based on HEALERC.PAS - provides HP restoration, potion sales, disease healing,
/// poison curing, and cursed item removal services
/// </summary>
public class HealerLocation : BaseLocation
{
    private const string HealerName = "The Golden Bow, Healing Hut";
    private const string Manager = "Jadu";

    // Disease BASE costs (scaled with diminishing formula to prevent endgame punishment)
    // Formula: baseCost * (1 + level^0.6) - this gives reasonable scaling:
    // Level 1: ~2x base, Level 50: ~13x base, Level 100: ~17x base
    private const int BlindnessBaseCost = 1500;
    private const int PlagueBaseCost = 2000;
    private const int SmallpoxBaseCost = 2500;
    private const int MeaslesBaseCost = 3000;
    private const int LeprosyBaseCost = 3500;
    private const int LoversBaneBaseCost = 1000;  // STD from Love Street
    private const int CursedItemBaseCost = 500;
    private const int PoisonBaseCost = 200;

    // Healing costs
    private const int HealingPotionCost = 50;      // Cost per potion
    private const int FullHealCostPerHP = 2;       // Cost per HP restored

    /// <summary>
    /// Calculate disease/ailment cure cost with diminishing scaling
    /// Uses level^0.6 curve instead of linear to prevent endgame punishment
    /// </summary>
    private long CalculateDiseaseCost(int baseCost, int playerLevel)
    {
        // Formula: baseCost * (1 + level^0.6)
        // Level 1: ~2x, Level 50: ~13x, Level 100: ~17x
        return (long)(baseCost * (1 + Math.Pow(playerLevel, 0.6)));
    }

    /// <summary>
    /// Get adjusted price with alignment, world event, and faction modifiers
    /// </summary>
    private long GetAdjustedPrice(long basePrice, Character player)
    {
        var alignmentModifier = AlignmentSystem.Instance.GetPriceModifier(player, isShadyShop: false);
        var worldEventModifier = WorldEventSystem.Instance.GlobalPriceModifier;
        // Apply faction discount (The Faith gets 25% off at healers)
        var factionModifier = UsurperRemake.Systems.FactionSystem.Instance.GetHealingPriceModifier();
        return (long)(basePrice * alignmentModifier * worldEventModifier * factionModifier * DifficultySystem.GetShopPriceMultiplier());
    }

    public HealerLocation() : base(
        GameLocation.Healer,
        "The Golden Bow",
        "You enter the healing hut. The smell of herbs and incense fills the air."
    ) { }

    protected override void SetupLocation()
    {
        PossibleExits = new List<GameLocation>
        {
            GameLocation.MainStreet
        };
    }

    protected override void DisplayLocation()
    {
        terminal.ClearScreen();

        if (IsScreenReader)
        {
            DisplayLocationSR();
            return;
        }

        if (IsBBSSession)
        {
            DisplayLocationBBS();
            return;
        }

        WriteBoxHeader(Loc.Get("healer.header"), "bright_cyan", 77);
        terminal.WriteLine("");

        ShowShopkeeperMood(Manager,
            Loc.Get("healer.shopkeeper_mood", Manager));
        terminal.WriteLine("");

        ShowNPCsInLocation();

        // Mira companion teaser — level 5+ (v0.49.6)
        if (GetCurrentPlayer() is { } miraCheck && miraCheck.Level >= 5
            && CompanionSystem.Instance != null
            && !(CompanionSystem.Instance.GetCompanion(CompanionId.Mira)?.IsRecruited ?? true)
            && !(CompanionSystem.Instance.GetCompanion(CompanionId.Mira)?.IsDead ?? true)
            && !miraCheck.HintsShown.Contains(HintSystem.HINT_COMPANION_MIRA_TEASER))
        {
            miraCheck.HintsShown.Add(HintSystem.HINT_COMPANION_MIRA_TEASER);
            terminal.SetColor("dark_yellow");
            terminal.WriteLine($"  {Loc.Get("healer.mira_teaser1")}");
            terminal.WriteLine($"  {Loc.Get("healer.mira_teaser2")}");
            terminal.SetColor("white");
            terminal.WriteLine("");
        }

        ShowMenu();
        ShowPlayerHealthStatus();
    }

    /// <summary>
    /// BBS compact display for 80x25 terminal
    /// </summary>
    private void DisplayLocationBBS()
    {
        var player = GetCurrentPlayer();
        ShowBBSHeader(Loc.Get("healer.header"));
        // 1-line health status
        terminal.SetColor("gray");
        terminal.Write(Loc.Get("healer.bbs_hp"));
        var hpPct = player.MaxHP > 0 ? (float)player.HP / player.MaxHP : 0f;
        terminal.SetColor(hpPct >= 0.7f ? "bright_green" : hpPct >= 0.3f ? "yellow" : "bright_red");
        terminal.Write($"{player.HP}/{player.MaxHP}");
        terminal.SetColor("gray");
        terminal.Write(Loc.Get("healer.bbs_gold"));
        terminal.SetColor("yellow");
        terminal.Write($"{player.Gold:N0}");
        terminal.SetColor("gray");
        terminal.Write(Loc.Get("healer.bbs_potions"));
        terminal.SetColor("green");
        terminal.Write($"{player.Healing}");
        // Show afflictions inline
        var afflictions = new List<string>();
        if (player.Poisoned) afflictions.Add(Loc.Get("healer.affliction_poison"));
        if (player.Blind) afflictions.Add(Loc.Get("healer.affliction_blind"));
        if (player.Plague) afflictions.Add(Loc.Get("healer.affliction_plague"));
        if (player.Smallpox) afflictions.Add(Loc.Get("healer.affliction_smallpox"));
        if (player.Measles) afflictions.Add(Loc.Get("healer.affliction_measles"));
        if (player.Leprosy) afflictions.Add(Loc.Get("healer.affliction_leprosy"));
        if (player.LoversBane) afflictions.Add(Loc.Get("healer.affliction_bane"));
        if (afflictions.Count > 0)
        {
            terminal.SetColor("bright_red");
            terminal.Write($"  [{string.Join(",", afflictions)}]");
        }
        terminal.WriteLine("");
        ShowBBSNPCs();
        // Menu rows
        ShowBBSMenuRow(("H", "bright_yellow", Loc.Get("healer.heal")), ("F", "bright_yellow", Loc.Get("healer.full_heal")), ("B", "bright_yellow", Loc.Get("healer.buy_potions")), ("M", "bright_yellow", Loc.Get("healer.mana_potions")));
        ShowBBSMenuRow(("P", "bright_yellow", Loc.Get("healer.cure_poison")), ("C", "bright_yellow", Loc.Get("healer.cure_disease")), ("D", "bright_yellow", Loc.Get("healer.decurse")));
        ShowBBSMenuRow(("N", "bright_yellow", Loc.Get("healer.buy_antidotes")), ("A", "bright_yellow", Loc.Get("healer.addiction")), ("S", "bright_yellow", Loc.Get("healer.status")), ("R", "bright_yellow", Loc.Get("healer.return")));
        ShowBBSFooter();
    }

    private void DisplayLocationSR()
    {
        terminal.WriteLine(Loc.Get("healer.sr_title"));
        terminal.WriteLine("");

        var player = GetCurrentPlayer();
        terminal.WriteLine($"{Loc.Get("combat.bar_hp")}: {player.HP}/{player.MaxHP}, {Loc.Get("ui.gold")}: {player.Gold:N0}, {Loc.Get("status.potions")}: {player.Healing}");

        // Show afflictions
        var afflictions = new List<string>();
        if (player.Poisoned) afflictions.Add(Loc.Get("healer.affliction_poisoned"));
        if (player.Blind) afflictions.Add(Loc.Get("healer.affliction_blind"));
        if (player.Plague) afflictions.Add(Loc.Get("healer.affliction_plague"));
        if (player.Smallpox) afflictions.Add(Loc.Get("healer.affliction_smallpox"));
        if (player.Measles) afflictions.Add(Loc.Get("healer.affliction_measles"));
        if (player.Leprosy) afflictions.Add(Loc.Get("healer.affliction_leprosy"));
        if (player.LoversBane) afflictions.Add(Loc.Get("healer.affliction_lovers_bane"));
        if (afflictions.Count > 0)
            terminal.WriteLine($"{Loc.Get("healer.afflictions_label")}: {string.Join(", ", afflictions)}");
        terminal.WriteLine("");

        ShowNPCsInLocation();

        terminal.WriteLine($"{Loc.Get("prefs.options")}");
        WriteSRMenuOption("H", Loc.Get("healer.heal"));
        WriteSRMenuOption("F", Loc.Get("healer.full_heal"));
        WriteSRMenuOption("B", Loc.Get("healer.buy_potions"));
        WriteSRMenuOption("M", Loc.Get("healer.mana_potions"));
        WriteSRMenuOption("P", Loc.Get("healer.cure_poison"));
        WriteSRMenuOption("C", Loc.Get("healer.cure_disease"));
        WriteSRMenuOption("D", Loc.Get("healer.decurse"));
        WriteSRMenuOption("N", Loc.Get("healer.buy_antidotes"));
        WriteSRMenuOption("A", Loc.Get("healer.addiction"));
        WriteSRMenuOption("S", Loc.Get("healer.status"));
        WriteSRMenuOption("R", Loc.Get("healer.return"));
        terminal.WriteLine("");

        ShowStatusLine();
    }

    protected override async Task<bool> ProcessChoice(string choice)
    {
        // Handle global quick commands first
        var (handled, shouldExit) = await TryProcessGlobalCommand(choice);
        if (handled) return shouldExit;

        switch (choice.ToUpper().Trim())
        {
            case "H":
                await HealHP();
                return false; // Stay in location
            case "F":
                await FullHeal();
                return false; // Stay in location
            case "B":
                await BuyPotions();
                return false; // Stay in location
            case "M":
                await BuyManaPotions();
                return false; // Stay in location
            case "N":
                await BuyAntidotes();
                return false;
            case "P":
                await CurePoison();
                return false; // Stay in location
            case "C":
                await CureDisease();
                return false; // Stay in location
            case "D":
                await RemoveCursedItem();
                return false; // Stay in location
            case "S":
                await DisplayPlayerStatus();
                return false; // Stay in location
            case "A":
                await CureAddiction();
                return false; // Stay in location
            case "R":
                await NavigateToLocation(GameLocation.MainStreet);
                return true; // Exit location (navigating away)
            case "?":
                ShowFullMenu();
                await terminal.PressAnyKey();
                return false; // Stay in location
            default:
                terminal.WriteLine(Loc.Get("healer.invalid_choice"), "red");
                await Task.Delay(1000);
                return false; // Stay in location
        }
    }

    protected override async Task<string> GetUserChoice()
    {
        var prompt = GetCurrentPlayer()?.Expert == true ?
            Loc.Get("healer.expert_prompt") :
            Loc.Get("ui.your_choice");

        terminal.SetColor("yellow");
        return await terminal.GetInput(prompt);
    }

    private void ShowMenu()
    {
        var player = GetCurrentPlayer();

        // Show alignment price modifier
        var alignmentModifier = AlignmentSystem.Instance.GetPriceModifier(player, isShadyShop: false);
        if (alignmentModifier != 1.0f)
        {
            var (alignText, alignColor) = AlignmentSystem.Instance.GetAlignmentDisplay(player);
            terminal.SetColor(alignColor);
            if (alignmentModifier < 1.0f)
                terminal.WriteLine($"  {Loc.Get("healer.alignment_discount", alignText, (int)((1.0f - alignmentModifier) * 100))}");
            else
                terminal.WriteLine($"  {Loc.Get("healer.alignment_markup", alignText, (int)((alignmentModifier - 1.0f) * 100))}");
        }

        // Show world event price modifier
        var worldEventModifier = WorldEventSystem.Instance.GlobalPriceModifier;
        if (Math.Abs(worldEventModifier - 1.0f) > 0.01f)
        {
            if (worldEventModifier < 1.0f)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine($"  {Loc.Get("healer.world_discount", (int)((1.0f - worldEventModifier) * 100))}");
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine($"  {Loc.Get("healer.world_markup", (int)((worldEventModifier - 1.0f) * 100))}");
            }
        }
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("healer.services_available"));
        terminal.WriteLine("");

        // Row 1 - Healing services
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("H");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("healer.menu_heal_suffix"));

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("F");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("healer.menu_full_suffix"));

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("B");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("healer.menu_potions_suffix"));

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("M");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("healer.menu_mana_suffix"));

        // Row 2 - Disease services
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("P");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("healer.menu_poison_suffix"));

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("C");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("healer.menu_disease_suffix"));

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("D");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("healer.menu_decurse_suffix"));

        // Row 3 - Addiction & Navigation
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("A");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("healer.menu_addiction_suffix"));

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("S");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("healer.menu_status_suffix"));

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("R");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("healer.menu_return_suffix"));
        terminal.WriteLine("");

        ShowStatusLine();
    }

    private void ShowFullMenu()
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("healer.services_title", HealerName), "bright_magenta");
        terminal.WriteLine("");

        var player = GetCurrentPlayer();

        WriteSectionHeader(Loc.Get("healer.section_healing"), "cyan");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("healer.menu_heal_hp", FullHealCostPerHP));
        terminal.WriteLine(Loc.Get("healer.menu_full_heal"));
        terminal.WriteLine(Loc.Get("healer.menu_buy_potions", HealingPotionCost));
        terminal.WriteLine(Loc.Get("healer.menu_mana_potions", Math.Max(75, player.Level * 3)));
        terminal.WriteLine(Loc.Get("healer.menu_poison_cure", $"{CalculateDiseaseCost(PoisonBaseCost, player.Level):N0}"));
        terminal.WriteLine("");

        WriteSectionHeader(Loc.Get("healer.section_disease"), "cyan");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("healer.menu_cure_disease"));
        terminal.WriteLine("                   " + Loc.Get("healer.menu_blindness", CalculateDiseaseCost(BlindnessBaseCost, player.Level).ToString("N0")));
        terminal.WriteLine("                   " + Loc.Get("healer.menu_plague", CalculateDiseaseCost(PlagueBaseCost, player.Level).ToString("N0")));
        terminal.WriteLine("                   " + Loc.Get("healer.menu_smallpox", CalculateDiseaseCost(SmallpoxBaseCost, player.Level).ToString("N0")));
        terminal.WriteLine("                   " + Loc.Get("healer.menu_measles", CalculateDiseaseCost(MeaslesBaseCost, player.Level).ToString("N0")));
        terminal.WriteLine("                   " + Loc.Get("healer.menu_leprosy", CalculateDiseaseCost(LeprosyBaseCost, player.Level).ToString("N0")));
        terminal.WriteLine("                   " + Loc.Get("healer.menu_lovers_bane", CalculateDiseaseCost(LoversBaneBaseCost, player.Level).ToString("N0")));
        terminal.WriteLine("");

        WriteSectionHeader(Loc.Get("healer.section_other"), "cyan");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("healer.menu_decurse", $"{CalculateDiseaseCost(CursedItemBaseCost, player.Level):N0}"));
        long rehabCost = GameConfig.RehabBaseCost + (player.Addict * GameConfig.RehabPerAddictionCost);
        terminal.WriteLine(Loc.Get("healer.menu_addiction", $"{rehabCost:N0}"));
        terminal.WriteLine(Loc.Get("healer.menu_status"));
        terminal.WriteLine(Loc.Get("healer.menu_return"));
        terminal.WriteLine("");
    }

    private void ShowPlayerHealthStatus()
    {
        var player = GetCurrentPlayer();

        terminal.SetColor("gray");
        terminal.Write($"{Loc.Get("combat.bar_hp")}: ");

        var hpPercent = (float)player.HP / player.MaxHP;
        if (hpPercent >= 0.7f)
            terminal.SetColor("green");
        else if (hpPercent >= 0.3f)
            terminal.SetColor("yellow");
        else
            terminal.SetColor("red");

        terminal.Write($"{player.HP}/{player.MaxHP}");

        terminal.SetColor("gray");
        terminal.Write($"  {Loc.Get("ui.gold")}: ");
        terminal.SetColor("yellow");
        terminal.Write($"{player.Gold:N0}");

        terminal.SetColor("gray");
        terminal.Write($"  {Loc.Get("status.potions")}: ");
        terminal.SetColor("green");
        terminal.WriteLine($"{player.Healing}");

        // Show afflictions
        var afflictions = new List<string>();
        if (player.Poisoned) afflictions.Add(Loc.Get("healer.affliction_poisoned"));
        if (player.Blind) afflictions.Add(Loc.Get("healer.affliction_blind"));
        if (player.Plague) afflictions.Add(Loc.Get("healer.affliction_plague"));
        if (player.Smallpox) afflictions.Add(Loc.Get("healer.affliction_smallpox"));
        if (player.Measles) afflictions.Add(Loc.Get("healer.affliction_measles"));
        if (player.Leprosy) afflictions.Add(Loc.Get("healer.affliction_leprosy"));
        if (player.LoversBane) afflictions.Add(Loc.Get("healer.affliction_lovers_bane"));

        if (afflictions.Count > 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"{Loc.Get("healer.afflictions_label")}: {string.Join(", ", afflictions)}");
        }

        terminal.WriteLine("");
    }

    /// <summary>
    /// Heal some HP for gold
    /// </summary>
    private async Task HealHP()
    {
        var player = GetCurrentPlayer();

        terminal.WriteLine("");

        if (player.HP >= player.MaxHP)
        {
            terminal.WriteLine(Loc.Get("healer.already_full", player.Name2, Manager), "cyan");
            await terminal.PressAnyKey();
            return;
        }

        long hpNeeded = player.MaxHP - player.HP;
        long maxCost = hpNeeded * FullHealCostPerHP;
        var (_, _, maxCostWithTax) = CityControlSystem.CalculateHealingTaxedPrice(maxCost);
        // Calculate effective per-HP cost including taxes for affordability
        long effectivePerHP = hpNeeded > 0 ? maxCostWithTax / hpNeeded : FullHealCostPerHP;
        if (effectivePerHP < FullHealCostPerHP) effectivePerHP = FullHealCostPerHP;

        terminal.WriteLine(Loc.Get("healer.how_much_hp"), "cyan");
        terminal.WriteLine(Loc.Get("healer.asks", Manager), "gray");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("shop.healing_cost", hpNeeded, $"{maxCostWithTax:N0}"), "gray");
        terminal.WriteLine(Loc.Get("healer.base_cost_info", FullHealCostPerHP), "gray");
        terminal.WriteLine("");

        var input = await terminal.GetInput(Loc.Get("healer.hp_prompt"));

        if (!long.TryParse(input, out long hpToHeal) || hpToHeal <= 0)
        {
            terminal.WriteLine(Loc.Get("healer.come_back_healing"), "cyan");
            await Task.Delay(1000);
            return;
        }

        // Cap at what they need
        if (hpToHeal > hpNeeded)
            hpToHeal = hpNeeded;

        long cost = hpToHeal * FullHealCostPerHP;
        var (healKingTax, healCityTax, healTotalWithTax) = CityControlSystem.CalculateHealingTaxedPrice(cost);

        if (player.Gold < healTotalWithTax)
        {
            long canAfford = effectivePerHP > 0 ? player.Gold / effectivePerHP : 0;
            terminal.WriteLine(Loc.Get("healer.cant_afford_healing"), "red");
            terminal.WriteLine(Loc.Get("healer.can_afford_up_to", canAfford), "yellow");
            await terminal.PressAnyKey();
            return;
        }

        CityControlSystem.Instance.DisplayTaxBreakdown(terminal, Loc.Get("healer.tax_healing"), cost);

        // Perform healing
        player.Gold -= healTotalWithTax;
        player.Statistics.RecordGoldSpent(healTotalWithTax);
        player.Statistics.RecordHealthRestored(hpToHeal);
        CityControlSystem.Instance.ProcessSaleTax(cost);
        player.HP += (int)hpToHeal;
        if (player.HP > player.MaxHP) player.HP = player.MaxHP;

        // Track healer telemetry
        TelemetrySystem.Instance.TrackShopTransaction(
            "healer", "heal", $"heal_{hpToHeal}hp", healTotalWithTax, player.Level, player.Gold
        );

        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("healer.hands_on_wounds", Manager), "gray");
        await Task.Delay(1000);
        terminal.WriteLine(Loc.Get("healer.warm_light"), "bright_green");
        terminal.WriteLine(Loc.Get("healer.healed_hp", hpToHeal), "green");
        terminal.WriteLine(Loc.Get("healer.cost_line", $"{healTotalWithTax:N0}"), "yellow");

        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Full heal - restore all HP
    /// </summary>
    private async Task FullHeal()
    {
        var player = GetCurrentPlayer();

        terminal.WriteLine("");

        if (player.HP >= player.MaxHP)
        {
            terminal.WriteLine(Loc.Get("healer.already_full2", player.Name2), "cyan");
            await terminal.PressAnyKey();
            return;
        }

        long hpNeeded = player.MaxHP - player.HP;
        long cost = hpNeeded * FullHealCostPerHP;
        var (fullHealKingTax, fullHealCityTax, fullHealTotalWithTax) = CityControlSystem.CalculateHealingTaxedPrice(cost);

        terminal.WriteLine(Loc.Get("healer.full_restore_cost", $"{fullHealTotalWithTax:N0}", Manager), "cyan");
        terminal.WriteLine("");

        CityControlSystem.Instance.DisplayTaxBreakdown(terminal, Loc.Get("healer.tax_full_healing"), cost);

        var confirm = await terminal.GetInput(Loc.Get("healer.proceed_full_heal"));

        if (confirm.ToUpper() != "Y")
        {
            terminal.WriteLine(Loc.Get("healer.as_you_wish"), "cyan");
            await Task.Delay(1000);
            return;
        }

        if (player.Gold < fullHealTotalWithTax)
        {
            terminal.WriteLine(Loc.Get("healer.cant_afford"), "red");
            await terminal.PressAnyKey();
            return;
        }

        // Perform full heal
        player.Gold -= fullHealTotalWithTax;
        player.Statistics.RecordGoldSpent(fullHealTotalWithTax);
        player.Statistics.RecordHealthRestored(hpNeeded);
        CityControlSystem.Instance.ProcessSaleTax(cost);
        player.HP = player.MaxHP;

        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("healer.begins_ritual", Manager), "gray");
        await Task.Delay(500);
        terminal.Write("...", "gray");
        await Task.Delay(500);
        terminal.Write("...", "gray");
        await Task.Delay(500);
        terminal.WriteLine("...", "gray");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("healer.divine_light"), "bright_yellow");
        terminal.WriteLine(Loc.Get("healer.completely_healed"), "bright_green");
        terminal.WriteLine(Loc.Get("healer.hp_restored_to", player.HP, player.MaxHP), "green");

        // Full heal also restores mana
        if (player.Mana < player.MaxMana)
        {
            player.Mana = player.MaxMana;
            terminal.WriteLine(Loc.Get("healer.mana_restored_to", player.Mana, player.MaxMana), "blue");
        }

        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Buy healing potions
    /// </summary>
    private async Task BuyPotions()
    {
        var player = GetCurrentPlayer();

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        var (_, _, singlePotionWithTax) = CityControlSystem.CalculateHealingTaxedPrice(HealingPotionCost);
        terminal.WriteLine(Loc.Get("healer.potions_price", singlePotionWithTax, Manager));
        terminal.WriteLine("");

        long maxAfford = singlePotionWithTax > 0 ? player.Gold / singlePotionWithTax : 0;
        int maxCanCarry = player.MaxPotions - (int)player.Healing;

        if (maxCanCarry <= 0)
        {
            terminal.WriteLine(Loc.Get("healer.max_potions", player.MaxPotions), "red");
            await terminal.PressAnyKey();
            return;
        }

        terminal.WriteLine(Loc.Get("healer.current_potions", player.Healing, player.MaxPotions), "gray");
        terminal.WriteLine(Loc.Get("healer.can_afford_potions", Math.Min(maxAfford, maxCanCarry)), "gray");
        terminal.WriteLine("");

        var input = await terminal.GetInput(Loc.Get("healer.how_many_potions"));

        if (!int.TryParse(input, out int quantity) || quantity <= 0)
        {
            terminal.WriteLine(Loc.Get("healer.come_back_supplies"), "cyan");
            await Task.Delay(1000);
            return;
        }

        quantity = Math.Min(quantity, maxCanCarry);

        long cost = quantity * HealingPotionCost;
        var (potionKingTax, potionCityTax, potionTotalWithTax) = CityControlSystem.CalculateHealingTaxedPrice(cost);

        if (player.Gold < potionTotalWithTax)
        {
            terminal.WriteLine(Loc.Get("healer.cant_afford_many"), "red");
            await terminal.PressAnyKey();
            return;
        }

        CityControlSystem.Instance.DisplayTaxBreakdown(terminal, Loc.Get("healer.tax_potions"), cost);

        // Purchase potions
        player.Gold -= potionTotalWithTax;
        player.Statistics.RecordPurchase(potionTotalWithTax);
        CityControlSystem.Instance.ProcessSaleTax(cost);
        player.Healing += quantity;

        // Track potion purchase telemetry
        TelemetrySystem.Instance.TrackShopTransaction(
            "healer", "buy", $"healing_potion_x{quantity}", potionTotalWithTax, player.Level, player.Gold
        );

        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("healer.handed_potions", Manager, quantity, quantity > 1 ? "s" : ""), "gray");
        terminal.WriteLine(Loc.Get("healer.now_have_potions", player.Healing), "green");
        terminal.WriteLine(Loc.Get("healer.cost_line", $"{potionTotalWithTax:N0}"), "yellow");

        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Buy mana potions
    /// </summary>
    private async Task BuyManaPotions()
    {
        var player = GetCurrentPlayer();

        // v0.52.5: Non-casters can buy mana potions to give to teammates via Aid Ally
        // (restriction removed — was blocking non-mana classes entirely)

        int potionPrice = Math.Max(75, player.Level * 3);
        int manaRestored = 30 + player.Level * 5;
        var (_, _, singleManaWithTax) = CityControlSystem.CalculateHealingTaxedPrice(potionPrice);

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("healer.mana_price", singleManaWithTax, manaRestored, Manager));
        terminal.WriteLine("");

        long maxAfford = singleManaWithTax > 0 ? player.Gold / singleManaWithTax : 0;
        int maxCanCarry = player.MaxManaPotions - (int)player.ManaPotions;

        terminal.WriteLine(Loc.Get("healer.current_mana_potions", player.ManaPotions, player.MaxManaPotions), "gray");
        terminal.WriteLine(Loc.Get("healer.can_afford_potions", Math.Min(maxAfford, maxCanCarry)), "gray");
        terminal.WriteLine("");

        if (maxCanCarry <= 0)
        {
            terminal.WriteLine(Loc.Get("healer.max_mana_potions"), "red");
            await terminal.PressAnyKey();
            return;
        }

        var input = await terminal.GetInput(Loc.Get("healer.mana_how_many"));

        if (!int.TryParse(input, out int quantity) || quantity <= 0)
        {
            terminal.WriteLine(Loc.Get("healer.come_back_supplies"), "cyan");
            await Task.Delay(1000);
            return;
        }

        quantity = Math.Min(quantity, maxCanCarry);

        long cost = quantity * potionPrice;
        var (_, _, potionTotalWithTax) = CityControlSystem.CalculateHealingTaxedPrice(cost);

        if (player.Gold < potionTotalWithTax)
        {
            terminal.WriteLine(Loc.Get("healer.cant_afford_many"), "red");
            await terminal.PressAnyKey();
            return;
        }

        CityControlSystem.Instance.DisplayTaxBreakdown(terminal, Loc.Get("healer.tax_mana_potions"), cost);

        player.Gold -= potionTotalWithTax;
        player.Statistics.RecordPurchase(potionTotalWithTax);
        player.Statistics.RecordGoldSpent(potionTotalWithTax);
        CityControlSystem.Instance.ProcessSaleTax(cost);
        player.ManaPotions += quantity;

        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("healer.handed_mana", Manager, quantity, quantity > 1 ? "s" : ""), "gray");
        terminal.WriteLine(Loc.Get("healer.now_have_mana", player.ManaPotions), "blue");
        terminal.WriteLine(Loc.Get("healer.cost_line", $"{potionTotalWithTax:N0}"), "yellow");

        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Buy antidotes for later use
    /// </summary>
    private async Task BuyAntidotes()
    {
        var player = GetCurrentPlayer();
        int antidoteBaseCost = 75;
        var (_, _, antidotePriceWithTax) = CityControlSystem.CalculateHealingTaxedPrice(antidoteBaseCost);

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("healer.antidote_intro", Manager));
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("healer.antidote_price", antidotePriceWithTax));
        terminal.WriteLine("");

        int maxCanCarry = player.MaxAntidotes - player.Antidotes;
        if (maxCanCarry <= 0)
        {
            terminal.WriteLine(Loc.Get("healer.max_antidotes", player.MaxAntidotes), "red");
            await terminal.PressAnyKey();
            return;
        }

        long maxAfford = antidotePriceWithTax > 0 ? player.Gold / antidotePriceWithTax : 0;
        terminal.WriteLine(Loc.Get("healer.current_antidotes", player.Antidotes, player.MaxAntidotes), "gray");
        terminal.WriteLine(Loc.Get("healer.can_afford_antidotes", Math.Min(maxAfford, maxCanCarry)), "gray");
        terminal.WriteLine("");

        var input = await terminal.GetInput(Loc.Get("healer.how_many_antidotes"));
        if (!int.TryParse(input, out int quantity) || quantity <= 0)
        {
            terminal.WriteLine(Loc.Get("healer.come_back_supplies"), "cyan");
            await Task.Delay(1000);
            return;
        }

        quantity = Math.Min(quantity, maxCanCarry);
        long cost = quantity * antidoteBaseCost;
        var (_, _, totalWithTax) = CityControlSystem.CalculateHealingTaxedPrice(cost);

        if (player.Gold < totalWithTax)
        {
            terminal.WriteLine(Loc.Get("healer.cant_afford_many"), "red");
            await terminal.PressAnyKey();
            return;
        }

        CityControlSystem.Instance.DisplayTaxBreakdown(terminal, Loc.Get("healer.tax_antidotes"), cost);

        player.Gold -= totalWithTax;
        player.Statistics.RecordPurchase(totalWithTax);
        CityControlSystem.Instance.ProcessSaleTax(cost);
        player.Antidotes += quantity;

        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("healer.handed_antidotes", Manager, quantity, quantity > 1 ? "s" : ""), "gray");
        terminal.WriteLine(Loc.Get("healer.now_have_antidotes", player.Antidotes), "green");
        terminal.WriteLine(Loc.Get("healer.cost_line", $"{totalWithTax:N0}"), "yellow");
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("healer.antidote_tip"));

        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Cure poison
    /// </summary>
    private async Task CurePoison()
    {
        var player = GetCurrentPlayer();

        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("healer.check_poison", Manager), "cyan");
        terminal.WriteLine("");

        await Task.Delay(1000);

        if (!player.Poisoned)
        {
            terminal.WriteLine(Loc.Get("healer.not_poisoned"), "green");
            await terminal.PressAnyKey();
            return;
        }

        long cost = CalculateDiseaseCost(PoisonBaseCost, player.Level);
        var (poisonKingTax, poisonCityTax, poisonTotalWithTax) = CityControlSystem.CalculateHealingTaxedPrice(cost);

        terminal.WriteLine(Loc.Get("healer.venom_found"), "cyan");
        terminal.WriteLine(Loc.Get("healer.purge_cost", $"{cost:N0}"), "cyan");
        terminal.WriteLine("");

        CityControlSystem.Instance.DisplayTaxBreakdown(terminal, Loc.Get("healer.tax_poison_cure"), cost);

        var confirm = await terminal.GetInput(Loc.Get("healer.cure_poison_prompt"));

        if (confirm.ToUpper() != "Y")
        {
            terminal.WriteLine(Loc.Get("healer.careful_poison"), "yellow");
            await Task.Delay(1500);
            return;
        }

        if (player.Gold < poisonTotalWithTax)
        {
            terminal.WriteLine(Loc.Get("healer.cant_afford_antidote"), "red");
            await terminal.PressAnyKey();
            return;
        }

        // Cure poison
        player.Gold -= poisonTotalWithTax;
        player.Statistics.RecordGoldSpent(poisonTotalWithTax);
        CityControlSystem.Instance.ProcessSaleTax(cost);
        player.Poison = 0;
        player.PoisonTurns = 0;

        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("healer.mixing_antidote", Manager), "gray");
        await Task.Delay(1000);
        terminal.WriteLine(Loc.Get("healer.drink_mixture"), "gray");
        await Task.Delay(1000);
        terminal.WriteLine(Loc.Get("healer.poison_purged"), "bright_green");
        terminal.WriteLine(Loc.Get("healer.no_longer_poisoned"), "green");

        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Cure diseases - from Pascal HEALERC.PAS
    /// </summary>
    private async Task CureDisease()
    {
        var player = GetCurrentPlayer();

        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("healer.examine_diseases", Manager), "cyan");
        terminal.WriteLine("");

        // Check for diseases
        var diseases = new Dictionary<string, (string Name, long Cost, Action Cure)>();

        if (player.Blind)
            diseases["B"] = (Loc.Get("healer.disease_blindness"), CalculateDiseaseCost(BlindnessBaseCost, player.Level), () => player.Blind = false);
        if (player.Plague)
            diseases["P"] = (Loc.Get("healer.disease_plague"), CalculateDiseaseCost(PlagueBaseCost, player.Level), () => player.Plague = false);
        if (player.Smallpox)
            diseases["S"] = (Loc.Get("healer.disease_smallpox"), CalculateDiseaseCost(SmallpoxBaseCost, player.Level), () => player.Smallpox = false);
        if (player.Measles)
            diseases["M"] = (Loc.Get("healer.disease_measles"), CalculateDiseaseCost(MeaslesBaseCost, player.Level), () => player.Measles = false);
        if (player.Leprosy)
            diseases["L"] = (Loc.Get("healer.disease_leprosy"), CalculateDiseaseCost(LeprosyBaseCost, player.Level), () => player.Leprosy = false);
        if (player.LoversBane)
            diseases["V"] = (Loc.Get("healer.disease_lovers_bane"), CalculateDiseaseCost(LoversBaneBaseCost, player.Level), () => player.LoversBane = false);

        if (diseases.Count == 0)
        {
            terminal.WriteLine(Loc.Get("healer.no_diseases"), "green");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("healer.wasting_time", Manager), "cyan");
            await terminal.PressAnyKey();
            return;
        }

        // Display diseases
        terminal.SetColor("magenta");
        terminal.WriteLine(Loc.Get("healer.affecting_diseases"));
        terminal.WriteLine("------------------");

        long totalCost = 0;
        foreach (var disease in diseases)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"({disease.Key}){disease.Value.Name} - {disease.Value.Cost:N0} {Loc.Get("healer.disease_gold")}");
            totalCost += disease.Value.Cost;
        }

        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("healer.cure_all", $"{totalCost:N0}"));
        terminal.WriteLine("");

        var choice = await terminal.GetInput(Loc.Get("healer.choose_disease"));
        choice = choice.ToUpper().Trim();

        if (choice == "R" || string.IsNullOrEmpty(choice))
        {
            terminal.WriteLine(Loc.Get("healer.come_back_treatment"), "cyan");
            await Task.Delay(1000);
            return;
        }

        if (choice == "C")
        {
            // Cure all
            var (cureAllKingTax, cureAllCityTax, cureAllTotalWithTax) = CityControlSystem.CalculateHealingTaxedPrice(totalCost);

            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("healer.complete_healing_cost", $"{totalCost:N0}", Manager), "cyan");

            CityControlSystem.Instance.DisplayTaxBreakdown(terminal, Loc.Get("healer.tax_disease_cures"), totalCost);

            var confirm = await terminal.GetInput(Loc.Get("healer.go_ahead_pay"));
            if (confirm.ToUpper() != "Y")
            {
                return;
            }

            if (player.Gold < cureAllTotalWithTax)
            {
                terminal.WriteLine(Loc.Get("healer.cant_afford"), "red");
                await terminal.PressAnyKey();
                return;
            }

            player.Gold -= cureAllTotalWithTax;
            player.Statistics.RecordGoldSpent(cureAllTotalWithTax);
            CityControlSystem.Instance.ProcessSaleTax(totalCost);
            foreach (var disease in diseases.Values)
            {
                disease.Cure();
                player.Statistics.RecordDiseaseCured();
            }

            await ShowHealingSequence();
        }
        else if (diseases.ContainsKey(choice))
        {
            // Cure single disease
            var disease = diseases[choice];
            var (cureOneKingTax, cureOneCityTax, cureOneTotalWithTax) = CityControlSystem.CalculateHealingTaxedPrice(disease.Cost);

            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("healer.healing_disease_cost", disease.Name, $"{disease.Cost:N0}", Manager), "cyan");

            CityControlSystem.Instance.DisplayTaxBreakdown(terminal, Loc.Get("healer.tax_disease_cure"), disease.Cost);

            var confirm = await terminal.GetInput(Loc.Get("healer.go_ahead_pay"));
            if (confirm.ToUpper() != "Y")
            {
                return;
            }

            if (player.Gold < cureOneTotalWithTax)
            {
                terminal.WriteLine(Loc.Get("healer.cant_afford"), "red");
                await terminal.PressAnyKey();
                return;
            }

            player.Gold -= cureOneTotalWithTax;
            player.Statistics.RecordGoldSpent(cureOneTotalWithTax);
            CityControlSystem.Instance.ProcessSaleTax(disease.Cost);
            disease.Cure();
            player.Statistics.RecordDiseaseCured();

            await ShowHealingSequence();
        }
        else
        {
            terminal.WriteLine(Loc.Get("healer.invalid_choice_short"), "red");
            await Task.Delay(1000);
        }
    }

    /// <summary>
    /// Pascal healing sequence with delays
    /// </summary>
    private async Task ShowHealingSequence()
    {
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("healer.give_gold", Manager));
        terminal.WriteLine(Loc.Get("healer.bed_nearby"));
        terminal.Write(Loc.Get("healer.fall_asleep"));

        for (int i = 0; i < 4; i++)
        {
            await Task.Delay(800);
            terminal.Write("...");
        }

        terminal.WriteLine("");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("healer.wake_up1"), "gray");
        terminal.WriteLine(Loc.Get("healer.wake_up2"), "green");
        terminal.WriteLine(Loc.Get("healer.walk_out", Manager), "gray");

        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Remove cursed items
    /// </summary>
    private async Task RemoveCursedItem()
    {
        var player = GetCurrentPlayer();

        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("healer.check_equipment", Manager), "cyan");
        terminal.WriteLine("");

        // Check equipped items for curses using BOTH legacy and modern equipment systems
        var cursedItems = new List<(string Name, string SlotName, Action RemoveAction)>();

        // Check modern EquippedItems system first
        foreach (var slot in player.EquippedItems.Keys.ToList())
        {
            var equipment = player.GetEquipment(slot);
            if (equipment != null && equipment.IsCursed)
            {
                var capturedSlot = slot;
                var capturedEquipment = equipment;
                cursedItems.Add((equipment.Name, slot.ToString(), () =>
                {
                    // Remove from modern equipment system
                    player.EquippedItems[capturedSlot] = 0;
                    // Also clear the curse flags if applicable
                    if (capturedSlot == EquipmentSlot.MainHand)
                        player.WeaponCursed = false;
                    else if (capturedSlot == EquipmentSlot.Body)
                        player.ArmorCursed = false;
                    else if (capturedSlot == EquipmentSlot.OffHand)
                        player.ShieldCursed = false;
                    player.RecalculateStats();
                }));
            }
        }

        // Also check legacy weapon (RHand slot) if not already found
        if (player.RHand > 0 && player.WeaponCursed &&
            !cursedItems.Any(c => c.SlotName == EquipmentSlot.MainHand.ToString()))
        {
            cursedItems.Add((player.WeaponName ?? "Weapon", "weapon", () =>
            {
                player.RHand = 0;
                player.WeaponCursed = false;
                player.RecalculateStats();
            }));
        }
        // Check legacy armor (Body slot)
        if (player.Body > 0 && player.ArmorCursed &&
            !cursedItems.Any(c => c.SlotName == EquipmentSlot.Body.ToString()))
        {
            cursedItems.Add((player.ArmorName ?? "Armor", "armor", () =>
            {
                player.Body = 0;
                player.ArmorCursed = false;
                player.RecalculateStats();
            }));
        }
        // Check legacy shield (Shield slot)
        if (player.Shield > 0 && player.ShieldCursed &&
            !cursedItems.Any(c => c.SlotName == EquipmentSlot.OffHand.ToString()))
        {
            cursedItems.Add(("Shield", "shield", () =>
            {
                player.Shield = 0;
                player.ShieldCursed = false;
                player.RecalculateStats();
            }));
        }

        if (cursedItems.Count == 0)
        {
            terminal.WriteLine(Loc.Get("healer.equipment_alright"), "cyan");
            terminal.WriteLine(Loc.Get("healer.nods_approvingly", Manager), "gray");
            await terminal.PressAnyKey();
            return;
        }

        long cost = CalculateDiseaseCost(CursedItemBaseCost, player.Level);
        var (curseKingTax, curseCityTax, curseTotalWithTax) = CityControlSystem.CalculateHealingTaxedPrice(cost);

        foreach (var item in cursedItems)
        {
            terminal.WriteLine(Loc.Get("shop.cursed_item_healer", item.Name), "red");
            terminal.WriteLine(Loc.Get("healer.curse_cost", $"{cost:N0}"), "cyan");
            terminal.WriteLine(Loc.Get("healer.curse_warning"), "yellow");
            terminal.WriteLine("");

            CityControlSystem.Instance.DisplayTaxBreakdown(terminal, Loc.Get("healer.tax_curse_removal"), cost);

            var confirm = await terminal.GetInput(Loc.Get("healer.remove_curse_prompt"));

            if (confirm.ToUpper() == "Y")
            {
                if (player.Gold < curseTotalWithTax)
                {
                    terminal.WriteLine(Loc.Get("healer.cant_afford"), "red");
                    continue;
                }

                player.Gold -= curseTotalWithTax;
                player.Statistics.RecordGoldSpent(curseTotalWithTax);
                CityControlSystem.Instance.ProcessSaleTax(cost);

                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("healer.strange_spells", Manager), "gray");
                await Task.Delay(500);
                terminal.Write("...", "gray");
                await Task.Delay(500);
                terminal.Write("...", "gray");
                await Task.Delay(500);
                terminal.WriteLine("...", "gray");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("healer.suddenly"), "bright_yellow");
                terminal.WriteLine(Loc.Get("healer.disintegrates", item.Name), "red");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("healer.smiles_pay1", Manager), "gray");
                terminal.WriteLine(Loc.Get("healer.smiles_pay2"), "gray");

                // Remove the cursed item
                item.RemoveAction();
            }
        }

        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Display full player status
    /// </summary>
    private async Task DisplayPlayerStatus()
    {
        var player = GetCurrentPlayer();

        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("healer.health_status"), "cyan");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($"{Loc.Get("ui.name_label")}:  {player.Name2}");
        terminal.WriteLine($"{Loc.Get("status.class")}: {player.Class}  {Loc.Get("status.race")}: {player.Race}");
        terminal.WriteLine($"{Loc.Get("ui.level")}: {player.Level}");
        terminal.WriteLine("");

        // HP Bar
        terminal.Write($"{Loc.Get("combat.bar_hp")}:    ");
        var hpPercent = (float)player.HP / player.MaxHP;
        if (hpPercent >= 0.7f) terminal.SetColor("green");
        else if (hpPercent >= 0.3f) terminal.SetColor("yellow");
        else terminal.SetColor("red");
        terminal.Write($"{player.HP}/{player.MaxHP}");
        terminal.SetColor("gray");
        terminal.WriteLine($"  ({hpPercent * 100:F0}%)");

        terminal.SetColor("yellow");
        terminal.WriteLine($"{Loc.Get("ui.gold")}:  {player.Gold:N0}");

        terminal.SetColor("green");
        terminal.WriteLine($"{Loc.Get("healer.healing_potions_label")}: {player.Healing}");
        terminal.WriteLine("");

        // Afflictions
        terminal.SetColor("magenta");
        terminal.WriteLine($"{Loc.Get("healer.affecting_diseases")}:");
        terminal.WriteLine("=-=-=-=-=-=-=-=-=-=");

        bool hasAffliction = false;

        if (player.Poisoned)
        {
            terminal.WriteLine(Loc.Get("healer.status_poisoned"), "red");
            hasAffliction = true;
        }
        if (player.Blind)
        {
            terminal.WriteLine(Loc.Get("healer.status_blindness"), "red");
            hasAffliction = true;
        }
        if (player.Plague)
        {
            terminal.WriteLine(Loc.Get("healer.status_plague"), "red");
            hasAffliction = true;
        }
        if (player.Smallpox)
        {
            terminal.WriteLine(Loc.Get("healer.status_smallpox"), "red");
            hasAffliction = true;
        }
        if (player.Measles)
        {
            terminal.WriteLine(Loc.Get("healer.status_measles"), "red");
            hasAffliction = true;
        }
        if (player.Leprosy)
        {
            terminal.WriteLine(Loc.Get("healer.status_leprosy"), "red");
            hasAffliction = true;
        }
        if (player.LoversBane)
        {
            terminal.WriteLine(Loc.Get("healer.status_lovers_bane"), "red");
            hasAffliction = true;
        }

        if (!hasAffliction)
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("healer.not_infected"), "green");
            terminal.WriteLine(Loc.Get("healer.stay_healthy"), "green");
        }

        // Drug & addiction status
        terminal.WriteLine("");
        terminal.SetColor("magenta");
        terminal.WriteLine(Loc.Get("healer.drug_status"));
        terminal.WriteLine("=-=-=-=-=-=");

        if (player.OnDrugs && player.ActiveDrug != DrugType.None)
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("healer.active_drug", player.ActiveDrug, player.DrugEffectDays));
        }
        else
        {
            terminal.SetColor("green");
            terminal.WriteLine(Loc.Get("healer.no_drug_effects"));
        }

        if (player.IsAddicted)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("healer.addicted_level", player.Addict));
            long rehabCost = GameConfig.RehabBaseCost + (player.Addict * GameConfig.RehabPerAddictionCost);
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("healer.rehab_available", $"{rehabCost:N0}"));
        }

        terminal.WriteLine("");
        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Addiction rehabilitation - cures addiction, clears tolerance, clears active drug
    /// </summary>
    private async Task CureAddiction()
    {
        var player = GetCurrentPlayer();

        terminal.WriteLine("");

        if (!player.IsAddicted && !player.OnDrugs)
        {
            terminal.WriteLine(Loc.Get("healer.no_dependency", player.Name2, Manager), "cyan");
            await terminal.PressAnyKey();
            return;
        }

        long baseCost = GameConfig.RehabBaseCost;
        long addictionCost = player.Addict * GameConfig.RehabPerAddictionCost;
        long totalCost = baseCost + addictionCost;
        var (_, _, totalWithTax) = CityControlSystem.CalculateHealingTaxedPrice(totalCost);

        WriteSectionHeader(Loc.Get("healer.rehab_program"), "bright_magenta");
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("healer.substance_taken_hold", player.Name2, Manager));
        terminal.WriteLine("");

        terminal.SetColor("white");
        if (player.OnDrugs)
            terminal.WriteLine($"  {Loc.Get("healer.active_drug_display", player.ActiveDrug, player.DrugEffectDays)}");
        if (player.IsAddicted)
            terminal.WriteLine($"  {Loc.Get("healer.addiction_level_display", player.Addict)}");
        if (player.DrugTolerance != null && player.DrugTolerance.Count > 0)
            terminal.WriteLine($"  {Loc.Get("healer.drug_tolerances", player.DrugTolerance.Count)}");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("healer.treatment_cost_quote", $"{totalCost:N0}"));
        terminal.SetColor("gray");
        terminal.WriteLine($"  {Loc.Get("healer.base_cost_label", $"{baseCost:N0}")}");
        if (addictionCost > 0)
            terminal.WriteLine($"  {Loc.Get("healer.surcharge", $"{addictionCost:N0}")}");
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("healer.treatment_will"));
        terminal.WriteLine($"  {Loc.Get("healer.will_purge")}");
        terminal.WriteLine($"  {Loc.Get("healer.will_cure")}");
        terminal.WriteLine($"  {Loc.Get("healer.will_reset")}");
        terminal.WriteLine("");

        CityControlSystem.Instance.DisplayTaxBreakdown(terminal, Loc.Get("healer.tax_rehab"), totalCost);

        var confirm = await terminal.GetInput(Loc.Get("healer.proceed_rehab"));

        if (confirm.ToUpper() != "Y")
        {
            terminal.WriteLine(Loc.Get("healer.door_open", player.Name2), "cyan");
            await Task.Delay(1000);
            return;
        }

        if (player.Gold < totalWithTax)
        {
            terminal.WriteLine(Loc.Get("healer.cant_afford_treatment"), "red");
            terminal.WriteLine(Loc.Get("healer.find_gold"), "cyan");
            await terminal.PressAnyKey();
            return;
        }

        // Perform rehabilitation
        player.Gold -= totalWithTax;
        player.Statistics.RecordGoldSpent(totalWithTax);
        CityControlSystem.Instance.ProcessSaleTax(totalCost);

        // Clear active drug (OnDrugs is computed from ActiveDrug != None)
        player.ActiveDrug = DrugType.None;
        player.DrugEffectDays = 0;

        // Cure addiction
        player.Addict = 0;

        // Clear tolerance
        if (player.DrugTolerance != null)
            player.DrugTolerance.Clear();

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("healer.private_room", Manager));
        await Task.Delay(1000);
        terminal.WriteLine(Loc.Get("healer.purifying_herbs"));
        await Task.Delay(1000);
        terminal.Write(Loc.Get("healer.treatment_begins"));
        for (int i = 0; i < 4; i++)
        {
            await Task.Delay(800);
            terminal.Write("...");
        }
        terminal.WriteLine("");
        terminal.WriteLine("");
        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("healer.painful_treatment"));
        await Task.Delay(1000);
        terminal.WriteLine(Loc.Get("healer.body_cleansed"), "green");
        terminal.WriteLine(Loc.Get("healer.addiction_cured"), "green");
        terminal.WriteLine(Loc.Get("healer.tolerances_reset"), "green");
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("healer.free_chains", player.Name2));
        terminal.WriteLine(Loc.Get("healer.stay_clean", Manager), "gray");

        // Track telemetry
        TelemetrySystem.Instance.TrackShopTransaction(
            "healer", "rehab", "addiction_rehab", totalWithTax, player.Level, player.Gold
        );

        await terminal.PressAnyKey();
    }
}
