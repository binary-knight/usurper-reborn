using UsurperRemake.Utils;
using UsurperRemake.Systems;
using UsurperRemake.BBS;
using UsurperRemake;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Armor Shop Location - Modern RPG slot-based armor system
/// Sells armor pieces for each body slot (Head, Body, Arms, Hands, Legs, Feet, Waist, Face, Cloak)
/// </summary>
public class ArmorShopLocation : BaseLocation
{
    private string shopkeeperName = "Reese";
    private EquipmentSlot? currentSlotCategory = null;
    private int currentPage = 0;
    private const int ItemsPerPage = 15;

    // Armor slots sold in this shop (includes accessories like rings and necklaces)
    private static readonly EquipmentSlot[] ArmorSlots = new[]
    {
        EquipmentSlot.Head,
        EquipmentSlot.Body,
        EquipmentSlot.Arms,
        EquipmentSlot.Hands,
        EquipmentSlot.Legs,
        EquipmentSlot.Feet,
        EquipmentSlot.Waist,
        EquipmentSlot.Face,
        EquipmentSlot.Cloak,
        EquipmentSlot.Neck,
        EquipmentSlot.LFinger,
        EquipmentSlot.RFinger
    };

    public ArmorShopLocation() : base(
        GameLocation.ArmorShop,
        "Armor Shop",
        "You enter the armor shop and notice a strange but appealing smell."
    ) { }

    protected override void SetupLocation()
    {
        base.SetupLocation();
        shopkeeperName = "Reese";
    }

    protected override string GetMudPromptName() => "Armor Shop";

    protected override string[]? GetAmbientMessages() => new[]
    {
        "Chainmail clinks softly as a suit sways on its stand.",
        "The creak of worked leather fills a brief silence.",
        "A faint smell of polish and tallow drifts through the shop.",
        "Distant hammer blows travel through the wall from the smithy.",
        "An armor stand shifts with a dull metallic scrape.",
    };

    protected override void DisplayLocation()
    {
        if (IsBBSSession && currentPlayer != null && currentPlayer.ArmHag >= 1)
        {
            if (currentSlotCategory == null) { DisplayLocationBBS(); return; }
        }

        terminal.ClearScreen();

        if (currentPlayer == null) return;

        // Check if player has been kicked out for bad haggling
        if (currentPlayer.ArmHag < 1)
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine("The strong desk-clerks throw you out!");
            terminal.WriteLine("You realize that you went a little bit too far in");
            terminal.WriteLine("your attempts to get a good deal.");
            terminal.WriteLine("");
            terminal.WriteLine("Press Enter to return to street...", "yellow");
            return;
        }

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔═════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine($"║{"ARMOR SHOP".PadLeft((77 + 10) / 2).PadRight(77)}║");
        terminal.WriteLine("╚═════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine($"Run by {shopkeeperName} the elf");
        terminal.WriteLine("");

        ShowNPCsInLocation();

        if (currentSlotCategory.HasValue)
        {
            // Show items for the selected slot
            ShowSlotItems(currentSlotCategory.Value);
        }
        else
        {
            // Show main menu with slot categories
            ShowMainMenu();
        }
    }

    private void ShowMainMenu()
    {
        ShowShopkeeperMood(shopkeeperName,
            $"{shopkeeperName} appears with a smile: \"What armor piece interests you today?\"");
        terminal.WriteLine("");

        terminal.SetColor("white");
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

        // Show current equipment summary
        ShowEquippedArmor();
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine("Select an armor category:");
        terminal.WriteLine("");

        int num = 1;
        foreach (var slot in ArmorSlots)
        {
            var currentItem = currentPlayer.GetEquipment(slot);
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write($"{num}");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.Write($"{slot.GetDisplayName().PadRight(12)}");

            if (currentItem != null)
            {
                terminal.SetColor("gray");
                terminal.Write(" - ");
                terminal.SetColor("bright_cyan");
                terminal.Write($"{currentItem.Name}");
                terminal.SetColor("gray");
                terminal.Write($" (AC:{currentItem.ArmorClass})");
            }
            else
            {
                terminal.SetColor("darkgray");
                terminal.Write(" - Empty");
            }
            terminal.WriteLine("");
            num++;
        }

        terminal.WriteLine("");

        // Sell option
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("S");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.WriteLine("ell armor");

        // Auto-equip option
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("A");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("magenta");
        terminal.WriteLine($"uto-buy best affordable armor for all slots");

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
        ShowBBSHeader("ARMOR SHOP");

        // 1-line description + gold
        terminal.SetColor("gray");
        terminal.Write($" Run by {shopkeeperName} the elf. You have ");
        terminal.SetColor("yellow");
        terminal.Write(FormatNumber(currentPlayer.Gold));
        terminal.SetColor("gray");
        terminal.WriteLine(" gold.");

        // Total AC summary
        long totalAC = 0;
        foreach (var slot in ArmorSlots)
        {
            var item = currentPlayer.GetEquipment(slot);
            if (item != null) totalAC += item.ArmorClass;
        }
        terminal.SetColor("gray");
        terminal.Write(" Total Armor Class: ");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"{totalAC}");

