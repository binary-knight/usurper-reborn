using UsurperRemake.BBS;
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

    public MarketplaceLocation() : base(GameLocation.AuctionHouse, "Auction House",
        "A grand hall echoes with the cries of auctioneers. Bidders crowd around display cases, inspecting goods with practiced eyes.")
    {
    }

    protected override void SetupLocation()
    {
        PossibleExits.Add(GameLocation.MainStreet);
    }

    protected override void DisplayLocation()
    {
        if (DoorMode.IsInDoorMode) { DisplayLocationBBS(); return; }

        terminal.ClearScreen();

        // Header
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔═════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine($"║{"AUCTION HOUSE".PadLeft((77 + 13) / 2).PadRight(77)}║");
        terminal.WriteLine("╚═════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        // Atmospheric description
        var stats = MarketplaceSystem.Instance.GetStatistics();
        terminal.SetColor("white");
        terminal.Write("A cavernous hall of polished stone, lined with glass display cases and ");
        terminal.WriteLine("wooden");
        terminal.Write("stalls. ");
        if (stats.TotalListings > 10)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("The air buzzes with haggling voices and the clink of coin purses.");
        }
        else if (stats.TotalListings > 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("A few merchants stand behind their stalls, calling to passersby.");
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("The stalls stand mostly empty today, dust settling on the counters.");
        }
        terminal.WriteLine("");

        // Auctioneer flavor
        terminal.SetColor("yellow");
        terminal.Write("Grimjaw");
        terminal.SetColor("gray");
        terminal.Write(", the half-orc auctioneer, looms behind the registry desk. ");
        if (stats.TotalListings == 0)
        {
            terminal.SetColor("white");
            terminal.WriteLine("He yawns.");
            terminal.SetColor("yellow");
            terminal.WriteLine("\"Slow day. You selling or just wasting my time?\"");
        }
        else if (stats.TotalListings < 5)
        {
            terminal.SetColor("white");
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine("\"Got a few things worth looking at. Browse the board.\"");
        }
        else
        {
            terminal.SetColor("white");
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine("\"Plenty of goods today! Step up, step up!\"");
        }
        terminal.WriteLine("");

        // Listing summary
        if (stats.TotalListings > 0)
        {
            terminal.SetColor("cyan");
            terminal.Write("  Listings: ");
            terminal.SetColor("white");
            terminal.Write($"{stats.TotalListings}");
            terminal.SetColor("gray");
            terminal.Write("  (");
            if (stats.PlayerListings > 0)
            {
                terminal.SetColor("bright_green");
                terminal.Write($"{stats.PlayerListings} player");
            }
            if (stats.PlayerListings > 0 && stats.NPCListings > 0)
            {
                terminal.SetColor("gray");
                terminal.Write(", ");
            }
            if (stats.NPCListings > 0)
            {
                terminal.SetColor("bright_cyan");
                terminal.Write($"{stats.NPCListings} NPC");
            }
            terminal.SetColor("gray");
            terminal.Write(")");

            terminal.SetColor("gray");
            terminal.Write("   Total value: ");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"{stats.TotalValue:N0} {GameConfig.MoneyType}");
        }
        terminal.WriteLine("");

        ShowNPCsInLocation();

        // Menu
        terminal.SetColor("cyan");
        terminal.WriteLine("What would you like to do?");
        terminal.WriteLine("");

        // Row 1
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("C");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("heck bulletin board   ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("B");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("uy item        ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("A");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine("dd item");

        // Row 2
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("S");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("tatus                 ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("R");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine("eturn");
        terminal.WriteLine("");

        ShowStatusLine();
    }

    /// <summary>
    /// Compact BBS display for 80x25 terminals.
    /// </summary>
    private void DisplayLocationBBS()
    {
        terminal.ClearScreen();
        ShowBBSHeader("AUCTION HOUSE");

        // 1-line description with listing count
        var stats = MarketplaceSystem.Instance.GetStatistics();
        terminal.SetColor("white");
        if (stats.TotalListings > 0)
        {
            terminal.Write(" Grimjaw's auction hall. ");
            terminal.SetColor("cyan");
            terminal.Write($"{stats.TotalListings}");
            terminal.SetColor("gray");
            terminal.Write(" listings");
            if (stats.TotalValue > 0)
            {
                terminal.Write(" worth ");
                terminal.SetColor("yellow");
                terminal.Write($"{stats.TotalValue:N0}g");
            }
            terminal.WriteLine("");
        }
        else
        {
            terminal.WriteLine(" Grimjaw's auction hall stands mostly empty. \"Slow day.\"");
        }

        ShowBBSNPCs();
        terminal.WriteLine("");

        // Menu rows
        ShowBBSMenuRow(("C", "bright_yellow", "Browse"), ("B", "bright_yellow", "Buy"), ("A", "bright_yellow", "AddItem"), ("S", "bright_yellow", "Status"), ("R", "bright_yellow", "Return"));

        ShowBBSFooter();
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
        terminal.WriteLine("╔═════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine($"║{"AUCTION HOUSE \u2014 LISTINGS".PadLeft((77 + 24) / 2).PadRight(77)}║");
        terminal.WriteLine("╚═════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        var listings = MarketplaceSystem.Instance.GetAllListings();
        if (listings.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  The bulletin board is bare. A few old nails jut from the cork.");
            terminal.SetColor("yellow");
            terminal.WriteLine("\n  Grimjaw shrugs. \"Nothing posted. Come back later, or list something yourself.\"");
        }
        else
        {
            // Column headers
            terminal.SetColor("gray");
            terminal.WriteLine($"  {"#",-4} {"Item",-30} {"Price",-16} {"Seller",-18} {"Age"}");
            terminal.SetColor("darkgray");
            terminal.WriteLine("  ─── ────────────────────────── ──────────────── ────────────────── ───");

            int idx = 1;
            foreach (var listing in listings)
            {
                var age = (DateTime.Now - listing.Posted).Days;
                string ageStr = age == 0 ? "new" : $"{age}d";
                string sellerDisplay = listing.IsNPCSeller ? $"{listing.Seller} (NPC)" : listing.Seller;

                // Item number
                terminal.SetColor("darkgray");
                terminal.Write($"  [{idx,-2}]");

                // Item name - color by rarity/type
                terminal.SetColor("white");
                string itemName = listing.Item.GetDisplayName();
                if (itemName.Length > 28) itemName = itemName[..28] + "..";
                terminal.Write($" {itemName,-30}");

                // Price
                terminal.SetColor("bright_yellow");
                terminal.Write($" {listing.Price,12:N0} gc ");

                // Seller
                terminal.SetColor(listing.IsNPCSeller ? "bright_cyan" : "bright_green");
                if (sellerDisplay.Length > 18) sellerDisplay = sellerDisplay[..18];
                terminal.Write($" {sellerDisplay,-18}");

                // Age
                terminal.SetColor("gray");
                terminal.WriteLine($" {ageStr}");

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

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔═════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine($"║{"AUCTION HOUSE \u2014 YOUR STATUS".PadLeft((77 + 28) / 2).PadRight(77)}║");
        terminal.WriteLine("╚═════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        var allListings = MarketplaceSystem.Instance.GetAllListings();
        var myListings = allListings.Where(l => l.Seller == currentPlayer.DisplayName && !l.IsNPCSeller).ToList();

        // Your listings section
        terminal.SetColor("cyan");
        terminal.WriteLine("  Your Active Listings:");
        terminal.SetColor("darkgray");
        terminal.WriteLine("  ─────────────────────────────────────────────────────────");

        if (myListings.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  You have nothing listed for sale.");
        }
        else
        {
            foreach (var listing in myListings)
            {
                var age = (DateTime.Now - listing.Posted).Days;
                string ageStr = age == 0 ? "today" : $"{age}d ago";
                terminal.SetColor("white");
                terminal.Write($"    {listing.Item.GetDisplayName()}");
                terminal.SetColor("gray");
                terminal.Write(" — ");
                terminal.SetColor("bright_yellow");
                terminal.Write($"{listing.Price:N0} {GameConfig.MoneyType}");
                terminal.SetColor("gray");
                terminal.WriteLine($"  (posted {ageStr})");
            }
        }
        terminal.WriteLine("");

        // Market overview
        var stats = MarketplaceSystem.Instance.GetStatistics();
        terminal.SetColor("cyan");
        terminal.WriteLine("  Market Overview:");
        terminal.SetColor("darkgray");
        terminal.WriteLine("  ─────────────────────────────────────────────────────────");
        terminal.SetColor("gray");
        terminal.Write("    Total listings: ");
        terminal.SetColor("white");
        terminal.Write($"{stats.TotalListings}");
        terminal.SetColor("gray");
        terminal.Write("   (");
        terminal.SetColor("bright_green");
        terminal.Write($"{stats.PlayerListings} player");
        terminal.SetColor("gray");
        terminal.Write(", ");
        terminal.SetColor("bright_cyan");
        terminal.Write($"{stats.NPCListings} NPC");
        terminal.SetColor("gray");
        terminal.WriteLine(")");
        terminal.SetColor("gray");
        terminal.Write("    Total value:    ");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"{stats.TotalValue:N0} {GameConfig.MoneyType}");
        terminal.SetColor("gray");
        terminal.Write("    Your gold:      ");
        terminal.SetColor("yellow");
        terminal.WriteLine($"{currentPlayer.Gold:N0} {GameConfig.MoneyType}");
        terminal.WriteLine("");

        await terminal.PressAnyKey();
    }
}
