using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UsurperRemake.BBS;
using UsurperRemake.Utils;
using UsurperRemake.Systems;

namespace UsurperRemake.Locations
{
    /// <summary>
    /// The Sanctum -- v0.62.x "Light and Dark" Phase 6 (Light activity hub). Structural yin/yang
    /// mirror of the Dark Alley as a Good/Holy player's home. Tier 1 ships three charity verbs
    /// (Alms / Orphanage / Hospice) and a read-only Hall of Heroes wall. Tournament of Honor and
    /// Crown commissions deferred to slice 6b.
    ///
    /// Access: Evil players are wards-barred at the door via AlignmentSystem.CanAccessLocation
    /// (mirrors Church/Temple wards). All other alignments are admitted; Dark gets a cold welcome
    /// flavor line but charity verbs work so a Dark player can wash darkness via paired-movement
    /// ChangeAlignment.
    ///
    /// Economy: gold flows OUT (player spends to climb Renown), NOT in. Each verb is daily-capped.
    /// Faith faction members get a 10% discount on charity costs (mirrors Phase 5's Black Market
    /// Shadows-rank discount). All Chivalry/Faith-standing climbs route through
    /// AlignmentSystem.ChangeAlignment + FactionSystem.ModifyReputation so the DR curve and the
    /// reputation cascade both fire correctly.
    /// </summary>
    public class SanctumLocation : BaseLocation
    {
        public SanctumLocation() : base(GameLocation.Sanctum, "The Sanctum",
            "A sunlit hall where the city's sick and forgotten are cared for. The poor come for bread, the lost for solace, the devout for purpose.")
        {
        }

        protected override void SetupLocation()
        {
            PossibleExits.Add(GameLocation.MainStreet);
        }

        protected override void DisplayLocation()
        {
            if (GameConfig.ScreenReaderMode)
            {
                DisplayLocationSR();
                return;
            }
            if (IsBBSSession)
            {
                DisplayLocationBBS();
                return;
            }
            DisplayLocationVisual();
        }

        private void DisplayLocationVisual()
        {
            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("sanctum.header"), "bright_yellow");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("sanctum.desc_1"));
            terminal.WriteLine(Loc.Get("sanctum.desc_2"));
            terminal.WriteLine("");

            // Cold-welcome flavor for Dark-aligned visitors (Evil is blocked at the door upstream
            // by CanAccessLocation, so the only Dark band that reaches here is Dark itself).
            var alignSys = AlignmentSystem.Instance;
            var band = alignSys.GetAlignment(currentPlayer);
            if (band == AlignmentSystem.AlignmentType.Dark)
            {
                terminal.SetColor("dark_gray");
                terminal.WriteLine($"  {Loc.Get("sanctum.cold_welcome")}");
                terminal.WriteLine("");
            }

