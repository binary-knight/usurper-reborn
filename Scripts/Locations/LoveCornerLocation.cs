using UsurperRemake.Utils;
using UsurperRemake.Systems;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// Love Corner location based on Pascal LOVERS.PAS
/// Complete dating, marriage, divorce, and family management system
/// Maintains perfect Pascal compatibility with original mechanics
/// </summary>
public class LoveCornerLocation : BaseLocation
{
    // Note: terminal and currentPlayer are inherited from BaseLocation

    public LoveCornerLocation() : base((GameLocation)GameConfig.LoveCorner, GameConfig.DefaultLoveCornerName, "A cozy corner for romance and gossip.") { }

    public override async Task EnterLocation(Character player, TerminalEmulator term)
    {
        await base.EnterLocation(player, term);
        // terminal and currentPlayer are set by base.EnterLocation

        await ShowLocationDescription(player);
        await MainLoop(player);
    }

    private async Task MainLoop(Character player)
    {
        bool stayInLocation = true;
        while (stayInLocation)
        {
            ShowPrompt(player);
            string command = await terminal.GetInput("");
            stayInLocation = await HandleCommand(player, command);
        }
    }

    private async Task<bool> HandleCommand(Character player, string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return true;

        return command.ToUpper() switch
        {
            "A" => await HandleApproachSomebody(player),
            "C" => await HandleChildrenInRealm(player),
            "D" => await HandleDivorce(player),
            "E" => await HandleExamineChild(player),
            "V" => await HandleVisitGossipMonger(player),
            "M" => await HandleMarriedCouples(player),
            "P" => await HandlePersonalRelations(player),
            "G" => await HandleGiftShop(player),
            "S" => await HandleStatus(player),
            "L" => await HandleLoveHistory(player),
            "R" => false, // Return - exit location
            "?" => await ShowMenuAndReturnTrue(player),
            _ => true // Stay in location for invalid input
        };
    }

    private async Task ShowLocationDescription(Character player)
    {
        terminal.WriteLine();
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.you_enter", GameConfig.DefaultLoveCornerName), TerminalEmulator.ColorGreen);
        terminal.WriteLine();

        if (!player.Expert)
        {
            await ShowMenu(player);
        }
    }

    private async Task ShowMenu(Character player)
    {
        terminal.ClearScreen();
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.banner", GameConfig.DefaultLoveCornerName), TerminalEmulator.ColorYellow);
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.intro1"));
        terminal.WriteLine(Loc.Get("love_corner.intro2"));
        terminal.WriteLine(Loc.Get("love_corner.intro3"));
        terminal.WriteLine(Loc.Get("love_corner.intro4"));
        terminal.WriteLine();

        terminal.WriteLine(Loc.Get("love_corner.menu_a"));
        terminal.WriteLine(Loc.Get("love_corner.menu_d", GameConfig.DefaultGossipMongerName));
        terminal.WriteLine(Loc.Get("love_corner.menu_m"));
        terminal.WriteLine(Loc.Get("love_corner.menu_p"));
        terminal.WriteLine(Loc.Get("love_corner.menu_s"));
        terminal.WriteLine(Loc.Get("love_corner.menu_l"));
        terminal.WriteLine();

        await Task.CompletedTask;
    }

    private void ShowPrompt(Character player)
    {
        if (player.Expert)
        {
            terminal.Write(Loc.Get("love_corner.expert_prompt", GameConfig.DefaultLoveCornerName));
        }
        else
        {
            terminal.Write(Loc.Get("ui.your_choice"));
        }
    }

