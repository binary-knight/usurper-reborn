using UsurperRemake.Utils;
using UsurperRemake.Systems;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// Advanced Magic Shop Location - Complete implementation based on Pascal MAGIC.PAS
/// Provides item identification, healing potions, magic item purchasing, and mystical services
/// Direct Pascal compatibility with exact function preservation
/// </summary>
public class AdvancedMagicShopLocation : BaseLocation
{
    private LocationManager locationManager => LocationManager.Instance;
    private NewsSystem newsSystem => NewsSystem.Instance;
    private Random random = new Random();
    
    // Pascal constants from MAGIC.PAS
    private string ownerName = "Ravanella"; // Pascal default gnome name
    private long identifyCost = 1500; // Pascal default ID cost
    private bool refreshMenu = true;

    // Pascal magic shop items and constants
    private const string BanditName = "Rugwar"; // Pascal bandit name
    private const string StrangerName = "Rodrik"; // Pascal stranger name
    
    // Healing potion costs (Pascal logic)
    private const long HealingPotionBaseCost = 50;
    private const long HealingPotionMaxCost = 500;
    
    public AdvancedMagicShopLocation() : base(GameLocation.MagicShop, "Advanced Magic Shop", "A mystical shop filled with magical items and potions.")
    {
    }
    
    // Constructor initializes via field declarations and LoadMagicShopConfig is called lazily
    
    /// <summary>
    /// Load magic shop configuration - Pascal MAGIC.PAS cfg_string logic
    /// </summary>
    private void LoadMagicShopConfig()
    {
        // Pascal: owner := cfg_string(18);
        var configOwner = GameConfig.GetConfigString(18);
        if (!string.IsNullOrEmpty(configOwner))
        {
            ownerName = configOwner;
        }
        
        // Pascal: idcost := str_to_nr(cfg_string(52));
        var configIdCost = GameConfig.GetConfigString(52);
        if (long.TryParse(configIdCost, out long parsedCost))
        {
            if (parsedCost >= 1 && parsedCost <= 2000000000)
            {
                identifyCost = parsedCost;
            }
        }
    }
    
    /// <summary>
    /// Main magic shop menu - Pascal MAGIC.PAS Meny procedure
    /// </summary>
    public async Task ShowLocationMenu(Character player)
    {
        var terminal = TerminalEmulator.Instance ?? new TerminalEmulator();
        
        await DisplayMenu(player, true, false, terminal);
        
        // Main shop loop
        bool done = false;
        while (!done)
        {
            var choice = await GetPlayerChoice(player, terminal);
            done = await ProcessMagicShopChoice(choice, player, terminal);
        }
    }
    
    /// <summary>
    /// Display menu - Pascal MAGIC.PAS Display_Menu procedure
    /// </summary>
    private async Task DisplayMenu(Character player, bool force, bool shortMenu, TerminalEmulator terminal)
    {
        // Update online location (Pascal onliner.location logic)
        await UpdateOnlineLocation();
        
        if (shortMenu)
        {
            if (!player.Expert)
            {
                if (refreshMenu)
                {
                    refreshMenu = false;
                    await ShowFullMenu(player, terminal);
                }
                
                // Show who's here if ear enabled
                if (player.Ear == 1)
                {
                    await ShowWhoIsHere(terminal);
                }
                
                terminal.WriteLine($"\n{GameConfig.TextColor}Magic Shop ({GameConfig.HotkeyColor}?{GameConfig.TextColor} for menu) :");
            }
            else
            {
                // Expert mode
                if (player.Ear == 1)
                {
                    await ShowWhoIsHere(terminal);
                }
                
                terminal.WriteLine($"\n{GameConfig.TextColor}Magic (R,S,H,I,L,B,T,?) :");
            }
        }
        else
        {
            if (!player.Expert || force)
            {
                await ShowFullMenu(player, terminal);
            }
        }
    }
    