            // Renown standing
            var renownTier = alignSys.GetRenownTier(currentPlayer);
            if (renownTier > AlignmentSystem.RenownTier.None)
            {
                string rankName = Loc.Get($"renown.tier_{renownTier.ToString().ToLowerInvariant()}");
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"  {Loc.Get("sanctum.your_standing", rankName)}");
                terminal.WriteLine("");
            }

            // Faith-member discount notice
            bool isFaithMember = FactionSystem.Instance?.PlayerFaction == Faction.TheFaith;
            if (isFaithMember)
            {
                terminal.SetColor("bright_cyan");
                terminal.WriteLine($"  {Loc.Get("sanctum.faith_member_discount", (int)(GameConfig.SanctumFaithMemberDiscount * 100))}");
                terminal.WriteLine("");
            }

            // Acts of Mercy section
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("sanctum.menu_acts_header"));
            terminal.SetColor("white");

            int level = currentPlayer.Level;
            long almsCost = ComputeCharityCost(GameConfig.AlmsGoldPerLevel * level, isFaithMember);
            long orphCost = ComputeCharityCost(GameConfig.OrphanageGoldPerLevel * level, isFaithMember);
            long hospCost = ComputeCharityCost(GameConfig.HospiceGoldPerLevel * level, isFaithMember);

            WriteCharityOption("A", "sanctum.menu_alms",
                almsCost, currentPlayer.AlmsGivenToday, GameConfig.MaxAlmsPerDay);
            WriteCharityOption("O", "sanctum.menu_orphanage",
                orphCost, currentPlayer.OrphanageGiftsToday, GameConfig.MaxOrphanageGiftsPerDay);
            WriteCharityOption("H", "sanctum.menu_hospice",
                hospCost, currentPlayer.HospiceTithesToday, GameConfig.MaxHospiceTithesPerDay);
            terminal.WriteLine("");

            // Other actions
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("sanctum.menu_other_header"));
            terminal.SetColor("white");
            WriteSanctumOption("V", Loc.Get("sanctum.menu_hall_of_heroes"));
            // v0.62.x Phase 7 (slice 6b): Tournament of Honor -- gated on Defender+ Renown.
            // Hidden (not greyed) below the standing -- the player has to earn the right to see it,
            // mirroring how Slice 5's Black Market is hidden until the player is Shadows-eligible
            // or freelance-evil-eligible.
            if (alignSys.GetRenownTier(currentPlayer) >= AlignmentSystem.RenownTier.Defender)
            {
                WriteSanctumOption("T", Loc.Get("sanctum.menu_tournament_of_honor"));
            }
            WriteSanctumOption("R", Loc.Get("sanctum.menu_return"));
            terminal.WriteLine("");

            ShowStatusLine();
        }

        private void DisplayLocationBBS()
        {
            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("sanctum.header"), "bright_yellow");
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("sanctum.desc_1"));
            terminal.WriteLine("");

            var alignSys = AlignmentSystem.Instance;
            var band = alignSys.GetAlignment(currentPlayer);
            if (band == AlignmentSystem.AlignmentType.Dark)
            {
                terminal.SetColor("dark_gray");
                terminal.WriteLine($"  {Loc.Get("sanctum.cold_welcome")}");
                terminal.WriteLine("");
            }

            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("sanctum.menu_acts_header"));
            ShowBBSMenuRow(
                ("A", "bright_yellow", Loc.Get("sanctum.menu_alms")),
                ("O", "bright_yellow", Loc.Get("sanctum.menu_orphanage")),
                ("H", "bright_yellow", Loc.Get("sanctum.menu_hospice")));
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("sanctum.menu_other_header"));
            bool showTournamentBBS = AlignmentSystem.Instance.GetRenownTier(currentPlayer) >= AlignmentSystem.RenownTier.Defender;
            if (showTournamentBBS)
            {
                ShowBBSMenuRow(
                    ("V", "bright_yellow", Loc.Get("sanctum.menu_hall_of_heroes")),
                    ("T", "bright_yellow", Loc.Get("sanctum.menu_tournament_of_honor")),
                    ("R", "bright_yellow", Loc.Get("sanctum.menu_return")));
            }
            else
            {
                // ShowBBSMenuRow accepts `params`; pass 2 cells instead of 3 to avoid the
                // cosmetic `[]` empty-bracket artifact the reviewer flagged.
                ShowBBSMenuRow(
                    ("V", "bright_yellow", Loc.Get("sanctum.menu_hall_of_heroes")),
                    ("R", "bright_yellow", Loc.Get("sanctum.menu_return")));
            }
            ShowBBSFooter();
        }

        private void DisplayLocationSR()
        {
            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("sanctum.header"), "bright_yellow");
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("sanctum.desc_1"));
            terminal.WriteLine(Loc.Get("sanctum.desc_2"));
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("sanctum.menu_acts_header"));
            WriteSRMenuOption("A", Loc.Get("sanctum.menu_alms"));
            WriteSRMenuOption("O", Loc.Get("sanctum.menu_orphanage"));
            WriteSRMenuOption("H", Loc.Get("sanctum.menu_hospice"));
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("sanctum.menu_other_header"));
            WriteSRMenuOption("V", Loc.Get("sanctum.menu_hall_of_heroes"));
            if (AlignmentSystem.Instance.GetRenownTier(currentPlayer) >= AlignmentSystem.RenownTier.Defender)
            {
                WriteSRMenuOption("T", Loc.Get("sanctum.menu_tournament_of_honor"));
            }
            WriteSRMenuOption("R", Loc.Get("sanctum.menu_return"));
            terminal.WriteLine("");
            ShowStatusLine();
        }

        protected override async Task<bool> ProcessChoice(string choice)
        {
            var (handled, shouldExit) = await TryProcessGlobalCommand(choice);
            if (handled) return shouldExit;

            if (string.IsNullOrWhiteSpace(choice)) return false;

            char ch = char.ToUpperInvariant(choice.Trim()[0]);
            switch (ch)
            {
                case 'A':
                    await GiveAlms();
                    return false;
                case 'O':
                    await FundOrphanage();
                    return false;
                case 'H':
                    await TitheHospice();
                    return false;
                case 'V':
                    await ShowHallOfHeroes();
                    return false;
                case 'T':
                    if (AlignmentSystem.Instance.GetRenownTier(currentPlayer) >= AlignmentSystem.RenownTier.Defender)
                    {
                        await StartHonorTournament();
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine(Loc.Get("sanctum.invalid_choice"));
                        await Task.Delay(900);
                    }
                    return false;
                case 'R':
                    await NavigateToLocation(GameLocation.MainStreet);
                    return true;
                case '?':
                    return false;
                default:
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("sanctum.invalid_choice"));
                    await Task.Delay(900);
                    return false;
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        // Helpers
        // ════════════════════════════════════════════════════════════════════════

        private long ComputeCharityCost(long baseCost, bool isFaithMember)
        {
            if (isFaithMember) baseCost = (long)(baseCost * (1.0f - GameConfig.SanctumFaithMemberDiscount));
            return Math.Max(1, baseCost);
        }

        private void WriteCharityOption(string key, string labelKey, long cost, int usedToday, int dailyCap)
        {
            bool atCap = usedToday >= dailyCap;
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor(atCap ? "dark_gray" : "bright_yellow");
            terminal.Write(key);
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor(atCap ? "dark_gray" : "white");
            terminal.Write(Loc.Get(labelKey));
            terminal.SetColor("gray");
            terminal.WriteLine($"  -- {cost} {GameConfig.MoneyType}  ({usedToday}/{dailyCap} today)");
        }

        private void WriteSanctumOption(string key, string label)
        {
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write(key);
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(label);
        }

        /// <summary>
        /// Shared charity-completion logic. Awards Chivalry through ChangeAlignment (so the DR
        /// curve and paired-darkness reduction both fire) and Faith standing through FactionSystem
        /// (so the cascade fires -- Faith work auto-dings Shadows). Also tracks the lifetime gold
        /// donation milestone for future achievement hooks.
        /// </summary>
        private void AwardCharityRenown(long goldSpent, int chivalryReward, int faithReward, string reason)
        {
            UsurperRemake.Systems.AlignmentSystem.Instance.ChangeAlignment(currentPlayer, chivalryReward, isGood: true, reason);
            UsurperRemake.Systems.FactionSystem.Instance?.ModifyReputation(Faction.TheFaith, faithReward);
            currentPlayer.LifetimeCharityGoldDonated += goldSpent;
            currentPlayer.Statistics?.RecordGoldSpent(goldSpent);
        }

        // ════════════════════════════════════════════════════════════════════════
        // Charity verbs
        // ════════════════════════════════════════════════════════════════════════

        private async Task GiveAlms()
        {
            terminal.WriteLine("");

            if (currentPlayer.AlmsGivenToday >= GameConfig.MaxAlmsPerDay)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {Loc.Get("sanctum.alms_cap_reached", GameConfig.MaxAlmsPerDay)}");
                await Task.Delay(1500);
                return;
            }

            bool isFaithMember = UsurperRemake.Systems.FactionSystem.Instance?.PlayerFaction == Faction.TheFaith;
            long cost = ComputeCharityCost(GameConfig.AlmsGoldPerLevel * currentPlayer.Level, isFaithMember);

            if (currentPlayer.Gold < cost)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"  {Loc.Get("sanctum.cannot_afford", cost)}");
                await Task.Delay(1500);
                return;
            }

            // Confirm
            string confirm = (await terminal.GetInput(Loc.Get("sanctum.alms_confirm", cost))).Trim().ToUpperInvariant();
            if (!(GameConfig.IsAffirmative(confirm)))
            {
                return;
            }

            currentPlayer.Gold -= cost;
            currentPlayer.AlmsGivenToday++;
            AwardCharityRenown(cost, GameConfig.AlmsChivalryReward, GameConfig.AlmsFaithStandingReward, "alms to the poor");

            terminal.SetColor("bright_green");
            terminal.WriteLine($"  {Loc.Get("sanctum.alms_flavor")}");
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("sanctum.alms_reward", GameConfig.AlmsChivalryReward, GameConfig.AlmsFaithStandingReward)}");
            await Task.Delay(1800);
        }

        private async Task FundOrphanage()
        {
            terminal.WriteLine("");

            if (currentPlayer.OrphanageGiftsToday >= GameConfig.MaxOrphanageGiftsPerDay)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {Loc.Get("sanctum.orphanage_cap_reached", GameConfig.MaxOrphanageGiftsPerDay)}");
                await Task.Delay(1500);
                return;
            }

            bool isFaithMember = UsurperRemake.Systems.FactionSystem.Instance?.PlayerFaction == Faction.TheFaith;
            long cost = ComputeCharityCost(GameConfig.OrphanageGoldPerLevel * currentPlayer.Level, isFaithMember);

            if (currentPlayer.Gold < cost)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"  {Loc.Get("sanctum.cannot_afford", cost)}");
                await Task.Delay(1500);
                return;
            }

            string confirm = (await terminal.GetInput(Loc.Get("sanctum.orphanage_confirm", cost))).Trim().ToUpperInvariant();
            if (!(GameConfig.IsAffirmative(confirm)))
            {
                return;
            }

            currentPlayer.Gold -= cost;
            currentPlayer.OrphanageGiftsToday++;
            AwardCharityRenown(cost, GameConfig.OrphanageChivalryReward, GameConfig.OrphanageFaithStandingReward, "funded the orphanage");

            terminal.SetColor("bright_green");
            terminal.WriteLine($"  {Loc.Get("sanctum.orphanage_flavor")}");
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("sanctum.orphanage_reward", GameConfig.OrphanageChivalryReward, GameConfig.OrphanageFaithStandingReward)}");
            await Task.Delay(1800);
        }

        private async Task TitheHospice()
        {
            terminal.WriteLine("");

            if (currentPlayer.HospiceTithesToday >= GameConfig.MaxHospiceTithesPerDay)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {Loc.Get("sanctum.hospice_cap_reached", GameConfig.MaxHospiceTithesPerDay)}");
                await Task.Delay(1500);
                return;
            }

            bool isFaithMember = UsurperRemake.Systems.FactionSystem.Instance?.PlayerFaction == Faction.TheFaith;
            long cost = ComputeCharityCost(GameConfig.HospiceGoldPerLevel * currentPlayer.Level, isFaithMember);

            if (currentPlayer.Gold < cost)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"  {Loc.Get("sanctum.cannot_afford", cost)}");
                await Task.Delay(1500);
                return;
            }

            string confirm = (await terminal.GetInput(Loc.Get("sanctum.hospice_confirm", cost))).Trim().ToUpperInvariant();
            if (!(GameConfig.IsAffirmative(confirm)))
            {
                return;
            }

            currentPlayer.Gold -= cost;
            currentPlayer.HospiceTithesToday++;
            AwardCharityRenown(cost, GameConfig.HospiceChivalryReward, GameConfig.HospiceFaithStandingReward, "tithed the hospice");

            terminal.SetColor("bright_green");
            terminal.WriteLine($"  {Loc.Get("sanctum.hospice_flavor")}");
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("sanctum.hospice_reward", GameConfig.HospiceChivalryReward, GameConfig.HospiceFaithStandingReward)}");
            await Task.Delay(1800);
        }

        // ════════════════════════════════════════════════════════════════════════
        // Hall of Heroes (statistics-based read-only wall for slice 1; slice 6b
        // can enrich with NPC-impression-based gratitude lines).
        // ════════════════════════════════════════════════════════════════════════

        private async Task ShowHallOfHeroes()
        {
            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("sanctum.hall_of_heroes_header"), "bright_yellow");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("sanctum.hall_intro_1"));
            terminal.WriteLine(Loc.Get("sanctum.hall_intro_2"));
            terminal.WriteLine("");

            // Lifetime totals
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("sanctum.hall_lifetime_header"));
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"  {Loc.Get("sanctum.hall_lifetime_donated", currentPlayer.LifetimeCharityGoldDonated)}");

            // Current renown standing
            var renownTier = AlignmentSystem.Instance.GetRenownTier(currentPlayer);
            if (renownTier > AlignmentSystem.RenownTier.None)
            {
                string rankName = Loc.Get($"renown.tier_{renownTier.ToString().ToLowerInvariant()}");
                terminal.SetColor("bright_cyan");
                terminal.WriteLine($"  {Loc.Get("sanctum.hall_current_renown", rankName, currentPlayer.Chivalry)}");
            }

            // Faith standing (cross-reference via FactionSystem)
            var factionSys = UsurperRemake.Systems.FactionSystem.Instance;
            if (factionSys != null && factionSys.FactionStanding.TryGetValue(Faction.TheFaith, out int faithStanding) && faithStanding > 0)
            {
                terminal.SetColor("bright_white");
                terminal.WriteLine($"  {Loc.Get("sanctum.hall_faith_standing", faithStanding)}");
            }
            terminal.WriteLine("");

            // Today's contributions
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("sanctum.hall_today_header"));
            terminal.SetColor("white");
            terminal.WriteLine($"  {Loc.Get("sanctum.hall_today_alms", currentPlayer.AlmsGivenToday, GameConfig.MaxAlmsPerDay)}");
            terminal.WriteLine($"  {Loc.Get("sanctum.hall_today_orphanage", currentPlayer.OrphanageGiftsToday, GameConfig.MaxOrphanageGiftsPerDay)}");
            terminal.WriteLine($"  {Loc.Get("sanctum.hall_today_hospice", currentPlayer.HospiceTithesToday, GameConfig.MaxHospiceTithesPerDay)}");
            terminal.WriteLine("");

            // Future-tease for slice 6b
            terminal.SetColor("dark_gray");
            terminal.WriteLine($"  {Loc.Get("sanctum.hall_coming_soon")}");
            terminal.WriteLine("");

            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("ui.press_enter"));
            await terminal.ReadKeyAsync();
        }

        // ════════════════════════════════════════════════════════════════════════
        // Tournament of Honor (Phase 7 / slice 6b). A focused 3-champion ritual.
        // Reuses ArenaChampionTier (the Gauntlet's tier counter -- highest reached
        // across either tournament wins), GauntletRunsToday (so you get one or the
        // other today, not both), and PFights (per-day combat-slot).
        // ════════════════════════════════════════════════════════════════════════

        private async Task StartHonorTournament()
        {
            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("tournament.header"), "bright_yellow");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("tournament.desc_1"));
            terminal.WriteLine(Loc.Get("tournament.desc_2"));
            terminal.WriteLine("");

            // Entry checks. Reuse the existing Gauntlet daily cap so this is "one or the other today,
            // not both" -- prevents Fame/loot stacking from both tournaments in a single session.
            if (currentPlayer.PFights <= 0)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("anchor_road.no_fights_left"));
                terminal.WriteLine("");
                terminal.SetColor("darkgray");
                terminal.WriteLine(Loc.Get("ui.press_enter"));
                await terminal.ReadKeyAsync();
                return;
            }

            if (currentPlayer.GauntletRunsToday >= GameConfig.MaxGauntletRunsPerDay)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("anchor_road.gauntlet_daily_cap_reached", GameConfig.MaxGauntletRunsPerDay));
                terminal.WriteLine("");
                terminal.SetColor("darkgray");
                terminal.WriteLine(Loc.Get("ui.press_enter"));
                await terminal.ReadKeyAsync();
                return;
            }

            // Same quadratic entry fee as the Gauntlet -- the ritual costs what the gladiatorial
            // arena does. The Tournament is shorter (3 fights vs 10) so gold-per-fight pays out
            // higher to compensate.
            long entryFee = (long)GameConfig.GauntletEntryFeeQuadraticCoefficient * currentPlayer.Level * currentPlayer.Level;
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("tournament.details_header"));
            terminal.SetColor("white");
            terminal.Write(Loc.Get("anchor_road.entry_fee"));
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("anchor_road.gold_amount", entryFee.ToString("N0")));
            terminal.SetColor("white");
            terminal.Write(Loc.Get("anchor_road.your_gold"));
            terminal.SetColor(currentPlayer.Gold >= entryFee ? "bright_green" : "red");
            terminal.WriteLine($"{currentPlayer.Gold:N0}");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("anchor_road.your_hp"));
            terminal.SetColor(currentPlayer.HP > currentPlayer.MaxHP / 2 ? "bright_green" : "red");
            terminal.WriteLine($"{currentPlayer.HP}/{currentPlayer.MaxHP}");
            int runsRemaining = Math.Max(0, GameConfig.MaxGauntletRunsPerDay - currentPlayer.GauntletRunsToday);
            terminal.SetColor("white");
            terminal.Write(Loc.Get("anchor_road.gauntlet_runs_today"));
            terminal.SetColor(runsRemaining > 0 ? "bright_green" : "red");
            terminal.WriteLine($"{runsRemaining}/{GameConfig.MaxGauntletRunsPerDay}");
            terminal.WriteLine("");

            if (currentPlayer.Gold < entryFee)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("anchor_road.need_gold", $"{entryFee:N0}", $"{currentPlayer.Gold:N0}"));
                terminal.WriteLine("");
                terminal.SetColor("darkgray");
                terminal.WriteLine(Loc.Get("ui.press_enter"));
                await terminal.ReadKeyAsync();
                return;
            }

            terminal.SetColor("cyan");
            string confirm = (await terminal.GetInput(Loc.Get("tournament.enter_prompt", $"{entryFee:N0}"))).Trim().ToUpperInvariant();
            if (!(GameConfig.IsAffirmative(confirm)))
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("tournament.cancelled"));
                await Task.Delay(900);
                return;
            }

            // Commit: deduct fee, burn the daily slot + the combat slot. Cap-state mutations happen
            // up front so a mid-tournament disconnect cleanly burns the entry (matching the Gauntlet's
            // behavior at the same point in the flow).
            currentPlayer.Gold -= entryFee;
            currentPlayer.PFights--;
            currentPlayer.GauntletRunsToday++;
            currentPlayer.Statistics?.RecordGoldSpent(entryFee);

            // Match the codebase Random.Shared idiom (per v0.52.11 audit) -- thread-safe, no
            // per-call state init.
            var rng = Random.Shared;
            int wavesCompleted = 0;
            long totalGold = 0;
            long totalXP = 0;
            int totalFame = 0;
            long totalChivalryGained = 0;  // ACTUAL Chivalry banked (post-DR), not the requested amount
            var champions = UsurperRemake.Data.HonorTournamentData.Champions;

            for (int waveIdx = 0; waveIdx < champions.Length; waveIdx++)
            {
                var championData = champions[waveIdx];

                terminal.ClearScreen();
                WriteSectionHeader(Loc.Get("tournament.wave_header", waveIdx + 1, champions.Length), "bright_yellow");
                terminal.WriteLine("");

                // Champion entrance theater (loc-keyed; English fields are the fallback source).
                terminal.SetColor("yellow");
                foreach (var line in championData.LocEntrance())
                {
                    terminal.WriteLine($"  {string.Format(line, currentPlayer.Name2 ?? "you")}");
                }
                terminal.SetColor("dark_magenta");
                terminal.WriteLine("");
                terminal.WriteLine($"  {championData.LocLore()}");
                terminal.WriteLine("");
                terminal.SetColor("dark_gray");
                terminal.WriteLine($"  {championData.LocCrowd()}");
                terminal.WriteLine("");
                await Task.Delay(1500);

                // Spawn the champion as a boss-tagged Monster with the role-specific stat multipliers
                // and the themed ability kit. Matches the Gauntlet's SpawnChampionMonster pattern at
                // AnchorRoadLocation.cs:1337 but local to this method so SanctumLocation isn't coupled.
                Monster monster = SpawnHonorChampion(championData, currentPlayer.Level, rng);

                terminal.SetColor("white");
                terminal.Write(Loc.Get("anchor_road.your_hp"));
                terminal.SetColor(currentPlayer.HP > currentPlayer.MaxHP / 2 ? "bright_green" : "red");
                terminal.WriteLine($"{currentPlayer.HP}/{currentPlayer.MaxHP}");
                terminal.WriteLine("");
                await Task.Delay(800);

                // Mirror the Gauntlet's death-roll-per-wave model: 25% chance a wave loss is a REAL
                // death (resurrection consumed, permadeath possible online); 75% it's a drag-out
                // (exhibition flag, HP=1, no resurrection consumed). The entry fee + daily slot are
                // the floor cost. See AnchorRoadLocation.cs:999 for the source pattern.
                bool deathRollThisWave = rng.Next(100) < GameConfig.GauntletDeathChancePercent;
                currentPlayer.IsExhibitionCombat = !deathRollThisWave;
                CombatResult result;
                var combatEngine = new CombatEngine(terminal);
                try
                {
                    result = await combatEngine.PlayerVsMonster(currentPlayer, monster, null, false);
                }
                finally
                {
                    currentPlayer.IsExhibitionCombat = false;
                }

                if (result.Outcome != CombatOutcome.Victory)
                {
                    // Loss: bail out of the ritual. Real-death consequences (if any) already handled
                    // by the combat engine's HandlePlayerDeath. Drag-out path leaves the player at
                    // HP=1 with the entry fee + daily slot consumed -- the gamble that defines the
                    // tournament's stakes. Branch flee vs defeat for narrative accuracy
                    // (combat-reviewer LOW finding: same message read wrong for fleeing players).
                    terminal.WriteLine("");
                    terminal.SetColor("red");
                    if (result.Outcome == CombatOutcome.PlayerEscaped)
                    {
                        terminal.WriteLine($"  {Loc.Get("tournament.flee", championData.LocName())}");
                    }
                    else
                    {
                        terminal.WriteLine($"  {Loc.Get("tournament.defeat", championData.LocName())}");
                    }
                    terminal.WriteLine("");
                    if (totalFame > 0)
                    {
                        // Partial-progress rewards still earned for the waves cleared before the loss.
                        terminal.SetColor("gray");
                        terminal.WriteLine($"  {Loc.Get("tournament.partial_summary", wavesCompleted, totalFame, totalGold, totalChivalryGained)}");
                    }
                    terminal.WriteLine("");
                    terminal.SetColor("darkgray");
                    terminal.WriteLine(Loc.Get("ui.press_enter"));
                    await terminal.ReadKeyAsync();
                    return;
                }

                wavesCompleted++;

                // Per-wave rewards (mirror the Gauntlet's per-wave model but with Chivalry routed
                // through ChangeAlignment so the v0.60.0 DR curve scales high-Chivalry gains down).
                long waveGold = GameConfig.GauntletGoldPerWavePerLevel * currentPlayer.Level;
                long waveXP = GameConfig.GauntletXPPerWave * (waveIdx + 4) * currentPlayer.Level; // wave-equiv +3 for honor-payout shape
                int waveFame = (waveIdx + 1) * 5; // 5 / 10 / 15 -- bigger per-wave Fame than Gauntlet warmup because each fight is a champion
                int waveChivalry = 10 + waveIdx * 5; // 10 / 15 / 20 across the three waves

                currentPlayer.Gold += waveGold;
                currentPlayer.Experience += waveXP;
                currentPlayer.Fame += waveFame;

                // Snapshot Chivalry before/after the ChangeAlignment so the summary reports the
                // ACTUAL gain after the v0.60.0 DR curve scales high-Chivalry players' rewards
                // down. Was previously reporting the requested 10/15/20 even when a Legend-tier
                // player only actually banked ~3/4/5. (combat-reviewer LOW finding.)
                long chivBefore = currentPlayer.Chivalry;
                UsurperRemake.Systems.AlignmentSystem.Instance.ChangeAlignment(currentPlayer, waveChivalry, isGood: true, "Tournament of Honor wave victory");
                long actualChivGained = currentPlayer.Chivalry - chivBefore;

                totalGold += waveGold;
                totalXP += waveXP;
                totalFame += waveFame;
                totalChivalryGained += actualChivGained;

                terminal.SetColor("bright_green");
                terminal.WriteLine($"  {Loc.Get("tournament.wave_won", waveIdx + 1, $"{waveGold:N0}", $"{waveXP:N0}")}");
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"  {Loc.Get("tournament.wave_fame_chivalry", waveFame, actualChivGained)}");

                // Themed champion drop. The champion data's `Drop.ItemName` + `Drop.LocDropFlavor`
                // describe a SPECIFIC item ("Aedric's Tarnished Blade"). The Gauntlet's
                // GenerateAndAwardChampionDrop maps the Drop.SlotHint to a real EquipmentSlot and
                // generates the right item type at top rarity. For the first-slice Tournament we
                // ship a simpler version: generate a generic class-appropriate loot item via
                // LootGenerator, then OVERRIDE the rolled name with the champion's themed item
                // name so the displayed name + flavor line + actual item all agree. Slice 7b can
                // port the Gauntlet's full slot-aware helper for richer drops.
                try
                {
                    var drop = LootGenerator.GenerateDungeonLoot(currentPlayer.Level + championData.LevelBonus, currentPlayer.Class);
                    if (drop != null)
                    {
                        // Rename to the themed item so the player sees Aedric's Tarnished Blade /
                        // Marrowking's Sigil / The Anonymous Mask rather than a generic procedural
                        // name. The underlying stats (power, slot, value) still came from the
                        // generic LootGenerator -- this is a cosmetic rename for narrative clarity.
                        if (!string.IsNullOrEmpty(championData.Drop.ItemName))
                        {
                            drop.Name = championData.Drop.ItemName;
                        }
                        if (!currentPlayer.IsInventoryFull)
                        {
                            currentPlayer.Inventory.Add(drop);
                            terminal.SetColor("cyan");
                            terminal.WriteLine($"  {Loc.Get("tournament.drop_claimed", drop.Name, championData.LocDropFlavor())}");
                        }
                        else
                        {
                            terminal.SetColor("dark_gray");
                            terminal.WriteLine($"  {Loc.Get("tournament.drop_inventory_full", drop.Name)}");
                        }
                    }
                }
                catch { /* defensive: loot-gen hiccup must not break the tournament */ }

                terminal.WriteLine("");

                // Heal between waves: match the Gauntlet's parity (AnchorRoadLocation.cs:1182-1185
                // heals 20% HP and restores 15% mana between waves). Gated on "not the last wave"
                // so we don't waste the heal after the final fight. (combat-reviewer LOW finding.)
                if (waveIdx < champions.Length - 1)
                {
                    long hpHeal = currentPlayer.MaxHP / 5;            // 20% MaxHP
                    long manaRestore = currentPlayer.MaxMana * 3 / 20; // 15% MaxMana
                    currentPlayer.HP = Math.Min(currentPlayer.MaxHP, currentPlayer.HP + hpHeal);
                    currentPlayer.Mana = Math.Min(currentPlayer.MaxMana, currentPlayer.Mana + manaRestore);
                    terminal.SetColor("dark_gray");
                    terminal.WriteLine($"  {Loc.Get("tournament.between_waves_heal", hpHeal, manaRestore)}");
                    terminal.WriteLine("");
                }

                await Task.Delay(1800);
            }

            // Full clear (all 3 waves). Award tier title via the shared ArenaChampionTier counter
            // (Hopeful at L5-19 ... GrandChampion at L80+) and tier-rewards from HonorTournamentData.
            // The tier-title text uses the existing Gauntlet flavor (Hopeful / Veteran / Master /
            // Champion / Grand Champion); slice 7b can introduce the Honor-flavored title series
            // (Honorbound / Defender of the Realm / Knight of the Tournament / Champion of Honor /
            // Paragon of the Lists) by tracking which tournament was most-recently completed.
            var earnedTier = UsurperRemake.Data.GauntletChampionData.GetTierForLevel(currentPlayer.Level);
            bool tierUpgraded = (int)earnedTier > currentPlayer.ArenaChampionTier;
            if (tierUpgraded)
            {
                currentPlayer.ArenaChampionTier = (int)earnedTier;
                // If the player has no NobleTitle (or has Sir/Dame from knighting), set the arena
                // title -- matches the Gauntlet's behavior at AnchorRoadLocation around the tier-up.
                string newTitle = UsurperRemake.Data.GauntletChampionData.GetTierTitle(earnedTier);
                if (string.IsNullOrEmpty(currentPlayer.NobleTitle) || currentPlayer.NobleTitle == "Sir" || currentPlayer.NobleTitle == "Dame")
                {
                    currentPlayer.NobleTitle = newTitle;
                }
            }

            // Unlock the corresponding tier achievement -- matches the Gauntlet's behavior at
            // AnchorRoadLocation.cs:1113/1169. The achievement IDs are shared via
            // GauntletChampionData.GetTierAchievementId (the achievement is about the player's
            // tier, not which tournament earned it). combat-reviewer LOW-MEDIUM finding: was
            // missing in the first cut, leaving Tournament-only players unable to unlock arena
            // achievements that they had legitimately earned.
            string tierAchievementId = UsurperRemake.Data.GauntletChampionData.GetTierAchievementId(earnedTier);
            if (!string.IsNullOrEmpty(tierAchievementId))
            {
                AchievementSystem.TryUnlock(currentPlayer, tierAchievementId);
            }

            var tierRewards = UsurperRemake.Data.HonorTournamentData.GetTierRewards(earnedTier);
            long bonusGold = tierRewards.GoldMultiplierPerLevel * currentPlayer.Level;
            long bonusXP = tierRewards.XpMultiplierPerLevel * currentPlayer.Level;
            int bonusFame = tierRewards.FameBonus;

            currentPlayer.Gold += bonusGold;
            currentPlayer.Experience += bonusXP;
            currentPlayer.Fame += bonusFame;

            totalGold += bonusGold;
            totalXP += bonusXP;
            totalFame += bonusFame;

            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("tournament.victory_header"), "bright_green");
            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"  {Loc.Get("tournament.full_clear_intro")}");
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine($"  {Loc.Get("tournament.summary_gold", $"{totalGold:N0}")}");
            terminal.WriteLine($"  {Loc.Get("tournament.summary_xp", $"{totalXP:N0}")}");
            terminal.WriteLine($"  {Loc.Get("tournament.summary_fame", totalFame)}");
            terminal.WriteLine($"  {Loc.Get("tournament.summary_chivalry", totalChivalryGained)}");
            if (tierUpgraded)
            {
                terminal.WriteLine("");
                terminal.SetColor("bright_magenta");
                terminal.WriteLine($"  {Loc.Get("tournament.tier_up", UsurperRemake.Data.GauntletChampionData.GetTierTitle(earnedTier))}");
            }
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("ui.press_enter"));
            await terminal.ReadKeyAsync();
        }

        /// <summary>
        /// Spawn the champion as a boss-tagged Monster with role-specific stat multipliers and
        /// the themed ability kit. Mirrors AnchorRoadLocation.SpawnChampionMonster but local to
        /// this method so SanctumLocation isn't cross-coupled to AnchorRoadLocation.
        /// </summary>
        private Monster SpawnHonorChampion(UsurperRemake.Data.HonorTournamentData.HonorChampion champion, long playerLevel, Random rng)
        {
            int effLevel = (int)Math.Max(1, Math.Min(100, playerLevel + champion.LevelBonus));
            // Generate as a non-boss base; the champion's HpMultiplier / AttackMultiplier /
            // DefenseMultiplier ARE the boss-tier scaling. Flag IsBoss = true AFTER generation so
            // phase transitions / last-stand cap / boss AI still fire WITHOUT the 2.8x HP double-
            // multiply that bit the Gauntlet at v0.61.6 (Quent the Lv.55 Barbarian getting one-shot
            // by the WEAKEST champion). See AnchorRoadLocation.cs:1340-1365 for the source rationale.
            var monster = MonsterGenerator.GenerateMonster(effLevel, isBoss: false, isMiniBoss: false, rng);
            monster.Name = champion.LocName();
            monster.MonsterColor = "bright_yellow";
            monster.HP = (long)(monster.HP * champion.HpMultiplier);
            monster.MaxHP = (long)(monster.MaxHP * champion.HpMultiplier);
            monster.Strength = (int)(monster.Strength * champion.AttackMultiplier);
            monster.Defence = (int)(monster.Defence * champion.DefenseMultiplier);
            monster.ArmPow = (int)(monster.ArmPow * champion.DefenseMultiplier);
            monster.IsBoss = true;
            // Themed ability kit overrides the random ability roll. Monster.SpecialAbilities is
            // List<string>; the MonsterAbilities pipeline parses to AbilityType at runtime.
            // Matches AnchorRoadLocation.SpawnChampionMonster's add pattern.
            if (champion.SpecialAbilities != null && champion.SpecialAbilities.Length > 0)
            {
                monster.SpecialAbilities.Clear();
                foreach (var ab in champion.SpecialAbilities)
                {
                    monster.SpecialAbilities.Add(ab);
                }
            }
            return monster;
        }
    }
}