    private async Task<bool> HandleApproachSomebody(Character player)
    {
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.approach_header"), TerminalEmulator.ColorCyan);
        terminal.WriteLine();

        if (player.IntimacyActs < 1)
        {
            terminal.WriteLine(Loc.Get("ui.no_intimacy_acts"), TerminalEmulator.ColorRed);
            terminal.WriteLine(Loc.Get("love_corner.come_back_tomorrow"));
            await terminal.PressAnyKey();
            return true;
        }

        string targetName = await terminal.GetInput(Loc.Get("love_corner.enter_name"));

        if (string.IsNullOrWhiteSpace(targetName))
        {
            terminal.WriteLine(Loc.Get("love_corner.invalid_name"));
            await terminal.PressAnyKey();
            return true;
        }

        // In a full implementation, would search for the character
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.searching", targetName), TerminalEmulator.ColorYellow);

        // Simulate finding the character and show dating menu
        return await ShowDatingMenu(player, targetName);
    }

    private async Task<bool> ShowDatingMenu(Character player, string targetName)
    {
        while (true)
        {
            terminal.WriteLine();
            terminal.WriteLine(Loc.Get("love_corner.dating_with", targetName), TerminalEmulator.ColorCyan);
            terminal.WriteLine();
            terminal.WriteLine(Loc.Get("love_corner.dating_menu1"));
            terminal.WriteLine(Loc.Get("love_corner.dating_menu2"));
            terminal.WriteLine(Loc.Get("love_corner.dating_menu3"));
            terminal.WriteLine(Loc.Get("love_corner.dating_menu4"));
            terminal.WriteLine();

            string choice = await terminal.GetInput(Loc.Get("love_corner.choose_action"));

            switch (choice?.ToUpper())
            {
                case "K":
                    await HandleKiss(player, targetName);
                    return true;
                case "D":
                    await HandleDinner(player, targetName);
                    return true;
                case "H":
                    await HandleHoldHands(player, targetName);
                    return true;
                case "I":
                    await HandleIntimate(player, targetName);
                    return true;
                case "M":
                    await HandleMarry(player, targetName);
                    return true;
                case "C":
                    await HandleChangeFeelings(player, targetName);
                    return true;
                case "R":
                    return true; // Return to main menu
                default:
                    terminal.WriteLine(Loc.Get("love_corner.invalid_choice"));
                    break;
            }
        }
    }

    private async Task HandleKiss(Character player, string targetName)
    {
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.kiss_lean", targetName), TerminalEmulator.ColorMagenta);

        // Calculate experience (Pascal equivalent)
        long experience = player.Level * GameConfig.KissExperienceMultiplier;
        experience = Math.Max(experience, 100);

        player.Experience += experience;
        player.IntimacyActs--;

        terminal.WriteLine(Loc.Get("love_corner.kiss_result", experience));
        terminal.WriteLine(Loc.Get("love_corner.acts_left", player.IntimacyActs));

        // Random chance to improve relationship
        var random = Random.Shared;
        if (random.Next(2) == 0)
        {
            terminal.WriteLine(Loc.Get("love_corner.relationship_improved", targetName), TerminalEmulator.ColorGreen);
        }

        await terminal.PressAnyKey();
    }

    private async Task HandleDinner(Character player, string targetName)
    {
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.dinner_invite", targetName), TerminalEmulator.ColorYellow);

        long experience = player.Level * GameConfig.DinnerExperienceMultiplier;
        experience = Math.Max(experience, 150);

        player.Experience += experience;
        player.IntimacyActs--;

        terminal.WriteLine(Loc.Get("love_corner.dinner_result", experience));
        terminal.WriteLine(Loc.Get("love_corner.dinner_wine"));
        terminal.WriteLine(Loc.Get("love_corner.acts_left", player.IntimacyActs));

        await terminal.PressAnyKey();
    }

    private async Task HandleHoldHands(Character player, string targetName)
    {
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.hold_hands", targetName), TerminalEmulator.ColorCyan);

        long experience = player.Level * GameConfig.HandHoldingExperienceMultiplier;
        experience = Math.Max(experience, 100);

        player.Experience += experience;
        player.IntimacyActs--;

        terminal.WriteLine(Loc.Get("love_corner.hold_result", experience));
        terminal.WriteLine(Loc.Get("love_corner.hold_walk"));
        terminal.WriteLine(Loc.Get("love_corner.acts_left", player.IntimacyActs));

        await terminal.PressAnyKey();
    }