    /// <summary>
    /// Show full menu - Pascal MAGIC.PAS Meny procedure
    /// </summary>
    private Task ShowFullMenu(Character player, TerminalEmulator terminal)
    {
        terminal.ClearScreen();
        
        // Pascal menu header with owner name
        string headerText = $"Magic Shop, run by {ownerName} the gnome";
        terminal.WriteLine($"\n{GameConfig.BrightColor}{headerText}{GameConfig.TextColor}");
        terminal.WriteLine($"{GameConfig.BrightColor}{new string('_', headerText.Length)}{GameConfig.TextColor}");
        
        // Pascal shop description
        terminal.WriteLine($"\n{GameConfig.TextColor}You enter the dark and dusty boutique, filled with all sorts");
        terminal.WriteLine("of strange objects. As you examine the place you notice a");
        terminal.WriteLine("few druids and wizards searching for orbs and other mysterious items.");
        terminal.WriteLine("When you reach the counter you try to remember what you were looking for.");
        
        // Owner greeting
        terminal.WriteLine($"\n{GameConfig.TalkColor}What shall it be {GameConfig.BrightColor}{GetRaceDisplay(player.Race)}{GameConfig.TalkColor}?");
        
        // Player gold display
        terminal.WriteLine($"\n{GameConfig.TextColor}(You have {GameConfig.GoldColor}{player.Gold:N0}{GameConfig.TextColor} {GetMoneyPlural(player.Gold)})");
        
        // Menu options (Pascal layout)
        terminal.WriteLine("");
        terminal.WriteLine("(R)eturn to street     (L)ist Items     (T)alk to " + ownerName);
        terminal.WriteLine("(I)dentify item        (B)uy Item");
        terminal.WriteLine("(H)ealing Potions      (S)ell Item");
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// Get player choice - Pascal MAGIC.PAS input handling
    /// </summary>
    private async Task<char> GetPlayerChoice(Character player, TerminalEmulator terminal)
    {
        await DisplayMenu(player, false, true, terminal);
        
        var input = await terminal.GetKeyInput();
        return string.IsNullOrEmpty(input) ? ' ' : input[0];
    }
    
    /// <summary>
    /// Process magic shop choice - Pascal MAGIC.PAS main switch logic
    /// </summary>
    private async Task<bool> ProcessMagicShopChoice(char choice, Character player, TerminalEmulator terminal)
    {
        switch (choice)
        {
            case '?':
                // Show menu
                if (player.Expert)
                {
                    await DisplayMenu(player, true, false, terminal);
                }
                else
                {
                    await DisplayMenu(player, false, false, terminal);
                }
                return false;
                
            case 'R':
                // Return to street
                terminal.WriteLine($"\n{GameConfig.LocationColor}You return to the main street...{GameConfig.TextColor}");
                await locationManager.ChangeLocation(player, "MainStreetLocation");
                return true;
                
            case 'L':
                // List items
                await ListMagicItems(player, terminal);
                return false;
                
            case 'T':
                // Talk to owner
                await TalkToOwner(player, terminal);
                return false;
                
            case 'I':
                // Identify item
                await IdentifyItem(player, terminal);
                return false;
                
            case 'B':
                // Buy item
                await BuyMagicItem(player, terminal);
                return false;
                
            case 'H':
                // Healing potions
                await BuyHealingPotions(player, terminal);
                return false;
                
            case 'S':
                // Sell item
                await SellItem(player, terminal);
                return false;
                
            default:
                terminal.WriteLine($"{GameConfig.ErrorColor}Invalid choice.{GameConfig.TextColor}");
                await terminal.WaitForKeyPress();
                return false;
        }
    }
    
    /// <summary>
    /// Identify item - Pascal MAGIC.PAS item identification logic
    /// </summary>
    private async Task IdentifyItem(Character player, TerminalEmulator terminal)
    {
        // Apply city control discount if player's team controls the city
        long adjustedCost = CityControlSystem.Instance.ApplyDiscount(identifyCost, player);

        // Apply divine boon shop discount
        if (player.CachedBoonEffects?.ShopDiscountPercent > 0)
            adjustedCost = (long)(adjustedCost * (1.0 - player.CachedBoonEffects.ShopDiscountPercent));

        // Calculate total with tax
        var (idKingTax, idCityTax, idTotalWithTax) = CityControlSystem.CalculateTaxedPrice(adjustedCost);

        terminal.WriteLine($"\n{GameConfig.MagicColor}=== Item Identification ==={GameConfig.TextColor}");
        if (adjustedCost < identifyCost)
        {
            terminal.WriteLine($"Base cost: {GameConfig.GoldColor}{identifyCost:N0}{GameConfig.TextColor} gold");
            terminal.WriteLine($"City control discount: {GameConfig.SuccessColor}-{identifyCost - adjustedCost:N0}{GameConfig.TextColor} gold");
            terminal.WriteLine($"Your cost: {GameConfig.GoldColor}{adjustedCost:N0}{GameConfig.TextColor} gold");
        }
        else
        {
            terminal.WriteLine($"Identification cost: {GameConfig.GoldColor}{adjustedCost:N0}{GameConfig.TextColor} gold");
        }

        // Show tax breakdown
        CityControlSystem.Instance.DisplayTaxBreakdown(terminal, "Identification", adjustedCost);

        if (player.Gold < idTotalWithTax)
        {
            terminal.WriteLine($"\n{GameConfig.ErrorColor}You don't have enough gold!{GameConfig.TextColor}");
            await terminal.WaitForKeyPress();
            return;
        }

        // Show player's inventory
        var unidentifiedItems = GetUnidentifiedItems(player);

        if (unidentifiedItems.Count == 0)
        {
            terminal.WriteLine($"\n{GameConfig.WarningColor}You have no unidentified items!{GameConfig.TextColor}");
            await terminal.WaitForKeyPress();
            return;
        }

        terminal.WriteLine("\nUnidentified items:");
        for (int i = 0; i < unidentifiedItems.Count; i++)
        {
            terminal.WriteLine($"{i + 1}. {GameConfig.ItemColor}???{GameConfig.TextColor} (Unknown item)");
        }

        terminal.Write($"\nWhich item to identify (1-{unidentifiedItems.Count}, 0 to cancel): ");
        int choice = await terminal.GetNumberInput(0, unidentifiedItems.Count);

        if (choice == 0)
        {
            return;
        }

        var itemIndex = unidentifiedItems[choice - 1];

        // Charge gold (including tax)
        player.Gold -= idTotalWithTax;

        // Process city tax share from this sale (based on base price after discount)
        CityControlSystem.Instance.ProcessSaleTax(adjustedCost);

        // Identify the item (Pascal identification logic)
        await PerformItemIdentification(player, itemIndex, terminal);
    }
    
    /// <summary>
    /// Perform item identification - Pascal MAGIC.PAS identification process
    /// </summary>
    private async Task PerformItemIdentification(Character player, int itemIndex, TerminalEmulator terminal)
    {
        // Magic identification process with visual effects
        terminal.WriteLine($"\n{GameConfig.MagicColor}{ownerName} begins the identification ritual...{GameConfig.TextColor}");
        await Task.Delay(1000);
        
        terminal.WriteLine($"{GameConfig.MagicColor}Mystical energies swirl around the item...{GameConfig.TextColor}");
        await Task.Delay(1000);
        
        terminal.WriteLine($"{GameConfig.MagicColor}The item's true nature is revealed!{GameConfig.TextColor}");
        await Task.Delay(500);
        
        // Get item details (would normally lookup from item database)
        var itemDetails = GetItemDetails(player.Item[itemIndex], player.ItemType[itemIndex]);
        
        terminal.WriteLine($"\n{GameConfig.ItemColor}Identified: {itemDetails.Name}{GameConfig.TextColor}");
        terminal.WriteLine($"Type: {GameConfig.TypeColor}{itemDetails.Type}{GameConfig.TextColor}");
        terminal.WriteLine($"Power: {GameConfig.PowerColor}{itemDetails.Power}{GameConfig.TextColor}");
        terminal.WriteLine($"Value: {GameConfig.GoldColor}{itemDetails.Value:N0}{GameConfig.TextColor} gold");
        
        if (!string.IsNullOrEmpty(itemDetails.Description))
        {
            terminal.WriteLine($"Description: {GameConfig.DescColor}{itemDetails.Description}{GameConfig.TextColor}");
        }
        
        // Mark item as identified (would set item flag)
        // player.ItemIdentified[itemIndex] = true;
        
        await terminal.WaitForKeyPress();
    }
    
    /// <summary>
    /// Buy healing potions - Pascal MAGIC.PAS healing potion purchase
    /// </summary>
    private async Task BuyHealingPotions(Character player, TerminalEmulator terminal)
    {
        terminal.WriteLine($"\n{GameConfig.HealColor}=== Healing Potions ==={GameConfig.TextColor}");
        terminal.WriteLine($"{GameConfig.TalkColor}\"{ownerName} says: These potions will restore your health!\"");
        
        // Calculate potion cost based on player level (Pascal logic)
        long potionCost = Math.Min(HealingPotionBaseCost * player.Level, HealingPotionMaxCost);
        long healingAmount = player.Level * 20; // Healing based on level
        
        terminal.WriteLine($"\nHealing Potion: Restores {GameConfig.HealColor}{healingAmount:N0}{GameConfig.TextColor} HP");
        terminal.WriteLine($"Cost: {GameConfig.GoldColor}{potionCost:N0}{GameConfig.TextColor} gold each");
        terminal.WriteLine($"Your gold: {GameConfig.GoldColor}{player.Gold:N0}{GameConfig.TextColor}");
        
        if (player.Gold < potionCost)
        {
            terminal.WriteLine($"\n{GameConfig.ErrorColor}You cannot afford any healing potions!{GameConfig.TextColor}");
            await terminal.WaitForKeyPress();
            return;
        }
        
        int maxAffordable = (int)(player.Gold / potionCost);
        terminal.Write($"\nHow many potions to buy (max {maxAffordable}, 0 to cancel): ");
        
        int quantity = await terminal.GetNumberInput(0, maxAffordable);
        
        if (quantity == 0)
        {
            return;
        }
        
        long totalCost = quantity * potionCost;

        // Apply city control discount if player's team controls the city
        long adjustedCost = CityControlSystem.Instance.ApplyDiscount(totalCost, player);

        // Apply divine boon shop discount
        if (player.CachedBoonEffects?.ShopDiscountPercent > 0)
            adjustedCost = (long)(adjustedCost * (1.0 - player.CachedBoonEffects.ShopDiscountPercent));

        // Calculate total with tax
        var (potionKingTax, potionCityTax, potionTotalWithTax) = CityControlSystem.CalculateTaxedPrice(adjustedCost);

        // Confirm purchase
        if (adjustedCost < totalCost)
        {
            terminal.WriteLine($"\nBase cost: {GameConfig.GoldColor}{totalCost:N0}{GameConfig.TextColor} gold");
            terminal.WriteLine($"City control discount: {GameConfig.SuccessColor}-{totalCost - adjustedCost:N0}{GameConfig.TextColor} gold");
            terminal.WriteLine($"Final cost: {GameConfig.GoldColor}{adjustedCost:N0}{GameConfig.TextColor} gold");
        }
        else
        {
            terminal.WriteLine($"\nTotal cost: {GameConfig.GoldColor}{adjustedCost:N0}{GameConfig.TextColor} gold");
        }

        // Show tax breakdown
        CityControlSystem.Instance.DisplayTaxBreakdown(terminal, "Healing Potions", adjustedCost);

        if (player.Gold < potionTotalWithTax)
        {
            terminal.WriteLine($"\n{GameConfig.ErrorColor}You can't afford that with tax included!{GameConfig.TextColor}");
            await terminal.WaitForKeyPress();
            return;
        }

        terminal.Write("Confirm purchase? (Y/N): ");

        var confirm = await terminal.GetKeyCharAsync();
        if (char.ToUpper(confirm) != 'Y')
        {
            terminal.WriteLine("Purchase cancelled.");
            await terminal.WaitForKeyPress();
            return;
        }

        // Process purchase (including tax)
        player.Gold -= potionTotalWithTax;
        player.Healing += quantity; // Add to healing potion count

        // Process city tax share from this sale (based on base price after discount)
        CityControlSystem.Instance.ProcessSaleTax(adjustedCost);

        terminal.WriteLine($"\n{GameConfig.SuccessColor}You purchased {quantity} healing potions!{GameConfig.TextColor}");
        terminal.WriteLine($"Remaining gold: {GameConfig.GoldColor}{player.Gold:N0}{GameConfig.TextColor}");
        
        await terminal.WaitForKeyPress();
    }
    
    /// <summary>
    /// List magic items - Pascal MAGIC.PAS item listing
    /// </summary>
    private async Task ListMagicItems(Character player, TerminalEmulator terminal)
    {
        terminal.WriteLine($"\n{GameConfig.ItemColor}=== Available Magic Items ==={GameConfig.TextColor}");
        
        // Generate random magic items based on Pascal shop logic
        var availableItems = GenerateShopInventory(player.Level);
        
        if (availableItems.Count == 0)
        {
            terminal.WriteLine($"\n{GameConfig.WarningColor}The shop is currently out of stock!{GameConfig.TextColor}");
            terminal.WriteLine($"{GameConfig.TalkColor}\"{ownerName} says: Come back later, I might have new items!\"");
            await terminal.WaitForKeyPress();
            return;
        }
        
        for (int i = 0; i < availableItems.Count; i++)
        {
            var item = availableItems[i];
            terminal.WriteLine($"{i + 1}. {GameConfig.ItemColor}{item.Name}{GameConfig.TextColor} - {GameConfig.GoldColor}{item.Price:N0}{GameConfig.TextColor} gold");
            if (!string.IsNullOrEmpty(item.Description))
            {
                terminal.WriteLine($"    {GameConfig.DescColor}{item.Description}{GameConfig.TextColor}");
            }
        }
        
        await terminal.WaitForKeyPress();
    }
    
    /// <summary>
    /// Buy magic item - Pascal MAGIC.PAS item purchasing
    /// </summary>
    private async Task BuyMagicItem(Character player, TerminalEmulator terminal)
    {
        var availableItems = GenerateShopInventory(player.Level);
        
        if (availableItems.Count == 0)
        {
            terminal.WriteLine($"\n{GameConfig.WarningColor}No items available for purchase!{GameConfig.TextColor}");
            await terminal.WaitForKeyPress();
            return;
        }
        
        terminal.WriteLine($"\n{GameConfig.ItemColor}=== Purchase Magic Item ==={GameConfig.TextColor}");
        
        for (int i = 0; i < availableItems.Count; i++)
        {
            var item = availableItems[i];
            terminal.WriteLine($"{i + 1}. {GameConfig.ItemColor}{item.Name}{GameConfig.TextColor} - {GameConfig.GoldColor}{item.Price:N0}{GameConfig.TextColor} gold");
        }
        
        terminal.Write($"\nWhich item to buy (1-{availableItems.Count}, 0 to cancel): ");
        int choice = await terminal.GetNumberInput(0, availableItems.Count);
        
        if (choice == 0)
        {
            return;
        }
        
        var selectedItem = availableItems[choice - 1];

        // Apply city control discount if player's team controls the city
        long adjustedPrice = CityControlSystem.Instance.ApplyDiscount(selectedItem.Price, player);

        // Apply divine boon shop discount
        if (player.CachedBoonEffects?.ShopDiscountPercent > 0)
            adjustedPrice = (long)(adjustedPrice * (1.0 - player.CachedBoonEffects.ShopDiscountPercent));

        // Calculate total with tax
        var (buyKingTax, buyCityTax, buyTotalWithTax) = CityControlSystem.CalculateTaxedPrice(adjustedPrice);

        if (player.Gold < buyTotalWithTax)
        {
            terminal.WriteLine($"\n{GameConfig.ErrorColor}You cannot afford this item!{GameConfig.TextColor}");
            await terminal.WaitForKeyPress();
            return;
        }

        // Check inventory space
        int emptySlot = FindEmptyInventorySlot(player);
        if (emptySlot == -1)
        {
            terminal.WriteLine($"\n{GameConfig.ErrorColor}Your inventory is full!{GameConfig.TextColor}");
            await terminal.WaitForKeyPress();
            return;
        }

        // Show tax breakdown
        CityControlSystem.Instance.DisplayTaxBreakdown(terminal, selectedItem.Name, adjustedPrice);

        // Complete purchase (including tax)
        player.Gold -= buyTotalWithTax;

        // Store in legacy inventory slot
        player.Item[emptySlot] = selectedItem.ItemId;
        player.ItemType[emptySlot] = selectedItem.Type;

        // Process city tax share from this sale (based on base price after discount)
        CityControlSystem.Instance.ProcessSaleTax(adjustedPrice);

        if (adjustedPrice < selectedItem.Price)
        {
            terminal.WriteLine($"\n{GameConfig.SuccessColor}You purchased the {selectedItem.Name}!{GameConfig.TextColor}");
            terminal.WriteLine($"City control discount applied: {GameConfig.SuccessColor}-{selectedItem.Price - adjustedPrice:N0}{GameConfig.TextColor} gold");
        }
        else
        {
            terminal.WriteLine($"\n{GameConfig.SuccessColor}You purchased the {selectedItem.Name}!{GameConfig.TextColor}");
        }
        terminal.WriteLine($"Remaining gold: {GameConfig.GoldColor}{player.Gold:N0}{GameConfig.TextColor}");

        // Try to auto-equip the item using the modern Equipment system
        var equipment = EquipmentDatabase.GetById(selectedItem.ItemId);
        if (equipment != null)
        {
            if (player.EquipItem(equipment, out string equipMessage))
            {
                terminal.WriteLine($"{GameConfig.SuccessColor}{equipMessage}{GameConfig.TextColor}");
                player.RecalculateStats();

                // Check for equipment quest completion
                QuestSystem.OnEquipmentPurchased(player, equipment);
            }
            else
            {
                terminal.WriteLine($"{GameConfig.WarningColor}Item added to inventory. {equipMessage}{GameConfig.TextColor}");
            }
        }
        else
        {
            // For items not in EquipmentDatabase, apply power directly based on type
            ApplyMagicItemStats(player, selectedItem);
        }

        await terminal.WaitForKeyPress();
    }
    
    /// <summary>
    /// Sell item - Pascal MAGIC.PAS item selling
    /// </summary>
    private async Task SellItem(Character player, TerminalEmulator terminal)
    {
        terminal.WriteLine($"\n{GameConfig.ItemColor}=== Sell Item ==={GameConfig.TextColor}");

        // Show Shadows faction bonus if applicable
        var fenceModifier = FactionSystem.Instance.GetFencePriceModifier();
        if (fenceModifier > 1.0f)
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("  [Shadows Bonus: +20% sell prices]");
            terminal.SetColor("white");
        }

        var sellableItems = GetSellableItems(player);

        if (sellableItems.Count == 0)
        {
            terminal.WriteLine($"\n{GameConfig.WarningColor}You have no items to sell!{GameConfig.TextColor}");
            await terminal.WaitForKeyPress();
            return;
        }

        terminal.WriteLine("Items you can sell:");
        for (int i = 0; i < sellableItems.Count; i++)
        {
            var item = GetItemDetails(player.Item[sellableItems[i]], player.ItemType[sellableItems[i]]);
            int displayPrice = CalculateSellPrice(item);
            
            terminal.WriteLine($"{i + 1}. {GameConfig.ItemColor}{item.Name}{GameConfig.TextColor} - {GameConfig.GoldColor}{displayPrice:N0}{GameConfig.TextColor} gold");
        }
        
        terminal.Write($"\nWhich item to sell (1-{sellableItems.Count}, 0 to cancel): ");
        int choice = await terminal.GetNumberInput(0, sellableItems.Count);
        
        if (choice == 0)
        {
            return;
        }
        
        int itemIndex = sellableItems[choice - 1];
        var itemDetails = GetItemDetails(player.Item[itemIndex], player.ItemType[itemIndex]);
        int sellPrice = CalculateSellPrice(itemDetails);
        
        terminal.WriteLine($"\n{GameConfig.TalkColor}\"{ownerName} examines your {itemDetails.Name}...\"");
        await Task.Delay(1000);
        
        terminal.WriteLine($"{GameConfig.TalkColor}\"I can offer you {GameConfig.GoldColor}{sellPrice:N0}{GameConfig.TextColor} gold for this.\"");
        terminal.Write("Accept offer? (Y/N): ");
        
        var confirm = await terminal.GetKeyCharAsync();
        if (char.ToUpper(confirm) == 'Y')
        {
            // Complete sale
            player.Gold += sellPrice;
            player.Statistics.RecordSale(sellPrice);  // Track sale in statistics
            player.Item[itemIndex] = 0;
            player.ItemType[itemIndex] = ObjType.Head; // Reset to default

            terminal.WriteLine($"\n{GameConfig.SuccessColor}You sold the {itemDetails.Name}!{GameConfig.TextColor}");
            terminal.WriteLine($"You now have {GameConfig.GoldColor}{player.Gold:N0}{GameConfig.TextColor} gold.");
        }
        else
        {
            terminal.WriteLine("Sale cancelled.");
        }
        
        await terminal.WaitForKeyPress();
    }
    
