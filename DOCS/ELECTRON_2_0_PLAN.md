# Electron client plan for 2.0

This is a design for the user's approval. No code starts before it is approved. It covers what the Electron client is today, what it lacks against the current game, three ways to build it for 2.0, a recommended one, an ordered list of slices, and the decisions only the user can make.

All facts were checked against `release-1.2.4` at d30af2b (game version 1.2.3, `Scripts/Core/GameConfig.cs:13`). Paths are relative to the repository root. The client was read, not run, for this plan.

## 1. What exists today

### Version and history

| Fact | Evidence |
|---|---|
| Client package version is 0.57.21 | `electron-client/package.json:3` |
| Last commit that changed client behaviour: bf3bb52, 2026-04-27, first contained in tag v0.60.0 | `git log -- electron-client` |
| Only later client commit: 0c42338, the sprite generator reads the PixelLab key from the environment | `git log -- electron-client` |
| CI job `build-electron` removed in 4f816d9, 2026-05-01, first contained in v0.60.2 | `git show 4f816d9` |
| ARCHITECTURE calls it Beta, with "Phase 9.5 polish landed in v0.60.0" (a game version; the client package stayed 0.57.21) | `DOCS/ARCHITECTURE.md:1460-1487` |
| Electron 32, xterm.js 5.5, electron-store 8 | `electron-client/package.json:17-26` |
| No test script and no tests in the client | `electron-client/package.json:6-14` |

### How it talks to the game

Local play: the main process spawns the game binary with `--local --electron` and pipes stdio (`electron-client/main.js:269`, `main.js:291-305`, input at `main.js:321-323`). The binary is looked up under `publish/<rid>` in development or `resources/game/` when packaged (`main.js:24-60`).

Online play: the renderer opens a WebSocket to `wss://usurper-reborn.net/ws` (`main.js:127`, `renderer.js:108-131`). The proxy forwards it to the MUD server and sends `X-Client:Electron` (`web/ssh-proxy.js:4176-4182`).

The protocol is one-way. The game writes `ESC ] 1337 ; usurper:{json} BEL` to `Console` (`Scripts/UI/ElectronBridge.cs:21-36`), with the envelope `{ e: type, d: data }` (`ElectronBridge.cs:27`). The client's ANSI parser pulls these out of the stream (`electron-client/src/ansi-parser.js:91`, `ansi-parser.js:169-170`) and routes them to `GameUI.handleGameEvent` (`renderer.js:255-256`, `src/game-ui.js:263-535`). Input goes back as plain keystrokes (`preload.js:8`).

The switch is the static `GameConfig.ElectronMode` (`GameConfig.cs:30`), set only by `--electron` (`Scripts/BBS/DoorMode.cs:288-291`). On the server, `X-Client:Electron` is read (`Scripts/Server/MudServer.cs:568-571`) and used only as a label in the who list (`Scripts/Systems/OnlineChatSystem.cs:463`). So an online Electron session gets no structured events at all; it runs on the text stream and the client's pattern matcher (`src/pattern-matcher.js:6-25`, `game-ui.js:2089-2170`).

A second structured channel already exists for MUD clients: `GmcpBridge`, which writes per session (`Scripts/Server/GmcpBridge.cs:382-395`) and has 30 call sites.

### What it renders

| Surface | Client code | Fed by |
|---|---|---|
| Town scene, location title, dock of menu buttons, NPC tags | `game-ui.js:263-293`, `game-ui.js:2034-2088`, `game-ui.js:2240-2266` | `location`, `stats`, `menu`, `npcs` from 24 locations |
| Dungeon room panel and map | `game-ui.js:2452`, `game-ui.js:2564` | `dungeon_room` (`Scripts/Locations/DungeonLocation.cs:3417`), `dungeon_map` (`DungeonLocation.cs:13453`) |
| Inventory paperdoll, slot picker | `game-ui.js:2705-2907` | `inventory*` (`Scripts/Systems/InventorySystem.cs:137`, `:225`, `:337`) |
| Character status, party cards, potions | `game-ui.js:2908-3085` | `character_status`, `party_status`, `potions_menu` |
| Combat battlefield, ability bar, log | `game-ui.js:3086-3504` | `combat_start`, `combat_status`, `combat_menu`, `combat_end` (`Scripts/Systems/CombatEngine.cs:2325`, `:12289`, `:29629`, `:29677`, `:27472`) |
| Pregame: main menu, saves, character creation, opening, recovery | `game-ui.js:1591-2007` | `GameEngine.cs:1820`, `:1865`, `:3456`; `CharacterCreationSystem.cs` (8 sites); `OpeningStorySystem.cs:77-102` |
| Dialogue, quests, level up, death, endings, ascension, boss phase | `game-ui.js:690-1245` | `DialogueSystem.cs`, `VisualNovelDialogueSystem.cs`, `QuestHallLocation.cs`, `LevelMasterLocation.cs:1592`, `CombatEngine.cs:22795`, `EndingsSystem.cs` |
| Chat, group invite, news, spectate, settings, sound | `game-ui.js:1246-1590`, `src/audio.js` | `MudChatSystem.cs:432`, `:715`, `:743`, `:1427`; `GameEngine.cs:2079`, `:4546`; `BaseLocation.cs:3827-3903` |
| Terminal view (xterm), toggled with F10 | `renderer.html:146-150`, `renderer.js:11` | the plain text stream |

