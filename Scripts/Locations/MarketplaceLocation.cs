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
        if (IsScreenReader) { DisplayLocationSR(); return; }
        if (IsBBSSession) { DisplayLocationBBS(); return; }

        terminal.ClearScreen();

        // Header
        WriteBoxHeader(Loc.Get("marketplace.header_visual"), "bright_cyan", 77);
        terminal.WriteLine("");

        // Atmospheric description
        var stats = MarketplaceSystem.Instance.GetStatistics();
        terminal.SetColor("white");
        terminal.Write(Loc.Get("marketplace.atmo_desc1"));
        terminal.WriteLine(Loc.Get("marketplace.atmo_desc2"));
        terminal.Write(Loc.Get("marketplace.atmo_desc3"));
        if (stats.TotalListings > 10)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("marketplace.atmo_busy"));
        }
        else if (stats.TotalListings > 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("marketplace.atmo_few"));
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("marketplace.atmo_empty"));
        }
        terminal.WriteLine("");

        // Auctioneer flavor
        terminal.SetColor("yellow");
        terminal.Write(Loc.Get("marketplace.grimjaw"));
        terminal.SetColor("gray");
        terminal.Write(Loc.Get("marketplace.grimjaw_desc"));
        if (stats.TotalListings == 0)
        {
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("marketplace.grimjaw_yawn"));
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("marketplace.grimjaw_slow"));
        }
        else if (stats.TotalListings < 5)
        {
            terminal.SetColor("white");
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("marketplace.grimjaw_few"));
        }
        else
        {
            terminal.SetColor("white");
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("marketplace.grimjaw_plenty"));
        }
        terminal.WriteLine("");

        // Listing summary
        if (stats.TotalListings > 0)
        {
            terminal.SetColor("cyan");
            terminal.Write(Loc.Get("marketplace.listings_label"));
            terminal.SetColor("white");
            terminal.Write($"{stats.TotalListings}");
            terminal.SetColor("gray");
            terminal.Write("  (");
            if (stats.PlayerListings > 0)
            {
                terminal.SetColor("bright_green");
                terminal.Write(Loc.Get("marketplace.player_count", stats.PlayerListings));
            }
            if (stats.PlayerListings > 0 && stats.NPCListings > 0)
            {
                terminal.SetColor("gray");
                terminal.Write(", ");
            }
            if (stats.NPCListings > 0)
            {
                terminal.SetColor("bright_cyan");
                terminal.Write(Loc.Get("marketplace.npc_count", stats.NPCListings));
            }
            terminal.SetColor("gray");
            terminal.Write(")");

            terminal.SetColor("gray");
            terminal.Write(Loc.Get("marketplace.total_value"));
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"{stats.TotalValue:N0} {GameConfig.MoneyType}");
        }
        terminal.WriteLine("");

        ShowNPCsInLocation();

        // Menu
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("marketplace.what_to_do"));
        terminal.WriteLine("");

        // Row 1
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("C");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("marketplace.menu_check"));

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("B");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("marketplace.menu_buy"));

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("A");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("marketplace.menu_add"));

        // Row 2
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("S");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("marketplace.menu_status"));

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("R");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("marketplace.menu_return"));
        terminal.WriteLine("");

        ShowStatusLine();
    }

    /// <summary>
    /// Compact BBS display for 80x25 terminals.
    /// </summary>
    private void DisplayLocationBBS()
    {
        terminal.ClearScreen();
        ShowBBSHeader(Loc.Get("marketplace.header_visual"));

        // 1-line description with listing count
        var stats = MarketplaceSystem.Instance.GetStatistics();
        terminal.SetColor("white");
        if (stats.TotalListings > 0)
        {
            terminal.Write(Loc.Get("marketplace.bbs_hall"));
            terminal.SetColor("cyan");
            terminal.Write($"{stats.TotalListings}");
            terminal.SetColor("gray");
            terminal.Write(Loc.Get("marketplace.bbs_listings"));
            if (stats.TotalValue > 0)
            {
                terminal.Write(Loc.Get("marketplace.bbs_worth"));
                terminal.SetColor("yellow");
                terminal.Write($"{stats.TotalValue:N0}g");
            }
            terminal.WriteLine("");
        }
        else
        {
            terminal.WriteLine(Loc.Get("marketplace.bbs_empty"));
        }

        ShowBBSNPCs();
        terminal.WriteLine("");

        // Menu rows
        ShowBBSMenuRow(("C", "bright_yellow", Loc.Get("marketplace.bbs_browse")), ("B", "bright_yellow", Loc.Get("marketplace.bbs_buy")), ("A", "bright_yellow", Loc.Get("marketplace.bbs_add")), ("S", "bright_yellow", Loc.Get("marketplace.bbs_status")), ("R", "bright_yellow", Loc.Get("marketplace.bbs_return")));

        ShowBBSFooter();
    }

    private void DisplayLocationSR()
    {
        terminal.ClearScreen();
        terminal.WriteLine(Loc.Get("marketplace.sr_title"));
        terminal.WriteLine("");

        var stats = MarketplaceSystem.Instance.GetStatistics();
        terminal.WriteLine(Loc.Get("marketplace.sr_listings", stats.TotalListings, stats.PlayerListings, stats.NPCListings));
        if (stats.TotalValue > 0)
            terminal.WriteLine(Loc.Get("marketplace.sr_total_value", $"{stats.TotalValue:N0}", GameConfig.MoneyType));
        terminal.WriteLine(Loc.Get("marketplace.sr_your_gold", $"{currentPlayer.Gold:N0}"));
        terminal.WriteLine("");

        ShowNPCsInLocation();

        WriteSRMenuOption("C", Loc.Get("marketplace.bulletin"));
        WriteSRMenuOption("B", Loc.Get("marketplace.buy"));
        WriteSRMenuOption("A", Loc.Get("marketplace.sell"));
        WriteSRMenuOption("S", Loc.Get("marketplace.status"));
        WriteSRMenuOption("R", Loc.Get("marketplace.return"));
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

        WriteBoxHeader(Loc.Get("marketplace.listings"), "bright_cyan", 77);
        terminal.WriteLine("");

        var listings = MarketplaceSystem.Instance.GetAllListings();
        if (listings.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("marketplace.board_empty1"));
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("marketplace.board_empty2"));
        }
        else
        {
            // Column headers
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("marketplace.col_num"),-4} {Loc.Get("marketplace.col_item"),-30} {Loc.Get("marketplace.col_price"),-16} {Loc.Get("marketplace.col_seller"),-18} {Loc.Get("marketplace.col_age")}");
            if (!IsScreenReader)
            {
                terminal.SetColor("darkgray");
                terminal.WriteLine("  ─── ────────────────────────── ──────────────── ────────────────── ───");
            }

            int idx = 1;
            foreach (var listing in listings)
            {
                var age = (DateTime.Now - listing.Posted).Days;
                string ageStr = age == 0 ? Loc.Get("marketplace.age_new") : $"{age}d";
                string sellerDisplay = listing.IsNPCSeller ? $"{listing.Seller} {Loc.Get("marketplace.npc_tag")}" : listing.Seller;

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
            terminal.WriteLine(Loc.Get("marketplace.nothing_available"), "yellow");
            await Task.Delay(1500);
            return;
        }

        await ShowBoard();
        var input = await terminal.GetInput(Loc.Get("marketplace.buy_prompt"));
        if (input.Trim().Equals("Q", StringComparison.OrdinalIgnoreCase)) return;
        if (!int.TryParse(input, out int choice) || choice < 1 || choice > listings.Count)
        {
            terminal.WriteLine(Loc.Get("ui.invalid_selection"), "red");
            await Task.Delay(1500);
            return;
        }

        var listing = listings[choice - 1];

        // Don't allow buying your own items
        if (listing.Seller == currentPlayer.DisplayName && !listing.IsNPCSeller)
        {
            terminal.WriteLine(Loc.Get("marketplace.cant_buy_own"), "red");
            await Task.Delay(1500);
            return;
        }

        if (currentPlayer.Gold < listing.Price)
        {
            terminal.WriteLine(Loc.Get("marketplace.cant_afford"), "red");
            await Task.Delay(1500);
            return;
        }

        // Find the actual index in the MarketplaceSystem listings
        int actualIndex = MarketplaceSystem.Instance.Listings.IndexOf(listing);
        if (actualIndex < 0)
        {
            terminal.WriteLine(Loc.Get("marketplace.no_longer_available"), "red");
            await Task.Delay(1500);
            return;
        }

        // Process the purchase
        if (MarketplaceSystem.Instance.PurchaseItem(actualIndex, currentPlayer))
        {
            var purchasedItem = listing.Item.Clone();
            currentPlayer.Inventory.Add(purchasedItem);
            terminal.WriteLine(Loc.Get("marketplace.transaction_complete"), "bright_green");

            // v1.1.1: this block told the player the item was equipped and added its stats,
            // then RecalculateStats reset every one of them; nothing was equipped. The item
            // is in the backpack; equip it from the inventory screen.

            // Generate news for NPC seller transactions
            if (listing.IsNPCSeller)
            {
                NewsSystem.Instance?.Newsy(false,
                    $"{currentPlayer.DisplayName} purchased {listing.Item.Name} from {listing.Seller} at the marketplace.");
            }
        }
        else
        {
            terminal.WriteLine(Loc.Get("marketplace.transaction_failed"), "red");
        }

        await Task.Delay(2000);
    }

    private async Task ListItem()
    {
        // Show player inventory with index numbers
        var sellable = currentPlayer.Inventory.Where(it => it != null).ToList();
        if (sellable.Count == 0)
        {
            terminal.WriteLine(Loc.Get("marketplace.nothing_to_sell"), "yellow");
            await Task.Delay(1500);
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("marketplace.your_inventory"));
        for (int i = 0; i < sellable.Count; i++)
        {
            terminal.WriteLine($"{i + 1}. {sellable[i].GetDisplayName()} ({Loc.Get("marketplace.value_label")}: {sellable[i].Value:N0})");
        }

        var input = await terminal.GetInput(Loc.Get("marketplace.sell_prompt"));
        if (input.Trim().Equals("Q", StringComparison.OrdinalIgnoreCase)) return;
        if (!int.TryParse(input, out int choice) || choice < 1 || choice > sellable.Count)
        {
            terminal.WriteLine(Loc.Get("ui.invalid_selection"), "red");
            await Task.Delay(1500);
            return;
        }

        var item = sellable[choice - 1];

        // Suggest a price based on item value
        terminal.WriteLine(Loc.Get("marketplace.suggested_price", $"{item.Value:N0}", GameConfig.MoneyType));
        terminal.WriteLine(Loc.Get("marketplace.enter_price"));
        var priceInput = await terminal.GetInput();

        long price;
        if (string.IsNullOrWhiteSpace(priceInput))
        {
            price = item.Value;
        }
        else if (!long.TryParse(priceInput, out price) || price < 0)
        {
            terminal.WriteLine(Loc.Get("marketplace.invalid_price"), "red");
            await Task.Delay(1500);
            return;
        }

        // Add item to marketplace via MarketplaceSystem
        MarketplaceSystem.Instance.ListItem(currentPlayer.DisplayName, item.Clone(), price);
        currentPlayer.Inventory.Remove(item);
        terminal.WriteLine(Loc.Get("marketplace.item_listed"), "bright_green");
        await Task.Delay(2000);
    }

    private new async Task ShowStatus()
    {
        terminal.ClearScreen();

        WriteBoxHeader(Loc.Get("marketplace.your_status"), "bright_cyan", 77);
        terminal.WriteLine("");

        var allListings = MarketplaceSystem.Instance.GetAllListings();
        var myListings = allListings.Where(l => l.Seller == currentPlayer.DisplayName && !l.IsNPCSeller).ToList();

        // Your listings section
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("marketplace.your_listings"));
        if (!IsScreenReader)
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine("  ─────────────────────────────────────────────────────────");
        }

        if (myListings.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("marketplace.nothing_listed"));
        }
        else
        {
            foreach (var listing in myListings)
            {
                var age = (DateTime.Now - listing.Posted).Days;
                string ageStr = age == 0 ? Loc.Get("marketplace.posted_today") : Loc.Get("marketplace.posted_ago", age);
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
        terminal.WriteLine(Loc.Get("marketplace.market_overview"));
        if (!IsScreenReader)
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine("  ─────────────────────────────────────────────────────────");
        }
        terminal.SetColor("gray");
        terminal.Write(Loc.Get("marketplace.total_listings_label"));
        terminal.SetColor("white");
        terminal.Write($"{stats.TotalListings}");
        terminal.SetColor("gray");
        terminal.Write("   (");
        terminal.SetColor("bright_green");
        terminal.Write(Loc.Get("marketplace.player_count", stats.PlayerListings));
        terminal.SetColor("gray");
        terminal.Write(", ");
        terminal.SetColor("bright_cyan");
        terminal.Write(Loc.Get("marketplace.npc_count", stats.NPCListings));
        terminal.SetColor("gray");
        terminal.WriteLine(")");
        terminal.SetColor("gray");
        terminal.Write(Loc.Get("marketplace.total_value_label"));
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"{stats.TotalValue:N0} {GameConfig.MoneyType}");
        terminal.SetColor("gray");
        terminal.Write(Loc.Get("marketplace.your_gold_label"));
        terminal.SetColor("yellow");
        terminal.WriteLine($"{currentPlayer.Gold:N0} {GameConfig.MoneyType}");
        terminal.WriteLine("");

        await terminal.PressAnyKey();
    }
}
