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
        return (long)(basePrice * alignmentModifier * worldEventModifier * factionModifier);
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

        if (DoorMode.IsInDoorMode)
        {
            DisplayLocationBBS();
            return;
        }

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔═════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine($"║{"THE GOLDEN BOW - HEALING HUT".PadLeft((77 + 28) / 2).PadRight(77)}║");
        terminal.WriteLine("╚═════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        ShowShopkeeperMood(Manager,
            $"{Manager} The Fat is sitting at his desk, reading a book.");
        terminal.WriteLine("");

        ShowNPCsInLocation();

        ShowMenu();
        ShowPlayerHealthStatus();
    }

    /// <summary>
    /// BBS compact display for 80x25 terminal
    /// </summary>
    private void DisplayLocationBBS()
    {
        var player = GetCurrentPlayer();
        ShowBBSHeader("THE GOLDEN BOW - HEALING HUT");
        // 1-line health status
        terminal.SetColor("gray");
        terminal.Write(" HP:");
        var hpPct = player.MaxHP > 0 ? (float)player.HP / player.MaxHP : 0f;
        terminal.SetColor(hpPct >= 0.7f ? "bright_green" : hpPct >= 0.3f ? "yellow" : "bright_red");
        terminal.Write($"{player.HP}/{player.MaxHP}");
        terminal.SetColor("gray");
        terminal.Write("  Gold:");
        terminal.SetColor("yellow");
        terminal.Write($"{player.Gold:N0}");
        terminal.SetColor("gray");
        terminal.Write("  Potions:");
        terminal.SetColor("green");
        terminal.Write($"{player.Healing}");
        // Show afflictions inline
        var afflictions = new List<string>();
        if (player.Poisoned) afflictions.Add("Poison");
        if (player.Blind) afflictions.Add("Blind");
        if (player.Plague) afflictions.Add("Plague");
        if (player.Smallpox) afflictions.Add("Smallpox");
        if (player.Measles) afflictions.Add("Measles");
        if (player.Leprosy) afflictions.Add("Leprosy");
        if (player.LoversBane) afflictions.Add("Bane");
        if (afflictions.Count > 0)
        {
            terminal.SetColor("bright_red");
            terminal.Write($"  [{string.Join(",", afflictions)}]");
        }
        terminal.WriteLine("");
        ShowBBSNPCs();
        // Menu rows
        ShowBBSMenuRow(("H", "bright_yellow", "Heal HP"), ("F", "bright_yellow", "Full Heal"), ("B", "bright_yellow", "Buy Potions"), ("M", "bright_yellow", "Mana Pots"));
        ShowBBSMenuRow(("P", "bright_yellow", "Poison Cure"), ("C", "bright_yellow", "Cure Disease"), ("D", "bright_yellow", "Decurse"));
        ShowBBSMenuRow(("A", "bright_yellow", "Addiction"), ("S", "bright_yellow", "Status"), ("R", "bright_yellow", "Return"));
        ShowBBSFooter();
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
                terminal.WriteLine("Invalid choice. Press ? for menu.", "red");
                await Task.Delay(1000);
                return false; // Stay in location
        }
    }

    protected override async Task<string> GetUserChoice()
    {
        var prompt = GetCurrentPlayer()?.Expert == true ?
            "Healing Hut (H,F,B,M,P,C,D,A,S,R,?) :" :
            "Healing Hut (? for menu) :";

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
                terminal.WriteLine($"  Your {alignText} alignment grants you a {(int)((1.0f - alignmentModifier) * 100)}% discount!");
            else
                terminal.WriteLine($"  Your {alignText} alignment causes a {(int)((alignmentModifier - 1.0f) * 100)}% markup.");
        }

        // Show world event price modifier
        var worldEventModifier = WorldEventSystem.Instance.GlobalPriceModifier;
        if (Math.Abs(worldEventModifier - 1.0f) > 0.01f)
        {
            if (worldEventModifier < 1.0f)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine($"  World Events: {(int)((1.0f - worldEventModifier) * 100)}% discount active!");
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine($"  World Events: {(int)((worldEventModifier - 1.0f) * 100)}% price increase!");
            }
        }
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine("Services Available:");
        terminal.WriteLine("");

        // Row 1 - Healing services
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("H");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("eal HP        ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("F");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("ull Heal       ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("B");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("uy Potions     ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("M");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine("ana Potions");

        // Row 2 - Disease services
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("P");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("oison Cure    ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("C");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("ure Disease    ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("D");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine("ecurse Item");

        // Row 3 - Addiction & Navigation
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("A");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("ddiction Rehab ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("S");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("tatus         ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("R");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine("eturn to street");
        terminal.WriteLine("");

        ShowStatusLine();
    }

    private void ShowFullMenu()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine($"-*- {HealerName} - Services -*-");
        terminal.WriteLine("");

        var player = GetCurrentPlayer();

        terminal.SetColor("cyan");
        terminal.WriteLine("═══ Healing Services ═══");
        terminal.SetColor("white");
        terminal.WriteLine($"(H)eal HP        - Restore some HP ({FullHealCostPerHP} gold per HP)");
        terminal.WriteLine($"(F)ull Heal      - Restore all HP (costs vary)");
        terminal.WriteLine($"(B)uy Potions    - Purchase healing potions ({HealingPotionCost} gold each)");
        terminal.WriteLine($"(M)ana Potions   - Purchase mana potions ({Math.Max(75, player.Level * 3)} gold each)");
        terminal.WriteLine($"(P)oison Cure    - Remove poison ({CalculateDiseaseCost(PoisonBaseCost, player.Level):N0} gold)");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine("═══ Disease Treatment ═══");
        terminal.SetColor("white");
        terminal.WriteLine($"(C)ure Disease   - Cure afflictions (cost varies by disease)");
        terminal.WriteLine("                   Blindness:    " + CalculateDiseaseCost(BlindnessBaseCost, player.Level).ToString("N0") + " gold");
        terminal.WriteLine("                   Plague:       " + CalculateDiseaseCost(PlagueBaseCost, player.Level).ToString("N0") + " gold");
        terminal.WriteLine("                   Smallpox:     " + CalculateDiseaseCost(SmallpoxBaseCost, player.Level).ToString("N0") + " gold");
        terminal.WriteLine("                   Measles:      " + CalculateDiseaseCost(MeaslesBaseCost, player.Level).ToString("N0") + " gold");
        terminal.WriteLine("                   Leprosy:      " + CalculateDiseaseCost(LeprosyBaseCost, player.Level).ToString("N0") + " gold");
        terminal.WriteLine("                   Lover's Bane: " + CalculateDiseaseCost(LoversBaneBaseCost, player.Level).ToString("N0") + " gold");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine("═══ Other Services ═══");
        terminal.SetColor("white");
        terminal.WriteLine($"(D)ecurse Item   - Remove curse from equipment ({CalculateDiseaseCost(CursedItemBaseCost, player.Level):N0} gold)");
        long rehabCost = GameConfig.RehabBaseCost + (player.Addict * GameConfig.RehabPerAddictionCost);
        terminal.WriteLine($"(A)ddiction Rehab- Cure drug addiction ({rehabCost:N0} gold)");
        terminal.WriteLine("(S)tatus         - View your current health status");
        terminal.WriteLine("(R)eturn         - Return to Main Street");
        terminal.WriteLine("");
    }

    private void ShowPlayerHealthStatus()
    {
        var player = GetCurrentPlayer();

        terminal.SetColor("gray");
        terminal.Write("HP: ");

        var hpPercent = (float)player.HP / player.MaxHP;
        if (hpPercent >= 0.7f)
            terminal.SetColor("green");
        else if (hpPercent >= 0.3f)
            terminal.SetColor("yellow");
        else
            terminal.SetColor("red");

        terminal.Write($"{player.HP}/{player.MaxHP}");

        terminal.SetColor("gray");
        terminal.Write("  Gold: ");
        terminal.SetColor("yellow");
        terminal.Write($"{player.Gold:N0}");

        terminal.SetColor("gray");
        terminal.Write("  Potions: ");
        terminal.SetColor("green");
        terminal.WriteLine($"{player.Healing}");

        // Show afflictions
        var afflictions = new List<string>();
        if (player.Poisoned) afflictions.Add("Poisoned");
        if (player.Blind) afflictions.Add("Blind");
        if (player.Plague) afflictions.Add("Plague");
        if (player.Smallpox) afflictions.Add("Smallpox");
        if (player.Measles) afflictions.Add("Measles");
        if (player.Leprosy) afflictions.Add("Leprosy");
        if (player.LoversBane) afflictions.Add("Lover's Bane");

        if (afflictions.Count > 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"Afflictions: {string.Join(", ", afflictions)}");
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
            terminal.WriteLine($"\"{player.Name2}, you are already at full health!\"", "cyan");
            terminal.WriteLine($", {Manager} says with a shrug.", "gray");
            await terminal.PressAnyKey();
            return;
        }

        long hpNeeded = player.MaxHP - player.HP;
        long maxCost = hpNeeded * FullHealCostPerHP;
        var (_, _, maxCostWithTax) = CityControlSystem.CalculateHealingTaxedPrice(maxCost);
        // Calculate effective per-HP cost including taxes for affordability
        long effectivePerHP = hpNeeded > 0 ? maxCostWithTax / hpNeeded : FullHealCostPerHP;
        if (effectivePerHP < FullHealCostPerHP) effectivePerHP = FullHealCostPerHP;

        terminal.WriteLine($"\"How much HP would you like restored?\"", "cyan");
        terminal.WriteLine($", {Manager} asks.", "gray");
        terminal.WriteLine("");
        terminal.WriteLine($"You need {hpNeeded} HP to be fully healed (costs {maxCostWithTax:N0} gold with taxes).", "gray");
        terminal.WriteLine($"Base cost is {FullHealCostPerHP} gold per HP restored.", "gray");
        terminal.WriteLine("");

        var input = await terminal.GetInput("How much HP to restore (0 to cancel)? ");

        if (!long.TryParse(input, out long hpToHeal) || hpToHeal <= 0)
        {
            terminal.WriteLine("\"Come back when you need healing.\"", "cyan");
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
            terminal.WriteLine("You can't afford that much healing!", "red");
            terminal.WriteLine($"You can afford up to {canAfford} HP.", "yellow");
            await terminal.PressAnyKey();
            return;
        }

        CityControlSystem.Instance.DisplayTaxBreakdown(terminal, "Healing", cost);

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
        terminal.WriteLine($"{Manager} places his hands on your wounds...", "gray");
        await Task.Delay(1000);
        terminal.WriteLine("A warm light flows through you!", "bright_green");
        terminal.WriteLine($"You are healed for {hpToHeal} HP!", "green");
        terminal.WriteLine($"Cost: {healTotalWithTax:N0} gold", "yellow");

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
            terminal.WriteLine($"\"You are already at full health, {player.Name2}!\"", "cyan");
            await terminal.PressAnyKey();
            return;
        }

        long hpNeeded = player.MaxHP - player.HP;
        long cost = hpNeeded * FullHealCostPerHP;
        var (fullHealKingTax, fullHealCityTax, fullHealTotalWithTax) = CityControlSystem.CalculateHealingTaxedPrice(cost);

        terminal.WriteLine($"\"A full restoration will cost you {fullHealTotalWithTax:N0} gold.\"", "cyan");
        terminal.WriteLine($", {Manager} says, examining your wounds.", "gray");
        terminal.WriteLine("");

        CityControlSystem.Instance.DisplayTaxBreakdown(terminal, "Full Healing", cost);

        var confirm = await terminal.GetInput("Proceed with full healing (Y/N)? ");

        if (confirm.ToUpper() != "Y")
        {
            terminal.WriteLine("\"As you wish.\"", "cyan");
            await Task.Delay(1000);
            return;
        }

        if (player.Gold < fullHealTotalWithTax)
        {
            terminal.WriteLine("You can't afford it!", "red");
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
        terminal.WriteLine($"{Manager} begins the healing ritual...", "gray");
        await Task.Delay(500);
        terminal.Write("...", "gray");
        await Task.Delay(500);
        terminal.Write("...", "gray");
        await Task.Delay(500);
        terminal.WriteLine("...", "gray");
        terminal.WriteLine("");
        terminal.WriteLine("Divine light washes over you!", "bright_yellow");
        terminal.WriteLine("You are completely healed!", "bright_green");
        terminal.WriteLine($"HP restored to {player.HP}/{player.MaxHP}", "green");

        // Full heal also restores mana
        if (player.Mana < player.MaxMana)
        {
            player.Mana = player.MaxMana;
            terminal.WriteLine($"Mana restored to {player.Mana}/{player.MaxMana}", "blue");
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
        terminal.WriteLine($"\"Healing potions are {singlePotionWithTax} gold each (with taxes).\"");
        terminal.WriteLine($", {Manager} says, gesturing to his shelf of vials.", "gray");
        terminal.WriteLine("");

        long maxAfford = singlePotionWithTax > 0 ? player.Gold / singlePotionWithTax : 0;
        int maxCanCarry = player.MaxPotions - (int)player.Healing;

        if (maxCanCarry <= 0)
        {
            terminal.WriteLine($"You're carrying the maximum number of healing potions! ({player.MaxPotions})", "red");
            await terminal.PressAnyKey();
            return;
        }

        terminal.WriteLine($"You currently have {player.Healing}/{player.MaxPotions} healing potions.", "gray");
        terminal.WriteLine($"You can afford up to {Math.Min(maxAfford, maxCanCarry)} potions.", "gray");
        terminal.WriteLine("");

        var input = await terminal.GetInput("How many potions to buy (0 to cancel)? ");

        if (!int.TryParse(input, out int quantity) || quantity <= 0)
        {
            terminal.WriteLine("\"Come back when you need supplies.\"", "cyan");
            await Task.Delay(1000);
            return;
        }

        quantity = Math.Min(quantity, maxCanCarry);

        long cost = quantity * HealingPotionCost;
        var (potionKingTax, potionCityTax, potionTotalWithTax) = CityControlSystem.CalculateHealingTaxedPrice(cost);

        if (player.Gold < potionTotalWithTax)
        {
            terminal.WriteLine("You can't afford that many!", "red");
            await terminal.PressAnyKey();
            return;
        }

        CityControlSystem.Instance.DisplayTaxBreakdown(terminal, "Healing Potions", cost);

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
        terminal.WriteLine($"{Manager} hands you {quantity} healing potion{(quantity > 1 ? "s" : "")}.", "gray");
        terminal.WriteLine($"You now have {player.Healing} healing potions.", "green");
        terminal.WriteLine($"Cost: {potionTotalWithTax:N0} gold", "yellow");

        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Buy mana potions
    /// </summary>
    private async Task BuyManaPotions()
    {
        var player = GetCurrentPlayer();

        int potionPrice = Math.Max(75, player.Level * 3);
        int manaRestored = 30 + player.Level * 5;
        var (_, _, singleManaWithTax) = CityControlSystem.CalculateHealingTaxedPrice(potionPrice);

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine($"\"Mana potions are {singleManaWithTax} gold each (with taxes). Each restores {manaRestored} mana.\"");
        terminal.WriteLine($", {Manager} says, pulling blue vials from a shelf.", "gray");
        terminal.WriteLine("");

        long maxAfford = singleManaWithTax > 0 ? player.Gold / singleManaWithTax : 0;
        int maxCanCarry = player.MaxManaPotions - (int)player.ManaPotions;

        terminal.WriteLine($"You currently have {player.ManaPotions} mana potions (max {player.MaxManaPotions}).", "gray");
        terminal.WriteLine($"You can afford up to {Math.Min(maxAfford, maxCanCarry)} potions.", "gray");
        terminal.WriteLine("");

        if (maxCanCarry <= 0)
        {
            terminal.WriteLine("You're carrying the maximum number of mana potions!", "red");
            await terminal.PressAnyKey();
            return;
        }

        var input = await terminal.GetInput("How many potions to buy (0 to cancel)? ");

        if (!int.TryParse(input, out int quantity) || quantity <= 0)
        {
            terminal.WriteLine("\"Come back when you need supplies.\"", "cyan");
            await Task.Delay(1000);
            return;
        }

        quantity = Math.Min(quantity, maxCanCarry);

        long cost = quantity * potionPrice;
        var (_, _, potionTotalWithTax) = CityControlSystem.CalculateHealingTaxedPrice(cost);

        if (player.Gold < potionTotalWithTax)
        {
            terminal.WriteLine("You can't afford that many!", "red");
            await terminal.PressAnyKey();
            return;
        }

        CityControlSystem.Instance.DisplayTaxBreakdown(terminal, "Mana Potions", cost);

        player.Gold -= potionTotalWithTax;
        player.Statistics.RecordPurchase(potionTotalWithTax);
        player.Statistics.RecordGoldSpent(potionTotalWithTax);
        CityControlSystem.Instance.ProcessSaleTax(cost);
        player.ManaPotions += quantity;

        terminal.WriteLine("");
        terminal.WriteLine($"{Manager} hands you {quantity} mana potion{(quantity > 1 ? "s" : "")}.", "gray");
        terminal.WriteLine($"You now have {player.ManaPotions} mana potions.", "blue");
        terminal.WriteLine($"Cost: {potionTotalWithTax:N0} gold", "yellow");

        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Cure poison
    /// </summary>
    private async Task CurePoison()
    {
        var player = GetCurrentPlayer();

        terminal.WriteLine("");
        terminal.WriteLine($"\"Let me check for poison in your blood...\"", "cyan");
        terminal.WriteLine($", {Manager} says, examining you carefully.", "gray");
        terminal.WriteLine("");

        await Task.Delay(1000);

        if (!player.Poisoned)
        {
            terminal.WriteLine("\"You are not poisoned! Your blood is clean.\"", "green");
            await terminal.PressAnyKey();
            return;
        }

        long cost = CalculateDiseaseCost(PoisonBaseCost, player.Level);
        var (poisonKingTax, poisonCityTax, poisonTotalWithTax) = CityControlSystem.CalculateHealingTaxedPrice(cost);

        terminal.WriteLine($"\"Ah yes, I can see the venom coursing through your veins.\"", "cyan");
        terminal.WriteLine($"\"To purge this poison will cost {cost:N0} gold.\"", "cyan");
        terminal.WriteLine("");

        CityControlSystem.Instance.DisplayTaxBreakdown(terminal, "Poison Cure", cost);

        var confirm = await terminal.GetInput("Cure the poison (Y/N)? ");

        if (confirm.ToUpper() != "Y")
        {
            terminal.WriteLine("\"Be careful, the poison will continue to harm you!\"", "yellow");
            await Task.Delay(1500);
            return;
        }

        if (player.Gold < poisonTotalWithTax)
        {
            terminal.WriteLine("You can't afford the antidote!", "red");
            await terminal.PressAnyKey();
            return;
        }

        // Cure poison
        player.Gold -= poisonTotalWithTax;
        player.Statistics.RecordGoldSpent(poisonTotalWithTax);
        CityControlSystem.Instance.ProcessSaleTax(cost);
        player.Poison = 0;

        terminal.WriteLine("");
        terminal.WriteLine($"{Manager} mixes a glowing green antidote...", "gray");
        await Task.Delay(1000);
        terminal.WriteLine("You drink the bitter mixture...", "gray");
        await Task.Delay(1000);
        terminal.WriteLine("The poison is purged from your body!", "bright_green");
        terminal.WriteLine("You are no longer poisoned!", "green");

        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Cure diseases - from Pascal HEALERC.PAS
    /// </summary>
    private async Task CureDisease()
    {
        var player = GetCurrentPlayer();

        terminal.WriteLine("");
        terminal.WriteLine($"\"Alright, let's have a look at you!\"", "cyan");
        terminal.WriteLine($", {Manager} says.", "gray");
        terminal.WriteLine("");

        // Check for diseases
        var diseases = new Dictionary<string, (string Name, long Cost, Action Cure)>();

        if (player.Blind)
            diseases["B"] = ("Blindness", CalculateDiseaseCost(BlindnessBaseCost, player.Level), () => player.Blind = false);
        if (player.Plague)
            diseases["P"] = ("Plague", CalculateDiseaseCost(PlagueBaseCost, player.Level), () => player.Plague = false);
        if (player.Smallpox)
            diseases["S"] = ("Smallpox", CalculateDiseaseCost(SmallpoxBaseCost, player.Level), () => player.Smallpox = false);
        if (player.Measles)
            diseases["M"] = ("Measles", CalculateDiseaseCost(MeaslesBaseCost, player.Level), () => player.Measles = false);
        if (player.Leprosy)
            diseases["L"] = ("Leprosy", CalculateDiseaseCost(LeprosyBaseCost, player.Level), () => player.Leprosy = false);
        if (player.LoversBane)
            diseases["V"] = ("Lover's Bane", CalculateDiseaseCost(LoversBaneBaseCost, player.Level), () => player.LoversBane = false);

        if (diseases.Count == 0)
        {
            terminal.WriteLine("No diseases found!", "green");
            terminal.WriteLine("");
            terminal.WriteLine($"\"You are wasting my time!\"", "cyan");
            terminal.WriteLine($", {Manager} says and returns to his desk.", "gray");
            await terminal.PressAnyKey();
            return;
        }

        // Display diseases
        terminal.SetColor("magenta");
        terminal.WriteLine("Affecting Diseases");
        terminal.WriteLine("------------------");

        long totalCost = 0;
        foreach (var disease in diseases)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"({disease.Key}){disease.Value.Name} - {disease.Value.Cost:N0} gold");
            totalCost += disease.Value.Cost;
        }

        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine($"(C)ure all diseases - {totalCost:N0} gold");
        terminal.WriteLine("");

        var choice = await terminal.GetInput("Choose disease to cure (or C for all, R to cancel): ");
        choice = choice.ToUpper().Trim();

        if (choice == "R" || string.IsNullOrEmpty(choice))
        {
            terminal.WriteLine("\"Come back when you're ready for treatment.\"", "cyan");
            await Task.Delay(1000);
            return;
        }

        if (choice == "C")
        {
            // Cure all
            var (cureAllKingTax, cureAllCityTax, cureAllTotalWithTax) = CityControlSystem.CalculateHealingTaxedPrice(totalCost);

            terminal.WriteLine("");
            terminal.WriteLine($"\"A complete healing process will cost you {totalCost:N0} gold.\"", "cyan");
            terminal.WriteLine($", {Manager} says.", "gray");

            CityControlSystem.Instance.DisplayTaxBreakdown(terminal, "Disease Cures", totalCost);

            var confirm = await terminal.GetInput("Go ahead and pay (Y/N)? ");
            if (confirm.ToUpper() != "Y")
            {
                return;
            }

            if (player.Gold < cureAllTotalWithTax)
            {
                terminal.WriteLine("You can't afford it!", "red");
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
            terminal.WriteLine($"\"For healing {disease.Name} I want {disease.Cost:N0} gold.\"", "cyan");
            terminal.WriteLine($", {Manager} says.", "gray");

            CityControlSystem.Instance.DisplayTaxBreakdown(terminal, "Disease Cure", disease.Cost);

            var confirm = await terminal.GetInput("Go ahead and pay (Y/N)? ");
            if (confirm.ToUpper() != "Y")
            {
                return;
            }

            if (player.Gold < cureOneTotalWithTax)
            {
                terminal.WriteLine("You can't afford it!", "red");
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
            terminal.WriteLine("Invalid choice.", "red");
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
        terminal.WriteLine($"You give {Manager} the gold. He tells you to lay down on a");
        terminal.WriteLine("bed, in a room nearby.");
        terminal.Write("You soon fall asleep");

        for (int i = 0; i < 4; i++)
        {
            await Task.Delay(800);
            terminal.Write("...");
        }

        terminal.WriteLine("");
        terminal.WriteLine("");
        terminal.WriteLine("When you wake up from your well earned sleep, you feel", "gray");
        terminal.WriteLine("much stronger than before!", "green");
        terminal.WriteLine($"You walk out to {Manager}...", "gray");

        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Remove cursed items
    /// </summary>
    private async Task RemoveCursedItem()
    {
        var player = GetCurrentPlayer();

        terminal.WriteLine("");
        terminal.WriteLine($"\"Alright, let's have a look at your equipment!\"", "cyan");
        terminal.WriteLine($", {Manager} says.", "gray");
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
            terminal.WriteLine($"\"Your equipment is alright!\"", "cyan");
            terminal.WriteLine($"{Manager} nods approvingly.", "gray");
            await terminal.PressAnyKey();
            return;
        }

        long cost = CalculateDiseaseCost(CursedItemBaseCost, player.Level);
        var (curseKingTax, curseCityTax, curseTotalWithTax) = CityControlSystem.CalculateHealingTaxedPrice(cost);

        foreach (var item in cursedItems)
        {
            terminal.WriteLine($"Your {item.Name} is CURSED!", "red");
            terminal.WriteLine($"\"It will cost {cost:N0} gold to remove the curse.\"", "cyan");
            terminal.WriteLine("WARNING: The item will be destroyed in the process!", "yellow");
            terminal.WriteLine("");

            CityControlSystem.Instance.DisplayTaxBreakdown(terminal, "Curse Removal", cost);

            var confirm = await terminal.GetInput("Remove the curse (Y/N)? ");

            if (confirm.ToUpper() == "Y")
            {
                if (player.Gold < curseTotalWithTax)
                {
                    terminal.WriteLine("You can't afford it!", "red");
                    continue;
                }

                player.Gold -= curseTotalWithTax;
                player.Statistics.RecordGoldSpent(curseTotalWithTax);
                CityControlSystem.Instance.ProcessSaleTax(cost);

                terminal.WriteLine("");
                terminal.WriteLine($"{Manager} recites some strange spells...", "gray");
                await Task.Delay(500);
                terminal.Write("...", "gray");
                await Task.Delay(500);
                terminal.Write("...", "gray");
                await Task.Delay(500);
                terminal.WriteLine("...", "gray");
                terminal.WriteLine("");
                terminal.WriteLine("Suddenly!", "bright_yellow");
                terminal.WriteLine($"The {item.Name} disintegrates!", "red");
                terminal.WriteLine("");
                terminal.WriteLine($"{Manager} smiles at you. You pay the old man for", "gray");
                terminal.WriteLine("his well performed service.", "gray");

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
        terminal.SetColor("cyan");
        terminal.WriteLine("═══════════════════════════════════════════");
        terminal.WriteLine("             YOUR HEALTH STATUS            ");
        terminal.WriteLine("═══════════════════════════════════════════");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($"Name:  {player.Name2}");
        terminal.WriteLine($"Class: {player.Class}  Race: {player.Race}");
        terminal.WriteLine($"Level: {player.Level}");
        terminal.WriteLine("");

        // HP Bar
        terminal.Write("HP:    ");
        var hpPercent = (float)player.HP / player.MaxHP;
        if (hpPercent >= 0.7f) terminal.SetColor("green");
        else if (hpPercent >= 0.3f) terminal.SetColor("yellow");
        else terminal.SetColor("red");
        terminal.Write($"{player.HP}/{player.MaxHP}");
        terminal.SetColor("gray");
        terminal.WriteLine($"  ({hpPercent * 100:F0}%)");

        terminal.SetColor("yellow");
        terminal.WriteLine($"Gold:  {player.Gold:N0}");

        terminal.SetColor("green");
        terminal.WriteLine($"Healing Potions: {player.Healing}");
        terminal.WriteLine("");

        // Afflictions
        terminal.SetColor("magenta");
        terminal.WriteLine("Affecting Diseases:");
        terminal.WriteLine("=-=-=-=-=-=-=-=-=-=");

        bool hasAffliction = false;

        if (player.Poisoned)
        {
            terminal.WriteLine("*POISONED* - Losing HP each day!", "red");
            hasAffliction = true;
        }
        if (player.Blind)
        {
            terminal.WriteLine("*Blindness* - Reduced accuracy", "red");
            hasAffliction = true;
        }
        if (player.Plague)
        {
            terminal.WriteLine("*Plague* - Severe stat penalties", "red");
            hasAffliction = true;
        }
        if (player.Smallpox)
        {
            terminal.WriteLine("*Smallpox* - Weakened constitution", "red");
            hasAffliction = true;
        }
        if (player.Measles)
        {
            terminal.WriteLine("*Measles* - Reduced abilities", "red");
            hasAffliction = true;
        }
        if (player.Leprosy)
        {
            terminal.WriteLine("*Leprosy* - Severe debilitation", "red");
            hasAffliction = true;
        }
        if (player.LoversBane)
        {
            terminal.WriteLine("*Lover's Bane* - Contracted at Love Street", "red");
            hasAffliction = true;
        }

        if (!hasAffliction)
        {
            terminal.WriteLine("");
            terminal.WriteLine("You are not infected!", "green");
            terminal.WriteLine("Stay healthy!", "green");
        }

        // Drug & addiction status
        terminal.WriteLine("");
        terminal.SetColor("magenta");
        terminal.WriteLine("Drug Status:");
        terminal.WriteLine("=-=-=-=-=-=");

        if (player.OnDrugs && player.ActiveDrug != DrugType.None)
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"Active Drug: {player.ActiveDrug} ({player.DrugEffectDays} days remaining)");
        }
        else
        {
            terminal.SetColor("green");
            terminal.WriteLine("No active drug effects.");
        }

        if (player.IsAddicted)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"*ADDICTED* - Addiction level: {player.Addict}%");
            long rehabCost = GameConfig.RehabBaseCost + (player.Addict * GameConfig.RehabPerAddictionCost);
            terminal.SetColor("yellow");
            terminal.WriteLine($"Rehab treatment available for {rehabCost:N0} gold.");
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
            terminal.WriteLine($"\"{player.Name2}, you show no signs of substance dependency.\"", "cyan");
            terminal.WriteLine($", {Manager} says after examining you.", "gray");
            await terminal.PressAnyKey();
            return;
        }

        long baseCost = GameConfig.RehabBaseCost;
        long addictionCost = player.Addict * GameConfig.RehabPerAddictionCost;
        long totalCost = baseCost + addictionCost;
        var (_, _, totalWithTax) = CityControlSystem.CalculateHealingTaxedPrice(totalCost);

        terminal.SetColor("bright_magenta");
        terminal.WriteLine("═══ Addiction Rehabilitation Program ═══");
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine($"\"{player.Name2}, I can see the substance has taken hold of you.\"");
        terminal.WriteLine($", {Manager} says gravely.", "gray");
        terminal.WriteLine("");

        terminal.SetColor("white");
        if (player.OnDrugs)
            terminal.WriteLine($"  Active Drug: {player.ActiveDrug} ({player.DrugEffectDays} days remaining)");
        if (player.IsAddicted)
            terminal.WriteLine($"  Addiction Level: {player.Addict}%");
        if (player.DrugTolerance != null && player.DrugTolerance.Count > 0)
            terminal.WriteLine($"  Drug Tolerances: {player.DrugTolerance.Count} substance(s)");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine($"\"The rehabilitation treatment will cost {totalCost:N0} gold.\"");
        terminal.SetColor("gray");
        terminal.WriteLine($"  Base cost: {baseCost:N0} gold");
        if (addictionCost > 0)
            terminal.WriteLine($"  Addiction severity surcharge: {addictionCost:N0} gold");
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine("This treatment will:");
        terminal.WriteLine("  - Purge all active drug effects immediately");
        terminal.WriteLine("  - Cure your addiction completely");
        terminal.WriteLine("  - Reset all drug tolerances");
        terminal.WriteLine("");

        CityControlSystem.Instance.DisplayTaxBreakdown(terminal, "Rehab Treatment", totalCost);

        var confirm = await terminal.GetInput("Proceed with rehabilitation (Y/N)? ");

        if (confirm.ToUpper() != "Y")
        {
            terminal.WriteLine($"\"The door is always open when you're ready, {player.Name2}.\"", "cyan");
            await Task.Delay(1000);
            return;
        }

        if (player.Gold < totalWithTax)
        {
            terminal.WriteLine("You can't afford the treatment!", "red");
            terminal.WriteLine($"\"Perhaps you can find the gold somehow... I'll be here.\"", "cyan");
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
        terminal.WriteLine($"{Manager} leads you to a private room in the back...");
        await Task.Delay(1000);
        terminal.WriteLine("He prepares a complex mixture of purifying herbs...");
        await Task.Delay(1000);
        terminal.Write("The treatment begins");
        for (int i = 0; i < 4; i++)
        {
            await Task.Delay(800);
            terminal.Write("...");
        }
        terminal.WriteLine("");
        terminal.WriteLine("");
        terminal.SetColor("bright_green");
        terminal.WriteLine("After hours of painful but effective treatment...");
        await Task.Delay(1000);
        terminal.WriteLine("Your body is cleansed of all substances!", "green");
        terminal.WriteLine("Your addiction has been cured!", "green");
        terminal.WriteLine("All drug tolerances have been reset!", "green");
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine($"\"{player.Name2}, you are free from the chains of addiction.\"");
        terminal.WriteLine($"\"{Manager} smiles warmly. \"Stay clean, my friend.\"", "gray");

        // Track telemetry
        TelemetrySystem.Instance.TrackShopTransaction(
            "healer", "rehab", "addiction_rehab", totalWithTax, player.Level, player.Gold
        );

        await terminal.PressAnyKey();
    }
}