The client handles 53 event types (`game-ui.js:267-535`). The game emits three it has no case for: `statue_list`, `statue_detail`, `statue_close` (`Scripts/Systems/FounderStatueSystem.cs:51`, `:125`, `:300`). Thirteen helpers in `ElectronBridge.cs` have no caller: `EmitCombatStart`, `EmitCombatAction`, `EmitNarration`, `EmitPrompt`, `EmitConfirm`, `EmitShopInventory`, `EmitEventEncounter`, `EmitTargetSelection`, `EmitFloorOverview`, `EmitQuestComplete`, `EmitSpectatorState`, `EmitSoundStop`, `EmitVolumeSet` (`ElectronBridge.cs:70-549`).

Art: 17 class sprites, 19 monster sprites, 17 scenes (`electron-client/assets/`). Scenes are picked by English keywords in the location name (`game-ui.js:34-37`, `game-ui.js:2247-2266`, `game-ui.js:2224-2230`).

### Its own language files

The client chrome has an English baseline in `src/i18n.js:17` and loads `lang/<code>.json` with `fetch` (`i18n.js:131-155`). `es`, `fr`, `hu` and `it` each hold all 81 template keys with translated values (checked against `lang/_template.json`), yet `lang/README.md:7-12` still says TODO for all four. `game-ui.js` references 42 of the 81 keys through `tr(...)`, and some strings with a key are still literal, for example `game-ui.js:792` ("No quests available."). The packaged file list does not include `lang/` (`package.json:37-58`), so a packaged build would fall back to English.

### Build and shipping

Local scripts publish the game and run `electron-builder --dir` per platform (`package.json:6-14`, targets at `package.json:59-120`). `test-launch.sh` is a Windows dev launcher. CI no longer builds the client: since 4f816d9, `build-steam` (`.github/workflows/ci-cd.yml:278-306`) and `steam-package` (`ci-cd.yml:470-520`) ship the WezTerm desktop bundle only. Before that, the client went into each Steam depot under `electron/` as an extra launch option.

### What was last working

No record in the repository says the client runs against the current game. The last client change is in v0.60.0, the last CI build before v0.60.2, and no test exercises the protocol on either side. The only test that reads `ElectronBridge.cs` checks that its payload classes match the localization scanner's list (`Tests/Localization/DataTextScannerTests.cs:141-145`). Slice 0 below establishes what works.

## 2. The gap

### Two causes

**One transport, one process.** `ElectronBridge.Emit` writes to `Console` behind a process-wide flag (`ElectronBridge.cs:23`, `:30`). It cannot reach an online session, which is why online Electron play is text only. Any 2.0 that includes online play needs a per-session channel.

**A second, hand-written path.** There are 111 `GameConfig.ElectronMode` references in 40 files. Several branches emit and then return before the text body, so in Electron mode anything added to the text path since is not shown at all:

| Screen | Branch | Effect |
|---|---|---|
| Main Street | `MainStreetLocation.cs:136-138` | text body skipped; Electron menu used instead |
| Dungeon room | `DungeonLocation.cs:2576-2580` | room text skipped |
| Inventory | `InventorySystem.cs:46-50` | text inventory skipped |
| Party management | `DungeonLocation.cs:10951-10973` | shows cards, waits for a key, returns; stances and the shared belt (`DungeonLocation.cs:11002-11057`) are unreachable |

