# Usurper Reborn v0.49.10 — Hotfix

## Critical Fix: Player Save Loading

Settlement system's `ProposalCooldowns` dictionary was serialized as an empty JSON array `[]` instead of an empty object `{}` in player save data. This caused deserialization to fail for **every player** on login — the game couldn't load any existing characters and offered "New Character" instead. All 22 affected player records have been patched in the live database. A tolerant JSON converter has been added so this class of type mismatch can never cause save loading failures again.

## Database Backup & Recovery

Auto-updater now backs up the online database before applying updates. Up to 5 rotating backups are kept (newest = backup_1, oldest = backup_5). Backups are stored alongside the database file. If the backup fails, the update still proceeds (non-fatal) but a warning is logged.

New **[R] Recovery** option in the SysOp Console (both local and online modes):
- View current database info (path, size, last modified)
- List available backups with size and date
- Create manual backup on demand
- Restore from any backup with safety confirmation (current DB saved as backup_0 before overwrite)

## Files Changed

- `Scripts/Core/GameConfig.cs` — Version 0.49.10
- `Scripts/Systems/SqlSaveBackend.cs` — `TolerantDictionaryConverter<TKey,TValue>` handles `[]` as empty dictionary during deserialization; registered for `Dictionary<string,int>` and `Dictionary<string,long>`
- `Scripts/Systems/VersionChecker.cs` — `CreateDatabaseBackup()` public method; called before auto-update extraction; 5 rotating backups with pruning
- `Scripts/Systems/SysOpConsoleManager.cs` — `[R] Recovery` menu option; `ShowRecoveryMenu()`, `CreateManualBackup()`, `RestoreBackup()` methods
