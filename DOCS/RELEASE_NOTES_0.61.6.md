# v0.61.6 -- Beta

Hotfix on top of v0.61.5. Three player-experience fixes that came out of Discord threads about the v0.60.0 difficulty increase feeling too punishing. The aggregate combat data did not support a global difficulty rollback (97.6% all-time win rate, 2.1% death rate over the last 48 hours, zero group-fight deaths in the last 7 days), so instead of nerfing monsters this release fixes the three structural things that turn a hard moment into an irritating one: a dungeon re-entry trap, the Cyclebreaker's group-fight weakness, and a Gauntlet champion HP double-multiplication bug.

## Dungeon remembers your last floor

`DungeonLocation.EnterLocation` initialized the floor with `currentDungeonLevel = Math.Max(1, playerLevel)` on every entry. So leaving the dungeon to shop, sell, or regear and then coming back snapped you to your character level instead of the floor you were actually on. For a Lv.50 character who had been carefully working floor 40, re-entry dropped them onto floor 50 and a fight they weren't geared for.

Fix: new `Character.LastDungeonFloor` field (0 = never entered). Dungeon entry now resumes the remembered floor when it is set, falling back to character level only on a true first-time entry:

```csharp
currentDungeonLevel = player != null && player.LastDungeonFloor > 0
    ? player.LastDungeonFloor
    : Math.Max(1, playerLevel);
```

The field updates at every floor-change site (descend, ascend, teleport, change-level) so it always reflects the floor the player was on when they left. The existing safety clamps still run after the resume: `GetMaxAccessibleFloor` (the uncleared-Old-God progression gate) and the `> maxDungeonLevel` cap. Resuming a floor below the player's level band is allowed and harmless -- backtracking to an easier floor was never restricted, only skipping ahead is.

Full save plumbing: `Character.LastDungeonFloor` -> `PlayerData.LastDungeonFloor` -> `SaveSystem` write + `GameEngine.LoadSaveByFileName` restore. Pre-v0.61.6 saves load with `LastDungeonFloor = 0` and fall back to character level on next entry (current behavior), then start remembering from that point on. Works in both single-player and online (the SQL backend serializes the full `PlayerData` blob).

## Cyclebreaker survivability in group fights

A high-level Cyclebreaker reported that the class is fine one-on-one, but its dodge only avoids one attacker per round -- so in a group fight it gets shredded before it can ramp its buffs.

Exactly right. `Character.DodgeNextAttack` is a single-use boolean -- the first incoming monster attack consumes it (`if (player.DodgeNextAttack) { player.DodgeNextAttack = false; ... }`), and every other monster in a group hits normally. The class that's strong 1v1 (where one dodge covers the only attacker) gets shredded in group fights before it can ramp its buffs, because the dodge only ever stops one hit out of the volley.