The menus the client shows are key lists written by hand next to each location, and they have drifted from the handlers:

| Location | Emitted keys | What the handler does | Evidence |
|---|---|---|---|
| Main Street | D I W A M B 1 T E > U V 2 H S Q (pre 1.1.13 keys) | resolves keys against the district table: M opens Merchant Row, A opens the Dark Alley, T opens Team Corner, H opens Home and Hearth, S opens the Status district | `MainStreetLocation.cs:574-598`, `:715-717`; `MainStreetDistricts.cs:40-95` |
| Temple | W A C D F O M S R | hall screen takes A U F P H M T R; W C D S O print a "moved" pointer; A opens the Nave, labelled "Altars" | `TempleLocation.cs:3568-3578`, `:3017-3028`, `:3040-3066` |
| Settlement | V C P T O R | T and O are not handled; S (services) is handled but not emitted | `SettlementLocation.cs:1292-1297`, `:299-316` |
| Healer | H F B M N P C D A S R | T (talk therapy) and W (willow draught), the Mental additions, are missing | `HealerLocation.cs:1624-1634`, `:258-264` |
| Combat | A D P E I R T L as literals, plus the quickbar | labels are English literals; the fixed keys are not checked against the combat handler | `CombatEngine.cs:29633-29640`, `:29642` |

These two causes matter more than any single missing feature: a payload added per feature on the current design would drift the same way.

### Features since the client was shelved

Checked by searching `electron-client/src/*.js` and `renderer.*` for each feature and by listing the `ElectronBridge` call sites. "No emit" means the feature's code has no `ElectronBridge` or `ElectronMode` reference.

| Feature | Client handling | Game side | Evidence |
|---|---|---|---|
| Gods, Favor, Faith | none ("favor", "faith" not in client) | no emit; Favor changes print through `FavorUi` text | `Scripts/Systems/FavorUi.cs:14-36`; `GodSystem.cs`, `GodDeedSystem.cs` no emit |
| Boons | none | no emit | `GodBoonSystem.cs`, `DivineBoonRegistry.cs` no emit |
| Temple rooms (1.2.0) | dock from a stale list | see the menu table | `TempleLocation.cs:3005`, `:3568-3578` |
| Mental (grief, addiction, dungeon wear, recovery) | none ("mental" only as a sprite name, `game-ui.js:71`) | `stats` has no Mental field; text only via `MentalUi` | `ElectronBridge.cs:57-66`; `MentalSystem.cs`, `MentalUi.cs` no emit |
| Sage wards and seals | none | `combat_status` carries only poisoned, stunned, burning | `CombatEngine.cs:12276`, `:8817` |
| World boss fight | none | own combat loop reading `GetInput`, no emit | `WorldBossSystem.cs:1732-1848` |
| Main Street districts (1.1.13) | none ("district" not in client) | stale menu | `MainStreetLocation.cs:574-598` |
| Settlements | name in a list only (`game-ui.js:2229`) | stale menu | `SettlementLocation.cs:1292-1297` |
| Party menu, stances, shared belt | party cards only, no actions | unreachable in Electron mode | `DungeonLocation.cs:10951-10973` |
| Haggling (weapon, armor, magic) | none | no emit | `HagglingEngine.cs` no emit |
| Chat history (`/history`) | chat panel has no history | online only, and online gets no events | `Scripts/Server/ChatHistoryStore.cs:13-23` |
| Sanctum, Marketplace, News, Team Corner, Dormitory, Gym, God World, Sys Op | none | these location files have no emit | `Scripts/Locations/` (no `ElectronBridge` in those files) |
| Founder statues | no case for `statue_*` | emits three events | `FounderStatueSystem.cs:51`, `:125`, `:300` |
| Localized location names | scene and location detection use English names | Temple sends a localized name, Main Street a literal | `game-ui.js:2224-2230`; `TempleLocation.cs:3554-3557`; `MainStreetLocation.cs:556` |

### The 203 Electron payload strings

The localization data ratchet counts English literals sent in Electron payloads. The baseline total is 203 (`Tests/Localization/hardcoded-data-baseline.json`, `electron` category). All 203 are untranslated by definition: they are raw English literals that reach the client in every language. The scan run for this plan (`Tests/Localization/loc-scan.sh`) splits them:

