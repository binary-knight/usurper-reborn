# v0.54.4 - Bug Fixes & BBS Socket Fix

## BBS Socket Handle Leak Fix

Long-standing bug affecting EleBBS and Mystic BBS sysops: after exiting the game, players had to disconnect from the entire BBS before they could re-enter. The socket handle inherited from DOOR32.SYS was never properly closed on exit.

Root cause: `SocketTerminal` created a `SafeSocketHandle` with `ownsHandle: false` and never closed the socket on dispose, based on the incorrect assumption that "the BBS owns the handle." In reality, the DOOR32.SYS spec requires the door process to close the inherited socket handle before exiting. Without this, the OS keeps the handle open and the BBS can't reclaim the connection.

- **`SafeSocketHandle` now created with `ownsHandle: true`** — disposing the Socket will close the underlying OS handle
- **`SocketTerminal.Dispose()` now explicitly shuts down and closes the socket** — calls `Socket.Shutdown(Both)` to send TCP FIN, then `Socket.Close()` and `Socket.Dispose()`, all wrapped in try/catch for resilience
- **Flush before dispose** — `StreamWriter` is flushed before disposal to ensure all buffered output reaches the BBS client
- **All dispose steps individually wrapped** — a failure in one step (e.g., reader already disposed) won't prevent socket cleanup

## Single-Target Magical Abilities Proccing Weapon Enchants

The v0.54.2 fix for "weapon enchants firing on AoE spells" only covered AoE spell paths. Single-target magical class abilities like Holy Smite (Cleric) still triggered weapon enchants — players reported that giving Mira a mana siphon sword caused the siphon to fire on her Holy Smite even though it's pure divine damage, not a weapon attack.

- **New `IsMagicalAbilityEffect()` helper** classifies ability `SpecialEffect` strings as magical/divine vs physical. Currently includes: `holy`, `shadow`, `shaman_lightning_bolt`, `fire`, `frost`, `ice`, `lightning`, `void`, `psychic`, `magic`, `charm`, `fear`, `confusion`, `necro`, `drain`.
- **Both single-target ability paths** now pass `isSpellDamage: IsMagicalAbilityEffect(...)` to `ApplyPostHitEnchantments`. Magical abilities no longer proc weapon enchants (lifedrinker, mana siphon, fire/frost/lightning/poison/holy/shadow elemental procs).
- **Physical melee abilities** (backstab, power_strike, execute, last_stand, whirlwind, cleave, etc.) are NOT in the magical set and continue to proc weapon enchants correctly.

## Mira-Veloura Companion Interaction

Mira's entire backstory is built around being a priestess in Veloura's temple, but there was zero interaction between them during the Veloura encounter. Players who brought Mira to Floor 40 got no special dialogue, no reactions, and no acknowledgment of their shared history.

- **Dungeon idle comments** — Mira's tension builds as the party approaches Floor 40. On floors 35-39 she trembles, remembers her temple, and struggles to say Veloura's name. On Floor 40 itself, she speaks only about Veloura. After the encounter, she reflects on the outcome.
- **Pre-encounter speech** — Before the Veloura dialogue choices, Mira steps forward and addresses her former goddess directly. She recalls her years of service, the corruption of the temple, and the death of Sister Aldara. She asks the player to remember that Veloura wasn't always a monster.
- **Combat modifier** — Veloura recognizes her former priestess and hesitates, dealing 10% less damage when Mira is in the party.
- **Post-encounter reaction** — Mira has unique reactions to each outcome:
  - **Saved**: Mira breaks down, grateful that Veloura remembered who she was, and prays for the first time in years
  - **Defeated (aggressive)**: Mira rationalizes that the corruption killed Veloura long ago, but doesn't fully believe it
  - **Defeated (merciful)**: Mira forgives Veloura and says goodbye
  - **Allied**: Mira grapples with the goddess she abandoned becoming their ally

## Companion Weapon Restrictions

Companions had no weapon type restrictions — auto-equip and manual equip would give Melodia a bow, Vex a staff, or Mira a greatsword. The `CanEquip()` check only enforced class restrictions on the mapped character class (e.g., Melodia -> Bard), but since most weapons have no class restriction, anything could be equipped.