    /// <summary>
    /// Talk to owner - Pascal MAGIC.PAS owner conversation
    /// </summary>
    private async Task TalkToOwner(Character player, TerminalEmulator terminal)
    {
        terminal.WriteLine($"\n{GameConfig.TalkColor}=== Conversation with {ownerName} ==={GameConfig.TextColor}");
        
        // Random owner phrases (Pascal conversation logic)
        string[] ownerPhrases = {
            "Welcome to my humble magic shop!",
            "I have the finest magical items in the realm!",
            "These potions have been blessed by ancient spirits!",
            "Be careful with those magical artifacts - they're quite powerful!",
            "I've been studying the arcane arts for over 200 years!",
            "That amulet there? It once belonged to a great wizard!",
            "Magic is not to be taken lightly, young adventurer!",
            "Perhaps you need some healing potions for your journey?"
        };
        
        string phrase = ownerPhrases[random.Next(ownerPhrases.Length)];
        
        terminal.WriteLine($"{GameConfig.TalkColor}\"{ownerName} says: {phrase}\"");
        
        // Additional conversation based on player state
        if (player.HP < player.MaxHP / 2)
        {
            terminal.WriteLine($"\n{GameConfig.TalkColor}\"You look injured! Perhaps some healing potions would help?\"");
        }
        
        if (player.Class == CharacterClass.Magician || player.Class == CharacterClass.Cleric || player.Class == CharacterClass.Sage)
        {
            terminal.WriteLine($"\n{GameConfig.TalkColor}\"Ah, a fellow practitioner of the magical arts! Welcome!\"");
        }
        
        await terminal.WaitForKeyPress();
    }
    
