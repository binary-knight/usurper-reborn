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
        LocationName = Loc.Get("quest_hall.name");
        LocationDescription = Loc.Get("quest_hall.description");
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

        // Phase 4: Electron mode emits structured location/menu state. Pattern B
        // — emit OR render text, both modes share GetChoice() input below.
        if (GameConfig.ElectronMode)
        {
            EmitElectronEvents(activeQuests.Count, availableQuests.Count);
        }
        else if (IsScreenReader)
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

        // Handle global quick commands (/health, /bug, etc.)
        var (handled, shouldExit) = await TryProcessGlobalCommand(choice);
        if (handled) return false;

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
        var quests = QuestSystem.GetAvailableQuests(currentPlayer);

        if (GameConfig.ElectronMode)
        {
            ElectronBridge.EmitQuestList(
                listType: "available",
                title: Loc.Get("quest.available"),
                quests: quests.Select((q, i) => BuildQuestSummary(q, (i + 1).ToString())).ToList());
            ElectronBridge.EmitPressAnyKey();
            await terminal.PressAnyKey();
            return;
        }

        terminal.WriteLine("");
        WriteSectionHeader(Loc.Get("quest.available"), "bright_cyan");

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
        var quests = QuestSystem.GetPlayerQuests(currentPlayer.Name2);

        if (GameConfig.ElectronMode)
        {
            // Use the quest log overlay (full details with progress bars) rather
            // than the simple list — players want to see objectives + progress here.
            ElectronBridge.EmitQuestLog(quests.Select(q => BuildQuestDetail(q)).ToList());
            ElectronBridge.EmitPressAnyKey();
            await terminal.PressAnyKey();
            return;
        }

        terminal.WriteLine("");
        WriteSectionHeader(Loc.Get("quest.active"), "bright_green");

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
        var quests = QuestSystem.GetAvailableQuests(currentPlayer);

        if (quests.Count == 0)
        {
            if (GameConfig.ElectronMode)
            {
                ElectronBridge.EmitQuestList(
                    listType: "claim",
                    title: Loc.Get("quest_hall.select_claim"),
                    quests: new List<ElectronBridge.QuestSummaryData>());
                ElectronBridge.EmitPressAnyKey();
            }
            terminal.WriteLine("");
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

        if (GameConfig.ElectronMode)
        {
            ElectronBridge.EmitQuestList(
                listType: "claim",
                title: Loc.Get("quest_hall.select_claim"),
                quests: quests.Select((q, i) => BuildQuestSummary(q, (i + 1).ToString())).ToList());
        }
        else
        {
            terminal.WriteLine("");
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
                    WriteSRMenuOption($"{i + 1}", $"{quest.GetDifficultyString()} - {quest.GetDisplayTitle()}");
                }
                else
                {
                    terminal.Write(" [", "white");
                    terminal.Write($"{i + 1}", "bright_yellow");
                    terminal.Write("] ", "white");
                    terminal.SetColor(diffColor);
                    terminal.Write($"[{quest.GetDifficultyString()}] ");
                    terminal.SetColor("white");
                    terminal.WriteLine(quest.GetDisplayTitle());
                }
            }

            WriteSRMenuOption("0", Loc.Get("ui.cancel"));
            terminal.WriteLine("");
        }

        var input = await terminal.GetInput(Loc.Get("quest_hall.select_prompt"));
        if (int.TryParse(input, out int selection) && selection > 0 && selection <= quests.Count)
        {
            var quest = quests[selection - 1];

            // Show quest details before confirming
            if (GameConfig.ElectronMode)
            {
                ElectronBridge.EmitQuestDetails(BuildQuestDetail(quest), confirmAction: "claim");
            }
            else
            {
                terminal.WriteLine("");
                DisplayQuestDetails(quest);
                terminal.WriteLine("");
            }

            var confirm = await terminal.GetInput(Loc.Get("quest_hall.accept_prompt"));
            if (GameConfig.IsAffirmative(confirm))
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
        var quests = QuestSystem.GetPlayerQuests(currentPlayer.Name2);

        if (quests.Count == 0)
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("ui.no_active_quests_turn_in"), "yellow");
            await Task.Delay(1000);
            return;
        }

        if (GameConfig.ElectronMode)
        {
            ElectronBridge.EmitQuestList(
                listType: "turnin",
                title: Loc.Get("quest_hall.select_turn_in"),
                quests: quests.Select((q, i) =>
                {
                    var s = BuildQuestSummary(q, (i + 1).ToString());
                    s.Progress = QuestSystem.GetQuestProgressSummary(q);
                    return s;
                }).ToList());
        }
        else
        {
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("quest_hall.select_turn_in"));
            terminal.SetColor("white");

            for (int i = 0; i < quests.Count; i++)
            {
                var quest = quests[i];
                var progress = QuestSystem.GetQuestProgressSummary(quest);
                WriteSRMenuOption($"{i + 1}", $"{quest.GetDisplayTitle()} - {progress}");
            }

            WriteSRMenuOption("0", Loc.Get("ui.cancel"));
            terminal.WriteLine("");
        }

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
        var quests = QuestSystem.GetPlayerQuests(currentPlayer.Name2);

        if (quests.Count == 0)
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("ui.no_active_quests_abandon"), "yellow");
            await Task.Delay(1000);
            return;
        }

        if (GameConfig.ElectronMode)
        {
            ElectronBridge.EmitQuestList(
                listType: "abandon",
                title: Loc.Get("quest_hall.select_abandon"),
                quests: quests.Select((q, i) =>
                {
                    var s = BuildQuestSummary(q, (i + 1).ToString());
                    s.Progress = QuestSystem.GetQuestProgressSummary(q);
                    return s;
                }).ToList());
        }
        else
        {
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("quest_hall.select_abandon"));
            terminal.SetColor("white");

            for (int i = 0; i < quests.Count; i++)
            {
                var quest = quests[i];
                var progress = QuestSystem.GetQuestProgressSummary(quest);
                WriteSRMenuOption($"{i + 1}", $"{quest.GetDisplayTitle()} - {progress}");
            }

            WriteSRMenuOption("0", Loc.Get("ui.cancel"));
            terminal.WriteLine("");
        }

        var input = await terminal.GetInput(Loc.Get("quest_hall.select_prompt"));
        if (int.TryParse(input, out int selection) && selection > 0 && selection <= quests.Count)
        {
            var quest = quests[selection - 1];
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("quest_hall.abandon_confirm", quest.GetDisplayTitle()), "yellow");
            terminal.WriteLine(Loc.Get("quest_hall.progress_lost"), "gray");
            var confirm = await terminal.GetInput(Loc.Get("ui.confirm"));

            if (GameConfig.IsAffirmative(confirm))
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
        // Get bounties from both the King and the Bounty Board
        var kingBounties = QuestSystem.GetKingBounties()
            .Where(q => string.IsNullOrEmpty(q.Occupier))
            .ToList();

        var otherBounties = QuestSystem.GetAvailableQuests(currentPlayer)
            .Where(q => q.QuestTarget == QuestTarget.Assassin && q.Initiator != "The Crown")
            .ToList();

        var allBounties = kingBounties.Concat(otherBounties).ToList();

        if (GameConfig.ElectronMode)
        {
            ElectronBridge.EmitQuestList(
                listType: "bounty",
                title: Loc.Get("quest.bounty_board"),
                quests: allBounties.Select((q, i) => BuildQuestSummary(q, (i + 1).ToString())).ToList());
            ElectronBridge.EmitPressAnyKey();
            await terminal.PressAnyKey();
            return;
        }

        terminal.WriteLine("");
        WriteSectionHeader(Loc.Get("quest.bounty_board"), "bright_red");
        terminal.WriteLine(Loc.Get("quest_hall.bounty_desc"));
        terminal.WriteLine("");

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
                terminal.WriteLine(bounty.GetDisplayTitle());
                terminal.SetColor("white");
                terminal.WriteLine($"  {bounty.GetDisplayComment()}", "gray");
                terminal.WriteLine($"  {Loc.Get("quest_hall.reward", bounty.GetRewardDescription())}", "yellow");
                terminal.WriteLine($"  {Loc.Get("quest_hall.difficulty_posted", bounty.GetDifficultyString(), bounty.GetDisplayInitiator())}", "gray");
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
        terminal.WriteLine(quest.GetDisplayTitle());
        terminal.SetColor("gray");
        terminal.WriteLine($"  {Loc.Get("quest_hall.from_levels", quest.GetDisplayInitiator(), quest.MinLevel, quest.MaxLevel)}");
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

        WriteSectionHeader(quest.GetDisplayTitle(), "bright_white");

        var displayComment = quest.GetDisplayComment();
        if (!string.IsNullOrEmpty(displayComment))
        {
            terminal.WriteLine($"  \"{displayComment}\"", "cyan");
        }

        terminal.Write($"  {Loc.Get("quest_hall.difficulty_label")}");
        terminal.WriteLine(quest.GetDifficultyString(), diffColor);

        terminal.WriteLine($"  {Loc.Get("quest_hall.posted_by", quest.GetDisplayInitiator())}");
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
                terminal.WriteLine($"    {status} {obj.GetDisplayDescription()} ({obj.CurrentProgress}/{obj.RequiredProgress})", color);
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

    /// <summary>
    /// Phase 4: emit Quest Hall menu state for the Electron client. Same Pattern
    /// B as MainStreet/Healer — emit + skip text, shared GetChoice() input.
    /// </summary>
    private void EmitElectronEvents(int activeCount, int availableCount)
    {
        var player = GetCurrentPlayer();
        if (player == null) return;

        ElectronBridge.EmitLocation(
            name: Loc.Get("quest_hall.menu_header"),
            description: Loc.Get("quest_hall.active_count_visual", activeCount, availableCount),
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
            new() { Key = "V", Label = Loc.Get("quest_hall.view"), Category = "list", Icon = "scroll" },
            new() { Key = "A", Label = Loc.Get("quest_hall.active"), Category = "list", Icon = "active-quest" },
            new() { Key = "C", Label = Loc.Get("quest_hall.claim"), Category = "action", Icon = "claim" },
            new() { Key = "T", Label = Loc.Get("quest_hall.turn_in"), Category = "action", Icon = "turn-in" },
            new() { Key = "B", Label = Loc.Get("quest_hall.bounty"), Category = "list", Icon = "bounty" },
            new() { Key = "X", Label = Loc.Get("quest_hall.abandon"), Category = "action", Icon = "abandon" },
            new() { Key = "R", Label = Loc.Get("shop.return"), Category = "navigate", Icon = "back" },
        };
        ElectronBridge.EmitMenu(menu);

        EmitNPCsInLocationToElectron();
    }

    /// <summary>
    /// Build a compact quest summary for the Electron quest list overlay.
    /// Used by available, claim, turn-in, abandon, and bounty list emits.
    /// </summary>
    private ElectronBridge.QuestSummaryData BuildQuestSummary(Quest quest, string key)
    {
        string status = quest.IsAbandoned ? "Abandoned"
            : quest.IsActive ? "Active"
            : quest.IsAvailable ? "Available"
            : "Unknown";

        return new ElectronBridge.QuestSummaryData
        {
            Key = key,
            Title = quest.GetDisplayTitle(),
            Description = string.IsNullOrEmpty(quest.GetDisplayComment()) ? null : quest.GetDisplayComment(),
            Difficulty = quest.GetDifficultyString(),
            MinLevel = quest.MinLevel,
            MaxLevel = quest.MaxLevel,
            Status = status,
            Eligible = currentPlayer.Level >= quest.MinLevel && currentPlayer.Level <= quest.MaxLevel
        };
    }

    /// <summary>
    /// Build a full quest detail payload for the accept / log / completion modal.
    /// Includes objectives with progress, reward breakdown, and the giver's name.
    /// </summary>
    private ElectronBridge.QuestDetailData BuildQuestDetail(Quest quest)
    {
        var objectives = new List<string>();
        foreach (var obj in quest.Objectives)
        {
            string check = obj.IsComplete ? "[+]" : "[ ]";
            objectives.Add($"{check} {obj.GetDisplayDescription()} ({obj.CurrentProgress}/{obj.RequiredProgress})");
        }
        if (quest.Objectives.Count == 0 && quest.Monsters.Count > 0)
        {
            foreach (var m in quest.Monsters)
            {
                objectives.Add($"Defeat {m.MonsterName} x{m.Count}");
            }
        }

        // Quest.Reward is a byte tier (0-255) and the meaning depends on RewardType.
        // Map the tiered reward into the appropriate QuestRewardData field; fall back
        // to the localized description string in Extras so JS always has something to
        // display even for novel reward types.
        var reward = new ElectronBridge.QuestRewardData();
        switch (quest.RewardType)
        {
            case QuestRewardType.Money:
                reward.Gold = quest.BountyGold > 0 ? quest.BountyGold : (long)quest.Reward * 1000;
                break;
            case QuestRewardType.Experience:
                reward.Experience = (long)quest.Reward * 1000;
                break;
            case QuestRewardType.Potions:
                reward.Potions = quest.Reward;
                break;
            case QuestRewardType.Chivalry:
                reward.Chivalry = quest.Reward;
                break;
            case QuestRewardType.Darkness:
                reward.Darkness = quest.Reward;
                break;
        }
        string rewardDesc = quest.GetRewardDescription();
        if (!string.IsNullOrWhiteSpace(rewardDesc)) reward.Extras.Add(rewardDesc);

        string status = quest.IsAbandoned ? "Abandoned"
            : quest.IsActive ? "Active"
            : quest.IsAvailable ? "Available"
            : "Unknown";

        return new ElectronBridge.QuestDetailData
        {
            Id = quest.Id,
            Title = quest.GetDisplayTitle(),
            Description = quest.GetDisplayComment() ?? "",
            Difficulty = quest.GetDifficultyString(),
            MinLevel = quest.MinLevel,
            MaxLevel = quest.MaxLevel,
            Objectives = objectives,
            Giver = quest.GetDisplayInitiator(),
            Status = status,
            TimeLimit = quest.DaysToComplete > 0 ? $"{quest.DaysToComplete} days" : null,
            Reward = reward
        };
    }
}