Fix: Probability Manipulation (the Cyclebreaker's signature passive, previously only a 25% debuff-resist) now also grants a passive per-attack evade. New `GameConfig.CyclebreakerPassiveEvadePercent = 18` -- an 18% chance to phase out of EACH incoming basic attack, rolled separately against every monster in the group rather than consumed by the first. Wired into the monster-attacks-player path right after the existing `DodgeNextAttack` check, so it stacks with (does not replace) the single-use dodge from Temporal Feint / Umbral Step.

Net effect: a Cyclebreaker in a 4-monster fight now shrugs off roughly one attack every one-and-a-half rounds on average, which is enough breathing room to land buffs and stabilize, without making the class unkillable (82% of attacks still land). The 1v1 experience is essentially unchanged (one extra evade roll per fight). New `combat.cyclebreaker_evade` flavor line in all 5 languages.

## Gauntlet champion HP double-multiplication

A Lv.55 Barbarian reported struggling with the first Gauntlet arena champion: after a couple of rounds they were taking roughly half their HP per hit.

Pulled that character's combat telemetry. The Barbarian is a wrecking ball: 123 wins, 1 death, takes literally **zero damage** from regular content (groups of 4 monsters at his level, dealing 40k-81k per fight, 0 taken). Not a class survivability problem. The only death was to Vargash the Blade-Bound, the first Gauntlet champion: a 13-round fight that dealt 65,572 damage, took 15,330, and was still lost.

Dealing 65k and still losing means Vargash's effective HP was enormous. Root cause: `SpawnChampionMonster` ([AnchorRoadLocation.cs:1324](Scripts/Locations/AnchorRoadLocation.cs#L1324)) called `MonsterGenerator.GenerateMonster(effLevel, isBoss: true)`, which already applies the **2.8x boss-tier HP multiplier** ([MonsterGenerator.cs:236](Scripts/Systems/MonsterGenerator.cs#L236)), and then multiplied HP **again** by the champion's own `HpMultiplier` (1.8x for Vargash). So Vargash's HP was base x 2.8 x 1.8 = **~5x base**, and the late champions stacked even worse: the Nameless Tyrant at 4.0x champion x 2.8x boss = **11.2x base HP** at playerLevel+14, effectively impossible.

The champion `HpMultiplier` values (1.4x Selithea up to 4.0x Nameless Tyrant) were designed to BE the boss-tier scaling, per the v0.60.11 design notes ("effective level = playerLevel + LevelBonus, then HP/ATK/DEF multiplied by role-specific multipliers"). Stacking them on top of the generator's boss tier was the bug -- the same bug class as the v0.61.3 "champions tankier than bosses" fix, this time in the Gauntlet.

**Why it killed this character specifically:** Vargash's kit is CriticalStrike + Cleave + Rage. Rage ([MonsterAbilities.cs:679](Scripts/Systems/MonsterAbilities.cs#L679)) grants a one-time +5 STR then a 1.5x-damage attack each proc; layered with a CriticalStrike crit, individual hits spike to roughly half a Lv.55 Barbarian's HP. The per-round average (~1,180) was survivable, but the ~5x HP pool dragged the fight to 13 rounds, giving those Rage+crit spikes enough rolls to accumulate a kill. A Barbarian has no self-heal, so a 13-round attrition fight is mathematically set up to lose where a Cleric or Paladin would heal through it. The fight length was the killer, and the fight length came from the double-multiplied HP.

Fix: `SpawnChampionMonster` now generates the base monster with `isBoss: false` (no 2.8x boss-tier multiplier), applies the champion's own multipliers as the sole boss scaling, then sets `monster.IsBoss = true` afterward. `GenerateMonster`'s `isBoss` parameter does exactly two things -- applies the stat multipliers and sets the `IsBoss` flag -- so generating non-boss and setting the flag manually preserves every boss combat behavior (phase transitions, last-stand cap, boss-targeted AI) without the HP double-multiply.

Effect on Vargash: HP drops from ~5x base to 1.8x base (~65k effective to ~23k). That fight goes from a 13-round attrition loss to a ~5-round win. The whole champion ladder comes back to its intended per-role curve, where the inverse pairing of HP and damage is restored -- the squishy ones (Vargash 1.8x HP / 1.4x atk, Black Twin 1.6x / 1.6x, Selithea 1.4x / 1.2x) hit hard but die fast, the tanks (Korr 3.0x HP / 1.0x atk + Stoneskin, Grok 2.6x / 1.5x + Regeneration) barely scratch you across a long fight, Aedric (2.0x) self-heals, and the Nameless Tyrant (4.0x, down from an impossible 11.2x) stays the deliberate capstone. Attack and defense were also double-multiplied (champion AttackMultiplier x the 1.25x boss-tier, champion DefenseMultiplier x the 1.2x boss-tier) and are now single-applied too.

## Level Master: removed broken Status option

Bug report: the Level Master's Status (view your statistics) option does nothing.

Confirmed: `LevelMasterLocation.ProcessChoice` had no `"S"` case, so pressing `[S] Status` fell through to the base handler and did nothing -- a dead menu entry. Rather than wire up a statistics screen the Level Master doesn't need (the global `%` status command and the character sheet already cover this everywhere), the `[S] Status` entry was removed from all three menu surfaces (visual, screen reader, Electron). The orphaned `level_master.menu_status` / `sr_status_desc` loc keys are left in place, harmlessly unused.

## Children frozen at coming-of-age (away-at-school queue)

Bug report: a player's home reported having a child, but no child appeared at home.

Root cause was in the child coming-of-age path. When a child reaches `ADULT_AGE` (18), `FamilySystem.ConvertChildToNPC` graduates them into an adult world NPC and marks the child record `Deleted`. But the method early-returns when the NPC population is at `MaxNPCPopulation` (the server runs at the 200-NPC cap), and that early-return left the child untouched. `ProcessDailyAging` only re-processes children with `Age < ADULT_AGE`, so once a child hit 18 with the conversion skipped it was never revisited -- it froze permanently as a non-deleted age-18 "child."

That produced the reported symptom across the whole server: `FamilySystem.GetChildrenOf` counts any non-deleted child (no age filter), so the family-records screen showed a child, while the at-home view (which filters `Age < ADULT_AGE`) correctly hid the now-adult offspring -- a count with nothing to show for it. A production data check found the overwhelming majority of children server-wide were stuck at exactly age 18 in this state.

The fix preserves the child rather than deleting them (a grown child shouldn't silently vanish from the player's family). When the population is at cap, the child now enters an "away" state: they have left home for higher learning. They stay on the family roster, shown explicitly as away (so the roster count is always explained), and a retry pass converts them into a real world NPC as soon as the population frees up.

Changes in `FamilySystem` plus a new child location:

1. **New `ChildLocationAway` state.** When `ConvertChildToNPC` hits the population cap, instead of freezing the child it sets their location to away-at-higher-learning and returns. The child is preserved, not deleted.
2. **Queue drain / retry.** `ProcessDailyAging` gets a second pass that runs any non-deleted child at or past `ADULT_AGE` (outside the orphanage, which has its own coming-of-age path) back through `ConvertChildToNPC` every tick. If the population now has room they graduate into a world NPC; if it is still at cap they remain parked as away. This both clears the already-frozen children and drains the away queue over time as NPC slots open up.
3. **Roster display.** The Home family records now label an away child explicitly (a new localized "away at higher learning" line in all five languages) so the count always corresponds to something the player can see, instead of a child that appears nowhere.

Net effect: existing frozen children unfreeze on the next aging tick -- either graduating into world NPCs if there is room or showing as away at school if not -- and no child is ever deleted by the cap. Future children that come of age during a full population wait in the away queue and join the world when space opens.

## Level Master: choose how many levels to raise

A player asked for more control over leveling -- a single trip to the Level Master could jump several levels at once when banked XP allowed, which isn't always what the player wants. `AttemptLevelRaise` looped through every available level unconditionally. Now, when more than one level is available, the Master prompts to raise a single level, raise all of them, or cancel. This pairs with the existing auto-level-up preference (on by default, toggleable in Preferences): a player who wants fine control turns auto-level-up off so XP banks, then chooses level-by-level at the Master. Two new localized prompt lines in all five languages.

## Localized the new-player hint system

A player reported that the contextual new-player tips (the first-login "go gear up at the shops" tip, and the others) render in English regardless of session language. `HintSystem` stored every hint title and message as a hardcoded English literal, and `GetClassCombatTip` returned hardcoded English per class. Refactored so each hint's title and message resolve at display time from loc keys derived from the hint ID (`hint.<id>.title` / `hint.<id>.msg`), the class-specific combat tip returns a localized key, and the "TIP" label is localized too. 45 new keys covering every hint title and message, the per-class first-combat tips, and the box label, translated into all five languages.

## Equipment slot names in shops and inventory

A player reported the armor shop showing untranslated slot names (e.g. "head", "body") even though the surrounding text (the empty-slot label) was already localized. The shared `EquipmentSlot.GetDisplayName()` extension returned hardcoded English. Routed it through `GameConfig.GetLocalizedSlotName` (the `equip.slot.{Slot}` keys added in v0.61.5, which cover every enum value), which localizes every caller at once -- armor shop, weapon shop, inventory, and the loot compare window -- in all five languages. Verified no caller compares the display name against a literal, so this is display-only and safe.

## Recreated character inheriting the old life's children

A player deleted their character, recreated one with the same name, and the next day received the daily child-gold bonus from a child that belonged to the deleted character. Children are matched to parents by display name (and ID), and a recreated character keeps the same name, so the new character name-matched the previous life's kid and collected its daily income. The NG+ flow already disowned previous-cycle children (v0.57.17), but the character-delete paths (delete-and-recreate from the slot menu, and alt-character delete) never did.

Added `DisownDeletedCharacterChildren`, called from both delete paths: in online mode the deleted character's children are disowned (matching parent slot cleared; both-parents-empty minors moved to the orphanage via the existing pipeline), mirroring the NG+ disown. The world-state persist is gated on actually having disowned something, so a stale or not-yet-loaded family roster can never blank out the shared children. Single-player is unaffected -- `CreateNewGame` resets `FamilySystem` entirely there. Note: children already inherited on existing saves before this fix are not retroactively cleaned by the code change.

## Inn / Magic Shop ambient messages and news feed chrome

A player reported the Inn's ambient flavor lines (the crackling-hearth message and others) rendering in English, plus the world news feed. Most locations already route ambient messages through `Loc.Get`, but `InnLocation` (7 lines) and `MagicShopLocation` (6 lines) still had hardcoded English; both now use loc keys, translated into all five languages. The news feed's chrome -- the "TOWN NEWS" header and the empty-feed line -- was also hardcoded and is now localized.

The individual news *entries* themselves are not yet localized: they are rendered to a final English string at creation time and stored that way in shared world state, written by whichever session generated the event, across dozens of creation sites. That is the same architecture the quest-text localization (v0.61.2) had to unwind with a key + args data model, and doing it for news is a dedicated effort rather than a hotfix item. Flagged for a future localization pass; the feed chrome is localized now so the surface at least reads correctly in the player's language.

## Location name in NPC story notifications

A player reported that the memorable-NPC story notifications (the "so-and-so whispers your name from the {location}" hints on Main Street) showed the location in English even in a localized session. `TownNPCStorySystem.GetNextNotification` built the location label with a switch that only localized two of the six possible locations (WeaponShop and MainStreet), falling through to the raw English string ("Church", "Healer", "Temple", "Inn") for the rest. Replaced the partial switch with a `location.name.{string}` lookup -- every memorable NPC's location string already has a matching key in all languages -- with a fallback to the raw string if a key is ever missing. All six story-NPC locations now localize.

## Wilderness region names and directions

A player reported that the wilderness area's region names and compass directions rendered in English in a localized session. The four regions (Whispering Forest / Iron Mountains / Blackmire Swamp / Stormbreak Coast), their N/E/S/W directions, their entry descriptions, and the eight named discoveries (Hidden Cave, Sacred Grove, Abandoned Mine, Eagle's Eyrie, Sunken Temple, Witch's Hollow, Smuggler's Cove, Stormwatch Lighthouse) were all read straight from the hardcoded English fields on the `WildernessData` definitions. Added five localized accessors on `WildernessData` (`GetRegionName` / `GetRegionDirection` / `GetRegionDescription` / `GetDiscoveryName` / `GetDiscoveryDescription`) that resolve `wilderness.region.{id}.name` / `.desc`, `wilderness.direction.{N|E|S|W}`, and `wilderness.discovery.{id}.name` / `.desc` with an English fallback, and routed every player-facing display site in `WildernessLocation` through them: the region menu (both visual and BBS), the too-dangerous warning, the region entry header and description, the monster-emerges line, the discovery-found message, and the discoveries list. 28 new keys (4 directions, 4 region names, 4 region descriptions, 8 discovery names, 8 discovery descriptions) in all five languages.

