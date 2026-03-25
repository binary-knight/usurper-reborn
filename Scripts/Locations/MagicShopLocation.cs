using UsurperRemake;
using UsurperRemake.Utils;
using UsurperRemake.Systems;
using UsurperRemake.BBS;
using System;
using System.Collections.Generic;
using System.Linq;

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
    
    // Accessory category browsing state (matches weapon/armor shop pattern)
    private enum AccessoryCategory { Rings, Necklaces }
    private AccessoryCategory? _currentAccessoryCategory = null;
    private int _accessoryPage = 0;
    private const int AccessoryItemsPerPage = 15;

    private Random random = Random.Shared;
    
    // Local list to hold shop NPCs (replaces legacy global variable reference)
    private readonly List<NPC> npcs = new();
    
    public MagicShopLocation() : base(
        GameLocation.MagicShop,
        "Magic Shop",
        "A dark and dusty boutique filled with mysterious magical items."
    )
    {
        // Add shop owner NPC
        var shopOwner = CreateShopOwner();
        npcs.Add(shopOwner);
    }

    protected override void SetupLocation()
    {
        base.SetupLocation();
        _currentAccessoryCategory = null;
        _accessoryPage = 0;
    }

    protected override string GetMudPromptName() => "Magic Shop";

    protected override string[]? GetAmbientMessages() => new[]
    {
        "A faint arcane hum pulses from somewhere behind the shelves.",
        "Pages rustle by themselves in an open tome.",
        "A candle flame bends sideways though there is no wind.",
        "Glass vials chime softly against one another.",
        "The air tastes faintly of ozone and old ink.",
        "A distant incantation trails off and falls silent.",
    };

    protected override void DisplayLocation()
    {
        if (_currentAccessoryCategory.HasValue)
        {
            ShowAccessoryCategoryItems(_currentAccessoryCategory.Value);
            return;
        }
        if (IsScreenReader && currentPlayer != null)
        {
            DisplayLocationSR();
            return;
        }
        if (IsBBSSession) { DisplayLocationBBS(); return; }
        DisplayMagicShopMenu(currentPlayer);
    }

    /// <summary>
    /// Screen reader accessible layout — plain text, no box-drawing.
    /// </summary>
    private void DisplayLocationSR()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("magic_shop.header"));
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("magic_shop.run_by", _ownerName));
        terminal.WriteLine("");

        ShowNPCsInLocation();

        terminal.SetColor("yellow");
        terminal.WriteLine($"{Loc.Get("ui.gold")}: {currentPlayer.Gold:N0}");

        // Alignment price modifier
        var alignMod = AlignmentSystem.Instance.GetPriceModifier(currentPlayer, isShadyShop: false);
        if (alignMod != 1.0f)
        {
            var (alignText, alignColor) = AlignmentSystem.Instance.GetAlignmentDisplay(currentPlayer);
            if (alignMod < 1.0f)
                terminal.WriteLine(Loc.Get("magic_shop.align_discount", alignText, $"{(int)((1.0f - alignMod) * 100)}"));
            else
                terminal.WriteLine(Loc.Get("magic_shop.align_markup", alignText, $"{(int)((alignMod - 1.0f) * 100)}"));
        }

        // World event modifier
        var worldMod = WorldEventSystem.Instance.GlobalPriceModifier;
        if (Math.Abs(worldMod - 1.0f) > 0.01f)
        {
            if (worldMod < 1.0f)
                terminal.WriteLine(Loc.Get("magic_shop.world_discount", $"{(int)((1.0f - worldMod) * 100)}"));
            else
                terminal.WriteLine(Loc.Get("magic_shop.world_markup", $"{(int)((worldMod - 1.0f) * 100)}"));
        }

        // Loyalty discount
        float loyaltyDiscount = GetLoyaltyDiscount(currentPlayer);
        if (loyaltyDiscount < 1.0f)
        {
            int discountPct = (int)((1.0f - loyaltyDiscount) * 100);
            terminal.WriteLine(Loc.Get("magic_shop.loyalty_discount", $"{discountPct}"));
        }
        terminal.WriteLine("");

        // Shopping
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("magic_shop.shopping"));
        WriteSRMenuOption("1", Loc.Get("magic_shop.rings"));
        WriteSRMenuOption("2", Loc.Get("magic_shop.necklaces"));
        WriteSRMenuOption("S", Loc.Get("magic_shop.sell"));
        WriteSRMenuOption("I", Loc.Get("magic_shop.identify"));
        terminal.WriteLine("");

        // Enchanting
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("magic_shop.enchanting"));
        WriteSRMenuOption("E", Loc.Get("magic_shop.enchant"));
        WriteSRMenuOption("W", Loc.Get("magic_shop.remove_enchant"));
        WriteSRMenuOption("C", Loc.Get("magic_shop.curse_removal"));
        terminal.WriteLine("");

        // Potions
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("magic_shop.potions"));
        WriteSRMenuOption("H", Loc.Get("magic_shop.healing_potions"));
        WriteSRMenuOption("M", Loc.Get("magic_shop.mana_potions"));
        terminal.WriteLine("");

        // Arcane Arts
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("magic_shop.arcane"));
        WriteSRMenuOption("V", Loc.Get("magic_shop.love_spells"));
        WriteSRMenuOption("K", Loc.Get("magic_shop.dark_arts"));
        WriteSRMenuOption("Y", Loc.Get("magic_shop.study_spells"));
        WriteSRMenuOption("G", Loc.Get("magic_shop.scrying"));
        terminal.WriteLine("");

        // Other
        WriteSRMenuOption("T", Loc.Get("magic_shop.talk_to", _ownerName));
        WriteSRMenuOption("R", Loc.Get("shop.return"));
        terminal.WriteLine("");
    }

    /// <summary>
    /// Compact BBS display for 80x25 terminals.
    /// </summary>
    private void DisplayLocationBBS()
    {
        terminal.ClearScreen();

        // Header
        ShowBBSHeader(Loc.Get("magic_shop.header"));

        // 1-line description + gold
        terminal.SetColor("gray");
        terminal.Write($" {Loc.Get("magic_shop.run_by", _ownerName)} {Loc.Get("shop.you_have")} ");
        terminal.SetColor("yellow");
        terminal.Write($"{currentPlayer.Gold:N0}");
        terminal.SetColor("gray");
        terminal.WriteLine(" gold.");

        // NPCs
        ShowBBSNPCs();
        terminal.WriteLine("");

        // Menu rows - Shopping
        terminal.SetColor("cyan");
        terminal.WriteLine($" {Loc.Get("magic_shop.bbs_shopping")}");
        ShowBBSMenuRow(("1", "bright_yellow", $" {Loc.Get("magic_shop.rings")}"), ("2", "bright_yellow", $" {Loc.Get("magic_shop.necklaces")}"), ("S", "bright_yellow", Loc.Get("magic_shop.bbs_sell")), ("I", "bright_yellow", Loc.Get("magic_shop.bbs_identify")));

        // Potions & Scrolls
        terminal.SetColor("cyan");
        terminal.WriteLine($" {Loc.Get("magic_shop.bbs_potions_scrolls")}");
        ShowBBSMenuRow(("H", "bright_yellow", Loc.Get("magic_shop.bbs_healing_pots")), ("M", "bright_yellow", Loc.Get("magic_shop.bbs_mana_pots")));

        // Enchanting & Arcane
        terminal.SetColor("cyan");
        terminal.WriteLine($" {Loc.Get("magic_shop.bbs_enchanting_arcane")}");
        ShowBBSMenuRow(("E", "bright_yellow", Loc.Get("magic_shop.bbs_enchant")), ("W", "bright_yellow", Loc.Get("magic_shop.bbs_remove_ench")), ("C", "bright_yellow", Loc.Get("magic_shop.bbs_curse_removal")));
        ShowBBSMenuRow(("V", "bright_yellow", Loc.Get("magic_shop.bbs_love_spells")), ("K", "bright_yellow", Loc.Get("magic_shop.bbs_dark_arts")), ("Y", "bright_yellow", Loc.Get("magic_shop.bbs_study")), ("G", "bright_yellow", Loc.Get("magic_shop.bbs_scry")));

        // Talk & Return
        ShowBBSMenuRow(("T", "bright_yellow", Loc.Get("magic_shop.bbs_talk_to", _ownerName)), ("R", "bright_yellow", Loc.Get("shop.return")));

        // Footer
        ShowBBSFooter();
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
    
    // Legacy static inventory removed in v0.49.2 — replaced by procedural inventory via ShopItemGenerator

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
        adjusted = DifficultySystem.ApplyShopPriceMultiplier(adjusted);
        return Math.Max(1, adjusted);
    }

    private async Task<bool> HandleMagicShopChoice(string choice, Character player)
    {
        ResetDailyTracking();

        // If browsing a category, route to category handler
        if (_currentAccessoryCategory.HasValue)
            return await ProcessAccessoryCategoryChoice(choice, player);

        switch (choice)
        {
            case "1":
                _currentAccessoryCategory = AccessoryCategory.Rings;
                _accessoryPage = 0;
                RequestRedisplay();
                return false;
            case "2":
                _currentAccessoryCategory = AccessoryCategory.Necklaces;
                _accessoryPage = 0;
                RequestRedisplay();
                return false;
            case "S":
                await SellAccessory(player);
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
        WriteBoxHeader(Loc.Get("magic_shop.header"), "bright_cyan");
        DisplayMessage("");
        DisplayMessage(Loc.Get("magic_shop.run_by", _ownerName), "gray");
        DisplayMessage("");

        ShowNPCsInLocation();

        // Shop description
        DisplayMessage(Loc.Get("magic_shop.desc_1"), "gray");
        DisplayMessage(Loc.Get("magic_shop.desc_2"), "gray");
        DisplayMessage(Loc.Get("magic_shop.desc_3"), "gray");
        DisplayMessage(Loc.Get("magic_shop.desc_4"), "gray");
        DisplayMessage("");

        // Greeting
        string raceGreeting = GetRaceGreeting(player.Race);
        DisplayMessage(Loc.Get("magic_shop.greeting", raceGreeting), "cyan");
        DisplayMessage("");

        // Player gold display
        DisplayMessage(Loc.Get("magic_shop.you_have_gold", $"{player.Gold:N0}"), "gray");

        // Show alignment price modifier
        var alignmentModifier = AlignmentSystem.Instance.GetPriceModifier(player, isShadyShop: false);
        if (alignmentModifier != 1.0f)
        {
            var (alignText, alignColor) = AlignmentSystem.Instance.GetAlignmentDisplay(player);
            if (alignmentModifier < 1.0f)
                DisplayMessage($"  {Loc.Get("magic_shop.visual_align_discount", alignText, $"{(int)((1.0f - alignmentModifier) * 100)}")}", alignColor);
            else
                DisplayMessage($"  {Loc.Get("magic_shop.visual_align_markup", alignText, $"{(int)((alignmentModifier - 1.0f) * 100)}")}", alignColor);
        }

        // Show world event price modifier
        var worldEventModifier = WorldEventSystem.Instance.GlobalPriceModifier;
        if (Math.Abs(worldEventModifier - 1.0f) > 0.01f)
        {
            if (worldEventModifier < 1.0f)
                DisplayMessage($"  {Loc.Get("magic_shop.world_discount", $"{(int)((1.0f - worldEventModifier) * 100)}")}", "bright_green");
            else
                DisplayMessage($"  {Loc.Get("magic_shop.world_markup", $"{(int)((worldEventModifier - 1.0f) * 100)}")}", "red");
        }
        DisplayMessage("");

        // Loyalty discount display
        float loyaltyDiscount = GetLoyaltyDiscount(player);
        if (loyaltyDiscount < 1.0f)
        {
            int discountPct = (int)((1.0f - loyaltyDiscount) * 100);
            DisplayMessage($"  {Loc.Get("magic_shop.visual_loyalty", $"{discountPct}")}", "bright_green");
        }
        DisplayMessage("");

        // Menu options - two-column layout
        DisplayMessage("  ═══ Shopping ═══                      ═══ Enchanting ═══", "cyan");
        terminal.WriteLine("");
        WriteMenuRow("1", "bright_yellow", " Rings", "E", "bright_yellow", "nchant Equipment");
        WriteMenuRow("2", "bright_yellow", " Necklaces", "W", "bright_yellow", " Remove Enchantment");
        WriteMenuRow("S", "bright_yellow", "ell Accessories", "C", "bright_yellow", "urse Removal");
        terminal.Write("  ");
        WriteMenuKey("I", "bright_yellow", "dentify Item");
        terminal.WriteLine("");
        terminal.WriteLine("");
        DisplayMessage("  ═══ Potions & Scrolls ═══             ═══ Arcane Arts ═══", "cyan");
        terminal.WriteLine("");
        WriteMenuRow("H", "bright_yellow", "ealing Potions", "V", "bright_yellow", " Love Spells");
        WriteMenuRow("M", "bright_yellow", "ana Potions", "K", "bright_yellow", " Dark Arts");
        WriteMenuRow("Y", "bright_yellow", " Study Spells", "G", "bright_yellow", " Scrying (NPC Info)");
        terminal.WriteLine("");
        terminal.WriteLine("");
        WriteMenuRow("T", "bright_yellow", $"alk to {_ownerName}", "R", "bright_yellow", "eturn to street");
        terminal.WriteLine("");
        terminal.WriteLine("");

        ShowStatusLine();
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
    
    
    private void IdentifyItem(Character player)
    {
        DisplayMessage("");

        var unidentifiedItems = player.Inventory.Where(item => !item.IsIdentified).ToList();
        if (unidentifiedItems.Count == 0)
        {
            DisplayMessage(Loc.Get("magic_shop.no_unidentified"), "gray");
            return;
        }

        long identifyCost = GetIdentificationCost(player.Level);

        WriteSectionHeader(Loc.Get("magic_shop.unidentified_items", unidentifiedItems.Count.ToString()), "cyan");
        DisplayMessage(Loc.Get("magic_shop.identify_cost", $"{identifyCost:N0}", $"{player.Gold:N0}"), "gray");
        DisplayMessage("");

        for (int i = 0; i < unidentifiedItems.Count; i++)
        {
            string unidName = LootGenerator.GetUnidentifiedName(unidentifiedItems[i]);
            DisplayMessage($"  {i + 1}. {unidName}", "magenta");
        }

        DisplayMessage("");
        DisplayMessage(Loc.Get("magic_shop.identify_prompt"), "yellow", false);
        string input = terminal.GetInputSync("");

        if (int.TryParse(input, out int itemIndex) && itemIndex > 0 && itemIndex <= unidentifiedItems.Count)
        {
            var (idKingTax, idCityTax, idTotalWithTax) = CityControlSystem.CalculateTaxedPrice(identifyCost);

            if (player.Gold < idTotalWithTax)
            {
                DisplayMessage(Loc.Get("ui.not_enough_gold_identify"), "red");
                return;
            }

            var item = unidentifiedItems[itemIndex - 1];
            string unidName = LootGenerator.GetUnidentifiedName(item);
            CityControlSystem.Instance.DisplayTaxBreakdown(terminal, Loc.Get("magic_shop.identification_label"), identifyCost);
            DisplayMessage(Loc.Get("magic_shop.identify_confirm", unidName, $"{idTotalWithTax:N0}"), "yellow", false);
            var confirm = terminal.GetInputSync("").ToUpper();
            DisplayMessage("");

            if (confirm == "Y")
            {
                player.Gold -= idTotalWithTax;
                CityControlSystem.Instance.ProcessSaleTax(identifyCost);
                item.IsIdentified = true;

                DisplayMessage(Loc.Get("magic_shop.identify_ritual", _ownerName), "gray");
                DisplayMessage("");
                DisplayMessage(Loc.Get("magic_shop.identify_result", item.Name), "bright_green");
                DisplayMessage("");

                // Show full item details
                DisplayItemDetails(item);
            }
        }
    }
    
    private void DisplayItemDetails(Item item)
    {
        WriteSectionHeader(Loc.Get("magic_shop.item_properties"), "cyan");
        DisplayMessage($"{Loc.Get("ui.name_label")}: {item.Name}", "white");
        DisplayMessage($"{Loc.Get("ui.value_label")}: {item.Value:N0} {Loc.Get("shop.gold_crowns")}", "yellow");
        
        if (item.Strength != 0) DisplayMessage($"{Loc.Get("ui.stat_strength")}: {(item.Strength > 0 ? "+" : "")}{item.Strength}", "green");
        if (item.Defence != 0) DisplayMessage($"{Loc.Get("ui.stat_defense")}: {(item.Defence > 0 ? "+" : "")}{item.Defence}", "green");
        if (item.Attack != 0) DisplayMessage($"{Loc.Get("ui.stat_attack")}: {(item.Attack > 0 ? "+" : "")}{item.Attack}", "green");
        if (item.Dexterity != 0) DisplayMessage($"{Loc.Get("ui.stat_dexterity")}: {(item.Dexterity > 0 ? "+" : "")}{item.Dexterity}", "green");
        if (item.Wisdom != 0) DisplayMessage($"{Loc.Get("ui.stat_wisdom")}: {(item.Wisdom > 0 ? "+" : "")}{item.Wisdom}", "green");
        if (item.MagicProperties.Mana != 0) DisplayMessage($"{Loc.Get("ui.stat_mana")}: {(item.MagicProperties.Mana > 0 ? "+" : "")}{item.MagicProperties.Mana}", "blue");
        
        if (item.StrengthRequired > 0) DisplayMessage($"{Loc.Get("ui.str_required")}: {item.StrengthRequired}", "red");
        
        // Disease curing
        if (item.MagicProperties.DiseaseImmunity != CureType.None)
        {
            string cureText = item.MagicProperties.DiseaseImmunity switch
            {
                CureType.All => Loc.Get("magic_shop.cures_all"),
                CureType.Blindness => Loc.Get("magic_shop.cures_blindness"),
                CureType.Plague => Loc.Get("magic_shop.cures_plague"),
                CureType.Smallpox => Loc.Get("magic_shop.cures_smallpox"),
                CureType.Measles => Loc.Get("magic_shop.cures_measles"),
                CureType.Leprosy => Loc.Get("magic_shop.cures_leprosy"),
                _ => ""
            };
            DisplayMessage(cureText, "green");
        }
        
        // Restrictions
        if (item.OnlyForGood) DisplayMessage(Loc.Get("shop.alignment_good_only"), "blue");
        if (item.OnlyForEvil) DisplayMessage(Loc.Get("shop.alignment_evil_only"), "red");
        if (item.IsCursed) DisplayMessage(Loc.Get("shop.cursed_warning", item.Name), "darkred");
    }
    
    private void BuyHealingPotions(Character player)
    {
        DisplayMessage("");

        // Calculate potion price with all modifiers (alignment, world events, faction, city, loyalty)
        long potionPrice = ApplyAllPriceModifiers(player.Level * GameConfig.HealingPotionLevelMultiplier, player);
        var (_, _, hpPotionUnitWithTax) = CityControlSystem.CalculateTaxedPrice(potionPrice);
        int maxPotionsCanBuy = hpPotionUnitWithTax > 0 ? (int)(player.Gold / hpPotionUnitWithTax) : 0;
        int maxPotionsCanCarry = player.MaxPotions - (int)player.Healing;
        int maxPotions = Math.Min(maxPotionsCanBuy, maxPotionsCanCarry);

        if (player.Gold < hpPotionUnitWithTax)
        {
            DisplayMessage(Loc.Get("ui.not_enough_gold"), "red");
            return;
        }

        if (player.Healing >= GameConfig.MaxHealingPotions)
        {
            DisplayMessage(Loc.Get("shop.max_potions"), "red");
            return;
        }

        if (maxPotions <= 0)
        {
            DisplayMessage(Loc.Get("shop.cant_afford_potions"), "red");
            return;
        }

        DisplayMessage(Loc.Get("magic_shop.potion_price", $"{potionPrice:N0}"), "gray");
        DisplayMessage(Loc.Get("magic_shop.potion_gold", $"{player.Gold:N0}"), "gray");
        DisplayMessage(Loc.Get("magic_shop.potion_current", $"{player.Healing}", $"{player.MaxPotions}"), "gray");
        DisplayMessage("");

        DisplayMessage(Loc.Get("magic_shop.potion_how_many", $"{maxPotions}"), "yellow", false);
        string input = terminal.GetInputSync("");

        if (int.TryParse(input, out int quantity) && quantity > 0 && quantity <= maxPotions)
        {
            long totalCost = quantity * potionPrice;
            var (hpKingTax, hpCityTax, hpTotalWithTax) = CityControlSystem.CalculateTaxedPrice(totalCost);

            if (player.Gold >= hpTotalWithTax)
            {
                CityControlSystem.Instance.DisplayTaxBreakdown(terminal, Loc.Get("magic_shop.healing_potions"), totalCost);
                player.Gold -= hpTotalWithTax;
                player.Healing += quantity;

                // Process city tax share from this sale
                CityControlSystem.Instance.ProcessSaleTax(totalCost);

                DisplayMessage(Loc.Get("magic_shop.potion_deal", "Ravanella", $"{quantity}"), "green");
                DisplayMessage(Loc.Get("magic_shop.potion_total", $"{hpTotalWithTax:N0}"), "gray");

                player.Statistics?.RecordGoldSpent(hpTotalWithTax);
                player.Statistics?.RecordMagicShopPurchase(hpTotalWithTax);
            }
            else
            {
                DisplayMessage(Loc.Get("magic_shop.potion_no_gold", _ownerName), "red");
            }
        }
        else
        {
            DisplayMessage(Loc.Get("ui.cancelled"), "red");
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
        WriteSectionHeader(Loc.Get("magic_shop.curse_removal"), "magenta");
        DisplayMessage("");
        DisplayMessage(Loc.Get("magic_shop.curse_intro_1", _ownerName), "gray");
        DisplayMessage(Loc.Get("magic_shop.curse_intro_2"), "cyan");
        DisplayMessage(Loc.Get("magic_shop.curse_intro_3"), "cyan");
        DisplayMessage("");

        // Find cursed items in player inventory
        var cursedItems = player.Inventory.Where(i => i.IsCursed).ToList();

        // Find cursed equipment on companions and NPC teammates
        var cursedTeamGear = new List<(string ownerName, EquipmentSlot slot, Equipment equip)>();

        // Check active companions
        if (CompanionSystem.Instance != null)
        {
            foreach (var companion in CompanionSystem.Instance.GetActiveCompanions())
            {
                foreach (var kvp in companion.EquippedItems)
                {
                    var equip = EquipmentDatabase.GetById(kvp.Value);
                    if (equip != null && equip.IsCursed)
                        cursedTeamGear.Add((companion.Name, kvp.Key, equip));
                }
            }
        }

        // Check NPC teammates (recruited NPCs in player's dungeon party)
        if (NPCSpawnSystem.Instance != null)
        {
            foreach (var npc in NPCSpawnSystem.Instance.ActiveNPCs)
            {
                if (!npc.IsDead && !string.IsNullOrEmpty(npc.Team) &&
                    !string.IsNullOrEmpty(currentPlayer.Team) && npc.Team == currentPlayer.Team)
                {
                    foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
                    {
                        if (slot == EquipmentSlot.None) continue;
                        var equip = npc.GetEquipment(slot);
                        if (equip != null && equip.IsCursed)
                            cursedTeamGear.Add((npc.DisplayName, slot, equip));
                    }
                }
            }
        }

        if (cursedItems.Count == 0 && cursedTeamGear.Count == 0)
        {
            DisplayMessage(Loc.Get("magic_shop.no_cursed_items"), "gray");
            DisplayMessage(Loc.Get("magic_shop.curse_fortunate"), "cyan");
            return;
        }

        int totalEntries = cursedItems.Count + cursedTeamGear.Count;

        // List player's cursed items
        if (cursedItems.Count > 0)
        {
            DisplayMessage(Loc.Get("magic_shop.cursed_items_list"), "darkred");
            for (int i = 0; i < cursedItems.Count; i++)
            {
                var item = cursedItems[i];
                long removalCost = CalculateCurseRemovalCost(item, player);
                DisplayMessage(Loc.Get("magic_shop.cursed_item_entry", $"{i + 1}", item.Name, $"{removalCost:N0}"), "red");
                DisplayCurseDetails(item);
            }
        }

        // List team members' cursed equipment
        if (cursedTeamGear.Count > 0)
        {
            DisplayMessage("");
            DisplayMessage("Cursed gear found on your team members:", "darkred");
            for (int i = 0; i < cursedTeamGear.Count; i++)
            {
                var (ownerName, slot, equip) = cursedTeamGear[i];
                long removalCost = CalculateEquipmentCurseRemovalCost(equip);
                int displayNum = cursedItems.Count + i + 1;
                DisplayMessage($"  {displayNum}. {equip.Name} ({ownerName}'s {slot.GetDisplayName()}) — {removalCost:N0} gold", "red");
                DisplayEquipmentCurseDetails(equip);
            }
        }

        DisplayMessage("");
        var input = await terminal.GetInput(Loc.Get("magic_shop.uncurse_prompt"));

        if (!int.TryParse(input, out int itemIndex) || itemIndex <= 0 || itemIndex > totalEntries)
            return;

        // Determine if this is a player item or team gear
        if (itemIndex <= cursedItems.Count)
        {
            // Player item — existing logic
            var targetItem = cursedItems[itemIndex - 1];
            await RemoveCurseFromPlayerItem(player, targetItem);
        }
        else
        {
            // Team member equipment
            int teamIdx = itemIndex - cursedItems.Count - 1;
            var (ownerName, slot, targetEquip) = cursedTeamGear[teamIdx];
            await RemoveCurseFromTeamEquipment(player, ownerName, slot, targetEquip);
        }
    }

    private async Task RemoveCurseFromPlayerItem(Character player, Item targetItem)
    {
        long cost = CalculateCurseRemovalCost(targetItem, player);
        var (curseKingTax, curseCityTax, curseTotalWithTax) = CityControlSystem.CalculateTaxedPrice(cost);

        if (player.Gold < curseTotalWithTax)
        {
            DisplayMessage("");
            DisplayMessage(Loc.Get("magic_shop.curse_no_gold"), "cyan");
            DisplayMessage(Loc.Get("magic_shop.curse_remains"), "red");
            return;
        }

        DisplayMessage("");
        CityControlSystem.Instance.DisplayTaxBreakdown(terminal, Loc.Get("magic_shop.curse_removal"), cost);
        var confirm = await terminal.GetInput(Loc.Get("magic_shop.curse_confirm", targetItem.Name, $"{curseTotalWithTax:N0}"));

        if (confirm.ToUpper() == "Y")
        {
            player.Gold -= curseTotalWithTax;
            CityControlSystem.Instance.ProcessSaleTax(cost);

            // Dramatic curse removal scene
            DisplayMessage("");
            DisplayMessage(Loc.Get("magic_shop.curse_scene_1", _ownerName, targetItem.Name), "gray");
            DisplayMessage(Loc.Get("magic_shop.curse_scene_2"), "gray");
            await Task.Delay(500);
            DisplayMessage(Loc.Get("magic_shop.curse_scene_3"), "magenta");
            await Task.Delay(500);
            DisplayMessage(Loc.Get("magic_shop.curse_scene_4"), "white");
            await Task.Delay(300);
            DisplayMessage(Loc.Get("magic_shop.curse_scene_5"), "dark_yellow");
            DisplayMessage("");

            // Remove the curse
            targetItem.IsCursed = false;
            targetItem.Cursed = false;

            // Reverse curse stat penalties (mirrors LootGenerator.ApplyCursePenalties)
            int penalty = Math.Max(5, targetItem.Attack / 10);
            targetItem.Strength += penalty / 2;
            targetItem.Dexterity += penalty / 3;
            targetItem.Wisdom += penalty / 3;
            // Remove all negative stat LootEffects from curse (ApplyCursePenalties adds negative CON)
            targetItem.LootEffects.RemoveAll(e => e.Item2 < 0 && (
                e.Item1 == (int)LootGenerator.SpecialEffect.Constitution ||
                e.Item1 == (int)LootGenerator.SpecialEffect.Intelligence ||
                e.Item1 == (int)LootGenerator.SpecialEffect.AllStats));

            // Purification penalty: the curse's dark power was intertwined with the item's strength.
            // Removing the curse costs ~20% of the item's base power ON TOP of removing the curse boost.
            // This makes equipping a cursed item a meaningful gamble:
            //   Equip cursed:  125% power + stat penalties + can't unequip  (high risk, high reward)
            //   Purify:         80% power, clean stats                     (safe but weaker)
            //   Never cursed:  100% power                                  (baseline)
            const float purificationPenalty = 0.80f; // 20% power loss from purification

            if (targetItem.Type == ObjType.Weapon)
                targetItem.Attack = (int)(targetItem.Attack / 1.25f * purificationPenalty);
            else if (targetItem.Type == ObjType.Body || targetItem.Type == ObjType.Shield)
                targetItem.Armor = (int)(targetItem.Armor / 1.25f * purificationPenalty);
            else if (targetItem.Type == ObjType.Fingers || targetItem.Type == ObjType.Neck)
            {
                // Accessories boosted Strength and HP by 1.3x before penalties were applied
                targetItem.Strength = (int)(targetItem.Strength / 1.3f * purificationPenalty);
                targetItem.HP = (int)(targetItem.HP / 1.3f * purificationPenalty);
            }

            // Value partially restored (curse halved it, purification recovers most but not all)
            targetItem.Value = (long)(targetItem.Value * 1.6); // 80% of original value (was halved, now x1.6)

            // Clean up curse-related name prefix, add "Purified" tag
            if (targetItem.Name.StartsWith("Cursed "))
                targetItem.Name = "Purified " + targetItem.Name.Substring(7);

            // Fix curse description
            if (targetItem.Description != null && targetItem.Description.Count > 1 &&
                targetItem.Description[1] != null && targetItem.Description[1].Contains("CURSED"))
                targetItem.Description[1] = "Purified — some power was lost in the cleansing.";

            // Fix any negative magic resistance
            if (targetItem.MagicProperties.MagicResistance < 0)
                targetItem.MagicProperties.MagicResistance = Math.Abs(targetItem.MagicProperties.MagicResistance) / 2;

            DisplayMessage(Loc.Get("magic_shop.curse_success", targetItem.Name), "bright_green");
            DisplayMessage(Loc.Get("magic_shop.curse_aftermath_1"), "cyan");
            DisplayMessage(Loc.Get("magic_shop.curse_aftermath_2"), "cyan");
            DisplayMessage(Loc.Get("magic_shop.curse_aftermath_3"), "cyan");

            player.Statistics?.RecordGoldSpent(curseTotalWithTax);
            player.Statistics?.RecordMagicShopPurchase(curseTotalWithTax);

            // Special Ocean lore for certain items
            if (targetItem.Name.Contains("Drowned") || targetItem.Name.Contains("Ocean") || targetItem.Name.Contains("Deep"))
            {
                DisplayMessage("");
                DisplayMessage(Loc.Get("shop.ocean_flavor"), "magenta");
                DisplayMessage("'Perhaps it was not cursed, but merely... homesick.'", "magenta");
            }
        }
    }

    private async Task RemoveCurseFromTeamEquipment(Character player, string ownerName, EquipmentSlot slot, Equipment targetEquip)
    {
        long cost = CalculateEquipmentCurseRemovalCost(targetEquip);
        var (curseKingTax, curseCityTax, curseTotalWithTax) = CityControlSystem.CalculateTaxedPrice(cost);

        if (player.Gold < curseTotalWithTax)
        {
            DisplayMessage("");
            DisplayMessage(Loc.Get("magic_shop.curse_no_gold"), "cyan");
            DisplayMessage(Loc.Get("magic_shop.curse_remains"), "red");
            return;
        }

        DisplayMessage("");
        CityControlSystem.Instance.DisplayTaxBreakdown(terminal, Loc.Get("magic_shop.curse_removal"), cost);
        var confirm = await terminal.GetInput($"Remove curse from {ownerName}'s {targetEquip.Name} for {curseTotalWithTax:N0} gold? (Y/N) ");

        if (confirm.ToUpper() == "Y")
        {
            player.Gold -= curseTotalWithTax;
            CityControlSystem.Instance.ProcessSaleTax(cost);

            // Dramatic curse removal scene
            DisplayMessage("");
            DisplayMessage($"{_ownerName} places {ownerName}'s {targetEquip.Name} on the altar...", "gray");
            DisplayMessage(Loc.Get("magic_shop.curse_scene_2"), "gray");
            await Task.Delay(500);
            DisplayMessage(Loc.Get("magic_shop.curse_scene_3"), "magenta");
            await Task.Delay(500);
            DisplayMessage(Loc.Get("magic_shop.curse_scene_4"), "white");
            await Task.Delay(300);
            DisplayMessage(Loc.Get("magic_shop.curse_scene_5"), "dark_yellow");
            DisplayMessage("");

            // Remove the curse from equipment
            targetEquip.IsCursed = false;

            // Reverse curse stat penalties (adapted for Equipment properties)
            int penalty = Math.Max(5, Math.Max(targetEquip.WeaponPower, targetEquip.ArmorClass) / 10);
            targetEquip.StrengthBonus += penalty / 2;
            targetEquip.DexterityBonus += penalty / 3;
            targetEquip.WisdomBonus += penalty / 3;
            targetEquip.MaxHPBonus += penalty * 2;

            // Purification penalty: 20% power loss
            const float purificationPenalty = 0.80f;

            if (targetEquip.WeaponPower > 0)
                targetEquip.WeaponPower = (int)(targetEquip.WeaponPower / 1.25f * purificationPenalty);
            if (targetEquip.ArmorClass > 0)
                targetEquip.ArmorClass = (int)(targetEquip.ArmorClass / 1.25f * purificationPenalty);
            if (targetEquip.ShieldBonus > 0)
                targetEquip.ShieldBonus = (int)(targetEquip.ShieldBonus / 1.25f * purificationPenalty);

            // Value partially restored
            targetEquip.Value = (long)(targetEquip.Value * 1.6);

            // Clean up name
            if (targetEquip.Name.StartsWith("Cursed "))
                targetEquip.Name = "Purified " + targetEquip.Name.Substring(7);

            // Fix any negative magic resistance
            if (targetEquip.MagicResistance < 0)
                targetEquip.MagicResistance = Math.Abs(targetEquip.MagicResistance) / 2;

            DisplayMessage($"The curse on {ownerName}'s {targetEquip.Name} has been lifted!", "bright_green");
            DisplayMessage(Loc.Get("magic_shop.curse_aftermath_1"), "cyan");
            DisplayMessage(Loc.Get("magic_shop.curse_aftermath_2"), "cyan");
            DisplayMessage($"{ownerName} looks visibly relieved.", "cyan");

            player.Statistics?.RecordGoldSpent(curseTotalWithTax);
            player.Statistics?.RecordMagicShopPurchase(curseTotalWithTax);
        }
    }

    private long CalculateEquipmentCurseRemovalCost(Equipment equip)
    {
        long baseCost = equip.Value * 2;
        int cursePower = 0;
        if (equip.StrengthBonus < 0) cursePower += Math.Abs(equip.StrengthBonus) * 100;
        if (equip.DefenceBonus < 0) cursePower += Math.Abs(equip.DefenceBonus) * 100;
        if (equip.DexterityBonus < 0) cursePower += Math.Abs(equip.DexterityBonus) * 100;
        if (equip.WisdomBonus < 0) cursePower += Math.Abs(equip.WisdomBonus) * 100;
        baseCost += cursePower;
        return Math.Max(500, baseCost);
    }

    private void DisplayEquipmentCurseDetails(Equipment equip)
    {
        var negatives = new List<string>();
        if (equip.StrengthBonus < 0) negatives.Add($"Str{equip.StrengthBonus}");
        if (equip.DefenceBonus < 0) negatives.Add($"Def{equip.DefenceBonus}");
        if (equip.DexterityBonus < 0) negatives.Add($"Dex{equip.DexterityBonus}");
        if (equip.WisdomBonus < 0) negatives.Add($"Wis{equip.WisdomBonus}");
        if (negatives.Count > 0)
            DisplayMessage($"     Curse effect: {string.Join(", ", negatives)}", "darkred");
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
        WriteSectionHeader(Loc.Get("magic_shop.enchant_bless"), "magenta");
        DisplayMessage("");
        DisplayMessage($"{_ownerName} waves a gnarled hand over a collection of glowing runes.", "gray");
        DisplayMessage(Loc.Get("magic_shop.old_enchant_intro"), "cyan");
        DisplayMessage("");

        DisplayMessage(Loc.Get("magic_shop.old_enchant_1"), "gray");
        DisplayMessage(Loc.Get("magic_shop.old_enchant_2"), "gray");
        DisplayMessage(Loc.Get("magic_shop.old_enchant_3"), "gray");
        DisplayMessage(Loc.Get("magic_shop.old_enchant_4"), "blue");
        DisplayMessage(Loc.Get("magic_shop.old_enchant_5"), "cyan");
        DisplayMessage(Loc.Get("magic_shop.old_enchant_6"), "yellow");
        DisplayMessage("");

        var input = await terminal.GetInput(Loc.Get("magic_shop.old_enchant_prompt"));
        if (!int.TryParse(input, out int enchantChoice) || enchantChoice <= 0 || enchantChoice > 6)
            return;

        long[] costs = { 0, 2000, 5000, 12000, 25000, 15000, 8000 };
        long cost = costs[enchantChoice];
        var (enchKingTax, enchCityTax, enchTotalWithTax) = CityControlSystem.CalculateTaxedPrice(cost);

        if (player.Gold < enchTotalWithTax)
        {
            DisplayMessage("");
            DisplayMessage("'The magical arts require material compensation,' the gnome says pointedly.", "cyan");
            DisplayMessage(Loc.Get("shop.insufficient_gold_enchant", $"{enchTotalWithTax:N0}"), "red");
            await terminal.WaitForKey();
            return;
        }

        // Show items that can be enchanted
        var enchantableItems = player.Inventory.Where(i =>
            i.Type == ObjType.Magic || i.MagicType != MagicItemType.None).ToList();

        if (enchantableItems.Count == 0)
        {
            DisplayMessage("");
            DisplayMessage(Loc.Get("magic_shop.old_no_enchantable"), "gray");
            DisplayMessage(Loc.Get("magic_shop.old_bring_items"), "cyan");
            await terminal.WaitForKey();
            return;
        }

        DisplayMessage("");
        DisplayMessage(Loc.Get("magic_shop.old_enchantable_items"), "cyan");
        for (int i = 0; i < enchantableItems.Count; i++)
        {
            var item = enchantableItems[i];
            string status = item.IsCursed ? Loc.Get("shop.cursed_no_enchant") : "";
            DisplayMessage($"{i + 1}. {item.Name}{status}", item.IsCursed ? "red" : "white");
        }

        DisplayMessage("");
        input = await terminal.GetInput(Loc.Get("magic_shop.old_choose_item"));

        if (!int.TryParse(input, out int itemIndex) || itemIndex <= 0 || itemIndex > enchantableItems.Count)
            return;

        var targetItem = enchantableItems[itemIndex - 1];

        if (targetItem.IsCursed)
        {
            DisplayMessage("");
            DisplayMessage(Loc.Get("magic_shop.old_cursed_enchant"), "red");
            DisplayMessage(Loc.Get("magic_shop.old_remove_first"), "cyan");
            await terminal.WaitForKey();
            return;
        }

        // For stat-boosting enchants, let player choose the stat
        int statChoice = 0;
        if (enchantChoice >= 1 && enchantChoice <= 3)
        {
            DisplayMessage("");
            DisplayMessage(Loc.Get("magic_shop.old_choose_stat"), "cyan");
            DisplayMessage(Loc.Get("magic_shop.old_stat_options"), "gray");
            input = await terminal.GetInput(Loc.Get("ui.choice"));
            if (!int.TryParse(input, out statChoice) || statChoice <= 0 || statChoice > 5)
                return;
        }

        DisplayMessage("");
        CityControlSystem.Instance.DisplayTaxBreakdown(terminal, "Enchantment", cost);
        var confirm = await terminal.GetInput(Loc.Get("magic_shop.old_enchant_confirm", targetItem.Name, $"{enchTotalWithTax:N0}"));

        if (confirm.ToUpper() != "Y")
            return;

        player.Gold -= enchTotalWithTax;
        CityControlSystem.Instance.ProcessSaleTax(cost);

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
        DisplayMessage(Loc.Get("magic_shop.old_enchant_complete"), "bright_green");

        player.Statistics?.RecordGoldSpent(enchTotalWithTax);
        player.Statistics?.RecordMagicShopPurchase(enchTotalWithTax);
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
        // New tiers (v0.30.9)
        ("Mythic",          24, 180000L, 2000, 55, "+24 to one stat"),
        ("Legendary",       30, 300000L, 3000, 65, "+30 to one stat"),
        ("Godforged",       38, 500000L, 5000, 75, "+38 to one stat"),
        ("Phoenix Fire",    20, 400000L, 4000, 60, "+20 power + fire damage on hit"),
        ("Frostbite",       20, 400000L, 4000, 60, "+20 power + chance to slow enemies"),
    };

    // Material requirements for high-tier enchantments (0-indexed tier → required materials)
    private static readonly Dictionary<int, (string materialId, int count)[]> EnchantMaterialRequirements = new()
    {
        [9]  = new[] { ("fading_starlight_dust", 1) },                                         // Mythic
        [10] = new[] { ("heart_of_the_ocean", 1), ("shadow_silk_thread", 1) },                 // Legendary
        [11] = new[] { ("eye_of_manwe", 1), ("terravoks_heartstone", 1) },                     // Godforged
        [12] = new[] { ("crimson_war_shard", 1), ("fading_starlight_dust", 1) },               // Phoenix Fire
        [13] = new[] { ("shadow_silk_thread", 1), ("fading_starlight_dust", 1) },              // Frostbite
    };

    private static readonly string[] StatNames = { "Weapon Power", "Strength", "Dexterity", "Defence", "Wisdom", "Armor Power" };

    private async Task EnchantEquipment(Character player)
    {
        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("magic_shop.enchant_header"), "magenta");
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.Write($"  {Loc.Get("magic_shop.enchant_examine", _ownerName)}");
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write($"  {Loc.Get("magic_shop.enchant_offer")}");
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
        terminal.WriteLine(Loc.Get("magic_shop.enchant_col_header"));
        WriteDivider(72);

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
                string enchTag = equip.GetEnchantmentCount() > 0 ? $" [E:{equip.GetEnchantmentCount()}/{GameConfig.MaxEnchantments}]" : "";
                string cursedTag = equip.IsCursed ? Loc.Get("shop.cursed_tag") : "";
                string displayName = equip.Name + enchTag + cursedTag;
                if (displayName.Length > 34) displayName = displayName.Substring(0, 31) + "...";

                if (equip.IsCursed)
                    terminal.SetColor("red");
                else if (equip.GetEnchantmentCount() >= GameConfig.MaxEnchantments)
                    terminal.SetColor("darkgray");
                else
                    terminal.SetColor(equip.GetRarityColor());
                terminal.Write($"{displayName,-35}");

                // Stats inline
                var stats = new List<string>();
                if (equip.WeaponPower > 0) stats.Add($"{Loc.Get("ui.stat_pow")}:{equip.WeaponPower}");
                if (equip.ArmorClass > 0) stats.Add($"{Loc.Get("ui.stat_ac")}:{equip.ArmorClass}");
                if (equip.StrengthBonus != 0) stats.Add($"{Loc.Get("ui.stat_str")}{(equip.StrengthBonus > 0 ? "+" : "")}{equip.StrengthBonus}");
                if (equip.DexterityBonus != 0) stats.Add($"{Loc.Get("ui.stat_dex")}{(equip.DexterityBonus > 0 ? "+" : "")}{equip.DexterityBonus}");
                if (equip.AgilityBonus != 0) stats.Add($"{Loc.Get("ui.stat_agi")}{(equip.AgilityBonus > 0 ? "+" : "")}{equip.AgilityBonus}");
                if (equip.ConstitutionBonus != 0) stats.Add($"{Loc.Get("ui.stat_con")}{(equip.ConstitutionBonus > 0 ? "+" : "")}{equip.ConstitutionBonus}");
                if (equip.IntelligenceBonus != 0) stats.Add($"{Loc.Get("ui.stat_int")}{(equip.IntelligenceBonus > 0 ? "+" : "")}{equip.IntelligenceBonus}");
                if (equip.WisdomBonus != 0) stats.Add($"{Loc.Get("ui.stat_wis")}{(equip.WisdomBonus > 0 ? "+" : "")}{equip.WisdomBonus}");
                if (equip.CharismaBonus != 0) stats.Add($"{Loc.Get("ui.stat_cha")}{(equip.CharismaBonus > 0 ? "+" : "")}{equip.CharismaBonus}");
                if (equip.DefenceBonus != 0) stats.Add($"{Loc.Get("ui.stat_def")}{(equip.DefenceBonus > 0 ? "+" : "")}{equip.DefenceBonus}");
                if (equip.MaxHPBonus != 0) stats.Add($"{Loc.Get("ui.stat_hp")}{(equip.MaxHPBonus > 0 ? "+" : "")}{equip.MaxHPBonus}");
                if (equip.MaxManaBonus != 0) stats.Add($"{Loc.Get("ui.stat_mana")}{(equip.MaxManaBonus > 0 ? "+" : "")}{equip.MaxManaBonus}");
                if (equip.StaminaBonus != 0) stats.Add($"{Loc.Get("ui.stat_sta")}{(equip.StaminaBonus > 0 ? "+" : "")}{equip.StaminaBonus}");
                terminal.SetColor("green");
                terminal.Write(string.Join(" ", stats));

                terminal.WriteLine("");
                idx++;
            }
        }

        if (equippedItems.Count == 0)
        {
            terminal.WriteLine("");
            DisplayMessage($"  {Loc.Get("magic_shop.no_equipment_enchant")}", "gray");
            DisplayMessage($"  {Loc.Get("magic_shop.come_back_armed")}", "cyan");
            await terminal.WaitForKey();
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(IsScreenReader ? "  0. Cancel" : "  [0] Cancel");
        terminal.WriteLine("");
        var slotInput = await terminal.GetInput($"  {Loc.Get("magic_shop.select_item_enchant")}");
        if (!int.TryParse(slotInput, out int slotChoice) || slotChoice < 1 || slotChoice > equippedItems.Count)
            return;

        var (selectedSlot, selectedEquip) = equippedItems[slotChoice - 1];

        if (selectedEquip.IsCursed)
        {
            DisplayMessage("");
            DisplayMessage(Loc.Get("magic_shop.cursed_no_enchant"), "red");
            DisplayMessage(Loc.Get("magic_shop.remove_curse_first"), "cyan");
            await terminal.WaitForKey();
            return;
        }

        if (selectedEquip.GetEnchantmentCount() >= GameConfig.MaxEnchantments)
        {
            DisplayMessage("");
            DisplayMessage(Loc.Get("shop.max_enchantments", GameConfig.MaxEnchantments), "red");
            DisplayMessage(Loc.Get("magic_shop.beyond_limits"), "cyan");
            await terminal.WaitForKey();
            return;
        }

        // Show enchantment options
        terminal.ClearScreen();
        WriteBoxHeader($"Enchanting: {selectedEquip.Name}", "magenta");
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine($"  {Loc.Get("magic_shop.you_have_gold", $"{player.Gold:N0}")}");
        terminal.WriteLine("");

        // Section: Stat enchants (tiers 1-4)
        WriteSectionHeader(Loc.Get("magic_shop.stat_enchant"), "white");
        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("magic_shop.enchant_tier_header"));
        WriteDivider(60);

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
        WriteSectionHeader(Loc.Get("magic_shop.special_enchant"), "white");
        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("magic_shop.enchant_tier_header"));
        WriteDivider(60);

        for (int i = 4; i < 9; i++)
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

        // Section: High-tier and Elemental enchants (tiers 10-14)
        terminal.WriteLine("");
        WriteSectionHeader(Loc.Get("magic_shop.mythic_enchant"), "white");
        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("magic_shop.enchant_tier_header_wide"));
        WriteDivider(66);

        for (int i = 9; i < EnchantTiers.Length; i++)
        {
            var tier = EnchantTiers[i];
            long cost = ApplyAllPriceModifiers(tier.baseCost + (player.Level * tier.levelScale), player);
            bool canAfford = player.Gold >= cost;
            bool meetsLevel = player.Level >= tier.minLevel;

            terminal.SetColor("gray");
            terminal.Write($"  [{i + 1,2}] ");

            if (!meetsLevel)
            {
                terminal.SetColor("darkgray");
                terminal.Write($"{tier.name,-17}{tier.description,-30}{cost,10:N0}g");
                terminal.SetColor("red");
                terminal.Write($"  Lv.{tier.minLevel}+");
            }
            else
            {
                terminal.SetColor(canAfford ? "bright_magenta" : "red");
                terminal.Write($"{tier.name,-17}");
                terminal.SetColor(canAfford ? "cyan" : "red");
                terminal.Write($"{tier.description,-30}");
                terminal.SetColor(canAfford ? "yellow" : "red");
                terminal.Write($"{cost,10:N0}g");

                // Show material requirements for high-tier enchants
                if (EnchantMaterialRequirements.TryGetValue(i, out var reqs))
                {
                    var matNames = reqs.Select(r => {
                        var mat = GameConfig.GetMaterialById(r.materialId);
                        bool has = player.HasMaterial(r.materialId, r.count);
                        return (name: $"{r.count}x {mat?.Name ?? r.materialId}", has);
                    }).ToList();
                    bool hasAll = matNames.All(m => m.has);
                    terminal.Write("  ");
                    for (int j = 0; j < matNames.Count; j++)
                    {
                        terminal.SetColor(matNames[j].has ? "bright_green" : "red");
                        terminal.Write(matNames[j].name);
                        if (j < matNames.Count - 1)
                        {
                            terminal.SetColor("gray");
                            terminal.Write(" + ");
                        }
                    }
                }
            }
            terminal.WriteLine("");
        }

        // Show warning about 4th/5th enchant failure risk
        int currentEnchants = selectedEquip.GetEnchantmentCount();
        if (currentEnchants >= 3)
        {
            terminal.WriteLine("");
            terminal.SetColor("bright_red");
            float failChance = currentEnchants == 3 ? GameConfig.FourthEnchantFailChance : GameConfig.FifthEnchantFailChance;
            terminal.WriteLine(Loc.Get("shop.enchant_warning", currentEnchants, $"{failChance * 100:N0}"));
            terminal.WriteLine(Loc.Get("magic_shop.enchant_failure_warning"));
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(IsScreenReader ? "  0. Cancel" : "  [0] Cancel");
        terminal.WriteLine("");
        var tierInput = await terminal.GetInput($"  {Loc.Get("magic_shop.select_enchantment")}");
        if (!int.TryParse(tierInput, out int tierChoice) || tierChoice < 1 || tierChoice > EnchantTiers.Length)
            return;

        var selectedTier = EnchantTiers[tierChoice - 1];
        long enchantCost = ApplyAllPriceModifiers(selectedTier.baseCost + (player.Level * selectedTier.levelScale), player);

        if (player.Level < selectedTier.minLevel)
        {
            DisplayMessage(Loc.Get("shop.enchant_level_req", selectedTier.minLevel), "red");
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
            DisplayMessage(Loc.Get("magic_shop.need_gold_enchant"), "cyan");
            DisplayMessage(Loc.Get("shop.insufficient_gold_short", $"{enchantCost:N0}"), "red");
            await terminal.WaitForKey();
            return;
        }

        // Check material requirements for high-tier enchants
        if (EnchantMaterialRequirements.TryGetValue(tierChoice - 1, out var matReqs))
        {
            var missing = matReqs.Where(r => !player.HasMaterial(r.materialId, r.count)).ToList();
            if (missing.Count > 0)
            {
                DisplayMessage("");
                DisplayMessage("'This enchantment requires rare materials,' the gnome says.", "cyan");
                foreach (var req in missing)
                {
                    var mat = GameConfig.GetMaterialById(req.materialId);
                    terminal.SetColor("red");
                    terminal.WriteLine($"  Missing: {req.count}x {mat?.Name ?? req.materialId}");
                }
                terminal.SetColor("darkgray");
                terminal.WriteLine("  These materials can be found deep in the dungeon.");
                await terminal.WaitForKey();
                return;
            }
        }

        // For stat-specific enchants (tiers 1-4, 10-12), let player choose stat
        int statChoice = -1;
        if ((tierChoice >= 1 && tierChoice <= 4) || (tierChoice >= 10 && tierChoice <= 12))
        {
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine($"  {Loc.Get("magic_shop.choose_stat")}");
            terminal.WriteLine("");
            for (int i = 0; i < StatNames.Length; i++)
            {
                terminal.SetColor("gray");
                terminal.Write($"  [{i + 1}] ");
                terminal.SetColor("cyan");
                terminal.WriteLine($"{StatNames[i]}");
            }
            terminal.WriteLine("");
            var statInput = await terminal.GetInput($"  {Loc.Get("magic_shop.stat_choice_prompt")}");
            if (!int.TryParse(statInput, out statChoice) || statChoice < 1 || statChoice > StatNames.Length)
                return;
        }

        // Confirm
        string enchantDesc = (tierChoice <= 4 || (tierChoice >= 10 && tierChoice <= 12)) ? $"+{selectedTier.bonus} {StatNames[statChoice - 1]}" : selectedTier.description;
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
        terminal.WriteLine($"  {Loc.Get("magic_shop.enchant_cost", $"{enchantCost:N0}")}");
        // Show material cost in confirmation
        if (EnchantMaterialRequirements.TryGetValue(tierChoice - 1, out var confirmReqs))
        {
            terminal.SetColor("bright_magenta");
            var matList = confirmReqs.Select(r => {
                var mat = GameConfig.GetMaterialById(r.materialId);
                return $"{r.count}x {mat?.Name ?? r.materialId}";
            });
            terminal.WriteLine($"  Materials: {string.Join(" + ", matList)}");
        }
        terminal.WriteLine("");
        var confirm = await terminal.GetInput($"  {Loc.Get("magic_shop.proceed_yn")}");
        if (confirm.ToUpper() != "Y")
            return;

        // Execute enchantment
        player.Gold -= enchantCost;

        // Consume materials (even on failure for 4th/5th enchant)
        if (EnchantMaterialRequirements.TryGetValue(tierChoice - 1, out var consumeReqs))
        {
            foreach (var req in consumeReqs)
            {
                player.ConsumeMaterial(req.materialId, req.count);
                var mat = GameConfig.GetMaterialById(req.materialId);
                terminal.SetColor(mat?.Color ?? "white");
                terminal.WriteLine($"  The {mat?.Name ?? req.materialId} dissolves into the enchantment...");
            }
            await Task.Delay(500);
        }

        // Check for failure on 4th/5th enchantment
        int currentEnchantCount = selectedEquip.GetEnchantmentCount();
        if (currentEnchantCount >= 3)
        {
            float failChance = currentEnchantCount == 3 ? GameConfig.FourthEnchantFailChance : GameConfig.FifthEnchantFailChance;
            var rng = Random.Shared;
            if (rng.NextDouble() < failChance)
            {
                // FAILURE — gold consumed, random existing enchant destroyed
                DisplayMessage("");
                DisplayMessage($"{_ownerName} places the item on the anvil...", "gray");
                await Task.Delay(500);
                DisplayMessage("The runes flare wildly! Unstable energies crackle!", "bright_red");
                await Task.Delay(500);
                DisplayMessage("CRACK! The enchantment backfires!", "bright_red");
                await Task.Delay(500);

                // Destroy one random existing enchant by decrementing count
                var damaged = selectedEquip.Clone();
                int oldCount = damaged.GetEnchantmentCount();
                if (oldCount > 0)
                {
                    // Replace the enchant marker with decremented count
                    string newMarker = oldCount > 1 ? $"[E:{oldCount - 1}]" : "";
                    damaged.Description = System.Text.RegularExpressions.Regex.Replace(
                        damaged.Description ?? "", @"\[E:\d+\]", newMarker).Trim();

                    // Reduce a random stat bonus as if one enchant was lost
                    var rngStat = rng.Next(6);
                    switch (rngStat)
                    {
                        case 0: damaged.WeaponPower = Math.Max(0, damaged.WeaponPower - 5); break;
                        case 1: damaged.StrengthBonus = Math.Max(0, damaged.StrengthBonus - 5); break;
                        case 2: damaged.DexterityBonus = Math.Max(0, damaged.DexterityBonus - 5); break;
                        case 3: damaged.DefenceBonus = Math.Max(0, damaged.DefenceBonus - 5); break;
                        case 4: damaged.WisdomBonus = Math.Max(0, damaged.WisdomBonus - 5); break;
                        case 5: damaged.ArmorClass = Math.Max(0, damaged.ArmorClass - 5); break;
                    }

                    EquipmentDatabase.RegisterDynamic(damaged);
                    player.UnequipSlot(selectedSlot);
                    player.EquipItem(damaged, selectedSlot, out _);
                    player.RecalculateStats();
                }

                DisplayMessage("");
                DisplayMessage("An existing enchantment was destroyed in the backlash!", "bright_red");
                DisplayMessage($"'I warned you... that's the risk of pushing beyond three enchantments.'", "cyan");
                DisplayMessage($"Your {enchantCost:N0} gold has been consumed by the failed attempt.", "yellow");
                player.Statistics?.RecordGoldSpent(enchantCost);
                await terminal.WaitForKey();
                return;
            }
        }

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
                enchanted.IntelligenceBonus += 6; enchanted.WisdomBonus += 4;
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
            case 10: case 11: case 12: // Mythic/Legendary/Godforged stat enchants
                ApplyEquipmentStatBonus(enchanted, statChoice, selectedTier.bonus);
                suffix = $" +{selectedTier.bonus} {StatNames[statChoice - 1].Substring(0, 3)}";
                break;
            case 13: // Phoenix Fire
                enchanted.WeaponPower += 20;
                enchanted.HasFireEnchant = true;
                suffix = " (Phoenix Fire)";
                break;
            case 14: // Frostbite
                enchanted.WeaponPower += 20;
                enchanted.HasFrostEnchant = true;
                suffix = " (Frostbite)";
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
        WriteSectionHeader(Loc.Get("magic_shop.enchant_removal"), "magenta");
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
            DisplayMessage(Loc.Get("magic_shop.no_enchanted_equipment"), "gray");
            await terminal.WaitForKey();
            return;
        }

        DisplayMessage("");
        DisplayMessage(Loc.Get("shop.strip_enchant_warning"), "red");
        long removalCost = ApplyAllPriceModifiers(5000 + (player.Level * 200), player);
        DisplayMessage(Loc.Get("magic_shop.removal_cost", $"{removalCost:N0}"), "yellow");
        DisplayMessage("");
        var input = await terminal.GetInput(Loc.Get("magic_shop.select_item_cancel"));
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
        var confirmInput = await terminal.GetInput(Loc.Get("magic_shop.remove_enchant_confirm", rmEquip.Name));
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
    // PROCEDURAL ACCESSORY SHOP - Rings/Necklaces from ShopItemGenerator
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Get filtered shop items for the given accessory category
    /// </summary>
    private List<Equipment> GetShopItemsForCategory(AccessoryCategory category)
    {
        var allItems = category == AccessoryCategory.Rings
            ? EquipmentDatabase.GetShopRings()
            : EquipmentDatabase.GetShopNecklaces();

        int playerLevel = currentPlayer?.Level ?? 1;
        return allItems
            .Where(e => e.MinLevel <= playerLevel + 15 && e.MinLevel >= Math.Max(1, playerLevel - 20))
            .ToList();
    }

    /// <summary>
    /// Display paginated accessory items for the selected category
    /// </summary>
    private void ShowAccessoryCategoryItems(AccessoryCategory category)
    {
        var player = currentPlayer;
        var items = GetShopItemsForCategory(category);
        string categoryName = category == AccessoryCategory.Rings ? Loc.Get("magic_shop.rings") : Loc.Get("magic_shop.necklaces");

        int totalPages = Math.Max(1, (items.Count + AccessoryItemsPerPage - 1) / AccessoryItemsPerPage);
        if (_accessoryPage >= totalPages) _accessoryPage = totalPages - 1;

        terminal.ClearScreen();

        // Header
        WriteSectionHeader(categoryName, "bright_yellow");
        terminal.WriteLine("");

        // Gold + item count + page
        terminal.SetColor("yellow");
        terminal.Write("  Gold: ");
        terminal.SetColor("bright_yellow");
        terminal.Write($"{player.Gold:N0}");
        terminal.SetColor("gray");
        terminal.WriteLine($"     {Loc.Get("magic_shop.items_available", $"{items.Count}", $"{_accessoryPage + 1}", $"{totalPages}")}");
        terminal.WriteLine("");

        // Show currently equipped item
        Equipment? currentItem = null;
        if (category == AccessoryCategory.Rings)
        {
            var lf = player.GetEquipment(EquipmentSlot.LFinger);
            var rf = player.GetEquipment(EquipmentSlot.RFinger);
            if (lf != null)
            {
                terminal.SetColor("cyan");
                terminal.Write("  L.Finger: ");
                terminal.SetColor("bright_white");
                terminal.Write(lf.Name);
                var lfStats = GetAccessoryBonusDescription(lf);
                if (!string.IsNullOrEmpty(lfStats)) { terminal.SetColor("green"); terminal.Write($"  {lfStats}"); }
                terminal.WriteLine("");
            }
            if (rf != null)
            {
                terminal.SetColor("cyan");
                terminal.Write("  R.Finger: ");
                terminal.SetColor("bright_white");
                terminal.Write(rf.Name);
                var rfStats = GetAccessoryBonusDescription(rf);
                if (!string.IsNullOrEmpty(rfStats)) { terminal.SetColor("green"); terminal.Write($"  {rfStats}"); }
                terminal.WriteLine("");
            }
            currentItem = lf ?? rf;
        }
        else
        {
            currentItem = player.GetEquipment(EquipmentSlot.Neck);
            if (currentItem != null)
            {
                terminal.SetColor("cyan");
                terminal.Write("  Equipped: ");
                terminal.SetColor("bright_white");
                terminal.Write(currentItem.Name);
                var eqStats = GetAccessoryBonusDescription(currentItem);
                if (!string.IsNullOrEmpty(eqStats)) { terminal.SetColor("green"); terminal.Write($"  {eqStats}"); }
                terminal.WriteLine("");
            }
        }
        terminal.WriteLine("");

        // Column header
        terminal.SetColor("bright_blue");
        terminal.WriteLine(Loc.Get("magic_shop.accessory_col_header"));
        WriteDivider(73);
        if (currentItem != null)
        {
            terminal.SetColor("darkgray");
            terminal.Write("  ");
            terminal.SetColor("bright_green");
            terminal.Write("[+]");
            terminal.SetColor("darkgray");
            terminal.Write(" upgrade  ");
            terminal.SetColor("red");
            terminal.Write("[-]");
            terminal.SetColor("darkgray");
            terminal.WriteLine(" downgrade vs equipped");
        }

        // Items on this page
        int start = _accessoryPage * AccessoryItemsPerPage;
        int end = Math.Min(start + AccessoryItemsPerPage, items.Count);

        for (int i = start; i < end; i++)
        {
            var item = items[i];
            long price = ApplyAllPriceModifiers(item.Value, player);
            bool canAfford = player.Gold >= price;
            bool meetsLevel = player.Level >= item.MinLevel;
            bool canBuy = canAfford && meetsLevel;
            int displayNum = i - start + 1;

            // Number
            terminal.SetColor(canBuy ? "bright_cyan" : "darkgray");
            terminal.Write($" {displayNum,2}. ");

            // Name (colored by rarity if affordable, dim if not)
            terminal.SetColor(canBuy ? item.GetRarityColor() : "darkgray");
            terminal.Write($"{item.Name,-26}");

            // Level requirement
            if (item.MinLevel > 1)
            {
                terminal.SetColor(!meetsLevel ? "red" : (canBuy ? "bright_cyan" : "darkgray"));
                terminal.Write($"{item.MinLevel,3}  ");
            }
            else
            {
                terminal.SetColor(canBuy ? "bright_cyan" : "darkgray");
                terminal.Write($"{"—",3}  ");
            }

            // Price
            terminal.SetColor(canBuy ? "yellow" : "darkgray");
            terminal.Write($"{price,10:N0}  ");

            // Bonus stats
            var bonuses = GetAccessoryBonusDescription(item);
            if (!string.IsNullOrEmpty(bonuses))
            {
                terminal.SetColor(canBuy ? "green" : "darkgray");
                terminal.Write(bonuses);
            }

            // Upgrade indicator
            if (canBuy && currentItem != null)
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
        terminal.Write("]Buy  ");

        if (_accessoryPage > 0)
        {
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("P");
            terminal.SetColor("darkgray");
            terminal.Write("]Prev  ");
        }

        if (_accessoryPage < totalPages - 1)
        {
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("N");
            terminal.SetColor("darkgray");
            terminal.Write("]Next  ");
        }

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("S");
        terminal.SetColor("darkgray");
        terminal.Write("]Sell  ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("B");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("red");
        terminal.WriteLine("Back");
        terminal.WriteLine("");

        ShowStatusLine();
    }

    /// <summary>
    /// Handle input while browsing an accessory category
    /// </summary>
    private async Task<bool> ProcessAccessoryCategoryChoice(string choice, Character player)
    {
        switch (choice)
        {
            case "R":
                _currentAccessoryCategory = null;
                _accessoryPage = 0;
                await NavigateToLocation(GameLocation.MainStreet);
                return true;

            case "B":
            case "X":
                _currentAccessoryCategory = null;
                _accessoryPage = 0;
                RequestRedisplay();
                return false;

            case "P":
                if (_accessoryPage > 0) _accessoryPage--;
                RequestRedisplay();
                return false;

            case "N":
            {
                var items = GetShopItemsForCategory(_currentAccessoryCategory!.Value);
                int totalPages = Math.Max(1, (items.Count + AccessoryItemsPerPage - 1) / AccessoryItemsPerPage);
                if (_accessoryPage < totalPages - 1) _accessoryPage++;
                RequestRedisplay();
                return false;
            }

            case "S":
                await SellAccessory(player);
                return false;

            default:
                if (int.TryParse(choice, out int itemNum) && itemNum >= 1 && _currentAccessoryCategory.HasValue)
                {
                    await BuyAccessoryItem(_currentAccessoryCategory.Value, itemNum, player);
                }
                return false;
        }
    }

    /// <summary>
    /// Purchase a specific accessory item by page-relative index
    /// </summary>
    private async Task BuyAccessoryItem(AccessoryCategory category, int itemIndex, Character player)
    {
        var items = GetShopItemsForCategory(category);
        int actualIdx = _accessoryPage * AccessoryItemsPerPage + itemIndex - 1;
        if (actualIdx < 0 || actualIdx >= items.Count) return;

        var item = items[actualIdx];
        long price = ApplyAllPriceModifiers(item.Value, player);
        var (kingTax, cityTax, totalWithTax) = CityControlSystem.CalculateTaxedPrice(price);

        if (player.Gold < totalWithTax)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"  {Loc.Get("magic_shop.cant_afford")}");
            await terminal.WaitForKey();
            return;
        }

        bool canEquipPersonally = player.Level >= item.MinLevel;

        if (!canEquipPersonally)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"  {Loc.Get("magic_shop.level_warning", $"{item.MinLevel}", $"{player.Level}")}");
            terminal.WriteLine($"  {Loc.Get("shop.item_to_inventory")}");
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

        CityControlSystem.Instance.DisplayTaxBreakdown(terminal, item.Name, price);
        var buyConfirm = await terminal.GetInput($"  {Loc.Get("magic_shop.buy_confirm", $"{totalWithTax:N0}")}");
        if (buyConfirm.Trim().ToUpper() != "Y") return;

        player.Gold -= totalWithTax;

        if (canEquipPersonally && !player.AutoEquipDisabled)
        {
            // Ask whether to equip or send to inventory
            terminal.SetColor("cyan");
            var equipChoice = await terminal.GetInput($"  {Loc.Get("magic_shop.equip_or_inventory")}");
            if (equipChoice.Trim().ToUpper().StartsWith("I"))
            {
                var invItem = player.ConvertEquipmentToLegacyItem(item);
                player.Inventory.Add(invItem);
                terminal.SetColor("bright_green");
                terminal.WriteLine($"  {Loc.Get("shop.purchased_inventory", item.Name)}");
            }
            else
            {
                // For rings, prompt which finger if both are occupied
                EquipmentSlot? targetSlot = null;
                if (category == AccessoryCategory.Rings)
                {
                    var lf = player.GetEquipment(EquipmentSlot.LFinger);
                    var rf = player.GetEquipment(EquipmentSlot.RFinger);
                    if (lf != null && rf != null)
                    {
                        terminal.SetColor("cyan");
                        terminal.WriteLine($"  {Loc.Get("magic_shop.both_rings_occupied")}");
                        terminal.SetColor("white");
                        if (IsScreenReader)
                        {
                            terminal.WriteLine($"    L. Left:  {lf.Name}");
                            terminal.WriteLine($"    R. Right: {rf.Name}");
                            terminal.WriteLine($"    C. Cancel purchase");
                        }
                        else
                        {
                            terminal.WriteLine($"    [L] Left:  {lf.Name}");
                            terminal.WriteLine($"    [R] Right: {rf.Name}");
                            terminal.WriteLine($"    [C] Cancel purchase");
                        }
                        var fingerChoice = await terminal.GetInput($"  {Loc.Get("magic_shop.replace_which_ring")}");
                        var fc = fingerChoice.Trim().ToUpper();
                        if (fc.StartsWith("C") || string.IsNullOrEmpty(fc))
                        {
                            player.Gold += totalWithTax;
                            terminal.SetColor("yellow");
                            terminal.WriteLine($"  {Loc.Get("ui.cancelled")}");
                            await terminal.WaitForKey();
                            return;
                        }
                        targetSlot = fc == "R"
                            ? EquipmentSlot.RFinger
                            : EquipmentSlot.LFinger;
                    }
                }

                if (player.EquipItem(item, targetSlot, out string msg))
                {
                    player.RecalculateStats();
                    terminal.SetColor("bright_green");
                    terminal.WriteLine($"  {Loc.Get("magic_shop.now_wearing", item.Name)}");
                }
                else
                {
                    // Equip failed — add to inventory instead
                    var invItem = player.ConvertEquipmentToLegacyItem(item);
                    player.Inventory.Add(invItem);
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"  {Loc.Get("shop.purchased_inventory", item.Name)}");
                }
            }
        }
        else
        {
            // Can't equip personally — add to inventory for companions/NPCs
            var invItem = player.ConvertEquipmentToLegacyItem(item);
            player.Inventory.Add(invItem);
            terminal.SetColor("bright_green");
            terminal.WriteLine($"  {Loc.Get("shop.purchased_inventory", item.Name)}");
        }

        player.Statistics?.RecordPurchase(totalWithTax);
        player.Statistics?.RecordAccessoryPurchase(totalWithTax);
        CityControlSystem.Instance.ProcessSaleTax(price);
        // Check for equipment quest completion
        QuestSystem.OnEquipmentPurchased(player, item);

        // Check accessory collection achievement
        if (player.GetEquipment(EquipmentSlot.LFinger) != null &&
            player.GetEquipment(EquipmentSlot.RFinger) != null &&
            player.GetEquipment(EquipmentSlot.Neck) != null)
            AchievementSystem.TryUnlock(player, "accessory_collector");

        await SaveSystem.Instance.AutoSave(player);
        await terminal.WaitForKey();
    }

    /// <summary>
    /// Sell accessories from inventory (with Sell All option)
    /// </summary>
    private async Task SellAccessory(Character player)
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("magic_shop.sell_accessories"), "bright_yellow");
        terminal.WriteLine("");

        // Find sellable accessories in inventory (non-equipped items)
        var sellable = new List<(Equipment item, long sellPrice)>();
        var fenceModifier = FactionSystem.Instance.GetFencePriceModifier();

        foreach (var invItem in player.Inventory)
        {
            // Check if this is an accessory (ring, necklace, or magic item)
            if (invItem.Type == ObjType.Magic || invItem.Type == ObjType.Fingers || invItem.Type == ObjType.Neck)
            {
                // Convert Item to Equipment for price calculation
                long sellPrice = (long)(Math.Max(1, invItem.Value / 2) * fenceModifier);
                // Use a temporary Equipment for display
                var equip = new Equipment { Name = invItem.Name, Value = invItem.Value };
                sellable.Add((equip, sellPrice));
            }
        }

        // Also check for any Equipment-type accessories in inventory via equipped items scan
        // (accessories picked up from dungeon loot are Equipment objects stored differently)

        if (sellable.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("magic_shop.no_accessories_sell")}");
            await terminal.WaitForKey();
            return;
        }

        if (fenceModifier > 1.0f)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine($"  Shadows Fence Bonus: {(int)((fenceModifier - 1.0f) * 100)}% better prices!");
            terminal.WriteLine("");
        }

        for (int i = 0; i < sellable.Count; i++)
        {
            var (item, sellPrice) = sellable[i];
            terminal.SetColor("bright_cyan");
            terminal.Write($"  {i + 1,2}. ");
            terminal.SetColor("white");
            terminal.Write($"{item.Name,-30}");
            terminal.SetColor("yellow");
            terminal.WriteLine($" - Sell for {sellPrice:N0} gold");
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.Write("  Sell which piece? (");
        terminal.SetColor("bright_yellow");
        terminal.Write("[A]");
        terminal.SetColor("gray");
        terminal.Write("ll, ");
        terminal.SetColor("bright_yellow");
        terminal.Write("[F]");
        terminal.SetColor("gray");
        terminal.Write("iltered, ");
        terminal.SetColor("bright_yellow");
        terminal.Write("0");
        terminal.SetColor("gray");
        terminal.WriteLine(" to cancel): ");

        var input = await terminal.GetInput("  > ");
        var upper = input.Trim().ToUpper();

        if (upper == "0" || upper == "Q" || upper == "B") return;

        if (upper == "F")
        {
            // Filtered sell - sell accessories matching player-chosen criteria
            var accessoryTypes = new[] { ObjType.Magic, ObjType.Fingers, ObjType.Neck };
            await FilteredSellFromBackpack(accessoryTypes, fenceModifier);
            return;
        }

        if (upper == "A")
        {
            long totalGold = sellable.Sum(s => s.sellPrice);
            terminal.SetColor("yellow");
            terminal.Write($"  Sell {sellable.Count} item{(sellable.Count > 1 ? "s" : "")} for ");
            terminal.SetColor("bright_yellow");
            terminal.Write($"{totalGold:N0}");
            terminal.SetColor("yellow");
            terminal.Write(" gold? (Y/N): ");
            var confirm = await terminal.GetInput("");
            if (confirm.Trim().ToUpper() == "Y")
            {
                // Remove all sold items from inventory (reverse to preserve indices)
                int soldCount = 0;
                for (int i = player.Inventory.Count - 1; i >= 0; i--)
                {
                    var invItem = player.Inventory[i];
                    if (invItem.Type == ObjType.Magic || invItem.Type == ObjType.Fingers || invItem.Type == ObjType.Neck)
                    {
                        long sellPrice = (long)(Math.Max(1, invItem.Value / 2) * fenceModifier);
                        player.Gold += sellPrice;
                        player.Statistics?.RecordSale(sellPrice);
                        player.Inventory.RemoveAt(i);
                        soldCount++;
                    }
                }
                terminal.SetColor("bright_green");
                terminal.WriteLine($"  {Loc.Get("shop.sold_accessories", soldCount, $"{totalGold:N0}")}");
                await SaveSystem.Instance.AutoSave(player);
            }
        }
        else if (int.TryParse(input, out int idx) && idx >= 1 && idx <= sellable.Count)
        {
            var (item, sellPrice) = sellable[idx - 1];
            // Find and remove the actual inventory item
            for (int i = 0; i < player.Inventory.Count; i++)
            {
                var invItem = player.Inventory[i];
                if ((invItem.Type == ObjType.Magic || invItem.Type == ObjType.Fingers || invItem.Type == ObjType.Neck)
                    && invItem.Name == item.Name)
                {
                    player.Gold += sellPrice;
                    player.Statistics?.RecordSale(sellPrice);
                    player.Inventory.RemoveAt(i);
                    terminal.SetColor("bright_green");
                    terminal.WriteLine($"  {Loc.Get("shop.sold_single", item.Name, $"{sellPrice:N0}")}");
                    await SaveSystem.Instance.AutoSave(player);
                    break;
                }
            }
        }

        await terminal.WaitForKey();
    }

    /// <summary>
    /// Get compact bonus description for accessory display (max 3 bonuses to fit in column)
    /// </summary>
    private static string GetAccessoryBonusDescription(Equipment item)
    {
        var bonuses = new List<string>();
        if (item.StrengthBonus != 0) bonuses.Add($"Str{(item.StrengthBonus > 0 ? "+" : "")}{item.StrengthBonus}");
        if (item.DexterityBonus != 0) bonuses.Add($"Dex{(item.DexterityBonus > 0 ? "+" : "")}{item.DexterityBonus}");
        if (item.AgilityBonus != 0) bonuses.Add($"Agi{(item.AgilityBonus > 0 ? "+" : "")}{item.AgilityBonus}");
        if (item.ConstitutionBonus != 0) bonuses.Add($"Con{(item.ConstitutionBonus > 0 ? "+" : "")}{item.ConstitutionBonus}");
        if (item.IntelligenceBonus != 0) bonuses.Add($"Int{(item.IntelligenceBonus > 0 ? "+" : "")}{item.IntelligenceBonus}");
        if (item.WisdomBonus != 0) bonuses.Add($"Wis{(item.WisdomBonus > 0 ? "+" : "")}{item.WisdomBonus}");
        if (item.CharismaBonus != 0) bonuses.Add($"Cha{(item.CharismaBonus > 0 ? "+" : "")}{item.CharismaBonus}");
        if (item.DefenceBonus != 0) bonuses.Add($"Def{(item.DefenceBonus > 0 ? "+" : "")}{item.DefenceBonus}");
        if (item.MaxHPBonus != 0) bonuses.Add($"HP+{item.MaxHPBonus}");
        if (item.MaxManaBonus != 0) bonuses.Add($"MP+{item.MaxManaBonus}");
        if (item.StaminaBonus != 0) bonuses.Add($"Sta+{item.StaminaBonus}");
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
        if (item.StaminaBonus != 0) bonuses.Add($"Sta{(item.StaminaBonus > 0 ? "+" : "")}{item.StaminaBonus}");
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
             + item.AgilityBonus * 3 + item.CharismaBonus * 3 + item.StaminaBonus * 3
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
            WriteBoxHeader(title, "magenta");

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
            WriteDivider(68);

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
            terminal.Write(IsScreenReader ? "  0. Cancel" : "  [0] Cancel");
            if (totalPages > 1)
            {
                terminal.Write(IsScreenReader ? "   N. Next   P. Prev" : "   [N]ext   [P]rev");
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
        WriteBoxHeader(Loc.Get("magic_shop.romance_header"), "magenta");
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
        terminal.WriteLine($"  {Loc.Get("magic_shop.gold_and_mana", $"{player.Gold:N0}", $"{player.Mana}")}");
        terminal.WriteLine("");

        // Column header
        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("magic_shop.love_col_header"));
        WriteDivider(78);

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
            terminal.WriteLine($"  {Loc.Get("magic_shop.dark_surcharge")}");
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(IsScreenReader ? $"  0. {Loc.Get("ui.cancel")}" : $"  [0] {Loc.Get("ui.cancel")}");
        terminal.WriteLine("");

        var spellInput = await terminal.GetInput($"  {Loc.Get("magic_shop.select_spell")}");
        if (!int.TryParse(spellInput, out int spellIdx) || spellIdx < 1 || spellIdx > LoveSpells.Length)
            return;

        var selected = LoveSpells[spellIdx - 1];
        long spellCost = ApplyAllPriceModifiers(selected.baseCost + (player.Level * selected.levelScale), player);
        if (evilSurcharge) spellCost = (long)(spellCost * 1.2);
        var (loveKingTax, loveCityTax, loveTotalWithTax) = CityControlSystem.CalculateTaxedPrice(spellCost);

        if (player.Level < selected.minLevel)
        {
            DisplayMessage($"  {Loc.Get("magic_shop.level_required", $"{selected.minLevel}")}", "red");
            await terminal.WaitForKey();
            return;
        }
        if (player.Mana < selected.manaCost)
        {
            DisplayMessage($"  {Loc.Get("magic_shop.insufficient_mana")}", "red");
            await terminal.WaitForKey();
            return;
        }
        if (player.Gold < loveTotalWithTax)
        {
            DisplayMessage($"  {Loc.Get("magic_shop.insufficient_gold")}", "red");
            await terminal.WaitForKey();
            return;
        }

        // Select target NPC - paginated picker with search
        var npcsAlive = NPCSpawnSystem.Instance?.ActiveNPCs?.Where(n => !n.IsDead).ToList() ?? new List<NPC>();
        if (npcsAlive.Count == 0)
        {
            DisplayMessage($"  {Loc.Get("magic_shop.no_npcs")}", "gray");
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
        if (selected.bypassCap)
        {
            bool alreadyUsedToday = false;
            if (DoorMode.IsOnlineMode)
            {
                var boundary = DailySystemManager.GetCurrentResetBoundary();
                alreadyUsedToday = player.LastBindingOfSoulsRealDate >= boundary;
            }
            else
            {
                alreadyUsedToday = _bindingOfSoulsUsedToday.Contains(targetNPC.Name1);
            }

            if (alreadyUsedToday)
            {
                terminal.SetColor("red");
                terminal.WriteLine("  'The Binding can only be cast once per day on the same soul.'");
                await terminal.WaitForKey();
                return;
            }
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
        terminal.Write($"  {Loc.Get("magic_shop.relationship_label")} ");
        terminal.SetColor(GetRelationshipColor(currentRel));
        terminal.Write(beforeName);
        terminal.SetColor("gray");
        terminal.Write(" --> ");
        terminal.SetColor(GetRelationshipColor(projectedRel));
        terminal.WriteLine($"{afterName} ({Loc.Get("magic_shop.estimated")})");
        terminal.SetColor("yellow");
        terminal.WriteLine($"  {Loc.Get("magic_shop.cost_gold_mana", $"{spellCost:N0}", $"{selected.manaCost}")}");
        CityControlSystem.Instance.DisplayTaxBreakdown(terminal, Loc.Get("magic_shop.love_spells"), spellCost);
        terminal.WriteLine("");

        var confirm = await terminal.GetInput($"  {Loc.Get("magic_shop.proceed_yn")}");
        if (confirm.ToUpper() != "Y")
            return;

        // Deduct costs
        player.Gold -= loveTotalWithTax;
        CityControlSystem.Instance.ProcessSaleTax(spellCost);
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
            player.Statistics?.RecordLoveSpellCast(loveTotalWithTax);
            player.Statistics?.RecordGoldSpent(loveTotalWithTax);
            await terminal.WaitForKey();
            return;
        }

        // Apply relationship change - direction 1 = IMPROVE (decrease relation number)
        if (selected.bypassCap)
        {
            // Binding of Souls - bypasses daily cap via overrideMaxFeeling
            RelationshipSystem.UpdateRelationship(player, targetNPC, 1, selected.steps, false, true);
            _bindingOfSoulsUsedToday.Add(targetNPC.Name1);
            if (DoorMode.IsOnlineMode)
                player.LastBindingOfSoulsRealDate = DateTime.UtcNow;
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

        player.Statistics?.RecordLoveSpellCast(loveTotalWithTax);
        player.Statistics?.RecordGoldSpent(loveTotalWithTax);
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
        WriteBoxHeader(Loc.Get("magic_shop.dark_arts_header"), "red");
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
        terminal.WriteLine($"  {Loc.Get("magic_shop.gold_and_mana", $"{player.Gold:N0}", $"{player.Mana}")}");
        terminal.WriteLine("");

        // Column header
        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("magic_shop.dark_col_header"));
        WriteDivider(86);

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
            terminal.WriteLine($"  {Loc.Get("magic_shop.good_alignment_warning")}");
        }

        terminal.SetColor("gray");
        terminal.WriteLine(IsScreenReader ? $"  0. {Loc.Get("ui.cancel")}" : $"  [0] {Loc.Get("ui.cancel")}");
        terminal.WriteLine("");

        var spellInput = await terminal.GetInput($"  {Loc.Get("magic_shop.select_spell")}");
        if (!int.TryParse(spellInput, out int spellIdx) || spellIdx < 1 || spellIdx > DeathSpells.Length)
            return;

        var selected = DeathSpells[spellIdx - 1];
        long spellCost = ApplyAllPriceModifiers(selected.baseCost + (player.Level * selected.levelScale), player);
        var (deathKingTax, deathCityTax, deathTotalWithTax) = CityControlSystem.CalculateTaxedPrice(spellCost);

        if (player.Level < selected.minLevel)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"  {Loc.Get("magic_shop.level_required", $"{selected.minLevel}")}");
            await terminal.WaitForKey();
            return;
        }
        if (player.Mana < selected.manaCost)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"  {Loc.Get("magic_shop.insufficient_mana")}");
            await terminal.WaitForKey();
            return;
        }
        if (player.Gold < deathTotalWithTax)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"  {Loc.Get("magic_shop.insufficient_gold")}");
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
            terminal.WriteLine($"  {Loc.Get("magic_shop.no_valid_targets")}");
            await terminal.WaitForKey();
            return;
        }

        var targetNPC = await PickNPCTarget(player, validTargets, $"Dark Arts: {selected.name}", showRelationship: false, showLevel: true);
        if (targetNPC == null) return;

        // Confirmation screen with full details
        int successChance = CalcDeathSpellSuccess(selected.baseSuccess, player.Intelligence);
        int darkShiftAmount = isGood ? selected.darkShift * 2 : selected.darkShift;

        terminal.ClearScreen();
        WriteBoxHeader($"Confirm: {selected.name}", "red");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.Write($"  {Loc.Get("magic_shop.dark_target")}   ");
        terminal.SetColor("bright_red");
        terminal.WriteLine($"{targetNPC.Name1} ({targetNPC.ClassName}, {Loc.Get("magic_shop.dark_level")} {targetNPC.Level})");

        terminal.SetColor("white");
        terminal.Write($"  {Loc.Get("magic_shop.dark_success")}  ");
        string chanceColor = successChance >= 70 ? "bright_green" : successChance >= 50 ? "yellow" : "red";
        terminal.SetColor(chanceColor);
        terminal.WriteLine($"{successChance}%");

        terminal.SetColor("white");
        terminal.Write($"  {Loc.Get("magic_shop.dark_cost_label")}     ");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("magic_shop.cost_gold_mana", $"{spellCost:N0}", $"{selected.manaCost}"));
        CityControlSystem.Instance.DisplayTaxBreakdown(terminal, Loc.Get("magic_shop.dark_arts"), spellCost);

        terminal.SetColor("white");
        terminal.Write($"  {Loc.Get("magic_shop.dark_darkness")} ");
        terminal.SetColor("red");
        terminal.WriteLine($"+{darkShiftAmount} {Loc.Get("magic_shop.alignment_shift")}{(isGood ? $" ({Loc.Get("magic_shop.doubled_good")})" : "")}");

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine($"  {Loc.Get("magic_shop.dark_on_success")}");
        terminal.WriteLine($"  {Loc.Get("magic_shop.dark_on_failure")}");
        terminal.WriteLine("");

        var confirm = await terminal.GetInput($"  {Loc.Get("magic_shop.dark_proceed")}");
        if (confirm.ToUpper() != "Y") return;

        // Deduct costs
        player.Gold -= deathTotalWithTax;
        CityControlSystem.Instance.ProcessSaleTax(spellCost);
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

            // Kill the NPC — dark magic assassination is always permanent
            targetNPC.IsDead = true;
            targetNPC.IsPermaDead = true;
            targetNPC.HP = 0;
            // No respawn — IsPermaDead blocks it

            // Blood price for dark magic kill
            WorldSimulator.ApplyBloodPrice(player, targetNPC, GameConfig.MurderWeightPerDarkMagicKill, isDeliberate: true);

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

            player.Statistics?.RecordDeathSpellCast(deathTotalWithTax);
            AchievementSystem.TryUnlock(player, "dark_magician");
            if ((player.Statistics?.TotalDeathSpellsCast ?? 0) >= 5)
                AchievementSystem.TryUnlock(player, "angel_of_death");

            // Prevent save cheesing — persist negative outcomes immediately
            if (DoorMode.IsOnlineMode)
                _ = GameEngine.Instance.SaveCurrentGame();
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

            player.Statistics?.RecordDeathSpellCast(deathTotalWithTax);
        }

        player.Statistics?.RecordGoldSpent(deathTotalWithTax);
        await terminal.WaitForKey();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MANA POTIONS - Unique to Magic Shop
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task BuyManaPotions(Character player)
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("magic_shop.mana_potions"), "blue");
        DisplayMessage("");

        int potionPrice = (int)ApplyAllPriceModifiers(Math.Max(75, player.Level * 3), player);
        var (_, _, mpPotionUnitWithTax) = CityControlSystem.CalculateTaxedPrice(potionPrice);
        int maxCanBuy = mpPotionUnitWithTax > 0 ? (int)(player.Gold / mpPotionUnitWithTax) : 0;
        int maxCanCarry = player.MaxManaPotions - (int)player.ManaPotions;
        int maxPotions = Math.Min(maxCanBuy, maxCanCarry);
        int manaRestored = 30 + player.Level * 5;

        DisplayMessage(Loc.Get("magic_shop.mana_restore", $"{manaRestored}"), "cyan");
        DisplayMessage(Loc.Get("magic_shop.mana_price", $"{potionPrice:N0}"), "gray");
        DisplayMessage(Loc.Get("magic_shop.mana_gold", $"{player.Gold:N0}"), "gray");
        DisplayMessage(Loc.Get("magic_shop.mana_current", $"{player.ManaPotions}", $"{player.MaxManaPotions}"), "blue");
        DisplayMessage("");

        if (player.ManaPotions >= player.MaxManaPotions)
        {
            DisplayMessage(Loc.Get("magic_shop.mana_max"), "red");
            await terminal.WaitForKey();
            return;
        }
        if (maxPotions <= 0)
        {
            DisplayMessage(Loc.Get("shop.cant_afford_potions"), "red");
            await terminal.WaitForKey();
            return;
        }

        var input = await terminal.GetInput(Loc.Get("magic_shop.potion_how_many", $"{maxPotions}"));
        if (int.TryParse(input, out int qty) && qty > 0 && qty <= maxPotions)
        {
            long totalCost = qty * potionPrice;
            var (mpKingTax, mpCityTax, mpTotalWithTax) = CityControlSystem.CalculateTaxedPrice(totalCost);
            CityControlSystem.Instance.DisplayTaxBreakdown(terminal, Loc.Get("magic_shop.mana_potions"), totalCost);
            player.Gold -= mpTotalWithTax;
            player.ManaPotions += qty;

            DisplayMessage(Loc.Get("magic_shop.mana_deal", _ownerName, $"{qty}"), "bright_green");
            DisplayMessage(Loc.Get("magic_shop.potion_total", $"{mpTotalWithTax:N0}"), "gray");

            player.Statistics?.RecordGoldSpent(mpTotalWithTax);
            player.Statistics?.RecordMagicShopPurchase(mpTotalWithTax);
            CityControlSystem.Instance.ProcessSaleTax(totalCost);
        }
        else
        {
            DisplayMessage(Loc.Get("ui.cancelled"), "gray");
        }

        await terminal.WaitForKey();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SCRYING SERVICE - NPC information
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task ScryNPC(Character player)
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("magic_shop.scrying"), "magenta");
        DisplayMessage("");
        DisplayMessage($"{_ownerName} gazes into a crystal orb that swirls with mist...", "gray");
        DisplayMessage("'Name the soul you seek, and I shall find them.'", "cyan");
        DisplayMessage("");

        long scryCost = ApplyAllPriceModifiers(1000 + (player.Level * 50), player);
        var (scryKingTax, scryCityTax, scryTotalWithTax) = CityControlSystem.CalculateTaxedPrice(scryCost);
        DisplayMessage(Loc.Get("magic_shop.scry_cost", $"{scryCost:N0}"), "yellow");
        CityControlSystem.Instance.DisplayTaxBreakdown(terminal, Loc.Get("magic_shop.scrying"), scryCost);
        DisplayMessage("");

        if (player.Gold < scryTotalWithTax)
        {
            DisplayMessage(Loc.Get("magic_shop.insufficient_gold"), "red");
            await terminal.WaitForKey();
            return;
        }

        var allNPCs = NPCSpawnSystem.Instance?.ActiveNPCs?.ToList() ?? new List<NPC>();
        if (allNPCs.Count == 0)
        {
            DisplayMessage(Loc.Get("magic_shop.no_npcs"), "gray");
            await terminal.WaitForKey();
            return;
        }

        var target = await PickNPCTarget(player, allNPCs, "Scrying - Choose a Soul", showRelationship: true, showLevel: true);
        if (target == null) return;

        player.Gold -= scryTotalWithTax;
        CityControlSystem.Instance.ProcessSaleTax(scryCost);

        DisplayMessage("");
        DisplayMessage("The mists part to reveal...", "magenta");
        await Task.Delay(500);
        DisplayMessage("");
        WriteSectionHeader(target.Name1, "cyan");
        DisplayMessage($"  {Loc.Get("magic_shop.scry_class")} {target.ClassName}    {Loc.Get("magic_shop.dark_level")}: {target.Level}", "white");
        DisplayMessage($"  {Loc.Get("magic_shop.scry_status")} {(target.IsDead ? Loc.Get("magic_shop.scry_dead") : Loc.Get("magic_shop.scry_alive"))}", target.IsDead ? "red" : "green");

        int rel = RelationshipSystem.GetRelationshipLevel(player, target);
        string relName = GetRelationshipDisplayName(rel);
        string relColor = GetRelationshipColor(rel);
        DisplayMessage($"  {Loc.Get("magic_shop.relationship_label")} {relName}", relColor);

        string spouseName = RelationshipSystem.GetSpouseName(target);
        if (!string.IsNullOrEmpty(spouseName))
            DisplayMessage($"  {Loc.Get("magic_shop.scry_married_to")} {spouseName}", "white");

        if (target.Brain?.Personality != null)
        {
            var p = target.Brain.Personality;
            string trait1 = p.Romanticism > 0.7f ? "Romantic" : p.Aggression > 0.7f ? "Aggressive" : p.Intelligence > 0.7f ? "Scholarly" : "Practical";
            string trait2 = p.Greed > 0.7f ? "Greedy" : p.Loyalty > 0.7f ? "Loyal" : p.Sociability > 0.7f ? "Social" : "Reserved";
            DisplayMessage($"  {Loc.Get("magic_shop.scry_personality")} {trait1}, {trait2}", "gray");
        }

        player.Statistics?.RecordGoldSpent(scryTotalWithTax);
        player.Statistics?.RecordMagicShopPurchase(scryTotalWithTax);

        await terminal.WaitForKey();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ENHANCED SHOPKEEPER DIALOGUE + WAVE FRAGMENT
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task TalkToOwnerEnhanced(Character player)
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("magic_shop.conversation_with", _ownerName), "cyan");
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
        DisplayMessage($"(Q) {Loc.Get("magic_shop.end_conversation")}", "gray");

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
