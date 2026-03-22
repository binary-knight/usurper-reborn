using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ModelItem = global::Item;
using UsurperRemake.BBS;
using UsurperRemake.Systems;
using UsurperRemake.Utils;

namespace UsurperRemake.Locations;

/// <summary>
/// Player home – allows resting, item storage, viewing trophies and family.
/// Simplified port of Pascal HOME.PAS but supports core mechanics needed now.
/// Now includes romance/family features.
/// </summary>
public class HomeLocation : BaseLocation
{
    // Static chest storage per player id (real name is unique key)
    // Public so SaveSystem can serialize/restore chest contents
    public static readonly Dictionary<string, List<ModelItem>> PlayerChests = new();
    private List<ModelItem> Chest => PlayerChests[playerKey];
    private string playerKey;

    public HomeLocation() : base(GameLocation.Home, "Your Home", "Your humble abode – a safe haven to rest and prepare for adventures.")
    {
    }

    protected override void SetupLocation()
    {
        PossibleExits = new()
        {
            GameLocation.AnchorRoad
        };

        LocationActions = new()
        {
            "Rest and recover (R)",
            "Deposit item to chest (D)",
            "Withdraw item from chest (W)",
            "View stored items (L)",
            "View trophies & stats (T)",
            "View family (F)",
            "Spend time with spouse (P)",
            "Visit bedroom (B)",
            "Upgrade home (U)",
            "Status (S)",
            "Resurrect partner or lover (!)",
            "Return to town (Q)"
        };
    }

    public override async Task EnterLocation(Character player, TerminalEmulator term)
    {
        playerKey = (player is Player p ? p.RealName : player.Name2) ?? player.Name2;
        if (!PlayerChests.ContainsKey(playerKey))
            PlayerChests[playerKey] = new List<ModelItem>();
        await base.EnterLocation(player, term);
    }

    protected override void DisplayLocation()
    {
        if (IsBBSSession) { DisplayLocationBBS(); return; }

        terminal.ClearScreen();

        // Header
        WriteBoxHeader(Loc.Get("home.header"), "bright_cyan");
        terminal.WriteLine("");

        // Quick stats bar
        terminal.SetColor("gray");
        terminal.Write($"  {Loc.Get("home.stat_hp")}");
        terminal.SetColor(currentPlayer.HP < currentPlayer.MaxHP / 4 ? "red" : (currentPlayer.HP < currentPlayer.MaxHP / 2 ? "yellow" : "bright_green"));
        terminal.Write($"{currentPlayer.HP}/{currentPlayer.MaxHP}");
        terminal.SetColor("gray");
        if (currentPlayer.IsManaClass)
        {
            terminal.Write($"  |  {Loc.Get("home.stat_mana")}");
            terminal.SetColor("bright_blue");
            terminal.Write($"{currentPlayer.Mana}/{currentPlayer.MaxMana}");
        }
        else
        {
            terminal.Write($"  |  {Loc.Get("home.stat_stamina")}");
            terminal.SetColor("bright_yellow");
            terminal.Write($"{currentPlayer.CurrentCombatStamina}/{currentPlayer.MaxCombatStamina}");
        }
        terminal.SetColor("gray");
        terminal.Write($"  |  {Loc.Get("home.stat_gold")}");
        terminal.SetColor("bright_yellow");
        terminal.Write($"{currentPlayer.Gold:N0}");
        terminal.SetColor("gray");
        terminal.Write($"  |  {Loc.Get("home.stat_potions")}");
        terminal.SetColor("bright_green");
        terminal.WriteLine($"{currentPlayer.Healing}");
        terminal.WriteLine("");

        // Dynamic description based on all upgrades
        terminal.SetColor("white");
        // Living quarters base description
        switch (currentPlayer.HomeLevel)
        {
            case 0:
                terminal.Write(Loc.Get("home.desc_level0"));
                break;
            case 1:
                terminal.Write(Loc.Get("home.desc_level1"));
                break;
            case 2:
                terminal.Write(Loc.Get("home.desc_level2"));
                break;
            case 3:
                terminal.Write(Loc.Get("home.desc_level3"));
                break;
            case 4:
                terminal.Write(Loc.Get("home.desc_level4"));
                break;
            default:
                terminal.Write(Loc.Get("home.desc_level5"));
                break;
        }
        // Bed detail
        switch (currentPlayer.BedLevel)
        {
            case 0: terminal.Write(Loc.Get("home.bed_level0")); break;
            case 1: terminal.Write(Loc.Get("home.bed_level1")); break;
            case 2: terminal.Write(Loc.Get("home.bed_level2")); break;
            case 3: terminal.Write(Loc.Get("home.bed_level3")); break;
            case 4: terminal.Write(Loc.Get("home.bed_level4")); break;
            default: terminal.Write(Loc.Get("home.bed_level5")); break;
        }
        // Hearth detail
        switch (currentPlayer.HearthLevel)
        {
            case 0: terminal.Write(Loc.Get("home.hearth_level0")); break;
            case 1: terminal.Write(Loc.Get("home.hearth_level1")); break;
            case 2: terminal.Write(Loc.Get("home.hearth_level2")); break;
            case 3: terminal.Write(Loc.Get("home.hearth_level3")); break;
            case 4: terminal.Write(Loc.Get("home.hearth_level4")); break;
            default: terminal.Write(Loc.Get("home.hearth_level5")); break;
        }
        terminal.WriteLine("");
        // Chest and garden on second line if upgraded
        var extras = new List<string>();
        if (currentPlayer.ChestLevel > 0)
            extras.Add(ChestNames[Math.Clamp(currentPlayer.ChestLevel, 0, 5)].ToLower());
        if (currentPlayer.GardenLevel > 0)
            extras.Add(GardenNames[Math.Clamp(currentPlayer.GardenLevel, 0, 5)].ToLower());
        if (currentPlayer.HasStudy)
            extras.Add(Loc.Get("home.extra_study"));
        if (currentPlayer.HasServants)
            extras.Add(Loc.Get("home.extra_servants"));
        if (extras.Count > 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("home.you_also_have", string.Join(", ", extras)));
        }
        terminal.WriteLine("");

        // Show storage & rest info
        int maxRests = GameConfig.HomeRestsPerDay[Math.Clamp(currentPlayer.HomeLevel, 0, 5)];
        int restsLeft = Math.Max(0, maxRests - currentPlayer.HomeRestsToday);
        int recoveryPct = (int)(GameConfig.HomeRecoveryPercent[Math.Clamp(currentPlayer.HomeLevel, 0, 5)] * 100);
        terminal.SetColor("gray");
        terminal.Write($"  {Loc.Get("home.stat_rest")}");
        terminal.SetColor(restsLeft > 0 ? "bright_green" : "red");
        terminal.Write($"{restsLeft}/{maxRests} today ({recoveryPct}%)");
        if (currentPlayer.ChestLevel > 0)
        {
            int maxCapacity = GameConfig.ChestCapacity[Math.Clamp(currentPlayer.ChestLevel, 0, 5)];
            terminal.SetColor("gray");
            terminal.Write($"  |  {Loc.Get("home.stat_chest")}");
            terminal.SetColor("cyan");
            terminal.Write($"{Chest.Count}/{maxCapacity}");
        }
        terminal.SetColor("gray");
        terminal.Write($"  |  {Loc.Get("home.stat_potions")}");
        terminal.SetColor("bright_green");
        terminal.WriteLine($"{currentPlayer.Healing}");
        terminal.WriteLine("");

        // Show family info if applicable
        var romance = RomanceTracker.Instance;
        var children = FamilySystem.Instance.GetChildrenOf(currentPlayer);

        // Check which partners are actually at home
        var partnersAtHome = new List<string>();
        var partnersAway = new List<(string name, string location)>();