Wilderness monster names and the deeper flavor prose (foraging, ruins, traveler dialogue) are out of scope here: monster names are a game-wide untranslated concern and the prose is a separate flavor layer, both better handled in a dedicated pass than bolted onto this hotfix.

## World boss menu showed a malformed `[Q] [B]ack`

A player reported the world boss screen's menu rendering as `[A] Attack Boss    [Q] [B]ack` -- a double-hotkey that read as broken next to the clean `[A] Attack Boss` label. The menu put `[Q]` in front of `Loc.Get("ui.back")`, and `ui.back` is the BBS-convention string `[B]ack`, so the two stacked into `[Q] [B]ack`. (A v0.60.8 pass made pressing `B` work as an alias but left the cosmetic problem in place.) Swapped to the plain-word `engine.back` label (already translated in all five languages), so it now renders `[A] Attack Boss    [Q] Back`. `Q` is the hotkey; `B` is still accepted as a defensive alias for players used to the old label. No new keys, no save change.

## World boss combat overhaul: abilities, buffs, and multi-potion

A player reported world boss combat felt flat and broken: only one potion could be used per turn, and class abilities "didn't seem to work." Both were real. The world boss runs its own combat loop (separate from the main dungeon combat engine, because the boss HP is a shared pool written to the database so many players can fight the same boss concurrently), and that loop had quietly dropped most of the real mechanics.

