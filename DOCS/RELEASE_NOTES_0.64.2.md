# Release Notes - v0.64.2 (The Brain)

UX consistency pass driven by player feedback: "nearly every screen has its own conventions on commands... why not make Q the default back-out key?" and "when equipping companions... it kicks me back out to the equip/take dialog instead of staying on the slot selection screen... how about an equip-best-from-backpack option?" Plus the headline QoL feature: the Adventurer's Journal.

## The Adventurer's Journal (`/journal`) -- "what should I do now?"

One key, from anywhere, that answers the question the game never answered. Designed via a game-designer pass that diagnosed the root problem as VISIBILITY, not content: active quests render beautifully (if you know `/q`), the story breadcrumb ("the next god waits...") plays exactly once in a post-boss cinematic, companion quests fire one notification, the hint system is one-shot, and unspent training points / claimable dailies are surfaced only inside the locations that consume them. The game's de facto answer to "what's next?" was "memorize 18 slash commands." Same disease that kept Auto-equip Best Gear undiscovered for 10 versions and required a dedicated alignment-visibility pass.

Type `/journal` (aliases `/jo`, `/next`, `/todo`) from anywhere:

- **NEXT STEP** -- a first-match-wins recommendation ladder (`JournalSystem.GetNextStep`, the only genuinely new logic): fresh Lv1 character -> "enter the Dungeons and win your first fight" (a PERMANENT arrow at the onboarding cliff, unlike the one-shot hints); critically hurt with no potions -> Healer; banked level-up (auto-level off) -> Level Master; quest/contract ready to turn in -> turn-in location; unspent training points; companion personal quest waiting; next seal or Old God within the player's accessible floor band (level +/- 10); first incomplete objective of the oldest quest; default "delve, you left off on floor N."
- **IN PROGRESS** -- active quests with incomplete objectives + progress counts, `[READY]` flags on completed quests/merc contracts, companion personal quests (waiting vs underway).
- **READY TO SPEND / CLAIM** -- unspent training points, banked level-ups, the free Paragon-Renown Temple blessing (pole-gated exactly like the Temple's own gate so the journal never recommends something the player is barred from).
- **THE WORLD** -- seal progress (N/7 + next seal floor), the next unresolved Old God and his floor, the remembered dungeon floor.

Architecture: pure static reads over live state in new `Scripts/Systems/JournalSystem.cs` -- ZERO new save fields (same derived-live pattern as Dread/Renown/Dynasty standing), no LLM anywhere, works identically in single-player, online, and BBS. Every rung and section builder is independently try/catch-guarded so a broken subsystem degrades to a missing line, never a broken journal. Three display modes: visual (boxed), BBS-compact, and a dedicated screen-reader branch (linear plain-text readout -- arguably an accessibility win in itself: one screen instead of interrogating five menus). Listed in `/help` in both visual and SR variants.

30 new `journal.*` loc keys in all 5 languages (Hungarian floor numbers use the suffix-safe `a(z) {0}. szinten` pattern; god names stay English per the proper-noun layer). 15 new unit tests on the ladder (rung ordering, fall-through guards, bare-Character never-throw). Deferred to slice 2: the Main Street one-line ticker (`> Next: Aurelion waits on floor 85. (/journal for more)`) and a bare-hotkey collision audit. Slice 3: online-only rows (unread mail, pending trades) + daily-cap rows (tribute/alms remaining).

## Companion equip flow overhaul (player request)

**The slot picker now loops.** All four slot-based equip flows (Team Corner `[G] Equip`, Home partner equip, Inn companion equip, Dungeon `[Y] Party` member equip) previously kicked the player back to the parent equip/unequip/take menu after every single equip -- outfitting a naked recruit meant re-entering the flow once per slot. The flow now returns to the SLOT PICKER after each equip (and after every dead-end: no items for slot, invalid pick, unidentified item, can't-equip refusal), so a full kit goes on in one continuous pass. Cancel at the slot picker (Q / Enter / invalid) exits the whole flow.

**Auto-equip Best Gear is now available everywhere.** The Inn's companion-manager has had an "Auto-equip Best Gear" option since v0.53.5 -- it scans the player's backpack per slot, scores candidates with the class-aware scorer (shields for tanks, daggers for assassins, staves for casters, instruments for bards), and equips every upgrade in one confirm. But it was ONLY reachable at the Inn, and only for companions. The implementation (`CompanionEquipBestGear` + `ScoreEquipment` + `ApplyClassWeaponPreference`) is promoted to `BaseLocation.RunEquipBestGear` and surfaced as `[B]` in all four equipment-management menus: Team Corner (works on team NPCs now, not just companions), Home (spouse/partner), Inn (unchanged), and the Dungeon party menu. Companion-wrapper sync is gated on `IsCompanion`; displaced items return to the player's backpack; cursed and unidentified items are never auto-equipped; online mode persists via `SaveAllSharedState` as before. Reuses the existing `inn.equip_best*` loc keys (already translated in all 5 languages), so zero new translation surface.

## Navigation key standardization, phase 1 (player request)

A survey of every interactive menu found Q already works as "back out" in most town locations (Home, Bank, Dark Alley, Level Master, Wilderness, Settlement, Church, Marketplace, Magic Shop top level) -- but a handful of screens collided or used different keys. This phase fixes the dangerous collision and the most-used inconsistent sub-menus:

- **Team Corner: `[Q] Quit Team` was a destructive landmine -- moved to `[L]`.** In most of the game Q backs out one level; at Team Corner it opened the LEAVE-YOUR-TEAM confirmation. A player reflexively pressing Q-then-Y to exit the menu could destroy their team membership (exactly the "costly errors" class the feedback described). Quit Team now lives on `[L]` across all three menu variants (visual, BBS, screen reader); `Q` at Team Corner now backs out to Main Street like everywhere else. Old muscle memory pressing Q fails SAFE (you leave the room, not the team).
- **Shop category views accept Q.** Weapon Shop, Armor Shop, and Magic Shop accessory categories used `[B]`/`[X]` to back out one level to the category list. Q is now accepted as an alias in all three (B and X still work).
- **Training menus accept Q.** All four Level Master training prompts used `[X]` to exit; Q is now an alias at each.

**"look to redraw" prompt hint removed (player request).** The MUD prompt read `[100hp 50mp] Inn | look to redraw >` on every single input line, and four locations' invalid-choice messages appended "Type 'look' to redraw menu." Now that screens auto-redraw (AutoLook), the hint was permanent noise. The prompt is now just `[100hp 50mp] Inn >`; the four `*.invalid_choice` values (Healer / Anchor Road / Temple / Inn) are trimmed to plain "Invalid choice." in all 5 languages. The `look` command itself still works for manual redraws and is now documented in `/help` (MUD mode only, new `base.help_look` key x5 languages) so it stays discoverable for players who keep auto-redraw off.

**Documented exceptions (unchanged, by design):** Main Street `Q = Quit game` (top level -- there is no "back" from Main Street; quitting IS the back action); Castle `Q = Royal Quests` and Inn companion menu `Q = Personal Quest` (established feature mnemonics; rebinding would break existing muscle memory for an active feature -- candidates for a future phase with proper labels); Prison `Q = quit` (context-specific). The global keys are unchanged and already consistent: `%` Status, `*` Inventory, `!` Bug Report, `H` Help, `R` Return to Main Street.

## Files Changed

- `Scripts/Systems/JournalSystem.cs` -- NEW: next-step recommendation ladder + In Progress / Claim / World section builders (pure static, singleton-guarded)
- `Tests/JournalSystemTests.cs` -- NEW: 15 ladder + section-builder tests
- `Scripts/Core/GameConfig.cs` -- Version 0.64.2
- `Scripts/Locations/BaseLocation.cs` -- `/journal` command (aliases `/jo` `/next` `/todo`) + `ShowJournal()` renderer (visual / BBS / SR) + `/help` rows; `RunEquipBestGear` / `ScoreEquipment` / `ApplyClassWeaponPreference` promoted from InnLocation (companion sync gated on IsCompanion, log tag EQUIP)
- `Scripts/Locations/InnLocation.cs` -- Three equip-best methods removed (now inherited); call site routed to shared helper; `CompanionEquipItemToCharacter` loops on the slot picker
- `Scripts/Locations/TeamCornerLocation.cs` -- `[B] Auto-equip Best Gear` in the member-equipment menu; `EquipItemToCharacter` loops on the slot picker; Quit Team rebound Q -> L at all three menu variants; Q = back to Main Street
- `Scripts/Locations/HomeLocation.cs` -- `[B] Auto-equip Best Gear` in the partner-equipment menu (visual + SR); `EquipItemToCharacter` loops on the slot picker
- `Scripts/Locations/DungeonLocation.cs` -- `[B] Auto-equip Best Gear` in the party-member equipment menu; `DungeonEquipItemToMember` loops on the slot picker
- `Scripts/Locations/WeaponShopLocation.cs` -- Q backs out of category view (alias for X/B)
- `Scripts/Locations/ArmorShopLocation.cs` -- Q backs out of category view (alias for X/B)
- `Scripts/Locations/MagicShopLocation.cs` -- Q backs out of accessory category view (alias for B/X)
- `Scripts/Systems/TrainingSystem.cs` -- Q accepted as exit alias at all four training prompts
- `Localization/en.json` / `es.json` / `fr.json` / `it.json` / `hu.json` -- 30 new `journal.*` keys per language
