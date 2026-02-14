using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Central marketplace manager - handles all NPC and player marketplace transactions.
    /// NPCs can list items for sale and buy items from other sellers.
    /// Marketplace listings persist across saves.
    /// </summary>
    public class MarketplaceSystem
    {
        private static MarketplaceSystem? _instance;
        public static MarketplaceSystem Instance => _instance ??= new MarketplaceSystem();

        private Random random = new();

        /// <summary>
        /// All active marketplace listings
        /// </summary>
        public List<MarketListing> Listings { get; private set; } = new();

        /// <summary>
        /// How long listings stay active before expiring (30 days)
        /// </summary>
        public const int ListingExpirationDays = 30;

        /// <summary>
        /// Minimum NPC price markup (100% = item value)
        /// </summary>
        public const float MinPriceMultiplier = 1.0f;

        /// <summary>
        /// Maximum NPC price markup (150% = 1.5x item value)
        /// </summary>
        public const float MaxPriceMultiplier = 1.5f;

        /// <summary>
        /// A single marketplace listing
        /// </summary>
        public class MarketListing
        {
            public global::Item Item { get; set; } = new global::Item();
            public string Seller { get; set; } = "";
            public bool IsNPCSeller { get; set; }
            public string SellerNPCId { get; set; } = "";
            public long Price { get; set; }
            public DateTime Posted { get; set; }

            public bool IsExpired => (DateTime.Now - Posted).TotalDays > ListingExpirationDays;
        }

        private MarketplaceSystem() { }

        /// <summary>
        /// List an item for sale from a player
        /// </summary>
        public void ListItem(string sellerName, global::Item item, long price)
        {
            var listing = new MarketListing
            {
                Item = item,
                Seller = sellerName,
                IsNPCSeller = false,
                SellerNPCId = "",
                Price = price,
                Posted = DateTime.Now
            };

            Listings.Add(listing);
            // GD.Print($"[Marketplace] {sellerName} listed {item.Name} for {price} gold");
        }

        /// <summary>
        /// List an item for sale from an NPC
        /// </summary>
        public void NPCListItem(NPC npc, global::Item item)
        {
            long price = CalculateNPCPrice(item, npc);

            var listing = new MarketListing
            {
                Item = item,
                Seller = npc.Name,
                IsNPCSeller = true,
                SellerNPCId = npc.Id,
                Price = price,
                Posted = DateTime.Now
            };

            Listings.Add(listing);

            // Generate news about the listing
            NewsSystem.Instance?.Newsy(false, $"{npc.Name} put {item.Name} up for sale at the marketplace.");
            // GD.Print($"[Marketplace] NPC {npc.Name} listed {item.Name} for {price} gold");
        }

        /// <summary>
        /// Calculate NPC selling price based on Greed trait (100-150% of item value)
        /// </summary>
        public long CalculateNPCPrice(global::Item item, NPC npc)
        {
            // Base value
            long baseValue = item.Value;
            if (baseValue <= 0) baseValue = 10; // Minimum value

            // Greed affects price: 0 Greed = 100%, 100 Greed = 150%
            float greed = npc.Brain?.Personality?.Greed ?? 50f;
            float greedFactor = greed / 100f; // 0.0 to 1.0
            float multiplier = MinPriceMultiplier + (greedFactor * (MaxPriceMultiplier - MinPriceMultiplier));

            // Add small randomness (+-5%)
            multiplier *= 0.95f + (float)random.NextDouble() * 0.1f;

            return Math.Max(1, (long)(baseValue * multiplier));
        }

        /// <summary>
        /// Purchase an item from the marketplace
        /// Returns true if purchase was successful
        /// </summary>
        public bool PurchaseItem(int listingIndex, Character buyer)
        {
            if (listingIndex < 0 || listingIndex >= Listings.Count)
                return false;

            var listing = Listings[listingIndex];

            // Check buyer has enough gold
            if (buyer.Gold < listing.Price)
            {
                // GD.Print($"[Marketplace] {buyer.Name2} can't afford {listing.Item.Name} ({listing.Price} gold)");
                return false;
            }

            // Process transaction
            buyer.Gold -= listing.Price;

            // Give gold to seller
            if (listing.IsNPCSeller)
            {
                var sellerNPC = NPCSpawnSystem.Instance?.ActiveNPCs?
                    .FirstOrDefault(n => n.Id == listing.SellerNPCId);
                sellerNPC?.GainGold(listing.Price);
            }
            // Player sellers would need a different mechanism (offline gold storage)

            // Remove listing
            Listings.RemoveAt(listingIndex);

            // GD.Print($"[Marketplace] {buyer.Name2} purchased {listing.Item.Name} for {listing.Price} gold from {listing.Seller}");
            return true;
        }

        /// <summary>
        /// NPC browses the marketplace and potentially buys something
        /// </summary>
        public void NPCBrowseAndBuy(NPC npc)
        {
            if (Listings.Count == 0) return;
            if (npc.Gold < 100) return; // Need some gold to shop

            // Find listings the NPC might want
            var affordableListings = Listings
                .Select((listing, index) => new { listing, index })
                .Where(x => x.listing.Price <= npc.Gold * 0.8) // Don't spend more than 80% of gold
                .Where(x => !x.listing.IsNPCSeller || x.listing.SellerNPCId != npc.Id) // Don't buy own items
                .Where(x => NPCWantsToBuy(npc, x.listing))
                .ToList();

            if (affordableListings.Count == 0) return;

            // Pick a random item to buy
            var choice = affordableListings[random.Next(affordableListings.Count)];
            var listing = choice.listing;

            // Make the purchase
            npc.Gold -= listing.Price;

            // Give gold to seller
            if (listing.IsNPCSeller)
            {
                var sellerNPC = NPCSpawnSystem.Instance?.ActiveNPCs?
                    .FirstOrDefault(n => n.Id == listing.SellerNPCId);
                sellerNPC?.GainGold(listing.Price);

                NewsSystem.Instance?.Newsy(false,
                    $"{npc.Name} bought {listing.Item.Name} from {listing.Seller} at the marketplace.");
            }

            // Equip or store the item
            EquipOrStoreItem(npc, listing.Item);

            // Remove listing
            Listings.RemoveAt(choice.index);

            // GD.Print($"[Marketplace] NPC {npc.Name} purchased {listing.Item.Name} for {listing.Price} gold");
        }

        /// <summary>
        /// Determine if an NPC wants to buy a particular listing
        /// Based on class, level, and current equipment
        /// </summary>
        public bool NPCWantsToBuy(NPC npc, MarketListing listing)
        {
            var item = listing.Item;

            // Check if item is appropriate for NPC's class
            if (!IsItemAppropriateForClass(item, npc.Class))
                return false;

            // Check if item is an upgrade
            if (item.Type == global::ObjType.Weapon)
            {
                // Buy if it's better than current weapon
                if (item.Attack > npc.WeapPow)
                    return random.NextDouble() < 0.7; // 70% chance if upgrade
            }
            else if (item.Type == global::ObjType.Body || item.Type == global::ObjType.Head ||
                     item.Type == global::ObjType.Arms || item.Type == global::ObjType.Legs)
            {
                // Buy if it's better than current armor
                if (item.Armor > npc.ArmPow)
                    return random.NextDouble() < 0.7; // 70% chance if upgrade
            }

            // Small chance to buy anyway (collectors, gifts, reselling)
            return random.NextDouble() < 0.1;
        }

        /// <summary>
        /// Check if an item is appropriate for a character class
        /// </summary>
        private bool IsItemAppropriateForClass(global::Item item, CharacterClass charClass)
        {
            // Mages/Sages prefer robes and staves
            if (charClass == CharacterClass.Magician || charClass == CharacterClass.Sage)
            {
                string nameLower = item.Name.ToLower();
                if (item.Type == global::ObjType.Body || item.Type == global::ObjType.Arms)
                {
                    // Prefer light armor
                    if (nameLower.Contains("plate") || nameLower.Contains("heavy"))
                        return false;
                }
            }

            // Warriors/Barbarians/Paladins can use anything
            if (charClass == CharacterClass.Warrior ||
                charClass == CharacterClass.Barbarian ||
                charClass == CharacterClass.Paladin)
            {
                return true;
            }

            // Assassins/Rangers prefer light armor and daggers/bows
            if (charClass == CharacterClass.Assassin || charClass == CharacterClass.Ranger)
            {
                string nameLower = item.Name.ToLower();
                if (item.Type == global::ObjType.Body || item.Type == global::ObjType.Arms)
                {
                    if (nameLower.Contains("plate") || nameLower.Contains("heavy"))
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Have NPC equip or store an item after purchasing.
        /// Internal so WorldSimulator can use it for online marketplace NPC purchases.
        /// </summary>
        internal void EquipOrStoreItem(NPC npc, global::Item item)
        {
            if (item.Type == global::ObjType.Weapon)
            {
                if (item.Attack > npc.WeapPow)
                {
                    // If current weapon is valuable, add to market inventory for resale
                    if (npc.WeapPow > 0 && npc.MarketInventory.Count < npc.MaxMarketInventory)
                    {
                        var oldWeapon = new global::Item
                        {
                            Name = "Old Weapon",
                            Value = (long)npc.WeapPow * 10,
                            Type = global::ObjType.Weapon,
                            Attack = (int)npc.WeapPow
                        };
                        npc.MarketInventory.Add(oldWeapon);
                    }

                    npc.WeapPow = item.Attack;
                    // GD.Print($"[Marketplace] {npc.Name} equipped {item.Name}");
                }
                else
                {
                    // Add to inventory for potential resale
                    if (npc.MarketInventory.Count < npc.MaxMarketInventory)
                    {
                        npc.MarketInventory.Add(item);
                    }
                }
            }
            else if (item.Type == global::ObjType.Body)
            {
                if (item.Armor > npc.ArmPow)
                {
                    // If current armor is valuable, add to market inventory for resale
                    if (npc.ArmPow > 0 && npc.MarketInventory.Count < npc.MaxMarketInventory)
                    {
                        var oldArmor = new global::Item
                        {
                            Name = "Old Armor",
                            Value = (long)npc.ArmPow * 10,
                            Type = global::ObjType.Body,
                            Armor = (int)npc.ArmPow
                        };
                        npc.MarketInventory.Add(oldArmor);
                    }

                    npc.ArmPow = item.Armor;
                    // GD.Print($"[Marketplace] {npc.Name} equipped {item.Name}");
                }
                else
                {
                    // Add to inventory for potential resale
                    if (npc.MarketInventory.Count < npc.MaxMarketInventory)
                    {
                        npc.MarketInventory.Add(item);
                    }
                }
            }
            else
            {
                // Other items go to inventory
                if (npc.MarketInventory.Count < npc.MaxMarketInventory)
                {
                    npc.MarketInventory.Add(item);
                }
            }
        }

        /// <summary>
        /// Remove expired listings
        /// </summary>
        public void CleanupExpiredListings()
        {
            int removed = Listings.RemoveAll(l => l.IsExpired);
            if (removed > 0)
            {
                // GD.Print($"[Marketplace] Removed {removed} expired listings");
            }
        }

        /// <summary>
        /// Get all listings for display
        /// </summary>
        public List<MarketListing> GetAllListings()
        {
            return Listings.OrderByDescending(l => l.Posted).ToList();
        }

        /// <summary>
        /// Get listings by seller
        /// </summary>
        public List<MarketListing> GetListingsBySeller(string sellerName)
        {
            return Listings.Where(l => l.Seller == sellerName).ToList();
        }

        /// <summary>
        /// Remove a listing by seller (when they cancel it)
        /// </summary>
        public global::Item? CancelListing(int listingIndex, string sellerName)
        {
            if (listingIndex < 0 || listingIndex >= Listings.Count)
                return null;

            var listing = Listings[listingIndex];
            if (listing.Seller != sellerName)
                return null;

            Listings.RemoveAt(listingIndex);
            return listing.Item;
        }

        /// <summary>
        /// Convert to save data format
        /// </summary>
        public List<MarketListingData> ToSaveData()
        {
            return Listings.Select(l => new MarketListingData
            {
                Item = new MarketItemData
                {
                    ItemName = l.Item.Name,
                    ItemValue = l.Item.Value,
                    ItemType = l.Item.Type,
                    Attack = l.Item.Attack,
                    Armor = l.Item.Armor,
                    Strength = l.Item.Strength,
                    Defence = l.Item.Defence,
                    IsCursed = l.Item.IsCursed
                },
                Seller = l.Seller,
                IsNPCSeller = l.IsNPCSeller,
                SellerNPCId = l.SellerNPCId,
                Price = l.Price,
                Posted = l.Posted
            }).ToList();
        }

        /// <summary>
        /// Load from save data
        /// </summary>
        public void LoadFromSaveData(List<MarketListingData> data)
        {
            Listings.Clear();

            foreach (var d in data)
            {
                var item = new global::Item
                {
                    Name = d.Item.ItemName,
                    Value = d.Item.ItemValue,
                    Type = d.Item.ItemType,
                    Attack = d.Item.Attack,
                    Armor = d.Item.Armor,
                    Strength = d.Item.Strength,
                    Defence = d.Item.Defence,
                    IsCursed = d.Item.IsCursed
                };

                var listing = new MarketListing
                {
                    Item = item,
                    Seller = d.Seller,
                    IsNPCSeller = d.IsNPCSeller,
                    SellerNPCId = d.SellerNPCId,
                    Price = d.Price,
                    Posted = d.Posted
                };

                Listings.Add(listing);
            }

            // GD.Print($"[Marketplace] Loaded {Listings.Count} listings from save data");
        }

        /// <summary>
        /// Clear all listings (for testing/reset)
        /// </summary>
        public void ClearAllListings()
        {
            Listings.Clear();
        }

        /// <summary>
        /// Get marketplace statistics
        /// </summary>
        public (int TotalListings, int NPCListings, int PlayerListings, long TotalValue) GetStatistics()
        {
            int total = Listings.Count;
            int npc = Listings.Count(l => l.IsNPCSeller);
            int player = total - npc;
            long value = Listings.Sum(l => l.Price);

            return (total, npc, player, value);
        }
    }
}
