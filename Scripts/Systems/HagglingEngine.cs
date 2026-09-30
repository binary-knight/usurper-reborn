using UsurperRemake.Utils;
using UsurperRemake.Systems;
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
    
    /// <summary>The outcome of one haggle: the agreed pre-tax price (the original if nothing
    /// was agreed) and whether the shopkeeper threw the player out.</summary>
    public readonly record struct HaggleResult(long Price, bool Kicked);

    /// <summary>
    /// Attempt to haggle for a better price. One call spends one attempt whatever the
    /// outcome; with no attempts left the shopkeeper's temper decides whether the player
    /// is thrown out (the caller applies the bar and leaves the shop).
    /// </summary>
    public static async Task<HaggleResult> Haggle(Character player, ShopType shopType, long originalCost, 
                                         string shopkeeperName, TerminalEmulator terminal)
    {
        terminal.SetColor("bright_yellow");
        if (!GameConfig.ScreenReaderMode)
            terminal.WriteLine($"═══ {Loc.Get("haggle.header")} ═══");
        else
            terminal.WriteLine(Loc.Get("haggle.header"));
        terminal.WriteLine("");
        
        // Check if player has haggling attempts left
        if (!CanHaggle(player, shopType))
        {
            bool kicked = await HandleNoHagglingAttemptsLeft(player, shopType, shopkeeperName, terminal);
            return new HaggleResult(originalCost, kicked);
        }
        
        // Special case: Trolls can't haggle at weapon shop if they already got race discount
        if (shopType == ShopType.Weapon && player.Race == CharacterRace.Troll)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("haggle.troll_already_discount"));
            terminal.WriteLine(Loc.Get("haggle.troll_get_out"));
            await Pacing.Wait(2000);
            return new HaggleResult(originalCost, false);
        }
        
        // Deduct haggling attempt
        DeductHagglingAttempt(player, shopType);
        
        // Get player's offer
        terminal.SetColor("yellow");
        terminal.Write(Loc.Get("haggle.give_offer"));
        
        var offerInput = await terminal.GetInput("");
        if (!long.TryParse(offerInput, out long offer) || offer <= 0 || offer >= originalCost)
        {
            terminal.WriteLine(Loc.Get("haggle.not_serious"), "red");
            await Pacing.Wait(1500);
            return new HaggleResult(originalCost, false);
        }
        
        // Calculate if haggling succeeds
        bool success = CalculateHagglingSuccess(player, originalCost, offer);
        
        if (!success)
        {
            await HandleHagglingFailure(shopType, terminal);
            return new HaggleResult(originalCost, false);
        }
        
        // Haggling succeeded!
        terminal.SetColor("green");
        terminal.WriteLine(Loc.Get("haggle.got_deal", player.DisplayName));
        
        if (await terminal.AskYesNoAsync(Loc.Get("haggle.accept_price")))
        {
            return new HaggleResult(offer, false);
        }
        
        return new HaggleResult(originalCost, false);
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
    internal static bool CalculateHagglingSuccess(Character player, long originalCost, long offer)
    {
        if (originalCost <= 0 || offer <= 0 || offer >= originalCost) return false;
        // Discount in whole percent, exact: the old (int)(ratio * 100) truncated, so a 10.9%
        // discount passed a 10% tier. Compare in integer arithmetic to avoid overflow too.
        // Maximum 20% discount allowed
        if ((originalCost - offer) * 5 > originalCost)
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
        
        return (originalCost - offer) * 100 <= (long)maxAllowedDiscount * originalCost;
    }
    
    /// <summary>
    /// Handle the case when player has no haggling attempts left
    /// </summary>
    private static async Task<bool> HandleNoHagglingAttemptsLeft(Character player, ShopType shopType, 
                                                          string shopkeeperName, TerminalEmulator terminal)
    {
        terminal.SetColor("red");
        
        switch (shopType)
        {
            case ShopType.Weapon:
                terminal.WriteLine(Loc.Get("haggle.weapon_angry", player.DisplayName));
                terminal.WriteLine(Loc.Get("haggle.accept_or_leave"));
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("haggle.weapon_face_red", shopkeeperName));
                break;

            case ShopType.Armor:
                terminal.WriteLine(Loc.Get("haggle.armor_damn", player.DisplayName));
                terminal.WriteLine(Loc.Get("haggle.accept_or_leave"));
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("haggle.armor_upset", shopkeeperName));
                break;
        }
        
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("haggle.insist_or_leave"));

        if (await terminal.AskYesNoAsync(Loc.Get("haggle.insist_prompt")))
        {
            // Player gets kicked out!
            terminal.SetColor("bright_red");
            terminal.WriteLine(Loc.Get("haggle.kicked_out"));

            // Create news entry (placeholder for news system)
            string shopName = shopType == ShopType.Weapon ? "Weaponshop" : "Armor Shop";
            terminal.WriteLine(Loc.Get("haggle.kicked_news", player.DisplayName, shopName));
            
            await Pacing.Wait(3000);
            return true;
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("haggle.end_discussion"));
            await Pacing.Wait(1500);
            return false;
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
                terminal.WriteLine(Loc.Get("haggle.weapon_fail_1"));
                terminal.WriteLine(Loc.Get("haggle.weapon_fail_2"));
                break;

            case ShopType.Armor:
                terminal.WriteLine(Loc.Get("haggle.armor_fail_1"));
                terminal.WriteLine(Loc.Get("haggle.armor_fail_2"));
                break;
        }
        
        await Pacing.Wait(2000);
    }
    
    /// <summary>
    /// Reset daily haggling attempts (called by daily system)
    /// </summary>
    public static void ResetDailyHaggling(Character player)
    {
        player.WeapHag = 3;  // Reset to 3 attempts per day
        player.ArmHag = 3;   // Reset to 3 attempts per day
        player.WeaponShopBarredUntilDay = 0; // v1.2: a kick-out lasts until the next day
        player.ArmorShopBarredUntilDay = 0;
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
