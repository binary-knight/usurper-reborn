# v0.54.6 - BBS Socket Hotfix

## BBS Socket Handle Fix (Corrected)

v0.54.4 fixed the socket handle leak that prevented BBS players from re-entering the game after quitting, but the fix was too aggressive — it called `Socket.Shutdown(Both)` and `closesocket()` which tore down the TCP connection entirely, disconnecting the user from the BBS.

The BBS and door process share the socket via handle inheritance. The door needs to release its handle reference when exiting, but the TCP connection must stay alive for the BBS to continue serving the user.

- **`CloseHandle()` instead of `closesocket()`** — releases our handle reference without tearing down the TCP connection. The BBS keeps its own handle and the user stays connected.
- **Reverted `ownsHandle` back to `false`** — prevents `SafeSocketHandle` from calling `closesocket()` in its finalizer
- **Raw socket handle stored at initialization** — the IntPtr from DOOR32.SYS is now saved BEFORE either I/O path (Socket or FileStream) is attempted and closed via `CloseHandle()` on dispose. The v0.54.5 fix only stored it in the Socket path, but EleBBS uses the FileStream fallback where `_rawSocketHandle` was never set, so `CloseHandle` was never called

## Companion "Days Together" Negative / Marriage "0 Days" Fix

"Days together" for companions showed negative numbers (e.g., -5) and marriage duration always showed 0 days. Root cause: both used `CurrentGameDay - RecruitedDay` (or `MarriedGameDay`), but in online mode the game day counter is per-session and desyncs. A companion recruited on "day 27" could be displayed when the player's session counter was at "day 22", producing -5.

- **New `RecruitedDate` DateTime on companions** — set to `DateTime.UtcNow` on recruitment, serialized, and used for real elapsed-time display. Falls back to game day calculation for pre-existing saves.
- **Vex death timer uses real time** — the disease countdown (`DaysUntilDeath`) now uses `RecruitedDate` for accurate wall-clock timing in online mode. Both CompanionSystem and DungeonLocation Vex quest checks updated.
- **Marriage duration uses `MarriedDate`** — the Home family display now prefers the `MarriedDate` DateTime (already tracked since marriage was implemented) over the desyncing `MarriedGameDay` counter.
- **All calculations clamped to `Math.Max(0, ...)`** — even with fallback paths, negative values are impossible.

## Account-Level Screen Reader and Language Persistence

Screen reader mode and language preference were only stored per-character in save data — they weren't applied until after the character loaded. SSH/online players had to re-enable screen reader mode every time they connected, because the auth screen and pre-game menus ran before the character was loaded.

- **New `screen_reader` and `language` columns on `players` table** — account-level preferences stored alongside login credentials
- **Applied immediately after authentication** — screen reader mode and language are active for the auth success message, main menu, and all pre-game screens. No more toggling every session.
- **Updated on every save** — when the player changes screen reader mode or language in preferences, the account-level columns are updated alongside the character save data
- **Three auth paths covered** — MudServer trusted auth (SSH relay), MudServer interactive auth (raw TCP), and OnlineAuthScreen (in-game [O]nline Play)

## Daily Counter Serialization Exploits

Two daily limit counters were never serialized, allowing players to bypass limits by relogging:

**InnDuelsToday** — the 3-duel-per-day limit at the Inn. Players could farm unlimited duel XP by relogging after each batch of 3.

**TavernStrangerTalkedToday** — the once-per-day Tavern Stranger encounter gives significant rewards (level * 100 XP, +5 STR/DEF, healing potions). Players could relog to talk to the stranger unlimited times, farming infinite stat boosts and XP.

- **Both counters now serialized** — added to SaveDataStructures, SaveSystem, and GameEngine restore path. A full sweep of all 23 daily counters on Character.cs confirmed these were the only two missing. The daily limit persists across save/load and relog.

## Knight Title Exploit Fix

Any player could get the permanent +5% damage / +5% defense knight buff without being knighted — just by selecting a title in preferences. `IsKnighted` was a computed property (`!string.IsNullOrEmpty(NobleTitle)`) that returned true for ANY title, including MetaProgression titles earned from NG+ cycles. A fresh level 1 character could set "Dame" and receive combat buffs.

