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

        if (DoorMode.IsInDoorMode)
        {
            DisplayLocationBBS();
            return;
        }

        // Standardized bank header
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔═════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine($"║{"THE IRONVAULT BANK".PadLeft((77 + 18) / 2).PadRight(77)}║");
        terminal.WriteLine("╚═════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine("              \"Where Your Fortune is Our Foundation\"");
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
        ShowBBSHeader("THE IRONVAULT BANK");
        // 1-line account summary
        terminal.SetColor("gray");
        terminal.Write(" On Hand:");
        terminal.SetColor("bright_yellow");
        terminal.Write($"{currentPlayer.Gold:N0}");
        terminal.SetColor("gray");
        terminal.Write("  Bank:");
        terminal.SetColor("yellow");
        terminal.Write($"{currentPlayer.BankGold:N0}");
        terminal.SetColor("gray");
        terminal.Write("  Total:");
        terminal.SetColor("bright_green");
        terminal.Write($"{(currentPlayer.Gold + currentPlayer.BankGold):N0}");
        if (currentPlayer.Loan > 0)
        {
            terminal.SetColor("gray");
            terminal.Write("  Loan:");
            terminal.SetColor("red");
            terminal.Write($"{currentPlayer.Loan:N0}");
        }
        if (currentPlayer.BankGuard)
        {
            terminal.SetColor("cyan");
            terminal.Write($"  Guard:ON");
        }
        terminal.WriteLine("");
        ShowBBSNPCs();
        // Menu rows
        ShowBBSMenuRow(("D", "bright_yellow", "Deposit"), ("W", "bright_yellow", "Withdraw"), ("T", "bright_yellow", "Transfer"));
        ShowBBSMenuRow(("L", "bright_yellow", "Loan"), ("I", "bright_yellow", "Interest"), ("A", "bright_yellow", "History"));
        ShowBBSMenuRow(("G", "bright_yellow", "Guard Duty"), ("*", "bright_yellow", "Resign Guard"), ("O", "bright_yellow", "rOb Bank!"));
        ShowBBSMenuRow(("R", "bright_yellow", "Return"), ("Q", "bright_yellow", "Return"));
        ShowBBSFooter();
    }

    private void DisplayBannedMessage()
    {
        terminal.SetColor("bright_red");
        terminal.WriteLine("+==========================================================+");
        terminal.WriteLine("|                  !!! ACCESS DENIED !!!                   |");
        terminal.WriteLine("+==========================================================+");
        terminal.WriteLine("");

        terminal.SetColor("red");
        terminal.WriteLine("As you approach the bank doors, two massive guards block your path.");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($"\"YOU!\" one of them snarls, recognizing you immediately.");
        terminal.WriteLine($"\"You owe the Ironvault Bank over {currentPlayer.Loan:N0} gold!\"");
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine("\"You are BANNED from this establishment until you pay off your debt.\"");
        terminal.WriteLine("\"The only way in is to repay what you owe... or face the consequences.\"");
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine("The guards crack their knuckles menacingly.");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("[Q] Leave (the only option)");
    }

    private void DisplayBankDescription()
    {
        terminal.SetColor("white");
        terminal.WriteLine("You step through the massive bronze doors into the Ironvault Bank, the");
        terminal.WriteLine("oldest and most secure financial institution in the realm. The marble");
        terminal.WriteLine("floors gleam under enchanted crystal chandeliers that never flicker.");
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine("Massive dwarven-forged vault doors dominate the far wall, inscribed with");
        terminal.WriteLine("ancient runes of protection. Armed guards stand at attention, their");
        terminal.WriteLine("eyes constantly scanning for trouble.");
        terminal.WriteLine("");

        // Describe the banker
        terminal.SetColor("cyan");
        terminal.Write($"Behind an ornate mahogany counter, ");
        terminal.SetColor("bright_cyan");
        terminal.Write($"{BankerName}");
        terminal.SetColor("cyan");
        terminal.WriteLine($" the gnome banker");
        terminal.WriteLine("adjusts his golden spectacles and peers up at you with shrewd eyes.");
        terminal.WriteLine("Despite his small stature, he radiates an aura of absolute authority");
        terminal.WriteLine("over all financial matters.");
        terminal.WriteLine("");

        // Show safe contents hint based on player observation skills
        if (currentPlayer.Intelligence > 50 || currentPlayer.Class == CharacterClass.Assassin)
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine($"[You estimate the vault holds around {_safeContents:N0} gold coins...]");
            terminal.WriteLine("");
        }
    }

    private void DisplayAccountSummary()
    {
        terminal.SetColor("bright_white");
        terminal.WriteLine("--- Your Account Status ---");

        terminal.SetColor("white");
        terminal.Write("Gold on Hand:  ");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"{currentPlayer.Gold:N0} gold");

        terminal.SetColor("white");
        terminal.Write("Gold in Bank:  ");
        terminal.SetColor("yellow");
        terminal.WriteLine($"{currentPlayer.BankGold:N0} gold");

        terminal.SetColor("white");
        terminal.Write("Total Worth:   ");
        terminal.SetColor("bright_green");
        terminal.WriteLine($"{(currentPlayer.Gold + currentPlayer.BankGold):N0} gold");

        if (currentPlayer.BankGuard)
        {
            terminal.SetColor("cyan");
            terminal.WriteLine($"Guard Status:  ACTIVE (Wage: {currentPlayer.BankWage:N0}/day)");
        }

        if (currentPlayer.Loan > 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"Outstanding Loan: {currentPlayer.Loan:N0} gold (5% daily interest!)");
        }

        terminal.WriteLine("");
    }

    private void DisplayBankMenu()
    {
        terminal.SetColor("cyan");
        terminal.WriteLine("Banking Services:");
        terminal.WriteLine("");

        // Basic services
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("D");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("eposit Gold       ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("W");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("ithdraw Gold      ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("T");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine("ransfer to Player");

        // Secondary services
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("L");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("oan Services     ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("I");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("nterest Info      ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("A");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine("ccount History");

        // Guard & crime
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("G");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("uard Duty        ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("*");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(" Resign Guard     ");

        terminal.SetColor("red");
        terminal.Write("r");
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("O");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("red");
        terminal.WriteLine("b the Bank!");

        terminal.WriteLine("");

        // Navigation
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("R");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("gray");
        terminal.Write(" Return to Main Street  ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("Q");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("gray");
        terminal.WriteLine(" Return to Main Street");

        terminal.WriteLine("");
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

            case "*":
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
        terminal.SetColor("bright_green");
        terminal.WriteLine("=== DEPOSIT GOLD ===");
        terminal.WriteLine("");

        if (currentPlayer.Gold <= 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"{BankerName} sighs heavily. \"You have no gold to deposit, my friend.\"");
            terminal.WriteLine("\"Come back when your pockets aren't so... empty.\"");
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("cyan");
        terminal.WriteLine($"{BankerName} leans forward eagerly, his eyes gleaming.");
        terminal.WriteLine($"\"How much would you like to deposit, {(currentPlayer.Sex == CharacterSex.Male ? "sir" : "madam")}?\"");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($"Gold on hand: {currentPlayer.Gold:N0}");
        terminal.WriteLine($"Current bank balance: {currentPlayer.BankGold:N0}");
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine("Enter amount (or 'all' for everything, 0 to cancel):");

        string input = await terminal.GetInput("> ");

        long amount;
        if (input.ToLower() == "all")
        {
            amount = currentPlayer.Gold;
        }
        else if (!long.TryParse(input, out amount) || amount < 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"\"{BankerName} frowns. \"That's not a valid amount.\"");
            await terminal.PressAnyKey();
            return;
        }

        if (amount == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("Transaction cancelled.");
            await terminal.PressAnyKey();
            return;
        }

        if (amount > currentPlayer.Gold)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"{BankerName} chuckles. \"You can't deposit more than you have!\"");
            await terminal.PressAnyKey();
            return;
        }

        // Process deposit (with overflow protection)
        currentPlayer.Gold -= amount;
        currentPlayer.BankGold = SafeAddGold(currentPlayer.BankGold, amount);
        _safeContents = SafeAddGold(_safeContents, amount);

        terminal.SetColor("bright_green");
        terminal.WriteLine("");
        terminal.WriteLine($"You deposit {amount:N0} gold coins.");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine($"{BankerName} counts the coins with practiced precision, then stamps your receipt.");
        terminal.WriteLine("\"Your gold is now safer than the King's crown jewels!\"");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($"New bank balance: {currentPlayer.BankGold:N0} gold");

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
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("=== WITHDRAW GOLD ===");
        terminal.WriteLine("");

        if (currentPlayer.BankGold <= 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"{BankerName} shakes his head sadly.");
            terminal.WriteLine("\"Your account is empty. Perhaps you should try earning some gold first?\"");
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("cyan");
        terminal.WriteLine($"{BankerName} looks slightly disappointed but professional.");
        terminal.WriteLine($"\"How much would you like to withdraw, {(currentPlayer.Sex == CharacterSex.Male ? "sir" : "madam")}?\"");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($"Bank balance: {currentPlayer.BankGold:N0}");
        terminal.WriteLine($"Gold on hand: {currentPlayer.Gold:N0}");
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine("Enter amount (or 'all' for everything, 0 to cancel):");

        string input = await terminal.GetInput("> ");

        long amount;
        if (input.ToLower() == "all")
        {
            amount = currentPlayer.BankGold;
        }
        else if (!long.TryParse(input, out amount) || amount < 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"{BankerName} frowns. \"Invalid amount.\"");
            await terminal.PressAnyKey();
            return;
        }

        if (amount == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("Transaction cancelled.");
            await terminal.PressAnyKey();
            return;
        }

        if (amount > currentPlayer.BankGold)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"{BankerName} sighs. \"You don't have that much in your account.\"");
            await terminal.PressAnyKey();
            return;
        }

        // Process withdrawal
        currentPlayer.BankGold -= amount;
        currentPlayer.Gold += amount;
        _safeContents = Math.Max(0, _safeContents - amount);

        terminal.SetColor("bright_yellow");
        terminal.WriteLine("");
        terminal.WriteLine($"You withdraw {amount:N0} gold coins.");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine($"{BankerName} counts out your gold with a hint of reluctance.");
        terminal.WriteLine("\"Do be careful with all that gold out there. The streets aren't always safe.\"");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($"Remaining bank balance: {currentPlayer.BankGold:N0} gold");

        await terminal.PressAnyKey();
    }

    private async Task TransferGold()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("=== TRANSFER GOLD ===");
        terminal.WriteLine("");

        if (currentPlayer.BankGold <= 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"{BankerName} shakes his head.");
            terminal.WriteLine("\"You need gold in your account to make a transfer.\"");
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("cyan");
        terminal.WriteLine($"{BankerName} pulls out a transfer ledger.");
        terminal.WriteLine("\"To whose account would you like to transfer gold?\"");
        terminal.WriteLine("");

        // Show list of players/NPCs
        var allNPCs = NPCSpawnSystem.Instance.ActiveNPCs ?? new List<NPC>();
        var validTargets = allNPCs.Where(n => n.IsAlive).Take(10).ToList();

        terminal.SetColor("white");
        terminal.WriteLine("Known account holders:");
        for (int i = 0; i < validTargets.Count; i++)
        {
            terminal.WriteLine($"  {i + 1}. {validTargets[i].Name}");
        }
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine("Enter recipient number (or name), 0 to cancel:");
        string recipientInput = await terminal.GetInput("> ");

        if (recipientInput == "0")
        {
            terminal.SetColor("gray");
            terminal.WriteLine("Transfer cancelled.");
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
            terminal.WriteLine($"{BankerName} checks his records. \"I can't find that account holder.\"");
            await terminal.PressAnyKey();
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine($"Transfer to: {recipient.Name}");
        terminal.WriteLine($"Your balance: {currentPlayer.BankGold:N0}");
        terminal.WriteLine("");
        terminal.WriteLine("Enter amount to transfer:");

        string amountInput = await terminal.GetInput("> ");
        if (!long.TryParse(amountInput, out long amount) || amount <= 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine("Invalid amount.");
            await terminal.PressAnyKey();
            return;
        }

        if (amount > currentPlayer.BankGold)
        {
            terminal.SetColor("red");
            terminal.WriteLine("Insufficient funds.");
            await terminal.PressAnyKey();
            return;
        }

        // Process transfer
        currentPlayer.BankGold -= amount;
        recipient.Gold += amount; // NPCs get it as cash

        terminal.SetColor("bright_green");
        terminal.WriteLine("");
        terminal.WriteLine($"Transfer complete! {amount:N0} gold sent to {recipient.Name}.");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine($"{BankerName} stamps the transfer document with a flourish.");
        terminal.WriteLine("\"The recipient has been notified. Excellent doing business with you!\"");

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
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("=== LOAN SERVICES ===");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine($"{BankerName} adjusts his spectacles and examines your file.");
        terminal.WriteLine("");

        if (currentPlayer.Loan > 0)
        {
            // Already has a loan - offer repayment
            terminal.SetColor("red");
            terminal.WriteLine($"Outstanding loan: {currentPlayer.Loan:N0} gold (5% daily interest!)");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("\"You already have an outstanding loan. Would you like to repay it?\"");
            terminal.WriteLine("");
            terminal.WriteLine($"  [1] Repay full amount ({currentPlayer.Loan:N0} gold)");
            terminal.WriteLine($"  [2] Partial repayment");
            terminal.WriteLine($"  [0] Not now");

            string choice = await terminal.GetInput("> ");

            if (choice == "1")
            {
                if (currentPlayer.Gold >= currentPlayer.Loan)
                {
                    currentPlayer.Gold -= currentPlayer.Loan;
                    currentPlayer.Loan = 0;
                    terminal.SetColor("bright_green");
                    terminal.WriteLine("Loan fully repaid! Your credit is restored.");
                }
                else
                {
                    terminal.SetColor("red");
                    terminal.WriteLine("You don't have enough gold to repay the full amount.");
                }
            }
            else if (choice == "2")
            {
                terminal.WriteLine("Enter repayment amount:");
                string repayInput = await terminal.GetInput("> ");
                if (long.TryParse(repayInput, out long repayAmount) && repayAmount > 0)
                {
                    repayAmount = Math.Min(repayAmount, Math.Min(currentPlayer.Gold, currentPlayer.Loan));
                    currentPlayer.Gold -= repayAmount;
                    currentPlayer.Loan -= repayAmount;
                    terminal.SetColor("green");
                    terminal.WriteLine($"You repaid {repayAmount:N0} gold. Remaining loan: {currentPlayer.Loan:N0}");
                }
            }
        }
        else
        {
            // Offer new loan - scales with level for late-game relevance
            // Base: 500 gold, +500 per level, max 250,000 (about 20 monster kills at level 100)
            long maxLoan = Math.Min(500 + (currentPlayer.Level * 500), 250000);

            terminal.SetColor("white");
            terminal.WriteLine("\"Ah, looking for some financial assistance, are we?\"");
            terminal.WriteLine("");
            terminal.WriteLine($"Based on your level ({currentPlayer.Level}), I can offer you up to {maxLoan:N0} gold.");
            terminal.WriteLine("");

            terminal.SetColor("yellow");
            terminal.WriteLine("WARNING: Loans accrue 5% daily interest!");
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine("FAILURE TO REPAY WILL RESULT IN:");
            terminal.SetColor("white");
            terminal.WriteLine("  - Automatic seizure of bank deposits");
            terminal.WriteLine("  - Debt collectors visiting you (they're not gentle)");
            terminal.WriteLine("  - Increase in your Darkness and Wanted level");
            terminal.WriteLine("  - Being banned from the bank entirely");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("Enter loan amount (0 to cancel):");
            string loanInput = await terminal.GetInput("> ");

            if (long.TryParse(loanInput, out long loanAmount) && loanAmount > 0)
            {
                loanAmount = Math.Min(loanAmount, maxLoan);
                currentPlayer.Gold += loanAmount;
                currentPlayer.Loan = loanAmount;

                terminal.SetColor("bright_green");
                terminal.WriteLine($"Loan approved! {loanAmount:N0} gold has been added to your purse.");
                terminal.SetColor("red");
                terminal.WriteLine("Remember: 5% daily interest. Don't make us send the collectors...");
            }
        }

        await terminal.PressAnyKey();
    }

    private async Task ShowInterestInfo()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("=== INTEREST & RATES ===");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine($"{BankerName} produces a well-worn pamphlet.");
        terminal.WriteLine("");

        terminal.SetColor("bright_white");
        terminal.WriteLine("IRONVAULT BANK RATE SCHEDULE");
        terminal.WriteLine("============================");
        terminal.WriteLine("");

        terminal.SetColor("green");
        terminal.WriteLine("Savings Interest:");
        terminal.SetColor("white");
        terminal.WriteLine("  Daily rate: 0.1% on all deposits");
        terminal.WriteLine("  Interest is calculated and added each day at midnight.");
        terminal.WriteLine("");

        terminal.SetColor("red");
        terminal.WriteLine("Loan Interest:");
        terminal.SetColor("white");
        terminal.WriteLine("  Daily rate: 5% on outstanding balance");
        terminal.WriteLine("  Interest compounds daily. Repay promptly!");
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine("Guard Wages:");
        terminal.SetColor("white");
        terminal.WriteLine("  Base: 1,000 gold/day");
        terminal.WriteLine($"  Level bonus: {GameConfig.GuardSalaryPerLevel} gold per level");
        terminal.WriteLine("  Hazard pay: Varies based on robbery attempts");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine("\"Questions? My door is always open... during business hours.\"");

        await terminal.PressAnyKey();
    }

    private async Task ShowAccountHistory()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_white");
        terminal.WriteLine("=== ACCOUNT SUMMARY ===");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine($"{BankerName} adjusts his spectacles and reviews your records...");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($"Account Holder: {currentPlayer.DisplayName}");
        terminal.WriteLine($"Account Status: {(currentPlayer.Loan > 0 ? "IN DEBT" : "Good Standing")}");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine("Financial Overview:");
        terminal.SetColor("white");
        terminal.WriteLine($"  Gold on Hand:      {currentPlayer.Gold:N0}");
        terminal.WriteLine($"  Gold in Bank:      {currentPlayer.BankGold:N0}");
        terminal.WriteLine($"  Total Net Worth:   {(currentPlayer.Gold + currentPlayer.BankGold - currentPlayer.Loan):N0}");
        terminal.WriteLine("");

        if (currentPlayer.Loan > 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine("Loan Details:");
            terminal.SetColor("white");
            terminal.WriteLine($"  Outstanding Loan:  {currentPlayer.Loan:N0} gold");
            terminal.WriteLine($"  Daily Interest:    {(long)(currentPlayer.Loan * LoanInterestRate):N0} gold (5%)");
            terminal.WriteLine("");
        }

        terminal.SetColor("cyan");
        terminal.WriteLine("Account Statistics:");
        terminal.SetColor("white");
        terminal.WriteLine($"  Interest Earned:   {currentPlayer.Interest:N0} gold (lifetime)");
        terminal.WriteLine($"  Daily Interest:    {(long)(currentPlayer.BankGold * DailyInterestRate):N0} gold (0.1%)");

        if (currentPlayer.BankGuard)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine("");
            terminal.WriteLine("Guard Employment:");
            terminal.SetColor("white");
            terminal.WriteLine($"  Status: ACTIVE");
            terminal.WriteLine($"  Daily Wage: {currentPlayer.BankWage:N0} gold");
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine($"\"Your account has been with us since the beginning,\" {BankerName} notes.");

        await terminal.PressAnyKey();
    }

    private async Task ApplyForGuardDuty()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("=== GUARD DUTY APPLICATION ===");
        terminal.WriteLine("");

        if (currentPlayer.BankGuard)
        {
            terminal.SetColor("cyan");
            terminal.WriteLine($"{BankerName} smiles. \"You're already one of our finest guards!\"");
            terminal.WriteLine($"\"Your current wage is {currentPlayer.BankWage:N0} gold per day.\"");
            await terminal.PressAnyKey();
            return;
        }

        // Check requirements
        if (currentPlayer.Level < GameConfig.MinLevelForGuard)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"{BankerName} shakes his head.");
            terminal.WriteLine($"\"I'm sorry, but we require guards to be at least level {GameConfig.MinLevelForGuard}.\"");
            terminal.WriteLine($"\"Come back when you have more experience.\"");
            await terminal.PressAnyKey();
            return;
        }

        if (currentPlayer.Darkness > GameConfig.MaxDarknessForGuard)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"{BankerName} eyes you suspiciously.");
            terminal.WriteLine("\"Our background check reveals... concerning information about your activities.\"");
            terminal.WriteLine("\"We cannot employ someone with such a dark reputation.\"");
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("cyan");
        terminal.WriteLine($"{BankerName} reviews your credentials carefully.");
        terminal.WriteLine("");

        int guardWage = 1000 + (currentPlayer.Level * GameConfig.GuardSalaryPerLevel);

        terminal.SetColor("white");
        terminal.WriteLine("GUARD POSITION DETAILS:");
        terminal.WriteLine($"  Daily wage: {guardWage:N0} gold");
        terminal.WriteLine("  Duties: Protect the bank from robbery attempts");
        terminal.WriteLine("  Risk: You may be called to fight dangerous criminals");
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine("Do you accept this position? (Y/N)");

        string accept = await terminal.GetInput("> ");

        if (accept.ToUpper() == "Y")
        {
            currentPlayer.BankGuard = true;
            currentPlayer.BankWage = guardWage;
            _activeGuardNames.Add(currentPlayer.DisplayName);

            terminal.SetColor("bright_green");
            terminal.WriteLine("");
            terminal.WriteLine("Congratulations! You are now a Bank Guard!");
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.WriteLine($"{BankerName} shakes your hand firmly.");
            terminal.WriteLine("\"Welcome to the team! Your wages will be deposited daily.\"");
            terminal.WriteLine("\"Keep your weapon ready and your eyes sharp!\"");

            // Announce publicly?
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine("Make your employment public? (Y/N)");
            terminal.WriteLine("(This may deter criminals... or make you a target)");

            string goPublic = await terminal.GetInput("> ");
            if (goPublic.ToUpper() == "Y")
            {
                NewsSystem.Instance.Newsy(true, $"{currentPlayer.DisplayName} has been hired as a guard at the Ironvault Bank!");
            }
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"{BankerName} nods understandingly. \"Perhaps another time.\"");
        }

        await terminal.PressAnyKey();
    }

    private async Task ResignGuardDuty()
    {
        terminal.ClearScreen();
        terminal.SetColor("yellow");
        terminal.WriteLine("=== RESIGN FROM GUARD DUTY ===");
        terminal.WriteLine("");

        if (!currentPlayer.BankGuard)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("You are not currently employed as a bank guard.");
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("cyan");
        terminal.WriteLine($"{BankerName} looks disappointed.");
        terminal.WriteLine("\"Are you certain you wish to resign? Your service has been valuable.\"");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($"Current wage: {currentPlayer.BankWage:N0} gold/day");
        terminal.WriteLine("");
        terminal.WriteLine("Confirm resignation? (Y/N)");

        string confirm = await terminal.GetInput("> ");

        if (confirm.ToUpper() == "Y")
        {
            currentPlayer.BankGuard = false;
            currentPlayer.BankWage = 0;
            _activeGuardNames.Remove(currentPlayer.DisplayName);

            terminal.SetColor("gray");
            terminal.WriteLine("");
            terminal.WriteLine("You have resigned from guard duty.");
            terminal.WriteLine($"{BankerName} sighs. \"Very well. Thank you for your service.\"");
        }
        else
        {
            terminal.SetColor("green");
            terminal.WriteLine("You decide to remain a guard.");
        }

        await terminal.PressAnyKey();
    }

    private async Task AttemptRobbery()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine("+=============================================================================+");
        terminal.WriteLine("|                         !!! BANK ROBBERY !!!                                |");
        terminal.WriteLine("+=============================================================================+");
        terminal.WriteLine("");

        // Check if player is a guard
        if (currentPlayer.BankGuard)
        {
            terminal.SetColor("red");
            terminal.WriteLine("You can't rob the bank you're sworn to protect!");
            terminal.WriteLine("That would be a serious breach of contract... and ethics.");
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
            terminal.WriteLine("You've exhausted your robbery attempts for today.");
            terminal.WriteLine("The guards are too alert after your previous attempt.");
            await terminal.PressAnyKey();
            return;
        }

        // Show the stakes
        terminal.SetColor("white");
        terminal.WriteLine("You survey the bank's defenses...");
        terminal.WriteLine("");

        int guardCount = CalculateGuardCount();
        terminal.SetColor("yellow");
        terminal.WriteLine($"  Safe Contents: ~{_safeContents:N0} gold (you could steal 25%)");
        terminal.WriteLine($"  Bank Guards: {guardCount} armed professionals");
        terminal.WriteLine($"  Player Guards: {_activeGuardNames.Count} adventurers on duty");
        terminal.WriteLine($"  Alarm System: Dwarven runes (very loud)");
        terminal.WriteLine("");

        terminal.SetColor("bright_red");
        terminal.WriteLine("WARNING: Robbery WILL:");
        terminal.SetColor("red");
        terminal.WriteLine("  - Set your Chivalry to 0");
        terminal.WriteLine("  - Increase your Darkness significantly");
        terminal.WriteLine("  - Make you WANTED by the authorities");
        terminal.WriteLine("  - Possibly kill you");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("Options:");
        terminal.WriteLine("  [R] Rob the bank (COMMIT TO CRIME)");
        terminal.WriteLine("  [I] Inspect guards first");
        terminal.WriteLine("  [A] Abort - this is crazy");

        string choice = await terminal.GetInput("> ");

        switch (choice.ToUpper())
        {
            case "R":
                terminal.SetColor("red");
                terminal.WriteLine("");
                terminal.WriteLine("Are you ABSOLUTELY SURE? Type 'ROB' to confirm:");
                string confirm = await terminal.GetInput("> ");

                if (confirm.ToUpper() == "ROB")
                {
                    await ExecuteRobbery(guardCount);
                }
                else
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine("Cold feet? Wise choice.");
                }
                break;

            case "I":
                await InspectGuards(guardCount);
                break;

            default:
                terminal.SetColor("gray");
                terminal.WriteLine("You quietly leave the bank. Some crimes aren't worth the risk.");
                break;
        }

        await terminal.PressAnyKey();
    }

    private async Task InspectGuards(int guardCount)
    {
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine("You casually observe the guards...");
        terminal.WriteLine("");

        // Captain
        terminal.SetColor("bright_red");
        terminal.WriteLine("  Captain of the Guard - Broadsword & Chainmail");
        terminal.SetColor("gray");
        terminal.WriteLine("    (Looks battle-hardened and dangerous)");

        // Regular guards
        for (int i = 1; i < guardCount; i++)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"  Guard #{i} - Halberd & Ringmail");
        }

        // Possible guard dog
        var random = new Random();
        if (random.Next(2) == 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine("  Guard Dog - Savage Pitbull");
            terminal.SetColor("gray");
            terminal.WriteLine("    (Straining at its leash, eyeing you hungrily)");
        }

        // Player guards
        if (_activeGuardNames.Count > 0)
        {
            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("Adventurer Guards on Duty:");
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
        terminal.WriteLine("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");
        terminal.WriteLine("!                         ROBBERY IN PROGRESS!                              !");
        terminal.WriteLine("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");
        terminal.WriteLine("");

        // Consequences happen regardless of outcome
        currentPlayer.Chivalry = 0;
        currentPlayer.Darkness += 100;
        currentPlayer.BankRobberyAttempts--;
        currentPlayer.WantedLvl += 5;
        _robberyAttemptsToday++;

        terminal.SetColor("white");
        terminal.WriteLine("You draw your weapon and charge toward the vault!");
        terminal.WriteLine($"{BankerName} screams and dives under his desk!");
        terminal.WriteLine("ALARMS BLARE throughout the bank!");
        terminal.WriteLine("");

        await Task.Delay(1500);

        // Create monsters for guards
        var monsters = new List<Monster>();

        // Captain
        var captain = new Monster
        {
            Name = "Captain of the Guard",
            HP = 100 + (currentPlayer.Level * 5),
            MaxHP = 100 + (currentPlayer.Level * 5),
            Strength = 30 + currentPlayer.Level,
            Defence = 20 + (currentPlayer.Level / 2),
            WeapPow = 25,
            ArmPow = 15,
            WeaponName = "Broadsword",
            ArmorName = "Chainmail"
        };
        monsters.Add(captain);

        // Regular guards
        for (int i = 1; i < guardCount; i++)
        {
            var guard = new Monster
            {
                Name = "Bank Guard",
                HP = 60 + (currentPlayer.Level * 3),
                MaxHP = 60 + (currentPlayer.Level * 3),
                Strength = 20 + (currentPlayer.Level / 2),
                Defence = 12 + (currentPlayer.Level / 3),
                WeapPow = 15,
                ArmPow = 10,
                WeaponName = "Halberd",
                ArmorName = "Ringmail"
            };
            monsters.Add(guard);
        }

        // Possible pitbull
        var random = new Random();
        if (random.Next(2) == 0)
        {
            var dog = new Monster
            {
                Name = "Guard Pitbull",
                HP = 80,
                MaxHP = 80,
                Strength = 35,
                Defence = 5,
                WeapPow = 40,
                ArmPow = 5,
                WeaponName = "Savage Jaws",
                ArmorName = "Thick Hide"
            };
            monsters.Add(dog);
        }

        terminal.SetColor("red");
        terminal.WriteLine($"{monsters.Count} enemies rush to stop you!");
        terminal.WriteLine("");

        await Task.Delay(1000);

        // Simple combat simulation
        bool victory = SimulateBankRobberyCombat(monsters);

        if (victory)
        {
            // Calculate loot (25% of safe) with overflow protection
            long stolenGold = _safeContents / 4;
            currentPlayer.Gold = SafeAddGold(currentPlayer.Gold, stolenGold);
            _safeContents -= stolenGold;

            terminal.SetColor("bright_green");
            terminal.WriteLine("");
            terminal.WriteLine("+=============================================================================+");
            terminal.WriteLine($"|               SUCCESS! You stole {stolenGold:N0} gold!                     |");
            terminal.WriteLine("+=============================================================================+");
            terminal.WriteLine("");

            terminal.SetColor("yellow");
            terminal.WriteLine("You grab handfuls of gold and flee into the night!");
            terminal.WriteLine("The authorities will be looking for you...");

            NewsSystem.Instance.Newsy(true, $"BANK ROBBERY! {currentPlayer.DisplayName} robbed the Ironvault Bank of {stolenGold:N0} gold!");
        }
        else
        {
            // Defeat
            terminal.SetColor("red");
            terminal.WriteLine("");
            terminal.WriteLine("+=============================================================================+");
            terminal.WriteLine("|                    DEFEATED! The guards overwhelm you!                      |");
            terminal.WriteLine("+=============================================================================+");
            terminal.WriteLine("");

            // Take damage and lose gold
            currentPlayer.HP = Math.Max(1, currentPlayer.HP / 2);
            long fine = Math.Min(currentPlayer.Gold, currentPlayer.Level * 2000);
            currentPlayer.Gold -= fine;

            terminal.SetColor("white");
            terminal.WriteLine("You are beaten badly and thrown out of the bank.");
            if (fine > 0)
            {
                terminal.WriteLine($"The guards confiscate {fine:N0} gold as 'damages'.");
            }

            NewsSystem.Instance.Newsy(true, $"{currentPlayer.DisplayName} attempted to rob the Ironvault Bank but was defeated by guards!");
        }
    }

    private bool SimulateBankRobberyCombat(List<Monster> monsters)
    {
        // Simplified combat for bank robbery
        // Player needs to defeat all guards
        var random = new Random();

        long playerPower = currentPlayer.Strength + currentPlayer.WeapPow + (currentPlayer.Level * 5);
        long guardPower = monsters.Sum(m => m.Strength + m.WeapPow);

        // Add some randomness
        playerPower += random.Next(0, (int)playerPower / 4);
        guardPower += random.Next(0, (int)guardPower / 4);

        // Player has advantage if well-prepared
        if (currentPlayer.HP >= currentPlayer.MaxHP * 0.8)
            playerPower += playerPower / 5;

        // Higher level players are more successful
        playerPower += currentPlayer.Level * 3;

        return playerPower > guardPower;
    }

    private int CalculateGuardCount()
    {
        int guards = 2; // Base guards

        // More guards for richer banks
        if (_safeContents > 100000) guards++;
        if (_safeContents > 250000) guards++;
        if (_safeContents > 500000) guards++;
        if (_safeContents > 1000000) guards += 2;

        // More guards after recent robbery attempts
        guards += _robberyAttemptsToday;

        return Math.Min(guards, 10); // Cap at 10
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
            // GD.Print($"[Bank] Paid {player.BankWage} guard wages to {player.DisplayName}");
        }

        // Add interest on savings
        if (player.BankGold > 0)
        {
            long interest = (long)(player.BankGold * DailyInterestRate);
            if (interest > 0)
            {
                player.Interest += interest;
                player.BankGold += interest;
                // GD.Print($"[Bank] Added {interest} interest to {player.DisplayName}'s account");
            }
        }

        // Charge loan interest and enforce consequences (with overflow protection)
        if (player.Loan > 0)
        {
            long loanInterest = (long)(player.Loan * LoanInterestRate);
            player.Loan = SafeAddGold(player.Loan, loanInterest);
            // GD.Print($"[Bank] Charged {loanInterest} loan interest to {player.DisplayName}");

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
