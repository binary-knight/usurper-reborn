using UsurperRemake.Utils;
using UsurperRemake.Systems;
using UsurperRemake.BBS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Weapon Shop Location - Modern RPG weapon and shield system
/// Sells One-Handed Weapons, Two-Handed Weapons, Bows, and Shields
/// One-handed weapon purchases prompt for Main Hand or Off-Hand slot
/// </summary>
public class WeaponShopLocation : BaseLocation
{
    private string shopkeeperName = "Tully";
    private WeaponCategory? currentCategory = null;
    private int currentPage = 0;
    private const int ItemsPerPage = 15;

    private enum WeaponCategory
    {
        OneHanded,
        TwoHanded,
        Bows,
        Shields
    }

    public WeaponShopLocation() : base(
        GameLocation.WeaponShop,
        "Weapon Shop",
        "You enter the dusty old weaponstore filled with all kinds of different weapons."
    ) { }

    protected override void SetupLocation()
    {
        base.SetupLocation();
        shopkeeperName = "Tully";
        currentCategory = null;
        currentPage = 0;
    }
    protected override string[]? GetAmbientMessages() => new[]
    {
        Loc.Get("weapon_shop.ambient_steel"),
        Loc.Get("weapon_shop.ambient_whetstone"),
        Loc.Get("weapon_shop.ambient_hammer"),
        Loc.Get("weapon_shop.ambient_oil"),
        Loc.Get("weapon_shop.ambient_blade"),
    };