    private async Task HandleIntimate(Character player, string targetName)
    {
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.intimate_embrace", targetName), TerminalEmulator.ColorRed);

        long experience = player.Level * GameConfig.IntimateExperienceMultiplier;
        experience = Math.Max(experience, 200);

        player.Experience += experience;
        player.IntimacyActs--;

        terminal.WriteLine(Loc.Get("love_corner.intimate_result", experience));
        terminal.WriteLine(Loc.Get("love_corner.intimate_emotion"));
        terminal.WriteLine(Loc.Get("love_corner.acts_left", player.IntimacyActs));

        // Potential for pregnancy in full implementation
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.gods_smile"), TerminalEmulator.ColorYellow);

        await terminal.PressAnyKey();
    }

    private async Task HandleMarry(Character player, string targetName)
    {
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.marriage_header"), TerminalEmulator.ColorYellow);
        terminal.WriteLine("==================");
        terminal.WriteLine();

        // Try to find the target character (NPC)
        var targetNPC = NPCSpawnSystem.Instance?.ActiveNPCs?.Find(n =>
            n.Name2.Equals(targetName, StringComparison.OrdinalIgnoreCase) ||
            n.Name1.Equals(targetName, StringComparison.OrdinalIgnoreCase));

        long weddingCost = GameConfig.WeddingCostBase;
        if (player.Gold < weddingCost)
        {
            terminal.WriteLine(Loc.Get("love_corner.wedding_cost", weddingCost), TerminalEmulator.ColorRed);
            terminal.WriteLine(Loc.Get("love_corner.you_only_have", player.Gold));
            await terminal.PressAnyKey();
            return;
        }

        terminal.WriteLine(Loc.Get("love_corner.wedding_with", targetName));
        terminal.WriteLine(Loc.Get("love_corner.cost_label", weddingCost));
        terminal.WriteLine();

        string confirm = await terminal.GetInput(Loc.Get("love_corner.proceed_ceremony"));
        if (confirm?.ToUpper() != "Y")
        {
            terminal.WriteLine(Loc.Get("love_corner.wedding_cancelled"));
            await terminal.PressAnyKey();
            return;
        }

        // Pay wedding cost regardless of outcome
        player.Gold -= weddingCost;

        // Use RelationshipSystem.PerformMarriage for proper tracking if we have the target
        if (targetNPC != null)
        {
            if (RelationshipSystem.PerformMarriage(player, targetNPC, out string message))
            {
                terminal.WriteLine();
                terminal.WriteLine(Loc.Get("love_corner.wedding_ceremony"), TerminalEmulator.ColorYellow);
                terminal.WriteLine();
                terminal.WriteLine(message, TerminalEmulator.ColorGreen);
            }
            else
            {
                terminal.WriteLine();
                terminal.WriteLine(message, TerminalEmulator.ColorRed);
                // Refund on failure
                player.Gold += weddingCost;
            }
        }
        else
        {
            // Fallback for when target NPC not found (e.g., offline player or unknown name)
            // Manually set marriage flags for compatibility
            player.IsMarried = true;
            player.Married = true;
            player.SpouseName = targetName;
            player.MarriedTimes++;
            player.IntimacyActs--;

            // Try to register with RomanceTracker using the target name as ID
            // (no NPC object available, so RelationshipSystem can't be used)
            RomanceTracker.Instance?.AddSpouse(targetName);

            var ceremonyMessages = GameConfig.WeddingCeremonyMessages;
            string ceremonyMessage = ceremonyMessages[Random.Shared.Next(ceremonyMessages.Length)];

            terminal.WriteLine();
            terminal.WriteLine(Loc.Get("love_corner.wedding_ceremony"), TerminalEmulator.ColorYellow);
            terminal.WriteLine();
            terminal.WriteLine(Loc.Get("love_corner.now_married", player.Name, targetName), TerminalEmulator.ColorGreen);
            terminal.WriteLine(ceremonyMessage);
            terminal.WriteLine();
            terminal.WriteLine(Loc.Get("love_corner.congratulations"), TerminalEmulator.ColorCyan);
        }

        await terminal.PressAnyKey();
    }

