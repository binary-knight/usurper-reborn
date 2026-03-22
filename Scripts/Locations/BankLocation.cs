using UsurperRemake.Utils;
using UsurperRemake.Systems;
using UsurperRemake.BBS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Bank of Wealth - The financial heart of the realm
/// Features: Deposits, Withdrawals, Transfers, Guard Duty, Bank Robberies, Loans, Safe Deposit Boxes
/// Based on Pascal BANK.PAS with expanded lore and modern async pattern
/// </summary>
public class BankLocation : BaseLocation
{
    private const string BankTitle = "The Ironvault Bank";
    private const string BankerName = "Groggo";
    private const string BankerTitle = "Master of Coin";

    // Bank safe information (shared across all instances)
    private static long _safeContents = 500000L; // Starting safe contents
    private static List<string> _activeGuardNames = new();
    private static int _robberyAttemptsToday = 0;
    private static DateTime _lastResetDate = DateTime.MinValue;

    // Interest rates
    private const float DailyInterestRate = 0.001f; // 0.1% daily interest
    private const float LoanInterestRate = 0.05f;   // 5% daily loan interest

    // Maximum gold to prevent overflow (99% of long.MaxValue)
    private const long MaxGold = long.MaxValue / 100 * 99;

    /// <summary>
    /// Safely add gold without overflow
    /// </summary>
    private static long SafeAddGold(long current, long amount)
    {
        if (amount <= 0) return current;
        if (current > MaxGold - amount) return MaxGold;
        return current + amount;
    }

    public BankLocation() : base(
        GameLocation.Bank,
        "The Ironvault Bank",
        "")
    {
        // Description set dynamically based on time/state
    }

    protected override void SetupLocation()
    {
        PossibleExits = new List<GameLocation>
        {
            GameLocation.MainStreet
        };
    }

    protected override void DisplayLocation()
    {
        // Reset daily robbery counter if new day
        if (_lastResetDate.Date != DateTime.Now.Date)
        {
            _robberyAttemptsToday = 0;
            _lastResetDate = DateTime.Now;
        }

        terminal.ClearScreen();

        // Check if player is banned from the bank
        if (IsPlayerBannedFromBank(currentPlayer))
        {
            DisplayBannedMessage();
            return;
        }

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

        // Standardized bank header
        WriteBoxHeader(Loc.Get("bank.header"), "bright_cyan");
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("bank.tagline"));
        terminal.WriteLine("");

        // Atmospheric description
        DisplayBankDescription();

        // Show NPCs in the bank
        ShowNPCsInLocation();

        // Account summary
        DisplayAccountSummary();

        // Bank menu
        DisplayBankMenu();