- **Companion-specific weapon whitelist in `CanEquip()`** — each companion now has a role-appropriate weapon list:
  - **Aldric** (Tank): Swords, axes, maces, hammers, flails, mauls, greatswords, greataxes
  - **Mira** (Healer): Maces, staves, hammers, flails
  - **Lyris** (Ranger): Bows, swords, daggers
  - **Vex** (Assassin): Daggers, swords, rapiers
  - **Melodia** (Bard): Instruments, rapiers, daggers
- **Inn wrapper fixed** — `CreateCompanionCharacterWrapper` in InnLocation was missing `IsCompanion = true` and `CompanionId`, so the weapon restriction was never checked during Inn equip/auto-equip. Now set.
- Applies to both manual equip and auto-equip ("Equip Best") flows

## Flametongue Attack Buff

Flametongue was the only Shaman weapon enchant with no secondary effect — Rockbiter gives +15% DEF, Frostbrand slows enemies, Stormstrike restores mana, but Flametongue just did fire damage. Now grants **+15% ATK** (based on STR) for the enchant's 5-round duration, making it the offensive counterpart to Rockbiter's defensive buff.

## Mystic Shaman Weapon Restrictions

Per the Mystic Shaman class design ("swords, axes, maces, mauls -- no bows, no staves"), Shaman is supposed to be a melee class with a restricted weapon pool. However, `CanEquip()` had no enforcement for this -- only the per-template `ClassRestrictions` list, which meant Shaman could potentially wield greatswords, greataxes, polearms, staves, bows, daggers, rapiers, and scimitars from any drop with empty class restrictions.

- **Hard weapon whitelist for Shaman** — `CanEquip()` now blocks any main-hand weapon that isn't a Sword, Axe, Mace, Hammer, Flail, or Maul. Bows, staves, greatswords, greataxes, polearms, daggers, rapiers, scimitars, and instruments are all rejected with a class restriction message.
- **Removed Polearm from enchant filter** — Flametongue, Rockbiter, Frostbrand, and Stormstrike previously listed Polearm as a valid weapon type, but Shaman can't wield polearms per design. Replaced with Hammer and Flail (which are valid Shaman weapons that were missing from the filter).

## Dungeon Mana Potion Drops for Spellcasters

Dungeon potion sources (potion caches, treasure rooms, room events) only ever gave healing potions. Mana classes (Magician, Cleric, Sage, Mystic Shaman, and all prestige classes) never found mana potions in the dungeon -- they had to buy them from the Magic Shop. When at max healing potions, additional potions were simply wasted.

- **All 3 dungeon potion sources now give mana potions to mana classes** — healing potions are prioritized first. When healing is full (or overflow from the drop), the remaining potions become mana potions for mana classes.
- **Potion cache** shows both healing and mana potion counts after pickup
- **Treasure rooms and room events** convert overflow healing potions to mana potions
- **Non-mana classes** (Warrior, Barbarian, Ranger, etc.) are unaffected -- they only find healing potions as before

## Child Naming and Duplicate Name Fix

Children of the same parents with the same first name were treated as duplicates and silently deleted. The duplicate prevention check in `RegisterChild()` and `DeserializeChildren()` matched on `Name + MotherID + FatherID` -- but with only 10 first names per sex, duplicates are inevitable for players with many children.

