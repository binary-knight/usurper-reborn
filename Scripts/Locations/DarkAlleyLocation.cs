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
    /// Dark Alley – the shady district featuring black-market style services.
    /// Inspired by SHADY.PAS from the original Usurper.
    /// Shady shops: Evil characters get discounts, good characters pay more.
    /// </summary>
    public class DarkAlleyLocation : BaseLocation
    {
        public DarkAlleyLocation() : base(GameLocation.DarkAlley, "Dark Alley",
            "You stumble into a dimly-lit back street where questionable vendors ply their trade.")
        {
        }

        protected override void SetupLocation()
        {
            PossibleExits.Add(GameLocation.MainStreet);
        }

        public override async Task EnterLocation(Character player, TerminalEmulator term)
        {
            // Set base class fields so methods called before base.EnterLocation() work
            currentPlayer = player;
            terminal = term;

            // Check if Dark Alley is accessible due to world events (e.g., Martial Law)
            var (accessible, reason) = WorldEventSystem.Instance.IsLocationAccessible("Dark Alley");
            if (!accessible)
            {
                term.SetColor("bright_red");
                term.WriteLine("");
                if (player.ScreenReaderMode)
                {
                    term.WriteLine(Loc.Get("dark_alley.access_denied"));
                }
                else
                {
                    term.WriteLine("═══════════════════════════════════════");
                    term.WriteLine($"          {Loc.Get("dark_alley.access_denied")}");
                    term.WriteLine("═══════════════════════════════════════");
                }
                term.WriteLine("");
                term.SetColor("red");
                term.WriteLine(reason);
                term.WriteLine("");
                term.SetColor("yellow");
                term.WriteLine(Loc.Get("dark_alley.guards_block"));
                term.WriteLine(Loc.Get("dark_alley.return_martial_law"));
                term.WriteLine("");
                await term.PressAnyKey(Loc.Get("dark_alley.press_enter_return"));
                throw new LocationExitException(GameLocation.MainStreet);
            }

            // Check for loan enforcer encounter (overdue loan)
            if (player.LoanDaysRemaining <= 0 && player.LoanAmount > 0)
            {
                await HandleEnforcerEncounter(player, term);
            }
            // Random shady encounter (15% chance)
            else if (Random.Shared.Next(1, 101) <= 15)
            {
                await HandleShadyEncounter(player, term);
            }

            await base.EnterLocation(player, term);
        }

        /// <summary>
        /// Check if the Shadows trust the player enough for underground services.
        /// Standing must be >= -50 (not Hostile or Hated).
        /// </summary>
        private bool IsUndergroundAccessAllowed()
        {
            var standing = FactionSystem.Instance?.FactionStanding[Faction.TheShadows] ?? 0;
            return standing >= -50;
        }

        /// <summary>
        /// Show rejection message when underground services are locked due to poor Shadows standing.
        /// </summary>
        private async Task ShowUndergroundRejection()
        {
            var standing = FactionSystem.Instance?.FactionStanding[Faction.TheShadows] ?? 0;
            terminal.SetColor("dark_red");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("dark_alley.rejection_hand"));
            terminal.SetColor("red");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("dark_alley.rejection_kind"));
            terminal.WriteLine(Loc.Get("dark_alley.rejection_enemies"));
            terminal.SetColor("gray");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("dark_alley.rejection_standing", standing.ToString("N0")));
            terminal.SetColor("yellow");
            terminal.Write(Loc.Get("dark_alley.rejection_try_paying"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("W");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("dark_alley.rejection_tribute"));
            terminal.WriteLine("");
            await terminal.PressAnyKey();
        }

        protected override async Task<bool> ProcessChoice(string choice)
        {
            // Handle global quick commands first
            var (handled, shouldExit) = await TryProcessGlobalCommand(choice);
            if (handled) return shouldExit;

            switch (choice.ToUpperInvariant())
            {
                case "D":
                {
                    long goldBefore = currentPlayer.Gold;
                    await VisitDrugPalace();
                    if (currentPlayer.Gold < goldBefore) GiveSmallShadowsStandingBoost();
                    return false;
                }
                case "S":
                {
                    long goldBefore = currentPlayer.Gold;
                    await VisitSteroidShop();
                    if (currentPlayer.Gold < goldBefore) GiveSmallShadowsStandingBoost();
                    return false;
                }
                case "O":
                {
                    long goldBefore = currentPlayer.Gold;
                    await VisitOrbsHealthClub();
                    if (currentPlayer.Gold < goldBefore) GiveSmallShadowsStandingBoost();
                    return false;
                }
                case "G":
                {
                    long goldBefore = currentPlayer.Gold;
                    await VisitGroggoMagic();
                    if (currentPlayer.Gold < goldBefore) GiveSmallShadowsStandingBoost();
                    return false;
                }
                case "B":
                {
                    long goldBefore = currentPlayer.Gold;
                    await VisitBeerHut();
                    if (currentPlayer.Gold < goldBefore) GiveSmallShadowsStandingBoost();
                    return false;
                }
                case "A":
                {
                    long goldBefore = currentPlayer.Gold;
                    await VisitAlchemistHeaven();
                    if (currentPlayer.Gold < goldBefore) GiveSmallShadowsStandingBoost();
                    return false;
                }
                case "J": // The Shadows faction recruitment
                    await ShowShadowsRecruitment();
                    return false;
                case "W": // Pay tribute to improve Shadows standing
                    await PayShadowsTribute();
                    return false;
                case "M": // Black Market (Shadows only)
                    await VisitBlackMarket();
                    return false;
                case "I": // Informant (Shadows only)
                    await VisitInformant();
                    return false;
                case "P": // Pickpocket
                    if (!IsUndergroundAccessAllowed()) { await ShowUndergroundRejection(); return false; }
                    await VisitPickpocket();
                    return false;
                case "C": // Gambling Den
                    if (!IsUndergroundAccessAllowed()) { await ShowUndergroundRejection(); return false; }
                    await VisitGamblingDen();
                    return false;
                case "T": // The Pit (Arena)
                    if (!IsUndergroundAccessAllowed()) { await ShowUndergroundRejection(); return false; }
                    await VisitThePit();
                    return false;
                case "L": // Loan Shark
                    if (!IsUndergroundAccessAllowed()) { await ShowUndergroundRejection(); return false; }
                    await VisitLoanShark();
                    return false;
                case "N": // Safe House
                    if (!IsUndergroundAccessAllowed()) { await ShowUndergroundRejection(); return false; }
                    await VisitSafeHouse();
                    return false;
                case "E": // Evil Deeds (moved from Main Street)
                    await ShowEvilDeeds();
                    return false;
                case "X": // Hidden easter egg - not shown in menu
                    await ExamineTheShadows();
                    return false;
                case "Q":
                case "R":
                    await NavigateToLocation(GameLocation.MainStreet);
                    return true;
                default:
                    return await base.ProcessChoice(choice);
            }
        }

        protected override string GetMudPromptName() => "Dark Alley";

        protected override string[]? GetAmbientMessages() => new[]
        {
            Loc.Get("dark_alley.ambient_footstep"),
            Loc.Get("dark_alley.ambient_skitter"),
            Loc.Get("dark_alley.ambient_drip"),
            Loc.Get("dark_alley.ambient_voices"),
            Loc.Get("dark_alley.ambient_shadow"),
            Loc.Get("dark_alley.ambient_laughter"),
        };

        private void DisplayLocationSR()
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("dark_alley.sr_title"));
            terminal.WriteLine("");

            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dark_alley.sr_desc"));
            terminal.WriteLine("");

            // Alignment info
            var alignment = AlignmentSystem.Instance.GetAlignment(currentPlayer);
            var (alignText, _) = AlignmentSystem.Instance.GetAlignmentDisplay(currentPlayer);
            var priceModifier = AlignmentSystem.Instance.GetPriceModifier(currentPlayer, isShadyShop: true);
            if (alignment == AlignmentSystem.AlignmentType.Holy || alignment == AlignmentSystem.AlignmentType.Good)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("dark_alley.sr_prices_higher", (int)((priceModifier - 1.0f) * 100), alignText));
            }
            else if (alignment == AlignmentSystem.AlignmentType.Dark || alignment == AlignmentSystem.AlignmentType.Evil)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("dark_alley.sr_discount", (int)((1.0f - priceModifier) * 100), alignText));
            }
            terminal.WriteLine("");

            ShowNPCsInLocation();

            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("dark_alley.sr_shady"));
            WriteSRMenuOption("D", Loc.Get("dark_alley.drug_palace"));
            WriteSRMenuOption("S", Loc.Get("dark_alley.steroid_shop"));
            WriteSRMenuOption("O", Loc.Get("dark_alley.orbs_club"));
            WriteSRMenuOption("G", Loc.Get("dark_alley.magic_services"));
            WriteSRMenuOption("B", Loc.Get("dark_alley.beer_hut"));
            WriteSRMenuOption("A", Loc.Get("dark_alley.alchemist"));
            terminal.WriteLine("");

            bool undergroundLocked = !IsUndergroundAccessAllowed();
            var shadowsStanding = FactionSystem.Instance?.FactionStanding[Faction.TheShadows] ?? 0;
            terminal.SetColor("dark_red");
            terminal.Write(Loc.Get("dark_alley.sr_underground"));
            if (undergroundLocked)
            {
                terminal.SetColor("red");
                terminal.Write(Loc.Get("dark_alley.sr_locked_standing", shadowsStanding));
            }
            terminal.WriteLine(":");
            string lockNote = undergroundLocked ? Loc.Get("dark_alley.locked_suffix") : "";
            WriteSRMenuOption("P", Loc.Get("dark_alley.sr_pickpocket") + lockNote);
            WriteSRMenuOption("C", Loc.Get("dark_alley.sr_gambling_den") + lockNote);
            WriteSRMenuOption("T", Loc.Get("dark_alley.sr_the_pit") + lockNote);
            WriteSRMenuOption("L", Loc.Get("dark_alley.sr_loan_shark") + lockNote);
            WriteSRMenuOption("N", Loc.Get("dark_alley.sr_safe_house") + lockNote);
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("dark_alley.sr_other"));
            if (shadowsStanding < 0)
                WriteSRMenuOption("W", Loc.Get("dark_alley.tribute"));
            var factionSystem = FactionSystem.Instance;
            if (factionSystem.PlayerFaction != Faction.TheShadows)
                WriteSRMenuOption("J", Loc.Get("dark_alley.join_shadows"));
            else
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("dark_alley.sr_shadow_member"));
            }
            if (FactionSystem.Instance?.HasBlackMarketAccess() == true)
                WriteSRMenuOption("M", Loc.Get("dark_alley.black_market"));
            if (FactionSystem.Instance?.HasInformationNetwork() == true)
                WriteSRMenuOption("I", Loc.Get("dark_alley.informant"));
            WriteSRMenuOption("E", Loc.Get("dark_alley.evil_deeds"));
            WriteSRMenuOption("R", Loc.Get("dark_alley.return"));
            terminal.WriteLine("");

            ShowStatusLine();
        }

        protected override void DisplayLocation()
        {
            if (IsScreenReader) { DisplayLocationSR(); return; }
            if (IsBBSSession) { DisplayLocationBBS(); return; }

            terminal.ClearScreen();

            // Phase 5: Electron mode emits Dark Alley menu state. Pattern B.
            if (GameConfig.ElectronMode)
            {
                EmitElectronEvents();
                return;
            }

            // Header - standardized format
            WriteBoxHeader(Loc.Get("dark_alley.header"), "bright_cyan", 77);
            terminal.WriteLine("");

            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dark_alley.visual_desc"));
            terminal.WriteLine("");

            // Show alignment reaction in shady area
            var alignment = AlignmentSystem.Instance.GetAlignment(currentPlayer);
            var (alignText, alignColor) = AlignmentSystem.Instance.GetAlignmentDisplay(currentPlayer);
            var priceModifier = AlignmentSystem.Instance.GetPriceModifier(currentPlayer, isShadyShop: true);

            if (alignment == AlignmentSystem.AlignmentType.Holy || alignment == AlignmentSystem.AlignmentType.Good)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("dark_alley.visual_virtuous"));
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("dark_alley.visual_prices_higher", (int)((priceModifier - 1.0f) * 100), alignText));
            }
            else if (alignment == AlignmentSystem.AlignmentType.Dark || alignment == AlignmentSystem.AlignmentType.Evil)
            {
                terminal.SetColor("bright_magenta");
                terminal.WriteLine(Loc.Get("dark_alley.visual_shadow_member"));
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("dark_alley.visual_discount", (int)((1.0f - priceModifier) * 100), alignText));
            }
            terminal.WriteLine("");

            ShowNPCsInLocation();

            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("dark_alley.visual_shady"));
            terminal.WriteLine("");

            // Row 1
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("D");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("dark_alley.menu_drug_palace"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("S");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dark_alley.menu_steroid_shop"));

            // Row 2
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("O");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("dark_alley.menu_orbs_club"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("G");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dark_alley.menu_groggo"));

            // Row 3
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("B");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("dark_alley.menu_bob_beer"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("A");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dark_alley.menu_alchemist"));

            terminal.WriteLine("");

            // Underground Services section
            bool undergroundLocked = !IsUndergroundAccessAllowed();
            var shadowsStanding = FactionSystem.Instance?.FactionStanding[Faction.TheShadows] ?? 0;

            terminal.SetColor("dark_red");
            terminal.WriteLine(Loc.Get("dark_alley.visual_underground"));
            if (undergroundLocked)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("dark_alley.visual_no_trust", shadowsStanding));
            }
            terminal.WriteLine("");

            string keyColor = undergroundLocked ? "darkgray" : "bright_yellow";
            string labelColor = undergroundLocked ? "darkgray" : "white";

            // Row 1
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor(keyColor);
            terminal.Write("P");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor(labelColor);
            terminal.WriteLine(Loc.Get("dark_alley.menu_pickpocket"));

            // Row 2
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor(keyColor);
            terminal.Write("C");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor(labelColor);
            terminal.Write(Loc.Get("dark_alley.menu_gambling"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor(keyColor);
            terminal.Write("T");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor(labelColor);
            terminal.WriteLine(Loc.Get("dark_alley.menu_pit"));

            // Row 3
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor(keyColor);
            terminal.Write("L");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor(labelColor);
            terminal.Write(Loc.Get("dark_alley.menu_loan_shark"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor(keyColor);
            terminal.Write("N");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor(labelColor);
            terminal.WriteLine(Loc.Get("dark_alley.menu_safe_house"));

            terminal.WriteLine("");

            // Pay Tribute option (always visible when standing is negative)
            if (shadowsStanding < 0)
            {
                terminal.SetColor("darkgray");
                terminal.Write(" [");
                terminal.SetColor("bright_yellow");
                terminal.Write("W");
                terminal.SetColor("darkgray");
                terminal.Write("]");
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("dark_alley.menu_pay_tribute"));
            }

            // The Shadows faction option
            var factionSystem = FactionSystem.Instance;
            if (factionSystem.PlayerFaction != Faction.TheShadows)
            {
                terminal.SetColor("darkgray");
                terminal.Write(" [");
                terminal.SetColor("bright_yellow");
                terminal.Write("J");
                terminal.SetColor("darkgray");
                terminal.Write("]");
                terminal.SetColor("bright_magenta");
                terminal.Write(Loc.Get("dark_alley.menu_join_shadows"));
                if (factionSystem.PlayerFaction == null)
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("dark_alley.figure_watches"));
                }
                else
                {
                    terminal.SetColor("dark_red");
                    terminal.WriteLine(Loc.Get("dark_alley.serve_another"));
                }
            }
            else
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("dark_alley.shadow_member_tag"));
            }

            // Black Market (Shadows only)
            if (FactionSystem.Instance?.HasBlackMarketAccess() == true)
            {
                terminal.SetColor("darkgray");
                terminal.Write(" [");
                terminal.SetColor("bright_yellow");
                terminal.Write("M");
                terminal.SetColor("darkgray");
                terminal.Write("]");
                terminal.SetColor("magenta");
                terminal.WriteLine(Loc.Get("dark_alley.menu_black_market"));
            }

            // Informant (Shadows only)
            if (FactionSystem.Instance?.HasInformationNetwork() == true)
            {
                terminal.SetColor("darkgray");
                terminal.Write(" [");
                terminal.SetColor("bright_yellow");
                terminal.Write("I");
                terminal.SetColor("darkgray");
                terminal.Write("]");
                terminal.SetColor("magenta");
                terminal.WriteLine(Loc.Get("dark_alley.menu_informant"));
            }

            // Evil Deeds
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("E");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("red");
            terminal.Write(Loc.Get("dark_alley.menu_evil_deeds"));
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dark_alley.evil_deeds_desc"));

            // Navigation
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("R");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dark_alley.menu_return"));
            terminal.WriteLine("");

            ShowStatusLine();
        }

        /// <summary>
        /// Compact BBS display for 80x25 terminals.
        /// </summary>
        private void DisplayLocationBBS()
        {
            terminal.ClearScreen();
            ShowBBSHeader(Loc.Get("dark_alley.header"));

            // 1-line description
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dark_alley.bbs_desc"));

            ShowBBSNPCs();
            terminal.WriteLine("");

            // Shady establishments (2 rows)
            terminal.SetColor("dark_red");
            terminal.WriteLine(Loc.Get("dark_alley.bbs_shady"));
            ShowBBSMenuRow(("D", "bright_yellow", Loc.Get("dark_alley.bbs_drug")), ("S", "bright_yellow", Loc.Get("dark_alley.bbs_steroids")), ("O", "bright_yellow", Loc.Get("dark_alley.bbs_orbs")), ("G", "bright_yellow", Loc.Get("dark_alley.bbs_groggo")));
            ShowBBSMenuRow(("B", "bright_yellow", Loc.Get("dark_alley.bbs_beer")), ("A", "bright_yellow", Loc.Get("dark_alley.bbs_alchemist")));

            // Underground services (2 rows)
            bool locked = !IsUndergroundAccessAllowed();
            string kc = locked ? "darkgray" : "bright_yellow";
            terminal.SetColor("dark_red");
            terminal.Write(Loc.Get("dark_alley.bbs_underground"));
            if (locked)
            {
                terminal.SetColor("red");
                terminal.Write(Loc.Get("dark_alley.bbs_locked"));
            }
            terminal.WriteLine(":");
            ShowBBSMenuRow(("P", kc, Loc.Get("dark_alley.bbs_pickpocket")), ("C", kc, Loc.Get("dark_alley.bbs_gamble")), ("T", kc, Loc.Get("dark_alley.bbs_the_pit")));
            ShowBBSMenuRow(("L", kc, Loc.Get("dark_alley.bbs_loan")), ("N", kc, Loc.Get("dark_alley.bbs_safe")));

            // Faction/special options (1 row)
            var factionSystem = FactionSystem.Instance;
            var shadowsStanding = FactionSystem.Instance?.FactionStanding[Faction.TheShadows] ?? 0;
            var specialItems = new List<(string key, string color, string label)>();
            if (shadowsStanding < 0)
                specialItems.Add(("W", "bright_yellow", Loc.Get("dark_alley.bbs_tribute")));
            if (factionSystem.PlayerFaction != Faction.TheShadows)
                specialItems.Add(("J", "bright_yellow", Loc.Get("dark_alley.bbs_join")));
            if (FactionSystem.Instance?.HasBlackMarketAccess() == true)
                specialItems.Add(("M", "bright_yellow", Loc.Get("dark_alley.bbs_black_mkt")));
            if (FactionSystem.Instance?.HasInformationNetwork() == true)
                specialItems.Add(("I", "bright_yellow", Loc.Get("dark_alley.bbs_informant")));
            if (specialItems.Count > 0)
                ShowBBSMenuRow(specialItems.ToArray());

            ShowBBSMenuRow(("E", "red", Loc.Get("dark_alley.bbs_evil")), ("R", "bright_yellow", Loc.Get("dark_alley.bbs_return")));

            ShowBBSFooter();
        }

        #region Individual shop handlers

        /// <summary>
        /// Get adjusted price for shady shop purchases (alignment + world events)
        /// </summary>
        private long GetAdjustedPrice(long basePrice)
        {
            var alignmentModifier = AlignmentSystem.Instance.GetPriceModifier(currentPlayer, isShadyShop: true);
            var worldEventModifier = WorldEventSystem.Instance.GlobalPriceModifier;
            // Shadows members get 10% discount on all shady shop purchases
            float shadowsDiscount = FactionSystem.Instance?.PlayerFaction == Faction.TheShadows ? 0.90f : 1.0f;
            return Math.Max(1, (long)(basePrice * alignmentModifier * worldEventModifier * shadowsDiscount));
        }

        private async Task VisitDrugPalace()
        {
            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("dark_alley.drug_palace_header"), "bright_magenta", 66);
            terminal.WriteLine("");

            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dark_alley.drug_enter"));
            terminal.WriteLine(Loc.Get("dark_alley.drug_enter2"));
            terminal.WriteLine("");

            if (currentPlayer.OnDrugs)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("dark_alley.drug_under_influence", currentPlayer.ActiveDrug));
                terminal.WriteLine(Loc.Get("dark_alley.drug_wear_off", currentPlayer.DrugEffectDays));
                terminal.WriteLine("");
            }

            if (currentPlayer.Addict > 0)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("dark_alley.drug_addiction_level", currentPlayer.Addict));
                terminal.WriteLine("");
            }

            // Drug menu
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("dark_alley.drug_available"));
            terminal.WriteLine("");

            var drugs = new (DrugType drug, string name, string desc, long basePrice)[]
            {
                (DrugType.Steroids, Loc.Get("dark_alley.drug_name_steroids"), Loc.Get("dark_alley.drug_desc_steroids"), 500),
                (DrugType.BerserkerRage, Loc.Get("dark_alley.drug_name_berserker"), Loc.Get("dark_alley.drug_desc_berserker"), 300),
                (DrugType.Haste, Loc.Get("dark_alley.drug_name_haste"), Loc.Get("dark_alley.drug_desc_haste"), 600),
                (DrugType.QuickSilver, Loc.Get("dark_alley.drug_name_quicksilver"), Loc.Get("dark_alley.drug_desc_quicksilver"), 550),
                (DrugType.ManaBoost, Loc.Get("dark_alley.drug_name_mana_boost"), Loc.Get("dark_alley.drug_desc_mana_boost"), 700),
                (DrugType.ThirdEye, Loc.Get("dark_alley.drug_name_third_eye"), Loc.Get("dark_alley.drug_desc_third_eye"), 650),
                (DrugType.Ironhide, Loc.Get("dark_alley.drug_name_ironhide"), Loc.Get("dark_alley.drug_desc_ironhide"), 500),
                (DrugType.Stoneskin, Loc.Get("dark_alley.drug_name_stoneskin"), Loc.Get("dark_alley.drug_desc_stoneskin"), 450),
                (DrugType.DarkEssence, Loc.Get("dark_alley.drug_name_dark_essence"), Loc.Get("dark_alley.drug_desc_dark_essence"), 1500),
                (DrugType.DemonBlood, Loc.Get("dark_alley.drug_name_demon_blood"), Loc.Get("dark_alley.drug_desc_demon_blood"), 2000)
            };

            for (int i = 0; i < drugs.Length; i++)
            {
                var d = drugs[i];
                long price = GetAdjustedPrice(d.basePrice);
                if (IsScreenReader)
                {
                    WriteSRMenuOption($"{i + 1}", $"{d.name} - {d.desc}, {price:N0}g");
                }
                else
                {
                    terminal.SetColor("darkgray");
                    terminal.Write("[");
                    terminal.SetColor("bright_yellow");
                    terminal.Write($"{i + 1}");
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor(d.drug >= DrugType.DarkEssence ? "red" : "white");
                    terminal.Write($"{d.name,-18}");
                    terminal.SetColor("gray");
                    terminal.Write($" {d.desc,-35}");
                    terminal.SetColor("yellow");
                    terminal.WriteLine($" {price:N0}g");
                }
            }

            terminal.WriteLine("");
            WriteSRMenuOption("0", Loc.Get("ui.leave"));
            terminal.WriteLine("");

            var choice = await GetChoice();

            if (!int.TryParse(choice, out int selection) || selection < 1 || selection > drugs.Length)
            {
                return;
            }

            var selected = drugs[selection - 1];
            long finalPrice = GetAdjustedPrice(selected.basePrice);

            if (currentPlayer.Gold < finalPrice)
            {
                terminal.WriteLine(Loc.Get("dark_alley.drug_no_gold"), "red");
                await Task.Delay(2000);
                return;
            }

            terminal.WriteLine(Loc.Get("dark_alley.drug_buy_confirm", selected.name, finalPrice), "yellow");
            var confirm = await terminal.GetInput("> ");

            if (confirm.ToUpper() != "Y")
            {
                terminal.WriteLine(Loc.Get("dark_alley.drug_back_away"), "gray");
                await Task.Delay(1500);
                return;
            }

            currentPlayer.Gold -= finalPrice;
            var (success, message) = DrugSystem.UseDrug(currentPlayer, selected.drug);

            if (success)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine("");
                terminal.WriteLine(message);
                terminal.WriteLine("");

                // Show effects based on drug type
                var effects = GetDrugEffectsForType(selected.drug);
                terminal.SetColor("cyan");
                if (effects.StrengthBonus > 0) terminal.WriteLine(Loc.Get("dark_alley.drug_eff_str", effects.StrengthBonus));
                if (effects.DexterityBonus > 0) terminal.WriteLine(Loc.Get("dark_alley.drug_eff_dex", effects.DexterityBonus));
                if (effects.AgilityBonus > 0) terminal.WriteLine(Loc.Get("dark_alley.drug_eff_agi", effects.AgilityBonus));
                if (effects.ConstitutionBonus > 0) terminal.WriteLine(Loc.Get("dark_alley.drug_eff_con", effects.ConstitutionBonus));
                if (effects.WisdomBonus > 0) terminal.WriteLine(Loc.Get("dark_alley.drug_eff_wis", effects.WisdomBonus));
                if (effects.ManaBonus > 0) terminal.WriteLine(Loc.Get("dark_alley.drug_eff_mana", effects.ManaBonus));
                if (effects.DamageBonus > 0) terminal.WriteLine(Loc.Get("dark_alley.drug_eff_dmg", effects.DamageBonus));
                if (effects.DefenseBonus > 0) terminal.WriteLine(Loc.Get("dark_alley.drug_eff_def", effects.DefenseBonus));
                if (effects.ExtraAttacks > 0) terminal.WriteLine(Loc.Get("dark_alley.drug_eff_attacks", effects.ExtraAttacks));

                terminal.SetColor("red");
                if (effects.DefensePenalty > 0) terminal.WriteLine(Loc.Get("dark_alley.drug_pen_def", effects.DefensePenalty));
                if (effects.AgilityPenalty > 0) terminal.WriteLine(Loc.Get("dark_alley.drug_pen_agi", effects.AgilityPenalty));
                if (effects.SpeedPenalty > 0) terminal.WriteLine(Loc.Get("dark_alley.drug_pen_speed", effects.SpeedPenalty));
                if (effects.HPDrain > 0) terminal.WriteLine(Loc.Get("dark_alley.drug_pen_hp_drain", effects.HPDrain));

                AlignmentSystem.Instance.ChangeAlignment(currentPlayer, 5, isGood: false, "dark_alley.drug_pen"); // v0.57.12: paired movement
                currentPlayer.Fame = Math.Max(0, currentPlayer.Fame - 3); // Infamy

                // Increase Shadows standing for shady dealings
                FactionSystem.Instance.ModifyReputation(Faction.TheShadows, 5);
                terminal.SetColor("bright_magenta");
                terminal.WriteLine(Loc.Get("dark_alley.drug_shadow_boost"));
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine(message);
            }

            await Task.Delay(2500);
        }

        private async Task VisitSteroidShop()
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("dark_alley.steroid_enter"), "white");

            if (currentPlayer.SteroidShopPurchases >= GameConfig.MaxSteroidShopPurchases)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("dark_alley.steroid_limit"));
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("dark_alley.steroid_max_reached", GameConfig.MaxSteroidShopPurchases));
                await Task.Delay(2000);
                return;
            }

            long price = GetAdjustedPrice(1000);
            terminal.WriteLine(Loc.Get("dark_alley.steroid_cost", price, GameConfig.MoneyType), "cyan");
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dark_alley.steroid_purchases", currentPlayer.SteroidShopPurchases, GameConfig.MaxSteroidShopPurchases));
            var ans = await terminal.GetInput(Loc.Get("dark_alley.steroid_inject_prompt"));
            if (ans.ToUpper() != "Y") return;

            if (currentPlayer.Gold < price)
            {
                terminal.WriteLine(Loc.Get("dark_alley.steroid_no_gold"), "red");
                await Task.Delay(1500);
                return;
            }

            currentPlayer.Gold -= price;
            currentPlayer.Strength += 5;
            currentPlayer.Stamina += 3;
            AlignmentSystem.Instance.ChangeAlignment(currentPlayer, 3, isGood: false, "dark_alley.steroids"); // v0.57.12: paired movement
            currentPlayer.Fame = Math.Max(0, currentPlayer.Fame - 2); // Infamy
            currentPlayer.SteroidShopPurchases++;

            terminal.WriteLine(Loc.Get("dark_alley.steroid_muscles"), "bright_green");
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dark_alley.steroid_remaining", GameConfig.MaxSteroidShopPurchases - currentPlayer.SteroidShopPurchases));
            await Task.Delay(2000);
        }

        private async Task VisitOrbsHealthClub()
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("dark_alley.orbs_enter"), "white");
            long price = GetAdjustedPrice(currentPlayer.Level * 50 + 100);
            terminal.WriteLine(Loc.Get("dark_alley.orbs_cost", price, GameConfig.MoneyType), "cyan");
            var ans = await terminal.GetInput(Loc.Get("dark_alley.orbs_pay_prompt"));
            if (ans.ToUpper() != "Y") return;

            if (currentPlayer.Gold < price)
            {
                terminal.WriteLine(Loc.Get("dark_alley.orbs_no_gold"), "red");
                await Task.Delay(1500);
                return;
            }

            currentPlayer.Gold -= price;
            currentPlayer.HP = currentPlayer.MaxHP;
            terminal.WriteLine(Loc.Get("dark_alley.orbs_healed"), "bright_green");
            await Task.Delay(2000);
        }

        private async Task VisitGroggoMagic()
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("dark_alley.groggo_enter"), "white");
            terminal.WriteLine(Loc.Get("dark_alley.groggo_enter2"), "yellow");
            terminal.WriteLine("");

            terminal.WriteLine(Loc.Get("dark_alley.groggo_menu_header"), "cyan");
            terminal.WriteLine(Loc.Get("dark_alley.groggo_intel"));
            terminal.WriteLine(Loc.Get("dark_alley.groggo_fortune"));
            terminal.WriteLine(Loc.Get("dark_alley.groggo_blessing"));
            terminal.WriteLine(Loc.Get("dark_alley.groggo_nevermind"));
            terminal.WriteLine("");

            var choice = await GetChoice();

            switch (choice)
            {
                case "1":
                    long intelPrice = GetAdjustedPrice(100);
                    if (currentPlayer.Gold < intelPrice)
                    {
                        terminal.WriteLine(Loc.Get("dark_alley.groggo_no_coin"), "red");
                        break;
                    }
                    currentPlayer.Gold -= intelPrice;
                    int dungeonFloor = Math.Max(1, currentPlayer.Level / 3); // Estimate based on player level
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("dark_alley.groggo_whispers"), "bright_magenta");
                    terminal.WriteLine(Loc.Get("dark_alley.groggo_floor_hint", dungeonFloor), "white");
                    terminal.WriteLine(Loc.Get("dark_alley.groggo_monster_hint", dungeonFloor * 2 + 5), "white");
                    terminal.WriteLine(Loc.Get("dark_alley.groggo_potion_hint"), "gray");
                    break;

                case "2":
                    long fortunePrice = GetAdjustedPrice(250);
                    if (currentPlayer.Gold < fortunePrice)
                    {
                        terminal.WriteLine(Loc.Get("dark_alley.groggo_no_fortune"), "red");
                        break;
                    }
                    currentPlayer.Gold -= fortunePrice;
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("dark_alley.groggo_crystal"), "bright_magenta");
                    var fortunes = new[] {
                        Loc.Get("dark_alley.fortune_1"),
                        Loc.Get("dark_alley.fortune_2"),
                        Loc.Get("dark_alley.fortune_3"),
                        Loc.Get("dark_alley.fortune_4"),
                        Loc.Get("dark_alley.fortune_5"),
                        Loc.Get("dark_alley.fortune_6")
                    };
                    terminal.WriteLine($"  {fortunes[Random.Shared.Next(0, fortunes.Length)]}", "white");
                    break;

                case "3":
                    long blessPrice = GetAdjustedPrice(500);
                    if (currentPlayer.Gold < blessPrice)
                    {
                        terminal.WriteLine(Loc.Get("dark_alley.groggo_no_shadow"), "red");
                        break;
                    }
                    currentPlayer.Gold -= blessPrice;
                    if (currentPlayer.GroggoShadowBlessingDex > 0)
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine(Loc.Get("dark_alley.groggo_already_blessed"));
                        currentPlayer.Gold += blessPrice; // Refund
                        break;
                    }
                    currentPlayer.GroggoShadowBlessingDex = 3;
                    currentPlayer.Dexterity += 3;
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("dark_alley.groggo_traces"), "bright_magenta");
                    terminal.WriteLine(Loc.Get("dark_alley.groggo_shadows_wrap"), "white");
                    terminal.WriteLine(Loc.Get("dark_alley.groggo_dex_bonus"), "bright_green");
                    break;

                default:
                    terminal.WriteLine(Loc.Get("dark_alley.groggo_comeback"), "gray");
                    break;
            }

            await Task.Delay(2000);
        }

        private async Task VisitBeerHut()
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("dark_alley.bob_mug"), "white");
            long price = GetAdjustedPrice(10);
            terminal.WriteLine(Loc.Get("dark_alley.bob_price", price), "yellow");

            var ans = await terminal.GetInput(Loc.Get("dark_alley.bob_drink_prompt"));
            if (ans.ToUpper() != "Y") return;

            if (currentPlayer.Gold < price)
            {
                terminal.WriteLine(Loc.Get("dark_alley.bob_no_gold"), "red");
                await Task.Delay(1500);
                return;
            }
            currentPlayer.Gold -= price;

            // Small random buff (not a penalty!)
            int effect = Random.Shared.Next(1, 5);
            switch (effect)
            {
                case 1:
                    currentPlayer.HP = Math.Min(currentPlayer.MaxHP, currentPlayer.HP + 10);
                    terminal.WriteLine(Loc.Get("dark_alley.bob_warmth"), "bright_green");
                    break;
                case 2:
                    terminal.WriteLine(Loc.Get("dark_alley.bob_brave"), "bright_green");
                    break;
                case 3:
                    currentPlayer.Gold += 5; // Bob gives you some change back
                    terminal.WriteLine(Loc.Get("dark_alley.bob_coins"), "bright_green");
                    break;
                default:
                    terminal.WriteLine(Loc.Get("dark_alley.bob_burns"), "yellow");
                    break;
            }
            await Task.Delay(1500);
        }

        private async Task VisitAlchemistHeaven()
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("dark_alley.alchemist_enter"), "white");
            long price = GetAdjustedPrice(300);
            terminal.WriteLine(Loc.Get("dark_alley.alchemist_price", price, GameConfig.MoneyType), "cyan");
            var ans = await terminal.GetInput(Loc.Get("dark_alley.alchemist_buy_prompt"));
            if (ans.ToUpper() != "Y") return;

            if (currentPlayer.Gold < price)
            {
                terminal.WriteLine(Loc.Get("dark_alley.alchemist_no_gold"), "red");
                await Task.Delay(1500);
                return;
            }

            currentPlayer.Gold -= price;
            int effect = Random.Shared.Next(1, 4);
            switch (effect)
            {
                case 1:
                    if (currentPlayer.AlchemistINTBoosts >= GameConfig.MaxAlchemistINTBoosts)
                    {
                        terminal.WriteLine(Loc.Get("dark_alley.alchemist_limit"), "yellow");
                    }
                    else
                    {
                        currentPlayer.Intelligence += 2;
                        currentPlayer.AlchemistINTBoosts++;
                        terminal.WriteLine(Loc.Get("dark_alley.alchemist_int_boost", GameConfig.MaxAlchemistINTBoosts - currentPlayer.AlchemistINTBoosts), "bright_green");
                    }
                    break;
                case 2:
                    currentPlayer.HP = Math.Min(currentPlayer.MaxHP, currentPlayer.HP + 20);
                    terminal.WriteLine(Loc.Get("dark_alley.alchemist_heal"), "bright_green");
                    break;
                default:
                    AlignmentSystem.Instance.ChangeAlignment(currentPlayer, 2, isGood: false, "dark_alley.alchemist_fizzle"); // v0.57.12: paired movement
                    terminal.WriteLine(Loc.Get("dark_alley.alchemist_fizzle"), "yellow");
                    break;
            }
            await Task.Delay(2000);
        }

        /// <summary>
        /// Get drug effects for a specific drug type (for display purposes)
        /// </summary>
        private DrugEffects GetDrugEffectsForType(DrugType drug)
        {
            return drug switch
            {
                DrugType.Steroids => new DrugEffects { StrengthBonus = 20, DamageBonus = 15 },
                DrugType.BerserkerRage => new DrugEffects { StrengthBonus = 30, AttackBonus = 25, DefensePenalty = 20 },
                DrugType.Haste => new DrugEffects { AgilityBonus = 25, ExtraAttacks = 1, HPDrain = 5 },
                DrugType.QuickSilver => new DrugEffects { DexterityBonus = 20, CritBonus = 15 },
                DrugType.ManaBoost => new DrugEffects { ManaBonus = 50, SpellPowerBonus = 20 },
                DrugType.ThirdEye => new DrugEffects { WisdomBonus = 15, MagicResistBonus = 25 },
                DrugType.Ironhide => new DrugEffects { ConstitutionBonus = 25, DefenseBonus = 20, AgilityPenalty = 10 },
                DrugType.Stoneskin => new DrugEffects { ArmorBonus = 30, SpeedPenalty = 15 },
                DrugType.DarkEssence => new DrugEffects { StrengthBonus = 15, AgilityBonus = 15, DexterityBonus = 15, ManaBonus = 25 },
                DrugType.DemonBlood => new DrugEffects { DamageBonus = 25, DarknessBonus = 10 },
                _ => new DrugEffects()
            };
        }

        #endregion

        #region The Shadows Faction Recruitment

        /// <summary>
        /// Show The Shadows faction recruitment UI
        /// Meet "The Faceless One" and potentially join The Shadows
        /// </summary>
        private async Task ShowShadowsRecruitment()
        {
            var factionSystem = FactionSystem.Instance;

            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("dark_alley.shadows_header"), "bright_magenta");
            terminal.WriteLine("");

            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dark_alley.shadows_intro1"));
            terminal.WriteLine(Loc.Get("dark_alley.shadows_intro2"));
            terminal.WriteLine("");
            await Task.Delay(1500);

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dark_alley.shadows_figure1"));
            terminal.WriteLine(Loc.Get("dark_alley.shadows_figure2"));
            terminal.WriteLine("");
            await Task.Delay(1500);

            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("dark_alley.shadows_noticed"));
            terminal.WriteLine(Loc.Get("dark_alley.shadows_attention"));
            terminal.WriteLine("");
            await Task.Delay(1500);

            // Check if already in a faction
            if (factionSystem.PlayerFaction != null)
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("dark_alley.shadows_study"));
                terminal.WriteLine("");
                terminal.SetColor("bright_magenta");
                terminal.WriteLine(Loc.Get("dark_alley.shadows_carry_mark", FactionSystem.Factions[factionSystem.PlayerFaction.Value].Name));
                terminal.WriteLine(Loc.Get("dark_alley.shadows_no_share"));
                terminal.WriteLine(Loc.Get("dark_alley.shadows_find_us"));
                terminal.WriteLine("");
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("dark_alley.shadows_dissolves"));
                await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
                return;
            }

            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dark_alley.shadows_beckons"));
            terminal.WriteLine("");
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("dark_alley.shadows_crown"));
            terminal.WriteLine(Loc.Get("dark_alley.shadows_demand"));
            terminal.WriteLine("");
            await Task.Delay(1500);

            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("dark_alley.shadows_currency"));
            terminal.WriteLine(Loc.Get("dark_alley.shadows_know"));
            terminal.WriteLine(Loc.Get("dark_alley.shadows_unseen"));
            terminal.WriteLine("");
            await Task.Delay(1500);

            // Show faction benefits
            terminal.SetColor("bright_yellow");
            WriteSectionHeader(Loc.Get("dark_alley.benefits_shadows"), "bright_yellow");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dark_alley.shadows_benefit1"));
            terminal.WriteLine(Loc.Get("dark_alley.shadows_benefit2"));
            terminal.WriteLine(Loc.Get("dark_alley.shadows_benefit3"));
            terminal.WriteLine(Loc.Get("dark_alley.shadows_benefit4"));
            terminal.WriteLine("");

            // Check requirements
            var (canJoin, reason) = factionSystem.CanJoinFaction(Faction.TheShadows, currentPlayer);

            if (!canJoin)
            {
                terminal.SetColor("red");
                WriteSectionHeader(Loc.Get("dark_alley.requirements_not_met"), "red");
                terminal.SetColor("yellow");
                terminal.WriteLine(reason);
                terminal.WriteLine("");
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("dark_alley.shadows_require"));
                terminal.WriteLine(Loc.Get("dark_alley.shadows_req_level"));
                terminal.WriteLine(Loc.Get("dark_alley.shadows_req_darkness"));
                terminal.WriteLine(Loc.Get("dark_alley.shadows_your_darkness", currentPlayer.Darkness));
                terminal.WriteLine("");
                terminal.SetColor("bright_magenta");
                terminal.WriteLine(Loc.Get("dark_alley.shadows_too_light"));
                terminal.WriteLine(Loc.Get("dark_alley.shadows_embrace"));
                terminal.WriteLine(Loc.Get("dark_alley.shadows_prove"));
                await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
                return;
            }

            // Can join - offer the choice
            terminal.SetColor("bright_green");
            WriteSectionHeader(Loc.Get("dark_alley.requirements_met"), "bright_green");
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dark_alley.shadows_nods"));
            terminal.WriteLine("");
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("dark_alley.shadows_understand"));
            terminal.WriteLine(Loc.Get("dark_alley.shadows_step_in"));
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("dark_alley.shadows_warning_header"));
            terminal.WriteLine(Loc.Get("dark_alley.shadows_warning1"));
            terminal.WriteLine(Loc.Get("dark_alley.shadows_warning2"));
            terminal.WriteLine("");

            var choice = await terminal.GetInputAsync(Loc.Get("dark_alley.shadows_join_prompt"));

            if (choice.ToUpper() == "Y")
            {
                await PerformShadowsInitiation(factionSystem);
            }
            else
            {
                terminal.WriteLine("");
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("dark_alley.shadows_shrug"));
                terminal.SetColor("bright_magenta");
                terminal.WriteLine(Loc.Get("dark_alley.shadows_patient"));
                terminal.WriteLine(Loc.Get("dark_alley.shadows_watching"));
                terminal.WriteLine("");
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("dark_alley.shadows_gone"));
            }

            await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
        }

        /// <summary>
        /// Perform the initiation ceremony to join The Shadows
        /// </summary>
        private async Task PerformShadowsInitiation(FactionSystem factionSystem)
        {
            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("dark_alley.initiation_header"), "bright_magenta");
            terminal.WriteLine("");

            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dark_alley.shadows_ritual1"));
            terminal.WriteLine(Loc.Get("dark_alley.shadows_ritual2"));
            terminal.WriteLine(Loc.Get("dark_alley.shadows_ritual3"));
            terminal.WriteLine("");
            await Task.Delay(1500);

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dark_alley.shadows_ritual4"));
            terminal.WriteLine(Loc.Get("dark_alley.shadows_ritual5"));
            terminal.WriteLine(Loc.Get("dark_alley.shadows_ritual6"));
            terminal.WriteLine("");
            await Task.Delay(1500);

            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("dark_alley.shadows_no_oath"));
            terminal.WriteLine("");
            await Task.Delay(1000);

            terminal.WriteLine(Loc.Get("dark_alley.shadows_understanding"));
            terminal.WriteLine("");
            await Task.Delay(1000);

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dark_alley.shadows_coin1"));
            terminal.WriteLine(Loc.Get("dark_alley.shadows_coin2"));
            terminal.WriteLine("");
            await Task.Delay(1500);

            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("dark_alley.shadows_keep"));
            terminal.WriteLine(Loc.Get("dark_alley.shadows_know_you"));
            terminal.WriteLine("");
            await Task.Delay(1500);

            // Actually join the faction
            factionSystem.JoinFaction(Faction.TheShadows, currentPlayer);

            WriteBoxHeader(Loc.Get("dark_alley.joined_shadows"), "bright_green");
            terminal.WriteLine("");

            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("dark_alley.shadows_welcome"));
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dark_alley.shadows_receive"));
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("dark_alley.shadows_perk1"));
            terminal.WriteLine(Loc.Get("dark_alley.shadows_perk2"));
            terminal.WriteLine(Loc.Get("dark_alley.shadows_perk3"));
            terminal.WriteLine(Loc.Get("dark_alley.shadows_perk4"));
            terminal.WriteLine("");

            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dark_alley.shadows_back_alley"));
            terminal.WriteLine(Loc.Get("dark_alley.shadows_coin_pocket"));

            // Generate news (anonymously - it's the Shadows after all)
            NewsSystem.Instance.Newsy(false, Loc.Get("dark_alley.news_shadow_joins"));

            // Log to debug
            DebugLogger.Instance.LogInfo("FACTION", $"{currentPlayer.Name2} joined The Shadows");
        }

        #endregion

        #region Easter Egg

        /// <summary>
        /// Hidden easter egg discovery - triggered by pressing 'X' in the Dark Alley
        /// </summary>
        private async Task ExamineTheShadows()
        {
            terminal.ClearScreen();
            terminal.SetColor("dark_magenta");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("dark_alley.easter_squint"));
            terminal.WriteLine("");
            await Task.Delay(1500);

            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dark_alley.easter_nothing"));
            terminal.WriteLine(Loc.Get("dark_alley.easter_adjust"));
            terminal.WriteLine("");
            await Task.Delay(2000);

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dark_alley.easter_letters"));
            terminal.WriteLine(Loc.Get("dark_alley.easter_shift"));
            terminal.WriteLine("");
            await Task.Delay(1500);

            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("dark_alley.easter_wave"));
            terminal.WriteLine(Loc.Get("dark_alley.easter_shadow"));
            terminal.WriteLine(Loc.Get("dark_alley.easter_jakob"));
            terminal.WriteLine("");
            await Task.Delay(2000);

            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("dark_alley.easter_discovered"));
            terminal.WriteLine("");

            // Unlock the secret achievement
            AchievementSystem.TryUnlock(currentPlayer, "easter_egg_1");
            await AchievementSystem.ShowPendingNotifications(terminal, currentPlayer);

            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dark_alley.easter_fade"));
            terminal.WriteLine(Loc.Get("dark_alley.easter_waiting"));
            terminal.WriteLine(Loc.Get("dark_alley.easter_curious"));
            terminal.WriteLine("");

            await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
        }

        #endregion

        #region Black Market and Informant

        private async Task VisitBlackMarket()
        {
            if (FactionSystem.Instance?.HasBlackMarketAccess() != true)
            {
                terminal.SetColor("red");
                terminal.WriteLine("\n" + Loc.Get("dark_alley.bm_no_access"));
                terminal.WriteLine(Loc.Get("dark_alley.bm_join_first"));
                await Task.Delay(2000);
                return;
            }

            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("dark_alley.black_market_header"), "bright_magenta", 66);
            terminal.WriteLine("");

            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dark_alley.bm_enter"));
            terminal.WriteLine(Loc.Get("dark_alley.bm_enter2"));
            terminal.WriteLine("");

            float rankDiscount = FactionSystem.Instance?.GetBlackMarketDiscount() ?? 0f;
            int level = currentPlayer.Level;

            long forgedPapersPrice = (long)((1000 + level * 100) * (1.0f - rankDiscount));
            long poisonVialPrice = (long)((300 + level * 20) * (1.0f - rankDiscount));
            long smokeBombPrice = (long)((500 + level * 30) * (1.0f - rankDiscount));

            WriteSRMenuOption("1", Loc.Get("dark_alley.bm_forged_papers", forgedPapersPrice));
            WriteSRMenuOption("2", Loc.Get("dark_alley.bm_poison_vials", poisonVialPrice));
            WriteSRMenuOption("3", Loc.Get("dark_alley.bm_smoke_bomb", smokeBombPrice));
            WriteSRMenuOption("0", Loc.Get("ui.leave"));
            terminal.WriteLine("");

            if (rankDiscount > 0)
            {
                terminal.SetColor("bright_magenta");
                terminal.WriteLine(Loc.Get("dark_alley.bm_rank_discount", rankDiscount * 100));
                terminal.WriteLine("");
            }

            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("dark_alley.bm_gold", currentPlayer.Gold));
            terminal.WriteLine("");

            var input = await terminal.GetInput(Loc.Get("dark_alley.bm_purchase_prompt"));
            switch (input.Trim())
            {
                case "1": // Forged Papers
                    if (currentPlayer.Gold < forgedPapersPrice)
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine(Loc.Get("dark_alley.bm_no_gold"));
                    }
                    else
                    {
                        currentPlayer.Gold -= forgedPapersPrice;
                        long reduction = Math.Min(100, currentPlayer.Darkness);
                        currentPlayer.Darkness -= (int)reduction;
                        terminal.SetColor("bright_green");
                        terminal.WriteLine(Loc.Get("dark_alley.bm_forged_result", reduction));
                        currentPlayer.Statistics?.RecordGoldSpent(forgedPapersPrice);
                    }
                    break;

                case "2": // Poison Vials
                    if (currentPlayer.PoisonVials >= GameConfig.MaxPoisonVials)
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine(Loc.Get("dark_alley.bm_poison_vial_limit", currentPlayer.PoisonVials, GameConfig.MaxPoisonVials));
                    }
                    else if (currentPlayer.Gold < poisonVialPrice)
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine(Loc.Get("dark_alley.bm_no_gold"));
                    }
                    else
                    {
                        currentPlayer.Gold -= poisonVialPrice;
                        int vialsToAdd = 3;
                        currentPlayer.PoisonVials = Math.Min(GameConfig.MaxPoisonVials, currentPlayer.PoisonVials + vialsToAdd);
                        terminal.SetColor("bright_green");
                        terminal.WriteLine(Loc.Get("dark_alley.bm_poison_result", vialsToAdd, currentPlayer.PoisonVials));
                        terminal.SetColor("cyan");
                        terminal.WriteLine(Loc.Get("dark_alley.bm_poison_use"));
                        currentPlayer.Statistics?.RecordGoldSpent(poisonVialPrice);
                    }
                    break;

                case "3": // Smoke Bomb
                    if (currentPlayer.SmokeBombs >= 3)
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine(Loc.Get("dark_alley.bm_smoke_limit"));
                    }
                    else if (currentPlayer.Gold < smokeBombPrice)
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine(Loc.Get("dark_alley.bm_no_gold"));
                    }
                    else
                    {
                        currentPlayer.Gold -= smokeBombPrice;
                        currentPlayer.SmokeBombs++;
                        terminal.SetColor("bright_green");
                        terminal.WriteLine(Loc.Get("dark_alley.bm_smoke_result", currentPlayer.SmokeBombs));
                        currentPlayer.Statistics?.RecordGoldSpent(smokeBombPrice);
                    }
                    break;

                default:
                    break;
            }

            await Task.Delay(2000);
        }

        private async Task VisitInformant()
        {
            if (FactionSystem.Instance?.HasInformationNetwork() != true)
            {
                terminal.SetColor("red");
                terminal.WriteLine("\n" + Loc.Get("dark_alley.informant_no_access"));
                terminal.WriteLine(Loc.Get("dark_alley.informant_join_first"));
                await Task.Delay(2000);
                return;
            }

            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("dark_alley.informant_header"), "bright_magenta", 66);
            terminal.WriteLine("");

            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dark_alley.informant_enter"));
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("dark_alley.informant_cost", GameConfig.InformantCost));
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("dark_alley.bm_gold", currentPlayer.Gold));
            terminal.WriteLine("");

            var input = await terminal.GetInput(Loc.Get("dark_alley.informant_pay_prompt"));
            if (input.Trim().ToUpper() != "Y")
                return;

            if (currentPlayer.Gold < GameConfig.InformantCost)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("dark_alley.informant_no_gold"));
                await Task.Delay(2000);
                return;
            }

            currentPlayer.Gold -= GameConfig.InformantCost;
            currentPlayer.Statistics?.RecordGoldSpent(GameConfig.InformantCost);

            var activeNPCs = NPCSpawnSystem.Instance?.ActiveNPCs;
            if (activeNPCs == null || activeNPCs.Count == 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("dark_alley.informant_quiet"));
                await Task.Delay(2000);
                return;
            }

            // Top 5 wealthiest NPCs
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("");
            WriteSectionHeader(Loc.Get("dark_alley.wealthiest_marks"), "bright_yellow");
            var wealthiest = activeNPCs
                .Where(n => !n.IsDead && n.Gold > 0)
                .OrderByDescending(n => n.Gold)
                .Take(5)
                .ToList();

            if (wealthiest.Count == 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("dark_alley.informant_nobody_rich"));
            }
            else
            {
                foreach (var npc in wealthiest)
                {
                    terminal.SetColor("white");
                    terminal.WriteLine($"  {npc.Name2,-20} {npc.Gold,8:N0}g  {Loc.Get("dark_alley.lvl_label")} {npc.Level}");
                }
            }

            // Wanted NPCs (high Darkness)
            terminal.SetColor("bright_red");
            terminal.WriteLine("");
            WriteSectionHeader(Loc.Get("dark_alley.wanted_darkness"), "bright_red");
            var wanted = activeNPCs
                .Where(n => !n.IsDead && n.Darkness > 200)
                .OrderByDescending(n => n.Darkness)
                .Take(5)
                .ToList();

            if (wanted.Count == 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("dark_alley.informant_no_wanted"));
            }
            else
            {
                foreach (var npc in wanted)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine($"  {npc.Name2,-20} {Loc.Get("dark_alley.darkness_label")}: {npc.Darkness}  {Loc.Get("dark_alley.lvl_label")} {npc.Level}");
                }
            }

            // Active quest targets
            var activeQuests = QuestSystem.GetActiveQuestsForPlayer(currentPlayer.Name2);
            if (activeQuests?.Count > 0)
            {
                terminal.SetColor("bright_cyan");
                terminal.WriteLine("");
                WriteSectionHeader(Loc.Get("dark_alley.active_targets"), "bright_cyan");
                foreach (var quest in activeQuests.Take(5))
                {
                    terminal.SetColor("cyan");
                    terminal.WriteLine($"  {quest.Title}: {quest.GetTargetDescription()}");
                }
            }

            terminal.WriteLine("");
            await terminal.PressAnyKey();
        }

        #endregion

        #region Underground Services

        /// <summary>
        /// Gambling Den - Three street-hustle games with daily limits.
        /// Loaded Dice, Three Card Monte, Skull & Bones.
        /// </summary>
        private async Task VisitGamblingDen()
        {
            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("dark_alley.gambling_den_header"), "dark_red", 66);
            terminal.WriteLine("");

            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dark_alley.gambling_enter"));
            terminal.WriteLine(Loc.Get("dark_alley.gambling_enter2"));
            terminal.WriteLine("");

            if (currentPlayer.GamblingRoundsToday >= 10)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("dark_alley.gambling_limit"));
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("dark_alley.gambling_limit_count"));
                await Task.Delay(2000);
                return;
            }

            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("dark_alley.gambling_rounds", 10 - currentPlayer.GamblingRoundsToday));
            terminal.WriteLine(Loc.Get("dark_alley.gambling_gold", currentPlayer.Gold));
            terminal.WriteLine("");

            WriteSRMenuOption("1", Loc.Get("dark_alley.game_dice"));
            WriteSRMenuOption("2", Loc.Get("dark_alley.game_monte"));
            WriteSRMenuOption("3", Loc.Get("dark_alley.game_skull"));
            WriteSRMenuOption("0", Loc.Get("ui.leave"));
            terminal.WriteLine("");

            var choice = await terminal.GetInput(Loc.Get("dark_alley.gambling_your_game"));
            if (choice != "1" && choice != "2" && choice != "3") return;

            // Get bet amount
            long minBet = 10;
            long maxBet = Math.Max(minBet, Math.Min(currentPlayer.Gold / 10, (long)currentPlayer.Level * 500));
            if (currentPlayer.Gold < minBet)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("dark_alley.gambling_min_gold"));
                await Task.Delay(1500);
                return;
            }

            long bet = 0;
            while (true)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("dark_alley.gambling_bet_prompt", minBet, maxBet));
                var betInput = await terminal.GetInput("> ");
                if (!long.TryParse(betInput, out bet) || bet == 0)
                    return;
                if (bet < minBet)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("dark_alley.gambling_min_wager", minBet));
                    continue;
                }
                if (bet > maxBet)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("dark_alley.gambling_max_wager", maxBet));
                    continue;
                }
                if (bet > currentPlayer.Gold)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("dark_alley.gambling_no_coin"));
                    continue;
                }
                break;
            }

            currentPlayer.Gold -= bet;
            currentPlayer.GamblingRoundsToday++;
            currentPlayer.DarkAlleyReputation = Math.Min(1000, currentPlayer.DarkAlleyReputation + 1);
            bool won = false;
            long winnings = 0;

            switch (choice)
            {
                case "1": // Loaded Dice
                    (won, winnings) = await PlayLoadedDice(bet);
                    break;
                case "2": // Three Card Monte
                    (won, winnings) = await PlayThreeCardMonte(bet);
                    break;
                case "3": // Skull & Bones
                    (won, winnings) = await PlaySkullAndBones(bet);
                    break;
            }

            if (won)
            {
                currentPlayer.Gold += winnings;
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("dark_alley.gambling_win", winnings));
                DebugLogger.Instance.LogInfo("GOLD", $"GAMBLING WIN: {currentPlayer.DisplayName} bet {bet:N0}g, won {winnings:N0}g (gold now {currentPlayer.Gold:N0})");
                currentPlayer.Statistics?.RecordGamblingWin(winnings - bet);
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("dark_alley.gambling_loss", bet));
                DebugLogger.Instance.LogInfo("GOLD", $"GAMBLING LOSS: {currentPlayer.DisplayName} lost {bet:N0}g bet (gold now {currentPlayer.Gold:N0})");
                currentPlayer.Statistics?.RecordGamblingLoss(bet);
            }

            // Check achievement
            if ((currentPlayer.Statistics?.TotalGoldFromGambling ?? 0) >= 1000)
            {
                AchievementSystem.TryUnlock(currentPlayer, "dark_alley_gambler");
                await AchievementSystem.ShowPendingNotifications(terminal, currentPlayer);
            }

            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dark_alley.gambling_rounds_left", 10 - currentPlayer.GamblingRoundsToday));
            await Task.Delay(2000);
        }

        private async Task<(bool won, long winnings)> PlayLoadedDice(long bet)
        {
            bool won = false;
            long winnings = 0;

            terminal.SetColor("white");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("dark_alley.dice_rattles"));
            terminal.WriteLine("");
            WriteSRMenuOption("1", Loc.Get("dark_alley.dice_over"));
            WriteSRMenuOption("2", Loc.Get("dark_alley.dice_under"));
            var guess = await terminal.GetInput("> ");
            bool guessOver = guess != "2"; // Default to over

            await Task.Delay(1000);
            int die1 = Random.Shared.Next(1, 7);
            int die2 = Random.Shared.Next(1, 7);
            int total = die1 + die2;

            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("dark_alley.dice_result", die1, die2, total));
            await Task.Delay(500);

            // ~45% base win + CHA/200 bonus
            float chaBonus = currentPlayer.Charisma / 200f;
            float baseChance = 0.45f + chaBonus;

            // Determine actual outcome based on chance (not the dice -- the dice are loaded!)
            float roll = (float)Random.Shared.NextDouble();
            if (roll < baseChance)
            {
                // Player wins - adjust displayed result to match their guess
                if ((guessOver && total <= 7) || (!guessOver && total >= 7))
                {
                    total = guessOver ? Random.Shared.Next(8, 13) : Random.Shared.Next(2, 7);
                    terminal.SetColor("bright_cyan");
                    terminal.WriteLine(Loc.Get("dark_alley.dice_actually", total));
                }
                won = true;
                winnings = (long)(bet * 1.8);
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("dark_alley.dice_favor"));
            }
            else
            {
                if ((guessOver && total > 7) || (!guessOver && total < 7))
                {
                    total = guessOver ? Random.Shared.Next(2, 8) : Random.Shared.Next(7, 13);
                    terminal.SetColor("bright_cyan");
                    terminal.WriteLine(Loc.Get("dark_alley.dice_actually", total));
                }
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("dark_alley.dice_betray"));
            }

            return (won, winnings);
        }

        private async Task<(bool won, long winnings)> PlayThreeCardMonte(long bet)
        {
            bool won = false;
            long winnings = 0;

            terminal.SetColor("white");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("dark_alley.cards_intro"));
            terminal.WriteLine(Loc.Get("dark_alley.cards_shuffle"));
            terminal.WriteLine("");
            await Task.Delay(1000);

            WriteSRMenuOption("1", Loc.Get("dark_alley.card_left"));
            WriteSRMenuOption("2", Loc.Get("dark_alley.card_middle"));
            WriteSRMenuOption("3", Loc.Get("dark_alley.card_right"));
            var pick = await terminal.GetInput("> ");

            await Task.Delay(1500);
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dark_alley.cards_flip"));
            await Task.Delay(500);

            // 33% base + DEX/500 bonus
            float dexBonus = currentPlayer.Dexterity / 500f;
            float chance = 0.33f + dexBonus;
            float roll = (float)Random.Shared.NextDouble();

            int queenPosition = Random.Shared.Next(1, 4);
            if (roll < chance)
            {
                queenPosition = int.TryParse(pick, out int p) && p >= 1 && p <= 3 ? p : 1;
                won = true;
                winnings = (long)(bet * 2.5);
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("dark_alley.cards_queen_win", queenPosition));
            }
            else
            {
                // Ensure queen is NOT where player picked
                if (int.TryParse(pick, out int pp) && pp >= 1 && pp <= 3)
                    queenPosition = pp == 1 ? 2 : pp == 2 ? 3 : 1;
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("dark_alley.cards_queen_lose", queenPosition));
            }

            return (won, winnings);
        }

        private async Task<(bool won, long winnings)> PlaySkullAndBones(long bet)
        {
            bool won = false;
            long winnings = 0;

            terminal.SetColor("white");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("dark_alley.skull_intro"));
            terminal.WriteLine("");
            WriteSRMenuOption("1", Loc.Get("dark_alley.skull_safe"));
            WriteSRMenuOption("2", Loc.Get("dark_alley.skull_risky"));
            WriteSRMenuOption("3", Loc.Get("dark_alley.skull_all"));
            var riskChoice = await terminal.GetInput("> ");

            // WIS-based insight bonus (unique mechanic for Skull & Bones)
            float wisBonus = currentPlayer.Wisdom / 300f;

            float winChance;
            float multiplier;
            switch (riskChoice)
            {
                case "2":
                    winChance = 0.30f + wisBonus;
                    multiplier = 3.0f;
                    break;
                case "3":
                    winChance = 0.15f + wisBonus;
                    multiplier = 5.0f;
                    break;
                default:
                    winChance = 0.45f + wisBonus;
                    multiplier = 2.0f;
                    break;
            }

            await Task.Delay(1500);
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dark_alley.skull_jaw"));
            await Task.Delay(1000);

            float roll = (float)Random.Shared.NextDouble();
            if (roll < winChance)
            {
                won = true;
                winnings = (long)(bet * multiplier);
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("dark_alley.skull_golden"));
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("dark_alley.skull_empty"));
            }

            return (won, winnings);
        }

        /// <summary>
        /// Pickpocket - Uses Thiefs daily counter. DEX-based success chance.
        /// Critical fail: prison. Failure: NPC combat. Success: steal gold.
        /// </summary>
        private async Task VisitPickpocket()
        {
            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("dark_alley.pickpocketing_header"), "dark_red", 66);
            terminal.WriteLine("");

            if (currentPlayer.Thiefs <= 0)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("dark_alley.pick_used_up"));
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("dark_alley.pick_streets_hot"));
                await Task.Delay(2000);
                return;
            }

            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dark_alley.pick_scan"));
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("dark_alley.pick_attempts", currentPlayer.Thiefs));
            terminal.WriteLine("");

            // Show 3-5 random NPCs as targets
            var allNPCs = NPCSpawnSystem.Instance?.ActiveNPCs?
                .Where(n => !n.IsDead && n.Gold > 0)
                .OrderBy(_ => Random.Shared.Next(0, 1001))
                .Take(Random.Shared.Next(3, 6))
                .ToList();

            if (allNPCs == null || allNPCs.Count == 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("dark_alley.pick_empty"));
                await Task.Delay(2000);
                return;
            }

            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("dark_alley.pick_marks"));
            terminal.WriteLine("");
            for (int i = 0; i < allNPCs.Count; i++)
            {
                var npc = allNPCs[i];
                string goldHint = npc.Gold > 1000 ? Loc.Get("dark_alley.pick_heavy_purse") : npc.Gold > 200 ? Loc.Get("dark_alley.pick_decent_coin") : Loc.Get("dark_alley.pick_light_pockets");
                WriteSRMenuOption($"{i + 1}", $"{npc.Name2}, {Loc.Get("dark_alley.lvl_label")} {npc.Level}, {goldHint}");
            }
            terminal.WriteLine("");
            WriteSRMenuOption("0", Loc.Get("dark_alley.changed_mind"));
            terminal.WriteLine("");

            var choice = await terminal.GetInput(Loc.Get("dark_alley.pick_target_prompt"));
            if (!int.TryParse(choice, out int sel) || sel < 1 || sel > allNPCs.Count)
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("dark_alley.pick_slip_away"));
                await Task.Delay(1000);
                return;
            }

            var target = allNPCs[sel - 1];
            currentPlayer.Thiefs--;

            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dark_alley.pick_approach", target.Name2));
            await Task.Delay(1500);

            // DEX check
            float chance = Math.Min(0.75f, 0.40f + currentPlayer.Dexterity * 0.005f +
                (currentPlayer.Class == CharacterClass.Assassin ? 0.15f : 0f));

            float roll = (float)Random.Shared.NextDouble();

            if (roll < 0.10f)
            {
                // Critical fail — guards catch you
                terminal.SetColor("bright_red");
                terminal.WriteLine(Loc.Get("dark_alley.pick_caught"));
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("dark_alley.pick_guards"));
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("dark_alley.pick_prison"));
                terminal.WriteLine("");
                currentPlayer.DaysInPrison = 1;
                currentPlayer.Statistics?.RecordPickpocketAttempt(false);
                await Task.Delay(2500);
                throw new LocationExitException(GameLocation.Prison);
            }
            else if (roll >= (1.0f - chance))
            {
                // Success — steal gold
                float stealPercent = Random.Shared.Next(5, 16) / 100f;
                long stolen = Math.Max(1, (long)(target.Gold * stealPercent));
                target.Gold -= stolen;
                currentPlayer.Gold += stolen;
                AlignmentSystem.Instance.ChangeAlignment(currentPlayer, 3, isGood: false, "dark_alley.pickpocket"); // v0.57.12: paired movement
                currentPlayer.DarkAlleyReputation = Math.Min(1000, currentPlayer.DarkAlleyReputation + 2);

                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("dark_alley.pick_success"));
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("dark_alley.pick_stolen", stolen, target.Name2));
                terminal.SetColor("magenta");
                terminal.WriteLine(Loc.Get("dark_alley.pick_dark_rep"));

                currentPlayer.Statistics?.RecordPickpocketAttempt(true, stolen);

                // Check achievement
                if ((currentPlayer.Statistics?.TotalPickpocketSuccesses ?? 0) >= 20)
                {
                    AchievementSystem.TryUnlock(currentPlayer, "dark_alley_pickpocket");
                    await AchievementSystem.ShowPendingNotifications(terminal, currentPlayer);
                }
            }
            else
            {
                // Failure — NPC attacks
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("dark_alley.pick_catches", target.Name2));
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("dark_alley.pick_rob_me"));
                terminal.WriteLine("");
                await Task.Delay(1500);

                currentPlayer.Statistics?.RecordPickpocketAttempt(false);

                // Combat with NPC
                var combatEngine = new CombatEngine(terminal);
                var result = await combatEngine.PlayerVsPlayer(currentPlayer, target);

                if (!currentPlayer.IsAlive)
                {
                    terminal.SetColor("bright_red");
                    terminal.WriteLine(Loc.Get("dark_alley.pick_gutter", target.Name2));
                    await Task.Delay(2000);
                }
            }

            await Task.Delay(2000);
        }

        /// <summary>
        /// The Pit - Underground arena for bare-knuckle fights.
        /// 3 fights/day. Monster or NPC fights with spectator betting.
        /// </summary>
        private async Task VisitThePit()
        {
            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("dark_alley.pit_header"), "dark_red", 66);
            terminal.WriteLine("");

            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dark_alley.pit_enter"));
            terminal.WriteLine(Loc.Get("dark_alley.pit_enter2"));
            terminal.WriteLine("");

            if (currentPlayer.PitFightsToday >= 3)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("dark_alley.pit_limit"));
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("dark_alley.pit_limit_count"));
                await Task.Delay(2000);
                return;
            }

            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("dark_alley.pit_fights_remaining", 3 - currentPlayer.PitFightsToday));
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dark_alley.pit_bare"));
            terminal.WriteLine("");

            WriteSRMenuOption("1", Loc.Get("dark_alley.pit_monster"));
            WriteSRMenuOption("2", Loc.Get("dark_alley.pit_npc"));
            WriteSRMenuOption("0", Loc.Get("dark_alley.leave_pit"));
            terminal.WriteLine("");

            var choice = await GetChoice();

            if (choice == "1")
            {
                await PitFightMonster();
            }
            else if (choice == "2")
            {
                await PitFightNPC();
            }
        }

        private async Task PitFightMonster()
        {
            // Spectator bet
            var (spectatorBet, betMultiplier) = await OfferSpectatorBet();

            // Generate monster at player level
            var monster = MonsterGenerator.GenerateMonster(currentPlayer.Level);
            monster.Name = "Pit " + monster.Name;
            monster.Gold *= 2; // 2x gold reward

            terminal.SetColor("bright_red");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("dark_alley.pit_released", monster.Name));
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dark_alley.pit_monster_stats", monster.Level, monster.HP));
            terminal.WriteLine("");
            await Task.Delay(1500);

            // Save armor, zero it for bare-knuckle fight. v0.60.8: mark the
            // fight as exhibition so a loss doesn't burn a resurrection on
            // top of the entry-fee + daily-pit-slot cost. HandlePlayerDeath
            // restores HP=1 and skips the death cinematic.
            long savedArmPow = currentPlayer.ArmPow;
            try
            {
                currentPlayer.ArmPow = 0;
                currentPlayer.IsExhibitionCombat = true;

                var combatEngine = new CombatEngine(terminal);
                var result = await combatEngine.PlayerVsMonster(currentPlayer, monster, null, false);

                currentPlayer.PitFightsToday++;
                currentPlayer.DarkAlleyReputation = Math.Min(1000, currentPlayer.DarkAlleyReputation + 5);
                AlignmentSystem.Instance.ChangeAlignment(currentPlayer, 2, isGood: false, "dark_alley.pit_fight"); // v0.57.12: paired movement

                if (result.Outcome == CombatOutcome.Victory)
                {
                    terminal.SetColor("bright_green");
                    terminal.WriteLine(Loc.Get("dark_alley.pit_victory"));

                    // Handle spectator bet win
                    if (spectatorBet > 0)
                    {
                        long betWinnings = (long)(spectatorBet * betMultiplier);
                        currentPlayer.Gold += betWinnings;
                        terminal.SetColor("yellow");
                        terminal.WriteLine(Loc.Get("dark_alley.pit_bet_payout", betWinnings));
                    }

                    LogPitFightBetResult(spectatorBet, betMultiplier, true);
                    currentPlayer.Statistics?.RecordPitFight(true, result.GoldGained);

                    if ((currentPlayer.Statistics?.TotalPitFightsWon ?? 0) >= 10)
                    {
                        AchievementSystem.TryUnlock(currentPlayer, "dark_alley_pit_champion");
                        await AchievementSystem.ShowPendingNotifications(terminal, currentPlayer);
                    }
                }
                else
                {
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("dark_alley.pit_defeat"));
                    terminal.SetColor("dark_gray");
                    terminal.WriteLine(Loc.Get("dark_alley.pit_recovered"));

                    if (spectatorBet > 0)
                    {
                        terminal.SetColor("dark_red");
                        terminal.WriteLine(Loc.Get("dark_alley.pit_bet_lost", spectatorBet));
                    }

                    LogPitFightBetResult(spectatorBet, betMultiplier, false);
                    currentPlayer.Statistics?.RecordPitFight(false);
                }
            }
            finally
            {
                currentPlayer.ArmPow = savedArmPow;
                currentPlayer.IsExhibitionCombat = false;
            }

            await Task.Delay(2000);
        }

        private async Task PitFightNPC()
        {
            // Show 3-5 NPCs within +-5 levels
            int minLevel = Math.Max(1, currentPlayer.Level - 5);
            int maxLevel = currentPlayer.Level + 5;
            var candidates = NPCSpawnSystem.Instance?.ActiveNPCs?
                .Where(n => !n.IsDead && n.Level >= minLevel && n.Level <= maxLevel)
                .OrderBy(_ => Random.Shared.Next(0, 1001))
                .Take(Random.Shared.Next(3, 6))
                .ToList();

            if (candidates == null || candidates.Count == 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("dark_alley.pit_no_fighters"));
                await Task.Delay(2000);
                return;
            }

            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("dark_alley.pit_fighters"));
            terminal.WriteLine("");
            for (int i = 0; i < candidates.Count; i++)
            {
                var npc = candidates[i];
                WriteSRMenuOption($"{i + 1}", $"{npc.Name2}, {Loc.Get("dark_alley.lvl_label")} {npc.Level}, {Loc.Get("ui.gold")}: {npc.Gold:N0}");
            }
            terminal.WriteLine("");
            WriteSRMenuOption("0", Loc.Get("dark_alley.back_out"));
            terminal.WriteLine("");

            var pick = await terminal.GetInput(Loc.Get("dark_alley.pit_challenge_prompt"));
            if (!int.TryParse(pick, out int sel) || sel < 1 || sel > candidates.Count)
                return;

            var opponent = candidates[sel - 1];

            // Spectator bet
            var (spectatorBet, betMultiplier) = await OfferSpectatorBet();

            terminal.SetColor("bright_red");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("dark_alley.pit_square_off", opponent.Name2));
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dark_alley.pit_no_mercy"));
            terminal.WriteLine("");
            await Task.Delay(1500);

            // Save armor, zero it
            long savedArmPow = currentPlayer.ArmPow;
            long savedOpponentArmPow = opponent.ArmPow;
            try
            {
                currentPlayer.ArmPow = 0;
                opponent.ArmPow = 0;

                var combatEngine = new CombatEngine(terminal);
                var result = await combatEngine.PlayerVsPlayer(currentPlayer, opponent);

                currentPlayer.PitFightsToday++;
                currentPlayer.DarkAlleyReputation = Math.Min(1000, currentPlayer.DarkAlleyReputation + 8); // NPC fights give more rep
                AlignmentSystem.Instance.ChangeAlignment(currentPlayer, 2, isGood: false, "dark_alley.pit_fight_npc"); // v0.57.12: paired movement

                if (result.Outcome == CombatOutcome.Victory)
                {
                    long goldTaken = (long)(opponent.Gold * 0.20);
                    opponent.Gold -= goldTaken;
                    currentPlayer.Gold += goldTaken;

                    terminal.SetColor("bright_green");
                    terminal.WriteLine(Loc.Get("dark_alley.pit_crowd_wild", goldTaken, opponent.Name2));

                    if (spectatorBet > 0)
                    {
                        long betWinnings = (long)(spectatorBet * betMultiplier);
                        currentPlayer.Gold += betWinnings;
                        terminal.SetColor("yellow");
                        terminal.WriteLine(Loc.Get("dark_alley.pit_bet_payout", betWinnings));
                    }

                    DebugLogger.Instance.LogInfo("GOLD", $"PIT NPC WIN: {currentPlayer.DisplayName} took {goldTaken:N0}g from {opponent.Name2} (gold now {currentPlayer.Gold:N0})");
                    LogPitFightBetResult(spectatorBet, betMultiplier, true);
                    currentPlayer.Statistics?.RecordPitFight(true, goldTaken);

                    if ((currentPlayer.Statistics?.TotalPitFightsWon ?? 0) >= 10)
                    {
                        AchievementSystem.TryUnlock(currentPlayer, "dark_alley_pit_champion");
                        await AchievementSystem.ShowPendingNotifications(terminal, currentPlayer);
                    }
                }
                else
                {
                    // v0.60.8: PvP path doesn't go through HandlePlayerDeath,
                    // so the exhibition flag wouldn't fire here. Restore HP=1
                    // inline so the player walks out of the alley alive instead
                    // of dying on their next encounter from leftover HP=0.
                    if (currentPlayer.HP <= 0) currentPlayer.HP = 1;

                    long goldLost = (long)(currentPlayer.Gold * 0.20);
                    currentPlayer.Gold -= goldLost;
                    opponent.Gold += goldLost;

                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("dark_alley.pit_stumble_out", goldLost));
                    terminal.SetColor("dark_gray");
                    terminal.WriteLine(Loc.Get("dark_alley.pit_recovered"));

                    if (spectatorBet > 0)
                    {
                        terminal.SetColor("dark_red");
                        terminal.WriteLine(Loc.Get("dark_alley.pit_bet_lost", spectatorBet));
                    }

                    DebugLogger.Instance.LogInfo("GOLD", $"PIT NPC LOSS: {currentPlayer.DisplayName} lost {goldLost:N0}g to {opponent.Name2} (gold now {currentPlayer.Gold:N0})");
                    LogPitFightBetResult(spectatorBet, betMultiplier, false);
                    currentPlayer.Statistics?.RecordPitFight(false);
                }
            }
            finally
            {
                currentPlayer.ArmPow = savedArmPow;
                opponent.ArmPow = savedOpponentArmPow;
            }

            await Task.Delay(2000);
        }

        private async Task<(long betAmount, float multiplier)> OfferSpectatorBet()
        {
            long betAmount = 0;
            float multiplier = 1.0f;

            if (currentPlayer.Gold <= 0) return (0, 1.0f);

            // Cap max bet based on level to prevent gold farming exploit
            // At level 29: max 5,800. At level 100: max 20,000.
            long maxBet = Math.Min(currentPlayer.Gold, (long)currentPlayer.Level * 200);

            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("dark_alley.pit_side_bet"));
            WriteSRMenuOption("1", Loc.Get("dark_alley.bet_safe"));
            WriteSRMenuOption("2", Loc.Get("dark_alley.bet_risky"));
            WriteSRMenuOption("3", Loc.Get("dark_alley.bet_reckless"));
            WriteSRMenuOption("0", Loc.Get("dark_alley.no_bet"));
            var betChoice = await terminal.GetInput("> ");

            if (betChoice != "1" && betChoice != "2" && betChoice != "3") return (0, 1.0f);

            multiplier = betChoice == "1" ? 1.5f : betChoice == "2" ? 2.0f : 2.5f;

            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("dark_alley.pit_wager_prompt", maxBet));
            var amountStr = await terminal.GetInput("> ");
            if (long.TryParse(amountStr, out long amt) && amt > 0 && amt <= maxBet)
            {
                betAmount = amt;
                currentPlayer.Gold -= amt;
                terminal.SetColor("magenta");
                terminal.WriteLine(Loc.Get("dark_alley.pit_bet_placed", amt, multiplier));
            }

            return (betAmount, multiplier);
        }

        private void LogPitFightBetResult(long betAmount, float multiplier, bool won)
        {
            if (betAmount <= 0) return;
            if (won)
            {
                long winnings = (long)(betAmount * multiplier);
                DebugLogger.Instance.LogInfo("GOLD", $"PIT BET WIN: {currentPlayer.DisplayName} bet {betAmount:N0}g at {multiplier}x, won {winnings:N0}g (gold now {currentPlayer.Gold:N0})");
            }
            else
            {
                DebugLogger.Instance.LogInfo("GOLD", $"PIT BET LOSS: {currentPlayer.DisplayName} lost {betAmount:N0}g bet (gold now {currentPlayer.Gold:N0})");
            }
        }

        /// <summary>
        /// Loan Shark - Borrow gold with a 5-day repayment window.
        /// Overdue loans trigger enforcer encounters.
        /// </summary>
        private async Task VisitLoanShark()
        {
            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("dark_alley.loan_shark_header"), "dark_red", 66);
            terminal.WriteLine("");

            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dark_alley.loan_enter"));
            terminal.WriteLine(Loc.Get("dark_alley.loan_enter2"));
            terminal.WriteLine("");

            if (currentPlayer.LoanAmount > 0)
            {
                // Active loan — show balance and repayment options
                long totalOwed = currentPlayer.LoanAmount + currentPlayer.LoanInterestAccrued;
                terminal.SetColor("yellow");
                WriteSectionHeader(Loc.Get("dark_alley.outstanding_loan"), "yellow");
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("dark_alley.loan_principal", currentPlayer.LoanAmount));
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("dark_alley.loan_interest_amt", currentPlayer.LoanInterestAccrued));
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("dark_alley.loan_total", totalOwed));
                terminal.SetColor(currentPlayer.LoanDaysRemaining > 0 ? "yellow" : "bright_red");
                terminal.WriteLine(Loc.Get("dark_alley.loan_days", currentPlayer.LoanDaysRemaining));
                if (currentPlayer.LoanDaysRemaining <= 0)
                {
                    terminal.SetColor("bright_red");
                    terminal.WriteLine(Loc.Get("dark_alley.loan_overdue"));
                }
                terminal.WriteLine("");

                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("dark_alley.loan_gold_hand", currentPlayer.Gold));
                terminal.WriteLine("");

                WriteSRMenuOption("1", Loc.Get("dark_alley.loan_repay_full", totalOwed));
                WriteSRMenuOption("2", Loc.Get("dark_alley.partial_payment"));
                WriteSRMenuOption("0", Loc.Get("ui.leave"));
                terminal.WriteLine("");

                var choice = await GetChoice();

                if (choice == "1")
                {
                    if (currentPlayer.Gold >= totalOwed)
                    {
                        currentPlayer.Gold -= totalOwed;
                        currentPlayer.LoanAmount = 0;
                        currentPlayer.LoanDaysRemaining = 0;
                        currentPlayer.LoanInterestAccrued = 0;
                        currentPlayer.DarkAlleyReputation = Math.Min(1000, currentPlayer.DarkAlleyReputation + 3);
                        AlignmentSystem.Instance.ChangeAlignment(currentPlayer, 1, isGood: true, "dark_alley.loan_paid"); // v0.57.12: paired movement (keeping your word)

                        terminal.SetColor("bright_green");
                        terminal.WriteLine(Loc.Get("dark_alley.loan_paid"));
                        terminal.WriteLine(Loc.Get("dark_alley.loan_clean"));
                        currentPlayer.Statistics?.RecordGoldSpent(totalOwed);

                        AchievementSystem.TryUnlock(currentPlayer, "dark_alley_debt_free");
                        await AchievementSystem.ShowPendingNotifications(terminal, currentPlayer);
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine(Loc.Get("dark_alley.loan_not_enough"));
                    }
                }
                else if (choice == "2")
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("dark_alley.loan_partial_how", Math.Min(currentPlayer.Gold, totalOwed)));
                    var amtStr = await terminal.GetInput("> ");
                    if (long.TryParse(amtStr, out long payment) && payment > 0 && payment <= currentPlayer.Gold)
                    {
                        if (payment > totalOwed) payment = totalOwed;
                        currentPlayer.Gold -= payment;
                        currentPlayer.Statistics?.RecordGoldSpent(payment);

                        // Apply payment to interest first, then principal
                        if (payment >= currentPlayer.LoanInterestAccrued)
                        {
                            payment -= currentPlayer.LoanInterestAccrued;
                            currentPlayer.LoanInterestAccrued = 0;
                            currentPlayer.LoanAmount -= payment;
                        }
                        else
                        {
                            currentPlayer.LoanInterestAccrued -= payment;
                        }

                        if (currentPlayer.LoanAmount <= 0)
                        {
                            currentPlayer.LoanAmount = 0;
                            currentPlayer.LoanDaysRemaining = 0;
                            currentPlayer.LoanInterestAccrued = 0;
                            currentPlayer.DarkAlleyReputation = Math.Min(1000, currentPlayer.DarkAlleyReputation + 3);
                            AlignmentSystem.Instance.ChangeAlignment(currentPlayer, 1, isGood: true, "dark_alley.loan_fully_paid"); // v0.57.12: paired movement
                            terminal.SetColor("bright_green");
                            terminal.WriteLine(Loc.Get("dark_alley.loan_fully_paid"));

                            AchievementSystem.TryUnlock(currentPlayer, "dark_alley_debt_free");
                            await AchievementSystem.ShowPendingNotifications(terminal, currentPlayer);
                        }
                        else
                        {
                            terminal.SetColor("yellow");
                            terminal.WriteLine(Loc.Get("dark_alley.loan_payment_accepted", currentPlayer.LoanAmount + currentPlayer.LoanInterestAccrued));
                        }
                    }
                    else
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine(Loc.Get("dark_alley.loan_changed_mind"));
                    }
                }
            }
            else
            {
                // No active loan — offer new loan
                long maxLoan = currentPlayer.Level * 500;
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("dark_alley.loan_forward"));
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("dark_alley.loan_offer", maxLoan));
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("dark_alley.loan_five_days"));
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("dark_alley.loan_deadline"));
                terminal.WriteLine("");

                WriteSRMenuOption("1", Loc.Get("dark_alley.take_loan"));
                WriteSRMenuOption("0", Loc.Get("ui.leave"));
                terminal.WriteLine("");

                var choice = await GetChoice();
                if (choice == "1")
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("dark_alley.loan_how_much", maxLoan));
                    var amtStr = await terminal.GetInput("> ");
                    if (long.TryParse(amtStr, out long amount) && amount > 0 && amount <= maxLoan)
                    {
                        currentPlayer.LoanAmount = amount;
                        currentPlayer.LoanDaysRemaining = 5;
                        currentPlayer.LoanInterestAccrued = 0;
                        currentPlayer.Gold += amount;
                        DebugLogger.Instance.LogInfo("GOLD", $"DARK ALLEY LOAN: {currentPlayer.DisplayName} took {amount:N0}g loan (gold now {currentPlayer.Gold:N0})");

                        terminal.SetColor("bright_green");
                        terminal.WriteLine(Loc.Get("dark_alley.loan_count_out", amount));
                        terminal.SetColor("red");
                        terminal.WriteLine(Loc.Get("dark_alley.loan_dont_forget"));
                        terminal.SetColor("gray");
                        terminal.WriteLine(Loc.Get("dark_alley.loan_interest"));
                    }
                    else
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine(Loc.Get("dark_alley.loan_wasting"));
                    }
                }
            }

            await Task.Delay(2000);
        }

        /// <summary>
        /// Fence Stolen Goods - Sell items from backpack at 70% value (80% for Shadows members).
        /// Accepts cursed items.
        /// </summary>
        private async Task VisitFence()
        {
            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("dark_alley.fence_header"), "dark_red", 66);
            terminal.WriteLine("");

            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dark_alley.fence_enter"));
            terminal.WriteLine(Loc.Get("dark_alley.fence_enter2"));
            terminal.WriteLine("");

            bool isShadows = FactionSystem.Instance?.PlayerFaction == Faction.TheShadows;
            float fenceRate = isShadows ? 0.80f : 0.70f;
            // Apply Shadows rank-scaled fence bonus (10-37.5% additional based on rank 0-8)
            if (isShadows && FactionSystem.Instance != null)
            {
                float factionBonus = FactionSystem.Instance.GetFencePriceModifier();
                fenceRate = Math.Min(0.95f, fenceRate * factionBonus); // Cap at 95% of item value
                terminal.SetColor("bright_magenta");
                terminal.WriteLine(Loc.Get("dark_alley.fence_shadow_bonus"));
                int displayPercent = (int)(fenceRate * 100);
                terminal.SetColor("magenta");
                terminal.WriteLine($"  Shadows rank bonus: {displayPercent}% of item value");
                terminal.WriteLine("");
            }

            // Gather items from player's Item/ItemType lists
            var itemsForSale = new List<(int index, string name, long value, bool cursed)>();
            if (currentPlayer.Item != null && currentPlayer.ItemType != null)
            {
                for (int i = 0; i < currentPlayer.Item.Count && i < currentPlayer.ItemType.Count; i++)
                {
                    int itemId = currentPlayer.Item[i];
                    if (itemId <= 0) continue;
                    var item = ItemManager.GetItem(itemId);
                    if (item == null) continue;

                    long fenceValue = Math.Max(1, (long)(item.Value * fenceRate));
                    itemsForSale.Add((i, item.Name, fenceValue, item.Cursed || item.IsCursed));
                }
            }

            if (itemsForSale.Count == 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("dark_alley.fence_nothing"));
                await Task.Delay(2000);
                return;
            }

            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("dark_alley.fence_items"));
            terminal.WriteLine("");
            for (int i = 0; i < itemsForSale.Count; i++)
            {
                var (_, name, value, cursed) = itemsForSale[i];
                string cursedTag = cursed ? Loc.Get("dark_alley.fence_cursed_tag") : "";
                WriteSRMenuOption($"{i + 1}", $"{name}, {value:N0}g{cursedTag}");
            }
            terminal.WriteLine("");
            WriteSRMenuOption("0", Loc.Get("ui.leave"));
            terminal.WriteLine("");

            var choice = await terminal.GetInput(Loc.Get("dark_alley.fence_sell_prompt"));
            if (!int.TryParse(choice, out int sel) || sel < 1 || sel > itemsForSale.Count)
                return;

            var selected = itemsForSale[sel - 1];
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("dark_alley.fence_confirm", selected.name, selected.value));
            var confirm = await terminal.GetInput("> ");
            if (confirm.ToUpper() != "Y") return;

            // Remove item from inventory
            currentPlayer.Item.RemoveAt(selected.index);
            currentPlayer.ItemType.RemoveAt(selected.index);
            currentPlayer.Gold += selected.value;
            currentPlayer.Statistics?.RecordSale(selected.value);
            AlignmentSystem.Instance.ChangeAlignment(currentPlayer, 1, isGood: false, "dark_alley.fence_goods"); // v0.57.12: paired movement
            currentPlayer.DarkAlleyReputation = Math.Min(1000, currentPlayer.DarkAlleyReputation + 1);

            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("dark_alley.fence_sold", selected.name, selected.value));
            if (selected.cursed)
            {
                terminal.SetColor("magenta");
                terminal.WriteLine(Loc.Get("dark_alley.fence_cursed"));
            }

            await Task.Delay(2000);
        }

        /// <summary>
        /// Safe House - Rest and heal in the underground. Requires Darkness >= 50.
        /// Small robbery risk. Shadows members exempt from robbery.
        /// </summary>
        private async Task VisitSafeHouse()
        {
            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("dark_alley.safe_house_header"), "dark_red", 66);
            terminal.WriteLine("");

            if (currentPlayer.Darkness < 50)
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("dark_alley.safe_locked"));
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("dark_alley.safe_locked2"));
                terminal.WriteLine(Loc.Get("dark_alley.safe_locked3"));
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("dark_alley.safe_darkness_req", currentPlayer.Darkness));
                await Task.Delay(2000);
                return;
            }

            long cost = 50;
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dark_alley.safe_enter"));
            terminal.WriteLine(Loc.Get("dark_alley.safe_enter2"));
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("dark_alley.safe_rest_cost", cost));
            terminal.SetColor("gray");
            terminal.WriteLine($"{Loc.Get("combat.bar_hp")}: {currentPlayer.HP}/{currentPlayer.MaxHP}");
            terminal.SetColor("yellow");
            terminal.WriteLine($"{Loc.Get("ui.gold")}: {currentPlayer.Gold:N0}");
            terminal.WriteLine("");

            var ans = await terminal.GetInput(Loc.Get("dark_alley.safe_rest_prompt"));
            if (ans.ToUpper() != "Y") return;

            if (currentPlayer.Gold < cost)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("dark_alley.safe_no_gold"));
                await Task.Delay(1500);
                return;
            }

            currentPlayer.Gold -= cost;
            currentPlayer.Statistics?.RecordGoldSpent(cost);

            // Restore 50% HP
            long healAmount = currentPlayer.MaxHP / 2;
            currentPlayer.HP = Math.Min(currentPlayer.MaxHP, currentPlayer.HP + healAmount);

            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("dark_alley.safe_healed", healAmount));

            // Restore 50% mana for casters
            if (currentPlayer.IsManaClass && currentPlayer.MaxMana > 0)
            {
                long manaRestore = currentPlayer.MaxMana / 2;
                currentPlayer.Mana = Math.Min(currentPlayer.MaxMana, currentPlayer.Mana + manaRestore);
                terminal.WriteLine($"  Mana restored: +{manaRestore}");
            }

            terminal.SetColor("gray");
            terminal.WriteLine($"{Loc.Get("combat.bar_hp")}: {currentPlayer.HP}/{currentPlayer.MaxHP}");

            currentPlayer.DarkAlleyReputation = Math.Min(1000, currentPlayer.DarkAlleyReputation + 1);

            // Robbery chance scaled by alignment (Shadows members exempt)
            // Evil players are respected (2%), neutral (8%), good players are easy marks (15%)
            bool isShadows = FactionSystem.Instance?.PlayerFaction == Faction.TheShadows;
            int robberyChance = currentPlayer.Darkness > 300 ? 2 : currentPlayer.Darkness > 100 ? 5 : currentPlayer.Chivalry > 200 ? 15 : 8;
            if (!isShadows && Random.Shared.Next(1, 101) <= robberyChance)
            {
                float lossPercent = Random.Shared.Next(5, 11) / 100f;
                long goldLost = Math.Max(1, (long)(currentPlayer.Gold * lossPercent));
                currentPlayer.Gold -= goldLost;

                terminal.SetColor("red");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("dark_alley.safe_robbed"));
                terminal.SetColor("bright_red");
                terminal.WriteLine(Loc.Get("dark_alley.safe_gold_lost", goldLost));
            }
            else if (isShadows)
            {
                terminal.SetColor("magenta");
                terminal.WriteLine(Loc.Get("dark_alley.safe_shadow_coin"));
            }

            // Shadows members who rest here are hidden from PvP attacks while offline
            if (isShadows)
            {
                currentPlayer.SafeHouseResting = true;
                terminal.SetColor("dark_magenta");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("dark_alley.safe_shadow_watch"));
            }

            await Task.Delay(2000);
        }

        /// <summary>
        /// Pay gold tribute to improve Shadows faction standing.
        /// Always available — this is the primary way to recover from negative standing.
        /// Cost scales with how hated you are. Each payment gives a fixed standing boost.
        /// </summary>
        private async Task PayShadowsTribute()
        {
            var factionSystem = FactionSystem.Instance;
            var standing = factionSystem?.FactionStanding[Faction.TheShadows] ?? 0;

            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("dark_alley.tribute_header"), "dark_magenta", 66);
            terminal.WriteLine("");

            if (standing >= 0)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("dark_alley.tribute_already"));
                await terminal.PressAnyKey();
                return;
            }

            // Cost: 100 gold base + 2 gold per point of negative standing
            // So at -2000, it costs 100 + 4000 = 4100 gold per tribute
            // Each tribute gives +50 standing
            long tributeCost = 100 + Math.Abs(standing) * 2;
            int standingGain = 50;

            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dark_alley.tribute_figure"));
            terminal.SetColor("dark_red");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("dark_alley.tribute_back"));
            terminal.WriteLine(Loc.Get("dark_alley.tribute_price"));
            terminal.WriteLine("");

            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("dark_alley.tribute_standing", standing));
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dark_alley.tribute_cost", tributeCost, standingGain));
            terminal.SetColor("gray");
            int tributesNeeded = standing < -50 ? (int)Math.Ceiling((-50.0 - standing) / standingGain) : 0;
            if (tributesNeeded > 0)
                terminal.WriteLine(Loc.Get("dark_alley.tribute_needed", tributesNeeded));
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("dark_alley.tribute_your_gold", currentPlayer.Gold));
            terminal.WriteLine("");

            var ans = await terminal.GetInput(Loc.Get("dark_alley.tribute_pay_prompt"));
            if (ans?.Trim().ToUpper() != "Y") return;

            if (currentPlayer.Gold < tributeCost)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("dark_alley.tribute_no_gold"));
                await Task.Delay(1500);
                return;
            }

            currentPlayer.Gold -= tributeCost;
            currentPlayer.Statistics?.RecordGoldSpent(tributeCost);
            factionSystem?.ModifyReputation(Faction.TheShadows, standingGain);

            var newStanding = factionSystem?.FactionStanding[Faction.TheShadows] ?? 0;

            terminal.SetColor("bright_green");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("dark_alley.tribute_accepted"));
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("dark_alley.tribute_standing_change", standing, newStanding, standingGain));

            if (newStanding >= -50 && standing < -50)
            {
                terminal.SetColor("bright_magenta");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("dark_alley.tribute_open"));
            }

            currentPlayer.DarkAlleyReputation = Math.Min(1000, currentPlayer.DarkAlleyReputation + 5);
            await Task.Delay(2000);
        }

        /// <summary>
        /// Small passive Shadows standing boost when the player spends gold at shady establishments.
        /// Helps players slowly rebuild standing through regular patronage.
        /// </summary>
        private void GiveSmallShadowsStandingBoost()
        {
            var factionSystem = FactionSystem.Instance;
            if (factionSystem == null) return;

            var standing = factionSystem.FactionStanding[Faction.TheShadows];
            // Only boost if standing is below Friendly (50) — no free rep for allies
            if (standing >= 50) return;

            factionSystem.ModifyReputation(Faction.TheShadows, 3);
            currentPlayer.DarkAlleyReputation = Math.Min(1000, currentPlayer.DarkAlleyReputation + 1);
        }

        #endregion

        #region Shady Encounters

        /// <summary>
        /// Random encounter when entering the Dark Alley (15% chance).
        /// Four types: Mugger, Beggar tip, Undercover guard, Shady merchant.
        /// </summary>
        private async Task HandleShadyEncounter(Character player, TerminalEmulator term)
        {
            int encounterType = Random.Shared.Next(1, 101);

            if (encounterType <= 30)
            {
                // Mugger (30%)
                term.SetColor("bright_red");
                term.WriteLine("");
                term.WriteLine(Loc.Get("dark_alley.enc_mugger_appear"));
                term.SetColor("red");
                term.WriteLine(Loc.Get("dark_alley.enc_mugger_demand"));
                term.WriteLine("");
                WriteSRMenuOption("1", Loc.Get("dark_alley.enc_mugger_pay"));
                term.Write("    ");
                WriteSRMenuOption("2", Loc.Get("dark_alley.enc_mugger_fight"));
                var choice = await term.GetInput("> ");

                if (choice == "1" && player.Gold >= 50)
                {
                    player.Gold -= 50;
                    term.SetColor("gray");
                    term.WriteLine(Loc.Get("dark_alley.enc_mugger_vanish"));
                    player.Statistics?.RecordGoldSpent(50);
                }
                else
                {
                    term.SetColor("bright_red");
                    term.WriteLine(Loc.Get("dark_alley.enc_mugger_wrong"));
                    term.WriteLine("");
                    await Task.Delay(1000);

                    // Create a mugger monster at player's level
                    var mugger = MonsterGenerator.GenerateMonster(player.Level);
                    mugger.Name = "Dark Alley Mugger";

                    var combatEngine = new CombatEngine(term);
                    await combatEngine.PlayerVsMonster(player, mugger, null, false);
                }
            }
            else if (encounterType <= 60)
            {
                // Beggar tip (30%)
                term.SetColor("gray");
                term.WriteLine("");
                term.WriteLine(Loc.Get("dark_alley.enc_beggar_tug"));
                term.SetColor("yellow");

                var tips = new[]
                {
                    Loc.Get("dark_alley.enc_beggar_tip1"),
                    Loc.Get("dark_alley.enc_beggar_tip2"),
                    Loc.Get("dark_alley.enc_beggar_tip3"),
                    Loc.Get("dark_alley.enc_beggar_tip4"),
                    Loc.Get("dark_alley.enc_beggar_tip5"),
                    Loc.Get("dark_alley.enc_beggar_tip6"),
                };
                term.WriteLine(tips[Random.Shared.Next(0, tips.Length)]);
                term.SetColor("gray");
                term.WriteLine(Loc.Get("dark_alley.enc_beggar_away"));
            }
            else if (encounterType <= 80)
            {
                // Undercover guard (20%)
                if (player.Darkness > 300)
                {
                    float arrestChance = 0.50f;
                    if ((float)Random.Shared.NextDouble() < arrestChance)
                    {
                        term.SetColor("bright_red");
                        term.WriteLine("");
                        term.WriteLine(Loc.Get("dark_alley.enc_guard_halt"));
                        term.SetColor("red");
                        term.WriteLine(Loc.Get("dark_alley.enc_guard_badge"));
                        term.SetColor("bright_red");
                        term.WriteLine(Loc.Get("dark_alley.enc_guard_prison"));
                        player.DaysInPrison = 1;
                        await Task.Delay(2500);
                        throw new LocationExitException(GameLocation.Prison);
                    }
                    else
                    {
                        term.SetColor("yellow");
                        term.WriteLine("");
                        term.WriteLine(Loc.Get("dark_alley.enc_guard_watching"));
                        term.SetColor("gray");
                        term.WriteLine(Loc.Get("dark_alley.enc_guard_slip"));
                    }
                }
                else
                {
                    term.SetColor("gray");
                    term.WriteLine("");
                    term.WriteLine(Loc.Get("dark_alley.enc_guard_bump"));
                    term.WriteLine(Loc.Get("dark_alley.enc_guard_official"));
                }
            }
            else
            {
                // Shady merchant (20%)
                term.SetColor("magenta");
                term.WriteLine("");
                term.WriteLine(Loc.Get("dark_alley.enc_merchant_sidle"));
                term.SetColor("yellow");

                int offer = Random.Shared.Next(1, 3);
                if (offer == 1)
                {
                    // Healing potion at half price
                    long potionPrice = GetAdjustedPrice(50);
                    term.WriteLine(Loc.Get("dark_alley.enc_merchant_potion", potionPrice));
                    WriteSRMenuOption("Y", Loc.Get("dark_alley.enc_merchant_buy"));
                    term.Write("    ");
                    WriteSRMenuOption("N", Loc.Get("dark_alley.enc_merchant_pass"));
                    term.WriteLine("");
                    var ans = await term.GetInput("> ");
                    if (ans.ToUpper() == "Y" && player.Gold >= potionPrice)
                    {
                        player.Gold -= potionPrice;
                        player.Healing = Math.Min(player.MaxPotions, player.Healing + 1);
                        term.SetColor("bright_green");
                        term.WriteLine(Loc.Get("dark_alley.enc_merchant_pocket"));
                    }
                    else if (ans.ToUpper() == "Y")
                    {
                        term.SetColor("red");
                        term.WriteLine(Loc.Get("dark_alley.enc_merchant_no_gold"));
                    }
                }
                else
                {
                    // Random stat boost (small)
                    long price = GetAdjustedPrice(100);
                    term.WriteLine(Loc.Get("dark_alley.enc_merchant_elixir", price));
                    WriteSRMenuOption("Y", Loc.Get("dark_alley.enc_merchant_buy"));
                    term.Write("    ");
                    WriteSRMenuOption("N", Loc.Get("dark_alley.enc_merchant_pass"));
                    term.WriteLine("");
                    var ans = await term.GetInput("> ");
                    if (ans.ToUpper() == "Y" && player.Gold >= price)
                    {
                        player.Gold -= price;
                        int stat = Random.Shared.Next(1, 4);
                        switch (stat)
                        {
                            case 1:
                                player.Strength += 1;
                                term.SetColor("bright_green");
                                term.WriteLine(Loc.Get("dark_alley.enc_merchant_str"));
                                break;
                            case 2:
                                player.Dexterity += 1;
                                term.SetColor("bright_green");
                                term.WriteLine(Loc.Get("dark_alley.enc_merchant_dex"));
                                break;
                            default:
                                player.Constitution += 1;
                                term.SetColor("bright_green");
                                term.WriteLine(Loc.Get("dark_alley.enc_merchant_con"));
                                break;
                        }
                    }
                    else if (ans.ToUpper() == "Y")
                    {
                        term.SetColor("red");
                        term.WriteLine(Loc.Get("dark_alley.enc_merchant_no_gold2"));
                    }
                }
            }

            term.WriteLine("");
            await Task.Delay(1500);
        }

        /// <summary>
        /// Enforcer encounter - triggered when loan is overdue (LoanDaysRemaining <= 0 && LoanAmount > 0).
        /// Win: loan forgiven. Lose: all gold taken, 25% HP damage, loan extended by 3 days.
        /// </summary>
        private async Task HandleEnforcerEncounter(Character player, TerminalEmulator term)
        {
            term.WriteLine("");
            WriteBoxHeader(Loc.Get("dark_alley.enforcer_header"), "bright_red", 66);
            term.WriteLine("");
            term.SetColor("red");
            term.WriteLine(Loc.Get("dark_alley.enforcer_appear"));
            term.SetColor("bright_red");
            term.WriteLine(Loc.Get("dark_alley.enforcer_demand"));
            term.WriteLine("");
            await Task.Delay(2000);

            // Generate enforcer at playerLevel + 5
            var enforcer = MonsterGenerator.GenerateMonster(player.Level + 5);
            enforcer.Name = "Loan Shark Enforcer";
            enforcer.Gold = 0; // No gold reward — this is punishment

            var combatEngine = new CombatEngine(term);
            var result = await combatEngine.PlayerVsMonster(player, enforcer, null, false);

            if (result.Outcome == CombatOutcome.Victory)
            {
                // Loan forgiven
                player.LoanAmount = 0;
                player.LoanDaysRemaining = 0;
                player.LoanInterestAccrued = 0;

                term.SetColor("bright_green");
                term.WriteLine("");
                term.WriteLine(Loc.Get("dark_alley.enforcer_crumple"));
                term.SetColor("yellow");
                term.WriteLine(Loc.Get("dark_alley.enforcer_settled"));
                term.SetColor("bright_green");
                term.WriteLine(Loc.Get("dark_alley.enforcer_forgiven"));
            }
            else if (player.IsAlive)
            {
                // Player lost but survived — take all gold, 25% HP, extend loan
                long goldTaken = player.Gold;
                player.Gold = 0;
                long hpDamage = player.MaxHP / 4;
                player.HP = Math.Max(1, player.HP - hpDamage);
                player.LoanDaysRemaining = 3; // Extension

                term.SetColor("bright_red");
                term.WriteLine("");
                term.WriteLine(Loc.Get("dark_alley.enforcer_beaten"));
                term.SetColor("red");
                term.WriteLine(Loc.Get("dark_alley.enforcer_lost", goldTaken, hpDamage));
                term.SetColor("yellow");
                term.WriteLine(Loc.Get("dark_alley.enforcer_extension"));
            }
            else
            {
                // Player died — loan forgiven (can't collect from a corpse), clear debt
                player.LoanAmount = 0;
                player.LoanDaysRemaining = 0;
                player.LoanInterestAccrued = 0;

                term.SetColor("bright_red");
                term.WriteLine("");
                term.WriteLine("The enforcer stands over your broken body.");
                term.SetColor("red");
                term.WriteLine("\"Consider the debt... settled.\"");
            }

            term.WriteLine("");
            await Task.Delay(2500);
        }

        #endregion

        #region Evil Deeds — Tiered Dark Path (v0.49.4)

        private enum DeedTier { Petty, Serious, Dark }

        private record EvilDeedDef(
            string Id, string Name, string Description, DeedTier Tier,
            int DarknessGain, int MinLevel, int MinDarkness,
            int GoldCost, int GoldRewardBase, int GoldRewardScale,
            int XPReward, int ShadowsFaction, int CrownFaction,
            float FailChance, int FailDamagePct, int FailGoldLoss,
            bool GeneratesNews, string? NewsText, string? SpecialEffect);

        private static readonly EvilDeedDef[] AllEvilDeeds = new[]
        {
            // ── Tier 1: Petty Crimes ──
            new EvilDeedDef("rob_beggar", "Rob a Beggar",
                "An old beggar dozes against the alley wall, a few copper coins\nscattered in his cup. He'd never even know they were gone.",
                DeedTier.Petty, 5, 0, 0, 0, 15, 35, 0, 1, 0, 0.05f, 0, 0,
                false, null, null),

            new EvilDeedDef("vandalize_shrine", "Vandalize a Shrine",
                "A small roadside shrine to the Seven sits unattended, its candles\nstill flickering. You topple it and scatter the offerings into the gutter.",
                DeedTier.Petty, 8, 0, 0, 0, 0, 0, 0, 0, -1, 0f, 0, 0,
                false, null, "chivalry_loss"),

            new EvilDeedDef("spread_rumors", "Spread Venomous Rumors",
                "The gossips near the well are always hungry for scandal. A well-placed\nlie about a merchant's debts could ruin someone — and entertain you.",
                DeedTier.Petty, 6, 0, 0, 0, 0, 0, 10, 1, 0, 0.10f, 0, 0,
                true, "{PLAYER} has been spreading dark whispers through town.", null),

            new EvilDeedDef("poison_well", "Poison the Well",
                "The public well serves dozens of families. A few drops of bitter\nnightshade, and by morning the healers will have their hands full.",
                DeedTier.Petty, 10, 0, 0, 25, 0, 0, 0, 0, -2, 0.15f, 0, 50,
                true, "The town well was poisoned! Guards are investigating.", null),

            new EvilDeedDef("extort_shopkeeper", "Extort a Shopkeeper",
                "The old cobbler on Anchor Road has been skimming the King's tax.\nYou know because you watched him do it. A quiet word, a meaningful look...",
                DeedTier.Petty, 8, 0, 0, 0, 50, 100, 0, 0, 0, 0.10f, 0, 0,
                false, null, null),

            // ── Tier 2: Serious Crimes ──
            new EvilDeedDef("desecrate_dead", "Desecrate the Dead",
                "The cemetery holds more than memories. The recently buried are sometimes\ninterred with jewelry. The gravedigger looks the other way — for a price.",
                DeedTier.Serious, 15, 5, 100, 30, 100, 200, 0, 0, 0, 0.15f, 10, 0,
                false, null, null),

            new EvilDeedDef("arson_market", "Arson in the Market",
                "The timber-framed stalls of the lower market are tinder-dry. One spark\nand the chaos will keep the guards busy for hours — perfect cover.",
                DeedTier.Serious, 20, 5, 100, 0, 0, 0, 25, 5, -5, 0.20f, 15, 0,
                true, "Fire ravages the lower market! Arson suspected.", null),

            new EvilDeedDef("blackmail_noble", "Blackmail a Noble",
                "You've been watching Lord Aldric's midnight visits to the Dark Alley.\nA man of his position has much to lose. Your silence has a price.",
                DeedTier.Serious, 15, 5, 100, 0, 200, 300, 0, 3, -3, 0.20f, 20, 100,
                false, null, null),

            new EvilDeedDef("whisper_noctura", "Whisper Noctura's Name",
                "They say if you speak the Shadow Queen's true name three times in\nabsolute darkness, she hears you. In the deepest corner of the alley,\nwhere no torch reaches, you whisper: 'Noctura... Noctura... Noctura...'\nThe shadows thicken. Something answers.",
                DeedTier.Serious, 25, 5, 100, 0, 0, 0, 50, 0, 0, 0.10f, 10, 0,
                false, null, "noctura"),

            new EvilDeedDef("sabotage_wagons", "Sabotage Crown Wagons",
                "The Crown's supply caravan passes through the narrow streets at dawn.\nA loosened axle pin, a spooked horse — the King's soldiers go hungry\nwhile the rebels feast.",
                DeedTier.Serious, 18, 5, 100, 0, 0, 0, 30, 8, -8, 0.15f, 0, 200,
                true, "{PLAYER} is wanted for sabotaging Crown supply lines.", "shadows_bonus"),

            // ── Tier 3: Dark Rituals ──
            new EvilDeedDef("blood_maelketh", "Blood Offering to Maelketh",
                "In a cellar beneath the alley, fanatics of the Broken Blade maintain\na hidden altar stained rust-red. They welcome you with hollow eyes.\nThe blade they offer is sharp. Your blood will feed a god's hunger.",
                DeedTier.Dark, 40, 15, 400, 0, 0, 0, 100, 5, 0, 0.20f, 30, 0,
                false, null, "blood_price"),

            new EvilDeedDef("dark_pact", "Forge a Dark Pact",
                "A figure in the alley offers something no merchant sells: certainty.\nSign your name in blood and ink blacker than midnight, and for ten\ncombats your blade will strike true. The price is written in letters\ntoo small to read.",
                DeedTier.Dark, 30, 15, 400, 500, 0, 0, 75, 0, 0, 0.25f, 25, 0,
                false, null, "dark_pact"),

            new EvilDeedDef("thorgrim_law", "Invoke Thorgrim's Law",
                "There is an older law than the King's. In the underground court beneath\nthe alley, you stand before a mockery of justice and pronounce sentence\non the weak. The shadows applaud.",
                DeedTier.Dark, 35, 15, 400, 0, 0, 0, 80, 5, -5, 0.15f, 0, 200,
                true, "A dark tribunal was held beneath the streets. The old laws stir.", "thorgrim"),

            new EvilDeedDef("void_sacrifice", "Sacrifice to the Void",
                "Beyond the alley, past the cellar, past tunnels that should not exist,\nthere is a place where the stone floor drops into nothing. The cultists\ncall it the Mouth. Cast something precious into it, and the Void\ngives power in return.",
                DeedTier.Dark, 50, 15, 400, 1000, 0, 0, 150, 5, 0, 0.15f, 0, 2000,
                false, null, "void"),

            new EvilDeedDef("shatter_seal", "Shatter a Seal Fragment",
                "The Seven Seals aren't just lore. Fragments of their power echo in\nhidden places. In the deepest part of the alley, you find such a\nfragment — a humming shard of ancient law. You could study it...\nor you could break it and drink in the power that spills out.",
                DeedTier.Dark, 60, 15, 400, 0, 0, 0, 200, 0, 0, 0.10f, 25, 0,
                true, "A tremor of dark energy ripples through the town. An ancient seal has been defiled.", "seal"),
        };

        private static readonly Random _deedRng = new();

        private bool MeetsDeedRequirements(EvilDeedDef deed)
        {
            if (currentPlayer.Level < deed.MinLevel) return false;
            if (currentPlayer.Darkness < deed.MinDarkness) return false;

            // Special requirements
            switch (deed.SpecialEffect)
            {
                case "noctura":
                    // Requires encountering Noctura OR high darkness
                    var story = StoryProgressionSystem.Instance;
                    bool metNoctura = story.OldGodStates.TryGetValue(OldGodType.Noctura, out var ns) &&
                        ns.Status != GodStatus.Unknown && ns.Status != GodStatus.Corrupted && ns.Status != GodStatus.Neutral;
                    if (!metNoctura && currentPlayer.Darkness < 300) return false;
                    break;
                case "thorgrim":
                    // Requires level 20+ or having encountered Thorgrim
                    var story2 = StoryProgressionSystem.Instance;
                    bool metThorgrim = story2.OldGodStates.TryGetValue(OldGodType.Thorgrim, out var ts) &&
                        ts.Status != GodStatus.Unknown && ts.Status != GodStatus.Corrupted;
                    if (currentPlayer.Level < 20 && !metThorgrim) return false;
                    break;
                case "seal":
                    // Requires at least 1 seal collected, once per cycle
                    if (StoryProgressionSystem.Instance.CollectedSeals.Count < 1) return false;
                    if (currentPlayer.HasShatteredSealFragment) return false;
                    break;
                case "void":
                    // The awakening grant is once-only, but the deed itself is repeatable
                    break;
            }
            return true;
        }

        private async Task ShowEvilDeeds()
        {
            terminal.ClearScreen();

            // Header
            WriteBoxHeader(Loc.Get("dark_alley.evil_deeds_header"), "bright_red", 66);
            terminal.WriteLine("");

            // Stats
            terminal.SetColor("gray");
            terminal.Write(Loc.Get("dark_alley.evil_darkness"));
            terminal.SetColor("red");
            terminal.Write($"{currentPlayer.Darkness}");
            terminal.SetColor("gray");
            terminal.Write(Loc.Get("dark_alley.evil_deeds_remaining"));
            terminal.SetColor(currentPlayer.DarkNr > 0 ? "bright_yellow" : "red");
            terminal.WriteLine($"{currentPlayer.DarkNr}");
            terminal.WriteLine("");

            if (currentPlayer.DarkNr <= 0)
            {
                terminal.SetColor("dark_red");
                terminal.WriteLine(Loc.Get("dark_alley.evil_filled"));
                terminal.WriteLine(Loc.Get("dark_alley.evil_return"));
                terminal.WriteLine("");
                await terminal.PressAnyKey();
                return;
            }

            // Build available deed list
            var available = AllEvilDeeds.Where(MeetsDeedRequirements).ToList();

            // Group by tier and display
            int num = 1;
            var indexMap = new Dictionary<int, EvilDeedDef>();

            foreach (var tier in new[] { DeedTier.Petty, DeedTier.Serious, DeedTier.Dark })
            {
                var tierDeeds = available.Where(d => d.Tier == tier).ToList();
                if (tierDeeds.Count == 0) continue;

                var (tierName, tierColor, tierReq) = tier switch
                {
                    DeedTier.Petty => (Loc.Get("dark_alley.evil_tier_petty"), "yellow", ""),
                    DeedTier.Serious => (Loc.Get("dark_alley.evil_tier_serious"), "bright_red", Loc.Get("dark_alley.evil_tier_serious_req", GameConfig.EvilDeedSeriousMinLevel, GameConfig.EvilDeedSeriousMinDarkness)),
                    DeedTier.Dark => (Loc.Get("dark_alley.evil_tier_dark"), "bright_magenta", Loc.Get("dark_alley.evil_tier_dark_req", GameConfig.EvilDeedDarkMinLevel, GameConfig.EvilDeedDarkMinDarkness)),
                    _ => ("", "white", "")
                };

                if (IsScreenReader)
                {
                    terminal.SetColor(tierColor);
                    terminal.Write($"  {tierName}");
                    if (tierReq.Length > 0) { terminal.SetColor("darkgray"); terminal.Write(tierReq); }
                    terminal.WriteLine(":");
                }
                else
                {
                    terminal.SetColor(tierColor);
                    terminal.Write($"  ── {tierName} ──");
                    if (tierReq.Length > 0) { terminal.SetColor("darkgray"); terminal.Write(tierReq); }
                    terminal.WriteLine("");
                }

                foreach (var deed in tierDeeds)
                {
                    indexMap[num] = deed;
                    if (IsScreenReader)
                    {
                        var deedName = Loc.Get($"dark_alley.deed_{deed.Id}_name");
                        var parts = new List<string> { deedName, $"+{deed.DarknessGain} Dark" };
                        if (deed.XPReward > 0) parts.Add($"+{deed.XPReward}XP");
                        if (deed.GoldRewardBase > 0) parts.Add("+gold");
                        if (deed.GoldCost > 0) parts.Add($"-{deed.GoldCost}g");
                        if (deed.FailChance > 0) parts.Add($"{(int)(deed.FailChance * 100)}% risk");
                        WriteSRMenuOption($"{num}", string.Join(", ", parts));
                    }
                    else
                    {
                        terminal.SetColor("darkgray");
                        terminal.Write($"  [{num,2}] ");
                        terminal.SetColor("white");
                        terminal.Write(Loc.Get($"dark_alley.deed_{deed.Id}_name").PadRight(28));
                        terminal.SetColor("red");
                        terminal.Write($"+{deed.DarknessGain} Dark ");
                        if (deed.XPReward > 0) { terminal.SetColor("cyan"); terminal.Write($"+{deed.XPReward}XP "); }
                        if (deed.GoldRewardBase > 0) { terminal.SetColor("bright_yellow"); terminal.Write($"+gold "); }
                        if (deed.GoldCost > 0) { terminal.SetColor("yellow"); terminal.Write($"-{deed.GoldCost}g "); }
                        if (deed.FailChance > 0) { terminal.SetColor("darkgray"); terminal.Write($"{(int)(deed.FailChance * 100)}%risk"); }
                        terminal.WriteLine("");
                    }
                    num++;
                }
                terminal.WriteLine("");
            }

            terminal.SetColor("gray");
            var input = await terminal.GetInput(Loc.Get("dark_alley.evil_choose_deed"));
            if (!int.TryParse(input, out int choice) || choice == 0 || !indexMap.ContainsKey(choice))
                return;

            await ExecuteEvilDeed(indexMap[choice]);
        }

        private async Task ExecuteEvilDeed(EvilDeedDef deed)
        {
            terminal.ClearScreen();

            // Show atmospheric description
            terminal.SetColor("bright_red");
            var localDeedName = Loc.Get($"dark_alley.deed_{deed.Id}_name");
            if (IsScreenReader)
                terminal.WriteLine(localDeedName);
            else
                terminal.WriteLine($"── {localDeedName} ──");
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get($"dark_alley.deed_{deed.Id}_desc"));
            terminal.WriteLine("");

            // Show costs/risks
            if (deed.GoldCost > 0)
            {
                if (currentPlayer.Gold < deed.GoldCost)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("dark_alley.evil_need_gold", deed.GoldCost, currentPlayer.Gold));
                    await terminal.PressAnyKey();
                    return;
                }
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("dark_alley.evil_cost", deed.GoldCost));
            }
            if (deed.SpecialEffect == "blood_price")
            {
                int hpCost = (int)(currentPlayer.MaxHP * 0.15f);
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("dark_alley.evil_blood_price", hpCost));
            }
            if (deed.FailChance > 0)
            {
                terminal.SetColor("darkgray");
                terminal.WriteLine(Loc.Get("dark_alley.evil_risk", (int)(deed.FailChance * 100)));
            }
            terminal.WriteLine("");

            var confirm = await terminal.GetInput(Loc.Get("dark_alley.evil_commit_prompt"));
            if (!confirm.Equals("Y", StringComparison.OrdinalIgnoreCase))
                return;

            terminal.WriteLine("");

            // Deduct daily counter
            currentPlayer.DarkNr--;

            // Deduct gold cost upfront
            if (deed.GoldCost > 0)
                currentPlayer.Gold -= deed.GoldCost;

            // Blood price (Maelketh offering)
            if (deed.SpecialEffect == "blood_price")
            {
                int hpCost = (int)(currentPlayer.MaxHP * 0.15f);
                currentPlayer.HP = Math.Max(1, currentPlayer.HP - hpCost);
                terminal.SetColor("dark_red");
                terminal.WriteLine(Loc.Get("dark_alley.evil_blood_cut", hpCost));
            }

            // Roll for failure
            float effectiveFailChance = deed.FailChance;

            // Noctura alliance reduces risk to 0
            if (deed.SpecialEffect == "noctura")
            {
                var story = StoryProgressionSystem.Instance;
                if (story.OldGodStates.TryGetValue(OldGodType.Noctura, out var ns) && ns.Status == GodStatus.Allied)
                    effectiveFailChance = 0f;
            }
            // Shadows faction reduces sabotage risk
            if (deed.SpecialEffect == "shadows_bonus" && FactionSystem.Instance?.PlayerFaction == Faction.TheShadows)
                effectiveFailChance = 0.05f;

            bool failed = _deedRng.NextDouble() < effectiveFailChance;

            if (failed)
            {
                // ── FAILURE ──
                terminal.SetColor("bright_red");
                terminal.WriteLine(Loc.Get("dark_alley.evil_caught"));
                terminal.WriteLine("");

                // Partial darkness (you tried)
                AlignmentSystem.Instance.ChangeAlignment(currentPlayer, Math.Max(3, deed.DarknessGain / 3), isGood: false, "dark_alley.evil_fail"); // v0.57.12: paired movement

                if (deed.FailDamagePct > 0)
                {
                    int dmg = (int)(currentPlayer.MaxHP * deed.FailDamagePct / 100f);
                    currentPlayer.HP = Math.Max(1, currentPlayer.HP - dmg);
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("dark_alley.evil_damage", dmg));
                }
                if (deed.FailGoldLoss > 0)
                {
                    long loss = Math.Min(currentPlayer.Gold, deed.FailGoldLoss);
                    currentPlayer.Gold -= loss;
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("dark_alley.evil_gold_loss", loss));
                }

                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("dark_alley.evil_slink"));
            }
            else
            {
                // ── SUCCESS ──
                terminal.SetColor("bright_magenta");
                terminal.WriteLine(Loc.Get("dark_alley.evil_done"));
                terminal.WriteLine("");

                // Darkness gain — v0.57.12: paired movement lowers chivalry by half (this is the v0.57.0 design intent)
                AlignmentSystem.Instance.ChangeAlignment(currentPlayer, deed.DarknessGain, isGood: false, "dark_alley.evil_deed");
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("dark_alley.evil_darkness_gain", deed.DarknessGain));

                // Reputation scales with deed tier
                int deedRep = deed.Tier == DeedTier.Dark ? 15 : deed.Tier == DeedTier.Serious ? 8 : 3;
                currentPlayer.DarkAlleyReputation = Math.Min(1000, currentPlayer.DarkAlleyReputation + deedRep);

                // Gold reward (level-scaled)
                if (deed.GoldRewardBase > 0)
                {
                    long gold = deed.GoldRewardBase + _deedRng.Next(deed.GoldRewardScale) + (currentPlayer.Level * 2);
                    currentPlayer.Gold += gold;
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine(Loc.Get("dark_alley.evil_gold_gain", gold));
                }

                // XP
                if (deed.XPReward > 0)
                {
                    long xp = deed.XPReward + (currentPlayer.Level * 3);
                    currentPlayer.Experience += xp;
                    terminal.SetColor("cyan");
                    terminal.WriteLine(Loc.Get("dark_alley.evil_xp_gain", xp));
                }

                // Faction changes
                if (deed.ShadowsFaction != 0)
                {
                    int shadowsGain = deed.ShadowsFaction;
                    // Shadows members get double rep from sabotage
                    if (deed.SpecialEffect == "shadows_bonus" && FactionSystem.Instance?.PlayerFaction == Faction.TheShadows)
                        shadowsGain *= 2;
                    FactionSystem.Instance?.ModifyReputation(Faction.TheShadows, shadowsGain);
                    terminal.SetColor("bright_magenta");
                    terminal.WriteLine(Loc.Get("dark_alley.evil_shadows_gain", $"{(shadowsGain > 0 ? "+" : "")}{shadowsGain}"));
                }
                if (deed.CrownFaction != 0)
                {
                    FactionSystem.Instance?.ModifyReputation(Faction.TheCrown, deed.CrownFaction);
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("dark_alley.evil_crown_gain", deed.CrownFaction));
                }

                // Chivalry loss from shrine vandalism
                if (deed.SpecialEffect == "chivalry_loss")
                {
                    currentPlayer.Chivalry = Math.Max(0, currentPlayer.Chivalry - 5);
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("dark_alley.evil_chiv_loss"));
                }

                // Dark Pact combat buff
                if (deed.SpecialEffect == "dark_pact")
                {
                    currentPlayer.DarkPactCombats = GameConfig.DarkPactDuration;
                    currentPlayer.DarkPactDamageBonus = GameConfig.DarkPactDamageBonus;
                    terminal.SetColor("bright_magenta");
                    terminal.WriteLine(Loc.Get("dark_alley.evil_dark_pact", (int)(GameConfig.DarkPactDamageBonus * 100), GameConfig.DarkPactDuration));
                }

                // Maelketh — reduced effect if defeated
                if (deed.Id == "blood_maelketh")
                {
                    var story = StoryProgressionSystem.Instance;
                    if (story.OldGodStates.TryGetValue(OldGodType.Maelketh, out var ms) && ms.Status == GodStatus.Defeated)
                    {
                        terminal.SetColor("darkgray");
                        terminal.WriteLine(Loc.Get("dark_alley.evil_altar_cold"));
                        terminal.WriteLine(Loc.Get("dark_alley.evil_altar_linger"));
                        // Already gave full rewards via the normal path — flavor only
                    }
                }

                // Void sacrifice — one-time awakening point
                if (deed.SpecialEffect == "void" && !currentPlayer.HasTouchedTheVoid)
                {
                    currentPlayer.HasTouchedTheVoid = true;
                    OceanPhilosophySystem.Instance?.GainInsight(10);
                    terminal.SetColor("bright_cyan");
                    terminal.WriteLine(Loc.Get("dark_alley.evil_void_whisper"));
                    terminal.WriteLine(Loc.Get("dark_alley.evil_awakening"));
                }

                // Seal fragment — once per cycle
                if (deed.SpecialEffect == "seal")
                {
                    currentPlayer.HasShatteredSealFragment = true;
                    terminal.SetColor("bright_magenta");
                    terminal.WriteLine(Loc.Get("dark_alley.evil_seal_echo"));
                }

                // News event
                if (deed.GeneratesNews && deed.NewsText != null)
                {
                    var newsText = deed.NewsText.Replace("{PLAYER}", currentPlayer.DisplayName);
                    NewsSystem.Instance.Newsy(false, newsText);
                }
            }

            terminal.WriteLine("");
            await terminal.PressAnyKey();
        }

        #endregion

        /// <summary>
        /// Phase 5: emit Dark Alley menu state for the Electron client. Pattern B.
        /// </summary>
        private void EmitElectronEvents()
        {
            var player = GetCurrentPlayer();
            if (player == null) return;

            ElectronBridge.EmitLocation(
                name: Loc.Get("dark_alley.header"),
                description: Loc.Get("dark_alley.visual_desc"),
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
                new() { Key = "D", Label = "Drug Palace", Category = "evil", Icon = "drug" },
                new() { Key = "S", Label = "Steroid Shop", Category = "evil", Icon = "steroid" },
                new() { Key = "O", Label = "Orbs Health Club", Category = "service", Icon = "orb" },
                new() { Key = "G", Label = "Groggo Magic", Category = "shop", Icon = "magic" },
                new() { Key = "B", Label = "Beer Hut", Category = "social", Icon = "beer" },
                new() { Key = "A", Label = "Alchemist Heaven", Category = "shop", Icon = "alchemist" },
                new() { Key = "J", Label = "Shadows Faction", Category = "team", Icon = "shadow" },
                new() { Key = "W", Label = "Pay Tribute", Category = "social", Icon = "tribute" },
                new() { Key = "M", Label = "Black Market", Category = "shop", Icon = "market" },
                new() { Key = "I", Label = "Informant", Category = "social", Icon = "informant" },
                new() { Key = "P", Label = "Pickpocket", Category = "evil", Icon = "pickpocket" },
                new() { Key = "C", Label = "Gambling Den", Category = "social", Icon = "dice" },
                new() { Key = "T", Label = "The Pit", Category = "combat", Icon = "pit" },
                new() { Key = "L", Label = "Loan Shark", Category = "shop", Icon = "loan" },
                new() { Key = "N", Label = "Safe House", Category = "service", Icon = "safe" },
                new() { Key = "E", Label = "Evil Deeds", Category = "evil", Icon = "evil" },
                new() { Key = "R", Label = Loc.Get("ui.return"), Category = "navigate", Icon = "back" },
            };
            ElectronBridge.EmitMenu(menu);

            EmitNPCsInLocationToElectron();
        }
    }
}