using UsurperRemake.Utils;
using UsurperRemake.Systems;
using UsurperRemake.BBS;
using UsurperRemake.UI;
using UsurperRemake.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// The Divine Realm — the only location immortal player-gods can access.
/// Based on the original 1993 Usurper GODWORLD.PAS immortal management system.
/// Gods manage believers, perform divine deeds, compete for followers, and
/// can renounce immortality to reroll their character.
/// </summary>
public class PantheonLocation : BaseLocation
{
    // Anti-grief: tracks last smite time per "godName>targetName" pair
    private static readonly Dictionary<string, DateTime> _smiteCooldowns = new();

    public PantheonLocation()
        : base(GameLocation.Pantheon, "The Divine Realm", "The cosmic plane where ascended gods dwell beyond mortal reach.")
    {
    }

    public override async Task EnterLocation(Character player, TerminalEmulator term)
    {
        currentPlayer = player;
        terminal = term;

        player.Location = (int)GameLocation.Pantheon;
        player.CurrentLocation = "The Divine Realm";

        if (OnlineStateManager.IsActive)
            OnlineStateManager.Instance!.UpdateLocation("The Divine Realm");

        await RunPantheonLoop();
    }

    private async Task RunPantheonLoop()
    {
        bool exitLoop = false;
        while (!exitLoop)
        {
            ShowPantheonMenu();
            string input = await terminal.GetInputAsync("\n  Your divine will: ");
            string choice = input.Trim().ToUpper();

            // Handle chat commands
            if (choice.StartsWith("/"))
            {
                // MUD mode: route through MudChatSystem
                if (UsurperRemake.Server.SessionContext.IsActive)
                {
                    bool handled = await UsurperRemake.Server.MudChatSystem.TryProcessCommand(choice, terminal);
                    if (handled) continue;
                }
                // Legacy online mode: route through OnlineChatSystem
                else if (DoorMode.IsOnlineMode)
                {
                    // Slash commands handled by location's TryProcessGlobalCommand
                }
            }

            switch (choice)
            {
                case "S":
                    await ShowDivineStatus();
                    break;
                case "B":
                    await ShowBelievers();
                    break;
                case "D":
                    await PerformDivineDeeds();
                    break;
                case "F":
                    await ConfigureBoons();
                    break;
                case "I":
                    await ShowImmortalRankings();
                    break;
                case "N":
                    await ShowNews();
                    break;
                case "C":
                    await SendProclamation();
                    break;
                case "V":
                    await VisitManwe();
                    break;
                case "R":
                    bool renounced = await RenounceImmortality();
                    if (renounced) return;
                    break;
                case "Q":
                    throw new LocationExitException(GameLocation.NoWhere);
                default:
                    terminal.WriteLine("  The cosmos does not understand.", "gray");
                    break;
            }
        }
    }

