using UsurperRemake;
using UsurperRemake.Utils;
using UsurperRemake.Systems;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
    
    private async Task<bool> HandleMagicShopChoice(string choice, Character player)
    {
        switch (choice)
        {
            case "L":
                ListMagicItemsByCategory(player);
                await terminal.WaitForKey();
                return false;
            case "B":
                BuyMagicItem(player);
                await terminal.WaitForKey();
                return false;
            case "S":
                SellItem(player);
                await terminal.WaitForKey();
                return false;
            case "I":
                IdentifyItem(player);
                await terminal.WaitForKey();
                return false;
            case "C":
                RemoveCurse(player);
                await terminal.WaitForKey();
                return false;
            case "E":
                EnchantItem(player);
                await terminal.WaitForKey();
                return false;
            case "H":
                BuyHealingPotions(player);
                await terminal.WaitForKey();
                return false;
            case "Y":
                await SpellLearningSystem.ShowSpellLearningMenu(player, terminal);
                return false;
            case "T":
                TalkToOwner(player);
                await terminal.WaitForKey();
                return false;
            case "D":
                await BuyDungeonResetScroll(player);
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

        // Menu options - standardized format
        DisplayMessage("═══ Shopping ═══                         ═══ Services ═══", "cyan");
        terminal.WriteLine("");

        // Row 1
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_green");
        terminal.Write("L");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("ist Items by Category       ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("I");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine("dentify item");

        // Row 2
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_green");
        terminal.Write("B");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("uy Item                     ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("C");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine("urse Removal");

        // Row 3
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_green");
        terminal.Write("S");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("ell Item                    ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("E");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine("nchant/Bless Item");

        // Row 4
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_green");
        terminal.Write("H");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("ealing Potions              ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_magenta");
        terminal.Write("Y");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine(" Study spells");

        terminal.WriteLine("");

        // Row 5 - Dungeon Reset Scroll
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_magenta");
        terminal.Write("D");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("ungeon Reset Scroll          ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_cyan");
        terminal.Write("T");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine($"alk to {_ownerName}");

        // Row 6 - Return
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_red");
        terminal.Write("R");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine("eturn to street");
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
    
    private void ProcessMagicShopMenu(Character player)
    {
        while (true)
        {
            DisplayMessage("Magic Shop (? for menu): ", "yellow", false);
            var choice = terminal.GetInputSync("").ToUpper();
            DisplayMessage(""); // New line
            
            switch (choice)
            {
                case "L":
                    ListMagicItemsByCategory(player);
                    break;
                case "B":
                    BuyMagicItem(player);
                    break;
                case "S":
                    SellItem(player);
                    break;
                case "I":
                    IdentifyItem(player);
                    break;
                case "C":
                    RemoveCurse(player);
                    break;
                case "E":
                    EnchantItem(player);
                    break;
                case "H":
                    BuyHealingPotions(player);
                    break;
                case "Y":
                    SpellLearningSystem.ShowSpellLearningMenu(player, terminal).Wait();
                    break;
                case "T":
                    TalkToOwner(player);
                    break;
                case "R":
                    // Return to previous location - navigation handled by LocationManager
                    return;
                case "?":
                    DisplayMagicShopMenu(player);
                    return;
                default:
                    DisplayMessage("Invalid choice. Press '?' for menu.", "red");
                    break;
            }
            
            DisplayMessage("");
            DisplayMessage("Press Enter to continue...", "yellow");
            Console.ReadKey();
            DisplayMagicShopMenu(player);
            return;
        }
    }
    
    private void ListMagicItems(Character player)
    {
        DisplayMessage("");
        DisplayMessage("═══ Magic Items for Sale ═══", "cyan");
        DisplayMessage("");
        
        int itemNumber = 1;
        foreach (var item in _magicInventory)
        {
            DisplayMessage($"{itemNumber}. {item.Name} - {item.Value:N0} gold", "white");
            
            // Show item properties
            var properties = new List<string>();
            if (item.Strength != 0) properties.Add($"Str {(item.Strength > 0 ? "+" : "")}{item.Strength}");
            if (item.Defence != 0) properties.Add($"Def {(item.Defence > 0 ? "+" : "")}{item.Defence}");
            if (item.Attack != 0) properties.Add($"Att {(item.Attack > 0 ? "+" : "")}{item.Attack}");
            if (item.Dexterity != 0) properties.Add($"Dex {(item.Dexterity > 0 ? "+" : "")}{item.Dexterity}");
            if (item.Wisdom != 0) properties.Add($"Wis {(item.Wisdom > 0 ? "+" : "")}{item.Wisdom}");
            if (item.MagicProperties.Mana != 0) properties.Add($"Mana {(item.MagicProperties.Mana > 0 ? "+" : "")}{item.MagicProperties.Mana}");
            
            if (properties.Count > 0)
            {
                DisplayMessage($"   ({string.Join(", ", properties)})", "green");
            }
            
            // Show restrictions
            if (item.OnlyForGood) DisplayMessage("   (Good characters only)", "blue");
            if (item.OnlyForEvil) DisplayMessage("   (Evil characters only)", "red");
            if (item.IsCursed) DisplayMessage("   (CURSED!)", "darkred");
            if (item.MagicProperties.DiseaseImmunity != CureType.None)
            {
                DisplayMessage($"   (Cures {item.MagicProperties.DiseaseImmunity})", "green");
            }
            
            DisplayMessage("");
            itemNumber++;
        }
    }
    
    private void BuyMagicItem(Character player)
    {
        DisplayMessage("");
        DisplayMessage("Enter Item # to buy: ", "yellow", false);
        string input = Console.ReadLine();

        if (int.TryParse(input, out int itemNumber) && itemNumber > 0 && itemNumber <= _magicInventory.Count)
        {
            var item = _magicInventory[itemNumber - 1];

            // Calculate adjusted price with alignment and world event modifiers
            var alignmentModifier = AlignmentSystem.Instance.GetPriceModifier(player, isShadyShop: false);
            var worldEventModifier = WorldEventSystem.Instance.GlobalPriceModifier;
            long adjustedPrice = (long)(item.Value * alignmentModifier * worldEventModifier);

            // Apply city control discount if player's team controls the city
            adjustedPrice = CityControlSystem.Instance.ApplyDiscount(adjustedPrice, player);

            // Apply faction discount (The Crown gets 10% off at shops)
            adjustedPrice = (long)(adjustedPrice * FactionSystem.Instance.GetShopPriceModifier());

            // Check restrictions
            if (item.OnlyForGood && player.Chivalry < 1 && player.Darkness > 0)
            {
                DisplayMessage("This item is charmed for good characters.", "red");
                DisplayMessage("You can buy it, but you cannot use it!", "red");
            }
            else if (item.OnlyForEvil && player.Chivalry > 0 && player.Darkness < 1)
            {
                DisplayMessage("This item is enchanted and can be used by evil characters only.", "red");
                DisplayMessage("You can buy it, but not use it!", "red");
            }

            if (item.StrengthRequired > player.Strength)
            {
                DisplayMessage("This item is too heavy for you to use!", "red");
            }

            // Check class restrictions (if any)
            // TODO: Implement class restrictions when needed

            // Show price with discount/markup if applicable
            var totalModifier = alignmentModifier * worldEventModifier;
            if (Math.Abs(totalModifier - 1.0f) > 0.01f)
                DisplayMessage($"Buy the {item.Name} for {adjustedPrice:N0} gold (was {item.Value:N0})? (Y/N): ", "yellow", false);
            else
                DisplayMessage($"Buy the {item.Name} for {adjustedPrice:N0} gold? (Y/N): ", "yellow", false);
            var confirm = Console.ReadKey().KeyChar.ToString().ToUpper();
            DisplayMessage("");

            if (confirm == "Y")
            {
                if (player.Gold < adjustedPrice)
                {
                    DisplayMessage("You don't have enough gold!", "red");
                }
                else if (player.Inventory.Count >= GameConfig.MaxInventoryItems)
                {
                    DisplayMessage("Your inventory is full!", "red");
                }
                else
                {
                    player.Gold -= adjustedPrice;
                    player.Inventory.Add(item.Clone());

                    // Process city tax share from this sale
                    CityControlSystem.Instance.ProcessSaleTax(adjustedPrice);

                    DisplayMessage("Done!", "green");
                    DisplayMessage($"You purchased the {item.Name}.", "gray");
                    
                    // Ask if they want to equip it immediately
                    DisplayMessage($"Start to use the {item.Name} immediately? (Y/N): ", "yellow", false);
                    var useNow = Console.ReadKey().KeyChar.ToString().ToUpper();
                    DisplayMessage("");
                    
                    if (useNow == "Y")
                    {
                        // TODO: Implement item equipping system
                        DisplayMessage($"You equip the {item.Name}.", "green");
                    }
                    else
                    {
                        DisplayMessage($"You put the {item.Name} in your backpack.", "gray");
                    }
                }
            }
        }
        else
        {
            DisplayMessage("Invalid item number.", "red");
        }
    }
    
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
        string input = Console.ReadLine();

        if (int.TryParse(input, out int itemIndex) && itemIndex > 0 && itemIndex <= player.Inventory.Count)
        {
            var item = player.Inventory[itemIndex - 1];
            long sellPrice = (long)((item.Value / 2) * fenceModifier); // Apply faction bonus

            // Check if shop wants this item type
            if (item.Type == ObjType.Magic || item.MagicType != MagicItemType.None)
            {
                DisplayMessage($"Sell {item.Name} for {sellPrice:N0} gold? (Y/N): ", "yellow", false);
                var confirm = Console.ReadKey().KeyChar.ToString().ToUpper();
                DisplayMessage("");

                if (confirm == "Y")
                {
                    player.Inventory.RemoveAt(itemIndex - 1);
                    player.Gold += sellPrice;
                    player.Statistics.RecordSale(sellPrice);  // Track sale in statistics
                    DisplayMessage("Deal!", "green");
                    DisplayMessage($"You sold the {item.Name} for {sellPrice:N0} gold.", "gray");
                }
            }
            else
            {
                // Random grumpy response
                var responses = new[]
                {
                    "You are not worth dealing with!",
                    "Hahaha...!",
                    "NO HAGGLING IN MY STORE!",
                    "Pay or get lost!"
                };
                var response = responses[new Random().Next(responses.Length)];
                DisplayMessage($"I don't buy that kind of items, {_ownerName} says.", "red");
                DisplayMessage($"{response}, {_ownerName} adds.", "red");
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
        
        DisplayMessage("Unidentified items:", "cyan");
        for (int i = 0; i < unidentifiedItems.Count; i++)
        {
            DisplayMessage($"{i + 1}. Unknown item", "white");
        }
        
        DisplayMessage("");
        long identifyCost = GetIdentificationCost(player.Level);
        DisplayMessage($"Identification costs {identifyCost:N0} gold per item.", "gray");
        DisplayMessage("Enter item # to identify (0 to cancel): ", "yellow", false);
        string input = Console.ReadLine();

        if (int.TryParse(input, out int itemIndex) && itemIndex > 0 && itemIndex <= unidentifiedItems.Count)
        {
            if (player.Gold < identifyCost)
            {
                DisplayMessage("You don't have enough gold for identification!", "red");
                return;
            }

            var item = unidentifiedItems[itemIndex - 1];
            DisplayMessage($"Identify {item.Name} for {identifyCost:N0} gold? (Y/N): ", "yellow", false);
            var confirm = Console.ReadKey().KeyChar.ToString().ToUpper();
            DisplayMessage("");

            if (confirm == "Y")
            {
                player.Gold -= identifyCost;
                item.IsIdentified = true;
                
                DisplayMessage($"{_ownerName} examines the {item.Name} carefully...", "gray");
                DisplayMessage("");
                DisplayMessage($"The {item.Name} is now identified!", "green");
                
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
        
        // Calculate potion price: level × 5
        int potionPrice = player.Level * GameConfig.HealingPotionLevelMultiplier;
        int maxPotionsCanBuy = (int)(player.Gold / potionPrice);
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
        DisplayMessage($"Current price is {potionPrice:N0} gold per potion.", "gray");
        DisplayMessage($"You have {player.Gold:N0} gold.", "gray");
        DisplayMessage($"You have {player.Healing} potions.", "gray");
        DisplayMessage("");
        
        DisplayMessage($"How many? (max {maxPotions} potions): ", "yellow", false);
        string input = terminal.GetInputSync("");
        
        if (int.TryParse(input, out int quantity) && quantity > 0 && quantity <= maxPotions)
        {
            int totalCost = quantity * potionPrice;
            
            // Apply city control discount
            long adjustedCost = CityControlSystem.Instance.ApplyDiscount(totalCost, player);

            // Apply faction discount (The Crown gets 10% off at shops)
            adjustedCost = (long)(adjustedCost * FactionSystem.Instance.GetShopPriceModifier());

            if (player.Gold >= adjustedCost)
            {
                player.Gold -= adjustedCost;
                player.Healing += quantity;

                // Process city tax share from this sale
                CityControlSystem.Instance.ProcessSaleTax(adjustedCost);

                DisplayMessage($"Ok, it's a deal. You buy {quantity} potions.", "green");
                DisplayMessage($"Total cost: {adjustedCost:N0} gold.", "gray");
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
    
    private void TalkToOwner(Character player)
    {
        DisplayMessage("");
        
        var responses = new[]
        {
            $"Welcome to my shop, {GetRaceGreeting(player.Race)}!",
            "These items hold great power, use them wisely.",
            "I've been collecting magical artifacts for centuries.",
            "Beware of cursed items - they can be as dangerous as they are powerful.",
            "The art of identification is not to be taken lightly.",
            "Healing potions? A bargain at these prices!",
            "Magic flows through everything if you know how to look.",
            "Good and evil items choose their wielders carefully."
        };
        
        var response = responses[new Random().Next(responses.Length)];
        DisplayMessage($"{_ownerName} says:", "cyan");
        DisplayMessage($"'{response}'", "white");
        
        // Special responses based on player status
        if (player.Class == CharacterClass.Magician || player.Class == CharacterClass.Sage)
        {
            DisplayMessage("", "gray");
            DisplayMessage("I sense magical potential in you. Choose your items carefully.", "magenta");
        }
        else if (player.Darkness > 50)
        {
            DisplayMessage("", "gray");
            DisplayMessage("Your aura is... interesting. Perhaps you'd be interested in some darker artifacts?", "darkred");
        }
        else if (player.Chivalry > 50)
        {
            DisplayMessage("", "gray");
            DisplayMessage("A noble spirit! I have some blessed items that might serve you well.", "blue");
        }
    }

    /// <summary>
    /// List magic items organized by category with pagination
    /// </summary>
    private void ListMagicItemsByCategory(Character player)
    {
        DisplayMessage("");
        DisplayMessage("═══ Browse Magic Items by Category ═══", "cyan");
        DisplayMessage("");
        DisplayMessage("(1) Rings          - Magical rings for your fingers", "gray");
        DisplayMessage("(2) Amulets        - Necklaces and pendants", "gray");
        DisplayMessage("(3) Belts          - Magical girdles and sashes", "gray");
        DisplayMessage("(4) Ocean Relics   - Items touched by Manwe's dream", "magenta");
        DisplayMessage("(5) Cursed Items   - Powerful but dangerous", "darkred");
        DisplayMessage("(6) All Items      - Browse everything", "gray");
        DisplayMessage("");
        DisplayMessage("Choose category (0 to cancel): ", "yellow", false);

        string input = Console.ReadLine();
        if (!int.TryParse(input, out int choice) || choice == 0)
            return;

        List<Item> filteredItems = choice switch
        {
            1 => _magicInventory.Where(i => i.MagicType == MagicItemType.Fingers).OrderBy(i => i.Value).ToList(),
            2 => _magicInventory.Where(i => i.MagicType == MagicItemType.Neck).OrderBy(i => i.Value).ToList(),
            3 => _magicInventory.Where(i => i.MagicType == MagicItemType.Waist).OrderBy(i => i.Value).ToList(),
            4 => _magicInventory.Where(i => HasLoreDescription(i) &&
                (i.Description[0].Contains("Ocean") || i.Description[0].Contains("wave") || i.Description[0].Contains("Manwe") ||
                 i.Description[0].Contains("water") || i.Description[0].Contains("Tide") || i.Description[0].Contains("Depth") ||
                 i.Name.Contains("Tide") || i.Name.Contains("Wave") || i.Name.Contains("Ocean") ||
                 i.Name.Contains("Deep") || i.Name.Contains("Fragment") || i.Name.Contains("Dreamer"))).OrderBy(i => i.Value).ToList(),
            5 => _magicInventory.Where(i => i.IsCursed).OrderBy(i => i.Value).ToList(),
            6 => _magicInventory.OrderBy(i => i.Value).ToList(),
            _ => new List<Item>()
        };

        string categoryName = choice switch
        {
            1 => "Rings",
            2 => "Amulets & Necklaces",
            3 => "Belts & Girdles",
            4 => "Ocean Relics",
            5 => "Cursed Items",
            6 => "All Magic Items",
            _ => "Unknown"
        };

        if (filteredItems.Count == 0)
        {
            DisplayMessage("No items found in this category.", "gray");
            return;
        }

        // Paginated display
        int itemsPerPage = 10;
        int totalPages = (filteredItems.Count + itemsPerPage - 1) / itemsPerPage;
        int currentPage = 0;

        while (true)
        {
            terminal.ClearScreen();
            DisplayMessage($"═══ {categoryName} ({filteredItems.Count} items) ═══", "cyan");
            DisplayMessage($"Page {currentPage + 1} of {totalPages}", "gray");
            DisplayMessage("");

            int startIndex = currentPage * itemsPerPage;
            int endIndex = Math.Min(startIndex + itemsPerPage, filteredItems.Count);

            for (int i = startIndex; i < endIndex; i++)
            {
                var item = filteredItems[i];
                int globalIndex = _magicInventory.IndexOf(item) + 1;

                // Item name with price
                string priceColor = item.IsCursed ? "darkred" : (item.OnlyForGood ? "blue" : (item.OnlyForEvil ? "red" : "white"));
                DisplayMessage($"{globalIndex,3}. {item.Name,-30} {item.Value,10:N0} gold", priceColor);

                // Stats line
                var stats = new List<string>();
                if (item.Strength != 0) stats.Add($"Str{(item.Strength > 0 ? "+" : "")}{item.Strength}");
                if (item.Defence != 0) stats.Add($"Def{(item.Defence > 0 ? "+" : "")}{item.Defence}");
                if (item.Attack != 0) stats.Add($"Att{(item.Attack > 0 ? "+" : "")}{item.Attack}");
                if (item.Dexterity != 0) stats.Add($"Dex{(item.Dexterity > 0 ? "+" : "")}{item.Dexterity}");
                if (item.Wisdom != 0) stats.Add($"Wis{(item.Wisdom > 0 ? "+" : "")}{item.Wisdom}");
                if (item.MagicProperties.Mana != 0) stats.Add($"Mana{(item.MagicProperties.Mana > 0 ? "+" : "")}{item.MagicProperties.Mana}");
                if (item.MagicProperties.MagicResistance != 0) stats.Add($"MR{(item.MagicProperties.MagicResistance > 0 ? "+" : "")}{item.MagicProperties.MagicResistance}");

                if (stats.Count > 0)
                    DisplayMessage($"     {string.Join(" ", stats)}", "green");

                // Lore description if available
                if (HasLoreDescription(item))
                    DisplayMessage($"     \"{item.Description[0]}\"", "gray");

                // Special flags
                var flags = new List<string>();
                if (item.OnlyForGood) flags.Add("[Good]");
                if (item.OnlyForEvil) flags.Add("[Evil]");
                if (item.IsCursed) flags.Add("[CURSED]");
                if (item.MagicProperties.DiseaseImmunity != CureType.None)
                    flags.Add($"[Cures {item.MagicProperties.DiseaseImmunity}]");

                if (flags.Count > 0)
                {
                    string flagColor = item.IsCursed ? "darkred" : (item.OnlyForGood ? "blue" : "yellow");
                    DisplayMessage($"     {string.Join(" ", flags)}", flagColor);
                }

                DisplayMessage(""); // Spacing between items
            }

            DisplayMessage("");
            DisplayMessage("(N)ext page  (P)revious page  (Q)uit browsing", "yellow");
            var key = Console.ReadKey(true).KeyChar.ToString().ToUpper();

            if (key == "N" && currentPage < totalPages - 1)
                currentPage++;
            else if (key == "P" && currentPage > 0)
                currentPage--;
            else if (key == "Q")
                break;
        }
    }

    /// <summary>
    /// Remove curse from an item - expensive but essential service
    /// </summary>
    private void RemoveCurse(Character player)
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
        DisplayMessage("Enter item # to uncurse (0 to cancel): ", "yellow", false);
        string input = Console.ReadLine();

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
        DisplayMessage($"Remove the curse from {targetItem.Name} for {cost:N0} gold? (Y/N): ", "yellow", false);
        var confirm = Console.ReadKey().KeyChar.ToString().ToUpper();
        DisplayMessage("");

        if (confirm == "Y")
        {
            player.Gold -= cost;

            // Dramatic curse removal scene
            DisplayMessage("");
            DisplayMessage($"{_ownerName} takes the {targetItem.Name} and places it in a circle of salt.", "gray");
            DisplayMessage("Ancient words fill the air, words older than the walls of this shop...", "gray");
            Thread.Sleep(500);
            DisplayMessage("The item shudders. Something dark rises from it like smoke...", "magenta");
            Thread.Sleep(500);
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
    private void EnchantItem(Character player)
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
        DisplayMessage("Choose enchantment type (0 to cancel): ", "yellow", false);

        string input = Console.ReadLine();
        if (!int.TryParse(input, out int enchantChoice) || enchantChoice <= 0 || enchantChoice > 6)
            return;

        long[] costs = { 0, 2000, 5000, 12000, 25000, 15000, 8000 };
        long cost = costs[enchantChoice];

        if (player.Gold < cost)
        {
            DisplayMessage("");
            DisplayMessage("'The magical arts require material compensation,' the gnome says pointedly.", "cyan");
            DisplayMessage($"You need {cost:N0} gold for this enchantment.", "red");
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
        DisplayMessage("Choose item to enchant (0 to cancel): ", "yellow", false);
        input = Console.ReadLine();

        if (!int.TryParse(input, out int itemIndex) || itemIndex <= 0 || itemIndex > enchantableItems.Count)
            return;

        var targetItem = enchantableItems[itemIndex - 1];

        if (targetItem.IsCursed)
        {
            DisplayMessage("");
            DisplayMessage("'I cannot enchant a cursed item. The dark magic would consume my work.'", "red");
            DisplayMessage("'Remove the curse first, then return.'", "cyan");
            return;
        }

        // For stat-boosting enchants, let player choose the stat
        int statChoice = 0;
        if (enchantChoice >= 1 && enchantChoice <= 3)
        {
            DisplayMessage("");
            DisplayMessage("Choose stat to enhance:", "cyan");
            DisplayMessage("(1) Strength  (2) Defence  (3) Dexterity  (4) Wisdom  (5) Attack", "gray");
            DisplayMessage("Choice: ", "yellow", false);
            input = Console.ReadLine();
            if (!int.TryParse(input, out statChoice) || statChoice <= 0 || statChoice > 5)
                return;
        }

        DisplayMessage("");
        DisplayMessage($"Enchant {targetItem.Name} for {cost:N0} gold? (Y/N): ", "yellow", false);
        var confirm = Console.ReadKey().KeyChar.ToString().ToUpper();
        DisplayMessage("");

        if (confirm != "Y")
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
        Thread.Sleep(500);

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
} 
