using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
                if (player.ScreenReaderMode)
                {
                    term.WriteLine(Loc.Get("church.entry_denied"));
                }
                else
                {
                    term.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                    { string t = Loc.Get("church.entry_denied"); int l = (78 - t.Length) / 2, r = 78 - t.Length - l; term.WriteLine($"║{new string(' ', l)}{t}{new string(' ', r)}║"); }
                    term.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
                }
                term.WriteLine("");
                term.SetColor("red");
                term.WriteLine(reason);
                term.WriteLine("");
                term.SetColor("gray");
                term.WriteLine(Loc.Get("church.holy_wards"));
                term.WriteLine(Loc.Get("church.cleanse_soul"));
                term.WriteLine("");
                term.SetColor("yellow");
                term.Write(Loc.Get("church.press_return"));
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
                    
                case "V": // Church Records
                    await DisplayChurchRecords();
                    return false;

                case "S": // Speak with the Bishop
                    await SpeakWithBishop();
                    return false;

                case "R": // Return to Main Street
                case "Q":
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

            if (IsScreenReader)
            {
                DisplayLocationSR();
                return;
            }

            if (IsBBSSession)
            {
                DisplayLocationBBS();
                return;
            }

            WriteBoxHeader(Loc.Get("church.header"), "bright_cyan");
            terminal.WriteLine("");
            
            // Church description
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("church.desc1"));
            terminal.WriteLine(Loc.Get("church.desc2"));
            terminal.WriteLine(Loc.Get("church.desc3"));
            terminal.WriteLine(Loc.Get("church.desc4"));
            terminal.WriteLine("");

            // Current player status
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("church.chivalry", currentPlayer.Chivalry));
            terminal.WriteLine(Loc.Get("church.darkness", currentPlayer.Darkness));
            terminal.WriteLine(Loc.Get("church.alignment", GetAlignmentDescription(currentPlayer)));
            terminal.WriteLine("");

            // Church staff greeting
            terminal.SetColor("yellow");
            if (currentPlayer.Chivalry > currentPlayer.Darkness)
            {
                terminal.WriteLine(Loc.Get("church.bishop_approves", bishopName));
            }
            else if (currentPlayer.Darkness > currentPlayer.Chivalry)
            {
                terminal.WriteLine(Loc.Get("church.bishop_concerned", bishopName));
            }
            else
            {
                terminal.WriteLine(Loc.Get("church.bishop_welcomes", bishopName));
            }
            terminal.WriteLine("");

            ShowNPCsInLocation();

            // Menu options
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("church.services_title"));
            if (!IsScreenReader)
                terminal.WriteLine("─────────────────────────");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("C");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("church.donate"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("B");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("church.blessing"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("H");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("church.healing"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("M");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("church.marriage"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("F");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("church.confess"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("V");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("church.records"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("S");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("church.bishop"));

            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("R");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("church.return"));
            terminal.WriteLine("");
            
            // Status line (basic)
            ShowStatusLine();
        }

        private void DisplayLocationSR()
        {
            terminal.WriteLine(Loc.Get("church.header"));
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("church.sr_desc"));
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("church.sr_alignment_line", currentPlayer.Chivalry, currentPlayer.Darkness, GetAlignmentDescription(currentPlayer)));
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            if (currentPlayer.Chivalry > currentPlayer.Darkness)
                terminal.WriteLine(Loc.Get("church.bishop_approves", bishopName));
            else if (currentPlayer.Darkness > currentPlayer.Chivalry)
                terminal.WriteLine(Loc.Get("church.bishop_concerned", bishopName));
            else
                terminal.WriteLine(Loc.Get("church.bishop_welcomes", bishopName));
            terminal.WriteLine("");
            ShowNPCsInLocation();
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("church.sr_services_title"));
            WriteSRMenuOption("C", Loc.Get("church.donate"));
            WriteSRMenuOption("B", Loc.Get("church.blessing"));
            WriteSRMenuOption("H", Loc.Get("church.healing"));
            WriteSRMenuOption("M", Loc.Get("church.marriage"));
            WriteSRMenuOption("F", Loc.Get("church.confess"));
            WriteSRMenuOption("V", Loc.Get("church.records"));
            WriteSRMenuOption("S", Loc.Get("church.bishop"));
            terminal.WriteLine("");
            WriteSRMenuOption("R", Loc.Get("church.return"));
            terminal.WriteLine("");
            ShowStatusLine();
        }

        /// <summary>
        /// BBS compact display for 80x25 terminal
        /// </summary>
        private void DisplayLocationBBS()
        {
            ShowBBSHeader(Loc.Get("church.header"));
            // 1-line alignment info
            terminal.SetColor("gray");
            terminal.Write(Loc.Get("church.bbs_alignment"));
            terminal.SetColor("cyan");
            terminal.Write(GetAlignmentDescription(currentPlayer));
            terminal.SetColor("gray");
            terminal.Write(Loc.Get("church.bbs_chivalry"));
            terminal.SetColor("bright_green");
            terminal.Write($"{currentPlayer.Chivalry}");
            terminal.SetColor("gray");
            terminal.Write(Loc.Get("church.bbs_darkness"));
            terminal.SetColor("red");
            terminal.WriteLine($"{currentPlayer.Darkness}");
            ShowBBSNPCs();
            // Menu rows
            ShowBBSMenuRow(("C", "bright_green", Loc.Get("church.bbs_donate")), ("B", "bright_yellow", Loc.Get("church.bbs_blessing")), ("H", "bright_cyan", Loc.Get("church.bbs_heal")), ("M", "bright_magenta", Loc.Get("church.bbs_marriage")));
            ShowBBSMenuRow(("F", "cyan", Loc.Get("church.bbs_confess")), ("V", "yellow", Loc.Get("church.bbs_records")), ("S", "magenta", Loc.Get("church.bbs_bishop")), ("R", "bright_red", Loc.Get("church.bbs_return")));
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
            terminal.WriteLine(Loc.Get("church.donate_have_gold", currentPlayer.Gold.ToString("N0"), GameConfig.MoneyType));
            terminal.WriteLine(Loc.Get("church.donate_how_much"));

            var input = await terminal.GetInput(Loc.Get("church.donate_amount_prompt"));
            if (!long.TryParse(input, out long amount))
            {
                terminal.WriteLine(Loc.Get("church.donate_invalid"), "red");
                await Task.Delay(1500);
                return;
            }

            if (amount <= 0)
            {
                terminal.WriteLine(Loc.Get("church.donate_zero"), "yellow");
                await Task.Delay(2000);
                return;
            }

            if (amount > currentPlayer.Gold)
            {
                terminal.WriteLine(Loc.Get("church.donate_no_gold"), "red");
                await Task.Delay(2000);
                return;
            }

            // Confirm donation
            var confirm = await terminal.GetInput(Loc.Get("church.donate_confirm", amount.ToString("N0"), GameConfig.MoneyType));
            if (confirm.ToUpper() != "Y")
            {
                terminal.WriteLine(Loc.Get("church.donate_cancelled"), "gray");
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
            terminal.WriteLine(Loc.Get("church.donate_appreciated", amount.ToString("N0"), GameConfig.MoneyType));
            terminal.WriteLine(Loc.Get("church.donate_virtue"));
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("church.donate_blessed_by", bishopName));
            terminal.WriteLine(Loc.Get("church.donate_chivalry_gain", chivalryGain));

            // Church donations are light actions - increase Faith standing
            int faithGain = Math.Max(1, (int)(amount / 100));
            UsurperRemake.Systems.FactionSystem.Instance.ModifyReputation(UsurperRemake.Systems.Faction.TheFaith, faithGain);
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("church.donate_faith_gain", faithGain));

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
                terminal.WriteLine(Loc.Get("church.blessing_pure"), "bright_green");
                await Task.Delay(2000);
                return;
            }

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("church.donate_have_gold", currentPlayer.Gold.ToString("N0"), GameConfig.MoneyType));
            terminal.WriteLine(Loc.Get("church.blessing_how_much"));

            var input = await terminal.GetInput(Loc.Get("church.donate_amount_prompt"));
            if (!long.TryParse(input, out long amount))
            {
                terminal.WriteLine(Loc.Get("church.donate_invalid"), "red");
                await Task.Delay(1500);
                return;
            }

            if (amount <= 0)
            {
                terminal.WriteLine(Loc.Get("church.blessing_no_offering"), "yellow");
                await Task.Delay(2000);
                return;
            }

            if (amount > currentPlayer.Gold)
            {
                terminal.WriteLine(Loc.Get("church.blessing_no_gold"), "red");
                await Task.Delay(2000);
                return;
            }

            // Confirm blessing purchase
            var confirm = await terminal.GetInput(Loc.Get("church.blessing_confirm", amount.ToString("N0"), GameConfig.MoneyType));
            if (confirm.ToUpper() != "Y")
            {
                terminal.WriteLine(Loc.Get("church.blessing_cancelled"), "gray");
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
            terminal.WriteLine(Loc.Get("church.blessing_contribution", amount.ToString("N0"), GameConfig.MoneyType));
            terminal.WriteLine(Loc.Get("church.blessing_lightens"));
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("church.blessing_ritual", bishopName));
            terminal.WriteLine(Loc.Get("church.blessing_divine_light"));
            terminal.WriteLine(Loc.Get("church.blessing_chivalry", chivalryGain));
            terminal.WriteLine(Loc.Get("church.blessing_darkness_decrease", Math.Max(1L, darknessReduction)));
            terminal.WriteLine(Loc.Get("church.blessing_days"));

            // Blessings are light actions - increase Faith standing
            int faithGain = Math.Max(1, (int)(amount / 75));
            UsurperRemake.Systems.FactionSystem.Instance.ModifyReputation(UsurperRemake.Systems.Faction.TheFaith, faithGain);
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("church.blessing_faith", faithGain));

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
            WriteSectionHeader(Loc.Get("church.healing_services"), "bright_cyan");
            terminal.WriteLine("");
            
            bool needsHealing = currentPlayer.HP < currentPlayer.MaxHP ||
                               currentPlayer.Blind || currentPlayer.Plague ||
                               currentPlayer.Smallpox || currentPlayer.Measles ||
                               currentPlayer.Leprosy;
            
            if (!needsHealing)
            {
                terminal.WriteLine(Loc.Get("church.heal_perfect"), "bright_green");
                terminal.WriteLine(Loc.Get("church.heal_blessed", priestName));
                await Task.Delay(2500);
                return;
            }

            // Calculate healing cost based on player level and conditions
            long healingCost = CalculateHealingCost(currentPlayer);

            terminal.WriteLine(Loc.Get("church.heal_examines", priestName), "white");
            await Task.Delay(1500);

            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("church.heal_available"), "yellow");
            
            if (currentPlayer.HP < currentPlayer.MaxHP)
            {
                terminal.WriteLine(Loc.Get("church.heal_restore_hp", (healingCost / 2).ToString("N0"), GameConfig.MoneyType));
            }

            if (currentPlayer.Blind)
            {
                terminal.WriteLine(Loc.Get("church.heal_blindness", healingCost.ToString("N0"), GameConfig.MoneyType));
            }

            if (currentPlayer.Plague)
            {
                terminal.WriteLine(Loc.Get("church.heal_plague", (healingCost * 2).ToString("N0"), GameConfig.MoneyType));
            }

            if (currentPlayer.Smallpox)
            {
                terminal.WriteLine(Loc.Get("church.heal_smallpox", healingCost.ToString("N0"), GameConfig.MoneyType));
            }

            if (currentPlayer.Measles)
            {
                terminal.WriteLine(Loc.Get("church.heal_measles", healingCost.ToString("N0"), GameConfig.MoneyType));
            }

            if (currentPlayer.Leprosy)
            {
                terminal.WriteLine(Loc.Get("church.heal_leprosy", (healingCost * 3).ToString("N0"), GameConfig.MoneyType));
            }

            terminal.WriteLine(Loc.Get("church.heal_complete", (healingCost * 3).ToString("N0"), GameConfig.MoneyType));
            terminal.WriteLine("");

            var choice = await terminal.GetInput(Loc.Get("church.heal_prompt"));
            
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
                        service = Loc.Get("church.service_health");
                        canHeal = true;
                    }
                    break;
                    
                case "B":
                    if (currentPlayer.Blind)
                    {
                        cost = baseCost;
                        service = Loc.Get("church.service_blindness");
                        canHeal = true;
                    }
                    break;
                    
                case "P":
                    if (currentPlayer.Plague)
                    {
                        cost = baseCost * 2;
                        service = Loc.Get("church.service_plague");
                        canHeal = true;
                    }
                    break;
                    
                case "S":
                    if (currentPlayer.Smallpox)
                    {
                        cost = baseCost;
                        service = Loc.Get("church.service_smallpox");
                        canHeal = true;
                    }
                    break;
                    
                case "M":
                    if (currentPlayer.Measles)
                    {
                        cost = baseCost;
                        service = Loc.Get("church.service_measles");
                        canHeal = true;
                    }
                    break;
                    
                case "L":
                    if (currentPlayer.Leprosy)
                    {
                        cost = baseCost * 3;
                        service = Loc.Get("church.service_leprosy");
                        canHeal = true;
                    }
                    break;
                    
                case "A":
                    cost = baseCost * 3;
                    service = Loc.Get("church.service_complete");
                    canHeal = true;
                    break;
                    
                case "N":
                    terminal.WriteLine(Loc.Get("church.heal_gods_watch"), "yellow");
                    await Task.Delay(1500);
                    return;
            }

            if (!canHeal)
            {
                terminal.WriteLine(Loc.Get("church.heal_not_needed"), "yellow");
                await Task.Delay(1500);
                return;
            }

            if (currentPlayer.Gold < cost)
            {
                terminal.WriteLine(Loc.Get("church.heal_need_gold", cost.ToString("N0"), GameConfig.MoneyType, service), "red");
                terminal.WriteLine(Loc.Get("church.heal_return_funds"), "gray");
                await Task.Delay(2000);
                return;
            }

            var confirm = await terminal.GetInput(Loc.Get("church.heal_pay_confirm", cost.ToString("N0"), GameConfig.MoneyType, service));
            if (confirm.ToUpper() != "Y")
            {
                terminal.WriteLine(Loc.Get("church.heal_cancelled"), "gray");
                await Task.Delay(1500);
                return;
            }
            
            // Process healing
            currentPlayer.Gold -= cost;
            currentPlayer.HealingsReceived += 1; // Track healings received
            
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("church.heal_ritual", priestName), "bright_yellow");
            await Task.Delay(2000);

            // Apply healing based on choice
            switch (choice)
            {
                case "H":
                    currentPlayer.HP = currentPlayer.MaxHP;
                    terminal.WriteLine(Loc.Get("church.heal_wounds_close"), "bright_green");
                    break;
                    
                case "B":
                    currentPlayer.Blind = false;
                    terminal.WriteLine(Loc.Get("church.heal_sight_restored"), "bright_green");
                    break;

                case "P":
                    currentPlayer.Plague = false;
                    terminal.WriteLine(Loc.Get("church.heal_plague_cured"), "bright_green");
                    break;

                case "S":
                    currentPlayer.Smallpox = false;
                    terminal.WriteLine(Loc.Get("church.heal_smallpox_cured"), "bright_green");
                    break;

                case "M":
                    currentPlayer.Measles = false;
                    terminal.WriteLine(Loc.Get("church.heal_measles_cured"), "bright_green");
                    break;

                case "L":
                    currentPlayer.Leprosy = false;
                    terminal.WriteLine(Loc.Get("church.heal_leprosy_cured"), "bright_green");
                    break;

                case "A":
                    currentPlayer.HP = currentPlayer.MaxHP;
                    currentPlayer.Blind = false;
                    currentPlayer.Plague = false;
                    currentPlayer.Smallpox = false;
                    currentPlayer.Measles = false;
                    currentPlayer.Leprosy = false;
                    terminal.WriteLine(Loc.Get("church.heal_all_cured"), "bright_white");
                    terminal.WriteLine(Loc.Get("church.heal_completely_restored"), "bright_green");
                    break;
            }

            // Grant small chivalry bonus for seeking healing
            int chivGain = Random.Shared.Next(1, 4);
            currentPlayer.Chivalry += chivGain;
            terminal.WriteLine(Loc.Get("church.heal_faith_wisdom", chivGain), "cyan");
            
            await Task.Delay(3000);
        }
        
        /// <summary>
        /// Process marriage ceremony
        /// </summary>
        private async Task ProcessMarriageCeremony()
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("church.marriage_title"), "bright_magenta");
            terminal.WriteLine("");

            if (currentPlayer.IsMarried)
            {
                terminal.WriteLine(Loc.Get("church.marriage_already", currentPlayer.SpouseName), "yellow");
                terminal.WriteLine(Loc.Get("church.marriage_no_perform"), "white");
                await Task.Delay(2500);
                return;
            }

            terminal.WriteLine(Loc.Get("church.marriage_bishop_smile", bishopName), "white");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("church.marriage_seeking"), "bright_yellow");
            terminal.WriteLine(Loc.Get("church.marriage_sacred"), "bright_yellow");
            terminal.WriteLine("");

            // Show eligible marriage candidates (NPCs in love with player)
            var eligibleNPCs = GetEligibleMarriageCandidates();

            if (eligibleNPCs.Count == 0)
            {
                terminal.WriteLine(Loc.Get("church.marriage_hearts_burn"), "bright_yellow");
                terminal.WriteLine(Loc.Get("church.marriage_consult"), "bright_yellow");
                terminal.WriteLine("");

                // Show the player's closest romantic relationships so they know how far they are
                var allNPCs = NPCSpawnSystem.Instance?.ActiveNPCs ?? new List<NPC>();
                var romance = RomanceTracker.Instance;
                var loverIds = new HashSet<string>(romance.CurrentLovers.Select(l => l.NPCId));
                var addedIds = new HashSet<string>();
                var prospects = new List<(NPC npc, int playerFeeling, int npcFeeling)>();

                // Always include current lovers from RomanceTracker
                foreach (var npc in allNPCs)
                {
                    if (!npc.IsAlive || npc.IsDead || npc.IsMarried) continue;
                    if (loverIds.Contains(npc.ID))
                    {
                        var pf = RelationshipSystem.GetRelationshipLevel(currentPlayer, npc);
                        var nf = RelationshipSystem.GetRelationshipLevel(npc, currentPlayer);
                        // Use Love level for lovers whose RelationshipSystem hasn't caught up
                        prospects.Add((npc, Math.Min(pf, GameConfig.RelationLove), Math.Min(nf, GameConfig.RelationLove)));
                        addedIds.Add(npc.ID);
                    }
                }

                // Also include NPCs with better-than-Normal relationship levels
                foreach (var npc in allNPCs)
                {
                    if (!npc.IsAlive || npc.IsDead || npc.IsMarried) continue;
                    if (addedIds.Contains(npc.ID)) continue;
                    var pf = RelationshipSystem.GetRelationshipLevel(currentPlayer, npc);
                    var nf = RelationshipSystem.GetRelationshipLevel(npc, currentPlayer);
                    if (pf < GameConfig.RelationNormal || nf < GameConfig.RelationNormal)
                        prospects.Add((npc, pf, nf));
                }

                if (prospects.Count > 0)
                {
                    // Sort by best combined feeling (lowest = closest to love)
                    prospects.Sort((a, b) => (a.playerFeeling + a.npcFeeling).CompareTo(b.playerFeeling + b.npcFeeling));
                    int shown = Math.Min(prospects.Count, 5);

                    terminal.WriteLine(Loc.Get("church.marriage_closest"), "cyan");
                    terminal.WriteLine(Loc.Get("church.marriage_header_row"), "darkgray");
                    if (!IsScreenReader)
                        terminal.WriteLine("  ─────────────────────────────────────────────────────", "darkgray");
                    for (int i = 0; i < shown; i++)
                    {
                        var (npc, pf, nf) = prospects[i];
                        var pfInfo = GetRelationshipDisplayInfo(pf);
                        var nfInfo = GetRelationshipDisplayInfo(nf);
                        string name = (npc.Name2 ?? npc.DisplayName).PadRight(25);
                        terminal.Write($"  {name}", "white");
                        terminal.Write($"{pfInfo.text,-14}", pfInfo.color);
                        terminal.WriteLine($"{nfInfo.text}", nfInfo.color);
                    }
                    terminal.WriteLine("");

                    // Check if anyone is close and give specific advice
                    var closest = prospects[0];
                    if (closest.playerFeeling <= GameConfig.RelationLove && closest.npcFeeling <= GameConfig.RelationPassion)
                        terminal.WriteLine(Loc.Get("church.marriage_nearly_ready", closest.npc.Name2), "bright_yellow");
                    else if (closest.playerFeeling <= GameConfig.RelationPassion || closest.npcFeeling <= GameConfig.RelationPassion)
                        terminal.WriteLine(Loc.Get("church.marriage_passion_stirs"), "bright_yellow");
                    else
                        terminal.WriteLine(Loc.Get("church.marriage_need_nurturing"), "bright_yellow");
                }
                else
                {
                    terminal.WriteLine(Loc.Get("church.marriage_no_connections"), "bright_yellow");
                }

                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("church.marriage_how_to"), "white");
                terminal.WriteLine(Loc.Get("church.marriage_step1"), "gray");
                terminal.WriteLine(Loc.Get("church.marriage_step2"), "gray");
                terminal.WriteLine(Loc.Get("church.marriage_step3"), "gray");
                terminal.WriteLine(Loc.Get("church.marriage_step4"), "gray");
                terminal.WriteLine(Loc.Get("church.marriage_step_note"), "gray");
                await terminal.PressAnyKey();
                return;
            }

            terminal.WriteLine(Loc.Get("church.marriage_those_who_would"), "bright_yellow");
            terminal.WriteLine("");

            for (int i = 0; i < eligibleNPCs.Count; i++)
            {
                var npc = eligibleNPCs[i];
                terminal.WriteLine($"  {i + 1}. {npc.Name2} ({npc.Class}, {Loc.Get("ui.level")} {npc.Level})", "bright_cyan");
            }
            terminal.WriteLine("");

            terminal.WriteLine(Loc.Get("church.marriage_services"), "cyan");
            terminal.WriteLine(Loc.Get("church.marriage_standard", GameConfig.MarriageCost.ToString("N0"), GameConfig.MoneyType));
            terminal.WriteLine(Loc.Get("church.marriage_elaborate", (GameConfig.MarriageCost * 2).ToString("N0"), GameConfig.MoneyType));
            terminal.WriteLine(Loc.Get("church.marriage_royal", (GameConfig.MarriageCost * 5).ToString("N0"), GameConfig.MoneyType));
            terminal.WriteLine("");

            var partnerInput = await terminal.GetInput(Loc.Get("church.marriage_who_prompt"));
            if (string.IsNullOrWhiteSpace(partnerInput) || partnerInput.ToUpper() == "Q")
            {
                terminal.WriteLine(Loc.Get("church.marriage_come_back"), "gray");
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
                terminal.WriteLine(Loc.Get("church.marriage_not_among", partnerInput), "red");
                terminal.WriteLine(Loc.Get("church.marriage_only_love"), "gray");
                await Task.Delay(2000);
                return;
            }

            var ceremonyType = await terminal.GetInput(Loc.Get("church.marriage_type_prompt"));

            long ceremonyCost = ceremonyType.ToUpper() switch
            {
                "E" => GameConfig.MarriageCost * 2,
                "R" => GameConfig.MarriageCost * 5,
                _ => GameConfig.MarriageCost
            };

            if (currentPlayer.Gold < ceremonyCost)
            {
                terminal.WriteLine(Loc.Get("church.marriage_need_gold", ceremonyCost.ToString("N0"), GameConfig.MoneyType), "red");
                await Task.Delay(2000);
                return;
            }

            var confirm = await terminal.GetInput(Loc.Get("church.marriage_proceed", targetNPC.Name2, ceremonyCost.ToString("N0"), GameConfig.MoneyType));
            if (confirm.ToUpper() != "Y")
            {
                terminal.WriteLine(Loc.Get("church.marriage_more_certain"), "gray");
                await Task.Delay(1500);
                return;
            }

            // Sync RelationshipSystem for lovers from RomanceTracker
            var romanceCheck = RomanceTracker.Instance;
            if (romanceCheck.CurrentLovers.Any(l => l.NPCId == targetNPC.ID))
            {
                var rel = RelationshipSystem.GetOrCreateRelationship(currentPlayer, targetNPC);
                if (rel.Relation1 > GameConfig.RelationLove)
                    rel.Relation1 = GameConfig.RelationLove;
                if (rel.Relation2 > GameConfig.RelationLove)
                    rel.Relation2 = GameConfig.RelationLove;
            }

            // Use the proper RelationshipSystem to validate and perform marriage
            currentPlayer.Gold -= ceremonyCost;

            bool success = RelationshipSystem.PerformMarriage(currentPlayer, targetNPC, out string marriageMessage);

            if (!success)
            {
                // Refund if marriage failed
                currentPlayer.Gold += ceremonyCost;
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("church.marriage_bishop_frowns", bishopName), "yellow");
                terminal.WriteLine($"\"{marriageMessage}\"", "bright_yellow");
                await Task.Delay(2500);
                return;
            }

            // Marriage ceremony display
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("church.wedding_title"), "bright_white");
            await Task.Delay(1000);

            terminal.WriteLine(Loc.Get("church.wedding_begins", bishopName), "bright_yellow");
            await Task.Delay(2000);

            terminal.WriteLine("");
            var ceremonyMsg = GameConfig.WeddingCeremonyMessages[Random.Shared.Next(0, GameConfig.WeddingCeremonyMessages.Length)];
            terminal.WriteLine($"\"{ceremonyMsg}\"", "bright_magenta");
            await Task.Delay(2000);

            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("church.wedding_married", targetNPC.Name2), "bright_green");
            terminal.WriteLine(Loc.Get("church.wedding_bells"), "bright_yellow");

            // Marriage bonuses
            currentPlayer.Chivalry += 10;
            currentPlayer.Charisma += 5;

            terminal.WriteLine(Loc.Get("church.wedding_chivalry"), "cyan");
            terminal.WriteLine(Loc.Get("church.wedding_charm"), "cyan");

            // Inform about children possibility
            if (currentPlayer.Sex != targetNPC.Sex)
            {
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("church.wedding_children"), "bright_yellow");
                terminal.WriteLine(Loc.Get("church.wedding_love_corner"), "gray");
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
            var romance = RomanceTracker.Instance;
            var loverIds = new HashSet<string>(romance.CurrentLovers.Select(l => l.NPCId));

            foreach (var npc in allNPCs)
            {
                if (!npc.IsAlive) continue;
                if (npc.IsMarried) continue;

                // Current lovers in RomanceTracker are eligible for marriage
                if (loverIds.Contains(npc.ID))
                {
                    eligible.Add(npc);
                    continue;
                }

                // Also check RelationshipSystem — both must be at Love or better
                var relation = RelationshipSystem.GetRelationshipLevel(currentPlayer, npc);
                var reverseRelation = RelationshipSystem.GetRelationshipLevel(npc, currentPlayer);

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
            WriteSectionHeader(Loc.Get("church.confession"), "bright_blue");
            terminal.WriteLine("");
            
            terminal.WriteLine(Loc.Get("church.confess_leads", priestName), "white");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("church.confess_speak"), "bright_yellow");
            terminal.WriteLine("");

            if (currentPlayer.Darkness <= 0)
            {
                terminal.WriteLine(Loc.Get("church.confess_pure"), "bright_green");
                terminal.WriteLine(Loc.Get("church.confess_righteous"), "bright_green");
                await Task.Delay(2500);
                return;
            }

            terminal.WriteLine(Loc.Get("church.confess_darkness", currentPlayer.Darkness), "red");
            terminal.WriteLine(Loc.Get("church.confess_reduce", Math.Min(currentPlayer.Darkness, 10)), "cyan");
            terminal.WriteLine("");

            var confess = await terminal.GetInput(Loc.Get("church.confess_prompt"));
            if (confess.ToUpper() != "Y")
            {
                terminal.WriteLine(Loc.Get("church.confess_return"), "gray");
                await Task.Delay(1500);
                return;
            }
            
            // Confession process
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("church.confess_begin"), "white");
            await Task.Delay(2000);
            
            long darknessReduction = Math.Min(currentPlayer.Darkness, Random.Shared.Next(5, 11));
            currentPlayer.Darkness = Math.Max(0, currentPlayer.Darkness - darknessReduction);
            
            // Small chivalry gain
            int chivalryGain = Random.Shared.Next(2, 6);
            currentPlayer.Chivalry += chivalryGain;
            
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("church.confess_forgiven"), "bright_yellow");
            terminal.WriteLine(Loc.Get("church.confess_sin_no_more"), "bright_yellow");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("church.confess_darkness_decrease", darknessReduction), "bright_green");
            terminal.WriteLine(Loc.Get("church.confess_chivalry_increase", chivalryGain), "cyan");
            terminal.WriteLine(Loc.Get("church.confess_cleansed"), "bright_white");

            await Task.Delay(3000);

            // Blood absolution — if the player carries murder weight, the priest detects it
            if (currentPlayer.MurderWeight > 0)
            {
                terminal.WriteLine("");
                terminal.SetColor("dark_red");
                terminal.WriteLine(Loc.Get("church.blood_pauses", priestName));
                await Task.Delay(2000);
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("church.blood_something_else"));
                terminal.WriteLine(Loc.Get("church.blood_sense_blood"));
                terminal.WriteLine("");
                await Task.Delay(2000);

                long absolveCost = GameConfig.BloodConfessionBaseCost + (long)(currentPlayer.MurderWeight * GameConfig.BloodConfessionCostPerWeight);
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("church.blood_cost", absolveCost.ToString("N0")));
                terminal.WriteLine(Loc.Get("church.blood_not_erase"));
                terminal.WriteLine("");
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("church.blood_murder_weight", currentPlayer.MurderWeight.ToString("F1")));
                terminal.WriteLine(Loc.Get("church.blood_absolution_cost", absolveCost.ToString("N0")));
                terminal.WriteLine(Loc.Get("church.blood_weight_reduced_by", GameConfig.MurderWeightConfessionReduction.ToString("F1")));
                terminal.WriteLine("");

                var absolve = await terminal.GetInput(Loc.Get("church.blood_accept"));
                if (absolve.Trim().ToUpper() == "Y")
                {
                    if (currentPlayer.Gold >= absolveCost)
                    {
                        currentPlayer.Gold -= absolveCost;
                        currentPlayer.Statistics?.RecordGoldSpent(absolveCost);
                        float reduction = Math.Min(currentPlayer.MurderWeight, GameConfig.MurderWeightConfessionReduction);
                        currentPlayer.MurderWeight -= reduction;
                        currentPlayer.LastConfession = DateTime.Now;

                        terminal.WriteLine("");
                        terminal.SetColor("bright_white");
                        terminal.WriteLine(Loc.Get("church.blood_hands", priestName));
                        await Task.Delay(1500);
                        terminal.SetColor("bright_cyan");
                        terminal.WriteLine(Loc.Get("church.blood_shared"));
                        terminal.WriteLine("");
                        terminal.SetColor("bright_green");
                        terminal.WriteLine(Loc.Get("church.blood_reduced", reduction.ToString("F1"), currentPlayer.MurderWeight.ToString("F1")));
                        terminal.SetColor("yellow");
                        terminal.WriteLine(Loc.Get("church.blood_gold_cost", absolveCost.ToString("N0")));
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine(Loc.Get("ui.not_enough_gold_amount", $"{currentPlayer.Gold:N0}", $"{absolveCost:N0}"));
                    }
                }
                else
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("church.blood_remains"));
                }
                await Task.Delay(2500);
            }
        }
        
        /// <summary>
        /// Display church records
        /// </summary>
        private async Task DisplayChurchRecords()
        {
            terminal.WriteLine("");
            WriteSectionHeader(Loc.Get("church.records"), "bright_cyan");
            terminal.WriteLine("");
            
            terminal.WriteLine(Loc.Get("church.records_title"), "white");
            if (!IsScreenReader)
                terminal.WriteLine("─────────────────────────────", "white");
            terminal.WriteLine("");

            // Player's church history
            terminal.WriteLine(Loc.Get("church.records_history"), "yellow");
            terminal.WriteLine(Loc.Get("church.records_donations", currentPlayer.ChurchDonations.ToString("N0"), GameConfig.MoneyType));
            terminal.WriteLine(Loc.Get("church.records_blessings", currentPlayer.BlessingsReceived));
            terminal.WriteLine(Loc.Get("church.records_healings", currentPlayer.HealingsReceived));
            terminal.WriteLine(Loc.Get("church.records_chivalry", currentPlayer.Chivalry));
            terminal.WriteLine(Loc.Get("church.records_darkness", currentPlayer.Darkness));
            terminal.WriteLine(Loc.Get("church.records_alignment", GetAlignmentDescription(currentPlayer)));
            terminal.WriteLine("");

            // Church statistics (placeholder for now)
            terminal.WriteLine(Loc.Get("church.records_church_stats"), "cyan");
            terminal.WriteLine(Loc.Get("church.records_month_donations", Random.Shared.Next(50000, 200001).ToString("N0"), GameConfig.MoneyType));
            terminal.WriteLine(Loc.Get("church.records_month_marriages", Random.Shared.Next(5, 26)));
            terminal.WriteLine(Loc.Get("church.records_month_blessings", Random.Shared.Next(100, 501)));
            terminal.WriteLine(Loc.Get("church.records_month_souls", Random.Shared.Next(50, 201)));
            terminal.WriteLine("");
            
            await terminal.PressAnyKey();
        }
        
        /// <summary>
        /// Speak with the Bishop
        /// </summary>
        private async Task SpeakWithBishop()
        {
            terminal.WriteLine("");
            WriteSectionHeader(Loc.Get("church.audience_bishop"), "bright_yellow");
            terminal.WriteLine("");
            
            terminal.WriteLine(Loc.Get("church.bishop_approaches", bishopName), "white");
            terminal.WriteLine("");

            // Bishop's response based on player's alignment
            if (currentPlayer.Chivalry > currentPlayer.Darkness * 2)
            {
                terminal.WriteLine(Loc.Get("church.bishop_righteous"), "bright_green");
                terminal.WriteLine(Loc.Get("church.bishop_beacon"), "bright_green");
                terminal.WriteLine(Loc.Get("church.bishop_continue"), "bright_green");

                // Reward for high chivalry
                if (Random.Shared.Next(1, 101) <= 25) // 25% chance
                {
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("church.bishop_reward"), "bright_yellow");
                    currentPlayer.DivineBlessing = Math.Max(currentPlayer.DivineBlessing, 3);
                    currentPlayer.Chivalry += 5;
                    terminal.WriteLine(Loc.Get("church.bishop_blessing_days"), "bright_white");
                    terminal.WriteLine(Loc.Get("church.bishop_chivalry5"), "cyan");
                }
            }
            else if (currentPlayer.Darkness > currentPlayer.Chivalry * 2)
            {
                terminal.WriteLine(Loc.Get("church.bishop_darkness_sense"), "red");
                terminal.WriteLine(Loc.Get("church.bishop_redemption"), "red");
                terminal.WriteLine(Loc.Get("church.bishop_consider"), "yellow");

                // Chance for forced confession
                if (Random.Shared.Next(1, 101) <= 30) // 30% chance
                {
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("church.bishop_insist"), "bright_red");
                    var forceConfess = await terminal.GetInput(Loc.Get("church.bishop_force_confess"));
                    if (forceConfess.ToUpper() == "Y")
                    {
                        await ProcessConfession();
                        return;
                    }
                }
            }
            else
            {
                terminal.WriteLine(Loc.Get("church.bishop_neutral1"), "white");
                terminal.WriteLine(Loc.Get("church.bishop_neutral2"), "white");
                terminal.WriteLine(Loc.Get("church.bishop_neutral3"), "cyan");
            }
            
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("church.bishop_wisdom_title"), "bright_yellow");

            var wisdom = Random.Shared.Next(1, 6);
            switch (wisdom)
            {
                case 1:
                    terminal.WriteLine(Loc.Get("church.bishop_wisdom1"));
                    break;
                case 2:
                    terminal.WriteLine(Loc.Get("church.bishop_wisdom2"));
                    break;
                case 3:
                    terminal.WriteLine(Loc.Get("church.bishop_wisdom3"));
                    break;
                case 4:
                    terminal.WriteLine(Loc.Get("church.bishop_wisdom4"));
                    break;
                case 5:
                    terminal.WriteLine(Loc.Get("church.bishop_wisdom5"));
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
                return Loc.Get("church.align_saintly");
            else if (player.Chivalry > player.Darkness * 2)
                return Loc.Get("church.align_very_good");
            else if (player.Chivalry > player.Darkness)
                return Loc.Get("church.align_good");
            else if (player.Chivalry == player.Darkness)
                return Loc.Get("church.align_neutral");
            else if (player.Darkness > player.Chivalry * 2)
                return Loc.Get("church.align_evil");
            else if (player.Darkness > player.Chivalry * 3)
                return Loc.Get("church.align_very_evil");
            else
                return Loc.Get("church.align_demonic");
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
            }
        }
    }
} 