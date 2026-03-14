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

    // Armor slots sold in this shop (accessories are in the Magic Shop)
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
        EquipmentSlot.Cloak
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
        Loc.Get("armor_shop.ambient_chainmail"),
        Loc.Get("armor_shop.ambient_leather"),
        Loc.Get("armor_shop.ambient_polish"),
        Loc.Get("armor_shop.ambient_hammer"),
        Loc.Get("armor_shop.ambient_stand"),
    };

    protected override void DisplayLocation()
    {
        if (IsScreenReader && currentPlayer != null && currentPlayer.ArmHag >= 1)
        {
            if (currentSlotCategory == null) { DisplayLocationSR(); return; }
        }

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
            terminal.WriteLine(Loc.Get("armor_shop.kicked_out_1"));
            terminal.WriteLine(Loc.Get("armor_shop.kicked_out_2"));
            terminal.WriteLine(Loc.Get("armor_shop.kicked_out_3"));
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("armor_shop.kicked_out_return"), "yellow");
            return;
        }

        WriteBoxHeader(Loc.Get("armor_shop.header"), "bright_cyan");
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("armor_shop.run_by", shopkeeperName));
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
            Loc.Get("armor_shop.shopkeeper_greeting", shopkeeperName));
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.Write(Loc.Get("armor_shop.you_have"));
        terminal.SetColor("yellow");
        terminal.Write(FormatNumber(currentPlayer.Gold));
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("armor_shop.gold_crowns"));

        // Show alignment price modifier
        var alignmentModifier = AlignmentSystem.Instance.GetPriceModifier(currentPlayer, isShadyShop: false);
        if (alignmentModifier != 1.0f)
        {
            var (alignText, alignColor) = AlignmentSystem.Instance.GetAlignmentDisplay(currentPlayer);
            terminal.SetColor(alignColor);
            if (alignmentModifier < 1.0f)
                terminal.WriteLine(Loc.Get("armor_shop.align_discount", alignText, (int)((1.0f - alignmentModifier) * 100)));
            else
                terminal.WriteLine(Loc.Get("armor_shop.align_markup", alignText, (int)((alignmentModifier - 1.0f) * 100)));
        }

        // Show world event price modifier
        var worldEventModifier = WorldEventSystem.Instance.GlobalPriceModifier;
        if (Math.Abs(worldEventModifier - 1.0f) > 0.01f)
        {
            if (worldEventModifier < 1.0f)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("armor_shop.world_discount", (int)((1.0f - worldEventModifier) * 100)));
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("armor_shop.world_markup", (int)((worldEventModifier - 1.0f) * 100)));
            }
        }
        terminal.WriteLine("");

        // Show current equipment summary
        ShowEquippedArmor();
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("shop.select_category"));
        terminal.WriteLine("");

        int num = 1;
        foreach (var slot in ArmorSlots)
        {
            var currentItem = currentPlayer.GetEquipment(slot);
            if (IsScreenReader)
            {
                string slotLabel = currentItem != null
                    ? $"{slot.GetDisplayName()} - {currentItem.Name} (AC:{currentItem.ArmorClass})"
                    : $"{slot.GetDisplayName()} - {Loc.Get("shop.empty")}";
                WriteSRMenuOption($"{num}", slotLabel);
            }
            else
            {
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
                    terminal.Write($" - {Loc.Get("shop.empty")}");
                }
                terminal.WriteLine("");
            }
            num++;
        }

        terminal.WriteLine("");

        WriteSRMenuOption("S", Loc.Get("armor_shop.sell"));
        WriteSRMenuOption("A", Loc.Get("armor_shop.auto_buy"));

        terminal.WriteLine("");
        WriteSRMenuOption("R", Loc.Get("shop.return"));
        terminal.WriteLine("");

        ShowStatusLine();

        // Show first shop hint for new players
        HintSystem.Instance.TryShowHint(HintSystem.HINT_FIRST_SHOP, terminal, currentPlayer.HintsShown);
    }

    private void DisplayLocationSR()
    {
        terminal.ClearScreen();
        terminal.WriteLine(Loc.Get("armor_shop.header"));
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine($"{Loc.Get("armor_shop.run_by", shopkeeperName)} {Loc.Get("shop.you_have", FormatNumber(currentPlayer.Gold))}");

        // Total AC summary
        long totalAC = 0;
        foreach (var slot in ArmorSlots)
        {
            var item = currentPlayer.GetEquipment(slot);
            if (item != null) totalAC += item.ArmorClass;
        }
        terminal.WriteLine(Loc.Get("armor_shop.total_ac", totalAC.ToString()));
        terminal.WriteLine("");

        ShowNPCsInLocation();

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("armor_shop.armor_slots"));
        int num = 1;
        foreach (var slot in ArmorSlots)
        {
            var currentItem = currentPlayer.GetEquipment(slot);
            string slotLabel = currentItem != null
                ? $"{slot.GetDisplayName()} - {currentItem.Name} (AC:{currentItem.ArmorClass})"
                : $"{slot.GetDisplayName()} - {Loc.Get("shop.empty")}";
            WriteSRMenuOption($"{num}", slotLabel);
            num++;
        }
        terminal.WriteLine("");
        WriteSRMenuOption("S", Loc.Get("armor_shop.sell"));
        WriteSRMenuOption("A", Loc.Get("armor_shop.auto_buy"));
        terminal.WriteLine("");
        WriteSRMenuOption("R", Loc.Get("shop.return"));
        terminal.WriteLine("");
        ShowStatusLine();
    }

    /// <summary>
    /// Compact BBS display for 80x25 terminals (main menu only).
    /// </summary>
    private void DisplayLocationBBS()
    {
        terminal.ClearScreen();

        // Header
        ShowBBSHeader(Loc.Get("armor_shop.header"));

        // 1-line description + gold
        terminal.SetColor("gray");
        terminal.Write($" {Loc.Get("armor_shop.run_by", shopkeeperName)} {Loc.Get("armor_shop.you_have")}");
        terminal.SetColor("yellow");
        terminal.Write(FormatNumber(currentPlayer.Gold));
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("armor_shop.gold_suffix"));

        // Total AC summary
        long totalAC = 0;
        foreach (var slot in ArmorSlots)
        {
            var item = currentPlayer.GetEquipment(slot);
            if (item != null) totalAC += item.ArmorClass;
        }
        terminal.SetColor("gray");
        terminal.Write($" {Loc.Get("armor_shop.total_ac_label")} ");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"{totalAC}");

        // NPCs
        ShowBBSNPCs();
        terminal.WriteLine("");

        // Slot categories in compact rows (9 armor slots)
        terminal.SetColor("cyan");
        terminal.WriteLine($" {Loc.Get("armor_shop.armor_slots")}");
        ShowBBSMenuRow(("1", "bright_yellow", Loc.Get("ui.head")), ("2", "bright_yellow", Loc.Get("ui.body")), ("3", "bright_yellow", Loc.Get("ui.arms")), ("4", "bright_yellow", Loc.Get("ui.hands")));
        ShowBBSMenuRow(("5", "bright_yellow", Loc.Get("ui.legs")), ("6", "bright_yellow", Loc.Get("ui.feet")), ("7", "bright_yellow", Loc.Get("ui.waist")), ("8", "bright_yellow", Loc.Get("ui.face")));
        ShowBBSMenuRow(("9", "bright_yellow", Loc.Get("ui.cloak")));

        // Actions
        ShowBBSMenuRow(("S", "bright_green", Loc.Get("armor_shop.bbs_sell")), ("A", "bright_magenta", Loc.Get("armor_shop.bbs_auto_buy")), ("R", "bright_red", Loc.Get("armor_shop.bbs_return")));

        // Footer
        ShowBBSFooter();
    }

    private void ShowEquippedArmor()
    {
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("armor_shop.current_armor"));

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
        terminal.Write($"{Loc.Get("armor_shop.total_ac_label")} ");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"{totalAC}");
    }

    /// <summary>
    /// Get filtered shop armor for a slot, scoped to player level
    /// </summary>
    private List<Equipment> GetShopArmorForSlot(EquipmentSlot slot)
    {
        var items = EquipmentDatabase.GetShopArmor(slot);
        // Show all items — players can buy for inventory to equip on NPCs/companions
        return items;
    }

    private void ShowSlotItems(EquipmentSlot slot)
    {
        var items = GetShopArmorForSlot(slot);

        var currentItem = currentPlayer.GetEquipment(slot);

        WriteSectionHeader(Loc.Get("armor_shop.slot_armor", slot.GetDisplayName()), "bright_yellow");
        terminal.WriteLine("");

        if (currentItem != null)
        {
            terminal.SetColor("cyan");
            terminal.Write(Loc.Get("armor_shop.currently_equipped"));
            terminal.SetColor("bright_white");
            terminal.Write($"{currentItem.Name}");
            terminal.SetColor("gray");
            terminal.WriteLine($" ({Loc.Get("ui.stat_ac")}: {currentItem.ArmorClass}, {Loc.Get("armor_shop.value_label")}: {FormatNumber(currentItem.Value)})");
            terminal.WriteLine("");
        }

        // Paginate items
        int startIndex = currentPage * ItemsPerPage;
        var pageItems = items.Skip(startIndex).Take(ItemsPerPage).ToList();
        int totalPages = (items.Count + ItemsPerPage - 1) / ItemsPerPage;

        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("armor_shop.page_info", currentPage + 1, totalPages, items.Count));
        terminal.WriteLine("");

        terminal.SetColor("bright_blue");
        terminal.WriteLine(Loc.Get("armor_shop.item_header"));
        WriteDivider(63);

        int num = 1;
        foreach (var item in pageItems)
        {
            bool canAfford = currentPlayer.Gold >= item.Value;
            bool meetsLevel = currentPlayer.Level >= item.MinLevel;
            bool isPrestige = currentPlayer.Class >= CharacterClass.Tidesworn;
            bool meetsClass = isPrestige || item.ClassRestrictions == null || item.ClassRestrictions.Count == 0
                || item.ClassRestrictions.Contains(currentPlayer.Class);
            bool canBuy = canAfford && meetsLevel && meetsClass;
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

            // Show class restriction tag
            var classTag = GetClassTag(item);
            if (!string.IsNullOrEmpty(classTag))
            {
                terminal.SetColor(!meetsClass ? "red" : "gray");
                terminal.Write($" [{classTag}]");
            }

            // Show armor weight class tag
            if (item.WeightClass != ArmorWeightClass.None)
            {
                terminal.SetColor(canBuy ? item.WeightClass.GetWeightColor() : "darkgray");
                terminal.Write($" [{item.WeightClass}]");
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
        terminal.Write(Loc.Get("armor_shop.buy_item"));

        if (currentPage > 0)
        {
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("P");
            terminal.SetColor("darkgray");
            terminal.Write($"] {Loc.Get("armor_shop.previous")}   ");
        }

        if (currentPage < totalPages - 1)
        {
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("N");
            terminal.SetColor("darkgray");
            terminal.Write($"] {Loc.Get("armor_shop.next")}   ");
        }

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("B");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("armor_shop.back"));
        terminal.WriteLine("");
    }

    private string GetBonusDescription(Equipment item)
    {
        var bonuses = new List<string>();

        if (item.StrengthBonus != 0) bonuses.Add($"{Loc.Get("ui.stat_str")}+{item.StrengthBonus}");
        if (item.DexterityBonus != 0) bonuses.Add($"{Loc.Get("ui.stat_dex")}+{item.DexterityBonus}");
        if (item.IntelligenceBonus != 0) bonuses.Add($"{Loc.Get("ui.stat_int")}+{item.IntelligenceBonus}");
        if (item.WisdomBonus != 0) bonuses.Add($"{Loc.Get("ui.stat_wis")}+{item.WisdomBonus}");
        if (item.ConstitutionBonus != 0) bonuses.Add($"{Loc.Get("ui.stat_con")}+{item.ConstitutionBonus}");
        if (item.CharismaBonus != 0) bonuses.Add($"{Loc.Get("ui.stat_cha")}+{item.CharismaBonus}");
        if (item.DefenceBonus != 0) bonuses.Add($"{Loc.Get("ui.stat_def")}+{item.DefenceBonus}");
        if (item.AgilityBonus != 0) bonuses.Add($"{Loc.Get("ui.stat_agi")}+{item.AgilityBonus}");
        if (item.MaxHPBonus != 0) bonuses.Add($"{Loc.Get("ui.stat_hp")}+{item.MaxHPBonus}");
        if (item.MaxManaBonus != 0) bonuses.Add($"{Loc.Get("ui.stat_mp")}+{item.MaxManaBonus}");
        if (item.StaminaBonus != 0) bonuses.Add($"{Loc.Get("ui.stat_sta")}+{item.StaminaBonus}");
        if (item.CriticalChanceBonus != 0) bonuses.Add($"{Loc.Get("ui.stat_crit")}+{item.CriticalChanceBonus}%");
        if (item.MagicResistance != 0) bonuses.Add($"{Loc.Get("ui.stat_mr")}+{item.MagicResistance}%");

        return string.Join(" ", bonuses.Take(3)); // Limit to 3 to fit
    }

    private static string GetClassTag(Equipment item)
    {
        if (item.ClassRestrictions == null || item.ClassRestrictions.Count == 0)
            return "";
        var abbrevs = item.ClassRestrictions.Select(c => c switch
        {
            CharacterClass.Warrior => "War",
            CharacterClass.Paladin => "Pal",
            CharacterClass.Barbarian => "Bar",
            CharacterClass.Ranger => "Rng",
            CharacterClass.Assassin => "Asn",
            CharacterClass.Magician => "Mag",
            CharacterClass.Sage => "Sag",
            CharacterClass.Cleric => "Clr",
            CharacterClass.Bard => "Brd",
            CharacterClass.Alchemist => "Alc",
            CharacterClass.Jester => "Jst",
            _ => c.ToString().Substring(0, 3),
        });
        return string.Join("/", abbrevs);
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

                terminal.WriteLine(Loc.Get("ui.invalid_selection"), "red");
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
                    var items = GetShopArmorForSlot(currentSlotCategory.Value);
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
        var items = GetShopArmorForSlot(slot);

        int actualIndex = currentPage * ItemsPerPage + itemIndex - 1;
        if (actualIndex < 0 || actualIndex >= items.Count)
        {
            terminal.WriteLine(Loc.Get("ui.invalid_selection"), "red");
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
            terminal.WriteLine(Loc.Get("shop.insufficient_gold", FormatNumber(armorTotalWithTax), FormatNumber(currentPlayer.Gold)));
            await Pause();
            return;
        }

        // Check if player can personally equip this item
        bool canEquipPersonally = true;
        string cantEquipReason = "";

        if (currentPlayer.Class < CharacterClass.Tidesworn
            && item.ClassRestrictions != null && item.ClassRestrictions.Count > 0
            && !item.ClassRestrictions.Contains(currentPlayer.Class))
        {
            canEquipPersonally = false;
            cantEquipReason = Loc.Get("armor_shop.class_restriction", GetClassTag(item));
        }
        else if (item.RequiresGood && currentPlayer.Chivalry <= currentPlayer.Darkness)
        {
            canEquipPersonally = false;
            cantEquipReason = Loc.Get("ui.requires_good");
        }
        else if (item.RequiresEvil && currentPlayer.Darkness <= currentPlayer.Chivalry)
        {
            canEquipPersonally = false;
            cantEquipReason = Loc.Get("ui.requires_evil");
        }
        else if (currentPlayer.Level < item.MinLevel)
        {
            canEquipPersonally = false;
            cantEquipReason = Loc.Get("armor_shop.requires_level", item.MinLevel, currentPlayer.Level);
        }

        if (!canEquipPersonally)
        {
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("armor_shop.warning_cant_equip", cantEquipReason));
            terminal.WriteLine(Loc.Get("shop.item_to_inventory"));
        }

        // Show tax breakdown
        CityControlSystem.Instance.DisplayTaxBreakdown(terminal, item.Name, adjustedPrice);

        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("armor_shop.buy_prompt_name", item.Name));
        terminal.SetColor("yellow");
        terminal.Write(FormatNumber(armorTotalWithTax));
        terminal.SetColor("white");
        if (armorKingTax > 0 || armorCityTax > 0)
        {
            terminal.SetColor("gray");
            terminal.Write(Loc.Get("armor_shop.incl_tax"));
            terminal.SetColor("white");
        }
        else if (Math.Abs(totalModifier - 1.0f) > 0.01f)
        {
            terminal.SetColor("gray");
            terminal.Write(Loc.Get("armor_shop.was_price", FormatNumber(item.Value)));
            terminal.SetColor("white");
        }
        terminal.Write(Loc.Get("armor_shop.gold_yn"));

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

        if (canEquipPersonally && !currentPlayer.AutoEquipDisabled)
        {
            // Ask whether to equip or send to inventory
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            var equipChoice = await terminal.GetInput(Loc.Get("armor_shop.equip_or_inventory"));
            if (equipChoice.Trim().ToUpper().StartsWith("I"))
            {
                var invItem = currentPlayer.ConvertEquipmentToLegacyItem(item);
                currentPlayer.Inventory.Add(invItem);
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("shop.purchased_inventory", item.Name));
            }
            else
            {
                // Equip the item (will auto-unequip old item)
                if (currentPlayer.EquipItem(item, out string message))
                {
                    terminal.SetColor("bright_green");
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("shop.purchased_equipped", item.Name));
                    terminal.SetColor("gray");
                    terminal.WriteLine(message);

                    // Recalculate combat stats
                    currentPlayer.RecalculateStats();

                }
                else
                {
                    // Equip failed — add to inventory instead
                    var invItem = currentPlayer.ConvertEquipmentToLegacyItem(item);
                    currentPlayer.Inventory.Add(invItem);
                    terminal.SetColor("yellow");
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("armor_shop.couldnt_equip", item.Name));
                }
            }
        }
        else
        {
            // Can't equip personally — add to inventory for companions/NPCs
            var invItem = currentPlayer.ConvertEquipmentToLegacyItem(item);
            currentPlayer.Inventory.Add(invItem);
            terminal.SetColor("bright_green");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("shop.purchased_inventory", item.Name));
        }

        // Track purchase (all paths — equip, inventory, or can't-equip)
        TelemetrySystem.Instance.TrackShopTransaction(
            "armor", "buy", item.Name, armorTotalWithTax,
            currentPlayer.Level, currentPlayer.Gold
        );
        QuestSystem.OnEquipmentPurchased(currentPlayer, item);

        // Auto-save after purchase
        await SaveSystem.Instance.AutoSave(currentPlayer);

        await Pause();
    }

    private async Task SellArmor()
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("armor_shop.sell_armor"), "bright_yellow");
        terminal.WriteLine("");

        // Get Shadows faction fence bonus modifier (1.0 normal, 1.2 with Shadows)
        var fenceModifier = FactionSystem.Instance.GetFencePriceModifier();
        bool hasFenceBonus = fenceModifier > 1.0f;

        if (hasFenceBonus)
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("armor_shop.shadows_bonus"));
            terminal.WriteLine("");
        }

        // Track all sellable items - equipped and inventory
        var sellableItems = new List<(bool isEquipped, EquipmentSlot? slot, int? invIndex, string name, long value, bool isCursed)>();
        int num = 1;

        // Show equipped armor first
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("armor_shop.equipped_header"));

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
                terminal.WriteLine(Loc.Get("armor_shop.sell_for_gold", FormatNumber(sellPrice)));
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
            terminal.WriteLine(Loc.Get("armor_shop.inventory_header"));

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
                terminal.WriteLine(Loc.Get("armor_shop.sell_for_gold", FormatNumber(displayPrice)));
                num++;
            }
        }

        if (sellableItems.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("ui.no_armor_to_sell"));
            await Pause();
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("armor_shop.sell_prompt"));

        var input = (await terminal.GetInput("")).Trim().ToUpper();

        if (input == "A")
        {
            var armorTypes = new[] { ObjType.Body, ObjType.Head, ObjType.Arms, ObjType.Hands,
                ObjType.Legs, ObjType.Feet, ObjType.Waist, ObjType.Face, ObjType.Abody };
            var sellable = currentPlayer.Inventory
                .Where(i => i.IsIdentified && !i.IsCursed && armorTypes.Contains(i.Type))
                .ToList();

            if (sellable.Count == 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("armor_shop.no_sellable_armor"));
                await Pause();
                return;
            }

            long totalGold = sellable.Sum(i => (long)((i.Value / 2) * fenceModifier));
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("armor_shop.bulk_sell_confirm", sellable.Count, FormatNumber(totalGold)));
            var bulkConfirm = (await terminal.GetInput("")).Trim().ToUpper();

            if (bulkConfirm == "Y")
            {
                foreach (var item in sellable)
                    currentPlayer.Inventory.Remove(item);
                currentPlayer.Gold += totalGold;
                currentPlayer.Statistics.RecordSale(totalGold);
                DebugLogger.Instance.LogInfo("GOLD", $"SHOP SELL: {currentPlayer.DisplayName} sold {sellable.Count} armor for {totalGold:N0}g (gold now {currentPlayer.Gold:N0})");
                currentPlayer.RecalculateStats();

                terminal.SetColor("bright_green");
                terminal.WriteLine("");
                terminal.WriteLine(sellable.Count > 1 ? Loc.Get("shop.sold_bulk", sellable.Count, FormatNumber(totalGold)) : Loc.Get("shop.sold_bulk_one", sellable.Count, FormatNumber(totalGold)));
            }
            await Pause();
            return;
        }

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
            terminal.WriteLine(Loc.Get("shop.cursed_cannot_sell", selected.name));
            await Pause();
            return;
        }

        terminal.SetColor("white");
        terminal.Write(Loc.Get("armor_shop.sell_confirm", selected.name, FormatNumber(price)));

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
            currentPlayer.Statistics.RecordSale(price);
            DebugLogger.Instance.LogInfo("GOLD", $"SHOP SELL: {currentPlayer.DisplayName} sold armor for {price:N0}g (gold now {currentPlayer.Gold:N0})");
            currentPlayer.RecalculateStats();

            // Track shop sale telemetry
            TelemetrySystem.Instance.TrackShopTransaction(
                "armor", "sell", selected.name, price,
                currentPlayer.Level, currentPlayer.Gold
            );

            terminal.SetColor("bright_green");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("shop.sold_single", selected.name, FormatNumber(price)));
        }

        await Pause();
    }

    private async Task AutoBuyBestArmor()
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("armor_shop.auto_buy"), "bright_magenta");
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
            var affordableArmor = EquipmentDatabase.GetShopArmor(slot)
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
                    terminal.WriteLine(Loc.Get("armor_shop.autobuy_already_best", slot.GetDisplayName(), currentItem.Name));
                }
                else
                {
                    terminal.SetColor("darkgray");
                    terminal.WriteLine(Loc.Get("armor_shop.autobuy_no_affordable", slot.GetDisplayName()));
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
                WriteSectionHeader(slot.GetDisplayName(), "bright_yellow");

                if (currentItem != null)
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("armor_shop.autobuy_current", currentItem.Name, currentItem.ArmorClass));
                }
                else
                {
                    terminal.SetColor("darkgray");
                    terminal.WriteLine(Loc.Get("armor_shop.autobuy_current_empty"));
                }

                // Show the armor offer
                terminal.SetColor("bright_cyan");
                terminal.WriteLine(Loc.Get("armor_shop.autobuy_upgrade", armor.Name));
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("armor_shop.autobuy_ac", armor.ArmorClass, armor.ArmorClass - currentAC));
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("armor_shop.autobuy_price", FormatNumber(itemPrice)));

                // Show tax breakdown
                CityControlSystem.Instance.DisplayTaxBreakdown(terminal, armor.Name, itemPrice);

                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("armor_shop.autobuy_your_gold", FormatNumber(currentPlayer.Gold)));

                terminal.WriteLine("");
                terminal.SetColor("bright_white");
                terminal.WriteLine(Loc.Get("armor_shop.autobuy_opt_buy"));
                terminal.WriteLine(Loc.Get("armor_shop.autobuy_opt_skip"));
                terminal.WriteLine(Loc.Get("armor_shop.autobuy_opt_skip_slot"));
                terminal.WriteLine(Loc.Get("armor_shop.autobuy_opt_cancel"));
                terminal.WriteLine("");

                terminal.SetColor("bright_cyan");
                terminal.Write(Loc.Get("ui.your_choice"));
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
                            terminal.WriteLine(Loc.Get("armor_shop.autobuy_purchased", armor.Name));

                            // Check for equipment quest completion
                            QuestSystem.OnEquipmentPurchased(currentPlayer, armor);
                        }
                        else
                        {
                            // Refund gold if equip failed (level/stat requirement)
                            currentPlayer.Gold += abItemTotal;
                            totalSpent -= abItemTotal;
                            terminal.SetColor("red");
                            terminal.WriteLine(Loc.Get("armor_shop.autobuy_cant_equip", equipMsg));
                        }
                        slotHandled = true;
                        break;

                    case "N":
                        // Skip to next option for this slot
                        armorIndex++;
                        if (armorIndex >= currentlyAffordable.Count)
                        {
                            terminal.SetColor("gray");
                            terminal.WriteLine(Loc.Get("armor_shop.autobuy_no_more", slot.GetDisplayName()));
                            slotHandled = true;
                        }
                        break;

                    case "S":
                        // Skip this slot entirely, move to next body part
                        terminal.SetColor("gray");
                        terminal.WriteLine(Loc.Get("armor_shop.autobuy_skipping", slot.GetDisplayName()));
                        slotHandled = true;
                        break;

                    case "C":
                        // Cancel entire auto-buy
                        terminal.SetColor("yellow");
                        terminal.WriteLine(Loc.Get("armor_shop.autobuy_cancelled"));
                        cancelled = true;
                        slotHandled = true;
                        break;

                    default:
                        terminal.SetColor("red");
                        terminal.WriteLine(Loc.Get("armor_shop.autobuy_invalid"));
                        break;
                }
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("armor_shop.autobuy_summary", purchased, FormatNumber(totalSpent)));

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
        terminal.Write(Loc.Get("ui.press_enter"));
        await terminal.GetInput("");
    }

    private static string FormatNumber(long value)
    {
        return value.ToString("N0");
    }
}
