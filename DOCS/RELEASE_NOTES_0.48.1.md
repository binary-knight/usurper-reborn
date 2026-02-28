# Usurper Reborn v0.48.1 — Accessibility, PvP Spells & Group Fixes

## Screen Reader Accessible Launchers

Feedback from a screen reader user revealed that WezTerm's GPU-rendered terminal is completely opaque to screen readers like NVDA and JAWS — they can read the window title but nothing else, and keyboard input doesn't register without clicking into the terminal first.

New **accessible launchers** bypass WezTerm entirely and run the game directly in the system console (conhost on Windows, default terminal on Linux/macOS), which screen readers can fully read:

- **`Play-Accessible.bat`** (Windows) — Launches in Windows Console with 80x50 window
- **`play-accessible.sh`** (Linux/macOS) — Launches in the default system terminal

These are included in all desktop (WezTerm) builds and Steam depots alongside the standard WezTerm launchers. The plain builds (for BBS sysops, server operators) are unaffected — they already run in the system console.

For Steam, a separate launch configuration pointing to `Play-Accessible.bat` / `play-accessible.sh` can be added in the Steamworks Partner dashboard so players see a "Play (Accessible / Screen Reader)" option in the Steam launch dialog.

## PvP Spell Effects Now Work

Spell effects like Spark's stun, Frost Bolt's freeze, Poison Cloud's poison, and Fear were completely non-functional in PvP combat (faction ambushes, arena, street encounters). Spells only dealt damage or healed — all secondary effects were silently ignored.

**Root cause**: `ExecutePvPSpell()` only applied `DamageAmount` and `HealAmount` from spell results. The `SpecialEffect`, `ProtectionBonus`, and `AttackBonus` fields were never read or applied.

**Fix**: New `ApplyPvPSpellEffect()` method maps spell effect strings to the Character `StatusEffect` system:
- **Stun effects** (lightning, stun, holy) — 1-2 round stun, skip turns
- **Damage over time** (poison, fire) — ongoing damage each round
- **Control effects** (sleep, fear, frost) — prevent actions or reduce accuracy
- **Debuffs** (weakness, slow, blind, curse) — reduce stats or attack speed
- **Buffs** (protection, attack bonus) — applied as Shielded/Empowered status

The PvP combat loop now checks `PreventsAction()` before each turn (stunned/sleeping/frozen characters skip their turn) and calls `ProcessStatusEffects()` each round for DoT damage and duration tick-down. Computer-controlled PvP opponents also benefit from these fixes — their spell casts now properly apply effects to the player.

## Group Dungeon: Quest Kill Tracking

Kill-monster quests (like "Expedition to Floor 7 — slay monsters along the way") were not counting kills for group followers. Only the party leader got credit toward quest objectives.

**Root cause**: `QuestSystem.OnMonsterKilled()` was only called for `result.Player` (the leader) in the victory handler, but never for grouped players in `DistributeGroupRewards()`.

**Fix**: Added `QuestSystem.OnMonsterKilled()` call for each grouped player inside the per-monster reward loop in `DistributeGroupRewards()`.

## Group Dungeon: Merchant Loot Distribution

Killing the dungeon merchant as a party only gave loot (gold, potions, darkness alignment) to the party leader. Group followers received nothing.

**Fix**: Gold is now split evenly among all alive group members (leader + followers). Each member independently receives 3 healing potions and +10 Darkness alignment. All participants see the loot messages on their own terminal.

## Player Immortals on God Ranking

The Temple's god ranking list now includes player-created gods (from ascending to immortality after defeating Manwe) alongside the built-in NPC gods. Player gods are shown in bright cyan to distinguish them from NPC gods (yellow). The list is sorted by number of followers, so popular player gods can outrank the default pantheon.

## "Also Here" Display Name Fix

The "Also here:" line when entering a room showed players' account usernames instead of their chosen display names. This defeated the purpose of the display name system introduced for players who want to use a character name different from their login name.

**Root cause**: `PlayerSession.ActiveCharacterName` was initialized to the account username in the constructor and only updated when loading an alt character — the primary character login path never set it to the display name.

**Fix**: `ActiveCharacterName` is now updated to the display name in both `LoadSaveByFileName()` (primary login) and `CreateNewGame()` (new character creation).

## MUD Prompt Hint for New Players

Players below level 5 now see a hint on the MUD prompt bar: `Main Street > (type 'look' to redraw)`. This helps new players who connect via MUD clients (Mudlet, TinTin++, VIP Mud) discover that they can re-display the current room. The hint disappears at level 5.

## WezTerm Window Size

Default WezTerm terminal window increased from 80x30 to 80x50 rows, providing more visible text without scrolling.

## Files Changed

- `GameConfig.cs` — Version 0.48.1
- `Scripts/Core/GameEngine.cs` — Alpha banner border alignment fix (lines 2-4 padded to consistent 72 chars); "Also here" display name fix in `LoadSaveByFileName()` and `CreateNewGame()` (sets `PlayerSession.ActiveCharacterName` to display name)
- `Scripts/Systems/CombatEngine.cs` — New `ApplyPvPSpellEffect()` method mapping spell effects to Character StatusEffect system; `ExecutePvPSpell()` now applies SpecialEffect/ProtectionBonus/AttackBonus; PvP combat loop checks `PreventsAction()` and calls `ProcessStatusEffects()` each round; computer AI spell casting applies effects to opponent; group quest kill tracking added in `DistributeGroupRewards()` via `QuestSystem.OnMonsterKilled()` per grouped player
- `Scripts/Locations/BaseLocation.cs` — MUD prompt hint "(type 'look' to redraw)" for players below level 5
- `Scripts/Locations/DungeonLocation.cs` — Group merchant encounter: gold split among alive group members; potions and darkness applied to all participants via RemoteTerminal
- `Scripts/Locations/TempleLocation.cs` — `DisplayGodRanking()` merges player immortals (from `GetImmortalGodsAsync()`) with NPC gods; sorted by followers; player gods shown in bright_cyan
- `wezterm.lua` — `initial_rows` changed from 30 to 50
- `launchers/Play-Accessible.bat` — **NEW** — Windows accessible launcher (bypasses WezTerm, runs in console)
- `launchers/play-accessible.sh` — **NEW** — Linux/macOS accessible launcher (bypasses WezTerm, runs in terminal)
- `.github/workflows/ci-cd.yml` — Accessible launchers copied into all 5 build targets (3 desktop + 2 Steam depots) with chmod +x for shell scripts