- **`IsKnighted` is now a real boolean** — only set to `true` during the Castle knighting ceremony (line 6383). Having a MetaProgression title or King/Queen title no longer triggers knight buffs.
- **Serialized and restored** — added to SaveDataStructures, SaveSystem, and GameEngine restore path
- **Migration** — existing saves with Sir/Dame title automatically get `IsKnighted = true` on load

## Lyris Companion Ranger Stats Fix

Lyris was overhauled to be a Ranger in v0.54.2 (backstory, quest, weapon changed to Bow), but her `CombatRole` was never changed from `Hybrid`. This meant her level-up stat gains were Paladin-like: +INT 2, +WIS 2, +CHA 2, +CON 2 per level — instead of the DEX/AGI/STR a Ranger should get. Her base stats were also caster-heavy (MagicPower: 50, HealingPower: 30).

- **CombatRole changed from Hybrid to Damage** — level-up now gives +DEX 3, +AGI 2, +ATK 3-4 per level (matching a Ranger's physical focus)
- **Base stats adjusted** — ATK 25->30, Speed 35->40, HP 200->220, MagicPower 50->10, HealingPower 30->15, Defense 15->18
- **Companion weapon restrictions unaffected** — Lyris's weapon whitelist (bows, swords, daggers) is per CompanionId, not per CombatRole

## Mystic Shaman Spell Hint Fix

New Mystic Shaman characters received a tip saying "Visit the Magic Shop to learn spells!" — but Shaman uses abilities (mana-powered via ClassAbilitySystem), not spells (SpellSystem). The Magic Shop correctly refused to teach spells, making the hint misleading. Now excluded from the hint.

## Comprehensive Localization Pass

A French player refunded the game claiming "only half translated." Audit found 347 missing/untranslated keys (220 in the Inn alone — bartender, food, drinking, companions) plus ~312 hardcoded English strings across 11 C# files. All fixed:

- **1,255 new localization keys** added to en.json (total now 18,134)
- **11 C# files localized** — CombatEngine (123 keys), DungeonLocation (173), BaseLocation (160), LoveStreetLocation (118), CharacterCreationLocation (39), MailSystem (37), DailySystemManager (69), TrainingSystem (36), WorldBossSystem (26), plus 9 smaller files (129)
- **All 1,255 keys translated** into French, Spanish, Hungarian, and Italian
- **All 5 languages synced** at 18,134 keys with 0 missing

## Companion Equipment Diagnostic Logging

Added `[COMPANION_EQUIP]` logging to `SyncCompanionEquipment` — every equipment slot change via the combat sync path is logged with old and new IDs. This will help diagnose the recurring companion equipment reversion reports.

## Version File Fix

`version.txt` shipped in CI builds contained two lines (`0.54.4` and `The Soul Update`) because the grep matched both `Version` and `VersionName` in GameConfig.cs. Both update scripts read the whole file, producing broken version strings — Linux showed "Current version: none" and Windows crashed with "Illegal characters in path" when the multi-line version was used in the backup filename.

- **CI grep** now uses `'Version ='` (with equals sign) and `head -1` to match only the version line
- **Linux update script** now reads only the first line of `version.txt` via `head -1`
- **Windows update script** now reads only the first line via `Get-Content -TotalCount 1` (was `Get-Content -Raw` which included the VersionName)

---

### Files Changed

- `Scripts/Core/GameConfig.cs` — Version 0.54.6
- `Scripts/BBS/SocketTerminal.cs` — Added `CloseHandle` P/Invoke; stored raw socket handle; `Dispose()` calls `CloseHandle` instead of `Socket.Shutdown`/`Close`/`Dispose`; reverted `ownsHandle` to `false`
- `Scripts/Systems/CompanionSystem.cs` — Lyris CombatRole Hybrid->Damage; base stats adjusted (ATK/Speed/HP up, MagicPower/HealingPower down); RecruitedDate property + serialization
- `Scripts/Systems/CompanionSystem.cs` — New `RecruitedDate` DateTime property on Companion + CompanionSaveData; set on recruitment; serialized/deserialized; Vex death timer uses real time
- `Scripts/Locations/InnLocation.cs` — "Days together" display uses `RecruitedDate` wall-clock time with fallback
- `Scripts/Locations/DungeonLocation.cs` — Vex quest encounter uses `RecruitedDate` for days calculation
- `Scripts/Locations/HomeLocation.cs` — Marriage duration uses `MarriedDate` DateTime instead of `MarriedGameDay` counter
- `Scripts/Systems/SqlSaveBackend.cs` — New `screen_reader` and `language` columns on players table; AuthenticatePlayer returns both preferences; WriteGameData persists both on save; migration for existing databases
- `Scripts/Server/MudServer.cs` — Both auth paths apply screen_reader and language from AuthenticatePlayer result
- `Scripts/Systems/OnlineAuthScreen.cs` — Applies screen_reader and language after successful auth
- `.github/workflows/ci-cd.yml` — version.txt grep uses `'Version ='` + `head -1` to avoid matching VersionName
- `scripts-server/updatecheck.sh` — Reads only first line of version.txt via `head -1`
- `scripts-server/updatecheck.ps1` — Reads only first line of version.txt via `Get-Content -TotalCount 1`
- `Scripts/Core/Character.cs` — `IsKnighted` changed from computed property to real bool
- `Scripts/Systems/SaveDataStructures.cs` — Added IsKnighted, InnDuelsToday, TavernStrangerTalkedToday
- `Scripts/Systems/SaveSystem.cs` — Serialize IsKnighted, InnDuelsToday, TavernStrangerTalkedToday
- `Scripts/Core/GameEngine.cs` — Restore IsKnighted (with Sir/Dame migration), InnDuelsToday, TavernStrangerTalkedToday
- `Scripts/Locations/CastleLocation.cs` — Set `IsKnighted = true` during knighting ceremony
- `Scripts/Locations/MainStreetLocation.cs` — Exclude MysticShaman from spell hint
- `Scripts/Systems/CombatEngine.cs` — Localized ~270 hardcoded strings across 2 passes
- `Scripts/Locations/DungeonLocation.cs` — Localized ~173 hardcoded strings; mana potion dungeon drops
- `Scripts/Locations/BaseLocation.cs` — Localized ~160 hardcoded strings
- `Scripts/Locations/LoveStreetLocation.cs` — Localized ~118 hardcoded strings
- `Scripts/Locations/CharacterCreationLocation.cs` — Localized ~39 hardcoded strings
- `Scripts/Systems/MailSystem.cs` — Localized ~37 hardcoded strings
- `Scripts/Systems/DailySystemManager.cs` — Localized ~69 hardcoded strings
- `Scripts/Systems/TrainingSystem.cs` — Localized ~36 hardcoded strings
- `Scripts/Systems/WorldBossSystem.cs` — Localized ~31 hardcoded strings
- `Scripts/Systems/AlignmentSystem.cs` — Localized ~36 hardcoded strings
- `Scripts/Data/RiddleDatabase.cs` — Localized ~25 hardcoded strings
- `Scripts/Systems/BugReportSystem.cs` — Localized ~7 hardcoded strings
- `Scripts/Systems/InventorySystem.cs` — Localized ~11 hardcoded strings
- `Scripts/Systems/LocationManager.cs` — Localized ~9 hardcoded strings
- `Scripts/Systems/TeamBalanceSystem.cs` — Localized ~17 hardcoded strings
- `Scripts/Systems/DialogueSystem.cs` — Localized 3 hardcoded strings
- `Scripts/Systems/OnlineChatSystem.cs` — Localized 2 hardcoded strings
- `Scripts/Systems/RareEncounters.cs` — Localized 1 hardcoded string
- `Scripts/Data/SecretBosses.cs` — Localized 1 hardcoded string
- `Scripts/Systems/OldGodBossSystem.cs` — Localized Mira pre-Veloura speech (9 strings)
- `Localization/en.json` — 18,134 keys (was 16,879)
- `Localization/fr.json` — 18,134 keys (synced)
- `Localization/es.json` — 18,134 keys (synced)
- `Localization/hu.json` — 18,134 keys (synced)
- `Localization/it.json` — 18,134 keys (synced)