- **Duplicate check now uses `BirthDate`** instead of `Name` — `BirthDate + MotherID + FatherID` is always unique since two children can't be born at the exact same millisecond to the same parents. Fixed in both `RegisterChild()` and `DeserializeChildren()`.
- **Players can now name their children at birth** — when a child is born, the player is prompted to enter a first name (the father's surname is kept). Pressing Enter keeps the auto-generated name.
- **Rename children at Home** — the `[C] Spend Time with Child` menu now shows `[S] Spend time` and `[N] Rename` options. Renaming changes the first name while preserving the surname.

## Magic Shop Healing Potion Cap Bug

Ravanella's Magic Shop refused to sell healing potions once the player had 50, even though the actual per-level cap is higher (20 + level - 1 = 69 at level 50). The buy check used the global constant `GameConfig.MaxHealingPotions` (50) instead of `player.MaxPotions`. The Healer's potion purchase correctly used the per-level cap, which is why it worked there.

- **Fixed** — Magic Shop healing potion check now uses `player.MaxPotions` matching the Healer

## Combat Loot Not Persisted in Online Mode

Items picked up from combat loot could silently vanish in online mode. The post-combat autosave did not reset the save throttle (60-second cooldown in online mode), so if the previous save was recent, the `AutoSave` call was silently skipped. The loot existed in memory but was never written to the database.

- **Post-combat autosave now resets the throttle** — both single-monster and multi-monster victory paths call `ResetAutoSaveThrottle()` before `AutoSave()`, ensuring loot pickups are always persisted immediately.

## Version File in Builds

BBS sysops running `updatecheck.ps1` saw "Current version: unknown" because `version.txt` was only created after the first successful update. Now:

- **CI builds include `version.txt`** — extracted from `GameConfig.cs` during the build, so every release zip ships with the correct version
- **PowerShell updater falls back to exe FileVersion** — if `version.txt` is missing, tries to read the version from the binary's metadata

## SQL Error Fixes

**Display name unique constraint failure** — The server has a `UNIQUE INDEX` on `LOWER(display_name)` in the players table. When saving, the `INSERT ... ON CONFLICT(username) DO UPDATE` could trigger a secondary unique constraint violation if another player already had the same display name (case-insensitive). The save silently failed, potentially losing progress. Now catches SQLite error code 19 and falls back to saving player data without updating `display_name`.

**Disposed object errors on session teardown** — Fire-and-forget calls to `AddNews()` and `GetPendingAdminCommands()` occasionally outlived the player's session, causing "Cannot access a disposed object" errors logged at ERR level (triggering Discord alerts). Downgraded `ObjectDisposedException` to DEBUG level for both methods.

---

### Files Changed

- `Scripts/Core/GameConfig.cs` — Version 0.54.4
- `Scripts/BBS/SocketTerminal.cs` — `SafeSocketHandle` ownsHandle changed to `true`; `Dispose()` now flushes writer, shuts down socket (TCP FIN), closes and disposes socket; all steps individually wrapped in try/catch
- `Scripts/Core/Items.cs` — Mystic Shaman weapon whitelist in `CanEquip()`; companion weapon restrictions per CompanionId (Aldric/Mira/Lyris/Vex/Melodia)
- `Scripts/Systems/CombatEngine.cs` — `IsMagicalAbilityEffect()` helper + `MagicalAbilityEffects` set; both single-target ability damage paths pass `isSpellDamage` so magical abilities skip weapon enchant procs; `ResetAutoSaveThrottle()` before both post-combat autosaves
- `Scripts/Systems/ClassAbilitySystem.cs` — Shaman enchant filter updated: removed Polearm, added Hammer and Flail; Flametongue description updated with +15% ATK
- `Scripts/Systems/CompanionSystem.cs` — New `IsCompanionActive()` method; Mira dungeon idle comments for floors 35-40 and post-Veloura reactions
- `Scripts/Systems/OldGodBossSystem.cs` — New `PlayCompanionBossReaction()` for Mira pre-Veloura speech; Veloura combat modifier when Mira is present (-10% boss damage)
- `Scripts/Systems/FamilySystem.cs` — Duplicate child check uses `BirthDate` instead of `Name` in both `RegisterChild()` and `DeserializeChildren()`
- `Scripts/Systems/IntimacySystem.cs` — Player prompted to name child at birth; Enter keeps auto-generated name
- `Scripts/Systems/SqlSaveBackend.cs` — WriteGameData catches display_name unique constraint (error 19) and falls back to data-only save; AddNews and GetPendingAdminCommands catch ObjectDisposedException at DEBUG level
- `Scripts/Locations/DungeonLocation.cs` — Mira post-encounter reactions for all Veloura outcomes; dungeon potion sources give mana potions to mana classes (potion cache, treasure rooms, room events)
- `Scripts/Locations/HomeLocation.cs` — Child interaction menu adds `[S] Spend time` / `[N] Rename` options
- `Scripts/Locations/InnLocation.cs` — `CreateCompanionCharacterWrapper` sets `IsCompanion` and `CompanionId`
- `Scripts/Locations/MagicShopLocation.cs` — Healing potion cap check uses `player.MaxPotions` instead of `GameConfig.MaxHealingPotions`
- `.github/workflows/ci-cd.yml` — Test job builds Tests.csproj; version.txt generation step; removed i386 dpkg; fixed exit code on Steamworks verify
- `scripts-server/updatecheck.ps1` — Falls back to exe FileVersion when version.txt missing
