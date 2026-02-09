using UsurperRemake;
using UsurperRemake.Utils;
using UsurperRemake.Systems;
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Magic Shop - Complete Pascal-compatible magical services
/// Features: Magic Items, Item Identification, Healing Potions, Trading
/// Based on Pascal MAGIC.PAS with full compatibility
/// </summary>
public partial class MagicShopLocation : BaseLocation
{
    private const string ShopTitle = "Magic Shop";
    private static string _ownerName = GameConfig.DefaultMagicShopOwner;

    /// <summary>
    /// Calculate level-scaled identification cost. Minimum 100 gold, scales with level.
    /// At level 1: 100 gold. At level 50: 2,600 gold. At level 100: 5,100 gold.
    /// </summary>
    private static long GetIdentificationCost(int playerLevel)
    {
        return Math.Max(100, 100 + (playerLevel * 50));
    }
    
    // Available magic items for sale
    private static List<Item> _magicInventory = new List<Item>();

    private Random random = new Random();
    
    // Local list to hold shop NPCs (replaces legacy global variable reference)
    private readonly List<NPC> npcs = new();
    
    public MagicShopLocation() : base(
        GameLocation.MagicShop,
        "Magic Shop",
        "A dark and dusty boutique filled with mysterious magical items."
    )
    {
        InitializeMagicInventory();

        // Add shop owner NPC
        var shopOwner = CreateShopOwner();
        npcs.Add(shopOwner);
    }

    protected override void SetupLocation()
    {
        base.SetupLocation();
    }

    protected override void DisplayLocation()
    {
        DisplayMagicShopMenu(currentPlayer);
    }

    protected override async Task<bool> ProcessChoice(string choice)
    {
        // Handle global quick commands first
        var (handled, shouldExit) = await TryProcessGlobalCommand(choice);
        if (handled) return shouldExit;

        return await HandleMagicShopChoice(choice.ToUpper(), currentPlayer);
    }
    
    private NPC CreateShopOwner()
    {
        var owner = new NPC(_ownerName, "merchant", CharacterClass.Magician, 30);
        owner.Level = 30;
        owner.Gold = 1000000L;
        owner.HP = owner.MaxHP = 150;
        owner.Strength = 12;
        owner.Defence = 15;
        owner.Agility = 20;
        owner.Charisma = 25;
        owner.Wisdom = 35;
        owner.Dexterity = 22;
        owner.Mana = owner.MaxMana = 200;
        
        // Magic shop owner personality - mystical and knowledgeable
        owner.Brain.Personality.Intelligence = 0.9f;
        owner.Brain.Personality.Mysticism = 1.0f;
        owner.Brain.Personality.Greed = 0.7f;
        owner.Brain.Personality.Patience = 0.8f;
        
        return owner;
    }
    
    private void InitializeMagicInventory()
    {
        _magicInventory.Clear();

        // ═══════════════════════════════════════════════════════════════════
        // RINGS - Varied magical rings for different builds and purposes
        // ═══════════════════════════════════════════════════════════════════

        // Basic Rings (Affordable starter items)
        AddMagicItem("Copper Ring", MagicItemType.Fingers, 500, dexterity: 1);
        AddMagicItem("Silver Band", MagicItemType.Fingers, 800, wisdom: 1, mana: 5);
        AddMagicItem("Iron Ring", MagicItemType.Fingers, 600, strength: 1, defense: 1);
        AddMagicItem("Jade Ring", MagicItemType.Fingers, 1000, wisdom: 2);

        // Combat Rings
        AddMagicItem("Ring of Dexterity", MagicItemType.Fingers, 1800, dexterity: 4);
        AddMagicItem("Ring of Might", MagicItemType.Fingers, 2000, strength: 4, attack: 2);
        AddMagicItem("Ring of Protection", MagicItemType.Fingers, 2800, defense: 3, magicRes: 10);
        AddMagicItem("Warrior's Signet", MagicItemType.Fingers, 3500, strength: 3, dexterity: 3, attack: 3);
        AddMagicItem("Berserker's Band", MagicItemType.Fingers, 4500, strength: 6, attack: 5, defense: -2);
        AddMagicItem("Ring of the Champion", MagicItemType.Fingers, 8000, strength: 5, dexterity: 5, defense: 3, attack: 4);

        // Magic Rings
        AddMagicItem("Mana Ring", MagicItemType.Fingers, 2200, mana: 15, wisdom: 2);
        AddMagicItem("Sage's Ring", MagicItemType.Fingers, 3500, wisdom: 4, mana: 18);
        AddMagicItem("Archmage's Band", MagicItemType.Fingers, 6500, wisdom: 6, mana: 30, magicRes: 15);
        AddMagicItem("Ring of Spellweaving", MagicItemType.Fingers, 7500, mana: 40, wisdom: 5, dexterity: 2);
        AddMagicItem("Master's Ring", MagicItemType.Fingers, 12000, wisdom: 8, mana: 50, dexterity: 4, magicRes: 20);

        // Ocean-Themed Rings (Lore items)
        AddMagicItem("Tidecaller's Ring", MagicItemType.Fingers, 5000, mana: 25, wisdom: 4,
            description: "A ring carved from pale blue coral, pulsing with the rhythm of distant waves.");
        AddMagicItem("Ring of the Deep", MagicItemType.Fingers, 7000, dexterity: 4, magicRes: 25, mana: 20,
            description: "From the lightless depths where the Ocean's dreams are darkest.");
        AddMagicItem("Wavecrest Signet", MagicItemType.Fingers, 9000, strength: 4, dexterity: 4, wisdom: 4,
            description: "The crest depicts a wave rising - or is it falling? The perspective shifts.");
        AddMagicItem("Fragment Ring", MagicItemType.Fingers, 15000, wisdom: 10, mana: 45, magicRes: 30,
            description: "Contains a droplet that never evaporates. It whispers of forgotten origins.");

        // Alignment-Specific Rings
        AddMagicItem("Ring of Radiance", MagicItemType.Fingers, 5500, wisdom: 5, mana: 20, goodOnly: true,
            description: "Glows softly in the presence of true virtue.");
        AddMagicItem("Paladin's Seal", MagicItemType.Fingers, 7500, strength: 4, defense: 4, magicRes: 20, goodOnly: true);
        AddMagicItem("Ring of Shadows", MagicItemType.Fingers, 4200, dexterity: 6, evilOnly: true);
        AddMagicItem("Nightbane Ring", MagicItemType.Fingers, 6000, dexterity: 5, attack: 4, evilOnly: true,
            description: "Forged in darkness, it hungers for the light of others.");
        AddMagicItem("Soulthief's Band", MagicItemType.Fingers, 9500, mana: 35, wisdom: 6, attack: 3, evilOnly: true);

        // Cursed Rings (Powerful but dangerous)
        AddMagicItem("Ring of Obsession", MagicItemType.Fingers, 3000, strength: 8, wisdom: -3, cursed: true,
            description: "It will not come off. It does not want to come off.");
        AddMagicItem("Withering Band", MagicItemType.Fingers, 4000, mana: 40, strength: -4, cursed: true);
        AddMagicItem("Ring of the Drowned", MagicItemType.Fingers, 6000, magicRes: 40, wisdom: 8, dexterity: -3, cursed: true,
            description: "Those who wear it hear the ocean calling them to return.");
        AddMagicItem("Betrayer's Signet", MagicItemType.Fingers, 8000, dexterity: 10, attack: 6, defense: -5, cursed: true);

        // Healing/Utility Rings
        AddMagicItem("Ring of Vitality", MagicItemType.Fingers, 3200, defense: 2, cureType: CureType.Plague);
        AddMagicItem("Ring of Purity", MagicItemType.Fingers, 4500, cureType: CureType.All, magicRes: 10, goodOnly: true);
        AddMagicItem("Antidote Ring", MagicItemType.Fingers, 2500, cureType: CureType.Plague, dexterity: 2);

        // ═══════════════════════════════════════════════════════════════════
        // AMULETS & NECKLACES - Powerful neck slot items
        // ═══════════════════════════════════════════════════════════════════

        // Basic Amulets
        AddMagicItem("Copper Medallion", MagicItemType.Neck, 600, defense: 1, wisdom: 1);
        AddMagicItem("Silver Pendant", MagicItemType.Neck, 900, mana: 8, magicRes: 5);
        AddMagicItem("Wooden Talisman", MagicItemType.Neck, 400, defense: 2);

        // Protective Amulets
        AddMagicItem("Amulet of Warding", MagicItemType.Neck, 2500, defense: 4, magicRes: 10);
        AddMagicItem("Pendant of Protection", MagicItemType.Neck, 3500, defense: 5, magicRes: 15);
        AddMagicItem("Guardian's Medallion", MagicItemType.Neck, 5000, defense: 6, magicRes: 20, strength: 2);
        AddMagicItem("Amulet of the Fortress", MagicItemType.Neck, 8500, defense: 10, magicRes: 25, strength: -2);
        AddMagicItem("Shield of the Ancients", MagicItemType.Neck, 15000, defense: 12, magicRes: 35, wisdom: 4);

        // Magical Amulets
        AddMagicItem("Amulet of Wisdom", MagicItemType.Neck, 2500, wisdom: 3, mana: 10);
        AddMagicItem("Crystal Pendant", MagicItemType.Neck, 4000, wisdom: 4, mana: 15, dexterity: 2);
        AddMagicItem("Starfire Amulet", MagicItemType.Neck, 6500, wisdom: 6, mana: 25, magicRes: 15);
        AddMagicItem("Amulet of the Arcane", MagicItemType.Neck, 10000, wisdom: 8, mana: 40, magicRes: 20);
        AddMagicItem("Pendant of Infinite Depths", MagicItemType.Neck, 18000, wisdom: 10, mana: 60, magicRes: 30,
            description: "Looking into its gem is like staring into a bottomless pool.");

        // Ocean-Themed Amulets (Lore items)
        AddMagicItem("Teardrop of Manwe", MagicItemType.Neck, 12000, wisdom: 8, mana: 35, magicRes: 25,
            description: "A crystallized tear from the Creator, shed in the first moment of separation.");
        AddMagicItem("Wavecaller's Pendant", MagicItemType.Neck, 7500, wisdom: 5, mana: 30, dexterity: 3,
            description: "The waves respond to those who wear this. Or perhaps they always did.");
        AddMagicItem("Amulet of the Depths", MagicItemType.Neck, 9500, defense: 5, magicRes: 30, wisdom: 6,
            description: "From where the pressure is so great that even light surrenders.");
        AddMagicItem("Tidebinder's Chain", MagicItemType.Neck, 11000, strength: 5, dexterity: 5, mana: 25,
            description: "Each link represents a binding - of water, of will, of memory.");
        AddMagicItem("Heart of the Ocean", MagicItemType.Neck, 25000, wisdom: 12, mana: 50, magicRes: 40, defense: 8,
            description: "It beats. Slowly. In rhythm with something vast and patient.");
        AddMagicItem("Dreamer's Medallion", MagicItemType.Neck, 20000, wisdom: 10, mana: 45, strength: 4, dexterity: 4,
            description: "The inscription reads: 'You are the Ocean, dreaming of waves.'");

        // Alignment-Specific Amulets
        AddMagicItem("Holy Symbol", MagicItemType.Neck, 4000, cureType: CureType.All, magicRes: 15, goodOnly: true);
        AddMagicItem("Amulet of Divine Grace", MagicItemType.Neck, 8000, wisdom: 6, mana: 25, defense: 4, goodOnly: true,
            description: "Blessed by those who remember the light before separation.");
        AddMagicItem("Medallion of the Pure", MagicItemType.Neck, 12000, cureType: CureType.All, wisdom: 8, defense: 6, goodOnly: true);
        AddMagicItem("Dark Medallion", MagicItemType.Neck, 4500, mana: 25, attack: 3, evilOnly: true);
        AddMagicItem("Pendant of Shadows", MagicItemType.Neck, 7000, dexterity: 6, attack: 4, magicRes: 15, evilOnly: true);
        AddMagicItem("Amulet of the Void", MagicItemType.Neck, 14000, mana: 40, wisdom: 7, magicRes: 25, evilOnly: true,
            description: "Darkness given form. It drinks light and gives nothing back.");

        // Cursed Amulets
        AddMagicItem("Choker of Binding", MagicItemType.Neck, 5000, strength: 10, dexterity: -4, cursed: true,
            description: "It tightens when you try to remove it.");
        AddMagicItem("Medallion of Madness", MagicItemType.Neck, 6500, wisdom: 12, mana: 40, defense: -5, cursed: true,
            description: "The voices it shares are ancient beyond measure.");
        AddMagicItem("Amulet of the Drowned God", MagicItemType.Neck, 10000, mana: 50, magicRes: 35, wisdom: -5, cursed: true,
            description: "A god died wearing this. Part of them remains.");
        AddMagicItem("Pendant of Eternal Hunger", MagicItemType.Neck, 8000, strength: 8, attack: 6, wisdom: -6, cursed: true);

        // Healing Amulets
        AddMagicItem("Amulet of Restoration", MagicItemType.Neck, 3500, cureType: CureType.Blindness, wisdom: 2);
        AddMagicItem("Healer's Pendant", MagicItemType.Neck, 5500, cureType: CureType.All, mana: 15, wisdom: 3);
        AddMagicItem("Plague Ward", MagicItemType.Neck, 2800, cureType: CureType.Plague, defense: 2);
        AddMagicItem("Purity Medallion", MagicItemType.Neck, 6000, cureType: CureType.All, defense: 4, magicRes: 15);

        // ═══════════════════════════════════════════════════════════════════
        // BELTS & GIRDLES - Waist slot items
        // ═══════════════════════════════════════════════════════════════════

        // Basic Belts
        AddMagicItem("Leather Belt", MagicItemType.Waist, 300, defense: 1);
        AddMagicItem("Studded Belt", MagicItemType.Waist, 600, defense: 2, strength: 1);
        AddMagicItem("Cloth Sash", MagicItemType.Waist, 400, mana: 5, dexterity: 1);

        // Strength Belts
        AddMagicItem("Belt of Strength", MagicItemType.Waist, 2000, strength: 3, attack: 2);
        AddMagicItem("Girdle of Giant Strength", MagicItemType.Waist, 5000, strength: 6, attack: 3, dexterity: -1);
        AddMagicItem("Champion's Belt", MagicItemType.Waist, 8000, strength: 5, attack: 4, defense: 3);
        AddMagicItem("Titan's Girdle", MagicItemType.Waist, 15000, strength: 10, attack: 6, defense: 4, dexterity: -2);

        // Dexterity Belts
        AddMagicItem("Girdle of Dexterity", MagicItemType.Waist, 2300, dexterity: 5, defense: 2);
        AddMagicItem("Acrobat's Sash", MagicItemType.Waist, 4000, dexterity: 6, attack: 2);
        AddMagicItem("Shadowdancer's Belt", MagicItemType.Waist, 7000, dexterity: 8, defense: 3, attack: 3);

        // Magic Belts
        AddMagicItem("Mage's Belt", MagicItemType.Waist, 3200, mana: 20, wisdom: 3);
        AddMagicItem("Sorcerer's Sash", MagicItemType.Waist, 5500, mana: 30, wisdom: 5, magicRes: 10);
        AddMagicItem("Arcane Girdle", MagicItemType.Waist, 9000, mana: 45, wisdom: 7, magicRes: 20);

        // Ocean-Themed Belts (Lore items)
        AddMagicItem("Tideweaver's Sash", MagicItemType.Waist, 6000, mana: 25, dexterity: 4, wisdom: 3,
            description: "Woven from kelp that grows where the Ocean dreams most vividly.");
        AddMagicItem("Belt of the Current", MagicItemType.Waist, 8500, dexterity: 7, strength: 3, attack: 3,
            description: "Move with the current, not against it. The water remembers the way.");
        AddMagicItem("Depthwalker's Girdle", MagicItemType.Waist, 12000, defense: 8, magicRes: 25, strength: 4,
            description: "Those who journey to the depths must carry their own pressure.");

        // Alignment Belts
        AddMagicItem("Crusader's Belt", MagicItemType.Waist, 6000, strength: 4, defense: 4, attack: 3, goodOnly: true);
        AddMagicItem("Belt of Righteous Fury", MagicItemType.Waist, 10000, strength: 6, attack: 5, defense: 2, goodOnly: true);
        AddMagicItem("Assassin's Sash", MagicItemType.Waist, 5500, dexterity: 7, attack: 4, defense: -1, evilOnly: true);
        AddMagicItem("Belt of Dark Power", MagicItemType.Waist, 9000, strength: 5, mana: 25, attack: 4, evilOnly: true);

        // Cursed Belts
        AddMagicItem("Cursed Girdle", MagicItemType.Waist, 5000, strength: -2, dexterity: 8, cursed: true);
        AddMagicItem("Belt of Burden", MagicItemType.Waist, 4000, defense: 10, dexterity: -6, cursed: true,
            description: "Heavy. So heavy. Like carrying the weight of worlds.");
        AddMagicItem("Parasite Sash", MagicItemType.Waist, 6000, mana: 35, strength: -3, defense: -2, cursed: true);

        // Healing Belts
        AddMagicItem("Belt of Health", MagicItemType.Waist, 3800, cureType: CureType.All, defense: 3);
        AddMagicItem("Girdle of Wellness", MagicItemType.Waist, 5000, cureType: CureType.Plague, strength: 3, defense: 3);
        AddMagicItem("Healer's Sash", MagicItemType.Waist, 4200, cureType: CureType.All, mana: 15, wisdom: 2);
    }