    private async Task HandleChangeFeelings(Character player, string targetName)
    {
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.change_feelings"), TerminalEmulator.ColorCyan);
        terminal.WriteLine("====================");
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.feelings_menu1"));
        terminal.WriteLine(Loc.Get("love_corner.feelings_menu2"));
        terminal.WriteLine(Loc.Get("love_corner.feelings_menu3"));
        terminal.WriteLine(Loc.Get("love_corner.feelings_menu4"));
        terminal.WriteLine(Loc.Get("love_corner.feelings_menu5"));
        terminal.WriteLine();

        string feeling = await terminal.GetInput(Loc.Get("love_corner.how_feel"));

        int newRelation = feeling?.ToUpper() switch
        {
            "L" => GameConfig.RelationLove,
            "P" => GameConfig.RelationPassion,
            "F" => GameConfig.RelationFriendship,
            "T" => GameConfig.RelationTrust,
            "R" => GameConfig.RelationRespect,
            "N" => GameConfig.RelationNormal,
            "S" => GameConfig.RelationSuspicious,
            "A" => GameConfig.RelationAnger,
            "E" => GameConfig.RelationEnemy,
            "H" => GameConfig.RelationHate,
            _ => GameConfig.RelationNormal
        };

        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.feelings_set", targetName, GetRelationshipName(newRelation)));

        // Display appropriate reaction
        if (newRelation == GameConfig.RelationLove)
        {
            terminal.WriteLine(Loc.Get("love_corner.love_reaction"), TerminalEmulator.ColorMagenta);
        }
        else if (newRelation == GameConfig.RelationHate)
        {
            terminal.WriteLine(Loc.Get("love_corner.hate_reaction"), TerminalEmulator.ColorRed);
        }

        await terminal.PressAnyKey();
    }

    private async Task<bool> HandleDivorce(Character player)
    {
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.divorce_header"), TerminalEmulator.ColorRed);
        terminal.WriteLine("==================");
        terminal.WriteLine();

        if (!player.IsMarried)
        {
            terminal.WriteLine(Loc.Get("love_corner.not_married"), TerminalEmulator.ColorRed);
            terminal.WriteLine(Loc.Get("love_corner.find_spouse"));
            await terminal.PressAnyKey();
            return true;
        }

        terminal.WriteLine(Loc.Get("love_corner.married_to", player.SpouseName));
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.divorce_process"), TerminalEmulator.ColorYellow);
        terminal.WriteLine(Loc.Get("love_corner.divorce_custody"));
        terminal.WriteLine(Loc.Get("love_corner.divorce_hostile"));
        terminal.WriteLine(Loc.Get("love_corner.divorce_cost", GameConfig.DivorceCostBase));
        terminal.WriteLine();

        if (player.Gold < GameConfig.DivorceCostBase)
        {
            terminal.WriteLine(Loc.Get("love_corner.need_gold_divorce", GameConfig.DivorceCostBase), TerminalEmulator.ColorRed);
            terminal.WriteLine(Loc.Get("love_corner.you_only_have", player.Gold));
            await terminal.PressAnyKey();
            return true;
        }

        string confirm1 = await terminal.GetInput(Loc.Get("ui.confirm_divorce"));
        if (confirm1?.ToUpper() != "Y")
        {
            terminal.WriteLine(Loc.Get("love_corner.divorce_cancelled"));
            await terminal.PressAnyKey();
            return true;
        }

        string confirm2 = await terminal.GetInput(Loc.Get("love_corner.confirm_custody"));
        if (confirm2?.ToUpper() != "Y")
        {
            terminal.WriteLine(Loc.Get("love_corner.divorce_cancelled"));
            await terminal.PressAnyKey();
            return true;
        }

        // Process divorce
        player.Gold -= GameConfig.DivorceCostBase;
        string exSpouse = player.SpouseName;

        // Sync divorce across all tracking systems
        var exSpouseNPC = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n =>
            (n.Name2 ?? n.Name1)?.Equals(exSpouse, StringComparison.OrdinalIgnoreCase) == true ||
            n.Name1?.Equals(exSpouse, StringComparison.OrdinalIgnoreCase) == true);

