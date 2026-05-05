# v0.60.8 -- Beta

Two bug fixes on top of v0.60.7. One PvP-protection gap reported by a player who was getting attacked while sleeping at home behind the Reinforced Door, and one single-player Linux quality-of-life report where the SysOpConfig file was showing up as a phantom save in the load menu.

---

## Dungeon Reset Scroll restored (PR #101 by Coosh)

Community PR by Coosh restores the Dungeon Reset Scroll to the Magic Shop. The scroll was removed in v0.42.4 when the dungeon respawn timer dropped from 6 hours to 1 hour, on the reasoning that a 1-hour wait made it redundant. With the tighter XP curve and loot improvements since then, players grinding the same floor have a meaningful reason to spend gold to skip the wait rather than sitting idle.

**`[D]` Dungeon Reset Scroll** is now visible in the Magic Shop in all three display modes (standard / SR / BBS). Selecting a floor immediately resets its monster rooms and back-dates `LastVisitedAt` past the respawn window so both reset paths fire on the next visit. Quadratic pricing (`1000 + level² × 5`) keeps low-level use manageable while scaling meaningfully into the late game (Lv.10 = 1.5K, Lv.50 = 13.5K, Lv.100 = 51K, before tax). Tax integration via `CityControlSystem.CalculateTaxedPrice` and `ProcessSaleTax` matches every other taxable shop.

**What can't be reset**: floors flagged `IsPermanentlyClear` (seal floors 15/30/45/60/80/99 and secret-boss floors), Old God boss floors (25/40/55/70/85/95/100), and floors already in their natural respawn window (no point paying when it's free in <1h).

### Post-merge polish

After merge the Phase 1 review surfaced three items, fixed in this release:

- **Hardcoded English body strings** -- the menu items used `Loc.Get` keys in all 5 languages (added in the PR), but the dialog body, gnome speech, prompts, and confirm/success/decline messages were all hardcoded English. A Spanish player would see a Spanish menu entry that opened an English shop dialog. Added 23 new keys per language (`magic_shop.reset_scroll_header` / `_intro1..4` / `_no_eligible*` / `_price` / `_you_have` / `_too_expensive` / `_floors_avail` / `_floor_entry` / `_prompt` / `_cancelled` / `_tax_label` / `_confirm` / `_unfurl1..2` / `_success*` / `_decline`) translated for en / es / fr / hu / it. All call sites in `BuyDungeonResetScroll` now route through `Loc.Get`.
- **Unicode separator** -- the `═══ Dungeon Reset Scroll ═══` header was hardcoded box-drawing characters that mangle in screen-reader and CP437 BBS modes. Replaced with `WriteSectionHeader(Loc.Get("magic_shop.reset_scroll_header"), "magenta")`, the same SR-aware helper the rest of the Magic Shop uses (`curse_removal`, `enchant_bless`, etc.).
- **Duplicated `OldGodFloors` array** -- the PR introduced a fourth literal copy of `{ 25, 40, 55, 70, 85, 95, 100 }`. Promoted `DungeonLocation.OldGodFloors` from `private` to `public static readonly` and replaced the duplicate copies in `MagicShopLocation`, `SettlementLocation`, and `AlignmentSystem`. Future floor changes touch one place.

Files: `Scripts/Locations/MagicShopLocation.cs` (PR + post-merge polish), `Scripts/Locations/DungeonLocation.cs` (`OldGodFloors` made public), `Scripts/Locations/SettlementLocation.cs` and `Scripts/Systems/AlignmentSystem.cs` (use central constant), `Localization/{en,es,fr,hu,it}.json` (23 new keys per language).

---

## Bug fix: poison and burn DoTs cancelling each other out

Player report (Lumina, Lv.23 Sage): *"Poison status does not work when burning status is active. Existing poison status is erased when Roast is cast. Does not work at all if I cast Poison after Roast."*

Architectural cause: monster fire DoT and poison DoT shared the **same** `PoisonRounds` counter on `Monster.cs`, with `IsBurning` as a flag-based "discriminator." The tick handler only fired ONE DoT per round (whichever the flag pointed at). Casting Roast on a poisoned target overwrote the burn state -- `IsBurning = true; PoisonRounds = N` -- which erased the poison's tick branch. Casting Poison after Roast did the inverse: set `Poisoned = true` but `IsBurning` stayed true so the tick still rendered as a burn.

