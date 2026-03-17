using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UsurperRemake.Systems;

/// <summary>
/// Quest Hall Location - Where players can view and claim quests/bounties
/// Replaces the old King-created quest system with NPC-generated bounties
/// </summary>
public class QuestHallLocation : BaseLocation
{
    public QuestHallLocation(TerminalEmulator terminal) : base()
    {
        LocationName = "Quest Hall";
        LocationDescription = "The royal quest board where bounties and missions are posted.";
    }

    public override async Task EnterLocation(Character player, TerminalEmulator term)
    {
        currentPlayer = player;
        terminal = term;

        terminal.WriteLine("");
        WriteBoxHeader(Loc.Get("quest_hall.header"), "bright_yellow", 40);
        terminal.SetColor("white");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("quest_hall.desc1"));
        terminal.WriteLine(Loc.Get("quest_hall.desc2"));
        terminal.WriteLine("");

        bool leaving = false;
        while (!leaving)
        {
            leaving = await ShowMenuAndProcess();
        }

        terminal.WriteLine(Loc.Get("quest_hall.leave"), "gray");
        await Task.Delay(500);

        // Return to Main Street via exception (standard navigation pattern)
        throw new LocationExitException(GameLocation.MainStreet);
    }

    private async Task<bool> ShowMenuAndProcess()
    {
        // Show active quest count
        var activeQuests = QuestSystem.GetPlayerQuests(currentPlayer.Name2);
        var availableQuests = QuestSystem.GetAvailableQuests(currentPlayer);

        if (IsScreenReader)
        {
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("quest_hall.active_count", activeQuests.Count, availableQuests.Count));
            terminal.WriteLine("");
            WriteSRMenuOption("V", Loc.Get("quest_hall.view"));
            WriteSRMenuOption("A", Loc.Get("quest_hall.active"));
            WriteSRMenuOption("C", Loc.Get("quest_hall.claim"));
            WriteSRMenuOption("T", Loc.Get("quest_hall.turn_in"));
            WriteSRMenuOption("B", Loc.Get("quest_hall.bounty"));
            WriteSRMenuOption("X", Loc.Get("quest_hall.abandon"));
            WriteSRMenuOption("R", Loc.Get("shop.return"));
            terminal.WriteLine("");
        }
        else
        {
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("quest_hall.menu_header"));
            terminal.SetColor("white");

            terminal.WriteLine(Loc.Get("quest_hall.active_count_visual", activeQuests.Count, availableQuests.Count));
            terminal.WriteLine("");

            terminal.Write(" [", "white");
            terminal.Write("V", "bright_yellow");
            terminal.Write("]", "white");
            terminal.Write(Loc.Get("quest_hall.view_menu"), "white");

            terminal.Write("[", "white");
            terminal.Write("A", "bright_yellow");
            terminal.Write("]", "white");
            terminal.WriteLine(Loc.Get("quest_hall.active_menu"), "white");

            terminal.Write(" [", "white");
            terminal.Write("C", "bright_yellow");
            terminal.Write("]", "white");
            terminal.Write(Loc.Get("quest_hall.claim_menu"), "white");

            terminal.Write("[", "white");
            terminal.Write("T", "bright_yellow");
            terminal.Write("]", "white");
            terminal.WriteLine(Loc.Get("quest_hall.turn_in_menu"), "white");

            terminal.Write(" [", "white");
            terminal.Write("B", "bright_yellow");
            terminal.Write("]", "white");
            terminal.Write(Loc.Get("quest_hall.bounty_menu"), "white");

            terminal.Write("[", "white");
            terminal.Write("X", "bright_yellow");
            terminal.Write("]", "white");
            terminal.WriteLine(Loc.Get("quest_hall.abandon_menu"), "white");

            terminal.Write(" [", "white");
            terminal.Write("R", "bright_yellow");
            terminal.Write("]", "white");
            terminal.WriteLine(Loc.Get("quest_hall.return_menu"), "white");

            terminal.WriteLine("");
        }

        var choice = await GetChoice();

        switch (choice.ToUpper().Trim())
        {
            case "V":
                await ViewAvailableQuests();
                break;
            case "A":
                await ViewActiveQuests();
                break;
            case "C":
                await ClaimQuest();
                break;
            case "T":
                await TurnInQuest();
                break;
            case "B":
                await ViewBountyBoard();
                break;
            case "X":
                await AbandonQuest();
                break;
            case "R":
            case "":
                return true;
            default:
                terminal.WriteLine(Loc.Get("quest_hall.invalid_choice"), "red");
                break;
        }

        return false;
    }

    private async Task ViewAvailableQuests()
    {
        terminal.WriteLine("");
        WriteSectionHeader(Loc.Get("quest.available"), "bright_cyan");

        var quests = QuestSystem.GetAvailableQuests(currentPlayer);

        if (quests.Count == 0)
        {
            if (currentPlayer.RoyQuestsToday >= GameConfig.MaxQuestsPerDay)
            {
                terminal.WriteLine(Loc.Get("quest_hall.daily_limit_reached"), "yellow");
                terminal.WriteLine(Loc.Get("quest_hall.daily_limit_info", GameConfig.MaxQuestsPerDay), "gray");
            }
            else
            {
                terminal.WriteLine(Loc.Get("quest_hall.no_quests_available"), "yellow");
                terminal.WriteLine(Loc.Get("quest_hall.your_level", currentPlayer.Level), "gray");
            }
        }
        else
        {
            foreach (var quest in quests)
            {
                DisplayQuestSummary(quest);
            }
        }

        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    private async Task ViewActiveQuests()
    {
        terminal.WriteLine("");
        WriteSectionHeader(Loc.Get("quest.active"), "bright_green");

        var quests = QuestSystem.GetPlayerQuests(currentPlayer.Name2);

        if (quests.Count == 0)
        {
            terminal.WriteLine(Loc.Get("ui.no_active_quests"), "yellow");
        }
        else
        {
            foreach (var quest in quests)
            {
                DisplayQuestDetails(quest);
            }
        }

        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    private async Task ClaimQuest()
    {
        terminal.WriteLine("");
        var quests = QuestSystem.GetAvailableQuests(currentPlayer);

        if (quests.Count == 0)
        {
            if (currentPlayer.RoyQuestsToday >= GameConfig.MaxQuestsPerDay)
            {
                terminal.WriteLine(Loc.Get("quest_hall.daily_limit_reached"), "yellow");
                terminal.WriteLine(Loc.Get("quest_hall.daily_limit_info", GameConfig.MaxQuestsPerDay), "gray");
            }
            else
            {
                terminal.WriteLine(Loc.Get("quest_hall.no_quests_claim"), "yellow");
            }
            await Task.Delay(1000);
            return;
        }

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("quest_hall.select_claim"));
        terminal.SetColor("white");

        for (int i = 0; i < quests.Count; i++)
        {
            var quest = quests[i];
            var diffColor = quest.Difficulty switch
            {
                1 => "green",
                2 => "yellow",
                3 => "bright_red",
                _ => "red"
            };
            if (IsScreenReader)
            {
                WriteSRMenuOption($"{i + 1}", $"{quest.GetDifficultyString()} - {quest.Title ?? quest.GetTargetDescription()}");
            }
            else
            {
                terminal.Write(" [", "white");
                terminal.Write($"{i + 1}", "bright_yellow");
                terminal.Write("] ", "white");
                terminal.SetColor(diffColor);
                terminal.Write($"[{quest.GetDifficultyString()}] ");
                terminal.SetColor("white");
                terminal.WriteLine(quest.Title ?? quest.GetTargetDescription());
            }
        }

        WriteSRMenuOption("0", Loc.Get("ui.cancel"));
        terminal.WriteLine("");

        var input = await terminal.GetInput(Loc.Get("quest_hall.select_prompt"));
        if (int.TryParse(input, out int selection) && selection > 0 && selection <= quests.Count)
        {
            var quest = quests[selection - 1];

            // Show quest details before confirming
            terminal.WriteLine("");
            DisplayQuestDetails(quest);
            terminal.WriteLine("");

            var confirm = await terminal.GetInput(Loc.Get("quest_hall.accept_prompt"));
            if (confirm.ToUpper().StartsWith("Y"))
            {
                // Cast to Player for ClaimQuest - if not a Player, create one with proper stats
                Player playerForQuest;
                if (currentPlayer is Player p)
                {
                    playerForQuest = p;
                }
                else
                {
                    // Create a Player wrapper with the character's actual stats
                    playerForQuest = new Player
                    {
                        Name2 = currentPlayer.Name2,
                        Level = currentPlayer.Level,
                        King = currentPlayer.King,
                        RoyQuestsToday = currentPlayer.RoyQuestsToday
                    };
                }
                var result = QuestSystem.ClaimQuest(playerForQuest, quest);
                if (result == QuestClaimResult.CanClaim)
                {
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("quest_hall.quest_accepted"), "bright_green");
                    terminal.WriteLine(Loc.Get("quest_hall.days_to_complete", quest.DaysToComplete), "cyan");
                }
                else
                {
                    terminal.WriteLine(Loc.Get("quest_hall.cannot_claim", result), "red");
                }
            }
            else
            {
                terminal.WriteLine(Loc.Get("quest_hall.quest_not_accepted"), "gray");
            }
        }

        await Task.Delay(500);
    }

    private async Task TurnInQuest()
    {
        terminal.WriteLine("");
        var quests = QuestSystem.GetPlayerQuests(currentPlayer.Name2);

        if (quests.Count == 0)
        {
            terminal.WriteLine(Loc.Get("ui.no_active_quests_turn_in"), "yellow");
            await Task.Delay(1000);
            return;
        }

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("quest_hall.select_turn_in"));
        terminal.SetColor("white");

        for (int i = 0; i < quests.Count; i++)
        {
            var quest = quests[i];
            var progress = QuestSystem.GetQuestProgressSummary(quest);
            WriteSRMenuOption($"{i + 1}", $"{quest.Title ?? quest.GetTargetDescription()} - {progress}");
        }

        WriteSRMenuOption("0", Loc.Get("ui.cancel"));
        terminal.WriteLine("");

        var input = await terminal.GetInput(Loc.Get("quest_hall.select_prompt"));
        if (int.TryParse(input, out int selection) && selection > 0 && selection <= quests.Count)
        {
            var quest = quests[selection - 1];
            var result = QuestSystem.CompleteQuest(currentPlayer, quest.Id, terminal);

            if (result == QuestCompletionResult.Success)
            {
                terminal.WriteLine($"  {Loc.Get("quest_hall.quests_completed", currentPlayer.RoyQuests)}", "gray");
            }
            else if (result == QuestCompletionResult.RequirementsNotMet)
            {
                terminal.WriteLine(Loc.Get("quest_hall.requirements_not_met"), "red");
            }
            else
            {
                terminal.WriteLine(Loc.Get("quest_hall.cannot_complete", result), "red");
            }
        }

        await terminal.PressAnyKey();
    }

    private async Task AbandonQuest()
    {
        terminal.WriteLine("");
        var quests = QuestSystem.GetPlayerQuests(currentPlayer.Name2);

        if (quests.Count == 0)
        {
            terminal.WriteLine(Loc.Get("ui.no_active_quests_abandon"), "yellow");
            await Task.Delay(1000);
            return;
        }

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("quest_hall.select_abandon"));
        terminal.SetColor("white");

        for (int i = 0; i < quests.Count; i++)
        {
            var quest = quests[i];
            var progress = QuestSystem.GetQuestProgressSummary(quest);
            WriteSRMenuOption($"{i + 1}", $"{quest.Title ?? quest.GetTargetDescription()} - {progress}");
        }

        WriteSRMenuOption("0", Loc.Get("ui.cancel"));
        terminal.WriteLine("");

        var input = await terminal.GetInput(Loc.Get("quest_hall.select_prompt"));
        if (int.TryParse(input, out int selection) && selection > 0 && selection <= quests.Count)
        {
            var quest = quests[selection - 1];
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("quest_hall.abandon_confirm", quest.Title ?? quest.GetTargetDescription()), "yellow");
            terminal.WriteLine(Loc.Get("quest_hall.progress_lost"), "gray");
            var confirm = await terminal.GetInput(Loc.Get("ui.confirm"));

            if (confirm.Trim().ToUpper() == "Y")
            {
                QuestSystem.AbandonQuest(currentPlayer, quest.Id);
                terminal.WriteLine(Loc.Get("quest_hall.quest_abandoned"), "yellow");
            }
            else
            {
                terminal.WriteLine(Loc.Get("ui.cancelled"), "gray");
            }
        }

        await terminal.PressAnyKey();
    }

    private async Task ViewBountyBoard()
    {
        terminal.WriteLine("");
        WriteSectionHeader(Loc.Get("quest.bounty_board"), "bright_red");
        terminal.WriteLine(Loc.Get("quest_hall.bounty_desc"));
        terminal.WriteLine("");

        // Get bounties from both the King and the Bounty Board
        var kingBounties = QuestSystem.GetKingBounties()
            .Where(q => string.IsNullOrEmpty(q.Occupier))
            .ToList();

        var otherBounties = QuestSystem.GetAvailableQuests(currentPlayer)
            .Where(q => q.QuestTarget == QuestTarget.Assassin && q.Initiator != "The Crown")
            .ToList();

        var allBounties = kingBounties.Concat(otherBounties).ToList();

        if (allBounties.Count == 0)
        {
            terminal.WriteLine(Loc.Get("quest_hall.no_bounties"), "gray");
            terminal.WriteLine(Loc.Get("quest_hall.check_back"), "gray");
        }
        else
        {
            foreach (var bounty in allBounties)
            {
                terminal.SetColor("red");
                terminal.Write(Loc.Get("quest_hall.wanted"));
                terminal.SetColor("bright_white");
                terminal.WriteLine(bounty.Title ?? Loc.Get("quest_hall.dangerous_criminal"));
                terminal.SetColor("white");
                terminal.WriteLine($"  {bounty.Comment}", "gray");
                terminal.WriteLine($"  {Loc.Get("quest_hall.reward", bounty.GetRewardDescription())}", "yellow");
                terminal.WriteLine($"  {Loc.Get("quest_hall.difficulty_posted", bounty.GetDifficultyString(), bounty.Initiator)}", "gray");
                terminal.WriteLine("");
            }
        }

        await terminal.PressAnyKey();
    }

    private void DisplayQuestSummary(Quest quest)
    {
        var diffColor = quest.Difficulty switch
        {
            1 => "green",
            2 => "yellow",
            3 => "bright_red",
            _ => "red"
        };

        terminal.SetColor(diffColor);
        terminal.Write($"[{quest.GetDifficultyString()}] ");
        terminal.SetColor("bright_white");
        terminal.WriteLine(quest.Title ?? quest.GetTargetDescription());
        terminal.SetColor("gray");
        terminal.WriteLine($"  {Loc.Get("quest_hall.from_levels", quest.Initiator, quest.MinLevel, quest.MaxLevel)}");
    }

    private void DisplayQuestDetails(Quest quest)
    {
        var diffColor = quest.Difficulty switch
        {
            1 => "green",
            2 => "yellow",
            3 => "bright_red",
            _ => "red"
        };

        WriteSectionHeader(quest.Title ?? quest.GetTargetDescription(), "bright_white");

        if (!string.IsNullOrEmpty(quest.Comment))
        {
            terminal.WriteLine($"  \"{quest.Comment}\"", "cyan");
        }

        terminal.Write($"  {Loc.Get("quest_hall.difficulty_label")}");
        terminal.WriteLine(quest.GetDifficultyString(), diffColor);

        terminal.WriteLine($"  {Loc.Get("quest_hall.posted_by", quest.Initiator)}");
        terminal.WriteLine($"  {Loc.Get("quest_hall.level_range", quest.MinLevel, quest.MaxLevel)}");
        terminal.WriteLine($"  {Loc.Get("quest_hall.time_limit", quest.DaysToComplete)}");
        terminal.WriteLine($"  {Loc.Get("quest_hall.quest_reward", quest.GetRewardDescription())}", "yellow");

        // Show objectives if any
        if (quest.Objectives.Count > 0)
        {
            terminal.WriteLine($"  {Loc.Get("quest_hall.objectives")}", "cyan");
            foreach (var obj in quest.Objectives)
            {
                var status = obj.IsComplete ? "[+]" : "[ ]";
                var color = obj.IsComplete ? "green" : "white";
                terminal.WriteLine($"    {status} {obj.Description} ({obj.CurrentProgress}/{obj.RequiredProgress})", color);
            }
        }

        // Show monster targets if any (legacy display, kept for quests that populate Monsters list)
        if (quest.Monsters.Count > 0 && quest.Objectives.Count == 0)
        {
            terminal.WriteLine($"  {Loc.Get("quest_hall.targets")}", "cyan");
            foreach (var monster in quest.Monsters)
            {
                terminal.WriteLine($"    - {monster.MonsterName} x{monster.Count}");
            }
        }

        // Show completion hint
        terminal.SetColor("darkgray");
        if (quest.QuestTarget == QuestTarget.Monster || quest.QuestTarget == QuestTarget.ClearBoss)
            terminal.WriteLine($"  {Loc.Get("quest_hall.hint_dungeon")}");
        else if (quest.QuestTarget == QuestTarget.ReachFloor)
            terminal.WriteLine($"  {Loc.Get("quest_hall.hint_floor")}");
        else if (quest.QuestTarget == QuestTarget.BuyWeapon || quest.QuestTarget == QuestTarget.BuyShield)
            terminal.WriteLine($"  {Loc.Get("quest_hall.hint_weapon_shop")}");
        else if (quest.QuestTarget == QuestTarget.BuyArmor)
            terminal.WriteLine($"  {Loc.Get("quest_hall.hint_armor_shop")}");
        else if (quest.QuestTarget == QuestTarget.BuyAccessory)
            terminal.WriteLine($"  {Loc.Get("quest_hall.hint_magic_shop")}");
        else if (quest.QuestTarget == QuestTarget.DefeatNPC)
            terminal.WriteLine($"  {Loc.Get("quest_hall.hint_defeat")}");
    }
}