        if (exSpouseNPC != null)
        {
            RelationshipSystem.ProcessDivorce(player, exSpouseNPC, out _);
            RomanceTracker.Instance?.Divorce(exSpouseNPC.ID, "Love Corner divorce", playerInitiated: true);

            // Clear spouse NPC's marriage state
            exSpouseNPC.Married = false;
            exSpouseNPC.IsMarried = false;
            exSpouseNPC.SpouseName = "";
        }

        player.IsMarried = false;
        player.Married = false;
        player.SpouseName = "";

        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.divorce_finalized"), TerminalEmulator.ColorRed);
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.good_riddance", exSpouse), TerminalEmulator.ColorYellow);
        terminal.WriteLine(Loc.Get("love_corner.lost_custody"));
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.single_life"), TerminalEmulator.ColorGreen);

        await terminal.PressAnyKey();
        return true;
    }

    private async Task<bool> HandleChildrenInRealm(Character player)
    {
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.children_header"), TerminalEmulator.ColorCyan);
        terminal.WriteLine("====================");
        terminal.WriteLine();

        // In full implementation, would list all children
        terminal.WriteLine(Loc.Get("love_corner.child_listing"));
        terminal.WriteLine(Loc.Get("love_corner.child_view"));
        terminal.WriteLine(Loc.Get("love_corner.child_orphans"));
        terminal.WriteLine(Loc.Get("love_corner.child_kidnapped"));
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.future_update"));

        await terminal.PressAnyKey();
        return true;
    }

    private async Task<bool> HandleExamineChild(Character player)
    {
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.your_children"), TerminalEmulator.ColorCyan);
        terminal.WriteLine("=============");
        terminal.WriteLine();

        var children = FamilySystem.Instance.GetChildrenOf(player);

        if (children.Count == 0)
        {
            terminal.WriteLine(Loc.Get("love_corner.no_children"));
            terminal.WriteLine();
            terminal.WriteLine(Loc.Get("love_corner.how_to_children1"));
            terminal.WriteLine(Loc.Get("love_corner.how_to_children2"), TerminalEmulator.ColorDarkGray);
            await terminal.PressAnyKey();
            return true;
        }

        terminal.WriteLine(Loc.Get("love_corner.child_count", children.Count, children.Count > 1 ? Loc.Get("love_corner.child_count_ren") : ""));
        terminal.WriteLine();

        foreach (var child in children)
        {
            terminal.WriteLine($"  {child.Name}", TerminalEmulator.ColorYellow);
            terminal.WriteLine(Loc.Get("love_corner.child_age", child.Age, child.Age != 1 ? "s" : ""));
            terminal.WriteLine(Loc.Get("love_corner.child_sex", child.Sex == CharacterSex.Male ? Loc.Get("love_corner.male") : Loc.Get("love_corner.female")));
            terminal.WriteLine(Loc.Get("love_corner.child_behavior", child.GetSoulDescription()));
            terminal.WriteLine(Loc.Get("love_corner.child_health", child.GetHealthDescription()));
            terminal.WriteLine(Loc.Get("love_corner.child_location", child.GetLocationDescription()));

            var marks = child.GetStatusMarks();
            if (!string.IsNullOrEmpty(marks))
            {
                terminal.WriteLine(Loc.Get("love_corner.child_status", marks), TerminalEmulator.ColorRed);
            }

            // Show other parent
            string otherParent = child.Mother == player.Name || child.MotherID == player.ID
                ? child.Father
                : child.Mother;
            if (!string.IsNullOrEmpty(otherParent))
            {
                terminal.WriteLine(Loc.Get("love_corner.child_other_parent", otherParent), TerminalEmulator.ColorDarkGray);
            }

            terminal.WriteLine();
        }

        // Check for any children approaching adulthood
        var teensCount = children.Count(c => c.Age >= 15 && c.Age < FamilySystem.ADULT_AGE);
        if (teensCount > 0)
        {
            terminal.WriteLine(Loc.Get("love_corner.teens_coming_of_age", teensCount), TerminalEmulator.ColorGreen);
            terminal.WriteLine(Loc.Get("love_corner.teens_independent"), TerminalEmulator.ColorDarkGray);
        }

        await terminal.PressAnyKey();
        return true;
    }

    private async Task<bool> HandleVisitGossipMonger(Character player)
    {
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.visiting_gossip", GameConfig.DefaultGossipMongerName), TerminalEmulator.ColorMagenta);
        terminal.WriteLine("=====================================");
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.gossip_welcome"));
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.services_available"));
        terminal.WriteLine(Loc.Get("love_corner.gossip_spy"));
        terminal.WriteLine(Loc.Get("love_corner.gossip_marriages"));
        terminal.WriteLine(Loc.Get("love_corner.gossip_divorces"));
        terminal.WriteLine(Loc.Get("love_corner.gossip_children"));
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.gossip_cost"));
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.future_update"));

        await terminal.PressAnyKey();
        return true;
    }

    private async Task<bool> HandleMarriedCouples(Character player)
    {
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.married_couples"), TerminalEmulator.ColorMagenta);
        terminal.WriteLine();

        // Get married couples from the relationship system
        var marriedCouples = RelationshipSystem.GetMarriedCouples();

        if (marriedCouples.Count == 0)
        {
            terminal.WriteLine(Loc.Get("love_corner.no_couples"));
        }
        else
        {
            foreach (var couple in marriedCouples)
            {
                terminal.WriteLine(couple);
            }
        }

        terminal.WriteLine();

        await terminal.PressAnyKey();
        return true;
    }

    private async Task<bool> HandlePersonalRelations(Character player)
    {
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.personal_relations", player.Name), TerminalEmulator.ColorCyan);
        terminal.WriteLine("=================================");
        terminal.WriteLine();

        if (player.IsMarried)
        {
            terminal.WriteLine(Loc.Get("love_corner.status_married_to", player.SpouseName), TerminalEmulator.ColorGreen);
            terminal.WriteLine(Loc.Get("love_corner.marriage_count", player.MarriedTimes));
            terminal.WriteLine();
        }

        terminal.WriteLine(Loc.Get("love_corner.children_count", player.Children));
        terminal.WriteLine(Loc.Get("love_corner.intimacy_left", player.IntimacyActs));
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.relations_feature"));
        terminal.WriteLine(Loc.Get("love_corner.relations_view"));
        terminal.WriteLine(Loc.Get("love_corner.relations_love_hate"));
        terminal.WriteLine(Loc.Get("love_corner.relations_history"));
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.relations_network"));

        await terminal.PressAnyKey();
        return true;
    }

    private async Task<bool> HandleGiftShop(Character player)
    {
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.gift_shop"), TerminalEmulator.ColorYellow);
        terminal.WriteLine("=========");
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.gift_welcome"));
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.gift_roses", GameConfig.RosesCost));
        terminal.WriteLine(Loc.Get("love_corner.gift_chocolates", GameConfig.ChocolatesCostBase));
        terminal.WriteLine(Loc.Get("love_corner.gift_jewelry", GameConfig.JewelryCostBase));
        terminal.WriteLine(Loc.Get("love_corner.gift_poison", GameConfig.PoisonCostBase));
        terminal.WriteLine(Loc.Get("love_corner.gift_exit"));
        terminal.WriteLine();

        string choice = await terminal.GetInput(Loc.Get("love_corner.gift_purchase_prompt"));

        switch (choice?.ToUpper())
        {
            case "R":
                return await PurchaseGift(player, "Roses", GameConfig.RosesCost);
            case "C":
                return await PurchaseGift(player, "Chocolates", GameConfig.ChocolatesCostBase);
            case "J":
                return await PurchaseGift(player, "Jewelry", GameConfig.JewelryCostBase);
            case "P":
                return await PurchasePoison(player);
            default:
                terminal.WriteLine(Loc.Get("love_corner.gift_thank_you"));
                await terminal.PressAnyKey();
                return true;
        }
    }

    private async Task<bool> PurchaseGift(Character player, string giftName, long cost)
    {
        if (player.Gold < cost)
        {
            terminal.WriteLine(Loc.Get("love_corner.gift_need_gold", cost, giftName), TerminalEmulator.ColorRed);
            terminal.WriteLine(Loc.Get("love_corner.you_only_have", player.Gold));
            await terminal.PressAnyKey();
            return true;
        }

        string recipient = await terminal.GetInput(Loc.Get("love_corner.gift_send_to", giftName));

        if (string.IsNullOrWhiteSpace(recipient))
        {
            terminal.WriteLine(Loc.Get("love_corner.gift_invalid_recipient"));
            await terminal.PressAnyKey();
            return true;
        }

        player.Gold -= cost;
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.gift_sent", giftName, recipient));
        terminal.WriteLine(Loc.Get("love_corner.gift_appreciate"));

        await terminal.PressAnyKey();
        return true;
    }

    private async Task<bool> PurchasePoison(Character player)
    {
        long cost = GameConfig.PoisonCostBase;

        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.poison_header"), TerminalEmulator.ColorRed);
        terminal.WriteLine("===============");
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.poison_offer"), TerminalEmulator.ColorDarkGray);
        terminal.WriteLine(Loc.Get("love_corner.poison_cost", cost));
        terminal.WriteLine();

        if (player.Gold < cost)
        {
            terminal.WriteLine(Loc.Get("love_corner.poison_need_gold", cost), TerminalEmulator.ColorRed);
            await terminal.PressAnyKey();
            return true;
        }

        string target = await terminal.GetInput(Loc.Get("love_corner.poison_target"));

        if (string.IsNullOrWhiteSpace(target))
        {
            terminal.WriteLine(Loc.Get("love_corner.poison_no_target"));
            await terminal.PressAnyKey();
            return true;
        }

        player.Gold -= cost;
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.poison_done", target), TerminalEmulator.ColorRed);
        terminal.WriteLine(Loc.Get("love_corner.poison_never"));

        await terminal.PressAnyKey();
        return true;
    }

    private async Task<bool> HandleStatus(Character player)
    {
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.status_header", player.Name), TerminalEmulator.ColorCyan);
        terminal.WriteLine("====================================");
        terminal.WriteLine();

        terminal.WriteLine(Loc.Get("love_corner.age", player.Age));
        terminal.WriteLine(Loc.Get("love_corner.sex", player.Sex == CharacterSex.Male ? Loc.Get("love_corner.male") : Loc.Get("love_corner.female")));
        terminal.WriteLine(Loc.Get("love_corner.race", player.Race));
        terminal.WriteLine();

        if (player.IsMarried)
        {
            terminal.WriteLine(Loc.Get("love_corner.marital_married", player.SpouseName), TerminalEmulator.ColorGreen);
            terminal.WriteLine(Loc.Get("love_corner.times_married", player.MarriedTimes));
        }
        else
        {
            terminal.WriteLine(Loc.Get("love_corner.marital_single"), TerminalEmulator.ColorYellow);
            if (player.MarriedTimes > 0)
            {
                terminal.WriteLine(Loc.Get("love_corner.previous_marriages", player.MarriedTimes));
            }
        }

        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.children_count", player.Children));
        terminal.WriteLine(Loc.Get("love_corner.intimacy_remaining", player.IntimacyActs));
        terminal.WriteLine();

        // Character personality assessment
        if (player.Chivalry >= player.Darkness)
        {
            terminal.WriteLine(Loc.Get("love_corner.good_hearted", player.Name), TerminalEmulator.ColorGreen);
        }
        else
        {
            terminal.WriteLine(Loc.Get("love_corner.evil_mind", player.Name), TerminalEmulator.ColorRed);
        }

        await terminal.PressAnyKey();
        return true;
    }

    private async Task<bool> HandleLoveHistory(Character player)
    {
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.history_header"), TerminalEmulator.ColorMagenta);
        terminal.WriteLine("===============");
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.history_intro"));
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.history_menu1"));
        terminal.WriteLine(Loc.Get("love_corner.history_menu2"));
        terminal.WriteLine(Loc.Get("love_corner.history_menu3"));
        terminal.WriteLine();

        string choice = await GetChoice();

        switch (choice?.ToUpper())
        {
            case "M":
                ShowMarriageHistory();
                break;
            case "C":
                ShowChildBirthHistory();
                break;
            case "1":
                return await HandleMarriedCouples(player);
            case "H":
                ShowHatedPlayersList();
                break;
            case "L":
                ShowLovedPlayersList();
                break;
            case "R":
                return true;
            default:
                terminal.WriteLine(Loc.Get("love_corner.invalid_choice"));
                break;
        }

        await terminal.PressAnyKey();
        return true;
    }

    private void ShowMarriageHistory()
    {
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.marriage_history"), TerminalEmulator.ColorYellow);
        terminal.WriteLine("==========================");
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.marriage_history_text"));
        terminal.WriteLine(Loc.Get("love_corner.marriage_history_log"));
    }

    private void ShowChildBirthHistory()
    {
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.child_birth_history"), TerminalEmulator.ColorCyan);
        terminal.WriteLine("==================");
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.child_birth_text"));
        terminal.WriteLine(Loc.Get("love_corner.child_birth_log"));
    }

    private void ShowHatedPlayersList()
    {
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.most_hated"), TerminalEmulator.ColorRed);
        terminal.WriteLine("==================");
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.most_hated_text"));
    }

    private void ShowLovedPlayersList()
    {
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.most_loved"), TerminalEmulator.ColorMagenta);
        terminal.WriteLine("==================");
        terminal.WriteLine();
        terminal.WriteLine(Loc.Get("love_corner.most_loved_text"));
    }

    private string GetRelationshipName(int relation)
    {
        return relation switch
        {
            GameConfig.RelationMarried => Loc.Get("love_corner.rel_married"),
            GameConfig.RelationLove => Loc.Get("love_corner.rel_love"),
            GameConfig.RelationPassion => Loc.Get("love_corner.rel_passion"),
            GameConfig.RelationFriendship => Loc.Get("love_corner.rel_friendship"),
            GameConfig.RelationTrust => Loc.Get("love_corner.rel_trust"),
            GameConfig.RelationRespect => Loc.Get("love_corner.rel_respect"),
            GameConfig.RelationNormal => Loc.Get("love_corner.rel_neutral"),
            GameConfig.RelationSuspicious => Loc.Get("love_corner.rel_suspicious"),
            GameConfig.RelationAnger => Loc.Get("love_corner.rel_anger"),
            GameConfig.RelationEnemy => Loc.Get("love_corner.rel_enemy"),
            GameConfig.RelationHate => Loc.Get("love_corner.rel_hate"),
            _ => Loc.Get("love_corner.rel_unknown")
        };
    }

    private async Task<bool> ShowMenuAndReturnTrue(Character player)
    {
        await ShowMenu(player);
        return true;
    }
}