**Abilities did nothing.** The world boss loop called the real ability system but then read back only the damage and healing from the result, throwing away the attack/defense/duration buffs. So Battle Cry, Focus, Shield Wall, Iron Will and the like printed their flavor line and had zero effect. Now the loop applies a used ability's buffs to the player the same way the main combat engine does: attack bonuses, defense bonuses, Shield Wall Formation's flat damage reduction, and Iron Will's status immunity all land and tick down over their proper duration, and the damage and boss-attack math actually read them. Spell buffs (protection / attack) were converted to the same mechanism (they previously set status icons the loop never consulted).

**Only one potion per turn.** The item action drank exactly one potion and handed the boss a free round for every sip. It now prompts for a quantity (a number, or "F" to drink until full) and drinks them all in a single action.

**Flat damage.** Basic, power, and precise strikes used a bespoke damage formula with a hand-rolled crit curve divorced from the rest of the game. They now use the real critical-hit chance (scaling with DEX and equipment crit bonus, matching dungeon combat), so gear and DEX investment pay off and hits actually vary. Buffs from working abilities now feed into this damage.

We deliberately kept the shared-HP multiplayer pool architecture intact (world bosses are meant to need several players; the per-round damage is still written atomically to the database), so this is an overhaul of the existing loop rather than a risky rewrite onto the dungeon combat engine. The boss's per-role aura and attack tuning is unchanged. New `world_boss.*` keys for the buff and multi-potion messages in all five languages.

## Druid's Shrine names and pilgrimage prose localized