        // NPCs
        ShowBBSNPCs();
        terminal.WriteLine("");

        // Slot categories in compact rows (3 per row for 12 slots)
        terminal.SetColor("cyan");
        terminal.WriteLine(" Armor Slots:");
        ShowBBSMenuRow(("1", "bright_yellow", "Head"), ("2", "bright_yellow", "Body"), ("3", "bright_yellow", "Arms"), ("4", "bright_yellow", "Hands"));
        ShowBBSMenuRow(("5", "bright_yellow", "Legs"), ("6", "bright_yellow", "Feet"), ("7", "bright_yellow", "Waist"), ("8", "bright_yellow", "Face"));
        ShowBBSMenuRow(("9", "bright_yellow", "Cloak"), ("10", "bright_yellow", "Neck"), ("11", "bright_yellow", "LRing"), ("12", "bright_yellow", "RRing"));

        // Actions
        ShowBBSMenuRow(("S", "bright_green", "ell"), ("A", "bright_magenta", "uto-Buy"), ("R", "bright_red", "eturn"));

        // Footer
        ShowBBSFooter();
    }

    private void ShowEquippedArmor()
    {
        terminal.SetColor("cyan");
        terminal.WriteLine("Current Armor:");

        long totalAC = 0;
        foreach (var slot in ArmorSlots)
        {
            var item = currentPlayer.GetEquipment(slot);
            if (item != null)
            {
                totalAC += item.ArmorClass;
            }
        }

        terminal.SetColor("white");
        terminal.Write("Total Armor Class: ");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"{totalAC}");
    }

    private void ShowSlotItems(EquipmentSlot slot)
    {
        var items = EquipmentDatabase.GetBySlot(slot)
            .OrderBy(i => i.Value)
            .ToList();

        var currentItem = currentPlayer.GetEquipment(slot);

        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"═══ {slot.GetDisplayName()} Armor ═══");
        terminal.WriteLine("");

        if (currentItem != null)
        {
            terminal.SetColor("cyan");
            terminal.Write("Currently Equipped: ");
            terminal.SetColor("bright_white");
            terminal.Write($"{currentItem.Name}");
            terminal.SetColor("gray");
            terminal.WriteLine($" (AC: {currentItem.ArmorClass}, Value: {FormatNumber(currentItem.Value)})");
            terminal.WriteLine("");
        }

        // Paginate items
        int startIndex = currentPage * ItemsPerPage;
        var pageItems = items.Skip(startIndex).Take(ItemsPerPage).ToList();
        int totalPages = (items.Count + ItemsPerPage - 1) / ItemsPerPage;

        terminal.SetColor("gray");
        terminal.WriteLine($"Page {currentPage + 1}/{totalPages} - {items.Count} items total");
        terminal.WriteLine("");

        terminal.SetColor("bright_blue");
        terminal.WriteLine("  #   Name                        Lvl  AC   Price       Bonus");
        terminal.SetColor("darkgray");
        terminal.WriteLine("───────────────────────────────────────────────────────────────");

        int num = 1;
        foreach (var item in pageItems)
        {
            bool canAfford = currentPlayer.Gold >= item.Value;
            bool meetsLevel = currentPlayer.Level >= item.MinLevel;
            bool canBuy = canAfford && meetsLevel;
            bool isUpgrade = currentItem == null || item.ArmorClass > currentItem.ArmorClass;

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

            terminal.SetColor(canBuy ? "bright_cyan" : "darkgray");
            terminal.Write($"{item.ArmorClass,4}  ");

            terminal.SetColor(canBuy ? "yellow" : "darkgray");
            terminal.Write($"{FormatNumber(item.Value),10}  ");

            // Show bonus stats
            var bonuses = GetBonusDescription(item);
            if (!string.IsNullOrEmpty(bonuses))
            {
                terminal.SetColor(canBuy ? "green" : "darkgray");
                terminal.Write(bonuses);
            }

            // Show upgrade indicator
            if (isUpgrade && canBuy)
            {
                terminal.SetColor("bright_green");
                terminal.Write(" ↑");
            }
            else if (!isUpgrade && currentItem != null)
            {
                terminal.SetColor("red");
                terminal.Write(" ↓");
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
        if (item.ConstitutionBonus != 0) bonuses.Add($"Con{(item.ConstitutionBonus > 0 ? "+" : "")}{item.ConstitutionBonus}");
        if (item.IntelligenceBonus != 0) bonuses.Add($"Int{(item.IntelligenceBonus > 0 ? "+" : "")}{item.IntelligenceBonus}");
        if (item.WisdomBonus != 0) bonuses.Add($"Wis{(item.WisdomBonus > 0 ? "+" : "")}{item.WisdomBonus}");
        if (item.MaxHPBonus != 0) bonuses.Add($"HP+{item.MaxHPBonus}");
        if (item.MaxManaBonus != 0) bonuses.Add($"MP+{item.MaxManaBonus}");
        if (item.MagicResistance != 0) bonuses.Add($"MR{item.MagicResistance}%");

        return string.Join(" ", bonuses.Take(3)); // Limit to 3 to fit
    }

    protected override async Task<bool> ProcessChoice(string choice)
    {
        // Handle global quick commands first
        var (handled, shouldExit) = await TryProcessGlobalCommand(choice);
        if (handled) return shouldExit;

        if (currentPlayer == null) return true;

        if (currentPlayer.ArmHag < 1)
        {
            await NavigateToLocation(GameLocation.MainStreet);
            return true;
        }

        var upperChoice = choice.ToUpper().Trim();

        // In slot view
        if (currentSlotCategory.HasValue)
        {
            return await ProcessSlotChoice(upperChoice);
        }

        // In main menu
        switch (upperChoice)
        {
            case "R":
                await NavigateToLocation(GameLocation.MainStreet);
                return true;

            case "S":
                await SellArmor();
                return false;

            case "A":
                await AutoBuyBestArmor();
                return false;

            case "?":
                DisplayLocation();
                return false;

            default:
                // Try to parse as slot number
                if (int.TryParse(upperChoice, out int slotNum) && slotNum >= 1 && slotNum <= ArmorSlots.Length)
                {
                    currentSlotCategory = ArmorSlots[slotNum - 1];
                    currentPage = 0;
                    RequestRedisplay();
                    return false;
                }

                terminal.WriteLine("Invalid choice!", "red");
                await Task.Delay(1000);
                return false;
        }
    }

    private async Task<bool> ProcessSlotChoice(string choice)
    {
        switch (choice)
        {
            case "R":
                await NavigateToLocation(GameLocation.MainStreet);
                return true;

            case "X":
            case "B":
                currentSlotCategory = null;
                currentPage = 0;
                RequestRedisplay();
                return false;

            case "P":
                if (currentPage > 0) currentPage--;
                RequestRedisplay();
                return false;

            case "N":
                if (currentSlotCategory.HasValue)
                {
                    var items = EquipmentDatabase.GetBySlot(currentSlotCategory.Value);
                    int totalPages = (items.Count + ItemsPerPage - 1) / ItemsPerPage;
                    if (currentPage < totalPages - 1) currentPage++;
                }
                RequestRedisplay();
                return false;

            default:
                // Try to parse as item number
                if (currentSlotCategory.HasValue && int.TryParse(choice, out int itemNum) && itemNum >= 1)
                {
                    await BuyItem(currentSlotCategory.Value, itemNum);
                    RequestRedisplay();
                }
                return false;
        }
    }

    private async Task BuyItem(EquipmentSlot slot, int itemIndex)
    {
        var items = EquipmentDatabase.GetBySlot(slot)
            .OrderBy(i => i.Value)
            .ToList();

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
        var totalModifier = alignmentModifier * worldEventModifier;
        long adjustedPrice = (long)(item.Value * totalModifier);

        // Apply city control discount if player's team controls the city
        adjustedPrice = CityControlSystem.Instance.ApplyDiscount(adjustedPrice, currentPlayer);

        // Apply faction discount (The Crown gets 10% off at shops)
        adjustedPrice = (long)(adjustedPrice * FactionSystem.Instance.GetShopPriceModifier());

        // Apply divine boon shop discount
        if (currentPlayer.CachedBoonEffects?.ShopDiscountPercent > 0)
            adjustedPrice = (long)(adjustedPrice * (1.0 - currentPlayer.CachedBoonEffects.ShopDiscountPercent));

        // Apply difficulty-based price multiplier
        adjustedPrice = DifficultySystem.ApplyShopPriceMultiplier(adjustedPrice);

        // Calculate total with tax
        var (armorKingTax, armorCityTax, armorTotalWithTax) = CityControlSystem.CalculateTaxedPrice(adjustedPrice);

        if (currentPlayer.Gold < armorTotalWithTax)
        {
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine($"You need {FormatNumber(armorTotalWithTax)} gold but only have {FormatNumber(currentPlayer.Gold)}!");
            await Pause();
            return;
        }

        // Check alignment requirements
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

        // Show tax breakdown
        CityControlSystem.Instance.DisplayTaxBreakdown(terminal, item.Name, adjustedPrice);

        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.Write($"Buy {item.Name} for ");
        terminal.SetColor("yellow");
        terminal.Write(FormatNumber(armorTotalWithTax));
        terminal.SetColor("white");
        if (armorKingTax > 0 || armorCityTax > 0)
        {
            terminal.SetColor("gray");
            terminal.Write($" (incl. tax)");
            terminal.SetColor("white");
        }
        else if (Math.Abs(totalModifier - 1.0f) > 0.01f)
        {
            terminal.SetColor("gray");
            terminal.Write($" (was {FormatNumber(item.Value)})");
            terminal.SetColor("white");
        }
        terminal.Write(" gold? (Y/N): ");

        var confirm = await terminal.GetInput("");
        if (confirm.ToUpper() != "Y")
        {
            return;
        }

        // Process purchase (total includes tax)
        currentPlayer.Gold -= armorTotalWithTax;
        currentPlayer.Statistics.RecordPurchase(armorTotalWithTax);

        // Show tax hint on first purchase
        HintSystem.Instance.TryShowHint(HintSystem.HINT_FIRST_PURCHASE_TAX, terminal, currentPlayer.HintsShown);

        // Process city tax share from this sale
        CityControlSystem.Instance.ProcessSaleTax(adjustedPrice);

        // Equip the item (will auto-unequip old item)
        if (currentPlayer.EquipItem(item, out string message))
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine("");
            terminal.WriteLine($"You purchased and equipped {item.Name}!");
            terminal.SetColor("gray");
            terminal.WriteLine(message);

            // Recalculate combat stats
            currentPlayer.RecalculateStats();

            // Track shop purchase telemetry
            TelemetrySystem.Instance.TrackShopTransaction(
                "armor", "buy", item.Name, armorTotalWithTax,
                currentPlayer.Level, currentPlayer.Gold
            );

            // Check for equipment quest completion
            QuestSystem.OnEquipmentPurchased(currentPlayer, item);
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine($"Failed to equip: {message}");
            // Refund
            currentPlayer.Gold += armorTotalWithTax;
        }

        // Auto-save after purchase
        await SaveSystem.Instance.AutoSave(currentPlayer);

        await Pause();
    }

    private async Task SellArmor()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("═══ Sell Armor ═══");
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

        // Show equipped armor first
        terminal.SetColor("cyan");
        terminal.WriteLine("[ EQUIPPED ]");

        foreach (var slot in ArmorSlots)
        {
            var item = currentPlayer.GetEquipment(slot);
            if (item != null)
            {
                sellableItems.Add((true, slot, null, item.Name, item.Value, item.IsCursed));
                long sellPrice = (long)((item.Value / 2) * fenceModifier);

                terminal.SetColor("bright_cyan");
                terminal.Write($"{num}. ");
                terminal.SetColor("white");
                terminal.Write($"{slot.GetDisplayName()}: {item.Name}");
                terminal.SetColor("yellow");
                terminal.WriteLine($" - Sell for {FormatNumber(sellPrice)} gold");
                num++;
            }
        }

        // Show inventory armor items
        // Armor types: Head, Body, Arms, Hands, Legs, Feet, Waist, Face, Neck, Abody (cloak)
        var armorObjTypes = new[] { ObjType.Head, ObjType.Body, ObjType.Arms, ObjType.Hands,
                                     ObjType.Legs, ObjType.Feet, ObjType.Waist, ObjType.Face,
                                     ObjType.Neck, ObjType.Abody };

        var inventoryArmor = currentPlayer.Inventory?
            .Select((item, index) => (item, index))
            .Where(x => armorObjTypes.Contains(x.item.Type))
            .ToList() ?? new List<(Item item, int index)>();

        if (inventoryArmor.Count > 0)
        {
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.WriteLine("[ INVENTORY ]");

            foreach (var (item, invIndex) in inventoryArmor)
            {
                sellableItems.Add((false, null, invIndex, item.Name, item.Value, item.IsCursed));
                long displayPrice = (long)((item.Value / 2) * fenceModifier);
                terminal.SetColor("bright_cyan");
                terminal.Write($"{num}. ");
                terminal.SetColor("white");
                terminal.Write($"{item.Name}");
                terminal.Write($" (AC:{item.Armor})");
                terminal.SetColor("yellow");
                terminal.WriteLine($" - Sell for {FormatNumber(displayPrice)} gold");
                num++;
            }
        }

        if (sellableItems.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("You have no armor to sell.");
            await Pause();
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.Write("Sell which piece? (0 to cancel): ");

        var input = await terminal.GetInput("");
        if (!int.TryParse(input, out int sellChoice) || sellChoice < 1 || sellChoice > sellableItems.Count)
        {
            return;
        }

        var selected = sellableItems[sellChoice - 1];
        long price = (long)((selected.value / 2) * fenceModifier);

        // Check if cursed
        if (selected.isCursed)
        {
            terminal.SetColor("red");
            terminal.WriteLine("");
            terminal.WriteLine($"The {selected.name} is CURSED and cannot be sold!");
            await Pause();
            return;
        }

        terminal.SetColor("white");
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
                "armor", "sell", selected.name, price,
                currentPlayer.Level, currentPlayer.Gold
            );

            terminal.SetColor("bright_green");
            terminal.WriteLine("");
            terminal.WriteLine($"Sold {selected.name} for {FormatNumber(price)} gold!");
        }

        await Pause();
    }

    private async Task AutoBuyBestArmor()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("═══ Auto-Buy Best Affordable Armor ═══");
        terminal.WriteLine("");

        int purchased = 0;
        long totalSpent = 0;
        bool cancelled = false;

        foreach (var slot in ArmorSlots)
        {
            if (cancelled) break;

            var currentItem = currentPlayer.GetEquipment(slot);
            int currentAC = currentItem?.ArmorClass ?? 0;

            // Get all affordable upgrades for this slot, sorted by armor class (best first)
            // Filter by CanEquip to exclude items the player can't use (level/stat requirements)
            var affordableArmor = EquipmentDatabase.GetBySlot(slot)
                .Where(i => i.ArmorClass > currentAC)
                .Where(i => i.CanEquip(currentPlayer, out _))
                .Where(i => !i.RequiresGood || currentPlayer.Chivalry > currentPlayer.Darkness)
                .Where(i => !i.RequiresEvil || currentPlayer.Darkness > currentPlayer.Chivalry)
                .OrderByDescending(i => i.ArmorClass)
                .ThenBy(i => i.Value)
                .ToList();

            // Filter to only affordable items based on current gold (include faction + boon discount)
            var factionMod = FactionSystem.Instance.GetShopPriceModifier();
            var boonDiscount = currentPlayer.CachedBoonEffects?.ShopDiscountPercent > 0
                ? (1.0 - currentPlayer.CachedBoonEffects.ShopDiscountPercent) : 1.0;
            var currentlyAffordable = affordableArmor
                .Where(i => (long)(CityControlSystem.Instance.ApplyDiscount(i.Value, currentPlayer) * factionMod * boonDiscount) <= currentPlayer.Gold)
                .ToList();

            if (currentlyAffordable.Count == 0)
            {
                if (currentItem != null)
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine($"  {slot.GetDisplayName()}: {currentItem.Name} is already best affordable");
                }
                else
                {
                    terminal.SetColor("darkgray");
                    terminal.WriteLine($"  {slot.GetDisplayName()}: No affordable armor found");
                }
                continue;
            }

            int armorIndex = 0;
            bool slotHandled = false;

            while (!slotHandled && armorIndex < currentlyAffordable.Count)
            {
                // Re-check affordability since gold may have changed
                var armor = currentlyAffordable[armorIndex];
                long itemPrice = CityControlSystem.Instance.ApplyDiscount(armor.Value, currentPlayer);
                // Apply faction discount (The Crown gets 10% off at shops)
                itemPrice = (long)(itemPrice * FactionSystem.Instance.GetShopPriceModifier());
                // Apply divine boon shop discount
                if (currentPlayer.CachedBoonEffects?.ShopDiscountPercent > 0)
                    itemPrice = (long)(itemPrice * (1.0 - currentPlayer.CachedBoonEffects.ShopDiscountPercent));

                // Calculate total with tax
                var (_, _, abItemTotal) = CityControlSystem.CalculateTaxedPrice(itemPrice);

                if (abItemTotal > currentPlayer.Gold)
                {
                    armorIndex++;
                    continue;
                }

                // Display current slot info
                terminal.WriteLine("");
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"─── {slot.GetDisplayName()} ───");

                if (currentItem != null)
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine($"  Current: {currentItem.Name} (AC: {currentItem.ArmorClass})");
                }
                else
                {
                    terminal.SetColor("darkgray");
                    terminal.WriteLine($"  Current: (empty)");
                }

                // Show the armor offer
                terminal.SetColor("bright_cyan");
                terminal.WriteLine($"  Upgrade: {armor.Name}");
                terminal.SetColor("white");
                terminal.WriteLine($"    Armor Class: {armor.ArmorClass} (+{armor.ArmorClass - currentAC})");
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"    Price: {FormatNumber(itemPrice)} gold");

                // Show tax breakdown
                CityControlSystem.Instance.DisplayTaxBreakdown(terminal, armor.Name, itemPrice);

                terminal.SetColor("gray");
                terminal.WriteLine($"    Your gold: {FormatNumber(currentPlayer.Gold)}");

                terminal.WriteLine("");
                terminal.SetColor("bright_white");
                terminal.WriteLine("  [Y] Buy this armor");
                terminal.WriteLine("  [N] Skip, show next option");
                terminal.WriteLine("  [S] Skip this slot entirely");
                terminal.WriteLine("  [C] Cancel auto-buy");
                terminal.WriteLine("");

                terminal.SetColor("bright_cyan");
                terminal.Write("Your choice: ");
                string choice = await terminal.GetInput("");

                switch (choice.ToUpper().Trim())
                {
                    case "Y":
                        // Purchase the armor (total includes tax)
                        currentPlayer.Gold -= abItemTotal;
                        currentPlayer.Statistics.RecordPurchase(abItemTotal);
                        totalSpent += abItemTotal;

                        // Process city tax share from this sale
                        CityControlSystem.Instance.ProcessSaleTax(itemPrice);

                        if (currentPlayer.EquipItem(armor, out string equipMsg))
                        {
                            purchased++;
                            terminal.SetColor("bright_green");
                            terminal.WriteLine($"  ✓ Purchased {armor.Name}!");

                            // Check for equipment quest completion
                            QuestSystem.OnEquipmentPurchased(currentPlayer, armor);
                        }
                        else
                        {
                            // Refund gold if equip failed (level/stat requirement)
                            currentPlayer.Gold += abItemTotal;
                            totalSpent -= abItemTotal;
                            terminal.SetColor("red");
                            terminal.WriteLine($"  ✗ Cannot equip: {equipMsg}");
                        }
                        slotHandled = true;
                        break;

                    case "N":
                        // Skip to next option for this slot
                        armorIndex++;
                        if (armorIndex >= currentlyAffordable.Count)
                        {
                            terminal.SetColor("gray");
                            terminal.WriteLine($"  No more options for {slot.GetDisplayName()}.");
                            slotHandled = true;
                        }
                        break;

                    case "S":
                        // Skip this slot entirely, move to next body part
                        terminal.SetColor("gray");
                        terminal.WriteLine($"  Skipping {slot.GetDisplayName()}.");
                        slotHandled = true;
                        break;

                    case "C":
                        // Cancel entire auto-buy
                        terminal.SetColor("yellow");
                        terminal.WriteLine("  Auto-buy cancelled.");
                        cancelled = true;
                        slotHandled = true;
                        break;

                    default:
                        terminal.SetColor("red");
                        terminal.WriteLine("  Invalid choice. Please enter Y, N, S, or C.");
                        break;
                }
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"Purchased {purchased} armor pieces for {FormatNumber(totalSpent)} gold.");

        if (purchased > 0)
        {
            currentPlayer.RecalculateStats();

            // Auto-save after purchase
            await SaveSystem.Instance.AutoSave(currentPlayer);
        }

        await Pause();
    }

    private async Task Pause()
    {
        terminal.SetColor("gray");
        terminal.Write("Press ENTER to continue...");
        await terminal.GetInput("");
    }

    private static string FormatNumber(long value)
    {
        return value.ToString("N0");
    }
}