    // Description parameter for lore items
    private void AddMagicItem(string name, MagicItemType type, int value,
        int strength = 0, int defense = 0, int attack = 0, int dexterity = 0,
        int wisdom = 0, int mana = 0, int magicRes = 0, CureType cureType = CureType.None,
        bool goodOnly = false, bool evilOnly = false, bool cursed = false,
        string description = "")
    {
        var item = new Item();
        item.Name = name;
        item.Value = value;
        item.Type = ObjType.Magic;
        item.MagicType = type;
        item.IsShopItem = true;
        if (!string.IsNullOrEmpty(description))
            item.Description[0] = description;

        // Set base stats
        item.Strength = strength;
        item.Defence = defense;
        item.Attack = attack;
        item.Dexterity = dexterity;
        item.Wisdom = wisdom;

        // Set magic properties
        item.MagicProperties.Mana = mana;
        item.MagicProperties.Wisdom = wisdom;
        item.MagicProperties.Dexterity = dexterity;
        item.MagicProperties.MagicResistance = magicRes;
        item.MagicProperties.DiseaseImmunity = cureType;

        // Set restrictions
        item.OnlyForGood = goodOnly;
        item.OnlyForEvil = evilOnly;
        item.IsCursed = cursed;

        _magicInventory.Add(item);
    }
    
    // Track daily love spell usage (NPC name -> times used today)
    private static HashSet<string> _bindingOfSoulsUsedToday = new();
    private static int _lastDailyResetDay = -1;

    private void ResetDailyTracking()
    {
        int currentDay = StoryProgressionSystem.Instance?.CurrentGameDay ?? 0;
        if (currentDay != _lastDailyResetDay)
        {
            _bindingOfSoulsUsedToday.Clear();
            _lastDailyResetDay = currentDay;
        }
    }

    /// <summary>
    /// Get loyalty discount based on total gold spent at magic shop
    /// </summary>
    private float GetLoyaltyDiscount(Character player)
    {
        long totalSpent = player.Statistics?.TotalMagicShopGoldSpent ?? 0;
        if (totalSpent >= 200000) return 0.85f;
        if (totalSpent >= 50000) return 0.90f;
        if (totalSpent >= 10000) return 0.95f;
        return 1.0f;
    }

    /// <summary>
    /// Apply all price modifiers (alignment, world event, faction, city control, loyalty)
    /// </summary>
    private long ApplyAllPriceModifiers(long basePrice, Character player)
    {
        var alignmentModifier = AlignmentSystem.Instance.GetPriceModifier(player, isShadyShop: false);
        var worldEventModifier = WorldEventSystem.Instance.GlobalPriceModifier;
        var factionModifier = FactionSystem.Instance.GetShopPriceModifier();
        var loyaltyModifier = GetLoyaltyDiscount(player);

        long adjusted = (long)(basePrice * alignmentModifier * worldEventModifier * factionModifier * loyaltyModifier);
        adjusted = CityControlSystem.Instance.ApplyDiscount(adjusted, player);
        return Math.Max(1, adjusted);
    }

    private async Task<bool> HandleMagicShopChoice(string choice, Character player)
    {
        ResetDailyTracking();

        switch (choice)
        {
            case "A":
                await BuyAccessory(player);
                return false;
            case "S":
                SellItem(player);
                await terminal.WaitForKey();
                return false;
            case "E":
                await EnchantEquipment(player);
                return false;
            case "I":
                IdentifyItem(player);
                await terminal.WaitForKey();
                return false;
            case "C":
                await RemoveCurse(player);
                await terminal.WaitForKey();
                return false;
            case "W":
                await RemoveEnchantment(player);
                return false;
            case "H":
                BuyHealingPotions(player);
                await terminal.WaitForKey();
                return false;
            case "M":
                await BuyManaPotions(player);
                return false;
            case "D":
                await BuyDungeonResetScroll(player);
                return false;
            case "V":
                await CastLoveSpell(player);
                return false;
            case "K":
                await CastDeathSpell(player);
                return false;
            case "Y":
                await SpellLearningSystem.ShowSpellLearningMenu(player, terminal);
                return false;
            case "G":
                await ScryNPC(player);
                return false;
            case "T":
                await TalkToOwnerEnhanced(player);
                return false;
            case "F":
                await EnchantItem(player);
                await terminal.WaitForKey();
                return false;
            case "R":
            case "Q":
                await NavigateToLocation(GameLocation.MainStreet);
                return true;
            default:
                return false;
        }
    }

    private void DisplayMagicShopMenu(Character player)
    {
        terminal.ClearScreen();

        // Shop header - standardized format
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔═════════════════════════════════════════════════════════════════════════════╗");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("║                              MAGIC SHOP                                     ║");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╚═════════════════════════════════════════════════════════════════════════════╝");
        DisplayMessage("");
        DisplayMessage($"Run by {_ownerName} the gnome", "gray");
        DisplayMessage("");
        
        // Shop description
        DisplayMessage("You enter the dark and dusty boutique, filled with all sorts", "gray");
        DisplayMessage("of strange objects. As you examine the place you notice a", "gray");
        DisplayMessage("few druids and wizards searching for orbs and other mysterious items.", "gray");
        DisplayMessage("When you reach the counter you try to remember what you were looking for.", "gray");
        DisplayMessage("");
        
        // Greeting
        string raceGreeting = GetRaceGreeting(player.Race);
        DisplayMessage($"What shall it be {raceGreeting}?", "cyan");
        DisplayMessage("");
        
        // Player gold display
        DisplayMessage($"(You have {player.Gold:N0} gold coins)", "gray");

        // Show alignment price modifier
        var alignmentModifier = AlignmentSystem.Instance.GetPriceModifier(player, isShadyShop: false);
        if (alignmentModifier != 1.0f)
        {
            var (alignText, alignColor) = AlignmentSystem.Instance.GetAlignmentDisplay(player);
            if (alignmentModifier < 1.0f)
                DisplayMessage($"  Your {alignText} alignment grants you a {(int)((1.0f - alignmentModifier) * 100)}% discount!", alignColor);
            else
                DisplayMessage($"  Your {alignText} alignment causes a {(int)((alignmentModifier - 1.0f) * 100)}% markup.", alignColor);
        }

        // Show world event price modifier
        var worldEventModifier = WorldEventSystem.Instance.GlobalPriceModifier;
        if (Math.Abs(worldEventModifier - 1.0f) > 0.01f)
        {
            if (worldEventModifier < 1.0f)
                DisplayMessage($"  World Events: {(int)((1.0f - worldEventModifier) * 100)}% discount active!", "bright_green");
            else
                DisplayMessage($"  World Events: {(int)((worldEventModifier - 1.0f) * 100)}% price increase!", "red");
        }
        DisplayMessage("");

        // Loyalty discount display
        float loyaltyDiscount = GetLoyaltyDiscount(player);
        if (loyaltyDiscount < 1.0f)
        {
            int discountPct = (int)((1.0f - loyaltyDiscount) * 100);
            DisplayMessage($"  Loyal Customer: {discountPct}% discount on all services!", "bright_green");
        }
        DisplayMessage("");

