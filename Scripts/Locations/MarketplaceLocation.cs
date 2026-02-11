using Godot;
using UsurperRemake.Systems;
using GlobalItem = global::Item;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace UsurperRemake.Locations;

/// <summary>
/// Marketplace – player and NPC item trading (simplified port of PLMARKET.PAS).
/// Players may list items for sale and purchase items from other players or NPCs.
/// NPCs actively participate in the marketplace through the WorldSimulator.
/// </summary>
public class MarketplaceLocation : BaseLocation
{
    private const int MaxListingAgeDays = 30; // Auto-expire after 30 days

    public MarketplaceLocation() : base(GameLocation.Marketplace, "Marketplace",
        "Wooden stalls creak under the weight of exotic wares. A bulletin board stands nearby, covered in notes.")
    {
    }

    protected override void SetupLocation()
    {
        PossibleExits.Add(GameLocation.MainStreet);
    }

    protected override void DisplayLocation()
    {
        terminal.ClearScreen();

        // Header - standardized format
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔═════════════════════════════════════════════════════════════════════════════╗");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("║                            PLAYERS' MARKET                                  ║");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╚═════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        ShowNPCsInLocation();

        terminal.SetColor("cyan");
        terminal.WriteLine("Market Actions:");
        terminal.WriteLine("");

        // Row 1
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_cyan");
        terminal.Write("C");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("heck bulletin board   ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_green");
        terminal.Write("B");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("uy item        ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_green");
        terminal.Write("A");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine("dd item");

        // Row 2
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_cyan");
        terminal.Write("S");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("tatus                 ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_red");
        terminal.Write("R");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine("eturn");
        terminal.WriteLine("");

        ShowStatusLine();
    }

    protected override async Task<bool> ProcessChoice(string choice)
    {
        // Handle global quick commands first
        var (handled, shouldExit) = await TryProcessGlobalCommand(choice);
        if (handled) return shouldExit;

        switch (choice.ToUpperInvariant())
        {
            case "C":
                await ShowBoard();
                return false;
            case "B":
                await BuyItem();
                return false;
            case "A":
                await ListItem();
                return false;
            case "S":
                await ShowStatus();
                return false;
            case "R":
            case "Q":
                await NavigateToLocation(GameLocation.MainStreet);
                return true;
            default:
                return await base.ProcessChoice(choice);
        }
    }