    private void ShowPantheonMenu()
    {
        terminal.ClearScreen();

        string godTitle = GetGodTitle(currentPlayer.GodLevel);
        int believers = CountBelievers(currentPlayer.DivineName);
        int deedsMax = GetDeedsPerDay(currentPlayer.GodLevel);

        terminal.SetColor("bright_yellow");
        terminal.WriteLine("╔═════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine($"║{"THE DIVINE REALM".PadLeft((77 + 16) / 2).PadRight(77)}║");
        terminal.WriteLine("╚═════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("bright_yellow");
        terminal.Write($"  Welcome, ");
        terminal.SetColor("white");
        terminal.Write($"{currentPlayer.DivineName}");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($" the {godTitle}");

        terminal.SetColor("gray");
        terminal.WriteLine($"  Alignment: {currentPlayer.GodAlignment}  |  Believers: {believers}  |  Deeds: {currentPlayer.DeedsLeft}/{deedsMax}");
        terminal.WriteLine("");

        // Menu
        WriteMenuOption("S", "Status", "View your divine stats");
        WriteMenuOption("B", "Believers", "View and manage your flock");
        WriteMenuOption("D", "Divine Deeds", $"Perform interventions ({currentPlayer.DeedsLeft} left)");
        WriteMenuOption("F", "Favors", "Configure boons for your followers");
        WriteMenuOption("I", "Immortals", "View other gods and rankings");
        WriteMenuOption("N", "News", "Read mortal and divine news");
        WriteMenuOption("C", "Comment", "Send a proclamation to your believers");
        WriteMenuOption("V", "Visit Manwe", "The Supreme Creator's throne");
        terminal.WriteLine("");
        WriteMenuOption("R", "Renounce", "Give up immortality and reroll");
        WriteMenuOption("Q", "Quit", "Leave the game");
    }

    private void WriteMenuOption(string key, string label, string desc)
    {
        terminal.SetColor("darkgray");
        terminal.Write("  [");
        terminal.SetColor("bright_yellow");
        terminal.Write(key);
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.Write(label.PadRight(18));
        terminal.SetColor("gray");
        terminal.WriteLine(desc);
    }

    #region Status

    private async Task ShowDivineStatus()
    {
        terminal.ClearScreen();

        string godTitle = GetGodTitle(currentPlayer.GodLevel);
        int believers = CountBelievers(currentPlayer.DivineName);
        long nextLevelExp = GetNextLevelExp(currentPlayer.GodLevel);
        int deedsMax = GetDeedsPerDay(currentPlayer.GodLevel);

        terminal.SetColor("bright_yellow");
        terminal.WriteLine("╔═════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine($"║{"DIVINE STATUS".PadLeft((77 + 13) / 2).PadRight(77)}║");
        terminal.WriteLine("╚═════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"  {currentPlayer.DivineName} the {godTitle}");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.Write("  Mortal Name:    ");
        terminal.SetColor("white");
        terminal.WriteLine($"{currentPlayer.Name2}");

        terminal.SetColor("cyan");
        terminal.Write("  Alignment:      ");
        terminal.SetColor("white");
        terminal.WriteLine($"{currentPlayer.GodAlignment}");

        terminal.SetColor("cyan");
        terminal.Write("  God Rank:       ");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"Level {currentPlayer.GodLevel} — {godTitle}");

        terminal.SetColor("cyan");
        terminal.Write("  Experience:     ");
        terminal.SetColor("white");
        if (currentPlayer.GodLevel < GameConfig.GodMaxLevel)
            terminal.WriteLine($"{currentPlayer.GodExperience:N0} / {nextLevelExp:N0}");
        else
            terminal.WriteLine($"{currentPlayer.GodExperience:N0} (MAX LEVEL)");

        terminal.SetColor("cyan");
        terminal.Write("  Believers:      ");
        terminal.SetColor("bright_green");
        terminal.WriteLine($"{believers}");

        terminal.SetColor("cyan");
        terminal.Write("  Deeds Today:    ");
        terminal.SetColor("white");
        terminal.WriteLine($"{currentPlayer.DeedsLeft} / {deedsMax}");

        terminal.SetColor("cyan");
        terminal.Write("  Ascension Date: ");
        terminal.SetColor("gray");
        terminal.WriteLine($"{currentPlayer.AscensionDate:yyyy-MM-dd}");

        terminal.SetColor("cyan");
        terminal.Write("  Daily Exp:      ");
        terminal.SetColor("white");
        terminal.WriteLine($"+{believers * currentPlayer.GodLevel * GameConfig.GodBelieverExpPerLevel} from believers");

        // Show configured boons
        var boonLines = DivineBoonRegistry.GetEffectSummaryLines(currentPlayer.DivineBoonConfig);
        if (boonLines.Count > 0)
        {
            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("  Configured Boons:");
            foreach (var line in boonLines)
            {
                terminal.SetColor("white");
                terminal.WriteLine($"    • {line}");
            }
        }
        else
        {
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine("  No boons configured. Use [F] Favors to set boons for your followers.");
        }

        terminal.WriteLine("");
        await terminal.GetInputAsync("  Press Enter to return...");
    }

    #endregion

    #region Believers

    private async Task ShowBelievers()
    {
        terminal.ClearScreen();

        terminal.SetColor("bright_yellow");
        terminal.WriteLine("╔═════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine($"║{"YOUR FAITHFUL FLOCK".PadLeft((77 + 19) / 2).PadRight(77)}║");
        terminal.WriteLine("╚═════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        var believers = await GetBelieverListAsync(currentPlayer.DivineName);

        if (believers.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  You have no believers yet. Use Divine Deeds to recruit followers.");
        }
        else
        {
            terminal.SetColor("cyan");
            terminal.WriteLine($"  Total Believers: {believers.Count}");
            terminal.WriteLine("");

            int idx = 1;
            foreach (var believer in believers.Take(20))
            {
                terminal.SetColor("white");
                terminal.Write($"  {idx,2}. ");
                terminal.SetColor(believer.IsPlayer ? "bright_cyan" : "bright_green");
                string tag = believer.IsPlayer ? "[PLAYER] " : "";
                terminal.Write($"{tag}{believer.Name,-20}");
                terminal.SetColor("gray");
                string onTag = believer.IsPlayer && believer.IsOnline ? " [ONLINE]" : "";
                terminal.WriteLine($" Lvl {believer.Level,3}  {believer.Class}{onTag}");
                idx++;
            }

            if (believers.Count > 20)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  ... and {believers.Count - 20} more.");
            }
        }

        terminal.WriteLine("");
        await terminal.GetInputAsync("  Press Enter to return...");
    }

    #endregion

    #region Configure Boons

    private async Task ConfigureBoons()
    {
        int believers = CountBelievers(currentPlayer.DivineName);
        int totalBudget = DivineBoonRegistry.CalculateBudget(currentPlayer.GodLevel, believers);
        var available = DivineBoonRegistry.GetAvailableBoons(currentPlayer.GodAlignment);

        while (true)
        {
            terminal.ClearScreen();

            terminal.SetColor("bright_yellow");
            terminal.WriteLine("╔═════════════════════════════════════════════════════════════════════════════╗");
            terminal.WriteLine($"║{"CONFIGURE DIVINE FAVORS".PadLeft((77 + 23) / 2).PadRight(77)}║");
            terminal.WriteLine("╚═════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            // Budget display
            int spent = DivineBoonRegistry.CalculateSpent(currentPlayer.DivineBoonConfig);
            int baseBudget = Math.Max(1, currentPlayer.GodLevel) * GameConfig.GodBoonBudgetPerLevel;
            int concentration = Math.Max(0, GameConfig.GodBoonConcentrationMax - believers * GameConfig.GodBoonConcentrationPerBeliever);

            terminal.SetColor("cyan");
            terminal.Write("  Budget: ");
            terminal.SetColor("white");
            terminal.Write($"{spent}/{totalBudget} pts");
            terminal.SetColor("gray");
            terminal.WriteLine($"  (Base: {baseBudget} from Level {currentPlayer.GodLevel}  +  Focus: {concentration} from {believers} believers)");
            terminal.SetColor("cyan");
            terminal.Write("  Alignment: ");
            terminal.SetColor("white");
            terminal.WriteLine($"{currentPlayer.GodAlignment}");
            terminal.WriteLine("");

            // Show active boons
            var activeBoons = DivineBoonRegistry.ParseConfig(currentPlayer.DivineBoonConfig);
            if (activeBoons.Count > 0)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine("  ACTIVE BOONS:");
                int idx = 1;
                foreach (var (boonId, tier) in activeBoons)
                {
                    var boon = DivineBoonRegistry.GetBoon(boonId);
                    if (boon == null) continue;
                    string tierStr = tier switch { 1 => "I", 2 => "II", 3 => "III", _ => "" };
                    int cost = boon.CostPerTier * tier;
                    string alignTag = boon.Alignments.Length > 0 ? $"[{string.Join("/", boon.Alignments)}]" : "[Any]";

                    terminal.SetColor("white");
                    terminal.Write($"  {idx,2}. ");
                    terminal.SetColor("bright_green");
                    terminal.Write($"{boon.Name} {tierStr,-5}");
                    terminal.SetColor("gray");
                    terminal.Write($" — {boon.GetEffectDescription(tier),-30}");
                    terminal.SetColor("darkgray");
                    terminal.WriteLine($" {alignTag,-12} ({cost} pts)");
                    idx++;
                }
                terminal.WriteLine("");
            }

            // Show available boons (including ones that can be upgraded)
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("  AVAILABLE BOONS:");

            var allBoons = DivineBoonRegistry.AllBoons;
            var activeDict = activeBoons.ToDictionary(b => b.boonId, b => b.tier);
            int optNum = activeBoons.Count + 1;
            var optionMap = new Dictionary<int, (string boonId, int nextTier, int cost)>();

            foreach (var boon in allBoons)
            {
                int currentTier = activeDict.ContainsKey(boon.Id) ? activeDict[boon.Id] : 0;
                if (currentTier >= boon.MaxTier) continue; // Already maxed
                int nextTier = currentTier + 1;
                int addedCost = boon.CostPerTier; // Cost of one more tier
                bool canAfford = spent + addedCost <= totalBudget;
                bool alignmentMatch = boon.IsAvailableForAlignment(currentPlayer.GodAlignment);

                string tierStr = nextTier switch { 1 => "I", 2 => "II", 3 => "III", _ => "" };
                string alignTag = boon.Alignments.Length > 0 ? $"[{string.Join("/", boon.Alignments)}]" : "[Any]";
                string action = currentTier > 0 ? "upgrade to" : "add";
                string label = currentTier > 0 ? $"{boon.Name} → {tierStr}" : $"{boon.Name} {tierStr}";

                if (!alignmentMatch)
                {
                    terminal.SetColor("darkgray");
                    terminal.WriteLine($"  {optNum,2}. {label,-25} — {boon.Description,-28} {alignTag,-12} (locked)");
                }
                else if (!canAfford)
                {
                    terminal.SetColor("darkgray");
                    terminal.Write($"  {optNum,2}. ");
                    terminal.SetColor("gray");
                    terminal.WriteLine($"{label,-25} — {boon.GetEffectDescription(nextTier),-28} {alignTag,-12} (+{addedCost} pts) *");
                }
                else
                {
                    terminal.SetColor("white");
                    terminal.Write($"  {optNum,2}. ");
                    terminal.SetColor("bright_cyan");
                    terminal.Write($"{label,-25}");
                    terminal.SetColor("gray");
                    terminal.Write($" — {boon.GetEffectDescription(nextTier),-28}");
                    terminal.SetColor("darkgray");
                    terminal.WriteLine($" {alignTag,-12} (+{addedCost} pts)");
                    optionMap[optNum] = (boon.Id, nextTier, addedCost);
                }
                optNum++;
            }

            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine("  * = insufficient budget   locked = wrong alignment");
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine("  [#] Add/upgrade boon   [R#] Remove boon   [0] Done");

            string input = await terminal.GetInputAsync("\n  Choice: ");
            string trimmed = input.Trim().ToUpper();

            if (trimmed == "0" || trimmed == "") break;

            // Remove a boon: R1, R2, etc.
            if (trimmed.StartsWith("R") && int.TryParse(trimmed.Substring(1), out int removeIdx) &&
                removeIdx >= 1 && removeIdx <= activeBoons.Count)
            {
                activeBoons.RemoveAt(removeIdx - 1);
                currentPlayer.DivineBoonConfig = DivineBoonRegistry.SerializeConfig(activeBoons);
                spent = DivineBoonRegistry.CalculateSpent(currentPlayer.DivineBoonConfig);

                terminal.SetColor("bright_red");
                terminal.WriteLine("  Boon removed.");
                await Task.Delay(500);
                continue;
            }

            // Add/upgrade a boon
            if (int.TryParse(trimmed, out int addIdx) && optionMap.ContainsKey(addIdx))
            {
                var (boonId, nextTier, cost) = optionMap[addIdx];

                // Update or add
                bool found = false;
                for (int i = 0; i < activeBoons.Count; i++)
                {
                    if (activeBoons[i].boonId == boonId)
                    {
                        activeBoons[i] = (boonId, nextTier);
                        found = true;
                        break;
                    }
                }
                if (!found) activeBoons.Add((boonId, nextTier));

                currentPlayer.DivineBoonConfig = DivineBoonRegistry.SerializeConfig(activeBoons);
                spent = DivineBoonRegistry.CalculateSpent(currentPlayer.DivineBoonConfig);

                var boon = DivineBoonRegistry.GetBoon(boonId);
                terminal.SetColor("bright_green");
                terminal.WriteLine($"  {boon?.Name ?? boonId} configured!");
                await Task.Delay(500);
            }
        }

        // Save to DB
        var backend = SaveSystem.Instance?.Backend as SqlSaveBackend;
        if (backend != null && DoorMode.IsOnlineMode)
        {
            try
            {
                // Get username for current session
                string username = "";
                if (UsurperRemake.Server.SessionContext.IsActive)
                    username = UsurperRemake.Server.SessionContext.Current?.Username ?? "";
                if (string.IsNullOrEmpty(username) && DoorMode.IsOnlineMode)
                    username = DoorMode.GetPlayerName()?.ToLowerInvariant() ?? "";

                if (!string.IsNullOrEmpty(username))
                    await backend.SetGodBoonConfig(username, currentPlayer.DivineBoonConfig);
            }
            catch { /* DB unavailable */ }

            // Notify online followers
            NotifyOnlineFollowers(currentPlayer.DivineName, currentPlayer.DivineBoonConfig);
        }

        // Auto-save
        try { await SaveSystem.Instance.AutoSave(currentPlayer); } catch { }

        terminal.SetColor("bright_yellow");
        terminal.WriteLine("  Divine favors updated. Your followers will feel the change.");
        await Task.Delay(1000);
    }

    /// <summary>Notify online followers that their god changed boon config</summary>
    private static void NotifyOnlineFollowers(string divineName, string newConfig)
    {
        if (MudServer.Instance == null) return;
        var newEffects = DivineBoonRegistry.CalculateEffects(newConfig);

        foreach (var kvp in MudServer.Instance.ActiveSessions)
        {
            var session = kvp.Value;
            var player = session.Context?.Engine?.CurrentPlayer;
            if (player != null && player.WorshippedGod == divineName && !player.IsImmortal)
            {
                player.CachedBoonEffects = newEffects;
                session.EnqueueMessage(
                    $"\u001b[1;33m  ✦ Your patron {divineName} has reconfigured their divine favors! ✦\u001b[0m");
            }
        }
    }

    #endregion

    #region Divine Deeds

    private async Task PerformDivineDeeds()
    {
        if (currentPlayer.DeedsLeft <= 0)
        {
            terminal.WriteLine("");
            terminal.WriteLine("  Your divine power is spent for today. Rest until the morrow.", "gray");
            await terminal.GetInputAsync("  Press Enter to return...");
            return;
        }

        terminal.ClearScreen();

        string godTitle = GetGodTitle(currentPlayer.GodLevel);
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("╔═════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine($"║{"DIVINE DEEDS".PadLeft((77 + 12) / 2).PadRight(77)}║");
        terminal.WriteLine("╚═════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($"  Deeds remaining: {currentPlayer.DeedsLeft}");
        terminal.WriteLine("");

        WriteMenuOption("1", "Recruit Believer", "Convert a pagan or steal from a rival");
        WriteMenuOption("2", "Bless Follower", "Grant a combat buff to a believer");
        WriteMenuOption("3", "Smite Mortal", "Strike down a non-believer");
        WriteMenuOption("4", "Poison Relations", "Worsen relations between two mortals");
        WriteMenuOption("5", "Free Prisoner", "Release an imprisoned character");
        WriteMenuOption("6", "Proclamation", "Send a divine message to all");
        terminal.WriteLine("");
        WriteMenuOption("0", "Back", "Return to the Divine Realm");

        string input = await terminal.GetInputAsync("\n  Choose a deed: ");
        string choice = input.Trim();

        switch (choice)
        {
            case "1": await DeedRecruitBeliever(); break;
            case "2": await DeedBlessFollower(); break;
            case "3": await DeedSmiteMortal(); break;
            case "4": await DeedPoisonRelationship(); break;
            case "5": await DeedFreePrisoner(); break;
            case "6": await DeedProclamation(); break;
        }
    }

    private async Task DeedRecruitBeliever()
    {
        // Build combined target list: NPCs + players (in MUD mode)
        var targets = new List<DeedTarget>();

        // NPC targets — all eligible
        var npcs = NPCSpawnSystem.Instance?.ActiveNPCs?
            .Where(n => !n.IsDead && n.WorshippedGod != currentPlayer.DivineName)
            .OrderBy(n => n.Level)
            .ToList() ?? new();

        foreach (var npc in npcs)
        {
            string status = string.IsNullOrEmpty(npc.WorshippedGod) ? "Pagan" : $"Follows {npc.WorshippedGod}";
            targets.Add(new DeedTarget { Name = npc.DisplayName, Level = npc.Level, Status = status, NpcRef = npc });
        }

        // Player targets (MUD mode)
        var backend = SaveSystem.Instance?.Backend as SqlSaveBackend;
        if (backend != null && DoorMode.IsOnlineMode)
        {
            try
            {
                var mortals = await backend.GetMortalPlayers(100);
                foreach (var m in mortals)
                {
                    if (m.WorshippedGod == currentPlayer.DivineName) continue;
                    string status = string.IsNullOrEmpty(m.WorshippedGod) ? "Pagan" : $"Follows {m.WorshippedGod}";
                    string onTag = m.IsOnline ? " [ONLINE]" : "";
                    targets.Add(new DeedTarget
                    {
                        Name = m.DisplayName, Level = m.Level, Status = status + onTag,
                        IsPlayer = true, Username = m.Username, IsOnline = m.IsOnline
                    });
                }
            }
            catch { /* DB unavailable — show NPCs only */ }
        }

        if (targets.Count == 0)
        {
            terminal.WriteLine("  There are no mortals available to recruit.", "gray");
            await terminal.GetInputAsync("  Press Enter to return...");
            return;
        }

        var target = await PickTarget(targets, "RECRUIT BELIEVER", "bright_yellow", "Target #");
        if (target == null) return;
        bool isPagan = target.Status.Contains("Pagan");
        var rng = new Random();

        currentPlayer.DeedsLeft--;

        if (isPagan)
        {
            float chance = GameConfig.GodRecruitPaganChance;
            if (target.IsPlayer) chance *= GameConfig.GodRecruitPlayerMultiplier;

            if (rng.NextDouble() < chance)
            {
                int expGain = GameConfig.GodRecruitPaganExp;
                if (target.IsPlayer) expGain = (int)(expGain * GameConfig.GodRecruitPlayerExpMultiplier);

                if (target.IsPlayer)
                    await ApplyRecruitToPlayer(target, currentPlayer.DivineName);
                else if (target.NpcRef != null)
                    target.NpcRef.WorshippedGod = currentPlayer.DivineName;

                currentPlayer.GodExperience += expGain;
                terminal.WriteLine("");
                terminal.SetColor("bright_green");
                terminal.WriteLine($"  {target.Name} falls to their knees in worship!");
                terminal.SetColor("white");
                terminal.WriteLine($"  +{expGain} divine experience.");
                RecalculateGodLevel();

                if (target.IsPlayer)
                    NewsSystem.Instance?.Newsy(true, $"[DIVINE] {target.Name} has converted to {currentPlayer.DivineName}!");
            }
            else
            {
                terminal.WriteLine("");
                terminal.SetColor("gray");
                terminal.WriteLine($"  {target.Name} resists your divine call. Perhaps next time.");
            }
        }
        else
        {
            float chance = 0.25f;
            if (target.IsPlayer) chance *= GameConfig.GodRecruitPlayerMultiplier;

            terminal.WriteLine("");
            if (rng.NextDouble() < chance)
            {
                int expGain = GameConfig.GodRecruitStealExp;
                if (target.IsPlayer) expGain = (int)(expGain * GameConfig.GodRecruitPlayerExpMultiplier);

                string oldGod = target.Status.Replace(" [ONLINE]", "").Replace("Follows ", "");

                if (target.IsPlayer)
                    await ApplyRecruitToPlayer(target, currentPlayer.DivineName);
                else if (target.NpcRef != null)
                    target.NpcRef.WorshippedGod = currentPlayer.DivineName;

                currentPlayer.GodExperience += expGain;
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"  {target.Name} abandons {oldGod} and turns to you!");
                terminal.SetColor("white");
                terminal.WriteLine($"  +{expGain} divine experience.");
                RecalculateGodLevel();

                if (target.IsPlayer)
                    NewsSystem.Instance?.Newsy(true, $"[DIVINE] {target.Name} has converted to {currentPlayer.DivineName}!");
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  {target.Name}'s faith holds firm. You cannot turn them.");
            }
        }

        await terminal.GetInputAsync("  Press Enter to continue...");
    }

    private async Task DeedBlessFollower()
    {
        var believers = await GetBelieverListAsync(currentPlayer.DivineName);

        if (believers.Count == 0)
        {
            terminal.WriteLine("  You have no believers to bless.", "gray");
            await terminal.GetInputAsync("  Press Enter to return...");
            return;
        }

        // Convert believers to DeedTargets for paginated picker
        var targets = believers.Select(b => new DeedTarget
        {
            Name = b.Name, Level = b.Level,
            Status = b.Class + (b.IsPlayer && b.IsOnline ? " [ONLINE]" : ""),
            IsPlayer = b.IsPlayer, Username = b.Username, IsOnline = b.IsOnline
        }).ToList();

        var picked = await PickTarget(targets, "BLESS FOLLOWER", "bright_yellow", "Bless #");
        if (picked == null) return;

        var target = believers.First(b => b.Name == picked.Name && b.IsPlayer == picked.IsPlayer);
        int expGain = GameConfig.GodBlessExp;
        if (target.IsPlayer) expGain = (int)(expGain * GameConfig.GodBlessPlayerExpMultiplier);

        currentPlayer.DeedsLeft--;
        currentPlayer.GodExperience += expGain;

        if (target.IsPlayer)
        {
            var dt = new DeedTarget { Name = target.Name, Username = target.Username, IsPlayer = true, IsOnline = target.IsOnline };
            await ApplyBlessToPlayer(dt, currentPlayer.DivineName);
            NewsSystem.Instance?.Newsy(true, $"[DIVINE] {currentPlayer.DivineName} blessed {target.Name}!");
        }
        else
        {
            var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.DisplayName == target.Name);
            if (npc != null)
            {
                npc.DivineBlessingCombats = GameConfig.GodBlessCombatDuration;
                npc.DivineBlessingBonus = GameConfig.GodBlessBonusPercent;
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"  Your divine light flows into {target.Name}!");
        terminal.SetColor("white");
        terminal.WriteLine($"  They receive +{(int)(GameConfig.GodBlessBonusPercent * 100)}% damage/defense for {GameConfig.GodBlessCombatDuration} combats.");
        terminal.SetColor("gray");
        terminal.WriteLine($"  +{expGain} divine experience.");
        RecalculateGodLevel();

        await terminal.GetInputAsync("  Press Enter to continue...");
    }

    private async Task DeedSmiteMortal()
    {
        // Build combined target list: NPCs + players
        var targets = new List<DeedTarget>();

        var npcs = NPCSpawnSystem.Instance?.ActiveNPCs?
            .Where(n => !n.IsDead && n.WorshippedGod != currentPlayer.DivineName)
            .OrderByDescending(n => n.Level)
            .ToList() ?? new();

        foreach (var npc in npcs)
        {
            targets.Add(new DeedTarget
            {
                Name = npc.DisplayName, Level = npc.Level,
                Status = $"HP: {npc.HP}/{npc.MaxHP}", NpcRef = npc
            });
        }

        // Player targets (MUD mode)
        var backend = SaveSystem.Instance?.Backend as SqlSaveBackend;
        if (backend != null && DoorMode.IsOnlineMode)
        {
            try
            {
                var mortals = await backend.GetMortalPlayers(100);
                foreach (var m in mortals)
                {
                    if (m.WorshippedGod == currentPlayer.DivineName) continue;

                    // Check smite cooldown
                    string cooldownKey = $"{currentPlayer.DivineName}>{m.Username}";
                    if (_smiteCooldowns.TryGetValue(cooldownKey, out var lastSmite) &&
                        (DateTime.UtcNow - lastSmite).TotalMinutes < GameConfig.GodSmitePlayerCooldownMinutes)
                        continue;

                    string onTag = m.IsOnline ? " [ONLINE]" : "";
                    targets.Add(new DeedTarget
                    {
                        Name = m.DisplayName, Level = m.Level,
                        Status = $"HP: {m.HP}/{m.MaxHP}{onTag}",
                        IsPlayer = true, Username = m.Username, IsOnline = m.IsOnline,
                        HP = m.HP, MaxHP = m.MaxHP
                    });
                }
            }
            catch { /* DB unavailable */ }
        }

        if (targets.Count == 0)
        {
            terminal.WriteLine("  There are no non-believers to smite.", "gray");
            await terminal.GetInputAsync("  Press Enter to return...");
            return;
        }

        var target = await PickTarget(targets, "SMITE MORTAL", "bright_red", "Smite #");
        if (target == null) return;
        var rng = new Random();
        float smitePercent = GameConfig.GodSmiteMinPercent + (float)(rng.NextDouble() * (GameConfig.GodSmiteMaxPercent - GameConfig.GodSmiteMinPercent));

        int expGain = GameConfig.GodSmiteExp;
        if (target.IsPlayer) expGain = (int)(expGain * GameConfig.GodSmitePlayerExpMultiplier);

        currentPlayer.DeedsLeft--;
        currentPlayer.GodExperience += expGain;

        if (target.IsPlayer)
        {
            await ApplySmiteToPlayer(target, smitePercent, currentPlayer.DivineName);
            // Record cooldown
            _smiteCooldowns[$"{currentPlayer.DivineName}>{target.Username}"] = DateTime.UtcNow;

            long estimatedDamage = Math.Max(1, (long)(target.MaxHP * smitePercent));
            terminal.WriteLine("");
            terminal.SetColor("bright_red");
            terminal.WriteLine($"  Divine lightning strikes {target.Name}!");
            terminal.SetColor("white");
            terminal.WriteLine($"  They take ~{estimatedDamage} damage!");
            NewsSystem.Instance?.Newsy(true, $"[DIVINE] {currentPlayer.DivineName} struck {target.Name} with divine lightning!");
        }
        else if (target.NpcRef != null)
        {
            long damage = Math.Max(1, (long)(target.NpcRef.MaxHP * smitePercent));
            target.NpcRef.HP = Math.Max(1, target.NpcRef.HP - damage);

            terminal.WriteLine("");
            terminal.SetColor("bright_red");
            terminal.WriteLine($"  Divine lightning strikes {target.Name}!");
            terminal.SetColor("white");
            terminal.WriteLine($"  They take {damage} damage! (HP: {target.NpcRef.HP}/{target.NpcRef.MaxHP})");
        }

        terminal.SetColor("gray");
        terminal.WriteLine($"  +{expGain} divine experience.");
        RecalculateGodLevel();

        await terminal.GetInputAsync("  Press Enter to continue...");
    }

    private async Task DeedPoisonRelationship()
    {
        var npcs = NPCSpawnSystem.Instance?.ActiveNPCs?
            .Where(n => !n.IsDead)
            .OrderBy(n => n.DisplayName)
            .ToList() ?? new();

        if (npcs.Count < 2)
        {
            terminal.WriteLine("  Not enough mortals to manipulate.", "gray");
            await terminal.GetInputAsync("  Press Enter to return...");
            return;
        }

        int idx1 = await PickNPC(npcs, "POISON RELATIONSHIP  (33% chance)", "bright_magenta", "First mortal #");
        if (idx1 < 0) return;

        int idx2 = await PickNPC(npcs, $"POISON RELATIONSHIP  (vs {npcs[idx1].DisplayName})", "bright_magenta", "Second mortal #");
        if (idx2 < 0 || idx2 == idx1) return;

        // Adjust to 1-based for the existing logic below
        idx1++; idx2++;

        currentPlayer.DeedsLeft--;

        var rng = new Random();
        if (rng.NextDouble() < GameConfig.GodPoisonRelationshipChance)
        {
            currentPlayer.GodExperience += GameConfig.GodPoisonRelationshipExp;
            terminal.WriteLine("");
            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"  Dark whispers flow between {npcs[idx1 - 1].DisplayName} and {npcs[idx2 - 1].DisplayName}.");
            terminal.SetColor("white");
            terminal.WriteLine("  Their bond weakens.");
            terminal.SetColor("gray");
            terminal.WriteLine($"  +{GameConfig.GodPoisonRelationshipExp} divine experience.");

            // Actually worsen the relationship
            RelationshipSystem.UpdateRelationship(npcs[idx1 - 1], npcs[idx2 - 1], -1, 2);
            RecalculateGodLevel();
        }
        else
        {
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine("  Your influence fails to take hold. Their bond is too strong.");
        }

        await terminal.GetInputAsync("  Press Enter to continue...");
    }

    private async Task DeedFreePrisoner()
    {
        var prisoners = NPCSpawnSystem.Instance?.ActiveNPCs?
            .Where(n => n.DaysInPrison > 0)
            .ToList() ?? new();

        if (prisoners.Count == 0)
        {
            terminal.WriteLine("  There are no prisoners to free.", "gray");
            await terminal.GetInputAsync("  Press Enter to return...");
            return;
        }

        int pickedIdx = await PickNPC(prisoners, "FREE PRISONER", "bright_cyan", "Free #",
            npc => $"{npc.DaysInPrison} days remaining");
        if (pickedIdx < 0) return;

        var target = prisoners[pickedIdx];
        target.DaysInPrison = 0;
        currentPlayer.DeedsLeft--;
        currentPlayer.GodExperience += GameConfig.GodFreePrisonerExp;

        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"  The prison walls shimmer and {target.DisplayName} walks free!");
        terminal.SetColor("gray");
        terminal.WriteLine($"  +{GameConfig.GodFreePrisonerExp} divine experience.");
        RecalculateGodLevel();

        await terminal.GetInputAsync("  Press Enter to continue...");
    }

    private async Task DeedProclamation()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("  DIVINE PROCLAMATION");
        terminal.SetColor("gray");
        terminal.WriteLine("  Your words will echo across the realm.");
        terminal.WriteLine("");

        string message = await terminal.GetInputAsync("  Your proclamation (max 120 chars): ");
        if (string.IsNullOrWhiteSpace(message)) return;

        if (message.Length > 120) message = message.Substring(0, 120);

        currentPlayer.DeedsLeft--;
        currentPlayer.GodExperience += GameConfig.GodProclamationExp;

        // Broadcast via news system
        string godTitle = GetGodTitle(currentPlayer.GodLevel);
        string newsEntry = $"[DIVINE] {currentPlayer.DivineName} the {godTitle} proclaims: \"{message}\"";

        // Broadcast to all online players
        if (UsurperRemake.Server.SessionContext.IsActive)
            UsurperRemake.Server.RoomRegistry.Instance?.BroadcastGlobal(
                $"\u001b[1;33m  {newsEntry}\u001b[0m");

        // Write to news
        NewsSystem.Instance?.Newsy(true, newsEntry);

        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("  Your words thunder across the mortal realm!");
        terminal.SetColor("gray");
        terminal.WriteLine($"  +{GameConfig.GodProclamationExp} divine experience.");
        RecalculateGodLevel();

        await terminal.GetInputAsync("  Press Enter to continue...");
    }

    #endregion

    #region Immortal Rankings

    private async Task ShowImmortalRankings()
    {
        terminal.ClearScreen();

        terminal.SetColor("bright_yellow");
        terminal.WriteLine("╔═════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine($"║{"THE PANTHEON — IMMORTAL RANKINGS".PadLeft((77 + 32) / 2).PadRight(77)}║");
        terminal.WriteLine("╚═════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        var gods = await GetAllImmortalsAsync();

        if (gods.Count <= 1)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  You are the only god in the realm.");
            terminal.WriteLine("");
        }

        if (gods.Count > 0)
        {
            terminal.SetColor("cyan");
            terminal.Write($"  {"#",-4}{"Divine Name",-22}{"Title",-14}{"Lvl",4}{"Exp",12}{"Blvrs",7}{"Status",9}");
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine("  " + new string('─', 72));

            int rank = 1;
            foreach (var god in gods.OrderByDescending(g => g.GodExperience))
            {
                bool isYou = god.DivineName == currentPlayer.DivineName;
                string title = GetGodTitle(god.GodLevel);
                int believers = CountBelievers(god.DivineName);
                string status = isYou ? "YOU" : (god.IsOnline ? "ONLINE" : "OFFLINE");

                terminal.SetColor(isYou ? "bright_yellow" : "white");
                terminal.Write($"  {rank,-4}");
                terminal.SetColor(isYou ? "bright_yellow" : "bright_green");
                terminal.Write($"{god.DivineName,-22}");
                terminal.SetColor(isYou ? "bright_yellow" : "gray");
                terminal.Write($"{title,-14}");
                terminal.SetColor("white");
                terminal.Write($"{god.GodLevel,4}");
                terminal.Write($"{god.GodExperience,12:N0}");
                terminal.SetColor("bright_green");
                terminal.Write($"{believers,7}");
                terminal.SetColor(god.IsOnline || isYou ? "bright_green" : "gray");
                terminal.WriteLine($"{status,9}");
                rank++;
            }
        }

        terminal.WriteLine("");
        await terminal.GetInputAsync("  Press Enter to return...");
    }

    #endregion

    #region News

    private async Task ShowNews()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("╔═════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine($"║{"DIVINE & MORTAL NEWS".PadLeft((77 + 20) / 2).PadRight(77)}║");
        terminal.WriteLine("╚═════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        var allNews = NewsSystem.Instance?.GetTodaysNews() ?? new List<string>();
        var news = allNews.Count > 15 ? allNews.GetRange(allNews.Count - 15, 15) : allNews;
        if (news.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  No news at this time.");
        }
        else
        {
            foreach (var item in news)
            {
                bool isDivine = item.Contains("[DIVINE]");
                terminal.SetColor(isDivine ? "bright_yellow" : "white");
                terminal.WriteLine($"  {item}");
            }
        }

        terminal.WriteLine("");
        await terminal.GetInputAsync("  Press Enter to return...");
    }

    #endregion

    #region Proclamation (separate from deed)

    private async Task SendProclamation()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("  SEND A COMMENT TO YOUR BELIEVERS");
        terminal.SetColor("gray");
        terminal.WriteLine("  This message will appear as news (no deed cost).");
        terminal.WriteLine("");

        string message = await terminal.GetInputAsync("  Your message (max 120 chars): ");
        if (string.IsNullOrWhiteSpace(message)) return;

        if (message.Length > 120) message = message.Substring(0, 120);

        string godTitle = GetGodTitle(currentPlayer.GodLevel);
        string newsEntry = $"{currentPlayer.DivineName} the {godTitle} speaks: \"{message}\"";

        NewsSystem.Instance?.Newsy(true,newsEntry);

        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("  Your words have been recorded in the annals.");

        await terminal.GetInputAsync("  Press Enter to return...");
    }

    #endregion

    #region Visit Manwe (Level Up)

    private async Task VisitManwe()
    {
        terminal.ClearScreen();

        terminal.SetColor("bright_magenta");
        terminal.WriteLine("  You approach the throne of Manwe, the Supreme Creator.");
        terminal.WriteLine("");

        await Task.Delay(500);

        if (currentPlayer.GodLevel >= GameConfig.GodMaxLevel)
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("  \"You have reached the pinnacle of divine power.\"");
            terminal.SetColor("white");
            terminal.WriteLine("  \"There is nothing more I can teach you.\"");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("  \"Go forth and shape the world as you see fit.\"");
        }
        else
        {
            long nextExp = GetNextLevelExp(currentPlayer.GodLevel);
            if (currentPlayer.GodExperience >= nextExp)
            {
                // Level up!
                currentPlayer.GodLevel++;
                string newTitle = GetGodTitle(currentPlayer.GodLevel);
                int newDeeds = GetDeedsPerDay(currentPlayer.GodLevel);

                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"  Manwe's eyes glow with approval.");
                terminal.WriteLine("");
                await Task.Delay(500);

                terminal.SetColor("white");
                terminal.WriteLine($"  \"Your power grows, {currentPlayer.DivineName}.\"");
                terminal.SetColor("bright_cyan");
                terminal.WriteLine($"  \"You are now a {newTitle}.\"");
                terminal.WriteLine("");
                terminal.SetColor("bright_green");
                terminal.WriteLine($"  GOD RANK UP! Level {currentPlayer.GodLevel} — {newTitle}");
                terminal.SetColor("white");
                terminal.WriteLine($"  Daily deeds increased to {newDeeds}.");

                // Reset deeds on level up
                currentPlayer.DeedsLeft = newDeeds;

                // News
                NewsSystem.Instance?.Newsy(true,
                    $"[DIVINE] {currentPlayer.DivineName} has ascended to {newTitle}!");
            }
            else
            {
                terminal.SetColor("white");
                terminal.WriteLine($"  \"You are not yet ready, {currentPlayer.DivineName}.\"");
                terminal.SetColor("gray");
                terminal.WriteLine($"  Experience: {currentPlayer.GodExperience:N0} / {nextExp:N0}");
                terminal.SetColor("white");
                terminal.WriteLine($"  \"Gather more believers. Perform more deeds.\"");
            }
        }

        terminal.WriteLine("");
        await terminal.GetInputAsync("  Press Enter to return...");
    }

    #endregion

    #region Renounce Immortality

    private async Task<bool> RenounceImmortality()
    {
        terminal.ClearScreen();

        terminal.SetColor("bright_red");
        terminal.WriteLine("╔═════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine($"║{"RENOUNCE IMMORTALITY".PadLeft((77 + 20) / 2).PadRight(77)}║");
        terminal.WriteLine("╚═════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("  WARNING: This is permanent and cannot be undone!");
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine("  - Your divine record will be erased");
        terminal.WriteLine("  - All believers will lose their faith");
        terminal.WriteLine("  - Your character data will be wiped");
        terminal.WriteLine("  - You will start fresh (cycle bonuses preserved)");
        terminal.WriteLine("");

        string confirm1 = await terminal.GetInputAsync("  Are you sure? (Type YES to confirm): ");
        if (confirm1.Trim().ToUpper() != "YES") return false;

        string confirm2 = await terminal.GetInputAsync("  This is truly permanent. Type your divine name to confirm: ");
        if (confirm2.Trim() != currentPlayer.DivineName) return false;

        // Auto-abdicate if player is the king
        if (currentPlayer.King)
        {
            CastleLocation.AbdicatePlayerThrone(currentPlayer, "abdicated the throne to start anew");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("  The throne has been abdicated as you renounce your divine power.");
            terminal.WriteLine("");
            await Task.Delay(1500);
        }

        // Clear all believers
        var believers = NPCSpawnSystem.Instance?.ActiveNPCs?
            .Where(n => n.WorshippedGod == currentPlayer.DivineName)
            .ToList() ?? new();
        foreach (var npc in believers)
        {
            npc.WorshippedGod = "";
        }

        // News
        NewsSystem.Instance?.Newsy(true,
            $"[DIVINE] {currentPlayer.DivineName} has fallen from the heavens! Their believers are left godless.");

        // Clear immortal state
        currentPlayer.IsImmortal = false;
        currentPlayer.DivineName = "";
        currentPlayer.GodLevel = 0;
        currentPlayer.GodExperience = 0;
        currentPlayer.DeedsLeft = 0;
        currentPlayer.GodAlignment = "";

        terminal.WriteLine("");
        terminal.SetColor("bright_red");
        terminal.WriteLine("  The divine light fades from your eyes...");
        terminal.SetColor("white");
        terminal.WriteLine("  You are mortal once more.");
        terminal.WriteLine("");

        await Task.Delay(1500);

        // Signal NG+ restart (preserves cycle bonuses)
        GameEngine.Instance.PendingNewGamePlus = true;

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("  The wheel turns. A new life awaits.");
        terminal.WriteLine("");

        await terminal.GetInputAsync("  Press Enter to begin anew...");
        return true;
    }

    #endregion

    #region Helper Methods

    /// <summary>Get the title for a god level (1-9)</summary>
    public static string GetGodTitle(int level)
    {
        int idx = Math.Clamp(level, 1, GameConfig.GodMaxLevel) - 1;
        return GameConfig.GodTitles[idx];
    }

    /// <summary>Get deeds per day for a god level</summary>
    public static int GetDeedsPerDay(int level)
    {
        int idx = Math.Clamp(level, 1, GameConfig.GodMaxLevel) - 1;
        return GameConfig.GodDeedsPerDay[idx];
    }

    /// <summary>Get exp needed for the next level</summary>
    public static long GetNextLevelExp(int currentLevel)
    {
        if (currentLevel >= GameConfig.GodMaxLevel) return long.MaxValue;
        return GameConfig.GodExpThresholds[currentLevel]; // array is 0-indexed, level is 1-based, so level N needs threshold[N]
    }

    /// <summary>Recalculate god level from experience (won't auto-promote, just ensures consistency)</summary>
    private void RecalculateGodLevel()
    {
        // Don't auto-level — must visit Manwe. But ensure level doesn't exceed what exp allows.
    }

    /// <summary>Count NPCs (and player believers in MUD mode) that worship a given divine name</summary>
    public static int CountBelievers(string divineName)
    {
        if (string.IsNullOrEmpty(divineName)) return 0;
        int npcCount = NPCSpawnSystem.Instance?.ActiveNPCs?
            .Count(n => !n.IsDead && n.WorshippedGod == divineName) ?? 0;

        // In MUD mode, also count player believers
        if (DoorMode.IsOnlineMode)
        {
            try
            {
                var backend = SaveSystem.Instance?.Backend as SqlSaveBackend;
                if (backend != null)
                    npcCount += backend.CountPlayerBelievers(divineName).GetAwaiter().GetResult();
            }
            catch { /* DB unavailable */ }
        }

        return npcCount;
    }

    /// <summary>Get list of believer info for display (async, includes players in MUD mode)</summary>
    private async Task<List<BelieverInfo>> GetBelieverListAsync(string divineName)
    {
        if (string.IsNullOrEmpty(divineName)) return new();

        var list = new List<BelieverInfo>();

        // NPC believers
        var npcs = NPCSpawnSystem.Instance?.ActiveNPCs?
            .Where(n => !n.IsDead && n.WorshippedGod == divineName)
            .ToList() ?? new();

        foreach (var npc in npcs)
        {
            list.Add(new BelieverInfo
            {
                Name = npc.DisplayName,
                Level = npc.Level,
                Class = npc.Class.ToString()
            });
        }

        // Player believers (MUD mode)
        var backend = SaveSystem.Instance?.Backend as SqlSaveBackend;
        if (backend != null && DoorMode.IsOnlineMode)
        {
            try
            {
                var mortals = await backend.GetMortalPlayers(50);
                foreach (var m in mortals.Where(p => p.WorshippedGod == divineName))
                {
                    list.Add(new BelieverInfo
                    {
                        Name = m.DisplayName,
                        Level = m.Level,
                        Class = ((CharacterClass)m.ClassId).ToString(),
                        IsPlayer = true,
                        IsOnline = m.IsOnline,
                        Username = m.Username
                    });
                }
            }
            catch { /* DB unavailable */ }
        }

        return list.OrderByDescending(b => b.Level).ToList();
    }

    /// <summary>Get all immortal player-gods (for rankings)</summary>
    private async Task<List<ImmortalInfo>> GetAllImmortalsAsync()
    {
        var list = new List<ImmortalInfo>();

        // Always include current player
        list.Add(new ImmortalInfo
        {
            DivineName = currentPlayer.DivineName,
            GodLevel = currentPlayer.GodLevel,
            GodExperience = currentPlayer.GodExperience,
            GodAlignment = currentPlayer.GodAlignment,
            IsOnline = true
        });

        // In MUD mode, query other immortals from DB
        var backend = SaveSystem.Instance?.Backend as SqlSaveBackend;
        if (backend != null && DoorMode.IsOnlineMode)
        {
            try
            {
                var immortals = await backend.GetImmortalPlayers();
                foreach (var god in immortals)
                {
                    // Don't duplicate self
                    if (god.DivineName == currentPlayer.DivineName) continue;
                    list.Add(new ImmortalInfo
                    {
                        DivineName = god.DivineName,
                        GodLevel = god.GodLevel,
                        GodExperience = god.GodExperience,
                        GodAlignment = god.GodAlignment,
                        IsOnline = god.IsOnline,
                        Username = god.Username
                    });
                }
            }
            catch { /* DB unavailable */ }
        }

        return list;
    }

    /// <summary>Calculate sacrifice power from gold amount (original scale from TEMPLE.PAS)</summary>
    public static int GetSacrificePower(long gold)
    {
        for (int i = 0; i < GameConfig.SacrificeTiers.Length; i++)
        {
            if (gold <= GameConfig.SacrificeTiers[i])
                return GameConfig.SacrificePower[i];
        }
        return GameConfig.SacrificePower[GameConfig.SacrificePower.Length - 1];
    }

    #endregion

    #region Paginated Target Selection

    /// <summary>
    /// Display a paginated list of DeedTargets and let the player pick one.
    /// Returns the selected target, or null if cancelled.
    /// </summary>
    private async Task<DeedTarget?> PickTarget(List<DeedTarget> targets, string title, string titleColor, string prompt)
    {
        if (targets.Count == 0) return null;

        const int pageSize = 15;
        int page = 0;
        int totalPages = (targets.Count + pageSize - 1) / pageSize;

        while (true)
        {
            terminal.ClearScreen();
            terminal.SetColor(titleColor);
            terminal.WriteLine($"  {title}");
            terminal.WriteLine("");

            int start = page * pageSize;
            int end = Math.Min(start + pageSize, targets.Count);

            for (int i = start; i < end; i++)
            {
                var t = targets[i];
                terminal.SetColor("white");
                terminal.Write($"  {i + 1,3}. ");
                terminal.SetColor(t.IsPlayer ? "bright_cyan" : "bright_green");
                string tag = t.IsPlayer ? "[PLAYER] " : "";
                terminal.Write($"{tag}{t.Name,-20}");
                terminal.SetColor("gray");
                terminal.WriteLine($" Lvl {t.Level,3}  {t.Status}");
            }

            terminal.WriteLine("");
            if (totalPages > 1)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  Page {page + 1}/{totalPages}  |  [N]ext  [P]rev  |  {targets.Count} total");
                terminal.WriteLine("");
            }

            string input = await terminal.GetInputAsync($"  {prompt} (0 to cancel): ");
            string upper = input.Trim().ToUpper();

            if (upper == "N" && page < totalPages - 1) { page++; continue; }
            if (upper == "P" && page > 0) { page--; continue; }

            if (!int.TryParse(input, out int idx) || idx < 1 || idx > targets.Count) return null;
            return targets[idx - 1];
        }
    }

    /// <summary>
    /// Display a paginated list of NPCs and let the player pick one.
    /// Returns the selected NPC index (0-based), or -1 if cancelled.
    /// </summary>
    private async Task<int> PickNPC(List<NPC> npcs, string title, string titleColor, string prompt, Func<NPC, string> extraInfo = null)
    {
        if (npcs.Count == 0) return -1;

        const int pageSize = 15;
        int page = 0;
        int totalPages = (npcs.Count + pageSize - 1) / pageSize;

        while (true)
        {
            terminal.ClearScreen();
            terminal.SetColor(titleColor);
            terminal.WriteLine($"  {title}");
            terminal.WriteLine("");

            int start = page * pageSize;
            int end = Math.Min(start + pageSize, npcs.Count);

            for (int i = start; i < end; i++)
            {
                terminal.SetColor("white");
                terminal.Write($"  {i + 1,3}. ");
                terminal.SetColor("bright_green");
                terminal.Write($"{npcs[i].DisplayName,-20}");
                terminal.SetColor("gray");
                if (extraInfo != null)
                    terminal.WriteLine($" {extraInfo(npcs[i])}");
                else
                    terminal.WriteLine("");
            }

            terminal.WriteLine("");
            if (totalPages > 1)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  Page {page + 1}/{totalPages}  |  [N]ext  [P]rev  |  {npcs.Count} total");
                terminal.WriteLine("");
            }

            string input = await terminal.GetInputAsync($"  {prompt} (0 to cancel): ");
            string upper = input.Trim().ToUpper();

            if (upper == "N" && page < totalPages - 1) { page++; continue; }
            if (upper == "P" && page > 0) { page--; continue; }

            if (!int.TryParse(input, out int idx) || idx < 1 || idx > npcs.Count) return -1;
            return idx - 1;
        }
    }

    #endregion

    #region Player Interaction Helpers

    /// <summary>Apply a divine blessing to a player (online: in-memory; offline: DB atomic update)</summary>
    private async Task ApplyBlessToPlayer(DeedTarget target, string godName)
    {
        var backend = SaveSystem.Instance?.Backend as SqlSaveBackend;
        if (backend == null) return;

        // Try online first
        if (target.IsOnline && MudServer.Instance != null &&
            MudServer.Instance.ActiveSessions.TryGetValue(target.Username.ToLowerInvariant(), out var session))
        {
            var player = session.Context?.Engine?.CurrentPlayer;
            if (player != null)
            {
                player.DivineBlessingCombats = GameConfig.GodBlessCombatDuration;
                player.DivineBlessingBonus = GameConfig.GodBlessBonusPercent;
                session.EnqueueMessage(
                    $"\u001b[1;36m  ✦ The god {godName} has blessed you! +{(int)(GameConfig.GodBlessBonusPercent * 100)}% damage/defense for {GameConfig.GodBlessCombatDuration} combats. ✦\u001b[0m");
                return;
            }
        }

        // Offline: atomic DB update + message
        await backend.ApplyDivineBlessing(target.Username, GameConfig.GodBlessCombatDuration, GameConfig.GodBlessBonusPercent);
        await backend.SendMessage(godName, target.Username, "divine",
            $"The god {godName} blessed you! +{(int)(GameConfig.GodBlessBonusPercent * 100)}% damage/defense for {GameConfig.GodBlessCombatDuration} combats.");
    }

    /// <summary>Apply a divine smite to a player (online: in-memory; offline: DB atomic update)</summary>
    private async Task ApplySmiteToPlayer(DeedTarget target, float damagePercent, string godName)
    {
        var backend = SaveSystem.Instance?.Backend as SqlSaveBackend;
        if (backend == null) return;

        // Try online first
        if (target.IsOnline && MudServer.Instance != null &&
            MudServer.Instance.ActiveSessions.TryGetValue(target.Username.ToLowerInvariant(), out var session))
        {
            var player = session.Context?.Engine?.CurrentPlayer;
            if (player != null)
            {
                long damage = Math.Max(1, (long)(player.MaxHP * damagePercent));
                player.HP = Math.Max(1, player.HP - damage);
                session.EnqueueMessage(
                    $"\u001b[1;31m  ⚡ The god {godName} has struck you with divine lightning! You take {damage} damage! ⚡\u001b[0m");
                return;
            }
        }

        // Offline: atomic DB update + message
        await backend.ApplyDivineSmite(target.Username, damagePercent);
        await backend.SendMessage(godName, target.Username, "divine",
            $"The god {godName} struck you with divine lightning while you were away!");
    }

    /// <summary>Apply recruitment to a player (online: in-memory; offline: DB atomic update)</summary>
    private async Task ApplyRecruitToPlayer(DeedTarget target, string godName)
    {
        var backend = SaveSystem.Instance?.Backend as SqlSaveBackend;
        if (backend == null) return;

        // Try online first
        if (target.IsOnline && MudServer.Instance != null &&
            MudServer.Instance.ActiveSessions.TryGetValue(target.Username.ToLowerInvariant(), out var session))
        {
            var player = session.Context?.Engine?.CurrentPlayer;
            if (player != null)
            {
                player.WorshippedGod = godName;
                session.EnqueueMessage(
                    $"\u001b[1;33m  ✦ A divine presence fills your soul... You now worship {godName}! ✦\u001b[0m");
                return;
            }
        }

        // Offline: atomic DB update + message
        await backend.SetPlayerWorshippedGod(target.Username, godName);
        await backend.SendMessage(godName, target.Username, "divine",
            $"The god {godName} has claimed you as a believer!");
    }

    #endregion

    #region Data Classes

    private class DeedTarget
    {
        public string Name { get; set; } = "";
        public int Level { get; set; }
        public string Status { get; set; } = "";
        public bool IsPlayer { get; set; }
        public bool IsOnline { get; set; }
        public string Username { get; set; } = "";
        public NPC? NpcRef { get; set; }
        public long HP { get; set; }
        public long MaxHP { get; set; }
    }

    private class BelieverInfo
    {
        public string Name { get; set; } = "";
        public int Level { get; set; }
        public string Class { get; set; } = "";
        public bool IsPlayer { get; set; }
        public bool IsOnline { get; set; }
        public string Username { get; set; } = "";
    }

    private class ImmortalInfo
    {
        public string DivineName { get; set; } = "";
        public int GodLevel { get; set; }
        public long GodExperience { get; set; }
        public string GodAlignment { get; set; } = "";
        public bool IsOnline { get; set; }
        public string Username { get; set; } = "";
    }

    #endregion
}
