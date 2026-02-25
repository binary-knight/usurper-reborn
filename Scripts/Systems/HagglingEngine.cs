using UsurperRemake.Utils;
using System;
using System.Threading.Tasks;

/// <summary>
/// Haggling Engine - Pascal-compatible trading system
/// Based on HAGGLEC.PAS with charisma-based success and daily limits
/// </summary>
public static class HagglingEngine
{
    // Haggling shop types (Pascal: shop parameter)
    public enum ShopType
    {
        Weapon = 'W',
        Armor = 'A'
    }
    
    /// <summary>
    /// Attempt to haggle for a better price
    /// Returns the final agreed price (original price if haggling failed)
    /// </summary>
    public static async Task<long> Haggle(Character player, ShopType shopType, long originalCost, 
                                         string shopkeeperName, TerminalEmulator terminal)
    {
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("═══ HAGGLE ═══");
        terminal.WriteLine("");
        
        // Check if player has haggling attempts left
        if (!CanHaggle(player, shopType))
        {
            await HandleNoHagglingAttemptsLeft(player, shopType, shopkeeperName, terminal);
            return originalCost;
        }
        
        // Special case: Trolls can't haggle at weapon shop if they already got race discount
        if (shopType == ShopType.Weapon && player.Race == CharacterRace.Troll)
        {
            terminal.SetColor("red");
            terminal.WriteLine("Hey! I've already given you a discount!");
            terminal.WriteLine("Don't expect any more! Get out!");
            await Task.Delay(2000);
            return originalCost;
        }
        
        // Deduct haggling attempt
        DeductHagglingAttempt(player, shopType);
        
        // Get player's offer
        terminal.SetColor("yellow");
        terminal.Write("Alright, give me an offer: ");
        
        var offerInput = await terminal.GetInput("");
        if (!long.TryParse(offerInput, out long offer) || offer <= 0 || offer >= originalCost)
        {
            terminal.WriteLine("That's not a serious offer!", "red");
            await Task.Delay(1500);
            return originalCost;
        }
        
        // Calculate if haggling succeeds
        bool success = CalculateHagglingSuccess(player, originalCost, offer);
        
        if (!success)
        {
            await HandleHagglingFailure(shopType, terminal);
            return originalCost;
        }
        
        // Haggling succeeded!
        terminal.SetColor("green");
        terminal.WriteLine($"Alright {player.DisplayName}! You've got a deal!");
        
        var confirm = await terminal.GetInput("Accept this price? (Y/N): ");
        if (confirm.ToUpper() == "Y")
        {
            return offer;
        }
        
        return originalCost;
    }
    
    /// <summary>
    /// Check if player can still haggle today
    /// </summary>
    public static bool CanHaggle(Character player, ShopType shopType)
    {
        return shopType switch
        {
            ShopType.Weapon => player.WeapHag > 0,
            ShopType.Armor => player.ArmHag > 0,
            _ => false
        };
    }
    
    /// <summary>
    /// Deduct a haggling attempt
    /// </summary>
    private static void DeductHagglingAttempt(Character player, ShopType shopType)
    {
        switch (shopType)
        {
            case ShopType.Weapon:
                player.WeapHag--;
                break;
            case ShopType.Armor:
                player.ArmHag--;
                break;
        }
    }
    
    /// <summary>
    /// Calculate haggling success based on charisma and discount percentage
    /// Pascal formula: max 20% discount, success based on charisma levels
    /// </summary>
    private static bool CalculateHagglingSuccess(Character player, long originalCost, long offer)
    {
        // Calculate discount percentage
        double discountRatio = (double)(originalCost - offer) / originalCost;
        int discountPercentage = (int)(discountRatio * 100);
        
        // Maximum 20% discount allowed
        double maxDiscountAmount = originalCost * 0.20;
        long minimumAcceptableOffer = originalCost - (long)maxDiscountAmount;
        
        if (offer < minimumAcceptableOffer)
        {
            return false; // Discount too high
        }
        
        // Charisma-based success rates (Pascal formula)
        int maxAllowedDiscount = player.Charisma switch
        {
            >= 201 => 20,    // 201+ charisma: up to 20% discount
            >= 176 => 17,    // 176-200 charisma: up to 17% discount  
            >= 126 => 13,    // 126-175 charisma: up to 13% discount
            >= 76 => 10,     // 76-125 charisma: up to 10% discount
            >= 26 => 7,      // 26-75 charisma: up to 7% discount
            _ => 4           // 1-25 charisma: up to 4% discount
        };
        
        return discountPercentage <= maxAllowedDiscount;
    }
    