        // Status line
        ShowStatusLine();
    }

    /// <summary>
    /// BBS compact display for 80x25 terminal
    /// </summary>
    private void DisplayLocationBBS()
    {
        ShowBBSHeader(Loc.Get("bank.header"));
        // 1-line account summary
        terminal.SetColor("gray");
        terminal.Write(Loc.Get("bank.bbs_on_hand"));
        terminal.SetColor("bright_yellow");
        terminal.Write($"{currentPlayer.Gold:N0}");
        terminal.SetColor("gray");
        terminal.Write(Loc.Get("bank.bbs_bank"));
        terminal.SetColor("yellow");
        terminal.Write($"{currentPlayer.BankGold:N0}");
        terminal.SetColor("gray");
        terminal.Write(Loc.Get("bank.bbs_total"));
        terminal.SetColor("bright_green");
        terminal.Write($"{(currentPlayer.Gold + currentPlayer.BankGold):N0}");
        if (currentPlayer.Loan > 0)
        {
            terminal.SetColor("gray");
            terminal.Write(Loc.Get("bank.bbs_loan"));
            terminal.SetColor("red");
            terminal.Write($"{currentPlayer.Loan:N0}");
        }
        if (currentPlayer.BankGuard)
        {
            terminal.SetColor("cyan");
            terminal.Write(Loc.Get("bank.bbs_guard_on"));
        }
        terminal.WriteLine("");
        ShowBBSNPCs();
        // Menu rows
        ShowBBSMenuRow(("D", "bright_yellow", Loc.Get("bank.deposit")), ("W", "bright_yellow", Loc.Get("bank.withdraw")), ("T", "bright_yellow", Loc.Get("bank.transfer")));
        ShowBBSMenuRow(("L", "bright_yellow", Loc.Get("bank.loan")), ("I", "bright_yellow", Loc.Get("bank.interest")), ("A", "bright_yellow", Loc.Get("bank.history")));
        ShowBBSMenuRow(("G", "bright_yellow", Loc.Get("bank.guard")), ("X", "bright_yellow", Loc.Get("bank.resign_guard")), ("O", "bright_yellow", Loc.Get("bank.robbery")));
        ShowBBSMenuRow(("R", "bright_yellow", Loc.Get("bank.return")), ("Q", "bright_yellow", Loc.Get("bank.return")));
        ShowBBSFooter();
    }

    private void DisplayBannedMessage()
    {
        WriteBoxHeader(Loc.Get("bank.access_denied"), "bright_red");
        terminal.WriteLine("");

        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("bank.banned_guards_block"));
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("bank.banned_recognized"));
        terminal.WriteLine(Loc.Get("bank.banned_owed", currentPlayer.Loan.ToString("N0")));
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("bank.banned_message"));
        terminal.WriteLine(Loc.Get("bank.banned_consequences"));
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("bank.banned_knuckles"));
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(IsScreenReader ? $"Q. {Loc.Get("bank.banned_leave")}" : $"[Q] {Loc.Get("bank.banned_leave")}");
    }

    private void DisplayBankDescription()
    {
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("bank.desc_enter"));
        terminal.WriteLine(Loc.Get("bank.desc_enter2"));
        terminal.WriteLine(Loc.Get("bank.desc_enter3"));
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("bank.desc_vault"));
        terminal.WriteLine(Loc.Get("bank.desc_vault2"));
        terminal.WriteLine(Loc.Get("bank.desc_vault3"));
        terminal.WriteLine("");

        // Describe the banker
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("bank.desc_banker_intro"));
        terminal.SetColor("bright_cyan");
        terminal.Write(Loc.Get("bank.desc_banker_name", BankerName));
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("bank.desc_banker_title"));
        terminal.WriteLine(Loc.Get("bank.desc_banker_desc"));
        terminal.WriteLine(Loc.Get("bank.desc_banker_desc2"));
        terminal.WriteLine(Loc.Get("bank.desc_banker_desc3"));
        terminal.WriteLine("");

        // Show safe contents hint based on player observation skills
        if (currentPlayer.Intelligence > 50 || currentPlayer.Class == CharacterClass.Assassin)
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("bank.desc_vault_estimate", _safeContents.ToString("N0")));
            terminal.WriteLine("");
        }
    }

    private void DisplayAccountSummary()
    {
        WriteSectionHeader(Loc.Get("bank.section_account"), "bright_white");

        terminal.SetColor("white");
        terminal.Write(Loc.Get("bank.gold_on_hand"));
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"{currentPlayer.Gold:N0} gold");

        terminal.SetColor("white");
        terminal.Write(Loc.Get("bank.gold_in_bank"));
        terminal.SetColor("yellow");
        terminal.WriteLine($"{currentPlayer.BankGold:N0} gold");

        terminal.SetColor("white");
        terminal.Write(Loc.Get("bank.total_worth"));
        terminal.SetColor("bright_green");
        terminal.WriteLine($"{(currentPlayer.Gold + currentPlayer.BankGold):N0} gold");

        if (currentPlayer.BankGuard)
        {
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("bank.guard_active", $"{currentPlayer.BankWage:N0}"));
        }

        if (currentPlayer.Loan > 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("bank.loan_status", $"{currentPlayer.Loan:N0}"));
        }

        terminal.WriteLine("");
    }

    private void DisplayBankMenu()
    {
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("bank.section_services"));
        terminal.WriteLine("");

        // Basic services
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("D");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("bank.menu_deposit"));

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("W");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("bank.menu_withdraw"));

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("T");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("bank.menu_transfer"));

        // Secondary services
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("L");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("bank.menu_loan"));

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("I");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("bank.menu_interest"));

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("A");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("bank.menu_history"));

        // Guard & crime
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("G");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("bank.menu_guard"));

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("X");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("bank.menu_resign"));

        terminal.SetColor("red");
        terminal.Write("r");
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("O");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("bank.menu_rob"));

        terminal.WriteLine("");

        // Navigation
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("R");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("bank.menu_return"));

        terminal.WriteLine("");
    }

    private void DisplayLocationSR()
    {
        terminal.ClearScreen();
        terminal.WriteLine(Loc.Get("bank.sr_title"));
        terminal.WriteLine(Loc.Get("bank.sr_tagline"));
        terminal.WriteLine("");

        // Account summary
        terminal.WriteLine($"{Loc.Get("bank.gold_on_hand")} {currentPlayer.Gold:N0}");
        terminal.WriteLine($"{Loc.Get("bank.gold_in_bank")} {currentPlayer.BankGold:N0}");
        terminal.WriteLine($"{Loc.Get("bank.total_worth")} {(currentPlayer.Gold + currentPlayer.BankGold):N0}");
        if (currentPlayer.BankGuard)
            terminal.WriteLine(Loc.Get("bank.sr_guard_status", currentPlayer.BankWage.ToString("N0")));
        if (currentPlayer.Loan > 0)
            terminal.WriteLine(Loc.Get("bank.sr_loan_status", currentPlayer.Loan.ToString("N0")));
        terminal.WriteLine("");

        // Show NPCs
        ShowNPCsInLocation();

        // Menu
        terminal.WriteLine(Loc.Get("bank.sr_banking_services"));
        WriteSRMenuOption("D", Loc.Get("bank.deposit"));
        WriteSRMenuOption("W", Loc.Get("bank.withdraw"));
        WriteSRMenuOption("T", Loc.Get("bank.transfer"));
        WriteSRMenuOption("L", Loc.Get("bank.loan"));
        WriteSRMenuOption("I", Loc.Get("bank.interest"));
        WriteSRMenuOption("A", Loc.Get("bank.history"));
        WriteSRMenuOption("G", Loc.Get("bank.guard"));
        WriteSRMenuOption("X", Loc.Get("bank.resign_guard"));
        WriteSRMenuOption("O", Loc.Get("bank.robbery"));
        WriteSRMenuOption("R", Loc.Get("bank.return"));
        terminal.WriteLine("");

        ShowStatusLine();
    }

    protected override async Task<bool> ProcessChoice(string choice)
    {
        // Handle global quick commands first
        var (handled, shouldExit) = await TryProcessGlobalCommand(choice);
        if (handled) return shouldExit;

        if (string.IsNullOrWhiteSpace(choice))
            return false;

        var upperChoice = choice.ToUpper().Trim();

        switch (upperChoice)
        {
            case "D":
                await DepositGold();
                return false;

            case "W":
                await WithdrawGold();
                return false;

            case "T":
                await TransferGold();
                return false;

            case "L":
                await LoanServices();
                return false;

            case "I":
                await ShowInterestInfo();
                return false;

            case "A":
                await ShowAccountHistory();
                return false;

            case "G":
                await ApplyForGuardDuty();
                return false;

            case "X":
                await ResignGuardDuty();
                return false;

            case "O":
                await AttemptRobbery();
                return false;

            case "R":
            case "Q":
            case "M":
                await NavigateToLocation(GameLocation.MainStreet);
                return true;

            default:
                return await base.ProcessChoice(choice);
        }
    }

    private async Task DepositGold()
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("bank.deposit_gold"), "bright_green");
        terminal.WriteLine("");

        if (currentPlayer.Gold <= 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("bank.no_gold_deposit", BankerName));
            terminal.WriteLine(Loc.Get("bank.empty_pockets"));
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("bank.leans_forward", BankerName));
        terminal.WriteLine(Loc.Get("bank.how_much_deposit", (currentPlayer.Sex == CharacterSex.Male ? Loc.Get("bank.sir") : Loc.Get("bank.madam"))));
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("bank.deposit_amount", $"{currentPlayer.Gold:N0}"));
        terminal.WriteLine(Loc.Get("bank.deposit_balance", $"{currentPlayer.BankGold:N0}"));
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("bank.deposit_prompt"));

        string input = await terminal.GetInput("> ");

        long amount;
        if (input.ToLower() == "all")
        {
            amount = currentPlayer.Gold;
        }
        else if (!long.TryParse(input, out amount) || amount < 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("bank.invalid_amount", BankerName));
            await terminal.PressAnyKey();
            return;
        }

        if (amount == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("bank.transaction_cancelled"));
            await terminal.PressAnyKey();
            return;
        }

        if (amount > currentPlayer.Gold)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("bank.more_than_have", BankerName));
            await terminal.PressAnyKey();
            return;
        }

        // Process deposit (with overflow protection)
        long goldBefore = currentPlayer.Gold;
        long bankBefore = currentPlayer.BankGold;
        currentPlayer.Gold -= amount;
        currentPlayer.BankGold = SafeAddGold(currentPlayer.BankGold, amount);
        _safeContents = SafeAddGold(_safeContents, amount);
        DebugLogger.Instance.LogInfo("GOLD", $"BANK DEPOSIT: {currentPlayer.DisplayName} deposited {amount:N0}g (gold {goldBefore:N0}->{currentPlayer.Gold:N0}, bank {bankBefore:N0}->{currentPlayer.BankGold:N0})");

        terminal.SetColor("bright_green");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("bank.deposit_success", $"{amount:N0}"));
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("bank.counts_coins", BankerName));
        terminal.WriteLine(Loc.Get("bank.safer_than_crown"));
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("bank.new_balance", $"{currentPlayer.BankGold:N0}"));

        // Generate news for large deposits
        if (amount >= 50000)
        {
            NewsSystem.Instance.Newsy(false, $"{currentPlayer.DisplayName} made a substantial deposit at the Ironvault Bank.");
        }

        await terminal.PressAnyKey();
    }

    private async Task WithdrawGold()
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("bank.withdraw_gold"), "bright_yellow");
        terminal.WriteLine("");

        if (currentPlayer.BankGold <= 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("bank.account_empty", BankerName));
            terminal.WriteLine(Loc.Get("bank.earn_some_gold"));
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("bank.disappointed", BankerName));
        terminal.WriteLine(Loc.Get("bank.how_much_withdraw", (currentPlayer.Sex == CharacterSex.Male ? Loc.Get("bank.sir") : Loc.Get("bank.madam"))));
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("bank.withdraw_amount", $"{currentPlayer.BankGold:N0}"));
        terminal.WriteLine(Loc.Get("bank.deposit_amount", $"{currentPlayer.Gold:N0}"));
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("bank.withdraw_prompt"));

        string input = await terminal.GetInput("> ");

        long amount;
        if (input.ToLower() == "all")
        {
            amount = currentPlayer.BankGold;
        }
        else if (!long.TryParse(input, out amount) || amount < 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("bank.invalid_amount2", BankerName));
            await terminal.PressAnyKey();
            return;
        }

        if (amount == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("bank.transaction_cancelled"));
            await terminal.PressAnyKey();
            return;
        }

        if (amount > currentPlayer.BankGold)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("bank.not_that_much", BankerName));
            await terminal.PressAnyKey();
            return;
        }

        // Process withdrawal
        long goldBeforeW = currentPlayer.Gold;
        long bankBeforeW = currentPlayer.BankGold;
        currentPlayer.BankGold -= amount;
        currentPlayer.Gold += amount;
        _safeContents = Math.Max(0, _safeContents - amount);
        DebugLogger.Instance.LogInfo("GOLD", $"BANK WITHDRAW: {currentPlayer.DisplayName} withdrew {amount:N0}g (gold {goldBeforeW:N0}->{currentPlayer.Gold:N0}, bank {bankBeforeW:N0}->{currentPlayer.BankGold:N0})");

        terminal.SetColor("bright_yellow");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("bank.withdraw_success", $"{amount:N0}"));
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("bank.counts_reluctance", BankerName));
        terminal.WriteLine(Loc.Get("bank.careful_gold"));
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("bank.remaining_balance", $"{currentPlayer.BankGold:N0}"));

        await terminal.PressAnyKey();
    }

    private async Task TransferGold()
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("bank.transfer_gold"), "bright_cyan");
        terminal.WriteLine("");

        if (currentPlayer.BankGold <= 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("bank.need_gold_transfer", BankerName));
            terminal.WriteLine(Loc.Get("bank.need_gold_transfer2"));
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("bank.transfer_ledger", BankerName));
        terminal.WriteLine(Loc.Get("bank.whose_account"));
        terminal.WriteLine("");

        // Show list of players/NPCs
        var allNPCs = NPCSpawnSystem.Instance.ActiveNPCs ?? new List<NPC>();
        var validTargets = allNPCs.Where(n => n.IsAlive && !n.IsDead).Take(10).ToList();

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("bank.account_holders"));
        for (int i = 0; i < validTargets.Count; i++)
        {
            terminal.WriteLine($"  {i + 1}. {validTargets[i].Name}");
        }
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("bank.select_recipient"));
        string recipientInput = await terminal.GetInput("> ");

        if (recipientInput == "0")
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("bank.transfer_cancelled"));
            await terminal.PressAnyKey();
            return;
        }

        NPC? recipient = null;
        if (int.TryParse(recipientInput, out int index) && index > 0 && index <= validTargets.Count)
        {
            recipient = validTargets[index - 1];
        }
        else
        {
            recipient = validTargets.FirstOrDefault(n =>
                n.Name.Contains(recipientInput, StringComparison.OrdinalIgnoreCase));
        }

        if (recipient == null)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("bank.cant_find_account", BankerName));
            await terminal.PressAnyKey();
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("bank.transfer_to", recipient.Name));
        terminal.WriteLine(Loc.Get("bank.transfer_your_balance", $"{currentPlayer.BankGold:N0}"));
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("bank.transfer_amount_prompt"));

        string amountInput = await terminal.GetInput("> ");
        if (!long.TryParse(amountInput, out long amount) || amount <= 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("bank.invalid_amount"));
            await terminal.PressAnyKey();
            return;
        }

        if (amount > currentPlayer.BankGold)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("bank.transfer_insufficient"));
            await terminal.PressAnyKey();
            return;
        }

        // Process transfer
        long bankBeforeT = currentPlayer.BankGold;
        currentPlayer.BankGold -= amount;
        recipient.Gold += amount; // NPCs get it as cash
        DebugLogger.Instance.LogInfo("GOLD", $"BANK TRANSFER: {currentPlayer.DisplayName} transferred {amount:N0}g to {recipient.Name} (bank {bankBeforeT:N0}->{currentPlayer.BankGold:N0})");

        terminal.SetColor("bright_green");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("bank.transfer_complete", $"{amount:N0}", recipient.Name));
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("bank.stamps_transfer", BankerName));
        terminal.WriteLine(Loc.Get("bank.recipient_notified"));

        // Improve relationship with recipient
        RelationshipSystem.UpdateRelationship(currentPlayer, recipient, 5, 3, false, false);

        // News for large transfers
        if (amount >= 10000)
        {
            NewsSystem.Instance.Newsy(false, $"{currentPlayer.DisplayName} made a generous transfer to {recipient.Name}.");
        }

        await terminal.PressAnyKey();
    }

    private async Task LoanServices()
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("bank.loan_services"), "bright_yellow");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("bank.adjusts_spectacles", BankerName));
        terminal.WriteLine("");

        if (currentPlayer.Loan > 0)
        {
            // Already has a loan - offer repayment
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("bank.loan_outstanding", $"{currentPlayer.Loan:N0}"));
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("bank.already_loan"));
            terminal.WriteLine("");
            if (IsScreenReader)
            {
                terminal.WriteLine($"  1. {Loc.Get("bank.repay_full", $"{currentPlayer.Loan:N0}")}");
                terminal.WriteLine($"  2. {Loc.Get("bank.partial_repay")}");
                terminal.WriteLine($"  0. {Loc.Get("bank.not_now")}");
            }
            else
            {
                terminal.WriteLine($"  [1] {Loc.Get("bank.repay_full", $"{currentPlayer.Loan:N0}")}");
                terminal.WriteLine($"  [2] {Loc.Get("bank.partial_repay")}");
                terminal.WriteLine($"  [0] {Loc.Get("bank.not_now")}");
            }

            string choice = await terminal.GetInput("> ");

            if (choice == "1")
            {
                if (currentPlayer.Gold >= currentPlayer.Loan)
                {
                    currentPlayer.Gold -= currentPlayer.Loan;
                    currentPlayer.Loan = 0;
                    terminal.SetColor("bright_green");
                    terminal.WriteLine(Loc.Get("bank.loan_repaid"));
                }
                else
                {
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("ui.not_enough_gold_repay"));
                }
            }
            else if (choice == "2")
            {
                terminal.WriteLine(Loc.Get("bank.loan_repay_prompt"));
                string repayInput = await terminal.GetInput("> ");
                if (long.TryParse(repayInput, out long repayAmount) && repayAmount > 0)
                {
                    repayAmount = Math.Min(repayAmount, Math.Min(currentPlayer.Gold, currentPlayer.Loan));
                    currentPlayer.Gold -= repayAmount;
                    currentPlayer.Loan -= repayAmount;
                    terminal.SetColor("green");
                    terminal.WriteLine(Loc.Get("bank.loan_partial_repaid", $"{repayAmount:N0}", $"{currentPlayer.Loan:N0}"));
                }
            }
        }
        else
        {
            // Offer new loan - scales with level for late-game relevance
            // Base: 500 gold, +500 per level, max 250,000 (about 20 monster kills at level 100)
            long maxLoan = Math.Min(500 + (currentPlayer.Level * 500), 250000);

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("bank.financial_assistance"));
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("bank.loan_offer", currentPlayer.Level, $"{maxLoan:N0}"));
            terminal.WriteLine("");

            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("bank.loan_warning"));
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("bank.loan_consequences"));
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("bank.loan_seize"));
            terminal.WriteLine(Loc.Get("bank.loan_collectors"));
            terminal.WriteLine(Loc.Get("bank.loan_darkness"));
            terminal.WriteLine(Loc.Get("bank.loan_banned"));
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("bank.loan_amount_prompt"));
            string loanInput = await terminal.GetInput("> ");

            if (long.TryParse(loanInput, out long loanAmount) && loanAmount > 0)
            {
                loanAmount = Math.Min(loanAmount, maxLoan);
                long goldBeforeLoan = currentPlayer.Gold;
                currentPlayer.Gold += loanAmount;
                currentPlayer.Loan = loanAmount;
                DebugLogger.Instance.LogInfo("GOLD", $"BANK LOAN: {currentPlayer.DisplayName} took loan {loanAmount:N0}g (gold {goldBeforeLoan:N0}->{currentPlayer.Gold:N0})");

                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("bank.loan_approved", $"{loanAmount:N0}"));
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("bank.loan_remember"));
            }
        }

        await terminal.PressAnyKey();
    }

    private async Task ShowInterestInfo()
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("bank.interest_rates"), "bright_cyan");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("bank.produces_pamphlet", BankerName));
        terminal.WriteLine("");

        WriteSectionHeader(Loc.Get("bank.rate_schedule"), "bright_white");
        terminal.WriteLine("");

        terminal.SetColor("green");
        terminal.WriteLine(Loc.Get("bank.interest_savings"));
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("bank.interest_savings_rate"));
        terminal.WriteLine(Loc.Get("bank.interest_savings_calc"));
        terminal.WriteLine("");

        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("bank.interest_loan"));
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("bank.interest_loan_rate"));
        terminal.WriteLine(Loc.Get("bank.interest_loan_compound"));
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("bank.guard_wages_title"));
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("bank.guard_wages_base"));
        terminal.WriteLine(Loc.Get("bank.guard_level_bonus", GameConfig.GuardSalaryPerLevel));
        terminal.WriteLine(Loc.Get("bank.guard_wages_hazard"));
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("bank.questions_open"));

        await terminal.PressAnyKey();
    }

    private async Task ShowAccountHistory()
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("bank.account_summary"), "bright_white");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("bank.reviews_records", BankerName));
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("bank.account_holder", currentPlayer.DisplayName));
        terminal.WriteLine($"Account Status: {(currentPlayer.Loan > 0 ? Loc.Get("bank.account_status_debt") : Loc.Get("bank.account_status_good"))}");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("bank.financial_overview"));
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("bank.gold_on_hand_detail", $"{currentPlayer.Gold:N0}"));
        terminal.WriteLine(Loc.Get("bank.gold_in_bank_detail", $"{currentPlayer.BankGold:N0}"));
        terminal.WriteLine(Loc.Get("bank.total_net_worth", $"{(currentPlayer.Gold + currentPlayer.BankGold - currentPlayer.Loan):N0}"));
        terminal.WriteLine("");

        if (currentPlayer.Loan > 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("bank.loan_details"));
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("bank.outstanding_loan", $"{currentPlayer.Loan:N0}"));
            terminal.WriteLine(Loc.Get("bank.daily_interest_loan", $"{(long)(currentPlayer.Loan * LoanInterestRate):N0}"));
            terminal.WriteLine("");
        }

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("bank.account_statistics"));
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("bank.interest_earned", $"{currentPlayer.Interest:N0}"));
        terminal.WriteLine(Loc.Get("bank.daily_interest_savings", $"{(long)(currentPlayer.BankGold * DailyInterestRate):N0}"));

        if (currentPlayer.BankGuard)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("bank.guard_employment"));
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("bank.status_active"));
            terminal.WriteLine(Loc.Get("bank.daily_wage", $"{currentPlayer.BankWage:N0}"));
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("bank.account_since", BankerName));

        await terminal.PressAnyKey();
    }

    private async Task ApplyForGuardDuty()
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("bank.guard_duty"), "bright_yellow");
        terminal.WriteLine("");

        if (currentPlayer.BankGuard)
        {
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("bank.guard_already", BankerName));
            terminal.WriteLine(Loc.Get("bank.guard_current_wage", currentPlayer.BankWage.ToString("N0")));
            await terminal.PressAnyKey();
            return;
        }

        // Check requirements
        if (currentPlayer.Level < GameConfig.MinLevelForGuard)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("bank.guard_level_req_head", BankerName));
            terminal.WriteLine(Loc.Get("bank.guard_level_req", GameConfig.MinLevelForGuard));
            terminal.WriteLine(Loc.Get("bank.guard_level_req_come_back"));
            await terminal.PressAnyKey();
            return;
        }

        if (currentPlayer.Darkness > GameConfig.MaxDarknessForGuard)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("bank.guard_darkness_head", BankerName));
            terminal.WriteLine(Loc.Get("bank.guard_darkness_check"));
            terminal.WriteLine(Loc.Get("bank.guard_darkness_deny"));
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("bank.guard_reviews", BankerName));
        terminal.WriteLine("");

        int guardWage = 1000 + (currentPlayer.Level * GameConfig.GuardSalaryPerLevel);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("bank.guard_position_title"));
        terminal.WriteLine(Loc.Get("bank.guard_daily_wage", guardWage.ToString("N0")));
        terminal.WriteLine(Loc.Get("bank.guard_duties"));
        terminal.WriteLine(Loc.Get("bank.guard_risk"));
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("bank.guard_accept_prompt"));

        string accept = await terminal.GetInput("> ");

        if (accept.ToUpper() == "Y")
        {
            currentPlayer.BankGuard = true;
            currentPlayer.BankWage = guardWage;
            _activeGuardNames.Add(currentPlayer.DisplayName);

            terminal.SetColor("bright_green");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("bank.guard_hired"));
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("bank.guard_handshake", BankerName));
            terminal.WriteLine(Loc.Get("bank.guard_welcome"));
            terminal.WriteLine(Loc.Get("bank.guard_sharp"));

            // Announce publicly?
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("bank.guard_public_prompt"));
            terminal.WriteLine(Loc.Get("bank.guard_public_hint"));

            string goPublic = await terminal.GetInput("> ");
            if (goPublic.ToUpper() == "Y")
            {
                NewsSystem.Instance.Newsy(true, $"{currentPlayer.DisplayName} has been hired as a guard at the Ironvault Bank!");
            }
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("bank.guard_another_time", BankerName));
        }

        await terminal.PressAnyKey();
    }

    private async Task ResignGuardDuty()
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("bank.resign_guard"), "yellow");
        terminal.WriteLine("");

        if (!currentPlayer.BankGuard)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("bank.resign_not_employed"));
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("bank.resign_disappointed", BankerName));
        terminal.WriteLine(Loc.Get("bank.resign_certain"));
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("bank.resign_wage_current", currentPlayer.BankWage.ToString("N0")));
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("bank.resign_confirm"));

        string confirm = await terminal.GetInput("> ");

        if (confirm.ToUpper() == "Y")
        {
            currentPlayer.BankGuard = false;
            currentPlayer.BankWage = 0;
            _activeGuardNames.Remove(currentPlayer.DisplayName);

            terminal.SetColor("gray");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("bank.resign_done"));
            terminal.WriteLine(Loc.Get("bank.resign_thanked", BankerName));
        }
        else
        {
            terminal.SetColor("green");
            terminal.WriteLine(Loc.Get("bank.resign_remain"));
        }

        await terminal.PressAnyKey();
    }

    private async Task AttemptRobbery()
    {
        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("bank.robbery_header"), "bright_red");
        terminal.WriteLine("");

        // Check if player is a guard
        if (currentPlayer.BankGuard)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("bank.rob_guard_conflict"));
            terminal.WriteLine(Loc.Get("bank.rob_guard_conflict2"));
            await terminal.PressAnyKey();
            return;
        }

        // Initialize robbery attempts if not yet set this session
        // (BankRobberyAttempts is not serialized — starts at 0 each session)
        if (currentPlayer.BankRobberyAttempts <= 0 && _robberyAttemptsToday == 0)
        {
            currentPlayer.BankRobberyAttempts = GameConfig.DefaultBankRobberyAttempts;
        }

        // Check daily robbery limit
        if (currentPlayer.BankRobberyAttempts <= 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("bank.rob_exhausted"));
            terminal.WriteLine(Loc.Get("bank.rob_exhausted2"));
            await terminal.PressAnyKey();
            return;
        }

        // Show the stakes
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("bank.rob_survey"));
        terminal.WriteLine("");

        int guardCount = CalculateGuardCount();
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("bank.rob_safe_contents", _safeContents.ToString("N0")));
        terminal.WriteLine(Loc.Get("bank.rob_bank_guards", guardCount));
        terminal.WriteLine(Loc.Get("bank.rob_player_guards", _activeGuardNames.Count));
        terminal.WriteLine(Loc.Get("bank.rob_alarm"));
        terminal.WriteLine("");

        terminal.SetColor("bright_red");
        terminal.WriteLine(Loc.Get("bank.rob_warning_title"));
        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("bank.rob_chivalry"));
        terminal.WriteLine(Loc.Get("bank.rob_darkness"));
        terminal.WriteLine(Loc.Get("bank.rob_wanted"));
        terminal.WriteLine(Loc.Get("bank.rob_death"));
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("bank.rob_options"));
        if (IsScreenReader)
        {
            terminal.WriteLine($"  R. {Loc.Get("bank.rob_option_rob")}");
            terminal.WriteLine($"  I. {Loc.Get("bank.rob_option_inspect")}");
            terminal.WriteLine($"  A. {Loc.Get("bank.rob_option_abort")}");
        }
        else
        {
            terminal.WriteLine($"  [R] {Loc.Get("bank.rob_option_rob")}");
            terminal.WriteLine($"  [I] {Loc.Get("bank.rob_option_inspect")}");
            terminal.WriteLine($"  [A] {Loc.Get("bank.rob_option_abort")}");
        }

        string choice = await terminal.GetInput("> ");

        switch (choice.ToUpper())
        {
            case "R":
                terminal.SetColor("red");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("bank.rob_confirm"));
                string confirm = await terminal.GetInput("> ");

                if (confirm.ToUpper() == "ROB")
                {
                    await ExecuteRobbery(guardCount);
                }
                else
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("bank.rob_cold_feet"));
                }
                break;

            case "I":
                await InspectGuards(guardCount);
                break;

            default:
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("bank.rob_leave"));
                break;
        }

        await terminal.PressAnyKey();
    }

    private async Task InspectGuards(int guardCount)
    {
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("bank.inspect_observe"));
        terminal.WriteLine("");

        // Captain
        terminal.SetColor("bright_red");
        terminal.WriteLine(Loc.Get("bank.inspect_captain"));
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("bank.inspect_captain_desc"));

        // Regular guards
        for (int i = 1; i < guardCount; i++)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("bank.inspect_guard", i));
        }

        // Possible guard dog
        var random = Random.Shared;
        if (random.Next(2) == 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("bank.inspect_dog"));
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("bank.inspect_dog_desc"));
        }

        // Player guards
        if (_activeGuardNames.Count > 0)
        {
            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("bank.inspect_adventurers"));
            foreach (var name in _activeGuardNames.Take(5))
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {name}");
            }
        }

        await Task.CompletedTask;
    }

    private async Task ExecuteRobbery(int guardCount)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine(Loc.Get("bank.robbery_banner_top"));
        terminal.WriteLine(Loc.Get("bank.robbery_banner_mid"));
        terminal.WriteLine(Loc.Get("bank.robbery_banner_top"));
        terminal.WriteLine("");

        // Consequences happen regardless of outcome
        currentPlayer.Chivalry = 0;
        currentPlayer.Darkness += 100;
        currentPlayer.BankRobberyAttempts--;
        currentPlayer.WantedLvl += 5;
        _robberyAttemptsToday++;

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("bank.rob_draw_weapon"));
        terminal.WriteLine(Loc.Get("bank.rob_banker_screams", BankerName));
        terminal.WriteLine(Loc.Get("bank.rob_alarms"));
        terminal.WriteLine("");

        await Task.Delay(1500);

        // Create monsters for guards — scaled to be tougher than same-level dungeon monsters.
        // Bank guards are elite professionals; robbing a bank should be a serious challenge.
        // Reference: dungeon monsters use 50*level + level^1.2*15 for HP.
        var monsters = new List<Monster>();
        int level = currentPlayer.Level;

        // Captain — elite combatant, should be a serious challenge (3x dungeon monster)
        long captainHP = (long)(150 * level + Math.Pow(level, 1.3) * 40);
        var captain = new Monster
        {
            Name = "Captain of the Guard",
            Level = level + 5, // Loot quality scales with monster level
            HP = captainHP,
            MaxHP = captainHP,
            Strength = 25 + (int)(level * 4.0),
            Defence = 20 + (int)(level * 3.0),
            WeapPow = 20 + level * 2,
            ArmPow = 15 + level,
            WeaponName = "Enchanted Broadsword",
            ArmorName = "Reinforced Plate"
        };
        monsters.Add(captain);

        // Regular guards — trained professionals (2x dungeon monster)
        for (int i = 1; i < guardCount; i++)
        {
            long guardHP = (long)(100 * level + Math.Pow(level, 1.2) * 25);
            var guard = new Monster
            {
                Name = "Bank Guard",
                Level = level,
                HP = guardHP,
                MaxHP = guardHP,
                Strength = 18 + level * 3,
                Defence = 15 + level * 2,
                WeapPow = 15 + level,
                ArmPow = 10 + level * 2 / 3,
                WeaponName = "Halberd",
                ArmorName = "Half-Plate"
            };
            monsters.Add(guard);
        }

        // War hounds — always present, fast and vicious
        var random = Random.Shared;
        int houndCount = 1 + random.Next(3); // 1-3 war hounds
        for (int h = 0; h < houndCount; h++)
        {
            long dogHP = (long)(60 * level + Math.Pow(level, 1.2) * 15);
            var dog = new Monster
            {
                Name = "War Hound",
                Level = level - 5,
                HP = dogHP,
                MaxHP = dogHP,
                Strength = 15 + (int)(level * 2.5),
                Defence = 8 + level,
                WeapPow = 15 + level,
                ArmPow = 5 + level / 3,
                WeaponName = "Iron-Tipped Jaws",
                ArmorName = "Studded Barding"
            };
            monsters.Add(dog);
        }

        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("bank.rob_enemies_rush", monsters.Count));
        terminal.WriteLine("");

        await Task.Delay(1000);

        // Real interactive combat against the guards
        var combatEngine = new CombatEngine(terminal);
        var result = await combatEngine.PlayerVsMonsters(currentPlayer, monsters, offerMonkEncounter: false);

        if (result.Outcome == CombatOutcome.Victory)
        {
            // Calculate loot (25% of safe) — exclude the robber's own deposits to prevent
            // deposit-rob-redeposit exploit (player was stealing their own money back endlessly)
            long otherPeoplesGold = Math.Max(0, _safeContents - currentPlayer.BankGold);
            long stolenGold = otherPeoplesGold / 4;

            if (stolenGold <= 0)
            {
                // Nothing to steal — vault only contains the robber's own gold
                terminal.WriteLine("");
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("bank.rob_vault_empty"));
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("bank.rob_only_your_gold"));
            }
            else
            {
                long goldBeforeRob = currentPlayer.Gold;
                currentPlayer.Gold = SafeAddGold(currentPlayer.Gold, stolenGold);
                _safeContents = Math.Max(0, _safeContents - stolenGold);
                DebugLogger.Instance.LogInfo("GOLD", $"BANK ROBBERY: {currentPlayer.DisplayName} stole {stolenGold:N0}g (gold {goldBeforeRob:N0}->{currentPlayer.Gold:N0})");

                terminal.WriteLine("");
                WriteBoxHeader(Loc.Get("bank.rob_success", stolenGold.ToString("N0")), "bright_green");
                terminal.WriteLine("");

                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("bank.rob_flee"));
                terminal.WriteLine(Loc.Get("bank.rob_authorities"));
                long vaultRemaining = Math.Max(0, _safeContents - currentPlayer.BankGold);
                terminal.WriteLine(Loc.Get("bank.rob_vault_remaining", vaultRemaining.ToString("N0")));

                NewsSystem.Instance.Newsy(true, $"BANK ROBBERY! {currentPlayer.DisplayName} robbed the Ironvault Bank of {stolenGold:N0} gold!");
            }
        }
        else if (result.Outcome == CombatOutcome.PlayerDied)
        {
            // Prevent permadeath from bank robbery — leave at 1 HP
            if (currentPlayer.HP <= 0)
                currentPlayer.HP = 1;

            terminal.WriteLine("");
            WriteBoxHeader(Loc.Get("bank.defeated"), "red");
            terminal.WriteLine("");

            // Confiscate gold on hand + some bank deposits as damages
            long fineOnHand = currentPlayer.Gold;
            long fineFromBank = Math.Min(currentPlayer.BankGold, (long)(currentPlayer.Level * 500));
            currentPlayer.Gold = 0;
            currentPlayer.BankGold -= fineFromBank;
            long totalFine = fineOnHand + fineFromBank;

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("bank.rob_beaten"));
            if (totalFine > 0)
            {
                terminal.WriteLine(Loc.Get("bank.rob_confiscate", totalFine.ToString("N0")));
                if (fineFromBank > 0)
                    terminal.WriteLine(Loc.Get("bank.rob_confiscate_bank", fineFromBank.ToString("N0")));
            }

            NewsSystem.Instance.Newsy(true, $"{currentPlayer.DisplayName} attempted to rob the Ironvault Bank but was defeated by guards!");
        }
        else
        {
            // Fled — still take consequences but no gold stolen
            terminal.SetColor("red");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("bank.rob_fled_empty"));
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("bank.rob_face_seen"));

            NewsSystem.Instance.Newsy(true, $"{currentPlayer.DisplayName} attempted to rob the Ironvault Bank but fled!");
        }
    }

    private int CalculateGuardCount()
    {
        int guards = 4; // Base guards (increased from 2)

        // More guards for richer banks
        if (_safeContents > 100000) guards++;
        if (_safeContents > 250000) guards++;
        if (_safeContents > 500000) guards += 2;
        if (_safeContents > 1000000) guards += 3;

        // More guards after recent robbery attempts
        guards += _robberyAttemptsToday * 2;

        return Math.Min(guards, 12); // Cap at 12
    }

    /// <summary>
    /// Daily maintenance - pay guard wages and calculate interest
    /// Called by DailySystemManager
    /// </summary>
    public static void ProcessDailyMaintenance(Character player)
    {
        // Pay guard wages
        if (player.BankGuard && player.IsAlive)
        {
            player.BankGold += player.BankWage;
            DebugLogger.Instance.LogInfo("GOLD", $"BANK GUARD WAGE: {player.DisplayName} +{player.BankWage:N0}g wage (bank now {player.BankGold:N0})");
        }

        // Add interest on savings
        if (player.BankGold > 0)
        {
            long interest = (long)(player.BankGold * DailyInterestRate);
            if (interest > 0)
            {
                player.Interest += interest;
                player.BankGold += interest;
                DebugLogger.Instance.LogInfo("GOLD", $"BANK INTEREST: {player.DisplayName} +{interest:N0}g interest (bank now {player.BankGold:N0})");
            }
        }

        // Charge loan interest and enforce consequences (with overflow protection)
        if (player.Loan > 0)
        {
            long loanInterest = (long)(player.Loan * LoanInterestRate);
            player.Loan = SafeAddGold(player.Loan, loanInterest);
            DebugLogger.Instance.LogInfo("GOLD", $"LOAN INTEREST: {player.DisplayName} loan +{loanInterest:N0}g interest (loan now {player.Loan:N0})");

            // LOAN CONSEQUENCES - escalating based on debt level
            ProcessLoanConsequences(player);
        }

        // Reset daily robbery attempts
        player.BankRobberyAttempts = GameConfig.DefaultBankRobberyAttempts;
    }

    /// <summary>
    /// Process consequences for unpaid loans
    /// </summary>
    private static void ProcessLoanConsequences(Character player)
    {
        if (player.Loan <= 0) return;

        // Threshold 1: Loan over 5,000 - Auto-seize bank deposits
        if (player.Loan > 5000 && player.BankGold > 0)
        {
            long seizure = Math.Min(player.BankGold, player.Loan);
            player.BankGold -= seizure;
            player.Loan -= seizure;
            // GD.Print($"[Bank] Seized {seizure} gold from {player.DisplayName}'s account to cover loan");
            NewsSystem.Instance.Newsy(false, $"The Ironvault Bank seized {seizure:N0} gold from {player.DisplayName}'s account.");
        }

        // Threshold 2: Loan over 10,000 - Debt collectors visit (deal damage)
        if (player.Loan > 10000)
        {
            // Debt collectors rough them up
            long damage = Math.Min(player.HP - 1, player.Loan / 500);
            if (damage > 0)
            {
                player.HP -= damage;
                // GD.Print($"[Bank] Debt collectors dealt {damage} damage to {player.DisplayName}");
                NewsSystem.Instance.Newsy(true, $"Debt collectors from the Ironvault Bank paid {player.DisplayName} a painful visit.");
            }

            // Increase darkness and wanted level
            player.Darkness += 5;
            player.WantedLvl += 1;
        }

        // Threshold 3: Loan over 25,000 - Confiscate gold on hand
        if (player.Loan > 25000 && player.Gold > 0)
        {
            long confiscate = Math.Min(player.Gold, player.Loan / 2);
            player.Gold -= confiscate;
            player.Loan -= confiscate;
            // GD.Print($"[Bank] Confiscated {confiscate} gold from {player.DisplayName}'s purse");
            NewsSystem.Instance.Newsy(true, $"Bank enforcers confiscated {confiscate:N0} gold directly from {player.DisplayName}!");
        }

        // Threshold 4: Loan over 50,000 - Banned from guard duty, more severe penalties
        if (player.Loan > 50000)
        {
            if (player.BankGuard)
            {
                player.BankGuard = false;
                player.BankWage = 0;
                NewsSystem.Instance.Newsy(true, $"{player.DisplayName} was fired from bank guard duty for unpaid debts!");
            }

            // Severe reputation hit
            player.Darkness += 20;
            player.WantedLvl += 3;
            player.Chivalry = Math.Max(0, player.Chivalry - 50);
        }
    }

    /// <summary>
    /// Check if player is banned from bank services due to massive debt
    /// </summary>
    public static bool IsPlayerBannedFromBank(Character player)
    {
        return player.Loan > 100000; // Banned if loan exceeds 100k
    }

    /// <summary>
    /// Get current safe contents
    /// </summary>
    public static long GetSafeContents() => _safeContents;

    /// <summary>
    /// Get active guard count
    /// </summary>
    public static int GetActiveGuardCount() => _activeGuardNames.Count;
}
