# v0.46.1 - Ascension (Hotfix)

Bug fixes for the alt character system and MUD mode character creation.

---

## Bug Fixes

### Critical: Alt Character AutoSave Overwrote Main Character (Online)

Playing an alt character caused the AutoSave system to write the alt's data to the main character's database row, destroying the main character's save. Root cause: `DoorMode.GetPlayerName()` and `OnlineUsername` always returned the immutable account username (e.g., "rage") instead of the active character key (e.g., "rage__alt"), because the MUD session context lookup used `ctx.Username` rather than `ctx.CharacterKey`. Fixed by using `CharacterKey` -- which is updated on alt switch -- in both `GetPlayerName()` and `OnlineUsername`. Also fixed `SaveCurrentGame()` to use `DoorMode.GetPlayerName()` in online mode (matching AutoSave behavior) instead of `currentPlayer.Name2`.

### MUD Mode Character Creation Now Prompts for Display Name

Previously, creating a new main character in MUD/online mode silently used the SSH account username as the in-game character name with no option to choose. Players now get to pick their character's in-game display name during creation, just like alt character creation already did.

### RunBBSDoorMode Account Identity Stability

The character selection menu in MUD mode used `GetPlayerName()` to determine the account identity, which could return corrupted or alt-switched values after character switching bugs. Now reads directly from the immutable `SessionContext.Username` field, ensuring the login menu always correctly identifies the account regardless of prior session state.

### Session Identity No Longer Corrupted by Display Name (Online)

After authentication, the MUD server replaced the session's account username with the `display_name` column from the database. If a bug corrupted the display name (e.g., alt character data overwriting the main row), the entire session identity became wrong -- the character selection menu couldn't find any saves, admin access was lost, and the welcome message showed the wrong name. The session identity now always uses the login account key, never the display name.

### Admin Console: Immortalize Player

Admins can now grant godhood to any player directly from the admin console using the new `[I] Immortalize Player` option under the God Management section. Prompts for divine name and alignment, then converts the player to an immortal god with a fresh alt slot. Useful for restoring immortal status after data loss or for testing.

---

## Balance Changes

### Combat Damage Rebalance: Weapon Power Soft Cap & Loot Scaling

High-level players with stacked legendary dungeon loot could deal extreme damage (57,000+ in a single basic attack) due to uncapped weapon power scaling. Two fixes address this:

**Loot power scaling reduced** — The dungeon loot generator's level scaling formula was reduced from `level/40` to `level/80`. New legendary weapons at level 90+ now have roughly half the weapon power they previously did, bringing them more in line with the game's damage budget. Previously-dropped items in player inventories are unaffected (their stats are baked in), but the soft cap below handles those.

**Weapon & armor power soft cap** — All combat damage and defense calculations now apply diminishing returns to weapon power and armor power above 500. The first 500 points have full effect; power above that threshold operates at 30% effectiveness. This prevents extreme equipment stacking from producing absurd damage spikes, while preserving the feeling of progression for normal play. The cap applies to all attack types: basic attacks, backstabs, power attacks, precise strikes, smites, berserker rage, PvP, and NPC/teammate attacks.

Example impact for a level 93 warrior with 1710 total weapon power:
- **Before**: Full 1710 weapon power feeds into damage formula → 57,000+ damage crits possible
- **After**: Effective weapon power = 500 + (1210 × 0.30) = 863 → damage crits cap around ~26,000

### Royal Bodyguard Scaling Overhaul

Royal mercenary bodyguards used simple linear stat scaling (`base + level × multiplier`) that didn't keep pace with high-level monster damage. At level 90+, bodyguards died in 1-2 hits from level-appropriate monsters, forcing the king to replace them after nearly every fight.

**HP formulas improved** — Added a quadratic scaling component (`level²/30`) on top of increased linear multipliers. This adds negligible HP at low levels but significantly boosts survivability at high levels where monster damage spikes. All four bodyguard roles also received increased armor power scaling to reduce incoming damage.

Example impact at level 92 (vs Archfiend dealing ~1,314 damage/hit):

| Role | Old HP | New HP | Old Hits to Kill | New Hits to Kill |
|------|--------|--------|------------------|------------------|
| Tank | 2,880 | 4,824 | ~2 | ~4 |
| Healer | 1,940 | 3,122 | ~1.5 | ~2.4 |
| DPS | 2,425 | 3,602 | ~2 | ~2.7 |
| Support | 2,425 | 3,602 | ~2 | ~2.7 |

At level 10, the increase is modest (Tank: 420 → 576 HP), keeping early-game balance intact.

---

## Files Changed

| File | Changes |
|------|---------|
| `Scripts/Core/GameConfig.cs` | Version 0.46.1 |
| `Scripts/BBS/DoorMode.cs` | `GetPlayerName()` and `OnlineUsername` now use `ctx.CharacterKey` instead of `ctx.Username`; `SetOnlineUsername()` updates `ctx.CharacterKey` |
| `Scripts/Core/GameEngine.cs` | `RunBBSDoorMode()` uses `ctx.Username` for account identity; welcome message uses save display name; `SaveCurrentGame()` uses `DoorMode.GetPlayerName()` in online mode; `SwitchToAltCharacter()` passes display name to `SwitchIdentity()` |
| `Scripts/Server/MudServer.cs` | Removed `displayName` override of session username after auth; session identity always uses login account key |
| `Scripts/Systems/CharacterCreationSystem.cs` | MUD mode main character creation prompts for in-game display name |
| `Scripts/Systems/OnlineAdminConsole.cs` | Added `[I] Immortalize Player` command with divine name, alignment, and live session update |
| `Scripts/Systems/LootGenerator.cs` | Reduced level scaling from `level/40` to `level/80` for weapons, armor, and accessories |
| `Scripts/Systems/CombatEngine.cs` | Added `GetEffectiveWeapPow()` and `GetEffectiveArmPow()` soft cap helpers (500 threshold, 30% above); applied to all 12 attack paths and 3 defense paths |
| `Scripts/Locations/CastleLocation.cs` | Royal mercenary HP formulas: added quadratic scaling component (`level²/30`), increased linear HP multipliers (Tank 30→45, Healer 20→30, DPS/Support 25→35), increased ArmPow scaling for all roles |