A player reported the five Druid's Shrines (the v0.61.0 wilderness pilgrimage feature) showed their names, effects, and flavor messages in English even in a localized session, including the lines like driving your blade into Maelketh's altar. Every shrine's name, patron-god title, first-sighting flavor, attunement prose, and one-line passive summary was read straight from the hardcoded English fields on the `DruidShrineData` definitions. Added five localized accessors on the shrine type (`LocName` / `LocGodPatron` / `LocFlavor` / `LocAttunement` / `LocPassiveSummary`) that resolve `shrine.{id}.name|god|flavor|attune|passive` with English fallback, and routed every display site through them: the wilderness entry's active-attunement reminder (visual + BBS), the pilgrimage menu (active-buff line, the five-shrine list, the confirmation flavor and passive, the attunement prose, the favor-milestone line), and the `/health` Active Buffs readout. 25 new `shrine.*` keys (5 shrines x name/god/flavor/attune/passive) in all five languages.

## World boss spawn announcement showed in English for everyone

A player reported the world boss appearance broadcast rendered in English regardless of session language. The templates (`world_boss.spawn_broadcast` + `world_boss.type_boss_to_join`) were already translated, but the broadcast is fired from the world-simulation tick, which has no session attached, so `Loc.Get` fell back to English and the single pre-rendered string was sent to every player. (The per-location-entry "X is rampaging" reminder already localized correctly because it renders when the player enters a location, inside their session.)

Fix: a new per-recipient broadcast path. `Loc.GetIn(lang, key, args)` renders a string in an explicitly chosen language independent of the current session, and `MudServer.BroadcastLocalized(lang => ...)` renders the message once per connected session in that session's own language (same delivery filters as the normal broadcast). The world boss spawn announcement now uses it, so each online player sees the announcement in their language. The same once-rendered-in-the-wrong-language problem affected the boss-defeat and phase-change broadcasts (those rendered in the triggering player's language and went out to everyone in it) -- both converted to the per-recipient path as well. The boss name and title embedded in the announcement are still English (boss names are a game-wide untranslated layer like monster names, flagged for a separate pass); the surrounding announcement now reads in the player's language. The world news *entry* for the spawn remains English (stored news content is the separate data-model refactor already noted). No new loc keys.

## Dormitory return option mislabeled "Anchor Road"

A player reported the Dormitory's `[R]` return option was labeled "Return to Anchor Road" but actually took them to Main Street (as expected). The dormitory is entered from Main Street and the navigation correctly went there; only the label was wrong, and it was wrong in every language (the `dormitory.return_anchor` loc value said "Anchor Road" across all five). Fixed the label to read "Return to Main Street" (each language using its own Main Street name and `[R]`-prefix style), and renamed the now-misleading key `dormitory.return_anchor` to `dormitory.return_main`. Navigation unchanged.

## Buckler in the off-hand produced dual-wield off-hand strikes

