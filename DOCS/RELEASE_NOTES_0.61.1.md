# v0.61.1 -- Beta

Hotfix release on top of v0.61.0. Two permadeath-related fixes (the zombie-character bug from GitHub issue #106, and a high-severity alt-character bug where dying on an alt could delete the player's main character), plus a Wilderness menu visibility fix, an NPC population-diversity audit pass, and two anti-spam caps on the family system.

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

## Fix: Pilgrimage and Tamed Beasts menu entries invisible outside BBS mode

Two new v0.61.0 features (`[P] Pilgrimage to a Druid's Shrine` in the Wilderness menu, and `[Y] Tamed Beasts` in the Home menu) were wired into the BBS-style menu rendering paths and the structured Electron menu data, but not into the visual / SSH / web text menu paths that most players actually use. The input handlers accepted both keys correctly, so typing `P` in the Wilderness or `Y` at Home still worked, but the menus didn't show the options as available, so unless a player went hunting for them nothing told them they existed. Two player reports: "no option to Pilgrimage" and "I don't see the tamed animal stuff when I'm in my home, not a menu option."

Fix for Wilderness: added the Pilgrimage line to `WildernessLocation.DisplayLocation` between the Discoveries section and the Return-to-Main-Street line, mirroring the BBS-path version. Renders as `[P] Pilgrimage -- attune to a Druid's Shrine (24h passive)` when no shrine is active, or `[P] Pilgrimage -- attuned to {Name} ({hours}h left)` when one is. Screen reader mode swaps the bracket form for `P. ...`. Four new localisation keys (`wilderness.pilgrimage_visual` / `_sr` / `_active_visual` / `_active_sr`) added in all five languages (en/es/fr/it/hu), each mirrored from the existing BBS-path translations.

Fix for Home: added `[Y] Tamed Beasts` as the third entry on the Family/Children row in `ShowHomeMenu`, alongside `[F] Family` and `[C] Spend Time with Child`. Uses the existing `home.pet_roster` localisation key, which was already present in all five languages from v0.61.0 but only wired into the BBS menu and Electron menu data.

## Fix: NPC orientation diversity could miss its minimums and dilute over time

Audit pass on `NPCSpawnSystem.EnsureOrientationDiversity()`. Two related issues caught.

**Issue 1: shuffled-pool loop bug.** The function maintains a minimum of 2 gay male, 2 lesbian, and 2 bisexual NPCs after the initial random-roll spawn pass. It walked the shuffled straight-NPC list with a single shared `idx` across three sequential loops (gay-male, lesbian, bisexual). When the gay-male loop encountered a female straight NPC, it skipped her, but the `idx++` still advanced. That same female was then unavailable to the lesbian loop. Under an unlucky shuffle (e.g., several females early in the list while the gay-male loop is still running), the function could conclude "successful run" while having only converted one gay male and zero lesbians. The function fired only at world birth, so the rolled-low result was the world's starting roster.

Fix: split the straight pool into separate gender-filtered lists. The gay-male loop pulls from straight males only, the lesbian loop pulls from straight females only, and the bisexual loop re-queries the remaining-still-straight pool gender-agnostically. The 2/2/2 floor is now reliably hit regardless of shuffle order. Also corrected a stale doc comment ("3 gay/lesbian and 3 bisexual") that pre-dated a constant change.

**Issue 2: diversity floor never re-applied as the population turned over.** `EnsureOrientationDiversity` was only called once, at initial world spawn from `InitializeClassicNPCs`. Two later NPC-addition paths bypassed it entirely:

- `NPCSpawnSystem.GenerateImmigrantNPC` -- new NPCs joining via the immigration system to replenish races
- `FamilySystem.ConvertChildToNPC` -- children born to NPCs reaching age 18 and becoming adult NPCs

Both paths use `PersonalityProfile.GenerateForArchetype("commoner")` for personality, which has a ~95/2/3 percent roll for straight / gay-lesbian / bisexual. With no diversity floor on the late-joiner paths, the long-running population trended straight as the diversity-floored initial roster died of old age and was replaced. After a few months of game time, the gay/lesbian/bisexual counts could fall well below the 2/2/2 design intent.

Fix: new `NPCSpawnSystem.NudgeLateJoinerOrientation(npc)` helper. Called from both `GenerateImmigrantNPC` and `ConvertChildToNPC` after the personality is assigned. Checks the current living-NPC orientation counts, and if a gap exists below the 2/2/2 minimum, the new arrival has a 60% chance of being rerolled to fill the gap (gender-appropriate for gay/lesbian, gender-agnostic for bisexual). Only nudges arrivals who rolled Straight in the default pass, so the natural roll still wins most of the time.

Net effect: world population diversity stays at or near the design minimum indefinitely instead of slowly straight-shifting. Visible to players over the next few real-world days as natural NPC turnover happens. The existing roster on the live server already has whatever distribution the buggy `EnsureOrientationDiversity` produced when world_state was first initialised; the new code shapes the population on the next immigrant arrival and the next child reaching adulthood, not retroactively.

## Anti-spam: family-system caps

A player was observed creating 14 newborn children in roughly one hour by chaining bedroom encounters with their spouse. The diminishing-returns curve on `ChildBonuses` already caps useful gameplay value at 5 children, so the spam wasn't grinding for stats. It was using the family tree as a name-graffiti system, tagging community-member references into the database as their offspring. Two caps added to close the vector.

**5-children hard cap.** New `GameConfig.MaxPlayerChildren = 5`. `IntimacySystem.CheckForPregnancy` now reads `FamilySystem.GetChildrenOf(player).Count` after the spouse/lover and gender checks. If the player already has 5 non-deleted children in the family registry, the pregnancy roll silently does not fire. Children who reach age 18 and convert to NPCs (`Deleted=true` after `ConvertChildToNPC`) and dead children stop counting toward the cap, so the slot reopens naturally over time. Silent gate rather than a flavor message, because pregnancy is already a 15%-per-encounter probabilistic event and a quiet no-op reads as "didn't conceive this time" rather than a UI dead-end.

**3-per-day intimate scene cap.** New `GameConfig.MaxIntimateEncountersPerDay = 3` and `Character.IntimateEncountersToday` daily counter, with full save/load plumbing mirroring the existing daily-counter pattern (`LoveStreetVisitsToday`, `DrinkingGamesToday`, `MurdersToday`, `GauntletRunsToday`, etc). Counter resets at midnight via `DailySystemManager.ApplyDailyReset`. New `EnforceDailyIntimateCap` helper gates the three `IntimacySystem` entry points: `StartIntimateScene` (single partner), `StartGroupScene` (multi-partner), and `ApplyIntimacyBenefitsOnly` (fade-to-black). `InitiateIntimateScene` delegates to `StartIntimateScene` so it's covered. Counter increments after the scene completes so a capped attempt doesn't burn a slot. Localised flavor line in all five languages explains the cap when hit: "You've shared enough intimate moments today. Some appetites need time to return. Try again tomorrow." LoveStreet's existing 2/day cap is left independent; LoveStreet and the bedroom path are different contexts with different economies.

Combined effect: a player can have at most 3 scenes per day, 15% pregnancy chance per spouse encounter, hard cap of 5 living children. The 14-children-in-an-hour pattern is mechanically impossible. The existing affected character keeps their offspring (no retroactive deletion); a future similar pattern can't recur.

**Note on the v0.61.0 binary that's currently on the server**: the issue #106 fixes were deployed mid-v0.61.0 lifecycle, so the running binary already has those patches. The version-string on `version.txt` says `0.61.0` but actually contains the issue-106 fixes. The next deploy of this v0.61.1 binary bumps the version-string to match.

## Files Changed

- `Scripts/Core/GameConfig.cs` -- Version 0.61.1. New `MaxPlayerChildren` and `MaxIntimateEncountersPerDay` constants.
- `Scripts/Core/Character.cs` -- New `IntimateEncountersToday` daily counter.
- `Scripts/Systems/OldGodBossSystem.cs` -- `IsIntentionalExit` short-circuit at the top of the boss-defeat block, before the "you wake up battered but alive" recovery flow runs.
- `Scripts/Systems/PermadeathHelper.cs` -- (1) Force-disconnect via `session.DisconnectAsync` after the permadeath cinematic so the session unwinds instead of leaving the player in zombie state until manual logout. (2) Route the save key through `CharacterKey` (falling back to `Username`) for `MarkUsernameErased`, `PurgePlayerWorldState`, and `DeleteGameData` so alt-character deaths target the alt's DB row instead of the main account's; separate `sessionLookupKey` continues to use `Username` for `ActiveSessions.TryGetValue` since the session dictionary is keyed by SSH account.
- `Scripts/Locations/WildernessLocation.cs` -- Added the `[P] Pilgrimage` line to the visual / SSH / web / Electron menu rendering path (was previously only emitted by the BBS-style rendering path).
- `Scripts/Locations/HomeLocation.cs` -- Added the `[Y] Tamed Beasts` line to the visual / SSH / web text menu rendering path (was previously only emitted by the BBS-style menu row and the Electron structured menu data).
- `Scripts/Systems/NPCSpawnSystem.cs` -- Rewrote `EnsureOrientationDiversity` with gender-split pools so the 2/2/2 floor is reliably hit regardless of shuffle order. Added `NudgeLateJoinerOrientation` helper called from `GenerateImmigrantNPC` so immigrants don't dilute population diversity over time.
- `Scripts/Systems/FamilySystem.cs` -- `ConvertChildToNPC` now calls `NudgeLateJoinerOrientation` after personality assignment so children of NPCs reaching adulthood also pass through the diversity floor.
- `Scripts/Systems/IntimacySystem.cs` -- New `EnforceDailyIntimateCap` helper gating `StartIntimateScene`, `StartGroupScene`, and `ApplyIntimacyBenefitsOnly`. Increment `IntimateEncountersToday` on scene completion. Hard cap of `MaxPlayerChildren` non-deleted children added to `CheckForPregnancy`.
- `Scripts/Systems/SaveDataStructures.cs` + `SaveSystem.cs` + `Core/GameEngine.cs` + `Systems/DailySystemManager.cs` + `Editor/PlayerSaveEditor.cs` -- `IntimateEncountersToday` save/load plumbing and midnight reset.
- `Localization/{en,es,fr,it,hu}.json` -- New `intimacy.daily_cap_reached` flavor line in all five languages. New `wilderness.pilgrimage_visual` / `_sr` / `_active_visual` / `_active_sr` keys in all five languages, mirrored from the existing BBS-path translations.

---

Reported by fastfinge (Samuel Proulx) in [issue #106](https://github.com/binary-knight/usurper-reborn/issues/106). Thanks for the precise repro and the clear two-bug-in-one description.