| System | Sites | Largest files |
|---|---|---|
| town locations | 112 | Dark Alley 16, Armor Shop 11, Pantheon 11, Love Corner 10, Healer 8, Inn 8, Temple 8, Weapon Shop 8 |
| castle, prison, home | 53 | Castle 21, Home 20, Prison 9, Prison Walk 3 |
| combat | 23 | CombatEngine 23 |
| dungeon | 7 | Settlement 5, Dungeon 2 |
| inventory and character | 4 | |
| other | 4 | Founder statues 4 |

By construct: 163 `new MenuItemData` labels, 26 `ElectronBridge.Emit` arguments, 4 `EmitLocation`, 3 `EmitLootItem`, 2 each of `ShopBrowseItem`, `NewsFeedSection` and `NewsFeedItem`, 1 `EmitCombatEnd`.

Of the 198 plain literals, 110 match an existing English value in `Localization/en.json` exactly (ignoring case), so a key can be reused; 88 need a new key. Five are interpolated strings and need keys with arguments.

## 3. Architecture options

### Option A: keep the event bridge, add payloads per system

Each missing feature gets its own event and client renderer, as Phase 1 to 9.5 did.

- Cost: one payload and one client panel per screen; about 30 location and system screens are missing or stale.
- Risk: the two causes stay. Menus keep drifting because they are a second list; text added later stays invisible behind the short-circuit branches; online still needs a transport change. The current state is the result of this approach after five months.

### Option B: render from a structured screen model shared with the terminal

Every screen describes itself as data (title, rows, menu entries with keys and Loc keys, status fields). The terminal renderer, the screen reader renderer and the Electron serializer all consume it.

- Cost: high. It touches the display code of every location and system, a large share of the 79 column chrome the 1.2.x releases fixed, and the screen reader path.
- Risk: regressions in the terminal and BBS game, which is the game most players use, for a client fewer players use. Long time before anything ships.
- Partial precedent: Main Street already drives every renderer from one table (`MainStreetDistricts.cs:9-12`), and the Temple builds its rooms from item lists (`TempleLocation.cs:3110-3130`).

### Option C: thin client over the text stream, with rich panels

The text stream is the source of truth in every mode. The client shows it styled in a main pane and adds panels fed by a small set of state payloads: vitals and Mental, gods and Favor, party, combat state, location, chat. Clickable menus come from the same entries the terminal prints.

- Cost: medium. Remove the short-circuit branches; add a per-session event sink; a few state payloads; one menu payload built from shared entries.
- Risk: it looks less like a graphical game on screens without a panel; text styling in the main pane depends on the ANSI stream, not on pattern matching, so it needs care with box drawing and the 79 column layout.

### Recommendation: Option C, with the menu piece of Option B

Reasons:

1. It removes both causes. Text is never skipped, so every feature added in 1.x shows up in the client without Electron work; and a per-session sink lets online sessions get the same events as local ones.
2. Menus stop drifting only if they come from the list the handler reads. Main Street (`StreetEntries`, `MainStreetDistricts.cs:40-83`), the Temple (`RoomItems`, `TempleLocation.cs:3110`) and combat (`GetQuickbarActions`, `CombatEngine.cs:29605-29607`) already have such lists; other locations get one as their menu slice lands.
3. Parity becomes the default and graphics an addition, so 2.0 can ship with a fixed set of panels and every screen still works.
4. The per-session sink can carry two encodings: OSC for local stdio and the existing GMCP framing for MUD sessions, so the online client and MUD clients share packages (`GmcpBridge.cs:382-395`).

## 4. Milestone plan

Each slice is one release item or one agent piece. Every slice passes the repo gate (full `dotnet build` and `dotnet test`, the wiki sequence with drift rc 0), has one negative per behaviour (break it, see red, restore), keeps the localization ratchets from going up, and puts player-visible text through Loc keys in en, es, fr, hu and it. The 79 column rule applies to terminal rows a slice adds or changes; client panels are HTML and are checked by client tests instead.

