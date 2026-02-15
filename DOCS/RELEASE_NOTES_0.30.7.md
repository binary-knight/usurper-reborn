# Usurper Reborn - v0.30.7 Bug Fixes

Fixes bank robbery being completely broken, NPC inn duels awarding massively inflated rewards, and royal court log spam.

---

## Bug Fixes

- **Bank robbery always said "exhausted robbery attempts"**: `BankRobberyAttempts` was never serialized in save data, so it defaulted to 0 every session. Players could never rob the bank because the limit check always saw 0 attempts remaining. Fixed by initializing attempts to the default (3) on first robbery access each session. Also fixed the bank daily maintenance reset which used a hardcoded `2` instead of the configured default of `3`.

- **NPC inn duels gave Level 100 rewards regardless of NPC level**: Challenging an NPC to a duel at the Inn created a combat monster with hardcoded `nr: 100`, which set the monster's Level to 100 for reward calculation. A Level 5 NPC with 207 HP would display as "Level 100" in combat and award ~30,000 XP and ~13,000 gold on defeat despite being trivially easy. Fixed by using the NPC's actual level (`npc.Level`) instead of hardcoded 100.

- **"Royal court saved to world_state" log spam**: The royal court save log message fired on every save, autosave, and world sim tick at Info level, flooding the log file. Downgraded to Debug level.

---

## Files Changed

| File | Changes |
|------|---------|
| `Scripts/Core/GameConfig.cs` | Version bump to 0.30.7 |
| `Scripts/Locations/BankLocation.cs` | Fixed bank robbery always failing — added session initialization of `BankRobberyAttempts` to default (3) when not yet set; fixed daily maintenance reset from hardcoded `2` to `GameConfig.DefaultBankRobberyAttempts` (3) |
| `Scripts/Locations/InnLocation.cs` | Fixed NPC duel monster creation from `nr: 100` to `nr: npc.Level` so rewards match actual NPC difficulty |
| `Scripts/Systems/OnlineStateManager.cs` | Downgraded "Royal court saved" log message from Info to Debug |