    protected override void DisplayLocation()
    {
        if (IsScreenReader && currentPlayer != null && !currentPlayer.IsBarredFromWeaponShop(GameEngine.Instance.SessionCurrentDay))
        {
            if (currentCategory == null) { DisplayLocationSR(); return; }
        }

        if (IsBBSSession && currentPlayer != null && !currentPlayer.IsBarredFromWeaponShop(GameEngine.Instance.SessionCurrentDay))
        {
            if (currentCategory == null) { DisplayLocationBBS(); return; }
        }

        terminal.ClearScreen();

        if (currentPlayer == null) return;

        // Check if player has been kicked out for bad haggling
        if (currentPlayer.IsBarredFromWeaponShop(GameEngine.Instance.SessionCurrentDay))
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine(Loc.Get("weapon_shop.kicked_out1"));
            terminal.WriteLine(Loc.Get("weapon_shop.kicked_out2"));
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("weapon_shop.kicked_return"), "yellow");
            return;
        }

        // Phase 4: Electron mode emits Weapon Shop top-level menu state.
        // Only the main category-picker menu is graphical for now — paginated
        // browse lists, buy/sell flows, identify dialogs all still text mode.
        // Pattern C (full shop with command-protocol) deferred.
        if (GameConfig.ElectronMode && currentCategory == null)
        {
            EmitElectronEvents();
            return;
        }

        WriteBoxHeader(Loc.Get("weapon_shop.header"), "bright_cyan");
        terminal.WriteLine("");

        ShowNPCsInLocation();

        if (currentCategory.HasValue)
        {
            ShowCategoryItems(currentCategory.Value);
        }
        else
        {
            ShowMainMenu();
        }
    }

    private void ShowMainMenu()
    {
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("weapon_shop.run_by", shopkeeperName));
        terminal.WriteLine("");

        ShowShopkeeperMood(shopkeeperName,
            Loc.Get("weapon_shop.shopkeeper_greeting"));
        terminal.WriteLine("");

        terminal.Write(Loc.Get("weapon_shop.you_have"));
        terminal.SetColor("yellow");
        terminal.Write(FormatNumber(currentPlayer.Gold));
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("weapon_shop.gold_crowns"));

        // Show alignment price modifier
        var alignmentModifier = AlignmentSystem.Instance.GetPriceModifier(currentPlayer, isShadyShop: false);
        if (alignmentModifier != 1.0f)
        {
            var (alignText, alignColor) = AlignmentSystem.Instance.GetAlignmentDisplay(currentPlayer);
            terminal.SetColor(alignColor);
            if (alignmentModifier < 1.0f)
                terminal.WriteLine(Loc.Get("weapon_shop.align_discount", alignText, (int)((1.0f - alignmentModifier) * 100)));
            else
                terminal.WriteLine(Loc.Get("weapon_shop.align_markup", alignText, (int)((alignmentModifier - 1.0f) * 100)));
        }

        // Show world event price modifier
        var worldEventModifier = WorldEventSystem.Instance.GlobalPriceModifier;
        if (Math.Abs(worldEventModifier - 1.0f) > 0.01f)
        {
            if (worldEventModifier < 1.0f)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("weapon_shop.world_discount", (int)((1.0f - worldEventModifier) * 100)));
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("weapon_shop.world_markup", (int)((worldEventModifier - 1.0f) * 100)));
            }
        }
        terminal.WriteLine("");

        // Show current weapon configuration
        ShowCurrentWeapons();
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("shop.select_category"));
        terminal.WriteLine("");

        WriteSRMenuOption("1", Loc.Get("weapon_shop.one_handed"));
        WriteSRMenuOption("2", Loc.Get("weapon_shop.two_handed"));
        WriteSRMenuOption("3", Loc.Get("weapon_shop.bows"));
        WriteSRMenuOption("4", Loc.Get("weapon_shop.shields"));

        terminal.WriteLine("");

        WriteSRMenuOption("S", Loc.Get("weapon_shop.sell"));
        WriteSRMenuOption("A", Loc.Get("shop.auto_buy"));
        WriteSRMenuOption("F", Loc.Get("weapon_shop.reforge"));

        terminal.WriteLine("");
        WriteSRMenuOption("R", Loc.Get("shop.return"));
        terminal.WriteLine("");

        ShowStatusLine();

        // Show first shop hint for new players
        HintSystem.Instance.TryShowHint(HintSystem.HINT_FIRST_SHOP, terminal, currentPlayer.HintsShown);
    }

    private void DisplayLocationSR()
    {
        terminal.ClearScreen();
        terminal.WriteLine(Loc.Get("weapon_shop.header"));
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine($"{Loc.Get("weapon_shop.run_by", shopkeeperName)} {Loc.Get("shop.you_have", FormatNumber(currentPlayer.Gold))}");
        terminal.WriteLine("");

        // Current weapons
        var mainHand = currentPlayer.GetEquipment(EquipmentSlot.MainHand);
        var offHand = currentPlayer.GetEquipment(EquipmentSlot.OffHand);
        terminal.SetColor("white");
        terminal.WriteLine($"{Loc.Get("shop.main_hand")} {(mainHand != null ? $"{mainHand.Name} (Pow:{mainHand.WeaponPower})" : Loc.Get("shop.empty"))}");
        terminal.WriteLine($"{Loc.Get("shop.off_hand")} {(offHand != null ? (offHand.WeaponType == WeaponType.Shield || offHand.WeaponType == WeaponType.Buckler || offHand.WeaponType == WeaponType.TowerShield ? $"{offHand.Name} (AC:{offHand.ShieldBonus})" : $"{offHand.Name} (Pow:{offHand.WeaponPower})") : (mainHand?.Handedness == WeaponHandedness.TwoHanded ? Loc.Get("shop.using_2h") : Loc.Get("shop.empty")))}");
        terminal.WriteLine("");

        ShowNPCsInLocation();

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("shop.categories"));
        WriteSRMenuOption("1", Loc.Get("weapon_shop.one_handed"));
        WriteSRMenuOption("2", Loc.Get("weapon_shop.two_handed"));
        WriteSRMenuOption("3", Loc.Get("weapon_shop.bows"));
        WriteSRMenuOption("4", Loc.Get("weapon_shop.shields"));
        terminal.WriteLine("");
        WriteSRMenuOption("S", Loc.Get("weapon_shop.sell"));
        WriteSRMenuOption("A", Loc.Get("shop.auto_buy"));
        WriteSRMenuOption("F", Loc.Get("weapon_shop.reforge"));
        terminal.WriteLine("");
        WriteSRMenuOption("R", Loc.Get("shop.return"));
        terminal.WriteLine("");
        ShowStatusLine();
    }

    /// <summary>
    /// Compact BBS display for 80x25 terminals (main menu only).
    /// </summary>
    private void DisplayLocationBBS()
    {
        terminal.ClearScreen();

        // Header
        ShowBBSHeader(Loc.Get("weapon_shop.header"));

        // 1-line description
        terminal.SetColor("gray");
        terminal.WriteLine($" {Loc.Get("weapon_shop.run_by", shopkeeperName)} {Loc.Get("weapon_shop.you_have")}{FormatNumber(currentPlayer.Gold)}{Loc.Get("weapon_shop.gold_suffix")}");

        // Current weapons summary
        var mainHand = currentPlayer.GetEquipment(EquipmentSlot.MainHand);
        var offHand = currentPlayer.GetEquipment(EquipmentSlot.OffHand);
        terminal.SetColor("gray");
        terminal.Write(Loc.Get("weapon_shop.main_label"));
        terminal.SetColor("white");
        terminal.Write(mainHand != null ? $"{mainHand.Name}" : Loc.Get("ui.empty"));
        terminal.SetColor("gray");
        terminal.Write(Loc.Get("weapon_shop.off_label"));
        terminal.SetColor("white");
        terminal.WriteLine(offHand != null ? $"{offHand.Name}" : Loc.Get("ui.empty"));

        // NPCs
        ShowBBSNPCs();
        terminal.WriteLine("");

        // Menu
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("weapon_shop.categories"));
        ShowBBSMenuRow(("1", "bright_yellow", Loc.Get("weapon_shop.bbs_one_hand")), ("2", "bright_yellow", Loc.Get("weapon_shop.bbs_two_hand")), ("3", "bright_yellow", Loc.Get("weapon_shop.bbs_bows")), ("4", "bright_yellow", Loc.Get("weapon_shop.bbs_shields")));
        ShowBBSMenuRow(("S", "bright_green", Loc.Get("weapon_shop.bbs_sell")), ("A", "bright_cyan", Loc.Get("weapon_shop.bbs_auto_buy")), ("F", "bright_magenta", Loc.Get("weapon_shop.bbs_reforge")), ("R", "bright_red", Loc.Get("weapon_shop.bbs_return")));

        // Footer
        ShowBBSFooter();
    }

    private void ShowCurrentWeapons()
    {
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("shop.current_weapons"));

        var mainHand = currentPlayer.GetEquipment(EquipmentSlot.MainHand);
        var offHand = currentPlayer.GetEquipment(EquipmentSlot.OffHand);

        terminal.SetColor("white");
        terminal.Write(Loc.Get("weapon_shop.main_hand_label"));
        if (mainHand != null)
        {
            terminal.SetColor("bright_white");
            terminal.Write(mainHand.Name);
            terminal.SetColor("gray");
            if (mainHand.Handedness == WeaponHandedness.TwoHanded)
                terminal.WriteLine(Loc.Get("weapon_shop.stat_2h_pow", mainHand.WeaponPower));
            else
                terminal.WriteLine(Loc.Get("weapon_shop.stat_1h_pow", mainHand.WeaponPower));
        }
        else
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("ui.empty"));
        }

        terminal.SetColor("white");
        terminal.Write(Loc.Get("weapon_shop.off_hand_label"));
        if (offHand != null)
        {
            terminal.SetColor("bright_white");
            terminal.Write(offHand.Name);
            terminal.SetColor("gray");
            if (offHand.WeaponType == WeaponType.Shield || offHand.WeaponType == WeaponType.Buckler || offHand.WeaponType == WeaponType.TowerShield)
                terminal.WriteLine(Loc.Get("weapon_shop.stat_shield", offHand.ShieldBonus, offHand.BlockChance));
            else
                terminal.WriteLine(Loc.Get("weapon_shop.stat_1h_pow", offHand.WeaponPower));
        }
        else if (mainHand?.Handedness == WeaponHandedness.TwoHanded)
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("weapon_shop.using_2h"));
        }
        else
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("ui.empty"));
        }

        // Show weapon configuration
        terminal.SetColor("gray");
        terminal.Write(Loc.Get("weapon_shop.config_label"));
        if (currentPlayer.IsTwoHanding)
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("weapon_shop.two_handed_desc"));
        }
        else if (currentPlayer.IsDualWielding)
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("weapon_shop.dual_wield_desc"));
        }
        else if (currentPlayer.HasShieldEquipped)
        {
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("weapon_shop.sword_board_desc"));
        }
        else if (mainHand != null)
        {
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("weapon_shop.one_handed_desc"));
        }
        else
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("weapon_shop.unarmed_desc"));
        }

        // Calculate total weapon power
        long totalPow = (mainHand?.WeaponPower ?? 0);
        if (currentPlayer.IsDualWielding)
        {
            totalPow += (offHand?.WeaponPower ?? 0) / 2; // Off-hand at 50%
        }

        terminal.SetColor("white");
        terminal.Write(Loc.Get("weapon_shop.total_weapon_power"));
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"{totalPow}");
    }

    /// <summary>
    /// Get filtered shop items for a weapon category, scoped to player level
    /// </summary>
    private List<Equipment> GetShopItemsForCategory(WeaponCategory category)
    {
        var items = category switch
        {
            WeaponCategory.OneHanded => EquipmentDatabase.GetShopWeapons(WeaponHandedness.OneHanded)
                .Where(w => w.WeaponType != WeaponType.Instrument).ToList(),
            WeaponCategory.TwoHanded => EquipmentDatabase.GetShopWeapons(WeaponHandedness.TwoHanded)
                .Where(w => w.WeaponType != WeaponType.Bow).ToList(),
            WeaponCategory.Bows => EquipmentDatabase.GetShopWeapons(WeaponHandedness.TwoHanded)
                .Where(w => w.WeaponType == WeaponType.Bow).ToList(),
            WeaponCategory.Shields => EquipmentDatabase.GetShopShields(),
            _ => new List<Equipment>()
        };

        // Show all items — players can buy for inventory to equip on NPCs/companions
        return items;
    }

    private void ShowCategoryItems(WeaponCategory category)
    {
        string categoryName = category switch
        {
            WeaponCategory.OneHanded => Loc.Get("weapon_shop.cat_one_handed"),
            WeaponCategory.TwoHanded => Loc.Get("weapon_shop.cat_two_handed"),
            WeaponCategory.Bows => Loc.Get("weapon_shop.cat_bows"),
            WeaponCategory.Shields => Loc.Get("weapon_shop.cat_shields"),
            _ => ""
        };
        if (string.IsNullOrEmpty(categoryName)) return;

        List<Equipment> items = GetShopItemsForCategory(category);

        // Phase 4 finish: Pattern C — Electron mode emits the paginated browse
        // state for graphical rendering. JS shows item cards with price/stats/
        // affordability hints. Click on item sends the on-page index back via
        // stdin which lands at ProcessCategoryChoice → BuyItem. Skip text body.
        if (GameConfig.ElectronMode)
        {
            EmitShopBrowseState(categoryName, category, items);
            return;
        }

        WriteSectionHeader(categoryName, "bright_yellow");
        terminal.WriteLine("");

        // Show current item in this category
        Equipment? currentItem = null;
        if (category == WeaponCategory.Shields)
        {
            currentItem = currentPlayer.GetEquipment(EquipmentSlot.OffHand);
            if (currentItem != null && currentItem.WeaponType != WeaponType.Shield && currentItem.WeaponType != WeaponType.Buckler && currentItem.WeaponType != WeaponType.TowerShield)
                currentItem = null;
        }
        else
        {
            currentItem = currentPlayer.GetEquipment(EquipmentSlot.MainHand);
        }

        if (currentItem != null)
        {
            terminal.SetColor("cyan");
            terminal.Write(Loc.Get("weapon_shop.current_prefix"));
            terminal.SetColor("bright_white");
            terminal.Write(currentItem.Name);
            terminal.SetColor("gray");
            if (category == WeaponCategory.Shields)
                terminal.WriteLine(Loc.Get("weapon_shop.current_shield_stats", currentItem.ShieldBonus, currentItem.BlockChance, FormatNumber(currentItem.Value)));
            else
                terminal.WriteLine(Loc.Get("weapon_shop.current_weapon_stats", currentItem.WeaponPower, FormatNumber(currentItem.Value)));
            terminal.WriteLine("");
        }

        // Paginate
        int startIndex = currentPage * ItemsPerPage;
        var pageItems = items.Skip(startIndex).Take(ItemsPerPage).ToList();
        int totalPages = (items.Count + ItemsPerPage - 1) / ItemsPerPage;

        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("weapon_shop.page_info", currentPage + 1, totalPages, items.Count));
        terminal.WriteLine("");

        if (category == WeaponCategory.Shields)
        {
            terminal.SetColor("bright_blue");
            terminal.WriteLine(Loc.Get("weapon_shop.shield_header"));
            WriteDivider(67);
        }
        else
        {
            terminal.SetColor("bright_blue");
            terminal.WriteLine(Loc.Get("weapon_shop.weapon_header"));
            WriteDivider(74);
        }

        int num = 1;
        foreach (var item in pageItems)
        {
            bool canAfford = currentPlayer.Gold >= item.Value;
            bool meetsLevel = currentPlayer.Level >= item.MinLevel;
            bool isPrestige = currentPlayer.Class >= CharacterClass.Tidesworn;
            bool meetsClass = isPrestige || item.ClassRestrictions == null || item.ClassRestrictions.Count == 0
                || item.ClassRestrictions.Contains(currentPlayer.Class);
            bool canBuy = canAfford && meetsLevel && meetsClass;

            terminal.SetColor(canBuy ? "bright_cyan" : "darkgray");
            terminal.Write($"{num,3}. ");

            terminal.SetColor(canBuy ? "white" : "darkgray");
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

            if (category == WeaponCategory.Shields)
            {
                terminal.SetColor(canBuy ? "bright_cyan" : "darkgray");
                terminal.Write($"{item.ShieldBonus,4}  ");
                terminal.Write($"{item.BlockChance,3}%   ");
            }
            else
            {
                terminal.SetColor(canBuy ? "bright_cyan" : "darkgray");
                terminal.Write($"{item.WeaponPower,4}  ");
                terminal.Write($"{item.WeaponType.ToString().Substring(0, Math.Min(8, item.WeaponType.ToString().Length)),-8}  ");
            }

            terminal.SetColor(canBuy ? "yellow" : "darkgray");
            terminal.Write($"{FormatNumber(item.Value),10}  ");

            // Show bonus stats
            var bonuses = GetBonusDescription(item);
            if (!string.IsNullOrEmpty(bonuses))
            {
                terminal.SetColor(canBuy ? "green" : "darkgray");
                terminal.Write(bonuses);
            }

            // Show class restriction tag
            var classTag = GetClassTag(item);
            if (!string.IsNullOrEmpty(classTag))
            {
                terminal.SetColor(!meetsClass ? "red" : "gray");
                terminal.Write($" [{classTag}]");
            }

            terminal.WriteLine("");
            num++;
        }

        terminal.WriteLine("");

        // Navigation
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("#");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("weapon_shop.buy_item"));

        if (currentPage > 0)
        {
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("P");
            terminal.SetColor("darkgray");
            terminal.Write(Loc.Get("weapon_shop.previous"));
        }

        if (currentPage < totalPages - 1)
        {
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("N");
            terminal.SetColor("darkgray");
            terminal.Write(Loc.Get("weapon_shop.next"));
        }

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("B");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("weapon_shop.back"));
        terminal.WriteLine("");
    }

    private string GetBonusDescription(Equipment item)
    {
        var bonuses = new List<string>();

        if (item.StrengthBonus != 0) bonuses.Add($"{Loc.Get("ui.stat_str")}+{item.StrengthBonus}");
        if (item.DexterityBonus != 0) bonuses.Add($"{Loc.Get("ui.stat_dex")}+{item.DexterityBonus}");
        if (item.IntelligenceBonus != 0) bonuses.Add($"{Loc.Get("ui.stat_int")}+{item.IntelligenceBonus}");
        if (item.WisdomBonus != 0) bonuses.Add($"{Loc.Get("ui.stat_wis")}+{item.WisdomBonus}");
        if (item.ConstitutionBonus != 0) bonuses.Add($"{Loc.Get("ui.stat_con")}+{item.ConstitutionBonus}");
        if (item.DefenceBonus != 0) bonuses.Add($"{Loc.Get("ui.stat_def")}+{item.DefenceBonus}");
        if (item.AgilityBonus != 0) bonuses.Add($"{Loc.Get("ui.stat_agi")}+{item.AgilityBonus}");
        if (item.CharismaBonus != 0) bonuses.Add($"{Loc.Get("ui.stat_cha")}+{item.CharismaBonus}");
        if (item.MaxHPBonus != 0) bonuses.Add($"{Loc.Get("ui.stat_hp")}+{item.MaxHPBonus}");
        if (item.MaxManaBonus != 0) bonuses.Add($"{Loc.Get("ui.stat_mp")}+{item.MaxManaBonus}");
        if (item.StaminaBonus != 0) bonuses.Add($"{Loc.Get("ui.stat_sta")}+{item.StaminaBonus}");
        if (item.CriticalChanceBonus != 0) bonuses.Add($"{Loc.Get("ui.stat_crit")}+{item.CriticalChanceBonus}%");
        if (item.CriticalDamageBonus != 0) bonuses.Add($"{Loc.Get("ui.stat_critd")}+{item.CriticalDamageBonus}%");
        if (item.ArmorPiercing != 0) bonuses.Add($"{Loc.Get("ui.stat_apen")}+{item.ArmorPiercing}%");
        if (item.MagicResistance != 0) bonuses.Add($"{Loc.Get("ui.stat_mr")}+{item.MagicResistance}%");
        if (item.LifeSteal != 0) bonuses.Add($"{Loc.Get("ui.stat_leech")}{item.LifeSteal}%");
        if (item.PoisonDamage != 0) bonuses.Add($"{Loc.Get("ui.stat_psn")}+{item.PoisonDamage}");

        // v0.62.1 (player report): sort alphabetically so the WeaponShop, ArmorShop,
        // and combat-loot comparison all show stat bonuses in the same predictable order.
        bonuses.Sort(System.StringComparer.Ordinal);
        return string.Join(" ", bonuses);
    }

    private static string GetClassTag(Equipment item)
    {
        if (item.ClassRestrictions == null || item.ClassRestrictions.Count == 0)
            return "";
        var abbrevs = item.ClassRestrictions.Select(c => c switch
        {
            CharacterClass.Warrior => "War",
            CharacterClass.Paladin => "Pal",
            CharacterClass.Barbarian => "Bar",
            CharacterClass.Ranger => "Rng",
            CharacterClass.Assassin => "Asn",
            CharacterClass.Magician => "Mag",
            CharacterClass.Sage => "Sag",
            CharacterClass.Cleric => "Clr",
            CharacterClass.Bard => "Brd",
            CharacterClass.Alchemist => "Alc",
            CharacterClass.Jester => "Jst",
            CharacterClass.MysticShaman => "Sha",
            _ => c.ToString().Substring(0, 3),
        });
        return string.Join("/", abbrevs);
    }

    protected override async Task<bool> ProcessChoice(string choice)
    {
        // Handle global quick commands first
        var (handled, shouldExit) = await TryProcessGlobalCommand(choice);
        if (handled) return shouldExit;

        if (currentPlayer == null) return true;

        if (currentPlayer.IsBarredFromWeaponShop(GameEngine.Instance.SessionCurrentDay))
        {
            await NavigateToLocation(GameLocation.MainStreet);
            return true;
        }

        var upperChoice = choice.ToUpper().Trim();

        // In category view
        if (currentCategory.HasValue)
        {
            return await ProcessCategoryChoice(upperChoice);
        }

        // In main menu
        switch (upperChoice)
        {
            case "R":
                await NavigateToLocation(GameLocation.MainStreet);
                return true;

            case "1":
                currentCategory = WeaponCategory.OneHanded;
                currentPage = 0;
                RequestRedisplay();
                return false;

            case "2":
                currentCategory = WeaponCategory.TwoHanded;
                currentPage = 0;
                RequestRedisplay();
                return false;

            case "3":
                currentCategory = WeaponCategory.Bows;
                currentPage = 0;
                RequestRedisplay();
                return false;

            case "4":
                currentCategory = WeaponCategory.Shields;
                currentPage = 0;
                RequestRedisplay();
                return false;

            case "S":
                await SellWeapon();
                return false;

            case "A":
                await AutoBuyBestWeapon();
                return false;

            case "F":
                await ReforgeWeapon();
                return false;

            case "?":
                return false;

            default:
                terminal.WriteLine(Loc.Get("weapon_shop.invalid_choice"), "red");
                await Task.Delay(1000);
                return false;
        }
    }

    private async Task<bool> ProcessCategoryChoice(string choice)
    {
        switch (choice)
        {
            case "R":
                currentCategory = null;
                currentPage = 0;
                await NavigateToLocation(GameLocation.MainStreet);
                return true;

            case "X":
            case "B":
            case "Q": // v0.64.2: Q backs out one level everywhere (key-consistency feedback)
                currentCategory = null;
                currentPage = 0;
                RequestRedisplay();
                return false;

            case "P":
                if (currentPage > 0) currentPage--;
                RequestRedisplay();
                return false;

            case "N":
                List<Equipment> items = GetShopItemsForCategory(currentCategory.Value);
                int totalPages = (items.Count + ItemsPerPage - 1) / ItemsPerPage;
                if (currentPage < totalPages - 1) currentPage++;
                RequestRedisplay();
                return false;

            case "S":
                await SellWeapon();
                return false;

            default:
                if (int.TryParse(choice, out int itemNum) && itemNum >= 1 && currentCategory.HasValue)
                {
                    await BuyItem(currentCategory.Value, itemNum);
                }
                return false;
        }
    }

    private async Task BuyItem(WeaponCategory category, int itemIndex)
    {
        List<Equipment> items = GetShopItemsForCategory(category);

        int actualIndex = currentPage * ItemsPerPage + itemIndex - 1;
        if (actualIndex < 0 || actualIndex >= items.Count)
        {
            terminal.WriteLine(Loc.Get("weapon_shop.invalid_item"), "red");
            await Task.Delay(1000);
            return;
        }

        var item = items[actualIndex];

        // Apply alignment and world event price modifiers
        var alignmentModifier = AlignmentSystem.Instance.GetPriceModifier(currentPlayer, isShadyShop: false);
        var worldEventModifier = WorldEventSystem.Instance.GlobalPriceModifier;
        long adjustedPrice = (long)(item.Value * alignmentModifier * worldEventModifier);

        // Apply city control discount if player's team controls the city
        adjustedPrice = CityControlSystem.Instance.ApplyDiscount(adjustedPrice, currentPlayer);

        // Apply faction discount (The Crown gets 10% off at shops)
        adjustedPrice = (long)(adjustedPrice * FactionSystem.Instance.GetShopPriceModifier());

        // Apply divine boon shop discount
        if (currentPlayer.CachedBoonEffects?.ShopDiscountPercent > 0)
            adjustedPrice = (long)(adjustedPrice * (1.0 - currentPlayer.CachedBoonEffects.ShopDiscountPercent));

        // Apply difficulty-based price multiplier
        adjustedPrice = DifficultySystem.ApplyShopPriceMultiplier(adjustedPrice);

        // Calculate total with tax
        var (kingTax, cityTax, totalWithTax) = CityControlSystem.CalculateTaxedPrice(adjustedPrice);

        // v1.2 (design item C): a player who can afford only a haggled price may still try;
        // the real check runs after negotiation.
        bool mayHaggle = HagglingEngine.CanHaggle(currentPlayer, HagglingEngine.ShopType.Weapon) && currentPlayer.Race != CharacterRace.Troll;
        long bestCaseTotal = mayHaggle ? CityControlSystem.CalculateTaxedPrice(adjustedPrice - adjustedPrice / 5).Item3 : totalWithTax;
        if (currentPlayer.Gold < bestCaseTotal)
        {
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("shop.insufficient_gold", FormatNumber(totalWithTax), FormatNumber(currentPlayer.Gold)));
            await Pause();
            return;
        }

        // Check if player can personally equip this item
        bool canEquipPersonally = true;
        string cantEquipReason = "";

        if (currentPlayer.Class < CharacterClass.Tidesworn
            && item.ClassRestrictions != null && item.ClassRestrictions.Count > 0
            && !item.ClassRestrictions.Contains(currentPlayer.Class))
        {
            canEquipPersonally = false;
            cantEquipReason = Loc.Get("weapon_shop.class_restriction", GetClassTag(item));
        }
        else if (item.RequiresGood && currentPlayer.Chivalry <= currentPlayer.Darkness)
        {
            canEquipPersonally = false;
            cantEquipReason = Loc.Get("ui.requires_good");
        }
        else if (item.RequiresEvil && currentPlayer.Darkness <= currentPlayer.Chivalry)
        {
            canEquipPersonally = false;
            cantEquipReason = Loc.Get("ui.requires_evil");
        }
        else if (currentPlayer.Level < item.MinLevel)
        {
            canEquipPersonally = false;
            cantEquipReason = Loc.Get("weapon_shop.requires_level", item.MinLevel, currentPlayer.Level);
        }

        if (!canEquipPersonally)
        {
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("weapon_shop.warning_cant_equip", cantEquipReason));
            terminal.WriteLine(Loc.Get("shop.item_to_inventory"));
        }

        // Warning for 2H weapons if shield equipped
        if (canEquipPersonally && item.Handedness == WeaponHandedness.TwoHanded && currentPlayer.HasShieldEquipped)
        {
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("weapon_shop.two_hand_warning"));
            terminal.WriteLine(Loc.Get("weapon_shop.shield_unequipped"));
        }

        // Show tax breakdown
        CityControlSystem.Instance.DisplayTaxBreakdown(terminal, item.Name, adjustedPrice);

        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("weapon_shop.buy_prompt_name", item.Name));
        terminal.SetColor("yellow");
        terminal.Write(FormatNumber(totalWithTax));
        terminal.SetColor("white");
        if (kingTax > 0 || cityTax > 0)
        {
            terminal.SetColor("gray");
            terminal.Write(Loc.Get("weapon_shop.incl_tax"));
            terminal.SetColor("white");
        }
        else
        {
            var totalModifier = alignmentModifier * worldEventModifier;
            if (Math.Abs(totalModifier - 1.0f) > 0.01f)
            {
                terminal.SetColor("gray");
                terminal.Write(Loc.Get("weapon_shop.was_price", FormatNumber(item.Value)));
                terminal.SetColor("white");
            }
        }
        terminal.WriteLine("");

        // v1.2 (design item C): [H]aggle over the pre-tax price; tax is recomputed on the agreed amount
        while (true)
        {
            int attemptsLeft = HagglingEngine.GetHagglingAttemptsLeft(currentPlayer, HagglingEngine.ShopType.Weapon);
            terminal.SetColor("white");
            terminal.Write(attemptsLeft > 0
                ? Loc.Get("shop.buy_prompt_haggle", FormatNumber(totalWithTax), attemptsLeft)
                : Loc.Get("shop.buy_prompt_no_haggle", FormatNumber(totalWithTax)));
            var confirm = (await terminal.GetInput("")).Trim().ToUpperInvariant();
            if (confirm == "H")
            {
                var haggle = await HagglingEngine.Haggle(currentPlayer, HagglingEngine.ShopType.Weapon, adjustedPrice, Loc.Get("weapon_shop.keeper_name"), terminal);
                if (haggle.Kicked)
                {
                    currentPlayer.WeaponShopBarredUntilDay = GameEngine.Instance.SessionCurrentDay + 1;
                    await NavigateToLocation(GameLocation.MainStreet);
                    return;
                }
                if (haggle.Price < adjustedPrice)
                {
                    adjustedPrice = haggle.Price;
                    (kingTax, cityTax, totalWithTax) = CityControlSystem.CalculateTaxedPrice(adjustedPrice);
                    terminal.WriteLine(Loc.Get("shop.haggle_price_agreed", FormatNumber(adjustedPrice), FormatNumber(kingTax + cityTax)), "bright_green");
                }
                continue;
            }
            if (!GameConfig.IsAffirmative(confirm))
            {
                return;
            }
            break;
        }

        if (currentPlayer.Gold < totalWithTax)
        {
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("shop.insufficient_gold", FormatNumber(totalWithTax), FormatNumber(currentPlayer.Gold)));
            await Pause();
            return;
        }

        currentPlayer.Gold -= totalWithTax;
        currentPlayer.Statistics.RecordPurchase(totalWithTax);
        // v0.61.3: check achievements immediately so threshold-crossing unlocks
        // (shopaholic at 50 items, big_spender at 100k spent) fire at the purchase
        // moment instead of being deferred to the next post-combat sweep. The
        // notification still surfaces via the standard BaseLocation hook on
        // next location entry, but the unlock itself is now correctly timed.
        AchievementSystem.CheckAchievements(currentPlayer);

        // Show tax hint on first purchase
        HintSystem.Instance.TryShowHint(HintSystem.HINT_FIRST_PURCHASE_TAX, terminal, currentPlayer.HintsShown);

        // Process city tax share from this sale
        CityControlSystem.Instance.ProcessSaleTax(adjustedPrice);

        if (canEquipPersonally && !currentPlayer.AutoEquipDisabled)
        {
            // Ask whether to equip or send to inventory
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            var equipChoice = await terminal.GetInput(Loc.Get("weapon_shop.equip_or_inventory"));
            if (equipChoice.Trim().ToUpper().StartsWith("I"))
            {
                var invItem = currentPlayer.ConvertEquipmentToLegacyItem(item);
                currentPlayer.Inventory.Add(invItem);
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("shop.purchased_inventory", item.Name));
            }
            else
            {
                // For one-handed weapons, ask which slot to use
                EquipmentSlot? targetSlot = null;
                if (Character.RequiresSlotSelection(item))
                {
                    targetSlot = await PromptForWeaponSlot();
                    if (targetSlot == null)
                    {
                        // Player cancelled slot selection — add to inventory instead
                        var invItem = currentPlayer.ConvertEquipmentToLegacyItem(item);
                        currentPlayer.Inventory.Add(invItem);
                        terminal.SetColor("bright_green");
                        terminal.WriteLine(Loc.Get("shop.purchased_inventory", item.Name));
                        await SaveSystem.Instance.AutoSave(currentPlayer);
                        await Pause();
                        return;
                    }
                }

                if (currentPlayer.EquipItem(item, targetSlot, out string message))
                {
                    terminal.SetColor("bright_green");
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("shop.purchased_equipped", item.Name));
                    if (!string.IsNullOrEmpty(message))
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine(message);
                    }
                    currentPlayer.RecalculateStats();
                }
                else
                {
                    // Equip failed — add to inventory instead
                    var invItem = currentPlayer.ConvertEquipmentToLegacyItem(item);
                    currentPlayer.Inventory.Add(invItem);
                    terminal.SetColor("yellow");
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("shop.couldnt_equip", item.Name));
                }
            }
        }
        else
        {
            // Can't equip personally — add to inventory for companions/NPCs
            var invItem = currentPlayer.ConvertEquipmentToLegacyItem(item);
            currentPlayer.Inventory.Add(invItem);
            terminal.SetColor("bright_green");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("shop.purchased_inventory", item.Name));
        }

        QuestSystem.OnEquipmentPurchased(currentPlayer, item);

        // Auto-save after purchase
        await SaveSystem.Instance.AutoSave(currentPlayer);

        await Pause();
    }

    private async Task SellWeapon()
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("weapon_shop.sell_header"), "bright_yellow");
        terminal.WriteLine("");

        // Get Shadows faction fence bonus modifier (1.0 normal, 1.2 with Shadows)
        var fenceModifier = FactionSystem.Instance.GetFencePriceModifier();
        bool hasFenceBonus = fenceModifier > 1.0f;

        if (hasFenceBonus)
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("weapon_shop.shadows_bonus"));
            terminal.WriteLine("");
        }

        // Track all sellable items - equipped and inventory
        var sellableItems = new List<(bool isEquipped, EquipmentSlot? slot, int? invIndex, string name, long value, bool isCursed)>();
        int num = 1;

        // Show equipped items first
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("weapon_shop.equipped_label"));

        var mainHand = currentPlayer.GetEquipment(EquipmentSlot.MainHand);
        if (mainHand != null)
        {
            sellableItems.Add((true, EquipmentSlot.MainHand, null, mainHand.Name, mainHand.Value, mainHand.IsCursed));
            long displayPrice = (long)((mainHand.Value / 2) * fenceModifier);
            terminal.SetColor("bright_cyan");
            terminal.Write($"{num}. ");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("weapon_shop.sell_main_hand", mainHand.Name));
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("weapon_shop.sell_for_gold", FormatNumber(displayPrice)));
            num++;
        }

        var offHand = currentPlayer.GetEquipment(EquipmentSlot.OffHand);
        if (offHand != null)
        {
            sellableItems.Add((true, EquipmentSlot.OffHand, null, offHand.Name, offHand.Value, offHand.IsCursed));
            long displayPrice = (long)((offHand.Value / 2) * fenceModifier);
            terminal.SetColor("bright_cyan");
            terminal.Write($"{num}. ");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("weapon_shop.sell_off_hand", offHand.Name));
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("weapon_shop.sell_for_gold", FormatNumber(displayPrice)));
            num++;
        }

        // Show inventory weapons/shields
        var inventoryWeapons = currentPlayer.Inventory?
            .Select((item, index) => (item, index))
            .Where(x => x.item.Type == ObjType.Weapon || x.item.Type == ObjType.Shield)
            .ToList() ?? new List<(Item item, int index)>();

        if (inventoryWeapons.Count > 0)
        {
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("weapon_shop.inventory_label"));

            foreach (var (item, invIndex) in inventoryWeapons)
            {
                if (!item.IsIdentified || item.IsCursed) continue; // v1.1.1: [A] sells only these; the list used to show more than it sold
                sellableItems.Add((false, null, invIndex, item.Name, item.Value, item.IsCursed));
                long displayPrice = (long)((item.Value / 2) * fenceModifier);
                terminal.SetColor("bright_cyan");
                terminal.Write($"{num}. ");
                terminal.SetColor("white");
                terminal.Write($"{item.Name}");
                if (item.Type == ObjType.Weapon)
                    terminal.Write(Loc.Get("weapon_shop.inv_wp", item.Attack));
                else
                    terminal.Write(Loc.Get("weapon_shop.inv_shield"));
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("weapon_shop.sell_for_gold", FormatNumber(displayPrice)));
                num++;
            }
        }

        if (sellableItems.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("ui.no_weapons_to_sell"));
            await Pause();
            return;
        }

        terminal.WriteLine("");
        terminal.Write(Loc.Get("weapon_shop.sell_prompt"));
        var input = (await terminal.GetInput("")).Trim().ToUpper();

        if (input == "A")
        {
            // Sell all weapons/shields from backpack (not equipped items)
            var sellable = currentPlayer.Inventory
                .Where(i => i.IsIdentified && !i.IsCursed &&
                       (i.Type == ObjType.Weapon || i.Type == ObjType.Shield))
                .ToList();

            if (sellable.Count == 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("weapon_shop.no_sellable"));
                await Pause();
                return;
            }

            long totalGold = sellable.Sum(i => (long)((i.Value / 2) * fenceModifier));
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("weapon_shop.bulk_sell_confirm", sellable.Count, FormatNumber(totalGold)));
            var bulkConfirm = (await terminal.GetInput("")).Trim().ToUpper();

            if (GameConfig.IsAffirmative(bulkConfirm))
            {
                foreach (var item in sellable)
                    currentPlayer.Inventory.Remove(item);
                currentPlayer.Gold += totalGold;
                currentPlayer.Statistics.RecordSale(totalGold);
                DebugLogger.Instance.LogInfo("GOLD", $"SHOP SELL: {currentPlayer.DisplayName} sold {sellable.Count} weapons for {totalGold:N0}g (gold now {currentPlayer.Gold:N0})");
                currentPlayer.RecalculateStats();

                terminal.SetColor("bright_green");
                terminal.WriteLine("");
                terminal.WriteLine(sellable.Count > 1 ? Loc.Get("shop.sold_bulk", sellable.Count, FormatNumber(totalGold)) : Loc.Get("shop.sold_bulk_one", sellable.Count, FormatNumber(totalGold)));
            }
            await Pause();
            return;
        }

        if (input == "F")
        {
            // Filtered sell - sell weapons/shields matching player-chosen criteria
            var weaponTypes = new[] { ObjType.Weapon, ObjType.Shield };
            await FilteredSellFromBackpack(weaponTypes, fenceModifier);
            return;
        }

        if (!int.TryParse(input, out int sellChoice) || sellChoice < 1 || sellChoice > sellableItems.Count)
        {
            return;
        }

        var selected = sellableItems[sellChoice - 1];
        long price = (long)((selected.value / 2) * fenceModifier);

        if (selected.isCursed)
        {
            terminal.SetColor("red");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("shop.cursed_cannot_sell", selected.name));
            await Pause();
            return;
        }

        terminal.Write(Loc.Get("weapon_shop.sell_confirm", selected.name, FormatNumber(price)));

        var confirm = await terminal.GetInput("");
        if (GameConfig.IsAffirmative(confirm))
        {
            if (selected.isEquipped && selected.slot.HasValue)
            {
                // Unequip and sell equipped item
                currentPlayer.UnequipSlot(selected.slot.Value);
            }
            else if (selected.invIndex.HasValue)
            {
                // Remove from inventory
                currentPlayer.Inventory.RemoveAt(selected.invIndex.Value);
            }

            currentPlayer.Gold += price;
            currentPlayer.Statistics.RecordSale(price);
            DebugLogger.Instance.LogInfo("GOLD", $"SHOP SELL: {currentPlayer.DisplayName} sold weapon for {price:N0}g (gold now {currentPlayer.Gold:N0})");
            currentPlayer.RecalculateStats();

            terminal.SetColor("bright_green");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("shop.sold_single", selected.name, FormatNumber(price)));
        }

        await Pause();
    }

    /// <summary>
    /// Reforge the player's equipped main-hand weapon.
    /// Rerolls stat bonuses within +/-15% variance of current values,
    /// with a 20% chance to upgrade rarity by one tier (max Artifact).
    /// Cost scales quadratically: level * level * 50 gold.
    /// </summary>
    private async Task ReforgeWeapon()
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("weapon_shop.reforge_title"), "bright_magenta");
        terminal.WriteLine("");

        // issue #112: reforge the player's own copy, never the shop's shared template
        var weapon = currentPlayer.EnsureOwnEquipmentCopy(EquipmentSlot.MainHand);
        if (weapon == null)
        {
            terminal.WriteLine(Loc.Get("weapon_shop.reforge_no_weapon"), "red");
            await terminal.PressAnyKey();
            return;
        }

        long cost = ReforgeCost(currentPlayer.Level);

        // v1.1.7: three reforges a day. The anvil was unlimited and nearly free at low level.
        if (currentPlayer.ReforgesToday >= GameConfig.MaxReforgesPerDay)
        {
            terminal.WriteLine(Loc.Get("weapon_shop.reforge_daily_limit", GameConfig.MaxReforgesPerDay), "yellow");
            await terminal.PressAnyKey();
            return;
        }

        // v1.1.7: a reforge cannot push a weapon past what a smith could do for someone of this
        // level. A weapon already beyond it (a lucky drop carried by a lower-level character) is
        // refused, never shrunk.
        int powerBound = ReforgePowerBound(currentPlayer.Level);
        if (weapon.WeaponPower > powerBound)
        {
            terminal.WriteLine(Loc.Get("weapon_shop.reforge_beyond_skill", shopkeeperName), "yellow");
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("weapon_shop.reforge_desc", shopkeeperName));
        terminal.WriteLine("");

        // Show current weapon stats
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("weapon_shop.reforge_current"));
        terminal.SetColor(weapon.GetRarityColor());
        terminal.Write($"  {weapon.Name}");
        WriteEquipmentStatSummary(weapon);
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine($"  {Loc.Get("weapon_shop.reforge_rarity")}: {weapon.Rarity}");
        terminal.WriteLine("");

        // Show cost
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("weapon_shop.reforge_cost", $"{cost:N0}"));
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("weapon_shop.reforge_gold", $"{currentPlayer.Gold:N0}"));
        terminal.WriteLine("");

        if (currentPlayer.Gold < cost)
        {
            terminal.WriteLine(Loc.Get("ui.not_enough_gold"), "red");
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("weapon_shop.reforge_warning"));
        terminal.WriteLine("");

        var confirm = await terminal.GetInput(Loc.Get("weapon_shop.reforge_confirm"));
        if (!GameConfig.IsAffirmative(confirm))
        {
            terminal.WriteLine(Loc.Get("ui.cancelled"), "gray");
            await terminal.PressAnyKey();
            return;
        }

        // v1.1.7: the roll is computed and committed as one step, before any awaited animation, so a
        // disconnect cannot separate the payment, the daily count, and the result.
        var before = weapon.Clone();
        var reforged = RollReforge(weapon, currentPlayer.Level, Random.Shared, out bool rarityUpgraded);
        currentPlayer.Gold -= cost;
        currentPlayer.Statistics?.RecordGoldSpent(cost);
        currentPlayer.ReforgesToday++;
        ApplyReforge(weapon, reforged);
        currentPlayer.RecalculateStats();
        DebugLogger.Instance.LogInfo("GOLD", $"REFORGE: {currentPlayer.DisplayName} Lv{currentPlayer.Level} paid {cost:N0}g, power {before.WeaponPower:N0}->{weapon.WeaponPower:N0}, value {before.Value:N0}->{weapon.Value:N0}, rarity {before.Rarity}->{weapon.Rarity}, today {currentPlayer.ReforgesToday}/{GameConfig.MaxReforgesPerDay}");

        // Show reforging animation
        terminal.WriteLine("");
        terminal.SetColor("bright_magenta");
        terminal.WriteLine(Loc.Get("weapon_shop.reforge_working", shopkeeperName));
        await Task.Delay(1500);

        if (rarityUpgraded)
        {
            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("weapon_shop.reforge_rarity_up", reforged.Rarity.ToString()));
        }

        terminal.WriteLine("");

        // Show comparison
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("weapon_shop.reforge_result"));
        terminal.SetColor(reforged.GetRarityColor());
        terminal.Write($"  {reforged.Name}");
        WriteEquipmentStatSummary(reforged);
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine($"  {Loc.Get("weapon_shop.reforge_rarity")}: {reforged.Rarity}");
        terminal.WriteLine("");

        // Show stat changes
        ShowStatChange(Loc.Get("ui.stat_wp"), before.WeaponPower, reforged.WeaponPower);
        ShowStatChange(Loc.Get("ui.stat_str"), before.StrengthBonus, reforged.StrengthBonus);
        ShowStatChange(Loc.Get("ui.stat_dex"), before.DexterityBonus, reforged.DexterityBonus);
        ShowStatChange(Loc.Get("ui.stat_con"), before.ConstitutionBonus, reforged.ConstitutionBonus);
        ShowStatChange(Loc.Get("ui.stat_int"), before.IntelligenceBonus, reforged.IntelligenceBonus);
        ShowStatChange(Loc.Get("ui.stat_wis"), before.WisdomBonus, reforged.WisdomBonus);
        ShowStatChange(Loc.Get("ui.stat_cha"), before.CharismaBonus, reforged.CharismaBonus);
        ShowStatChange(Loc.Get("ui.stat_agi"), before.AgilityBonus, reforged.AgilityBonus);
        ShowStatChange(Loc.Get("ui.stat_crit"), before.CriticalChanceBonus, reforged.CriticalChanceBonus);
        ShowStatChange(Loc.Get("ui.stat_leech"), before.LifeSteal, reforged.LifeSteal);
        terminal.WriteLine("");

        // v0.60.11: a reforge commits unconditionally; v1.1.7: it was committed above, before the animation.


        terminal.WriteLine("");
        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("weapon_shop.reforge_accepted", shopkeeperName));

        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Reroll a stat value with +/-variance around the original, multiplied by rarityBoost.
    /// For stats that are 0, they stay 0 (no new stats are invented).
    /// Negative stats (from cursed items) can become more or less negative.
    /// </summary>
    /// <summary>v1.1.7: level squared times the multiplier, the endgame surcharge, and a floor so level 1 is not free.</summary>
    internal static long ReforgeCost(int level)
    {
        long cost = (long)level * level * GameConfig.ReforgeCostMultiplier;
        if (level > GameConfig.ReforgeEndgameThreshold)
            cost += (long)(level - GameConfig.ReforgeEndgameThreshold) * GameConfig.ReforgeEndgameSurchargePerLevel;
        return Math.Max(GameConfig.ReforgeMinCost, cost);
    }

    /// <summary>
    /// v1.1.7: the most weapon power a reforge may produce for a character of this level: one and a
    /// half times the strongest weapon the loot tables can drop at that level (LootGenerator: top
    /// template 135 x (1 + level/80) x Artifact 4.0 x 1.15 variance), never above the item bound.
    /// 815 x 1.5 at level 25, 1,397 x 1.5 at level 100.
    /// </summary>
    internal static int ReforgePowerBound(int level)
    {
        double legit = GameConfig.LootTopWeaponBasePower * (1.0 + Math.Max(1, level) / 80.0) * GameConfig.LootArtifactPowerMult * 1.15;
        return (int)Math.Min(GameConfig.MaxItemPower, legit * GameConfig.ReforgePowerBoundFactor);
    }

    /// <summary>
    /// v1.1.7: the reforge as a pure function of the weapon, the level and the dice. Inputs are
    /// clamped first; every roll is bounded before its cast to int; power is bounded by level; value
    /// follows the formula in long arithmetic (the old int multiply overflowed, and a running
    /// maximum ratcheted value upward on every lucky roll).
    /// </summary>
    internal static Equipment RollReforge(Equipment weapon, int level, Random rng, out bool rarityUpgraded)
    {
        var reforged = weapon.Clone();
        reforged.Id = weapon.Id; // same ID so the equipped reference stays valid
        reforged.ClampStats();

        rarityUpgraded = false;
        if (weapon.Rarity < EquipmentRarity.Artifact && rng.NextDouble() < GameConfig.ReforgeUpgradeChance)
        {
            reforged.Rarity = weapon.Rarity + 1;
            rarityUpgraded = true;
        }
        double variance = GameConfig.ReforgeVariance;
        double boost = rarityUpgraded ? 1.15 : 1.0;
        int stat = GameConfig.MaxItemStatBonus, pct = GameConfig.MaxItemPercent;

        reforged.WeaponPower = RerollStat(reforged.WeaponPower, variance, boost, rng, Math.Min(ReforgePowerBound(level), GameConfig.MaxItemPower), minValue: 1);
        reforged.StrengthBonus = RerollStat(reforged.StrengthBonus, variance, boost, rng, stat);
        reforged.DexterityBonus = RerollStat(reforged.DexterityBonus, variance, boost, rng, stat);
        reforged.ConstitutionBonus = RerollStat(reforged.ConstitutionBonus, variance, boost, rng, stat);
        reforged.IntelligenceBonus = RerollStat(reforged.IntelligenceBonus, variance, boost, rng, stat);
        reforged.WisdomBonus = RerollStat(reforged.WisdomBonus, variance, boost, rng, stat);
        reforged.CharismaBonus = RerollStat(reforged.CharismaBonus, variance, boost, rng, stat);
        reforged.AgilityBonus = RerollStat(reforged.AgilityBonus, variance, boost, rng, stat);
        reforged.MaxHPBonus = RerollStat(reforged.MaxHPBonus, variance, boost, rng, GameConfig.MaxItemVitalBonus);
        reforged.MaxManaBonus = RerollStat(reforged.MaxManaBonus, variance, boost, rng, GameConfig.MaxItemVitalBonus);
        reforged.DefenceBonus = RerollStat(reforged.DefenceBonus, variance, boost, rng, stat);
        reforged.StaminaBonus = RerollStat(reforged.StaminaBonus, variance, boost, rng, stat);
        reforged.CriticalChanceBonus = RerollStat(reforged.CriticalChanceBonus, variance, boost, rng, pct);
        reforged.CriticalDamageBonus = RerollStat(reforged.CriticalDamageBonus, variance, boost, rng, stat);
        reforged.LifeSteal = RerollStat(reforged.LifeSteal, variance, boost, rng, pct);
        reforged.MagicResistance = RerollStat(reforged.MagicResistance, variance, boost, rng, pct);
        reforged.PoisonDamage = RerollStat(reforged.PoisonDamage, variance, boost, rng, stat);

        reforged.Value = ReforgeValue(reforged.WeaponPower, reforged.Rarity);
        reforged.ClampStats();
        return reforged;
    }

    /// <summary>v1.1.7: power x 15 x (1 + rarity/2) in long arithmetic, at least 1, at most the value bound. No running maximum.</summary>
    internal static long ReforgeValue(int weaponPower, EquipmentRarity rarity)
    {
        double value = (long)Math.Max(0, weaponPower) * 15L * (1.0 + (int)rarity * 0.5);
        return (long)Math.Clamp(value, 1, GameConfig.MaxItemValue);
    }

    internal static void ApplyReforge(Equipment weapon, Equipment reforged)
    {
        weapon.WeaponPower = reforged.WeaponPower;
        weapon.Rarity = reforged.Rarity;
        weapon.StrengthBonus = reforged.StrengthBonus;
        weapon.DexterityBonus = reforged.DexterityBonus;
        weapon.ConstitutionBonus = reforged.ConstitutionBonus;
        weapon.IntelligenceBonus = reforged.IntelligenceBonus;
        weapon.WisdomBonus = reforged.WisdomBonus;
        weapon.CharismaBonus = reforged.CharismaBonus;
        weapon.AgilityBonus = reforged.AgilityBonus;
        weapon.MaxHPBonus = reforged.MaxHPBonus;
        weapon.MaxManaBonus = reforged.MaxManaBonus;
        weapon.DefenceBonus = reforged.DefenceBonus;
        weapon.StaminaBonus = reforged.StaminaBonus;
        weapon.CriticalChanceBonus = reforged.CriticalChanceBonus;
        weapon.CriticalDamageBonus = reforged.CriticalDamageBonus;
        weapon.LifeSteal = reforged.LifeSteal;
        weapon.MagicResistance = reforged.MagicResistance;
        weapon.PoisonDamage = reforged.PoisonDamage;
        weapon.Value = reforged.Value;
        weapon.ClampStats();
    }

    internal static int RerollStat(int original, double variance, double rarityBoost, Random rng, int bound, int minValue = int.MinValue)
    {
        if (original == 0) return 0;

        double baseValue = original * rarityBoost;
        double range = Math.Abs(baseValue) * variance;
        double rolled = baseValue + (rng.NextDouble() * 2 - 1) * range;

        // v1.1.7: bound the double before the cast; a cast of an out-of-range double is undefined
        int result = (int)Math.Round(Math.Clamp(rolled, -(double)bound, bound));

        // Preserve sign: positive stays positive, negative stays negative
        if (original > 0 && result < 1) result = 1;
        if (original < 0 && result > -1) result = -1;
        if (minValue != int.MinValue && result < minValue) result = minValue;
        return result;
    }

    /// <summary>
    /// Display a stat change with color coding (green for improvement, red for worse).
    /// Only shows stats that have a non-zero value.
    /// </summary>
    private void ShowStatChange(string label, int oldValue, int newValue)
    {
        if (oldValue == 0 && newValue == 0) return;

        int diff = newValue - oldValue;
        string color = diff > 0 ? "bright_green" : diff < 0 ? "red" : "gray";
        string sign = diff > 0 ? "+" : "";

        terminal.SetColor("gray");
        terminal.Write($"  {label}: {oldValue} -> ");
        terminal.SetColor(color);
        terminal.WriteLine($"{newValue} ({sign}{diff})");
    }

    private async Task AutoBuyBestWeapon()
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("weapon_shop.auto_buy"), "bright_cyan");
        terminal.WriteLine("");

        var currentWeapon = currentPlayer.GetEquipment(EquipmentSlot.MainHand);
        int currentPow = currentWeapon?.WeaponPower ?? 0;

        // Get all affordable upgrades sorted by power (best first)
        // Filter by CanEquip to exclude items the player can't use (level/stat requirements)
        var affordableWeapons = EquipmentDatabase.GetShopWeapons(WeaponHandedness.OneHanded)
            .Concat(EquipmentDatabase.GetShopWeapons(WeaponHandedness.TwoHanded))
            .Where(w => w.WeaponPower > currentPow)
            .Where(w => w.CanEquip(currentPlayer, out _))
            .Where(w => w.Value <= currentPlayer.Gold)
            .Where(w => !w.RequiresGood || currentPlayer.Chivalry > currentPlayer.Darkness)
            .Where(w => !w.RequiresEvil || currentPlayer.Darkness > currentPlayer.Chivalry)
            .OrderByDescending(w => w.WeaponPower)
            .ThenBy(w => w.Value)
            .ToList();

        if (affordableWeapons.Count == 0)
        {
            terminal.WriteLine("");
            if (currentWeapon != null)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("weapon_shop.autobuy_already_best", currentWeapon.Name, currentPow));
                terminal.WriteLine(Loc.Get("weapon_shop.autobuy_best_afford", FormatNumber(currentPlayer.Gold)));
            }
            else
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("weapon_shop.autobuy_no_affordable", FormatNumber(currentPlayer.Gold)));
            }
            terminal.WriteLine("");
            await Pause();
            return;
        }

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("weapon_shop.autobuy_current", currentWeapon?.Name ?? Loc.Get("ui.none"), currentPow));
        terminal.WriteLine(Loc.Get("weapon_shop.autobuy_your_gold", FormatNumber(currentPlayer.Gold)));
        terminal.WriteLine("");

        // Iterate through weapons, letting player choose
        int weaponIndex = 0;
        bool purchased = false;

        while (weaponIndex < affordableWeapons.Count)
        {
            // Re-check affordability (gold may have changed)
            var weapon = affordableWeapons[weaponIndex];
            long adjustedPrice = CityControlSystem.Instance.ApplyDiscount(weapon.Value, currentPlayer);
            // Apply faction discount (The Crown gets 10% off at shops)
            adjustedPrice = (long)(adjustedPrice * FactionSystem.Instance.GetShopPriceModifier());
            // Apply difficulty-based price multiplier
            adjustedPrice = DifficultySystem.ApplyShopPriceMultiplier(adjustedPrice);

            // Calculate total with tax
            var (abKingTax, abCityTax, abTotal) = CityControlSystem.CalculateTaxedPrice(adjustedPrice);

            if (abTotal > currentPlayer.Gold)
            {
                weaponIndex++;
                continue;
            }

            // Show the weapon offer
            WriteDivider(37, "bright_yellow");
            terminal.SetColor("cyan");
            terminal.WriteLine($"  {weapon.Name}");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("weapon_shop.autobuy_wp", weapon.WeaponPower, currentPow, weapon.WeaponPower - currentPow));
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("weapon_shop.autobuy_price", FormatNumber(adjustedPrice)));

            // Show tax breakdown
            CityControlSystem.Instance.DisplayTaxBreakdown(terminal, weapon.Name, adjustedPrice);

            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("weapon_shop.autobuy_gold_after", FormatNumber(currentPlayer.Gold - abTotal)));
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("weapon_shop.auto_buy_yes"));
            terminal.WriteLine(Loc.Get("weapon_shop.auto_buy_no"));
            terminal.WriteLine(Loc.Get("weapon_shop.auto_buy_skip"));
            terminal.WriteLine(Loc.Get("weapon_shop.auto_buy_cancel"));
            terminal.WriteLine("");
            terminal.Write(Loc.Get("ui.your_choice"));

            var choice = await terminal.GetInput("");
            terminal.WriteLine("");

            switch (choice.ToUpper().Trim())
            {
                case "Y":
                    // Purchase this weapon (total includes tax)
                    currentPlayer.Gold -= abTotal;
                    currentPlayer.Statistics.RecordPurchase(abTotal);
                    AchievementSystem.CheckAchievements(currentPlayer); // v0.61.3: immediate achievement check
                    // v1.1.1: the city tax used to be forwarded here, before the slot prompt and
                    // the equip check; a cancel refunded the price but the tax (which can land in
                    // the player's own bank) stayed. It is processed on success only, below.

                    // For one-handed weapons, ask which slot to use
                    EquipmentSlot? targetSlot = null;
                    if (Character.RequiresSlotSelection(weapon))
                    {
                        targetSlot = await PromptForWeaponSlot();
                        if (targetSlot == null)
                        {
                            // Player cancelled - refund gold and undo stats
                            currentPlayer.Gold += abTotal;
                            currentPlayer.Statistics.TotalGoldSpent -= abTotal;
                            currentPlayer.Statistics.TotalItemsBought--;
                            terminal.SetColor("yellow");
                            terminal.WriteLine(Loc.Get("weapon_shop.purchase_cancelled"));
                            await Pause();
                            return;
                        }
                    }

                    if (currentPlayer.EquipItem(weapon, targetSlot, out string message))
                    {
                        terminal.SetColor("bright_green");
                        terminal.WriteLine(Loc.Get("weapon_shop.autobuy_purchased", weapon.Name));
                        if (!string.IsNullOrEmpty(message))
                        {
                            terminal.SetColor("gray");
                            terminal.WriteLine(message);
                        }
                        purchased = true;
                        currentPlayer.RecalculateStats();
                        CityControlSystem.Instance.ProcessSaleTax(adjustedPrice);

                        // Check for equipment quest completion
                        QuestSystem.OnEquipmentPurchased(currentPlayer, weapon);
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine(Loc.Get("weapon_shop.autobuy_failed", message));
                        currentPlayer.Gold += abTotal;
                    }

                    // Done with auto-buy after purchasing
                    if (purchased)
                    {
                        await SaveSystem.Instance.AutoSave(currentPlayer);
                    }
                    await Pause();
                    return;

                case "N":
                    // Skip to next weapon option
                    weaponIndex++;
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("weapon_shop.skipped"));
                    terminal.WriteLine("");
                    break;

                case "S":
                    // Skip weapon slot entirely
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("weapon_shop.skipping_slot"));
                    await Pause();
                    return;

                case "C":
                    // Cancel entirely
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("weapon_shop.auto_cancelled"));
                    await Pause();
                    return;

                default:
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("weapon_shop.auto_invalid"));
                    break;
            }
        }

        // No more options
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("weapon_shop.autobuy_no_more"));
        await Pause();
    }

    private async Task Pause()
    {
        terminal.SetColor("gray");
        terminal.Write(Loc.Get("ui.press_enter"));
        await terminal.GetInput("");
    }

    /// <summary>
    /// Prompt player to choose which hand to equip a one-handed weapon in
    /// </summary>
    private async Task<EquipmentSlot?> PromptForWeaponSlot()
    {
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("weapon_shop.one_hand_where"));
        terminal.WriteLine("");

        // Show current equipment in both slots
        var mainHandItem = currentPlayer.GetEquipment(EquipmentSlot.MainHand);
        var offHandItem = currentPlayer.GetEquipment(EquipmentSlot.OffHand);

        terminal.SetColor("white");
        terminal.Write(Loc.Get("weapon_shop.one_hand_main"));
        if (mainHandItem != null)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(mainHandItem.Name);
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("ui.empty"));
        }

        terminal.SetColor("white");
        terminal.Write(Loc.Get("weapon_shop.one_hand_off"));
        if (offHandItem != null)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(offHandItem.Name);
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("ui.empty"));
        }

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("weapon_shop.one_hand_cancel"));
        terminal.WriteLine("");

        terminal.Write(Loc.Get("ui.your_choice"));
        var slotChoice = await terminal.GetInput("");

        return slotChoice.ToUpper() switch
        {
            "M" => EquipmentSlot.MainHand,
            "O" => EquipmentSlot.OffHand,
            _ => null // Cancel
        };
    }

    private static string FormatNumber(long value)
    {
        return value.ToString("N0");
    }

    /// <summary>
    /// Phase 4 finish: emit paginated browse state (Pattern C). JS renders
    /// item cards with affordability/level/class hints; click an item sends
    /// the on-page numeric index via stdin which routes to BuyItem. Pagination
    /// keys (P/N) and Return (R) handled in ProcessCategoryChoice unchanged.
    /// </summary>
    private void EmitShopBrowseState(string categoryName, WeaponCategory category, List<Equipment> items)
    {
        var player = GetCurrentPlayer();
        if (player == null) return;

        int startIndex = currentPage * ItemsPerPage;
        var pageItems = items.Skip(startIndex).Take(ItemsPerPage).ToList();
        int totalPages = (items.Count + ItemsPerPage - 1) / ItemsPerPage;
        bool isPrestige = player.Class >= CharacterClass.Tidesworn;

        var browseItems = new List<ElectronBridge.ShopBrowseItem>();
        for (int i = 0; i < pageItems.Count; i++)
        {
            var item = pageItems[i];
            bool canAfford = player.Gold >= item.Value;
            bool meetsLevel = player.Level >= item.MinLevel;
            bool meetsClass = isPrestige || item.ClassRestrictions == null || item.ClassRestrictions.Count == 0
                || item.ClassRestrictions.Contains(player.Class);

            var bonuses = new Dictionary<string, int>();
            if (item.StrengthBonus != 0) bonuses["STR"] = item.StrengthBonus;
            if (item.DefenceBonus != 0) bonuses["DEF"] = item.DefenceBonus;
            if (item.DexterityBonus != 0) bonuses["DEX"] = item.DexterityBonus;
            if (item.WisdomBonus != 0) bonuses["WIS"] = item.WisdomBonus;

            int power = category == WeaponCategory.Shields
                ? item.ShieldBonus
                : item.WeaponPower;

            browseItems.Add(new ElectronBridge.ShopBrowseItem
            {
                Key = (i + 1).ToString(),
                Name = item.Name,
                Slot = category == WeaponCategory.Shields ? "Shield" : "Weapon",
                Price = item.Value,
                Power = power,
                MinLevel = item.MinLevel,
                Rarity = "common",
                Affordable = canAfford,
                LevelOk = meetsLevel,
                ClassOk = meetsClass,
                Bonuses = bonuses.Count > 0 ? bonuses : null,
            });
        }

        ElectronBridge.EmitShopBrowse(
            shopName: Loc.Get("weapon_shop.header"),
            category: categoryName,
            currentPage: currentPage + 1,
            totalPages: Math.Max(1, totalPages),
            items: browseItems,
            playerGold: player.Gold);
    }

    /// <summary>
    /// Phase 4: emit Weapon Shop top-level category menu for Electron client.
    /// Pattern B for the entry; sub-screens (browse list, buy, sell, identify)
    /// still render text. Pattern C (full graphical shop) is a future phase.
    /// </summary>
    private void EmitElectronEvents()
    {
        var player = GetCurrentPlayer();
        if (player == null) return;

        ElectronBridge.EmitLocation(
            name: Loc.Get("weapon_shop.header"),
            description: "",
            timeOfDay: "");

        bool isManaClass = player is Player p && p.IsManaClass;
        ElectronBridge.EmitStats(
            hp: player.HP, maxHp: player.MaxHP,
            mana: isManaClass ? player.Mana : 0, maxMana: isManaClass ? player.MaxMana : 0,
            stamina: isManaClass ? 0 : player.Stamina, maxStamina: isManaClass ? 0 : player.BaseStamina,
            gold: player.Gold, level: player.Level,
            className: player.ClassName, raceName: player.Race.ToString(),
            playerName: player.DisplayName);

        var menu = new List<ElectronBridge.MenuItemData>
        {
            new() { Key = "1", Label = "One-Handed", Category = "browse", Icon = "sword" },
            new() { Key = "2", Label = "Two-Handed", Category = "browse", Icon = "greatsword" },
            new() { Key = "3", Label = "Bows", Category = "browse", Icon = "bow" },
            new() { Key = "4", Label = "Shields", Category = "browse", Icon = "shield" },
            new() { Key = "S", Label = "Sell Weapon", Category = "sell", Icon = "sell" },
            new() { Key = "I", Label = "Identify Item", Category = "service", Icon = "identify" },
            new() { Key = "R", Label = Loc.Get("ui.return"), Category = "navigate", Icon = "back" },
        };
        ElectronBridge.EmitMenu(menu);

        EmitNPCsInLocationToElectron();
    }
}