A player reported equipping a buckler but still making off-hand strikes (dual-wield behavior). Every off-hand-strike path gates on `Character.IsDualWielding`, which is suppressed by `HasShieldEquipped` -- but `HasShieldEquipped` only recognized a shield by its `WeaponType` (Shield / Buckler / TowerShield). A dynamic buckler whose `WeaponType` had been mangled into a one-handed weapon (e.g. routed through the loot weapon path, which has no shield keyword and defaults to Sword) read as a normal one-handed off-hand weapon, so `IsDualWielding` returned true and the off-hand swing fired with the shield. Fix: `HasShieldEquipped` now uses the same defense-in-depth detection the v0.60.0 teammate-pickup fix (issue #86) already used elsewhere -- an off-hand item counts as a shield if its `WeaponType` is a shield type, OR it carries shield stats (`ShieldBonus` / `BlockChance`), OR its name looks like a shield (`LooksLikeShieldByName`, which matches "Buckler", "Shield", "Aegis", etc.). Any functionally-shield off-hand item now blocks dual-wielding regardless of how its `WeaponType` got set. The login save-migration already heals such items' `WeaponType` on relog; this closes the mid-session gap at the single chokepoint that governs all off-hand strikes. No new loc keys.

## Dungeon boss-kill broadcast shown in English

A player reported that when another player defeats a dungeon boss, the room broadcast ("X has defeated the mighty Y!") shows in English regardless of session language. The two boss-kill broadcast sites (single- and multi-monster victory) passed a hardcoded English string to `RoomRegistry.BroadcastAction`, which delivers one pre-rendered string to every player in the room. Fix: a per-recipient room broadcast (`RoomRegistry.BroadcastToRoomLocalized` + the static `BroadcastActionLocalized(lang => ...)` convenience wrapper) renders the announcement once per player in that player's language, using the new `combat.boss_defeated_broadcast` template and `Loc.GetIn`. Each player now reads the kill announcement in their own language. (As with other announcements, the boss name itself stays English -- monster names are a game-wide untranslated layer.) New `combat.boss_defeated_broadcast` key in all 5 languages.

## Dungeon puzzles fully localized

A player reported dungeon puzzles render in English. Confirmed and large: `PuzzleSystem` generated every puzzle's description and hint text as hardcoded English, displayed via the dungeon's puzzle handler. The surface was ~280 displayed strings across all nine generated puzzle types -- lever sequence, symbol alignment, pressure plates, number grid, memory, light/darkness, alchemy combination, elemental, and mirror reflection -- and most of it is procedurally-assembled riddle and clue logic: 24 number-riddles (cryptic clues that point at a digit), ~150 symbol riddles (one per dungeon-theme symbol), torch lit/unlit verses, mirror-angle descriptions, pressure-plate wear patterns, alchemy ingredient lore, and the multi-line elemental hints. Unlike narrative prose, these are solvability-critical: a non-English player literally cannot solve a lever puzzle whose number-riddles are in English, so they could not ship as English-only keys.

All ~280 strings were extracted to `puzzle.*` loc keys and the generators rewired to resolve them at display time (the puzzles generate inside the player's own session, so they render in the session language). All four non-English languages (hu/es/fr/it) were fully translated, with the riddles adapted per-language rather than translated word-for-word so each clue still points to the same answer (e.g. the "swan's neck curves like this numeral" clue for 2 keeps the digit-shape idea; "fingers on one hand" for 5 and "days complete a week" for 7 carry across directly). What deliberately stayed canonical: the puzzle symbol words themselves (skull, water, sun, etc. -- they double as the answer the player types, so localizing them would change the solve logic, same reasoning as monster names) and the elemental-puzzle solution tokens the player types (LEFT/CENTER/RIGHT, TORCH/WALL/FLOOR, the numbers) -- only the surrounding prose around those tokens was translated. The puzzle Title strings stayed English too: they are internal solved-tracking keys, never displayed, and localizing them would break cross-language dedup.

## Gauntlet warmup-wave entry text localized

A player reported the Gauntlet's warmup-wave entry flavor ("a dire beast is released," "a foreign sellsword, no name announced," etc.) shows in English. The six warmup-wave lines lived as a hardcoded English array in `GauntletChampionData` and were displayed directly. Moved them to `gauntlet.warmup.0..5` loc keys (the English array stays as the source/fallback) resolved at display time in the session language, translated into all five languages. Also localized the hardcoded "(Level N)" suffix shown next to the opponent on both the warmup and champion-entrance lines (new `gauntlet.level_suffix` key). The champion proper names/titles and the deeper per-champion entrance lore stay English for now (champion names are part of the monster-name layer; the multi-line champion lore is a larger separate surface, flagged for a future pass). 7 new `gauntlet.*` keys in all 5 languages.

## Gauntlet champion entrance theater localized

Follow-on from the warmup-flavor fix and a proactive sweep for the same bug class: the seven Gauntlet champions' entrance theater was hardcoded English in `GauntletChampionData`. Localized every displayed piece -- each champion's multi-line herald entrance announcement, the italic god-lore reveal, the crowd reaction, and the dropped-item flavor description -- plus the 12-line between-fights crowd ambiance pool and the "Patron:" label. Added localized accessors on the champion type (`LocEntrance` / `LocLore` / `LocCrowd` / `LocDropFlavor`) and a `GetLocalizedCrowdAmbiance` helper, resolving `gauntlet.champ.{id}.*` / `gauntlet.crowd_ambiance.{i}` keys with the English text as source/fallback. 65 new keys translated into all four non-English languages, preserving the multi-line herald formatting (leading-space continuation lines and the quoted spoken lines). The champion proper names, titles, and patron-god names stay English (proper-noun layer). With the warmup flavor already done, the whole Gauntlet entry experience now reads in the player's language.

This came out of a proactive sweep of the codebase for the recurring "hardcoded English flavor in a data file" bug class. The sweep found a larger backlog (boss dialogue, riddle text, beast/wilderness flavor, etc.) which has been recorded as a tracked localization-debt list to clear in dedicated passes rather than one bug report at a time.

## What we deliberately did NOT change

The Discord threads raised that the difficulty felt too hard, but the telemetry said otherwise in aggregate, so this release does not touch the global monster scaling from v0.60.0. The specific pain was a death-spiral (lose your tank, gift it XP, watch it die under-geared, end up squishy and stuck) made worse by the re-entry trap above. The dungeon-floor-memory fix removes the trap; the Cyclebreaker buff addresses the class that was most exposed in group content. Larger structural items from the same telemetry pass (late-game monster offense falling off, an explicit catch-up mechanic for under-geared death-spirals) remain on the list for a future balance release rather than a hotfix.

## Files changed

- `Scripts/Core/GameConfig.cs` -- new `CyclebreakerPassiveEvadePercent = 18` constant. Version bumped 0.61.5 -> 0.61.6.
- `Scripts/Core/Character.cs` -- new `LastDungeonFloor` field.
- `Scripts/Locations/DungeonLocation.cs` -- dungeon entry resumes `LastDungeonFloor`; field updated at all 7 floor-change sites.
- `Scripts/Systems/CombatEngine.cs` -- Cyclebreaker passive per-attack evade in the monster-attacks-player path.
- `Scripts/Systems/SaveDataStructures.cs` -- `PlayerData.LastDungeonFloor` field.
- `Scripts/Systems/SaveSystem.cs` -- serialize `LastDungeonFloor`.
- `Scripts/Core/GameEngine.cs` -- restore `LastDungeonFloor` on load.
- `Scripts/Locations/AnchorRoadLocation.cs` -- `SpawnChampionMonster` generates non-boss base + sets `IsBoss = true` after applying champion multipliers (fixes HP double-multiply).
- `Scripts/Locations/LevelMasterLocation.cs` -- removed the broken `[S] Status` option from all three menu surfaces (visual, screen reader, Electron).
- `Scripts/Systems/FamilySystem.cs` -- coming-of-age at population cap parks the child in `ChildLocationAway` instead of freezing; `ProcessDailyAging` retry pass drains the away queue and unfreezes stuck adults.
- `Scripts/Core/GameConfig.cs` -- new `ChildLocationAway` constant.
- `Scripts/Locations/HomeLocation.cs` -- family records label away children.
- `Localization/*.json` -- new `home.family_child_away` key in all 5 languages.
- `Scripts/Locations/LevelMasterLocation.cs` -- `AttemptLevelRaise` offers single-level / all / cancel when multiple levels are available; new `level_master.multi_level_available` / `multi_level_options` keys in all 5 languages.
- `Scripts/Systems/HintSystem.cs` -- hint titles/messages and per-class combat tips now resolve from loc keys instead of hardcoded English; 45 new `hint.*` keys in all 5 languages.
- `Scripts/Core/EquipmentEnums.cs` -- `EquipmentSlot.GetDisplayName()` routes through `GameConfig.GetLocalizedSlotName` so slot names localize everywhere (shops, inventory, compare window).
- `Scripts/Core/GameEngine.cs` -- new `DisownDeletedCharacterChildren` helper called from the main-delete and alt-delete slot-menu paths so a recreated same-named character no longer inherits the deleted character's children (online only).
- `Scripts/Locations/InnLocation.cs` / `Scripts/Locations/MagicShopLocation.cs` -- ambient messages routed through loc keys (13 new keys in 5 languages).
- `Scripts/Systems/OnlineChatSystem.cs` -- news feed header / empty-feed line localized (`news.town_news_header`, `news.none_recent`).
- `Scripts/Systems/TownNPCStorySystem.cs` -- story-notification location label now uses `location.name.{string}` lookup instead of a 2-of-6 switch, so all six story-NPC locations localize.
- `Localization/en.json` / `hu.json` / `es.json` / `fr.json` / `it.json` -- new `combat.cyclebreaker_evade` key.
- `Scripts/Data/WildernessData.cs` -- five localized accessors (`GetRegionName` / `GetRegionDirection` / `GetRegionDescription` / `GetDiscoveryName` / `GetDiscoveryDescription`) reading `wilderness.region.*` / `wilderness.direction.*` / `wilderness.discovery.*` with English fallback.
- `Scripts/Locations/WildernessLocation.cs` -- all region / direction / discovery display sites routed through the new accessors.
- `Localization/*.json` -- 28 new `wilderness.*` keys (4 directions, 4 region names + 4 descriptions, 8 discovery names + 8 descriptions) in all 5 languages.
- `Scripts/Systems/WorldBossSystem.cs` -- world boss menu Back entry uses plain-word `engine.back` instead of the BBS-convention `ui.back` (`[B]ack`), so it renders `[Q] Back` instead of `[Q] [B]ack`. No new keys.
- `Scripts/Systems/WorldBossSystem.cs` -- world boss combat overhaul: used-ability buffs (attack/defense/Shield Wall Formation damage reduction/Iron Will immunity) now applied to the player and read by the damage and boss-attack math; spell buffs routed through the same `Temp*` fields; multi-potion item action (quantity or drink-to-full); basic/power/precise damage use the real `StatEffectsSystem.GetCriticalHitChance`; player buffs reset at combat start and tick down per round. Removed the dead `TempAttackBonus`/`TempDefenseBonus` fields from `WorldBossCombatState`.
- `Localization/*.json` -- 8 new `world_boss.*` keys (buff_attack_dur, buff_defense_dur, buff_damage_reduction, buff_status_immunity, potion_qty_heal, potion_qty_mana, drank_heal_multi, drank_mana_multi) in all 5 languages.
- `Scripts/Data/DruidShrineData.cs` -- five localized accessors (`LocName` / `LocGodPatron` / `LocFlavor` / `LocAttunement` / `LocPassiveSummary`) reading `shrine.{id}.*` with English fallback.
- `Scripts/Locations/WildernessLocation.cs` / `Scripts/Locations/BaseLocation.cs` -- all shrine name / patron / flavor / attunement / passive display sites routed through the new accessors.
- `Localization/*.json` -- 25 new `shrine.*` keys (5 shrines x name/god/flavor/attune/passive) in all 5 languages.
- `Scripts/Systems/LocalizationSystem.cs` -- new `Loc.GetIn(lang, key[, args])` to render a string in an explicit language for per-recipient broadcasts.
- `Scripts/Server/MudServer.cs` -- new `BroadcastLocalized(lang => ...)` that renders a broadcast once per session in each recipient's language.
- `Scripts/Systems/WorldBossSystem.cs` -- world boss spawn / defeat / phase-change broadcasts use `BroadcastLocalized` so each player reads them in their own language (the spawn one previously rendered once in English from the world-sim tick). No new keys (templates were already translated).
- `Scripts/Locations/DormitoryLocation.cs` -- return option uses renamed `dormitory.return_main` key; `Localization/*.json` renamed `dormitory.return_anchor` -> `dormitory.return_main` and corrected the label from "Anchor Road" to "Main Street" in all 5 languages (navigation already went to Main Street).
- `Scripts/Core/Character.cs` -- `HasShieldEquipped` hardened to recognize a shield by WeaponType OR shield stats (ShieldBonus/BlockChance) OR shield-like name, so a buckler/shield with a mangled WeaponType no longer enables dual-wield off-hand strikes.
- `Scripts/Server/RoomRegistry.cs` -- new `BroadcastToRoomLocalized` + `BroadcastActionLocalized(lang => ...)` for per-recipient-language room broadcasts.
- `Scripts/Systems/CombatEngine.cs` -- single- and multi-monster dungeon boss-kill room broadcasts use `BroadcastActionLocalized` + new `combat.boss_defeated_broadcast` key so each player reads them in their own language.
- `Localization/*.json` -- new `combat.boss_defeated_broadcast` key in all 5 languages.
- `Scripts/Systems/PuzzleSystem.cs` -- all puzzle descriptions, hint headers, hint-line templates, number-riddles, symbol riddles, torch verses, mirror-angle descriptions, pressure-plate wear patterns, alchemy ingredient lore, symbol-clue theme intros, and elemental-puzzle desc/hints rewired from hardcoded English to `puzzle.*` loc keys (loc-pool lookups for the random-variant pools). Symbol words, elemental solution tokens, Roman position numerals, and puzzle Titles stay canonical.
- `Localization/*.json` -- ~280 new `puzzle.*` keys in all 5 languages (hu/es/fr/it riddles adapted per-language to stay solvable; format args / leading-space templates / solution tokens / newlines preserved; French re-run to restore diacritics).
- `Scripts/Locations/AnchorRoadLocation.cs` -- Gauntlet warmup-wave flavor now resolved from `gauntlet.warmup.{i}` loc keys; opponent "(Level N)" suffix on warmup + champion-entrance lines uses new `gauntlet.level_suffix` key.
- `Localization/*.json` -- 7 new `gauntlet.*` keys (6 warmup lines + level suffix) in all 5 languages.
- `Scripts/Data/GauntletChampionData.cs` -- localized accessors (`LocEntrance` / `LocLore` / `LocCrowd` / `LocDropFlavor` on the champion type; `GetLocalizedCrowdAmbiance`) reading `gauntlet.champ.{id}.*` / `gauntlet.crowd_ambiance.{i}` with English fallback.
- `Scripts/Locations/AnchorRoadLocation.cs` -- champion entrance announcement / lore / crowd / drop-flavor / crowd-ambiance / "Patron:" label routed through the accessors.
- `Localization/*.json` -- 65 new `gauntlet.champ.*` / `gauntlet.crowd_ambiance.*` / `gauntlet.patron_label` keys in all 5 languages (champion proper names/titles/patron-god names stay English).

675/675 tests pass. No save format change beyond the additive `LastDungeonFloor` field (pre-v0.61.6 saves default it to 0).