| # | Slice | Definition of done | Tests it needs |
|---|---|---|---|
| 0 | Revive and measure | Client builds with `electron-builder --dir` on Windows, Linux and macOS against the current game; a written list of what works and what breaks, screen by screen | none in the gate; a recorded checklist |
| 1 | Client test harness | `npm test` in `electron-client/` replays recorded OSC streams into `handleGameEvent` and checks the DOM | the harness itself; a negative on one handler |
| 2 | Per-session event sink | `ElectronBridge.Emit` writes through the current session or terminal, not `Console`; local output byte for byte unchanged | unit test that an emit lands on the session stream and not on another session's; negative |
| 3 | Text always renders | The short-circuit branches emit and fall through instead of returning; party management, stances and the shared belt reachable in Electron mode | a scanner test that no `ElectronMode` branch returns before the text body; negative per screen |
| 4 | Shared menus, Main Street and Temple | The menu payload is built from `StreetEntries`/`DistrictsShown` and the Temple room items; labels from Loc keys | test that every emitted key is accepted by that view's `ProcessChoice`; negative with a stale key |
| 5 | Shared menus, remaining locations | Same for the other 22 emitting locations; each gets a menu list if it has none | the same key subset test per location |
| 6 | Payload text to Loc | The 203 sites go through Loc (110 reuse keys, 88 new, 5 with arguments); data baseline `electron` count reaches 0 | `DataTextRatchetTests` with the baseline lowered; `LocalizationIntegrityTests` |
| 7 | Player state panel | One state payload with vitals, Mental band, god, Favor, boons; client panel | payload unit tests; client replay test; negative per field |
| 8 | Combat panel | Combat menu from the quickbar and the combat key table; wards, seals and statuses in combat state; the world boss loop emits the same state | key subset test against the combat handler; payload tests; negative |
| 9 | Online | Online Electron sessions receive events over the MUD path; chat panel with `/history`; who list label kept | integration test on a MUD session; negative |
| 10 | Client chrome text | `lang/` in the packaged files; README status corrected; literals in `game-ui.js` moved to keys | client test that every key used exists in all four lang files; negative |
| 11 | Leftovers | Founder statue events handled or removed; the 13 unused helpers wired or deleted; scene lookup by location id, not English name | client replay tests; payload type test (`DataTextScannerTests.cs:141-145`) kept in step |
| 12 | Ship | CI builds the client per platform again; Steam launch option per the platform decision | CI run on a branch; install check per platform |

Repository rules each slice must keep: a new payload class in `ElectronBridge.cs` must be added to `DataTextScanner.ElectronPayloadTypes` (`Tests/Localization/DataTextScanner.cs:95-101`) or the gate goes red; the per-file `electron` count in `hardcoded-data-baseline.json` may only go down (`Tests/Localization/loc-scan.sh --write-data-baseline`).

Slices 0 and 1 come first because nothing after them can be judged without them. Slices 2 and 3 carry the recommendation; if the user picks another option, slices 3 to 5 change and the rest stay.

## 5. Decisions for the user

1. **Decision: architecture.** Options: A (payloads per system), B (shared screen model), C with shared menus (recommended).
2. **Decision: 2.0 scope.** Options: full parity with the terminal game on every screen; or every screen working through the text pane plus a fixed set of panels (state, party, combat, map, inventory, chat); or a smaller set of panels.
3. **Decision: local, online, or both.** Options: local only (slice 9 drops); online only; both. Online needs slice 2 either way.
4. **Decision: 2.0 platforms.** Options: Windows only; Windows and Linux; Windows, Linux and macOS (Intel and ARM, as the Steam build has today, `ci-cd.yml:287-290`).
5. **Decision: distribution.** Options: a second Steam launch option beside the WezTerm build; replace the WezTerm build; a separate download.
6. **Decision: 1.x and Electron payloads meanwhile.** Options: freeze (no new payloads, gaps go into this plan); keep adding payloads per feature; adopt the proposal in section 6.
7. **Decision: art.** Options: keep the current PixelLab sprites and scenes and fill gaps the same way; commission or generate a new set; no new art in 2.0 beyond what exists.
8. **Decision: client version number.** Options: follow the game version; keep its own number.

## 6. What 1.x can do now (proposal)

This is a proposal for the user to approve, not a rule in force.

1. New 1.x features add no `GameConfig.ElectronMode` branch that returns before the text body, and no new hand-written `EmitMenu` key list.
2. A ratchet test counts `ElectronMode` references (111 in 40 files today); the count may only go down, like the localization ratchets.
3. Electron gaps found in 1.x reviews are added to section 2 of this plan instead of becoming merge requirements.
4. When a 1.x change already touches a location's Electron menu, it fixes that menu's labels to Loc keys, which also lowers the 203 count.
