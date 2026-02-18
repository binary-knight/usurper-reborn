using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using UsurperRemake.Utils;
using UsurperRemake.Systems;
using UsurperRemake.BBS;

namespace UsurperRemake.Locations
{
    /// <summary>
    /// Church of Good Deeds - Complete Pascal-compatible church system
    /// Based on GOODC.PAS with donations, blessings, healing, and marriage ceremonies
    /// Focuses on chivalry, good deeds, and moral alignment
    /// Evil characters are denied entry by holy wards!
    /// </summary>
    public partial class ChurchLocation : BaseLocation
    {
        // Church staff and configuration
        private readonly string bishopName;
        private readonly string priestName;

        public ChurchLocation()
        {
            // The base class will provide TerminalEmulator instance when entering the location.

            LocationName = "Church of Good Deeds";
            LocationId = GameLocation.Church;
            Description = "A peaceful sanctuary where the faithful come to seek salvation, perform good deeds, and find spiritual guidance.";

            bishopName = GameConfig.DefaultBishopName ?? "Bishop Aurelius";
            priestName = GameConfig.DefaultPriestName ?? "Father Benedict";

            SetupLocation();
        }

        /// <summary>
        /// Override EnterLocation to check alignment before allowing entry
        /// Evil characters are barred from the holy sanctuary!
        /// </summary>
        public override async Task EnterLocation(Character player, TerminalEmulator term)
        {
            var (canAccess, reason) = AlignmentSystem.Instance.CanAccessLocation(player, GameLocation.Church);

            if (!canAccess)
            {
                term.ClearScreen();
                term.SetColor("bright_red");
                term.WriteLine("╔═══════════════════════════════════════════════════════════════════════════╗");
                term.WriteLine("║                        ENTRY DENIED!                                     ║");
                term.WriteLine("╚═══════════════════════════════════════════════════════════════════════════╝");
                term.WriteLine("");
                term.SetColor("red");
                term.WriteLine(reason);
                term.WriteLine("");
                term.SetColor("gray");
                term.WriteLine("The holy wards surrounding this sacred place repel those of dark alignment.");
                term.WriteLine("Perhaps confession at a neutral shrine could help cleanse your soul...");
                term.WriteLine("");
                term.SetColor("yellow");
                term.Write("Press Enter to return to the street...");
                await term.GetKeyInput();
                throw new LocationExitException(GameLocation.MainStreet);
            }

            // If there's a warning but still allowed entry
            if (!string.IsNullOrEmpty(reason))
            {
                term.SetColor("yellow");
                term.WriteLine(reason);
                await Task.Delay(1500);
            }

            await base.EnterLocation(player, term);
        }
        
        protected override void SetupLocation()
        {
            PossibleExits = new List<GameLocation>
            {
                GameLocation.MainStreet
            };
            
            LocationActions = new List<string>
            {
                "Make a donation to the Church",
                "Purchase a blessing for your soul", 
                "Seek healing services",
                "Arrange a marriage ceremony",
                "Confess your sins",
                "View church records",
                "Speak with the Bishop",
                "Return to Main Street"
            };
        }
        
        /// <summary>
        /// Main church processing loop based on Pascal GOODC.PAS
        /// </summary>
        protected override async Task<bool> ProcessChoice(string choice)
        {
            // Handle global quick commands first
            var (handled, shouldExit) = await TryProcessGlobalCommand(choice);
            if (handled) return shouldExit;

            var upperChoice = choice.ToUpper().Trim();

            switch (upperChoice)
            {
                case "C": // Donate to Church (Collection)
                    await ProcessChurchDonation();
                    return false;
                    
                case "B": // Purchase a blessing
                    await ProcessBlessingPurchase();
                    return false;
                    
                case "H": // Healing Services
                    await ProcessHealingServices();
                    return false;
                    
                case "M": // Marriage Ceremony
                    await ProcessMarriageCeremony();
                    return false;
                    
                case "F": // Confess your sins
                    await ProcessConfession();
                    return false;
                    
                case "R": // Church Records
                    await DisplayChurchRecords();
                    return false;
                    
                case "S": // Speak with the Bishop
                    await SpeakWithBishop();
                    return false;
                    
                case "Q": // Return to Main Street
                case "1":
                    await NavigateToLocation(GameLocation.MainStreet);
                    return true;
                    
                default:
                    return await base.ProcessChoice(choice);
            }
        }
        
