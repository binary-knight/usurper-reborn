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
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔═════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine($"║{"YOUR HOME".PadLeft((77 + 9) / 2).PadRight(77)}║");
        terminal.WriteLine("╚═════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        // Quick stats bar
        terminal.SetColor("gray");
        terminal.Write("  HP: ");
        terminal.SetColor(currentPlayer.HP < currentPlayer.MaxHP / 4 ? "red" : (currentPlayer.HP < currentPlayer.MaxHP / 2 ? "yellow" : "bright_green"));
        terminal.Write($"{currentPlayer.HP}/{currentPlayer.MaxHP}");
        terminal.SetColor("gray");
        terminal.Write("  |  Mana: ");
        terminal.SetColor("bright_blue");
        terminal.Write($"{currentPlayer.Mana}/{currentPlayer.MaxMana}");
        terminal.SetColor("gray");
        terminal.Write("  |  Gold: ");
        terminal.SetColor("bright_yellow");
        terminal.Write($"{currentPlayer.Gold:N0}");
        terminal.SetColor("gray");
        terminal.Write("  |  Potions: ");
        terminal.SetColor("bright_green");
        terminal.WriteLine($"{currentPlayer.Healing}");
        terminal.WriteLine("");

        // Dynamic description based on all upgrades
        terminal.SetColor("white");
        // Living quarters base description
        switch (currentPlayer.HomeLevel)
        {
            case 0:
                terminal.Write("You stand in a drafty, dilapidated shack. The walls are thin and the roof leaks.");
                break;
            case 1:
                terminal.Write("Your home has seen some repairs. The walls are patched, keeping out the worst of the wind.");
                break;
            case 2:
                terminal.Write("A sturdy cottage with solid walls and a proper door. It feels like a real home.");
                break;
            case 3:
                terminal.Write("A comfortable home with good furniture and warm light.");
                break;
            case 4:
                terminal.Write("A fine manor with quality furnishings and elegant decor.");
                break;
            default:
                terminal.Write("A grand estate befitting a hero, beautifully appointed throughout.");
                break;
        }
        // Bed detail
        switch (currentPlayer.BedLevel)
        {
            case 0: terminal.Write(" A moth-eaten straw pile serves as your bed."); break;
            case 1: terminal.Write(" A simple cot sits in the corner."); break;
            case 2: terminal.Write(" A sturdy wooden bed frame holds a thin mattress."); break;
            case 3: terminal.Write(" A plush feather mattress promises restful sleep."); break;
            case 4: terminal.Write(" An ornate four-poster bed dominates the bedroom."); break;
            default: terminal.Write(" A magnificent canopy bed draped in silk awaits you."); break;
        }
        // Hearth detail
        switch (currentPlayer.HearthLevel)
        {
            case 0: terminal.Write(" A cold firepit sits unused."); break;
            case 1: terminal.Write(" A simple hearth crackles with warmth."); break;
            case 2: terminal.Write(" A stone fireplace radiates steady heat."); break;
            case 3: terminal.Write(" An iron stove fills the room with comforting warmth."); break;
            case 4: terminal.Write(" A grand fireplace roars with inviting flames."); break;
            default: terminal.Write(" An eternal flame burns without fuel, filling every room with warmth."); break;
        }
        terminal.WriteLine("");
        // Chest and garden on second line if upgraded
        var extras = new List<string>();
        if (currentPlayer.ChestLevel > 0)
            extras.Add(ChestNames[Math.Clamp(currentPlayer.ChestLevel, 0, 5)].ToLower());
        if (currentPlayer.GardenLevel > 0)
            extras.Add(GardenNames[Math.Clamp(currentPlayer.GardenLevel, 0, 5)].ToLower());
        if (currentPlayer.HasStudy)
            extras.Add("a study lined with books");
        if (currentPlayer.HasServants)
            extras.Add("servants' quarters");
        if (extras.Count > 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("You also have " + string.Join(", ", extras) + ".");
        }
        terminal.WriteLine("");

        // Show storage & rest info
        int maxRests = GameConfig.HomeRestsPerDay[Math.Clamp(currentPlayer.HomeLevel, 0, 5)];
        int restsLeft = Math.Max(0, maxRests - currentPlayer.HomeRestsToday);
        int recoveryPct = (int)(GameConfig.HomeRecoveryPercent[Math.Clamp(currentPlayer.HomeLevel, 0, 5)] * 100);
        terminal.SetColor("gray");
        terminal.Write("  Rest: ");
        terminal.SetColor(restsLeft > 0 ? "bright_green" : "red");
        terminal.Write($"{restsLeft}/{maxRests} today ({recoveryPct}%)");
        if (currentPlayer.ChestLevel > 0)
        {
            int maxCapacity = GameConfig.ChestCapacity[Math.Clamp(currentPlayer.ChestLevel, 0, 5)];
            terminal.SetColor("gray");
            terminal.Write("  |  Chest: ");
            terminal.SetColor("cyan");
            terminal.Write($"{Chest.Count}/{maxCapacity}");
        }
        terminal.SetColor("gray");
        terminal.Write("  |  Potions: ");
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
                terminal.Write($"{string.Join(" and ", partnersAtHome)} {(partnersAtHome.Count == 1 ? "is" : "are")} here with you");
                if (children.Count > 0)
                {
                    terminal.SetColor("bright_yellow");
                    terminal.Write($" ({children.Count} child{(children.Count != 1 ? "ren" : "")})");
                }
                terminal.WriteLine(".");
            }
            else if (children.Count > 0)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"Your {children.Count} child{(children.Count != 1 ? "ren are" : " is")} here.");
            }

            if (partnersAway.Count > 0)
            {
                terminal.SetColor("gray");
                foreach (var (name, loc) in partnersAway)
                {
                    terminal.WriteLine($"  {name} is at {loc}.");
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
        terminal.WriteLine("--- Home Activities ---");
        terminal.WriteLine("");

        // Row 1: Core actions
        WriteMenuCol(" ", "E", "Rest & Recover", true);
        WriteMenuCol("", "U", "Upgrades", true);
        WriteMenuNL("", "S", "Status", true);

        // Row 2: Chest operations (dimmed if no chest)
        WriteMenuCol(" ", "D", "Deposit Item", hasChest);
        WriteMenuCol("", "W", "Withdraw Item", hasChest);
        WriteMenuNL("", "L", "List Chest", hasChest);

        // Row 3: Garden, Herbs, Trophies, Family
        WriteMenuCol(" ", "A", "Gather Herbs", hasGarden);
        WriteMenuCol("", "J", "Use Herb", currentPlayer.TotalHerbCount > 0);
        WriteMenuNL("", "T", "Trophies", hasTrophies);

        WriteMenuCol(" ", "F", "Family", true);

        // Row 4: Romance
        WriteMenuCol(" ", "P", "Partner Time", true);
        WriteMenuCol("", "B", "Bedroom", true);
        WriteMenuNL("", "!", "Resurrect", true);

        // Row 5: Items
        WriteMenuCol(" ", "I", "Inventory", true);
        WriteMenuCol("", "G", "Gear Partner", true);
        WriteMenuNL("", "H", "Heal (Potion)", true);

        terminal.WriteLine("");

        // Single-player: Wait until nightfall
        if (!UsurperRemake.BBS.DoorMode.IsOnlineMode && currentPlayer != null
            && !DailySystemManager.CanRestForNight(currentPlayer))
        {
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("Z");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("dark_cyan");
            terminal.WriteLine("Wait until nightfall");
        }

        // Navigation row
        WriteMenuNL(" ", "R", "Return", true);

        terminal.WriteLine("");
    }

    // Write a menu option padded to a fixed 26-char column width
    private void WriteMenuOption(string prefix, string key, string label, bool available, int width)
    {
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
        ShowBBSHeader("YOUR HOME");

        // 1-line description based on home level
        terminal.SetColor("white");
        string bbsDesc = currentPlayer.HomeLevel switch
        {
            0 => " A drafty shack with a leaky roof and moth-eaten straw pile.",
            1 => " A patched-up home. The walls keep out the worst of the wind.",
            2 => " A sturdy cottage with solid walls. Feels like a real home.",
            3 => " A comfortable home with good furniture and warm light.",
            4 => " A fine manor with elegant decor. The envy of townsfolk.",
            _ => " A grand estate befitting a hero. Luxury and serenity abound."
        };
        terminal.WriteLine(bbsDesc);

        // Compact info
        int bbsMaxRests = GameConfig.HomeRestsPerDay[Math.Clamp(currentPlayer.HomeLevel, 0, 5)];
        int bbsRestsLeft = Math.Max(0, bbsMaxRests - currentPlayer.HomeRestsToday);
        int bbsRecovery = (int)(GameConfig.HomeRecoveryPercent[Math.Clamp(currentPlayer.HomeLevel, 0, 5)] * 100);
        terminal.SetColor("gray");
        terminal.Write($" Rest:{bbsRestsLeft}/{bbsMaxRests}({bbsRecovery}%)");
        if (currentPlayer.ChestLevel > 0)
        {
            int maxCap = GameConfig.ChestCapacity[Math.Clamp(currentPlayer.ChestLevel, 0, 5)];
            terminal.Write($"  Chest:{Chest.Count}/{maxCap}");
        }
        terminal.Write($"  Potions:");
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
                terminal.Write($" {string.Join(", ", partnersAtHome)} here");
            if (children.Count > 0)
            {
                terminal.SetColor("bright_yellow");
                terminal.Write($" {children.Count} child{(children.Count != 1 ? "ren" : "")}");
            }
            terminal.WriteLine("");
        }

        terminal.WriteLine("");

        // Menu rows - consistent layout regardless of upgrades
        ShowBBSMenuRow(("E", "bright_yellow", "Rest"), ("U", "bright_yellow", "Upgrades"), ("S", "bright_yellow", "Status"));
        ShowBBSMenuRow(("D", "bright_yellow", "Deposit"), ("W", "bright_yellow", "Withdraw"), ("L", "bright_yellow", "List Chest"));
        ShowBBSMenuRow(("A", "bright_yellow", "Herbs"), ("T", "bright_yellow", "Trophies"), ("F", "bright_yellow", "Family"));
        ShowBBSMenuRow(("P", "bright_yellow", "Partner"), ("B", "bright_yellow", "Bedroom"), ("!", "bright_yellow", "Resurrect"));
        ShowBBSMenuRow(("I", "bright_yellow", "Inventory"), ("G", "bright_yellow", "Gear"), ("H", "bright_yellow", "Heal(Pot)"));
        ShowBBSMenuRow(("R", "bright_yellow", "Return"));

        ShowBBSFooter();
    }

    protected override async Task<bool> ProcessChoice(string choice)
    {
        if (string.IsNullOrWhiteSpace(choice))
            return false;

        var c = choice.Trim().ToUpperInvariant();

        // Handle ! locally first (Resurrect) before global handler claims it for bug report
        if (c == "!")
        {
            await ResurrectAlly();
            return false;
        }

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
                    terminal.WriteLine("You don't have a chest yet. Visit [U]pgrades to buy one.", "yellow");
                    await terminal.WaitForKey();
                }
                else
                    await DepositItem();
                return false;
            case "W":
                if (currentPlayer.ChestLevel <= 0)
                {
                    terminal.WriteLine("You don't have a chest yet. Visit [U]pgrades to buy one.", "yellow");
                    await terminal.WaitForKey();
                }
                else
                    await WithdrawItem();
                return false;
            case "L":
                if (currentPlayer.ChestLevel <= 0)
                {
                    terminal.WriteLine("You don't have a chest yet. Visit [U]pgrades to buy one.", "yellow");
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
                ShowTrophies();
                await terminal.WaitForKey();
                return false;
            case "F":
                await ShowFamily();
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
                if (!UsurperRemake.BBS.DoorMode.IsOnlineMode && currentPlayer != null)
                    await DailySystemManager.Instance.WaitUntilEvening(currentPlayer, terminal);
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
                terminal.WriteLine("Your straw pile is too uncomfortable to rest on again today.");
            else
                terminal.WriteLine("You've already rested as much as you can today.");
            terminal.SetColor("gray");
            terminal.WriteLine($"Rests used: {currentPlayer.HomeRestsToday}/{maxRests}. Try again tomorrow.");
            await terminal.WaitForKey();
            return;
        }

        // Flavor text based on home level
        switch (homeLevel)
        {
            case 0:
                terminal.WriteLine("You curl up on the moth-eaten straw pile...", "gray");
                break;
            case 1:
                terminal.WriteLine("You lie down on your simple cot...", "gray");
                break;
            case 2:
                terminal.WriteLine("You rest in your wooden bed...", "gray");
                break;
            default:
                terminal.WriteLine("You relax in the comfort of your bed...", "gray");
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
            terminal.WriteLine("You rest, but dark memories haunt your sleep...", "dark_red");
        }

        if (restEfficiency >= 1.0f)
        {
            terminal.WriteLine("You feel completely rejuvenated!", "bright_green");
        }
        else
        {
            terminal.SetColor("green");
            terminal.WriteLine($"Recovered {healAmount} HP and {manaAmount} mana. ({(int)(restEfficiency * 100)}% recovery)");
        }

        currentPlayer.HomeRestsToday++;

        // Apply Well-Rested buff from Hearth
        int hearthLevel = Math.Clamp(currentPlayer.HearthLevel, 0, 5);
        if (hearthLevel > 0)
        {
            float bonus = GameConfig.HearthDamageBonus[hearthLevel];
            int combats = GameConfig.HearthCombatDuration[hearthLevel];
            currentPlayer.WellRestedCombats = combats;
            currentPlayer.WellRestedBonus = bonus;
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"The warmth of your hearth invigorates you! (+{(int)(bonus * 100)}% damage/defense for {combats} combats)");
        }

        // Show remaining rests
        int restsLeft = maxRests - currentPlayer.HomeRestsToday;
        terminal.SetColor("gray");
        terminal.WriteLine($"Rests remaining today: {restsLeft}/{maxRests}");

        // Check for dreams during rest at home (nightmares take priority)
        var dream = DreamSystem.Instance.GetDreamForRest(currentPlayer, 0);
        if (dream != null)
        {
            await Task.Delay(1500);
            terminal.WriteLine("");
            terminal.SetColor("dark_magenta");
            terminal.WriteLine("As sleep takes you, dreams unfold...");
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

        // In single-player, resting at home advances the day if it's nighttime
        if (!UsurperRemake.BBS.DoorMode.IsOnlineMode)
        {
            if (DailySystemManager.CanRestForNight(currentPlayer))
            {
                terminal.WriteLine("");
                terminal.SetColor("gray");
                terminal.WriteLine("You settle in for a proper night's rest...");
                await Task.Delay(1500);
                await DailySystemManager.Instance.RestAndAdvanceToMorning(currentPlayer);
                terminal.SetColor("yellow");
                terminal.WriteLine($"A new day dawns. (Day {DailySystemManager.Instance.CurrentDay})");
                await Task.Delay(1500);
            }
            else
            {
                terminal.WriteLine("");
                terminal.SetColor("gray");
                var period = DailySystemManager.GetTimePeriodString(currentPlayer);
                terminal.WriteLine($"It's only {period}. You rest briefly but it's too early to sleep for the night.");
            }
        }

        await terminal.WaitForKey();
    }

    private async Task GatherHerbs()
    {
        int gardenLevel = Math.Clamp(currentPlayer.GardenLevel, 0, 5);
        int maxHerbs = GameConfig.HerbsPerDay[gardenLevel];

        if (gardenLevel <= 0)
        {
            terminal.WriteLine("You don't have a herb garden. Visit [U]pgrades to build one.", "yellow");
            await terminal.WaitForKey();
            return;
        }

        int herbsLeft = Math.Max(0, maxHerbs - currentPlayer.HerbsGatheredToday);
        if (herbsLeft <= 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("You've already gathered all the herbs your garden can produce today.");
            terminal.SetColor("gray");
            terminal.WriteLine($"Herbs gathered: {currentPlayer.HerbsGatheredToday}/{maxHerbs}. Try again tomorrow.");
            await terminal.WaitForKey();
            return;
        }

        while (herbsLeft > 0)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_green");
            terminal.WriteLine("═══ HERB GARDEN ═══");
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine($"Gathers remaining today: {herbsLeft}");
            terminal.WriteLine("");

            // Show available herb types based on garden level
            terminal.SetColor("white");
            terminal.WriteLine("Which herb would you like to gather?");
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
            terminal.WriteLine("  [Q] Done gathering");
            terminal.WriteLine("");
            terminal.Write("Choice: ", "white");

            string input = (await terminal.ReadLineAsync())?.Trim().ToUpper() ?? "";
            if (input == "Q" || string.IsNullOrEmpty(input)) break;

            if (int.TryParse(input, out int choice) && choice >= 1 && choice <= gardenLevel && choice <= 5)
            {
                var herbType = (HerbType)choice;
                int count = currentPlayer.GetHerbCount(herbType);
                int max = GameConfig.HerbMaxCarry[choice];
                if (count >= max)
                {
                    terminal.WriteLine($"Your pouch is full of {HerbData.GetName(herbType)}! ({count}/{max})", "yellow");
                    await terminal.WaitForKey();
                    continue;
                }

                currentPlayer.AddHerb(herbType);
                currentPlayer.HerbsGatheredToday++;
                herbsLeft--;

                terminal.SetColor(HerbData.GetColor(herbType));
                terminal.WriteLine($"Gathered a {HerbData.GetName(herbType)}! ({currentPlayer.GetHerbCount(herbType)}/{max})");
                await Task.Delay(500);
            }
        }

        terminal.SetColor("gray");
        terminal.WriteLine("You brush the dirt from your hands and head inside.");
        await terminal.WaitForKey();
    }

    /// <summary>
    /// Show herb pouch and let player use an herb. Shared by Home, Dungeon, and BaseLocation.
    /// </summary>
    public static async Task UseHerbMenu(Character player, TerminalEmulator terminal)
    {
        if (player.TotalHerbCount <= 0)
        {
            terminal.WriteLine("Your herb pouch is empty. Gather herbs from your garden at home.", "yellow");
            await terminal.WaitForKey();
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("bright_green");
        terminal.WriteLine("═══ HERB POUCH ═══");
        terminal.WriteLine("");

        if (player.HasActiveHerbBuff)
        {
            var buffName = HerbData.GetName((HerbType)player.HerbBuffType);
            terminal.SetColor("cyan");
            terminal.WriteLine($"Active buff: {buffName} ({player.HerbBuffCombats} combats remaining)");
            terminal.WriteLine("");
        }

        terminal.SetColor("white");
        terminal.WriteLine("Select an herb to use:");
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
            terminal.Write($"  [{idx}] {HerbData.GetName(type)} x{count}");
            terminal.SetColor("gray");
            terminal.WriteLine($" — {HerbData.GetDescription(type)}");
            idx++;
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine("  [Q] Cancel");
        terminal.WriteLine("");
        terminal.Write("Choice: ", "white");

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
                long healAmount = (long)(player.MaxHP * GameConfig.HerbHealPercent);
                healAmount = Math.Min(healAmount, player.MaxHP - player.HP);
                player.HP += healAmount;
                terminal.WriteLine($"You crush a {herbName} and drink the extract. Restored {healAmount} HP! ({player.HP}/{player.MaxHP})");
                break;

            case HerbType.IronbarkRoot:
                player.HerbBuffType = (int)HerbType.IronbarkRoot;
                player.HerbBuffCombats = GameConfig.HerbBuffDuration;
                player.HerbBuffValue = GameConfig.HerbDefenseBonus;
                player.HerbExtraAttacks = 0;
                terminal.WriteLine($"You chew a tough {herbName}. Your skin hardens! (+{(int)(GameConfig.HerbDefenseBonus * 100)}% defense for {GameConfig.HerbBuffDuration} combats)");
                break;

            case HerbType.FirebloomPetal:
                player.HerbBuffType = (int)HerbType.FirebloomPetal;
                player.HerbBuffCombats = GameConfig.HerbBuffDuration;
                player.HerbBuffValue = GameConfig.HerbDamageBonus;
                player.HerbExtraAttacks = 0;
                terminal.WriteLine($"You inhale the fiery scent of a {herbName}. Your strikes burn with power! (+{(int)(GameConfig.HerbDamageBonus * 100)}% damage for {GameConfig.HerbBuffDuration} combats)");
                break;

            case HerbType.Swiftthistle:
                player.HerbBuffType = (int)HerbType.Swiftthistle;
                player.HerbBuffCombats = GameConfig.HerbSwiftDuration;
                player.HerbBuffValue = 0;
                player.HerbExtraAttacks = GameConfig.HerbExtraAttackCount;
                terminal.WriteLine($"The {herbName} sends energy coursing through your limbs! (+{GameConfig.HerbExtraAttackCount} extra attack for {GameConfig.HerbSwiftDuration} combats)");
                break;

            case HerbType.StarbloomEssence:
                long manaRestore = (long)(player.MaxMana * GameConfig.HerbManaRestorePercent);
                manaRestore = Math.Min(manaRestore, player.MaxMana - player.Mana);
                player.Mana += manaRestore;
                player.HerbBuffType = (int)HerbType.StarbloomEssence;
                player.HerbBuffCombats = GameConfig.HerbBuffDuration;
                player.HerbBuffValue = GameConfig.HerbSpellBonus;
                player.HerbExtraAttacks = 0;
                terminal.WriteLine($"Starbloom essence floods your mind with arcane clarity! Restored {manaRestore} mana. (+{(int)(GameConfig.HerbSpellBonus * 100)}% spell damage for {GameConfig.HerbBuffDuration} combats)");
                break;
        }

        await terminal.WaitForKey();
    }

    private async Task DepositItem()
    {
        if (!currentPlayer.Inventory.Any())
        {
            terminal.WriteLine("You have no items to store.", "yellow");
            await terminal.WaitForKey();
            return;
        }
        int maxCapacity = GameConfig.ChestCapacity[Math.Clamp(currentPlayer.ChestLevel, 0, 5)];
        if (Chest.Count >= maxCapacity)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"Your chest is full! ({Chest.Count}/{maxCapacity} items)");
            terminal.SetColor("gray");
            terminal.WriteLine("Upgrade your chest to store more items.");
            await terminal.WaitForKey();
            return;
        }
        terminal.WriteLine($"Select item to deposit ({Chest.Count}/{maxCapacity} stored, or 0 to cancel):", "cyan");
        for (int i = 0; i < currentPlayer.Inventory.Count; i++)
        {
            terminal.WriteLine($"  {i + 1}. {currentPlayer.Inventory[i].GetDisplayName()}");
        }
        var input = await terminal.GetInput("Choice: ");
        if (int.TryParse(input, out int idx) && idx > 0 && idx <= currentPlayer.Inventory.Count)
        {
            var item = currentPlayer.Inventory[idx - 1];
            currentPlayer.Inventory.RemoveAt(idx - 1);
            Chest.Add(item);
            terminal.WriteLine($"Stored {item.GetDisplayName()} in your chest. ({Chest.Count}/{maxCapacity})", "green");
        }
        else
        {
            terminal.WriteLine("Cancelled.", "gray");
        }
        await terminal.WaitForKey();
    }

    private async Task WithdrawItem()
    {
        if (!Chest.Any())
        {
            terminal.WriteLine("Your chest is empty.", "yellow");
            await terminal.WaitForKey();
            return;
        }
        terminal.WriteLine("Select item number to withdraw (or 0 to cancel):", "cyan");
        for (int i = 0; i < Chest.Count; i++)
        {
            terminal.WriteLine($"  {i + 1}. {Chest[i].GetDisplayName()}");
        }
        var input = await terminal.GetInput("Choice: ");
        if (int.TryParse(input, out int idx) && idx > 0 && idx <= Chest.Count)
        {
            var item = Chest[idx - 1];
            Chest.RemoveAt(idx - 1);
            currentPlayer.Inventory.Add(item);
            terminal.WriteLine($"Retrieved {item.GetDisplayName()} from your chest.", "green");
        }
        else
        {
            terminal.WriteLine("Cancelled.", "gray");
        }
        await terminal.WaitForKey();
    }

    private void ShowChestContents()
    {
        terminal.WriteLine("\nItems in your chest:", "bright_cyan");
        if (!Chest.Any())
        {
            terminal.WriteLine("  (empty)", "gray");
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
        terminal.WriteLine("\nTrophies & Achievements", "bright_cyan");
        terminal.WriteLine();

        // Use the proper PlayerAchievements from Character base class
        // Note: Player.Achievements hides Character.Achievements, so we cast to Character
        var achievements = ((Character)currentPlayer).Achievements;

        if (achievements.UnlockedCount > 0)
        {
            // Show summary
            terminal.SetColor("white");
            terminal.WriteLine($"  Total Unlocked: {achievements.UnlockedCount} / {AchievementSystem.TotalAchievements}");
            terminal.WriteLine($"  Achievement Points: {achievements.TotalPoints}");
            terminal.WriteLine($"  Completion: {achievements.CompletionPercentage:F1}%");
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
            terminal.WriteLine("  No achievements unlocked yet.", "gray");
            terminal.WriteLine();
            terminal.WriteLine("  Explore the dungeon, defeat monsters, and complete", "gray");
            terminal.WriteLine("  challenges to earn achievements and rewards!", "gray");
        }
    }

    private async Task UseHealingPotion()
    {
        if (currentPlayer.HP >= currentPlayer.MaxHP)
        {
            terminal.WriteLine("You're already at full health!", "bright_green");
            await terminal.WaitForKey();
            return;
        }

        if (currentPlayer.Healing <= 0)
        {
            terminal.WriteLine("You don't have any healing potions!", "red");
            terminal.WriteLine("Visit the Healer or Magic Shop to buy some.", "gray");
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
        terminal.WriteLine($"You drink a healing potion...");
        terminal.WriteLine($"Restored {actualHeal} HP! ({currentPlayer.HP}/{currentPlayer.MaxHP})");
        terminal.SetColor("gray");
        terminal.WriteLine($"Potions remaining: {currentPlayer.Healing}");
        await terminal.WaitForKey();
    }

    private new async Task ShowInventory()
    {
        terminal.WriteLine("\n", "white");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("=== YOUR INVENTORY ===");
        terminal.WriteLine();

        if (!currentPlayer.Inventory.Any())
        {
            terminal.WriteLine("  Your inventory is empty.", "gray");
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
                terminal.Write($" (Value: {item.Value:N0} gold)");
            }
            terminal.WriteLine();
        }

        terminal.WriteLine();
        terminal.SetColor("cyan");
        terminal.Write("Options: ");
        terminal.SetColor("bright_yellow");
        terminal.Write("[D]");
        terminal.SetColor("cyan");
        terminal.Write("eposit to chest, ");
        terminal.SetColor("bright_yellow");
        terminal.Write("[E]");
        terminal.SetColor("cyan");
        terminal.Write("quip item, ");
        terminal.SetColor("bright_yellow");
        terminal.Write("[Q]");
        terminal.SetColor("cyan");
        terminal.WriteLine("uit");

        var input = await terminal.GetInput("Choice: ");
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
            terminal.WriteLine("No items to equip.", "yellow");
            await terminal.WaitForKey();
            return;
        }

        terminal.WriteLine("\nSelect item number to use/equip (or 0 to cancel):", "cyan");
        for (int i = 0; i < currentPlayer.Inventory.Count; i++)
        {
            var item = currentPlayer.Inventory[i];
            terminal.Write($"  {i + 1}. ");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(item.GetDisplayName());
        }
        terminal.SetColor("white");

        var input = await terminal.GetInput("Choice: ");
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
                terminal.WriteLine($"Used {item.GetDisplayName()}!", "bright_green");
            }
        }
        else
        {
            terminal.WriteLine("Cancelled.", "gray");
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
            WisdomBonus = item.Wisdom,
            CharismaBonus = item.Charisma,
            MaxHPBonus = item.HP,
            MaxManaBonus = item.Mana,
            Value = item.Value,
            IsCursed = item.IsCursed,
            Rarity = EquipmentRarity.Common
        };

        // Register in database to get an ID
        EquipmentDatabase.RegisterDynamic(equipment);

        // For rings, ask which finger
        if (targetSlot == EquipmentSlot.LFinger)
        {
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.WriteLine("Equip to which finger?");
            terminal.SetColor("white");
            terminal.WriteLine("  (L) Left finger");
            terminal.WriteLine("  (R) Right finger");
            terminal.WriteLine("  (C) Cancel");
            terminal.Write("Choice: ");
            var fingerChoice = await terminal.GetInput("");
            if (fingerChoice.ToUpper() == "R")
            {
                targetSlot = EquipmentSlot.RFinger;
                equipment.Slot = EquipmentSlot.RFinger;
            }
            else if (fingerChoice.ToUpper() != "L")
            {
                terminal.WriteLine("Cancelled.", "gray");
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
                terminal.WriteLine("Cancelled.", "gray");
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
            terminal.WriteLine($"Equipped {item.GetDisplayName()}!");
            if (!string.IsNullOrEmpty(message))
            {
                terminal.SetColor("gray");
                terminal.WriteLine(message);
            }
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine($"Cannot equip: {message}");
        }
    }

    /// <summary>
    /// Prompt player to choose which hand to equip a one-handed weapon in
    /// </summary>
    private async Task<EquipmentSlot?> PromptForWeaponSlotHome()
    {
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine("This is a one-handed weapon. Where would you like to equip it?");
        terminal.WriteLine("");

        // Show current equipment in both slots
        var mainHandItem = currentPlayer.GetEquipment(EquipmentSlot.MainHand);
        var offHandItem = currentPlayer.GetEquipment(EquipmentSlot.OffHand);

        terminal.SetColor("white");
        terminal.Write("  (M) Main Hand: ");
        if (mainHandItem != null)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(mainHandItem.Name);
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("Empty");
        }

        terminal.SetColor("white");
        terminal.Write("  (O) Off-Hand:  ");
        if (offHandItem != null)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(offHandItem.Name);
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("Empty");
        }

        terminal.SetColor("white");
        terminal.WriteLine("  (C) Cancel");
        terminal.WriteLine("");

        terminal.Write("Your choice: ");
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
        terminal.WriteLine("╔══════════════════════════════════════╗", "bright_cyan");
        terminal.WriteLine("║            FAMILY & LOVED ONES       ║", "bright_cyan");
        terminal.WriteLine("╚══════════════════════════════════════╝", "bright_cyan");
        terminal.WriteLine();

        var romance = RomanceTracker.Instance;
        bool hasFamily = false;

        // Show spouse(s)
        if (romance.Spouses.Count > 0)
        {
            hasFamily = true;
            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"  <3 SPOUSE{(romance.Spouses.Count > 1 ? "S" : "")} <3");
            terminal.SetColor("white");

            foreach (var spouse in romance.Spouses)
            {
                var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == spouse.NPCId);
                var name = npc?.Name ?? spouse.NPCId;
                var marriedDays = (int)(DateTime.Now - spouse.MarriedDate).TotalDays;

                terminal.Write($"    ");
                terminal.SetColor("bright_red");
                terminal.Write("<3 ");
                terminal.SetColor("bright_white");
                terminal.Write(name);
                terminal.SetColor("gray");
                terminal.WriteLine($" - Married {marriedDays} day{(marriedDays != 1 ? "s" : "")}");

                if (spouse.Children > 0)
                {
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine($"      Children together: {spouse.Children}");
                }

                if (spouse.AcceptsPolyamory)
                {
                    terminal.SetColor("magenta");
                    terminal.WriteLine("      (Open to polyamory)");
                }
            }
            terminal.WriteLine();
        }

        // Show lovers
        if (romance.CurrentLovers.Count > 0)
        {
            hasFamily = true;
            terminal.SetColor("magenta");
            terminal.WriteLine("  LOVERS");
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
                terminal.Write($" - Together {daysTogether} day{(daysTogether != 1 ? "s" : "")}");

                if (lover.IsExclusive)
                {
                    terminal.SetColor("bright_cyan");
                    terminal.Write(" [Exclusive]");
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
            terminal.WriteLine("  FRIENDS WITH BENEFITS");
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
            terminal.WriteLine($"  CHILDREN: {children.Count}");
            terminal.SetColor("white");

            foreach (var child in children)
            {
                terminal.Write("    ");
                terminal.SetColor("bright_green");
                terminal.Write("* ");
                terminal.SetColor("bright_white");
                terminal.Write($"{child.Name}");
                terminal.SetColor("gray");
                terminal.Write($" - {child.Age} year{(child.Age != 1 ? "s" : "")} old, {(child.Sex == CharacterSex.Male ? "boy" : "girl")}");

                // Show behavior indicator
                terminal.SetColor(child.Soul > 100 ? "bright_cyan" : (child.Soul < -100 ? "red" : "white"));
                terminal.WriteLine($" ({child.GetSoulDescription()})");

                // Show health issues
                if (child.Health != GameConfig.ChildHealthNormal)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine($"        Health: {child.GetHealthDescription()}");
                }
            }

            // Check for children approaching adulthood
            var teensCount = children.Count(c => c.Age >= 15 && c.Age < FamilySystem.ADULT_AGE);
            if (teensCount > 0)
            {
                terminal.SetColor("bright_cyan");
                terminal.WriteLine($"    ({teensCount} will come of age soon and become adult NPCs!)");
            }
            terminal.WriteLine();
        }

        // Show ex-spouses (detailed records)
        if (romance.ExSpouses.Count > 0)
        {
            terminal.SetColor("dark_red");
            terminal.WriteLine($"  EX-SPOUSES: {romance.ExSpouses.Count}");
            terminal.SetColor("gray");
            foreach (var ex in romance.ExSpouses)
            {
                var marriageDuration = (ex.DivorceDate - ex.MarriedDate).Days;
                var daysSinceDivorce = (DateTime.Now - ex.DivorceDate).Days;
                var initiator = ex.PlayerInitiated ? "you" : "them";

                terminal.Write($"    - {ex.NPCName}");
                terminal.SetColor("dark_gray");
                terminal.WriteLine($" (married {marriageDuration} days, divorced {daysSinceDivorce} days ago by {initiator})");
                terminal.SetColor("gray");

                if (ex.ChildrenTogether > 0)
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"      ^ {ex.ChildrenTogether} child(ren) together");
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
            terminal.WriteLine($"  PAST RELATIONSHIPS: {exLoversOnly.Count}");
            terminal.SetColor("gray");
            foreach (var exId in exLoversOnly.Take(5)) // Show max 5
            {
                var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == exId);
                var name = npc?.Name ?? exId;
                terminal.WriteLine($"    - {name}");
            }
            if (exLoversOnly.Count > 5)
            {
                terminal.WriteLine($"    ... and {exLoversOnly.Count - 5} more");
            }
            terminal.WriteLine();
        }

        if (!hasFamily)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  You live alone... for now.");
            terminal.WriteLine();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("  Tip: Meet someone special at Main Street or Love Corner!");
        }

        terminal.SetColor("white");
        await terminal.WaitForKey();
    }

    private async Task SpendTimeWithSpouse()
    {
        var romance = RomanceTracker.Instance;

        if (romance.Spouses.Count == 0 && romance.CurrentLovers.Count == 0)
        {
            terminal.WriteLine("\nYou have no spouse or lover to spend time with.", "yellow");
            terminal.WriteLine("Perhaps you should get out there and meet someone?", "gray");
            await terminal.WaitForKey();
            return;
        }

        terminal.WriteLine("\n", "white");
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("Who would you like to spend time with?");
        terminal.WriteLine();

        var options = new List<(string id, string name, string type)>();

        foreach (var spouse in romance.Spouses)
        {
            var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == spouse.NPCId);
            var name = npc?.Name ?? spouse.NPCId;
            options.Add((spouse.NPCId, name, "spouse"));
        }

        foreach (var lover in romance.CurrentLovers)
        {
            var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == lover.NPCId);
            var name = npc?.Name ?? lover.NPCId;
            options.Add((lover.NPCId, name, "lover"));
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
        terminal.WriteLine(" Cancel");
        terminal.WriteLine();

        var input = await terminal.GetInput("Choice: ");
        if (!int.TryParse(input, out int choice) || choice < 1 || choice > options.Count)
        {
            terminal.WriteLine("Cancelled.", "gray");
            await terminal.WaitForKey();
            return;
        }

        var selected = options[choice - 1];
        var selectedNpc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == selected.id);

        if (selectedNpc == null)
        {
            terminal.WriteLine($"{selected.name} is not available right now.", "yellow");
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
        terminal.WriteLine($"You spend quality time with {partner.Name}...");
        terminal.WriteLine();

        terminal.SetColor("bright_yellow");
        terminal.Write("  [1]");
        terminal.SetColor("white");
        terminal.WriteLine(" Have a romantic dinner together");
        terminal.SetColor("bright_yellow");
        terminal.Write("  [2]");
        terminal.SetColor("white");
        terminal.WriteLine(" Take a walk and hold hands");
        terminal.SetColor("bright_yellow");
        terminal.Write("  [3]");
        terminal.SetColor("white");
        terminal.WriteLine(" Cuddle by the fire");
        terminal.SetColor("bright_yellow");
        terminal.Write("  [4]");
        terminal.SetColor("white");
        terminal.WriteLine(" Have a deep conversation");
        if (relationType == "spouse")
        {
            terminal.SetColor("bright_yellow");
            terminal.Write("  [5]");
            terminal.SetColor("bright_red");
            terminal.WriteLine(" Retire to the bedroom...");
            terminal.SetColor("bright_yellow");
            terminal.Write("  [6]");
            terminal.SetColor("yellow");
            terminal.WriteLine(" Discuss our relationship...");
        }
        terminal.SetColor("bright_yellow");
        terminal.Write("  [0]");
        terminal.SetColor("gray");
        terminal.WriteLine(" Cancel");
        terminal.WriteLine();

        var input = await terminal.GetInput("Choice: ");
        if (!int.TryParse(input, out int choice) || choice < 1)
        {
            terminal.WriteLine("You decide to spend time alone.", "gray");
            await terminal.WaitForKey();
            return;
        }

        terminal.WriteLine();

        switch (choice)
        {
            case 1: // Romantic dinner
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"You prepare a lovely dinner for {partner.Name}.");
                terminal.SetColor("white");
                terminal.WriteLine("The candlelight flickers as you share stories and laughter.");
                terminal.WriteLine($"{partner.Name} gazes at you with affection.");

                // XP bonus for married couples
                if (relationType == "spouse")
                {
                    long xpBonus = currentPlayer.Level * 50;
                    currentPlayer.Experience += xpBonus;
                    terminal.SetColor("bright_green");
                    terminal.WriteLine($"Your bond strengthens! (+{xpBonus} XP)");
                }
                break;

            case 2: // Walk and hold hands
                terminal.SetColor("cyan");
                terminal.WriteLine($"You and {partner.Name} walk hand in hand through the garden.");
                terminal.SetColor("white");
                terminal.WriteLine("The evening air is cool and refreshing.");
                terminal.WriteLine($"{partner.Name} rests their head on your shoulder.");

                // Small HP recovery from relaxation
                currentPlayer.HP = Math.Min(currentPlayer.HP + currentPlayer.MaxHP / 20, currentPlayer.MaxHP);
                terminal.SetColor("bright_green");
                terminal.WriteLine("The peaceful moment restores you slightly.");
                break;

            case 3: // Cuddle by fire
                terminal.SetColor("bright_red");
                terminal.WriteLine("You settle by the crackling fire together.");
                terminal.SetColor("white");
                terminal.WriteLine($"{partner.Name} nestles close to you for warmth.");
                terminal.WriteLine("You feel utterly at peace in this moment.");

                // Mana recovery from emotional connection
                currentPlayer.Mana = Math.Min(currentPlayer.Mana + currentPlayer.MaxMana / 10, currentPlayer.MaxMana);
                terminal.SetColor("bright_blue");
                terminal.WriteLine("Your spiritual connection is renewed.");
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
                terminal.WriteLine("Invalid choice.", "gray");
                break;

            case 6: // Discuss relationship (spouse only)
                if (relationType == "spouse")
                {
                    await DiscussRelationship(partner);
                    return;
                }
                terminal.WriteLine("Invalid choice.", "gray");
                break;

            default:
                terminal.WriteLine("Invalid choice.", "gray");
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
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══════════════════════════════════════");
        terminal.WriteLine("      RELATIONSHIP DISCUSSION");
        terminal.WriteLine("═══════════════════════════════════════");
        terminal.WriteLine();

        terminal.SetColor("white");
        terminal.WriteLine($"You sit down with {spouse.Name} to discuss your relationship.");
        terminal.WriteLine();

        // Show current status
        if (spouseData != null)
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"  Marriage duration: {(int)(DateTime.Now - spouseData.MarriedDate).TotalDays} days");
            terminal.WriteLine($"  Children together: {spouseData.Children}");
            terminal.WriteLine($"  Polyamory status: {(spouseData.AcceptsPolyamory ? "Open" : "Monogamous")}");
            terminal.WriteLine();
        }

        terminal.SetColor("white");
        terminal.WriteLine("What would you like to discuss?");
        terminal.WriteLine();
        terminal.SetColor("bright_yellow");
        terminal.Write("  [1]");
        terminal.SetColor("white");
        terminal.WriteLine(" Express your love and commitment");

        if (spouseData != null && !spouseData.AcceptsPolyamory)
        {
            terminal.SetColor("bright_yellow");
            terminal.Write("  [2]");
            terminal.SetColor("magenta");
            terminal.WriteLine(" Discuss opening our marriage (polyamory)");
        }
        else if (spouseData != null && spouseData.AcceptsPolyamory)
        {
            terminal.SetColor("bright_yellow");
            terminal.Write("  [2]");
            terminal.SetColor("magenta");
            terminal.WriteLine(" Discuss returning to monogamy");
        }

        terminal.SetColor("bright_yellow");
        terminal.Write("  [3]");
        terminal.SetColor("red");
        terminal.WriteLine(" Discuss separation/divorce...");
        terminal.SetColor("bright_yellow");
        terminal.Write("  [0]");
        terminal.SetColor("gray");
        terminal.WriteLine(" Never mind");
        terminal.WriteLine();

        var input = await terminal.GetInput("Choice: ");
        if (!int.TryParse(input, out int choice) || choice < 1)
        {
            terminal.WriteLine("You decide to talk about something else.", "gray");
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
                terminal.WriteLine("Invalid choice.", "gray");
                break;
        }

        await terminal.WaitForKey();
    }

    private async Task ExpressLove(NPC spouse)
    {
        terminal.WriteLine();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine($"You take {spouse.Name}'s hands in yours and look into their eyes.");
        terminal.WriteLine();

        await Task.Delay(1000);

        terminal.SetColor("white");
        terminal.WriteLine("\"I just wanted you to know how much you mean to me.\"");
        terminal.WriteLine("\"Every day with you is a blessing.\"");
        terminal.WriteLine();

        await Task.Delay(1500);

        var personality = spouse.Brain?.Personality;
        float romanticism = personality?.Romanticism ?? 0.5f;

        terminal.SetColor("bright_cyan");
        if (romanticism > 0.6f)
        {
            terminal.WriteLine($"{spouse.Name}'s eyes glisten with emotion.");
            terminal.WriteLine($"\"And I love you more than words can express,\" they whisper.");
        }
        else
        {
            terminal.WriteLine($"{spouse.Name} smiles warmly.");
            terminal.WriteLine($"\"I know. And I feel the same way.\"");
        }

        // Boost relationship (lower number = better in this system)
        var spouseRecord = RomanceTracker.Instance.Spouses.FirstOrDefault(s => s.NPCId == spouse.ID);
        if (spouseRecord != null)
        {
            spouseRecord.LoveLevel = Math.Max(1, spouseRecord.LoveLevel - 2);
        }

        terminal.SetColor("bright_green");
        terminal.WriteLine();
        terminal.WriteLine("Your bond deepens.");
    }

    private async Task DiscussPolyamory(NPC spouse, Spouse? spouseData)
    {
        if (spouseData == null) return;

        terminal.WriteLine();

        if (!spouseData.AcceptsPolyamory)
        {
            // Trying to open the marriage
            terminal.SetColor("yellow");
            terminal.WriteLine($"You broach a difficult subject with {spouse.Name}...");
            terminal.WriteLine();

            await Task.Delay(1000);

            terminal.SetColor("white");
            terminal.WriteLine("\"I've been thinking about our relationship,\" you begin carefully.");
            terminal.WriteLine("\"I love you deeply, and I want to be honest with you.\"");
            terminal.WriteLine("\"I've been wondering if you might be open to...\"");
            terminal.WriteLine("\"...the idea of us having other partners as well?\"");
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
                terminal.WriteLine($"{spouse.Name} is quiet for a long moment, then takes your hand.");
                terminal.WriteLine();
                terminal.WriteLine($"\"I... I've thought about this too, actually.\"");
                terminal.WriteLine($"\"Our love is strong. I don't think it would diminish\"");
                terminal.WriteLine($"\"what we have if you found connection elsewhere.\"");
                terminal.WriteLine();

                await Task.Delay(1500);

                terminal.SetColor("bright_magenta");
                terminal.WriteLine($"\"Yes. I'm willing to try this. But promise me...\"");
                terminal.WriteLine($"\"Promise me you'll always come home to me.\"");
                terminal.WriteLine();

                terminal.SetColor("bright_green");
                terminal.WriteLine("Your marriage is now open to polyamory!");

                spouseData.AcceptsPolyamory = true;
                spouseData.KnowsAboutOthers = true;
            }
            else
            {
                terminal.WriteLine($"{spouse.Name}'s expression falls.");
                terminal.WriteLine();

                if (jealousy > 0.6f)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine($"\"What? You want to be with OTHER people?\"");
                    terminal.WriteLine($"\"Am I not enough for you? Is that what you're saying?\"");
                    terminal.WriteLine();
                    terminal.SetColor("yellow");
                    terminal.WriteLine("The conversation becomes tense...");

                    // Damage relationship (higher number = worse in this system)
                    if (spouseData != null)
                    {
                        spouseData.LoveLevel = Math.Min(100, spouseData.LoveLevel + 3);
                    }
                }
                else
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"\"I... I understand what you're asking, but...\"");
                    terminal.WriteLine($"\"I don't think I could handle that. I'm sorry.\"");
                    terminal.WriteLine($"\"I need our relationship to be just us.\"");
                    terminal.WriteLine();
                    terminal.SetColor("gray");
                    terminal.WriteLine("They're not ready for that conversation yet.");

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
            terminal.WriteLine($"You approach {spouse.Name} about your open marriage...");
            terminal.WriteLine();

            await Task.Delay(1000);

            terminal.SetColor("white");
            terminal.WriteLine("\"I've been thinking... maybe we should close our marriage.\"");
            terminal.WriteLine("\"Just be with each other. What do you think?\"");
            terminal.WriteLine();

            await Task.Delay(1500);

            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"{spouse.Name} nods thoughtfully.");
            terminal.WriteLine($"\"If that's what you want, I'm happy with that.\"");
            terminal.WriteLine($"\"What matters most is that we're together.\"");
            terminal.WriteLine();

            terminal.SetColor("bright_green");
            terminal.WriteLine("Your marriage is now monogamous.");

            spouseData.AcceptsPolyamory = false;

            // Note: This doesn't automatically remove other lovers
            // The player will need to handle those relationships separately
            if (RomanceTracker.Instance.CurrentLovers.Count > 0)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine();
                terminal.WriteLine("(Note: You should end your other relationships to honor this commitment.)");
            }
        }
    }

    private async Task DiscussDivorce(NPC spouse, Spouse? spouseData)
    {
        if (spouseData == null) return;

        terminal.WriteLine();
        terminal.SetColor("red");
        terminal.WriteLine("═══════════════════════════════════════");
        terminal.WriteLine("         A DIFFICULT CONVERSATION");
        terminal.WriteLine("═══════════════════════════════════════");
        terminal.WriteLine();

        terminal.SetColor("white");
        terminal.WriteLine($"You take a deep breath before speaking to {spouse.Name}...");
        terminal.WriteLine();

        await Task.Delay(1500);

        terminal.SetColor("yellow");
        terminal.WriteLine("\"We need to talk about us. About our future.\"");
        terminal.WriteLine("\"I've been doing a lot of thinking, and...\"");
        terminal.WriteLine();

        await Task.Delay(1500);

        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"{spouse.Name} looks at you with growing concern.");
        terminal.WriteLine($"\"What is it? You're scaring me...\"");
        terminal.WriteLine();

        await Task.Delay(1000);

        terminal.SetColor("red");
        terminal.WriteLine("Are you sure you want to ask for a divorce?");

        if (spouseData.Children > 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"Warning: You have {spouseData.Children} child(ren) together.");
            terminal.WriteLine("You will lose custody of your children!");
        }

        terminal.WriteLine();
        terminal.SetColor("bright_yellow");
        terminal.Write("  [Y]");
        terminal.SetColor("white");
        terminal.WriteLine(" Yes, I want a divorce");
        terminal.SetColor("bright_yellow");
        terminal.Write("  [N]");
        terminal.SetColor("white");
        terminal.WriteLine(" No, I changed my mind");
        terminal.WriteLine();

        var input = await terminal.GetInput("Choice: ");
        if (input.Trim().ToUpperInvariant() != "Y")
        {
            terminal.WriteLine();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"You reach out and take {spouse.Name}'s hand.");
            terminal.WriteLine("\"I'm sorry. I didn't mean to scare you. I love you.\"");
            terminal.WriteLine();
            terminal.SetColor("white");
            terminal.WriteLine($"{spouse.Name} exhales with relief, squeezing your hand tightly.");
            return;
        }

        // Process the divorce
        terminal.WriteLine();
        terminal.SetColor("white");
        terminal.WriteLine("\"I think... I think we should end this.\"");
        terminal.WriteLine("\"I want a divorce.\"");
        terminal.WriteLine();

        await Task.Delay(2000);

        var personality = spouse.Brain?.Personality;
        // Use Impulsiveness as proxy for emotional volatility
        float volatility = personality?.Impulsiveness ?? 0.5f;

        terminal.SetColor("bright_cyan");
        if (volatility > 0.6f)
        {
            terminal.WriteLine($"{spouse.Name}'s face contorts with shock and anger.");
            terminal.WriteLine($"\"WHAT?! After everything we've been through?!\"");
            terminal.WriteLine($"\"How could you do this to me?!\"");
        }
        else
        {
            terminal.WriteLine($"{spouse.Name}'s eyes fill with tears.");
            terminal.WriteLine($"\"I... I see. I suppose I knew something was wrong.\"");
            terminal.WriteLine($"\"If that's truly what you want...\"");
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

        terminal.SetColor("gray");
        terminal.WriteLine("═══════════════════════════════════════");
        terminal.WriteLine();
        terminal.SetColor("red");
        terminal.WriteLine("Your marriage has ended.");
        terminal.WriteLine();

        if (spouseData.Children > 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"{spouse.Name} has taken custody of the children.");
        }

        terminal.SetColor("gray");
        terminal.WriteLine();
        terminal.WriteLine($"{spouse.Name} gathers their things and leaves your home.");

        // Move spouse out of home
        spouse.UpdateLocation("Inn");

        // Generate news
        NewsSystem.Instance?.WriteDivorceNews(currentPlayer.Name, spouse.Name);
    }

    private async Task DiscussIntimateFantasies(NPC spouse, Spouse? spouseData)
    {
        if (spouseData == null) return;

        terminal.WriteLine();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("═══════════════════════════════════════");
        terminal.WriteLine("        INTIMATE FANTASIES");
        terminal.WriteLine("═══════════════════════════════════════");
        terminal.WriteLine();

        terminal.SetColor("white");
        terminal.WriteLine($"You curl up next to {spouse.Name} and speak softly...");
        terminal.WriteLine("\"I want to talk about fantasies. Things we might explore together.\"");
        terminal.WriteLine();

        await Task.Delay(1500);

        var personality = spouse.Brain?.Personality;
        float adventurousness = personality?.Adventurousness ?? 0.5f;
        float voyeurism = personality?.Voyeurism ?? 0.3f;
        float exhibitionism = personality?.Exhibitionism ?? 0.3f;

        terminal.SetColor("bright_cyan");
        if (adventurousness > 0.5f)
        {
            terminal.WriteLine($"{spouse.Name} smiles with a playful glint in their eye.");
            terminal.WriteLine("\"I'm listening. What did you have in mind?\"");
        }
        else
        {
            terminal.WriteLine($"{spouse.Name} looks curious but a bit nervous.");
            terminal.WriteLine("\"Okay... what kind of fantasies?\"");
        }

        terminal.WriteLine();
        terminal.SetColor("white");
        terminal.WriteLine("What do you want to discuss?");
        terminal.WriteLine();
        terminal.SetColor("bright_yellow");
        terminal.Write("  [1]");
        terminal.SetColor("white");
        terminal.WriteLine(" Group encounters (threesomes, moresomes)");
        terminal.SetColor("bright_yellow");
        terminal.Write("  [2]");
        terminal.SetColor("white");
        terminal.WriteLine(" Watching (voyeurism)");
        terminal.SetColor("bright_yellow");
        terminal.Write("  [3]");
        terminal.SetColor("white");
        terminal.WriteLine(" Being watched (exhibitionism)");
        terminal.SetColor("bright_yellow");
        terminal.Write("  [0]");
        terminal.SetColor("gray");
        terminal.WriteLine(" Never mind");
        terminal.WriteLine();

        var input = await terminal.GetInput("Choice: ");
        if (!int.TryParse(input, out int choice) || choice < 1)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("You decide not to pursue this conversation right now.");
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
        terminal.WriteLine("\"Have you ever thought about... bringing someone else into our bed?\"");
        terminal.WriteLine("\"A third person, sharing an experience together?\"");
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
            terminal.WriteLine($"{spouse.Name}'s eyes light up with excitement.");
            terminal.WriteLine("\"Actually... I've thought about that too.\"");
            terminal.WriteLine("\"It could be incredible, experiencing that together.\"");
            terminal.WriteLine();

            terminal.SetColor("bright_green");
            terminal.WriteLine($"{spouse.Name} is open to group encounters!");

            // Mark as consenting
            RomanceTracker.Instance.AgreedStructures[spouse.ID] = RelationshipStructure.OpenRelationship;
        }
        else if (interested)
        {
            terminal.WriteLine($"{spouse.Name} considers the idea carefully.");
            terminal.WriteLine("\"I... I'm not sure. It's a big step.\"");
            terminal.WriteLine("\"Maybe someday, if the right person came along...\"");
            terminal.WriteLine();

            terminal.SetColor("yellow");
            terminal.WriteLine($"{spouse.Name} might be open to this in the future.");
        }
        else if (jealousy > 0.6f)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"{spouse.Name}'s expression hardens.");
            terminal.WriteLine("\"Absolutely not. I don't share. Period.\"");
            terminal.WriteLine("\"I can't believe you'd even suggest that!\"");
            terminal.WriteLine();

            terminal.SetColor("yellow");
            terminal.WriteLine("The conversation has seriously upset them...");

            // Severe damage and moderate divorce chance for jealous spouse
            await HandleSensitiveTopicRejection(spouse, spouseData, 8, 0.08f, "threesomes");
        }
        else
        {
            terminal.WriteLine($"{spouse.Name} shakes their head gently.");
            terminal.WriteLine("\"That's not really something I'm interested in.\"");
            terminal.WriteLine("\"I prefer it to just be us.\"");
            terminal.WriteLine();

            terminal.SetColor("gray");
            terminal.WriteLine("They're not interested in group encounters.");

            // Mild damage for gentle rejection
            await HandleSensitiveTopicRejection(spouse, spouseData, 3, 0.02f, "group encounters");
        }
    }

    private async Task DiscussVoyeurism(NPC spouse, Spouse spouseData, float voyeurism)
    {
        terminal.WriteLine();
        terminal.SetColor("white");
        terminal.WriteLine("\"I want to share something with you...\"");
        terminal.WriteLine("\"I find the idea of watching... arousing.\"");
        terminal.WriteLine();

        await Task.Delay(1500);

        var personality = spouse.Brain?.Personality;
        float adventurousness = personality?.Adventurousness ?? 0.5f;

        terminal.SetColor("bright_cyan");
        if (voyeurism > 0.6f || (adventurousness > 0.7f && voyeurism > 0.4f))
        {
            terminal.WriteLine($"{spouse.Name} leans in closer, intrigued.");
            terminal.WriteLine("\"Watching me with someone else? Or watching together?\"");
            terminal.WriteLine("\"I have to admit... the idea excites me too.\"");
            terminal.WriteLine();

            terminal.SetColor("bright_green");
            terminal.WriteLine("They're open to exploring voyeuristic fantasies!");
        }
        else if (adventurousness > 0.5f)
        {
            terminal.WriteLine($"{spouse.Name} tilts their head thoughtfully.");
            terminal.WriteLine("\"That's... interesting. I've never really considered it.\"");
            terminal.WriteLine("\"What exactly did you have in mind?\"");
            terminal.WriteLine();

            terminal.SetColor("yellow");
            terminal.WriteLine("They're curious but cautious.");
        }
        else
        {
            terminal.WriteLine($"{spouse.Name} looks puzzled.");
            terminal.WriteLine("\"I'm not really into that sort of thing.\"");
            terminal.WriteLine("\"I prefer to be the only one in your eyes.\"");
            terminal.WriteLine();

            terminal.SetColor("gray");
            terminal.WriteLine("They prefer traditional intimacy.");

            // Light damage for this topic
            await HandleSensitiveTopicRejection(spouse, spouseData, 2, 0.01f, "voyeurism");
        }
    }

    private async Task DiscussExhibitionism(NPC spouse, Spouse spouseData, float exhibitionism)
    {
        terminal.WriteLine();
        terminal.SetColor("white");
        terminal.WriteLine("\"There's something I want to confess...\"");
        terminal.WriteLine("\"The idea of being watched... it excites me.\"");
        terminal.WriteLine();

        await Task.Delay(1500);

        var personality = spouse.Brain?.Personality;
        float adventurousness = personality?.Adventurousness ?? 0.5f;

        terminal.SetColor("bright_cyan");
        if (exhibitionism > 0.6f || (adventurousness > 0.7f && exhibitionism > 0.4f))
        {
            terminal.WriteLine($"{spouse.Name}'s eyes darken with desire.");
            terminal.WriteLine("\"Really? Because I've had similar thoughts...\"");
            terminal.WriteLine("\"The thrill of being seen, together...\"");
            terminal.WriteLine();

            terminal.SetColor("bright_green");
            terminal.WriteLine("They share your exhibitionist interests!");
        }
        else if (adventurousness > 0.5f)
        {
            terminal.WriteLine($"{spouse.Name} looks surprised but not put off.");
            terminal.WriteLine("\"That's... bold. I never knew that about you.\"");
            terminal.WriteLine("\"I'm not sure I'd be comfortable with it, but I don't judge.\"");
            terminal.WriteLine();

            terminal.SetColor("yellow");
            terminal.WriteLine("They're understanding but not personally interested.");
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine($"{spouse.Name} looks uncomfortable.");
            terminal.WriteLine("\"I could never do something like that.\"");
            terminal.WriteLine("\"What we share should be private.\"");
            terminal.WriteLine();

            terminal.SetColor("gray");
            terminal.WriteLine("They strongly prefer privacy.");

            // Moderate damage - exhibitionism can be uncomfortable for conservative partners
            await HandleSensitiveTopicRejection(spouse, spouseData, 4, 0.03f, "exhibitionism");
        }
    }

    private async Task DiscussAlternativeArrangements(NPC spouse, Spouse? spouseData)
    {
        if (spouseData == null) return;

        terminal.WriteLine();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("═══════════════════════════════════════");
        terminal.WriteLine("    ALTERNATIVE ARRANGEMENTS");
        terminal.WriteLine("═══════════════════════════════════════");
        terminal.WriteLine();

        terminal.SetColor("white");
        terminal.WriteLine($"You broach a sensitive subject with {spouse.Name}...");
        terminal.WriteLine("\"I want to discuss some... unconventional relationship dynamics.\"");
        terminal.WriteLine();

        await Task.Delay(1500);

        var personality = spouse.Brain?.Personality;
        float adventurousness = personality?.Adventurousness ?? 0.5f;

        terminal.SetColor("bright_cyan");
        if (adventurousness > 0.5f)
        {
            terminal.WriteLine($"{spouse.Name} raises an eyebrow but nods.");
            terminal.WriteLine("\"I'm listening. What's on your mind?\"");
        }
        else
        {
            terminal.WriteLine($"{spouse.Name} looks uncertain.");
            terminal.WriteLine("\"What do you mean by unconventional?\"");
        }

        terminal.WriteLine();
        terminal.SetColor("white");
        terminal.WriteLine("What arrangement do you want to discuss?");
        terminal.WriteLine();
        terminal.SetColor("bright_yellow");
        terminal.Write("  [1]");
        terminal.SetColor("white");
        terminal.WriteLine(" Hotwifing/Hothusbanding (your partner with others while you watch/know)");
        terminal.SetColor("bright_yellow");
        terminal.Write("  [2]");
        terminal.SetColor("white");
        terminal.WriteLine(" Cuckolding (a specific power dynamic version)");
        terminal.SetColor("bright_yellow");
        terminal.Write("  [3]");
        terminal.SetColor("white");
        terminal.WriteLine(" Stag/Vixen (you enjoy sharing your partner)");
        terminal.SetColor("bright_yellow");
        terminal.Write("  [0]");
        terminal.SetColor("gray");
        terminal.WriteLine(" Never mind");
        terminal.WriteLine();

        var input = await terminal.GetInput("Choice: ");
        if (!int.TryParse(input, out int choice) || choice < 1)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("You decide not to pursue this conversation.");
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
        terminal.WriteLine("\"I've been thinking about something...\"");
        terminal.WriteLine($"\"The idea of you being with someone else, with my blessing...\"");
        terminal.WriteLine("\"It's called hotwifing. Or hothusbanding. Depending on the dynamic.\"");
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
            terminal.WriteLine($"{spouse.Name} is quiet for a moment, processing.");
            terminal.WriteLine("\"You're saying... you'd want me to be with others?\"");
            terminal.WriteLine("\"And you'd... enjoy that? Knowing about it?\"");
            terminal.WriteLine();

            await Task.Delay(1500);

            terminal.WriteLine("A slow smile crosses their face.");
            terminal.WriteLine("\"I never thought I'd hear you say that.\"");
            terminal.WriteLine("\"I... I think I could enjoy that. With the right person.\"");
            terminal.WriteLine();

            terminal.SetColor("bright_green");
            terminal.WriteLine($"{spouse.Name} agrees to try hotwifing/hothusbanding!");

            // Set up arrangement tracking
            spouseData.AcceptsPolyamory = true;
            spouseData.KnowsAboutOthers = true;
            RomanceTracker.Instance.AgreedStructures[spouse.ID] = RelationshipStructure.OpenRelationship;

            await Task.Delay(1500);

            // Offer to try it now
            terminal.WriteLine();
            terminal.SetColor("white");
            terminal.WriteLine("Do you want them to try it tonight?");
            terminal.WriteLine();
            terminal.SetColor("bright_yellow");
            terminal.Write("  [Y]");
            terminal.SetColor("white");
            terminal.WriteLine(" Yes, let's try it");
            terminal.SetColor("bright_yellow");
            terminal.Write("  [N]");
            terminal.SetColor("white");
            terminal.WriteLine(" No, maybe another time");
            terminal.WriteLine();

            var input = await terminal.GetInput("Choice: ");
            if (input.Trim().ToUpperInvariant() == "Y")
            {
                await PlayHotwifingScene(spouse, spouseData);
            }
        }
        else if (adventurousness > 0.4f)
        {
            terminal.WriteLine($"{spouse.Name} looks genuinely surprised.");
            terminal.WriteLine("\"That's... a lot to take in.\"");
            terminal.WriteLine("\"I'm not saying no, but I need time to think about it.\"");
            terminal.WriteLine();

            terminal.SetColor("yellow");
            terminal.WriteLine("They need time to consider this.");
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine($"{spouse.Name}'s face flushes.");
            terminal.WriteLine("\"What? You want me to be with other people?\"");
            terminal.WriteLine("\"That's not something I'd ever be comfortable with.\"");
            terminal.WriteLine();

            terminal.SetColor("yellow");
            terminal.WriteLine("The suggestion has upset them.");

            // Significant damage - hotwifing is a major ask
            await HandleSensitiveTopicRejection(spouse, spouseData, 6, 0.06f, "hotwifing");
        }
    }

    private async Task PlayHotwifingScene(NPC spouse, Spouse spouseData)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("═══════════════════════════════════════");
        terminal.WriteLine("          A NIGHT TO REMEMBER");
        terminal.WriteLine("═══════════════════════════════════════");
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
            terminal.WriteLine("Unfortunately, there's no one suitable available tonight.");
            terminal.WriteLine($"{spouse.Name} gives you a playful shrug. \"Another time, perhaps.\"");
            return;
        }

        // Select a random one
        var random = new Random();
        var thirdParty = potentialDates[random.Next(potentialDates.Count)];
        string thirdName = thirdParty.Name;
        string spouseGender = spouse.Sex == CharacterSex.Female ? "she" : "he";
        string spousePossessive = spouse.Sex == CharacterSex.Female ? "her" : "his";
        string thirdGender = thirdParty.Sex == CharacterSex.Female ? "she" : "he";

        terminal.SetColor("white");
        terminal.WriteLine($"{spouse.Name} gets ready for the evening, choosing {spousePossessive} most alluring outfit.");
        terminal.WriteLine($"You watch with a mix of excitement and nervousness as {spouseGender} prepares.");
        terminal.WriteLine();

        await Task.Delay(2000);

        terminal.SetColor("cyan");
        terminal.WriteLine($"\"{thirdName} asked me out last week,\" {spouseGender} admits.");
        terminal.WriteLine($"\"I told {(thirdParty.Sex == CharacterSex.Female ? "her" : "him")} I was married, but now...\"");
        terminal.WriteLine($"{spouseGender.ToUpperInvariant()[0]}{spouseGender.Substring(1)} smiles mischievously. \"Now I have your permission.\"");
        terminal.WriteLine();

        await Task.Delay(2000);

        terminal.SetColor("gray");
        terminal.WriteLine("════════════════════════════════════════");
        terminal.WriteLine($"  {spouse.Name} leaves for {spousePossessive} date...");
        terminal.WriteLine("════════════════════════════════════════");
        terminal.WriteLine();

        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine("The hours pass slowly. You imagine what might be happening...");
        terminal.WriteLine("The anticipation is almost unbearable.");
        terminal.WriteLine();

        await Task.Delay(2000);

        // The date scene (described, not shown)
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("════════════════════════════════════════");
        terminal.WriteLine("  LATER THAT NIGHT...");
        terminal.WriteLine("════════════════════════════════════════");
        terminal.WriteLine();

        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine($"The door opens. {spouse.Name} returns, flushed and breathless.");
        terminal.WriteLine($"{spouseGender.ToUpperInvariant()[0]}{spouseGender.Substring(1)} looks at you with smoldering eyes.");
        terminal.WriteLine();

        await Task.Delay(1500);

        terminal.SetColor("cyan");
        terminal.WriteLine($"\"{thirdName} was... very attentive,\" {spouseGender} whispers, coming closer.");
        terminal.WriteLine($"\"We had dinner, then drinks, and then...\"");
        terminal.WriteLine();

        await Task.Delay(2000);

        // Spouse describes the encounter
        terminal.SetColor("bright_magenta");
        terminal.WriteLine($"{spouseGender.ToUpperInvariant()[0]}{spouseGender.Substring(1)} tells you everything.");
        terminal.WriteLine($"How {thirdName}'s hands felt. The first kiss. The passion.");
        terminal.WriteLine($"Every detail, whispered in your ear as {spouseGender} presses against you.");
        terminal.WriteLine();

        await Task.Delay(2500);

        terminal.SetColor("white");
        terminal.WriteLine($"\"But I came home to you,\" {spouseGender} breathes.");
        terminal.WriteLine($"\"I always come home to you.\"");
        terminal.WriteLine();

        await Task.Delay(1500);

        // The reclamation
        terminal.SetColor("bright_red");
        terminal.WriteLine("════════════════════════════════════════");
        terminal.WriteLine("          RECLAMATION");
        terminal.WriteLine("════════════════════════════════════════");
        terminal.WriteLine();

        await Task.Delay(1000);

        terminal.SetColor("white");
        terminal.WriteLine("The fire between you ignites like never before.");
        terminal.WriteLine($"Every touch is electric, possessive, passionate.");
        terminal.WriteLine($"You claim what's yours, and {spouseGender} surrenders completely.");
        terminal.WriteLine();

        await Task.Delay(2000);

        terminal.SetColor("bright_magenta");
        terminal.WriteLine("The night that follows is unlike any other.");
        terminal.WriteLine($"The stories {spouseGender} tells only fuel the passion.");
        terminal.WriteLine("By morning, you're both exhausted... and closer than ever.");
        terminal.WriteLine();

        await Task.Delay(1500);

        // Record the encounter and set up arrangement
        RomanceTracker.Instance.SetupCuckoldArrangement(spouse.ID, thirdParty.ID, true);

        // Relationship boost
        spouseData.LoveLevel = Math.Max(1, spouseData.LoveLevel - 3);

        terminal.SetColor("bright_green");
        terminal.WriteLine("Your bond with each other has deepened through this shared experience.");
        terminal.WriteLine();

        await terminal.GetInput("Press Enter to continue...");
    }

    private async Task DiscussCuckolding(NPC spouse, Spouse spouseData)
    {
        terminal.WriteLine();
        terminal.SetColor("white");
        terminal.WriteLine("\"This is difficult to explain, but I want to be honest with you...\"");
        terminal.WriteLine("\"There's a dynamic called cuckolding. It involves power exchange.\"");
        terminal.WriteLine("\"You would be with others, and I would... submit to that knowledge.\"");
        terminal.WriteLine("\"Here. In our home. While I watch.\"");
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
            terminal.WriteLine($"{spouse.Name} studies your face intently.");
            terminal.WriteLine("\"So... you're saying you want me to be dominant?\"");
            terminal.WriteLine("\"To take other lovers while you... watch? In our own home?\"");
            terminal.WriteLine();

            await Task.Delay(1500);

            terminal.WriteLine("Something shifts in their demeanor.");
            terminal.WriteLine("\"I have to admit... the idea of having that power is intriguing.\"");
            terminal.WriteLine("\"If that's what you truly want...\"");
            terminal.WriteLine();

            terminal.SetColor("bright_green");
            terminal.WriteLine($"{spouse.Name} agrees to explore cuckolding!");

            // Set up cuckold arrangement
            spouseData.AcceptsPolyamory = true;
            RomanceTracker.Instance.AgreedStructures[spouse.ID] = RelationshipStructure.OpenRelationship;

            await Task.Delay(1500);

            // Offer to try it now
            terminal.WriteLine();
            terminal.SetColor("white");
            terminal.WriteLine("Do you want to try it tonight?");
            terminal.WriteLine();
            terminal.SetColor("bright_yellow");
            terminal.Write("  [Y]");
            terminal.SetColor("white");
            terminal.WriteLine(" Yes, let's try it");
            terminal.SetColor("bright_yellow");
            terminal.Write("  [N]");
            terminal.SetColor("white");
            terminal.WriteLine(" No, maybe another time");
            terminal.WriteLine();

            var input = await terminal.GetInput("Choice: ");
            if (input.Trim().ToUpperInvariant() == "Y")
            {
                await PlayCuckoldingScene(spouse, spouseData);
            }
        }
        else if (adventurousness > 0.4f)
        {
            terminal.WriteLine($"{spouse.Name} looks confused.");
            terminal.WriteLine("\"I... I'm not sure I understand what you're asking.\"");
            terminal.WriteLine("\"This seems very complicated. Are you sure this is healthy?\"");
            terminal.WriteLine();

            terminal.SetColor("yellow");
            terminal.WriteLine("They don't understand or connect with this dynamic.");

            // Moderate damage for confusion
            await HandleSensitiveTopicRejection(spouse, spouseData, 4, 0.03f, "cuckolding");
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine($"{spouse.Name} looks disturbed.");
            terminal.WriteLine("\"Why would you want that? It sounds like punishment.\"");
            terminal.WriteLine("\"I don't want that kind of relationship at all.\"");
            terminal.WriteLine();

            terminal.SetColor("yellow");
            terminal.WriteLine("This has seriously upset them.");

            // Severe damage - cuckolding request can be very disturbing to some
            await HandleSensitiveTopicRejection(spouse, spouseData, 10, 0.12f, "cuckolding");
        }
    }

    private async Task PlayCuckoldingScene(NPC spouse, Spouse spouseData)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("═══════════════════════════════════════");
        terminal.WriteLine("          THE ARRANGEMENT");
        terminal.WriteLine("═══════════════════════════════════════");
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
            terminal.WriteLine("Unfortunately, there's no one suitable available tonight.");
            terminal.WriteLine($"{spouse.Name} gives you a knowing look. \"We'll find someone.\"");
            return;
        }

        // Select a random one
        var random = new Random();
        var thirdParty = potentialLovers[random.Next(potentialLovers.Count)];
        string thirdName = thirdParty.Name;
        string spouseGender = spouse.Sex == CharacterSex.Female ? "she" : "he";
        string spousePossessive = spouse.Sex == CharacterSex.Female ? "her" : "his";
        string thirdGender = thirdParty.Sex == CharacterSex.Female ? "she" : "he";
        string thirdPossessive = thirdParty.Sex == CharacterSex.Female ? "her" : "his";

        terminal.SetColor("white");
        terminal.WriteLine($"{spouse.Name} sends a message to {thirdName}.");
        terminal.WriteLine($"\"Come over tonight. My spouse... wants to watch.\"");
        terminal.WriteLine();

        await Task.Delay(2000);

        terminal.SetColor("gray");
        terminal.WriteLine("════════════════════════════════════════");
        terminal.WriteLine("  An hour later, there's a knock at the door...");
        terminal.WriteLine("════════════════════════════════════════");
        terminal.WriteLine();

        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine($"{thirdName} enters, looking uncertain at first.");
        terminal.WriteLine($"{spouse.Name} takes {thirdPossessive} hand confidently.");
        terminal.WriteLine($"\"Don't worry about {(currentPlayer?.Sex == CharacterSex.Female ? "her" : "him")},\" {spouseGender} says.");
        terminal.WriteLine($"\"{(currentPlayer?.Sex == CharacterSex.Female ? "She" : "He")} wants this. Don't you?\"");
        terminal.WriteLine();

        await Task.Delay(2000);

        terminal.SetColor("cyan");
        terminal.WriteLine($"{spouse.Name} looks at you with a new intensity in {spousePossessive} eyes.");
        terminal.WriteLine($"\"Sit there,\" {spouseGender} commands, pointing to the chair in the corner.");
        terminal.WriteLine($"\"And watch.\"");
        terminal.WriteLine();

        await Task.Delay(2000);

        terminal.SetColor("bright_magenta");
        terminal.WriteLine("════════════════════════════════════════");
        terminal.WriteLine("  You take your place...");
        terminal.WriteLine("════════════════════════════════════════");
        terminal.WriteLine();

        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine($"{spouse.Name} and {thirdName} move toward each other.");
        terminal.WriteLine($"The first kiss is tentative, then grows more passionate.");
        terminal.WriteLine($"You watch from your chair, heart racing.");
        terminal.WriteLine();

        await Task.Delay(2000);

        terminal.SetColor("bright_magenta");
        terminal.WriteLine($"{spouse.Name} glances at you occasionally, making sure you're watching.");
        terminal.WriteLine($"There's power in {spousePossessive} gaze. Control. Dominance.");
        terminal.WriteLine($"This is what you asked for, and {spouseGender} owns it completely.");
        terminal.WriteLine();

        await Task.Delay(2500);

        // The scene progresses
        terminal.SetColor("cyan");
        terminal.WriteLine("Clothing falls away. Bodies intertwine.");
        terminal.WriteLine($"{spouse.Name} is in complete control, directing every moment.");
        terminal.WriteLine($"Sometimes {spouseGender} looks at you. Sometimes {spouseGender} ignores you entirely.");
        terminal.WriteLine("Both feel equally intense.");
        terminal.WriteLine();

        await Task.Delay(2500);

        terminal.SetColor("white");
        terminal.WriteLine("The sounds. The movements. The way they move together.");
        terminal.WriteLine($"You sit there, exactly where {spouse.Name} told you to.");
        terminal.WriteLine("Watching everything unfold in your own bedroom.");
        terminal.WriteLine();

        await Task.Delay(2000);

        terminal.SetColor("bright_magenta");
        terminal.WriteLine("════════════════════════════════════════");
        terminal.WriteLine("  LATER...");
        terminal.WriteLine("════════════════════════════════════════");
        terminal.WriteLine();

        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine($"{thirdName} gathers {thirdPossessive} things and leaves.");
        terminal.WriteLine($"A knowing nod to you on the way out.");
        terminal.WriteLine();

        await Task.Delay(1500);

        terminal.SetColor("cyan");
        terminal.WriteLine($"{spouse.Name} lies back on the bed, satisfied, powerful.");
        terminal.WriteLine($"\"{spouseGender.ToUpperInvariant()[0]}{spouseGender.Substring(1)} beckons you over.\"");
        terminal.WriteLine($"\"You may approach now.\"");
        terminal.WriteLine();

        await Task.Delay(2000);

        terminal.SetColor("white");
        terminal.WriteLine("The dynamic has shifted between you forever.");
        terminal.WriteLine($"{spouse.Name} has discovered something in {spousePossessive}self.");
        terminal.WriteLine("And you've given them that power willingly.");
        terminal.WriteLine();

        await Task.Delay(1500);

        // Record the encounter
        RomanceTracker.Instance.SetupCuckoldArrangement(spouse.ID, thirdParty.ID, true);

        // This is a complex dynamic - relationship may strengthen or become more complicated
        spouseData.LoveLevel = Math.Max(1, spouseData.LoveLevel - 1);

        terminal.SetColor("bright_green");
        terminal.WriteLine("A new chapter in your relationship has begun.");
        terminal.WriteLine();

        await terminal.GetInput("Press Enter to continue...");
    }

    private async Task DiscussStagVixen(NPC spouse, Spouse spouseData)
    {
        terminal.WriteLine();
        terminal.SetColor("white");
        terminal.WriteLine("\"I want to share something with you...\"");
        terminal.WriteLine("\"The idea of you being with someone else, while I watch or participate...\"");
        terminal.WriteLine("\"Not out of submission, but pride. Stag and Vixen, they call it.\"");
        terminal.WriteLine("\"I want to show you off. Share you. Celebrate you.\"");
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
            terminal.WriteLine($"{spouse.Name}'s breath catches.");
            terminal.WriteLine("\"You want to... watch me? Show me off?\"");
            terminal.WriteLine("\"That's... actually kind of hot.\"");
            terminal.WriteLine();

            await Task.Delay(1500);

            terminal.WriteLine("A mischievous smile forms on their lips.");
            terminal.WriteLine("\"I like being admired. And the idea of you being proud...\"");
            terminal.WriteLine("\"Yes. I think I'd like to try that.\"");
            terminal.WriteLine();

            terminal.SetColor("bright_green");
            terminal.WriteLine($"{spouse.Name} agrees to explore the Stag/Vixen dynamic!");

            spouseData.AcceptsPolyamory = true;
            spouseData.KnowsAboutOthers = true;
            RomanceTracker.Instance.AgreedStructures[spouse.ID] = RelationshipStructure.OpenRelationship;
        }
        else if (adventurousness > 0.4f)
        {
            terminal.WriteLine($"{spouse.Name} considers this thoughtfully.");
            terminal.WriteLine("\"It's flattering that you see me that way...\"");
            terminal.WriteLine("\"But I'm not sure I'm comfortable being... shared.\"");
            terminal.WriteLine();

            terminal.SetColor("yellow");
            terminal.WriteLine("They're flattered but not ready.");

            // Light damage - they took it well
            await HandleSensitiveTopicRejection(spouse, spouseData, 2, 0.01f, "sharing");
        }
        else
        {
            terminal.WriteLine($"{spouse.Name} shakes their head.");
            terminal.WriteLine("\"I don't want to be with anyone else.\"");
            terminal.WriteLine("\"You're all I need. Why isn't that enough?\"");
            terminal.WriteLine();

            terminal.SetColor("gray");
            terminal.WriteLine("They prefer monogamy.");

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
        var random = new Random();
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

            terminal.SetColor("red");
            terminal.WriteLine("═══════════════════════════════════════");
            terminal.WriteLine("          A TERRIBLE SILENCE");
            terminal.WriteLine("═══════════════════════════════════════");
            terminal.WriteLine();

            await Task.Delay(1500);

            string spouseGender = spouse.Sex == CharacterSex.Female ? "she" : "he";
            string spousePossessive = spouse.Sex == CharacterSex.Female ? "her" : "his";

            terminal.SetColor("white");
            terminal.WriteLine($"{spouse.Name} is quiet for a very long time.");
            terminal.WriteLine($"When {spouseGender} finally speaks, {spousePossessive} voice is cold.");
            terminal.WriteLine();

            await Task.Delay(2000);

            terminal.SetColor("bright_red");
            terminal.WriteLine($"\"I've been trying to make this work. I really have.\"");
            terminal.WriteLine($"\"But this... asking me about {topicName}...\"");
            terminal.WriteLine($"\"It makes me realize we want very different things.\"");
            terminal.WriteLine();

            await Task.Delay(2000);

            terminal.SetColor("red");
            terminal.WriteLine($"\"I want a divorce.\"");
            terminal.WriteLine();

            await Task.Delay(1500);

            terminal.SetColor("yellow");
            terminal.WriteLine("Your spouse has asked for a divorce.");
            terminal.WriteLine();
            terminal.SetColor("bright_yellow");
            terminal.Write("  [A]");
            terminal.SetColor("yellow");
            terminal.WriteLine(" Accept the divorce");
            terminal.SetColor("bright_yellow");
            terminal.Write("  [P]");
            terminal.SetColor("yellow");
            terminal.WriteLine(" Plead with them to reconsider");
            terminal.WriteLine();

            var input = await terminal.GetInput("Choice: ");

            if (input.Trim().ToUpperInvariant() == "P")
            {
                // Pleading - small chance to save marriage
                float pleadSuccess = 0.3f - (spouseData.LoveLevel / 300f); // Harder if relationship worse
                if (jealousy > 0.6f) pleadSuccess -= 0.1f;

                await Task.Delay(1000);

                terminal.SetColor("white");
                terminal.WriteLine();
                terminal.WriteLine("\"Please... I'm sorry. I didn't mean to hurt you.\"");
                terminal.WriteLine("\"I love you. Can we please work through this?\"");
                terminal.WriteLine();

                await Task.Delay(2000);

                if (random.NextDouble() < pleadSuccess)
                {
                    terminal.SetColor("bright_cyan");
                    terminal.WriteLine($"{spouse.Name}'s expression softens slightly.");
                    terminal.WriteLine($"\"I... I don't know. Maybe we can try counseling.\"");
                    terminal.WriteLine($"\"But if you ever bring up something like that again...\"");
                    terminal.WriteLine();

                    terminal.SetColor("yellow");
                    terminal.WriteLine("Your marriage has been saved... barely.");
                    terminal.WriteLine("But the damage will take time to heal.");

                    // Severe relationship damage but no divorce
                    spouseData.LoveLevel = Math.Min(100, spouseData.LoveLevel + 10);
                }
                else
                {
                    terminal.SetColor("red");
                    terminal.WriteLine($"{spouse.Name} shakes {spousePossessive} head.");
                    terminal.WriteLine($"\"No. I've made up my mind. This is over.\"");
                    terminal.WriteLine();

                    await ProcessSpouseDivorce(spouse, spouseData);
                }
            }
            else
            {
                // Accept divorce
                terminal.SetColor("gray");
                terminal.WriteLine();
                terminal.WriteLine("You accept their decision in silence.");
                terminal.WriteLine();

                await ProcessSpouseDivorce(spouse, spouseData);
            }
        }
        else if (relationshipStrained)
        {
            // Relationship is strained but no divorce... yet
            terminal.WriteLine();
            terminal.SetColor("yellow");
            terminal.WriteLine("You sense your relationship is becoming strained.");
            terminal.WriteLine("Perhaps it's best to be more careful with sensitive topics.");
        }
    }

    /// <summary>
    /// Process a spouse-initiated divorce
    /// </summary>
    private async Task ProcessSpouseDivorce(NPC spouse, Spouse spouseData)
    {
        terminal.SetColor("red");
        terminal.WriteLine("═══════════════════════════════════════");
        terminal.WriteLine("         YOUR MARRIAGE HAS ENDED");
        terminal.WriteLine("═══════════════════════════════════════");
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
            terminal.WriteLine($"{spouse.Name} has taken custody of the children.");
            terminal.WriteLine();
        }

        terminal.SetColor("gray");
        terminal.WriteLine($"{spouse.Name} packs their belongings and leaves.");
        terminal.WriteLine("The door closes with a terrible finality.");

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
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("═══════════════════════════════════════");
        terminal.WriteLine("         THE MASTER BEDROOM");
        terminal.WriteLine("═══════════════════════════════════════");
        terminal.WriteLine();

        if (romance.Spouses.Count == 0 && romance.CurrentLovers.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("Your bed seems cold and empty...");
            terminal.WriteLine("Perhaps you should find someone special to share it with.");
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
            terminal.WriteLine("Your partner isn't home right now.");
            terminal.SetColor("gray");
            terminal.WriteLine("They might be at Main Street or elsewhere in town.");
            await terminal.WaitForKey();
            return;
        }

        if (availablePartners.Count == 1)
        {
            var partner = availablePartners[0];
            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"{partner.Name} is here, looking inviting...");
            terminal.WriteLine();
            terminal.SetColor("bright_yellow");
            terminal.Write("  [1]");
            terminal.SetColor("white");
            terminal.WriteLine($" Join {partner.Name} in bed");
            terminal.SetColor("bright_yellow");
            terminal.Write("  [0]");
            terminal.SetColor("white");
            terminal.WriteLine(" Leave the bedroom");

            var input = await terminal.GetInput("Choice: ");
            if (input == "1")
            {
                await IntimacySystem.Instance.InitiateIntimateScene(currentPlayer, partner, terminal);
            }
            else
            {
                terminal.WriteLine("You quietly leave the bedroom.", "gray");
                await terminal.WaitForKey();
            }
        }
        else
        {
            // Multiple partners available
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("Multiple partners are here waiting for you...");
            terminal.WriteLine();

            for (int i = 0; i < availablePartners.Count; i++)
            {
                terminal.SetColor("white");
                terminal.WriteLine($"  [{i + 1}] {availablePartners[i].Name}");
            }
            terminal.SetColor("bright_yellow");
            terminal.Write("  [0]");
            terminal.SetColor("gray");
            terminal.WriteLine(" Leave the bedroom");

            var input = await terminal.GetInput("Choice: ");
            if (int.TryParse(input, out int choice) && choice >= 1 && choice <= availablePartners.Count)
            {
                await IntimacySystem.Instance.InitiateIntimateScene(currentPlayer, availablePartners[choice - 1], terminal);
            }
            else
            {
                terminal.WriteLine("You quietly leave the bedroom.", "gray");
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
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        { const string t = "MASTER CRAFTSMAN'S RENOVATIONS"; int l = (62 - t.Length) / 2, r = 62 - t.Length - l; terminal.WriteLine($"║{new string(' ', l)}{t}{new string(' ', r)}║"); }
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        terminal.WriteLine();

        terminal.SetColor("gray");
        terminal.WriteLine($"  Your gold: {currentPlayer.Gold:N0}");
        terminal.WriteLine();

        // Tiered upgrades
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("--- Room Upgrades ---");
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
        terminal.WriteLine("--- Special Purchases ---");

        // Trophy Room
        long trophyRoomCost = 500_000;
        ShowOneTimePurchase(opt++, "Trophy Room", currentPlayer.HasTrophyRoom, trophyRoomCost, "Display achievements & bosses");
        // Study / Library
        long studyCost = 750_000;
        ShowOneTimePurchase(opt++, "Study / Library", currentPlayer.HasStudy, studyCost, "+5% XP from combat");
        // Servants' Quarters
        long servantsCost = 500_000;
        ShowOneTimePurchase(opt++, "Servants' Quarters", currentPlayer.HasServants, servantsCost, $"Daily gold income ({GameConfig.ServantsDailyGoldBase}+lvl*{GameConfig.ServantsDailyGoldPerLevel})");
        // Legendary Armory
        long armoryCost = 2_500_000;
        ShowOneTimePurchase(opt++, "Legendary Armory", currentPlayer.HasLegendaryArmory, armoryCost, "+5% damage & defense permanently");
        // Fountain of Vitality
        long fountainCost = 5_000_000;
        ShowOneTimePurchase(opt++, "Fountain of Vitality", currentPlayer.HasVitalityFountain, fountainCost, "+10% max HP permanently");

        terminal.WriteLine();
        terminal.SetColor("bright_yellow");
        terminal.Write("[0]");
        terminal.SetColor("white");
        terminal.WriteLine(" Return");
        terminal.WriteLine();

        var input = await terminal.GetInput("Select upgrade: ");
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
                        terminal.WriteLine($"Upgraded to {LivingQuartersNames[lvl]}!");
                        terminal.WriteLine($"Rest now recovers {(int)(GameConfig.HomeRecoveryPercent[lvl] * 100)}% HP/Mana, {GameConfig.HomeRestsPerDay[lvl]}x per day.");
                    });
                break;
            case 2:
                await PurchaseUpgrade("Bed", GetBedCost(currentPlayer.BedLevel),
                    currentPlayer.BedLevel < 5, () => {
                        currentPlayer.BedLevel++;
                        int lvl = Math.Clamp(currentPlayer.BedLevel, 0, 5);
                        terminal.SetColor("cyan");
                        terminal.WriteLine($"Upgraded to {BedNames[lvl]}!");
                        float mod = GameConfig.BedFertilityModifier[lvl];
                        terminal.WriteLine(mod <= 0 ? "Fertility penalty removed!" : $"Fertility bonus: +{(int)(mod * 100)}%");
                    });
                break;
            case 3:
                await PurchaseUpgrade("Storage Chest", GetChestUpgradeCost(currentPlayer.ChestLevel),
                    currentPlayer.ChestLevel < 5, () => {
                        currentPlayer.ChestLevel++;
                        int lvl = Math.Clamp(currentPlayer.ChestLevel, 0, 5);
                        terminal.SetColor("cyan");
                        terminal.WriteLine($"Upgraded to {ChestNames[lvl]}!");
                        terminal.WriteLine($"Chest now holds up to {GameConfig.ChestCapacity[lvl]} items.");
                    });
                break;
            case 4:
                await PurchaseUpgrade("Hearth", GetHearthCost(currentPlayer.HearthLevel),
                    currentPlayer.HearthLevel < 5, () => {
                        currentPlayer.HearthLevel++;
                        int lvl = Math.Clamp(currentPlayer.HearthLevel, 0, 5);
                        terminal.SetColor("cyan");
                        terminal.WriteLine($"Upgraded to {HearthNames[lvl]}!");
                        terminal.WriteLine($"Well-Rested buff: +{(int)(GameConfig.HearthDamageBonus[lvl] * 100)}% damage/defense for {GameConfig.HearthCombatDuration[lvl]} combats after resting.");
                    });
                break;
            case 5:
                await PurchaseUpgrade("Herb Garden", GetGardenCost(currentPlayer.GardenLevel),
                    currentPlayer.GardenLevel < 5, () => {
                        currentPlayer.GardenLevel++;
                        int lvl = Math.Clamp(currentPlayer.GardenLevel, 0, 5);
                        terminal.SetColor("cyan");
                        terminal.WriteLine($"Upgraded to {GardenNames[lvl]}!");
                        terminal.WriteLine($"Gather up to {GameConfig.HerbsPerDay[lvl]} herbs per day.");
                        if (lvl >= 1 && lvl <= 5)
                        {
                            var newHerb = (HerbType)lvl;
                            terminal.SetColor(HerbData.GetColor(newHerb));
                            terminal.WriteLine($"New herb unlocked: {HerbData.GetName(newHerb)} — {HerbData.GetDescription(newHerb)}");
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
                        terminal.WriteLine("A magnificent study lined with ancient tomes!");
                        terminal.WriteLine($"+{(int)(GameConfig.StudyXPBonus * 100)}% XP from all combat.");
                    });
                break;
            case 9:
                await PurchaseUpgrade("Servants' Quarters", servantsCost,
                    !currentPlayer.HasServants, () => {
                        currentPlayer.HasServants = true;
                        terminal.SetColor("cyan");
                        terminal.WriteLine("A loyal staff moves into the servants' quarters!");
                        terminal.WriteLine($"They'll collect {GameConfig.ServantsDailyGoldBase} + (your level * {GameConfig.ServantsDailyGoldPerLevel}) gold daily.");
                    });
                break;
            case 10:
                await PurchaseUpgrade("Legendary Armory", armoryCost,
                    !currentPlayer.HasLegendaryArmory, () => { currentPlayer.HasLegendaryArmory = true; ApplyArmoryBonus(); });
                break;
            case 11:
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

        if (maxed)
        {
            terminal.SetColor("bright_green");
            terminal.Write($"  [{num}] {name}");
            terminal.SetColor("bright_green");
            terminal.WriteLine($" MAXED - {currentTierName} (Lv {level}) [{currentBonus}]");
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
        if (owned)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine($"  [{num}] {name} - OWNED");
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
            terminal.WriteLine($"{name} is already at maximum level!");
            await terminal.WaitForKey();
            return;
        }

        if (currentPlayer.Gold < cost)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"You need {cost:N0} gold for {name}!");
            terminal.WriteLine($"You only have {currentPlayer.Gold:N0} gold.");
            await terminal.WaitForKey();
            return;
        }

        terminal.SetColor("yellow");
        terminal.WriteLine($"Purchase {name} for {cost:N0} gold?");
        var confirm = await terminal.GetInput("(Y/N): ");

        if (confirm.Trim().ToUpperInvariant() == "Y")
        {
            currentPlayer.Gold -= cost;
            currentPlayer.Statistics.RecordGoldSpent(cost);
            applyUpgrade();
            currentPlayer.RecalculateStats();

            terminal.SetColor("bright_green");
            terminal.WriteLine($"\n*** {name.ToUpper()} PURCHASED! ***");
            terminal.WriteLine("The craftsmen get to work immediately...");
            await Task.Delay(1500);
            terminal.WriteLine("Your home has been upgraded!");
        }
        else
        {
            terminal.WriteLine("Purchase cancelled.", "gray");
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
        terminal.WriteLine($"Training Room upgraded to level {currentPlayer.TrainingRoomLevel}!");
        terminal.WriteLine("+1 to ALL base stats!");
    }

    private void ApplyArmoryBonus()
    {
        currentPlayer.PermanentDamageBonus += 5;
        currentPlayer.PermanentDefenseBonus += 5;
        terminal.SetColor("cyan");
        terminal.WriteLine("Legendary Armory installed!");
        terminal.WriteLine("+5% damage and +5% defense permanently!");
    }

    private void ApplyFountainBonus()
    {
        long hpBonus = currentPlayer.MaxHP / 10;
        currentPlayer.BonusMaxHP += hpBonus;
        terminal.SetColor("cyan");
        terminal.WriteLine("Fountain of Vitality constructed!");
        terminal.WriteLine($"+{hpBonus} max HP permanently!");
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
            terminal.WriteLine("You don't have any lovers or partners.");
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
            terminal.WriteLine("All your allies members are alive!");
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine("Dead Team Members:");
        for (int i = 0; i < deadMembers.Count; i++)
        {
            var dead = deadMembers[i];
            long cost = dead.Level * 1000; // Resurrection cost
            terminal.SetColor("white");
            terminal.WriteLine($"{i + 1}. {dead.DisplayName} (Level {dead.Level}) - Cost: {cost:N0} gold");
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write("Enter number to resurrect (0 to cancel): ");
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (int.TryParse(input, out int choice) && choice >= 1 && choice <= deadMembers.Count)
        {
            var toResurrect = deadMembers[choice - 1];
            long cost = toResurrect.Level * 1000;

            if (currentPlayer.Gold < cost)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"You need {cost:N0} gold to resurrect {toResurrect.DisplayName}!");
            }
            else
            {
                currentPlayer.Gold -= cost;
                toResurrect.HP = toResurrect.MaxHP / 2; // Resurrect at half HP
toResurrect.IsDead = false;
                terminal.WriteLine("");
                terminal.SetColor("bright_green");
                terminal.WriteLine($"{toResurrect.DisplayName} has been resurrected!");
                terminal.WriteLine($"Cost: {cost:N0} gold");

                NewsSystem.Instance.Newsy(true, $"{toResurrect.DisplayName} was resurrected by their ally '{currentPlayer.Name}'!");
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine("Press Enter to continue...");
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
            terminal.WriteLine("You have no spouse or lover to equip.");
            terminal.WriteLine("Find love first, then gear them up for adventure!");
            await Task.Delay(2500);
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        { const string t = "EQUIP YOUR PARTNER"; int l = (78 - t.Length) / 2, r = 78 - t.Length - l; terminal.WriteLine($"║{new string(' ', l)}{t}{new string(' ', r)}║"); }
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        // List partners
        terminal.SetColor("white");
        terminal.WriteLine("Your Partners:");
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
            terminal.WriteLine($"Lv {npc.Level} {npc.Class}");
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write("Select partner to equip (0 to cancel): ");
        terminal.SetColor("white");

        var input = await terminal.ReadLineAsync();
        if (!int.TryParse(input, out int partnerIdx) || partnerIdx < 1 || partnerIdx > partners.Count)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("Cancelled.");
            await Task.Delay(1000);
            return;
        }

        var selectedPartner = partners[partnerIdx - 1].npc;
        await ManageCharacterEquipment(selectedPartner);

        // Auto-save after equipment changes to persist NPC equipment state
        await SaveSystem.Instance.AutoSave(currentPlayer);
    }

    /// <summary>
    /// Manage equipment for a specific character (spouse or lover)
    /// </summary>
    private async Task ManageCharacterEquipment(Character target)
    {
        while (true)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"═══════════════════════════════════════════════════════════════════════════════");
            terminal.WriteLine($"                    EQUIPMENT: {target.DisplayName.ToUpper()}");
            terminal.WriteLine($"═══════════════════════════════════════════════════════════════════════════════");
            terminal.WriteLine("");

            // Show target's stats
            terminal.SetColor("white");
            terminal.WriteLine($"  Level: {target.Level}  Class: {target.Class}  Race: {target.Race}");
            terminal.WriteLine($"  HP: {target.HP}/{target.MaxHP}  Mana: {target.Mana}/{target.MaxMana}");
            terminal.WriteLine($"  Str: {target.Strength}  Def: {target.Defence}  Agi: {target.Agility}");
            terminal.WriteLine("");

            // Show current equipment
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("Current Equipment:");
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
            terminal.WriteLine("Options:");
            terminal.SetColor("bright_yellow");
            terminal.Write("  [E]");
            terminal.SetColor("white");
            terminal.WriteLine(" Equip item from your inventory");
            terminal.SetColor("bright_yellow");
            terminal.Write("  [U]");
            terminal.SetColor("white");
            terminal.WriteLine(" Unequip item from them");
            terminal.SetColor("bright_yellow");
            terminal.Write("  [T]");
            terminal.SetColor("white");
            terminal.WriteLine(" Take all their equipment");
            terminal.SetColor("bright_yellow");
            terminal.Write("  [Q]");
            terminal.SetColor("white");
            terminal.WriteLine(" Done / Return");
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.Write("Choice: ");
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
        var item = target.GetEquipment(slot);
        terminal.SetColor("gray");
        terminal.Write($"  {label,-12}: ");
        if (item != null)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine(item.Name);
        }
        else
        {
            // Check if off-hand is empty because of a two-handed weapon
            if (slot == EquipmentSlot.OffHand)
            {
                var mainHand = target.GetEquipment(EquipmentSlot.MainHand);
                if (mainHand?.Handedness == WeaponHandedness.TwoHanded)
                {
                    terminal.SetColor("darkgray");
                    terminal.WriteLine("(using 2H weapon)");
                    return;
                }
            }
            terminal.SetColor("darkgray");
            terminal.WriteLine("(empty)");
        }
    }

    /// <summary>
    /// Equip an item from the player's inventory to a character
    /// </summary>
    private async Task EquipItemToCharacter(Character target)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine($"═══ EQUIP ITEM TO {target.DisplayName.ToUpper()} ═══");
        terminal.WriteLine("");

        // Collect equippable items from player's inventory and equipped items
        var equipmentItems = new List<(Equipment item, bool isEquipped, EquipmentSlot? fromSlot)>();

        // Add equippable items from player's inventory
        foreach (var invItem in currentPlayer.Inventory)
        {
            var equipment = ConvertInventoryItemToEquipment(invItem);
            if (equipment != null)
                equipmentItems.Add((equipment, false, (EquipmentSlot?)null));
        }

        // Add player's currently equipped items
        foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
        {
            if (slot == EquipmentSlot.None) continue;
            var equipped = currentPlayer.GetEquipment(slot);
            if (equipped != null)
            {
                equipmentItems.Add((equipped, true, slot));
            }
        }

        if (equipmentItems.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("You have no equipment to give.");
            await Task.Delay(2000);
            return;
        }

        // Display available items
        terminal.SetColor("white");
        terminal.WriteLine("Available equipment:");
        terminal.WriteLine("");

        for (int i = 0; i < equipmentItems.Count; i++)
        {
            var (item, isEquipped, fromSlot) = equipmentItems[i];
            terminal.SetColor("bright_yellow");
            terminal.Write($"  {i + 1}. ");
            terminal.SetColor("white");
            terminal.Write($"{item.Name} ");

            // Show item stats
            terminal.SetColor("gray");
            if (item.WeaponPower > 0)
                terminal.Write($"[Atk:{item.WeaponPower}] ");
            if (item.ArmorClass > 0)
                terminal.Write($"[AC:{item.ArmorClass}] ");
            if (item.ShieldBonus > 0)
                terminal.Write($"[Shield:{item.ShieldBonus}] ");

            // Show if currently equipped by player
            if (isEquipped)
            {
                terminal.SetColor("cyan");
                terminal.Write($"(your {fromSlot?.GetDisplayName()})");
            }

            // Check if target can use it
            if (!item.CanEquip(target, out string reason))
            {
                terminal.SetColor("red");
                terminal.Write($" [{reason}]");
            }

            terminal.WriteLine("");
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write("Select item (0 to cancel): ");
        terminal.SetColor("white");

        var input = await terminal.ReadLineAsync();
        if (!int.TryParse(input, out int itemIdx) || itemIdx < 1 || itemIdx > equipmentItems.Count)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("Cancelled.");
            await Task.Delay(1000);
            return;
        }

        var (selectedItem, wasEquipped, sourceSlot) = equipmentItems[itemIdx - 1];

        // Check if target can equip
        if (!selectedItem.CanEquip(target, out string equipReason))
        {
            terminal.SetColor("red");
            terminal.WriteLine($"{target.DisplayName} cannot use this item: {equipReason}");
            await Task.Delay(2000);
            return;
        }

        // For one-handed weapons, ask which hand
        EquipmentSlot? targetSlot = null;
        if (selectedItem.Handedness == WeaponHandedness.OneHanded &&
            (selectedItem.Slot == EquipmentSlot.MainHand || selectedItem.Slot == EquipmentSlot.OffHand))
        {
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.Write("Which hand? ");
            terminal.SetColor("bright_yellow");
            terminal.Write("[M]");
            terminal.SetColor("cyan");
            terminal.Write("ain hand or ");
            terminal.SetColor("bright_yellow");
            terminal.Write("[O]");
            terminal.SetColor("cyan");
            terminal.WriteLine("ff hand?");
            terminal.Write(": ");
            terminal.SetColor("white");
            var handChoice = (await terminal.ReadLineAsync()).ToUpper().Trim();
            if (handChoice.StartsWith("O"))
                targetSlot = EquipmentSlot.OffHand;
            else
                targetSlot = EquipmentSlot.MainHand;
        }

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
            terminal.WriteLine($"{target.DisplayName} equipped {selectedItem.Name}!");
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
            terminal.WriteLine($"Failed to equip: {message}");
        }

        await Task.Delay(2000);
    }

    /// <summary>
    /// Unequip an item from a character and add to player's inventory
    /// </summary>
    private async Task UnequipItemFromCharacter(Character target)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine($"═══ UNEQUIP FROM {target.DisplayName.ToUpper()} ═══");
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
            terminal.WriteLine($"{target.DisplayName} has no equipment to unequip.");
            await Task.Delay(2000);
            return;
        }

        terminal.SetColor("white");
        terminal.WriteLine("Equipped items:");
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
        terminal.Write("Select slot to unequip (0 to cancel): ");
        terminal.SetColor("white");

        var input = await terminal.ReadLineAsync();
        if (!int.TryParse(input, out int slotIdx) || slotIdx < 1 || slotIdx > equippedSlots.Count)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("Cancelled.");
            await Task.Delay(1000);
            return;
        }

        var (selectedSlot, selectedItem) = equippedSlots[slotIdx - 1];

        // Check if cursed
        if (selectedItem.IsCursed)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"The {selectedItem.Name} is cursed and cannot be removed!");
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
            terminal.WriteLine($"Took {unequipped.Name} from {target.DisplayName}.");
            terminal.SetColor("gray");
            terminal.WriteLine("Item added to your inventory.");
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine("Failed to unequip item.");
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
        terminal.WriteLine($"Take ALL equipment from {target.DisplayName}?");
        terminal.Write("This will leave them with nothing. Confirm (Y/N): ");
        terminal.SetColor("white");

        var confirm = await terminal.ReadLineAsync();
        if (!confirm.ToUpper().StartsWith("Y"))
        {
            terminal.SetColor("gray");
            terminal.WriteLine("Cancelled.");
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
            terminal.WriteLine($"Took {itemsTaken} item{(itemsTaken != 1 ? "s" : "")} from {target.DisplayName}.");
        }
        else
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"{target.DisplayName} had no equipment to take.");
        }

        if (cursedItems.Count > 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"Could not remove cursed items: {string.Join(", ", cursedItems)}");
        }

        await Task.Delay(2000);
    }

    /// <summary>
    /// Convert Equipment to legacy Item for inventory storage
    /// </summary>
    private ModelItem ConvertEquipmentToItem(Equipment equipment)
    {
        return new ModelItem
        {
            Name = equipment.Name,
            Type = SlotToObjType(equipment.Slot),
            Value = equipment.Value,
            Attack = equipment.WeaponPower,
            Armor = equipment.ArmorClass,
            Strength = equipment.StrengthBonus,
            Dexterity = equipment.DexterityBonus,
            HP = equipment.MaxHPBonus,
            Mana = equipment.MaxManaBonus,
            Defence = equipment.DefenceBonus,
            IsCursed = equipment.IsCursed,
            MinLevel = equipment.MinLevel,
            StrengthNeeded = equipment.StrengthRequired,
            RequiresGood = equipment.RequiresGood,
            RequiresEvil = equipment.RequiresEvil,
            ItemID = equipment.Id
        };
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