        // Menu options - two-column layout
        DisplayMessage("  ═══ Shopping ═══                      ═══ Enchanting ═══", "cyan");
        terminal.WriteLine("");
        WriteMenuRow("A", "bright_green", "ccessories (Rings/Necklaces)", "E", "bright_yellow", "nchant Equipment");
        WriteMenuRow("S", "bright_green", "ell Item", "W", "bright_yellow", " Remove Enchantment");
        WriteMenuRow("I", "bright_green", "dentify Item", "C", "bright_yellow", "urse Removal");
        terminal.WriteLine("");
        DisplayMessage("  ═══ Potions & Scrolls ═══             ═══ Arcane Arts ═══", "cyan");
        terminal.WriteLine("");
        WriteMenuRow("H", "bright_green", "ealing Potions", "V", "bright_magenta", " Love Spells");
        WriteMenuRow("M", "bright_green", "ana Potions", "K", "bright_magenta", " Dark Arts");
        WriteMenuRow("D", "bright_green", "ungeon Reset Scroll", "Y", "bright_magenta", " Study Spells");
        terminal.Write(new string(' ', 42));
        WriteMenuKey("G", "bright_magenta", " Scrying (NPC Info)");
        terminal.WriteLine("");
        terminal.WriteLine("");
        WriteMenuRow("T", "bright_cyan", $"alk to {_ownerName}", "F", "gray", " Fortify Curio");
        terminal.Write("  ");
        WriteMenuKey("R", "bright_red", "eturn to street");
        terminal.WriteLine("");
        terminal.WriteLine("");
    }
    
    private string GetRaceGreeting(CharacterRace race)
    {
        return race switch
        {
            CharacterRace.Human => "human",
            CharacterRace.Elf => "elf",
            CharacterRace.Dwarf => "dwarf", 
            CharacterRace.Hobbit => "hobbit",
            CharacterRace.Gnome => "fellow gnome",
            _ => "traveler"
        };
    }
    
    // Legacy curio listing/buying methods removed in v0.26.0 - replaced by modern Accessory Shop [A]
    
    private void SellItem(Character player)
    {
        DisplayMessage("");

        if (player.Inventory.Count == 0)
        {
            DisplayMessage("You have nothing to sell.", "gray");
            return;
        }

        // Get Shadows faction fence bonus modifier (1.0 normal, 1.2 with Shadows)
        var fenceModifier = FactionSystem.Instance.GetFencePriceModifier();
        bool hasFenceBonus = fenceModifier > 1.0f;

        if (hasFenceBonus)
        {
            DisplayMessage("  [Shadows Bonus: +20% sell prices]", "bright_magenta");
            DisplayMessage("");
        }

        DisplayMessage("Your inventory:", "cyan");
        for (int i = 0; i < player.Inventory.Count; i++)
        {
            var item = player.Inventory[i];
            long displayPrice = (long)((item.Value / 2) * fenceModifier);
            DisplayMessage($"{i + 1}. {item.Name} (worth {displayPrice:N0} gold)", "white");
        }

        DisplayMessage("");
        DisplayMessage("Enter item # to sell (0 to cancel): ", "yellow", false);
        string input = terminal.GetInputSync("");

        if (int.TryParse(input, out int itemIndex) && itemIndex > 0 && itemIndex <= player.Inventory.Count)
        {
            var item = player.Inventory[itemIndex - 1];

            // Check if cursed
            if (item.IsCursed)
            {
                DisplayMessage("");
                DisplayMessage($"The {item.Name} is CURSED and cannot be sold!", "red");
                DisplayMessage("Visit the Healer to have the curse removed.", "gray");
                return;
            }

            long sellPrice = (long)((item.Value / 2) * fenceModifier); // Apply faction bonus

            // Check if shop wants this item type
            if (item.Type == ObjType.Magic || item.MagicType != MagicItemType.None)
            {
                DisplayMessage($"Sell {item.Name} for {sellPrice:N0} gold? (Y/N): ", "yellow", false);
                var confirm = terminal.GetInputSync("").ToUpper();
                DisplayMessage("");

                if (confirm == "Y")
                {
                    player.Inventory.RemoveAt(itemIndex - 1);
                    player.Gold += sellPrice;
                    player.Statistics.RecordSale(sellPrice);
                    DisplayMessage("Deal!", "green");
                    DisplayMessage($"You sold the {item.Name} for {sellPrice:N0} gold.", "gray");
                }
            }
            else
            {
                DisplayMessage($"'{_ownerName} examines the {item.Name} and shakes their head.'", "gray");
                DisplayMessage("'I only deal in magical goods - rings, amulets, belts, and the like.'", "cyan");
                DisplayMessage("'Try the weapon or armor shops for mundane items.'", "cyan");
            }
        }
    }
    
    private void IdentifyItem(Character player)
    {
        DisplayMessage("");

        var unidentifiedItems = player.Inventory.Where(item => !item.IsIdentified).ToList();
        if (unidentifiedItems.Count == 0)
        {
            DisplayMessage("You have no unidentified items.", "gray");
            return;
        }

        long identifyCost = GetIdentificationCost(player.Level);

        DisplayMessage($"═══ Unidentified Items ({unidentifiedItems.Count}) ═══", "cyan");
        DisplayMessage($"Identification costs {identifyCost:N0} gold per item. You have {player.Gold:N0} gold.", "gray");
        DisplayMessage("");

        for (int i = 0; i < unidentifiedItems.Count; i++)
        {
            string unidName = LootGenerator.GetUnidentifiedName(unidentifiedItems[i]);
            DisplayMessage($"  {i + 1}. {unidName}", "magenta");
        }

        DisplayMessage("");
        DisplayMessage("Enter item # to identify (0 to cancel): ", "yellow", false);
        string input = terminal.GetInputSync("");

        if (int.TryParse(input, out int itemIndex) && itemIndex > 0 && itemIndex <= unidentifiedItems.Count)
        {
            if (player.Gold < identifyCost)
            {
                DisplayMessage("You don't have enough gold for identification!", "red");
                return;
            }

            var item = unidentifiedItems[itemIndex - 1];
            string unidName = LootGenerator.GetUnidentifiedName(item);
            DisplayMessage($"Identify the {unidName} for {identifyCost:N0} gold? (Y/N): ", "yellow", false);
            var confirm = terminal.GetInputSync("").ToUpper();
            DisplayMessage("");

            if (confirm == "Y")
            {
                player.Gold -= identifyCost;
                item.IsIdentified = true;

                DisplayMessage($"{_ownerName} passes the item through a shimmer of arcane light...", "gray");
                DisplayMessage("");
                DisplayMessage($"It is a {item.Name}!", "bright_green");
                DisplayMessage("");

                // Show full item details
                DisplayItemDetails(item);
            }
        }
    }
    
    private void DisplayItemDetails(Item item)
    {
        DisplayMessage("═══ Item Properties ═══", "cyan");
        DisplayMessage($"Name: {item.Name}", "white");
        DisplayMessage($"Value: {item.Value:N0} gold", "yellow");
        
        if (item.Strength != 0) DisplayMessage($"Strength: {(item.Strength > 0 ? "+" : "")}{item.Strength}", "green");
        if (item.Defence != 0) DisplayMessage($"Defence: {(item.Defence > 0 ? "+" : "")}{item.Defence}", "green");
        if (item.Attack != 0) DisplayMessage($"Attack: {(item.Attack > 0 ? "+" : "")}{item.Attack}", "green");
        if (item.Dexterity != 0) DisplayMessage($"Dexterity: {(item.Dexterity > 0 ? "+" : "")}{item.Dexterity}", "green");
        if (item.Wisdom != 0) DisplayMessage($"Wisdom: {(item.Wisdom > 0 ? "+" : "")}{item.Wisdom}", "green");
        if (item.MagicProperties.Mana != 0) DisplayMessage($"Mana: {(item.MagicProperties.Mana > 0 ? "+" : "")}{item.MagicProperties.Mana}", "blue");
        
        if (item.StrengthRequired > 0) DisplayMessage($"Strength Required: {item.StrengthRequired}", "red");
        
        // Disease curing
        if (item.MagicProperties.DiseaseImmunity != CureType.None)
        {
            string cureText = item.MagicProperties.DiseaseImmunity switch
            {
                CureType.All => "It cures Every known disease!",
                CureType.Blindness => "It cures Blindness!",
                CureType.Plague => "It cures the Plague!",
                CureType.Smallpox => "It cures Smallpox!",
                CureType.Measles => "It cures Measles!",
                CureType.Leprosy => "It cures Leprosy!",
                _ => ""
            };
            DisplayMessage(cureText, "green");
        }
        
        // Restrictions
        if (item.OnlyForGood) DisplayMessage("This item can only be used by good characters.", "blue");
        if (item.OnlyForEvil) DisplayMessage("This item can only be used by evil characters.", "red");
        if (item.IsCursed) DisplayMessage($"The {item.Name} is CURSED!", "darkred");
    }
    
    private void BuyHealingPotions(Character player)
    {
        DisplayMessage("");

        // Calculate potion price with all modifiers (alignment, world events, faction, city, loyalty)
        long potionPrice = ApplyAllPriceModifiers(player.Level * GameConfig.HealingPotionLevelMultiplier, player);
        int maxPotionsCanBuy = potionPrice > 0 ? (int)(player.Gold / potionPrice) : 0;
        int maxPotionsCanCarry = GameConfig.MaxHealingPotions - (int)player.Healing;
        int maxPotions = Math.Min(maxPotionsCanBuy, maxPotionsCanCarry);

        if (player.Gold < potionPrice)
        {
            DisplayMessage("You don't have enough gold!", "red");
            return;
        }

        if (player.Healing >= GameConfig.MaxHealingPotions)
        {
            DisplayMessage("You already have the maximum number of healing potions!", "red");
            return;
        }

        if (maxPotions <= 0)
        {
            DisplayMessage("You can't afford any potions!", "red");
            return;
        }

        DisplayMessage($"Current price is {potionPrice:N0} gold per potion.", "gray");
        DisplayMessage($"You have {player.Gold:N0} gold.", "gray");
        DisplayMessage($"You have {player.Healing} potions.", "gray");
        DisplayMessage("");

        DisplayMessage($"How many? (max {maxPotions} potions): ", "yellow", false);
        string input = terminal.GetInputSync("");

        if (int.TryParse(input, out int quantity) && quantity > 0 && quantity <= maxPotions)
        {
            long totalCost = quantity * potionPrice;

            if (player.Gold >= totalCost)
            {
                player.Gold -= totalCost;
                player.Healing += quantity;

                // Process city tax share from this sale
                CityControlSystem.Instance.ProcessSaleTax(totalCost);

                DisplayMessage($"Ok, it's a deal. You buy {quantity} potions.", "green");
                DisplayMessage($"Total cost: {totalCost:N0} gold.", "gray");

                player.Statistics?.RecordGoldSpent(totalCost);
                player.Statistics?.RecordMagicShopPurchase(totalCost);
            }
            else
            {
                DisplayMessage($"{_ownerName} looks at you and laughs...Who are you trying to fool?", "red");
            }
        }
        else
        {
            DisplayMessage("Aborted.", "red");
        }
    }
    
    // Legacy TalkToOwner removed in v0.26.0 - replaced by TalkToOwnerEnhanced [T]

    /// <summary>
    /// List magic items organized by category with pagination
    /// </summary>
    // Legacy ListMagicItemsByCategory removed in v0.26.0 - replaced by modern Accessory Shop [A]

    /// <summary>
    /// Remove curse from an item - expensive but essential service
    /// </summary>
    private async Task RemoveCurse(Character player)
    {
        DisplayMessage("");
        DisplayMessage("═══ Curse Removal Service ═══", "magenta");
        DisplayMessage("");
        DisplayMessage($"{_ownerName} peers at you with knowing eyes.", "gray");
        DisplayMessage("'Curses are tricky things. They bind to the soul, not just the flesh.'", "cyan");
        DisplayMessage("'I can break such bonds, but it requires... significant effort.'", "cyan");
        DisplayMessage("");

        // Find cursed items in inventory
        var cursedItems = player.Inventory.Where(i => i.IsCursed).ToList();

        if (cursedItems.Count == 0)
        {
            DisplayMessage("You have no cursed items in your possession.", "gray");
            DisplayMessage("'Consider yourself fortunate,' the gnome says with a wry smile.", "cyan");
            return;
        }

        DisplayMessage("Cursed items in your inventory:", "darkred");
        for (int i = 0; i < cursedItems.Count; i++)
        {
            var item = cursedItems[i];
            long removalCost = CalculateCurseRemovalCost(item, player);
            DisplayMessage($"{i + 1}. {item.Name} - Removal cost: {removalCost:N0} gold", "red");

            // Show the curse's nature
            DisplayCurseDetails(item);
        }

        DisplayMessage("");
        var input = await terminal.GetInput("Enter item # to uncurse (0 to cancel): ");

        if (!int.TryParse(input, out int itemIndex) || itemIndex <= 0 || itemIndex > cursedItems.Count)
            return;

        var targetItem = cursedItems[itemIndex - 1];
        long cost = CalculateCurseRemovalCost(targetItem, player);

        if (player.Gold < cost)
        {
            DisplayMessage("");
            DisplayMessage("'You lack the gold for such a ritual,' the gnome says sadly.", "cyan");
            DisplayMessage("'The curse remains.'", "red");
            return;
        }

        DisplayMessage("");
        var confirm = await terminal.GetInput($"Remove the curse from {targetItem.Name} for {cost:N0} gold? (Y/N): ");

        if (confirm.ToUpper() == "Y")
        {
            player.Gold -= cost;

            // Dramatic curse removal scene
            DisplayMessage("");
            DisplayMessage($"{_ownerName} takes the {targetItem.Name} and places it in a circle of salt.", "gray");
            DisplayMessage("Ancient words fill the air, words older than the walls of this shop...", "gray");
            await Task.Delay(500);
            DisplayMessage("The item shudders. Something dark rises from it like smoke...", "magenta");
            await Task.Delay(500);
            DisplayMessage("And dissipates into nothingness.", "white");
            DisplayMessage("");

            // Remove the curse
            targetItem.IsCursed = false;
            targetItem.Cursed = false;

            // Add a small bonus for uncursed items (they're purified)
            if (targetItem.MagicProperties.MagicResistance < 0)
                targetItem.MagicProperties.MagicResistance = Math.Abs(targetItem.MagicProperties.MagicResistance) / 2;

            DisplayMessage($"The {targetItem.Name} is now free of its curse!", "bright_green");
            DisplayMessage("'It is done,' the gnome says, looking tired but satisfied.", "cyan");

            player.Statistics?.RecordGoldSpent(cost);
            player.Statistics?.RecordMagicShopPurchase(cost);

            // Special Ocean lore for certain items
            if (targetItem.Name.Contains("Drowned") || targetItem.Name.Contains("Ocean") || targetItem.Name.Contains("Deep"))
            {
                DisplayMessage("");
                DisplayMessage("'This item... it remembers the depths. The Ocean's dreams.'", "magenta");
                DisplayMessage("'Perhaps it was not cursed, but merely... homesick.'", "magenta");
            }
        }
    }

    private long CalculateCurseRemovalCost(Item item, Character player)
    {
        // Base cost is 2x the item's value
        long baseCost = item.Value * 2;

        // Powerful curses cost more (items with big negative stats)
        int cursePower = 0;
        if (item.Strength < 0) cursePower += Math.Abs(item.Strength) * 100;
        if (item.Defence < 0) cursePower += Math.Abs(item.Defence) * 100;
        if (item.Dexterity < 0) cursePower += Math.Abs(item.Dexterity) * 100;
        if (item.Wisdom < 0) cursePower += Math.Abs(item.Wisdom) * 100;

        baseCost += cursePower;

        // Minimum cost
        return Math.Max(500, baseCost);
    }

    private void DisplayCurseDetails(Item item)
    {
        var negatives = new List<string>();
        if (item.Strength < 0) negatives.Add($"Str{item.Strength}");
        if (item.Defence < 0) negatives.Add($"Def{item.Defence}");
        if (item.Dexterity < 0) negatives.Add($"Dex{item.Dexterity}");
        if (item.Wisdom < 0) negatives.Add($"Wis{item.Wisdom}");

        if (negatives.Count > 0)
            DisplayMessage($"     Curse effect: {string.Join(", ", negatives)}", "darkred");

        if (HasLoreDescription(item))
            DisplayMessage($"     \"{item.Description[0]}\"", "gray");
    }

    /// <summary>
    /// Helper to check if an item has a lore description
    /// </summary>
    private bool HasLoreDescription(Item item)
    {
        return item.Description != null &&
               item.Description.Count > 0 &&
               !string.IsNullOrEmpty(item.Description[0]);
    }

    /// <summary>
    /// Enchant or bless items - add magical properties
    /// </summary>
    private async Task EnchantItem(Character player)
    {
        DisplayMessage("");
        DisplayMessage("═══ Enchantment & Blessing Services ═══", "magenta");
        DisplayMessage("");
        DisplayMessage($"{_ownerName} waves a gnarled hand over a collection of glowing runes.", "gray");
        DisplayMessage("'I can imbue your items with magical essence, or seek blessings from the divine.'", "cyan");
        DisplayMessage("");

        DisplayMessage("(1) Minor Enchantment   - +2 to one stat           2,000 gold", "gray");
        DisplayMessage("(2) Standard Enchant    - +4 to one stat           5,000 gold", "gray");
        DisplayMessage("(3) Greater Enchantment - +6 to one stat          12,000 gold", "gray");
        DisplayMessage("(4) Divine Blessing     - +3 to all stats         25,000 gold", "blue");
        DisplayMessage("(5) Ocean's Touch       - Special mana bonus      15,000 gold", "cyan");
        DisplayMessage("(6) Ward Against Evil   - +20 Magic Resistance     8,000 gold", "yellow");
        DisplayMessage("");

        var input = await terminal.GetInput("Choose enchantment type (0 to cancel): ");
        if (!int.TryParse(input, out int enchantChoice) || enchantChoice <= 0 || enchantChoice > 6)
            return;

        long[] costs = { 0, 2000, 5000, 12000, 25000, 15000, 8000 };
        long cost = costs[enchantChoice];

        if (player.Gold < cost)
        {
            DisplayMessage("");
            DisplayMessage("'The magical arts require material compensation,' the gnome says pointedly.", "cyan");
            DisplayMessage($"You need {cost:N0} gold for this enchantment.", "red");
            await terminal.WaitForKey();
            return;
        }

        // Show items that can be enchanted
        var enchantableItems = player.Inventory.Where(i =>
            i.Type == ObjType.Magic || i.MagicType != MagicItemType.None).ToList();

        if (enchantableItems.Count == 0)
        {
            DisplayMessage("");
            DisplayMessage("You have no items suitable for enchantment.", "gray");
            DisplayMessage("'Bring me rings, amulets, or belts,' the gnome suggests.", "cyan");
            await terminal.WaitForKey();
            return;
        }

        DisplayMessage("");
        DisplayMessage("Items that can be enchanted:", "cyan");
        for (int i = 0; i < enchantableItems.Count; i++)
        {
            var item = enchantableItems[i];
            string status = item.IsCursed ? " [CURSED - cannot enchant]" : "";
            DisplayMessage($"{i + 1}. {item.Name}{status}", item.IsCursed ? "red" : "white");
        }

        DisplayMessage("");
        input = await terminal.GetInput("Choose item to enchant (0 to cancel): ");

        if (!int.TryParse(input, out int itemIndex) || itemIndex <= 0 || itemIndex > enchantableItems.Count)
            return;

        var targetItem = enchantableItems[itemIndex - 1];

        if (targetItem.IsCursed)
        {
            DisplayMessage("");
            DisplayMessage("'I cannot enchant a cursed item. The dark magic would consume my work.'", "red");
            DisplayMessage("'Remove the curse first, then return.'", "cyan");
            await terminal.WaitForKey();
            return;
        }

        // For stat-boosting enchants, let player choose the stat
        int statChoice = 0;
        if (enchantChoice >= 1 && enchantChoice <= 3)
        {
            DisplayMessage("");
            DisplayMessage("Choose stat to enhance:", "cyan");
            DisplayMessage("(1) Strength  (2) Defence  (3) Dexterity  (4) Wisdom  (5) Attack", "gray");
            input = await terminal.GetInput("Choice: ");
            if (!int.TryParse(input, out statChoice) || statChoice <= 0 || statChoice > 5)
                return;
        }

        DisplayMessage("");
        var confirm = await terminal.GetInput($"Enchant {targetItem.Name} for {cost:N0} gold? (Y/N): ");

        if (confirm.ToUpper() != "Y")
            return;

        player.Gold -= cost;

        // Apply the enchantment
        int bonus = enchantChoice switch
        {
            1 => 2,
            2 => 4,
            3 => 6,
            _ => 0
        };

        DisplayMessage("");
        DisplayMessage($"{_ownerName} begins the enchantment ritual...", "gray");
        await Task.Delay(500);

        switch (enchantChoice)
        {
            case 1:
            case 2:
            case 3:
                ApplyStatEnchant(targetItem, statChoice, bonus);
                DisplayMessage($"Magical energy flows into the {targetItem.Name}!", "magenta");
                break;

            case 4: // Divine Blessing
                targetItem.Strength += 3;
                targetItem.Defence += 3;
                targetItem.Dexterity += 3;
                targetItem.Wisdom += 3;
                targetItem.Attack += 3;
                targetItem.MagicProperties.Wisdom += 3;
                DisplayMessage("Divine light suffuses the item with holy power!", "bright_yellow");
                DisplayMessage($"The {targetItem.Name} is now blessed!", "blue");
                break;

            case 5: // Ocean's Touch
                targetItem.MagicProperties.Mana += 30;
                targetItem.Wisdom += 2;
                targetItem.MagicProperties.Wisdom += 2;
                DisplayMessage("The scent of salt and distant tides fills the air...", "cyan");
                DisplayMessage($"The {targetItem.Name} now carries the Ocean's blessing!", "blue");
                DisplayMessage("'The waves remember all who seek their wisdom,' the gnome whispers.", "gray");
                break;

            case 6: // Ward Against Evil
                targetItem.MagicProperties.MagicResistance += 20;
                targetItem.Defence += 2;
                DisplayMessage("Protective runes flare to life on the item's surface!", "yellow");
                DisplayMessage($"The {targetItem.Name} now provides magical protection!", "green");
                break;
        }

        // Update item name to show it's been enchanted (if not already)
        if (!targetItem.Name.Contains("+") && !targetItem.Name.Contains("Blessed") &&
            !targetItem.Name.Contains("Enchanted"))
        {
            string suffix = enchantChoice switch
            {
                4 => " (Blessed)",
                5 => " (Ocean-Touched)",
                6 => " (Warded)",
                _ => $" +{bonus}"
            };

            // Only add suffix if name isn't too long
            if (targetItem.Name.Length + suffix.Length < 35)
                targetItem.Name += suffix;
        }

        // Increase item value
        targetItem.Value = (long)(targetItem.Value * 1.5);

        DisplayMessage("");
        DisplayMessage("The enchantment is complete!", "bright_green");

        player.Statistics?.RecordGoldSpent(cost);
        player.Statistics?.RecordMagicShopPurchase(cost);
    }

    private void ApplyStatEnchant(Item item, int statChoice, int bonus)
    {
        switch (statChoice)
        {
            case 1:
                item.Strength += bonus;
                break;
            case 2:
                item.Defence += bonus;
                break;
            case 3:
                item.Dexterity += bonus;
                item.MagicProperties.Dexterity += bonus;
                break;
            case 4:
                item.Wisdom += bonus;
                item.MagicProperties.Wisdom += bonus;
                break;
            case 5:
                item.Attack += bonus;
                break;
        }
    }

    /// <summary>
    /// Buy a Dungeon Reset Scroll to reset a dungeon floor's monsters
    /// </summary>
    private async Task BuyDungeonResetScroll(Character player)
    {
        DisplayMessage("");
        DisplayMessage("═══ Dungeon Reset Scroll ═══", "magenta");
        DisplayMessage("");
        DisplayMessage($"{_ownerName} pulls out an ancient scroll covered in glowing runes.", "gray");
        DisplayMessage("'This scroll contains the power to disturb the dungeon's slumber.'", "cyan");
        DisplayMessage("'Monsters that have been slain will rise again, treasures replenished.'", "cyan");
        DisplayMessage("'Use it wisely - the dungeon remembers those who abuse its cycles.'", "cyan");
        DisplayMessage("");

        // Get floors that have been cleared (have state and were cleared within respawn period)
        var clearedFloors = player.DungeonFloorStates
            .Where(kvp => kvp.Value.EverCleared && !kvp.Value.IsPermanentlyClear && !kvp.Value.ShouldRespawn())
            .OrderBy(kvp => kvp.Key)
            .ToList();

        if (clearedFloors.Count == 0)
        {
            DisplayMessage("You have no dungeon floors eligible for reset.", "gray");
            DisplayMessage("'Floors that are permanently cleared or already respawning cannot be reset.'", "cyan");
            DisplayMessage("'Come back when you've conquered some dungeon levels.'", "cyan");
            return;
        }

        // Calculate price based on player level
        long scrollPrice = CalculateResetScrollPrice(player.Level);

        DisplayMessage($"Reset Scroll Price: {scrollPrice:N0} gold", "yellow");
        DisplayMessage($"You have: {player.Gold:N0} gold", "gray");
        DisplayMessage("");

        if (player.Gold < scrollPrice)
        {
            DisplayMessage("'You lack the gold for such powerful magic,' the gnome says.", "red");
            return;
        }

        DisplayMessage("Floors available for reset:", "cyan");
        for (int i = 0; i < clearedFloors.Count; i++)
        {
            var floor = clearedFloors[i];
            var hoursSinceCleared = (DateTime.Now - floor.Value.LastClearedAt).TotalHours;
            var hoursUntilRespawn = DungeonFloorState.RESPAWN_HOURS - hoursSinceCleared;
            DisplayMessage($"{i + 1}. Floor {floor.Key} (respawns naturally in {hoursUntilRespawn:F1} hours)", "white");
        }

        DisplayMessage("");
        DisplayMessage("Enter floor # to reset (0 to cancel): ", "yellow", false);
        string input = await terminal.GetInput("");

        if (!int.TryParse(input, out int floorChoice) || floorChoice <= 0 || floorChoice > clearedFloors.Count)
        {
            DisplayMessage("Cancelled.", "gray");
            return;
        }

        var selectedFloor = clearedFloors[floorChoice - 1];

        DisplayMessage("");
        DisplayMessage($"Reset Floor {selectedFloor.Key} for {scrollPrice:N0} gold? (Y/N): ", "yellow", false);
        var confirm = (await terminal.GetInput("")).ToUpper();

        if (confirm == "Y")
        {
            player.Gold -= scrollPrice;

            // Reset the floor by clearing its LastClearedAt timestamp
            // This will make ShouldRespawn() return true on next visit
            selectedFloor.Value.LastClearedAt = DateTime.MinValue;

            // Also clear the room states so monsters respawn
            foreach (var room in selectedFloor.Value.RoomStates.Values)
            {
                room.IsCleared = false;  // Monsters will respawn
            }

            DisplayMessage("");
            DisplayMessage($"{_ownerName} unrolls the scroll and speaks words of power...", "gray");
            DisplayMessage("The parchment ignites with ethereal flame!", "magenta");
            DisplayMessage("");
            DisplayMessage($"Floor {selectedFloor.Key} has been reset!", "bright_green");
            DisplayMessage("Monsters will await you when you next descend.", "cyan");
            DisplayMessage("'The dungeon stirs once more,' the gnome says with a knowing smile.", "gray");

            // Track telemetry
            TelemetrySystem.Instance.TrackShopTransaction("magic_shop", "buy", "Dungeon Reset Scroll", scrollPrice, player.Level, player.Gold);
        }
        else
        {
            DisplayMessage("Transaction cancelled.", "gray");
        }
    }

    /// <summary>
    /// Calculate the price of a dungeon reset scroll based on player level
    /// Higher level players pay more since they likely have more gold
    /// </summary>
    private long CalculateResetScrollPrice(int playerLevel)
    {
        // Base price 1000 gold + 200 per level
        // Level 1: 1,200 gold
        // Level 10: 3,000 gold
        // Level 50: 11,000 gold
        // Level 100: 21,000 gold
        return 1000 + (playerLevel * 200);
    }

    /// <summary>
    /// Get current magic shop owner name
    /// </summary>
    public static string GetOwnerName()
    {
        return _ownerName;
    }
    
    /// <summary>
    /// Set magic shop owner name (from configuration)
    /// </summary>
    public static void SetOwnerName(string name)
    {
        if (!string.IsNullOrEmpty(name))
        {
            _ownerName = name;
        }
    }
    
    // Note: SetIdentificationCost is no longer used - identification cost now scales dynamically with player level
    // See GetIdentificationCost() method
    
    /// <summary>
    /// Get available magic items for external systems
    /// </summary>
    public static List<Item> GetMagicInventory()
    {
        return new List<Item>(_magicInventory);
    }
    
    private void DisplayMessage(string message, string color = "white", bool newLine = true)
    {
        if (newLine)
            terminal.WriteLine(message, color);
        else
            terminal.Write(message, color);
    }

    private void WriteMenuKey(string key, string keyColor, string label)
    {
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor(keyColor);
        terminal.Write(key);
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(label);
    }

    private void WriteMenuRow(string key1, string color1, string label1, string key2, string color2, string label2)
    {
        // Fixed-width two-column layout: left column 38 chars, right column starts at position 39
        terminal.Write("  ");
        WriteMenuKey(key1, color1, label1);
        // Calculate visible chars used: 2 (indent) + 3 ([X]) + label1.Length
        int leftUsed = 2 + 3 + label1.Length;
        int padding = Math.Max(1, 40 - leftUsed);
        terminal.Write(new string(' ', padding));
        WriteMenuKey(key2, color2, label2);
        terminal.WriteLine("");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // EQUIPMENT ENCHANTING - Enchant equipped weapons and armor
    // ═══════════════════════════════════════════════════════════════════════════

    private static readonly (string name, int bonus, long baseCost, int levelScale, int minLevel, string description)[] EnchantTiers = new[]
    {
        ("Minor",           2,  3000L,  100, 1,  "+2 to one stat"),
        ("Standard",        4,  8000L,  200, 10, "+4 to one stat"),
        ("Greater",         6,  20000L, 400, 20, "+6 to one stat"),
        ("Superior",        8,  50000L, 800, 40, "+8 to one stat"),
        ("Divine Blessing", 3,  35000L, 600, 30, "+3 to all stats"),
        ("Ocean's Touch",   0,  20000L, 300, 20, "+30 mana, +4 wisdom"),
        ("Ward",            0,  12000L, 200, 15, "+20 magic resist, +2 defence"),
        ("Predator",        0,  25000L, 500, 30, "+5% crit, +10% crit damage"),
        ("Lifedrinker",     0,  30000L, 500, 35, "+3% lifesteal"),
    };

    private static readonly string[] StatNames = { "Weapon Power", "Strength", "Dexterity", "Defence", "Wisdom", "Armor Class" };

    private async Task EnchantEquipment(Character player)
    {
        terminal.ClearScreen();
        terminal.SetColor("magenta");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                          ENCHANTMENT FORGE                                  ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.Write($"  {_ownerName} examines your equipment with glowing eyes.");
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write("  'I can strengthen what you carry, if you have the gold.'");
        terminal.WriteLine("");
        terminal.WriteLine("");

        // Show equipped items
        var enchantableSlots = new[] {
            EquipmentSlot.MainHand, EquipmentSlot.OffHand, EquipmentSlot.Head, EquipmentSlot.Body,
            EquipmentSlot.Arms, EquipmentSlot.Hands, EquipmentSlot.Legs, EquipmentSlot.Feet,
            EquipmentSlot.Waist, EquipmentSlot.Face, EquipmentSlot.Cloak,
            EquipmentSlot.Neck, EquipmentSlot.LFinger, EquipmentSlot.RFinger
        };

        // Column header
        terminal.SetColor("darkgray");
        terminal.WriteLine("   #  Slot      Name                              Stats");
        terminal.WriteLine("  ─── ──────── ────────────────────────────────── ──────────────────────────");

        var equippedItems = new List<(EquipmentSlot slot, Equipment equip)>();
        int idx = 1;
        foreach (var slot in enchantableSlots)
        {
            var equip = player.GetEquipment(slot);
            if (equip != null)
            {
                equippedItems.Add((slot, equip));

                // Number
                terminal.SetColor("gray");
                terminal.Write($"  [{idx,1}] ");

                // Slot name (8 chars)
                string slotName = slot switch
                {
                    EquipmentSlot.MainHand => "Weapon",
                    EquipmentSlot.OffHand => "OffHand",
                    EquipmentSlot.LFinger => "L.Ring",
                    EquipmentSlot.RFinger => "R.Ring",
                    _ => slot.ToString()
                };
                terminal.SetColor("darkgray");
                terminal.Write($"{slotName,-9}");

                // Item name with enchant/cursed tags
                string enchTag = equip.GetEnchantmentCount() > 0 ? $" [E:{equip.GetEnchantmentCount()}/3]" : "";
                string cursedTag = equip.IsCursed ? " [CURSED]" : "";
                string displayName = equip.Name + enchTag + cursedTag;
                if (displayName.Length > 34) displayName = displayName.Substring(0, 31) + "...";

                if (equip.IsCursed)
                    terminal.SetColor("red");
                else if (equip.GetEnchantmentCount() >= 3)
                    terminal.SetColor("darkgray");
                else
                    terminal.SetColor(equip.GetRarityColor());
                terminal.Write($"{displayName,-35}");

                // Stats inline
                var stats = new List<string>();
                if (equip.WeaponPower > 0) stats.Add($"Pow:{equip.WeaponPower}");
                if (equip.ArmorClass > 0) stats.Add($"AC:{equip.ArmorClass}");
                if (equip.StrengthBonus != 0) stats.Add($"Str{(equip.StrengthBonus > 0 ? "+" : "")}{equip.StrengthBonus}");
                if (equip.DexterityBonus != 0) stats.Add($"Dex{(equip.DexterityBonus > 0 ? "+" : "")}{equip.DexterityBonus}");
                if (equip.DefenceBonus != 0) stats.Add($"Def{(equip.DefenceBonus > 0 ? "+" : "")}{equip.DefenceBonus}");
                if (equip.WisdomBonus != 0) stats.Add($"Wis{(equip.WisdomBonus > 0 ? "+" : "")}{equip.WisdomBonus}");
                if (equip.MaxManaBonus != 0) stats.Add($"Mana{(equip.MaxManaBonus > 0 ? "+" : "")}{equip.MaxManaBonus}");
                terminal.SetColor("green");
                terminal.Write(string.Join(" ", stats));

                terminal.WriteLine("");
                idx++;
            }
        }

        if (equippedItems.Count == 0)
        {
            terminal.WriteLine("");
            DisplayMessage("  You have no equipment to enchant.", "gray");
            DisplayMessage("  'Come back when you're properly armed,' the gnome says.", "cyan");
            await terminal.WaitForKey();
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine("  [0] Cancel");
        terminal.WriteLine("");
        var slotInput = await terminal.GetInput("  Select item to enchant: ");
        if (!int.TryParse(slotInput, out int slotChoice) || slotChoice < 1 || slotChoice > equippedItems.Count)
            return;

        var (selectedSlot, selectedEquip) = equippedItems[slotChoice - 1];

        if (selectedEquip.IsCursed)
        {
            DisplayMessage("");
            DisplayMessage("'I cannot enchant a cursed item. The dark magic would consume my work.'", "red");
            DisplayMessage("'Remove the curse first, then return.'", "cyan");
            await terminal.WaitForKey();
            return;
        }

        if (selectedEquip.GetEnchantmentCount() >= 3)
        {
            DisplayMessage("");
            DisplayMessage("'This item already holds three enchantments. Any more would shatter it.'", "red");
            DisplayMessage("'Remove an enchantment first if you want to add a different one.'", "cyan");
            await terminal.WaitForKey();
            return;
        }

        // Show enchantment options
        terminal.ClearScreen();
        terminal.SetColor("magenta");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.SetColor("magenta");
        terminal.Write("║  Enchanting: ");
        terminal.SetColor(selectedEquip.GetRarityColor());
        terminal.Write(selectedEquip.Name);
        int padLen = 63 - selectedEquip.Name.Length;
        if (padLen < 0) padLen = 0;
        terminal.SetColor("magenta");
        terminal.WriteLine(new string(' ', padLen) + "║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine($"  You have {player.Gold:N0} gold");
        terminal.WriteLine("");

        // Section: Stat enchants (tiers 1-4)
        terminal.SetColor("white");
        terminal.WriteLine("  ─── Stat Enchantments (choose a stat) ───");
        terminal.SetColor("darkgray");
        terminal.WriteLine("   #  Tier             Effect                  Cost");
        terminal.SetColor("darkgray");
        terminal.WriteLine("  ─── ──────────────── ─────────────────────── ──────────────");

        for (int i = 0; i < 4; i++)
        {
            var tier = EnchantTiers[i];
            long cost = ApplyAllPriceModifiers(tier.baseCost + (player.Level * tier.levelScale), player);
            bool canAfford = player.Gold >= cost;
            bool meetsLevel = player.Level >= tier.minLevel;

            terminal.SetColor("gray");
            terminal.Write($"  [{i + 1}] ");

            if (!meetsLevel)
            {
                terminal.SetColor("darkgray");
                terminal.Write($"{tier.name,-17}{tier.description,-24}{cost,10:N0}g");
                terminal.SetColor("red");
                terminal.Write($"  Lv.{tier.minLevel}+");
            }
            else
            {
                terminal.SetColor(canAfford ? "white" : "red");
                terminal.Write($"{tier.name,-17}");
                terminal.SetColor(canAfford ? "cyan" : "red");
                terminal.Write($"{tier.description,-24}");
                terminal.SetColor(canAfford ? "yellow" : "red");
                terminal.Write($"{cost,10:N0}g");
            }
            terminal.WriteLine("");
        }

        // Section: Special enchants (tiers 5-9)
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine("  ─── Special Enchantments ───");
        terminal.SetColor("darkgray");
        terminal.WriteLine("   #  Tier             Effect                  Cost");
        terminal.SetColor("darkgray");
        terminal.WriteLine("  ─── ──────────────── ─────────────────────── ──────────────");

        for (int i = 4; i < EnchantTiers.Length; i++)
        {
            var tier = EnchantTiers[i];
            long cost = ApplyAllPriceModifiers(tier.baseCost + (player.Level * tier.levelScale), player);
            bool canAfford = player.Gold >= cost;
            bool meetsLevel = player.Level >= tier.minLevel;
            bool meetsAwakening = i != 5 || (OceanPhilosophySystem.Instance?.AwakeningLevel ?? 0) >= 2;

            terminal.SetColor("gray");
            terminal.Write($"  [{i + 1}] ");

            if (!meetsLevel)
            {
                terminal.SetColor("darkgray");
                terminal.Write($"{tier.name,-17}{tier.description,-24}{cost,10:N0}g");
                terminal.SetColor("red");
                terminal.Write($"  Lv.{tier.minLevel}+");
            }
            else if (!meetsAwakening)
            {
                terminal.SetColor("darkgray");
                terminal.Write($"{tier.name,-17}{tier.description,-24}{cost,10:N0}g");
                terminal.SetColor("magenta");
                terminal.Write("  Awakening 2+");
            }
            else
            {
                terminal.SetColor(canAfford ? "white" : "red");
                terminal.Write($"{tier.name,-17}");
                terminal.SetColor(canAfford ? "cyan" : "red");
                terminal.Write($"{tier.description,-24}");
                terminal.SetColor(canAfford ? "yellow" : "red");
                terminal.Write($"{cost,10:N0}g");
            }
            terminal.WriteLine("");
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine("  [0] Cancel");
        terminal.WriteLine("");
        var tierInput = await terminal.GetInput("  Select enchantment: ");
        if (!int.TryParse(tierInput, out int tierChoice) || tierChoice < 1 || tierChoice > EnchantTiers.Length)
            return;

        var selectedTier = EnchantTiers[tierChoice - 1];
        long enchantCost = ApplyAllPriceModifiers(selectedTier.baseCost + (player.Level * selectedTier.levelScale), player);

        if (player.Level < selectedTier.minLevel)
        {
            DisplayMessage($"'You need to be at least level {selectedTier.minLevel} for this enchantment.'", "red");
            await terminal.WaitForKey();
            return;
        }

        if (tierChoice == 6 && (OceanPhilosophySystem.Instance?.AwakeningLevel ?? 0) < 2)
        {
            DisplayMessage("'The Ocean's power requires a deeper connection than you possess.'", "cyan");
            await terminal.WaitForKey();
            return;
        }

        if (player.Gold < enchantCost)
        {
            DisplayMessage("");
            DisplayMessage("'The magical arts require material compensation,' the gnome says.", "cyan");
            DisplayMessage($"You need {enchantCost:N0} gold.", "red");
            await terminal.WaitForKey();
            return;
        }

        // For stat-specific enchants (tiers 1-4), let player choose stat
        int statChoice = -1;
        if (tierChoice >= 1 && tierChoice <= 4)
        {
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine("  Choose stat to enhance:");
            terminal.WriteLine("");
            for (int i = 0; i < StatNames.Length; i++)
            {
                terminal.SetColor("gray");
                terminal.Write($"  [{i + 1}] ");
                terminal.SetColor("cyan");
                terminal.WriteLine($"{StatNames[i]}");
            }
            terminal.WriteLine("");
            var statInput = await terminal.GetInput("  Stat choice: ");
            if (!int.TryParse(statInput, out statChoice) || statChoice < 1 || statChoice > StatNames.Length)
                return;
        }

        // Confirm
        string enchantDesc = tierChoice <= 4 ? $"+{selectedTier.bonus} {StatNames[statChoice - 1]}" : selectedTier.description;
        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.Write($"  Enchant ");
        terminal.SetColor(selectedEquip.GetRarityColor());
        terminal.Write(selectedEquip.Name);
        terminal.SetColor("yellow");
        terminal.Write($" with ");
        terminal.SetColor("bright_magenta");
        terminal.Write($"{selectedTier.name}");
        terminal.SetColor("yellow");
        terminal.Write($" ({enchantDesc})");
        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine($"  Cost: {enchantCost:N0} gold");
        terminal.WriteLine("");
        var confirm = await terminal.GetInput("  Proceed? (Y/N): ");
        if (confirm.ToUpper() != "Y")
            return;

        // Execute enchantment
        player.Gold -= enchantCost;

        // Clone the equipment
        var enchanted = selectedEquip.Clone();
        enchanted.IncrementEnchantmentCount();

        // Apply the enchantment
        string suffix;
        switch (tierChoice)
        {
            case 1: case 2: case 3: case 4: // Stat enchants
                ApplyEquipmentStatBonus(enchanted, statChoice, selectedTier.bonus);
                suffix = $" +{selectedTier.bonus} {StatNames[statChoice - 1].Substring(0, 3)}";
                break;
            case 5: // Divine Blessing
                enchanted.StrengthBonus += 3; enchanted.DexterityBonus += 3;
                enchanted.DefenceBonus += 3; enchanted.WisdomBonus += 3;
                enchanted.WeaponPower += 3; enchanted.ArmorClass += 3;
                suffix = " (Blessed)";
                break;
            case 6: // Ocean's Touch
                enchanted.MaxManaBonus += 30; enchanted.WisdomBonus += 4;
                suffix = " (Ocean-Touched)";
                break;
            case 7: // Ward
                enchanted.MagicResistance += 20; enchanted.DefenceBonus += 2;
                suffix = " (Warded)";
                break;
            case 8: // Predator
                enchanted.CriticalChanceBonus += 5; enchanted.CriticalDamageBonus += 10;
                suffix = " (Predator)";
                break;
            case 9: // Lifedrinker
                enchanted.LifeSteal += 3;
                suffix = " (Lifedrinker)";
                break;
            default:
                suffix = "";
                break;
        }

        // Update name
        if (enchanted.Name.Length + suffix.Length < 40)
            enchanted.Name += suffix;

        // Increase value
        enchanted.Value = (long)(enchanted.Value * 1.5);

        // Register in database and equip
        EquipmentDatabase.RegisterDynamic(enchanted);
        player.UnequipSlot(selectedSlot);
        player.EquipItem(enchanted, selectedSlot, out _);
        player.RecalculateStats();

        // Dramatic enchantment scene
        DisplayMessage("");
        DisplayMessage($"{_ownerName} places the {selectedEquip.Name} on an anvil carved with ancient runes...", "gray");
        await Task.Delay(500);
        DisplayMessage("Sparks fly as magical energy courses through the item!", "magenta");
        await Task.Delay(500);
        DisplayMessage("");
        DisplayMessage($"Your {selectedEquip.Name} is now {enchanted.Name}!", "bright_green");

        // Track stats
        player.Statistics?.RecordEnchantment(enchantCost);
        player.Statistics?.RecordGoldSpent(enchantCost);
        CityControlSystem.Instance.ProcessSaleTax(enchantCost);
        TelemetrySystem.Instance.TrackShopTransaction("magic_shop", "enchant", enchanted.Name, enchantCost, player.Level, player.Gold);

        // Achievements
        AchievementSystem.TryUnlock(player, "first_enchant");
        if ((player.Statistics?.TotalEnchantmentsApplied ?? 0) >= 10)
            AchievementSystem.TryUnlock(player, "master_enchanter");

        await terminal.WaitForKey();
    }

    private void ApplyEquipmentStatBonus(Equipment equip, int statChoice, int bonus)
    {
        switch (statChoice)
        {
            case 1: equip.WeaponPower += bonus; break;
            case 2: equip.StrengthBonus += bonus; break;
            case 3: equip.DexterityBonus += bonus; break;
            case 4: equip.DefenceBonus += bonus; break;
            case 5: equip.WisdomBonus += bonus; break;
            case 6: equip.ArmorClass += bonus; break;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ENCHANTMENT REMOVAL - Remove enchantments from dynamic equipment
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task RemoveEnchantment(Character player)
    {
        terminal.ClearScreen();
        DisplayMessage("═══ Enchantment Removal ═══", "magenta");
        DisplayMessage("");
        DisplayMessage($"'{_ownerName} nods gravely. 'Removing an enchantment is delicate work.'", "cyan");
        DisplayMessage("");

        // Find enchanted equipment
        var enchantableSlots = new[] {
            EquipmentSlot.MainHand, EquipmentSlot.OffHand, EquipmentSlot.Head, EquipmentSlot.Body,
            EquipmentSlot.Arms, EquipmentSlot.Hands, EquipmentSlot.Legs, EquipmentSlot.Feet,
            EquipmentSlot.Waist, EquipmentSlot.Face, EquipmentSlot.Cloak,
            EquipmentSlot.Neck, EquipmentSlot.LFinger, EquipmentSlot.RFinger
        };

        var enchantedItems = new List<(EquipmentSlot slot, Equipment equip)>();
        int idx = 1;
        foreach (var slot in enchantableSlots)
        {
            var equip = player.GetEquipment(slot);
            if (equip != null && equip.GetEnchantmentCount() > 0)
            {
                enchantedItems.Add((slot, equip));
                DisplayMessage($"  ({idx}) {slot}: {equip.Name} [E:{equip.GetEnchantmentCount()}]", equip.GetRarityColor());
                idx++;
            }
        }

        if (enchantedItems.Count == 0)
        {
            DisplayMessage("You have no enchanted equipment.", "gray");
            await terminal.WaitForKey();
            return;
        }

        DisplayMessage("");
        DisplayMessage("WARNING: This will remove ALL enchantments, returning the item to base form.", "red");
        long removalCost = ApplyAllPriceModifiers(5000 + (player.Level * 200), player);
        DisplayMessage($"Cost: {removalCost:N0} gold", "yellow");
        DisplayMessage("");
        var input = await terminal.GetInput("Select item (0 to cancel): ");
        if (!int.TryParse(input, out int choice) || choice < 1 || choice > enchantedItems.Count)
        {
            await terminal.WaitForKey();
            return;
        }

        if (player.Gold < removalCost)
        {
            DisplayMessage("'You lack the gold,' the gnome says.", "red");
            await terminal.WaitForKey();
            return;
        }

        var (rmSlot, rmEquip) = enchantedItems[choice - 1];
        var confirmInput = await terminal.GetInput($"Remove enchantments from {rmEquip.Name}? (Y/N): ");
        if (confirmInput.ToUpper() != "Y")
        {
            await terminal.WaitForKey();
            return;
        }

        // Find the base equipment by looking up by original ID pattern
        // For dynamic equipment, we can't easily get back to the original - so just strip enchantments
        player.Gold -= removalCost;

        // Create a clean clone and reset enchantment tracking
        var stripped = rmEquip.Clone();
        stripped.Description = System.Text.RegularExpressions.Regex.Replace(stripped.Description ?? "", @"\s*\[E:\d+\]", "");

        // Strip name suffixes
        string[] suffixes = { " (Blessed)", " (Ocean-Touched)", " (Warded)", " (Predator)", " (Lifedrinker)" };
        foreach (var sfx in suffixes)
            stripped.Name = stripped.Name.Replace(sfx, "");
        // Strip stat suffixes like " +2 Str", " +4 Dex", etc.
        stripped.Name = System.Text.RegularExpressions.Regex.Replace(stripped.Name, @"\s\+\d+\s\w{3}", "");

        EquipmentDatabase.RegisterDynamic(stripped);
        player.UnequipSlot(rmSlot);
        player.EquipItem(stripped, rmSlot, out _);
        player.RecalculateStats();

        DisplayMessage("");
        DisplayMessage("The enchantments dissolve into wisps of fading light...", "magenta");
        DisplayMessage($"Your {stripped.Name} has been restored to its base form.", "yellow");
        player.Statistics?.RecordGoldSpent(removalCost);

        await terminal.WaitForKey();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MODERN ACCESSORY SHOP - Buy rings/necklaces from Equipment system
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task BuyAccessory(Character player)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("═══ Ravanella's Accessories ═══");
        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.Write($"  Gold: ");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"{player.Gold:N0}");
        terminal.WriteLine("");

        terminal.SetColor("bright_cyan");
        terminal.Write("  1");
        terminal.SetColor("darkgray");
        terminal.Write(") ");
        terminal.SetColor("white");
        terminal.Write("Rings        ");
        terminal.SetColor("gray");
        terminal.WriteLine("- Magical rings for your fingers");

        terminal.SetColor("bright_cyan");
        terminal.Write("  2");
        terminal.SetColor("darkgray");
        terminal.Write(") ");
        terminal.SetColor("white");
        terminal.Write("Necklaces    ");
        terminal.SetColor("gray");
        terminal.WriteLine("- Enchanted amulets and pendants");

        terminal.SetColor("bright_cyan");
        terminal.Write("  3");
        terminal.SetColor("darkgray");
        terminal.Write(") ");
        terminal.SetColor("white");
        terminal.Write("Belts        ");
        terminal.SetColor("gray");
        terminal.WriteLine("- Mystical sashes and girdles");

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.Write("  0");
        terminal.SetColor("darkgray");
        terminal.WriteLine(") Back");
        terminal.WriteLine("");

        var catInput = await terminal.GetInput("Choose category: ");
        if (!int.TryParse(catInput, out int cat) || cat < 1 || cat > 3)
            return;

        EquipmentSlot targetSlot = cat switch
        {
            1 => EquipmentSlot.LFinger,
            2 => EquipmentSlot.Neck,
            _ => EquipmentSlot.Waist
        };
        string categoryName = cat switch { 1 => "Rings", 2 => "Necklaces", _ => "Belts" };

        // Get base equipment only (exclude dynamic/enchanted items with ID >= 100000)
        var items = EquipmentDatabase.GetBySlot(targetSlot)
            .Where(e => e.MinLevel <= player.Level && !e.IsUnique && e.Id < 100000)
            .OrderBy(e => e.Value)
            .ToList();

        // For rings, also include RFinger items
        if (cat == 1)
        {
            items.AddRange(EquipmentDatabase.GetBySlot(EquipmentSlot.RFinger)
                .Where(e => e.MinLevel <= player.Level && !e.IsUnique && e.Id < 100000)
                .OrderBy(e => e.Value));
            items = items.DistinctBy(e => e.Id).OrderBy(e => e.Value).ToList();
        }

        if (items.Count == 0)
        {
            DisplayMessage("No items available for your level.", "gray");
            await terminal.WaitForKey();
            return;
        }

        // Get currently equipped item for comparison
        Equipment? currentItem = cat switch
        {
            1 => player.GetEquipment(EquipmentSlot.LFinger) ?? player.GetEquipment(EquipmentSlot.RFinger),
            2 => player.GetEquipment(EquipmentSlot.Neck),
            _ => player.GetEquipment(EquipmentSlot.Waist)
        };

        int perPage = 15;
        int totalPages = (items.Count + perPage - 1) / perPage;
        int page = 0;

        while (true)
        {
            terminal.ClearScreen();

            // Header
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"═══ {categoryName} ═══");
            terminal.WriteLine("");

            // Gold display
            terminal.SetColor("yellow");
            terminal.Write("  Gold: ");
            terminal.SetColor("bright_yellow");
            terminal.Write($"{player.Gold:N0}");
            terminal.SetColor("gray");
            terminal.WriteLine($"     {items.Count} items available - Page {page + 1}/{totalPages}");
            terminal.WriteLine("");

            // Show currently equipped item
            if (currentItem != null)
            {
                terminal.SetColor("cyan");
                terminal.Write("  Equipped: ");
                terminal.SetColor("bright_white");
                terminal.Write($"{currentItem.Name}");
                terminal.SetColor("gray");
                terminal.Write("  ");
                var eqStats = GetAccessoryBonusDescription(currentItem);
                if (!string.IsNullOrEmpty(eqStats))
                {
                    terminal.SetColor("green");
                    terminal.Write(eqStats);
                }
                terminal.WriteLine("");
                terminal.WriteLine("");
            }

            // Column header
            terminal.SetColor("bright_blue");
            terminal.WriteLine("  #   Name                          Price       Bonuses");
            terminal.SetColor("darkgray");
            terminal.WriteLine("─────────────────────────────────────────────────────────────────────────");

            // Items on this page
            int start = page * perPage;
            int end = Math.Min(start + perPage, items.Count);

            for (int i = start; i < end; i++)
            {
                var item = items[i];
                long price = ApplyAllPriceModifiers(item.Value, player);
                bool canAfford = player.Gold >= price;
                int displayNum = i - start + 1;

                // Number
                terminal.SetColor(canAfford ? "bright_cyan" : "darkgray");
                terminal.Write($" {displayNum,2}. ");

                // Name (colored by rarity if affordable, dim if not)
                terminal.SetColor(canAfford ? item.GetRarityColor() : "darkgray");
                terminal.Write($"{item.Name,-28}");

                // Price
                terminal.SetColor(canAfford ? "yellow" : "darkgray");
                terminal.Write($"{price,10:N0}  ");

                // Bonus stats (compact, inline)
                var bonuses = GetAccessoryBonusDescription(item);
                if (!string.IsNullOrEmpty(bonuses))
                {
                    terminal.SetColor(canAfford ? "green" : "darkgray");
                    terminal.Write(bonuses);
                }

                // Upgrade indicator
                if (canAfford && currentItem != null)
                {
                    int itemScore = GetAccessoryScore(item);
                    int currentScore = GetAccessoryScore(currentItem);
                    if (itemScore > currentScore)
                    {
                        terminal.SetColor("bright_green");
                        terminal.Write(" [+]");
                    }
                    else if (itemScore < currentScore)
                    {
                        terminal.SetColor("red");
                        terminal.Write(" [-]");
                    }
                }

                terminal.WriteLine("");
            }

            terminal.WriteLine("");

            // Navigation bar
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("#");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.Write("Buy   ");

            if (page > 0)
            {
                terminal.SetColor("darkgray");
                terminal.Write("[");
                terminal.SetColor("bright_cyan");
                terminal.Write("P");
                terminal.SetColor("darkgray");
                terminal.Write("] Prev   ");
            }

            if (page < totalPages - 1)
            {
                terminal.SetColor("darkgray");
                terminal.Write("[");
                terminal.SetColor("bright_cyan");
                terminal.Write("N");
                terminal.SetColor("darkgray");
                terminal.Write("] Next   ");
            }

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_red");
            terminal.Write("Q");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("red");
            terminal.WriteLine("Back");
            terminal.WriteLine("");

            var nav = await terminal.GetInput("> ");
            var navUpper = nav.ToUpper();

            if (navUpper == "N" && page < totalPages - 1) { page++; continue; }
            if (navUpper == "P" && page > 0) { page--; continue; }
            if (navUpper == "Q" || navUpper == "0") break;

            // Page-relative numbering: user types 1-15 for items on current page
            if (int.TryParse(nav, out int displayIdx) && displayIdx >= 1 && displayIdx <= (end - start))
            {
                int actualIdx = start + displayIdx - 1;
                var item = items[actualIdx];
                long price = ApplyAllPriceModifiers(item.Value, player);

                if (player.Gold < price)
                {
                    DisplayMessage("You can't afford that!", "red");
                    await terminal.WaitForKey();
                    continue;
                }

                // Check alignment restrictions
                if (item.RequiresGood && player.Darkness > player.Chivalry)
                {
                    DisplayMessage("This item requires a good alignment.", "red");
                    await terminal.WaitForKey();
                    continue;
                }
                if (item.RequiresEvil && player.Chivalry > player.Darkness)
                {
                    DisplayMessage("This item requires an evil alignment.", "red");
                    await terminal.WaitForKey();
                    continue;
                }

                // Show item detail before purchase
                terminal.WriteLine("");
                terminal.SetColor("bright_white");
                terminal.WriteLine($"  {item.Name}");
                terminal.SetColor("gray");
                terminal.Write($"  Rarity: ");
                terminal.SetColor(item.GetRarityColor());
                terminal.Write($"{item.Rarity}");
                if (item.MinLevel > 0)
                {
                    terminal.SetColor("gray");
                    terminal.Write($"   Level: {item.MinLevel}+");
                }
                terminal.WriteLine("");
                var detailStats = GetAccessoryDetailedStats(item);
                if (!string.IsNullOrEmpty(detailStats))
                {
                    terminal.SetColor("bright_green");
                    terminal.WriteLine($"  {detailStats}");
                }
                terminal.WriteLine("");

                var buyConfirm = await terminal.GetInput($"  Buy for {price:N0} gold? (Y/N): ");
                if (buyConfirm.ToUpper() == "Y")
                {
                    player.Gold -= price;
                    if (player.EquipItem(item, out string msg))
                    {
                        player.RecalculateStats();
                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"  You now wear the {item.Name}!");

                        // Update current item reference for comparison display
                        currentItem = cat switch
                        {
                            1 => player.GetEquipment(EquipmentSlot.LFinger) ?? player.GetEquipment(EquipmentSlot.RFinger),
                            2 => player.GetEquipment(EquipmentSlot.Neck),
                            _ => player.GetEquipment(EquipmentSlot.Waist)
                        };
                    }
                    else
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine($"  Purchased {item.Name}. {msg}");
                    }

                    player.Statistics?.RecordPurchase(price);
                    player.Statistics?.RecordAccessoryPurchase(price);
                    CityControlSystem.Instance.ProcessSaleTax(price);
                    TelemetrySystem.Instance.TrackShopTransaction("magic_shop", "buy_accessory", item.Name, price, player.Level, player.Gold);

                    // Check accessory collection achievement
                    if (player.GetEquipment(EquipmentSlot.LFinger) != null &&
                        player.GetEquipment(EquipmentSlot.RFinger) != null &&
                        player.GetEquipment(EquipmentSlot.Neck) != null)
                        AchievementSystem.TryUnlock(player, "accessory_collector");

                    await terminal.WaitForKey();
                }
            }
        }
    }

    /// <summary>
    /// Get compact bonus description for accessory display (max 3 bonuses to fit in column)
    /// </summary>
    private static string GetAccessoryBonusDescription(Equipment item)
    {
        var bonuses = new List<string>();
        if (item.StrengthBonus != 0) bonuses.Add($"Str{(item.StrengthBonus > 0 ? "+" : "")}{item.StrengthBonus}");
        if (item.DexterityBonus != 0) bonuses.Add($"Dex{(item.DexterityBonus > 0 ? "+" : "")}{item.DexterityBonus}");
        if (item.ConstitutionBonus != 0) bonuses.Add($"Con{(item.ConstitutionBonus > 0 ? "+" : "")}{item.ConstitutionBonus}");
        if (item.IntelligenceBonus != 0) bonuses.Add($"Int{(item.IntelligenceBonus > 0 ? "+" : "")}{item.IntelligenceBonus}");
        if (item.WisdomBonus != 0) bonuses.Add($"Wis{(item.WisdomBonus > 0 ? "+" : "")}{item.WisdomBonus}");
        if (item.DefenceBonus != 0) bonuses.Add($"Def{(item.DefenceBonus > 0 ? "+" : "")}{item.DefenceBonus}");
        if (item.MaxHPBonus != 0) bonuses.Add($"HP+{item.MaxHPBonus}");
        if (item.MaxManaBonus != 0) bonuses.Add($"MP+{item.MaxManaBonus}");
        if (item.MagicResistance != 0) bonuses.Add($"MR+{item.MagicResistance}");
        if (item.CriticalChanceBonus != 0) bonuses.Add($"Crit+{item.CriticalChanceBonus}%");
        if (item.CriticalDamageBonus != 0) bonuses.Add($"CritD+{item.CriticalDamageBonus}%");
        if (item.LifeSteal != 0) bonuses.Add($"LS+{item.LifeSteal}%");
        return string.Join(" ", bonuses.Take(4));
    }

    /// <summary>
    /// Get detailed stats for purchase confirmation display (all bonuses shown)
    /// </summary>
    private static string GetAccessoryDetailedStats(Equipment item)
    {
        var bonuses = new List<string>();
        if (item.StrengthBonus != 0) bonuses.Add($"Str{(item.StrengthBonus > 0 ? "+" : "")}{item.StrengthBonus}");
        if (item.DexterityBonus != 0) bonuses.Add($"Dex{(item.DexterityBonus > 0 ? "+" : "")}{item.DexterityBonus}");
        if (item.ConstitutionBonus != 0) bonuses.Add($"Con{(item.ConstitutionBonus > 0 ? "+" : "")}{item.ConstitutionBonus}");
        if (item.IntelligenceBonus != 0) bonuses.Add($"Int{(item.IntelligenceBonus > 0 ? "+" : "")}{item.IntelligenceBonus}");
        if (item.WisdomBonus != 0) bonuses.Add($"Wis{(item.WisdomBonus > 0 ? "+" : "")}{item.WisdomBonus}");
        if (item.DefenceBonus != 0) bonuses.Add($"Def{(item.DefenceBonus > 0 ? "+" : "")}{item.DefenceBonus}");
        if (item.AgilityBonus != 0) bonuses.Add($"Agi{(item.AgilityBonus > 0 ? "+" : "")}{item.AgilityBonus}");
        if (item.CharismaBonus != 0) bonuses.Add($"Cha{(item.CharismaBonus > 0 ? "+" : "")}{item.CharismaBonus}");
        if (item.MaxHPBonus != 0) bonuses.Add($"HP+{item.MaxHPBonus}");
        if (item.MaxManaBonus != 0) bonuses.Add($"MP+{item.MaxManaBonus}");
        if (item.MagicResistance != 0) bonuses.Add($"MR+{item.MagicResistance}");
        if (item.CriticalChanceBonus != 0) bonuses.Add($"Crit+{item.CriticalChanceBonus}%");
        if (item.CriticalDamageBonus != 0) bonuses.Add($"CritDmg+{item.CriticalDamageBonus}%");
        if (item.LifeSteal != 0) bonuses.Add($"Lifesteal+{item.LifeSteal}%");
        return string.Join("  ", bonuses);
    }

    /// <summary>
    /// Calculate a simple score for accessory comparison (upgrade/downgrade indicator)
    /// </summary>
    private static int GetAccessoryScore(Equipment item)
    {
        return item.StrengthBonus * 3 + item.DexterityBonus * 3 + item.ConstitutionBonus * 3
             + item.IntelligenceBonus * 3 + item.WisdomBonus * 3 + item.DefenceBonus * 3
             + item.AgilityBonus * 3 + item.CharismaBonus * 3
             + item.MaxHPBonus / 5 + item.MaxManaBonus / 5
             + item.MagicResistance + item.CriticalChanceBonus * 2
             + item.CriticalDamageBonus + item.LifeSteal * 3;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // LOVE SPELLS - Relationship magic
    // ═══════════════════════════════════════════════════════════════════════════

    private static readonly (string name, int steps, long baseCost, int levelScale, int manaCost, int minLevel, bool bypassCap, string effect)[] LoveSpells = new[]
    {
        ("Charm of Fondness",         1, 300L,   30,  10, 3,  false, "Warm their feelings slightly"),
        ("Enchantment of Attraction", 2, 1000L,  60,  20, 8,  false, "Noticeably improve their regard"),
        ("Heart's Desire",            3, 3000L,  120, 40, 18, false, "Deeply shift their affections"),
        ("Binding of Souls",          2, 8000L,  300, 80, 35, true,  "Powerful bond (ignores daily limit)"),
    };

    private static string GetRelationshipDisplayName(int rel)
    {
        return rel switch
        {
            <= 10 => "Married",
            <= 20 => "Love",
            <= 30 => "Passion",
            <= 40 => "Friendship",
            <= 50 => "Trust",
            <= 60 => "Respect",
            <= 70 => "Neutral",
            <= 80 => "Suspicious",
            <= 90 => "Anger",
            <= 100 => "Enemy",
            _ => "Hate"
        };
    }

    private static string GetRelationshipColor(int rel)
    {
        return rel switch
        {
            <= 10 => "bright_yellow",  // Married
            <= 20 => "bright_magenta", // Love
            <= 30 => "magenta",        // Passion
            <= 40 => "bright_green",   // Friendship
            <= 50 => "green",          // Trust
            <= 60 => "cyan",           // Respect
            <= 70 => "white",          // Neutral
            <= 80 => "yellow",         // Suspicious
            <= 90 => "red",            // Anger
            <= 100 => "bright_red",    // Enemy
            _ => "bright_red"          // Hate
        };
    }

    /// <summary>
    /// Paginated NPC picker with search. Shows 15 NPCs per page with [N]ext/[P]rev/[S]earch.
    /// Returns selected NPC or null if cancelled.
    /// </summary>
    private async Task<NPC?> PickNPCTarget(Character player, List<NPC> npcList, string title, bool showRelationship, bool showLevel)
    {
        const int perPage = 15;
        int page = 0;
        string? searchFilter = null;

        while (true)
        {
            var filtered = searchFilter != null
                ? npcList.Where(n => n.Name1.Contains(searchFilter, StringComparison.OrdinalIgnoreCase)).ToList()
                : npcList;

            int totalPages = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)perPage));
            if (page >= totalPages) page = totalPages - 1;
            if (page < 0) page = 0;

            int startIdx = page * perPage;
            int endIdx = Math.Min(startIdx + perPage, filtered.Count);

            terminal.ClearScreen();
            terminal.SetColor("magenta");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            terminal.SetColor("magenta");
            terminal.Write("║  ");
            terminal.SetColor("bright_magenta");
            terminal.Write(title);
            int titlePad = 75 - title.Length;
            if (titlePad < 0) titlePad = 0;
            terminal.SetColor("magenta");
            terminal.WriteLine(new string(' ', titlePad) + "║");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");

            if (searchFilter != null)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"  Search: \"{searchFilter}\" ({filtered.Count} matches)");
            }
            terminal.WriteLine("");

            // Column headers
            terminal.SetColor("darkgray");
            if (showRelationship && showLevel)
                terminal.WriteLine("    #  Name                        Class        Lv  Relationship");
            else if (showRelationship)
                terminal.WriteLine("    #  Name                        Class        Relationship");
            else if (showLevel)
                terminal.WriteLine("    #  Name                        Class        Lv  Status");
            else
                terminal.WriteLine("    #  Name                        Class");
            terminal.SetColor("darkgray");
            terminal.WriteLine("   " + new string('-', 65));

            if (filtered.Count == 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine("  No NPCs found.");
            }
            else
            {
                for (int i = startIdx; i < endIdx; i++)
                {
                    var npc = filtered[i];
                    int displayNum = i - startIdx + 1;

                    terminal.SetColor("gray");
                    terminal.Write($"  [{displayNum,2}] ");

                    bool dimmed = false;
                    if (showRelationship)
                    {
                        int rel = RelationshipSystem.GetRelationshipLevel(player, npc);
                        dimmed = rel <= 20; // at Love or better
                    }

                    terminal.SetColor(dimmed ? "darkgray" : "white");
                    string npcName = npc.Name1.Length > 27 ? npc.Name1.Substring(0, 24) + "..." : npc.Name1;
                    terminal.Write($"{npcName,-28}");

                    terminal.SetColor("darkgray");
                    terminal.Write($"{npc.Class,-13}");

                    if (showLevel)
                    {
                        terminal.SetColor("gray");
                        terminal.Write($"{npc.Level,-4}");
                    }

                    if (showRelationship)
                    {
                        int rel = RelationshipSystem.GetRelationshipLevel(player, npc);
                        string relName = GetRelationshipDisplayName(rel);
                        string relColor = GetRelationshipColor(rel);
                        terminal.SetColor(relColor);
                        terminal.Write(relName);
                        if (rel <= 20)
                        {
                            terminal.SetColor("darkgray");
                            terminal.Write(" (max)");
                        }
                    }
                    else if (showLevel)
                    {
                        terminal.SetColor(npc.IsDead ? "red" : "green");
                        terminal.Write(npc.IsDead ? "Dead" : "Alive");
                    }

                    terminal.WriteLine("");
                }
            }

            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.Write("  [0] Cancel");
            if (totalPages > 1)
            {
                terminal.Write("   [N]ext   [P]rev");
                terminal.SetColor("darkgray");
                terminal.Write($"   Page {page + 1}/{totalPages}");
            }
            terminal.Write("   [S]earch");
            if (searchFilter != null) terminal.Write("   [C]lear search");
            terminal.WriteLine("");
            terminal.WriteLine("");

            var input = await terminal.GetInput("  Target: ");
            if (string.IsNullOrEmpty(input)) continue;

            string upper = input.Trim().ToUpper();
            if (upper == "0") return null;
            if (upper == "N" && totalPages > 1) { page = (page + 1) % totalPages; continue; }
            if (upper == "P" && totalPages > 1) { page = (page - 1 + totalPages) % totalPages; continue; }
            if (upper == "S")
            {
                var search = await terminal.GetInput("  Search name: ");
                if (!string.IsNullOrWhiteSpace(search))
                {
                    searchFilter = search.Trim();
                    page = 0;
                }
                continue;
            }
            if (upper == "C") { searchFilter = null; page = 0; continue; }

            if (int.TryParse(input, out int choice) && choice >= 1 && choice <= (endIdx - startIdx))
            {
                return filtered[startIdx + choice - 1];
            }
        }
    }

    private async Task CastLoveSpell(Character player)
    {
        terminal.ClearScreen();
        terminal.SetColor("magenta");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                           ARCANE ROMANCE                                    ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine($"  {_ownerName} produces a collection of shimmering vials and glowing crystals.");
        terminal.SetColor("cyan");
        terminal.WriteLine("  'Love is the most powerful magic. And the most dangerous.'");

        // Ocean Philosophy warning
        if ((OceanPhilosophySystem.Instance?.AwakeningLevel ?? 0) >= 4)
        {
            terminal.WriteLine("");
            terminal.SetColor("magenta");
            terminal.WriteLine("  'You understand that these bonds are real, even if begun through magic?'");
            terminal.WriteLine("  'The wave cannot force the ocean to love it -- it already does.'");
        }
        terminal.WriteLine("");

        // Evil surcharge
        bool evilSurcharge = player.Darkness > player.Chivalry + 20;

        terminal.SetColor("gray");
        terminal.WriteLine($"  You have {player.Gold:N0} gold, {player.Mana} mana");
        terminal.WriteLine("");

        // Column header
        terminal.SetColor("darkgray");
        terminal.WriteLine("   #  Spell                      Effect                             Cost");
        terminal.WriteLine("  ─── ────────────────────────── ────────────────────────────────── ──────────");

        for (int i = 0; i < LoveSpells.Length; i++)
        {
            var spell = LoveSpells[i];
            long cost = ApplyAllPriceModifiers(spell.baseCost + (player.Level * spell.levelScale), player);
            if (evilSurcharge) cost = (long)(cost * 1.2);
            bool meetsLevel = player.Level >= spell.minLevel;
            bool hasMana = player.Mana >= spell.manaCost;
            bool canAfford = player.Gold >= cost;

            terminal.SetColor("gray");
            terminal.Write($"  [{i + 1}] ");

            if (!meetsLevel)
            {
                terminal.SetColor("darkgray");
                terminal.Write($"{spell.name,-27}{spell.effect,-35}{cost,7:N0}g {spell.manaCost,3}mp");
                terminal.SetColor("red");
                terminal.Write($"  Lv.{spell.minLevel}+");
            }
            else if (!hasMana)
            {
                terminal.SetColor("red");
                terminal.Write($"{spell.name,-27}");
                terminal.SetColor("red");
                terminal.Write($"{spell.effect,-35}{cost,7:N0}g ");
                terminal.SetColor("bright_red");
                terminal.Write($"{spell.manaCost,3}mp");
            }
            else
            {
                terminal.SetColor(canAfford ? "white" : "red");
                terminal.Write($"{spell.name,-27}");
                terminal.SetColor(canAfford ? "cyan" : "red");
                terminal.Write($"{spell.effect,-35}");
                terminal.SetColor(canAfford ? "yellow" : "red");
                terminal.Write($"{cost,7:N0}g ");
                terminal.SetColor(canAfford ? "gray" : "red");
                terminal.Write($"{spell.manaCost,3}mp");
            }
            terminal.WriteLine("");
        }

        if (evilSurcharge)
        {
            terminal.SetColor("red");
            terminal.WriteLine("  (20% surcharge for dark alignment)");
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine("  [0] Cancel");
        terminal.WriteLine("");

        var spellInput = await terminal.GetInput("  Select spell: ");
        if (!int.TryParse(spellInput, out int spellIdx) || spellIdx < 1 || spellIdx > LoveSpells.Length)
            return;

        var selected = LoveSpells[spellIdx - 1];
        long spellCost = ApplyAllPriceModifiers(selected.baseCost + (player.Level * selected.levelScale), player);
        if (evilSurcharge) spellCost = (long)(spellCost * 1.2);

        if (player.Level < selected.minLevel)
        {
            DisplayMessage("  You must be at least level {selected.minLevel}.", "red");
            await terminal.WaitForKey();
            return;
        }
        if (player.Mana < selected.manaCost)
        {
            DisplayMessage("  Insufficient mana.", "red");
            await terminal.WaitForKey();
            return;
        }
        if (player.Gold < spellCost)
        {
            DisplayMessage("  Insufficient gold.", "red");
            await terminal.WaitForKey();
            return;
        }

        // Select target NPC - paginated picker with search
        var npcsAlive = NPCSpawnSystem.Instance?.ActiveNPCs?.Where(n => !n.IsDead).ToList() ?? new List<NPC>();
        if (npcsAlive.Count == 0)
        {
            DisplayMessage("  No NPCs available.", "gray");
            await terminal.WaitForKey();
            return;
        }

        var targetNPC = await PickNPCTarget(player, npcsAlive, $"Casting: {selected.name}", showRelationship: true, showLevel: false);
        if (targetNPC == null) return;

        // Check if already at Love or better
        int currentRel = RelationshipSystem.GetRelationshipLevel(player, targetNPC);
        if (currentRel <= 20)
        {
            terminal.SetColor("cyan");
            terminal.WriteLine($"  '{targetNPC.Name1} already adores you. My magic cannot improve upon that.'");
            await terminal.WaitForKey();
            return;
        }

        // Check Binding of Souls daily limit
        if (selected.bypassCap && _bindingOfSoulsUsedToday.Contains(targetNPC.Name1))
        {
            terminal.SetColor("red");
            terminal.WriteLine("  'The Binding can only be cast once per day on the same soul.'");
            await terminal.WaitForKey();
            return;
        }

        // Show confirmation with before/after preview
        string beforeName = GetRelationshipDisplayName(currentRel);
        // Estimate where the relationship will end up
        int projectedRel = currentRel;
        for (int s = 0; s < selected.steps; s++)
            if (projectedRel > 20) projectedRel -= 10;
        string afterName = GetRelationshipDisplayName(projectedRel);

        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.Write($"  Cast ");
        terminal.SetColor("bright_magenta");
        terminal.Write(selected.name);
        terminal.SetColor("white");
        terminal.Write(" on ");
        terminal.SetColor("bright_magenta");
        terminal.Write(targetNPC.Name1);
        terminal.SetColor("white");
        terminal.WriteLine("?");
        terminal.SetColor("gray");
        terminal.Write("  Relationship: ");
        terminal.SetColor(GetRelationshipColor(currentRel));
        terminal.Write(beforeName);
        terminal.SetColor("gray");
        terminal.Write(" --> ");
        terminal.SetColor(GetRelationshipColor(projectedRel));
        terminal.WriteLine($"{afterName} (estimated)");
        terminal.SetColor("yellow");
        terminal.WriteLine($"  Cost: {spellCost:N0} gold, {selected.manaCost} mana");
        terminal.WriteLine("");

        var confirm = await terminal.GetInput("  Proceed? (Y/N): ");
        if (confirm.ToUpper() != "Y")
            return;

        // Deduct costs
        player.Gold -= spellCost;
        player.Mana -= selected.manaCost;

        // Check resistance (high Commitment NPCs)
        float commitment = targetNPC.Brain?.Personality?.Commitment ?? 0.5f;
        if (commitment > 0.7f && random.Next(100) < 25)
        {
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine($"  {_ownerName} whispers ancient words over a rose-colored crystal...");
            await Task.Delay(500);
            terminal.SetColor("yellow");
            terminal.WriteLine("  The magic dissipates harmlessly.");
            terminal.SetColor("red");
            terminal.WriteLine($"  {targetNPC.Name1} has too strong a will for such charms.");
            terminal.SetColor("darkgray");
            terminal.WriteLine("  (Gold and mana still consumed)");
            player.Statistics?.RecordLoveSpellCast(spellCost);
            player.Statistics?.RecordGoldSpent(spellCost);
            await terminal.WaitForKey();
            return;
        }

        // Apply relationship change - direction 1 = IMPROVE (decrease relation number)
        if (selected.bypassCap)
        {
            // Binding of Souls - bypasses daily cap via overrideMaxFeeling
            RelationshipSystem.UpdateRelationship(player, targetNPC, 1, selected.steps, false, true);
            _bindingOfSoulsUsedToday.Add(targetNPC.Name1);
        }
        else
        {
            RelationshipSystem.UpdateRelationship(player, targetNPC, 1, selected.steps);
        }

        int newRel = RelationshipSystem.GetRelationshipLevel(player, targetNPC);
        string newRelName = GetRelationshipDisplayName(newRel);

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine($"  {_ownerName} whispers ancient words over a rose-colored crystal...");
        await Task.Delay(500);
        terminal.SetColor("magenta");
        terminal.WriteLine("  The crystal pulses with warmth and then shatters softly.");
        terminal.SetColor("white");
        terminal.WriteLine("  A feeling of connection washes over you.");
        terminal.WriteLine("");
        terminal.SetColor("bright_green");
        terminal.WriteLine($"  {targetNPC.Name1}'s feelings toward you have improved!");
        terminal.SetColor("gray");
        terminal.Write("  Relationship: ");
        terminal.SetColor(GetRelationshipColor(currentRel));
        terminal.Write(beforeName);
        terminal.SetColor("gray");
        terminal.Write(" --> ");
        terminal.SetColor(GetRelationshipColor(newRel));
        terminal.WriteLine(newRelName);

        player.Statistics?.RecordLoveSpellCast(spellCost);
        player.Statistics?.RecordGoldSpent(spellCost);
        TelemetrySystem.Instance.TrackShopTransaction("magic_shop", "love_spell", selected.name, spellCost, player.Level, player.Gold);

        if ((player.Statistics?.TotalLoveSpellsCast ?? 0) >= 5)
            AchievementSystem.TryUnlock(player, "love_magician");

        await terminal.WaitForKey();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // DEATH SPELLS - Dark Arts
    // ═══════════════════════════════════════════════════════════════════════════

    private static readonly (string name, int baseSuccess, long baseCost, int levelScale, int manaCost, int minLevel, int darkShift, string desc)[] DeathSpells = new[]
    {
        ("Weakening Hex",   40, 3000L,  200, 30,  15, 5,  "Drains life force - may kill target"),
        ("Death's Touch",   60, 10000L, 500, 60,  25, 15, "Kills target with necrotic energy"),
        ("Soul Severance",  80, 30000L, 1000, 100, 40, 25, "Rips the soul from the body"),
    };

    // Shopkeepers and companions that cannot be targeted
    private static readonly HashSet<string> ProtectedNPCs = new(StringComparer.OrdinalIgnoreCase)
    {
        "Tully", "Reese", "Ravanella",              // shopkeepers
        "Lyra", "Vex", "Kael", "Thorne",           // companions
    };

    /// <summary>Calculate death spell success chance. Uses INT with diminishing returns.</summary>
    private static int CalcDeathSpellSuccess(int baseSuccess, long intelligence)
    {
        // Diminishing returns: first 30 INT gives full bonus, after that halved
        int intBonus;
        if (intelligence <= 30)
            intBonus = (int)(intelligence / 3);       // 0-10 bonus from first 30 INT
        else
            intBonus = 10 + (int)((intelligence - 30) / 6);  // slower scaling after 30
        return Math.Min(90, baseSuccess + intBonus);
    }

    private async Task CastDeathSpell(Character player)
    {
        terminal.ClearScreen();
        terminal.SetColor("red");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                              DARK ARTS                                      ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine($"  {_ownerName}'s eyes darken. 'These are not services I offer lightly.'");
        terminal.SetColor("cyan");
        terminal.WriteLine("  'The shadows exact a price beyond gold.'");

        if ((OceanPhilosophySystem.Instance?.AwakeningLevel ?? 0) >= 3)
        {
            terminal.SetColor("magenta");
            terminal.WriteLine("  'You know what you do is the Ocean hurting itself. Proceed?'");
        }
        terminal.WriteLine("");

        bool isGood = player.Chivalry > player.Darkness + 20;

        terminal.SetColor("gray");
        terminal.WriteLine($"  You have {player.Gold:N0} gold, {player.Mana} mana");
        terminal.WriteLine("");

        // Column header
        terminal.SetColor("darkgray");
        terminal.WriteLine("   #  Spell              Effect                          Chance  Cost        Mana  Dark");
        terminal.WriteLine("  ─── ────────────────── ─────────────────────────────── ─────── ─────────── ───── ─────");

        for (int i = 0; i < DeathSpells.Length; i++)
        {
            var spell = DeathSpells[i];
            long cost = ApplyAllPriceModifiers(spell.baseCost + (player.Level * spell.levelScale), player);
            int displaySuccess = CalcDeathSpellSuccess(spell.baseSuccess, player.Intelligence);
            bool meetsLevel = player.Level >= spell.minLevel;
            bool canAfford = player.Gold >= cost && player.Mana >= spell.manaCost;
            int darkShift = isGood ? spell.darkShift * 2 : spell.darkShift;

            terminal.SetColor("gray");
            terminal.Write($"  [{i + 1}] ");

            if (!meetsLevel)
            {
                terminal.SetColor("darkgray");
                terminal.Write($"{spell.name,-19}{spell.desc,-32}");
                terminal.Write($"  ---   ");
                terminal.Write($"{"---",11}  {"---",3}   ");
                terminal.Write($"[Lv.{spell.minLevel}]");
            }
            else
            {
                terminal.SetColor(canAfford ? "bright_red" : "red");
                terminal.Write($"{spell.name,-19}");
                terminal.SetColor(canAfford ? "gray" : "darkgray");
                terminal.Write($"{spell.desc,-32}");
                terminal.SetColor(canAfford ? "white" : "darkgray");
                terminal.Write($"{displaySuccess,4}%   ");
                terminal.SetColor(canAfford ? "yellow" : "darkgray");
                terminal.Write($"{cost,11:N0}");
                terminal.SetColor(canAfford ? "cyan" : "darkgray");
                terminal.Write($" {spell.manaCost,4}mp");
                terminal.SetColor("red");
                terminal.Write($"  +{darkShift}");
            }
            terminal.WriteLine("");
        }

        terminal.WriteLine("");
        if (isGood)
        {
            terminal.SetColor("red");
            terminal.WriteLine("  WARNING: Your good alignment doubles the darkness penalty!");
        }

        terminal.SetColor("gray");
        terminal.WriteLine("  [0] Cancel");
        terminal.WriteLine("");

        var spellInput = await terminal.GetInput("  Select spell: ");
        if (!int.TryParse(spellInput, out int spellIdx) || spellIdx < 1 || spellIdx > DeathSpells.Length)
            return;

        var selected = DeathSpells[spellIdx - 1];
        long spellCost = ApplyAllPriceModifiers(selected.baseCost + (player.Level * selected.levelScale), player);

        if (player.Level < selected.minLevel)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"  You must be at least level {selected.minLevel}.");
            await terminal.WaitForKey();
            return;
        }
        if (player.Mana < selected.manaCost)
        {
            terminal.SetColor("red");
            terminal.WriteLine("  Insufficient mana.");
            await terminal.WaitForKey();
            return;
        }
        if (player.Gold < spellCost)
        {
            terminal.SetColor("red");
            terminal.WriteLine("  Insufficient gold.");
            await terminal.WaitForKey();
            return;
        }

        // Alignment warning for good characters
        if (isGood)
        {
            terminal.WriteLine("");
            terminal.SetColor("blue");
            terminal.WriteLine("  Your noble heart resists this dark path.");
            var warnConfirm = await terminal.GetInput("  Are you sure you want to proceed? (Y/N): ");
            if (warnConfirm.ToUpper() != "Y") return;
        }

        // Build target list: exclude protected NPCs, spouse, and current King
        var npcsAlive = NPCSpawnSystem.Instance?.ActiveNPCs?.Where(n => !n.IsDead).ToList() ?? new List<NPC>();
        var currentKing = CastleLocation.GetCurrentKing();
        string? kingName = currentKing?.Name;
        var validTargets = npcsAlive.Where(n =>
            !ProtectedNPCs.Contains(n.Name1) &&
            !(RelationshipSystem.GetSpouseName(player) == n.Name1) &&
            !(kingName != null && n.Name1.Equals(kingName, StringComparison.OrdinalIgnoreCase))
        ).ToList();

        if (validTargets.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  No valid targets available.");
            await terminal.WaitForKey();
            return;
        }

        var targetNPC = await PickNPCTarget(player, validTargets, $"Dark Arts: {selected.name}", showRelationship: false, showLevel: true);
        if (targetNPC == null) return;

        // Confirmation screen with full details
        int successChance = CalcDeathSpellSuccess(selected.baseSuccess, player.Intelligence);
        int darkShiftAmount = isGood ? selected.darkShift * 2 : selected.darkShift;

        terminal.ClearScreen();
        terminal.SetColor("red");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.SetColor("red");
        terminal.Write("║  Confirm: ");
        terminal.SetColor("bright_red");
        terminal.Write(selected.name);
        int pad = 66 - selected.name.Length;
        if (pad < 0) pad = 0;
        terminal.SetColor("red");
        terminal.WriteLine(new string(' ', pad) + "║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.Write("  Target:   ");
        terminal.SetColor("bright_red");
        terminal.WriteLine($"{targetNPC.Name1} ({targetNPC.Class}, Level {targetNPC.Level})");

        terminal.SetColor("white");
        terminal.Write("  Success:  ");
        string chanceColor = successChance >= 70 ? "bright_green" : successChance >= 50 ? "yellow" : "red";
        terminal.SetColor(chanceColor);
        terminal.WriteLine($"{successChance}%");

        terminal.SetColor("white");
        terminal.Write("  Cost:     ");
        terminal.SetColor("yellow");
        terminal.WriteLine($"{spellCost:N0} gold, {selected.manaCost} mana");

        terminal.SetColor("white");
        terminal.Write("  Darkness: ");
        terminal.SetColor("red");
        terminal.WriteLine($"+{darkShiftAmount} alignment shift{(isGood ? " (doubled - good alignment)" : "")}");

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine("  On success: Target dies. All NPCs will view you with suspicion.");
        terminal.WriteLine("  On failure: Target becomes hostile. Gold and mana still spent.");
        terminal.WriteLine("");

        var confirm = await terminal.GetInput("  Proceed with dark magic? (Y/N): ");
        if (confirm.ToUpper() != "Y") return;

        // Deduct costs
        player.Gold -= spellCost;
        player.Mana -= selected.manaCost;

        // Calculate success
        bool success = random.Next(100) < successChance;

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine($"  {_ownerName} draws a circle of black salt on the floor...");
        await Task.Delay(800);
        terminal.SetColor("darkred");
        terminal.WriteLine("  Dark energy gathers, spiraling toward an unseen target...");
        await Task.Delay(800);

        if (success)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  The candles flicker and die.");
            await Task.Delay(500);
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.WriteLine($"  'It is done. {targetNPC.Name1} has passed beyond the veil.'");

            // Kill the NPC
            targetNPC.IsDead = true;
            targetNPC.HP = 0;
            WorldSimulator.Instance?.QueueNPCForRespawn(targetNPC.Name1);

            // Alignment shift
            player.Darkness += darkShiftAmount;
            player.Chivalry = Math.Max(0, player.Chivalry - darkShiftAmount / 2);
            terminal.SetColor("red");
            terminal.WriteLine($"  Your alignment shifts toward darkness. (+{darkShiftAmount} Darkness)");

            // Worsen relationships with ALL living NPCs (not just first 10)
            int affectedCount = 0;
            foreach (var npc in npcsAlive.Where(n => n != targetNPC && !n.IsDead))
            {
                int worsenSteps = random.Next(2, 4);
                RelationshipSystem.UpdateRelationship(player, npc, -1, worsenSteps);
                affectedCount++;
            }
            terminal.SetColor("gray");
            if (affectedCount > 0)
                terminal.WriteLine($"  News of the death spreads. {affectedCount} NPCs view you with suspicion.");

            player.Statistics?.RecordDeathSpellCast(spellCost);
            AchievementSystem.TryUnlock(player, "dark_magician");
            if ((player.Statistics?.TotalDeathSpellsCast ?? 0) >= 5)
                AchievementSystem.TryUnlock(player, "angel_of_death");
        }
        else
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("  The dark energy dissipates... the spell has failed.");
            terminal.SetColor("white");
            terminal.WriteLine($"  {targetNPC.Name1} somehow resists the magic!");
            terminal.WriteLine("");

            // Failure consequences: NPC becomes hostile
            RelationshipSystem.UpdateRelationship(player, targetNPC, -1, 5);
            terminal.SetColor("red");
            terminal.WriteLine($"  {targetNPC.Name1} senses what you attempted. They are furious.");

            // Still shift alignment (you tried)
            player.Darkness += darkShiftAmount / 2;
            terminal.SetColor("red");
            terminal.WriteLine($"  Your alignment shifts toward darkness. (+{darkShiftAmount / 2} Darkness)");

            terminal.SetColor("darkgray");
            terminal.WriteLine("  (Gold and mana still consumed)");

            player.Statistics?.RecordDeathSpellCast(spellCost);
        }

        player.Statistics?.RecordGoldSpent(spellCost);
        TelemetrySystem.Instance.TrackShopTransaction("magic_shop", "death_spell", selected.name, spellCost, player.Level, player.Gold);

        await terminal.WaitForKey();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MANA POTIONS - Unique to Magic Shop
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task BuyManaPotions(Character player)
    {
        terminal.ClearScreen();
        DisplayMessage("═══ Mana Potions ═══", "blue");
        DisplayMessage("");

        int potionPrice = (int)ApplyAllPriceModifiers(player.Level * 8, player);
        int maxCanBuy = potionPrice > 0 ? (int)(player.Gold / potionPrice) : 0;
        int maxCanCarry = player.MaxManaPotions - (int)player.ManaPotions;
        int maxPotions = Math.Min(maxCanBuy, maxCanCarry);
        int manaRestored = 30 + player.Level * 2;

        DisplayMessage($"Each potion restores {manaRestored} mana.", "cyan");
        DisplayMessage($"Price: {potionPrice:N0} gold per potion", "gray");
        DisplayMessage($"You have: {player.Gold:N0} gold", "gray");
        DisplayMessage($"Mana potions: {player.ManaPotions}/{player.MaxManaPotions}", "blue");
        DisplayMessage("");

        if (player.ManaPotions >= player.MaxManaPotions)
        {
            DisplayMessage("You're carrying the maximum number of mana potions!", "red");
            await terminal.WaitForKey();
            return;
        }
        if (maxPotions <= 0)
        {
            DisplayMessage("You can't afford any potions!", "red");
            await terminal.WaitForKey();
            return;
        }

        var input = await terminal.GetInput($"How many? (max {maxPotions}): ");
        if (int.TryParse(input, out int qty) && qty > 0 && qty <= maxPotions)
        {
            long totalCost = qty * potionPrice;
            player.Gold -= totalCost;
            player.ManaPotions += qty;

            DisplayMessage($"You buy {qty} mana potion{(qty > 1 ? "s" : "")}.", "bright_green");
            DisplayMessage($"Total cost: {totalCost:N0} gold.", "gray");

            player.Statistics?.RecordGoldSpent(totalCost);
            player.Statistics?.RecordMagicShopPurchase(totalCost);
            CityControlSystem.Instance.ProcessSaleTax(totalCost);
        }
        else
        {
            DisplayMessage("Cancelled.", "gray");
        }

        await terminal.WaitForKey();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SCRYING SERVICE - NPC information
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task ScryNPC(Character player)
    {
        terminal.ClearScreen();
        DisplayMessage("═══ Scrying Service ═══", "magenta");
        DisplayMessage("");
        DisplayMessage($"{_ownerName} gazes into a crystal orb that swirls with mist...", "gray");
        DisplayMessage("'Name the soul you seek, and I shall find them.'", "cyan");
        DisplayMessage("");

        long scryCost = ApplyAllPriceModifiers(1000 + (player.Level * 50), player);
        DisplayMessage($"Cost: {scryCost:N0} gold per reading", "yellow");
        DisplayMessage("");

        if (player.Gold < scryCost)
        {
            DisplayMessage("Insufficient gold.", "red");
            await terminal.WaitForKey();
            return;
        }

        var allNPCs = NPCSpawnSystem.Instance?.ActiveNPCs?.ToList() ?? new List<NPC>();
        if (allNPCs.Count == 0)
        {
            DisplayMessage("No NPCs in the world.", "gray");
            await terminal.WaitForKey();
            return;
        }

        var target = await PickNPCTarget(player, allNPCs, "Scrying - Choose a Soul", showRelationship: true, showLevel: true);
        if (target == null) return;

        player.Gold -= scryCost;

        DisplayMessage("");
        DisplayMessage("The mists part to reveal...", "magenta");
        await Task.Delay(500);
        DisplayMessage("");
        DisplayMessage($"═══ {target.Name1} ═══", "cyan");
        DisplayMessage($"  Class: {target.Class}    Level: {target.Level}", "white");
        DisplayMessage($"  Status: {(target.IsDead ? "DEAD" : "Alive")}", target.IsDead ? "red" : "green");

        int rel = RelationshipSystem.GetRelationshipLevel(player, target);
        string relName = GetRelationshipDisplayName(rel);
        string relColor = GetRelationshipColor(rel);
        DisplayMessage($"  Relationship: {relName}", relColor);

        string spouseName = RelationshipSystem.GetSpouseName(target);
        if (!string.IsNullOrEmpty(spouseName))
            DisplayMessage($"  Married to: {spouseName}", "white");

        if (target.Brain?.Personality != null)
        {
            var p = target.Brain.Personality;
            string trait1 = p.Romanticism > 0.7f ? "Romantic" : p.Aggression > 0.7f ? "Aggressive" : p.Intelligence > 0.7f ? "Scholarly" : "Practical";
            string trait2 = p.Greed > 0.7f ? "Greedy" : p.Loyalty > 0.7f ? "Loyal" : p.Sociability > 0.7f ? "Social" : "Reserved";
            DisplayMessage($"  Personality: {trait1}, {trait2}", "gray");
        }

        player.Statistics?.RecordGoldSpent(scryCost);
        player.Statistics?.RecordMagicShopPurchase(scryCost);

        await terminal.WaitForKey();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ENHANCED SHOPKEEPER DIALOGUE + WAVE FRAGMENT
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task TalkToOwnerEnhanced(Character player)
    {
        terminal.ClearScreen();
        DisplayMessage($"═══ Conversation with {_ownerName} ═══", "cyan");
        DisplayMessage("");

        int awakeningLevel = OceanPhilosophySystem.Instance?.AwakeningLevel ?? 0;
        bool hasCorruptionFragment = OceanPhilosophySystem.Instance?.CollectedFragments?.Contains(WaveFragment.TheCorruption) ?? false;
        string playerAddress = hasCorruptionFragment ? "Dreamer" : GetRaceGreeting(player.Race);
        long totalSpent = player.Statistics?.TotalMagicShopGoldSpent ?? 0;
        long deathSpells = player.Statistics?.TotalDeathSpellsCast ?? 0;

        // Context-aware greeting
        if (deathSpells > 0)
        {
            DisplayMessage($"The shadows on you grow heavier each time, {playerAddress}.", "gray");
            DisplayMessage($"{_ownerName} studies you with knowing, troubled eyes.", "gray");
        }
        else if (totalSpent >= 50000)
        {
            DisplayMessage($"Ah, my most valued patron! Welcome back, {playerAddress}.", "cyan");
        }
        else
        {
            DisplayMessage($"'Welcome, {playerAddress},' {_ownerName} says, looking up from an ancient tome.", "cyan");
        }
        DisplayMessage("");

        // Class-specific commentary
        string classComment = player.Class switch
        {
            CharacterClass.Magician => "'A fellow practitioner of the arcane. We understand each other.'",
            CharacterClass.Sage => "'The wisdom you carry... it resonates with the items here.'",
            CharacterClass.Cleric => "'The divine and the arcane are closer than most believe.'",
            CharacterClass.Paladin => "'A holy warrior in a shop of mysteries. How delightfully contradictory.'",
            CharacterClass.Jester => "'I've warded everything against theft. Just so you know.'",
            CharacterClass.Assassin => "'Your kind appreciates the... specialized services I offer.'",
            CharacterClass.Ranger => "'The wild magic in you makes these items sing differently.'",
            CharacterClass.Barbarian => "'Most of your kind dismiss magic. Yet here you stand.'",
            CharacterClass.Warrior => "'Steel and sorcery make a powerful combination.'",
            CharacterClass.Bard => "'Music and magic share the same root. Let me show you.'",
            _ => "'Every soul has magical potential, if they know where to look.'"
        };
        DisplayMessage(classComment, "cyan");
        DisplayMessage("");

        // Story-aware comments
        var story = StoryProgressionSystem.Instance;
        if (story != null)
        {
            int godsDefeated = story.OldGodStates?.Count(kvp => kvp.Value?.Status == GodStatus.Defeated) ?? 0;
            if (godsDefeated >= 3)
                DisplayMessage("'You've faced the Old Gods and lived. That changes a person.'", "magenta");
            else if (godsDefeated >= 1)
                DisplayMessage("'I hear whispers of your deeds in the deep places.'", "gray");
        }

        if (player.King)
            DisplayMessage("'Your Majesty graces my humble shop. How may I serve the crown?'", "yellow");

        // Alignment commentary
        if (player.Darkness > 80)
            DisplayMessage("'The darkness in you... it makes certain items resonate. Be careful.'", "darkred");
        else if (player.Chivalry > 80)
            DisplayMessage("'Your noble spirit brightens this dusty shop.'", "blue");

        // Awakening wisdom
        if (awakeningLevel >= 5)
        {
            string wisdom = OceanPhilosophySystem.Instance?.GetAmbientWisdom() ?? "";
            if (!string.IsNullOrEmpty(wisdom))
            {
                DisplayMessage("");
                DisplayMessage($"'...{wisdom}'", "magenta");
            }
        }

        // Check for Wave Fragment trigger
        bool fragmentAvailable = awakeningLevel >= 3
            && (OceanPhilosophySystem.Instance?.CollectedFragments?.Count ?? 0) >= 3
            && deathSpells >= 1
            && !hasCorruptionFragment;

        if (fragmentAvailable)
        {
            DisplayMessage("");
            DisplayMessage("Something troubles you... a darkness since you used the death magic.", "magenta");
            DisplayMessage("");
            DisplayMessage("(F) Ask about the darkness you've felt", "bright_magenta");
        }

        // Loyalty discount info
        if (totalSpent >= 10000 && totalSpent < 200000)
        {
            long nextThreshold = totalSpent < 50000 ? 50000 : 200000;
            DisplayMessage("");
            DisplayMessage($"'You've spent {totalSpent:N0} gold here. At {nextThreshold:N0}, your loyalty discount increases.'", "gray");
        }

        DisplayMessage("");
        DisplayMessage("(Q) End conversation", "gray");

        var choice = await terminal.GetInput("> ");
        if (choice.ToUpper() == "F" && fragmentAvailable)
        {
            await ShowCorruptionFragment(player);
        }
    }

    private async Task ShowCorruptionFragment(Character player)
    {
        terminal.ClearScreen();
        DisplayMessage("");
        DisplayMessage($"{_ownerName}'s expression becomes grave.", "gray");
        await Task.Delay(1000);
        DisplayMessage("");
        DisplayMessage("'Every death spell fragments something inside the caster.'", "cyan");
        DisplayMessage("'I've seen it before. Many times. Over more years than you'd believe.'", "cyan");
        await Task.Delay(1500);
        DisplayMessage("");
        DisplayMessage("He reaches beneath the counter and produces a dark crystal.", "gray");
        DisplayMessage("It pulses with a sickly light, like a bruise on reality.", "magenta");
        await Task.Delay(1500);
        DisplayMessage("");
        DisplayMessage("'The Ocean dreamed of separation, and separation became corruption.'", "magenta");
        DisplayMessage("'Power that takes life is the Ocean forgetting what it is.'", "magenta");
        await Task.Delay(1500);
        DisplayMessage("");
        DisplayMessage("'This formed from the residue of every death spell cast in this shop.'", "cyan");
        DisplayMessage("'Centuries of dark magic, crystallized into understanding.'", "cyan");
        DisplayMessage("'Take it. Understand what corruption truly means.'", "cyan");
        await Task.Delay(1000);
        DisplayMessage("");

        // Award the fragment
        OceanPhilosophySystem.Instance?.CollectFragment(WaveFragment.TheCorruption);

        DisplayMessage("You received: Wave Fragment - The Corruption", "bright_magenta");
        DisplayMessage("'The corruption is not evil. It is forgetting. Remember that.'", "magenta");
        DisplayMessage("");

        AchievementSystem.TryUnlock(player, "corruption_fragment");

        await terminal.WaitForKey();
    }
} 
