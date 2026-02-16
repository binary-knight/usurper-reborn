using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

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
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("╔════════════════════════════════════════╗");
        terminal.WriteLine("║          THE QUEST HALL                ║");
        terminal.WriteLine("╚════════════════════════════════════════╝");
        terminal.SetColor("white");
        terminal.WriteLine("");
        terminal.WriteLine("A large board dominates the wall, covered with bounty notices");
        terminal.WriteLine("and quest postings from the Royal Council and local guilds.");
        terminal.WriteLine("");

        bool leaving = false;
        while (!leaving)
        {
            leaving = await ShowMenuAndProcess();
        }

        terminal.WriteLine("You leave the Quest Hall.", "gray");
        await Task.Delay(500);

        // Return to Main Street via exception (standard navigation pattern)
        throw new LocationExitException(GameLocation.MainStreet);
    }

    private async Task<bool> ShowMenuAndProcess()
    {
        terminal.SetColor("cyan");
        terminal.WriteLine("─── Quest Hall ───");
        terminal.SetColor("white");

        // Show active quest count
        var activeQuests = QuestSystem.GetPlayerQuests(currentPlayer.Name2);
        var availableQuests = QuestSystem.GetAvailableQuests(currentPlayer);

        terminal.WriteLine($"Active Quests: {activeQuests.Count}  |  Available: {availableQuests.Count}");
        terminal.WriteLine("");

        terminal.SetColor("bright_cyan");
        terminal.Write(" [V]");
        terminal.SetColor("white");
        terminal.Write("iew Available Quests    ");

        terminal.SetColor("bright_cyan");
        terminal.Write("[A]");
        terminal.SetColor("white");
        terminal.WriteLine("ctive Quests");

        terminal.SetColor("bright_green");
        terminal.Write(" [C]");
        terminal.SetColor("white");
        terminal.Write("laim a Quest           ");

        terminal.SetColor("bright_green");
        terminal.Write("[T]");
        terminal.SetColor("white");
        terminal.WriteLine("urn In Quest");

        terminal.SetColor("bright_yellow");
        terminal.Write(" [B]");
        terminal.SetColor("white");
        terminal.Write("ounty Board            ");

        terminal.SetColor("gray");
        terminal.Write("[R]");
        terminal.SetColor("white");
        terminal.WriteLine("eturn to Street");

        terminal.WriteLine("");

        var choice = await terminal.GetInput("Your choice: ");

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
            case "R":
            case "":
                return true;
            default:
                terminal.WriteLine("Invalid choice.", "red");
                break;
        }

        return false;
    }

    private async Task ViewAvailableQuests()
    {
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══ Available Quests ═══");
        terminal.SetColor("white");

        var quests = QuestSystem.GetAvailableQuests(currentPlayer);

        if (quests.Count == 0)
        {
            terminal.WriteLine("No quests available for your level.", "yellow");
            terminal.WriteLine($"(Your level: {currentPlayer.Level})", "gray");
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
        terminal.SetColor("bright_green");
        terminal.WriteLine("═══ Your Active Quests ═══");
        terminal.SetColor("white");

        var quests = QuestSystem.GetPlayerQuests(currentPlayer.Name2);

        if (quests.Count == 0)
        {
            terminal.WriteLine("You have no active quests.", "yellow");
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
            terminal.WriteLine("No quests available to claim.", "yellow");
            await Task.Delay(1000);
            return;
        }

        terminal.SetColor("cyan");
        terminal.WriteLine("Select a quest to claim:");
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
            terminal.Write($" [{i + 1}] ");
            terminal.SetColor(diffColor);
            terminal.Write($"[{quest.GetDifficultyString()}] ");
            terminal.SetColor("white");
            terminal.WriteLine(quest.Title ?? quest.GetTargetDescription());
        }

        terminal.WriteLine(" [0] Cancel");
        terminal.WriteLine("");

        var input = await terminal.GetInput("Select: ");
        if (int.TryParse(input, out int selection) && selection > 0 && selection <= quests.Count)
        {
            var quest = quests[selection - 1];

            // Show quest details before confirming
            terminal.WriteLine("");
            DisplayQuestDetails(quest);
            terminal.WriteLine("");

            var confirm = await terminal.GetInput("Accept this quest? (Y/N): ");
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
                    terminal.WriteLine("Quest accepted!", "bright_green");
                    terminal.WriteLine($"You have {quest.DaysToComplete} days to complete this quest.", "cyan");
                }
                else
                {
                    terminal.WriteLine($"Cannot claim quest: {result}", "red");
                }
            }
            else
            {
                terminal.WriteLine("Quest not accepted.", "gray");
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
            terminal.WriteLine("You have no active quests to turn in.", "yellow");
            await Task.Delay(1000);
            return;
        }

        terminal.SetColor("cyan");
        terminal.WriteLine("Select a quest to turn in:");
        terminal.SetColor("white");

        for (int i = 0; i < quests.Count; i++)
        {
            var quest = quests[i];
            var progress = QuestSystem.GetQuestProgressSummary(quest);
            terminal.WriteLine($" [{i + 1}] {quest.Title ?? quest.GetTargetDescription()}");
            terminal.WriteLine($"     {progress}", "gray");
        }

        terminal.WriteLine(" [0] Cancel");
        terminal.WriteLine("");

        var input = await terminal.GetInput("Select: ");
        if (int.TryParse(input, out int selection) && selection > 0 && selection <= quests.Count)
        {
            var quest = quests[selection - 1];
            var result = QuestSystem.CompleteQuest(currentPlayer, quest.Id, terminal);

            if (result == QuestCompletionResult.Success)
            {
                terminal.WriteLine($"  Quests completed: {currentPlayer.RoyQuests}", "gray");
            }
            else if (result == QuestCompletionResult.RequirementsNotMet)
            {
                terminal.WriteLine("You haven't completed the quest requirements yet.", "red");
            }
            else
            {
                terminal.WriteLine($"Cannot complete quest: {result}", "red");
            }
        }

        await terminal.PressAnyKey();
    }

    private async Task ViewBountyBoard()
    {
        terminal.WriteLine("");
        terminal.SetColor("bright_red");
        terminal.WriteLine("═══ BOUNTY BOARD ═══");
        terminal.SetColor("white");
        terminal.WriteLine("Bounties posted by the Crown for dangerous individuals.");
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
            terminal.WriteLine("No bounties currently posted.", "gray");
            terminal.WriteLine("Check back later - the King may post new bounties.", "gray");
        }
        else
        {
            foreach (var bounty in allBounties)
            {
                terminal.SetColor("red");
                terminal.Write("WANTED: ");
                terminal.SetColor("bright_white");
                terminal.WriteLine(bounty.Title ?? "Dangerous criminal");
                terminal.SetColor("white");
                terminal.WriteLine($"  {bounty.Comment}", "gray");
                terminal.WriteLine($"  Reward: {bounty.GetRewardDescription()}", "yellow");
                terminal.WriteLine($"  Difficulty: {bounty.GetDifficultyString()}  |  Posted by: {bounty.Initiator}", "gray");
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
        terminal.WriteLine($"  From: {quest.Initiator}  |  Levels {quest.MinLevel}-{quest.MaxLevel}");
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

        terminal.SetColor("bright_white");
        terminal.WriteLine($"╔═ {quest.Title ?? quest.GetTargetDescription()} ═╗");
        terminal.SetColor("white");

        if (!string.IsNullOrEmpty(quest.Comment))
        {
            terminal.WriteLine($"  \"{quest.Comment}\"", "cyan");
        }

        terminal.Write("  Difficulty: ");
        terminal.WriteLine(quest.GetDifficultyString(), diffColor);

        terminal.WriteLine($"  Posted by: {quest.Initiator}");
        terminal.WriteLine($"  Level range: {quest.MinLevel} - {quest.MaxLevel}");
        terminal.WriteLine($"  Time limit: {quest.DaysToComplete} days");
        terminal.WriteLine($"  Reward: {quest.GetRewardDescription()}", "yellow");

        // Show objectives if any
        if (quest.Objectives.Count > 0)
        {
            terminal.WriteLine("  Objectives:", "cyan");
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
            terminal.WriteLine("  Targets:", "cyan");
            foreach (var monster in quest.Monsters)
            {
                terminal.WriteLine($"    - {monster.MonsterName} x{monster.Count}");
            }
        }

        // Show completion hint
        terminal.SetColor("darkgray");
        if (quest.QuestTarget == QuestTarget.Monster || quest.QuestTarget == QuestTarget.ClearBoss)
            terminal.WriteLine("  Hint: Fight monsters in the Dungeon to complete this quest.");
        else if (quest.QuestTarget == QuestTarget.ReachFloor)
            terminal.WriteLine("  Hint: Explore deeper into the Dungeon to reach the target floor.");
        else if (quest.QuestTarget == QuestTarget.FindArtifact)
            terminal.WriteLine("  Hint: Search dungeon rooms for the artifact.");
        else if (quest.QuestTarget == QuestTarget.BuyWeapon || quest.QuestTarget == QuestTarget.BuyArmor ||
                 quest.QuestTarget == QuestTarget.BuyAccessory || quest.QuestTarget == QuestTarget.BuyShield)
            terminal.WriteLine("  Hint: Purchase the item from the appropriate shop in town.");
        else if (quest.QuestTarget == QuestTarget.DefeatNPC)
        {
            // Faith faction "Redeem" quests use DefeatNPC target but TalkToNPC objective
            bool isTalkQuest = quest.Objectives.Any(o => o.ObjectiveType == QuestObjectiveType.TalkToNPC);
            if (isTalkQuest)
                terminal.WriteLine("  Hint: Find the NPC in town and talk to them.");
            else
                terminal.WriteLine("  Hint: Find and defeat the target in combat.");
        }
    }
}