        protected override void DisplayLocation()
        {
            terminal.ClearScreen();

            if (DoorMode.IsInDoorMode)
            {
                DisplayLocationBBS();
                return;
            }

            // Church header - standardized format
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("╔═════════════════════════════════════════════════════════════════════════════╗");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("║                          CHURCH OF GOOD DEEDS                               ║");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("╚═════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");
            
            // Church description
            terminal.SetColor("white");
            terminal.WriteLine("You stand in a magnificent cathedral with soaring arches and stained glass");
            terminal.WriteLine("windows casting colorful light across the stone floor. The air is filled");
            terminal.WriteLine("with the scent of incense and the soft murmur of prayers. Candles flicker");
            terminal.WriteLine("on the altar, and religious artifacts line the walls.");
            terminal.WriteLine("");
            
            // Current player status
            terminal.SetColor("cyan");
            terminal.WriteLine($"Your current chivalry: {currentPlayer.Chivalry}");
            terminal.WriteLine($"Your current darkness: {currentPlayer.Darkness}");
            terminal.WriteLine($"Your moral alignment: {GetAlignmentDescription(currentPlayer)}");
            terminal.WriteLine("");
            
            // Church staff greeting
            terminal.SetColor("yellow");
            if (currentPlayer.Chivalry > currentPlayer.Darkness)
            {
                terminal.WriteLine($"{bishopName} nods approvingly at your righteous presence.");
            }
            else if (currentPlayer.Darkness > currentPlayer.Chivalry)
            {
                terminal.WriteLine($"{bishopName} looks concerned about the darkness in your soul.");
            }
            else
            {
                terminal.WriteLine($"{bishopName} welcomes you to this sacred place.");
            }
            terminal.WriteLine("");

            ShowNPCsInLocation();

            // Menu options
            terminal.SetColor("bright_green");
            terminal.WriteLine("Church Services Available:");
            terminal.WriteLine("─────────────────────────");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("C");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Make a donation to the Church");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("B");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Purchase a blessing for your soul");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("H");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Seek healing services");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("M");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Arrange a marriage ceremony");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("F");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Confess your sins");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("R");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("View church records");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("S");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Speak with the Bishop");

            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("Q");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("red");
            terminal.WriteLine("Return to Main Street");
            terminal.WriteLine("");
            
            // Status line (basic)
            ShowStatusLine();
        }

        /// <summary>
        /// BBS compact display for 80x25 terminal
        /// </summary>
        private void DisplayLocationBBS()
        {
            ShowBBSHeader("CHURCH OF GOOD DEEDS");
            // 1-line alignment info
            terminal.SetColor("gray");
            terminal.Write($" Alignment: ");
            terminal.SetColor("cyan");
            terminal.Write(GetAlignmentDescription(currentPlayer));
            terminal.SetColor("gray");
            terminal.Write($"  Chivalry:");
            terminal.SetColor("bright_green");
            terminal.Write($"{currentPlayer.Chivalry}");
            terminal.SetColor("gray");
            terminal.Write($"  Darkness:");
            terminal.SetColor("red");
            terminal.WriteLine($"{currentPlayer.Darkness}");
            ShowBBSNPCs();
            // Menu rows
            ShowBBSMenuRow(("C", "bright_green", "Donate"), ("B", "bright_yellow", "Blessing"), ("H", "bright_cyan", "Heal"), ("M", "bright_magenta", "Marriage"));
            ShowBBSMenuRow(("F", "cyan", "Confess"), ("R", "yellow", "Records"), ("S", "magenta", "Bishop"), ("Q", "bright_red", "Return"));
            ShowBBSFooter();
        }

        /// <summary>
        /// Process church donation - Pascal GOODC.PAS collection functionality
        /// </summary>
        private async Task ProcessChurchDonation()
        {
            terminal.WriteLine("");
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine($"You have {currentPlayer.Gold:N0} {GameConfig.MoneyType}.");
            terminal.WriteLine("How much do you want to give to the Church?");
            
            var input = await terminal.GetInput("Amount: ");
            if (!long.TryParse(input, out long amount))
            {
                terminal.WriteLine("Invalid amount.", "red");
                await Task.Delay(1500);
                return;
            }
            
            if (amount <= 0)
            {
                terminal.WriteLine("The Church appreciates your presence, if not your generosity.", "yellow");
                await Task.Delay(2000);
                return;
            }
            
            if (amount > currentPlayer.Gold)
            {
                terminal.WriteLine("Scoundrel! You don't have that much!", "red");
                await Task.Delay(2000);
                return;
            }
            
            // Confirm donation
            var confirm = await terminal.GetInput($"Donate {amount:N0} {GameConfig.MoneyType} to the Church? (Y/N): ");
            if (confirm.ToUpper() != "Y")
            {
                terminal.WriteLine("Perhaps another time then.", "gray");
                await Task.Delay(1500);
                return;
            }
            
            // Process donation
            currentPlayer.Gold -= amount;
            currentPlayer.ChurchDonations += amount; // Track total donations
            
            // Calculate chivalry gain (Pascal formula: amount / 11, minimum 1)
            long chivalryGain = Math.Max(1, amount / 11);
            currentPlayer.Chivalry += (int)chivalryGain;
            
            // Reduce darkness slightly
            if (currentPlayer.Darkness > 0)
            {
                currentPlayer.Darkness = Math.Max(0, currentPlayer.Darkness - 1);
            }
            
            terminal.WriteLine("");
            terminal.SetColor("bright_green");
            terminal.WriteLine($"Your contribution of {amount:N0} {GameConfig.MoneyType} is appreciated.");
            terminal.WriteLine("Your virtue and support from the Church increase.");
            terminal.WriteLine("");
            terminal.WriteLine($"You are blessed by {bishopName}.");
            terminal.WriteLine($"You gain {chivalryGain} chivalry points!");

            // Church donations are light actions - increase Faith standing
            int faithGain = Math.Max(1, (int)(amount / 100));
            UsurperRemake.Systems.FactionSystem.Instance.ModifyReputation(UsurperRemake.Systems.Faction.TheFaith, faithGain);
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"Your piety has been noted. (+{faithGain} Faith standing)");

            // Create news entry
            await CreateNewsEntry("Good-Doer", $"{currentPlayer.DisplayName} donated money to the Church.", "");

            await Task.Delay(3000);
        }
        
        /// <summary>
        /// Process blessing purchase - Pascal GOODC.PAS blessing functionality
        /// </summary>
        private async Task ProcessBlessingPurchase()
        {
            terminal.WriteLine("");
            terminal.WriteLine("");
            
            if (currentPlayer.Darkness < 1)
            {
                terminal.WriteLine("Your soul is in no need of salvation (lucky you).", "bright_green");
                await Task.Delay(2000);
                return;
            }
            
            terminal.SetColor("white");
            terminal.WriteLine($"You have {currentPlayer.Gold:N0} {GameConfig.MoneyType}.");
            terminal.WriteLine("How much do you want to give for a blessing?");
            
            var input = await terminal.GetInput("Amount: ");
            if (!long.TryParse(input, out long amount))
            {
                terminal.WriteLine("Invalid amount.", "red");
                await Task.Delay(1500);
                return;
            }
            
            if (amount <= 0)
            {
                terminal.WriteLine("The Church cannot provide salvation without proper offering.", "yellow");
                await Task.Delay(2000);
                return;
            }
            
            if (amount > currentPlayer.Gold)
            {
                terminal.WriteLine("You don't have that much gold!", "red");
                await Task.Delay(2000);
                return;
            }
            
            // Confirm blessing purchase
            var confirm = await terminal.GetInput($"Purchase blessing for {amount:N0} {GameConfig.MoneyType}? (Y/N): ");
            if (confirm.ToUpper() != "Y")
            {
                terminal.WriteLine("Your soul remains as it was.", "gray");
                await Task.Delay(1500);
                return;
            }
            
            // Process blessing
            currentPlayer.Gold -= amount;
            currentPlayer.BlessingsReceived += 1; // Track blessings received
            
            // Calculate blessing effect (Pascal formula: amount / 15, minimum 1)
            long chivalryGain = Math.Max(1, amount / 15);
            currentPlayer.Chivalry += (int)chivalryGain;
            
            // Reduce darkness more significantly than donation
            long darknessReduction = Math.Min(currentPlayer.Darkness, (long)(amount / 100));
            currentPlayer.Darkness = Math.Max(0, currentPlayer.Darkness - Math.Max(1L, darknessReduction));
            
            // Apply divine blessing effect
            currentPlayer.DivineBlessing = Math.Max(currentPlayer.DivineBlessing, 7); // 7 days of blessing
            
            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"Your contribution of {amount:N0} {GameConfig.MoneyType} gives you salvation.");
            terminal.WriteLine("Your dark soul lightens.");
            terminal.WriteLine("");
            terminal.WriteLine($"{bishopName} performs a sacred ritual over you.");
            terminal.WriteLine("Divine light surrounds you!");
            terminal.WriteLine($"You gain {chivalryGain} chivalry points!");
            terminal.WriteLine($"Your darkness decreases by {Math.Max(1L, darknessReduction)} points!");
            terminal.WriteLine("You are blessed for 7 days!");

            // Blessings are light actions - increase Faith standing
            int faithGain = Math.Max(1, (int)(amount / 75));
            UsurperRemake.Systems.FactionSystem.Instance.ModifyReputation(UsurperRemake.Systems.Faction.TheFaith, faithGain);
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"The Faith recognizes your devotion. (+{faithGain} Faith standing)");

            // Create news entry
            await CreateNewsEntry("Blessed", $"{currentPlayer.DisplayName} purchased a blessing.", "");

            await Task.Delay(4000);
        }
        
        /// <summary>
        /// Process healing services
        /// </summary>
        private async Task ProcessHealingServices()
        {
            terminal.WriteLine("");
            terminal.WriteLine("═══ CHURCH HEALING SERVICES ═══", "bright_cyan");
            terminal.WriteLine("");
            
            bool needsHealing = currentPlayer.HP < currentPlayer.MaxHP ||
                               currentPlayer.Blind || currentPlayer.Plague ||
                               currentPlayer.Smallpox || currentPlayer.Measles ||
                               currentPlayer.Leprosy;
            
            if (!needsHealing)
            {
                terminal.WriteLine("You are in perfect health, both body and soul.", "bright_green");
                terminal.WriteLine($"{priestName} blesses you for your good fortune.");
                await Task.Delay(2500);
                return;
            }
            
            // Calculate healing cost based on player level and conditions
            long healingCost = CalculateHealingCost(currentPlayer);
            
            terminal.WriteLine($"{priestName} examines you carefully...", "white");
            await Task.Delay(1500);
            
            terminal.WriteLine("");
            terminal.WriteLine("Available healing services:", "yellow");
            
            if (currentPlayer.HP < currentPlayer.MaxHP)
            {
                terminal.WriteLine($"- Restore health: {healingCost / 2:N0} {GameConfig.MoneyType}");
            }
            
            if (currentPlayer.Blind)
            {
                terminal.WriteLine($"- Cure blindness: {healingCost:N0} {GameConfig.MoneyType}");
            }
            
            if (currentPlayer.Plague)
            {
                terminal.WriteLine($"- Cure plague: {healingCost * 2:N0} {GameConfig.MoneyType}");
            }
            
            if (currentPlayer.Smallpox)
            {
                terminal.WriteLine($"- Cure smallpox: {healingCost:N0} {GameConfig.MoneyType}");
            }
            
            if (currentPlayer.Measles)
            {
                terminal.WriteLine($"- Cure measles: {healingCost:N0} {GameConfig.MoneyType}");
            }
            
            if (currentPlayer.Leprosy)
            {
                terminal.WriteLine($"- Cure leprosy: {healingCost * 3:N0} {GameConfig.MoneyType}");
            }
            
            terminal.WriteLine($"- Complete healing (all conditions): {healingCost * 3:N0} {GameConfig.MoneyType}");
            terminal.WriteLine("");
            
            var choice = await terminal.GetInput("What healing do you seek? (H)ealth, (B)lindness, (P)lague, (S)mallpox, (M)easles, (L)eprosy, (A)ll, (N)one: ");
            
            await ProcessHealingChoice(choice.ToUpper(), healingCost);
        }
        
        /// <summary>
        /// Process specific healing choice
        /// </summary>
        private async Task ProcessHealingChoice(string choice, long baseCost)
        {
            long cost = 0;
            string service = "";
            bool canHeal = false;
            
            switch (choice)
            {
                case "H":
                    if (currentPlayer.HP < currentPlayer.MaxHP)
                    {
                        cost = baseCost / 2;
                        service = "health restoration";
                        canHeal = true;
                    }
                    break;
                    
                case "B":
                    if (currentPlayer.Blind)
                    {
                        cost = baseCost;
                        service = "blindness cure";
                        canHeal = true;
                    }
                    break;
                    
                case "P":
                    if (currentPlayer.Plague)
                    {
                        cost = baseCost * 2;
                        service = "plague cure";
                        canHeal = true;
                    }
                    break;
                    
                case "S":
                    if (currentPlayer.Smallpox)
                    {
                        cost = baseCost;
                        service = "smallpox cure";
                        canHeal = true;
                    }
                    break;
                    
                case "M":
                    if (currentPlayer.Measles)
                    {
                        cost = baseCost;
                        service = "measles cure";
                        canHeal = true;
                    }
                    break;
                    
                case "L":
                    if (currentPlayer.Leprosy)
                    {
                        cost = baseCost * 3;
                        service = "leprosy cure";
                        canHeal = true;
                    }
                    break;
                    
                case "A":
                    cost = baseCost * 3;
                    service = "complete healing";
                    canHeal = true;
                    break;
                    
                case "N":
                    terminal.WriteLine("May the gods watch over you.", "yellow");
                    await Task.Delay(1500);
                    return;
            }
            
            if (!canHeal)
            {
                terminal.WriteLine("You don't need that particular healing.", "yellow");
                await Task.Delay(1500);
                return;
            }
            
            if (currentPlayer.Gold < cost)
            {
                terminal.WriteLine($"You need {cost:N0} {GameConfig.MoneyType} for {service}.", "red");
                terminal.WriteLine("Return when you have sufficient funds.", "gray");
                await Task.Delay(2000);
                return;
            }
            
            var confirm = await terminal.GetInput($"Pay {cost:N0} {GameConfig.MoneyType} for {service}? (Y/N): ");
            if (confirm.ToUpper() != "Y")
            {
                terminal.WriteLine("Perhaps another time.", "gray");
                await Task.Delay(1500);
                return;
            }
            
            // Process healing
            currentPlayer.Gold -= cost;
            currentPlayer.HealingsReceived += 1; // Track healings received
            
            terminal.WriteLine("");
            terminal.WriteLine($"{priestName} begins a sacred healing ritual...", "bright_yellow");
            await Task.Delay(2000);
            
            // Apply healing based on choice
            switch (choice)
            {
                case "H":
                    currentPlayer.HP = currentPlayer.MaxHP;
                    terminal.WriteLine("Your wounds close and your strength returns!", "bright_green");
                    break;
                    
                case "B":
                    currentPlayer.Blind = false;
                    terminal.WriteLine("Your sight is restored! The world comes back into focus!", "bright_green");
                    break;
                    
                case "P":
                    currentPlayer.Plague = false;
                    terminal.WriteLine("The plague leaves your body! You feel purified!", "bright_green");
                    break;
                    
                case "S":
                    currentPlayer.Smallpox = false;
                    terminal.WriteLine("The smallpox is cured! Your skin clears!", "bright_green");
                    break;
                    
                case "M":
                    currentPlayer.Measles = false;
                    terminal.WriteLine("The measles fade away! You feel healthy again!", "bright_green");
                    break;
                    
                case "L":
                    currentPlayer.Leprosy = false;
                    terminal.WriteLine("The leprosy is banished! Your body is made whole!", "bright_green");
                    break;
                    
                case "A":
                    currentPlayer.HP = currentPlayer.MaxHP;
                    currentPlayer.Blind = false;
                    currentPlayer.Plague = false;
                    currentPlayer.Smallpox = false;
                    currentPlayer.Measles = false;
                    currentPlayer.Leprosy = false;
                    terminal.WriteLine("Divine light fills your body! All ailments are cured!", "bright_white");
                    terminal.WriteLine("You are completely restored!", "bright_green");
                    break;
            }
            
            // Grant small chivalry bonus for seeking healing
            currentPlayer.Chivalry += GD.RandRange(1, 3);
            terminal.WriteLine($"Your faith in divine healing grants you wisdom! (+{GD.RandRange(1, 3)} chivalry)", "cyan");
            
            await Task.Delay(3000);
        }
        
        /// <summary>
        /// Process marriage ceremony
        /// </summary>
        private async Task ProcessMarriageCeremony()
        {
            terminal.WriteLine("");
            terminal.WriteLine("=== MARRIAGE CEREMONIES ===", "bright_magenta");
            terminal.WriteLine("");

            if (currentPlayer.IsMarried)
            {
                terminal.WriteLine($"You are already married to {currentPlayer.SpouseName}!", "yellow");
                terminal.WriteLine("The Church does not perform ceremonies for those already wed.", "white");
                await Task.Delay(2500);
                return;
            }

            terminal.WriteLine($"{bishopName} approaches with a warm smile.", "white");
            terminal.WriteLine("");
            terminal.WriteLine("\"Ah, seeking the blessed union of marriage!\"", "bright_yellow");
            terminal.WriteLine("\"This is one of the Church's most sacred ceremonies.\"", "bright_yellow");
            terminal.WriteLine("");

            // Show eligible marriage candidates (NPCs in love with player)
            var eligibleNPCs = GetEligibleMarriageCandidates();

            if (eligibleNPCs.Count == 0)
            {
                terminal.WriteLine("\"However, I see no one who is ready to marry you.\"", "bright_yellow");
                terminal.WriteLine("");
                terminal.WriteLine("To marry someone, you must first build a relationship with them.", "gray");
                terminal.WriteLine("Visit the Love Corner to court potential partners.", "gray");
                terminal.WriteLine("Both of you must be in love before marriage is possible.", "gray");
                await Task.Delay(3000);
                return;
            }

            terminal.WriteLine("\"I see there are those who would marry you:\"", "bright_yellow");
            terminal.WriteLine("");

            for (int i = 0; i < eligibleNPCs.Count; i++)
            {
                var npc = eligibleNPCs[i];
                terminal.WriteLine($"  {i + 1}. {npc.Name2} ({npc.Class}, Level {npc.Level})", "bright_cyan");
            }
            terminal.WriteLine("");

            terminal.WriteLine("Marriage ceremony services:", "cyan");
            terminal.WriteLine($"- Standard ceremony: {GameConfig.MarriageCost:N0} {GameConfig.MoneyType}");
            terminal.WriteLine($"- Elaborate ceremony: {GameConfig.MarriageCost * 2:N0} {GameConfig.MoneyType}");
            terminal.WriteLine($"- Royal ceremony: {GameConfig.MarriageCost * 5:N0} {GameConfig.MoneyType}");
            terminal.WriteLine("");

            var partnerInput = await terminal.GetInput("Who do you wish to marry? (Enter number or name, or Q to cancel): ");
            if (string.IsNullOrWhiteSpace(partnerInput) || partnerInput.ToUpper() == "Q")
            {
                terminal.WriteLine("\"Come back when you are ready.\"", "gray");
                await Task.Delay(1500);
                return;
            }

            // Find the NPC - by number or name
            NPC? targetNPC = null;
            if (int.TryParse(partnerInput, out int selection) && selection >= 1 && selection <= eligibleNPCs.Count)
            {
                targetNPC = eligibleNPCs[selection - 1];
            }
            else
            {
                targetNPC = eligibleNPCs.FirstOrDefault(n =>
                    n.Name2.Equals(partnerInput, StringComparison.OrdinalIgnoreCase));
            }

            if (targetNPC == null)
            {
                terminal.WriteLine($"\"{partnerInput}\" is not among those who would marry you.", "red");
                terminal.WriteLine("You can only marry someone who is in love with you.", "gray");
                await Task.Delay(2000);
                return;
            }

            var ceremonyType = await terminal.GetInput("What type of ceremony? (S)tandard, (E)laborate, (R)oyal: ");

            long ceremonyCost = ceremonyType.ToUpper() switch
            {
                "E" => GameConfig.MarriageCost * 2,
                "R" => GameConfig.MarriageCost * 5,
                _ => GameConfig.MarriageCost
            };

            if (currentPlayer.Gold < ceremonyCost)
            {
                terminal.WriteLine($"You need {ceremonyCost:N0} {GameConfig.MoneyType} for this ceremony.", "red");
                await Task.Delay(2000);
                return;
            }

            var confirm = await terminal.GetInput($"Proceed with marriage to {targetNPC.Name2} for {ceremonyCost:N0} {GameConfig.MoneyType}? (Y/N): ");
            if (confirm.ToUpper() != "Y")
            {
                terminal.WriteLine("Perhaps when you're more certain.", "gray");
                await Task.Delay(1500);
                return;
            }

            // Use the proper RelationshipSystem to validate and perform marriage
            currentPlayer.Gold -= ceremonyCost;

            bool success = RelationshipSystem.PerformMarriage(currentPlayer, targetNPC, out string marriageMessage);

            if (!success)
            {
                // Refund if marriage failed
                currentPlayer.Gold += ceremonyCost;
                terminal.WriteLine("");
                terminal.WriteLine($"{bishopName} frowns and shakes his head.", "yellow");
                terminal.WriteLine($"\"{marriageMessage}\"", "bright_yellow");
                await Task.Delay(2500);
                return;
            }

            // Marriage ceremony display
            terminal.WriteLine("");
            terminal.WriteLine("=== WEDDING CEREMONY ===", "bright_white");
            await Task.Delay(1000);

            terminal.WriteLine($"{bishopName} begins the sacred ceremony...", "bright_yellow");
            await Task.Delay(2000);

            terminal.WriteLine("");
            var ceremonyMsg = GameConfig.WeddingCeremonyMessages[GD.RandRange(0, GameConfig.WeddingCeremonyMessages.Length - 1)];
            terminal.WriteLine($"\"{ceremonyMsg}\"", "bright_magenta");
            await Task.Delay(2000);

            terminal.WriteLine("");
            terminal.WriteLine($"You are now married to {targetNPC.Name2}!", "bright_green");
            terminal.WriteLine("The Church bells ring in celebration!", "bright_yellow");

            // Marriage bonuses
            currentPlayer.Chivalry += 10;
            currentPlayer.Charisma += 5;

            terminal.WriteLine($"Your chivalry increases by 10!", "cyan");
            terminal.WriteLine($"Your charm increases by 5!", "cyan");

            // Inform about children possibility
            if (currentPlayer.Sex != targetNPC.Sex)
            {
                terminal.WriteLine("");
                terminal.WriteLine("\"May your union be blessed with children!\"", "bright_yellow");
                terminal.WriteLine("(Visit the Love Corner for intimate moments)", "gray");
            }

            // Create news entry
            await CreateNewsEntry("Wedding Bells", $"{currentPlayer.DisplayName} married {targetNPC.Name2} in a beautiful ceremony!", "The whole kingdom celebrates this union!");

            await Task.Delay(4000);
        }

        /// <summary>
        /// Get NPCs who are eligible to marry the current player (both in love)
        /// </summary>
        private List<NPC> GetEligibleMarriageCandidates()
        {
            var eligible = new List<NPC>();
            var allNPCs = NPCSpawnSystem.Instance?.ActiveNPCs ?? new List<NPC>();

            foreach (var npc in allNPCs)
            {
                if (!npc.IsAlive) continue;
                if (npc.IsMarried) continue;

                // Check if both player and NPC are in love with each other
                var relation = RelationshipSystem.GetRelationshipLevel(currentPlayer, npc);
                var reverseRelation = RelationshipSystem.GetRelationshipLevel(npc, currentPlayer);

                // Both must be at RelationLove (20) or better (lower number = better)
                if (relation <= GameConfig.RelationLove && reverseRelation <= GameConfig.RelationLove)
                {
                    eligible.Add(npc);
                }
            }

            return eligible;
        }
        
        /// <summary>
        /// Process confession
        /// </summary>
        private async Task ProcessConfession()
        {
            terminal.WriteLine("");
            terminal.WriteLine("═══ CONFESSION ═══", "bright_blue");
            terminal.WriteLine("");
            
            terminal.WriteLine($"{priestName} leads you to a private confessional booth.", "white");
            terminal.WriteLine("");
            terminal.WriteLine("\"Speak, my child, and unburden your soul.\"", "bright_yellow");
            terminal.WriteLine("");
            
            if (currentPlayer.Darkness <= 0)
            {
                terminal.WriteLine("\"Your soul is pure, you have no need for confession.\"", "bright_green");
                terminal.WriteLine("\"Go forth and continue your righteous path.\"", "bright_green");
                await Task.Delay(2500);
                return;
            }
            
            terminal.WriteLine($"Your current darkness: {currentPlayer.Darkness}", "red");
            terminal.WriteLine($"Confession can reduce your darkness by up to {Math.Min(currentPlayer.Darkness, 10)} points.", "cyan");
            terminal.WriteLine("");
            
            var confess = await terminal.GetInput("Do you wish to confess your sins? (Y/N): ");
            if (confess.ToUpper() != "Y")
            {
                terminal.WriteLine("\"Return when you are ready to face your sins.\"", "gray");
                await Task.Delay(1500);
                return;
            }
            
            // Confession process
            terminal.WriteLine("");
            terminal.WriteLine("You begin to confess your sins...", "white");
            await Task.Delay(2000);
            
            long darknessReduction = Math.Min(currentPlayer.Darkness, GD.RandRange(5, 10));
            currentPlayer.Darkness = Math.Max(0, currentPlayer.Darkness - darknessReduction);
            
            // Small chivalry gain
            int chivalryGain = GD.RandRange(2, 5);
            currentPlayer.Chivalry += chivalryGain;
            
            terminal.WriteLine("");
            terminal.WriteLine("\"Your sins are forgiven, my child.\"", "bright_yellow");
            terminal.WriteLine("\"Go forth and sin no more.\"", "bright_yellow");
            terminal.WriteLine("");
            terminal.WriteLine($"Your darkness decreases by {darknessReduction} points!", "bright_green");
            terminal.WriteLine($"Your chivalry increases by {chivalryGain} points!", "cyan");
            terminal.WriteLine("You feel spiritually cleansed!", "bright_white");
            
            await Task.Delay(3000);
        }
        
        /// <summary>
        /// Display church records
        /// </summary>
        private async Task DisplayChurchRecords()
        {
            terminal.WriteLine("");
            terminal.WriteLine("═══ CHURCH RECORDS ═══", "bright_cyan");
            terminal.WriteLine("");
            
            terminal.WriteLine("Church Records and Statistics:", "white");
            terminal.WriteLine("─────────────────────────────", "white");
            terminal.WriteLine("");
            
            // Player's church history
            terminal.WriteLine("Your Church History:", "yellow");
            terminal.WriteLine($"- Total donations made: {currentPlayer.ChurchDonations:N0} {GameConfig.MoneyType}");
            terminal.WriteLine($"- Blessings received: {currentPlayer.BlessingsReceived}");
            terminal.WriteLine($"- Healings received: {currentPlayer.HealingsReceived}");
            terminal.WriteLine($"- Current chivalry: {currentPlayer.Chivalry}");
            terminal.WriteLine($"- Current darkness: {currentPlayer.Darkness}");
            terminal.WriteLine($"- Moral alignment: {GetAlignmentDescription(currentPlayer)}");
            terminal.WriteLine("");
            
            // Church statistics (placeholder for now)
            terminal.WriteLine("Church Statistics:", "cyan");
            terminal.WriteLine($"- Total church donations this month: {GD.RandRange(50000, 200000):N0} {GameConfig.MoneyType}");
            terminal.WriteLine($"- Marriages performed this month: {GD.RandRange(5, 25)}");
            terminal.WriteLine($"- Blessings given this month: {GD.RandRange(100, 500)}");
            terminal.WriteLine($"- Souls saved from darkness: {GD.RandRange(50, 200)}");
            terminal.WriteLine("");
            
            await terminal.PressAnyKey();
        }
        
        /// <summary>
        /// Speak with the Bishop
        /// </summary>
        private async Task SpeakWithBishop()
        {
            terminal.WriteLine("");
            terminal.WriteLine("═══ AUDIENCE WITH THE BISHOP ═══", "bright_yellow");
            terminal.WriteLine("");
            
            terminal.WriteLine($"{bishopName} approaches with a serene expression.", "white");
            terminal.WriteLine("");
            
            // Bishop's response based on player's alignment
            if (currentPlayer.Chivalry > currentPlayer.Darkness * 2)
            {
                terminal.WriteLine("\"Ah, a truly righteous soul stands before me!\"", "bright_green");
                terminal.WriteLine("\"Your good deeds shine like a beacon in the darkness.\"", "bright_green");
                terminal.WriteLine("\"Continue on your path of virtue, my child.\"", "bright_green");
                
                // Reward for high chivalry
                if (GD.RandRange(1, 100) <= 25) // 25% chance
                {
                    terminal.WriteLine("");
                    terminal.WriteLine("\"As a reward for your righteousness, accept this blessing!\"", "bright_yellow");
                    currentPlayer.DivineBlessing = Math.Max(currentPlayer.DivineBlessing, 3);
                    currentPlayer.Chivalry += 5;
                    terminal.WriteLine("You receive a divine blessing for 3 days!", "bright_white");
                    terminal.WriteLine("Your chivalry increases by 5!", "cyan");
                }
            }
            else if (currentPlayer.Darkness > currentPlayer.Chivalry * 2)
            {
                terminal.WriteLine("\"I sense great darkness in your soul, child.\"", "red");
                terminal.WriteLine("\"You must seek redemption before it's too late.\"", "red");
                terminal.WriteLine("\"Consider making a donation or purchasing a blessing.\"", "yellow");
                
                // Chance for forced confession
                if (GD.RandRange(1, 100) <= 30) // 30% chance
                {
                    terminal.WriteLine("");
                    terminal.WriteLine("\"In fact, I insist you confess your sins immediately!\"", "bright_red");
                    var forceConfess = await terminal.GetInput("The Bishop strongly encourages confession. Confess now? (Y/N): ");
                    if (forceConfess.ToUpper() == "Y")
                    {
                        await ProcessConfession();
                        return;
                    }
                }
            }
            else
            {
                terminal.WriteLine("\"Welcome, child. Your soul walks the line between light and dark.\"", "white");
                terminal.WriteLine("\"Choose your path wisely, for each deed shapes your destiny.\"", "white");
                terminal.WriteLine("\"The Church stands ready to guide you toward righteousness.\"", "cyan");
            }
            
            terminal.WriteLine("");
            terminal.WriteLine("Bishop's Wisdom:", "bright_yellow");
            
            var wisdom = GD.RandRange(1, 5);
            switch (wisdom)
            {
                case 1:
                    terminal.WriteLine("\"Charity given in secret is worth twice that given in public.\"");
                    break;
                case 2:
                    terminal.WriteLine("\"A pure heart is more valuable than all the gold in the kingdom.\"");
                    break;
                case 3:
                    terminal.WriteLine("\"Those who help others will find help when they need it most.\"");
                    break;
                case 4:
                    terminal.WriteLine("\"Darkness flees from the light of good deeds.\"");
                    break;
                case 5:
                    terminal.WriteLine("\"The path to salvation is paved with acts of kindness.\"");
                    break;
            }
            
            terminal.WriteLine("");
            await terminal.PressAnyKey();
        }
        
        /// <summary>
        /// Calculate healing cost based on player condition
        /// </summary>
        private long CalculateHealingCost(Character player)
        {
            long baseCost = player.Level * 50 + 100;
            
            // Adjust based on alignment - good players get discounts
            if (player.Chivalry > player.Darkness)
            {
                baseCost = (long)(baseCost * 0.8); // 20% discount for good players
            }
            else if (player.Darkness > player.Chivalry * 2)
            {
                baseCost = (long)(baseCost * 1.5); // 50% markup for evil players
            }
            
            return Math.Max(50, baseCost);
        }
        
        /// <summary>
        /// Get alignment description
        /// </summary>
        private string GetAlignmentDescription(Character player)
        {
            if (player.Chivalry > player.Darkness * 3)
                return "Saintly";
            else if (player.Chivalry > player.Darkness * 2)
                return "Very Good";
            else if (player.Chivalry > player.Darkness)
                return "Good";
            else if (player.Chivalry == player.Darkness)
                return "Neutral";
            else if (player.Darkness > player.Chivalry * 2)
                return "Evil";
            else if (player.Darkness > player.Chivalry * 3)
                return "Very Evil";
            else
                return "Demonic";
        }
        
        /// <summary>
        /// Create news entry
        /// </summary>
        private async Task CreateNewsEntry(string category, string headline, string details)
        {
            try
            {
                var newsSystem = NewsSystem.Instance;
                string fullMessage = $"{headline}";
                if (!string.IsNullOrEmpty(details))
                {
                    fullMessage += $" {details}";
                }
                newsSystem.WriteNews(GameConfig.NewsCategory.General, fullMessage);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Failed to create news entry: {ex.Message}");
            }
        }
    }
} 