    /// <summary>
    /// Handle the case when player has no haggling attempts left
    /// </summary>
    private static async Task HandleNoHagglingAttemptsLeft(Character player, ShopType shopType, 
                                                          string shopkeeperName, TerminalEmulator terminal)
    {
        terminal.SetColor("red");
        
        switch (shopType)
        {
            case ShopType.Weapon:
                terminal.WriteLine($"You are making me very angry {player.DisplayName}!");
                terminal.WriteLine("Accept my offer or leave!");
                terminal.WriteLine("");
                terminal.WriteLine($"{shopkeeperName}'s face turns unhealthy red...");
                break;
                
            case ShopType.Armor:
                terminal.WriteLine($"Damn you {player.DisplayName}!");
                terminal.WriteLine("Accept my offer or leave!");
                terminal.WriteLine("");
                terminal.WriteLine($"{shopkeeperName} seems to be quite upset with your behaviour.");
                break;
        }
        
        terminal.WriteLine("");
        terminal.WriteLine("What are you going to do? Insist once more or leave?");
        
        var choice = await terminal.GetInput("Insist? (Y/N): ");
        
        if (choice.ToUpper() == "Y")
        {
            // Player gets kicked out!
            terminal.SetColor("bright_red");
            terminal.WriteLine("You have been kicked out!");
            
            // Create news entry (placeholder for news system)
            string shopName = shopType == ShopType.Weapon ? "Weaponshop" : "Armor Shop";
            terminal.WriteLine($"[NEWS] {player.DisplayName} was kicked out from the {shopName}!");
            
            await Task.Delay(3000);
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("End of discussion.");
            await Task.Delay(1500);
        }
    }
    
    /// <summary>
    /// Handle haggling failure
    /// </summary>
    private static async Task HandleHagglingFailure(ShopType shopType, TerminalEmulator terminal)
    {
        terminal.SetColor("red");
        
        switch (shopType)
        {
            case ShopType.Weapon:
                terminal.WriteLine("Haha! You think you're funny huh!?");
                terminal.WriteLine("Get out of here before I get nasty!");
                break;
                
            case ShopType.Armor:
                terminal.WriteLine("Stop kidding around and get out, so I can");
                terminal.WriteLine("do some serious business!");
                break;
        }
        
        await Task.Delay(2000);
    }
    
    /// <summary>
    /// Reset daily haggling attempts (called by daily system)
    /// </summary>
    public static void ResetDailyHaggling(Character player)
    {
        player.WeapHag = 3;  // Reset to 3 attempts per day
        player.ArmHag = 3;   // Reset to 3 attempts per day
    }
    
    /// <summary>
    /// Calculate race-based discount for weapon shop
    /// </summary>
    public static long ApplyRaceDiscount(Character player, long originalPrice)
    {
        // Trolls get 10% discount at weapon shop (Pascal formula)
        if (player.Race == CharacterRace.Troll)
        {
            double discount = originalPrice * 0.1;
            return originalPrice - (long)discount;
        }
        
        return originalPrice;
    }
    
    /// <summary>
    /// Get haggling attempts remaining
    /// </summary>
    public static int GetHagglingAttemptsLeft(Character player, ShopType shopType)
    {
        return shopType switch
        {
            ShopType.Weapon => player.WeapHag,
            ShopType.Armor => player.ArmHag,
            _ => 0
        };
    }
} 