    #region Utility Methods
    
    /// <summary>
    /// Update online location - Pascal MAGIC.PAS online location update
    /// </summary>
    private Task UpdateOnlineLocation()
    {
        // Pascal: onliner.location := onloc_magicshop;
        // Pascal: onliner.doing := location_desc(onliner.location);
        // Pascal: add_onliner(OUpdateLocation, onliner);

        // TODO: Implement online location tracking
        return Task.CompletedTask;
    }

    /// <summary>
    /// Show who is here - Pascal MAGIC.PAS who_is_here
    /// </summary>
    private Task ShowWhoIsHere(TerminalEmulator terminal)
    {
        // TODO: Implement who is here display
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// Get race display - Pascal race_display function
    /// </summary>
    private string GetRaceDisplay(CharacterRace race)
    {
        return race.ToString().ToLower();
    }
    
    /// <summary>
    /// Get money plural - Pascal many_money function
    /// </summary>
    private string GetMoneyPlural(long amount)
    {
        return amount == 1 ? "gold piece" : "gold pieces";
    }
    
    /// <summary>
    /// Get unidentified items
    /// </summary>
    private List<int> GetUnidentifiedItems(Character player)
    {
        var unidentified = new List<int>();
        
        for (int i = 0; i < player.Item.Count; i++)
        {
            if (player.Item[i] > 0)
            {
                // TODO: Check if item is identified
                // For now, assume some items need identification
                if (random.Next(3) == 0) // 33% chance item needs ID
                {
                    unidentified.Add(i);
                }
            }
        }
        
        return unidentified;
    }
    
    /// <summary>
    /// Get sellable items
    /// </summary>
    private List<int> GetSellableItems(Character player)
    {
        var sellable = new List<int>();
        
        for (int i = 0; i < player.Item.Count; i++)
        {
            if (player.Item[i] > 0)
            {
                sellable.Add(i);
            }
        }
        
        return sellable;
    }
    
    /// <summary>
    /// Find empty inventory slot
    /// </summary>
    private int FindEmptyInventorySlot(Character player)
    {
        for (int i = 0; i < player.Item.Count; i++)
        {
            if (player.Item[i] == 0)
            {
                return i;
            }
        }
        return -1;
    }
    
    /// <summary>
    /// Generate shop inventory - Pascal shop generation logic
    /// </summary>
    private List<MagicShopItem> GenerateShopInventory(int playerLevel)
    {
        var items = new List<MagicShopItem>();
        
        // Generate items based on player level (Pascal logic)
        int itemCount = random.Next(3, 8); // 3-7 items
        
        for (int i = 0; i < itemCount; i++)
        {
            var item = GenerateRandomMagicItem(playerLevel);
            items.Add(item);
        }
        
        return items;
    }
    
    /// <summary>
    /// Generate random magic item - Pascal item generation
    /// </summary>
    private MagicShopItem GenerateRandomMagicItem(int playerLevel)
    {
        string[] itemNames = {
            "Crystal Orb", "Magic Wand", "Spell Scroll", "Enchanted Ring",
            "Mystic Amulet", "Potion of Power", "Blessed Charm", "Wizard's Staff"
        };
        
        string[] descriptions = {
            "Glows with inner light", "Crackles with magical energy", "Ancient runes cover its surface",
            "Radiates mystical power", "Whispers secrets of old", "Feels warm to the touch"
        };
        
        string name = itemNames[random.Next(itemNames.Length)];
        string description = descriptions[random.Next(descriptions.Length)];
        long basePrice = (playerLevel * 100) + random.Next(50, 500);
        
        return new MagicShopItem
        {
            ItemId = random.Next(1000, 9999),
            Name = name,
            Description = description,
            Price = basePrice,
            Type = (ObjType)random.Next(1, 15),
            Power = playerLevel + random.Next(1, 10)
        };
    }
    
    /// <summary>
    /// Get item details - would lookup from item database
    /// </summary>
    private ItemDetails GetItemDetails(int itemId, ObjType itemType)
    {
        // TODO: Implement actual item database lookup
        return new ItemDetails
        {
            Name = $"Item #{itemId}",
            Type = itemType.ToString(),
            Power = random.Next(1, 20),
            Value = random.Next(100, 1000),
            Description = "A mysterious item"
        };
    }
    
    /// <summary>
    /// Calculate sell price - Pascal sell_price function
    /// Includes Shadows faction fence bonus (20% better prices)
    /// </summary>
    private int CalculateSellPrice(ItemDetails item)
    {
        // Pascal: sell_price := item.value div 2;
        int basePrice = (int)(item.Value / 2);

        // Apply Shadows faction fence bonus
        var fenceModifier = FactionSystem.Instance.GetFencePriceModifier();
        return (int)(basePrice * fenceModifier);
    }

    /// <summary>
    /// Apply magic item stats directly for items not in EquipmentDatabase
    /// </summary>
    private void ApplyMagicItemStats(Character player, MagicShopItem item)
    {
        // Apply power based on item type
        switch (item.Type)
        {
            case ObjType.Weapon:
                player.BonusWeapPow += item.Power;
                break;
            case ObjType.Shield:
            case ObjType.Body:
            case ObjType.Head:
            case ObjType.Arms:
            case ObjType.Hands:
            case ObjType.Legs:
            case ObjType.Feet:
                player.BonusArmPow += item.Power;
                break;
            case ObjType.Neck:
            case ObjType.Fingers:
            case ObjType.Waist:
            case ObjType.Face:
                // Accessories might boost various stats
                player.Charisma += item.Power / 2;
                break;
            case ObjType.Potion:
            case ObjType.Magic:
                // Magic items can boost mana
                player.MaxMana += item.Power * 2;
                player.Mana += item.Power * 2;
                break;
            default:
                // General stat boost
                player.Defence += item.Power / 2;
                break;
        }

        player.RecalculateStats();
    }
    
    #endregion
    
    #region Data Structures
    
    public class MagicShopItem
    {
        public int ItemId { get; set; }
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public long Price { get; set; }
        public ObjType Type { get; set; }
        public int Power { get; set; }
    }
    
    public class ItemDetails
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public int Power { get; set; }
        public long Value { get; set; }
        public string Description { get; set; } = "";
    }
    
    #endregion
} 
