# v0.61.1 -- Beta

Hotfix on top of v0.61.0. Two permadeath-related fixes: the zombie-character bug from GitHub issue #106, and a high-severity alt-character bug discovered while auditing the permadeath flow where dying on an alt could delete the player's main character (potentially an immortal) instead of the alt that actually died.

## Fix: Old God permadeath left character in zombie state until logout (issue #106)

Player report (fastfinge / Samuel Proulx): "After you die to a God in the dungeon, even when out of res, your character is not deleted until you actually log off. As well, you don't awaken in the temple like it says. You awaken in the dungeon instead."

Confirmed two layered bugs.

**Layer 1: `OldGodBossSystem` post-defeat block ignored permadeath state.**

When a player dies during an Old God boss fight, the call stack is roughly: `OldGodBossSystem.FightOldGod` -> `CombatEngine.PlayerVsMonsters` -> `CombatEngine.HandlePlayerDeath`. The death handler correctly routed to `HandleExcessiveDeathsPermadeath` -> `PermadeathHelper.ExecutePermadeath` when the player was at 0 resurrections, which played the permadeath cinematic ("your character has been erased forever"), purged world-state references, deleted the DB row, and broadcast the eulogy server-wide. Then control returned all the way up to the Old God boss wrapper, which unconditionally ran its own post-defeat recovery block at `OldGodBossSystem.cs:1425`:

```
"Everything goes dark..."
"But something pulls you back from the edge."
"You wake up, battered but alive. The god is gone."
player.HP = player.MaxHP / 4
player.Experience -= (boss.Level * 100)
```

Direct contradiction with the permadeath cinematic the player just saw. HP came back. The XP penalty applied to a character that had just been deleted. The player ended up at a "Press Enter to continue" prompt still in the dungeon.

