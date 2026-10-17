---
title: Gold, shops and banking
path: /wiki/en/items/banking/
checked: 1.3.4
sources: Scripts/Locations/BankLocation.cs, Scripts/Systems/BankVaultSystem.cs, Scripts/Core/GameConfig.cs, Scripts/Systems/DailySystemManager.cs
history: 1.3.4 | none
history: 1.3.3 | none
history: 1.3.2 | A bank robbery you lose leaves you at 1 HP and the bank takes its fine, with no death.
history: 1.2.5 | Bank screens, bank news and the bank's guards are shown in all five languages.
history: 1.2.4 | The Bank's count of robbery attempts for the day is kept across server restarts.
---
Gold on hand and bank gold are separate balances. The Bank, on Guild Row (G, then B), offers deposits, withdrawals, interest, loans, a guard job, transfers (online only) and a robbery. Shop prices, selling and haggling are in [shops](/wiki/en/items/shops/).

## Bank interest

Savings earn 0.1 percent a day, paid into your account at the daily reset: 1,000 gold a day on a balance of 1,000,000. Gold on hand earns nothing. Keep enough on hand for healing, Mental treatment and travel; death and Mental rescue can affect carried gold.

## Bank loan

The largest bank loan is 500 + level x 500 gold, capped at 250,000: 10,500 at level 20. You can hold one loan at a time and repay it in full or in part. A loan grows 5 percent at each daily reset.

## Unpaid bank loan

Each daily reset, after the loan grows, an unpaid loan is enforced by its size:

- Over 5,000: the bank takes your deposits against the loan, up to the amount owed.
- Over 10,000: debt collectors deal loan / 500 damage (never killing you), add Darkness and 1 wanted level.
- Over 25,000: the bank takes gold on hand, up to half the loan.
- Over 50,000: you lose the bank guard job, gain more Darkness and 3 wanted levels, and lose 50 Chivalry.
- Over 100,000: the bank refuses you service.

## Bank guard job

The bank guard job needs level {{balance:MinLevelForGuard}} and Darkness of {{balance:MaxDarknessForGuard}} or less. It pays 1,000 + level x {{balance:GuardSalaryPerLevel}} gold into your account at each daily reset: 4,000 at level 20 and 16,000 at level 100. The wage follows your current level.

## Bank transfer fee

Online only, you can wire gold from your account to another player. The bank keeps a fee of {{percent:BankTransferFeePercent}} percent, rounded up and at least 1 gold: you send the full amount and the other player receives the rest. Confirm the name carefully.

## Rob the bank

You have {{balance:DefaultBankRobberyAttempts}} robbery attempts a day, renewed at the daily reset. Any attempt sets your Chivalry to 0 and adds 100 Darkness and 5 wanted levels, win or lose.

The robbery is a fight against a guard captain and guards, 4 in all, +1 with over 100,000 gold in the vault, +1 over 250,000, +2 over 500,000, +3 over 1,000,000 and +2 for each robbery attempted in the world that day, at most 12, plus 1 to 3 war hounds. They are built from your level. Losing the fight is not a death and uses no resurrection: you are left at 1 HP, and the bank takes all your gold on hand and up to level x 500 gold from your account.

A won robbery takes a quarter of the vault that is not your own deposit, at most 250,000 gold. The vault starts at 500,000, grows 25,000 + {{balance:BankVaultRefillRatePercent}} percent a day and stops at 5,000,000. Online the vault is one pot shared by every player.

Server state and treasury activity change over time. No live accounts or player wealth are exported into this wiki.