The same monster-side burn-apply pattern existed in 4 places: `case "fire"` in single-monster spell handler, single-monster ability handler, multi-monster ability handler, and the engulfed-in-flames spell. All four set `IsBurning = true` and overwrote `PoisonRounds`. (The Mystic Shaman's Searing Totem additionally set `IsBurning = true` without ANY duration, so the burn flag was true but the tick gate `PoisonRounds > 0` was false -- the burn never actually fired.)

### Fix

New independent `BurnRounds` field on `Monster` (alongside existing `PoisonRounds`). Burn-apply sites now set `IsBurning = true; BurnRounds = duration` and leave `PoisonRounds` / `Poisoned` alone. Poison-apply sites unchanged.

Tick handler in `ProcessMonsterStatusEffects` rewritten as two independent blocks. Both burn and poison can fire on the same round if both are active. Burn deals slightly more per-tick than poison (matches the higher-damage nature of fire). Each block has its own death-message and depleted-flag-reset path.

Status display updated: instead of showing one of `BURN(N)` or `PSN(N)` based on the discriminator flag, it now shows both independently when both are active. Searing Totem now properly seeds `BurnRounds` so the totem's fire DoT actually ticks.

Player side (Character.cs) was unaffected -- the player uses the generic `ActiveStatuses` dictionary where `Poisoned` and `Burning` are already independent entries with their own per-round damage logic.

Files: `Scripts/Core/Monster.cs` (new `BurnRounds` field, reset on combat-state-clear), `Scripts/Systems/CombatEngine.cs` (tick handler split, 4 apply sites migrated, Searing Totem fix, status display).

---

## Bug fix: home-sleepers attackable in the PvP arena

Player report: "Players who go to sleep in their houses with doors upgrade are still available for attacking in the PvP arena. Sleeping in the house is supposed to prevent all PvP while offline."

The Reinforced Door home upgrade is sold explicitly as the safe-sleep option (the Inn and Dormitory sleeper-attack flows already filter by `SleepLocation == "inn"` / `== "dormitory"`, so home-sleepers were never vulnerable through those paths). But `ArenaLocation.ChooseOpponent` had 6 opponent-eligibility filters and none of them checked `sleeping_players`. Anyone asleep at home still appeared in the arena's opponent list and was attackable.

Fix: `ChooseOpponent` now fetches the sleeping-players list alongside the other backend reads, builds a case-insensitive `HashSet<string>` of usernames where `SleepLocation == "home"` and not dead, and adds a `.Where(p => !sleepingAtHome.Contains(p.Username))` clause to the eligibility chain. Home-sleepers are now PvP-immune across all three vectors (arena + the already-correct inn / dormitory flows).

Files: `Scripts/Locations/ArenaLocation.cs`.

---

## Bug fix: paid arenas (Gauntlet, Pit Fight) consumed a resurrection on loss

Player report (Spud, Lv.28 Cleric): *"Losing in the gauntlet counted as a real death. perhaps a warning before entering if that's how it's supposed to be."*

The Gauntlet at AnchorRoad and the Dark Alley pit fight (vs monster path) both call `CombatEngine.PlayerVsMonster` directly. A wave loss in the Gauntlet -- or a single round loss in the pit fight -- went through the normal PvE death pipeline: a resurrection burned online, full Temple/Deal/Accept menu single-player. On top of the entry fee + daily fight slot already paid, that's a triple penalty for a paid arena entertainment surface.

Architectural cause: only `IsArrestCombat` (used by the Royal Guard murder-arrest combat) had a short-circuit in `HandlePlayerDeath` to set HP=1, skip resurrection consumption, and skip the death cinematic. No equivalent flag existed for paid-arena surfaces, so they all went through the lethal path.

### Fix

New transient `Character.IsExhibitionCombat` flag (parallel to `IsArrestCombat`, not serialized). `HandlePlayerDeath` checks it right after the arrest short-circuit -- if true: HP set to 1, no resurrection consumed, no permadeath, no death cinematic, `EXHIBITION` log line. Caller clears in `finally`.

Wired into both PvE arena surfaces: `AnchorRoadLocation.StartGauntlet` sets the flag around each wave's `PlayerVsMonster`, `DarkAlleyLocation.PitFightMonster` sets it for the bare-knuckle fight. Loss flavor text gets a follow-up "Medics drag you out and patch you up. Your soul is intact -- only your pride and the entry fee are gone." line so players see the recovery is automatic. Gauntlet entry screen gains a small "(Medics stand by: a loss costs the entry fee, not a resurrection.)" note so the stakes are clear before paying in.

Pit fight vs NPC (PvP path through `PlayerVsPlayer`) doesn't route through `HandlePlayerDeath` so resurrection wasn't consumed there to begin with -- but the loss left the player at HP=0, which was its own latent bug (would die on the very next encounter from leftover damage). The PvP loss branch now restores `HP = 1` inline if it dropped to zero, with the same recovery flavor line for consistency.

What still costs you: entry fee (Gauntlet) / spectator-bet stake (Pit Fight), the daily fight slot, any wave/pit rewards from rounds you didn't win, and any HP/mana/status damage you took. Just not a resurrection on top.

Files: `Scripts/Core/Character.cs` (new `IsExhibitionCombat` flag, JsonIgnore), `Scripts/Systems/CombatEngine.cs` (short-circuit in `HandlePlayerDeath`), `Scripts/Locations/AnchorRoadLocation.cs` (flag set/unset around wave combat, recovery line, entry-screen safety note), `Scripts/Locations/DarkAlleyLocation.cs` (flag set/unset around monster pit fight, HP=1 restore on PvP pit fight loss, recovery line on both paths), `Localization/{en,es,fr,hu,it}.json` (3 new keys per language: `anchor_road.gauntlet_recovered`, `anchor_road.gauntlet_desc_safety`, `dark_alley.pit_recovered`).

---

## Bug fix: Cleric quickbar showed duplicate ability/spell names

Player report (Spud, Lv.37 Human Cleric): *"The cleric has two abilities called Holy Smite. One uses mana, the other stamina. Should have a different name."*

The Cleric class kit had four name collisions where a class ability (stamina-cost) and a spell (mana-cost) shared the exact same display name. The quickbar/menu rendered both with identical labels and players had no way to tell at a glance which entry was which:

- **Holy Smite** -- ability (lv8, 30 STA, +holy bonus vs undead) vs spell slot 9 (35 MP, 45-65 dmg, 2x vs undead/demons)
- **Sanctuary** -- ability (lv16, +45 DEF, 35 STA) vs spell slot 7 (25 MP, +18 protection, 3 rounds)
- **Resurrection Prayer** -- ability (lv68, 160 heal + 30 DEF, 75 STA) vs spell slot 22 (160 MP, 320-450 hp)
- **Divine Intervention** -- ability (lv80, invulnerable 2 rounds, 85 STA) vs spell slot 20 (120 MP, +85 protection, 5 rounds)

A programmatic audit of every class abilities-vs-spells pair confirmed the collisions are Cleric-only (the other six spell-using classes -- Magician, Sage, Tidesworn, Wavecaller, Cyclebreaker, Abysswarden -- have no overlap). Per the bug-fix conversation, the abilities (stamina, the newer additions) were renamed; the spells keep their iconic names.

### Renames (Cleric class abilities only)

| Ability id | Old name | New name |
|---|---|---|
| `holy_smite` | Holy Smite | **Holy Shite** |
| `sanctuary` | Sanctuary | **Just a Flesh Wound** |
| `resurrection_prayer` | Resurrection Prayer | **I'm Not Dead Yet** |
| `divine_intervention` | Divine Intervention | **The Spanish Inquisition** |

Mechanics, level requirements, costs, cooldowns, special effects, and ability ids are all unchanged. Only the player-facing `Name` and `Description` fields were edited, so existing learned-ability lists / quickbar slots / save data round-trip cleanly with no migration needed.

The `combat.ability_holy_smite` flavor key (which fires from the `SpecialEffect = "holy"` handler when a holy-tagged ability hits an undead/demon target) used to shout "HOLY SMITE!" -- mismatched against the new ability name. Reworded to be name-agnostic ("Holy fire burns the undead! +{0} damage!") in all 5 languages so the bonus flavor reads cleanly regardless of which holy ability triggered it.

Files: `Scripts/Systems/ClassAbilitySystem.cs` (4 ability `Name` + `Description` field updates, with audit comment), `Localization/{en,es,fr,hu,it}.json` (`combat.ability_holy_smite` reworded to be ability-name-agnostic).

---

## Bug fix: NPC AI casting Focus then Battle Cry (issue #99)

Reporter (fastfinge, GitHub issue #99): *"Teach NPC AI not to use focus then battlecry. Or for that matter, any skill that increases attack for 1 round, followed by any non-attack skill. I notice NPCs using focus then battlecry, and it's a bit silly."*

Confirmed and exactly as described. `Focus` (universal ability, AttackBonus 20, Duration 1) and `Battle Cry` (Warrior/Barbarian, AttackBonus 40, Duration 4) both set `TempAttackBonus` + `TempAttackBonusDuration` on the casting teammate. Round-end bookkeeping decrements the duration, and Focus's Duration=1 means the buff is gone before the NPC's next action -- so casting Focus and then Battle Cry on the next round both wastes Focus's stamina (bonus expired without ever firing on an attack) AND lets Battle Cry overwrite Focus's bonus (40 > 20) before any attack consumed it. Worse, the NPC could keep stacking buffs round after round and never actually swing while buffed.

Architectural cause: `TryTeammateClassAbility` in `CombatEngine.cs` had filters for defensive-buff stacking (line 17180, the v0.56.1 tank defensive-spread fix) and status-immunity stacking (line 17195, the v0.57.7 Iron-Will-overwriting-Arcane-Immunity fix) but no equivalent filter for attack-buff stacking. Once an NPC was attack-buffed, the pool selection was free to pick another non-attack ability the next round.

### Fix

New filter in `TryTeammateClassAbility` immediately after the tank-priority taunt block. When the teammate has an active attack buff (`TempAttackBonusDuration > 0 && TempAttackBonus > 0`) AND no one in the party needs healing, the affordable-abilities pool is narrowed to `Attack` / `Debuff` types. If at least one damage ability remains, the AI picks from that subset (consuming the buff). If no damage abilities are available, the function returns false and the teammate falls through to a basic attack on the weakest monster -- which still consumes the buff.

Two intentional exemptions, both higher-priority than buff use:

- **Tank-priority taunt** (line 17218-17229) runs first; a tank without an active taunt on any monster gets to taunt regardless of attack-buff state. Tanks that self-buff and then need to taunt aren't blocked.
- **Healing need** (`anyoneNeedsHealing == true`, set at line 17162 when any party member is below 70% HP) bypasses the filter so heals can still be chosen. Keeping a teammate alive is more important than spending an attack buff this round.

The fix complements the existing buff-stacking-prevention filters and follows the same pattern. No changes to ability data, mechanics, or the buff-overwrite resolution in `ApplyAbilityEffects` (which already keeps the stronger of new/existing AttackBonus, so player-cast Bardic Inspiration on top of the NPC's Battle Cry still does the right thing).

Files: `Scripts/Systems/CombatEngine.cs` (one new filter block in `TryTeammateClassAbility`).

---

## FILE_ID.DIZ included in download zips

Sysop request: a BBS file database expects `FILE_ID.DIZ` inside the downloaded archive to populate the door's description. Previously the zip only contained `LICENSE`, `GPL_NOTICE.txt`, and `README.txt`.

Added: `dist/FILE_ID.DIZ` (10 lines, max 46 chars wide, ASCII, CRLF line endings -- the standard BBS file-database format). The CI workflow now copies it into the build output alongside the existing compliance files, so it ships in all 6 plain platform zips (and transitively in the WezTerm desktop bundles for Windows x64 / Linux x64). No effect on Steam build or live-server deploy.

Content patterned after the original 1993 Usurper FILE_ID.DIZ (kept the *kicking fantasy* tagline) but updated for the v0.60.x feature set: 100-floor dungeon, seven Old Gods, online multiplayer, ascend-to-godhood path, GPL v2.

Files: `dist/FILE_ID.DIZ` (new), `.github/workflows/ci-cd.yml` (one extra `cp` in the GPL-compliance step).

---

## Bug fix: SysOpConfig file appearing as a phantom save

Player report (single-player Linux): *"The game creates `~/.local/share/UsurperReloaded/saves/sysop_config.json` when it starts up, and that file is picked up as a save."*

The file is the SysOpConfig (admin tunables, MOTD, difficulty multipliers, BBS-mode persistence layer). It lives in the save directory intentionally so BBS sysops get per-BBS-namespaced configuration, but `FileSaveBackend`'s three listing paths (`GetAllSaves`, `GetPlayerSaves`, `GetAllPlayerNames`) only filtered by `_autosave` / `_backup` / `emergency_` patterns. `sysop_config.json` slipped through, failed `SaveGameData` deserialization, and fell into the v0.57.18 filename-fallback path -- showing up as a phantom "sysop_config" character in the load menu.

Fix: new `IsAuxiliaryFile(fileName)` private static helper at the top of `FileSaveBackend` returning true for `sysop_config.json` (and easy to extend if any future auxiliary files land in the save dir). All three listing methods now skip auxiliary files alongside the existing rotation / backup / emergency filters. The file's persistence path is unchanged -- BBS namespacing is preserved.

Existing single-player saves with the phantom slot will see it disappear automatically on next launch.

Files: `Scripts/Systems/FileSaveBackend.cs`.

---

## Files Changed

### No SQLite schema changes
Code-only release. Existing `server_config` / `server_config_schema` / `server_config_apply_queue` schema from v0.60.7 carries forward unchanged.

### New files
- `dist/FILE_ID.DIZ` -- BBS file-database description (10 lines, CRLF).

### Modified files (game)
- `Scripts/Core/GameConfig.cs` -- version bump to 0.60.8.
- `Scripts/Core/Character.cs` -- new `IsExhibitionCombat` JsonIgnore flag for paid-arena non-lethal combat.
- `Scripts/Core/Monster.cs` -- new `BurnRounds` field; reset to 0 alongside `IsBurning` on combat-state-clear.
- `Scripts/Locations/AnchorRoadLocation.cs` -- Gauntlet wave combat wrapped in exhibition flag (try/finally); recovery flavor line on collapse; entry-screen safety note.
- `Scripts/Locations/ArenaLocation.cs` -- `ChooseOpponent` skips home-sleepers from the PvP opponent list.
- `Scripts/Locations/DarkAlleyLocation.cs` -- pit fight vs monster wrapped in exhibition flag; HP=1 restore on PvP pit fight loss; recovery flavor line on both defeat paths.
- `Scripts/Locations/DungeonLocation.cs` -- `OldGodFloors` promoted from `private` to `public static readonly` so other systems can reference the canonical list rather than duplicating literals.
- `Scripts/Locations/MagicShopLocation.cs` -- Dungeon Reset Scroll restored (PR #101); body strings localized; `WriteSectionHeader` replaces hardcoded Unicode separator; uses central `OldGodFloors`.
- `Scripts/Locations/SettlementLocation.cs` -- watchtower scout report uses central `OldGodFloors`.
- `Scripts/Systems/AlignmentSystem.cs` -- floor-hint logic uses central `OldGodFloors`.
- `Scripts/Systems/ClassAbilitySystem.cs` -- 4 Cleric ability `Name` + `Description` fields renamed to break the spell-name collision (Holy Shite / Just a Flesh Wound / I'm Not Dead Yet / The Spanish Inquisition). Mechanics and ids unchanged.
- `Scripts/Systems/CombatEngine.cs` -- burn DoT separated from poison DoT (independent counters, both can tick per round); 4 burn-apply sites + Searing Totem migrated; status display shows BURN and PSN independently. Plus new `IsExhibitionCombat` short-circuit in `HandlePlayerDeath` (HP=1, no resurrection consumed, no death cinematic). Plus new attack-buff-stacking filter in `TryTeammateClassAbility` (issue #99) so NPCs don't waste Focus / Battle Cry by chaining a non-attack skill after them.
- `Scripts/Systems/FileSaveBackend.cs` -- new `IsAuxiliaryFile` predicate filters `sysop_config.json` from all three save-listing paths.

### Modified files (localization)
- `Localization/{en,es,fr,hu,it}.json` -- 23 new keys per language for Dungeon Reset Scroll body strings; 3 new keys per language for paid-arena non-lethal flavor (`anchor_road.gauntlet_recovered`, `anchor_road.gauntlet_desc_safety`, `dark_alley.pit_recovered`); reworded `combat.ability_holy_smite` to be ability-name-agnostic.

### Modified files (CI)
- `.github/workflows/ci-cd.yml` -- copy `dist/FILE_ID.DIZ` into the build output so it ships in every platform zip.

---

## Deploy notes

- Standard recipe: publish linux-x64, tar, scp, restart `usurper-mud` and `sshd-usurper`. Web service unchanged.
- The SysOpConfig phantom-save fix only matters for single-player file-backed saves. The live MUD server uses `SqlSaveBackend` and was unaffected.
- The home-sleeper PvP fix matters for any MUD server with players who use the Reinforced Door upgrade.
