using UsurperRemake.Utils;
using UsurperRemake.Systems;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System;
using UsurperRemake.Server;

/// <summary>
/// Prison Location - The prisoner's perspective inside the Royal Prison
/// Based on PRISONC.PAS from the original Usurper Pascal implementation
/// Provides escape attempts, prisoner communication, and royal justice system
/// </summary>
public partial class PrisonLocation : BaseLocation
{
    private readonly GameEngine gameEngine;
    private new readonly TerminalEmulator terminal;
    private bool refreshMenu = true;

    public PrisonLocation(GameEngine engine, TerminalEmulator term) : base("prison")
    {
        gameEngine = engine;
        terminal = term;
        SetLocationProperties();
    }

    // Add parameterless constructor for compatibility
    public PrisonLocation() : base("prison")
    {
        gameEngine = GameEngine.Instance;
        terminal = GameEngine.Instance.Terminal;
        SetLocationProperties();
    }

    private void SetLocationProperties()
    {
        LocationId = GameLocation.Prison;
        LocationName = GameConfig.DefaultPrisonName;
        LocationDescription = "You are locked in a cold, damp prison cell";
        AllowedClasses = new HashSet<CharacterClass>(); // All classes allowed
        LevelRequirement = 1;

        // Add all character classes to allowed set
        foreach (CharacterClass charClass in Enum.GetValues<CharacterClass>())
        {
            AllowedClasses.Add(charClass);
        }
    }

    /// <summary>
    /// Override base EnterLocation to handle prison-specific logic
    /// </summary>
    public override async Task EnterLocation(Character player, TerminalEmulator term)
    {
        if (player == null) return;
        // v1.2.5: this override skips BaseLocation.EnterLocation, so the base player is set here;
        // IsScreenReader reads it, and without it the screen reader menu was never shown
        currentPlayer = player;

        // Check if player is actually imprisoned
        if (player.DaysInPrison <= 0)
        {
            await terminal.WriteLineAsync(Loc.Get("prison.not_imprisoned"));
            await Pacing.Wait(1000);
            // Navigate player to Main Street properly
            throw new LocationExitException(GameLocation.MainStreet);
        }

        refreshMenu = true;
        await ShowPrisonInterface(player);
    }

    private async Task ShowPrisonInterface(Character player)
    {
        char choice = '?';
        bool exitPrison = false;

        while (!exitPrison)
        {
            // Check if sentence is served (days ran out)
            if (player.DaysInPrison <= 0)
            {
                await terminal.WriteLineAsync();
                await terminal.WriteColorLineAsync(Loc.Get("prison.guards_open"), TerminalEmulator.ColorGreen);
                await terminal.WriteLineAsync(Loc.Get("prison.sentence_served"));
                await terminal.WriteColorLineAsync(Loc.Get("prison.you_are_free"), TerminalEmulator.ColorGreen);
                player.CellDoorOpen = false;
                player.RescuedBy = "";
                player.HP = Math.Max(player.HP, player.MaxHP / 2); // Restore some health

                // Reduce Darkness for serving time — prevents arrest loop
                long darknessReduction = Math.Min(player.Darkness, 75);
                if (darknessReduction > 0)
                {
                    player.Darkness -= darknessReduction;
                    await terminal.WriteColorLineAsync(Loc.Get("prison.darkness_reduced", darknessReduction), "bright_green");
                }
                await Pacing.Wait(1500);
                throw new LocationExitException(GameLocation.MainStreet);
            }

            // Update location status if needed
            await UpdatePrisonStatus(player);

            // Check if player can walk out (cell door open by rescue)
            if (await CanOpenCellDoor(player))
            {
                await HandleCellDoorOpen(player);
                throw new LocationExitException(GameLocation.MainStreet);
            }

            // Show who else is here if enabled
            if (ShouldShowOthersHere(player))
            {
                await ShowOthersHere(player);
            }

            // Display menu
            if (GameConfig.ElectronMode)
            {
                EmitElectronEvents(player);
            }
            else
            {
                await DisplayPrisonMenu(player, true, true);
            }

            // Get user input
            string input = (await terminal.ReadLineAsync())?.Trim() ?? "";

            // v0.57.12: prisoners can use communication slash commands (PR #84) — shouting through the
            // bars, getting messages out, checking /who — but NOT action/admin commands like group
            // management, guild admin, or guild banking. A locked-in-cell player moving guild gold or
            // inviting someone to a dungeon party doesn't make in-world sense and is an exploit vector.
            if (input.StartsWith("/") && SessionContext.IsActive)
            {
                if (IsPrisonAllowedSlashCommand(input))
                {
                    if (await MudChatSystem.TryProcessCommand(input, terminal))
                        continue;
                }
                else
                {
                    await terminal.WriteColorLineAsync(
                        $"  {Loc.Get("prison.speak_only")}",
                        TerminalEmulator.ColorDarkGray);
                    continue;
                }
            }

            if (input.Length == 0) continue;
            choice = char.ToUpper(input[0]);

            // Process user choice - returns true if player escaped/freed
            exitPrison = await ProcessPrisonChoice(player, choice);
        }
    }

