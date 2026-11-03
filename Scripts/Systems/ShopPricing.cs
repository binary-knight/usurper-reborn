namespace UsurperRemake.Systems;

/// <summary>
/// 1.3.10 (D2): the one price of a Weapon Shop or Armor Shop item. The list's affordability, the direct buy, auto-buy
/// and auto-buy's affordability filter all read it, so every path applies the same modifiers in the same order:
/// alignment and world event, city control, faction, the divine boon, difficulty, and (Weapon Shop only) a Troll's
/// discount. Auto-buy skipped alignment, world events and one of boon or difficulty, and filtered on the raw value.
/// </summary>
public static class ShopPricing
{
    /// <summary>The price before tax (what haggling starts from and the sale tax is figured on).</summary>
    public static long Price(Character player, long baseValue, bool weaponShop)
    {
        var alignmentModifier = AlignmentSystem.Instance.GetPriceModifier(player, isShadyShop: false);
        var worldEventModifier = WorldEventSystem.Instance.GlobalPriceModifier;
        long price = (long)(baseValue * alignmentModifier * worldEventModifier);
        price = CityControlSystem.Instance.ApplyDiscount(price, player);
        price = (long)(price * FactionSystem.Instance.GetShopPriceModifier());
        if (player.CachedBoonEffects?.ShopDiscountPercent > 0)
            price = (long)(price * (1.0 - player.CachedBoonEffects.ShopDiscountPercent));
        price = DifficultySystem.ApplyShopPriceMultiplier(price);
        // 1.3.2: a Troll pays 10 percent less for weapons, after every other modifier and before the tax
        if (weaponShop) price = HagglingEngine.ApplyRaceDiscount(player, price);
        return price;
    }

    /// <summary>The price with the king's and the city's tax: what the player pays without haggling.</summary>
    public static long Total(Character player, long baseValue, bool weaponShop) =>
        CityControlSystem.CalculateTaxedPrice(Price(player, baseValue, weaponShop)).Item3;
}
