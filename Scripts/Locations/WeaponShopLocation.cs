using UsurperRemake.Utils;
using UsurperRemake.Systems;
using UsurperRemake.BBS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Weapon Shop Location - Modern RPG weapon and shield system
/// Sells One-Handed Weapons, Two-Handed Weapons, and Shields
/// Supports dual-wielding configuration
/// </summary>
public class WeaponShopLocation : BaseLocation
{
    private string shopkeeperName = "Tully";
    private WeaponCategory? currentCategory = null;
    private int currentPage = 0;
    private const int ItemsPerPage = 15;

    private enum WeaponCategory
    {
        OneHanded,
        TwoHanded,
        Shields,
        DualWield
    }

    public WeaponShopLocation() : base(
        GameLocation.WeaponShop,
        "Weapon Shop",
        "You enter the dusty old weaponstore filled with all kinds of different weapons."
    ) { }

    protected override void SetupLocation()
    {
        base.SetupLocation();
        shopkeeperName = "Tully";
    }

    protected override string GetMudPromptName() => "Weapon Shop";

    protected override string[]? GetAmbientMessages() => new[]
    {
        "Steel rings against steel somewhere in the back.",
        "The slow grind of a whetstone fills the room.",
        "A hammer pounds away at an anvil in the smithy next door.",
        "The sharp smell of oil and metal hangs in the air.",
        "A blade is drawn from its scabbard and inspected with a soft whistle.",
    };

    protected override void DisplayLocation()
    {
        if (IsBBSSession && currentPlayer != null && currentPlayer.WeapHag >= 1)
        {
            if (currentCategory == null) { DisplayLocationBBS(); return; }
        }

        terminal.ClearScreen();

        if (currentPlayer == null) return;

        // Check if player has been kicked out for bad haggling
        if (currentPlayer.WeapHag < 1)
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine("The big trolls pick you up and throw you out!");
            terminal.WriteLine("Maybe you should be more careful about haggling next time...");
            terminal.WriteLine("");
            terminal.WriteLine("Press Enter to return to street...", "yellow");
            return;
        }

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔═════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine($"║{"WEAPON SHOP".PadLeft((77 + 11) / 2).PadRight(77)}║");
        terminal.WriteLine("╚═════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        ShowNPCsInLocation();

        if (currentCategory.HasValue)
        {
            ShowCategoryItems(currentCategory.Value);
        }
        else
        {
            ShowMainMenu();
        }
    }