    // v0.57.12: whitelist of slash commands a prisoner is allowed to use. Communication only —
    // chat/shout/tell/who/emote/title and the in-world-inert guild info+chat commands (`/guild`,
    // `/ginfo`, `/gc`). Every action/admin command (group management, guild admin, bank transfers,
    // accepting party invites, etc.) falls through and the prisoner sees a "you can only speak" message.
    private static readonly HashSet<string> PrisonAllowedSlashCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "say", "s", "shout", "tell", "t", "emote", "me", "gossip", "gos",
        "who", "w", "title", "guild", "ginfo", "gc"
    };

    private static bool IsPrisonAllowedSlashCommand(string input)
    {
        if (string.IsNullOrEmpty(input) || !input.StartsWith("/")) return false;
        int spaceIdx = input.IndexOf(' ');
        string cmd = spaceIdx > 0 ? input.Substring(1, spaceIdx - 1) : input.Substring(1);
        return PrisonAllowedSlashCommands.Contains(cmd);
    }

    private Task UpdatePrisonStatus(Character player)
    {
        // This would typically update the online player location
        // For now, just ensure the location is set correctly
        refreshMenu = true;
        return Task.CompletedTask;
    }

    private async Task<bool> CanOpenCellDoor(Character player)
    {
        // In Pascal, this checks if onliner.location == onloc_prisonerop
        // For this implementation, we'll check if player has been rescued
        // This would be set by another player breaking them out
        await Task.CompletedTask;
        return player.CellDoorOpen;
    }

    private async Task HandleCellDoorOpen(Character player)
    {
        await terminal.WriteLineAsync();
        await terminal.WriteColorLineAsync(Loc.Get("prison.cell_door_open"), TerminalEmulator.ColorGreen);
        await terminal.WriteLineAsync();

        if (!string.IsNullOrEmpty(player.RescuedBy))
        {
            await terminal.WriteColorAsync(player.RescuedBy, TerminalEmulator.ColorCyan);
            await terminal.WriteLineAsync(Loc.Get("prison.broke_out"));
            await terminal.WriteLineAsync(Loc.Get("prison.owe_freedom"));
        }
        else
        {
            await terminal.WriteLineAsync(Loc.Get("prison.someone_unlocked"));
        }

        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync(Loc.Get("prison.walk_out"));
        await terminal.WriteColorLineAsync(Loc.Get("prison.you_are_free"), TerminalEmulator.ColorGreen);

        // Reset player state
        player.HP = player.MaxHP;
        player.DaysInPrison = 0;
        player.CellDoorOpen = false;
        player.RescuedBy = "";

        // Return to dormitory
        await Task.Delay(GameConfig.PrisonCellOpenDelay);
    }

    private bool ShouldShowOthersHere(Character player)
    {
        // In Pascal: if player.ear = global_ear_all
        // For now, always show others
        return true;
    }

    private Task ShowOthersHere(Character player)
    {
        // Online prisoner display not yet implemented
        return Task.CompletedTask;
    }

    private async Task DisplayPrisonMenu(Character player, bool force, bool isShort)
    {
        if (isShort)
        {
            if (!player.Expert)
            {
                if (refreshMenu)
                {
                    refreshMenu = false;
                    await ShowPrisonMenuFull();
                }

                await terminal.WriteLineAsync();
                await terminal.WriteAsync(Loc.Get("prison.prompt_prefix"));
                await terminal.WriteColorAsync("?", TerminalEmulator.ColorYellow);
                await terminal.WriteAsync(Loc.Get("prison.prompt_suffix"));
            }
            else
            {
                await terminal.WriteLineAsync();
                await terminal.WriteAsync(Loc.Get("prison.prompt_expert"));
            }
        }
        else
        {
            if (!player.Expert || force)
            {
                await ShowPrisonMenuFull();
            }
        }
    }

    private async Task ShowPrisonMenuFull()
    {
        await terminal.ClearScreenAsync();
        // v0.60.0 beta (player report: prison menu rendering all-red): set
        // explicit default color at the top so plain WriteLineAsync calls
        // below don't inherit a leftover color (eg the murder-cinematic red).
        terminal.SetColor("white");
        await terminal.WriteLineAsync();

        // Prison header
        if (!IsScreenReader)
        {
            // v1.2.5: the bar is at least as wide as the framed title in the player's language
            string title = Loc.Get("prison.title");
            string bar = new string('I', Math.Max(24, title.Length + 8)), pillar = bar[..3];
            await terminal.WriteColorLineAsync(bar, TerminalEmulator.ColorCyan);
            await terminal.WriteColorLineAsync($"{pillar} {title} {pillar}", TerminalEmulator.ColorCyan);
            await terminal.WriteColorLineAsync(bar, TerminalEmulator.ColorCyan);
        }
        else
        {
            await terminal.WriteColorLineAsync(Loc.Get("prison.title"), TerminalEmulator.ColorCyan);
        }
        terminal.SetColor("white");
        await terminal.WriteLineAsync();

        // Prison atmosphere description
        await terminal.WriteLineAsync(Loc.Get("prison.atmo1"));
        await terminal.WriteLineAsync(Loc.Get("prison.atmo2"));
        await terminal.WriteLineAsync(Loc.Get("prison.atmo3"));
        await terminal.WriteLineAsync(Loc.Get("prison.atmo4"));
        await terminal.WriteLineAsync(Loc.Get("prison.atmo5"));
        await terminal.WriteLineAsync();

        // Menu options
        terminal.SetColor("white");
        if (IsScreenReader)
        {
            await terminal.WriteLineAsync(Loc.Get("prison.sr_menu_who"));
            await terminal.WriteLineAsync(Loc.Get("prison.sr_menu_demand"));
            await terminal.WriteLineAsync(Loc.Get("prison.sr_menu_open"));
            await terminal.WriteLineAsync(Loc.Get("prison.sr_menu_escape"));
            await terminal.WriteLineAsync(Loc.Get("prison.sr_menu_status"));
            await terminal.WriteLineAsync(Loc.Get("prison.sr_menu_activities"));
            await terminal.WriteLineAsync(Loc.Get("prison.sr_menu_bail"));
            await terminal.WriteLineAsync(Loc.Get("prison.sr_menu_petition"));

            var currentPlayer = gameEngine?.CurrentPlayer;
            if (currentPlayer != null && CanMeetVex(currentPlayer))
            {
                await terminal.WriteLineAsync(Loc.Get("prison.sr_menu_vex"));
            }

            await terminal.WriteLineAsync(Loc.Get("prison.sr_menu_quit"));
        }
        else
        {
            await terminal.WriteLineAsync(Loc.Get("prison.menu_row1"));
            await terminal.WriteLineAsync(Loc.Get("prison.menu_row2"));
            await terminal.WriteLineAsync(Loc.Get("prison.menu_row3"));
            await terminal.WriteLineAsync(Loc.Get("prison.menu_row4"));

            // Check for Vex companion availability - get player from game engine
            var currentPlayer = gameEngine?.CurrentPlayer;
            if (currentPlayer != null && CanMeetVex(currentPlayer))
            {
                await terminal.WriteColorAsync("(V)", TerminalEmulator.ColorYellow);
                await terminal.WriteColorLineAsync(Loc.Get("prison.menu_vex_suffix"), TerminalEmulator.ColorCyan);
            }

            await terminal.WriteLineAsync(Loc.Get("prison.menu_quit"));
        }
    }

    /// <summary>
    /// Check if Vex can be encountered in prison
    /// </summary>
    private bool CanMeetVex(Character player)
    {
        var companionSystem = CompanionSystem.Instance;
        var vex = companionSystem.GetCompanion(CompanionId.Vex);

        if (vex == null || vex.IsRecruited || vex.IsDead)
            return false;

        if (player.Level < vex.RecruitLevel)
            return false;

        var story = StoryProgressionSystem.Instance;
        if (story.HasStoryFlag("vex_prison_encounter_complete"))
            return false;

        return true;
    }

    private async Task<bool> ProcessPrisonChoice(Character player, char choice)
    {
        switch (choice)
        {
            case '?':
                await HandleMenuDisplay(player);
                return false;
            case 'S':
                await HandleStatusDisplay(player);
                return false;
            case 'Q':
                return await HandleQuitConfirmation(player);
            case 'O':
                await HandleOpenCellDoor(player);
                return false;
            case 'D':
                await HandleDemandRelease(player);
                return false;
            case 'E':
                return await HandleEscapeAttempt(player);
            case 'W':
                await HandleListPrisoners(player);
                return false;
            case 'A':
                await HandleActivities(player);
                return false;
            case 'B':
                return await HandlePayBail(player);
            case 'P':
                await HandlePetitionKing(player);
                return false;
            case 'V':
                try
                {
                    return await HandleVexEncounter(player);
                }
                catch (LocationExitException) { throw; }
                catch (Exception ex)
                {
                    DebugLogger.Instance.LogError("PRISON", $"Vex encounter failed: {ex.Message}");
                    await terminal.WriteLineAsync();
                    await terminal.WriteColorLineAsync(Loc.Get("prison.no_one_unusual"), TerminalEmulator.ColorDarkGray);
                    await Pacing.Wait(1000);
                    return false;
                }
            default:
                // Invalid choice, do nothing
                return false;
        }
    }

    /// <summary>
    /// Handle bail payment — player pays gold to get released immediately
    /// </summary>
    private async Task<bool> HandlePayBail(Character player)
    {
        if (player.IsMurderConvict)
        {
            await terminal.WriteColorLineAsync($"  {Loc.Get("prison.bail_murder")}", TerminalEmulator.ColorRed);
            await Pacing.Wait(1500);
            return false;
        }

        var king = CastleLocation.GetCurrentKing();
        if (king == null)
        {
            await terminal.WriteColorLineAsync($"  {Loc.Get("prison.bail_no_king")}", TerminalEmulator.ColorYellow);
            await Pacing.Wait(2000);
            return false;
        }

        // Find this player's prison record
        string playerName = player.DisplayName ?? player.Name2 ?? "";
        if (!king.Prisoners.TryGetValue(playerName, out var record))
        {
            await terminal.WriteColorLineAsync($"  {Loc.Get("prison.bail_none")}", TerminalEmulator.ColorYellow);
            await Pacing.Wait(2000);
            return false;
        }

        if (record.BailAmount <= 0)
        {
            await terminal.WriteColorLineAsync($"  {Loc.Get("prison.bail_not_set")}", TerminalEmulator.ColorYellow);
            await terminal.WriteColorLineAsync($"  {Loc.Get("prison.bail_wait")}", TerminalEmulator.ColorDarkGray);
            await Pacing.Wait(2000);
            return false;
        }

        await terminal.WriteLineAsync();
        await terminal.WriteColorLineAsync($"  {Loc.Get("prison.bail_amount", $"{record.BailAmount:N0}")}", TerminalEmulator.ColorCyan);
        await terminal.WriteColorLineAsync($"  {Loc.Get("prison.bail_you_have", $"{player.Gold:N0}")}", TerminalEmulator.ColorWhite);
        await terminal.WriteLineAsync();

        if (player.Gold < record.BailAmount)
        {
            await terminal.WriteColorLineAsync($"  {Loc.Get("prison.bail_cannot_afford")}", TerminalEmulator.ColorRed);
            await Pacing.Wait(2000);
            return false;
        }

        await terminal.WriteColorAsync($"  {Loc.Get("prison.bail_confirm", $"{record.BailAmount:N0}")}", TerminalEmulator.ColorCyan);
        if (!await terminal.AskYesNoAsync(""))
        {
            await terminal.WriteColorLineAsync($"  {Loc.Get("prison.bail_keep_gold")}", TerminalEmulator.ColorDarkGray);
            await Pacing.Wait(1500);
            return false;
        }

        // v1.1.13: the bail into the treasury and the release are one guarded court change; the gold
        // leaves and the cell opens only once it is written
        long bailPaid = record.BailAmount;
        if (!await PayBailAsync(CastleLocation.TreasuryOsm(), player, playerName, bailPaid))
        {
            await WriteWrappedAsync(Loc.Get("castle.court_change_failed"), TerminalEmulator.ColorRed);
            await Pacing.Wait(2000);
            return false;
        }

        await terminal.WriteLineAsync();
        await terminal.WriteColorLineAsync($"  {Loc.Get("prison.bail_paid", $"{bailPaid:N0}")}", TerminalEmulator.ColorYellow);
        await terminal.WriteColorLineAsync($"  {Loc.Get("prison.bail_door_opens")}", TerminalEmulator.ColorGreen);
        await terminal.WriteColorLineAsync($"  {Loc.Get("prison.bail_free")}", TerminalEmulator.ColorGreen);
        await terminal.WriteLineAsync();

        NewsSystem.Instance?.Newsy(true, Loc.Get("prison.news_bail_paid", playerName, $"{bailPaid:N0}"));

        await terminal.WaitForKey();
        return true; // Exit prison
    }

    /// <summary>
    /// v1.1.13: bail paid into the stored treasury and the prisoner's record removed in one guarded court
    /// change; the player's gold leaves and the prison days clear only once it is written.
    /// </summary>
    internal static async Task<bool> PayBailAsync(UsurperRemake.Systems.OnlineStateManager? osm, Character player, string playerName, long bail,
        Func<Task>? beforeWrite = null)
    {
        if (player.Gold < bail) return false;
        if (!await CastleLocation.CourtChangeAsync(osm, court =>
            {
                var stored = court.Prisoners.FirstOrDefault(p => p.CharacterName == playerName);
                if (stored == null || stored.BailAmount != bail) return false;
                court.Treasury += bail;
                court.Prisoners.Remove(stored);
                return true;
            }, beforeWrite)) return false;
        player.Gold -= bail;
        player.DaysInPrison = 0;
        return true;
    }

    /// <summary>
    /// v1.1.13: a granted pardon. A prison record the stored court holds is removed in one guarded court change
    /// and the player is freed once it is written; with no record there (an arrest that made none) nothing is
    /// written and the player is freed. False when a record is there and its removal was not written.
    /// </summary>
    internal static async Task<bool> PardonAsync(UsurperRemake.Systems.OnlineStateManager? osm, Character player, string playerName)
    {
        bool hadRecord = false;
        bool removed = await CastleLocation.CourtChangeAsync(osm, court =>
        {
            hadRecord = court.Prisoners.Any(p => p.CharacterName == playerName);
            return court.Prisoners.RemoveAll(p => p.CharacterName == playerName) > 0;
        });
        if (!removed && hadRecord) return false;
        player.DaysInPrison = 0;
        return true;
    }

    /// <summary>v1.1.13: bail set on the prisoner's stored record, as one guarded court change (false: no record there).</summary>
    internal static Task<bool> SetBailAsync(UsurperRemake.Systems.OnlineStateManager? osm, string playerName, long bail) =>
        CastleLocation.CourtChangeAsync(osm, court =>
        {
            var stored = court.Prisoners.FirstOrDefault(p => p.CharacterName == playerName);
            if (stored == null) return false;
            stored.BailAmount = bail;
            return true;
        });

    /// <summary>
    /// Handle petition to the king for release or bail setting
    /// </summary>
    private async Task HandlePetitionKing(Character player)
    {
        var king = CastleLocation.GetCurrentKing();
        if (king == null)
        {
            await terminal.WriteColorLineAsync($"  {Loc.Get("prison.petition_no_king")}", TerminalEmulator.ColorYellow);
            await Pacing.Wait(2000);
            return;
        }

        string playerName = player.DisplayName ?? player.Name2 ?? "";

        await terminal.ClearScreenAsync();
        await terminal.WriteColorLineAsync($"  ═══ {Loc.Get("prison.petition_header")} ═══", TerminalEmulator.ColorCyan);
        await terminal.WriteLineAsync();
        await terminal.WriteColorLineAsync($"  {Loc.Get("prison.petition_intro")}", TerminalEmulator.ColorWhite);
        await terminal.WriteLineAsync();
        await terminal.WriteColorLineAsync($"  1. {Loc.Get("prison.petition_opt_bail")}", TerminalEmulator.ColorCyan);
        await terminal.WriteColorLineAsync($"  2. {Loc.Get("prison.petition_opt_clemency")}", TerminalEmulator.ColorCyan);
        await terminal.WriteColorLineAsync($"  0. {Loc.Get("ui.cancel")}", TerminalEmulator.ColorDarkGray);
        await terminal.WriteLineAsync();

        string choice = await terminal.ReadLineAsync();

        if (choice?.Trim() == "1")
        {
            // Request bail
            bool hasBail = king.Prisoners.TryGetValue(playerName, out var record) && record?.BailAmount > 0;
            if (hasBail)
            {
                await terminal.WriteColorLineAsync($"  {Loc.Get("prison.bail_already_set", $"{record!.BailAmount:N0}")}", TerminalEmulator.ColorYellow);
            }
            else
            {
                // NPC king auto-sets bail based on player level and crime
                if (king.AI == CharacterAI.Computer)
                {
                    long bailAmount = 1000 + player.Level * 500;
                    // v1.1.13: the bail is set on the stored record in one guarded court change, so paying it at once
                    // meets the same amount
                    if (await SetBailAsync(CastleLocation.TreasuryOsm(), playerName, bailAmount))
                    {
                        await terminal.WriteColorLineAsync($"  {Loc.Get("prison.petition_considers")}", TerminalEmulator.ColorWhite);
                        await Pacing.Wait(1500);
                        await terminal.WriteColorLineAsync($"  {Loc.Get("prison.bail_set_now", $"{bailAmount:N0}")}", TerminalEmulator.ColorGreen);
                        await terminal.WriteColorLineAsync($"  {Loc.Get("prison.bail_use_b")}", TerminalEmulator.ColorCyan);
                    }
                    else
                    {
                        await terminal.WriteColorLineAsync($"  {Loc.Get("prison.petition_no_record")}", TerminalEmulator.ColorRed);
                    }
                }
                else
                {
                    // Human king — send a message
                    if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
                    {
                        var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
                        if (backend != null)
                        {
                            try
                            {
                                string kingUsername = king.Name.ToLowerInvariant();
                                await backend.SendMessageLocalized(playerName, kingUsername, "petition",
                                    lang => BailPetitionMail(lang, playerName));
                            }
                            catch { }
                        }
                    }
                    await terminal.WriteColorLineAsync($"  {Loc.Get("prison.petition_sent")}", TerminalEmulator.ColorCyan);
                    await terminal.WriteColorLineAsync($"  {Loc.Get("prison.petition_await")}", TerminalEmulator.ColorDarkGray);
                }
            }
        }
        else if (choice?.Trim() == "2")
        {
            // Plead for clemency
            if (king.AI == CharacterAI.Computer)
            {
                // NPC king: 20% chance of pardon based on chivalry
                // v0.57.12: divisor was /500 (+2 max at cap=1000, effectively inert). Originally scaled against
                // the dead MaxChivalry=30000 constant. Changed to /50 so max chivalry gives +20 bonus,
                // preserving the intended spread with `Math.Clamp(5, 40)` still bounding the result.
                int pardonChance = 10 + (int)(player.Chivalry / 50);
                pardonChance = Math.Clamp(pardonChance, 5, 40);

                await terminal.WriteColorLineAsync($"  {Loc.Get("prison.plea_considers")}", TerminalEmulator.ColorWhite);
                await Pacing.Wait(2000);

                // v1.1.13: a court prison record is removed in one guarded court change; an arrest without one
                // (a street arrest) is pardoned with no court write
                if (Random.Shared.Next(100) < pardonChance
                    && await PardonAsync(CastleLocation.TreasuryOsm(), player, playerName))
                {

                    await terminal.WriteColorLineAsync($"  {Loc.Get("prison.plea_mercy")}", TerminalEmulator.ColorGreen);
                    await terminal.WriteColorLineAsync($"  {Loc.Get("prison.plea_pardoned")}", TerminalEmulator.ColorGreen);
                    NewsSystem.Instance?.Newsy(true, Loc.Get("prison.news_pardoned", playerName));
                    await terminal.WaitForKey();
                    return;
                }
                else
                {
                    await terminal.WriteColorLineAsync($"  {Loc.Get("prison.plea_not_forgotten")}", TerminalEmulator.ColorRed);
                    await terminal.WriteColorLineAsync($"  {Loc.Get("prison.plea_denied")}", TerminalEmulator.ColorRed);
                }
            }
            else
            {
                // Human king — send message
                if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
                {
                    var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
                    if (backend != null)
                    {
                        try
                        {
                            string kingUsername = king.Name.ToLowerInvariant();
                            await backend.SendMessageLocalized(playerName, kingUsername, "petition",
                                lang => ClemencyPleaMail(lang, playerName));
                        }
                        catch { }
                    }
                }
                await terminal.WriteColorLineAsync($"  {Loc.Get("prison.plea_sent")}", TerminalEmulator.ColorCyan);
                await terminal.WriteColorLineAsync($"  {Loc.Get("prison.petition_await")}", TerminalEmulator.ColorDarkGray);
            }
        }

        await Pacing.Wait(2000);
    }

    /// <summary>
    /// Handle prison activity selection - allows prisoners to build stats
    /// </summary>
    private async Task HandleActivities(Character player)
    {
        await terminal.ClearScreenAsync();
        if (IsScreenReader)
        {
            await terminal.WriteColorLineAsync(Loc.Get("prison.activities_header"), TerminalEmulator.ColorCyan);
        }
        else
        {
            await terminal.WriteColorLineAsync("═══════════════════════════════════════", TerminalEmulator.ColorCyan);
            await terminal.WriteColorLineAsync($"           {Loc.Get("prison.activities_header")}           ", TerminalEmulator.ColorCyan);
            await terminal.WriteColorLineAsync("═══════════════════════════════════════", TerminalEmulator.ColorCyan);
        }
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync(Loc.Get("prison.activities_intro1"));
        await terminal.WriteLineAsync(Loc.Get("prison.activities_intro2"));
        await terminal.WriteLineAsync();

        var activities = PrisonActivitySystem.Instance.GetAvailableActivities();
        int i = 1;
        foreach (var activity in activities)
        {
            await terminal.WriteColorAsync($"({i}) ", TerminalEmulator.ColorYellow);
            await terminal.WriteAsync($"{PrisonActivitySystem.ActivityName(activity),-15} ");
            await terminal.WriteColorAsync(PrisonActivitySystem.ActivityEffect(activity), TerminalEmulator.ColorGreen);
            await terminal.WriteLineAsync();
            await terminal.WriteColorLineAsync($"    {PrisonActivitySystem.ActivityDescription(activity)}", TerminalEmulator.ColorDarkGray);
            i++;
        }

        await terminal.WriteLineAsync();
        await terminal.WriteAsync(Loc.Get("prison.choose_activity"));
        string input = await terminal.ReadLineAsync();

        if (int.TryParse(input, out int choice) && choice >= 1 && choice <= activities.Count)
        {
            var selectedActivity = activities[choice - 1];
            string result = await PrisonActivitySystem.Instance.PerformActivity(player, selectedActivity);

            await terminal.WriteLineAsync();
            await terminal.WriteColorLineAsync(result, TerminalEmulator.ColorGreen);
            await terminal.WriteLineAsync();
            await terminal.PressAnyKey();
        }

        refreshMenu = true;
    }

    private async Task HandleMenuDisplay(Character player)
    {
        if (player.Expert)
            await DisplayPrisonMenu(player, true, false);
        else
            await DisplayPrisonMenu(player, false, false);
    }

    private async Task HandleStatusDisplay(Character player)
    {
        await ShowCharacterStatus(player);
    }

    private async Task ShowCharacterStatus(Character player)
    {
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync($"=== {Loc.Get("prison.title")} ===");
        await terminal.WriteLineAsync($"{Loc.Get("ui.name_label")}: {player.DisplayName}");
        await terminal.WriteLineAsync($"{Loc.Get("ui.level")}: {player.Level}");
        await terminal.WriteLineAsync($"{Loc.Get("ui.health_label")}: {player.HP}/{player.MaxHP}");
        await terminal.WriteLineAsync(Loc.Get("prison.days_remaining", player.DaysInPrison));
        await terminal.WriteLineAsync(Loc.Get("prison.escape_attempts", player.PrisonEscapes));

        if (player.DaysInPrison == 1)
            await terminal.WriteLineAsync(Loc.Get("prison.released_tomorrow"));
        else
            await terminal.WriteLineAsync(Loc.Get("prison.days_left", player.DaysInPrison));

        await terminal.WriteLineAsync();
        await terminal.PressAnyKey();
    }

    private async Task<bool> HandleQuitConfirmation(Character player)
    {
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync();

        bool confirmed = await terminal.ConfirmAsync(Loc.Get("prison.quit_confirm"), false);
        if (!confirmed)
        {
            // Don't quit, continue prison loop
            return false;
        }

        // Player is logging out - display sleep message
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync(Loc.Get("prison.sleep_hay"));
        await terminal.WriteLineAsync(Loc.Get("prison.long_cold_night"));
        await terminal.WriteLineAsync();
        await Pacing.Wait(1500);

        // Save and quit - throw game exit exception
        throw new GameExitException("Player logging out from prison");
    }

    private async Task HandleOpenCellDoor(Character player)
    {
        await terminal.WriteLineAsync();

        if (player.IsMurderConvict)
        {
            await terminal.WriteColorLineAsync($"  {Loc.Get("prison.door_max_security")}", TerminalEmulator.ColorRed);
            await Pacing.Wait(1500);
            return;
        }

        // Check if cell door can be opened (player was rescued)
        if (await CanOpenCellDoor(player))
        {
            await HandleCellDoorOpen(player);
        }
        else
        {
            await terminal.WriteLineAsync();
            await terminal.WriteColorLineAsync(Loc.Get("prison.iron_door"), TerminalEmulator.ColorRed);
            await terminal.WriteColorLineAsync(Loc.Get("prison.trapped"), TerminalEmulator.ColorRed);
            await Pacing.Wait(1000);
        }
    }

    private async Task HandleDemandRelease(Character player)
    {
        if (player.IsMurderConvict)
        {
            await terminal.WriteLineAsync();
            await terminal.WriteColorLineAsync($"  {Loc.Get("prison.demand_murderer")}", TerminalEmulator.ColorRed);
            await Pacing.Wait(1500);
            return;
        }

        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync();
        await terminal.WriteColorAsync(Loc.Get("prison.clear_throat"), TerminalEmulator.ColorWhite);
        await terminal.WriteColorLineAsync(Loc.Get("prison.let_me_out"), TerminalEmulator.ColorCyan);

        await Task.Delay(GameConfig.PrisonGuardResponseDelay);
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync(Loc.Get("prison.dark_voice"));

        // Random guard response (Pascal: case random(5))
        var random = new System.Random();
        // v1.2.5: the five answers (GameConfig.PrisonDemandResponse1-5 in English) in the player's language
        string response = Loc.Get($"prison.demand_response_{random.Next(5) + 1}");

        await terminal.WriteColorLineAsync(response, TerminalEmulator.ColorMagenta);
        await terminal.WriteLineAsync(Loc.Get("prison.released_probably"));

        // Send demand message to the king
        if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
        {
            var king = CastleLocation.GetCurrentKing();
            if (king != null)
            {
                var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
                if (backend != null)
                {
                    string playerName = player.DisplayName ?? player.Name2 ?? "";
                    try
                    {
                        await backend.SendMessageLocalized(playerName, king.Name, "petition",
                            lang => DemandReleaseMail(lang, playerName));
                    }
                    catch { }
                }
                await terminal.WriteColorLineAsync($"  {Loc.Get("prison.demand_echo")}", TerminalEmulator.ColorDarkGray);
            }
        }
    }

    private async Task<bool> HandleEscapeAttempt(Character player)
    {
        await terminal.WriteLineAsync();

        // Murder convicts cannot escape — maximum security
        if (player.IsMurderConvict)
        {
            await terminal.WriteColorLineAsync("", TerminalEmulator.ColorRed);
            await terminal.WriteColorLineAsync($"  {Loc.Get("prison.escape_max_security")}", TerminalEmulator.ColorRed);
            await terminal.WriteColorLineAsync($"  {Loc.Get("prison.escape_no_chance")}", TerminalEmulator.ColorRed);
            await terminal.WriteColorLineAsync($"  {Loc.Get("prison.escape_full_sentence")}", TerminalEmulator.ColorRed);
            await Pacing.Wait(2000);
            return false;
        }

        if (player.PrisonEscapes < 1)
        {
            await terminal.WriteLineAsync();
            await terminal.WriteColorLineAsync(Loc.Get("prison.no_escapes"), TerminalEmulator.ColorRed);
            await Pacing.Wait(1000);
            return false;
        }

        await terminal.WriteLineAsync();
        bool confirmed = await terminal.ConfirmAsync(Loc.Get("prison.jailbreak_confirm"), true);

        if (!confirmed)
        {
            return false;
        }

        // Use escape attempt
        player.PrisonEscapes--;

        await terminal.WriteLineAsync();
        await Task.Delay(GameConfig.PrisonEscapeDelay);

        // Escape chance based on dexterity and level (better than 50/50)
        var random = new System.Random();
        int escapeChance = 40 + (int)(player.Dexterity / 3) + (player.Level / 2);
        escapeChance = Math.Clamp(escapeChance, 30, 80); // 30-80% chance
        bool success = random.Next(100) < escapeChance;

        if (!success)
        {
            await terminal.WriteColorLineAsync(Loc.Get("prison.escape_failed"), TerminalEmulator.ColorRed);

            // Generate news about failed escape
            NewsSystem.Instance.Newsy(true, Loc.Get("prison.news_escape_failed", player.DisplayName));

            await terminal.WriteLineAsync(Loc.Get("prison.guards_heard"));
            await terminal.WriteLineAsync(Loc.Get("prison.sentence_extended"));
            if (player.DaysInPrison < 255) player.DaysInPrison++;
            await Pacing.Wait(1500);
            return false;
        }
        else
        {
            await terminal.WriteColorLineAsync(Loc.Get("prison.escape_success"), TerminalEmulator.ColorGreen);

            // Generate news about successful escape
            NewsSystem.Instance.Newsy(true, Loc.Get("prison.news_escaped", player.DisplayName));

            await terminal.WriteLineAsync();
            await Pacing.Wait(1000);

            // Free the player
            player.HP = player.MaxHP;
            player.DaysInPrison = 0;
            player.CellDoorOpen = false;

            await terminal.WriteLineAsync(Loc.Get("prison.escaped_message"));
            await terminal.WriteLineAsync(Loc.Get("prison.free_return"));
            await Pacing.Wait(1500);

            // Navigate to Main Street
            throw new LocationExitException(GameLocation.MainStreet);
        }
    }

    private async Task HandleListPrisoners(Character player)
    {
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync();
        await terminal.WriteColorLineAsync(Loc.Get("prison.prisoners_header"), TerminalEmulator.ColorWhite);
        await terminal.WriteColorLineAsync("=========", TerminalEmulator.ColorWhite);

        // List other prisoners
        var prisoners = await GetOtherPrisoners(player);

        if (prisoners.Count == 0)
        {
            await terminal.WriteColorLineAsync(Loc.Get("prison.only_prisoner"), TerminalEmulator.ColorCyan);
        }
        else
        {
            foreach (var prisoner in prisoners)
            {
                await ShowPrisonerInfo(prisoner);
            }
        }

        // Show player's remaining time
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync(Loc.Get("prison.days_left_info", player.DaysInPrison));

        await terminal.WriteLineAsync();
        await terminal.PressAnyKey();
    }

    private async Task<List<Character>> GetOtherPrisoners(Character currentPlayer)
    {
        var prisoners = new List<Character>();
        await Task.CompletedTask;

        // Get NPC prisoners from the NPCSpawnSystem
        var npcPrisoners = NPCSpawnSystem.Instance.GetPrisoners();
        foreach (var npc in npcPrisoners)
        {
            prisoners.Add(npc);
        }

        // Could also add player prisoners here if multiplayer is enabled

        return prisoners;
    }

    private async Task ShowPrisonerInfo(Character prisoner)
    {
        await terminal.WriteColorAsync(prisoner.DisplayName, TerminalEmulator.ColorCyan);
        await terminal.WriteAsync($" {Loc.Get("prison.the_race", GetRaceDisplay(prisoner.Race))}");

        // Show if online/offline/dead
        if (await IsPlayerOnline(prisoner))
        {
            await terminal.WriteColorAsync(Loc.Get("prison.awake"), TerminalEmulator.ColorGreen);
        }
        else if (prisoner.HP < 1)
        {
            await terminal.WriteColorAsync(Loc.Get("prison.dead"), TerminalEmulator.ColorRed);
        }
        else
        {
            await terminal.WriteAsync(Loc.Get("prison.sleeping"));
        }

        // Show days left
        int daysLeft = prisoner.DaysInPrison > 0 ? prisoner.DaysInPrison : 1;
        await terminal.WriteLineAsync(Loc.Get("prison.days_left_parens", daysLeft));
    }

    private string GetRaceDisplay(CharacterRace race)
    {
        return GameConfig.GetLocalizedRaceName(race);
    }

    private Task<bool> IsPlayerOnline(Character player)
    {
        // Online player checking not yet implemented
        return Task.FromResult(false);
    }

    public Task<List<string>> GetLocationCommands(Character player)
    {
        var commands = new List<string>
        {
            Loc.Get("prison.cmd_menu"),
            Loc.Get("prison.cmd_who"),
            Loc.Get("prison.cmd_demand"),
            Loc.Get("prison.cmd_open"),
            Loc.Get("prison.cmd_escape"),
            Loc.Get("prison.cmd_status"),
            Loc.Get("prison.cmd_quit")
        };

        return Task.FromResult(commands);
    }

    public Task<bool> CanEnterLocation(Character player)
    {
        // Can only enter if actually imprisoned
        return Task.FromResult(player.DaysInPrison > 0);
    }

    public Task<string> GetLocationStatus(Character player)
    {
        int daysLeft = player.DaysInPrison;
        return Task.FromResult(Loc.Get("prison.location_status", daysLeft, player.PrisonEscapes));
    }

    /// <summary>v1.2.5: text wrapped to 79 columns, each row indented by two spaces.</summary>
    private async Task WriteWrappedAsync(string text, string color)
    {
        foreach (var line in UsurperRemake.UI.UIHelper.WordWrap(text, 77))
            await terminal.WriteColorLineAsync("  " + line, color);
    }

    /// <summary>v1.2.5: a prisoner's bail petition to a human monarch, in the monarch's language.</summary>
    internal static string BailPetitionMail(string lang, string prisoner) =>
        Loc.GetIn(lang, "prison.mail_petition_bail", prisoner);

    /// <summary>v1.2.5: a prisoner's plea for clemency to a human monarch, in the monarch's language.</summary>
    internal static string ClemencyPleaMail(string lang, string prisoner) =>
        Loc.GetIn(lang, "prison.mail_plea_clemency", prisoner);

    /// <summary>v1.2.5: a prisoner's demand for release to a human monarch, in the monarch's language.</summary>
    internal static string DemandReleaseMail(string lang, string prisoner) =>
        Loc.GetIn(lang, "prison.mail_demand_release", prisoner);

    #region Vex Companion Recruitment

    /// <summary>
    /// Handle encountering Vex in prison - he can help you escape
    /// Returns true if player escaped (exits prison)
    /// </summary>
    private async Task<bool> HandleVexEncounter(Character player)
    {
        if (!CanMeetVex(player))
        {
            await terminal.WriteLineAsync();
            await terminal.WriteColorLineAsync(Loc.Get("prison.no_one_unusual"), TerminalEmulator.ColorDarkGray);
            await Pacing.Wait(1500);
            return false;
        }

        // v0.60.0 beta: defensive null guards. Player report: pressing [V]
        // in prison threw "Object reference not set to an instance of an
        // object" because something in the Vex object chain was null
        // (DialogueHints, CombatRole, Abilities, BackstoryBrief, etc.) on
        // a fresh-character context. CanMeetVex passed (vex existed) but
        // its sub-fields weren't fully initialized. Defensive defaults.
        var companionSystem = CompanionSystem.Instance;
        if (companionSystem == null)
        {
            await terminal.WriteLineAsync();
            await terminal.WriteColorLineAsync(Loc.Get("prison.no_one_unusual"), TerminalEmulator.ColorDarkGray);
            return false;
        }
        var vex = companionSystem.GetCompanion(CompanionId.Vex);
        if (vex == null)
        {
            await terminal.WriteLineAsync();
            await terminal.WriteColorLineAsync(Loc.Get("prison.no_one_unusual"), TerminalEmulator.ColorDarkGray);
            return false;
        }
        // Pre-extract sub-fields with safe fallbacks so the cinematic
        // never crashes on a missing piece of companion metadata.
        string vexName = vex.Name ?? "Vex";
        // v1.2.5: the companion's texts in the player's language
        string vexTitle = vex.Title != null ? vex.LocTitle : "";
        string vexRole = InnLocation.RoleName(vex.CombatRole);
        string vexAbilities = vex.Abilities != null ? string.Join(", ", vex.LocAbilities) : "";
        string vexBackstory = vex.BackstoryBrief != null ? vex.LocBackstory : "";
        var hints = vex.DialogueHints;
        string hint0 = (hints != null && hints.Length > 0) ? vex.LocDialogueHint(0) : "...";
        string hint1 = (hints != null && hints.Length > 1) ? vex.LocDialogueHint(1) : "...";
        string hint2 = (hints != null && hints.Length > 2) ? vex.LocDialogueHint(2) : "...";

        await terminal.ClearScreenAsync();
        await terminal.WriteLineAsync();
        WriteBoxHeader(Loc.Get("prison.voice_darkness"), "cyan", 66);
        terminal.WriteLine("");
        await Pacing.Wait(1000);

        await terminal.WriteLineAsync(Loc.Get("prison.vex_voice"));
        await terminal.WriteLineAsync();
        await Pacing.Wait(500);

        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_psst1"), TerminalEmulator.ColorYellow);
        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_psst2"), TerminalEmulator.ColorYellow);
        await terminal.WriteLineAsync();
        await Pacing.Wait(1500);

        await terminal.WriteLineAsync(Loc.Get("prison.vex_peer1"));
        await terminal.WriteLineAsync(Loc.Get("prison.vex_peer2"));
        await terminal.WriteLineAsync(Loc.Get("prison.vex_peer3"));
        await terminal.WriteLineAsync();
        await Pacing.Wait(1500);

        await terminal.WriteColorAsync($"\"{hint0}\"", TerminalEmulator.ColorCyan);
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync();
        await Pacing.Wait(2000);

        await terminal.WriteLineAsync(Loc.Get("prison.vex_metal"));
        await terminal.WriteLineAsync(Loc.Get("prison.vex_locks1"));
        await terminal.WriteLineAsync(Loc.Get("prison.vex_locks2"));
        await terminal.WriteLineAsync();
        await Pacing.Wait(1500);

        await terminal.WriteColorAsync($"\"{hint1}\"", TerminalEmulator.ColorCyan);
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync();
        await Pacing.Wait(1500);

        // Show his details
        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_intro", vexName, vexTitle), TerminalEmulator.ColorYellow);
        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_role", vexRole), TerminalEmulator.ColorYellow);
        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_abilities", vexAbilities), TerminalEmulator.ColorYellow);
        await terminal.WriteLineAsync();

        foreach (var row in UsurperRemake.UI.UIHelper.WordWrap(vexBackstory))   // v1.2.5: fits 79 columns
            await terminal.WriteColorLineAsync(row, TerminalEmulator.ColorDarkGray);
        await terminal.WriteLineAsync();
        await Pacing.Wait(1500);

        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_menu_escape"), TerminalEmulator.ColorGreen);
        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_menu_talk"), TerminalEmulator.ColorCyan);
        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_menu_leave"), TerminalEmulator.ColorDarkGray);
        await terminal.WriteLineAsync();

        await terminal.WriteAsync(Loc.Get("ui.your_choice"));
        string choice = await terminal.ReadLineAsync();

        switch (choice.ToUpper())
        {
            case "E":
                return await VexHelpsEscape(player, vex);

            case "T":
                await TalkToVex(player, vex);
                return false;

            default:
                await terminal.WriteLineAsync();
                await terminal.WriteColorLineAsync(Loc.Get("prison.vex_shake_head"), TerminalEmulator.ColorDarkGray);
                await terminal.WriteLineAsync();
                await terminal.WriteColorLineAsync(Loc.Get("prison.vex_your_loss"), TerminalEmulator.ColorYellow);
                await terminal.WriteColorAsync($"\"{hint2}\"", TerminalEmulator.ColorCyan);
                await terminal.WriteLineAsync();
                await Pacing.Wait(2000);
                break;
        }

        // Mark encounter as complete
        StoryProgressionSystem.Instance.SetStoryFlag("vex_prison_encounter_complete", true);
        refreshMenu = true;
        return false;
    }

    /// <summary>
    /// Vex helps the player escape from prison
    /// </summary>
    private async Task<bool> VexHelpsEscape(Character player, UsurperRemake.Systems.Companion vex)
    {
        var companionSystem = CompanionSystem.Instance;
        string vexName = vex?.Name ?? "Vex";

        await terminal.WriteLineAsync();
        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_excellent"), TerminalEmulator.ColorYellow);
        await terminal.WriteLineAsync();
        await Pacing.Wait(1000);

        await terminal.WriteLineAsync(Loc.Get("prison.vex_works_lock"));
        await terminal.WriteLineAsync(Loc.Get("prison.vex_clicks"));
        await terminal.WriteLineAsync();
        await Pacing.Wait(1500);

        await terminal.WriteLineAsync(Loc.Get("prison.vex_trick1"));
        await terminal.WriteLineAsync(Loc.Get("prison.vex_trick2"));
        await terminal.WriteLineAsync(Loc.Get("prison.vex_trick3"));
        await terminal.WriteLineAsync();
        await Pacing.Wait(1500);

        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_click_loud"), TerminalEmulator.ColorGreen);
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync(Loc.Get("prison.vex_door_open"));
        await terminal.WriteLineAsync();
        await Pacing.Wait(1000);

        await terminal.WriteLineAsync(Loc.Get("prison.vex_smoke"));
        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_sewers1"), TerminalEmulator.ColorYellow);
        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_sewers2"), TerminalEmulator.ColorYellow);
        await terminal.WriteLineAsync();
        await Pacing.Wait(1500);

        await terminal.WriteLineAsync(Loc.Get("prison.vex_passages1"));
        await terminal.WriteLineAsync(Loc.Get("prison.vex_passages2"));
        await terminal.WriteLineAsync();
        await Pacing.Wait(1000);

        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_dying1"), TerminalEmulator.ColorCyan);
        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_dying2"), TerminalEmulator.ColorCyan);
        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_dying3"), TerminalEmulator.ColorCyan);
        await terminal.WriteLineAsync();
        await Pacing.Wait(2000);

        await terminal.WriteLineAsync(Loc.Get("prison.vex_glance1"));
        await terminal.WriteLineAsync(Loc.Get("prison.vex_glance2"));
        await terminal.WriteLineAsync();
        await Pacing.Wait(1000);

        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_tag_along"), TerminalEmulator.ColorYellow);
        await terminal.WriteLineAsync();
        await Pacing.Wait(1500);

        // Recruit Vex
        bool success = await companionSystem.RecruitCompanion(
            CompanionId.Vex, player, terminal);

        if (success)
        {
            await terminal.WriteColorLineAsync(Loc.Get("prison.vex_joined", vexName), TerminalEmulator.ColorGreen);
            await terminal.WriteLineAsync();
            await terminal.WriteColorLineAsync(Loc.Get("prison.vex_warning_dying"), TerminalEmulator.ColorRed);
            await terminal.WriteColorLineAsync(Loc.Get("prison.vex_make_most"), TerminalEmulator.ColorYellow);
            await terminal.WriteLineAsync();

            // Generate news
            NewsSystem.Instance.Newsy(true, Loc.Get("prison.news_vex_escape", player.DisplayName, vexName));
        }

        // Free the player
        player.HP = player.MaxHP;
        player.DaysInPrison = 0;
        player.CellDoorOpen = false;

        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_emerge"), TerminalEmulator.ColorWhite);
        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_free"), TerminalEmulator.ColorGreen);
        await terminal.WriteLineAsync();

        // Mark encounter as complete
        StoryProgressionSystem.Instance.SetStoryFlag("vex_prison_encounter_complete", true);

        await terminal.PressAnyKey();

        // Navigate to Main Street
        throw new LocationExitException(GameLocation.MainStreet);
    }

    /// <summary>
    /// Have a deeper conversation with Vex about his condition
    /// </summary>
    private async Task TalkToVex(Character player, UsurperRemake.Systems.Companion vex)
    {
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync(Loc.Get("prison.vex_condition"));
        await terminal.WriteLineAsync();
        await Pacing.Wait(1000);

        await terminal.WriteColorLineAsync(vex.Description ?? "", TerminalEmulator.ColorWhite);
        await terminal.WriteLineAsync();
        await Pacing.Wait(1500);

        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_born1"), TerminalEmulator.ColorCyan);
        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_born2"), TerminalEmulator.ColorCyan);
        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_born3"), TerminalEmulator.ColorCyan);
        await terminal.WriteLineAsync();
        await Pacing.Wait(2000);

        await terminal.WriteLineAsync(Loc.Get("prison.vex_lockpick"));
        await terminal.WriteLineAsync(Loc.Get("prison.vex_best_thief1"));
        await terminal.WriteLineAsync(Loc.Get("prison.vex_best_thief2"));
        await terminal.WriteLineAsync(Loc.Get("prison.vex_best_thief3"));
        await terminal.WriteLineAsync();
        await Pacing.Wait(2000);

        if (!string.IsNullOrEmpty(vex.PersonalQuestDescription))
        {
            await terminal.WriteColorLineAsync(Loc.Get("prison.vex_personal_quest", vex.LocQuestName), TerminalEmulator.ColorMagenta);
            foreach (var row in UsurperRemake.UI.UIHelper.WordWrap($"\"{vex.LocQuestDescription}\""))   // v1.2.5: fits 79 columns
                await terminal.WriteColorLineAsync(row, TerminalEmulator.ColorMagenta);
            await terminal.WriteLineAsync();
        }

        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_too_short"), TerminalEmulator.ColorYellow);
        await terminal.WriteLineAsync();
        await Pacing.Wait(1500);

        await terminal.WriteAsync(Loc.Get("prison.vex_escape_prompt"));

        if (await terminal.AskYesNoAsync(""))
        {
            await VexHelpsEscape(player, vex);
        }
        else
        {
            await terminal.WriteLineAsync();
            await terminal.WriteColorLineAsync(Loc.Get("prison.vex_suit_yourself"), TerminalEmulator.ColorYellow);
            await Pacing.Wait(1500);
        }
    }

    #endregion

    private void EmitElectronEvents(Character player)
    {
        if (player == null) return;

        ElectronBridge.EmitLocation(
            name: Loc.Get("prison.title"),
            description: Loc.Get("engine.days_remaining", player.DaysInPrison),
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
            new() { Key = "W", Label = Loc.Get("prison.electron_who"), Category = "info", Icon = "list" },
            new() { Key = "D", Label = Loc.Get("prison.electron_demand"), Category = "social", Icon = "shout" },
            new() { Key = "O", Label = Loc.Get("prison.electron_open"), Category = "action", Icon = "door" },
            new() { Key = "E", Label = Loc.Get("prison.electron_escape"), Category = "danger", Icon = "escape" },
            new() { Key = "S", Label = Loc.Get("menu.action.status"), Category = "info", Icon = "info" },
            new() { Key = "A", Label = Loc.Get("prison.electron_activities"), Category = "action", Icon = "activity" },
            new() { Key = "B", Label = Loc.Get("prison.electron_bail"), Category = "shop", Icon = "gold" },
            new() { Key = "P", Label = Loc.Get("prison.electron_petition"), Category = "social", Icon = "petition" },
        };

        if (CanMeetVex(player))
        {
            menu.Add(new() { Key = "V", Label = Loc.Get("prison.electron_vex"), Category = "social", Icon = "vex" });
        }

        menu.Add(new() { Key = "Q", Label = Loc.Get("prison.electron_quit"), Category = "navigate", Icon = "back" });

        ElectronBridge.EmitMenu(menu);
    }
}
