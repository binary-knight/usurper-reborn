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

        // 1.2.0 Temple gods piece 2: an immortal without a domain (ascended before domains, or who
        // left it for later) is asked once on arrival until one is chosen
        if (player.IsImmortal && GodBoonSystem.ParseDomain(player.DivineDomain) == GodDomain.None)
            await GodDomainPicker.PickAsync(player, term);

        await RunPantheonLoop();
    }

    private async Task RunPantheonLoop()
    {
        bool exitLoop = false;
        while (!exitLoop)
        {
            // 1.2.0: a god boon update another session left pending (a player-god's reconfig,
            // domain or recruit) is applied here, in this player's own session
            GodBoonSystem.ApplyPendingBoonRecalc(currentPlayer);
            // Phase 5: Electron mode emits Pantheon (divine realm) menu state.
            if (GameConfig.ElectronMode)
            {
                EmitElectronEvents();
            }
            else
            {
                ShowPantheonMenu();
            }
            string input = await terminal.GetInputAsync(Loc.Get("pantheon.prompt_divine_will"));
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
                case "H":
                    await UsurperRemake.Systems.FounderStatueSystem.ShowStatuesAt(
                        UsurperRemake.Data.FounderStatueData.StatueLocationTag.Pantheon, terminal);
                    break;
                case "R":
                    bool renounced = await RenounceImmortality();
                    if (renounced) return;
                    break;
                case "Q":
                    throw new LocationExitException(GameLocation.NoWhere);
                default:
                    terminal.WriteLine(Loc.Get("pantheon.invalid_choice"), "gray");
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

        WriteBoxHeader(Loc.Get("pantheon.divine_realm"), "bright_yellow", 77);
        terminal.WriteLine("");

        terminal.SetColor("bright_yellow");
        terminal.Write(Loc.Get("pantheon.welcome"));
        terminal.SetColor("white");
        terminal.Write($"{currentPlayer.DivineName}");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("pantheon.the_title", godTitle));

        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("pantheon.status_line", currentPlayer.GodAlignment, believers, currentPlayer.DeedsLeft, deedsMax));
        terminal.WriteLine("");

        // Menu
        WriteMenuOption("S", Loc.Get("pantheon.menu_status"), Loc.Get("pantheon.menu_status_desc"));
        WriteMenuOption("B", Loc.Get("pantheon.menu_believers"), Loc.Get("pantheon.menu_believers_desc"));
        WriteMenuOption("D", Loc.Get("pantheon.menu_deeds"), Loc.Get("pantheon.menu_deeds_desc", currentPlayer.DeedsLeft));
        WriteMenuOption("F", Loc.Get("pantheon.menu_favors"), Loc.Get("pantheon.menu_favors_desc"));
        WriteMenuOption("I", Loc.Get("pantheon.menu_immortals"), Loc.Get("pantheon.menu_immortals_desc"));
        WriteMenuOption("N", Loc.Get("pantheon.menu_news"), Loc.Get("pantheon.menu_news_desc"));
        WriteMenuOption("C", Loc.Get("pantheon.menu_comment"), Loc.Get("pantheon.menu_comment_desc"));
        WriteMenuOption("V", Loc.Get("pantheon.menu_visit_manwe"), Loc.Get("pantheon.menu_visit_manwe_desc"));
        WriteMenuOption("H", "Hall of the Ascended", "Walk among the alpha-era founder statues");
        terminal.WriteLine("");
        WriteMenuOption("R", Loc.Get("pantheon.menu_renounce"), Loc.Get("pantheon.menu_renounce_desc"));
        WriteMenuOption("Q", Loc.Get("pantheon.menu_quit"), Loc.Get("pantheon.menu_quit_desc"));
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

        WriteBoxHeader(Loc.Get("pantheon.divine_status"), "bright_yellow", 77);
        terminal.WriteLine("");

        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"  {currentPlayer.DivineName}{Loc.Get("pantheon.the_title", godTitle)}");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("pantheon.mortal_name_label"));
        terminal.SetColor("white");
        terminal.WriteLine($"{currentPlayer.Name2}");

        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("pantheon.alignment_label"));
        terminal.SetColor("white");
        terminal.WriteLine($"{currentPlayer.GodAlignment}");

        // 1.2.0 Temple gods piece 2: the god's domain and its boon's current scale for followers
        var ownDomain = GodBoonSystem.ParseDomain(currentPlayer.DivineDomain);
        int ownScale = 0;
        if (ownDomain != GodDomain.None && DoorMode.IsOnlineMode && SaveSystem.Instance?.Backend is SqlSaveBackend domainBackend)
        {
            try { ownScale = (await GodBoonSystem.PlayerGodBoonAsync(currentPlayer.DivineName, domainBackend, DateTime.UtcNow)).ScalePct; }
            catch { /* DB unavailable: shown at the floor */ }
        }
        if (ownDomain != GodDomain.None && ownScale <= 0) ownScale = GameConfig.GodPlayerBoonFloorPct;
        terminal.SetColor("cyan");
        terminal.WriteLine(ownDomain == GodDomain.None
            ? Loc.Get("god.player_domain_none")
            : Loc.Get("god.player_domain_line", GodBoonSystem.DomainName(ownDomain), ownScale));

        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("pantheon.god_rank_label"));
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("pantheon.god_rank_value", currentPlayer.GodLevel, godTitle));

        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("pantheon.experience_label"));
        terminal.SetColor("white");
        if (currentPlayer.GodLevel < GameConfig.GodMaxLevel)
            terminal.WriteLine($"{currentPlayer.GodExperience:N0} / {nextLevelExp:N0}");
        else
            terminal.WriteLine($"{currentPlayer.GodExperience:N0}{Loc.Get("pantheon.max_level")}");

        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("pantheon.believers_label"));
        terminal.SetColor("bright_green");
        terminal.WriteLine($"{believers}");

        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("pantheon.deeds_today_label"));
        terminal.SetColor("white");
        terminal.WriteLine($"{currentPlayer.DeedsLeft} / {deedsMax}");

        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("pantheon.ascension_date_label"));
        terminal.SetColor("gray");
        terminal.WriteLine(GameConfig.FormatDate(currentPlayer.AscensionDate, currentPlayer.DateFormatPreference));

        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("pantheon.daily_exp_label"));
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("pantheon.daily_exp_value", DailyBelieverExp(believers, currentPlayer.GodLevel)));

        // Show configured boons
        var boonLines = DivineBoonRegistry.GetEffectSummaryLines(currentPlayer.DivineBoonConfig);
        if (boonLines.Count > 0)
        {
            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("pantheon.configured_boons"));
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
            terminal.WriteLine(Loc.Get("pantheon.no_boons"));
        }

        terminal.WriteLine("");
        await terminal.PressAnyKey(Loc.Get("pantheon.press_enter_return"));
    }

    #endregion

    #region Believers

    private async Task ShowBelievers()
    {
        terminal.ClearScreen();

        WriteBoxHeader(Loc.Get("pantheon.faithful_flock"), "bright_yellow", 77);
        terminal.WriteLine("");

        var believers = await GetBelieverListAsync(currentPlayer.DivineName);

        if (believers.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("pantheon.no_believers"));
        }
        else
        {
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("pantheon.total_believers", believers.Count));
            terminal.WriteLine("");

            int idx = 1;
            foreach (var believer in believers.Take(20))
            {
                terminal.SetColor("white");
                terminal.Write($"  {idx,2}. ");
                terminal.SetColor(believer.IsPlayer ? "bright_cyan" : "bright_green");
                string tag = believer.IsPlayer ? Loc.Get("pantheon.believer_player_tag") : "";
                terminal.Write($"{tag}{believer.Name,-20}");
                terminal.SetColor("gray");
                string onTag = believer.IsPlayer && believer.IsOnline ? Loc.Get("pantheon.believer_online_tag") : "";
                terminal.WriteLine($" {Loc.Get("pantheon.rankings_col_lvl")} {believer.Level,3}  {believer.Class}{onTag}");
                idx++;
            }

            if (believers.Count > 20)
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("pantheon.and_more", believers.Count - 20));
            }
        }

        terminal.WriteLine("");
        await terminal.PressAnyKey(Loc.Get("pantheon.press_enter_return"));
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

            WriteBoxHeader(Loc.Get("pantheon.configure_favors"), "bright_yellow", 77);
            terminal.WriteLine("");

            // Budget display
            int spent = DivineBoonRegistry.CalculateSpent(currentPlayer.DivineBoonConfig);
            int baseBudget = Math.Max(1, currentPlayer.GodLevel) * GameConfig.GodBoonBudgetPerLevel;
            int concentration = Math.Max(0, GameConfig.GodBoonConcentrationMax - believers * GameConfig.GodBoonConcentrationPerBeliever);

            terminal.SetColor("cyan");
            terminal.Write(Loc.Get("pantheon.budget_label"));
            terminal.SetColor("white");
            terminal.Write(Loc.Get("pantheon.budget_value", spent, totalBudget));
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("pantheon.budget_breakdown", baseBudget, currentPlayer.GodLevel, concentration, believers));
            terminal.SetColor("cyan");
            terminal.Write(Loc.Get("pantheon.alignment_label"));
            terminal.SetColor("white");
            terminal.WriteLine($"{currentPlayer.GodAlignment}");
            terminal.WriteLine("");

            // Show active boons
            var activeBoons = DivineBoonRegistry.ParseConfig(currentPlayer.DivineBoonConfig);
            if (activeBoons.Count > 0)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("pantheon.active_boons_header"));
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
                    terminal.Write($" -- {boon.GetEffectDescription(tier),-29}");
                    terminal.SetColor("darkgray");
                    terminal.WriteLine($" {alignTag,-12} ({cost} pts)");
                    idx++;
                }
                terminal.WriteLine("");
            }

            // Show available boons (including ones that can be upgraded)
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("pantheon.available_boons_header"));

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
                    terminal.WriteLine($"  {optNum,2}. {label,-25} -- {boon.Description,-27} {alignTag,-12} {Loc.Get("pantheon.boon_locked")}");
                }
                else if (!canAfford)
                {
                    terminal.SetColor("darkgray");
                    terminal.Write($"  {optNum,2}. ");
                    terminal.SetColor("gray");
                    terminal.WriteLine($"{label,-25} -- {boon.GetEffectDescription(nextTier),-27} {alignTag,-12} (+{addedCost} pts) *");
                }
                else
                {
                    terminal.SetColor("white");
                    terminal.Write($"  {optNum,2}. ");
                    terminal.SetColor("bright_cyan");
                    terminal.Write($"{label,-25}");
                    terminal.SetColor("gray");
                    terminal.Write($" -- {boon.GetEffectDescription(nextTier),-27}");
                    terminal.SetColor("darkgray");
                    terminal.WriteLine($" {alignTag,-12} (+{addedCost} pts)");
                    optionMap[optNum] = (boon.Id, nextTier, addedCost);
                }
                optNum++;
            }

            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("pantheon.boon_legend"));
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("pantheon.boon_menu_help"));

            string input = await terminal.GetInputAsync(Loc.Get("pantheon.boon_choice_prompt"));
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
                terminal.WriteLine(Loc.Get("pantheon.boon_removed"));
                await Pacing.Wait(500);
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
                terminal.WriteLine(Loc.Get("pantheon.boon_configured", boon?.Name ?? boonId));
                await Pacing.Wait(500);
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

        // Auto-save. v0.65.5 (T1-5): log instead of swallowing so a failed favor-update save is visible.
        try { await SaveSystem.Instance.AutoSave(currentPlayer); }
        catch (Exception saveEx) { DebugLogger.Instance.LogError("SAVE", $"Pantheon favor autosave failed: {saveEx.Message}"); }

        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("pantheon.favors_updated"));
        await Pacing.Wait(1000);
    }

    /// <summary>Notify online followers that their god changed boon config</summary>
    private static void NotifyOnlineFollowers(string divineName, string newConfig)
    {
        if (MudServer.Instance == null) return;

        foreach (var kvp in MudServer.Instance.ActiveSessions)
        {
            var session = kvp.Value;
            var player = session.Context?.Engine?.CurrentPlayer;
            if (player != null && player.WorshippedGod == divineName && !player.IsImmortal)
            {
                GodBoonSystem.SetConfiguredBoons(player, newConfig);   // 1.2.0: max HP and mana follow at the follower's next safe point
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
            terminal.WriteLine(Loc.Get("pantheon.no_deeds_left"), "gray");
            await terminal.PressAnyKey(Loc.Get("pantheon.press_enter_return"));
            return;
        }

        terminal.ClearScreen();

        string godTitle = GetGodTitle(currentPlayer.GodLevel);
        WriteBoxHeader(Loc.Get("pantheon.divine_deeds"), "bright_yellow", 77);
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("pantheon.deeds_remaining", currentPlayer.DeedsLeft));
        terminal.WriteLine("");

        WriteMenuOption("1", Loc.Get("pantheon.menu_recruit"), Loc.Get("pantheon.menu_recruit_desc"));
        WriteMenuOption("2", Loc.Get("pantheon.menu_bless"), Loc.Get("pantheon.menu_bless_desc"));
        WriteMenuOption("3", Loc.Get("pantheon.menu_smite"), Loc.Get("pantheon.menu_smite_desc"));
        WriteMenuOption("4", Loc.Get("pantheon.menu_poison"), Loc.Get("pantheon.menu_poison_desc"));
        WriteMenuOption("5", Loc.Get("pantheon.menu_free"), Loc.Get("pantheon.menu_free_desc"));
        WriteMenuOption("6", Loc.Get("pantheon.menu_proclamation"), Loc.Get("pantheon.menu_proclamation_desc"));
        WriteMenuOption("7", Loc.Get("pantheon.menu_chastise"), Loc.Get("pantheon.menu_chastise_desc"));
        terminal.WriteLine("");
        WriteMenuOption("0", Loc.Get("pantheon.menu_back"), Loc.Get("pantheon.menu_back_desc"));

        string input = await terminal.GetInputAsync(Loc.Get("pantheon.prompt_choose_deed"));
        string choice = input.Trim();

        switch (choice)
        {
            case "1": await DeedRecruitBeliever(); break;
            case "2": await DeedBlessFollower(); break;
            case "3": await DeedSmiteMortal(); break;
            case "4": await DeedPoisonRelationship(); break;
            case "5": await DeedFreePrisoner(); break;
            case "6": await DeedProclamation(); break;
            case "7": await DeedChastiseFollower(); break;
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
            // 1.2.0: a loosely devout NPC (recruits like a pagan) is marked wavering (user ruling 2026-09-29)
            string status = string.IsNullOrEmpty(npc.WorshippedGod) ? Loc.Get("pantheon.pagan")
                : RecruitsLikePagan(npc) ? Loc.Get("pantheon.follows_wavering", npc.WorshippedGod)
                : Loc.Get("pantheon.follows", npc.WorshippedGod);
            targets.Add(new DeedTarget
            {
                Name = npc.DisplayName, Level = npc.Level, Status = status, NpcRef = npc,
                RecruitsLikePagan = RecruitsLikePagan(npc), OldGod = npc.WorshippedGod ?? ""
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
                    string status = string.IsNullOrEmpty(m.WorshippedGod) ? Loc.Get("pantheon.pagan") : Loc.Get("pantheon.follows", m.WorshippedGod);
                    string onTag = m.IsOnline ? " [ONLINE]" : "";
                    targets.Add(new DeedTarget
                    {
                        Name = m.DisplayName, Level = m.Level, Status = status + onTag,
                        IsPlayer = true, Username = m.Username, IsOnline = m.IsOnline,
                        RecruitsLikePagan = string.IsNullOrEmpty(m.WorshippedGod), OldGod = m.WorshippedGod ?? ""
                    });
                }
            }
            catch { /* DB unavailable — show NPCs only */ }
        }

        if (targets.Count == 0)
        {
            terminal.WriteLine(Loc.Get("pantheon.no_mortals_to_recruit"), "gray");
            await terminal.PressAnyKey(Loc.Get("pantheon.press_enter_return"));
            return;
        }

        var target = await PickTarget(targets, "RECRUIT BELIEVER", "bright_yellow", "Target #");
        if (target == null) return;
        // 1.2.0 Temple gods: a pagan, or an NPC loosely devout to its canon god, is recruited as a pagan
        bool isPagan = target.RecruitsLikePagan;
        var rng = Random.Shared;

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
                terminal.WriteLine(Loc.Get("pantheon.recruit_success", target.Name));
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("pantheon.exp_gain", expGain));
                RecalculateGodLevel();

                if (target.IsPlayer)
                    NewsSystem.Instance?.Newsy(true, $"[DIVINE] {target.Name} has converted to {currentPlayer.DivineName}!");
            }
            else
            {
                terminal.WriteLine("");
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("pantheon.recruit_failure", target.Name));
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

                string oldGod = target.OldGod;

                if (target.IsPlayer)
                    await ApplyRecruitToPlayer(target, currentPlayer.DivineName);
                else if (target.NpcRef != null)
                    target.NpcRef.WorshippedGod = currentPlayer.DivineName;

                currentPlayer.GodExperience += expGain;
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("pantheon.steal_success", target.Name, oldGod));
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("pantheon.exp_gain", expGain));
                RecalculateGodLevel();

                if (target.IsPlayer)
                    NewsSystem.Instance?.Newsy(true, $"[DIVINE] {target.Name} has converted to {currentPlayer.DivineName}!");
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("pantheon.steal_failure", target.Name));
            }
        }

        await terminal.PressAnyKey(Loc.Get("pantheon.press_enter_continue"));
    }

    private async Task DeedBlessFollower()
    {
        var believers = await GetBelieverListAsync(currentPlayer.DivineName);

        if (believers.Count == 0)
        {
            terminal.WriteLine(Loc.Get("pantheon.no_believers_to_bless"), "gray");
            await terminal.PressAnyKey(Loc.Get("pantheon.press_enter_return"));
            return;
        }

        // Convert believers to DeedTargets for paginated picker
        var targets = believers.Select(b => new DeedTarget
        {
            Name = b.Name, Level = b.Level,
            Status = b.Class + (b.IsPlayer && b.IsOnline ? " [ONLINE]" : ""),
            IsPlayer = b.IsPlayer, Username = b.Username, IsOnline = b.IsOnline
        }).ToList();

        var picked = await PickTarget(targets, Loc.Get("pantheon.bless_title"), "bright_yellow", Loc.Get("pantheon.bless_prompt"));
        if (picked == null) return;

        var target = believers.First(b => b.Name == picked.Name && b.IsPlayer == picked.IsPlayer);
        int expGain = GameConfig.GodBlessExp;
        if (target.IsPlayer) expGain = (int)(expGain * GameConfig.GodBlessPlayerExpMultiplier);

        // 1.2.0 Temple gods piece 5b: the follower gains Favor and the bonus follows their tier
        // (ImmortalDeedSystem). A target who no longer follows the god is refused: no deed, no XP.
        BlessOutcome outcome;
        if (target.IsPlayer)
        {
            var dt = new DeedTarget { Name = target.Name, Username = target.Username, IsPlayer = true, IsOnline = target.IsOnline };
            outcome = await ApplyBlessToPlayer(dt, currentPlayer.DivineName);
        }
        else
        {
            var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.DisplayName == target.Name);
            outcome = npc != null && !npc.IsDead && ImmortalDeedSystem.IsOwnFollower(currentPlayer.DivineName, npc.WorshippedGod)
                ? ImmortalDeedSystem.Bless(npc, currentPlayer.DivineName, otherSession: false)
                : new BlessOutcome(true, 0, 0, 0f, 0);
        }
        if (outcome.Refused)
        {
            terminal.WriteLine("");
            terminal.WriteLine(outcome.Failed ? Loc.Get("pantheon.deed_no_answer") : Loc.Get("pantheon.not_your_follower", target.Name), "gray");
            await terminal.PressAnyKey(Loc.Get("pantheon.press_enter_return"));
            return;
        }

        currentPlayer.DeedsLeft--;
        currentPlayer.GodExperience += expGain;
        if (target.IsPlayer)
            NewsSystem.Instance?.Newsy(true, Loc.Get("pantheon.bless_news", currentPlayer.DivineName, target.Name));

        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("pantheon.bless_success", target.Name));
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("pantheon.bless_effect", (int)Math.Round(outcome.Bonus * 100), outcome.Combats));
        if (target.IsPlayer)
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(outcome.FavorGained > 0
                ? Loc.Get("pantheon.bless_favor", target.Name, outcome.FavorGained, outcome.FavorNow)
                : Loc.Get("pantheon.bless_favor_capped", target.Name));
        }
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("pantheon.exp_gain", expGain));
        RecalculateGodLevel();

        await terminal.PressAnyKey(Loc.Get("pantheon.press_enter_continue"));
    }

    private async Task DeedSmiteMortal()
    {
        // Build combined target list: NPCs + players
        var targets = new List<DeedTarget>();

        var npcs = NPCSpawnSystem.Instance?.ActiveNPCs?
            .Where(n => !n.IsDead && ImmortalDeedSystem.CanSmite(currentPlayer.DivineName, n.WorshippedGod))
            .OrderByDescending(n => n.Level)
            .ToList() ?? new();

        foreach (var npc in npcs)
        {
            targets.Add(new DeedTarget
            {
                Name = npc.DisplayName, Level = npc.Level,
                Status = $"{Loc.Get("combat.bar_hp")}: {npc.HP}/{npc.MaxHP}", NpcRef = npc
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
                    if (!ImmortalDeedSystem.CanSmite(currentPlayer.DivineName, m.WorshippedGod)) continue;

                    // Check smite cooldown
                    string cooldownKey = $"{currentPlayer.DivineName}>{m.Username}";
                    if (_smiteCooldowns.TryGetValue(cooldownKey, out var lastSmite) &&
                        (DateTime.UtcNow - lastSmite).TotalMinutes < GameConfig.GodSmitePlayerCooldownMinutes)
                        continue;

                    string onTag = m.IsOnline ? " [ONLINE]" : "";
                    targets.Add(new DeedTarget
                    {
                        Name = m.DisplayName, Level = m.Level,
                        Status = $"{Loc.Get("combat.bar_hp")}: {m.HP}/{m.MaxHP}{onTag}",
                        IsPlayer = true, Username = m.Username, IsOnline = m.IsOnline,
                        HP = m.HP, MaxHP = m.MaxHP
                    });
                }
            }
            catch { /* DB unavailable */ }
        }

        if (targets.Count == 0)
        {
            terminal.WriteLine(Loc.Get("pantheon.no_mortals_to_smite"), "gray");
            await terminal.PressAnyKey(Loc.Get("pantheon.press_enter_return"));
            return;
        }

        var target = await PickTarget(targets, Loc.Get("pantheon.smite_title"), "bright_red", Loc.Get("pantheon.smite_prompt"));
        if (target == null) return;

        // 1.2.0 Temple gods piece 5b: a god never smites its own follower (the target's god read again
        // now, so one converted since the list was drawn is refused): no deed, no XP
        if (!ImmortalDeedSystem.CanSmite(currentPlayer.DivineName, await CurrentGodOfAsync(target)))
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("pantheon.smite_own_follower", target.Name), "gray");
            await terminal.PressAnyKey(Loc.Get("pantheon.press_enter_return"));
            return;
        }
        var rng = Random.Shared;
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
            terminal.WriteLine(Loc.Get("pantheon.smite_strike", target.Name));
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("pantheon.smite_player_damage", estimatedDamage));
            NewsSystem.Instance?.Newsy(true, Loc.Get("pantheon.smite_news", currentPlayer.DivineName, target.Name));
        }
        else if (target.NpcRef != null)
        {
            long damage = Math.Max(1, (long)(target.NpcRef.MaxHP * smitePercent));
            target.NpcRef.HP = Math.Max(1, target.NpcRef.HP - damage);

            terminal.WriteLine("");
            terminal.SetColor("bright_red");
            terminal.WriteLine(Loc.Get("pantheon.smite_strike", target.Name));
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("pantheon.smite_npc_damage", damage, target.NpcRef.HP, target.NpcRef.MaxHP));
        }

        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("pantheon.exp_gain", expGain));
        RecalculateGodLevel();

        await terminal.PressAnyKey(Loc.Get("pantheon.press_enter_continue"));
    }

    /// <summary>
    /// 1.2.0 Temple gods piece 5b: Chastise, one deed. A player follower (NPCs have no Favor) loses
    /// GodChastiseFavorLoss Favor, once a day each (ImmortalDeedSystem.CanChastise, kept on the god).
    /// No divine XP. A target who no longer follows the god is refused: no deed spent.
    /// </summary>
    private async Task DeedChastiseFollower()
    {
        var followers = (await GetBelieverListAsync(currentPlayer.DivineName))
            .Where(b => b.IsPlayer && ImmortalDeedSystem.CanChastise(currentPlayer, b.Username))
            .ToList();
        if (followers.Count == 0)
        {
            terminal.WriteLine(Loc.Get("pantheon.no_followers_to_chastise"), "gray");
            await terminal.PressAnyKey(Loc.Get("pantheon.press_enter_return"));
            return;
        }

        var targets = followers.Select(b => new DeedTarget
        {
            Name = b.Name, Level = b.Level,
            Status = b.Class + (b.IsOnline ? " [ONLINE]" : ""),
            IsPlayer = true, Username = b.Username, IsOnline = b.IsOnline
        }).ToList();

        var target = await PickTarget(targets, Loc.Get("pantheon.chastise_title"), "dark_red", Loc.Get("pantheon.chastise_prompt"));
        if (target == null) return;
        if (!ImmortalDeedSystem.CanChastise(currentPlayer, target.Username)) return;

        var outcome = await ApplyChastiseToPlayer(target, currentPlayer.DivineName);
        if (outcome.Refused)
        {
            terminal.WriteLine("");
            terminal.WriteLine(outcome.Failed ? Loc.Get("pantheon.deed_no_answer") : Loc.Get("pantheon.not_your_follower", target.Name), "gray");
            await terminal.PressAnyKey(Loc.Get("pantheon.press_enter_return"));
            return;
        }

        currentPlayer.DeedsLeft--;
        ImmortalDeedSystem.MarkChastised(currentPlayer, target.Username);

        terminal.WriteLine("");
        terminal.SetColor("dark_red");
        terminal.WriteLine(Loc.Get("pantheon.chastise_success", target.Name));
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("pantheon.chastise_favor", target.Name, outcome.FavorLost, outcome.FavorNow));

        await terminal.PressAnyKey(Loc.Get("pantheon.press_enter_continue"));
    }

    private async Task DeedPoisonRelationship()
    {
        var npcs = NPCSpawnSystem.Instance?.ActiveNPCs?
            .Where(n => !n.IsDead)
            .OrderBy(n => n.DisplayName)
            .ToList() ?? new();

        if (npcs.Count < 2)
        {
            terminal.WriteLine(Loc.Get("pantheon.not_enough_mortals"), "gray");
            await terminal.PressAnyKey(Loc.Get("pantheon.press_enter_return"));
            return;
        }

        int idx1 = await PickNPC(npcs, "POISON RELATIONSHIP  (33% chance)", "bright_magenta", "First mortal #");
        if (idx1 < 0) return;

        int idx2 = await PickNPC(npcs, $"POISON RELATIONSHIP  (vs {npcs[idx1].DisplayName})", "bright_magenta", "Second mortal #");
        if (idx2 < 0 || idx2 == idx1) return;

        // Adjust to 1-based for the existing logic below
        idx1++; idx2++;

        currentPlayer.DeedsLeft--;

        var rng = Random.Shared;
        if (rng.NextDouble() < GameConfig.GodPoisonRelationshipChance)
        {
            currentPlayer.GodExperience += GameConfig.GodPoisonRelationshipExp;
            terminal.WriteLine("");
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("pantheon.poison_success", npcs[idx1 - 1].DisplayName, npcs[idx2 - 1].DisplayName));
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("pantheon.poison_weakens"));
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("pantheon.exp_gain", GameConfig.GodPoisonRelationshipExp));

            // Actually worsen the relationship
            RelationshipSystem.UpdateRelationship(npcs[idx1 - 1], npcs[idx2 - 1], -1, 2);
            RecalculateGodLevel();
        }
        else
        {
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("pantheon.poison_failure"));
        }

        await terminal.PressAnyKey(Loc.Get("pantheon.press_enter_continue"));
    }

    private async Task DeedFreePrisoner()
    {
        var prisoners = NPCSpawnSystem.Instance?.ActiveNPCs?
            .Where(n => n.DaysInPrison > 0)
            .ToList() ?? new();

        if (prisoners.Count == 0)
        {
            terminal.WriteLine(Loc.Get("pantheon.no_prisoners"), "gray");
            await terminal.PressAnyKey(Loc.Get("pantheon.press_enter_return"));
            return;
        }

        int pickedIdx = await PickNPC(prisoners, "FREE PRISONER", "bright_cyan", "Free #",
            npc => $"{npc.DaysInPrison}{Loc.Get("pantheon.prison_days")}");
        if (pickedIdx < 0) return;

        var target = prisoners[pickedIdx];
        target.DaysInPrison = 0;
        currentPlayer.DeedsLeft--;
        currentPlayer.GodExperience += GameConfig.GodFreePrisonerExp;

        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("pantheon.free_success", target.DisplayName));
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("pantheon.exp_gain", GameConfig.GodFreePrisonerExp));
        RecalculateGodLevel();

        await terminal.PressAnyKey(Loc.Get("pantheon.press_enter_continue"));
    }

    private async Task DeedProclamation()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("pantheon.proclamation_header"));
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("pantheon.proclamation_intro"));
        terminal.WriteLine("");

        string message = await terminal.GetInputAsync(Loc.Get("pantheon.proclamation_prompt"));
        if (string.IsNullOrWhiteSpace(message)) return;

        if (message.Length > 120) message = message.Substring(0, 120);

        currentPlayer.DeedsLeft--;
        currentPlayer.GodExperience += GameConfig.GodProclamationExp;

        // Broadcast via news system
        string godTitle = GetGodTitleShared(currentPlayer.GodLevel);   // shared news and broadcast: English
        string newsEntry = $"[DIVINE] {currentPlayer.DivineName} the {godTitle} proclaims: \"{message}\"";

        // Broadcast to all online players
        if (UsurperRemake.Server.SessionContext.IsActive)
            UsurperRemake.Server.RoomRegistry.Instance?.BroadcastGlobal(
                $"\u001b[1;33m  {newsEntry}\u001b[0m");

        // Write to news
        NewsSystem.Instance?.Newsy(true, newsEntry);

        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("pantheon.proclamation_success"));
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("pantheon.exp_gain", GameConfig.GodProclamationExp));
        RecalculateGodLevel();

        await terminal.PressAnyKey(Loc.Get("pantheon.press_enter_continue"));
    }

    #endregion

    #region Immortal Rankings

    private async Task ShowImmortalRankings()
    {
        terminal.ClearScreen();

        WriteBoxHeader(Loc.Get("pantheon.immortal_rankings"), "bright_yellow", 77);
        terminal.WriteLine("");

        var gods = await GetAllImmortalsAsync();

        if (gods.Count <= 1)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("pantheon.only_god"));
            terminal.WriteLine("");
        }

        if (gods.Count > 0)
        {
            terminal.SetColor("cyan");
            terminal.Write($"  {Loc.Get("pantheon.rankings_col_num"),-4}{Loc.Get("pantheon.rankings_col_name"),-22}{Loc.Get("pantheon.rankings_col_title"),-14}{Loc.Get("pantheon.rankings_col_lvl"),4}{Loc.Get("pantheon.rankings_col_exp"),12}{Loc.Get("pantheon.rankings_col_believers"),7}{Loc.Get("pantheon.rankings_col_status"),9}");
            terminal.WriteLine("");
            if (!IsScreenReader)
            {
                terminal.SetColor("gray");
                terminal.WriteLine("  " + new string('─', 72));
            }

            var standings = ReadStandingsOnce();
            int rank = 1;
            foreach (var god in gods.OrderByDescending(g => g.GodExperience))
            {
                bool isYou = god.DivineName == currentPlayer.DivineName;
                string title = GetGodTitle(god.GodLevel);
                int believers = CountBelievers(god.DivineName, standings);
                string status = isYou ? Loc.Get("pantheon.ranking_you") : (god.IsOnline ? Loc.Get("pantheon.ranking_online") : Loc.Get("pantheon.ranking_offline"));

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
        await terminal.PressAnyKey(Loc.Get("pantheon.press_enter_return"));
    }

    #endregion

    #region News

    private async Task ShowNews()
    {
        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("pantheon.divine_mortal_news"), "bright_yellow", 77);
        terminal.WriteLine("");

        var allNews = NewsSystem.Instance?.GetTodaysNews() ?? new List<string>();
        var news = allNews.Count > 15 ? allNews.GetRange(allNews.Count - 15, 15) : allNews;
        if (news.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("pantheon.no_news"));
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
        await terminal.PressAnyKey(Loc.Get("pantheon.press_enter_return"));
    }

    #endregion

    #region Proclamation (separate from deed)

    private async Task SendProclamation()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("pantheon.comment_header"));
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("pantheon.comment_intro"));
        terminal.WriteLine("");

        string message = await terminal.GetInputAsync(Loc.Get("pantheon.comment_prompt"));
        if (string.IsNullOrWhiteSpace(message)) return;

        if (message.Length > 120) message = message.Substring(0, 120);

        string godTitle = GetGodTitleShared(currentPlayer.GodLevel);   // shared news: English
        string newsEntry = $"{currentPlayer.DivineName} the {godTitle} speaks: \"{message}\"";

        NewsSystem.Instance?.Newsy(true,newsEntry);

        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("pantheon.comment_success"));

        await terminal.PressAnyKey(Loc.Get("pantheon.press_enter_return"));
    }

    #endregion

    #region Visit Manwe (Level Up)

    private async Task VisitManwe()
    {
        terminal.ClearScreen();

        terminal.SetColor("bright_magenta");
        terminal.WriteLine(Loc.Get("pantheon.manwe_approach"));
        terminal.WriteLine("");

        await Pacing.Wait(500);

        if (currentPlayer.GodLevel >= GameConfig.GodMaxLevel)
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("pantheon.manwe_max_1"));
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("pantheon.manwe_max_2"));
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("pantheon.manwe_max_3"));
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
                terminal.WriteLine(Loc.Get("pantheon.manwe_approval"));
                terminal.WriteLine("");
                await Pacing.Wait(500);

                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("pantheon.manwe_power_grows", currentPlayer.DivineName));
                terminal.SetColor("bright_cyan");
                terminal.WriteLine(Loc.Get("pantheon.manwe_now_title", newTitle));
                terminal.WriteLine("");
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("pantheon.god_rankup", currentPlayer.GodLevel, newTitle));
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("pantheon.deeds_increased", newDeeds));

                // Reset deeds on level up
                currentPlayer.DeedsLeft = newDeeds;

                // News
                NewsSystem.Instance?.Newsy(true,
                    $"[DIVINE] {currentPlayer.DivineName} has ascended to {GetGodTitleShared(currentPlayer.GodLevel)}!");
            }
            else
            {
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("pantheon.manwe_not_ready", currentPlayer.DivineName));
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("pantheon.experience_progress", currentPlayer.GodExperience.ToString("N0"), nextExp.ToString("N0")));
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("pantheon.manwe_gather_more"));
            }
        }

        terminal.WriteLine("");
        await terminal.PressAnyKey(Loc.Get("pantheon.press_enter_return"));
    }

    #endregion

    #region Renounce Immortality

    private async Task<bool> RenounceImmortality()
    {
        terminal.ClearScreen();

        WriteBoxHeader(Loc.Get("pantheon.renounce_immortality"), "bright_red", 77);
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("pantheon.renounce_warning"));
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("pantheon.renounce_1"));
        terminal.WriteLine(Loc.Get("pantheon.renounce_2"));
        terminal.WriteLine(Loc.Get("pantheon.renounce_3"));
        terminal.WriteLine(Loc.Get("pantheon.renounce_4"));
        terminal.WriteLine("");

        string confirm1 = await terminal.GetInputAsync(Loc.Get("pantheon.renounce_confirm_yes"));
        if (confirm1.Trim().ToUpper() != "YES") return false;

        string confirm2 = await terminal.GetInputAsync(Loc.Get("pantheon.renounce_confirm_name"));
        if (confirm2.Trim() != currentPlayer.DivineName) return false;

        // Auto-abdicate if player is the king
        if (currentPlayer.King)
        {
            // v1.1.13: the abdication is written first; nothing is renounced when it fails
            if (!await CastleLocation.AbdicatePlayerThroneAsync(currentPlayer, "abdicated the throne to start anew"))
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("castle.court_change_failed"));
                await Pacing.Wait(1500);
                return false;
            }
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("pantheon.renounce_abdicate"));
            terminal.WriteLine("");
            await Pacing.Wait(1500);
        }

        // Clear all believers: NPCs, and (1.2.4) player followers in the game and in saved games
        await ClearFollowersOfAsync(currentPlayer.DivineName);

        // News
        NewsSystem.Instance?.Newsy(true,
            $"[DIVINE] {currentPlayer.DivineName} has fallen from the heavens! Their believers are left godless.");

        // Capture alignment before clearing (needed for legacy migration below)
        string godAlignment = currentPlayer.GodAlignment ?? "";

        // Clear immortal state. 1.2.4: the alt slot stays earned (also for a god that ascended before
        // the flag existed); CreateNewGame carries it to the new life.
        currentPlayer.HasEarnedAltSlot = true;
        currentPlayer.IsImmortal = false;
        currentPlayer.DivineName = "";
        currentPlayer.GodLevel = 0;
        currentPlayer.GodExperience = 0;
        currentPlayer.DeedsLeft = 0;
        currentPlayer.GodAlignment = "";

        // v0.57.17: replaced the legacy-only migration block with a proper StartNewCycle
        // call. Previously this only ran for cycle-1 players (CurrentCycle <= 1 ||
        // CompletedEndings.Count == 0) and even then it only fixed up the cycle
        // counter / endings list — it never reset god states. NG+ via renounce was
        // effectively broken: Manwe and every other resolved god carried over in
        // Defeated/Saved/Allied/Consumed state, and CanEncounterBoss refuses to
        // spawn gods in those states (OldGodBossSystem.cs:136-140), so the player
        // entered the new cycle with no Old God encounters available — Manwe at
        // floor 100 was already "dead" with no way to retrigger him. Cycle-2+
        // renouncers got it worse: the legacy migration's `if` guard skipped them
        // entirely, so they got ZERO state reset.
        // Fix: route through StoryProgressionSystem.StartNewCycle(EndingType), which
        // is the same method the normal post-Manwe-ascension NG+ path uses. It
        // calls InitializeOldGods() (resets every god to Corrupted/Dying/etc) and
        // InitializeKeyNPCs() (resets relationships), in addition to the cycle and
        // ending bookkeeping the old block did.
        var story = StoryProgressionSystem.Instance;
        EndingType inferredEnding = godAlignment switch
        {
            "Light" => EndingType.Savior,
            "Dark" => EndingType.Usurper,
            _ => EndingType.Defiant
        };
        // Dedup so a second renounce of the same alignment doesn't pad CompletedEndings.
        if (story.CompletedEndings.Contains(inferredEnding))
            story.StartNewCycle(new List<string>());
        else
            story.StartNewCycle(inferredEnding);

        terminal.WriteLine("");
        terminal.SetColor("bright_red");
        terminal.WriteLine(Loc.Get("pantheon.renounce_fade"));
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("pantheon.renounce_mortal"));
        terminal.WriteLine("");

        await Pacing.Wait(1500);

        // Signal NG+ restart (preserves cycle bonuses)
        GameEngine.Instance.PendingNewGamePlus = true;

        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("pantheon.renounce_new_life"));
        terminal.WriteLine("");

        await terminal.PressAnyKey(Loc.Get("pantheon.renounce_begin_anew"));
        return true;
    }

    /// <summary>
    /// 1.2.4: a renouncing god's followers lose their god. NPC believers are cleared in memory. A
    /// player in the game in another session is switched to no god as another's act (Favor goes
    /// with the god, no wrath) and told in their session's language. Every saved mortal character
    /// still following the god (SqlSaveBackend.ClearPlayerFollowersOf, one transaction) is cleared,
    /// and those not told in a session get the notice as mail in the save's language, so each
    /// player is told once. Returns the number of players cleared.
    /// </summary>
    internal static async Task<int> ClearFollowersOfAsync(string divineName)
    {
        if (string.IsNullOrWhiteSpace(divineName)) return 0;

        foreach (var npc in NPCSpawnSystem.Instance?.ActiveNPCs?.Where(n => n.WorshippedGod == divineName).ToList() ?? new())
            npc.WorshippedGod = "";

        var told = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in MudServer.Instance?.ActiveSessions.Values.ToList() ?? new())
        {
            var player = session?.Context?.Engine?.CurrentPlayer;
            // a canon god wins over a stale player-god entry (the load clears that one), so only a
            // player who follows this god now is switched
            if (player == null || player.IsImmortal || GodRegistry.GetWorshippedGod(player)?.Name != divineName) continue;
            GodSwitchSystem.Switch(player, null, GodChangeBy.Other, otherSession: true);
            string lang = session!.Context?.Language ?? "en";
            session.EnqueueMessage($"\u001b[1;33m  {Loc.GetIn(lang, "pantheon.follower_god_renounced", divineName)}\u001b[0m");
            session.EnqueueMessage($"\u001b[1;33m  {Loc.GetIn(lang, "pantheon.follower_now_godless")}\u001b[0m");
            string key = !string.IsNullOrEmpty(session.Context?.CharacterKey) ? session.Context!.CharacterKey : session.Username;
            if (!string.IsNullOrEmpty(key)) told.Add(key);
        }

        if (SaveSystem.Instance?.Backend is not SqlSaveBackend backend) return told.Count;
        var saved = await backend.ClearPlayerFollowersOf(divineName);
        foreach (var (key, lang) in saved)
        {
            if (told.Contains(key)) continue;
            told.Add(key);
            await backend.SendMessageToKey(divineName, key, "divine",
                Loc.GetIn(lang, "pantheon.follower_god_renounced", divineName) + " " + Loc.GetIn(lang, "pantheon.follower_now_godless"));
        }
        return told.Count;
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// 1.2.0 Temple gods: an NPC the recruit deed takes as a pagan (GodRecruitPaganChance and
    /// GodRecruitPaganExp): one with no god, or one loosely devout to its canon god
    /// (NpcFaithSystem.IsLooselyDevout). Any other NPC is a steal.
    /// </summary>
    public static bool RecruitsLikePagan(NPC npc) =>
        npc != null && (NpcFaithSystem.GodOf(npc).Length == 0 || NpcFaithSystem.IsLooselyDevout(npc));

    /// <summary>Get the title for a god level (1-9)</summary>
    public static string GetGodTitle(int level) => GodText.Title(Math.Clamp(level, 1, GameConfig.GodMaxLevel));

    /// <summary>The English title for a god level (1-9), for news and broadcasts every player shares.</summary>
    public static string GetGodTitleShared(int level) => GameConfig.GodTitles[Math.Clamp(level, 1, GameConfig.GodMaxLevel) - 1];

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
    /// <summary>
    /// 1.2.4: the divine experience believers grant at each daily reset. The payout
    /// (DailySystemManager) and the Status screen both read it, so they show the same number.
    /// </summary>
    public static long DailyBelieverExp(int believers, int godLevel) =>
        (long)Math.Max(0, believers) * Math.Max(0, godLevel) * GameConfig.GodBelieverExpPerLevel;

    public static int CountBelievers(string divineName)
    {
        if (string.IsNullOrEmpty(divineName)) return 0;
        // 1.2.0: the same NPC and player counts the Temple ranking and altars show
        int npcCount = GodRegistry.CountNpcFollowers(divineName);

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

    /// <summary>
    /// Count believers using standings already read once for the whole listing, so a ranking
    /// over N gods does one GetGodStandings read instead of N. Pass null standings (single
    /// player, or the DB unavailable) to count NPCs only.
    /// </summary>
    public static int CountBelievers(string divineName, Dictionary<string, GodStanding> standings)
    {
        if (string.IsNullOrEmpty(divineName)) return 0;
        int npcCount = GodRegistry.CountNpcFollowers(divineName);
        if (standings != null && standings.TryGetValue(divineName.Trim(), out var s))
            npcCount += s.Followers;
        return npcCount;
    }

    /// <summary>The GetGodStandings read for one listing, or null when unavailable (single player or DB error).</summary>
    private static Dictionary<string, GodStanding> ReadStandingsOnce()
    {
        if (!DoorMode.IsOnlineMode) return null;
        try
        {
            return (SaveSystem.Instance?.Backend as SqlSaveBackend)?.GetGodStandings();
        }
        catch { return null; }
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
                string tag = t.IsPlayer ? Loc.Get("pantheon.believer_player_tag") : "";
                terminal.Write($"{tag}{t.Name,-20}");
                terminal.SetColor("gray");
                terminal.WriteLine($" {Loc.Get("pantheon.rankings_col_lvl")} {t.Level,3}  {t.Status}");
            }

            terminal.WriteLine("");
            if (totalPages > 1)
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("pantheon.page_format", page + 1, totalPages, targets.Count));
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
                terminal.WriteLine(Loc.Get("pantheon.page_format", page + 1, totalPages, npcs.Count));
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

    /// <summary>
    /// The live character of a player in the game in another session on this server, with its
    /// session, or null.
    /// </summary>
    private static (PlayerSession Session, Character Player)? LiveSessionOf(string username)
    {
        if (string.IsNullOrEmpty(username) || MudServer.Instance == null) return null;
        if (!MudServer.Instance.ActiveSessions.TryGetValue(username.ToLowerInvariant(), out var session)) return null;
        var player = session.Context?.Engine?.CurrentPlayer;
        return player == null ? null : (session, player);
    }

    /// <summary>The god a deed target follows now: the NPC's, the live player's, else the saved one.</summary>
    private static async Task<string> CurrentGodOfAsync(DeedTarget target)
    {
        if (!target.IsPlayer) return target.NpcRef?.WorshippedGod ?? "";
        if (LiveSessionOf(target.Username) is { } live)
            return GodRegistry.GetWorshippedGod(live.Player)?.Name ?? "";
        if (SaveSystem.Instance?.Backend is SqlSaveBackend backend)
            return await backend.GetSavedWorshippedGod(target.Username);
        return "";
    }

    /// <summary>
    /// 1.2.0 Temple gods piece 5b: a divine blessing on a player follower. In the game in another
    /// session: ImmortalDeedSystem.Bless on the live character (Favor in memory, the tier's stat update
    /// left to that session, which saves it), and the message and the Favor line are sent to that
    /// session in its language. Not online: ImmortalDeedSystem.BlessSaved on the save in one SQL
    /// transaction (only the Favor and blessing fields are written), and the message is mailed in
    /// the save's language. Refused when the player no longer follows the god; Refused and Failed
    /// when the save could not be read or written (the database busy or failing).
    /// </summary>
    private async Task<BlessOutcome> ApplyBlessToPlayer(DeedTarget target, string godName)
    {
        var refused = new BlessOutcome(true, 0, 0, 0f, 0);
        var backend = SaveSystem.Instance?.Backend as SqlSaveBackend;
        if (backend == null) return refused;

        if (LiveSessionOf(target.Username) is { } live)
        {
            var outcome = ImmortalDeedSystem.Bless(live.Player, godName, otherSession: true);
            if (!outcome.Refused)
            {
                string lang = live.Session.Context?.Language ?? "en";
                live.Session.EnqueueMessage($"\u001b[1;36m  {Loc.GetIn(lang, "pantheon.bless_received", godName, (int)Math.Round(outcome.Bonus * 100), outcome.Combats)}\u001b[0m");
                if (outcome.FavorGained > 0)
                    live.Session.EnqueueMessage($"\u001b[1;33m  {Loc.GetIn(lang, "favor.gain", godName, outcome.FavorGained, outcome.FavorNow)}\u001b[0m");
            }
            return outcome;
        }

        var saved = await backend.UpdateFollowerSaveOffline<(BlessOutcome Outcome, string Lang)?>(target.Username, (p, gods) =>
        {
            var o = ImmortalDeedSystem.BlessSaved(p, gods, godName);
            return (!o.Refused, (o, string.IsNullOrEmpty(p.Language) ? "en" : p.Language));
        });
        if (saved.Failed) return refused with { Failed = true };
        if (saved.Result is not { } s || s.Outcome.Refused) return refused;
        string text = Loc.GetIn(s.Lang, "pantheon.bless_received", godName, (int)Math.Round(s.Outcome.Bonus * 100), s.Outcome.Combats);
        if (s.Outcome.FavorGained > 0)
            text += " " + Loc.GetIn(s.Lang, "favor.gain", godName, s.Outcome.FavorGained, s.Outcome.FavorNow);
        await backend.SendMessageToKey(godName, target.Username, "divine", text);
        return s.Outcome;
    }

    /// <summary>
    /// 1.2.0 Temple gods piece 5b: a chastise of a player follower. In the game in another session:
    /// ImmortalDeedSystem.Chastise on the live character (Favor in memory, the tier's stat update left
    /// to that session, which saves it), and the message and the Favor line are sent to that session
    /// in its language. Not online: ImmortalDeedSystem.ChastiseSaved on the save in one SQL transaction
    /// (only the Favor fields change), and the message is mailed in the save's language. Refused when
    /// the player no longer follows the god; Refused and Failed when the save could not be read or
    /// written (the database busy or failing).
    /// </summary>
    private async Task<ChastiseOutcome> ApplyChastiseToPlayer(DeedTarget target, string godName)
    {
        var refused = new ChastiseOutcome(true, 0, 0);
        var backend = SaveSystem.Instance?.Backend as SqlSaveBackend;
        if (backend == null) return refused;

        if (LiveSessionOf(target.Username) is { } live)
        {
            var outcome = ImmortalDeedSystem.Chastise(live.Player, godName, otherSession: true);
            if (!outcome.Refused)
            {
                string lang = live.Session.Context?.Language ?? "en";
                live.Session.EnqueueMessage($"\u001b[1;31m  {Loc.GetIn(lang, "pantheon.chastise_received", godName)}\u001b[0m");
                if (outcome.FavorLost > 0)
                    live.Session.EnqueueMessage($"\u001b[0;31m  {Loc.GetIn(lang, "favor.loss", godName, outcome.FavorLost, outcome.FavorNow)}\u001b[0m");
            }
            return outcome;
        }

        var saved = await backend.UpdateFollowerSaveOffline<(ChastiseOutcome Outcome, string Lang)?>(target.Username, (p, gods) =>
        {
            var o = ImmortalDeedSystem.ChastiseSaved(p, gods, godName);
            return (!o.Refused, (o, string.IsNullOrEmpty(p.Language) ? "en" : p.Language));
        });
        if (saved.Failed) return refused with { Failed = true };
        if (saved.Result is not { } s || s.Outcome.Refused) return refused;
        string text = Loc.GetIn(s.Lang, "pantheon.chastise_received", godName);
        if (s.Outcome.FavorLost > 0)
            text += " " + Loc.GetIn(s.Lang, "favor.loss", godName, s.Outcome.FavorLost, s.Outcome.FavorNow);
        await backend.SendMessageToKey(godName, target.Username, "divine", text);
        return s.Outcome;
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
                string lang = session.Context?.Language ?? "en";
                session.EnqueueMessage($"\u001b[1;31m  {Loc.GetIn(lang, "pantheon.smite_received", godName, damage)}\u001b[0m");
                return;
            }
        }

        // Offline: atomic DB update + message (in the save's language)
        await backend.ApplyDivineSmite(target.Username, damagePercent);
        string savedLang = (await backend.ReadGameData(target.Username))?.Player?.Language ?? "";
        await backend.SendMessageToKey(godName, target.Username, "divine",
            Loc.GetIn(savedLang.Length == 0 ? "en" : savedLang, "pantheon.smite_received_offline", godName));
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
                // 1.2.0: one god, so a canon choice is cleared. Temple gods piece 4: another player's act, not
                // the worshipper's, so Favor goes with the old god and no wrath follows
                GodSwitchSystem.Switch(player, godName, GodChangeBy.Other, otherSession: true);
                await GodBoonSystem.ApplyRecruitAsync(currentPlayer, player);   // 1.2.0: the god's boons and domain at once
                session.EnqueueMessage(
                    $"\u001b[1;33m  ✦ A divine presence fills your soul... You now worship {godName}! ✦\u001b[0m");
                return;
            }
        }

        // Offline: atomic DB update + message
        await backend.SetPlayerWorshippedGod(target.Username, godName);
        await backend.SendMessageToKey(godName, target.Username, "divine",
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
        /// <summary>Recruit: true for a pagan, or an NPC loosely devout to its god (RecruitsLikePagan).</summary>
        public bool RecruitsLikePagan { get; set; }
        /// <summary>Recruit: the god the target follows now ("" for a pagan).</summary>
        public string OldGod { get; set; } = "";
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

    /// <summary>
    /// Phase 5: emit Pantheon (divine realm) menu state for the Electron client. Pattern B.
    /// </summary>
    private void EmitElectronEvents()
    {
        var player = currentPlayer;
        if (player == null) return;

        ElectronBridge.EmitLocation(
            name: Loc.Get("pantheon.divine_realm"),
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
            new() { Key = "S", Label = "Divine Status", Category = "info", Icon = "info" },
            new() { Key = "B", Label = "Manage Believers", Category = "divine", Icon = "believers" },
            new() { Key = "D", Label = "Perform Divine Deeds", Category = "divine", Icon = "deed" },
            new() { Key = "F", Label = "Configure Boons", Category = "divine", Icon = "boon" },
            new() { Key = "I", Label = "Immortal Rankings", Category = "info", Icon = "rank" },
            new() { Key = "N", Label = "World News", Category = "info", Icon = "news" },
            new() { Key = "C", Label = "Send Proclamation", Category = "divine", Icon = "proclaim" },
            new() { Key = "V", Label = "Visit Manwe", Category = "social", Icon = "manwe" },
            new() { Key = "H", Label = "Hall of the Ascended", Category = "info", Icon = "statue" },
            new() { Key = "R", Label = "Renounce Immortality", Category = "danger", Icon = "renounce" },
            new() { Key = "Q", Label = "Quit Realm", Category = "navigate", Icon = "back" },
        };
        ElectronBridge.EmitMenu(menu);
    }
}