    private void ShowMainMenu()
    {
        terminal.SetColor("yellow");
        terminal.WriteLine($"Weaponstore, run by {shopkeeperName} the troll");
        terminal.WriteLine("");

        ShowShopkeeperMood(shopkeeperName,
            "A fat troll stumbles out from a back room and greets you.");
        terminal.WriteLine("");

        terminal.Write("You have ");
        terminal.SetColor("yellow");
        terminal.Write(FormatNumber(currentPlayer.Gold));
        terminal.SetColor("white");
        terminal.WriteLine(" gold crowns.");

        // Show alignment price modifier
        var alignmentModifier = AlignmentSystem.Instance.GetPriceModifier(currentPlayer, isShadyShop: false);
        if (alignmentModifier != 1.0f)
        {
            var (alignText, alignColor) = AlignmentSystem.Instance.GetAlignmentDisplay(currentPlayer);
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

        // Show current weapon configuration
        ShowCurrentWeapons();
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine("Select a category:");
        terminal.WriteLine("");

        // One-handed weapons
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("1");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.WriteLine("One-Handed Weapons (for dual-wield or sword+shield)");

        // Two-handed weapons
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("2");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.WriteLine("Two-Handed Weapons (high damage, both hands occupied)");

        // Shields
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("3");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.WriteLine("Shields (off-hand defense)");

        terminal.WriteLine("");

        // Dual-wield setup
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("D");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("magenta");
        terminal.WriteLine("ual-Wield Setup (equip second weapon in off-hand)");

        // Sell option
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("S");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.WriteLine("ell weapons/shields");

        // Auto-equip
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("A");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("cyan");
        terminal.WriteLine("uto-buy best affordable weapon");

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("R");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("red");
        terminal.WriteLine("eturn to street");
        terminal.WriteLine("");

        ShowStatusLine();

        // Show first shop hint for new players
        HintSystem.Instance.TryShowHint(HintSystem.HINT_FIRST_SHOP, terminal, currentPlayer.HintsShown);
    }

    /// <summary>
    /// Compact BBS display for 80x25 terminals (main menu only).
    /// </summary>
    private void DisplayLocationBBS()
    {
        terminal.ClearScreen();

        // Header
        ShowBBSHeader("WEAPON SHOP");

        // 1-line description
        terminal.SetColor("gray");
        terminal.WriteLine($" Run by {shopkeeperName} the troll. You have {FormatNumber(currentPlayer.Gold)} gold.");

        // Current weapons summary
        var mainHand = currentPlayer.GetEquipment(EquipmentSlot.MainHand);
        var offHand = currentPlayer.GetEquipment(EquipmentSlot.OffHand);
        terminal.SetColor("gray");
        terminal.Write(" Main:");
        terminal.SetColor("white");
        terminal.Write(mainHand != null ? $"{mainHand.Name}" : "Empty");
        terminal.SetColor("gray");
        terminal.Write("  Off:");
        terminal.SetColor("white");
        terminal.WriteLine(offHand != null ? $"{offHand.Name}" : "Empty");

        // NPCs
        ShowBBSNPCs();
        terminal.WriteLine("");

        // Menu
        terminal.SetColor("cyan");
        terminal.WriteLine(" Categories:");
        ShowBBSMenuRow(("1", "bright_yellow", "One-Hand"), ("2", "bright_yellow", "Two-Hand"), ("3", "bright_yellow", "Shields"), ("D", "bright_magenta", "ual-Wield"));
        ShowBBSMenuRow(("S", "bright_green", "ell"), ("A", "bright_cyan", "uto-Buy"), ("R", "bright_red", "eturn"));

        // Footer
        ShowBBSFooter();
    }

    private void ShowCurrentWeapons()
    {
        terminal.SetColor("cyan");
        terminal.WriteLine("Current Weapons:");

        var mainHand = currentPlayer.GetEquipment(EquipmentSlot.MainHand);
        var offHand = currentPlayer.GetEquipment(EquipmentSlot.OffHand);

        terminal.SetColor("white");
        terminal.Write("  Main Hand: ");
        if (mainHand != null)
        {
            terminal.SetColor("bright_white");
            terminal.Write(mainHand.Name);
            terminal.SetColor("gray");
            if (mainHand.Handedness == WeaponHandedness.TwoHanded)
                terminal.WriteLine($" (2H, Pow:{mainHand.WeaponPower})");
            else
                terminal.WriteLine($" (1H, Pow:{mainHand.WeaponPower})");
        }
        else
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine("Empty");
        }

        terminal.SetColor("white");
        terminal.Write("  Off Hand:  ");
        if (offHand != null)
        {
            terminal.SetColor("bright_white");
            terminal.Write(offHand.Name);
            terminal.SetColor("gray");
            if (offHand.WeaponType == WeaponType.Shield || offHand.WeaponType == WeaponType.Buckler || offHand.WeaponType == WeaponType.TowerShield)
                terminal.WriteLine($" (Shield, AC:{offHand.ShieldBonus}, Block:{offHand.BlockChance}%)");
            else
                terminal.WriteLine($" (1H, Pow:{offHand.WeaponPower})");
        }
        else if (mainHand?.Handedness == WeaponHandedness.TwoHanded)
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine("(using 2H weapon)");
        }
        else
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine("Empty");
        }

        // Show weapon configuration
        terminal.SetColor("gray");
        terminal.Write("  Config: ");
        if (currentPlayer.IsTwoHanding)
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("Two-Handed (+25% damage, -15% defense)");
        }
        else if (currentPlayer.IsDualWielding)
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("Dual-Wielding (+1 attack, -10% defense)");
        }
        else if (currentPlayer.HasShieldEquipped)
        {
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("Sword & Board (balanced defense)");
        }
        else if (mainHand != null)
        {
            terminal.SetColor("white");
            terminal.WriteLine("One-Handed (standard)");
        }
        else
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine("Unarmed");
        }

        // Calculate total weapon power
        long totalPow = (mainHand?.WeaponPower ?? 0);
        if (currentPlayer.IsDualWielding)
        {
            totalPow += (offHand?.WeaponPower ?? 0) / 2; // Off-hand at 50%
        }

        terminal.SetColor("white");
        terminal.Write("  Total Weapon Power: ");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"{totalPow}");
    }

    private void ShowCategoryItems(WeaponCategory category)
    {
        List<Equipment> items;
        string categoryName;

        switch (category)
        {
            case WeaponCategory.OneHanded:
                items = EquipmentDatabase.GetWeaponsByHandedness(WeaponHandedness.OneHanded).OrderBy(i => i.Value).ToList();
                categoryName = "One-Handed Weapons";
                break;
            case WeaponCategory.TwoHanded:
                items = EquipmentDatabase.GetWeaponsByHandedness(WeaponHandedness.TwoHanded).OrderBy(i => i.Value).ToList();
                categoryName = "Two-Handed Weapons";
                break;
            case WeaponCategory.Shields:
                items = EquipmentDatabase.GetShields().OrderBy(i => i.Value).ToList();
                categoryName = "Shields";
                break;
            default:
                return;
        }

        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"═══ {categoryName} ═══");
        terminal.WriteLine("");

        // Show current item in this category
        Equipment? currentItem = null;
        if (category == WeaponCategory.Shields)
        {
            currentItem = currentPlayer.GetEquipment(EquipmentSlot.OffHand);
            if (currentItem != null && currentItem.WeaponType != WeaponType.Shield && currentItem.WeaponType != WeaponType.Buckler && currentItem.WeaponType != WeaponType.TowerShield)
                currentItem = null;
        }
        else
        {
            currentItem = currentPlayer.GetEquipment(EquipmentSlot.MainHand);
        }

        if (currentItem != null)
        {
            terminal.SetColor("cyan");
            terminal.Write("Current: ");
            terminal.SetColor("bright_white");
            terminal.Write(currentItem.Name);
            terminal.SetColor("gray");
            if (category == WeaponCategory.Shields)
                terminal.WriteLine($" (AC:{currentItem.ShieldBonus}, Block:{currentItem.BlockChance}%, Value:{FormatNumber(currentItem.Value)})");
            else
                terminal.WriteLine($" (Pow:{currentItem.WeaponPower}, Value:{FormatNumber(currentItem.Value)})");
            terminal.WriteLine("");
        }

        // Paginate
        int startIndex = currentPage * ItemsPerPage;
        var pageItems = items.Skip(startIndex).Take(ItemsPerPage).ToList();
        int totalPages = (items.Count + ItemsPerPage - 1) / ItemsPerPage;

        terminal.SetColor("gray");
        terminal.WriteLine($"Page {currentPage + 1}/{totalPages} - {items.Count} items total");
        terminal.WriteLine("");

        if (category == WeaponCategory.Shields)
        {
            terminal.SetColor("bright_blue");
            terminal.WriteLine("  #   Name                        Lvl  AC   Block  Price       Bonus");
            terminal.SetColor("darkgray");
            terminal.WriteLine("───────────────────────────────────────────────────────────────────────");
        }
        else
        {
            terminal.SetColor("bright_blue");
            terminal.WriteLine("  #   Name                        Lvl  Pow  Type      Price       Bonus");
            terminal.SetColor("darkgray");
            terminal.WriteLine("──────────────────────────────────────────────────────────────────────────");
        }

        int num = 1;
        foreach (var item in pageItems)
        {
            bool canAfford = currentPlayer.Gold >= item.Value;
            bool meetsLevel = currentPlayer.Level >= item.MinLevel;
            bool canBuy = canAfford && meetsLevel;

            terminal.SetColor(canBuy ? "bright_cyan" : "darkgray");
            terminal.Write($"{num,3}. ");

            terminal.SetColor(canBuy ? "white" : "darkgray");
            terminal.Write($"{item.Name,-26}");

            // Level requirement
            if (item.MinLevel > 1)
            {
                terminal.SetColor(!meetsLevel ? "red" : (canBuy ? "bright_cyan" : "darkgray"));
                terminal.Write($"{item.MinLevel,3}  ");
            }
            else
            {
                terminal.SetColor(canBuy ? "bright_cyan" : "darkgray");
                terminal.Write($"{"—",3}  ");
            }

            if (category == WeaponCategory.Shields)
            {
                terminal.SetColor(canBuy ? "bright_cyan" : "darkgray");
                terminal.Write($"{item.ShieldBonus,4}  ");
                terminal.Write($"{item.BlockChance,3}%   ");
            }
            else
            {
                terminal.SetColor(canBuy ? "bright_cyan" : "darkgray");
                terminal.Write($"{item.WeaponPower,4}  ");
                terminal.Write($"{item.WeaponType.ToString().Substring(0, Math.Min(8, item.WeaponType.ToString().Length)),-8}  ");
            }

            terminal.SetColor(canBuy ? "yellow" : "darkgray");
            terminal.Write($"{FormatNumber(item.Value),10}  ");

            // Show bonus stats
            var bonuses = GetBonusDescription(item);
            if (!string.IsNullOrEmpty(bonuses))
            {
                terminal.SetColor(canBuy ? "green" : "darkgray");
                terminal.Write(bonuses);
            }

            terminal.WriteLine("");
            num++;
        }

        terminal.WriteLine("");

        // Navigation
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("#");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.Write("Buy item   ");

        if (currentPage > 0)
        {
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("P");
            terminal.SetColor("darkgray");
            terminal.Write("] Previous   ");
        }

        if (currentPage < totalPages - 1)
        {
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("N");
            terminal.SetColor("darkgray");
            terminal.Write("] Next   ");
        }

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("B");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("red");
        terminal.WriteLine("Back");
        terminal.WriteLine("");
    }

    private string GetBonusDescription(Equipment item)
    {
        var bonuses = new List<string>();

        if (item.StrengthBonus != 0) bonuses.Add($"Str{(item.StrengthBonus > 0 ? "+" : "")}{item.StrengthBonus}");
        if (item.DexterityBonus != 0) bonuses.Add($"Dex{(item.DexterityBonus > 0 ? "+" : "")}{item.DexterityBonus}");
        if (item.CriticalChanceBonus != 0) bonuses.Add($"Crit+{item.CriticalChanceBonus}%");
        if (item.LifeSteal != 0) bonuses.Add($"Leech{item.LifeSteal}%");
        if (item.PoisonDamage != 0) bonuses.Add($"Poison+{item.PoisonDamage}");

        return string.Join(" ", bonuses.Take(3));
    }

    protected override async Task<bool> ProcessChoice(string choice)
    {
        // Handle global quick commands first
        var (handled, shouldExit) = await TryProcessGlobalCommand(choice);
        if (handled) return shouldExit;

        if (currentPlayer == null) return true;

        if (currentPlayer.WeapHag < 1)
        {
            await NavigateToLocation(GameLocation.MainStreet);
            return true;
        }

        var upperChoice = choice.ToUpper().Trim();

        // In category view
        if (currentCategory.HasValue)
        {
            return await ProcessCategoryChoice(upperChoice);
        }

        // In main menu
        switch (upperChoice)
        {
            case "R":
                await NavigateToLocation(GameLocation.MainStreet);
                return true;

            case "1":
                currentCategory = WeaponCategory.OneHanded;
                currentPage = 0;
                RequestRedisplay();
                return false;

            case "2":
                currentCategory = WeaponCategory.TwoHanded;
                currentPage = 0;
                RequestRedisplay();
                return false;

            case "3":
                currentCategory = WeaponCategory.Shields;
                currentPage = 0;
                RequestRedisplay();
                return false;

            case "D":
                await DualWieldSetup();
                return false;

            case "S":
                await SellWeapon();
                return false;

            case "A":
                await AutoBuyBestWeapon();
                return false;

            case "?":
                return false;

            default:
                terminal.WriteLine("Invalid choice!", "red");
                await Task.Delay(1000);
                return false;
        }
    }

    private async Task<bool> ProcessCategoryChoice(string choice)
    {
        switch (choice)
        {
            case "R":
                await NavigateToLocation(GameLocation.MainStreet);
                return true;

            case "X":
            case "B":
                currentCategory = null;
                currentPage = 0;
                RequestRedisplay();
                return false;

            case "P":
                if (currentPage > 0) currentPage--;
                RequestRedisplay();
                return false;

            case "N":
                List<Equipment> items = currentCategory switch
                {
                    WeaponCategory.OneHanded => EquipmentDatabase.GetWeaponsByHandedness(WeaponHandedness.OneHanded),
                    WeaponCategory.TwoHanded => EquipmentDatabase.GetWeaponsByHandedness(WeaponHandedness.TwoHanded),
                    WeaponCategory.Shields => EquipmentDatabase.GetShields(),
                    _ => new List<Equipment>()
                };
                int totalPages = (items.Count + ItemsPerPage - 1) / ItemsPerPage;
                if (currentPage < totalPages - 1) currentPage++;
                RequestRedisplay();
                return false;

            case "D":
                await DualWieldSetup();
                return false;

            case "S":
                await SellWeapon();
                return false;

            default:
                if (int.TryParse(choice, out int itemNum) && itemNum >= 1 && currentCategory.HasValue)
                {
                    await BuyItem(currentCategory.Value, itemNum);
                }
                return false;
        }
    }

    private async Task BuyItem(WeaponCategory category, int itemIndex)
    {
        List<Equipment> items = category switch
        {
            WeaponCategory.OneHanded => EquipmentDatabase.GetWeaponsByHandedness(WeaponHandedness.OneHanded).OrderBy(i => i.Value).ToList(),
            WeaponCategory.TwoHanded => EquipmentDatabase.GetWeaponsByHandedness(WeaponHandedness.TwoHanded).OrderBy(i => i.Value).ToList(),
            WeaponCategory.Shields => EquipmentDatabase.GetShields().OrderBy(i => i.Value).ToList(),
            _ => new List<Equipment>()
        };

        int actualIndex = currentPage * ItemsPerPage + itemIndex - 1;
        if (actualIndex < 0 || actualIndex >= items.Count)
        {
            terminal.WriteLine("Invalid item number!", "red");
            await Task.Delay(1000);
            return;
        }

        var item = items[actualIndex];

        // Apply alignment and world event price modifiers
        var alignmentModifier = AlignmentSystem.Instance.GetPriceModifier(currentPlayer, isShadyShop: false);
        var worldEventModifier = WorldEventSystem.Instance.GlobalPriceModifier;
        long adjustedPrice = (long)(item.Value * alignmentModifier * worldEventModifier);

        // Apply city control discount if player's team controls the city
        adjustedPrice = CityControlSystem.Instance.ApplyDiscount(adjustedPrice, currentPlayer);

        // Apply faction discount (The Crown gets 10% off at shops)
        adjustedPrice = (long)(adjustedPrice * FactionSystem.Instance.GetShopPriceModifier());

        // Apply divine boon shop discount
        if (currentPlayer.CachedBoonEffects?.ShopDiscountPercent > 0)
            adjustedPrice = (long)(adjustedPrice * (1.0 - currentPlayer.CachedBoonEffects.ShopDiscountPercent));

        // Calculate total with tax
        var (kingTax, cityTax, totalWithTax) = CityControlSystem.CalculateTaxedPrice(adjustedPrice);

        if (currentPlayer.Gold < totalWithTax)
        {
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine($"You need {FormatNumber(totalWithTax)} gold but only have {FormatNumber(currentPlayer.Gold)}!");
            await Pause();
            return;
        }

        // Check alignment
        if (item.RequiresGood && currentPlayer.Chivalry <= currentPlayer.Darkness)
        {
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine($"{item.Name} requires a good alignment!");
            await Pause();
            return;
        }

        if (item.RequiresEvil && currentPlayer.Darkness <= currentPlayer.Chivalry)
        {
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine($"{item.Name} requires an evil alignment!");
            await Pause();
            return;
        }

        // Warning for 2H weapons if shield equipped
        if (item.Handedness == WeaponHandedness.TwoHanded && currentPlayer.HasShieldEquipped)
        {
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine("Warning: Two-handed weapons require both hands!");
            terminal.WriteLine("Your shield will be unequipped.");
        }

        // Show tax breakdown
        CityControlSystem.Instance.DisplayTaxBreakdown(terminal, item.Name, adjustedPrice);

        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.Write($"Buy {item.Name} for ");
        terminal.SetColor("yellow");
        terminal.Write(FormatNumber(totalWithTax));
        terminal.SetColor("white");
        if (kingTax > 0 || cityTax > 0)
        {
            terminal.SetColor("gray");
            terminal.Write($" (incl. tax)");
            terminal.SetColor("white");
        }
        else
        {
            var totalModifier = alignmentModifier * worldEventModifier;
            if (Math.Abs(totalModifier - 1.0f) > 0.01f)
            {
                terminal.SetColor("gray");
                terminal.Write($" (was {FormatNumber(item.Value)})");
                terminal.SetColor("white");
            }
        }
        terminal.Write(" gold? (Y/N): ");

        var confirm = await terminal.GetInput("");
        if (confirm.ToUpper() != "Y")
        {
            return;
        }

        currentPlayer.Gold -= totalWithTax;
        currentPlayer.Statistics.RecordPurchase(totalWithTax);

        // Show tax hint on first purchase
        HintSystem.Instance.TryShowHint(HintSystem.HINT_FIRST_PURCHASE_TAX, terminal, currentPlayer.HintsShown);

        // Process city tax share from this sale
        CityControlSystem.Instance.ProcessSaleTax(adjustedPrice);

        // For one-handed weapons, ask which slot to use
        EquipmentSlot? targetSlot = null;
        if (Character.RequiresSlotSelection(item))
        {
            targetSlot = await PromptForWeaponSlot();
            if (targetSlot == null)
            {
                // Player cancelled - refund and add to inventory instead
                currentPlayer.Gold += totalWithTax;
                terminal.SetColor("yellow");
                terminal.WriteLine("Purchase cancelled.");
                await Pause();
                return;
            }
        }

        if (currentPlayer.EquipItem(item, targetSlot, out string message))
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine("");
            terminal.WriteLine($"You purchased and equipped {item.Name}!");
            if (!string.IsNullOrEmpty(message))
            {
                terminal.SetColor("gray");
                terminal.WriteLine(message);
            }
            currentPlayer.RecalculateStats();

            // Track shop purchase telemetry
            TelemetrySystem.Instance.TrackShopTransaction(
                "weapon", "buy", item.Name, totalWithTax,
                currentPlayer.Level, currentPlayer.Gold
            );

            // Check for equipment quest completion
            QuestSystem.OnEquipmentPurchased(currentPlayer, item);
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine($"Failed to equip: {message}");
            currentPlayer.Gold += totalWithTax;
        }

        // Auto-save after purchase
        await SaveSystem.Instance.AutoSave(currentPlayer);

        await Pause();
    }

    private async Task DualWieldSetup()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("═══ Dual-Wield Setup ═══");
        terminal.WriteLine("");

        // Check if using 2H weapon
        if (currentPlayer.IsTwoHanding)
        {
            terminal.SetColor("red");
            terminal.WriteLine("Cannot dual-wield while using a two-handed weapon!");
            terminal.WriteLine("Equip a one-handed weapon first.");
            await Pause();
            return;
        }

        var mainHand = currentPlayer.GetEquipment(EquipmentSlot.MainHand);
        if (mainHand == null || mainHand.Handedness != WeaponHandedness.OneHanded)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("You need a one-handed weapon in your main hand to dual-wield.");
            await Pause();
            return;
        }

        terminal.SetColor("white");
        terminal.WriteLine("Select a one-handed weapon for your off-hand:");
        terminal.WriteLine("");

        var oneHandedWeapons = EquipmentDatabase.GetWeaponsByHandedness(WeaponHandedness.OneHanded)
            .Where(w => currentPlayer.Gold >= w.Value)
            .OrderBy(w => w.Value)
            .Take(15)
            .ToList();

        if (oneHandedWeapons.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("No affordable one-handed weapons available.");
            await Pause();
            return;
        }

        int num = 1;
        foreach (var weapon in oneHandedWeapons)
        {
            terminal.SetColor("bright_cyan");
            terminal.Write($"{num}. ");
            terminal.SetColor("white");
            terminal.Write($"{weapon.Name,-28}");
            terminal.SetColor("bright_cyan");
            terminal.Write($"Pow:{weapon.WeaponPower,3}  ");
            terminal.SetColor("yellow");
            terminal.WriteLine($"{FormatNumber(weapon.Value)}");
            num++;
        }

        terminal.WriteLine("");
        terminal.Write("Select weapon (0 to cancel): ");
        var input = await terminal.GetInput("");

        if (!int.TryParse(input, out int selection) || selection < 1 || selection > oneHandedWeapons.Count)
        {
            return;
        }

        var selectedWeapon = oneHandedWeapons[selection - 1];

        // Apply city control discount
        long adjustedWeaponPrice = CityControlSystem.Instance.ApplyDiscount(selectedWeapon.Value, currentPlayer);

        // Apply divine boon shop discount
        if (currentPlayer.CachedBoonEffects?.ShopDiscountPercent > 0)
            adjustedWeaponPrice = (long)(adjustedWeaponPrice * (1.0 - currentPlayer.CachedBoonEffects.ShopDiscountPercent));

        // Calculate total with tax
        var (_, _, dualWieldTotal) = CityControlSystem.CalculateTaxedPrice(adjustedWeaponPrice);

        if (currentPlayer.Gold < dualWieldTotal)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"You need {FormatNumber(dualWieldTotal)} gold (incl. tax) but only have {FormatNumber(currentPlayer.Gold)}!");
            await Pause();
            return;
        }

        // Show tax breakdown
        CityControlSystem.Instance.DisplayTaxBreakdown(terminal, selectedWeapon.Name, adjustedWeaponPrice);

        currentPlayer.Gold -= dualWieldTotal;

        // Process city tax share from this sale
        CityControlSystem.Instance.ProcessSaleTax(adjustedWeaponPrice);

        // Directly equip to off-hand
        currentPlayer.UnequipSlot(EquipmentSlot.OffHand);
        currentPlayer.EquippedItems[EquipmentSlot.OffHand] = selectedWeapon.Id;
        selectedWeapon.ApplyToCharacter(currentPlayer);

        terminal.SetColor("bright_green");
        terminal.WriteLine("");
        terminal.WriteLine($"Equipped {selectedWeapon.Name} in your off-hand!");
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("You are now dual-wielding! (+1 attack, -10% defense)");

        currentPlayer.RecalculateStats();

        // Auto-save after purchase
        await SaveSystem.Instance.AutoSave(currentPlayer);

        await Pause();
    }

    private async Task SellWeapon()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("═══ Sell Weapons/Shields ═══");
        terminal.WriteLine("");

        // Get Shadows faction fence bonus modifier (1.0 normal, 1.2 with Shadows)
        var fenceModifier = FactionSystem.Instance.GetFencePriceModifier();
        bool hasFenceBonus = fenceModifier > 1.0f;

        if (hasFenceBonus)
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("  [Shadows Bonus: +20% sell prices]");
            terminal.WriteLine("");
        }

        // Track all sellable items - equipped and inventory
        var sellableItems = new List<(bool isEquipped, EquipmentSlot? slot, int? invIndex, string name, long value, bool isCursed)>();
        int num = 1;

        // Show equipped items first
        terminal.SetColor("cyan");
        terminal.WriteLine("[ EQUIPPED ]");

        var mainHand = currentPlayer.GetEquipment(EquipmentSlot.MainHand);
        if (mainHand != null)
        {
            sellableItems.Add((true, EquipmentSlot.MainHand, null, mainHand.Name, mainHand.Value, mainHand.IsCursed));
            long displayPrice = (long)((mainHand.Value / 2) * fenceModifier);
            terminal.SetColor("bright_cyan");
            terminal.Write($"{num}. ");
            terminal.SetColor("white");
            terminal.Write($"Main Hand: {mainHand.Name}");
            terminal.SetColor("yellow");
            terminal.WriteLine($" - Sell for {FormatNumber(displayPrice)} gold");
            num++;
        }

        var offHand = currentPlayer.GetEquipment(EquipmentSlot.OffHand);
        if (offHand != null)
        {
            sellableItems.Add((true, EquipmentSlot.OffHand, null, offHand.Name, offHand.Value, offHand.IsCursed));
            long displayPrice = (long)((offHand.Value / 2) * fenceModifier);
            terminal.SetColor("bright_cyan");
            terminal.Write($"{num}. ");
            terminal.SetColor("white");
            terminal.Write($"Off Hand: {offHand.Name}");
            terminal.SetColor("yellow");
            terminal.WriteLine($" - Sell for {FormatNumber(displayPrice)} gold");
            num++;
        }

        // Show inventory weapons/shields
        var inventoryWeapons = currentPlayer.Inventory?
            .Select((item, index) => (item, index))
            .Where(x => x.item.Type == ObjType.Weapon || x.item.Type == ObjType.Shield)
            .ToList() ?? new List<(Item item, int index)>();

        if (inventoryWeapons.Count > 0)
        {
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.WriteLine("[ INVENTORY ]");

            foreach (var (item, invIndex) in inventoryWeapons)
            {
                sellableItems.Add((false, null, invIndex, item.Name, item.Value, item.IsCursed));
                long displayPrice = (long)((item.Value / 2) * fenceModifier);
                terminal.SetColor("bright_cyan");
                terminal.Write($"{num}. ");
                terminal.SetColor("white");
                terminal.Write($"{item.Name}");
                if (item.Type == ObjType.Weapon)
                    terminal.Write($" (WP:{item.Attack})");
                else
                    terminal.Write($" (Shield)");
                terminal.SetColor("yellow");
                terminal.WriteLine($" - Sell for {FormatNumber(displayPrice)} gold");
                num++;
            }
        }

        if (sellableItems.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("You have no weapons or shields to sell.");
            await Pause();
            return;
        }

        terminal.WriteLine("");
        terminal.Write("Sell which? (0 to cancel): ");
        var input = await terminal.GetInput("");

        if (!int.TryParse(input, out int sellChoice) || sellChoice < 1 || sellChoice > sellableItems.Count)
        {
            return;
        }

        var selected = sellableItems[sellChoice - 1];
        long price = (long)((selected.value / 2) * fenceModifier);

        if (selected.isCursed)
        {
            terminal.SetColor("red");
            terminal.WriteLine("");
            terminal.WriteLine($"The {selected.name} is CURSED and cannot be sold!");
            await Pause();
            return;
        }

        terminal.Write($"Sell {selected.name} for ");
        terminal.SetColor("yellow");
        terminal.Write(FormatNumber(price));
        terminal.SetColor("white");
        terminal.Write(" gold? (Y/N): ");

        var confirm = await terminal.GetInput("");
        if (confirm.ToUpper() == "Y")
        {
            if (selected.isEquipped && selected.slot.HasValue)
            {
                // Unequip and sell equipped item
                currentPlayer.UnequipSlot(selected.slot.Value);
            }
            else if (selected.invIndex.HasValue)
            {
                // Remove from inventory
                currentPlayer.Inventory.RemoveAt(selected.invIndex.Value);
            }

            currentPlayer.Gold += price;
            currentPlayer.Statistics.RecordSale(price);  // Track sale in statistics
            currentPlayer.RecalculateStats();

            // Track shop sale telemetry
            TelemetrySystem.Instance.TrackShopTransaction(
                "weapon", "sell", selected.name, price,
                currentPlayer.Level, currentPlayer.Gold
            );

            terminal.SetColor("bright_green");
            terminal.WriteLine("");
            terminal.WriteLine($"Sold {selected.name} for {FormatNumber(price)} gold!");
        }

        await Pause();
    }

    private async Task AutoBuyBestWeapon()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══ Auto-Buy Best Affordable Weapon ═══");
        terminal.WriteLine("");

        var currentWeapon = currentPlayer.GetEquipment(EquipmentSlot.MainHand);
        int currentPow = currentWeapon?.WeaponPower ?? 0;

        // Get all affordable upgrades sorted by power (best first)
        // Filter by CanEquip to exclude items the player can't use (level/stat requirements)
        var affordableWeapons = EquipmentDatabase.GetWeaponsByHandedness(WeaponHandedness.OneHanded)
            .Concat(EquipmentDatabase.GetWeaponsByHandedness(WeaponHandedness.TwoHanded))
            .Where(w => w.WeaponPower > currentPow)
            .Where(w => w.CanEquip(currentPlayer, out _))
            .Where(w => w.Value <= currentPlayer.Gold)
            .Where(w => !w.RequiresGood || currentPlayer.Chivalry > currentPlayer.Darkness)
            .Where(w => !w.RequiresEvil || currentPlayer.Darkness > currentPlayer.Chivalry)
            .OrderByDescending(w => w.WeaponPower)
            .ThenBy(w => w.Value)
            .ToList();

        if (affordableWeapons.Count == 0)
        {
            if (currentWeapon != null)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"Your {currentWeapon.Name} is already the best weapon you can afford.");
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine("No affordable weapons found.");
            }
            await Pause();
            return;
        }

        terminal.SetColor("white");
        terminal.WriteLine($"Current weapon: {currentWeapon?.Name ?? "(none)"} (WP: {currentPow})");
        terminal.WriteLine($"Your gold: {FormatNumber(currentPlayer.Gold)}");
        terminal.WriteLine("");

        // Iterate through weapons, letting player choose
        int weaponIndex = 0;
        bool purchased = false;

        while (weaponIndex < affordableWeapons.Count)
        {
            // Re-check affordability (gold may have changed)
            var weapon = affordableWeapons[weaponIndex];
            long adjustedPrice = CityControlSystem.Instance.ApplyDiscount(weapon.Value, currentPlayer);
            // Apply faction discount (The Crown gets 10% off at shops)
            adjustedPrice = (long)(adjustedPrice * FactionSystem.Instance.GetShopPriceModifier());

            // Calculate total with tax
            var (abKingTax, abCityTax, abTotal) = CityControlSystem.CalculateTaxedPrice(adjustedPrice);

            if (abTotal > currentPlayer.Gold)
            {
                weaponIndex++;
                continue;
            }

            // Show the weapon offer
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("─────────────────────────────────────");
            terminal.SetColor("cyan");
            terminal.WriteLine($"  {weapon.Name}");
            terminal.SetColor("white");
            terminal.WriteLine($"  Weapon Power: {weapon.WeaponPower} (currently: {currentPow}, +{weapon.WeaponPower - currentPow})");
            terminal.SetColor("yellow");
            terminal.WriteLine($"  Price: {FormatNumber(adjustedPrice)} gold");

            // Show tax breakdown
            CityControlSystem.Instance.DisplayTaxBreakdown(terminal, weapon.Name, adjustedPrice);

            terminal.SetColor("gray");
            terminal.WriteLine($"  (Gold after purchase: {FormatNumber(currentPlayer.Gold - abTotal)})");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("  [Y] Buy this weapon");
            terminal.WriteLine("  [N] Skip, show next option");
            terminal.WriteLine("  [S] Skip weapon slot entirely");
            terminal.WriteLine("  [C] Cancel auto-buy");
            terminal.WriteLine("");
            terminal.Write("Your choice: ");

            var choice = await terminal.GetInput("");
            terminal.WriteLine("");

            switch (choice.ToUpper().Trim())
            {
                case "Y":
                    // Purchase this weapon (total includes tax)
                    currentPlayer.Gold -= abTotal;
                    currentPlayer.Statistics.RecordPurchase(abTotal);
                    CityControlSystem.Instance.ProcessSaleTax(adjustedPrice);

                    // For one-handed weapons, ask which slot to use
                    EquipmentSlot? targetSlot = null;
                    if (Character.RequiresSlotSelection(weapon))
                    {
                        targetSlot = await PromptForWeaponSlot();
                        if (targetSlot == null)
                        {
                            // Player cancelled - refund
                            currentPlayer.Gold += abTotal;
                            terminal.SetColor("yellow");
                            terminal.WriteLine("Purchase cancelled.");
                            await Pause();
                            return;
                        }
                    }

                    if (currentPlayer.EquipItem(weapon, targetSlot, out string message))
                    {
                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"Bought and equipped {weapon.Name}!");
                        if (!string.IsNullOrEmpty(message))
                        {
                            terminal.SetColor("gray");
                            terminal.WriteLine(message);
                        }
                        purchased = true;
                        currentPlayer.RecalculateStats();

                        // Check for equipment quest completion
                        QuestSystem.OnEquipmentPurchased(currentPlayer, weapon);
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine($"Failed: {message}");
                        currentPlayer.Gold += abTotal;
                    }

                    // Done with auto-buy after purchasing
                    if (purchased)
                    {
                        await SaveSystem.Instance.AutoSave(currentPlayer);
                    }
                    await Pause();
                    return;

                case "N":
                    // Skip to next weapon option
                    weaponIndex++;
                    terminal.SetColor("gray");
                    terminal.WriteLine("Skipped.");
                    terminal.WriteLine("");
                    break;

                case "S":
                    // Skip weapon slot entirely
                    terminal.SetColor("gray");
                    terminal.WriteLine("Skipping weapon slot.");
                    await Pause();
                    return;

                case "C":
                    // Cancel entirely
                    terminal.SetColor("yellow");
                    terminal.WriteLine("Auto-buy cancelled.");
                    await Pause();
                    return;

                default:
                    terminal.SetColor("red");
                    terminal.WriteLine("Invalid choice. Please enter Y, N, S, or C.");
                    break;
            }
        }

        // No more options
        terminal.SetColor("gray");
        terminal.WriteLine("No more affordable weapon upgrades available.");
        await Pause();
    }

    private async Task Pause()
    {
        terminal.SetColor("gray");
        terminal.Write("Press ENTER to continue...");
        await terminal.GetInput("");
    }

    /// <summary>
    /// Prompt player to choose which hand to equip a one-handed weapon in
    /// </summary>
    private async Task<EquipmentSlot?> PromptForWeaponSlot()
    {
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine("This is a one-handed weapon. Where would you like to equip it?");
        terminal.WriteLine("");

        // Show current equipment in both slots
        var mainHandItem = currentPlayer.GetEquipment(EquipmentSlot.MainHand);
        var offHandItem = currentPlayer.GetEquipment(EquipmentSlot.OffHand);

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
        terminal.WriteLine("  (C) Cancel");
        terminal.WriteLine("");

        terminal.Write("Your choice: ");
        var slotChoice = await terminal.GetInput("");

        return slotChoice.ToUpper() switch
        {
            "M" => EquipmentSlot.MainHand,
            "O" => EquipmentSlot.OffHand,
            _ => null // Cancel
        };
    }

    private static string FormatNumber(long value)
    {
        return value.ToString("N0");
    }
}