    private async Task ShowBoard()
    {
        MarketplaceSystem.Instance.CleanupExpiredListings();
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("BULLETIN BOARD\n");

        var listings = MarketplaceSystem.Instance.GetAllListings();
        if (listings.Count == 0)
        {
            terminal.WriteLine("No items for sale right now.", "yellow");
        }
        else
        {
            int idx = 1;
            foreach (var listing in listings)
            {
                var age = (DateTime.Now - listing.Posted).Days;
                string sellerDisplay = listing.IsNPCSeller ? $"{listing.Seller} (NPC)" : listing.Seller;
                terminal.WriteLine($"[{idx}] {listing.Item.GetDisplayName()} — {listing.Price:N0} {GameConfig.MoneyType} (by {sellerDisplay}, {age}d)");
                idx++;
            }
        }
        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    private async Task BuyItem()
    {
        MarketplaceSystem.Instance.CleanupExpiredListings();
        var listings = MarketplaceSystem.Instance.GetAllListings();

        if (listings.Count == 0)
        {
            terminal.WriteLine("Nothing available for purchase.", "yellow");
            await Task.Delay(1500);
            return;
        }

        await ShowBoard();
        var input = await terminal.GetInput("Enter number of item to buy (or Q to cancel): ");
        if (input.Trim().Equals("Q", StringComparison.OrdinalIgnoreCase)) return;
        if (!int.TryParse(input, out int choice) || choice < 1 || choice > listings.Count)
        {
            terminal.WriteLine("Invalid selection.", "red");
            await Task.Delay(1500);
            return;
        }

        var listing = listings[choice - 1];

        // Don't allow buying your own items
        if (listing.Seller == currentPlayer.DisplayName && !listing.IsNPCSeller)
        {
            terminal.WriteLine("You cannot buy your own listing!", "red");
            await Task.Delay(1500);
            return;
        }

        if (currentPlayer.Gold < listing.Price)
        {
            terminal.WriteLine("You cannot afford that item!", "red");
            await Task.Delay(1500);
            return;
        }

        // Find the actual index in the MarketplaceSystem listings
        int actualIndex = MarketplaceSystem.Instance.Listings.IndexOf(listing);
        if (actualIndex < 0)
        {
            terminal.WriteLine("Item no longer available.", "red");
            await Task.Delay(1500);
            return;
        }

        // Process the purchase
        if (MarketplaceSystem.Instance.PurchaseItem(actualIndex, currentPlayer))
        {
            var purchasedItem = listing.Item.Clone();
            currentPlayer.Inventory.Add(purchasedItem);
            terminal.WriteLine("Transaction complete! The item is now yours.", "bright_green");

            // Auto-equip if it's better than current weapon/armor (legacy system compatibility)
            bool equipped = false;
            if (purchasedItem.Type == global::ObjType.Weapon && purchasedItem.Attack > currentPlayer.WeapPow)
            {
                currentPlayer.WeapPow = purchasedItem.Attack;
                terminal.WriteLine($"Equipped {purchasedItem.Name}! (+{purchasedItem.Attack} weapon power)", "bright_cyan");
                equipped = true;
            }
            else if ((purchasedItem.Type == global::ObjType.Body || purchasedItem.Type == global::ObjType.Head ||
                      purchasedItem.Type == global::ObjType.Arms || purchasedItem.Type == global::ObjType.Legs)
                     && purchasedItem.Armor > currentPlayer.ArmPow)
            {
                currentPlayer.ArmPow = purchasedItem.Armor;
                terminal.WriteLine($"Equipped {purchasedItem.Name}! (+{purchasedItem.Armor} armor power)", "bright_cyan");
                equipped = true;
            }

            // Apply any stat bonuses from the item
            if (purchasedItem.Strength > 0) currentPlayer.Strength += purchasedItem.Strength;
            if (purchasedItem.Defence > 0) currentPlayer.Defence += purchasedItem.Defence;
            if (purchasedItem.HP > 0) { currentPlayer.MaxHP += purchasedItem.HP; currentPlayer.HP += purchasedItem.HP; }
            if (purchasedItem.Mana > 0) { currentPlayer.MaxMana += purchasedItem.Mana; currentPlayer.Mana += purchasedItem.Mana; }

            // Recalculate stats if item was equipped or had bonuses
            if (equipped || purchasedItem.Strength > 0 || purchasedItem.Defence > 0 || purchasedItem.HP > 0)
            {
                currentPlayer.RecalculateStats();
            }

            // Generate news for NPC seller transactions
            if (listing.IsNPCSeller)
            {
                NewsSystem.Instance?.Newsy(false,
                    $"{currentPlayer.DisplayName} purchased {listing.Item.Name} from {listing.Seller} at the marketplace.");
            }
        }
        else
        {
            terminal.WriteLine("Transaction failed!", "red");
        }

        await Task.Delay(2000);
    }

    private async Task ListItem()
    {
        // Show player inventory with index numbers
        var sellable = currentPlayer.Inventory.Where(it => it != null).ToList();
        if (sellable.Count == 0)
        {
            terminal.WriteLine("You have nothing to sell.", "yellow");
            await Task.Delay(1500);
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("cyan");
        terminal.WriteLine("Your Inventory:");
        for (int i = 0; i < sellable.Count; i++)
        {
            terminal.WriteLine($"{i + 1}. {sellable[i].GetDisplayName()} (Value: {sellable[i].Value:N0})");
        }

        var input = await terminal.GetInput("Enter number of item to sell (or Q to cancel): ");
        if (input.Trim().Equals("Q", StringComparison.OrdinalIgnoreCase)) return;
        if (!int.TryParse(input, out int choice) || choice < 1 || choice > sellable.Count)
        {
            terminal.WriteLine("Invalid selection.", "red");
            await Task.Delay(1500);
            return;
        }

        var item = sellable[choice - 1];

        // Suggest a price based on item value
        terminal.WriteLine($"\nSuggested price: {item.Value:N0} {GameConfig.MoneyType}");
        terminal.WriteLine("Enter the price for the item (or press Enter for suggested price):");
        var priceInput = await terminal.GetInput();

        long price;
        if (string.IsNullOrWhiteSpace(priceInput))
        {
            price = item.Value;
        }
        else if (!long.TryParse(priceInput, out price) || price < 0)
        {
            terminal.WriteLine("Invalid price format.", "red");
            await Task.Delay(1500);
            return;
        }

        // Add item to marketplace via MarketplaceSystem
        MarketplaceSystem.Instance.ListItem(currentPlayer.DisplayName, item.Clone(), price);
        currentPlayer.Inventory.Remove(item);
        terminal.WriteLine("Item listed successfully!", "bright_green");
        await Task.Delay(2000);
    }

    private new async Task ShowStatus()
    {
        terminal.ClearScreen();
        terminal.SetColor("cyan");
        terminal.WriteLine("Your Market Activity:\n");

        var allListings = MarketplaceSystem.Instance.GetAllListings();
        var myListings = allListings.Where(l => l.Seller == currentPlayer.DisplayName && !l.IsNPCSeller).ToList();

        terminal.WriteLine($"Active listings: {myListings.Count}");

        if (myListings.Count > 0)
        {
            terminal.WriteLine("\nYour listings:");
            foreach (var listing in myListings)
            {
                var age = (DateTime.Now - listing.Posted).Days;
                terminal.WriteLine($"  {listing.Item.GetDisplayName()} - {listing.Price:N0} {GameConfig.MoneyType} ({age}d old)");
            }
        }

        // Show marketplace statistics
        var stats = MarketplaceSystem.Instance.GetStatistics();
        terminal.WriteLine($"\nMarketplace totals:");
        terminal.WriteLine($"  Total listings: {stats.TotalListings}");
        terminal.WriteLine($"  NPC listings: {stats.NPCListings}");
        terminal.WriteLine($"  Player listings: {stats.PlayerListings}");
        terminal.WriteLine($"  Total value: {stats.TotalValue:N0} {GameConfig.MoneyType}\n");

        await terminal.PressAnyKey();
    }
}
