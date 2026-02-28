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
            // Check if Dark Alley is accessible due to world events (e.g., Martial Law)
            var (accessible, reason) = WorldEventSystem.Instance.IsLocationAccessible("Dark Alley");
            if (!accessible)
            {
                term.SetColor("bright_red");
                term.WriteLine("");
                term.WriteLine("═══════════════════════════════════════");
                term.WriteLine("          ACCESS DENIED");
                term.WriteLine("═══════════════════════════════════════");
                term.WriteLine("");
                term.SetColor("red");
                term.WriteLine(reason);
                term.WriteLine("");
                term.SetColor("yellow");
                term.WriteLine("Guards block the entrance to the Dark Alley.");
                term.WriteLine("You must return when martial law is lifted.");
                term.WriteLine("");
                await term.PressAnyKey("Press Enter to return...");
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
            terminal.WriteLine("A heavy hand lands on your shoulder. You turn to find cold eyes staring you down.");
            terminal.SetColor("red");
            terminal.WriteLine("");
            terminal.WriteLine("\"We don't serve your kind here. The Shadows have long memories,");
            terminal.WriteLine(" and you've made too many enemies in this alley.\"");
            terminal.SetColor("gray");
            terminal.WriteLine("");
            terminal.WriteLine($"  Shadows standing: {standing:N0} — you need at least -50 to access underground services.");
            terminal.SetColor("yellow");
            terminal.Write("  Try paying ");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("W");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("yellow");
            terminal.WriteLine("tribute to improve your standing.");
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
                case "F": // Fence stolen goods
                    if (!IsUndergroundAccessAllowed()) { await ShowUndergroundRejection(); return false; }
                    await VisitFence();
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
            "A distant footstep echoes and then stops.",
            "Something small skitters across the cobblestones.",
            "Water drips steadily from a broken gutter overhead.",
            "Hushed voices drift from a darkened doorway.",
            "A shadow shifts at the far end of the alley.",
            "Distant laughter, low and brief, comes from somewhere unseen.",
        };

        protected override void DisplayLocation()
        {
            if (IsBBSSession) { DisplayLocationBBS(); return; }

            terminal.ClearScreen();

            // Header - standardized format
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("╔═════════════════════════════════════════════════════════════════════════════╗");
            terminal.WriteLine($"║{"THE DARK ALLEY".PadLeft((77 + 14) / 2).PadRight(77)}║");
            terminal.WriteLine("╚═════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            terminal.SetColor("gray");
            terminal.WriteLine("Torches sputter in the moist air and the smell of trash " +
                                "mixes with exotic spices.  Whispers of illicit trade echo " +
                                "between crooked doorways.");
            terminal.WriteLine("");

            // Show alignment reaction in shady area
            var alignment = AlignmentSystem.Instance.GetAlignment(currentPlayer);
            var (alignText, alignColor) = AlignmentSystem.Instance.GetAlignmentDisplay(currentPlayer);
            var priceModifier = AlignmentSystem.Instance.GetPriceModifier(currentPlayer, isShadyShop: true);

            if (alignment == AlignmentSystem.AlignmentType.Holy || alignment == AlignmentSystem.AlignmentType.Good)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("The vendors eye you suspiciously. Your virtuous aura doesn't belong here.");
                terminal.SetColor("red");
                terminal.WriteLine($"  Prices are {(int)((priceModifier - 1.0f) * 100)}% higher for someone of {alignText} alignment.");
            }
            else if (alignment == AlignmentSystem.AlignmentType.Dark || alignment == AlignmentSystem.AlignmentType.Evil)
            {
                terminal.SetColor("bright_magenta");
                terminal.WriteLine("The shady merchants nod in recognition. You're one of them.");
                terminal.SetColor("bright_green");
                terminal.WriteLine($"  You receive a {(int)((1.0f - priceModifier) * 100)}% discount as a fellow {alignText} soul.");
            }
            terminal.WriteLine("");

            ShowNPCsInLocation();

            terminal.SetColor("cyan");
            terminal.WriteLine("Shady establishments:");
            terminal.WriteLine("");

            // Row 1
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("D");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write("rug Palace             ");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("S");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine("teroid Shop");

            // Row 2
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("O");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write("rbs Health Club        ");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("G");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine("roggo's Magic Services");

            // Row 3
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("B");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write("ob's Beer Hut          ");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("A");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine("lchemist's Heaven");

            terminal.WriteLine("");

            // Underground Services section
            bool undergroundLocked = !IsUndergroundAccessAllowed();
            var shadowsStanding = FactionSystem.Instance?.FactionStanding[Faction.TheShadows] ?? 0;

            terminal.SetColor("dark_red");
            terminal.WriteLine("Underground Services:");
            if (undergroundLocked)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"  The Shadows don't trust you. (Standing: {shadowsStanding:N0}, need -50+)");
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
            terminal.Write("ickpocket              ");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor(keyColor);
            terminal.Write("F");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor(labelColor);
            terminal.WriteLine("ence Stolen Goods");

            // Row 2
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor(keyColor);
            terminal.Write("C");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor(labelColor);
            terminal.Write(" Gambling Den           ");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor(keyColor);
            terminal.Write("T");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor(labelColor);
            terminal.WriteLine(" The Pit (Arena)");

            // Row 3
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor(keyColor);
            terminal.Write("L");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor(labelColor);
            terminal.Write("oan Shark               ");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor(keyColor);
            terminal.Write("N");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor(labelColor);
            terminal.WriteLine(" Safe House");

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
                terminal.WriteLine(" Pay Tribute to the Shadows");
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
                terminal.Write("oin The Shadows ");
                if (factionSystem.PlayerFaction == null)
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine("(a figure watches from the darkness...)");
                }
                else
                {
                    terminal.SetColor("dark_red");
                    terminal.WriteLine("(you serve another...)");
                }
            }
            else
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine(" You are a member of The Shadows.");
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
                terminal.WriteLine(" Black Market");
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
                terminal.WriteLine("nformant");
            }

            // Navigation
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("R");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine("eturn to Main Street");
            terminal.WriteLine("");

            ShowStatusLine();
        }

        /// <summary>
        /// Compact BBS display for 80x25 terminals.
        /// </summary>
        private void DisplayLocationBBS()
        {
            terminal.ClearScreen();
            ShowBBSHeader("THE DARK ALLEY");

            // 1-line description
            terminal.SetColor("gray");
            terminal.WriteLine(" Torches sputter in the moist air. Whispers of illicit trade echo in the dark.");

            ShowBBSNPCs();
            terminal.WriteLine("");

            // Shady establishments (2 rows)
            terminal.SetColor("dark_red");
            terminal.WriteLine(" Shady Establishments:");
            ShowBBSMenuRow(("D", "bright_yellow", "DrugPalace"), ("S", "bright_yellow", "Steroids"), ("O", "bright_yellow", "OrbsClub"), ("G", "bright_yellow", "Groggo"));
            ShowBBSMenuRow(("B", "bright_yellow", "BeerHut"), ("A", "bright_yellow", "Alchemist"));

            // Underground services (2 rows)
            bool locked = !IsUndergroundAccessAllowed();
            string kc = locked ? "darkgray" : "bright_yellow";
            terminal.SetColor("dark_red");
            terminal.Write(" Underground");
            if (locked)
            {
                terminal.SetColor("red");
                terminal.Write(" [LOCKED]");
            }
            terminal.WriteLine(":");
            ShowBBSMenuRow(("P", kc, "Pickpocket"), ("F", kc, "Fence"), ("C", kc, "Gamble"), ("T", kc, "ThePit"));
            ShowBBSMenuRow(("L", kc, "LoanShark"), ("N", kc, "SafeHouse"));

            // Faction/special options (1 row)
            var factionSystem = FactionSystem.Instance;
            var shadowsStanding = FactionSystem.Instance?.FactionStanding[Faction.TheShadows] ?? 0;
            var specialItems = new List<(string key, string color, string label)>();
            if (shadowsStanding < 0)
                specialItems.Add(("W", "bright_yellow", "Tribute"));
            if (factionSystem.PlayerFaction != Faction.TheShadows)
                specialItems.Add(("J", "bright_yellow", "JoinShadows"));
            if (FactionSystem.Instance?.HasBlackMarketAccess() == true)
                specialItems.Add(("M", "bright_yellow", "BlackMkt"));
            if (FactionSystem.Instance?.HasInformationNetwork() == true)
                specialItems.Add(("I", "bright_yellow", "Informant"));
            if (specialItems.Count > 0)
                ShowBBSMenuRow(specialItems.ToArray());

            ShowBBSMenuRow(("R", "bright_yellow", "Return"));

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
            return (long)(basePrice * alignmentModifier * worldEventModifier);
        }

        private async Task VisitDrugPalace()
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
            terminal.WriteLine("║                       THE DRUG PALACE                            ║");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            terminal.SetColor("gray");
            terminal.WriteLine("You enter a smoky den lined with velvet curtains. A hooded dealer");
            terminal.WriteLine("spreads an array of colorful vials and packets across the table.");
            terminal.WriteLine("");

            if (currentPlayer.OnDrugs)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"You're already under the influence of {currentPlayer.ActiveDrug}!");
                terminal.WriteLine($"Effects will wear off in {currentPlayer.DrugEffectDays} day(s).");
                terminal.WriteLine("");
            }

            if (currentPlayer.Addict > 0)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"Addiction Level: {currentPlayer.Addict}/100");
                terminal.WriteLine("");
            }

            // Drug menu
            terminal.SetColor("yellow");
            terminal.WriteLine("Available substances:");
            terminal.WriteLine("");

            var drugs = new (DrugType drug, string name, string desc, long basePrice)[]
            {
                (DrugType.Steroids, "Steroids", "+STR, +DMG (3 days)", 500),
                (DrugType.BerserkerRage, "Berserker Rage", "+STR, +ATK, -DEF (1 day)", 300),
                (DrugType.Haste, "Haste Powder", "+AGI, +Attacks, HP drain (2 days)", 600),
                (DrugType.QuickSilver, "Quicksilver", "+DEX, +Crit (2 days)", 550),
                (DrugType.ManaBoost, "Mana Boost", "+Mana, +Spell Power (3 days)", 700),
                (DrugType.ThirdEye, "Third Eye", "+WIS, +Magic Resist (3 days)", 650),
                (DrugType.Ironhide, "Ironhide", "+CON, +DEF, -AGI (2 days)", 500),
                (DrugType.Stoneskin, "Stoneskin", "+Armor, -Speed (2 days)", 450),
                (DrugType.DarkEssence, "Dark Essence", "+All Stats, HIGH ADDICTION (1 day)", 1500),
                (DrugType.DemonBlood, "Demon Blood", "+DMG, +Darkness, VERY ADDICTIVE (2 days)", 2000)
            };

            for (int i = 0; i < drugs.Length; i++)
            {
                var d = drugs[i];
                long price = GetAdjustedPrice(d.basePrice);
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

            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("0");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("yellow");
            terminal.WriteLine(" Leave");
            terminal.WriteLine("");

            var choice = await terminal.GetInput("Your choice: ");

            if (!int.TryParse(choice, out int selection) || selection < 1 || selection > drugs.Length)
            {
                return;
            }

            var selected = drugs[selection - 1];
            long finalPrice = GetAdjustedPrice(selected.basePrice);

            if (currentPlayer.Gold < finalPrice)
            {
                terminal.WriteLine("The dealer laughs. \"Come back when you have real money!\"", "red");
                await Task.Delay(2000);
                return;
            }

            terminal.WriteLine($"Buy {selected.name} for {finalPrice:N0} gold? (Y/N)", "yellow");
            var confirm = await terminal.GetInput("> ");

            if (confirm.ToUpper() != "Y")
            {
                terminal.WriteLine("You back away from the table.", "gray");
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
                if (effects.StrengthBonus > 0) terminal.WriteLine($"  +{effects.StrengthBonus} Strength");
                if (effects.DexterityBonus > 0) terminal.WriteLine($"  +{effects.DexterityBonus} Dexterity");
                if (effects.AgilityBonus > 0) terminal.WriteLine($"  +{effects.AgilityBonus} Agility");
                if (effects.ConstitutionBonus > 0) terminal.WriteLine($"  +{effects.ConstitutionBonus} Constitution");
                if (effects.WisdomBonus > 0) terminal.WriteLine($"  +{effects.WisdomBonus} Wisdom");
                if (effects.ManaBonus > 0) terminal.WriteLine($"  +{effects.ManaBonus} Mana");
                if (effects.DamageBonus > 0) terminal.WriteLine($"  +{effects.DamageBonus}% Damage");
                if (effects.DefenseBonus > 0) terminal.WriteLine($"  +{effects.DefenseBonus} Defense");
                if (effects.ExtraAttacks > 0) terminal.WriteLine($"  +{effects.ExtraAttacks} Extra Attacks");

                terminal.SetColor("red");
                if (effects.DefensePenalty > 0) terminal.WriteLine($"  -{effects.DefensePenalty} Defense");
                if (effects.AgilityPenalty > 0) terminal.WriteLine($"  -{effects.AgilityPenalty} Agility");
                if (effects.SpeedPenalty > 0) terminal.WriteLine($"  -{effects.SpeedPenalty} Speed");
                if (effects.HPDrain > 0) terminal.WriteLine($"  -{effects.HPDrain} HP/round drain");

                currentPlayer.Darkness += 5; // Dark act

                // Increase Shadows standing for shady dealings
                FactionSystem.Instance.ModifyReputation(Faction.TheShadows, 5);
                terminal.SetColor("bright_magenta");
                terminal.WriteLine("  The Shadows have noted your... activities. (+5 Shadows standing)");
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
            terminal.WriteLine("A muscular dwarf guards crates of suspicious vials.", "white");

            if (currentPlayer.SteroidShopPurchases >= GameConfig.MaxSteroidShopPurchases)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("The dwarf shakes his head. \"Your body can't handle any more, friend.\"");
                terminal.SetColor("gray");
                terminal.WriteLine($"(Maximum {GameConfig.MaxSteroidShopPurchases} lifetime purchases reached)");
                await Task.Delay(2000);
                return;
            }

            long price = GetAdjustedPrice(1000);
            terminal.WriteLine($"Bulk-up serum costs {price:N0} {GameConfig.MoneyType}.", "cyan");
            terminal.SetColor("gray");
            terminal.WriteLine($"(Purchases: {currentPlayer.SteroidShopPurchases}/{GameConfig.MaxSteroidShopPurchases})");
            var ans = await terminal.GetInput("Inject? (Y/N): ");
            if (ans.ToUpper() != "Y") return;

            if (currentPlayer.Gold < price)
            {
                terminal.WriteLine("You can't afford that!", "red");
                await Task.Delay(1500);
                return;
            }

            currentPlayer.Gold -= price;
            currentPlayer.Strength += 5;
            currentPlayer.Stamina += 3;
            currentPlayer.Darkness += 3;
            currentPlayer.SteroidShopPurchases++;

            terminal.WriteLine("Your muscles swell unnaturally!", "bright_green");
            terminal.SetColor("gray");
            terminal.WriteLine($"({GameConfig.MaxSteroidShopPurchases - currentPlayer.SteroidShopPurchases} purchases remaining)");
            await Task.Delay(2000);
        }

        private async Task VisitOrbsHealthClub()
        {
            terminal.WriteLine("");
            terminal.WriteLine("A hooded cleric guides you to glowing orbs floating in a pool.", "white");
            long price = GetAdjustedPrice(currentPlayer.Level * 50 + 100);
            terminal.WriteLine($"Restoring vitality costs {price:N0} {GameConfig.MoneyType}.", "cyan");
            var ans = await terminal.GetInput("Pay? (Y/N): ");
            if (ans.ToUpper() != "Y") return;

            if (currentPlayer.Gold < price)
            {
                terminal.WriteLine("Insufficient gold.", "red");
                await Task.Delay(1500);
                return;
            }

            currentPlayer.Gold -= price;
            currentPlayer.HP = currentPlayer.MaxHP;
            terminal.WriteLine("Warm light knits your wounds together – you are fully healed!", "bright_green");
            await Task.Delay(2000);
        }

        private async Task VisitGroggoMagic()
        {
            terminal.WriteLine("");
            terminal.WriteLine("The infamous gnome Groggo grins widely behind a cluttered desk.", "white");
            terminal.WriteLine("\"Secrets, charms, and forbidden knowledge! What'll it be?\"", "yellow");
            terminal.WriteLine("");

            terminal.WriteLine("Groggo's Services:", "cyan");
            terminal.WriteLine("  (1) Dungeon Intel - 100 gold (reveals monsters on current floor)");
            terminal.WriteLine("  (2) Fortune Reading - 250 gold (hints about upcoming events)");
            terminal.WriteLine("  (3) Blessing of Shadows - 500 gold (temporary stealth bonus)");
            terminal.WriteLine("  (0) Never mind");
            terminal.WriteLine("");

            var choice = await terminal.GetInput("Your choice: ");

            switch (choice)
            {
                case "1":
                    long intelPrice = GetAdjustedPrice(100);
                    if (currentPlayer.Gold < intelPrice)
                    {
                        terminal.WriteLine("Groggo scoffs. \"Come back with coin!\"", "red");
                        break;
                    }
                    currentPlayer.Gold -= intelPrice;
                    int dungeonFloor = Math.Max(1, currentPlayer.Level / 3); // Estimate based on player level
                    terminal.WriteLine("");
                    terminal.WriteLine("Groggo whispers dungeon secrets:", "bright_magenta");
                    terminal.WriteLine($"  \"For someone of your skill, floor {dungeonFloor} should be manageable...\"", "white");
                    terminal.WriteLine($"  \"Monsters there are around level {dungeonFloor * 2 + 5}.\"", "white");
                    terminal.WriteLine($"  \"Bring potions. Many potions.\"", "gray");
                    break;

                case "2":
                    long fortunePrice = GetAdjustedPrice(250);
                    if (currentPlayer.Gold < fortunePrice)
                    {
                        terminal.WriteLine("Groggo scoffs. \"The future costs money, friend!\"", "red");
                        break;
                    }
                    currentPlayer.Gold -= fortunePrice;
                    terminal.WriteLine("");
                    terminal.WriteLine("Groggo peers into a murky crystal ball:", "bright_magenta");
                    var fortunes = new[] {
                        "\"I see gold in your future... but also danger.\"",
                        "\"A powerful enemy watches you from the shadows.\"",
                        "\"The deeper you go, the greater the rewards.\"",
                        "\"Trust not the smiling stranger in the Inn.\"",
                        "\"Your destiny is intertwined with the Seven Seals.\"",
                        "\"The old gods stir in their prisons...\""
                    };
                    terminal.WriteLine($"  {fortunes[Random.Shared.Next(0, fortunes.Length)]}", "white");
                    break;

                case "3":
                    long blessPrice = GetAdjustedPrice(500);
                    if (currentPlayer.Gold < blessPrice)
                    {
                        terminal.WriteLine("Groggo scoffs. \"Shadow magic isn't cheap!\"", "red");
                        break;
                    }
                    currentPlayer.Gold -= blessPrice;
                    if (currentPlayer.GroggoShadowBlessingDex > 0)
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine("You already have the Blessing of Shadows active!");
                        currentPlayer.Gold += blessPrice; // Refund
                        break;
                    }
                    currentPlayer.GroggoShadowBlessingDex = 3;
                    currentPlayer.Dexterity += 3;
                    terminal.WriteLine("");
                    terminal.WriteLine("Groggo traces arcane symbols in the air...", "bright_magenta");
                    terminal.WriteLine("Shadows wrap around you like a cloak!", "white");
                    terminal.WriteLine("  Dexterity +3 (until next rest)", "bright_green");
                    break;

                default:
                    terminal.WriteLine("\"Come back when you need something.\"", "gray");
                    break;
            }

            await Task.Delay(2000);
        }

        private async Task VisitBeerHut()
        {
            terminal.WriteLine("");
            terminal.WriteLine("Bob hands you a frothy mug that smells vaguely of goblin sweat.", "white");
            long price = GetAdjustedPrice(10);
            terminal.WriteLine($"\"Just {price} gold for liquid courage!\" Bob grins.", "yellow");

            var ans = await terminal.GetInput("Drink? (Y/N): ");
            if (ans.ToUpper() != "Y") return;

            if (currentPlayer.Gold < price)
            {
                terminal.WriteLine("Bob laughs, \"Pay first, friend!\"", "red");
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
                    terminal.WriteLine("The warmth spreads through you. (+10 HP)", "bright_green");
                    break;
                case 2:
                    terminal.WriteLine("You feel brave! (Nothing happened, but you feel good.)", "bright_green");
                    break;
                case 3:
                    currentPlayer.Gold += 5; // Bob gives you some change back
                    terminal.WriteLine("Bob winks and slides some coins back. \"For a friend.\" (+5 gold)", "bright_green");
                    break;
                default:
                    terminal.WriteLine("It burns going down! You feel... something.", "yellow");
                    break;
            }
            await Task.Delay(1500);
        }

        private async Task VisitAlchemistHeaven()
        {
            terminal.WriteLine("");
            terminal.WriteLine("Shelves of bubbling concoctions line the walls.", "white");
            long price = GetAdjustedPrice(300);
            terminal.WriteLine($"A random experimental potion costs {price:N0} {GameConfig.MoneyType}.", "cyan");
            var ans = await terminal.GetInput("Buy? (Y/N): ");
            if (ans.ToUpper() != "Y") return;

            if (currentPlayer.Gold < price)
            {
                terminal.WriteLine("The alchemist shakes his head – no credit.", "red");
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
                        terminal.WriteLine("Your mind has been enhanced to its limit by alchemy.", "yellow");
                    }
                    else
                    {
                        currentPlayer.Intelligence += 2;
                        currentPlayer.AlchemistINTBoosts++;
                        terminal.WriteLine($"Your mind feels sharper! (+2 INT, {GameConfig.MaxAlchemistINTBoosts - currentPlayer.AlchemistINTBoosts} boosts remaining)", "bright_green");
                    }
                    break;
                case 2:
                    currentPlayer.HP = Math.Min(currentPlayer.MaxHP, currentPlayer.HP + 20);
                    terminal.WriteLine("A warm glow mends some wounds. (+20 HP)", "bright_green");
                    break;
                default:
                    currentPlayer.Darkness += 2;
                    terminal.WriteLine("The potion fizzles nastily… you feel uneasy. (+2 Darkness)", "yellow");
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
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            terminal.WriteLine("║                              THE SHADOWS                                     ║");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            terminal.SetColor("gray");
            terminal.WriteLine("You feel eyes on you. The shadows in the corner of the alley seem");
            terminal.WriteLine("to deepen, to solidify into something almost human...");
            terminal.WriteLine("");
            await Task.Delay(1500);

            terminal.SetColor("white");
            terminal.WriteLine("A figure emerges. You cannot see their face - it's wrapped in darkness");
            terminal.WriteLine("that seems to move and shift like smoke. Only their voice is clear.");
            terminal.WriteLine("");
            await Task.Delay(1500);

            terminal.SetColor("bright_magenta");
            terminal.WriteLine("\"You've been noticed,\" the voice whispers, neither male nor female.");
            terminal.WriteLine("\"Not everyone who walks these alleys catches our attention.\"");
            terminal.WriteLine("");
            await Task.Delay(1500);

            // Check if already in a faction
            if (factionSystem.PlayerFaction != null)
            {
                terminal.SetColor("gray");
                terminal.WriteLine("The faceless figure tilts its head, studying you.");
                terminal.WriteLine("");
                terminal.SetColor("bright_magenta");
                terminal.WriteLine($"\"You carry the mark of {FactionSystem.Factions[factionSystem.PlayerFaction.Value].Name}.\"");
                terminal.WriteLine("\"The Shadows do not share. We do not compete.\"");
                terminal.WriteLine("\"When you are ready to walk free of chains... find us.\"");
                terminal.WriteLine("");
                terminal.SetColor("gray");
                terminal.WriteLine("The figure dissolves back into the darkness.");
                await terminal.GetInputAsync("Press Enter to continue...");
                return;
            }

            terminal.SetColor("gray");
            terminal.WriteLine("The figure beckons you deeper into the shadows.");
            terminal.WriteLine("");
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("\"The Crown demands obedience. The Faith demands worship.\"");
            terminal.WriteLine("\"We demand nothing. We offer... opportunity.\"");
            terminal.WriteLine("");
            await Task.Delay(1500);

            terminal.SetColor("cyan");
            terminal.WriteLine("\"Information is currency. Secrets are power.\"");
            terminal.WriteLine("\"The Shadows know what the Crown hides. What The Faith fears.\"");
            terminal.WriteLine("\"We move unseen. We profit while others fight.\"");
            terminal.WriteLine("");
            await Task.Delay(1500);

            // Show faction benefits
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("═══ Benefits of The Shadows ═══");
            terminal.SetColor("white");
            terminal.WriteLine("• 20% better prices when selling items (fence bonus)");
            terminal.WriteLine("• Access to exclusive black market goods");
            terminal.WriteLine("• Friendly treatment from thieves and assassin NPCs");
            terminal.WriteLine("• Information network reveals hidden opportunities");
            terminal.WriteLine("");

            // Check requirements
            var (canJoin, reason) = factionSystem.CanJoinFaction(Faction.TheShadows, currentPlayer);

            if (!canJoin)
            {
                terminal.SetColor("red");
                terminal.WriteLine("═══ Requirements Not Met ═══");
                terminal.SetColor("yellow");
                terminal.WriteLine(reason);
                terminal.WriteLine("");
                terminal.SetColor("gray");
                terminal.WriteLine("The Shadows require:");
                terminal.WriteLine("• Level 10 or higher");
                terminal.WriteLine("• Darkness 200+ (or complete a favor for The Shadows)");
                terminal.WriteLine($"  Your Darkness: {currentPlayer.Darkness}");
                terminal.WriteLine("");
                terminal.SetColor("bright_magenta");
                terminal.WriteLine("\"You walk too much in the light,\" the voice observes.");
                terminal.WriteLine("\"Embrace the darkness. Do what must be done.\"");
                terminal.WriteLine("\"Or... prove yourself with a favor. We remember those who help us.\"");
                await terminal.GetInputAsync("Press Enter to continue...");
                return;
            }

            // Can join - offer the choice
            terminal.SetColor("bright_green");
            terminal.WriteLine("═══ Requirements Met ═══");
            terminal.SetColor("gray");
            terminal.WriteLine("The faceless figure nods - somehow you can tell, even without seeing.");
            terminal.WriteLine("");
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("\"You understand how the world works. Good.\"");
            terminal.WriteLine("\"Will you step into the shadows with us?\"");
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine("WARNING: Joining The Shadows will:");
            terminal.WriteLine("• Lock you out of The Crown and The Faith");
            terminal.WriteLine("• Decrease standing with rival factions by 100");
            terminal.WriteLine("");

            var choice = await terminal.GetInputAsync("Join The Shadows? (Y/N) ");

            if (choice.ToUpper() == "Y")
            {
                await PerformShadowsInitiation(factionSystem);
            }
            else
            {
                terminal.WriteLine("");
                terminal.SetColor("gray");
                terminal.WriteLine("The figure shrugs - or seems to.");
                terminal.SetColor("bright_magenta");
                terminal.WriteLine("\"The offer remains. The shadows are patient.\"");
                terminal.WriteLine("\"We will be watching.\"");
                terminal.WriteLine("");
                terminal.SetColor("gray");
                terminal.WriteLine("Between one blink and the next, the figure is gone.");
            }

            await terminal.GetInputAsync("Press Enter to continue...");
        }

        /// <summary>
        /// Perform the initiation ceremony to join The Shadows
        /// </summary>
        private async Task PerformShadowsInitiation(FactionSystem factionSystem)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            terminal.WriteLine("║                         THE INITIATION                                       ║");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            terminal.SetColor("gray");
            terminal.WriteLine("The figure leads you deeper into the darkness.");
            terminal.WriteLine("The alley twists and turns in ways that should be impossible.");
            terminal.WriteLine("You lose all sense of direction.");
            terminal.WriteLine("");
            await Task.Delay(1500);

            terminal.SetColor("white");
            terminal.WriteLine("Finally, you stand in a chamber of absolute darkness.");
            terminal.WriteLine("You cannot see the walls. You cannot see the floor.");
            terminal.WriteLine("You can only see the figure, outlined in shadow.");
            terminal.WriteLine("");
            await Task.Delay(1500);

            terminal.SetColor("bright_magenta");
            terminal.WriteLine("\"There is no oath,\" the voice says. \"No vow. No ritual.\"");
            terminal.WriteLine("");
            await Task.Delay(1000);

            terminal.WriteLine("\"There is only understanding.\"");
            terminal.WriteLine("");
            await Task.Delay(1000);

            terminal.SetColor("white");
            terminal.WriteLine("Something cold touches your hand. A coin, heavier than gold.");
            terminal.WriteLine("You cannot see its face, but you can feel the symbol carved into it.");
            terminal.WriteLine("");
            await Task.Delay(1500);

            terminal.SetColor("bright_magenta");
            terminal.WriteLine("\"Keep this. Show it when you need to.\"");
            terminal.WriteLine("\"Those who see it will know you walk with us.\"");
            terminal.WriteLine("");
            await Task.Delay(1500);

            // Actually join the faction
            factionSystem.JoinFaction(Faction.TheShadows, currentPlayer);

            terminal.SetColor("bright_green");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            terminal.WriteLine("║              YOU HAVE JOINED THE SHADOWS                                     ║");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            terminal.SetColor("bright_magenta");
            terminal.WriteLine("\"Welcome to the darkness. Use it well.\"");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("As a member of The Shadows, you will receive:");
            terminal.SetColor("bright_green");
            terminal.WriteLine("• 20% better sell prices at black markets");
            terminal.WriteLine("• Access to exclusive Shadows-only goods");
            terminal.WriteLine("• Recognition from thieves and assassins");
            terminal.WriteLine("• The information network works for you now");
            terminal.WriteLine("");

            terminal.SetColor("gray");
            terminal.WriteLine("When you blink, you find yourself back in the Dark Alley.");
            terminal.WriteLine("The figure is gone. But you can feel the shadow coin in your pocket.");

            // Generate news (anonymously - it's the Shadows after all)
            NewsSystem.Instance.Newsy(false, $"A new shadow moves through Dorashire...");

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
            terminal.WriteLine("You squint into the deepest shadows of the alley...");
            terminal.WriteLine("");
            await Task.Delay(1500);

            terminal.SetColor("gray");
            terminal.WriteLine("At first, you see nothing but darkness.");
            terminal.WriteLine("But as your eyes adjust, shapes begin to form...");
            terminal.WriteLine("");
            await Task.Delay(2000);

            terminal.SetColor("white");
            terminal.WriteLine("Letters. Ancient letters, carved into the very shadows themselves.");
            terminal.WriteLine("They seem to shift and dance, but you can just make out the words:");
            terminal.WriteLine("");
            await Task.Delay(1500);

            terminal.SetColor("bright_magenta");
            terminal.WriteLine("   \"The Wave returns to the Ocean.\"");
            terminal.WriteLine("   \"The Shadow remembers what the Light forgets.\"");
            terminal.WriteLine("   \"Jakob was here. 1993.\"");
            terminal.WriteLine("");
            await Task.Delay(2000);

            terminal.SetColor("bright_yellow");
            terminal.WriteLine("You have discovered something hidden in this dark place!");
            terminal.WriteLine("");

            // Unlock the secret achievement
            AchievementSystem.TryUnlock(currentPlayer, "easter_egg_1");
            await AchievementSystem.ShowPendingNotifications(terminal, currentPlayer);

            terminal.SetColor("gray");
            terminal.WriteLine("The shadows shift, and the words fade from view.");
            terminal.WriteLine("But you know they are still there, waiting for another");
            terminal.WriteLine("curious soul to find them in the darkness.");
            terminal.WriteLine("");

            await terminal.GetInputAsync("Press Enter to continue...");
        }

        #endregion

        #region Black Market and Informant

        private async Task VisitBlackMarket()
        {
            if (FactionSystem.Instance?.HasBlackMarketAccess() != true)
            {
                terminal.SetColor("red");
                terminal.WriteLine("\n  You dont have access to the Black Market.");
                terminal.WriteLine("  Join the Shadows first.");
                await Task.Delay(2000);
                return;
            }

            terminal.ClearScreen();
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
            { const string t = "THE BLACK MARKET"; int l = (66 - t.Length) / 2, r = 66 - t.Length - l; terminal.WriteLine($"║{new string(' ', l)}{t}{new string(' ', r)}║"); }
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            terminal.SetColor("gray");
            terminal.WriteLine("  A hooded figure beckons you behind a false wall.");
            terminal.WriteLine("  Crates of contraband line the narrow room.");
            terminal.WriteLine("");

            float rankDiscount = FactionSystem.Instance?.GetBlackMarketDiscount() ?? 0f;
            int level = currentPlayer.Level;

            long forgedPapersPrice = (long)((1000 + level * 100) * (1.0f - rankDiscount));
            long poisonVialPrice = (long)((300 + level * 20) * (1.0f - rankDiscount));
            long smokeBombPrice = (long)((500 + level * 30) * (1.0f - rankDiscount));

            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("1");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine($"Forged Papers     {forgedPapersPrice,8:N0}g  — Reduce Darkness by 100");
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("2");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor(currentPlayer.PoisonVials >= GameConfig.MaxPoisonVials ? "gray" : "white");
            terminal.WriteLine($"Poison Vials (x3) {poisonVialPrice,8:N0}g  — Coat blade in combat [B]");
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("3");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor(currentPlayer.SmokeBombs >= 3 ? "gray" : "white");
            terminal.WriteLine($"Smoke Bomb        {smokeBombPrice,8:N0}g  — Guaranteed escape (max 3)");
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("0");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine($"Leave");
            terminal.WriteLine("");

            if (rankDiscount > 0)
            {
                terminal.SetColor("bright_magenta");
                terminal.WriteLine($"  Rank discount: {rankDiscount * 100:F0}% off");
                terminal.WriteLine("");
            }

            terminal.SetColor("yellow");
            terminal.WriteLine($"  Gold: {currentPlayer.Gold:N0}");
            terminal.WriteLine("");

            var input = await terminal.GetInput("  Purchase? ");
            switch (input.Trim())
            {
                case "1": // Forged Papers
                    if (currentPlayer.Gold < forgedPapersPrice)
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine("  Not enough gold.");
                    }
                    else
                    {
                        currentPlayer.Gold -= forgedPapersPrice;
                        long reduction = Math.Min(100, currentPlayer.Darkness);
                        currentPlayer.Darkness -= (int)reduction;
                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"  The forger hands you new papers. Darkness reduced by {reduction}.");
                        currentPlayer.Statistics?.RecordGoldSpent(forgedPapersPrice);
                    }
                    break;

                case "2": // Poison Vials
                    if (currentPlayer.PoisonVials >= GameConfig.MaxPoisonVials)
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine($"  You can't carry any more vials. ({currentPlayer.PoisonVials}/{GameConfig.MaxPoisonVials})");
                    }
                    else if (currentPlayer.Gold < poisonVialPrice)
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine("  Not enough gold.");
                    }
                    else
                    {
                        currentPlayer.Gold -= poisonVialPrice;
                        int vialsToAdd = 3;
                        currentPlayer.PoisonVials = Math.Min(GameConfig.MaxPoisonVials, currentPlayer.PoisonVials + vialsToAdd);
                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"  You acquire {vialsToAdd} poison vials. (Total: {currentPlayer.PoisonVials})");
                        terminal.SetColor("cyan");
                        terminal.WriteLine("  Use [B] Coat Blade during combat to apply a poison.");
                        currentPlayer.Statistics?.RecordGoldSpent(poisonVialPrice);
                    }
                    break;

                case "3": // Smoke Bomb
                    if (currentPlayer.SmokeBombs >= 3)
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine("  You cant carry any more. Three is the limit.");
                    }
                    else if (currentPlayer.Gold < smokeBombPrice)
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine("  Not enough gold.");
                    }
                    else
                    {
                        currentPlayer.Gold -= smokeBombPrice;
                        currentPlayer.SmokeBombs++;
                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"  Smoke bomb acquired. You now carry {currentPlayer.SmokeBombs}.");
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
                terminal.WriteLine("\n  You dont have access to the information network.");
                terminal.WriteLine("  Join the Shadows first.");
                await Task.Delay(2000);
                return;
            }

            terminal.ClearScreen();
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
            { const string t = "THE INFORMANT"; int l = (66 - t.Length) / 2, r = 66 - t.Length - l; terminal.WriteLine($"║{new string(' ', l)}{t}{new string(' ', r)}║"); }
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            terminal.SetColor("gray");
            terminal.WriteLine("  A wiry figure in a dark corner taps the table impatiently.");
            terminal.SetColor("yellow");
            terminal.WriteLine($"  \"Intel costs {GameConfig.InformantCost} gold. Take it or leave it.\"");
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine($"  Gold: {currentPlayer.Gold:N0}");
            terminal.WriteLine("");

            var input = await terminal.GetInput("  Pay for intel? (Y/N): ");
            if (input.Trim().ToUpper() != "Y")
                return;

            if (currentPlayer.Gold < GameConfig.InformantCost)
            {
                terminal.SetColor("red");
                terminal.WriteLine("  \"Come back when you can afford it.\"");
                await Task.Delay(2000);
                return;
            }

            currentPlayer.Gold -= GameConfig.InformantCost;
            currentPlayer.Statistics?.RecordGoldSpent(GameConfig.InformantCost);

            var activeNPCs = NPCSpawnSystem.Instance?.ActiveNPCs;
            if (activeNPCs == null || activeNPCs.Count == 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine("  \"Nothing to report. Town's dead quiet.\"");
                await Task.Delay(2000);
                return;
            }

            // Top 5 wealthiest NPCs
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("\n  ═══ Wealthiest Marks ═══");
            var wealthiest = activeNPCs
                .Where(n => !n.IsDead && n.Gold > 0)
                .OrderByDescending(n => n.Gold)
                .Take(5)
                .ToList();

            if (wealthiest.Count == 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine("  Nobody's carrying much right now.");
            }
            else
            {
                foreach (var npc in wealthiest)
                {
                    terminal.SetColor("white");
                    terminal.WriteLine($"  {npc.Name2,-20} {npc.Gold,8:N0}g  Lvl {npc.Level}");
                }
            }

            // Wanted NPCs (high Darkness)
            terminal.SetColor("bright_red");
            terminal.WriteLine("\n  ═══ Wanted (Darkness > 200) ═══");
            var wanted = activeNPCs
                .Where(n => !n.IsDead && n.Darkness > 200)
                .OrderByDescending(n => n.Darkness)
                .Take(5)
                .ToList();

            if (wanted.Count == 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine("  Nobody on the wanted list right now.");
            }
            else
            {
                foreach (var npc in wanted)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine($"  {npc.Name2,-20} Darkness: {npc.Darkness}  Lvl {npc.Level}");
                }
            }

            // Active quest targets
            var activeQuests = QuestSystem.GetActiveQuestsForPlayer(currentPlayer.Name2);
            if (activeQuests?.Count > 0)
            {
                terminal.SetColor("bright_cyan");
                terminal.WriteLine("\n  ═══ Your Active Targets ═══");
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
            terminal.SetColor("dark_red");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
            { const string t = "THE GAMBLING DEN"; int l = (66 - t.Length) / 2, r = 66 - t.Length - l; terminal.WriteLine($"║{new string(' ', l)}{t}{new string(' ', r)}║"); }
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            terminal.SetColor("gray");
            terminal.WriteLine("Smoke curls through the low-ceilinged room. A scarred half-orc");
            terminal.WriteLine("runs the tables while nervous eyes watch from every corner.");
            terminal.WriteLine("");

            if (currentPlayer.GamblingRoundsToday >= 10)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("The half-orc waves you off. \"You've had enough action for today.\"");
                terminal.SetColor("gray");
                terminal.WriteLine("(10/10 daily rounds used)");
                await Task.Delay(2000);
                return;
            }

            terminal.SetColor("yellow");
            terminal.WriteLine($"Rounds remaining today: {10 - currentPlayer.GamblingRoundsToday}/10");
            terminal.WriteLine($"Gold on hand: {currentPlayer.Gold:N0}");
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("1");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("cyan");
            terminal.WriteLine(" Loaded Dice       - Guess over/under 7. Win = 1.8x bet");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("2");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("cyan");
            terminal.WriteLine(" Three Card Monte  - Pick the right card. Win = 2.5x bet");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("3");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("cyan");
            terminal.WriteLine(" Skull & Bones     - Choose your risk. 2x/3x/5x payout");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("0");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("cyan");
            terminal.WriteLine(" Leave");
            terminal.WriteLine("");

            var choice = await terminal.GetInput("Your game: ");
            if (choice != "1" && choice != "2" && choice != "3") return;

            // Get bet amount
            long minBet = 10;
            long maxBet = Math.Max(minBet, currentPlayer.Gold / 10);
            if (currentPlayer.Gold < minBet)
            {
                terminal.SetColor("red");
                terminal.WriteLine("\"You need at least 10 gold to play, rat.\"");
                await Task.Delay(1500);
                return;
            }

            long bet = 0;
            while (true)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"Place your bet (min {minBet}, max {maxBet:N0}, 0 to leave): ");
                var betInput = await terminal.GetInput("> ");
                if (!long.TryParse(betInput, out bet) || bet == 0)
                    return;
                if (bet < minBet)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine($"\"Minimum wager is {minBet} gold.\"");
                    continue;
                }
                if (bet > maxBet)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine($"\"Max wager is {maxBet:N0} gold.\"");
                    continue;
                }
                if (bet > currentPlayer.Gold)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine("\"You ain't got that kind of coin.\"");
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
                terminal.WriteLine($"You pocket {winnings:N0} gold!");
                currentPlayer.Statistics?.RecordGamblingWin(winnings - bet);
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine($"You lost {bet:N0} gold. The house always wins... eventually.");
                currentPlayer.Statistics?.RecordGamblingLoss(bet);
            }

            // Check achievement
            if ((currentPlayer.Statistics?.TotalGoldFromGambling ?? 0) >= 1000)
            {
                AchievementSystem.TryUnlock(currentPlayer, "dark_alley_gambler");
                await AchievementSystem.ShowPendingNotifications(terminal, currentPlayer);
            }

            terminal.SetColor("gray");
            terminal.WriteLine($"Rounds remaining: {10 - currentPlayer.GamblingRoundsToday}/10");
            await Task.Delay(2000);
        }

        private async Task<(bool won, long winnings)> PlayLoadedDice(long bet)
        {
            bool won = false;
            long winnings = 0;

            terminal.SetColor("white");
            terminal.WriteLine("");
            terminal.WriteLine("The half-orc rattles two dice in a leather cup.");
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("1");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("yellow");
            terminal.Write(" Over 7    ");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("2");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("yellow");
            terminal.WriteLine(" Under 7");
            var guess = await terminal.GetInput("> ");
            bool guessOver = guess != "2"; // Default to over

            await Task.Delay(1000);
            int die1 = Random.Shared.Next(1, 7);
            int die2 = Random.Shared.Next(1, 7);
            int total = die1 + die2;

            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"The dice tumble... {die1} + {die2} = {total}!");
            await Task.Delay(500);

            // ~45% base win + CHA/200 bonus
            float chaBonus = currentPlayer.Charisma / 200f;
            float baseChance = 0.45f + chaBonus;

            // Determine actual outcome based on chance (not the dice -- the dice are loaded!)
            float roll = (float)new Random().NextDouble();
            if (roll < baseChance)
            {
                // Player wins - adjust displayed result to match their guess
                if ((guessOver && total <= 7) || (!guessOver && total >= 7))
                {
                    total = guessOver ? Random.Shared.Next(8, 13) : Random.Shared.Next(2, 7);
                    terminal.SetColor("bright_cyan");
                    terminal.WriteLine($"Wait... actually it's {total}!");
                }
                won = true;
                winnings = (long)(bet * 1.8);
                terminal.SetColor("bright_green");
                terminal.WriteLine($"You called it! The dice favor you.");
            }
            else
            {
                if ((guessOver && total > 7) || (!guessOver && total < 7))
                {
                    total = guessOver ? Random.Shared.Next(2, 8) : Random.Shared.Next(7, 13);
                    terminal.SetColor("bright_cyan");
                    terminal.WriteLine($"Wait... actually it's {total}!");
                }
                terminal.SetColor("red");
                terminal.WriteLine("The dice betray you.");
            }

            return (won, winnings);
        }

        private async Task<(bool won, long winnings)> PlayThreeCardMonte(long bet)
        {
            bool won = false;
            long winnings = 0;

            terminal.SetColor("white");
            terminal.WriteLine("");
            terminal.WriteLine("Three cards face-down. One is the Queen of Shadows.");
            terminal.WriteLine("The dealer's hands blur as he shuffles them.");
            terminal.WriteLine("");
            await Task.Delay(1000);

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("1");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("yellow");
            terminal.Write(" Left card    ");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("2");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("yellow");
            terminal.Write(" Middle card    ");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("3");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("yellow");
            terminal.WriteLine(" Right card");
            var pick = await terminal.GetInput("> ");

            await Task.Delay(1500);
            terminal.SetColor("white");
            terminal.WriteLine("The dealer flips the cards...");
            await Task.Delay(500);

            // 33% base + DEX/500 bonus
            float dexBonus = currentPlayer.Dexterity / 500f;
            float chance = 0.33f + dexBonus;
            float roll = (float)new Random().NextDouble();

            int queenPosition = Random.Shared.Next(1, 4);
            if (roll < chance)
            {
                queenPosition = int.TryParse(pick, out int p) && p >= 1 && p <= 3 ? p : 1;
                won = true;
                winnings = (long)(bet * 2.5);
                terminal.SetColor("bright_green");
                terminal.WriteLine($"The Queen smiles at you from card {queenPosition}. Sharp eyes!");
            }
            else
            {
                // Ensure queen is NOT where player picked
                if (int.TryParse(pick, out int pp) && pp >= 1 && pp <= 3)
                    queenPosition = pp == 1 ? 2 : pp == 2 ? 3 : 1;
                terminal.SetColor("red");
                terminal.WriteLine($"The Queen was hiding on card {queenPosition}. Better luck next time.");
            }

            return (won, winnings);
        }

        private async Task<(bool won, long winnings)> PlaySkullAndBones(long bet)
        {
            bool won = false;
            long winnings = 0;

            terminal.SetColor("white");
            terminal.WriteLine("");
            terminal.WriteLine("A grinning skull sits on the table. Choose your risk.");
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("1");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("yellow");
            terminal.WriteLine(" Safe bet   - 2x payout (45% win)");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("2");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("yellow");
            terminal.WriteLine(" Risky bet  - 3x payout (30% win)");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("3");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("yellow");
            terminal.WriteLine(" All or nothing - 5x payout (15% win)");
            var riskChoice = await terminal.GetInput("> ");

            float winChance;
            float multiplier;
            switch (riskChoice)
            {
                case "2":
                    winChance = 0.30f;
                    multiplier = 3.0f;
                    break;
                case "3":
                    winChance = 0.15f;
                    multiplier = 5.0f;
                    break;
                default:
                    winChance = 0.45f;
                    multiplier = 2.0f;
                    break;
            }

            await Task.Delay(1500);
            terminal.SetColor("white");
            terminal.WriteLine("The skull's jaw drops open...");
            await Task.Delay(1000);

            float roll = (float)new Random().NextDouble();
            if (roll < winChance)
            {
                won = true;
                winnings = (long)(bet * multiplier);
                terminal.SetColor("bright_green");
                terminal.WriteLine("A golden tooth gleams inside! Fortune favors the bold!");
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine("Empty. The skull laughs silently at your misfortune.");
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
            terminal.SetColor("dark_red");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
            { const string t = "PICKPOCKETING"; int l = (66 - t.Length) / 2, r = 66 - t.Length - l; terminal.WriteLine($"║{new string(' ', l)}{t}{new string(' ', r)}║"); }
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            if (currentPlayer.Thiefs <= 0)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("You've used up all your thievery attempts for the day.");
                terminal.SetColor("gray");
                terminal.WriteLine("The streets are too hot right now. Come back tomorrow.");
                await Task.Delay(2000);
                return;
            }

            terminal.SetColor("gray");
            terminal.WriteLine("You pull your hood low and scan the crowd for marks...");
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine($"Thievery attempts remaining: {currentPlayer.Thiefs}");
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
                terminal.WriteLine("The streets are empty. No marks to be found.");
                await Task.Delay(2000);
                return;
            }

            terminal.SetColor("cyan");
            terminal.WriteLine("Potential marks:");
            terminal.WriteLine("");
            for (int i = 0; i < allNPCs.Count; i++)
            {
                var npc = allNPCs[i];
                string goldHint = npc.Gold > 1000 ? "heavy purse" : npc.Gold > 200 ? "decent coin" : "light pockets";
                terminal.SetColor("darkgray");
                terminal.Write("  [");
                terminal.SetColor("bright_yellow");
                terminal.Write($"{i + 1}");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("yellow");
                terminal.Write($"{npc.Name2,-20}");
                terminal.SetColor("gray");
                terminal.Write($" Lvl {npc.Level,-4}");
                terminal.SetColor(npc.Gold > 1000 ? "bright_green" : npc.Gold > 200 ? "yellow" : "gray");
                terminal.WriteLine($" ({goldHint})");
            }
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("0");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("yellow");
            terminal.WriteLine(" Changed my mind");
            terminal.WriteLine("");

            var choice = await terminal.GetInput("Target: ");
            if (!int.TryParse(choice, out int sel) || sel < 1 || sel > allNPCs.Count)
            {
                terminal.SetColor("gray");
                terminal.WriteLine("You slip back into the shadows.");
                await Task.Delay(1000);
                return;
            }

            var target = allNPCs[sel - 1];
            currentPlayer.Thiefs--;

            terminal.SetColor("gray");
            terminal.WriteLine($"You approach {target.Name2} from behind...");
            await Task.Delay(1500);

            // DEX check
            float chance = Math.Min(0.75f, 0.40f + currentPlayer.Dexterity * 0.005f +
                (currentPlayer.Class == CharacterClass.Assassin ? 0.15f : 0f));

            float roll = (float)new Random().NextDouble();

            if (roll < 0.10f)
            {
                // Critical fail — guards catch you
                terminal.SetColor("bright_red");
                terminal.WriteLine("\"STOP! THIEF!\"");
                terminal.WriteLine("");
                terminal.WriteLine("Guards materialize from every alley. There's no escape.");
                terminal.SetColor("red");
                terminal.WriteLine("You are dragged to the prison!");
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
                currentPlayer.Darkness += 3;
                currentPlayer.DarkAlleyReputation = Math.Min(1000, currentPlayer.DarkAlleyReputation + 2);

                terminal.SetColor("bright_green");
                terminal.WriteLine($"Your fingers find the purse... got it!");
                terminal.SetColor("yellow");
                terminal.WriteLine($"Stolen: {stolen:N0} gold from {target.Name2}");
                terminal.SetColor("magenta");
                terminal.WriteLine("+3 Darkness, +2 Reputation");

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
                terminal.WriteLine($"{target.Name2} catches your hand!");
                terminal.SetColor("white");
                terminal.WriteLine($"\"You think you can rob ME?!\"");
                terminal.WriteLine("");
                await Task.Delay(1500);

                currentPlayer.Statistics?.RecordPickpocketAttempt(false);

                // Combat with NPC
                var combatEngine = new CombatEngine(terminal);
                var result = await combatEngine.PlayerVsPlayer(currentPlayer, target);

                if (!currentPlayer.IsAlive)
                {
                    terminal.SetColor("bright_red");
                    terminal.WriteLine($"{target.Name2} leaves you bleeding in the gutter.");
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
            terminal.SetColor("dark_red");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
            { const string t = "THE PIT"; int l = (66 - t.Length) / 2, r = 66 - t.Length - l; terminal.WriteLine($"║{new string(' ', l)}{t}{new string(' ', r)}║"); }
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            terminal.SetColor("gray");
            terminal.WriteLine("A roaring crowd surrounds a blood-stained fighting pit.");
            terminal.WriteLine("The air reeks of sweat, ale, and desperation.");
            terminal.WriteLine("");

            if (currentPlayer.PitFightsToday >= 3)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("The pit boss shakes his head. \"Three fights is the limit. Rules are rules.\"");
                terminal.SetColor("gray");
                terminal.WriteLine("(3/3 daily fights used)");
                await Task.Delay(2000);
                return;
            }

            terminal.SetColor("yellow");
            terminal.WriteLine($"Fights remaining today: {3 - currentPlayer.PitFightsToday}/3");
            terminal.SetColor("gray");
            terminal.WriteLine("All pit fights are bare-knuckle — no armor allowed!");
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("1");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("cyan");
            terminal.WriteLine(" Fight a monster  (generated at your level, 2x gold)");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("2");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("cyan");
            terminal.WriteLine(" Fight an NPC     (winner takes 20% of loser's gold)");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("0");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("cyan");
            terminal.WriteLine(" Leave the pit");
            terminal.WriteLine("");

            var choice = await terminal.GetInput("Your choice: ");

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
            terminal.WriteLine($"A {monster.Name} is released into the pit!");
            terminal.SetColor("gray");
            terminal.WriteLine($"Level {monster.Level} — HP: {monster.HP}");
            terminal.WriteLine("");
            await Task.Delay(1500);

            // Save armor, zero it for bare-knuckle fight
            long savedArmPow = currentPlayer.ArmPow;
            try
            {
                currentPlayer.ArmPow = 0;

                var combatEngine = new CombatEngine(terminal);
                var result = await combatEngine.PlayerVsMonster(currentPlayer, monster, null, false);

                currentPlayer.PitFightsToday++;
                currentPlayer.DarkAlleyReputation = Math.Min(1000, currentPlayer.DarkAlleyReputation + 5);
                currentPlayer.Darkness += 2;

                if (result.Outcome == CombatOutcome.Victory)
                {
                    terminal.SetColor("bright_green");
                    terminal.WriteLine("The crowd roars! You are victorious!");

                    // Handle spectator bet win
                    if (spectatorBet > 0)
                    {
                        long betWinnings = (long)(spectatorBet * betMultiplier);
                        currentPlayer.Gold += betWinnings;
                        terminal.SetColor("yellow");
                        terminal.WriteLine($"Spectator bet pays out: +{betWinnings:N0} gold!");
                    }

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
                    terminal.WriteLine("You fall to the dirt. The crowd jeers.");

                    if (spectatorBet > 0)
                    {
                        terminal.SetColor("dark_red");
                        terminal.WriteLine($"Lost your spectator bet of {spectatorBet:N0} gold.");
                    }

                    currentPlayer.Statistics?.RecordPitFight(false);
                }
            }
            finally
            {
                currentPlayer.ArmPow = savedArmPow;
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
                terminal.WriteLine("No fighters are stepping up to the pit tonight.");
                await Task.Delay(2000);
                return;
            }

            terminal.SetColor("cyan");
            terminal.WriteLine("Fighters in the pit tonight:");
            terminal.WriteLine("");
            for (int i = 0; i < candidates.Count; i++)
            {
                var npc = candidates[i];
                terminal.SetColor("darkgray");
                terminal.Write("  [");
                terminal.SetColor("bright_yellow");
                terminal.Write($"{i + 1}");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("yellow");
                terminal.Write($"{npc.Name2,-20}");
                terminal.SetColor("gray");
                terminal.Write($" Lvl {npc.Level,-4}");
                terminal.SetColor("cyan");
                terminal.WriteLine($" Gold: {npc.Gold:N0}");
            }
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("0");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("yellow");
            terminal.WriteLine(" Back out");
            terminal.WriteLine("");

            var pick = await terminal.GetInput("Challenge: ");
            if (!int.TryParse(pick, out int sel) || sel < 1 || sel > candidates.Count)
                return;

            var opponent = candidates[sel - 1];

            // Spectator bet
            var (spectatorBet, betMultiplier) = await OfferSpectatorBet();

            terminal.SetColor("bright_red");
            terminal.WriteLine("");
            terminal.WriteLine($"You square off against {opponent.Name2}!");
            terminal.SetColor("gray");
            terminal.WriteLine("No armor. No mercy.");
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
                currentPlayer.DarkAlleyReputation = Math.Min(1000, currentPlayer.DarkAlleyReputation + 5);
                currentPlayer.Darkness += 2;

                if (result.Outcome == CombatOutcome.Victory)
                {
                    long goldTaken = (long)(opponent.Gold * 0.20);
                    opponent.Gold -= goldTaken;
                    currentPlayer.Gold += goldTaken;

                    terminal.SetColor("bright_green");
                    terminal.WriteLine($"The crowd goes wild! You claim {goldTaken:N0} gold from {opponent.Name2}!");

                    if (spectatorBet > 0)
                    {
                        long betWinnings = (long)(spectatorBet * betMultiplier);
                        currentPlayer.Gold += betWinnings;
                        terminal.SetColor("yellow");
                        terminal.WriteLine($"Spectator bet pays out: +{betWinnings:N0} gold!");
                    }

                    currentPlayer.Statistics?.RecordPitFight(true, goldTaken);

                    if ((currentPlayer.Statistics?.TotalPitFightsWon ?? 0) >= 10)
                    {
                        AchievementSystem.TryUnlock(currentPlayer, "dark_alley_pit_champion");
                        await AchievementSystem.ShowPendingNotifications(terminal, currentPlayer);
                    }
                }
                else
                {
                    long goldLost = (long)(currentPlayer.Gold * 0.20);
                    currentPlayer.Gold -= goldLost;
                    opponent.Gold += goldLost;

                    terminal.SetColor("red");
                    terminal.WriteLine($"You stumble out of the pit, {goldLost:N0} gold poorer.");

                    if (spectatorBet > 0)
                    {
                        terminal.SetColor("dark_red");
                        terminal.WriteLine($"Lost your spectator bet of {spectatorBet:N0} gold.");
                    }

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

            terminal.SetColor("yellow");
            terminal.WriteLine("Place a side bet on the fight?");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("1");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("yellow");
            terminal.Write(" 1.5x (safe)  ");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("2");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("yellow");
            terminal.Write(" 2x (risky)  ");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("3");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("yellow");
            terminal.Write(" 3x (reckless)  ");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("0");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("yellow");
            terminal.WriteLine(" No bet");
            var betChoice = await terminal.GetInput("> ");

            if (betChoice != "1" && betChoice != "2" && betChoice != "3") return (0, 1.0f);

            multiplier = betChoice == "1" ? 1.5f : betChoice == "2" ? 2.0f : 3.0f;

            terminal.SetColor("yellow");
            terminal.WriteLine($"How much to wager? (max {currentPlayer.Gold:N0}): ");
            var amountStr = await terminal.GetInput("> ");
            if (long.TryParse(amountStr, out long amt) && amt > 0 && amt <= currentPlayer.Gold)
            {
                betAmount = amt;
                currentPlayer.Gold -= amt;
                terminal.SetColor("magenta");
                terminal.WriteLine($"Bet placed: {amt:N0} gold at {multiplier}x");
            }

            return (betAmount, multiplier);
        }

        /// <summary>
        /// Loan Shark - Borrow gold with a 5-day repayment window.
        /// Overdue loans trigger enforcer encounters.
        /// </summary>
        private async Task VisitLoanShark()
        {
            terminal.ClearScreen();
            terminal.SetColor("dark_red");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
            { const string t = "THE LOAN SHARK"; int l = (66 - t.Length) / 2, r = 66 - t.Length - l; terminal.WriteLine($"║{new string(' ', l)}{t}{new string(' ', r)}║"); }
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            terminal.SetColor("gray");
            terminal.WriteLine("A gaunt man with golden teeth sits behind a reinforced desk.");
            terminal.WriteLine("Two massive enforcers flank the doorway.");
            terminal.WriteLine("");

            if (currentPlayer.LoanAmount > 0)
            {
                // Active loan — show balance and repayment options
                long totalOwed = currentPlayer.LoanAmount + currentPlayer.LoanInterestAccrued;
                terminal.SetColor("yellow");
                terminal.WriteLine("═══ Outstanding Loan ═══");
                terminal.SetColor("white");
                terminal.WriteLine($"  Principal:  {currentPlayer.LoanAmount:N0} gold");
                terminal.SetColor("red");
                terminal.WriteLine($"  Interest:   {currentPlayer.LoanInterestAccrued:N0} gold");
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"  Total owed: {totalOwed:N0} gold");
                terminal.SetColor(currentPlayer.LoanDaysRemaining > 0 ? "yellow" : "bright_red");
                terminal.WriteLine($"  Days remaining: {currentPlayer.LoanDaysRemaining}");
                if (currentPlayer.LoanDaysRemaining <= 0)
                {
                    terminal.SetColor("bright_red");
                    terminal.WriteLine("  ** OVERDUE — Enforcers are looking for you! **");
                }
                terminal.WriteLine("");

                terminal.SetColor("yellow");
                terminal.WriteLine($"Gold on hand: {currentPlayer.Gold:N0}");
                terminal.WriteLine("");

                terminal.SetColor("darkgray");
                terminal.Write("[");
                terminal.SetColor("bright_yellow");
                terminal.Write("1");
                terminal.SetColor("darkgray");
                terminal.Write("]");
                terminal.SetColor("cyan");
                terminal.WriteLine($" Repay in full ({totalOwed:N0} gold)");
                terminal.SetColor("darkgray");
                terminal.Write("[");
                terminal.SetColor("bright_yellow");
                terminal.Write("2");
                terminal.SetColor("darkgray");
                terminal.Write("]");
                terminal.SetColor("cyan");
                terminal.WriteLine(" Make partial payment");
                terminal.SetColor("darkgray");
                terminal.Write("[");
                terminal.SetColor("bright_yellow");
                terminal.Write("0");
                terminal.SetColor("darkgray");
                terminal.Write("]");
                terminal.SetColor("cyan");
                terminal.WriteLine(" Leave");
                terminal.WriteLine("");

                var choice = await terminal.GetInput("Your choice: ");

                if (choice == "1")
                {
                    if (currentPlayer.Gold >= totalOwed)
                    {
                        currentPlayer.Gold -= totalOwed;
                        currentPlayer.LoanAmount = 0;
                        currentPlayer.LoanDaysRemaining = 0;
                        currentPlayer.LoanInterestAccrued = 0;
                        currentPlayer.DarkAlleyReputation = Math.Min(1000, currentPlayer.DarkAlleyReputation + 3);

                        terminal.SetColor("bright_green");
                        terminal.WriteLine("The loan shark counts the gold and nods.");
                        terminal.WriteLine("\"Pleasure doing business. You're clean.\"");
                        currentPlayer.Statistics?.RecordGoldSpent(totalOwed);

                        AchievementSystem.TryUnlock(currentPlayer, "dark_alley_debt_free");
                        await AchievementSystem.ShowPendingNotifications(terminal, currentPlayer);
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine("\"You don't have enough. Don't waste my time.\"");
                    }
                }
                else if (choice == "2")
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"How much? (max {Math.Min(currentPlayer.Gold, totalOwed):N0}): ");
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
                            terminal.SetColor("bright_green");
                            terminal.WriteLine("Debt fully paid! \"You're clean.\"");

                            AchievementSystem.TryUnlock(currentPlayer, "dark_alley_debt_free");
                            await AchievementSystem.ShowPendingNotifications(terminal, currentPlayer);
                        }
                        else
                        {
                            terminal.SetColor("yellow");
                            terminal.WriteLine($"Payment accepted. Remaining: {currentPlayer.LoanAmount + currentPlayer.LoanInterestAccrued:N0} gold.");
                        }
                    }
                    else
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine("\"Changed your mind? Don't keep me waiting too long.\"");
                    }
                }
            }
            else
            {
                // No active loan — offer new loan
                long maxLoan = currentPlayer.Level * 500;
                terminal.SetColor("white");
                terminal.WriteLine("The loan shark leans forward with a predatory grin.");
                terminal.SetColor("yellow");
                terminal.WriteLine($"\"Need some coin? I can offer up to {maxLoan:N0} gold.\"");
                terminal.SetColor("gray");
                terminal.WriteLine("\"Five days to pay it back. Interest accrues daily.\"");
                terminal.SetColor("red");
                terminal.WriteLine("\"Miss the deadline... and my boys will collect in other ways.\"");
                terminal.WriteLine("");

                terminal.SetColor("darkgray");
                terminal.Write("[");
                terminal.SetColor("bright_yellow");
                terminal.Write("1");
                terminal.SetColor("darkgray");
                terminal.Write("]");
                terminal.SetColor("cyan");
                terminal.WriteLine(" Take out a loan");
                terminal.SetColor("darkgray");
                terminal.Write("[");
                terminal.SetColor("bright_yellow");
                terminal.Write("0");
                terminal.SetColor("darkgray");
                terminal.Write("]");
                terminal.SetColor("cyan");
                terminal.WriteLine(" Leave");
                terminal.WriteLine("");

                var choice = await terminal.GetInput("Your choice: ");
                if (choice == "1")
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"How much? (max {maxLoan:N0}): ");
                    var amtStr = await terminal.GetInput("> ");
                    if (long.TryParse(amtStr, out long amount) && amount > 0 && amount <= maxLoan)
                    {
                        currentPlayer.LoanAmount = amount;
                        currentPlayer.LoanDaysRemaining = 5;
                        currentPlayer.LoanInterestAccrued = 0;
                        currentPlayer.Gold += amount;

                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"The loan shark counts out {amount:N0} gold.");
                        terminal.SetColor("red");
                        terminal.WriteLine($"\"Five days. Don't forget.\"");
                        terminal.SetColor("gray");
                        terminal.WriteLine("(Interest accrues 10% daily. Repay at the Loan Shark.)");
                    }
                    else
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine("\"Wasting my time? Get out.\"");
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
            terminal.SetColor("dark_red");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
            { const string t = "FENCE STOLEN GOODS"; int l = (66 - t.Length) / 2, r = 66 - t.Length - l; terminal.WriteLine($"║{new string(' ', l)}{t}{new string(' ', r)}║"); }
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            terminal.SetColor("gray");
            terminal.WriteLine("A weasel-faced man examines goods through a cracked magnifying glass.");
            terminal.WriteLine("\"I buy anything. No questions asked.\"");
            terminal.WriteLine("");

            bool isShadows = FactionSystem.Instance?.PlayerFaction == Faction.TheShadows;
            float fenceRate = isShadows ? 0.80f : 0.70f;
            if (isShadows)
            {
                terminal.SetColor("bright_magenta");
                terminal.WriteLine("(Shadows member: 80% value instead of 70%)");
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
                terminal.WriteLine("\"You got nothing worth my time. Beat it.\"");
                await Task.Delay(2000);
                return;
            }

            terminal.SetColor("cyan");
            terminal.WriteLine("Items you can fence:");
            terminal.WriteLine("");
            for (int i = 0; i < itemsForSale.Count; i++)
            {
                var (_, name, value, cursed) = itemsForSale[i];
                terminal.SetColor("darkgray");
                terminal.Write("  [");
                terminal.SetColor("bright_yellow");
                terminal.Write($"{i + 1}");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor(cursed ? "red" : "white");
                terminal.Write($"{name,-30}");
                terminal.SetColor("yellow");
                terminal.Write($" {value:N0}g");
                if (cursed)
                {
                    terminal.SetColor("red");
                    terminal.Write("  [CURSED]");
                }
                terminal.WriteLine("");
            }
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("0");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("yellow");
            terminal.WriteLine(" Leave");
            terminal.WriteLine("");

            var choice = await terminal.GetInput("Sell which item? ");
            if (!int.TryParse(choice, out int sel) || sel < 1 || sel > itemsForSale.Count)
                return;

            var selected = itemsForSale[sel - 1];
            terminal.SetColor("yellow");
            terminal.WriteLine($"Sell {selected.name} for {selected.value:N0} gold? (Y/N)");
            var confirm = await terminal.GetInput("> ");
            if (confirm.ToUpper() != "Y") return;

            // Remove item from inventory
            currentPlayer.Item.RemoveAt(selected.index);
            currentPlayer.ItemType.RemoveAt(selected.index);
            currentPlayer.Gold += selected.value;
            currentPlayer.Statistics?.RecordSale(selected.value);

            terminal.SetColor("bright_green");
            terminal.WriteLine($"The fence pockets {selected.name} and slides you {selected.value:N0} gold.");
            if (selected.cursed)
            {
                terminal.SetColor("magenta");
                terminal.WriteLine("\"Cursed, eh? I know a buyer for everything.\"");
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
            terminal.SetColor("dark_red");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
            { const string t = "THE SAFE HOUSE"; int l = (66 - t.Length) / 2, r = 66 - t.Length - l; terminal.WriteLine($"║{new string(' ', l)}{t}{new string(' ', r)}║"); }
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            if (currentPlayer.Darkness < 50)
            {
                terminal.SetColor("gray");
                terminal.WriteLine("A heavy door blocks your way. A slot opens and eyes peer out.");
                terminal.SetColor("red");
                terminal.WriteLine("\"You don't belong here, clean-skin. Come back when you've");
                terminal.WriteLine(" got some real darkness in your soul.\"");
                terminal.SetColor("gray");
                terminal.WriteLine($"(Requires Darkness 50+. Yours: {currentPlayer.Darkness})");
                await Task.Delay(2000);
                return;
            }

            long cost = 50;
            terminal.SetColor("gray");
            terminal.WriteLine("A cramped room with a filthy cot and a locked door.");
            terminal.WriteLine("At least nobody will find you here... probably.");
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine($"Rest here for {cost} gold? (restores 50% HP)");
            terminal.SetColor("gray");
            terminal.WriteLine($"Current HP: {currentPlayer.HP}/{currentPlayer.MaxHP}");
            terminal.SetColor("yellow");
            terminal.WriteLine($"Gold: {currentPlayer.Gold:N0}");
            terminal.WriteLine("");

            var ans = await terminal.GetInput("Rest? (Y/N): ");
            if (ans.ToUpper() != "Y") return;

            if (currentPlayer.Gold < cost)
            {
                terminal.SetColor("red");
                terminal.WriteLine("\"No gold, no bed. Sleep in the gutter.\"");
                await Task.Delay(1500);
                return;
            }

            currentPlayer.Gold -= cost;
            currentPlayer.Statistics?.RecordGoldSpent(cost);

            // Restore 50% HP
            long healAmount = currentPlayer.MaxHP / 2;
            currentPlayer.HP = Math.Min(currentPlayer.MaxHP, currentPlayer.HP + healAmount);

            terminal.SetColor("bright_green");
            terminal.WriteLine($"You catch some rest. (+{healAmount} HP)");
            terminal.SetColor("gray");
            terminal.WriteLine($"HP: {currentPlayer.HP}/{currentPlayer.MaxHP}");

            currentPlayer.DarkAlleyReputation = Math.Min(1000, currentPlayer.DarkAlleyReputation + 1);

            // 10% robbery chance (Shadows members exempt)
            bool isShadows = FactionSystem.Instance?.PlayerFaction == Faction.TheShadows;
            if (!isShadows && Random.Shared.Next(1, 101) <= 10)
            {
                float lossPercent = Random.Shared.Next(5, 11) / 100f;
                long goldLost = Math.Max(1, (long)(currentPlayer.Gold * lossPercent));
                currentPlayer.Gold -= goldLost;

                terminal.SetColor("red");
                terminal.WriteLine("");
                terminal.WriteLine($"You wake to find your purse lighter. Someone picked your pocket!");
                terminal.SetColor("bright_red");
                terminal.WriteLine($"Lost {goldLost:N0} gold while sleeping.");
            }
            else if (isShadows)
            {
                terminal.SetColor("magenta");
                terminal.WriteLine("Your Shadow coin keeps the thieves at bay.");
            }

            // Shadows members who rest here are hidden from PvP attacks while offline
            if (isShadows)
            {
                currentPlayer.SafeHouseResting = true;
                terminal.SetColor("dark_magenta");
                terminal.WriteLine("");
                terminal.WriteLine("The Shadows watch over you. No one will find you here.");
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
            terminal.SetColor("dark_magenta");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
            { const string t = "PAY TRIBUTE"; int l = (66 - t.Length) / 2, r = 66 - t.Length - l; terminal.WriteLine($"║{new string(' ', l)}{t}{new string(' ', r)}║"); }
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            if (standing >= 0)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine("The Shadows already tolerate you. No tribute needed.");
                await terminal.PressAnyKey();
                return;
            }

            // Cost: 100 gold base + 2 gold per point of negative standing
            // So at -2000, it costs 100 + 4000 = 4100 gold per tribute
            // Each tribute gives +50 standing
            long tributeCost = 100 + Math.Abs(standing) * 2;
            int standingGain = 50;

            terminal.SetColor("gray");
            terminal.WriteLine("A cloaked figure emerges from the shadows.");
            terminal.SetColor("dark_red");
            terminal.WriteLine("");
            terminal.WriteLine("\"Word is you want back in our good graces. That can be arranged...\"");
            terminal.WriteLine("\"...for the right price.\"");
            terminal.WriteLine("");

            terminal.SetColor("yellow");
            terminal.WriteLine($"  Current Shadows standing: {standing:N0}");
            terminal.SetColor("white");
            terminal.WriteLine($"  Tribute cost: {tributeCost:N0} gold  (+{standingGain} standing)");
            terminal.SetColor("gray");
            int tributesNeeded = standing < -50 ? (int)Math.Ceiling((-50.0 - standing) / standingGain) : 0;
            if (tributesNeeded > 0)
                terminal.WriteLine($"  ~{tributesNeeded} tributes to unlock underground services");
            terminal.SetColor("yellow");
            terminal.WriteLine($"  Your gold: {currentPlayer.Gold:N0}");
            terminal.WriteLine("");

            var ans = await terminal.GetInput("Pay tribute? (Y/N): ");
            if (ans?.Trim().ToUpper() != "Y") return;

            if (currentPlayer.Gold < tributeCost)
            {
                terminal.SetColor("red");
                terminal.WriteLine("\"Come back when you can afford it. We don't do charity.\"");
                await Task.Delay(1500);
                return;
            }

            currentPlayer.Gold -= tributeCost;
            currentPlayer.Statistics?.RecordGoldSpent(tributeCost);
            factionSystem?.ModifyReputation(Faction.TheShadows, standingGain);

            var newStanding = factionSystem?.FactionStanding[Faction.TheShadows] ?? 0;

            terminal.SetColor("bright_green");
            terminal.WriteLine("");
            terminal.WriteLine($"The figure pockets your gold and nods.");
            terminal.SetColor("yellow");
            terminal.WriteLine($"  Shadows standing: {standing:N0} → {newStanding:N0} (+{standingGain})");

            if (newStanding >= -50 && standing < -50)
            {
                terminal.SetColor("bright_magenta");
                terminal.WriteLine("");
                terminal.WriteLine("  \"The underground is open to you again. Don't make us regret it.\"");
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
                term.WriteLine("A figure steps from the shadows, blade gleaming!");
                term.SetColor("red");
                term.WriteLine("\"Hand over 50 gold or I'll gut you like a fish!\"");
                term.WriteLine("");
                term.SetColor("darkgray");
                term.Write("[");
                term.SetColor("bright_yellow");
                term.Write("1");
                term.SetColor("darkgray");
                term.Write("]");
                term.SetColor("yellow");
                term.Write(" Pay 50 gold    ");
                term.SetColor("darkgray");
                term.Write("[");
                term.SetColor("bright_yellow");
                term.Write("2");
                term.SetColor("darkgray");
                term.Write("]");
                term.SetColor("yellow");
                term.WriteLine(" Fight!");
                var choice = await term.GetInput("> ");

                if (choice == "1" && player.Gold >= 50)
                {
                    player.Gold -= 50;
                    term.SetColor("gray");
                    term.WriteLine("You hand over the gold. The mugger vanishes into the dark.");
                    player.Statistics?.RecordGoldSpent(50);
                }
                else
                {
                    term.SetColor("bright_red");
                    term.WriteLine("\"Wrong answer!\"");
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
                term.WriteLine("A ragged beggar tugs at your sleeve.");
                term.SetColor("yellow");

                var tips = new[]
                {
                    "\"The monsters on the deeper floors carry enchanted weapons, they do...\"",
                    "\"Watch out for the loan shark's enforcers. They break kneecaps for fun.\"",
                    "\"I heard the guards are planning a raid on the alley tomorrow...\"",
                    "\"There's a secret path in the dungeon. Look for the cracked wall.\"",
                    "\"The fence pays more for cursed items than regular ones, if you know how to ask.\"",
                    "\"The pit fights are rigged, but if yer strong enough it don't matter.\"",
                };
                term.WriteLine(tips[Random.Shared.Next(0, tips.Length)]);
                term.SetColor("gray");
                term.WriteLine("The beggar shuffles away.");
            }
            else if (encounterType <= 80)
            {
                // Undercover guard (20%)
                if (player.Darkness > 300)
                {
                    float arrestChance = 0.50f;
                    if ((float)new Random().NextDouble() < arrestChance)
                    {
                        term.SetColor("bright_red");
                        term.WriteLine("");
                        term.WriteLine("\"HALT! City Watch! You're under arrest!\"");
                        term.SetColor("red");
                        term.WriteLine("An undercover guard reveals a badge. More guards swarm in.");
                        term.SetColor("bright_red");
                        term.WriteLine("You are dragged to the prison!");
                        player.DaysInPrison = 1;
                        await Task.Delay(2500);
                        throw new LocationExitException(GameLocation.Prison);
                    }
                    else
                    {
                        term.SetColor("yellow");
                        term.WriteLine("");
                        term.WriteLine("You notice someone watching you a bit too intently...");
                        term.SetColor("gray");
                        term.WriteLine("Undercover guard? You slip away before they can act.");
                    }
                }
                else
                {
                    term.SetColor("gray");
                    term.WriteLine("");
                    term.WriteLine("A well-dressed man bumps into you, mutters an apology, and moves on.");
                    term.WriteLine("Something about him seemed... official.");
                }
            }
            else
            {
                // Shady merchant (20%)
                term.SetColor("magenta");
                term.WriteLine("");
                term.WriteLine("A cloaked figure sidles up to you.");
                term.SetColor("yellow");

                int offer = Random.Shared.Next(1, 3);
                if (offer == 1)
                {
                    // Healing potion at half price
                    long potionPrice = GetAdjustedPrice(50);
                    term.WriteLine($"\"Psst... healing potion. Real good stuff. Only {potionPrice} gold.\"");
                    term.SetColor("darkgray");
                    term.Write("[");
                    term.SetColor("bright_yellow");
                    term.Write("Y");
                    term.SetColor("darkgray");
                    term.Write("]");
                    term.SetColor("cyan");
                    term.Write(" Buy    ");
                    term.SetColor("darkgray");
                    term.Write("[");
                    term.SetColor("bright_yellow");
                    term.Write("N");
                    term.SetColor("darkgray");
                    term.Write("]");
                    term.SetColor("cyan");
                    term.WriteLine(" Pass");
                    var ans = await term.GetInput("> ");
                    if (ans.ToUpper() == "Y" && player.Gold >= potionPrice)
                    {
                        player.Gold -= potionPrice;
                        player.Healing = Math.Min(player.MaxPotions, player.Healing + 1);
                        term.SetColor("bright_green");
                        term.WriteLine("You pocket the potion. It smells alright... probably.");
                    }
                    else if (ans.ToUpper() == "Y")
                    {
                        term.SetColor("red");
                        term.WriteLine("\"No gold? Scram.\"");
                    }
                }
                else
                {
                    // Random stat boost (small)
                    long price = GetAdjustedPrice(100);
                    term.WriteLine($"\"Special elixir. Makes you... better. {price} gold.\"");
                    term.SetColor("darkgray");
                    term.Write("[");
                    term.SetColor("bright_yellow");
                    term.Write("Y");
                    term.SetColor("darkgray");
                    term.Write("]");
                    term.SetColor("cyan");
                    term.Write(" Buy    ");
                    term.SetColor("darkgray");
                    term.Write("[");
                    term.SetColor("bright_yellow");
                    term.Write("N");
                    term.SetColor("darkgray");
                    term.Write("]");
                    term.SetColor("cyan");
                    term.WriteLine(" Pass");
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
                                term.WriteLine("A warm rush of power. (+1 STR)");
                                break;
                            case 2:
                                player.Dexterity += 1;
                                term.SetColor("bright_green");
                                term.WriteLine("Your fingers feel nimbler. (+1 DEX)");
                                break;
                            default:
                                player.Constitution += 1;
                                term.SetColor("bright_green");
                                term.WriteLine("You feel hardier. (+1 CON)");
                                break;
                        }
                    }
                    else if (ans.ToUpper() == "Y")
                    {
                        term.SetColor("red");
                        term.WriteLine("\"No gold? Don't waste my time.\"");
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
            term.SetColor("bright_red");
            term.WriteLine("");
            term.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
            { const string t = "THE ENFORCER HAS FOUND YOU"; int l = (66 - t.Length) / 2, r = 66 - t.Length - l; term.WriteLine($"║{new string(' ', l)}{t}{new string(' ', r)}║"); }
            term.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
            term.WriteLine("");
            term.SetColor("red");
            term.WriteLine("A massive figure blocks your path. Scarred knuckles crack.");
            term.SetColor("bright_red");
            term.WriteLine("\"You owe the boss. Time to pay up... one way or another.\"");
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
                term.WriteLine("The enforcer crumples to the ground.");
                term.SetColor("yellow");
                term.WriteLine("Word on the street is the loan shark considers the debt settled.");
                term.SetColor("bright_green");
                term.WriteLine("Your loan has been forgiven!");
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
                term.WriteLine("The enforcer beats you senseless and takes everything.");
                term.SetColor("red");
                term.WriteLine($"Lost all gold ({goldTaken:N0}). Took {hpDamage} HP damage.");
                term.SetColor("yellow");
                term.WriteLine("\"The boss is giving you 3 more days. Don't waste them.\"");
            }

            term.WriteLine("");
            await Task.Delay(2500);
        }

        #endregion
    }
}