        foreach (var spouse in romance.Spouses)
        {
            var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == spouse.NPCId);
            var name = npc?.Name ?? spouse.NPCName;
            if (npc != null && npc.IsAlive == true && (npc.CurrentLocation == "Home" || npc.CurrentLocation == "Your Home"))
            {
                partnersAtHome.Add(name);
            }
            else if (npc != null && npc.IsAlive == true)
            {
                partnersAway.Add((name, npc.CurrentLocation));
            }
        }

        foreach (var lover in romance.CurrentLovers)
        {
            var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == lover.NPCId);
            var name = npc?.Name ?? lover.NPCName;
            if (npc != null && npc.IsAlive == true && (npc.CurrentLocation == "Home" || npc.CurrentLocation == "Your Home"))
            {
                partnersAtHome.Add(name);
            }
            else if (npc != null && npc.IsAlive == true )
            {
                partnersAway.Add((name, npc.CurrentLocation));
            }
        }

        if (partnersAtHome.Count > 0 || partnersAway.Count > 0 || children.Count > 0)
        {
            if (partnersAtHome.Count > 0)
            {
                terminal.SetColor("bright_magenta");
                terminal.Write(Loc.Get("home.partners_here", string.Join(" and ", partnersAtHome), partnersAtHome.Count == 1 ? Loc.Get("home.partners_is") : Loc.Get("home.partners_are")));
                if (children.Count > 0)
                {
                    terminal.SetColor("bright_yellow");
                    terminal.Write(Loc.Get("home.children_count", children.Count, children.Count != 1 ? Loc.Get("home.children_ren") : ""));
                }
                terminal.WriteLine(".");
            }
            else if (children.Count > 0)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("home.children_here", children.Count, children.Count != 1 ? Loc.Get("home.children_ren_are") : Loc.Get("home.children_is")));
            }

            if (partnersAway.Count > 0)
            {
                terminal.SetColor("gray");
                foreach (var (name, loc) in partnersAway)
                {
                    terminal.WriteLine(Loc.Get("home.partner_at_location", name, loc));
                }
            }
            terminal.WriteLine("");
        }

        // Menu
        ShowHomeMenu();

        // Status line
        ShowStatusLine();
    }

    private void ShowHomeMenu()
    {
        bool hasChest = currentPlayer.ChestLevel > 0;
        bool hasGarden = currentPlayer.GardenLevel > 0;
        bool hasTrophies = currentPlayer.HasTrophyRoom;

        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"--- {Loc.Get("home.activities")} ---");
        terminal.WriteLine("");

        // Row 1: Core actions
        WriteMenuCol(" ", "E", Loc.Get("home.rest"), true);
        WriteMenuCol("", "U", Loc.Get("home.upgrades"), true);
        WriteMenuNL("", "S", Loc.Get("dungeon.status"), true);

        // Row 2: Chest operations (dimmed if no chest)
        WriteMenuCol(" ", "D", Loc.Get("home.deposit"), hasChest);
        WriteMenuCol("", "W", Loc.Get("home.withdraw"), hasChest);
        WriteMenuNL("", "L", Loc.Get("home.list_chest"), hasChest);

        // Row 3: Garden, Herbs, Trophies, Family
        WriteMenuCol(" ", "A", Loc.Get("home.gather_herbs"), hasGarden);
        WriteMenuCol("", "J", Loc.Get("home.use_herb"), currentPlayer.TotalHerbCount > 0);
        WriteMenuNL("", "T", Loc.Get("home.trophies"), hasTrophies);

        var hasChildrenAtHome = FamilySystem.Instance.GetChildrenOf(currentPlayer)
            .Any(c => c.Age < FamilySystem.ADULT_AGE && c.Location == GameConfig.ChildLocationHome && !c.Deleted);
        WriteMenuCol(" ", "F", Loc.Get("home.family"), true);
        WriteMenuNL("", "C", Loc.Get("home.children_interact"), hasChildrenAtHome);

        // Row 4: Romance
        WriteMenuCol(" ", "P", Loc.Get("home.partner"), true);
        WriteMenuCol("", "B", Loc.Get("home.bedroom"), true);
        WriteMenuNL("", "X", Loc.Get("home.resurrect"), true);

        // Row 5: Items
        WriteMenuCol(" ", "I", Loc.Get("dungeon.inventory"), true);
        WriteMenuCol("", "G", Loc.Get("home.gear_partner"), true);
        WriteMenuNL("", "H", Loc.Get("home.heal_potion"), true);

        terminal.WriteLine("");

        // Sleep or Wait
        if (!UsurperRemake.BBS.DoorMode.IsOnlineMode && currentPlayer != null)
        {
            if (IsScreenReader)
            {
                string sleepLabel = DailySystemManager.CanRestForNight(currentPlayer)
                    ? Loc.Get("home.sleep")
                    : Loc.Get("home.wait_night");
                terminal.WriteLine($" Z. {sleepLabel}");
            }
            else
            {
                terminal.SetColor("darkgray");
                terminal.Write(" [");
                terminal.SetColor("bright_yellow");
                terminal.Write("Z");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                if (DailySystemManager.CanRestForNight(currentPlayer))
                {
                    terminal.SetColor("bright_green");
                    terminal.WriteLine(Loc.Get("home.sleep"));
                }
                else
                {
                    terminal.SetColor("dark_cyan");
                    terminal.WriteLine(Loc.Get("home.wait_night"));
                }
            }
        }
        else if (UsurperRemake.BBS.DoorMode.IsOnlineMode && currentPlayer != null && currentPlayer.HasReinforcedDoor)
        {
            if (IsScreenReader)
            {
                terminal.WriteLine($" Z. {Loc.Get("home.sleep_safe")}");
            }
            else
            {
                terminal.SetColor("darkgray");
                terminal.Write(" [");
                terminal.SetColor("bright_yellow");
                terminal.Write("Z");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("home.sleep_safe"));
            }
        }

        // Navigation row
        WriteMenuNL(" ", "R", Loc.Get("home.return_label"), true);

        terminal.WriteLine("");
    }

    // Write a menu option padded to a fixed 26-char column width
    private void WriteMenuOption(string prefix, string key, string label, bool available, int width)
    {
        if (IsScreenReader)
        {
            // Plain text: "  E. Rest & Recover" padded to column width
            string plain = $"{prefix}{key}. {label}";
            terminal.Write(plain.PadRight(Math.Max(plain.Length, width)));
            return;
        }
        string keyColor = available ? "bright_yellow" : "dark_gray";
        string textColor = available ? "white" : "dark_gray";
        terminal.Write(prefix);
        terminal.SetColor("dark_gray");
        terminal.Write("[");
        terminal.SetColor(keyColor);
        terminal.Write(key);
        terminal.SetColor("dark_gray");
        terminal.Write("]");
        terminal.SetColor(textColor);
        // [X] = 3 chars, label needs to fill remaining width minus prefix
        int labelWidth = width - prefix.Length - 3;
        terminal.Write(label.PadRight(Math.Max(0, labelWidth)));
    }

    private void WriteMenuCol(string prefix, string key, string label, bool available)
        => WriteMenuOption(prefix, key, label, available, 26);

    private void WriteMenuNL(string prefix, string key, string label, bool available)
    {
        if (IsScreenReader)
        {
            terminal.WriteLine($"{prefix}{key}. {label}");
            return;
        }
        string keyColor = available ? "bright_yellow" : "dark_gray";
        string textColor = available ? "white" : "dark_gray";
        terminal.Write(prefix);
        terminal.SetColor("dark_gray");
        terminal.Write("[");
        terminal.SetColor(keyColor);
        terminal.Write(key);
        terminal.SetColor("dark_gray");
        terminal.Write("]");
        terminal.SetColor(textColor);
        terminal.WriteLine($" {label}");
    }

    /// <summary>
    /// Compact BBS display for 80x25 terminals.
    /// </summary>
    private void DisplayLocationBBS()
    {
        terminal.ClearScreen();
        ShowBBSHeader(Loc.Get("home.header"));

        // 1-line description based on home level
        terminal.SetColor("white");
        string bbsDesc = currentPlayer.HomeLevel switch
        {
            0 => Loc.Get("home.bbs_desc_level0"),
            1 => Loc.Get("home.bbs_desc_level1"),
            2 => Loc.Get("home.bbs_desc_level2"),
            3 => Loc.Get("home.bbs_desc_level3"),
            4 => Loc.Get("home.bbs_desc_level4"),
            _ => Loc.Get("home.bbs_desc_level5")
        };
        terminal.WriteLine(bbsDesc);

        // Compact info
        int bbsMaxRests = GameConfig.HomeRestsPerDay[Math.Clamp(currentPlayer.HomeLevel, 0, 5)];
        int bbsRestsLeft = Math.Max(0, bbsMaxRests - currentPlayer.HomeRestsToday);
        int bbsRecovery = (int)(GameConfig.HomeRecoveryPercent[Math.Clamp(currentPlayer.HomeLevel, 0, 5)] * 100);
        terminal.SetColor("gray");
        terminal.Write(Loc.Get("home.bbs_rest_label", bbsRestsLeft, bbsMaxRests, bbsRecovery));
        if (currentPlayer.ChestLevel > 0)
        {
            int maxCap = GameConfig.ChestCapacity[Math.Clamp(currentPlayer.ChestLevel, 0, 5)];
            terminal.Write(Loc.Get("home.bbs_chest_label", Chest.Count, maxCap));
        }
        terminal.Write(Loc.Get("home.bbs_potions_label"));
        terminal.SetColor("bright_green");
        terminal.WriteLine($"{currentPlayer.Healing}");

        // Compact family status (1 line)
        var romance = RomanceTracker.Instance;
        var children = FamilySystem.Instance.GetChildrenOf(currentPlayer);
        var partnersAtHome = new List<string>();
        foreach (var spouse in romance.Spouses)
        {
            var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == spouse.NPCId);
            if (npc != null && npc.IsAlive == true && (npc.CurrentLocation == "Home" || npc.CurrentLocation == "Your Home"))
                partnersAtHome.Add(npc.Name ?? spouse.NPCName);
        }
        foreach (var lover in romance.CurrentLovers)
        {
            var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == lover.NPCId);
            if (npc != null && npc.IsAlive == true && (npc.CurrentLocation == "Home" || npc.CurrentLocation == "Your Home"))
                partnersAtHome.Add(npc.Name ?? lover.NPCName);
        }
        if (partnersAtHome.Count > 0 || children.Count > 0)
        {
            terminal.SetColor("bright_magenta");
            if (partnersAtHome.Count > 0)
                terminal.Write(Loc.Get("home.bbs_partners_here", string.Join(", ", partnersAtHome)));
            if (children.Count > 0)
            {
                terminal.SetColor("bright_yellow");
                terminal.Write(Loc.Get("home.bbs_children", children.Count, children.Count != 1 ? Loc.Get("home.children_ren") : ""));
            }
            terminal.WriteLine("");
        }

        terminal.WriteLine("");

        // Menu rows - consistent layout regardless of upgrades
        ShowBBSMenuRow(("E", "bright_yellow", Loc.Get("home.rest")), ("U", "bright_yellow", Loc.Get("home.upgrades")), ("S", "bright_yellow", Loc.Get("dungeon.status")));
        ShowBBSMenuRow(("D", "bright_yellow", Loc.Get("home.deposit")), ("W", "bright_yellow", Loc.Get("home.withdraw")), ("L", "bright_yellow", Loc.Get("home.list_chest")));
        ShowBBSMenuRow(("A", "bright_yellow", Loc.Get("home.gather_herbs")), ("T", "bright_yellow", Loc.Get("home.trophies")), ("F", "bright_yellow", Loc.Get("home.family")));
        ShowBBSMenuRow(("C", "bright_yellow", Loc.Get("home.children_interact")), ("P", "bright_yellow", Loc.Get("home.partner")), ("B", "bright_yellow", Loc.Get("home.bedroom")));
        ShowBBSMenuRow(("X", "bright_yellow", Loc.Get("home.resurrect")), ("I", "bright_yellow", Loc.Get("dungeon.inventory")), ("G", "bright_yellow", Loc.Get("home.gear_partner")));
        ShowBBSMenuRow(("H", "bright_yellow", Loc.Get("home.heal_potion")));
        if (!UsurperRemake.BBS.DoorMode.IsOnlineMode && currentPlayer != null)
        {
            string zLabel = DailySystemManager.CanRestForNight(currentPlayer) ? Loc.Get("home.sleep") : Loc.Get("home.wait_night");
            ShowBBSMenuRow(("Z", "bright_yellow", zLabel), ("R", "bright_yellow", Loc.Get("home.return_label")));
        }
        else if (UsurperRemake.BBS.DoorMode.IsOnlineMode && currentPlayer != null && currentPlayer.HasReinforcedDoor)
        {
            ShowBBSMenuRow(("Z", "bright_yellow", Loc.Get("home.sleep_safe")), ("R", "bright_yellow", Loc.Get("home.return_label")));
        }
        else
        {
            ShowBBSMenuRow(("R", "bright_yellow", Loc.Get("home.return_label")));
        }

        ShowBBSFooter();
    }

    protected override async Task<bool> ProcessChoice(string choice)
    {
        if (string.IsNullOrWhiteSpace(choice))
            return false;

        var c = choice.Trim().ToUpperInvariant();

        // Handle global quick commands
        var (handled, shouldExit) = await TryProcessGlobalCommand(choice);
        if (handled) return shouldExit;

        switch (c)
        {
            case "E":
                await DoRest();
                return false;
            case "D":
                if (currentPlayer.ChestLevel <= 0)
                {
                    terminal.WriteLine(Loc.Get("home.no_chest"), "yellow");
                    await terminal.WaitForKey();
                }
                else
                    await DepositItem();
                return false;
            case "W":
                if (currentPlayer.ChestLevel <= 0)
                {
                    terminal.WriteLine(Loc.Get("home.no_chest"), "yellow");
                    await terminal.WaitForKey();
                }
                else
                    await WithdrawItem();
                return false;
            case "L":
                if (currentPlayer.ChestLevel <= 0)
                {
                    terminal.WriteLine(Loc.Get("home.no_chest"), "yellow");
                    await terminal.WaitForKey();
                }
                else
                {
                    ShowChestContents();
                    await terminal.WaitForKey();
                }
                return false;
            case "A":
                await GatherHerbs();
                return false;
            case "J":
                await UseHerbMenu();
                return false;
            case "T":
                if (!currentPlayer.HasTrophyRoom)
                {
                    terminal.WriteLine(Loc.Get("home.no_trophy_room"), "yellow");
                    await terminal.WaitForKey();
                }
                else
                {
                    ShowTrophies();
                    await terminal.WaitForKey();
                }
                return false;
            case "F":
                await ShowFamily();
                return false;
            case "C":
                await InteractWithChild();
                return false;
            case "P":
                await SpendTimeWithSpouse();
                return false;
            case "B":
                await VisitBedroom();
                return false;
                case "!":
                await ResurrectAlly();
                return false;
            case "H":
                await UseHealingPotion();
                return false;
            case "I":
                await ShowInventory();
                return false;
            case "G":
                await EquipPartner();
                return false;
            case "S":
                await ShowStatus();
                return false;
            case "U":
                await ShowHomeUpgrades();
                return false;
            case "Z":
                if (UsurperRemake.BBS.DoorMode.IsOnlineMode && currentPlayer != null && currentPlayer.HasReinforcedDoor)
                {
                    await SleepAtHomeOnline();
                    return true;
                }
                else if (!UsurperRemake.BBS.DoorMode.IsOnlineMode && currentPlayer != null)
                {
                    if (DailySystemManager.CanRestForNight(currentPlayer))
                        await SleepAtHome();
                    else
                        await DailySystemManager.Instance.WaitUntilEvening(currentPlayer, terminal);
                }
                return false;
            case "X":
                await ResurrectAlly();
                return false;
            case "R":
            case "Q":
            case "M": // Also allow M for Main Street
                await NavigateToLocation(GameLocation.MainStreet);
                return true;
            default:
                return await base.ProcessChoice(choice);
        }
    }

    private async Task DoRest()
    {
        int homeLevel = Math.Clamp(currentPlayer.HomeLevel, 0, 5);
        int maxRests = GameConfig.HomeRestsPerDay[homeLevel];
        float recoveryPercent = GameConfig.HomeRecoveryPercent[homeLevel];

        // Check daily rest limit
        if (currentPlayer.HomeRestsToday >= maxRests)
        {
            terminal.SetColor("yellow");
            if (homeLevel == 0)
                terminal.WriteLine(Loc.Get("home.rest_straw_uncomfort"));
            else
                terminal.WriteLine(Loc.Get("home.rest_maxed"));
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("home.rest_used_today", currentPlayer.HomeRestsToday, maxRests));
            await terminal.WaitForKey();
            return;
        }

        // Flavor text based on home level
        switch (homeLevel)
        {
            case 0:
                terminal.WriteLine(Loc.Get("home.rest_straw"), "gray");
                break;
            case 1:
                terminal.WriteLine(Loc.Get("home.rest_cot"), "gray");
                break;
            case 2:
                terminal.WriteLine(Loc.Get("home.rest_wooden"), "gray");
                break;
            default:
                terminal.WriteLine(Loc.Get("home.rest_comfort"), "gray");
                break;
        }
        await Task.Delay(1500);

        // Blood Price rest penalty — dark memories reduce rest effectiveness (multiplicative)
        float restEfficiency = recoveryPercent;
        if (currentPlayer.MurderWeight >= 6f) restEfficiency *= 0.50f;
        else if (currentPlayer.MurderWeight >= 3f) restEfficiency *= 0.75f;

        long healAmount = (long)((currentPlayer.MaxHP - currentPlayer.HP) * restEfficiency);
        long manaAmount = (long)((currentPlayer.MaxMana - currentPlayer.Mana) * restEfficiency);
        currentPlayer.HP = Math.Min(currentPlayer.MaxHP, currentPlayer.HP + healAmount);
        currentPlayer.Mana = Math.Min(currentPlayer.MaxMana, currentPlayer.Mana + manaAmount);

        if (currentPlayer.MurderWeight >= 3f)
        {
            terminal.WriteLine(Loc.Get("home.rest_grief"), "dark_red");
        }

        if (restEfficiency >= 1.0f)
        {
            terminal.WriteLine(Loc.Get("home.rest_rejuvenated"), "bright_green");
        }
        else
        {
            terminal.SetColor("green");
            if (currentPlayer.IsManaClass)
                terminal.WriteLine(Loc.Get("home.rest_recovered_mana", healAmount, manaAmount, (int)(restEfficiency * 100)));
            else
                terminal.WriteLine(Loc.Get("home.rest_recovered_hp", healAmount, (int)(restEfficiency * 100)));
        }

        currentPlayer.HomeRestsToday++;

        // Reduce fatigue from home rest (single-player only)
        if (!UsurperRemake.BBS.DoorMode.IsOnlineMode && currentPlayer.Fatigue > 0)
        {
            int oldFatigue = currentPlayer.Fatigue;
            currentPlayer.Fatigue = Math.Max(0, currentPlayer.Fatigue - GameConfig.FatigueReductionHomeRest);
            if (currentPlayer.Fatigue < oldFatigue)
                terminal.WriteLine(Loc.Get("home.rest_fatigue_refreshed", oldFatigue - currentPlayer.Fatigue), "bright_green");
        }

        // Apply Well-Rested buff from Hearth
        int hearthLevel = Math.Clamp(currentPlayer.HearthLevel, 0, 5);
        if (hearthLevel > 0)
        {
            float bonus = GameConfig.HearthDamageBonus[hearthLevel];
            int combats = GameConfig.HearthCombatDuration[hearthLevel];
            currentPlayer.WellRestedCombats = combats;
            currentPlayer.WellRestedBonus = bonus;
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("home.rest_hearth_buff", (int)(bonus * 100), combats));
        }

        // Show remaining rests
        int restsLeft = maxRests - currentPlayer.HomeRestsToday;
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("home.rest_remaining", restsLeft, maxRests));

        // Check for dreams during rest at home (nightmares take priority)
        var dream = DreamSystem.Instance.GetDreamForRest(currentPlayer, 0);
        if (dream != null)
        {
            await Task.Delay(1500);
            terminal.WriteLine("");
            terminal.SetColor("dark_magenta");
            terminal.WriteLine(Loc.Get("home.sleep_dreams"));
            terminal.WriteLine("");
            await Task.Delay(1500);

            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"=== {dream.Title} ===");
            terminal.WriteLine("");

            terminal.SetColor("magenta");
            foreach (var line in dream.Content)
            {
                terminal.WriteLine($"  {line}");
                await Task.Delay(1200);
            }

            if (!string.IsNullOrEmpty(dream.PhilosophicalHint))
            {
                terminal.WriteLine("");
                terminal.SetColor("dark_cyan");
                terminal.WriteLine($"  ({dream.PhilosophicalHint})");
            }

            terminal.WriteLine("");
            DreamSystem.Instance.ExperienceDream(dream.Id);
        }

        await terminal.WaitForKey();
    }

    /// <summary>
    /// Online mode: sleep at home behind the reinforced door.
    /// Requires HasReinforcedDoor upgrade.
    /// </summary>
    private async Task SleepAtHomeOnline()
    {
        if (currentPlayer == null) return;

        currentPlayer.HP = currentPlayer.MaxHP;
        currentPlayer.Mana = currentPlayer.MaxMana;
        currentPlayer.Stamina = Math.Max(currentPlayer.Stamina, currentPlayer.Constitution * 2);

        var backend = SaveSystem.Instance.Backend as UsurperRemake.Systems.SqlSaveBackend;
        if (backend != null)
        {
            var username = UsurperRemake.BBS.DoorMode.OnlineUsername ?? currentPlayer.Name2;
            await backend.RegisterSleepingPlayer(username, "home", "[]", 1);
        }

        terminal.SetColor("gray");
        terminal.WriteLine($"\n  {Loc.Get("home.sleep_reinforced")}");
        throw new LocationExitException(GameLocation.NoWhere);
    }

    private async Task SleepAtHome()
    {
        if (UsurperRemake.BBS.DoorMode.IsOnlineMode || currentPlayer == null)
            return;

        if (!DailySystemManager.CanRestForNight(currentPlayer))
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("home.sleep_not_evening"));
            await terminal.WaitForKey();
            return;
        }

        int homeLevel = Math.Clamp(currentPlayer.HomeLevel, 0, 5);

        // Flavor text based on home level
        terminal.WriteLine("");
        switch (homeLevel)
        {
            case 0:
                terminal.WriteLine(Loc.Get("home.sleep_straw"), "gray");
                break;
            case 1:
                terminal.WriteLine(Loc.Get("home.sleep_cot"), "gray");
                break;
            case 2:
                terminal.WriteLine(Loc.Get("home.sleep_wooden"), "gray");
                break;
            default:
                terminal.WriteLine(Loc.Get("home.sleep_comfort"), "gray");
                break;
        }
        await Task.Delay(1500);

        // Full HP/Mana/Stamina recovery with Blood Price penalty
        float restEfficiency = 1.0f;
        if (currentPlayer.MurderWeight >= 6f) restEfficiency = 0.50f;
        else if (currentPlayer.MurderWeight >= 3f) restEfficiency = 0.75f;

        long healAmount = (long)((currentPlayer.MaxHP - currentPlayer.HP) * restEfficiency);
        long manaAmount = (long)((currentPlayer.MaxMana - currentPlayer.Mana) * restEfficiency);
        long staminaAmount = (long)((currentPlayer.MaxCombatStamina - currentPlayer.CurrentCombatStamina) * restEfficiency);
        currentPlayer.HP = Math.Min(currentPlayer.MaxHP, currentPlayer.HP + healAmount);
        currentPlayer.Mana = Math.Min(currentPlayer.MaxMana, currentPlayer.Mana + manaAmount);
        currentPlayer.CurrentCombatStamina = Math.Min(currentPlayer.MaxCombatStamina, currentPlayer.CurrentCombatStamina + staminaAmount);

        if (currentPlayer.MurderWeight >= 3f)
        {
            terminal.WriteLine(Loc.Get("home.sleep_grief"), "dark_red");
        }

        if (restEfficiency >= 1.0f)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("home.sleep_refreshed"));
        }
        else
        {
            terminal.SetColor("green");
            if (currentPlayer.IsManaClass)
                terminal.WriteLine(Loc.Get("home.sleep_recovered_mana", healAmount, manaAmount, (int)(restEfficiency * 100)));
            else
                terminal.WriteLine(Loc.Get("home.sleep_recovered_stamina", healAmount, staminaAmount, (int)(restEfficiency * 100)));
        }

        // Apply Well-Rested buff from Hearth
        int hearthLevel = Math.Clamp(currentPlayer.HearthLevel, 0, 5);
        if (hearthLevel > 0)
        {
            float bonus = GameConfig.HearthDamageBonus[hearthLevel];
            int combats = GameConfig.HearthCombatDuration[hearthLevel];
            currentPlayer.WellRestedCombats = combats;
            currentPlayer.WellRestedBonus = bonus;
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("home.rest_hearth_buff", (int)(bonus * 100), combats));
        }

        // Check for dreams
        var dream = DreamSystem.Instance.GetDreamForRest(currentPlayer, 0);
        if (dream != null)
        {
            await Task.Delay(1500);
            terminal.WriteLine("");
            terminal.SetColor("dark_magenta");
            terminal.WriteLine(Loc.Get("home.sleep_dreams"));
            terminal.WriteLine("");
            await Task.Delay(1500);

            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"=== {dream.Title} ===");
            terminal.WriteLine("");

            terminal.SetColor("magenta");
            foreach (var line in dream.Content)
            {
                terminal.WriteLine($"  {line}");
                await Task.Delay(1200);
            }

            if (!string.IsNullOrEmpty(dream.PhilosophicalHint))
            {
                terminal.WriteLine("");
                terminal.SetColor("dark_cyan");
                terminal.WriteLine($"  ({dream.PhilosophicalHint})");
            }

            terminal.WriteLine("");
            DreamSystem.Instance.ExperienceDream(dream.Id);
        }

        // Advance to morning
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("home.sleep_drift"));
        await Task.Delay(2000);
        await DailySystemManager.Instance.RestAndAdvanceToMorning(currentPlayer);
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("home.sleep_new_day", DailySystemManager.Instance.CurrentDay));
        await Task.Delay(1500);

        await terminal.WaitForKey();
    }

    private async Task GatherHerbs()
    {
        int gardenLevel = Math.Clamp(currentPlayer.GardenLevel, 0, 5);
        int maxHerbs = GameConfig.HerbsPerDay[gardenLevel];

        if (gardenLevel <= 0)
        {
            terminal.WriteLine(Loc.Get("home.no_herb_garden"), "yellow");
            await terminal.WaitForKey();
            return;
        }

        int herbsLeft = Math.Max(0, maxHerbs - currentPlayer.HerbsGatheredToday);
        if (herbsLeft <= 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("home.herbs_gathered_today"));
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("home.herb_gathered_count", currentPlayer.HerbsGatheredToday, maxHerbs));
            await terminal.WaitForKey();
            return;
        }

        while (herbsLeft > 0)
        {
            terminal.ClearScreen();
            WriteSectionHeader(Loc.Get("home.herb_garden"), "bright_green");
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("home.herb_gathers_remaining", herbsLeft));
            terminal.WriteLine("");

            // Show available herb types based on garden level
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("home.herb_which"));
            terminal.WriteLine("");

            var available = new List<HerbType>();
            for (int i = 1; i <= gardenLevel && i <= 5; i++)
            {
                var type = (HerbType)i;
                int count = currentPlayer.GetHerbCount(type);
                int max = GameConfig.HerbMaxCarry[i];
                bool full = count >= max;
                string color = full ? "darkgray" : HerbData.GetColor(type);
                string fullTag = full ? " [FULL]" : "";
                terminal.SetColor(color);
                terminal.WriteLine($"  [{i}] {HerbData.GetName(type)} ({count}/{max}){fullTag}");
                terminal.SetColor("gray");
                terminal.WriteLine($"      {HerbData.GetDescription(type)}");
                if (!full) available.Add(type);
            }

            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.WriteLine($"  [Q] {Loc.Get("home.herb_done")}");
            terminal.WriteLine("");
            terminal.Write(Loc.Get("ui.choice"), "white");

            string input = (await terminal.ReadLineAsync())?.Trim().ToUpper() ?? "";
            if (input == "Q" || string.IsNullOrEmpty(input)) break;

            if (int.TryParse(input, out int choice) && choice >= 1 && choice <= gardenLevel && choice <= 5)
            {
                var herbType = (HerbType)choice;
                int count = currentPlayer.GetHerbCount(herbType);
                int max = GameConfig.HerbMaxCarry[choice];
                if (count >= max)
                {
                    terminal.WriteLine(Loc.Get("home.herb_pouch_full", HerbData.GetName(herbType), count, max), "yellow");
                    await terminal.WaitForKey();
                    continue;
                }

                currentPlayer.AddHerb(herbType);
                currentPlayer.HerbsGatheredToday++;
                herbsLeft--;

                terminal.SetColor(HerbData.GetColor(herbType));
                terminal.WriteLine(Loc.Get("home.herb_gathered", HerbData.GetName(herbType), currentPlayer.GetHerbCount(herbType), max));
                await Task.Delay(500);
            }
        }

        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("home.herb_done_msg"));
        await terminal.WaitForKey();
    }

    /// <summary>
    /// Show herb pouch and let player use an herb. Shared by Home, Dungeon, and BaseLocation.
    /// </summary>
    public static async Task UseHerbMenu(Character player, TerminalEmulator terminal)
    {
        if (player.TotalHerbCount <= 0)
        {
            terminal.WriteLine(Loc.Get("home.herb_pouch_empty"), "yellow");
            await terminal.WaitForKey();
            return;
        }

        terminal.ClearScreen();
        if (player.ScreenReaderMode)
        {
            terminal.WriteLine(Loc.Get("home.herb_pouch_title"));
        }
        else
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine($"═══ {Loc.Get("home.herb_pouch_title")} ═══");
        }
        terminal.WriteLine("");

        if (player.HasActiveHerbBuff)
        {
            var buffName = HerbData.GetName((HerbType)player.HerbBuffType);
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("home.herb_active_buff", buffName, player.HerbBuffCombats));
            terminal.WriteLine("");
        }

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("home.herb_select"));
        terminal.WriteLine("");

        var options = new List<HerbType>();
        int idx = 1;
        foreach (HerbType type in Enum.GetValues(typeof(HerbType)))
        {
            if (type == HerbType.None) continue;
            int count = player.GetHerbCount(type);
            if (count <= 0) continue;

            options.Add(type);
            terminal.SetColor(HerbData.GetColor(type));
            terminal.Write(GameConfig.ScreenReaderMode
                ? $"  {idx}. {HerbData.GetName(type)} x{count}"
                : $"  [{idx}] {HerbData.GetName(type)} x{count}");
            terminal.SetColor("gray");
            terminal.WriteLine($" — {HerbData.GetDescription(type)}");
            idx++;
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(GameConfig.ScreenReaderMode ? $"  Q. {Loc.Get("home.herb_cancel")}" : $"  [Q] {Loc.Get("home.herb_cancel")}");
        terminal.WriteLine("");
        terminal.Write(Loc.Get("ui.choice"), "white");

        string input = (await terminal.ReadLineAsync())?.Trim().ToUpper() ?? "";
        if (input == "Q" || string.IsNullOrEmpty(input)) return;

        if (int.TryParse(input, out int sel) && sel >= 1 && sel <= options.Count)
        {
            var herbType = options[sel - 1];
            await ApplyHerbEffect(player, herbType, terminal);
        }
    }

    private async Task UseHerbMenu()
    {
        await UseHerbMenu(currentPlayer, terminal);
    }

    /// <summary>
    /// Apply an herb's effect to the player. Consumes 1 herb from inventory.
    /// </summary>
    public static async Task ApplyHerbEffect(Character player, HerbType type, TerminalEmulator terminal)
    {
        if (!player.ConsumeHerb(type)) return;

        string herbName = HerbData.GetName(type);
        terminal.SetColor(HerbData.GetColor(type));

        switch (type)
        {
            case HerbType.HealingHerb:
                float herbHealPct = GameConfig.HerbHealPercent;
                if (player.Class == CharacterClass.Alchemist)
                    herbHealPct *= (1.0f + GameConfig.AlchemistPotionMasteryBonus);
                long healAmount = (long)(player.MaxHP * herbHealPct);
                healAmount = Math.Min(healAmount, player.MaxHP - player.HP);
                player.HP += healAmount;
                terminal.WriteLine(Loc.Get("home.herb_healing_use", herbName, healAmount, player.HP, player.MaxHP));
                if (player.Class == CharacterClass.Alchemist)
                    terminal.WriteLine(Loc.Get("home.potion_mastery_enhance"), "bright_cyan");
                break;

            case HerbType.IronbarkRoot:
                player.HerbBuffType = (int)HerbType.IronbarkRoot;
                player.HerbBuffCombats = player.Class == CharacterClass.Alchemist
                    ? (int)(GameConfig.HerbBuffDuration * 1.5) : GameConfig.HerbBuffDuration;
                player.HerbBuffValue = GameConfig.HerbDefenseBonus;
                player.HerbExtraAttacks = 0;
                terminal.WriteLine(Loc.Get("home.herb_ironbark_use", herbName, (int)(GameConfig.HerbDefenseBonus * 100), player.HerbBuffCombats));
                if (player.Class == CharacterClass.Alchemist)
                    terminal.WriteLine(Loc.Get("home.potion_mastery_extend"), "bright_cyan");
                break;

            case HerbType.FirebloomPetal:
                player.HerbBuffType = (int)HerbType.FirebloomPetal;
                player.HerbBuffCombats = player.Class == CharacterClass.Alchemist
                    ? (int)(GameConfig.HerbBuffDuration * 1.5) : GameConfig.HerbBuffDuration;
                player.HerbBuffValue = GameConfig.HerbDamageBonus;
                player.HerbExtraAttacks = 0;
                terminal.WriteLine(Loc.Get("home.herb_firebloom_use", herbName, (int)(GameConfig.HerbDamageBonus * 100), player.HerbBuffCombats));
                if (player.Class == CharacterClass.Alchemist)
                    terminal.WriteLine(Loc.Get("home.potion_mastery_extend"), "bright_cyan");
                break;

            case HerbType.Swiftthistle:
                player.HerbBuffType = (int)HerbType.Swiftthistle;
                player.HerbBuffCombats = player.Class == CharacterClass.Alchemist
                    ? (int)(GameConfig.HerbSwiftDuration * 1.5) : GameConfig.HerbSwiftDuration;
                player.HerbBuffValue = 0;
                player.HerbExtraAttacks = GameConfig.HerbExtraAttackCount;
                terminal.WriteLine(Loc.Get("home.herb_swift_use", herbName, GameConfig.HerbExtraAttackCount, player.HerbBuffCombats));
                if (player.Class == CharacterClass.Alchemist)
                    terminal.WriteLine(Loc.Get("home.potion_mastery_extend"), "bright_cyan");
                break;

            case HerbType.StarbloomEssence:
                long manaRestore = (long)(player.MaxMana * GameConfig.HerbManaRestorePercent);
                manaRestore = Math.Min(manaRestore, player.MaxMana - player.Mana);
                player.Mana += manaRestore;
                player.HerbBuffType = (int)HerbType.StarbloomEssence;
                player.HerbBuffCombats = player.Class == CharacterClass.Alchemist
                    ? (int)(GameConfig.HerbBuffDuration * 1.5) : GameConfig.HerbBuffDuration;
                player.HerbBuffValue = GameConfig.HerbSpellBonus;
                player.HerbExtraAttacks = 0;
                terminal.WriteLine(Loc.Get("home.herb_starbloom_use", manaRestore, (int)(GameConfig.HerbSpellBonus * 100), player.HerbBuffCombats));
                if (player.Class == CharacterClass.Alchemist)
                    terminal.WriteLine(Loc.Get("home.potion_mastery_extend"), "bright_cyan");
                break;
        }

        await terminal.WaitForKey();
    }

    private async Task DepositItem()
    {
        if (!currentPlayer.Inventory.Any())
        {
            terminal.WriteLine(Loc.Get("ui.no_items_to_store"), "yellow");
            await terminal.WaitForKey();
            return;
        }
        int maxCapacity = GameConfig.ChestCapacity[Math.Clamp(currentPlayer.ChestLevel, 0, 5)];
        if (Chest.Count >= maxCapacity)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("home.chest_full", Chest.Count, maxCapacity));
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("home.chest_upgrade"));
            await terminal.WaitForKey();
            return;
        }
        terminal.WriteLine(Loc.Get("home.chest_deposit_select", Chest.Count, maxCapacity), "cyan");
        for (int i = 0; i < currentPlayer.Inventory.Count; i++)
        {
            terminal.WriteLine($"  {i + 1}. {currentPlayer.Inventory[i].GetDisplayName()}");
        }
        var input = await terminal.GetInput(Loc.Get("ui.choice"));
        if (int.TryParse(input, out int idx) && idx > 0 && idx <= currentPlayer.Inventory.Count)
        {
            var item = currentPlayer.Inventory[idx - 1];
            currentPlayer.Inventory.RemoveAt(idx - 1);
            Chest.Add(item);
            terminal.WriteLine(Loc.Get("home.chest_stored", item.GetDisplayName(), Chest.Count, maxCapacity), "green");

            // Force immediate save — chest changes bypass the 60s auto-save throttle
            // to prevent item loss on disconnect/crash
            if (DoorMode.IsOnlineMode)
                SaveSystem.Instance.ResetAutoSaveThrottle();
        }
        else
        {
            terminal.WriteLine(Loc.Get("ui.cancelled"), "gray");
        }
        await terminal.WaitForKey();
    }

    private async Task WithdrawItem()
    {
        if (!Chest.Any())
        {
            terminal.WriteLine(Loc.Get("home.chest_empty"), "yellow");
            await terminal.WaitForKey();
            return;
        }
        terminal.WriteLine(Loc.Get("home.chest_select_withdraw"), "cyan");
        for (int i = 0; i < Chest.Count; i++)
        {
            terminal.WriteLine($"  {i + 1}. {Chest[i].GetDisplayName()}");
        }
        var input = await terminal.GetInput(Loc.Get("ui.choice"));
        if (int.TryParse(input, out int idx) && idx > 0 && idx <= Chest.Count)
        {
            var item = Chest[idx - 1];
            Chest.RemoveAt(idx - 1);
            currentPlayer.Inventory.Add(item);
            terminal.WriteLine(Loc.Get("home.chest_retrieved", item.GetDisplayName()), "green");

            // Force immediate save — chest changes bypass the 60s auto-save throttle
            if (DoorMode.IsOnlineMode)
                SaveSystem.Instance.ResetAutoSaveThrottle();
        }
        else
        {
            terminal.WriteLine(Loc.Get("ui.cancelled"), "gray");
        }
        await terminal.WaitForKey();
    }

    private void ShowChestContents()
    {
        terminal.WriteLine($"\n{Loc.Get("home.chest_items")}", "bright_cyan");
        if (!Chest.Any())
        {
            terminal.WriteLine(Loc.Get("home.chest_empty_label"), "gray");
        }
        else
        {
            for (int i = 0; i < Chest.Count; i++)
            {
                terminal.WriteLine($"  {i + 1}. {Chest[i].GetDisplayName()}");
            }
        }
    }

    private void ShowTrophies()
    {
        terminal.WriteLine($"\n{Loc.Get("home.trophies_title")}", "bright_cyan");
        terminal.WriteLine();

        // Use the proper PlayerAchievements from Character base class
        // Note: Player.Achievements hides Character.Achievements, so we cast to Character
        var achievements = ((Character)currentPlayer).Achievements;

        if (achievements.UnlockedCount > 0)
        {
            // Show summary
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("home.trophies_total_unlocked", achievements.UnlockedCount, AchievementSystem.TotalAchievements));
            terminal.WriteLine(Loc.Get("home.trophies_points", achievements.TotalPoints));
            terminal.WriteLine(Loc.Get("home.trophies_completion", $"{achievements.CompletionPercentage:F1}"));
            terminal.WriteLine();

            // Show unlocked achievements by category
            foreach (AchievementCategory category in Enum.GetValues(typeof(AchievementCategory)))
            {
                var categoryAchievements = AchievementSystem.GetByCategory(category)
                    .Where(a => achievements.IsUnlocked(a.Id))
                    .ToList();

                if (categoryAchievements.Any())
                {
                    terminal.SetColor("cyan");
                    terminal.WriteLine($"  === {category} ===");

                    foreach (var achievement in categoryAchievements)
                    {
                        terminal.SetColor(achievement.GetTierColor());
                        terminal.Write($"    {achievement.GetTierSymbol()} ");
                        terminal.SetColor("bright_green");
                        terminal.Write($"[X] {achievement.Name}");
                        terminal.SetColor("gray");
                        terminal.WriteLine($" - {achievement.Description}");
                    }
                    terminal.WriteLine();
                }
            }

            terminal.SetColor("white");
        }
        else
        {
            terminal.WriteLine(Loc.Get("home.trophies_none"), "gray");
            terminal.WriteLine();
            terminal.WriteLine(Loc.Get("home.trophies_hint1"), "gray");
            terminal.WriteLine(Loc.Get("home.trophies_hint2"), "gray");
        }
    }

    private async Task UseHealingPotion()
    {
        if (currentPlayer.HP >= currentPlayer.MaxHP)
        {
            terminal.WriteLine(Loc.Get("home.potion_full_health"), "bright_green");
            await terminal.WaitForKey();
            return;
        }

        if (currentPlayer.Healing <= 0)
        {
            terminal.WriteLine(Loc.Get("home.potion_none"), "red");
            terminal.WriteLine(Loc.Get("home.potion_buy_hint"), "gray");
            await terminal.WaitForKey();
            return;
        }

        // Use a potion
        currentPlayer.Healing--;
        long healAmount = Math.Max(50, currentPlayer.MaxHP / 4); // Heal 25% or at least 50 HP
        long oldHP = currentPlayer.HP;
        currentPlayer.HP = Math.Min(currentPlayer.HP + healAmount, currentPlayer.MaxHP);
        long actualHeal = currentPlayer.HP - oldHP;

        // Track statistics
        currentPlayer.Statistics.RecordPotionUsed(actualHeal);

        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("home.potion_drink"));
        terminal.WriteLine(Loc.Get("home.potion_restored", actualHeal, currentPlayer.HP, currentPlayer.MaxHP));
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("home.potion_remaining", currentPlayer.Healing));
        await terminal.WaitForKey();
    }

    private new async Task ShowInventory()
    {
        terminal.WriteLine("\n", "white");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("home.inventory_title"));
        terminal.WriteLine();

        if (!currentPlayer.Inventory.Any())
        {
            terminal.WriteLine(Loc.Get("home.inventory_empty"), "gray");
            await terminal.WaitForKey();
            return;
        }

        terminal.SetColor("white");
        for (int i = 0; i < currentPlayer.Inventory.Count; i++)
        {
            var item = currentPlayer.Inventory[i];
            terminal.Write($"  {i + 1}. ");
            terminal.SetColor("bright_yellow");
            terminal.Write(item.GetDisplayName());
            terminal.SetColor("gray");
            if (item.Value > 0)
            {
                terminal.Write(Loc.Get("home.inventory_value", $"{item.Value:N0}"));
            }
            terminal.WriteLine();
        }

        terminal.WriteLine();
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("home.inventory_options"));
        terminal.SetColor("bright_yellow");
        terminal.Write("[D]");
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("home.inventory_deposit"));
        terminal.SetColor("bright_yellow");
        terminal.Write("[E]");
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("home.inventory_equip"));
        terminal.SetColor("bright_yellow");
        terminal.Write("[Q]");
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("home.inventory_quit"));

        var input = await terminal.GetInput(Loc.Get("ui.choice"));
        var c = input.Trim().ToUpperInvariant();

        switch (c)
        {
            case "D":
                await DepositItem();
                break;
            case "E":
                await EquipItemFromInventory();
                break;
            default:
                break;
        }
    }

    private async Task EquipItemFromInventory()
    {
        if (!currentPlayer.Inventory.Any())
        {
            terminal.WriteLine(Loc.Get("home.no_items_equip"), "yellow");
            await terminal.WaitForKey();
            return;
        }

        terminal.WriteLine(Loc.Get("home.equip_select_item"), "cyan");
        for (int i = 0; i < currentPlayer.Inventory.Count; i++)
        {
            var item = currentPlayer.Inventory[i];
            terminal.Write($"  {i + 1}. ");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(item.GetDisplayName());
        }
        terminal.SetColor("white");

        var input = await terminal.GetInput(Loc.Get("ui.choice"));
        if (int.TryParse(input, out int idx) && idx > 0 && idx <= currentPlayer.Inventory.Count)
        {
            var item = currentPlayer.Inventory[idx - 1];

            // Check if this is an equippable item (weapon, armor, etc.)
            if (IsEquippableItem(item))
            {
                await EquipItemProper(item, idx - 1);
            }
            else
            {
                // Non-equippable items (potions, food, etc.) - just apply effects
                item.ApplyEffects(currentPlayer);
                currentPlayer.Inventory.RemoveAt(idx - 1);
                currentPlayer.RecalculateStats();
                terminal.WriteLine(Loc.Get("home.equip_used", item.GetDisplayName()), "bright_green");
            }
        }
        else
        {
            terminal.WriteLine(Loc.Get("ui.cancelled"), "gray");
        }
        await terminal.WaitForKey();
    }

    /// <summary>
    /// Check if an item is equippable (weapon, armor, shield, etc.)
    /// </summary>
    private bool IsEquippableItem(ModelItem item)
    {
        return item.Type switch
        {
            ObjType.Weapon => true,
            ObjType.Shield => true,
            ObjType.Body => true,
            ObjType.Head => true,
            ObjType.Arms => true,
            ObjType.Hands => true,
            ObjType.Legs => true,
            ObjType.Feet => true,
            ObjType.Waist => true,
            ObjType.Neck => true,
            ObjType.Face => true,
            ObjType.Fingers => true,
            ObjType.Magic => (int)item.MagicType == 5 || (int)item.MagicType == 9 || (int)item.MagicType == 10, // Ring, Belt, Amulet
            _ => false
        };
    }

    /// <summary>
    /// Properly equip an item using the Equipment system with slot selection
    /// </summary>
    private async Task EquipItemProper(ModelItem item, int inventoryIndex)
    {
        // Determine which slot this item goes in
        EquipmentSlot targetSlot = item.Type switch
        {
            ObjType.Weapon => EquipmentSlot.MainHand,
            ObjType.Shield => EquipmentSlot.OffHand,
            ObjType.Body => EquipmentSlot.Body,
            ObjType.Head => EquipmentSlot.Head,
            ObjType.Arms => EquipmentSlot.Arms,
            ObjType.Hands => EquipmentSlot.Hands,
            ObjType.Legs => EquipmentSlot.Legs,
            ObjType.Feet => EquipmentSlot.Feet,
            ObjType.Waist => EquipmentSlot.Waist,
            ObjType.Neck => EquipmentSlot.Neck,
            ObjType.Face => EquipmentSlot.Face,
            ObjType.Fingers => EquipmentSlot.LFinger,
            ObjType.Abody => EquipmentSlot.Cloak,
            ObjType.Magic => (int)item.MagicType switch
            {
                5 => EquipmentSlot.LFinger,  // Ring
                9 => EquipmentSlot.Waist,    // Belt
                10 => EquipmentSlot.Neck,    // Amulet
                _ => EquipmentSlot.MainHand
            },
            _ => EquipmentSlot.MainHand
        };

        // Determine handedness for weapons (default to None for non-weapons like armor)
        WeaponHandedness handedness = WeaponHandedness.None;
        if (item.Type == ObjType.Weapon)
        {
            // Check if it's a two-handed weapon based on name or attack power
            string nameLower = item.Name.ToLower();
            if (nameLower.Contains("two-hand") || nameLower.Contains("2h") ||
                nameLower.Contains("greatsword") || nameLower.Contains("greataxe") ||
                nameLower.Contains("halberd") || nameLower.Contains("pike") ||
                nameLower.Contains("longbow") || nameLower.Contains("crossbow") ||
                nameLower.Contains("staff") || nameLower.Contains("quarterstaff"))
            {
                handedness = WeaponHandedness.TwoHanded;
            }
            else
            {
                handedness = WeaponHandedness.OneHanded;
            }
        }
        else if (item.Type == ObjType.Shield)
        {
            handedness = WeaponHandedness.OffHandOnly;
        }

        // Convert Item to Equipment
        var equipment = new Equipment
        {
            Name = item.Name,
            Slot = targetSlot,
            Handedness = handedness,
            WeaponPower = item.Attack,
            ArmorClass = item.Armor,
            ShieldBonus = item.Type == ObjType.Shield ? item.Armor : 0,
            DefenceBonus = item.Defence,
            StrengthBonus = item.Strength,
            DexterityBonus = item.Dexterity,
            AgilityBonus = item.Agility,
            WisdomBonus = item.Wisdom,
            CharismaBonus = item.Charisma,
            MaxHPBonus = item.HP,
            MaxManaBonus = item.Mana,
            Value = item.Value,
            IsCursed = item.IsCursed,
            Rarity = EquipmentRarity.Common
        };

        // Transfer CON/INT from LootEffects (these stats are stored as encoded effects)
        if (item.LootEffects != null)
        {
            foreach (var (effectType, value) in item.LootEffects)
            {
                var effect = (LootGenerator.SpecialEffect)effectType;
                switch (effect)
                {
                    case LootGenerator.SpecialEffect.Constitution: equipment.ConstitutionBonus += value; break;
                    case LootGenerator.SpecialEffect.Intelligence: equipment.IntelligenceBonus += value; break;
                    case LootGenerator.SpecialEffect.AllStats:
                        equipment.ConstitutionBonus += value;
                        equipment.IntelligenceBonus += value;
                        equipment.CharismaBonus += value;
                        break;
                }
            }
        }

        // Register in database to get an ID
        EquipmentDatabase.RegisterDynamic(equipment);

        // For rings, ask which finger
        if (targetSlot == EquipmentSlot.LFinger)
        {
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("home.equip_which_finger"));
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("home.equip_left_finger"));
            terminal.WriteLine(Loc.Get("home.equip_right_finger"));
            terminal.WriteLine(Loc.Get("home.equip_cancel_option"));
            terminal.Write(Loc.Get("ui.choice"));
            var fingerChoice = await terminal.GetInput("");
            if (fingerChoice.ToUpper() == "R")
            {
                targetSlot = EquipmentSlot.RFinger;
                equipment.Slot = EquipmentSlot.RFinger;
            }
            else if (fingerChoice.ToUpper() != "L")
            {
                terminal.WriteLine(Loc.Get("ui.cancelled"), "gray");
                return;
            }
        }

        // For one-handed weapons, ask which slot to use
        EquipmentSlot? finalSlot = null;
        if (Character.RequiresSlotSelection(equipment))
        {
            finalSlot = await PromptForWeaponSlotHome();
            if (finalSlot == null)
            {
                terminal.WriteLine(Loc.Get("ui.cancelled"), "gray");
                return;
            }
        }

        // Equip the item
        if (currentPlayer.EquipItem(equipment, finalSlot, out string message))
        {
            // Remove from inventory
            currentPlayer.Inventory.RemoveAt(inventoryIndex);
            currentPlayer.RecalculateStats();

            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("home.equip_equipped", item.GetDisplayName()));
            if (!string.IsNullOrEmpty(message))
            {
                terminal.SetColor("gray");
                terminal.WriteLine(message);
            }
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("home.equip_cannot", message));
        }
    }

    /// <summary>
    /// Prompt player to choose which hand to equip a one-handed weapon in
    /// </summary>
    private async Task<EquipmentSlot?> PromptForWeaponSlotHome()
    {
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("home.equip_onehand_where"));
        terminal.WriteLine("");

        // Show current equipment in both slots
        var mainHandItem = currentPlayer.GetEquipment(EquipmentSlot.MainHand);
        var offHandItem = currentPlayer.GetEquipment(EquipmentSlot.OffHand);

        terminal.SetColor("white");
        terminal.Write(Loc.Get("home.equip_main_hand_label"));
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
        terminal.Write(Loc.Get("home.equip_off_hand_label"));
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
        terminal.WriteLine(Loc.Get("home.equip_cancel_label"));
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

    private async Task ShowFamily()
    {
        terminal.WriteLine("\n", "white");
        WriteBoxHeader(Loc.Get("home.family"), "bright_cyan", 38);
        terminal.WriteLine();

        var romance = RomanceTracker.Instance;
        bool hasFamily = false;

        // Show spouse(s)
        if (romance.Spouses.Count > 0)
        {
            hasFamily = true;
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("home.family_spouses_label", romance.Spouses.Count > 1 ? "S" : ""));
            terminal.SetColor("white");

            foreach (var spouse in romance.Spouses)
            {
                var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == spouse.NPCId);
                var name = npc?.Name ?? spouse.NPCId;
                var marriedDays = spouse.MarriedGameDay > 0
                    ? Math.Max(0, DailySystemManager.Instance.CurrentDay - spouse.MarriedGameDay)
                    : (int)(DateTime.Now - spouse.MarriedDate).TotalDays; // Fallback for old saves

                terminal.Write($"    ");
                terminal.SetColor("bright_red");
                terminal.Write("<3 ");
                terminal.SetColor("bright_white");
                terminal.Write(name);
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("home.family_married_days", marriedDays, marriedDays != 1 ? "s" : ""));

                if (spouse.Children > 0)
                {
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine(Loc.Get("home.family_children_together", spouse.Children));
                }

                if (spouse.AcceptsPolyamory)
                {
                    terminal.SetColor("magenta");
                    terminal.WriteLine(Loc.Get("home.family_polyamory"));
                }
            }
            terminal.WriteLine();
        }

        // Show lovers
        if (romance.CurrentLovers.Count > 0)
        {
            hasFamily = true;
            terminal.SetColor("magenta");
            terminal.WriteLine(Loc.Get("home.family_lovers_label"));
            terminal.SetColor("white");

            foreach (var lover in romance.CurrentLovers)
            {
                var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == lover.NPCId);
                var name = npc?.Name ?? lover.NPCId;
                var daysTogether = (int)(DateTime.Now - lover.RelationshipStart).TotalDays;

                terminal.Write($"    ");
                terminal.SetColor("bright_magenta");
                terminal.Write("<3 ");
                terminal.SetColor("white");
                terminal.Write(name);
                terminal.SetColor("gray");
                terminal.Write(Loc.Get("home.family_together_days", daysTogether, daysTogether != 1 ? "s" : ""));

                if (lover.IsExclusive)
                {
                    terminal.SetColor("bright_cyan");
                    terminal.Write(Loc.Get("home.family_exclusive"));
                }
                terminal.WriteLine();
            }
            terminal.WriteLine();
        }

        // Show friends with benefits
        if (romance.FriendsWithBenefits.Count > 0)
        {
            hasFamily = true;
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("home.family_fwb_label"));
            terminal.SetColor("white");

            foreach (var fwbId in romance.FriendsWithBenefits)
            {
                var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == fwbId);
                var name = npc?.Name ?? fwbId;
                terminal.WriteLine($"    ~ {name}");
            }
            terminal.WriteLine();
        }

        // Show children from FamilySystem
        var children = FamilySystem.Instance.GetChildrenOf(currentPlayer);
        if (children.Count > 0)
        {
            hasFamily = true;
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("home.family_children_label", children.Count));
            terminal.SetColor("white");

            foreach (var child in children)
            {
                terminal.Write("    ");
                terminal.SetColor("bright_green");
                terminal.Write("* ");
                terminal.SetColor("bright_white");
                terminal.Write($"{child.Name}");
                terminal.SetColor("gray");
                terminal.Write(Loc.Get("home.family_child_age", child.Age, child.Age != 1 ? "s" : "", child.Sex == CharacterSex.Male ? Loc.Get("home.family_child_boy") : Loc.Get("home.family_child_girl")));

                // Show behavior indicator
                terminal.SetColor(child.Soul > 100 ? "bright_cyan" : (child.Soul < -100 ? "red" : "white"));
                terminal.WriteLine($" ({child.GetSoulDescription()})");

                // Show health issues
                if (child.Health != GameConfig.ChildHealthNormal)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("home.family_child_health", child.GetHealthDescription()));
                }
            }

            // Check for children approaching adulthood
            var teensCount = children.Count(c => c.Age >= 15 && c.Age < FamilySystem.ADULT_AGE);
            if (teensCount > 0)
            {
                terminal.SetColor("bright_cyan");
                terminal.WriteLine(Loc.Get("home.family_teens_coming", teensCount));
            }
            terminal.WriteLine();
        }

        // Show ex-spouses (detailed records)
        if (romance.ExSpouses.Count > 0)
        {
            terminal.SetColor("dark_red");
            terminal.WriteLine(Loc.Get("home.family_ex_spouses", romance.ExSpouses.Count));
            terminal.SetColor("gray");
            foreach (var ex in romance.ExSpouses)
            {
                var marriageDuration = ex.MarriedGameDay > 0 && ex.DivorceGameDay > 0
                    ? Math.Max(0, ex.DivorceGameDay - ex.MarriedGameDay)
                    : (ex.DivorceDate - ex.MarriedDate).Days; // Fallback for old saves
                var daysSinceDivorce = ex.DivorceGameDay > 0
                    ? Math.Max(0, DailySystemManager.Instance.CurrentDay - ex.DivorceGameDay)
                    : (DateTime.Now - ex.DivorceDate).Days; // Fallback for old saves
                var initiator = ex.PlayerInitiated ? Loc.Get("home.family_ex_by_you") : Loc.Get("home.family_ex_by_them");

                terminal.Write($"    - {ex.NPCName}");
                terminal.SetColor("dark_gray");
                terminal.WriteLine(Loc.Get("home.family_ex_marriage_info", marriageDuration, daysSinceDivorce, initiator));
                terminal.SetColor("gray");

                if (ex.ChildrenTogether > 0)
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("home.family_ex_children", ex.ChildrenTogether));
                    terminal.SetColor("gray");
                }
            }
            terminal.WriteLine();
        }

        // Show other exes (ex-lovers, not ex-spouses)
        var exLoversOnly = romance.Exes.Where(id => !romance.ExSpouses.Any(es => es.NPCId == id)).ToList();
        if (exLoversOnly.Count > 0)
        {
            terminal.SetColor("dark_gray");
            terminal.WriteLine(Loc.Get("home.family_past_label", exLoversOnly.Count));
            terminal.SetColor("gray");
            foreach (var exId in exLoversOnly.Take(5)) // Show max 5
            {
                var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == exId);
                var name = npc?.Name ?? exId;
                terminal.WriteLine($"    - {name}");
            }
            if (exLoversOnly.Count > 5)
            {
                terminal.WriteLine(Loc.Get("home.family_and_more", exLoversOnly.Count - 5));
            }
            terminal.WriteLine();
        }

        if (!hasFamily)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("home.family_alone"));
            terminal.WriteLine();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("home.family_tip"));
        }

        terminal.SetColor("white");
        await terminal.WaitForKey();
    }

    private async Task InteractWithChild()
    {
        var children = FamilySystem.Instance.GetChildrenOf(currentPlayer)
            .Where(c => c.Age < FamilySystem.ADULT_AGE && c.Location == GameConfig.ChildLocationHome && !c.Deleted && !c.Kidnapped)
            .ToList();

        if (children.Count == 0)
        {
            terminal.WriteLine();
            terminal.WriteLine(Loc.Get("home.parenting_no_children"), "gray");
            await terminal.WaitForKey();
            return;
        }

        // Select child
        Child selectedChild;
        if (children.Count == 1)
        {
            selectedChild = children[0];
        }
        else
        {
            terminal.WriteLine();
            WriteBoxHeader(Loc.Get("home.children_interact"), "bright_cyan", 38);
            terminal.WriteLine();
            terminal.WriteLine(Loc.Get("home.parenting_select"), "bright_white");
            terminal.WriteLine();

            for (int i = 0; i < children.Count; i++)
            {
                var c = children[i];
                string soulColor = c.Soul > 100 ? "bright_cyan" : (c.Soul < -100 ? "red" : "white");
                terminal.Write($"  ");
                terminal.Write($"[{i + 1}] ", "bright_yellow");
                terminal.Write($"{c.Name}", "bright_white");
                terminal.Write($" (age {c.Age}, ", "gray");
                terminal.Write(c.GetSoulDescription(), soulColor);
                terminal.WriteLine(")", "gray");
            }

            terminal.WriteLine();
            string input = await GetChoice();
            if (!int.TryParse(input, out int idx) || idx < 1 || idx > children.Count)
                return;
            selectedChild = children[idx - 1];
        }

        // Check cooldown
        int currentDay = DailySystemManager.Instance?.CurrentDay ?? 0;
        if (selectedChild.LastParentingDay >= currentDay && currentDay > 0)
        {
            terminal.WriteLine();
            terminal.WriteLine(Loc.Get("home.parenting_cooldown", selectedChild.Name), "yellow");
            await terminal.WaitForKey();
            return;
        }

        // Get a random scenario for this child's age group
        var scenario = ParentingScenarios.GetRandomScenario(selectedChild);

        // Display scenario
        terminal.WriteLine();
        WriteBoxHeader(Loc.Get("home.children_interact"), "bright_cyan", 38);
        terminal.WriteLine();
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get(scenario.DescriptionKey, selectedChild.Name));
        terminal.WriteLine();

        // Display choices
        for (int i = 0; i < scenario.Choices.Length; i++)
        {
            terminal.Write($"  [{i + 1}] ", "bright_yellow");
            terminal.WriteLine(Loc.Get(scenario.Choices[i].LabelKey), "white");
        }

        terminal.WriteLine();
        string choiceInput = await GetChoice();
        if (!int.TryParse(choiceInput, out int choiceIdx) || choiceIdx < 1 || choiceIdx > scenario.Choices.Length)
            return;

        var chosen = scenario.Choices[choiceIdx - 1];

        // Calculate final soul change with alignment modifier
        int alignmentMod = ParentingScenarios.CalculateAlignmentModifier(currentPlayer, chosen);
        int finalChange = chosen.SoulChange + alignmentMod;

        // Apply soul change
        if (finalChange > 0)
            selectedChild.ImproveSoul(finalChange);
        else if (finalChange < 0)
            selectedChild.WorsenSoul(Math.Abs(finalChange));

        // Display result
        terminal.WriteLine();
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get(chosen.ResultKey, selectedChild.Name));
        terminal.WriteLine();

        // Show soul change
        if (finalChange > 0)
        {
            terminal.WriteLine(Loc.Get("home.parenting_soul_gain", selectedChild.Name, finalChange), "bright_green");
        }
        else if (finalChange < 0)
        {
            terminal.WriteLine(Loc.Get("home.parenting_soul_loss", selectedChild.Name, finalChange), "red");
        }

        // Show alignment influence message
        if (alignmentMod > 0 && chosen.IsVirtuous)
            terminal.WriteLine(Loc.Get("home.parenting_alignment_virtue"), "bright_cyan");
        else if (alignmentMod < 0 && !chosen.IsVirtuous && !chosen.IsNeutral)
            terminal.WriteLine(Loc.Get("home.parenting_alignment_dark"), "dark_red");
        else if (alignmentMod < 0 && chosen.IsVirtuous)
            terminal.WriteLine(Loc.Get("home.parenting_alignment_mismatch_virtue"), "yellow");
        else if (alignmentMod > 0 && !chosen.IsVirtuous && !chosen.IsNeutral)
            terminal.WriteLine(Loc.Get("home.parenting_alignment_mismatch_dark"), "yellow");

        // Show current soul status
        string soulDescColor = selectedChild.Soul > 100 ? "bright_cyan" : (selectedChild.Soul < -100 ? "red" : "white");
        terminal.Write(Loc.Get("home.parenting_soul_status", selectedChild.Name), "gray");
        terminal.WriteLine($" {selectedChild.GetSoulDescription()}", soulDescColor);

        // Set cooldown
        selectedChild.LastParentingDay = currentDay;

        await terminal.WaitForKey();
    }

    private async Task SpendTimeWithSpouse()
    {
        var romance = RomanceTracker.Instance;

        if (romance.Spouses.Count == 0 && romance.CurrentLovers.Count == 0)
        {
            terminal.WriteLine(Loc.Get("home.partner_no_spouse"), "yellow");
            terminal.WriteLine(Loc.Get("home.partner_go_meet_msg"), "gray");
            await terminal.WaitForKey();
            return;
        }

        terminal.WriteLine("\n", "white");
        terminal.SetColor("bright_magenta");
        terminal.WriteLine(Loc.Get("home.partner_who_spend"));
        terminal.WriteLine();

        var options = new List<(string id, string name, string type)>();

        foreach (var spouse in romance.Spouses)
        {
            var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == spouse.NPCId);
            if (npc == null || npc.IsDead) continue;
            options.Add((spouse.NPCId, npc.Name ?? spouse.NPCId, "spouse"));
        }

        foreach (var lover in romance.CurrentLovers)
        {
            var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == lover.NPCId);
            if (npc == null || npc.IsDead) continue;
            options.Add((lover.NPCId, npc.Name ?? lover.NPCId, "lover"));
        }

        terminal.SetColor("white");
        for (int i = 0; i < options.Count; i++)
        {
            var opt = options[i];
            terminal.Write($"  [{i + 1}] ");
            terminal.SetColor(opt.type == "spouse" ? "bright_red" : "bright_magenta");
            terminal.Write($"<3 {opt.name}");
            terminal.SetColor("gray");
            terminal.WriteLine($" ({opt.type})");
        }
        terminal.SetColor("bright_yellow");
        terminal.Write("  [0]");
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("home.partner_cancel_label"));
        terminal.WriteLine();

        var input = await terminal.GetInput(Loc.Get("ui.choice"));
        if (!int.TryParse(input, out int choice) || choice < 1 || choice > options.Count)
        {
            terminal.WriteLine(Loc.Get("ui.cancelled"), "gray");
            await terminal.WaitForKey();
            return;
        }

        var selected = options[choice - 1];
        var selectedNpc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == selected.id);

        if (selectedNpc == null)
        {
            terminal.WriteLine(Loc.Get("home.partner_not_available_msg", selected.name), "yellow");
            await terminal.WaitForKey();
            return;
        }

        await SpendQualityTime(selectedNpc, selected.type);
    }

    private async Task SpendQualityTime(NPC partner, string relationType)
    {
        partner.IsInConversation = true; // Protect from world sim during romantic interaction
        try
        {
        terminal.WriteLine("\n", "white");
        terminal.SetColor("bright_magenta");
        terminal.WriteLine(Loc.Get("home.partner_quality_time", partner.Name));
        terminal.WriteLine();

        terminal.SetColor("bright_yellow");
        terminal.Write("  [1]");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("home.partner_dinner"));
        terminal.SetColor("bright_yellow");
        terminal.Write("  [2]");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("home.partner_walk"));
        terminal.SetColor("bright_yellow");
        terminal.Write("  [3]");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("home.partner_cuddle"));
        terminal.SetColor("bright_yellow");
        terminal.Write("  [4]");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("home.partner_conversation"));
        if (relationType == "spouse")
        {
            terminal.SetColor("bright_yellow");
            terminal.Write("  [5]");
            terminal.SetColor("bright_red");
            terminal.WriteLine(Loc.Get("home.partner_bedroom_option"));
            terminal.SetColor("bright_yellow");
            terminal.Write("  [6]");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("home.partner_discuss_option"));
        }
        terminal.SetColor("bright_yellow");
        terminal.Write("  [0]");
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("home.partner_cancel_label"));
        terminal.WriteLine();

        var input = await terminal.GetInput(Loc.Get("ui.choice"));
        if (!int.TryParse(input, out int choice) || choice < 1)
        {
            terminal.WriteLine(Loc.Get("home.partner_time_alone_msg"), "gray");
            await terminal.WaitForKey();
            return;
        }

        terminal.WriteLine();

        switch (choice)
        {
            case 1: // Romantic dinner
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("home.partner_dinner_prepare", partner.Name));
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("home.partner_dinner_candlelight"));
                terminal.WriteLine(Loc.Get("home.partner_dinner_gaze", partner.Name));

                // XP bonus for married couples
                if (relationType == "spouse")
                {
                    long xpBonus = currentPlayer.Level * 50;
                    currentPlayer.Experience += xpBonus;
                    terminal.SetColor("bright_green");
                    terminal.WriteLine(Loc.Get("home.partner_bond_xp", xpBonus));
                }
                break;

            case 2: // Walk and hold hands
                terminal.SetColor("cyan");
                terminal.WriteLine(Loc.Get("home.partner_walk_garden", partner.Name));
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("home.partner_walk_evening"));
                terminal.WriteLine(Loc.Get("home.partner_walk_head", partner.Name));

                // Small HP recovery from relaxation
                currentPlayer.HP = Math.Min(currentPlayer.HP + currentPlayer.MaxHP / 20, currentPlayer.MaxHP);
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("home.partner_walk_restores"));
                break;

            case 3: // Cuddle by fire
                terminal.SetColor("bright_red");
                terminal.WriteLine(Loc.Get("home.partner_cuddle_fire"));
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("home.partner_cuddle_nestle", partner.Name));
                terminal.WriteLine(Loc.Get("home.partner_cuddle_peace"));

                // Mana recovery from emotional connection
                currentPlayer.Mana = Math.Min(currentPlayer.Mana + currentPlayer.MaxMana / 10, currentPlayer.MaxMana);
                terminal.SetColor("bright_blue");
                terminal.WriteLine(Loc.Get("home.partner_cuddle_renewed"));
                break;

            case 4: // Deep conversation
                await VisualNovelDialogueSystem.Instance.StartConversation(currentPlayer, partner, terminal);
                return; // Already handled

            case 5: // Bedroom (spouse only)
                if (relationType == "spouse")
                {
                    await IntimacySystem.Instance.InitiateIntimateScene(currentPlayer, partner, terminal);
                    return;
                }
                terminal.WriteLine(Loc.Get("ui.invalid_choice"), "gray");
                break;

            case 6: // Discuss relationship (spouse only)
                if (relationType == "spouse")
                {
                    await DiscussRelationship(partner);
                    return;
                }
                terminal.WriteLine(Loc.Get("ui.invalid_choice"), "gray");
                break;

            default:
                terminal.WriteLine(Loc.Get("ui.invalid_choice"), "gray");
                break;
        }

        await terminal.WaitForKey();
        }
        finally { partner.IsInConversation = false; }
    }

    private async Task DiscussRelationship(NPC spouse)
    {
        var romance = RomanceTracker.Instance;
        var spouseData = romance.Spouses.FirstOrDefault(s => s.NPCId == spouse.ID);

        terminal.WriteLine("\n", "white");
        WriteSectionHeader(Loc.Get("home.relationship_discussion"), "bright_cyan");
        terminal.WriteLine();

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("home.discuss_sit_msg", spouse.Name));
        terminal.WriteLine();

        // Show current status
        if (spouseData != null)
        {
            terminal.SetColor("gray");
            var marriageDays = spouseData.MarriedGameDay > 0
                ? Math.Max(0, DailySystemManager.Instance.CurrentDay - spouseData.MarriedGameDay)
                : (int)(DateTime.Now - spouseData.MarriedDate).TotalDays; // Fallback for old saves
            terminal.WriteLine(Loc.Get("home.discuss_duration_msg", marriageDays));
            terminal.WriteLine(Loc.Get("home.discuss_children_msg", spouseData.Children));
            terminal.WriteLine(Loc.Get("home.discuss_polyamory_msg", spouseData.AcceptsPolyamory ? Loc.Get("home.discuss_polyamory_open_msg") : Loc.Get("home.discuss_polyamory_mono_msg")));
            terminal.WriteLine();
        }

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("home.discuss_what_msg"));
        terminal.WriteLine();
        terminal.SetColor("bright_yellow");
        terminal.Write("  [1]");
        terminal.SetColor("white");
        terminal.WriteLine($" {Loc.Get("home.discuss_opt_love")}");

        if (spouseData != null && !spouseData.AcceptsPolyamory)
        {
            terminal.SetColor("bright_yellow");
            terminal.Write("  [2]");
            terminal.SetColor("magenta");
            terminal.WriteLine($" {Loc.Get("home.discuss_opt_poly_open")}");
        }
        else if (spouseData != null && spouseData.AcceptsPolyamory)
        {
            terminal.SetColor("bright_yellow");
            terminal.Write("  [2]");
            terminal.SetColor("magenta");
            terminal.WriteLine($" {Loc.Get("home.discuss_opt_poly_close")}");
        }

        terminal.SetColor("bright_yellow");
        terminal.Write("  [3]");
        terminal.SetColor("red");
        terminal.WriteLine($" {Loc.Get("home.discuss_opt_divorce")}");
        terminal.SetColor("bright_yellow");
        terminal.Write("  [0]");
        terminal.SetColor("gray");
        terminal.WriteLine($" {Loc.Get("home.discuss_opt_nevermind")}");
        terminal.WriteLine();

        var input = await terminal.GetInput(Loc.Get("ui.choice"));
        if (!int.TryParse(input, out int choice) || choice < 1)
        {
            terminal.WriteLine(Loc.Get("home.discuss_talk_else_msg"), "gray");
            await terminal.WaitForKey();
            return;
        }

        switch (choice)
        {
            case 1:
                await ExpressLove(spouse);
                break;
            case 2:
                await DiscussPolyamory(spouse, spouseData);
                break;
            case 3:
                await DiscussDivorce(spouse, spouseData);
                break;
            default:
                terminal.WriteLine(Loc.Get("ui.invalid_choice"), "gray");
                break;
        }

        await terminal.WaitForKey();
    }

    private async Task ExpressLove(NPC spouse)
    {
        terminal.WriteLine();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine(Loc.Get("home.love_take_hands", spouse.Name));
        terminal.WriteLine();

        await Task.Delay(1000);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("home.love_line1"));
        terminal.WriteLine(Loc.Get("home.love_line2"));
        terminal.WriteLine();

        await Task.Delay(1500);

        var personality = spouse.Brain?.Personality;
        float romanticism = personality?.Romanticism ?? 0.5f;

        terminal.SetColor("bright_cyan");
        if (romanticism > 0.6f)
        {
            terminal.WriteLine(Loc.Get("home.love_romantic_eyes", spouse.Name));
            terminal.WriteLine(Loc.Get("home.love_romantic_whisper"));
        }
        else
        {
            terminal.WriteLine(Loc.Get("home.love_warm_smile", spouse.Name));
            terminal.WriteLine(Loc.Get("home.love_warm_reply"));
        }

        // Boost relationship (lower number = better in this system)
        var spouseRecord = RomanceTracker.Instance.Spouses.FirstOrDefault(s => s.NPCId == spouse.ID);
        if (spouseRecord != null)
        {
            spouseRecord.LoveLevel = Math.Max(1, spouseRecord.LoveLevel - 2);
        }

        terminal.SetColor("bright_green");
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("home.love_bond_deepens_msg"));
    }

    private async Task DiscussPolyamory(NPC spouse, Spouse? spouseData)
    {
        if (spouseData == null) return;

        terminal.WriteLine();

        if (!spouseData.AcceptsPolyamory)
        {
            // Trying to open the marriage
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("home.poly_broach", spouse.Name));
            terminal.WriteLine();

            await Task.Delay(1000);

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("home.poly_line1"));
            terminal.WriteLine(Loc.Get("home.poly_line2"));
            terminal.WriteLine(Loc.Get("home.poly_line3"));
            terminal.WriteLine(Loc.Get("home.poly_line4"));
            terminal.WriteLine();

            await Task.Delay(2000);

            var personality = spouse.Brain?.Personality;
            // Use Adventurousness as proxy for openness to new relationship structures
            float openness = personality?.Adventurousness ?? 0.5f;
            float jealousy = personality?.Jealousy ?? 0.5f;

            // Check if spouse would accept based on personality
            bool wouldAccept = openness > 0.6f && jealousy < 0.4f;

            // Also factor in relationship strength
            int loveLevel = spouseData.LoveLevel;
            if (loveLevel >= 15 && jealousy < 0.5f) wouldAccept = true;

            terminal.SetColor("bright_cyan");
            if (wouldAccept)
            {
                terminal.WriteLine(Loc.Get("home.poly_accept_quiet", spouse.Name));
                terminal.WriteLine();
                terminal.WriteLine(Loc.Get("home.poly_accept_thought"));
                terminal.WriteLine(Loc.Get("home.poly_accept_strong"));
                terminal.WriteLine(Loc.Get("home.poly_accept_diminish"));
                terminal.WriteLine();

                await Task.Delay(1500);

                terminal.SetColor("bright_magenta");
                terminal.WriteLine(Loc.Get("home.poly_accept_willing"));
                terminal.WriteLine(Loc.Get("home.poly_accept_promise"));
                terminal.WriteLine();

                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("home.poly_open_success"));

                spouseData.AcceptsPolyamory = true;
                spouseData.KnowsAboutOthers = true;
            }
            else
            {
                terminal.WriteLine(Loc.Get("home.poly_reject_falls", spouse.Name));
                terminal.WriteLine();

                if (jealousy > 0.6f)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("home.poly_reject_jealous1"));
                    terminal.WriteLine(Loc.Get("home.poly_reject_jealous2"));
                    terminal.WriteLine();
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("home.poly_tense"));

                    // Damage relationship (higher number = worse in this system)
                    if (spouseData != null)
                    {
                        spouseData.LoveLevel = Math.Min(100, spouseData.LoveLevel + 3);
                    }
                }
                else
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("home.poly_reject_gentle1"));
                    terminal.WriteLine(Loc.Get("home.poly_reject_gentle2"));
                    terminal.WriteLine(Loc.Get("home.poly_reject_gentle3"));
                    terminal.WriteLine();
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("home.poly_not_ready"));

                    // Small relationship impact (higher number = worse)
                    if (spouseData != null)
                    {
                        spouseData.LoveLevel = Math.Min(100, spouseData.LoveLevel + 1);
                    }
                }
            }
        }
        else
        {
            // Already poly, discussing returning to monogamy
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("home.poly_close_approach", spouse.Name));
            terminal.WriteLine();

            await Task.Delay(1000);

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("home.mono_line1"));
            terminal.WriteLine(Loc.Get("home.mono_line2"));
            terminal.WriteLine();

            await Task.Delay(1500);

            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("home.mono_nods", spouse.Name));
            terminal.WriteLine(Loc.Get("home.mono_happy"));
            terminal.WriteLine(Loc.Get("home.mono_together"));
            terminal.WriteLine();

            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("home.poly_now_mono"));

            spouseData.AcceptsPolyamory = false;

            // Note: This doesn't automatically remove other lovers
            // The player will need to handle those relationships separately
            if (RomanceTracker.Instance.CurrentLovers.Count > 0)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine();
                terminal.WriteLine(Loc.Get("home.mono_note_other_relationships"));
            }
        }
    }

    private async Task DiscussDivorce(NPC spouse, Spouse? spouseData)
    {
        if (spouseData == null) return;

        terminal.WriteLine();
        WriteSectionHeader(Loc.Get("home.difficult_conversation"), "red");
        terminal.WriteLine();

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("home.divorce_breath", spouse.Name));
        terminal.WriteLine();

        await Task.Delay(1500);

        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("home.divorce_talk_line1"));
        terminal.WriteLine(Loc.Get("home.divorce_talk_line2"));
        terminal.WriteLine();

        await Task.Delay(1500);

        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("home.divorce_concern", spouse.Name));
        terminal.WriteLine(Loc.Get("home.divorce_scaring"));
        terminal.WriteLine();

        await Task.Delay(1000);

        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("ui.confirm_divorce_ask"));

        if (spouseData.Children > 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("home.divorce_warning_children", spouseData.Children));
            terminal.WriteLine(Loc.Get("home.divorce_lose_custody"));
        }

        terminal.WriteLine();
        terminal.SetColor("bright_yellow");
        terminal.Write("  [Y]");
        terminal.SetColor("white");
        terminal.WriteLine($" {Loc.Get("home.divorce_yes")}");
        terminal.SetColor("bright_yellow");
        terminal.Write("  [N]");
        terminal.SetColor("white");
        terminal.WriteLine($" {Loc.Get("home.divorce_no")}");
        terminal.WriteLine();

        var input = await terminal.GetInput(Loc.Get("ui.choice"));
        if (input.Trim().ToUpperInvariant() != "Y")
        {
            terminal.WriteLine();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("home.divorce_reach_hand", spouse.Name));
            terminal.WriteLine(Loc.Get("home.divorce_cancel_sorry"));
            terminal.WriteLine();
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("home.divorce_cancel_relief", spouse.Name));
            return;
        }

        // Process the divorce
        terminal.WriteLine();
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("home.divorce_end_line1"));
        terminal.WriteLine(Loc.Get("home.divorce_end_line2"));
        terminal.WriteLine();

        await Task.Delay(2000);

        var personality = spouse.Brain?.Personality;
        // Use Impulsiveness as proxy for emotional volatility
        float volatility = personality?.Impulsiveness ?? 0.5f;

        terminal.SetColor("bright_cyan");
        if (volatility > 0.6f)
        {
            terminal.WriteLine(Loc.Get("home.divorce_angry_face", spouse.Name));
            terminal.WriteLine(Loc.Get("home.divorce_angry_what"));
            terminal.WriteLine(Loc.Get("home.divorce_angry_how"));
        }
        else
        {
            terminal.WriteLine(Loc.Get("home.divorce_sad_tears", spouse.Name));
            terminal.WriteLine(Loc.Get("home.divorce_sad_knew"));
            terminal.WriteLine(Loc.Get("home.divorce_sad_want"));
        }

        terminal.WriteLine();
        await Task.Delay(2000);

        // Process divorce - try RelationshipSystem first, but don't fail if it doesn't have a record
        // (RomanceTracker may have the marriage without RelationshipSystem knowing about it)
        bool relationshipSystemSuccess = RelationshipSystem.ProcessDivorce(currentPlayer, spouse, out string message);

        // Always process the RomanceTracker divorce if we have them as a spouse there
        // This ensures the divorce happens even if RelationshipSystem didn't track the marriage
        RomanceTracker.Instance.Divorce(spouse.ID, "Player requested divorce", playerInitiated: true);

        // Clear marriage flags on both characters regardless
        currentPlayer.Married = false;
        currentPlayer.IsMarried = false;
        currentPlayer.SpouseName = "";
        spouse.Married = false;
        spouse.IsMarried = false;
        spouse.SpouseName = "";

        WriteThickDivider(39, "gray");
        terminal.WriteLine();
        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("home.divorce_ended_msg"));
        terminal.WriteLine();

        if (spouseData.Children > 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("home.divorce_custody", spouse.Name));
        }

        terminal.SetColor("gray");
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("home.divorce_leaves", spouse.Name));

        // Move spouse out of home
        spouse.UpdateLocation("Inn");

        // Generate news
        NewsSystem.Instance?.WriteDivorceNews(currentPlayer.Name, spouse.Name);
    }

    private async Task DiscussIntimateFantasies(NPC spouse, Spouse? spouseData)
    {
        if (spouseData == null) return;

        terminal.WriteLine();
        WriteSectionHeader(Loc.Get("home.intimate_fantasies"), "bright_magenta");
        terminal.WriteLine();

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("home.fantasies_curl", spouse.Name));
        terminal.WriteLine(Loc.Get("home.fantasies_talk"));
        terminal.WriteLine();

        await Task.Delay(1500);

        var personality = spouse.Brain?.Personality;
        float adventurousness = personality?.Adventurousness ?? 0.5f;
        float voyeurism = personality?.Voyeurism ?? 0.3f;
        float exhibitionism = personality?.Exhibitionism ?? 0.3f;

        terminal.SetColor("bright_cyan");
        if (adventurousness > 0.5f)
        {
            terminal.WriteLine(Loc.Get("home.fantasies_playful", spouse.Name));
            terminal.WriteLine(Loc.Get("home.fantasies_listening"));
        }
        else
        {
            terminal.WriteLine(Loc.Get("home.fantasies_nervous", spouse.Name));
            terminal.WriteLine(Loc.Get("home.fantasies_what_kind"));
        }

        terminal.WriteLine();
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("home.fantasies_what_discuss"));
        terminal.WriteLine();
        terminal.SetColor("bright_yellow");
        terminal.Write("  [1]");
        terminal.SetColor("white");
        terminal.WriteLine($" {Loc.Get("home.fantasies_opt_group")}");
        terminal.SetColor("bright_yellow");
        terminal.Write("  [2]");
        terminal.SetColor("white");
        terminal.WriteLine($" {Loc.Get("home.fantasies_opt_voyeur")}");
        terminal.SetColor("bright_yellow");
        terminal.Write("  [3]");
        terminal.SetColor("white");
        terminal.WriteLine($" {Loc.Get("home.fantasies_opt_exhibit")}");
        terminal.SetColor("bright_yellow");
        terminal.Write("  [0]");
        terminal.SetColor("gray");
        terminal.WriteLine($" {Loc.Get("home.discuss_opt_nevermind")}");
        terminal.WriteLine();

        var input = await terminal.GetInput(Loc.Get("ui.choice"));
        if (!int.TryParse(input, out int choice) || choice < 1)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("home.fantasies_not_pursue"));
            return;
        }

        switch (choice)
        {
            case 1:
                await DiscussGroupEncounters(spouse, spouseData, adventurousness);
                break;
            case 2:
                await DiscussVoyeurism(spouse, spouseData, voyeurism);
                break;
            case 3:
                await DiscussExhibitionism(spouse, spouseData, exhibitionism);
                break;
        }
    }

    private async Task DiscussGroupEncounters(NPC spouse, Spouse spouseData, float adventurousness)
    {
        terminal.WriteLine();
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("home.group_line1"));
        terminal.WriteLine(Loc.Get("home.group_line2"));
        terminal.WriteLine();

        await Task.Delay(2000);

        var personality = spouse.Brain?.Personality;
        float jealousy = personality?.Jealousy ?? 0.5f;

        // Determine if spouse would be interested
        bool interested = adventurousness > 0.6f && jealousy < 0.5f;
        bool veryInterested = adventurousness > 0.75f && jealousy < 0.3f;

        terminal.SetColor("bright_cyan");
        if (veryInterested)
        {
            terminal.WriteLine(Loc.Get("home.group_very_excited", spouse.Name));
            terminal.WriteLine(Loc.Get("home.group_very_thought"));
            terminal.WriteLine(Loc.Get("home.group_very_incredible"));
            terminal.WriteLine();

            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("home.group_very_open", spouse.Name));

            // Mark as consenting
            RomanceTracker.Instance.AgreedStructures[spouse.ID] = RelationshipStructure.OpenRelationship;
        }
        else if (interested)
        {
            terminal.WriteLine(Loc.Get("home.group_considers", spouse.Name));
            terminal.WriteLine(Loc.Get("home.group_not_sure"));
            terminal.WriteLine(Loc.Get("home.group_maybe_someday"));
            terminal.WriteLine();

            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("home.group_future", spouse.Name));
        }
        else if (jealousy > 0.6f)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("home.group_jealous_hardens", spouse.Name));
            terminal.WriteLine(Loc.Get("home.group_jealous_no"));
            terminal.WriteLine(Loc.Get("home.group_jealous_believe"));
            terminal.WriteLine();

            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("home.group_upset"));

            // Severe damage and moderate divorce chance for jealous spouse
            await HandleSensitiveTopicRejection(spouse, spouseData, 8, 0.08f, "threesomes");
        }
        else
        {
            terminal.WriteLine(Loc.Get("home.group_gentle_shake", spouse.Name));
            terminal.WriteLine(Loc.Get("home.group_gentle_notinterested"));
            terminal.WriteLine(Loc.Get("home.group_gentle_justus"));
            terminal.WriteLine();

            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("home.group_not_interested"));

            // Mild damage for gentle rejection
            await HandleSensitiveTopicRejection(spouse, spouseData, 3, 0.02f, "group encounters");
        }
    }

    private async Task DiscussVoyeurism(NPC spouse, Spouse spouseData, float voyeurism)
    {
        terminal.WriteLine();
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("home.voyeur_share_line1"));
        terminal.WriteLine(Loc.Get("home.voyeur_share_line2"));
        terminal.WriteLine();

        await Task.Delay(1500);

        var personality = spouse.Brain?.Personality;
        float adventurousness = personality?.Adventurousness ?? 0.5f;

        terminal.SetColor("bright_cyan");
        if (voyeurism > 0.6f || (adventurousness > 0.7f && voyeurism > 0.4f))
        {
            terminal.WriteLine(Loc.Get("home.voyeur_intrigued", spouse.Name));
            terminal.WriteLine(Loc.Get("home.voyeur_watching_who"));
            terminal.WriteLine(Loc.Get("home.voyeur_excites"));
            terminal.WriteLine();

            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("home.voyeur_open"));
        }
        else if (adventurousness > 0.5f)
        {
            terminal.WriteLine(Loc.Get("home.voyeur_thoughtful", spouse.Name));
            terminal.WriteLine(Loc.Get("home.voyeur_interesting"));
            terminal.WriteLine(Loc.Get("home.voyeur_what_mind"));
            terminal.WriteLine();

            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("home.voyeur_curious"));
        }
        else
        {
            terminal.WriteLine(Loc.Get("home.voyeur_puzzled", spouse.Name));
            terminal.WriteLine(Loc.Get("home.voyeur_not_into"));
            terminal.WriteLine(Loc.Get("home.voyeur_only_one"));
            terminal.WriteLine();

            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("home.voyeur_prefer_trad"));

            // Light damage for this topic
            await HandleSensitiveTopicRejection(spouse, spouseData, 2, 0.01f, "voyeurism");
        }
    }

    private async Task DiscussExhibitionism(NPC spouse, Spouse spouseData, float exhibitionism)
    {
        terminal.WriteLine();
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("home.exhibit_confess_line1"));
        terminal.WriteLine(Loc.Get("home.exhibit_confess_line2"));
        terminal.WriteLine();

        await Task.Delay(1500);

        var personality = spouse.Brain?.Personality;
        float adventurousness = personality?.Adventurousness ?? 0.5f;

        terminal.SetColor("bright_cyan");
        if (exhibitionism > 0.6f || (adventurousness > 0.7f && exhibitionism > 0.4f))
        {
            terminal.WriteLine(Loc.Get("home.exhibit_desire", spouse.Name));
            terminal.WriteLine(Loc.Get("home.exhibit_similar"));
            terminal.WriteLine(Loc.Get("home.exhibit_thrill"));
            terminal.WriteLine();

            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("home.exhibit_share"));
        }
        else if (adventurousness > 0.5f)
        {
            terminal.WriteLine(Loc.Get("home.exhibit_surprised", spouse.Name));
            terminal.WriteLine(Loc.Get("home.exhibit_bold"));
            terminal.WriteLine(Loc.Get("home.exhibit_no_judge"));
            terminal.WriteLine();

            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("home.exhibit_understanding"));
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("home.exhibit_uncomfortable", spouse.Name));
            terminal.WriteLine(Loc.Get("home.exhibit_never"));
            terminal.WriteLine(Loc.Get("home.exhibit_private"));
            terminal.WriteLine();

            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("home.exhibit_prefer_privacy"));

            // Moderate damage - exhibitionism can be uncomfortable for conservative partners
            await HandleSensitiveTopicRejection(spouse, spouseData, 4, 0.03f, "exhibitionism");
        }
    }

    private async Task DiscussAlternativeArrangements(NPC spouse, Spouse? spouseData)
    {
        if (spouseData == null) return;

        terminal.WriteLine();
        WriteSectionHeader(Loc.Get("home.alternative_arrangements"), "bright_magenta");
        terminal.WriteLine();

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("home.alt_broach", spouse.Name));
        terminal.WriteLine(Loc.Get("home.alt_unconventional"));
        terminal.WriteLine();

        await Task.Delay(1500);

        var personality = spouse.Brain?.Personality;
        float adventurousness = personality?.Adventurousness ?? 0.5f;

        terminal.SetColor("bright_cyan");
        if (adventurousness > 0.5f)
        {
            terminal.WriteLine(Loc.Get("home.alt_eyebrow", spouse.Name));
            terminal.WriteLine(Loc.Get("home.alt_listening"));
        }
        else
        {
            terminal.WriteLine(Loc.Get("home.alt_uncertain", spouse.Name));
            terminal.WriteLine(Loc.Get("home.alt_what_mean"));
        }

        terminal.WriteLine();
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("home.alt_what_arrangement"));
        terminal.WriteLine();
        terminal.SetColor("bright_yellow");
        terminal.Write("  [1]");
        terminal.SetColor("white");
        terminal.WriteLine($" {Loc.Get("home.alt_opt_hotwife")}");
        terminal.SetColor("bright_yellow");
        terminal.Write("  [2]");
        terminal.SetColor("white");
        terminal.WriteLine($" {Loc.Get("home.alt_opt_cuckold")}");
        terminal.SetColor("bright_yellow");
        terminal.Write("  [3]");
        terminal.SetColor("white");
        terminal.WriteLine($" {Loc.Get("home.alt_opt_stag")}");
        terminal.SetColor("bright_yellow");
        terminal.Write("  [0]");
        terminal.SetColor("gray");
        terminal.WriteLine($" {Loc.Get("home.discuss_opt_nevermind")}");
        terminal.WriteLine();

        var input = await terminal.GetInput(Loc.Get("ui.choice"));
        if (!int.TryParse(input, out int choice) || choice < 1)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("home.alt_not_pursue"));
            return;
        }

        switch (choice)
        {
            case 1:
                await DiscussHotwifing(spouse, spouseData);
                break;
            case 2:
                await DiscussCuckolding(spouse, spouseData);
                break;
            case 3:
                await DiscussStagVixen(spouse, spouseData);
                break;
        }
    }

    private async Task DiscussHotwifing(NPC spouse, Spouse spouseData)
    {
        terminal.WriteLine();
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("home.hw_thinking"));
        terminal.WriteLine(Loc.Get("home.hw_idea_blessing"));
        terminal.WriteLine(Loc.Get("home.hw_called"));
        terminal.WriteLine();

        await Task.Delay(2000);

        var personality = spouse.Brain?.Personality;
        float adventurousness = personality?.Adventurousness ?? 0.5f;
        float flirtatiousness = personality?.Flirtatiousness ?? 0.5f;
        float sensuality = personality?.Sensuality ?? 0.5f;

        // Higher chance if spouse is adventurous, flirtatious, and sensual
        bool interested = (adventurousness > 0.6f && flirtatiousness > 0.5f) ||
                         (sensuality > 0.7f && adventurousness > 0.5f);

        terminal.SetColor("bright_cyan");
        if (interested)
        {
            terminal.WriteLine(Loc.Get("home.hw_quiet", spouse.Name));
            terminal.WriteLine(Loc.Get("home.hw_with_others"));
            terminal.WriteLine(Loc.Get("home.hw_enjoy_knowing"));
            terminal.WriteLine();

            await Task.Delay(1500);

            terminal.WriteLine(Loc.Get("home.alt_slow_smile"));
            terminal.WriteLine(Loc.Get("home.hw_never_thought"));
            terminal.WriteLine(Loc.Get("home.hw_could_enjoy"));
            terminal.WriteLine();

            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("home.hw_agrees", spouse.Name));

            // Set up arrangement tracking
            spouseData.AcceptsPolyamory = true;
            spouseData.KnowsAboutOthers = true;
            RomanceTracker.Instance.AgreedStructures[spouse.ID] = RelationshipStructure.OpenRelationship;

            await Task.Delay(1500);

            // Offer to try it now
            terminal.WriteLine();
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("home.alt_hw_tonight"));
            terminal.WriteLine();
            terminal.SetColor("bright_yellow");
            terminal.Write("  [Y]");
            terminal.SetColor("white");
            terminal.WriteLine($" {Loc.Get("home.alt_yes_try")}");
            terminal.SetColor("bright_yellow");
            terminal.Write("  [N]");
            terminal.SetColor("white");
            terminal.WriteLine($" {Loc.Get("home.alt_no_another_time")}");
            terminal.WriteLine();

            var input = await terminal.GetInput(Loc.Get("ui.choice"));
            if (input.Trim().ToUpperInvariant() == "Y")
            {
                await PlayHotwifingScene(spouse, spouseData);
            }
        }
        else if (adventurousness > 0.4f)
        {
            terminal.WriteLine(Loc.Get("home.hw_surprised", spouse.Name));
            terminal.WriteLine(Loc.Get("home.hw_a_lot"));
            terminal.WriteLine(Loc.Get("home.hw_not_saying_no"));
            terminal.WriteLine();

            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("home.alt_hw_need_time"));
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("home.hw_flushes", spouse.Name));
            terminal.WriteLine(Loc.Get("home.hw_other_people"));
            terminal.WriteLine(Loc.Get("home.hw_not_comfortable"));
            terminal.WriteLine();

            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("home.alt_hw_upset"));

            // Significant damage - hotwifing is a major ask
            await HandleSensitiveTopicRejection(spouse, spouseData, 6, 0.06f, "hotwifing");
        }
    }

    private async Task PlayHotwifingScene(NPC spouse, Spouse spouseData)
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("home.night_to_remember"), "bright_magenta");
        terminal.WriteLine();

        // Find a suitable third party NPC (exclude dead NPCs)
        var potentialDates = NPCSpawnSystem.Instance?.ActiveNPCs?
            .Where(n => n.IsAlive && !n.IsDead && n.ID != spouse.ID)
            .Where(n => spouse.Sex == CharacterSex.Female ? n.Sex == CharacterSex.Male : n.Sex == CharacterSex.Female)
            .OrderByDescending(n => n.Level)
            .Take(5)
            .ToList() ?? new List<NPC>();

        if (potentialDates.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("home.alt_no_one_tonight"));
            terminal.WriteLine(Loc.Get("home.alt_hw_find_later"));
            return;
        }

        // Select a random one
        var random = Random.Shared;
        var thirdParty = potentialDates[random.Next(potentialDates.Count)];
        string thirdName = thirdParty.Name;
        string spouseGender = spouse.Sex == CharacterSex.Female ? "she" : "he";
        string spousePossessive = spouse.Sex == CharacterSex.Female ? "her" : "his";
        string thirdGender = thirdParty.Sex == CharacterSex.Female ? "she" : "he";

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("home.hw_gets_ready", spouse.Name, spousePossessive));
        terminal.WriteLine(Loc.Get("home.hw_prepares", spouseGender));
        terminal.WriteLine();

        await Task.Delay(2000);

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("home.hw_asked_out", thirdName, spouseGender));
        terminal.WriteLine(Loc.Get("home.hw_told_married", thirdParty.Sex == CharacterSex.Female ? "her" : "him"));
        terminal.WriteLine(Loc.Get("home.hw_permission", $"{spouseGender.ToUpperInvariant()[0]}{spouseGender.Substring(1)}"));
        terminal.WriteLine();

        await Task.Delay(2000);

        WriteSectionHeader(Loc.Get("home.hw_leaves_date", spouse.Name, spousePossessive), "gray");
        terminal.WriteLine();

        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("home.hw_hours_pass"));
        terminal.WriteLine(Loc.Get("home.hw_anticipation"));
        terminal.WriteLine();

        await Task.Delay(2000);

        // The date scene (described, not shown)
        WriteSectionHeader(Loc.Get("home.later_that_night"), "bright_magenta");
        terminal.WriteLine();

        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("home.hw_returns", spouse.Name));
        terminal.WriteLine(Loc.Get("home.hw_smoldering", $"{spouseGender.ToUpperInvariant()[0]}{spouseGender.Substring(1)}"));
        terminal.WriteLine();

        await Task.Delay(1500);

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("home.hw_attentive", thirdName, spouseGender));
        terminal.WriteLine(Loc.Get("home.hw_dinner_drinks"));
        terminal.WriteLine();

        await Task.Delay(2000);

        // Spouse describes the encounter
        terminal.SetColor("bright_magenta");
        terminal.WriteLine(Loc.Get("home.hw_tells_everything", $"{spouseGender.ToUpperInvariant()[0]}{spouseGender.Substring(1)}"));
        terminal.WriteLine(Loc.Get("home.hw_details", thirdName));
        terminal.WriteLine(Loc.Get("home.hw_whispered", spouseGender));
        terminal.WriteLine();

        await Task.Delay(2500);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("home.hw_came_home", spouseGender));
        terminal.WriteLine(Loc.Get("home.hw_always_home"));
        terminal.WriteLine();

        await Task.Delay(1500);

        // The reclamation
        WriteSectionHeader(Loc.Get("home.reclamation"), "bright_red");
        terminal.WriteLine();

        await Task.Delay(1000);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("home.hw_the_fire"));
        terminal.WriteLine(Loc.Get("home.hw_electric"));
        terminal.WriteLine(Loc.Get("home.hw_claim_yours", spouseGender));
        terminal.WriteLine();

        await Task.Delay(2000);

        terminal.SetColor("bright_magenta");
        terminal.WriteLine(Loc.Get("home.hw_night_unlike"));
        terminal.WriteLine(Loc.Get("home.hw_stories_fuel", spouseGender));
        terminal.WriteLine(Loc.Get("home.hw_morning_exhausted"));
        terminal.WriteLine();

        await Task.Delay(1500);

        // Record the encounter and set up arrangement
        RomanceTracker.Instance.SetupCuckoldArrangement(spouse.ID, thirdParty.ID, true);

        // Relationship boost
        spouseData.LoveLevel = Math.Max(1, spouseData.LoveLevel - 3);

        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("home.hw_bond_deepened"));
        terminal.WriteLine();

        await terminal.GetInput(Loc.Get("ui.press_enter"));
    }

    private async Task DiscussCuckolding(NPC spouse, Spouse spouseData)
    {
        terminal.WriteLine();
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("home.cuck_line1"));
        terminal.WriteLine(Loc.Get("home.cuck_line2"));
        terminal.WriteLine(Loc.Get("home.cuck_line3"));
        terminal.WriteLine(Loc.Get("home.cuck_line4"));
        terminal.WriteLine();

        await Task.Delay(2500);

        var personality = spouse.Brain?.Personality;
        float adventurousness = personality?.Adventurousness ?? 0.5f;
        float dominance = 1.0f - (personality?.Tenderness ?? 0.5f); // Higher tenderness = less dominant

        // Cuckolding requires specific personality combination
        bool compatible = adventurousness > 0.6f && dominance > 0.5f;

        terminal.SetColor("bright_cyan");
        if (compatible)
        {
            terminal.WriteLine(Loc.Get("home.cuck_studies", spouse.Name));
            terminal.WriteLine(Loc.Get("home.cuck_dominant"));
            terminal.WriteLine(Loc.Get("home.cuck_other_lovers"));
            terminal.WriteLine();

            await Task.Delay(1500);

            terminal.WriteLine(Loc.Get("home.cuck_shift"));
            terminal.WriteLine(Loc.Get("home.cuck_power_intriguing"));
            terminal.WriteLine(Loc.Get("home.cuck_truly_want"));
            terminal.WriteLine();

            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("home.cuck_agrees", spouse.Name));

            // Set up cuckold arrangement
            spouseData.AcceptsPolyamory = true;
            RomanceTracker.Instance.AgreedStructures[spouse.ID] = RelationshipStructure.OpenRelationship;

            await Task.Delay(1500);

            // Offer to try it now
            terminal.WriteLine();
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("home.cuck_tonight"));
            terminal.WriteLine();
            terminal.SetColor("bright_yellow");
            terminal.Write("  [Y]");
            terminal.SetColor("white");
            terminal.WriteLine($" {Loc.Get("home.alt_yes_try")}");
            terminal.SetColor("bright_yellow");
            terminal.Write("  [N]");
            terminal.SetColor("white");
            terminal.WriteLine($" {Loc.Get("home.alt_no_another_time")}");
            terminal.WriteLine();

            var input = await terminal.GetInput(Loc.Get("ui.choice"));
            if (input.Trim().ToUpperInvariant() == "Y")
            {
                await PlayCuckoldingScene(spouse, spouseData);
            }
        }
        else if (adventurousness > 0.4f)
        {
            terminal.WriteLine(Loc.Get("home.cuck_confused", spouse.Name));
            terminal.WriteLine(Loc.Get("home.cuck_not_understand_line"));
            terminal.WriteLine(Loc.Get("home.cuck_complicated"));
            terminal.WriteLine();

            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("home.cuck_not_understand"));

            // Moderate damage for confusion
            await HandleSensitiveTopicRejection(spouse, spouseData, 4, 0.03f, "cuckolding");
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("home.cuck_disturbed", spouse.Name));
            terminal.WriteLine(Loc.Get("home.cuck_punishment"));
            terminal.WriteLine(Loc.Get("home.cuck_dont_want"));
            terminal.WriteLine();

            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("home.cuck_upset"));

            // Severe damage - cuckolding request can be very disturbing to some
            await HandleSensitiveTopicRejection(spouse, spouseData, 10, 0.12f, "cuckolding");
        }
    }

    private async Task PlayCuckoldingScene(NPC spouse, Spouse spouseData)
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("home.the_arrangement"), "bright_magenta");
        terminal.WriteLine();

        // Find a suitable third party NPC (exclude dead NPCs)
        var potentialLovers = NPCSpawnSystem.Instance?.ActiveNPCs?
            .Where(n => n.IsAlive && !n.IsDead && n.ID != spouse.ID)
            .Where(n => spouse.Sex == CharacterSex.Female ? n.Sex == CharacterSex.Male : n.Sex == CharacterSex.Female)
            .OrderByDescending(n => n.Level)
            .Take(5)
            .ToList() ?? new List<NPC>();

        if (potentialLovers.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("home.cuck_no_one"));
            terminal.WriteLine(Loc.Get("home.alt_hw_find_later"));
            return;
        }

        // Select a random one
        var random = Random.Shared;
        var thirdParty = potentialLovers[random.Next(potentialLovers.Count)];
        string thirdName = thirdParty.Name;
        string spouseGender = spouse.Sex == CharacterSex.Female ? "she" : "he";
        string spousePossessive = spouse.Sex == CharacterSex.Female ? "her" : "his";
        string thirdGender = thirdParty.Sex == CharacterSex.Female ? "she" : "he";
        string thirdPossessive = thirdParty.Sex == CharacterSex.Female ? "her" : "his";

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("home.cuck_sends_message", spouse.Name, thirdName));
        terminal.WriteLine(Loc.Get("home.cuck_come_over"));
        terminal.WriteLine();

        await Task.Delay(2000);

        WriteSectionHeader(Loc.Get("home.knock_at_door"), "gray");
        terminal.WriteLine();

        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("home.cuck_enters", thirdName));
        terminal.WriteLine(Loc.Get("home.cuck_takes_hand", spouse.Name, thirdPossessive));
        terminal.WriteLine(Loc.Get("home.cuck_dont_worry", currentPlayer?.Sex == CharacterSex.Female ? "her" : "him", spouseGender));
        terminal.WriteLine(Loc.Get("home.cuck_wants_this", currentPlayer?.Sex == CharacterSex.Female ? "She" : "He"));
        terminal.WriteLine();

        await Task.Delay(2000);

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("home.cuck_intensity", spouse.Name, spousePossessive));
        terminal.WriteLine(Loc.Get("home.cuck_sit_there", spouseGender));
        terminal.WriteLine(Loc.Get("home.cuck_and_watch"));
        terminal.WriteLine();

        await Task.Delay(2000);

        WriteSectionHeader(Loc.Get("home.take_your_place"), "bright_magenta");
        terminal.WriteLine();

        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("home.cuck_move_toward", spouse.Name, thirdName));
        terminal.WriteLine(Loc.Get("home.cuck_first_kiss"));
        terminal.WriteLine(Loc.Get("home.cuck_watch_chair"));
        terminal.WriteLine();

        await Task.Delay(2000);

        terminal.SetColor("bright_magenta");
        terminal.WriteLine(Loc.Get("home.cuck_glances", spouse.Name));
        terminal.WriteLine(Loc.Get("home.cuck_power_gaze", spousePossessive));
        terminal.WriteLine(Loc.Get("home.cuck_owns_it", spouseGender));
        terminal.WriteLine();

        await Task.Delay(2500);

        // The scene progresses
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("home.cuck_clothing_falls"));
        terminal.WriteLine(Loc.Get("home.cuck_in_control", spouse.Name));
        terminal.WriteLine(Loc.Get("home.cuck_looks_at_you", spouseGender, spouseGender));
        terminal.WriteLine(Loc.Get("home.cuck_both_intense"));
        terminal.WriteLine();

        await Task.Delay(2500);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("home.cuck_sounds"));
        terminal.WriteLine(Loc.Get("home.cuck_told_you", spouse.Name));
        terminal.WriteLine(Loc.Get("home.cuck_watching"));
        terminal.WriteLine();

        await Task.Delay(2000);

        WriteSectionHeader(Loc.Get("home.later"), "bright_magenta");
        terminal.WriteLine();

        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("home.cuck_gathers_leaves", thirdName, thirdPossessive));
        terminal.WriteLine(Loc.Get("home.cuck_nod_out"));
        terminal.WriteLine();

        await Task.Delay(1500);

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("home.cuck_lies_back", spouse.Name));
        terminal.WriteLine(Loc.Get("home.cuck_beckons", $"{spouseGender.ToUpperInvariant()[0]}{spouseGender.Substring(1)}"));
        terminal.WriteLine(Loc.Get("home.cuck_approach"));
        terminal.WriteLine();

        await Task.Delay(2000);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("home.cuck_dynamic_shifted"));
        terminal.WriteLine(Loc.Get("home.cuck_discovered", spouse.Name, spousePossessive));
        terminal.WriteLine(Loc.Get("home.cuck_gave_power"));
        terminal.WriteLine();

        await Task.Delay(1500);

        // Record the encounter
        RomanceTracker.Instance.SetupCuckoldArrangement(spouse.ID, thirdParty.ID, true);

        // This is a complex dynamic - relationship may strengthen or become more complicated
        spouseData.LoveLevel = Math.Max(1, spouseData.LoveLevel - 1);

        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("home.cuck_new_chapter"));
        terminal.WriteLine();

        await terminal.GetInput(Loc.Get("ui.press_enter"));
    }

    private async Task DiscussStagVixen(NPC spouse, Spouse spouseData)
    {
        terminal.WriteLine();
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("home.stag_line1"));
        terminal.WriteLine(Loc.Get("home.stag_line2"));
        terminal.WriteLine(Loc.Get("home.stag_line3"));
        terminal.WriteLine(Loc.Get("home.stag_line4"));
        terminal.WriteLine();

        await Task.Delay(2000);

        var personality = spouse.Brain?.Personality;
        float adventurousness = personality?.Adventurousness ?? 0.5f;
        float exhibitionism = personality?.Exhibitionism ?? 0.3f;
        float sensuality = personality?.Sensuality ?? 0.5f;

        // Stag/Vixen appeals to adventurous, exhibitionist personalities
        bool interested = (adventurousness > 0.5f && exhibitionism > 0.4f) ||
                         (sensuality > 0.6f && adventurousness > 0.55f);

        terminal.SetColor("bright_cyan");
        if (interested)
        {
            terminal.WriteLine(Loc.Get("home.stag_breath", spouse.Name));
            terminal.WriteLine(Loc.Get("home.stag_show_off"));
            terminal.WriteLine(Loc.Get("home.stag_kind_hot"));
            terminal.WriteLine();

            await Task.Delay(1500);

            terminal.WriteLine(Loc.Get("home.stag_mischievous"));
            terminal.WriteLine(Loc.Get("home.stag_admired"));
            terminal.WriteLine(Loc.Get("home.stag_like_try"));
            terminal.WriteLine();

            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("home.stag_agrees", spouse.Name));

            spouseData.AcceptsPolyamory = true;
            spouseData.KnowsAboutOthers = true;
            RomanceTracker.Instance.AgreedStructures[spouse.ID] = RelationshipStructure.OpenRelationship;
        }
        else if (adventurousness > 0.4f)
        {
            terminal.WriteLine(Loc.Get("home.stag_considers", spouse.Name));
            terminal.WriteLine(Loc.Get("home.stag_flattering"));
            terminal.WriteLine(Loc.Get("home.stag_not_comfortable"));
            terminal.WriteLine();

            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("home.stag_not_ready"));

            // Light damage - they took it well
            await HandleSensitiveTopicRejection(spouse, spouseData, 2, 0.01f, "sharing");
        }
        else
        {
            terminal.WriteLine(Loc.Get("home.stag_shakes", spouse.Name));
            terminal.WriteLine(Loc.Get("home.stag_dont_want"));
            terminal.WriteLine(Loc.Get("home.stag_all_need"));
            terminal.WriteLine();

            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("home.stag_prefer_mono"));

            // Mild damage plus slight hurt feelings
            await HandleSensitiveTopicRejection(spouse, spouseData, 4, 0.03f, "sharing me with others");
        }
    }

    /// <summary>
    /// Handle relationship damage when a sensitive topic is rejected.
    /// Includes chance of spouse initiating divorce for severe rejections.
    /// </summary>
    /// <param name="spouse">The NPC spouse</param>
    /// <param name="spouseData">The spouse data from RomanceTracker</param>
    /// <param name="damageAmount">How much to increase LoveLevel (higher = worse)</param>
    /// <param name="divorceChance">Probability (0-1) of spouse initiating divorce</param>
    /// <param name="topicName">Name of the topic for dialogue</param>
    private async Task HandleSensitiveTopicRejection(NPC spouse, Spouse spouseData, int damageAmount, float divorceChance, string topicName)
    {
        // Apply relationship damage
        spouseData.LoveLevel = Math.Min(100, spouseData.LoveLevel + damageAmount);

        // Check if relationship is severely damaged (LoveLevel > 70 is bad)
        bool relationshipStrained = spouseData.LoveLevel > 60;

        // Roll for divorce chance (higher if relationship already strained)
        var random = Random.Shared;
        float effectiveDivorceChance = divorceChance;
        if (relationshipStrained)
        {
            effectiveDivorceChance *= 2.0f; // Double chance if already strained
        }

        // High jealousy spouses are more likely to divorce over these topics
        float jealousy = spouse.Brain?.Personality?.Jealousy ?? 0.5f;
        if (jealousy > 0.7f)
        {
            effectiveDivorceChance *= 1.5f;
        }

        bool spouseWantsDivorce = random.NextDouble() < effectiveDivorceChance;

        if (spouseWantsDivorce && spouseData.LoveLevel > 40)
        {
            terminal.WriteLine();
            await Task.Delay(2000);

            WriteSectionHeader(Loc.Get("home.terrible_silence"), "red");
            terminal.WriteLine();

            await Task.Delay(1500);

            string spouseGender = spouse.Sex == CharacterSex.Female ? "she" : "he";
            string spousePossessive = spouse.Sex == CharacterSex.Female ? "her" : "his";

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("home.reject_quiet", spouse.Name));
            terminal.WriteLine(Loc.Get("home.reject_cold_voice", spouseGender, spousePossessive));
            terminal.WriteLine();

            await Task.Delay(2000);

            terminal.SetColor("bright_red");
            terminal.WriteLine(Loc.Get("home.reject_trying"));
            terminal.WriteLine(Loc.Get("home.reject_asking_about", topicName));
            terminal.WriteLine(Loc.Get("home.reject_different_things"));
            terminal.WriteLine();

            await Task.Delay(2000);

            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("home.reject_want_divorce"));
            terminal.WriteLine();

            await Task.Delay(1500);

            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("home.reject_divorce_ask"));
            terminal.WriteLine();
            terminal.SetColor("bright_yellow");
            terminal.Write("  [A]");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("home.reject_accept"));
            terminal.SetColor("bright_yellow");
            terminal.Write("  [P]");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("home.reject_fight"));
            terminal.WriteLine();

            var input = await terminal.GetInput(Loc.Get("ui.choice"));

            if (input.Trim().ToUpperInvariant() == "P")
            {
                // Pleading - small chance to save marriage
                float pleadSuccess = 0.3f - (spouseData.LoveLevel / 300f); // Harder if relationship worse
                if (jealousy > 0.6f) pleadSuccess -= 0.1f;

                await Task.Delay(1000);

                terminal.SetColor("white");
                terminal.WriteLine();
                terminal.WriteLine(Loc.Get("home.reject_plead_sorry"));
                terminal.WriteLine(Loc.Get("home.reject_plead_love"));
                terminal.WriteLine();

                await Task.Delay(2000);

                if (random.NextDouble() < pleadSuccess)
                {
                    terminal.SetColor("bright_cyan");
                    terminal.WriteLine(Loc.Get("home.reject_softens", spouse.Name));
                    terminal.WriteLine(Loc.Get("home.reject_counseling"));
                    terminal.WriteLine(Loc.Get("home.reject_never_again"));
                    terminal.WriteLine();

                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("home.reject_saved"));
                    terminal.WriteLine(Loc.Get("home.reject_damage_heal"));

                    // Severe relationship damage but no divorce
                    spouseData.LoveLevel = Math.Min(100, spouseData.LoveLevel + 10);
                }
                else
                {
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("home.reject_shakes_head", spouse.Name, spousePossessive));
                    terminal.WriteLine(Loc.Get("home.reject_made_up_mind"));
                    terminal.WriteLine();

                    await ProcessSpouseDivorce(spouse, spouseData);
                }
            }
            else
            {
                // Accept divorce
                terminal.SetColor("gray");
                terminal.WriteLine();
                terminal.WriteLine(Loc.Get("home.reject_accept_silence"));
                terminal.WriteLine();

                await ProcessSpouseDivorce(spouse, spouseData);
            }
        }
        else if (relationshipStrained)
        {
            // Relationship is strained but no divorce... yet
            terminal.WriteLine();
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("home.reject_strained"));
            terminal.WriteLine(Loc.Get("home.reject_careful"));
        }
    }

    /// <summary>
    /// Process a spouse-initiated divorce
    /// </summary>
    private async Task ProcessSpouseDivorce(NPC spouse, Spouse spouseData)
    {
        WriteSectionHeader(Loc.Get("home.marriage_ended"), "red");
        terminal.WriteLine();

        await Task.Delay(1500);

        // Process divorce - try RelationshipSystem first, but don't fail if it doesn't have a record
        bool relationshipSystemSuccess = RelationshipSystem.ProcessDivorce(currentPlayer, spouse, out string message);

        // Always process the RomanceTracker divorce - this ensures the divorce happens
        // even if RelationshipSystem didn't track the marriage
        RomanceTracker.Instance.Divorce(spouse.ID, "Spouse left due to incompatible relationship views", playerInitiated: false);

        // Clear marriage flags on both characters regardless
        currentPlayer.Married = false;
        currentPlayer.IsMarried = false;
        currentPlayer.SpouseName = "";
        spouse.Married = false;
        spouse.IsMarried = false;
        spouse.SpouseName = "";

        if (spouseData.Children > 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("home.divorce_custody", spouse.Name));
            terminal.WriteLine();
        }

        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("home.divorce_packs", spouse.Name));
        terminal.WriteLine(Loc.Get("home.divorce_finality"));

        // Move spouse out of home
        spouse.UpdateLocation("Inn");

        // Generate news
        NewsSystem.Instance?.WriteDivorceNews(spouse.Name, currentPlayer.Name);

        await Task.Delay(1500);
    }

    private async Task VisitBedroom()
    {
        var romance = RomanceTracker.Instance;

        terminal.WriteLine("\n", "white");
        WriteSectionHeader(Loc.Get("home.master_bedroom"), "bright_magenta");
        terminal.WriteLine();

        if (romance.Spouses.Count == 0 && romance.CurrentLovers.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("home.bed_cold_empty"));
            terminal.WriteLine(Loc.Get("home.bed_find_someone"));
            await terminal.WaitForKey();
            return;
        }

        // Check if spouse is home (location can be "Home" or "Your Home")
        // Filter out dead NPCs - they can't participate in intimate scenes
        var availablePartners = new List<NPC>();

        foreach (var spouse in romance.Spouses)
        {
            var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == spouse.NPCId);
            if (npc != null && !npc.IsDead && (npc.CurrentLocation == "Home" || npc.CurrentLocation == "Your Home"))
            {
                availablePartners.Add(npc);
            }
        }

        foreach (var lover in romance.CurrentLovers)
        {
            var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == lover.NPCId);
            if (npc != null && !npc.IsDead && (npc.CurrentLocation == "Home" || npc.CurrentLocation == "Your Home"))
            {
                availablePartners.Add(npc);
            }
        }

        if (availablePartners.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("home.partner_not_home"));
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("home.partner_elsewhere"));
            await terminal.WaitForKey();
            return;
        }

        if (availablePartners.Count == 1)
        {
            var partner = availablePartners[0];
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("home.bedroom_here_inviting", partner.Name));
            terminal.WriteLine();
            terminal.SetColor("bright_yellow");
            terminal.Write("  [1]");
            terminal.SetColor("white");
            terminal.WriteLine($" {Loc.Get("home.join_in_bed", partner.Name)}");
            terminal.SetColor("bright_yellow");
            terminal.Write("  [0]");
            terminal.SetColor("white");
            terminal.WriteLine($" {Loc.Get("home.leave_bedroom")}");

            var input = await terminal.GetInput(Loc.Get("ui.choice"));
            if (input == "1")
            {
                await IntimacySystem.Instance.InitiateIntimateScene(currentPlayer, partner, terminal);
            }
            else
            {
                terminal.WriteLine(Loc.Get("home.leave_bedroom_msg"), "gray");
                await terminal.WaitForKey();
            }
        }
        else
        {
            // Multiple partners available
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("home.multiple_partners"));
            terminal.WriteLine();

            for (int i = 0; i < availablePartners.Count; i++)
            {
                terminal.SetColor("white");
                terminal.WriteLine($"  [{i + 1}] {availablePartners[i].Name}");
            }
            terminal.SetColor("bright_yellow");
            terminal.Write("  [0]");
            terminal.SetColor("gray");
            terminal.WriteLine($" {Loc.Get("home.leave_bedroom")}");

            var input = await terminal.GetInput(Loc.Get("ui.choice"));
            if (int.TryParse(input, out int choice) && choice >= 1 && choice <= availablePartners.Count)
            {
                await IntimacySystem.Instance.InitiateIntimateScene(currentPlayer, availablePartners[choice - 1], terminal);
            }
            else
            {
                terminal.WriteLine(Loc.Get("home.leave_bedroom_msg"), "gray");
                await terminal.WaitForKey();
            }
        }
    }

    #region Home Upgrade System - v0.44.0 Overhaul

    private static readonly string[] LivingQuartersNames = { "Dilapidated Shack", "Patched Walls", "Sturdy Cottage", "Comfortable Home", "Fine Manor", "Grand Estate" };
    private static readonly string[] BedNames = { "Moth-Eaten Straw Pile", "Simple Cot", "Wooden Bed Frame", "Feather Mattress", "Four-Poster Bed", "Royal Canopy Bed" };
    private static readonly string[] ChestNames = { "No Chest", "Wooden Crate", "Iron-Bound Chest", "Reinforced Vault", "Enchanted Vault", "Dimensional Vault" };
    private static readonly string[] HearthNames = { "Cold Firepit", "Simple Hearth", "Stone Fireplace", "Iron Stove", "Grand Fireplace", "Eternal Flame" };
    private static readonly string[] GardenNames = { "Bare Dirt", "Small Herb Patch", "Tended Garden", "Flourishing Garden", "Alchemist's Garden", "Enchanted Greenhouse" };

    private async Task ShowHomeUpgrades()
    {
        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("home.upgrades"), "bright_yellow", 62);
        terminal.WriteLine();

        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("home.your_gold", $"{currentPlayer.Gold:N0}"));
        terminal.WriteLine();

        // Tiered upgrades
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("home.upgrade_room_title"));
        int opt = 1;

        // Living Quarters
        int hlCur = Math.Clamp(currentPlayer.HomeLevel, 0, 5);
        int hlNext = Math.Clamp(currentPlayer.HomeLevel + 1, 0, 5);
        ShowTieredOption(opt++, "Living Quarters", LivingQuartersNames, currentPlayer.HomeLevel, 5, GetLivingQuartersCost(currentPlayer.HomeLevel),
            $"{(int)(GameConfig.HomeRecoveryPercent[hlCur] * 100)}% rest, {GameConfig.HomeRestsPerDay[hlCur]}x/day",
            $"{(int)(GameConfig.HomeRecoveryPercent[hlNext] * 100)}% rest, {GameConfig.HomeRestsPerDay[hlNext]}x/day");

        // Bed
        int blCur = Math.Clamp(currentPlayer.BedLevel, 0, 5);
        int blNext = Math.Clamp(currentPlayer.BedLevel + 1, 0, 5);
        string bedCurStr = blCur == 0 ? "-50% fertility" : (GameConfig.BedFertilityModifier[blCur] == 0f ? "No modifier" : $"+{(int)(GameConfig.BedFertilityModifier[blCur] * 100)}% fertility");
        string bedNextStr = GameConfig.BedFertilityModifier[blNext] == 0f ? "No penalty" : $"+{(int)(GameConfig.BedFertilityModifier[blNext] * 100)}% fertility";
        ShowTieredOption(opt++, "Bed", BedNames, currentPlayer.BedLevel, 5, GetBedCost(currentPlayer.BedLevel), bedCurStr, bedNextStr);

        // Storage Chest
        int clCur = Math.Clamp(currentPlayer.ChestLevel, 0, 5);
        int clNext = Math.Clamp(currentPlayer.ChestLevel + 1, 0, 5);
        ShowTieredOption(opt++, "Storage Chest", ChestNames, currentPlayer.ChestLevel, 5, GetChestUpgradeCost(currentPlayer.ChestLevel),
            $"{GameConfig.ChestCapacity[clCur]} items",
            $"{GameConfig.ChestCapacity[clNext]} items");

        // Hearth
        int heCur = Math.Clamp(currentPlayer.HearthLevel, 0, 5);
        int heNext = Math.Clamp(currentPlayer.HearthLevel + 1, 0, 5);
        string hearthCurStr = heCur == 0 ? "No buff" : $"+{(int)(GameConfig.HearthDamageBonus[heCur] * 100)}% dmg/def, {GameConfig.HearthCombatDuration[heCur]} combats";
        string hearthNextStr = $"+{(int)(GameConfig.HearthDamageBonus[heNext] * 100)}% dmg/def, {GameConfig.HearthCombatDuration[heNext]} combats";
        ShowTieredOption(opt++, "Hearth", HearthNames, currentPlayer.HearthLevel, 5, GetHearthCost(currentPlayer.HearthLevel), hearthCurStr, hearthNextStr);

        // Herb Garden
        int glCur = Math.Clamp(currentPlayer.GardenLevel, 0, 5);
        int glNext = Math.Clamp(currentPlayer.GardenLevel + 1, 0, 5);
        ShowTieredOption(opt++, "Herb Garden", GardenNames, currentPlayer.GardenLevel, 5, GetGardenCost(currentPlayer.GardenLevel),
            $"{GameConfig.HerbsPerDay[glCur]} herbs/day",
            $"{GameConfig.HerbsPerDay[glNext]} herbs/day");

        // Training Room
        int trCur = currentPlayer.TrainingRoomLevel;
        int trNext = Math.Min(trCur + 1, 10);
        ShowTieredOption(opt++, "Training Room", null, trCur, 10, GetTrainingRoomCost(trCur),
            $"+{trCur} all stats",
            $"+{trNext} all stats");

        terminal.WriteLine();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("home.upgrade_special_title"));

        // Trophy Room
        long trophyRoomCost = 500_000;
        ShowOneTimePurchase(opt++, "Trophy Room", currentPlayer.HasTrophyRoom, trophyRoomCost, "Display achievements & bosses");
        // Study / Library
        long studyCost = 750_000;
        ShowOneTimePurchase(opt++, "Study / Library", currentPlayer.HasStudy, studyCost, "+5% XP from combat");
        // Servants' Quarters
        long servantsCost = 500_000;
        ShowOneTimePurchase(opt++, "Servants' Quarters", currentPlayer.HasServants, servantsCost, $"Daily gold income ({GameConfig.ServantsDailyGoldBase}+lvl*{GameConfig.ServantsDailyGoldPerLevel})");
        // Reinforced Door
        long reinforcedDoorCost = GameConfig.ReinforcedDoorCost;
        ShowOneTimePurchase(opt++, "Reinforced Door", currentPlayer.HasReinforcedDoor, reinforcedDoorCost, "Sleep safely at home in online mode");
        // Legendary Armory
        long armoryCost = 2_500_000;
        ShowOneTimePurchase(opt++, "Legendary Armory", currentPlayer.HasLegendaryArmory, armoryCost, "+5% damage & defense permanently");
        // Fountain of Vitality
        long fountainCost = 5_000_000;
        ShowOneTimePurchase(opt++, "Fountain of Vitality", currentPlayer.HasVitalityFountain, fountainCost, "+10% max HP permanently");

        terminal.WriteLine();
        if (IsScreenReader)
        {
            terminal.WriteLine($"0. {Loc.Get("home.upgrade_return")}");
        }
        else
        {
            terminal.SetColor("bright_yellow");
            terminal.Write("[0]");
            terminal.SetColor("white");
            terminal.WriteLine($" {Loc.Get("home.upgrade_return")}");
        }
        terminal.WriteLine();

        var input = await terminal.GetInput(Loc.Get("home.select_upgrade"));
        if (!int.TryParse(input, out int choice) || choice < 1)
            return;

        switch (choice)
        {
            case 1:
                await PurchaseUpgrade("Living Quarters", GetLivingQuartersCost(currentPlayer.HomeLevel),
                    currentPlayer.HomeLevel < 5, () => {
                        currentPlayer.HomeLevel++;
                        int lvl = Math.Clamp(currentPlayer.HomeLevel, 0, 5);
                        terminal.SetColor("cyan");
                        terminal.WriteLine(Loc.Get("home.upgraded_to", LivingQuartersNames[lvl]));
                        terminal.WriteLine(Loc.Get("home.upgrade_rest_stats", (int)(GameConfig.HomeRecoveryPercent[lvl] * 100), GameConfig.HomeRestsPerDay[lvl]));
                    });
                break;
            case 2:
                await PurchaseUpgrade("Bed", GetBedCost(currentPlayer.BedLevel),
                    currentPlayer.BedLevel < 5, () => {
                        currentPlayer.BedLevel++;
                        int lvl = Math.Clamp(currentPlayer.BedLevel, 0, 5);
                        terminal.SetColor("cyan");
                        terminal.WriteLine(Loc.Get("home.upgraded_to", BedNames[lvl]));
                        float mod = GameConfig.BedFertilityModifier[lvl];
                        terminal.WriteLine(mod <= 0 ? Loc.Get("home.fertility_removed") : Loc.Get("home.fertility_bonus", (int)(mod * 100)));
                    });
                break;
            case 3:
                await PurchaseUpgrade("Storage Chest", GetChestUpgradeCost(currentPlayer.ChestLevel),
                    currentPlayer.ChestLevel < 5, () => {
                        currentPlayer.ChestLevel++;
                        int lvl = Math.Clamp(currentPlayer.ChestLevel, 0, 5);
                        terminal.SetColor("cyan");
                        terminal.WriteLine(Loc.Get("home.upgraded_to", ChestNames[lvl]));
                        terminal.WriteLine(Loc.Get("home.upgrade_chest_holds", GameConfig.ChestCapacity[lvl]));
                    });
                break;
            case 4:
                await PurchaseUpgrade("Hearth", GetHearthCost(currentPlayer.HearthLevel),
                    currentPlayer.HearthLevel < 5, () => {
                        currentPlayer.HearthLevel++;
                        int lvl = Math.Clamp(currentPlayer.HearthLevel, 0, 5);
                        terminal.SetColor("cyan");
                        terminal.WriteLine(Loc.Get("home.upgraded_to", HearthNames[lvl]));
                        terminal.WriteLine(Loc.Get("home.upgrade_hearth_buff", (int)(GameConfig.HearthDamageBonus[lvl] * 100), GameConfig.HearthCombatDuration[lvl]));
                    });
                break;
            case 5:
                await PurchaseUpgrade("Herb Garden", GetGardenCost(currentPlayer.GardenLevel),
                    currentPlayer.GardenLevel < 5, () => {
                        currentPlayer.GardenLevel++;
                        int lvl = Math.Clamp(currentPlayer.GardenLevel, 0, 5);
                        terminal.SetColor("cyan");
                        terminal.WriteLine(Loc.Get("home.upgraded_to", GardenNames[lvl]));
                        terminal.WriteLine(Loc.Get("home.upgrade_herbs_day", GameConfig.HerbsPerDay[lvl]));
                        if (lvl >= 1 && lvl <= 5)
                        {
                            var newHerb = (HerbType)lvl;
                            terminal.SetColor(HerbData.GetColor(newHerb));
                            terminal.WriteLine(Loc.Get("home.upgrade_new_herb", HerbData.GetName(newHerb), HerbData.GetDescription(newHerb)));
                        }
                    });
                break;
            case 6:
                await PurchaseUpgrade("Training Room", GetTrainingRoomCost(currentPlayer.TrainingRoomLevel),
                    currentPlayer.TrainingRoomLevel < 10, () => { currentPlayer.TrainingRoomLevel++; ApplyTrainingBonus(); });
                break;
            case 7:
                await PurchaseUpgrade("Trophy Room", trophyRoomCost,
                    !currentPlayer.HasTrophyRoom, () => { currentPlayer.HasTrophyRoom = true; });
                break;
            case 8:
                await PurchaseUpgrade("Study / Library", studyCost,
                    !currentPlayer.HasStudy, () => {
                        currentPlayer.HasStudy = true;
                        terminal.SetColor("cyan");
                        terminal.WriteLine(Loc.Get("home.study_desc"));
                        terminal.WriteLine(Loc.Get("home.study_bonus", (int)(GameConfig.StudyXPBonus * 100)));
                    });
                break;
            case 9:
                await PurchaseUpgrade("Servants' Quarters", servantsCost,
                    !currentPlayer.HasServants, () => {
                        currentPlayer.HasServants = true;
                        terminal.SetColor("cyan");
                        terminal.WriteLine(Loc.Get("home.servants_desc"));
                        terminal.WriteLine(Loc.Get("home.upgrade_servants_collect", GameConfig.ServantsDailyGoldBase, GameConfig.ServantsDailyGoldPerLevel));
                    });
                break;
            case 10:
                await PurchaseUpgrade("Reinforced Door", reinforcedDoorCost,
                    !currentPlayer.HasReinforcedDoor, () => {
                        currentPlayer.HasReinforcedDoor = true;
                        terminal.SetColor("cyan");
                        terminal.WriteLine(Loc.Get("home.reinforced_door_desc"));
                        terminal.WriteLine(Loc.Get("home.reinforced_door_safe"));
                    });
                break;
            case 11:
                await PurchaseUpgrade("Legendary Armory", armoryCost,
                    !currentPlayer.HasLegendaryArmory, () => { currentPlayer.HasLegendaryArmory = true; ApplyArmoryBonus(); });
                break;
            case 12:
                await PurchaseUpgrade("Fountain of Vitality", fountainCost,
                    !currentPlayer.HasVitalityFountain, () => { currentPlayer.HasVitalityFountain = true; ApplyFountainBonus(); });
                break;
        }
    }

    private void ShowTieredOption(int num, string name, string[]? tierNames, int level, int maxLevel, long cost, string currentBonus, string nextBonus)
    {
        bool maxed = level >= maxLevel;
        bool affordable = currentPlayer.Gold >= cost;
        string currentTierName = tierNames != null && level < tierNames.Length ? tierNames[level] : "";
        string nextTierName = tierNames != null && level + 1 < tierNames.Length ? tierNames[level + 1] : "";

        if (IsScreenReader)
        {
            if (maxed)
                terminal.WriteLine(Loc.Get("home.upgrade_sr_maxed", num, name, currentTierName, level, currentBonus));
            else
            {
                string tierText = nextTierName != "" ? $": {nextTierName}" : "";
                terminal.WriteLine(Loc.Get("home.upgrade_sr_next", num, name, level + 1, tierText, $"{cost:N0}", nextBonus));
            }
            return;
        }

        if (maxed)
        {
            terminal.SetColor("bright_green");
            terminal.Write($"  [{num}] {name}");
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("home.upgrade_maxed_label", currentTierName, level, currentBonus));
        }
        else
        {
            terminal.SetColor(affordable ? "bright_yellow" : "dark_gray");
            terminal.Write($"  [{num}]");
            terminal.SetColor(affordable ? "white" : "dark_gray");
            string tierText = nextTierName != "" ? $": {nextTierName}" : "";
            terminal.Write($" {name} Lv {level + 1}{tierText}");
            terminal.SetColor(affordable ? "yellow" : "dark_gray");
            terminal.WriteLine($"  {cost:N0}g  [{nextBonus}]");
        }
    }

    private void ShowOneTimePurchase(int num, string name, bool owned, long cost, string desc)
    {
        if (IsScreenReader)
        {
            if (owned)
                terminal.WriteLine(Loc.Get("home.upgrade_otp_sr_owned", num, name));
            else
                terminal.WriteLine(Loc.Get("home.upgrade_otp_sr_buy", num, name, $"{cost:N0}", desc));
            return;
        }

        if (owned)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("home.upgrade_otp_owned", num, name));
        }
        else
        {
            bool affordable = currentPlayer.Gold >= cost;
            terminal.SetColor(affordable ? "white" : "dark_gray");
            terminal.Write($"  [{num}] {name}");
            terminal.SetColor(affordable ? "yellow" : "dark_gray");
            terminal.WriteLine($"  {cost:N0}g - {desc}");
        }
    }

    private long GetLivingQuartersCost(int level) => level switch
    {
        0 => 25_000, 1 => 75_000, 2 => 200_000, 3 => 500_000, 4 => 1_500_000, _ => long.MaxValue
    };

    private long GetBedCost(int level) => level switch
    {
        0 => 10_000, 1 => 50_000, 2 => 150_000, 3 => 400_000, 4 => 1_000_000, _ => long.MaxValue
    };

    private long GetChestUpgradeCost(int level) => level switch
    {
        0 => 15_000, 1 => 60_000, 2 => 200_000, 3 => 500_000, 4 => 1_200_000, _ => long.MaxValue
    };

    private long GetHearthCost(int level) => level switch
    {
        0 => 20_000, 1 => 80_000, 2 => 250_000, 3 => 750_000, 4 => 1_500_000, _ => long.MaxValue
    };

    private long GetGardenCost(int level) => level switch
    {
        0 => 30_000, 1 => 100_000, 2 => 300_000, 3 => 800_000, 4 => 2_000_000, _ => long.MaxValue
    };

    private long GetTrainingRoomCost(int level) => level switch
    {
        0 => 100_000, 1 => 200_000, 2 => 350_000, 3 => 550_000, 4 => 800_000,
        5 => 1_100_000, 6 => 1_500_000, 7 => 2_000_000, 8 => 2_700_000, 9 => 3_500_000,
        _ => long.MaxValue
    };

    private async Task PurchaseUpgrade(string name, long cost, bool available, Action applyUpgrade)
    {
        if (!available)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("home.upgrade_max_level", name));
            await terminal.WaitForKey();
            return;
        }

        if (currentPlayer.Gold < cost)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("home.upgrade_need_gold", $"{cost:N0}", name));
            terminal.WriteLine(Loc.Get("home.upgrade_only_have", $"{currentPlayer.Gold:N0}"));
            await terminal.WaitForKey();
            return;
        }

        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("home.upgrade_confirm", name, $"{cost:N0}"));
        var confirm = await terminal.GetInput("(Y/N): ");

        if (confirm.Trim().ToUpperInvariant() == "Y")
        {
            currentPlayer.Gold -= cost;
            currentPlayer.Statistics.RecordGoldSpent(cost);
            applyUpgrade();
            currentPlayer.RecalculateStats();

            terminal.SetColor("bright_green");
            terminal.WriteLine($"\n{Loc.Get("home.upgrade_success", name.ToUpper())}");
            terminal.WriteLine(Loc.Get("home.upgrade_craftsmen"));
            await Task.Delay(1500);
            terminal.WriteLine(Loc.Get("home.upgrade_home_done"));

            // Save immediately after upgrade to prevent data loss on disconnect
            _ = GameEngine.Instance.SaveCurrentGame();
        }
        else
        {
            terminal.WriteLine(Loc.Get("home.upgrade_cancelled"), "gray");
        }
        await terminal.WaitForKey();
    }

    private void ApplyTrainingBonus()
    {
        currentPlayer.BaseStrength++;
        currentPlayer.BaseDexterity++;
        currentPlayer.BaseConstitution++;
        currentPlayer.BaseIntelligence++;
        currentPlayer.BaseWisdom++;
        currentPlayer.BaseCharisma++;

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("home.training_room_upgrade", currentPlayer.TrainingRoomLevel));
        terminal.WriteLine(Loc.Get("home.training_room_bonus"));
    }

    private void ApplyArmoryBonus()
    {
        currentPlayer.PermanentDamageBonus += 5;
        currentPlayer.PermanentDefenseBonus += 5;
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("home.armory_installed"));
        terminal.WriteLine(Loc.Get("home.armory_bonus"));
    }

    private void ApplyFountainBonus()
    {
        long hpBonus = currentPlayer.MaxHP / 10;
        currentPlayer.BonusMaxHP += hpBonus;
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("home.fountain_installed"));
        terminal.WriteLine(Loc.Get("home.fountain_hp_bonus", hpBonus));
    }

    /// <summary>
    /// Resurrect a dead teammate
    /// </summary>
    private async Task ResurrectAlly()
    {
        var romance = RomanceTracker.Instance;
        
        if (romance.Spouses.Count == 0 && romance.CurrentLovers.Count == 0)
        {
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("home.no_lovers"));
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        // Find dead allies (check IsDead flag for permanent death, not IsAlive which is just HP > 0)
        List<NPC> deadMembers = new List<NPC>();

        var allWorldNPCs = NPCSpawnSystem.Instance?.ActiveNPCs;

        if (allWorldNPCs != null)
        {
            foreach (var spouse in romance.Spouses)
            {
                var npc = allWorldNPCs.FirstOrDefault(n => n.ID == spouse.NPCId);

                // Check IsDead (permanent death) OR !IsAlive (currently at 0 HP)
                if (npc != null && (npc.IsDead || !npc.IsAlive) && !npc.IsAgedDeath && !npc.IsPermaDead)
                {
                    deadMembers.Add(npc);
                }
            }

            foreach (var lover in romance.CurrentLovers)
            {
                var npc = allWorldNPCs.FirstOrDefault(n => n.ID == lover.NPCId);

                // Check IsDead (permanent death) OR !IsAlive (currently at 0 HP)
                if (npc != null && (npc.IsDead || !npc.IsAlive) && !deadMembers.Contains(npc))
                {
                    deadMembers.Add(npc);
                }
            }
        }

        if (deadMembers.Count == 0)
        {
            terminal.WriteLine("");
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("home.all_alive"));
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("home.dead_team_members"));
        for (int i = 0; i < deadMembers.Count; i++)
        {
            var dead = deadMembers[i];
            long cost = dead.Level * 1000; // Resurrection cost
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("home.resurrect_entry", i + 1, dead.DisplayName, dead.Level, $"{cost:N0}"));
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("home.resurrect_select"));
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (int.TryParse(input, out int choice) && choice >= 1 && choice <= deadMembers.Count)
        {
            var toResurrect = deadMembers[choice - 1];
            long cost = toResurrect.Level * 1000;

            if (currentPlayer.Gold < cost)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("home.resurrect_need_gold", $"{cost:N0}", toResurrect.DisplayName));
            }
            else
            {
                currentPlayer.Gold -= cost;
                toResurrect.HP = toResurrect.MaxHP / 2; // Resurrect at half HP
toResurrect.IsDead = false;
                terminal.WriteLine("");
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("home.resurrect_success", toResurrect.DisplayName));
                terminal.WriteLine(Loc.Get("home.resurrect_cost", $"{cost:N0}"));

                NewsSystem.Instance.Newsy(true, $"{toResurrect.DisplayName} was resurrected by their ally '{currentPlayer.Name}'!");
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
        await terminal.ReadKeyAsync();
    }

    #endregion

    #region Partner Equipment Management

    /// <summary>
    /// Equip a spouse or lover with items from your inventory
    /// </summary>
    private async Task EquipPartner()
    {
        var romance = RomanceTracker.Instance;
        var partners = new List<(NPC npc, string relationship)>();

        // Get all spouses
        foreach (var spouse in romance.Spouses)
        {
            var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == spouse.NPCId);
            if (npc != null && npc.IsAlive)
            {
                partners.Add((npc, "Spouse"));
            }
        }

        // Get all lovers
        foreach (var lover in romance.CurrentLovers)
        {
            var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == lover.NPCId);
            if (npc != null && npc.IsAlive)
            {
                partners.Add((npc, "Lover"));
            }
        }

        if (partners.Count == 0)
        {
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("home.no_spouse_equip"));
            terminal.WriteLine(Loc.Get("home.no_spouse_find_love"));
            await Task.Delay(2500);
            return;
        }

        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("home.equip_partner"), "bright_magenta");
        terminal.WriteLine("");

        // List partners
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("home.your_partners"));
        terminal.WriteLine("");

        for (int i = 0; i < partners.Count; i++)
        {
            var (npc, relationship) = partners[i];
            terminal.SetColor("bright_yellow");
            terminal.Write($"  {i + 1}. ");
            terminal.SetColor("bright_magenta");
            terminal.Write($"{npc.DisplayName} ");
            terminal.SetColor("gray");
            terminal.Write($"({relationship}) ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("home.equip_npc_level", npc.Level, npc.Class));
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("home.select_partner"));
        terminal.SetColor("white");

        var input = await terminal.ReadLineAsync();
        if (!int.TryParse(input, out int partnerIdx) || partnerIdx < 1 || partnerIdx > partners.Count)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("ui.cancelled"));
            await Task.Delay(1000);
            return;
        }

        var selectedPartner = partners[partnerIdx - 1].npc;
        await ManageCharacterEquipment(selectedPartner);

        // Sync equipment changes to canonical NPC in ActiveNPCs (handles orphaned references)
        CombatEngine.SyncNPCTeammateToActiveNPCs(selectedPartner);

        // Auto-save after equipment changes to persist NPC equipment state
        await SaveSystem.Instance.AutoSave(currentPlayer);

        // Force NPC world_state save so equipment survives world-sim reload cycles
        if (DoorMode.IsOnlineMode && OnlineStateManager.Instance != null)
        {
            _ = Task.Run(async () =>
            {
                try { await OnlineStateManager.Instance.SaveAllSharedState(); }
                catch { /* best-effort */ }
            });
        }
    }

    /// <summary>
    /// Manage equipment for a specific character (spouse or lover)
    /// </summary>
    private async Task ManageCharacterEquipment(Character target)
    {
        while (true)
        {
            terminal.ClearScreen();
            WriteSectionHeader(Loc.Get("home.equip_header", target.DisplayName.ToUpper()), "bright_magenta");
            terminal.WriteLine("");

            // Show target's stats
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("home.equip_stats_level", target.Level, target.Class, target.Race));
            terminal.WriteLine(Loc.Get("home.equip_stats_hp", target.HP, target.MaxHP, target.Mana, target.MaxMana));
            terminal.WriteLine(Loc.Get("home.equip_stats_str", target.Strength, target.Dexterity, target.Agility, target.Constitution));
            terminal.WriteLine(Loc.Get("home.equip_stats_int", target.Intelligence, target.Wisdom, target.Charisma, target.Defence));
            terminal.WriteLine("");

            // Show current equipment
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("home.current_equipment"));
            terminal.SetColor("white");

            DisplayEquipmentSlot(target, EquipmentSlot.MainHand, "Main Hand");
            DisplayEquipmentSlot(target, EquipmentSlot.OffHand, "Off Hand");
            DisplayEquipmentSlot(target, EquipmentSlot.Head, "Head");
            DisplayEquipmentSlot(target, EquipmentSlot.Body, "Body");
            DisplayEquipmentSlot(target, EquipmentSlot.Arms, "Arms");
            DisplayEquipmentSlot(target, EquipmentSlot.Hands, "Hands");
            DisplayEquipmentSlot(target, EquipmentSlot.Legs, "Legs");
            DisplayEquipmentSlot(target, EquipmentSlot.Feet, "Feet");
            DisplayEquipmentSlot(target, EquipmentSlot.Cloak, "Cloak");
            DisplayEquipmentSlot(target, EquipmentSlot.Neck, "Neck");
            DisplayEquipmentSlot(target, EquipmentSlot.LFinger, "Left Ring");
            DisplayEquipmentSlot(target, EquipmentSlot.RFinger, "Right Ring");
            terminal.WriteLine("");

            // Show options
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("home.options"));
            if (IsScreenReader)
            {
                terminal.WriteLine($"  E. {Loc.Get("home.equip_from_inventory")}");
                terminal.WriteLine($"  U. {Loc.Get("home.unequip_item")}");
                terminal.WriteLine($"  T. {Loc.Get("home.take_all_equipment")}");
                terminal.WriteLine($"  Q. {Loc.Get("home.done_return")}");
            }
            else
            {
                terminal.SetColor("bright_yellow");
                terminal.Write("  [E]");
                terminal.SetColor("white");
                terminal.WriteLine($" {Loc.Get("home.equip_from_inventory")}");
                terminal.SetColor("bright_yellow");
                terminal.Write("  [U]");
                terminal.SetColor("white");
                terminal.WriteLine($" {Loc.Get("home.unequip_item")}");
                terminal.SetColor("bright_yellow");
                terminal.Write("  [T]");
                terminal.SetColor("white");
                terminal.WriteLine($" {Loc.Get("home.take_all_equipment")}");
                terminal.SetColor("bright_yellow");
                terminal.Write("  [Q]");
                terminal.SetColor("white");
                terminal.WriteLine($" {Loc.Get("home.done_return")}");
            }
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.Write(Loc.Get("ui.choice"));
            terminal.SetColor("white");

            var choice = (await terminal.ReadLineAsync()).ToUpper().Trim();

            switch (choice)
            {
                case "E":
                    await EquipItemToCharacter(target);
                    break;
                case "U":
                    await UnequipItemFromCharacter(target);
                    break;
                case "T":
                    await TakeAllEquipment(target);
                    break;
                case "Q":
                case "":
                    return;
            }
        }
    }

    /// <summary>
    /// Display an equipment slot with its current item
    /// </summary>
    private void DisplayEquipmentSlot(Character target, EquipmentSlot slot, string label)
    {
        DisplayEquipmentSlotWithStats(target, slot, label);
    }

    /// <summary>
    /// Equip an item from the player's inventory to a character (slot-based flow)
    /// </summary>
    private async Task EquipItemToCharacter(Character target)
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("home.equip_to_header", target.DisplayName.ToUpper()), "bright_magenta");
        terminal.WriteLine("");

        // Step 1: Pick a slot
        var selectedSlot = await PromptForEquipmentSlot(target);
        if (selectedSlot == null) return;

        // Step 2: Get items that match this slot
        var equipmentItems = GetItemsForSlot(selectedSlot.Value);

        if (equipmentItems.Count == 0)
        {
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine("  No items available for this slot.");
            await Task.Delay(2000);
            return;
        }

        // Step 3: Show current item in slot
        terminal.WriteLine("");
        var currentItem = target.GetEquipment(selectedSlot.Value);
        terminal.SetColor("white");
        terminal.Write($"  Current: ");
        if (currentItem != null)
        {
            terminal.SetColor(currentItem.IsIdentified ? currentItem.GetRarityColor() : "magenta");
            terminal.Write(currentItem.IsIdentified ? currentItem.Name : "Unidentified");
            if (currentItem.IsIdentified) WriteEquipmentStatSummary(currentItem);
            terminal.WriteLine("");
        }
        else
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("home.equip_empty"));
        }
        terminal.WriteLine("");

        // Step 4: Display matching items with full stats
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("home.available_equipment"));
        terminal.WriteLine("");
        DisplayEquipmentItemList(equipmentItems, target);

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("home.select_item_cancel"));
        terminal.SetColor("white");

        var input = await terminal.ReadLineAsync();
        if (!int.TryParse(input, out int itemIdx) || itemIdx < 1 || itemIdx > equipmentItems.Count)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("ui.cancelled"));
            await Task.Delay(1000);
            return;
        }

        var (selectedItem, wasEquipped, sourceSlot) = equipmentItems[itemIdx - 1];

        // Block unidentified items
        if (!selectedItem.IsIdentified)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("  Must identify the item first.");
            await Task.Delay(2000);
            return;
        }

        // Check if target can equip
        if (!selectedItem.CanEquip(target, out string equipReason))
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("home.cannot_use_item", target.DisplayName, equipReason));
            await Task.Delay(2000);
            return;
        }

        // Use the slot the player already picked (no need to ask which hand)
        EquipmentSlot? targetSlot = selectedSlot.Value;

        // Remove from player
        if (wasEquipped && sourceSlot.HasValue)
        {
            currentPlayer.UnequipSlot(sourceSlot.Value);
            currentPlayer.RecalculateStats();
        }
        else
        {
            // Remove from inventory (find by name)
            var invItem = currentPlayer.Inventory.FirstOrDefault(i => i.Name == selectedItem.Name);
            if (invItem != null)
            {
                currentPlayer.Inventory.Remove(invItem);
            }
        }

        // Track items in target's inventory BEFORE equipping, so we can move displaced items to player
        var targetInventoryBefore = target.Inventory.Count;

        // Equip to target - EquipItem adds displaced items to target's inventory
        var result = target.EquipItem(selectedItem, targetSlot, out string message);
        target.RecalculateStats();

        if (result)
        {
            // Move any items that were added to target's inventory (displaced equipment) to player's inventory
            if (target.Inventory.Count > targetInventoryBefore)
            {
                var displacedItems = target.Inventory.Skip(targetInventoryBefore).ToList();
                foreach (var displaced in displacedItems)
                {
                    target.Inventory.Remove(displaced);
                    currentPlayer.Inventory.Add(displaced);
                }
            }

            terminal.WriteLine("");
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("home.equipped_item", target.DisplayName, selectedItem.Name));
            if (!string.IsNullOrEmpty(message))
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(message);
            }
        }
        else
        {
            // Failed - return item to player
            var legacyItem = ConvertEquipmentToItem(selectedItem);
            currentPlayer.Inventory.Add(legacyItem);
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("home.equip_failed", message));
        }

        await Task.Delay(2000);
    }

    /// <summary>
    /// Unequip an item from a character and add to player's inventory
    /// </summary>
    private async Task UnequipItemFromCharacter(Character target)
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("home.unequip_header", target.DisplayName.ToUpper()), "bright_magenta");
        terminal.WriteLine("");

        // Get all equipped slots
        var equippedSlots = new List<(EquipmentSlot slot, Equipment item)>();
        foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
        {
            if (slot == EquipmentSlot.None) continue;
            var item = target.GetEquipment(slot);
            if (item != null)
            {
                equippedSlots.Add((slot, item));
            }
        }

        if (equippedSlots.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("home.equip_no_equipment", target.DisplayName));
            await Task.Delay(2000);
            return;
        }

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("home.equipped_items"));
        terminal.WriteLine("");

        for (int i = 0; i < equippedSlots.Count; i++)
        {
            var (slot, item) = equippedSlots[i];
            terminal.SetColor("bright_yellow");
            terminal.Write($"  {i + 1}. ");
            terminal.SetColor("gray");
            terminal.Write($"[{slot.GetDisplayName(),-12}] ");
            terminal.SetColor("white");
            terminal.Write($"{item.Name}");
            if (item.IsCursed)
            {
                terminal.SetColor("red");
                terminal.Write(" (CURSED)");
            }
            terminal.WriteLine("");
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("home.select_unequip"));
        terminal.SetColor("white");

        var input = await terminal.ReadLineAsync();
        if (!int.TryParse(input, out int slotIdx) || slotIdx < 1 || slotIdx > equippedSlots.Count)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("ui.cancelled"));
            await Task.Delay(1000);
            return;
        }

        var (selectedSlot, selectedItem) = equippedSlots[slotIdx - 1];

        // Check if cursed
        if (selectedItem.IsCursed)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("home.cursed_no_remove", selectedItem.Name));
            await Task.Delay(2000);
            return;
        }

        // Unequip and add to player inventory
        var unequipped = target.UnequipSlot(selectedSlot);
        if (unequipped != null)
        {
            target.RecalculateStats();
            var legacyItem = ConvertEquipmentToItem(unequipped);
            currentPlayer.Inventory.Add(legacyItem);

            terminal.WriteLine("");
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("home.took_item", unequipped.Name, target.DisplayName));
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("home.item_to_inventory"));
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("home.unequip_failed"));
        }

        await Task.Delay(2000);
    }

    /// <summary>
    /// Take all equipment from a character
    /// </summary>
    private async Task TakeAllEquipment(Character target)
    {
        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("home.take_all_confirm", target.DisplayName));
        terminal.Write(Loc.Get("home.take_all_warning"));
        terminal.SetColor("white");

        var confirm = await terminal.ReadLineAsync();
        if (!confirm.ToUpper().StartsWith("Y"))
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("ui.cancelled"));
            await Task.Delay(1000);
            return;
        }

        int itemsTaken = 0;
        var cursedItems = new List<string>();

        foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
        {
            if (slot == EquipmentSlot.None) continue;
            var item = target.GetEquipment(slot);
            if (item != null)
            {
                if (item.IsCursed)
                {
                    cursedItems.Add(item.Name);
                    continue;
                }

                var unequipped = target.UnequipSlot(slot);
                if (unequipped != null)
                {
                    var legacyItem = ConvertEquipmentToItem(unequipped);
                    currentPlayer.Inventory.Add(legacyItem);
                    itemsTaken++;
                }
            }
        }

        target.RecalculateStats();

        terminal.WriteLine("");
        if (itemsTaken > 0)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("home.took_items", itemsTaken, target.DisplayName));
        }
        else
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("home.no_equipment_take", target.DisplayName));
        }

        if (cursedItems.Count > 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("home.cursed_not_removed", string.Join(", ", cursedItems)));
        }

        await Task.Delay(2000);
    }

    /// <summary>
    /// Convert Equipment to legacy Item for inventory storage
    /// </summary>
    private ModelItem ConvertEquipmentToItem(Equipment equipment)
    {
        // Delegate to the canonical implementation that preserves LootEffects
        // (INT, CON, enchantments, proc effects)
        return currentPlayer.ConvertEquipmentToLegacyItem(equipment);
    }

    /// <summary>
    /// Convert EquipmentSlot to ObjType for legacy item system
    /// </summary>
    private ObjType SlotToObjType(EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.Head => ObjType.Head,
        EquipmentSlot.Body => ObjType.Body,
        EquipmentSlot.Arms => ObjType.Arms,
        EquipmentSlot.Hands => ObjType.Hands,
        EquipmentSlot.Legs => ObjType.Legs,
        EquipmentSlot.Feet => ObjType.Feet,
        EquipmentSlot.MainHand => ObjType.Weapon,
        EquipmentSlot.OffHand => ObjType.Shield,
        EquipmentSlot.Neck => ObjType.Neck,
        EquipmentSlot.Neck2 => ObjType.Neck,
        EquipmentSlot.LFinger => ObjType.Fingers,
        EquipmentSlot.RFinger => ObjType.Fingers,
        EquipmentSlot.Cloak => ObjType.Abody,
        EquipmentSlot.Waist => ObjType.Waist,
        _ => ObjType.Magic
    };

    #endregion
}
