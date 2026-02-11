using Godot;
using System;
using System.Threading.Tasks;
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

            await base.EnterLocation(player, term);
        }

        protected override async Task<bool> ProcessChoice(string choice)
        {
            // Handle global quick commands first
            var (handled, shouldExit) = await TryProcessGlobalCommand(choice);
            if (handled) return shouldExit;

            switch (choice.ToUpperInvariant())
            {
                case "D":
                    await VisitDrugPalace();
                    return false;
                case "S":
                    await VisitSteroidShop();
                    return false;
                case "O":
                    await VisitOrbsHealthClub();
                    return false;
                case "G":
                    await VisitGroggoMagic();
                    return false;
                case "B":
                    await VisitBeerHut();
                    return false;
                case "A":
                    await VisitAlchemistHeaven();
                    return false;
                case "J": // The Shadows faction recruitment
                    await ShowShadowsRecruitment();
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

        protected override void DisplayLocation()
        {
            terminal.ClearScreen();

            // Header - standardized format
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("╔═════════════════════════════════════════════════════════════════════════════╗");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("║                              THE DARK ALLEY                                 ║");
            terminal.SetColor("bright_cyan");
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
            terminal.SetColor("bright_magenta");
            terminal.Write("D");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write("rug Palace             ");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_magenta");
            terminal.Write("S");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine("teroid Shop");

            // Row 2
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_magenta");
            terminal.Write("O");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write("rbs Health Club        ");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_magenta");
            terminal.Write("G");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine("roggo's Magic Services");

            // Row 3
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_magenta");
            terminal.Write("B");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write("ob's Beer Hut          ");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_magenta");
            terminal.Write("A");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine("lchemist's Heaven");

            terminal.WriteLine("");

            // The Shadows faction option
            var factionSystem = FactionSystem.Instance;
            if (factionSystem.PlayerFaction != Faction.TheShadows)
            {
                terminal.SetColor("darkgray");
                terminal.Write(" [");
                terminal.SetColor("bright_magenta");
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

            // Navigation
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_red");
            terminal.Write("Q");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine(" Return to Main Street");
            terminal.WriteLine("");

            ShowStatusLine();
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
                terminal.SetColor("cyan");
                terminal.Write($"[{i + 1}] ");
                terminal.SetColor(d.drug >= DrugType.DarkEssence ? "red" : "white");
                terminal.Write($"{d.name,-18}");
                terminal.SetColor("gray");
                terminal.Write($" {d.desc,-35}");
                terminal.SetColor("yellow");
                terminal.WriteLine($" {price:N0}g");
            }

            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine("[0] Leave");
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
            long price = GetAdjustedPrice(1000);
            terminal.WriteLine($"Bulk-up serum costs {price:N0} {GameConfig.MoneyType}.", "cyan");
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

            terminal.WriteLine("Your muscles swell unnaturally!", "bright_green");
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
                    terminal.WriteLine($"  {fortunes[GD.RandRange(0, fortunes.Length - 1)]}", "white");
                    break;

                case "3":
                    long blessPrice = GetAdjustedPrice(500);
                    if (currentPlayer.Gold < blessPrice)
                    {
                        terminal.WriteLine("Groggo scoffs. \"Shadow magic isn't cheap!\"", "red");
                        break;
                    }
                    currentPlayer.Gold -= blessPrice;
                    currentPlayer.Dexterity += 3; // Temporary-ish bonus (persists until rest)
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
            int effect = GD.RandRange(1, 4);
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
            int effect = GD.RandRange(1, 3);
            switch (effect)
            {
                case 1:
                    currentPlayer.Intelligence += 2;
                    terminal.WriteLine("Your mind feels sharper! (+2 INT)", "bright_green");
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
            await AchievementSystem.ShowPendingNotifications(terminal);

            terminal.SetColor("gray");
            terminal.WriteLine("The shadows shift, and the words fade from view.");
            terminal.WriteLine("But you know they are still there, waiting for another");
            terminal.WriteLine("curious soul to find them in the darkness.");
            terminal.WriteLine("");

            await terminal.GetInputAsync("Press Enter to continue...");
        }

        #endregion
    }
} 