Fix: at the top of the boss-defeat block, check `SessionContext.Current.IsIntentionalExit`. `ExecutePermadeath` sets this flag as part of its cleanup. If set, return the `PlayerDefeated` result struct immediately -- no wake-up message, no HP restore, no XP penalty (the character is gone, there's nothing left to penalize).

**Layer 2: `PermadeathHelper.ExecutePermadeath` didn't actively disconnect the session.**

The original design assumed `IsIntentionalExit = true` would unwind the session loop, but that flag is actually just a "don't run emergency save on disconnect" marker -- it doesn't kill the network read. Nothing in the player's running game loop was watching for it, so after the permadeath cinematic finished printing "Disconnecting." and returning, the player was still connected, still in the dungeon, still able to walk to other rooms and pick menu options. Their character was gone from the DB (and any save attempt was blocked by `MarkUsernameErased`), but the in-memory `Character` object kept responding to commands until the player manually logged off. fastfinge correctly described this as "your character is not deleted until you actually log off" from his perspective -- the visible state of the character persisted in the active session even though the DB had already been wiped.

Fix: at the end of `ExecutePermadeath`, after `IsIntentionalExit = true` is set, do a fire-and-forget `session.DisconnectAsync("You have been permadied.")`. This closes the TCP socket, which unblocks the session's blocking `ReadLineAsync` with an exception, which propagates through `PlayerSession.RunAsync`'s catch block and tears the session down through its normal cleanup path. Looks up the session via `MudServer.Instance.ActiveSessions` by lowercased username (same pattern the function already uses for `SuppressDisconnectSave`).

Fire-and-forget rather than awaited because `DisconnectAsync` has a built-in 2-second delay to let the disconnect message render, and we don't want to block the calling code waiting for that. The socket-close still races into effect on the next IO point, which is the next prompt or terminal write -- always within milliseconds.

**Net behavior after the fix.**

Dying to an Old God at 0 resurrections now: plays the cinematic, deletes the DB row, broadcasts the server-wide eulogy, posts a news entry, returns control to `OldGodBossSystem`, which sees `IsIntentionalExit` set and returns immediately (skipping the contradictory wake-up message and HP restore), and then the socket closes from `ExecutePermadeath`'s fire-and-forget disconnect, ending the session cleanly. The player sees the cinematic, the disconnect message, then the client-side reports "connection closed by peer." No zombie character, no contradictory "you wake up" message, no log-off-to-finalize-deletion delay.

## Fix: alt-character permadeath could delete your main character instead

Discovered while auditing the permadeath flow above. High severity: any player with an alt was one bad fight away from losing their main character (potentially their immortal) without realising it, regardless of how many resurrections the main had remaining.

**Root cause.** `PermadeathHelper.ExecutePermadeath` read the save key off `SessionContext.Current.Username`. That field is `init`-only on `SessionContext` and is locked to the SSH account name at session creation. The account name is also the main character's save key (e.g. `rage`). When a player switches to an alt via `SwitchToAltCharacter`, only `SessionContext.CharacterKey` updates to the alt key (e.g. `rage__alt`). `Username` never changes for the lifetime of the session.

Every save-routing site in the game reads `CharacterKey` via `DoorMode.GetPlayerName()` -- AutoSave, manual save, world-state writes -- so alt saves correctly land in the `rage__alt` row. Permadeath was the one site reading `Username` directly.

Consequence: alt dies with 0 resurrections -> `DeleteGameData("rage", ...)` archives and deletes the main character's row from `players`, `PurgePlayerWorldState("rage")` strips the main's guild membership / god worship / kingship / marriage / NPC pregnancies / child parentage, the server-wide eulogy broadcasts the alt's display name dying, and the alt's `rage__alt` row sits untouched in the DB. The /restore window then offers to restore the main (which the player didn't lose) instead of the alt (which actually died). For an immortal main, the same bug would have wiped a player-god from the Pantheon while the mortal alt walked off without a scratch.

**Fix.** Two-key routing in `PermadeathHelper`:

- **Save key (`CharacterKey` first, fall back to `Username`).** Used for `MarkUsernameErased`, `PurgePlayerWorldState`, and `DeleteGameData`. On an alt, all three target the alt's row. On the main, behaviour is identical to pre-fix (CharacterKey == Username when no switch happened). The fallback covers single-player / BBS where `SessionContext` is null and `CharacterKey` may be empty.

- **Session lookup (`Username` only).** `MudServer.ActiveSessions` is keyed by SSH account because one socket serves both main and alt -- you can't have two sessions for one account. The `SuppressDisconnectSave` flag and the force-disconnect both have to use `Username` to find the right session entry. Pre-fix this happened to work for mains and was broken for alts (the alt-key lookup against an account-keyed dictionary just missed entirely, silently skipping `SuppressDisconnectSave` and silently skipping `DisconnectAsync`).

**Net behavior after the fix.**

| Scenario | Pre-fix | Post-fix |
|---|---|---|
| Main dies, 0 res | Main row deleted (correct) | Main row deleted (correct) |
| Alt dies, 0 res, main has res left | **Main row deleted, alt survives** | Alt row deleted, main untouched |
| Alt dies, 0 res, main is immortal | **Immortal deleted from Pantheon, alt survives** | Alt row deleted, immortal untouched |
| Single-player / BBS (no SessionContext) | Player name fallback (correct) | Player name fallback (correct) |

Existing accounts that already had a main wiped by this bug have a 7-day `/restore` window via `deleted_characters` -- contact an admin on Discord with the wiped character name.

**Note on the v0.61.0 binary that's currently on the server**: the issue #106 fixes were deployed mid-v0.61.0 lifecycle, so the running binary already has those patches. The version-string on `version.txt` says `0.61.0` but actually contains the issue-106 fixes. The next deploy of this v0.61.1 binary bumps the version-string to match.

## Files Changed

- `Scripts/Core/GameConfig.cs` -- Version 0.61.1.
- `Scripts/Systems/OldGodBossSystem.cs` -- `IsIntentionalExit` short-circuit at the top of the boss-defeat block, before the "you wake up battered but alive" recovery flow runs.
- `Scripts/Systems/PermadeathHelper.cs` -- (1) Force-disconnect via `session.DisconnectAsync` after the permadeath cinematic so the session unwinds instead of leaving the player in zombie state until manual logout. (2) Route the save key through `CharacterKey` (falling back to `Username`) for `MarkUsernameErased`, `PurgePlayerWorldState`, and `DeleteGameData` so alt-character deaths target the alt's DB row instead of the main account's; separate `sessionLookupKey` continues to use `Username` for `ActiveSessions.TryGetValue` since the session dictionary is keyed by SSH account.

---

Reported by fastfinge (Samuel Proulx) in [issue #106](https://github.com/binary-knight/usurper-reborn/issues/106). Thanks for the precise repro and the clear two-bug-in-one description.
