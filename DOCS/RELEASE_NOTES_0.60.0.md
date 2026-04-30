# v0.60.0 -- Beta

The beta release. Everything authored after v0.57.20 is consolidated here, including a major new feature (GMCP for modern MUD clients), a one-time founder commemoration tied to the alpha-era online server, several long-tail bug fixes, an architectural fix for character-deletion data loss, and the full JS-side translation pipeline for the Electron graphical client.

---

## Beta launch and the alpha wipe

The online server database is wiped at beta launch. Eleven alpha-era founders, identified from the live database before the wipe, are commemorated in-world by themed statues at three permanent locations. Two of those eleven (Coosh and Zengazu) accidentally deleted their own characters before the wipe; their statues are visibly cracked, with dramatic-irony inscriptions. See "Founder Statues" below for the full list and placements.

The same wipe revealed a real architectural issue (Zengazu hit it twice, on different characters): prestige class unlocks were tied to the character save file, not the SSH account. Tier 1 of the fix ships in this release; full account/character separation is scheduled for a v0.60.x point release once beta has stabilized.

---

## GMCP support -- modern MUD client integration

Adds GMCP (Generic MUD Communication Protocol) out-of-band event support to the MUD server so Mudlet, MUSHclient, TinTin++, and other modern MUD clients can render player vitals, status, room info, and chat messages in dedicated UI panes (gauges, status bars, split windows, chat capture) instead of dumping everything into the scroll buffer.

Player request from a TinTin++ user pointed out that the split-screen status pane in TT++ has nothing to feed it without GMCP -- the client sees only one stream of ANSI text and can't separate narrative output from status updates. With this release, those split panes work.

### What players get with a GMCP-capable client

- **Char.Vitals** -- live HP / MaxHP / MP / MaxMP / SP delta-pushed every prompt cycle. Mudlet renders this as a graphical health bar; TT++ scripts can wire it to a status pane; MUSHclient has a built-in mapper.
- **Char.Status** -- name, class, level, race, gold, bank, xp, location. Pushed on every location change.
- **Room.Info** -- room number + name on every location change. Future iterations will add the exit list (current shape just emits placeholder).
- **Comm.Channel.Text** -- every `/gossip`, `/shout`, `/tell`, and `/gc` message goes out as a structured `{channel, talker, text}` payload to recipients. Chat capture scripts work without text-pattern triggers.

GMCP is a strict opt-in via telnet negotiation. SSH-relayed sessions, web terminal users, BBS terminals, and the local Electron client never see a single GMCP byte -- they get the same plain-ANSI experience they always have.

### Wire format

Standard GMCP framing per the [official spec](https://www.ironrealms.com/gmcp-doc):

```
IAC (0xFF) SB (0xFA) GMCP (0xC9) "Package.Message" SP "{json}" IAC (0xFF) SE (0xF0)
```

Two implementation details that matter:

1. **0xFF doubling inside SB.** Per RFC 854, any 0xFF byte inside an SB block must be sent as `0xFF 0xFF` so the SE terminator (`0xFF 0xF0`) is unambiguous. UTF-8-encoded JSON never produces a single 0xFF for any valid Unicode codepoint, but `GmcpBridge.EscapeIacBytes` handles it defensively for caller-controlled package name strings.
2. **camelCase JSON.** Conventional GMCP servers (Achaea, all Iron Realms muds) use camelCase property names. `GmcpBridge` configures `JsonNamingPolicy.CamelCase` so off-the-shelf Mudlet scripts and existing Achaea-compatible clients work without remapping.

### Negotiation flow

1. Client connects to port 4000 (or sslh-multiplexed equivalent).
2. After the 500ms AUTH-header timeout (which distinguishes raw MUD clients from SSH-proxied AUTH connections), the server sends `IAC WILL ECHO`, `IAC WILL SGA`, and `IAC WILL GMCP` in a single packet.
3. Inside `ProbeTtypeAsync`, the server reads incoming IAC sequences for ~250ms looking for both the TTYPE response AND any `IAC DO GMCP` / `IAC DONT GMCP` reply.
4. If `IAC DO GMCP` arrives, the session is marked `GmcpEnabled = true` on `SessionContext`. `IAC DONT GMCP` (or no response) leaves it false.
5. From that point on, `GmcpBridge.Emit("Package.Name", payload)` checks `SessionContext.Current.GmcpEnabled` and writes IAC-framed bytes to the session's `OutputStream`. Non-GMCP sessions get no frames.

The negotiation is bundled into the existing TTYPE probe to avoid a second 250ms round-trip -- both responses race in the same window.

### Packages shipped

| Package | Trigger | Payload Shape |
|---|---|---|
| `Char.Vitals` | Top of `BaseLocation.LocationLoop` (every prompt cycle) when any tracked stat changed | `{ hp, maxHp, mp, maxMp, sp }` |
| `Char.Status` | `LocationManager.EnterLocation` (every location change) | `{ name, class, level, race, gold, bank, xp, location }` |
| `Room.Info` | `LocationManager.EnterLocation` (every location change) | `{ num, name, area, exits }` |
| `Comm.Channel.Text` | `MudChatSystem.HandleGossip` / `HandleShout` / `HandleTell` / `HandleGuildChat` | `{ channel, talker, text }` |

Selection rationale: these are the four packages every Mudlet/MUSHclient/TT++ user actually scripts against on day one. Combat events, inventory deltas, character skills, and room exits are deferred to a v0.60.x point release once we see what the real userbase wants.

### What's not shipped (yet)

- **Char.Items.* (inventory deltas)** -- would need hooks at every InventorySystem mutation site. Iterative.
- **Char.Skills.* (skill list / proficiency changes)** -- needs hooks in TrainingSystem.
- **Combat events** -- full GMCP combat protocol is non-trivial. Defer to a point release.
- **Char.MaxStats / Char.BaseStats** -- players rarely script against these vs. Vitals.
- **Room.Exits enum** -- Room.Info ships with empty `exits` placeholder for now. Each location implementation would need to expose its exit list, which is more refactoring than a hotfix-class change warrants.
- **Server-supported package list (`Core.Supports.Set`)** -- clients can negotiate which packages they want; we currently emit unconditionally to GMCP-enabled sessions. A point release should respect the supports list so we don't waste bandwidth on packages clients ignore.

---

## Founder statues -- alpha-era commemoration

Eleven alpha-era founders are commemorated in-world at beta launch. Identified from the live online database before the wipe; criteria: reached Level 100, ascended to Immortal, or defeated Manwe and started NG+. Each statue carries a unique themed ASCII silhouette (harvest god, benevolent giver, speed-runner, dark conqueror, pure healer, long-walker, contradiction-bearer, cycle-wanderer, first shaman, lost immortal, renamed-and-erased) and a carved-stone narrative composed from that founder's actual playstyle data.

### The eleven

| Founder | Class | Identity | Achievement |
|---|---|---|---|
| spudman | Barbarian | "Spud of the Harvest" | Lv.100 Cycle II Savior, immortal |
| quent | Barbarian | "Quent the Benevolent" | Lv.100 Cycle II Savior, immortal |
| fastfinge | Warrior | "fastfinge the Unburdened" | Lv.100, immortal |
| sandoval | Warrior | (43,753 lifetime Darkness pre-cap) | Lv.93 Cycle II Usurper, immortal |
| amaranth | Tidesworn | "the Unstained" | Lv.57 Cycle II Savior |
| lueldora | Tidesworn | "Lumina Starbloom, Long-Walker" | Lv.51 Cycle II Savior, 610 hours, all 7 seals |
| mystic | Cyclebreaker | (faith-hearted, chose Usurper) | Lv.34 Cycle II Usurper |
| biaxin | Voidreaver | "Alistar The Almighty, Cycle-Walker" | Cycle 4, Usurper + Defiant endings |
| sedz | Mystic Shaman | "First Shaman" | Cycle II Savior |
| Coosh | (cracked / lost) | Immortal who deleted on a class-reroll | Cycle II, accidentally erased |
| Zengazu | (cracked / lost) | NG+ veteran who renamed to "2zengazu2furious" then deleted | Cycle II, accidentally erased |

### Placements

- **Pantheon: Hall of the Ascended** -- 5 statues (4 immortals + cracked Coosh). Visible only to immortals (the divine realm).
- **Castle Courtyard: The Slayers of Manwe** -- 9 statues (8 NG+ veterans + cracked Zengazu). Visible to all players from inside the throne room or from OutsideCastle.
- **Temple: Hall of the Ascended** (mortal-facing duplicate) -- 5 statues (same as Pantheon). Mortals can't enter the Pantheon, so the immortal-founder statues are mirrored here at the Temple where the public venerates the gods. Same data, two access points.
- **Main Square: Founders' Plinths** -- 3 mini statues for the Lv.100 trio (spudman, quent, fastfinge).

### Access

- **`[V] Courtyard Statues`** -- Castle royal and commoner menus
- **`[V] Hall of the Ascended`** -- Temple menu
- **`[H] Hall of the Ascended`** -- Pantheon menu (immortal-only)
- **`/founders` / `/statues` / `/hall`** -- universal slash command from any location, opens a hub picker for all three placements

### Cracked variant

Coosh and Zengazu's statues render in `dark_gray` with their own cracked ASCII art (Coosh: dissolving halo with diagonal cracks and "( no refund )" engraving; Zengazu: scratched-out name plate showing "2 ze 2 / n##zu fu### / ## us"). Their detail screens add `(Their record was lost to misadventure.)` in italic gray above the carved-stone narrative.

The data is hardcoded in `Scripts/Data/FounderStatueData.cs` and shipped in the binary. No DB schema, no save migration, survives the beta wipe by design.

---

## Tier 1 character-deletion safety

Two players (one of them Zengazu) accidentally deleted characters and lost prestige class unlocks plus immortal status. Investigation revealed the prestige check at `CharacterCreationSystem.GetUnlockedPrestigeClasses` reads `StoryProgressionSystem.CompletedEndings` from the **character save** rather than the existing `MetaProgressionSystem.UnlockedEndings` which lives in a separate file. `MetaProgressionSystem` already records ending unlocks via `RecordEndingUnlock()` -- the prestige check just wasn't consulting it.

Tier 1 ships now (pre-beta). Full account/character architectural separation is Tier 2, scheduled for a point release.

### Prestige unlock now SSH-account-scoped

`GetUnlockedPrestigeClasses` unions the current character's `CompletedEndings` with the account's `MetaProgressionSystem.UnlockedEndings`. The cycle gate is satisfied if either the current character is past cycle 1 OR the account has any recorded ending. A player who deletes their immortal character and rerolls on the same SSH account now keeps prestige class access. A fresh-character reroll picks up where the deleted one left off.

### Enhanced delete-character warning

The "Delete Save" flow at Main Street now enumerates explicitly what gets erased (inventory / stats / quests / cycle / immortal status), what's preserved (account-level prestige unlocks, ending records, Steam achievements), and prominently displays the 7-day grace window before the typed `DELETE` confirmation. The success message after deletion also reminds the player how to restore.

### `deleted_characters` archive table + 7-day grace window

New SQLite table in the online server's database. When a character is deleted via `SqlSaveBackend.DeleteGameData`, the JSON blob is copied here with `expires_at = now + 7 days` BEFORE the live `player_data` is cleared. Indexed on lowercased username + `expires_at`. Each delete also opportunistically purges expired rows (cheap with the index).

### `/restore` slash command

Looks up the most recent archived character for the calling SSH account, refuses if there's already an active character on that account (won't silently overwrite a new reroll), restores the JSON blob to `players.player_data`, removes the archive entry, tells the player to log out + back in. Failure modes (expired, no active row, race condition, sql error) all have specific error messages routed through the slash-command response.

### Why this is enough for beta

- Recovers the most common accidental-delete failure mode (player doesn't realize delete is permanent).
- Doesn't require a save format change.
- The prestige-unlock fix solves the specific bug Zengazu hit twice without architectural refactoring.
- Tier 2 (full account/character split with multi-character per account) is a 40-60h refactor scheduled for a v0.60.x point release once beta is stable.

---

## /tell multi-word display name fix

Player report (`2Zengazu2Furious`): "I can't seem to /tell my friend Lumina starbloom." Root cause: `MudChatSystem.HandleTell`'s parser at `args.Substring(0, spaceIndex)` took everything before the **first** space as the target name. So `/tell Lumina Starbloom hi` parsed as `target=Lumina, message="Starbloom hi"` -- the second word of her display name got eaten by the message. Multi-word display names couldn't be addressed at all.

Fix: new `ResolveTargetAndMessage` helper does **greedy longest-prefix matching**. For `/tell Lumina Starbloom hi friend`:

1. Try target = `"Lumina Starbloom hi"` → no match
2. Try target = `"Lumina Starbloom"` → match → message = `"hi friend"`

So `/tell Lumina Starbloom hi friend` now correctly delivers `"hi friend"` to Lumina Starbloom. Single-word names still work -- the loop just stops at the only available prefix. Falls back gracefully to "X is not online" with the first-word interpretation if no prefix resolves, so the player gets a sensible error instead of something baffling.

`/group` was audited and already takes the entire args as a target name (no message parsing), so it already handled multi-word names correctly. `/spectate` is initiated via the main menu, not chat-system parsing, so it's also unaffected.

---

## French translation gaps + UTF-8 defense in depth

Player report on the live server: "the french translation is not encoding correctly. some words aren't translated."

### Translation gaps

Audit of `Localization/fr.json` against `en.json` found 18,171 keys with 270 sharing the English value. Most of those (~260) are intentional cognates (Combat, Faction, Exploration, Confession, Abdication, Anticipation), proper nouns (USURPER REBORN, Halls of Avarice, Seth Able), or universal acronyms (CON, DEX, INT, MVP, XP). About 10 were genuine gaps and are now translated:

- `prefs.off`: "OFF" → **"ARRÊT"**
- `spec.active_marker`: "[ACTIVE]" → **"[ACTIF]"**
- `dungeon.strangers_orcs`: "orcs" → **"orques"**
- `base.standing_hostile`: " (Hostile)" → " (Hostile)" (already cognate, kept)
- Plus minor banner/header polish on faction headers, achievement category labels, confession headers, abdication headers

### UTF-8 defense in depth

Investigation of the encoding complaint showed the player-output path uses explicit `Encoding.UTF8.GetBytes()` on the network stream, and `fr.json` itself contains valid UTF-8. The most likely root cause is the player's SSH client not being configured for UTF-8. As a server-side belt-and-suspenders, `Console/Bootstrap/Program.cs` now explicitly sets `Console.OutputEncoding = UTF8` and `Console.InputEncoding = UTF8` at startup (skipped only for BBS door mode where CP437 is intentional). This protects against systemd-on-Linux services running without a UTF-8 locale, where `Console.Out` defaults to ASCII and silently strips accented characters from any code path that uses `Console.WriteLine` rather than the network stream directly.

---

## Electron graphical client -- Phase 9.5 polish

(Cumulative work from v0.58 graphical-client phases consolidated for the beta launch.)

### Settings overlay

New `/settings` and `/set` slash commands open a focused settings card with: language `<select>`, font scale slider (0.85x–1.4x), screen reader toggle, compact mode toggle, art toggle, date format readout. Each control fires its own `/settings KEY VALUE` slash command on change so updates apply incrementally. Confirmation toast confirms each change. Font scale persists via `localStorage.usurper.fontScale` so preference survives restart.

Sub-commands: `/settings lang es`, `/settings sr on`, `/settings compact off`, `/settings art on` -- all callable directly without opening the overlay.

### JS-side i18n shim

New `electron-client/src/i18n.js` with `window.i18n.t(key, ...args)`, `setLanguage(code)`, `getLanguage()`, `getEnglishSource()`. Embedded English baseline (~75 keys) covering settings, lifecycle events, multiplayer overlays, common UI. Fallback chain: current language → embedded English → key (so missing keys surface as the literal key, easy to spot during translation review).

Translations dropped for all four target languages: `electron-client/lang/{es,fr,hu,it}.json`. Translation method: 4 parallel general-purpose agents, each given the English source JSON + glossary hints (alignment terms, banner CAPS convention, placeholder preservation, "(Y)/(N)" → language-appropriate keys where natural). About 75 keys × 4 languages = 300 translations.

JS-rendered overlays refactored to use `t()`: level-up, death, quest complete, group invite, spectate, chat panel, news feed, boss phase, settings.

### Audio infrastructure scaffolding

Web Audio receiver in `electron-client/src/audio.js`. Lazy `AudioContext` creation gated on user interaction (browser autoplay policy). Channel mixer graph: `master → {sfx, music, ui}`. Per-channel volume restored from `localStorage.usurper.volumes`. `loadBuffer()` lazy-fetches `assets/audio/<channel>/<name>.ogg` on first play, caches misses to avoid repeated 404s. `playSound()` silent no-op on missing buffer -- emit-from-C# is safe even before assets exist.

Bridge helpers added in `Scripts/UI/ElectronBridge.cs`:
- `EmitSound(soundId, volume, pitch, channel)` -- fire a sound trigger
- `EmitSoundStop(soundId)` -- stop a looping sound
- `EmitVolumeSet(channel, volume)` -- set per-channel volume

C# emit hooks at four sites:
- `LevelMasterLocation.CheckAutoLevelUp` -- `sfx.level_up_milestone` for every-5th levels, `sfx.level_up` otherwise
- `CombatEngine.HandlePlayerDeath` -- `sfx.death_permadeath` for Nightmare mode, `sfx.death` otherwise
- `AchievementSystem.TryUnlock` -- `sfx.achievement.{tier}` per Bronze/Silver/Gold/Platinum/Diamond
- `CombatEngine.PlayBossPhaseTransition` -- `sfx.boss.phase_transition`

Asset directory structure (`electron-client/assets/audio/sfx,music,ui`) created with `README.md` documenting the path convention and the 10 currently-emitted soundIds. Assets directory is empty in this release. As `.ogg` files land, they become audible immediately with no code changes.

---

## Files Changed (cumulative)

### New files

- `Scripts/Data/FounderStatueData.cs` -- 17 statue placements representing 11 unique founders, with per-founder narrative inscriptions, achievement tags, custom art arrays, and `IsCracked` flag for the two who deleted themselves.
- `Scripts/Systems/FounderStatueSystem.cs` -- list rendering, examine flow, ASCII art rendering with custom-art support per founder, Electron emit for graphical clients.
- `Scripts/Server/GmcpBridge.cs` -- Static class with `Emit(package, payload)` for current-session emit and `EmitTo(session, package, payload)` for cross-session fan-out.
- `Tests/GmcpBridgeTests.cs` -- 8 unit tests for GMCP framing, IAC escaping, JSON output, edge cases.
- `electron-client/src/i18n.js` -- JS i18n shim with embedded English + per-language overrides.
- `electron-client/src/audio.js` -- Web Audio receiver with channel mixer.
- `electron-client/lang/{es,fr,hu,it}.json` -- Translations for the JS overlay strings.
- `electron-client/lang/_template.json` -- Source template for translators.
- `electron-client/lang/README.md` -- Translation workflow notes.
- `electron-client/assets/audio/README.md` -- Audio asset path convention.

### Modified files

- `Scripts/Core/GameConfig.cs` -- version bump 0.57.20 → 0.60.0; `VersionName` updated to "Beta".
- `Scripts/Server/SessionContext.cs` -- new `GmcpEnabled` property.
- `Scripts/Server/MudServer.cs` -- initial negotiation extended with `IAC WILL GMCP`; `ProbeTtypeAsync` recognizes `IAC DO GMCP`; passes through to `PlayerSession`.
- `Scripts/Server/PlayerSession.cs` -- accepts `gmcpEnabled` parameter, stores it, copies to `ctx.GmcpEnabled`.
- `Scripts/Systems/LocationManager.cs` -- `EnterLocation` emits `Room.Info` and `Char.Status` after the room registry update.
- `Scripts/Locations/BaseLocation.cs` -- `EmitGmcpVitals` private method with 5-field delta tracking. New `/founders` / `/statues` / `/hall` slash command + `ShowFounderHubMenu` hub picker. New `/settings` / `/set` slash command + `HandleSettingsCommand` + `EmitSettingsScreenToElectron`. Em dashes replaced with cleaner punctuation in the founder hub copy.
- `Scripts/Server/MudChatSystem.cs` -- `Comm.Channel.Text` GMCP fan-out via `FanoutChannelText` helper. New `ResolveTargetAndMessage` greedy multi-word name parser for `/tell`. New `HandleRestore` for the `/restore` slash command. New `restore` case in the command switch.
- `Scripts/Systems/CharacterCreationSystem.cs` -- `GetUnlockedPrestigeClasses` unions character-scope `CompletedEndings` with account-scope `MetaProgressionSystem.UnlockedEndings`. Cycle gate satisfied if either character is past cycle 1 OR account has recorded endings.
- `Scripts/Systems/SqlSaveBackend.cs` -- new `deleted_characters` table + indexes. `DeleteGameData` archives JSON blob to `deleted_characters` with 7-day expiry before clearing `player_data`. New `GetMostRecentDeletedCharacter` and `RestoreFromDeleted` methods. New `DeletedCharacterInfo` data class.
- `Scripts/Locations/MainStreetLocation.cs` -- enhanced delete-character warning enumerating what's lost vs preserved, with prominent grace-window note.
- `Scripts/Locations/CastleLocation.cs` -- `[V] Courtyard Statues` action wired into both royal and commoner menus + Phase 5 Electron emit.
- `Scripts/Locations/PantheonLocation.cs` -- `[H] Hall of the Ascended` menu option (immortal-only access path).
- `Scripts/Locations/TempleLocation.cs` -- `[V] Hall of the Ascended` menu option (mortals' access path to immortal-founder statues).
- `Console/Bootstrap/Program.cs` -- explicit `Console.OutputEncoding = UTF8` and `InputEncoding = UTF8` at startup (skipped only for BBS door mode).
- `Localization/fr.json` -- translation gaps fixed (~10 strings).
- `Scripts/UI/ElectronBridge.cs` -- Phase 9 settings + Phase 9.5 audio bridge helpers (`EmitSettingsScreen`, `EmitSettingsApplied`, `EmitSound`, `EmitSoundStop`, `EmitVolumeSet`); Phase 6 dialogue + quest helpers; Phase 7 lifecycle event helpers; Phase 8 multiplayer parity helpers; new `customArt` and `artColor` fields on `statue_detail` emit.
- `Scripts/Locations/LevelMasterLocation.cs` -- `EmitSound` for level-up.
- `Scripts/Systems/CombatEngine.cs` -- `EmitSound` for player death, boss phase transition.
- `Scripts/Systems/AchievementSystem.cs` -- `EmitSound` for tier-specific achievement chime.
- `electron-client/src/game-ui.js` -- new event handlers for `statue_list` / `statue_detail` / `settings` / `settings_applied` / `sound` / `sound_stop` / `volume_set`. JS-rendered overlays refactored to use `window.i18n.t()`.
- `electron-client/styles/gui.css` -- new `--gui-font-scale` CSS variable, `.settings-card` styling.
- `electron-client/renderer.html` -- script loads `i18n.js` and `audio.js` before `game-ui.js`.

---

## Tests

8 new GMCP tests + 0 changes to existing tests. Full suite: 661/661 green.

---

## Gods are no longer recruitable as team members

Player report (Spudder, Lv.22 Voidreaver -- alt of "spudman" who ascended as Spud of the Harvest): "God characters showing up in team after rolling new character, can be used in dungeons."

Two distinct holes in the team-recruitment pipeline allowed immortal characters to end up on mortal rosters and be marched into combat:

1. **Recruitment filter missing the immortal check.** `TeamSystem.IsRecruitable` already filtered out dead NPCs, NPCs already on a team, special NPCs (Seth Able etc.), prisoners, and partners (spouses/lovers/FWB/exes). Immortals weren't on the list. Players who had ascended (`IsImmortal = true`) appeared in the recruitment menu like any other NPC, with their own level + class + relationship score -- and could be hired with gold.

2. **Ascension didn't auto-quit the team.** `EndingsSystem.OfferImmortality` already auto-abdicates if the ascending player is the king, but it never quit their team membership. A player who was on a team and ascended remained listed as a team member. Then their teammates could see them in team rosters, take them into dungeons, etc.

For Spudder specifically, his main account `spudman` ascended cleanly (no team to leave at the time, so #2 didn't apply to him). His issue is more likely #1 -- the alt's recruitment menu surfaced other ascended players as eligible, and he hired one or more without realizing they were gods.

Fix:

- **`TeamSystem.IsRecruitable`** now blocks `npc.IsImmortal` along with the existing five filters. New immortals never appear in the recruitment list.
- **`EndingsSystem.OfferImmortality`** auto-quits the player's current team via `new TeamSystem().QuitTeam(player)` after the king-abdication step. The QuitTeam helper handles the side effects (CTurf cleanup, team-dissolution check if last member). New flavor message: "Your mortal team bonds dissolve in the divine ascension."

What this does NOT do: heal existing teams that already contain immortals as members. The data lives in the SQL `player_teams` table indexed by team name + member name; a one-shot heal would need to scan teams, resolve each member name to its NPC record, check `IsImmortal`, and unlink. That's a defensible v0.60.x point-release task once the prevention layer is in place. For now, sysops can clean up via the wizard console (`OnlineAdminConsole`) by removing the immortal manually from any team where they're listed.

**Files modified:**
- `Scripts/Systems/TeamSystem.cs` -- `IsRecruitable` adds `npc.IsImmortal` filter.
- `Scripts/Systems/EndingsSystem.cs` -- `OfferImmortality` auto-quits team after auto-abdicating king.

### Guild parity (same fix shape, same release)

Question raised in audit follow-up: "When we patched gods out of teams, did we also make sure they aren't in guilds too?" Audit confirmed no -- the guild system had the same two holes:

1. **`GuildSystem.AddMember` had no `IsImmortal` check.** An ascended player could accept a guild invite via `/accept` and would appear on the guild roster like any mortal member. The same "have a god in our guild" narrative break the team filter prevented.
2. **`GuildSystem.CreateGuild` also bypasses AddMember**, doing a direct `INSERT INTO guild_members` for the founder, so it needed an independent immortal check at the top.
3. **`EndingsSystem.OfferImmortality` didn't auto-leave guild** the same way it auto-quits team. An ascending player who was already in a guild stayed on the roster permanently -- and once the new AddMember/CreateGuild filters land, they couldn't even rejoin if removed.

Fix: same shape as the team patch.
- `GuildSystem.AddMember` checks `SessionContext.Current.Player?.IsImmortal` for the username being added (the accept-invite flow runs while the invitee has an active session, so the live Character is reachable). Returns `"Gods do not join guilds."` on hit.
- `GuildSystem.CreateGuild` adds the same check at the top with the message `"Gods do not lead guilds."`. Catches the founder direct-insert path.
- `EndingsSystem.OfferImmortality` queries `GuildSystem.GetPlayerGuild(username)`, calls `RemoveMember(username)` if the player is in one (RemoveMember handles leader-promotion automatically if they led the guild), and prints `"Your guild crest tarnishes and falls away. The faithful have no guild."`

Same caveat as teams: existing guilds that already contain immortals from before this fix are not auto-cleaned. Sysop intervention via the admin console can resolve any stragglers.

**Additional files modified:**
- `Scripts/Systems/GuildSystem.cs` -- `AddMember` and `CreateGuild` both gate on `IsImmortal`.
- `Scripts/Systems/EndingsSystem.cs` -- `OfferImmortality` now also auto-leaves guild after auto-quitting team.

## Permanently mis-typed shields now self-heal on load

Player report (Glamdring, Lv.58 Troll Warrior): "Shield abilities are not active for me. Even though a shield is equipped they are not usable. I did notice that my combat style is listed as dual wield even with the shield equipped."

This is the same bug-class as the v0.53.13 "Shield-turned-sword on save reload" fix, but for a corner case that v0.53.13's heal couldn't catch. The v0.53.13 migration added a `isShield = ShieldBonus > 0 || BlockChance > 0 || WeaponType ∈ {Shield, Buckler, TowerShield}` check that skips the bad weapon-type re-inference. Subsequent loads heal an item that was momentarily mis-typed. **But** for a player whose shield got mangled by a load that ran BEFORE v0.53.13, then got saved with `WeaponType=Sword, ShieldBonus=0, BlockChance=0` (because the mangled state was the new reality at save time), no later load can recognize the item as a shield. All three stat-side signals are gone. The item silently stays a "Sword" forever.

`HasShieldEquipped` then returns false, `IsDualWielding` returns true (one-handed weapon + one-handed "sword" in offhand), and:
- Shield-required abilities (`shield_wall`, `shield_wall_formation`, `divine_shield`, `divine_mandate`, `iron_fortress`, `aura_of_protection`, etc.) fail their `RequiresShield` gate.
- Combat status display says "dual wield" because the engine sees two one-handed weapons.
- Tank spec damage bonuses tied to shields don't apply.
- IsDualWielding fires an off-hand "swing" each round that does nothing because the "weapon" has no WeaponPower.

Fix: name-based shield detection added as a **third signal** alongside the stat checks. New `ShopItemGenerator.LooksLikeShieldByName(string name)` matches the keywords `Shield`, `Buckler`, `Aegis`, `Bulwark`, `Pavise`, `Targe`, `Heater`, `Kite`, `Round Wood`, `Ward of `, `Fortress`, and `Tower` (excluding `Towering` to avoid false positives). Both migration sites in `GameEngine.cs` (single-player save load and online-mode SQL load) now consult this as a third clause: if the item is in OffHand AND the name looks shield-like, it's treated as a shield even when all stat signals were stripped. The existing heal branch then re-runs `InferShieldType(name)` to restore the proper WeaponType (Shield/Buckler/TowerShield) and `Handedness = OffHandOnly`.

After Glamdring's next login, his shield will be detected by name, healed back to a proper shield, and his shield abilities will activate. The fix is one-shot: once the shield's `WeaponType` is corrected and saved, subsequent loads stay fast (the migration's existing stat-side signals match again).

This does NOT recover lost ShieldBonus / BlockChance values -- those were zeroed by the original v0.53.13-era bug and we have no source-of-truth to restore them from. Glamdring's shield will function as a shield (handedness, ability gating, and "sword and board" detection all work) but with whatever stat values are currently in the equipment record. If he wants the original BlockChance back, he can sell + rebuy / reroll the shield, but the basic functionality is fixed.

**Files modified:**
- `Scripts/Systems/ShopItemGenerator.cs` -- new `LooksLikeShieldByName(string name)` helper.
- `Scripts/Core/GameEngine.cs` -- both load-path migration sites add the name-based shield detection as a third clause to `isShield` / `isShieldNpc`.

## Magic Shop enchantment silently stripped existing enchants

Player report (Lumina Starbloom, Lv.39 Wavecaller): "Adding enchantment to a looted weapon removes existing enchantments. I had Piercing Bardiche WP:262 ArmorPen 48%, Fire, Frost. Wanted to add lifedrinker. But after Ravanella enchanted the weapon, only lifedrinker remained. There was no warning the weapon had too many enchantments."

Two distinct issues:

### Cause #1: `Equipment.Clone()` was missing 13 fields

The Magic Shop's enchant flow does `var enchanted = selectedEquip.Clone(); enchanted.IncrementEnchantmentCount(); /* apply new enchant */; player.EquipItem(enchanted);`. The intent is "copy the existing equipment, layer the new enchant on top, replace the original." But `Equipment.Clone()` was hand-written and silently dropped:

- `HasFireEnchant`, `HasFrostEnchant`, `HasLightningEnchant`, `HasPoisonEnchant`, `HasHolyEnchant`, `HasShadowEnchant` (6 elemental enchant flags from v0.30.9+)
- `ManaSteal`, `ArmorPiercing`, `Thorns`, `HPRegen`, `ManaRegen` (5 proc-based enchantment ints from v0.40.5)
- `HasBossSlayer`, `HasTitanResolve` (2 world-boss-exclusive flags from v0.54.0)

So Lumina's Piercing Bardiche (`HasFireEnchant=true`, `HasFrostEnchant=true`, `ArmorPiercing=48`) went through `Clone()` and the cloned copy reset all three to defaults (false, false, 0). The Lifedrinker enchant added `LifeSteal += 3` and the resulting weapon had ONLY Lifedrinker. Same bug class as the v0.53.5 "Loot stat transfer fix -- CHA/AGI/STA silently dropped on equip" -- fields added to the schema after Clone() was first written never got threaded into the copy method.

Fix: 13 fields added to `Clone()` to bring it back into parity with the field declarations. Verified by inspection that no other code calls `Clone()` with intent to RESET these fields (Magic Shop uses Clone() for both the success and 4th/5th-enchant-failure paths; the failure path WANTS to drop one enchant via decremented counter + stat reduction, but doesn't expect the elemental flags to silently vanish either).

### Cause #2: no pre-attempt warning when the weapon already has 3+ enchants

The Magic Shop has a "4th/5th enchant has X% chance to fail and destroy an existing enchant at random" mechanic via `GameConfig.FourthEnchantFailChance` / `FifthEnchantFailChance`. The narrative flavor at the failure point already says: `'I warned you... that's the risk of pushing beyond three enchantments.'` -- but that's narrated AFTER the gold and materials are spent. There was no actual warning before the player committed.

Fix: pre-confirmation warning block in `MagicShopLocation.cs`. When the selected weapon has `GetEnchantmentCount() >= 3`, the confirmation prompt now prepends a bright-red WARNING listing:

- The current enchant count
- The exact failure percentage based on the current count
- What the failure consequences are (gold + materials consumed, one existing enchant destroyed at random)
- An in-fiction line from Ravanella ("You are testing the weave.")

The player still has to type `Y` at the existing proceed prompt; no second typed phrase. The warning is informational, not a separate gate -- players still control their own risk.

**Files modified:**
- `Scripts/Core/Items.cs` -- `Equipment.Clone()` now copies all elemental enchant flags, proc-based enchantment ints, and world-boss-exclusive flags. 13 fields added.
- `Scripts/Locations/MagicShopLocation.cs` -- pre-confirmation warning when `GetEnchantmentCount() >= 3`.

## Symphony of the Depths -- teammates now actually get the auto-crit

Player report (Lumina Starbloom, Lv.30 Wavecaller): "Symphony of the Depths does not always trigger crit on the next attack. Several attacks after using Symphony were normal attacks, despite the message saying a crit is guaranteed on the next hit."

Root cause: the spell description explicitly promises **"All allies: +60 attack, +40 defense, guaranteed crit on next hit"** but the implementation only set the `Hidden` status (the engine's auto-crit-on-next-attack marker) on the caster. Per the SpellSystem `case 5` block at line 1512, `caster.ActiveStatuses[StatusEffect.Hidden] = 2` was applied to the caster only. The multi-target buff loop in `CombatEngine` at line 14789 then iterated teammates and applied `+60 attack` (TempAttackBonus) and `+40 defense` (Blessed + MagicACBonus) -- but did NOT propagate Hidden. So teammates' next attacks were ordinary, exactly matching Lumina's report.

Lumina has Mira and Melodia in her party (per her save log), so when they attacked between her Symphony cast and her next own attack, they fired normal hits. The "next hit" the description was promising for "all allies" was only ever delivered to the caster.

Fix: in the multi-target buff loop, after applying ProtectionBonus and AttackBonus to each teammate, propagate `Hidden = 2` if the caster has Hidden (Symphony's marker). Hidden=2 matches the caster's value and the established `shadow`-ability convention from v0.57.17. Teammates have no per-round `ProcessStatusEffects` tick anyway, so the `2` vs `1` distinction matters less for them, but consistency keeps the code symmetric.

After the fix, Lumina + Mira + Melodia each crit on their next attack after Symphony resolves. The "guaranteed crit on next hit" promise in the description is now actually true for all allies.

**Files modified:**
- `Scripts/Systems/CombatEngine.cs` -- Symphony / Covenant multi-target buff loop now propagates `Hidden` status to all living teammates when the caster has it.

## /gos mute now blocks Discord-originated gossip too

Player report: "The /gos mute feature isn't blocking Discord gossips, just in-game gossips. It should turn it totally off for the player."

The v0.57.15 per-channel mute (`/gos` / `/shout` / `/gc` / `/tell` with no message toggles mute) wires `channelKey` through `RoomRegistry.BroadcastGlobal` so recipients with the channel in their `MutedChannels` set are skipped. In-game gossip respects this. The Discord inbound path missed it.

`MudServer.DiscordBridgePollerAsync` polls the `discord_gossip` SQLite table and rebroadcasts each Discord-side message in-game. Pre-fix, that broadcast called `RoomRegistry.Instance.BroadcastGlobal(text)` with no `channelKey` argument so the per-channel mute filter was skipped entirely. A player who had typed `/gos` to silence gossip kept seeing every Discord post in the same channel.

The GMCP fan-out for chat-capture clients (Mudlet/MUSHclient/TT++ split panes feeding `Comm.Channel.Text` to a status pane) was also missing for Discord-inbound, so those clients got Discord posts through the regular ANSI scroll instead of the structured GMCP stream they'd subscribed to.

Fix:
- `BroadcastGlobal` call in `DiscordBridgePollerAsync` now passes `channelKey: "gossip"` so the same `MutedChannels` filter that gates in-game `/gos` also gates Discord-inbound gossip.
- `MudChatSystem.FanoutChannelText` (the GMCP `Comm.Channel.Text` emitter) was changed from `private static` to `internal static` so the Discord poller can call it. Discord-inbound gossip now also reaches GMCP-enabled clients via the same channel, with the same mute-respecting fanout logic.

Outbound direction (a muted player typing `/gos` themselves) intentionally still sends -- the player explicitly took an action; muting is for incoming traffic.

**Files modified:**
- `Scripts/Server/MudServer.cs` -- Discord poller passes `channelKey` and calls `FanoutChannelText`.
- `Scripts/Server/MudChatSystem.cs` -- `FanoutChannelText` visibility raised from `private` to `internal`.

## Lifedrinker cap

Player report: a single 11,873-damage hit drained 14,571 HP via Lifedrinker plus 716 from Divine Boon, total 15,287 healed for 11,873 damage. ~129% combined lifesteal. Mathematically and thematically wrong, you cannot drain more life than the target had to give.

Two stacking gaps caused it:

1. **`Character.GetEquipmentLifeSteal()` summed `LifeSteal` from every equipped slot with no cap.** Stack Lifedrinker on weapon (30%) + necklace (25%) + two rings (20% each) + a body armor piece (10%) and you sit at 105% lifesteal from equipment alone. Then it gets multiplied by damage and applied as flat HP heal. No upper bound.

2. **Five lifesteal sources fire independently in `ApplyPostHitEnchantments`** with no shared budget: divine blessing (dark god boon), equipment Lifedrinker, divine boon (worshipped player-god), Abysswarden Abyssal Siphon class passive (10%), and ability-granted status lifesteal (Voidreaver/Assassin/etc.). Each computed its own slice of damage and applied it to HP separately. Stacking three or four of these on one hit easily produced > 100% combined lifesteal.

**Two-layer cap:**

- **Per-source cap on equipment lifesteal.** `GetEquipmentLifeSteal` now clamps the sum to `GameConfig.MaxEquipmentLifeStealPercent = 60`. Stacked Lifedrinker enchants still help, but the combined total maxes at 60% of damage. Individual enchant tooltips/displays are unchanged (the cap is at sum-time, not per-piece) so a player can still see "+30% LifeSteal" on a weapon and reason about gear correctly.
- **Total per-attack cap across all sources.** New shared budget `damage * GameConfig.MaxTotalLifestealPercent / 100` (default 100%). Each of the five sources draws from this budget in declaration order; once the budget is exhausted, later sources scale down or skip entirely. So even with maxed equipment lifesteal AND active Divine Boon AND Abysswarden passive AND status lifesteal all firing on one hit, total heal cannot exceed damage dealt.

For the player's report case (11,873 damage):
- Pre-fix: equipment lifesteal at ~123% = 14,571 healed + boon 716 + others = ~15,300 total heal.
- Post-fix: equipment capped at 60% = 7,124 healed, then boon scales into the remaining 100%-60% = 40% budget = 4,749 cap (their boon was 716, well under), then later sources draw what's left. Total heal cannot exceed 11,873.

**Files modified:**
- `Scripts/Core/GameConfig.cs` -- new `MaxEquipmentLifeStealPercent` and `MaxTotalLifestealPercent` constants.
- `Scripts/Core/Character.cs` -- `GetEquipmentLifeSteal` clamps to per-source cap.
- `Scripts/Systems/CombatEngine.cs` -- `ApplyPostHitEnchantments` refactored with a shared `lifestealBudget` and `ApplyLifestealSlice` local helper that all five lifesteal sources draw from in sequence.

## Alignment audit -- diminishing returns near the cap

The alignment-cheese pass below capped per-action gold-to-alignment conversion. This deeper audit covers the wider problem: even with per-action caps, a determined player could chain Old God saves (+150 each), knighthood (+50), and 30+ small donations to reach the 1000-point cap in a single session. Player request: getting 1000 should be hard.

Audited every alignment grant site (98+ `ChangeAlignment` calls plus 5 direct-mutation bypasses) and added two changes:

### 1. Diminishing returns on the GAIN side, applied globally in `ChangeAlignment`

The closer a scale gets to the 1000-point cap, the smaller each gain becomes:

| Current value | Grant multiplier |
|---|---|
| 0..499  | 100% (normal early/mid progression) |
| 500..699 | 75% |
| 700..849 | 50% |
| 850..949 | 25% |
| 950..999 | 10% |

All 98+ grant sites benefit automatically because `ChangeAlignment` is the funnel. Paired-movement (the v0.57.0 design where good deeds also lower darkness, and vice versa) uses the un-scaled amount on the OPPOSITE side, so a small evil deed at high chivalry still meaningfully cleanses the chivalry. Only the gain side is scaled.

**Progression simulation (start at 0, no opposing alignment to offset):**

| Per-action grant | Actions to reach 1000 |
|---|---|
| +50 (knighthood, castle/temple capped donations) | 38 |
| +25 (church donation/blessing) | 84 |
| +5 (routine quests, beggar gifts, bandage wounded) | 391 |
| +150 (Old God save, story-gated, max 7 per cycle) | 12 (if all 7 Old Gods spared/saved) |

Realistic mixed late-game path: knighthood (1 action) + 4-5 Old God saves (~40 actions worth of +150 gated by phase 4-7 dungeon floors) + ~30 capped temple/castle/church donations. Roughly one full play-through to max if dedicated, multiple if balanced.

Symmetric for darkness: maxing evil from 0 needs ~20 temple sacrifices, multiple murders (capped via DR), bank robberies, Old God killings, plus altar desecrations.

### 2. Routed remaining direct-mutation bypass sites through `ChangeAlignment`

The v0.57.12 audit caught most sites; v0.60.0's audit found four more that mutated `Chivalry` / `Darkness` directly and skipped both paired movement and (now) DR:

- `BankLocation.cs:1549` (loan-delinquency minor, +5 darkness)
- `BankLocation.cs:1574` (loan-delinquency severe, +20 darkness)
- `MagicShopLocation.cs:3397` (death-spell success, dark shift)
- `MagicShopLocation.cs:3440` (death-spell failure, half dark shift)
- `BaseLocation.cs:1207-1212` (encounter-stage choice rewards, both directions)
- `CastleLocation.cs:1980` (king banishment punishment, +500 darkness)

All now go through `ChangeAlignment`. The opposing-side direct decrements (e.g. `Chivalry = Math.Max(0, Chivalry - 5000)` for banishment, `Chivalry -= 50` for severe loan-delinquency) stay direct because they are flat punishments, not gains -- DR doesn't apply to losing alignment.

`QuestSystem.cs` already routed its quest reward grants through `ChangeAlignment` in v0.57.12; `QuestHallLocation.cs:724/727` only populates a display DTO and never touches the live character, so it doesn't need wrapping.

### Why per-action caps AND DR

The cheese pass alone would still let a player donate 20 times to reach max via the +50 cap. DR alone would still let one Old God save move alignment +150 from 0. Both together produce the right curve: small donations matter early, big story events have impact mid-game, and the final stretch (850 to 1000) costs 5-10x as much as the first 500 points. Maxing alignment is now a long-term commitment, not a checkbook decision or a single dungeon run.

### Files

- `Scripts/Systems/AlignmentSystem.cs` -- `ChangeAlignment` now scales gain by current value via new `ScaleAlignmentByDR` helper. Paired opposite-side movement unchanged (uses raw amount).
- `Scripts/Locations/BankLocation.cs`, `MagicShopLocation.cs`, `CastleLocation.cs`, `BaseLocation.cs` -- four bypass sites routed through `ChangeAlignment`.

## Alignment cheese pass

Player report from in-game gossip: "Overloading goodness or darkness is crazy easy. Donate 100k gold to an evil god, probably less, and I have max darkness. Pay the king some 200k, then go to church and pay them like 50k, and I have max chivalry."

Audit found three alignment-grant paths that converted gold to alignment 1:1 without per-action caps, against an `AlignmentCap` of 1000. The Castle treasury donation was already correctly capped at +50 per action (CastleLocation.cs:7090); two other paths missed the same cap:

| Path | Pre-fix formula | 100k donation result | Behavior |
|---|---|---|---|
| Castle treasury | `Math.Min(50, amount/100)` | +50 chivalry | already capped (correct) |
| Church donation | `amount/11` | +9090 chivalry, clamped to 1000 | maxed in one transaction |
| Church blessing | `amount/15` | +6666 chivalry, clamped to 1000 | maxed in one transaction |
| Temple gold sacrifice (evil/good god) | `amount/100` | +1000 darkness or chivalry, exactly the cap | maxed in one transaction |

The setter clamp (added in v0.57.12) prevented values from exceeding 1000, but it didn't slow down the path to 1000. One donation = full alignment.

**Fix.** Cap per-action alignment gain across all three paths so reaching the alignment cap requires sustained behavior, not one big payment:

- New `GameConfig.MaxChivalryGainPerChurchAction = 25` -- church donation and blessing now cap at +25 chivalry per action regardless of donation size. A 100k donation still costs 100k gold (more, with the v0.60.0 issue #89 penance) but only moves alignment by +25.
- New `GameConfig.MaxAlignmentGainPerTempleSacrifice = 50` -- temple gold sacrifice to evil/good gods caps at +50 darkness/chivalry per action. A 100k sacrifice grants +50 darkness (and the same value to faction reputation, also intentional -- big donors should not buy a top faction rank in one shot either).
- Castle treasury donation unchanged (already capped at 50).

Net effect: maxing chivalry from 0 now requires roughly 40 church donations or 20 castle/temple actions plus knighthood (a one-shot +50 grant) and quest rewards. Maxing darkness from 0 requires roughly 20 temple sacrifices, plus murders, bank robberies, and altar desecrations. Both paths cost real time and real gold; alignment is once again a meaningful long-term lever instead of a checkbook decision.

The raw gold cost is unchanged, the displayed actual delta after clamping (added in this same release) makes the new per-action gain transparent to the player. Faction reputation paths (`TheFaith` from church donations, `TheShadows` from evil sacrifices) inherit the temple cap because they share the same `standingGain` variable; this is intentional and prevents factional rank-buying.

**Files modified:**
- `Scripts/Core/GameConfig.cs` -- new `MaxChivalryGainPerChurchAction` and `MaxAlignmentGainPerTempleSacrifice` constants.
- `Scripts/Locations/ChurchLocation.cs` -- both donation and blessing paths cap `requestedChivalryGain`.
- `Scripts/Locations/TempleLocation.cs` -- gold sacrifice path caps `standingGain`.

## Stun-lock cheese pass

Player report from in-game gossip channel: "I just stun-lock the boss and walk through the fight." Audit confirmed the cheese was real and structural, not a one-off bug. The issue lived in three places at once:

1. **Three independent stun fields on `Monster`** -- legacy `Stunned` (bool), modern `IsStunned + StunDuration`, and a third `StunRounds` counter -- each ticked separately in `ProcessMonsterAction`. A player using both a Magician Stun spell AND a Lightning-enchanted weapon would set TWO different fields on one monster; the monster then skipped two consecutive monster turns from a single round of player input.
2. **No "already stunned" check in any application path.** Spells, abilities, and enchant procs all used the pattern `if (target.StunImmunityRounds > 0) resist; else target.IsStunned = true;`. They never asked "is this monster already stunned right now?", so a stun applied while already stunned just refreshed the duration via `Math.Max`. Indefinite refresh = perma-stun.
3. **Immunity window was 1 round AND ticked down before the player's next attack.** `StunImmunityRounds = 1` after a stun expired, then `ProcessMonsterAction` decremented it to 0 on the very same monster turn, leaving zero gap before the next player input. Effective immunity duration: zero.

Lightning Enchant procs were the highest-volume vector. 15% per attack, but with multi-attack classes and dual-wielding the proc rolls compound to ~50% per round. With three stun fields stacking and no "already stunned" guard, a Magician/Sage with a Lightning Bardiche could keep an Old God boss perma-stunned for an entire fight.

**Fix: centralized `CombatEngine.TryStunMonster(Monster, int)` helper that gates every stun application.** All 14+ stun-application call sites (Magician/Sage Stun and Web spells, Cleric Web, Wavecaller Temporal Paradox, Cyclebreaker Temporal Prison, Bard Dissonant Wave, Bard Legendary, Ranger Legendary Shot, Lightning enchant proc both single + multi-monster, Assassin Nightshade Extract / Widows Kiss poisons, Sage holy-pacify spell-convert, generic ability "stun" effect) now route through this single helper. The helper enforces:

- **Refuse if already stunned in any system.** Checks `IsStunned`, `Stunned`, AND `StunRounds > 0`. No more refresh-stack onto an active stun. The player has to wait for the current stun to expire before attempting another.
- **Refuse during post-recovery immunity.** `StunImmunityRounds` is now set to `GameConfig.StunImmunityRoundsAfterRecovery = 3` (was hardcoded `1`) when a stun expires. With the longer window, the monster always gets at least one free round to act between consecutive stuns.
- **Boss/mini-boss flat resist + duration cap.** Bosses and mini-bosses get a 50% flat resist roll BEFORE the stun is applied (`GameConfig.BossStunResistChance`). On a successful application, duration is capped at `GameConfig.MaxStunDurationBoss = 1` regardless of source. Old God bosses are now stuns-resistant by design rather than perma-stunnable.
- **Diminishing returns on consecutive stuns.** New `Monster.RecentStunCount` field; first stun = 100% duration, second = 50%, third = 25%, fourth attempt within the DR window = outright immune. The DR window (`GameConfig.StunDRWindowRounds = 5` rounds without a stun) lets `RecentStunCount` decay back toward zero, so a stun-resistant boss eventually becomes vulnerable again later in a long fight rather than staying permanently DR-immune after one good chain.
- **Hard duration cap.** Normal monsters cap at `GameConfig.MaxStunDurationNormal = 3` rounds. No matter what an ability claims, no stun lasts longer than that.

`ProcessMonsterAction`'s tick logic was also fixed: `StunImmunityRounds` and the new DR decay (`RoundsSinceLastStun`) tick on every monster turn (alive, not stunned), not only when a stun expires. The pre-fix flow exited early on stun-expire and skipped the immunity tick on the same turn, which is why a 1-round immunity often acted like zero.

**Net effect.** A Magician with a Lightning Bardiche fighting a regular dungeon monster: first stun lands at full duration, second stun (if attempted within DR window) lands at half, third at quarter, fourth fizzles until the window resets. The same Magician fighting an Old God: 50% chance the first stun is resisted outright, 1-round cap if it lands, full DR thereafter. Stun is still useful as a "buy a free round" mechanic; it is no longer a fight-skipping cheese.

**Files modified:**
- `Scripts/Core/Monster.cs` -- new `RecentStunCount` and `RoundsSinceLastStun` fields.
- `Scripts/Core/GameConfig.cs` -- new `StunImmunityRoundsAfterRecovery`, `StunDRWindowRounds`, `MaxStunDurationNormal`, `MaxStunDurationBoss`, `BossStunResistChance` constants.
- `Scripts/Systems/CombatEngine.cs` -- new `TryStunMonster` helper, refactored 14+ stun-application sites, updated `ProcessMonsterAction` tick logic.

## Issue triage pass (issues #85, #86, #87, #88, #89, #91)

Six community-reported bugs from fastfinge knocked out together.

### #85 Turn Undead damaged living enemies

The Cleric Lv.6 spell "Turn Undead" used `SpecialEffect = "holy"`, which is a generic +50% bonus on top of regular spell damage. The base 35-50 damage applied to anything regardless of type, so a Cleric in a fight against orcs could cast Turn Undead and deal full damage to non-undead targets. Player expectation matches the D&D-style "Turn Undead" namesake: living enemies should be unaffected.

Fix: dedicated `SpecialEffect = "turn_undead"` routed through both single-monster and multi-monster spell paths. When the target's MonsterClass is not Undead/Demon (and `Undead == 0` for hand-tagged monsters), the spell prints "X is not undead. The holy light has no effect." and applies zero damage. Companion AI (`GetBestOffensiveSpell`) drops Turn Undead from the candidate list when no undead/demon target exists, so a Cleric companion in a fight against orcs no longer burns a turn on a guaranteed whiff. New `combat.spell_turn_undead_unaffected` loc key in all 5 languages. Spell description updated to make the restriction explicit.

### #86 Autocompare suggested weapons that were "better than my shield"

When a teammate has sword + shield, `IsDualWielding` should return false (the existing `!HasShieldEquipped` clause handles it for correctly-typed shields). But for shields that got mis-typed by an older buggy load (re-inferred as `WeaponType.Sword`, no `BlockChance`/`ShieldBonus` left), `HasShieldEquipped` reads false, `IsDualWielding` reads true, and the autocompare's dual-wield "pick the weaker slot" branch targets the OffHand. With shield stats stripped, every weapon looked like a huge upgrade vs effectively-zero defense.

Fix: defense-in-depth shield detection in `TryTeammatePickupItem`. Before treating as dual-wield, check whether the OffHand item smells like a shield via stat signals (`BlockChance > 0 || ShieldBonus > 0`) or name keywords (`ShopItemGenerator.LooksLikeShieldByName`). If yes, skip the slot-swap and keep MainHand as the comparison target. Catches both correctly-typed and mis-typed shields without touching the player-side comparison display, which already always targets MainHand for weapon drops.

### #87 Sometimes enemies disappear after combat

v0.57.12 added a kill-tracking sweep at the top of `HandleVictoryMultiMonster`: any dead monster not yet in `DefeatedMonsters` gets added before reward calculation. That fix only ran on the victory branch. If the player fled after killing some enemies, `HandlePartialVictory` read `DefeatedMonsters.Count` directly and the untracked kills were lost. Same shape if the player died with kills mixed in.

Fix: hoist the sweep to run unconditionally right before the outcome branch, so flee, victory, and death paths all see the complete kill list. Idempotent (only adds dead monsters not already tracked). Player report: "After combat, sometimes I'm told I defeated 0 enemies and got 0 XP. Or when fighting more than one enemy, the results give me rewards for one less enemy than I actually fought."

### #88 Weapon enchant procs triggered after the enemy was already dead

`CheckElementalEnchantProcs` fired Fire/Frost/Lightning/Poison/Holy/Shadow procs unconditionally after every basic attack. When the basic attack's damage already killed the target, the proc rolls still ran, dealt their bonus damage to a 0-HP corpse, and printed messages like "Flames erupt! Skeleton takes 15 damage!" on a dead body. Pure spam, no balance value.

Fix: gate the elemental proc damage/status branches on `target.HP > 0 && target.IsAlive`. Mana steal at the bottom of the same method intentionally still fires on the killing blow (it's an attacker-side resource gain that's still useful when the target dies). Same fix in the `CheckElementalEnchantProcsMonster` twin for multi-monster combat.

### #89 Church confession was free and unlimited

Confession at the church reduced 5-10 darkness and granted 2-5 chivalry per use, with no cost and no limit. Confession at the temple costs gold for darkness cleansing; the church should have an equivalent friction. Player suggestion was "a cost (maybe in penance?)".

Fix: penance gold cost added to `ProcessConfession`, scaling with the player's darkness so the price tracks the benefit. New constants `GameConfig.ConfessionBaseCost = 100` and `ConfessionCostPerDarkness = 5`. 50 darkness = 350g, 200 darkness = 1100g, 1000 darkness (cap) = 5100g. Confession refuses if the player can't afford it. Three new loc keys (`church.confess_penance_cost`, `church.confess_no_gold`, `church.confess_penance_paid`) in all 5 languages. Existing blood-absolution flow (which already had a gold cost) is unchanged.

### #91 Damage immunities by monster type

Player request: angels immune to holy damage and Syphon (mana steal), demons immune to shadow damage and Lifedrinker (equipment lifesteal). The codebase already does name-based type detection for the +2x holy bonus on undead, so the new immunities follow the same pattern.

Fix: new helpers `IsAngelMonster` (matches Angel/Archangel/Seraph/Throne/Empyrean/Cherub/Celestial in the name) and `IsDemonMonster` (matches Demon/Devil/Imp/Archfiend/Hellspawn/Fiend, plus the existing `MonsterClass.Demon` enum). Holy enchant proc skips with "Divine light washes over X, who absorbs it harmlessly" when target is angelic. Shadow enchant proc skips with "Shadow tendrils slide off X's infernal hide" when target is demonic. Mana steal silently fizzles on angels (they carry no mana). Lifedrinker silently fizzles on demons (no mortal life force to drain). Two new loc keys in all 5 languages.

**Files modified across all six fixes:**
- `Scripts/Systems/CombatEngine.cs` -- Turn Undead damage gate (single + multi-monster), companion AI Turn Undead filter, autocompare shield detection, kill-tracking sweep hoisted, post-death enchant gate (both procs methods), angel/demon helpers, holy/shadow/siphon/lifedrinker immunity gates.
- `Scripts/Systems/SpellSystem.cs` -- Turn Undead `SpecialEffect = "turn_undead"`, description text update.
- `Scripts/Locations/ChurchLocation.cs` -- penance cost, gold check, deduction, paid-message.
- `Scripts/Core/GameConfig.cs` -- `ConfessionBaseCost`, `ConfessionCostPerDarkness` constants.
- `Localization/{en,es,fr,hu,it}.json` -- six new keys (`combat.spell_turn_undead_unaffected`, `church.confess_penance_cost`, `church.confess_no_gold`, `church.confess_penance_paid`, `combat.enchant_holy_immune_angel`, `combat.enchant_shadow_immune_demon`).

## Idle disconnect warning now beeps (issue #90)

GitHub issue #90 from fastfinge: "Sometimes I get distracted. Because work. Or meetings. Or whatever. And I have to minimize the window. … Would it be possible to add a 'beep!' sound that plays when the 'two minute warning' idle disconnection message plays?"

The MUD server's idle watchdog at [MudServer.cs:1014](Scripts/Server/MudServer.cs#L1014) was firing the warning text fine but as a silent yellow line -- invisible to anyone with the window minimized.

Fix: emit an ASCII BEL character (`\x07`) before the warning text and emit a structured `sound` event for the Electron client. The BEL is universal across every client we ship to -- xterm, WezTerm, Mudlet, TinTin++, MUSHclient, VIP Mud, NVDA-paired clients, web terminal (xterm.js with audible-bell on), SyncTERM/NetRunner, and the bundled `Play.bat` desktop terminal all honor it. Two BELs because some terminals coalesce a single bell into a tick that's easy to miss while alt-tabbed; two reliably gets noticed without crossing into obnoxious.

Implementation routes through `Terminal.WriteRawAnsi("\x07\x07")` so the byte passes through the existing plain-text / CP437 / ANSI handling untouched (`ToPlainText` strips ANSI escapes and markup tags but explicitly leaves control characters like BEL alone -- important for screen-reader MUD clients that translate BEL to system sound or NVDA speech feedback). For the Electron graphical client, `ElectronBridge.EmitSound("sfx.idle_warning", channel: "ui")` fires; the actual `.ogg` asset is part of the Phase 9.5 audio scaffolding backlog and the JS side gracefully ignores unknown soundIds, so this is safe to ship before the audio file lands.

**Files modified:**
- `Scripts/Server/MudServer.cs` -- idle-warning block now emits `\x07\x07` via `WriteRawAnsi` and an Electron `sfx.idle_warning` sound event (when `GameConfig.ElectronMode`).

## Auto-detect host screen reader on Windows (issue #92)

GitHub issue #92 from fastfinge: blind Steam players might not realize they need to set the `--screen-reader` launch option (via Steam launch options or by picking `Play-Accessible.bat`) to enable screen-reader mode at the title screen and character creation. The accessible launcher exists, but discovering it requires a sighted helper or a hint in the Steam description.

Fix: on Windows, auto-detect a running screen reader at startup using the `SPI_GETSCREENREADER` system parameter (NVDA, JAWS, Narrator, and most other Windows screen readers set this flag while running -- it's the documented Microsoft accessibility API for this purpose). When detected and no explicit `--screen-reader` flag was passed, the game enables screen-reader mode automatically and prints a one-line announcement so the user knows what happened and how to opt out via in-game preferences.

**Implementation:**

- New `Scripts/UI/AccessibilityDetection.cs` -- single-method helper with the `[DllImport("user32.dll")] SystemParametersInfo(SPI_GETSCREENREADER, 0, out bool, 0)` P/Invoke. Returns `false` on macOS / Linux / containers without `user32.dll` so the game stays portable. Any P/Invoke exception is treated as "no screen reader detected" -- the explicit flag still works as the fallback.
- `Console/Bootstrap/Program.cs` -- auto-detect runs *after* `DoorMode.ParseCommandLineArgs` (so `--screen-reader` short-circuits the check via `if (!GameConfig.ScreenReaderMode && ...)`) and *before* the door-mode vs standard-console-mode branch dispatches. Both Steam paths (`Play.bat` → WezTerm → `--local` → door-mode-with-CommType.Local AND any direct standard-mode launch) trigger the check.
- Skipped for non-local door connections: real BBS doors (the host's screen reader is irrelevant to the remote user, and BBS clients negotiate accessibility via the existing TTYPE probe in `MudServer`), MUD server / MUD relay / world-sim modes (headless services). Detection runs for `--local`, no-args, and pure standard-console launches.
- Saved characters' explicit `ScreenReaderMode` preference still wins on character load via `GameConfig.ScreenReaderMode = player.ScreenReaderMode` at `GameEngine.LoadSaveByFileName` -- this only sets the pre-character-load default and the inheritance value for fresh characters at creation time.

After the fix, a blind player who installs the game on Windows and launches it via the Steam shortcut sees:

```
Screen reader detected -- accessible mode enabled automatically.
(Disable in-game via the Preferences menu if you don't want this.)
```

The main menu, character creation, and all subsequent screens are then accessible without any launch-option configuration. Sighted players whose screen-reader software was running for an unrelated reason can flip the toggle off in-game.

macOS (VoiceOver) and Linux (Orca) don't expose an equivalent system-wide flag, so users on those platforms still need the `--screen-reader` flag or the in-game preferences toggle. The Steam blind-gaming audience is overwhelmingly Windows so this fix covers the high-value target; cross-platform detection can land in a follow-up release if requested.

**Files added:**
- `Scripts/UI/AccessibilityDetection.cs` -- Windows SPI_GETSCREENREADER P/Invoke wrapper.

**Files modified:**
- `Console/Bootstrap/Program.cs` -- auto-detect call between argument parsing and dispatch, with announcement to stdout when SR is enabled by detection rather than the explicit flag.

## Stun (and other control effects) now actually disable party members

Player report: "Stun only works on enemies. A party member afflicted with stun can act next round as though nothing happened."

Confirmed and broader than just stun. Both teammate action paths -- `ProcessTeammateAction` (single-monster) at [CombatEngine.cs:5684](Scripts/Systems/CombatEngine.cs#L5684) and `ProcessTeammateActionMultiMonster` at [CombatEngine.cs:16070](Scripts/Systems/CombatEngine.cs#L16070) -- only had `if (!teammate.IsAlive) return;` at the top. They never:

1. Called `teammate.ProcessStatusEffects()` to tick status durations or apply DoT damage (poison, bleed, burn).
2. Checked `teammate.CanAct()` for `PreventsAction` statuses (Stun / Paralyze / Sleep / Freeze).

So when a monster ability landed a Stun on a party member via the path at [CombatEngine.cs:17325](Scripts/Systems/CombatEngine.cs#L17325) (`companion.ApplyStatus(abilityResult.InflictStatus, abilityResult.StatusDuration)`), the entry went into `ActiveStatuses[StatusEffect.Stunned]` and just sat there forever. The teammate's next turn ran the full healing → spell → ability → basic-attack chain like nothing happened, AND because the duration was never decremented, the status entry remained for the rest of combat (it eventually got blown away by the next combat-init reset, which is why nobody noticed the duration leak earlier).

Same gap covered Sleep (via spells like Magician's Sleep), Frozen (frost spells), and Paralyze. The player path has had this guarded since combat was first written -- `player.ProcessStatusEffects()` ticks at [line 1654](Scripts/Systems/CombatEngine.cs#L1654), `player.CanAct()` checks at [line 1660](Scripts/Systems/CombatEngine.cs#L1660), and the `HasStatus(Stunned)` skip prints "you are stunned and cannot act!" at [line 1670](Scripts/Systems/CombatEngine.cs#L1670). Teammates skipped the whole thing.

Fix: both teammate methods now mirror the player path -- `ProcessStatusEffects()` first (so DoTs land and durations tick), then `IsAlive` re-check (in case the DoT killed the teammate), then `CanAct()` guard with a "{Name} is stunned and cannot act!" line that bails out of the action. New `combat.teammate_status_prevented` localization key with `{0}` name + `{1}` status placeholders added in all 5 language files.

After the fix: a Mind Flayer's stun on Aldric actually skips Aldric's turn and the duration ticks down so it expires correctly. Sleep, Freeze, and Paralyze on teammates work the same way. Ongoing poison/bleed ticks now also fire on teammates each round (previously they were silently invisible -- applied by status apply, but the per-round damage hook never ran).

**Files modified:**
- `Scripts/Systems/CombatEngine.cs` -- `ProcessTeammateAction` and `ProcessTeammateActionMultiMonster` now tick `ProcessStatusEffects` and guard on `CanAct` at the top of each method.
- `Localization/{en,es,fr,hu,it}.json` -- new `combat.teammate_status_prevented` key.

## Church donation chivalry display matched the cap

Player report (the reporter, Lv.91 Mutant Assassin): "you cap chivalry at 1000 yet, when I go to the church, I donated 100,000 gold and it says I gained 9090 chivalry -- that's a crazy amount."

`ChurchLocation.ProcessDonation` at [line 418](Scripts/Locations/ChurchLocation.cs#L418) used the original Pascal formula `chivalryGain = Math.Max(1, amount / 11)` and then displayed that raw number in the post-donation message. The `Character.Chivalry` setter has clamped to `GameConfig.AlignmentCap = 1000` since v0.57.12 so the actual stat couldn't go past the cap, but the message stubbornly displayed the uncapped 9090. A player at 950 chivalry donating 100,000g actually gained +50 (clamp), but the screen read "+9090". A player already at 1000 chivalry donating ANY amount gained 0 but the screen still said "+9090". The same shape was sitting in `ProcessBlessingPurchase` at [line 502](Scripts/Locations/ChurchLocation.cs#L502) using the `amount / 15` formula.

The v0.57.12 alignment audit explicitly left church donation/blessing/confession as direct mutations rather than routing through `AlignmentSystem.ChangeAlignment` because they have asymmetric design (donation: small chivalry boost + flat -1 darkness; blessing: bigger chivalry + proportional darkness reduction) that paired-movement's 50% rule would flatten. That decision still stands -- the bug here is purely the display side.

**Fix:**
- Both `ProcessDonation` and `ProcessBlessingPurchase` now snapshot `Chivalry` and `Darkness` before mutation, apply the existing formulas (still goes through the v0.57.12 setter clamp), and compute `actualChivalryGain` and `actualDarknessReduction` from the before/after snapshot. The post-action message displays the actual deltas -- so the player sees the real change to their stat.
- New `church.donate_chivalry_capped` flavor line added in all 5 language files for the case where the player donates while already at chivalry cap (clarifies that the donation provided no virtue benefit instead of just printing "+0 chivalry").
- Blessing path mirrors the same pattern, reading the actual darkness reduction rather than the requested amount so the line matches reality if the player started below the requested reduction.

After the fix: at 950 chivalry, donating 100,000g shows "+50 chivalry" (the actual delta). At 1000 chivalry, donating shows the new "your virtue could not climb higher" line. Donations still work as before -- the gold sink is unchanged, only the displayed numbers now match what landed.

**Files modified:**
- `Scripts/Locations/ChurchLocation.cs` -- `ProcessDonation` and `ProcessBlessingPurchase` snapshot before mutation, display post-clamp deltas, emit `donate_chivalry_capped` line when already at cap.
- `Localization/{en,es,fr,hu,it}.json` -- new `church.donate_chivalry_capped` key.

## Wave Echo no longer silently fails on debuffed targets at high level

Player report (the reporter, Lv.55 Wavecaller, floor 54): "I wonder if Wave Echo is working properly. There used to be a message the damage was doubled on a weakened target, usually after I cast Siren's Lament. Now there is no such message."

Wave Echo is a Wavecaller level-5 ability with `BaseDamage = 100, SpecialEffect = "double_vs_debuffed"`. The ability description promises double damage if the target is debuffed (poisoned, weakened, stunned, etc).

`Type = AbilityType.Attack` abilities run damage in two phases:
1. The base-damage block ([CombatEngine.cs:12263-12404](Scripts/Systems/CombatEngine.cs#L12263-L12404) for multi-monster, mirror in single-monster) applies `abilityResult.Damage` with stat scaling, crit roll, and defense reduction, then deducts HP and emits the standard "you deal X damage" line.
2. A post-damage `switch (abilityResult.SpecialEffect)` block at [line 13565](Scripts/Systems/CombatEngine.cs#L13565) (and twin at [line 20764](Scripts/Systems/CombatEngine.cs#L20764)) had `case "double_vs_debuffed"` adding *more* damage and printing the resonance flavor message -- but the case was guarded by `if (target != null && target.IsAlive)`.

At low levels the base damage doesn't kill the monster, the post-damage handler runs, the resonance message prints. The reporter saw this fine in the early game. By Lv.55 the base damage already kills floor-54 monsters in one hit, `target.IsAlive == false` skips the entire case, and the resonance message is gone -- even though the player has the buff prerequisite (Siren's Lament weaken applied) and the ability is supposedly proccing.

Same bug-class as v0.57.2's Shadow Harvest fix (separate post-damage handler that double-applied damage and silently skipped when the first hit killed the monster). The fix pattern is identical -- inline the modifier into the base-damage branch like Backstab and Shadow Harvest.

**Fix:**
- New `else if (abilityResult.SpecialEffect == "double_vs_debuffed" && target != null)` branch added to the base-damage block in both [`ApplyAbilityEffectsMultiMonster`](Scripts/Systems/CombatEngine.cs#L12298-L12321) and [`ApplyAbilityEffects`](Scripts/Systems/CombatEngine.cs#L19689-L19708). Checks the same 12 debuff conditions, doubles `actualDamage` if any are active, and emits one of two new flavor lines (`combat.wave_echo_resonates` for the doubled hit, `combat.wave_echo_strikes` for the standard hit). The doubling now flows through the rest of the base-damage pipeline (crit roll, defense reduction, HP deduction, post-hit enchantments) and the standard "you deal X damage" line reports the *correct* doubled total.
- The post-damage `case "double_vs_debuffed"` in both switches becomes a no-op so the ability doesn't double-dip on damage. The case stays so unrouted ability code paths don't warn.
- New keys `combat.wave_echo_strikes` and `combat.wave_echo_resonates` added to all 5 language files (`en.json`, `es.json`, `fr.json`, `hu.json`, `it.json`).

After the fix: Cast Siren's Lament → enemies have `WeakenRounds > 0` → cast Wave Echo → resonance message prints + damage actually doubled. The "RESONATES with X's weakness -- DAMAGE DOUBLED!" line shows reliably from level 5 to 100 regardless of whether the base hit one-shots the target.

**Files modified:**
- `Scripts/Systems/CombatEngine.cs` -- inline `double_vs_debuffed` branches in both ability paths, post-damage switch cases reduced to no-op.
- `Localization/{en,es,fr,hu,it}.json` -- new `combat.wave_echo_strikes` and `combat.wave_echo_resonates` keys.

## Newborn / renamed children no longer disappear in online mode

Player report (Lumina, Lv.53 Wavecaller): "I renamed a child and it disappeared. I should have 5 children, but only 4 are present. The child was newly born and renaming was the first thing I did."

The same architectural class as the marriage cross-player contamination bug fixed in v0.52.12. `FamilySystem.Instance` is a process-wide singleton holding every child every player has -- births, renames, orphan moves, and adult conversion all mutate this one shared list. WorldSimService's tick is the only authoritative writer of `world_state["children"]` (the durable copy that survives MUD server restarts). Player sessions read from world_state on login but used to *also* write the singleton from their own stale player-save children list inside `SaveSystem.RestoreStorySystems` at line 2461 -- `FamilySystem.Instance.DeserializeChildren(data.Children)` clears `_children` and replaces it with the logging-in player's view.

Cross-player race that broke Lumina's newborn:

1. Lumina has a baby in IntimacySystem (`RegisterChild` adds it to the singleton). Singleton has 5 of her children.
2. Lumina renames the newborn from the Home parenting menu (`HomeLocation.cs:1965` mutates `selectedChild.Name`).
3. WorldSim has not ticked yet so `world_state["children"]` still reflects the pre-baby state with 4 of her children.
4. Another player logs in. Their `LoadSaveByFileName` runs `RestoreStorySystems` which calls `DeserializeChildren` with that player's stale save snapshot -- the singleton is wiped and replaced with the second player's view, which does not contain Lumina's newborn (the new player went offline before the baby was born).
5. WorldSim's next tick fires while the singleton is in this contaminated state. `SaveChildrenState()` reads the singleton (which now has no record of Lumina's newborn) and overwrites `world_state["children"]`. The newborn and the rename are gone permanently.
6. Lumina's session continues. The singleton no longer has her newborn (it was clobbered in step 4 and re-loaded from the stale world_state in step 5). The Home parenting menu shows 4.

The marriage equivalent of this bug was already fixed by skipping `RestoreMarriages`/`RestoreAffairs` in online mode (see `LoadSharedChildrenAndMarriages` comment at GameEngine.cs:3786). Children needed the same treatment.

**Three-part fix:**

1. **Skip `DeserializeChildren` in online mode during `RestoreStorySystems`.** `LoadSharedChildrenAndMarriages` runs immediately after `RestoreStorySystems` in the online-mode load path (GameEngine.cs:2797-2801 and 4275-4282) and reloads from world_state, so the singleton still ends up with the authoritative children list -- the player's stale save copy is just no longer allowed to clobber the singleton in the brief window before that reload. Single-player mode is untouched (`!IsOnlineMode` falls through to the original `DeserializeChildren` call).

2. **Persist births to world_state immediately.** `OnlineStateManager.SaveSharedChildrenNow()` (new public method) writes the same wrapper schema WorldSimService uses (`childrenRaw` + dashboard summary). Fire-and-forget invocation added to `IntimacySystem.cs` right after the player names the newborn. The baby is durable across an unexpected MUD restart instead of living only in the singleton until WorldSim's next tick.

3. **Persist renames to world_state immediately.** Same `SaveSharedChildrenNow()` fire-and-forget call added to the rename branch in `HomeLocation.InteractWithChild` (after the `selectedChild.Name = newFirst + surname` mutation).

Existing affected players: Lumina's missing newborn cannot be recovered from world_state because WorldSim already overwrote the durable copy with the contaminated singleton. Sysop intervention via `OnlineAdminConsole` would be needed to reconstruct the child manually. Going forward, any newborn or rename is durable from the moment of the action, and concurrent logins cannot wipe in-flight children.

**Files modified:**
- `Scripts/Systems/SaveSystem.cs` -- `RestoreStorySystems` now skips `FamilySystem.DeserializeChildren` when `DoorMode.IsOnlineMode`. Comment block explains the cross-player contamination class and points at the marriage-skip equivalent.
- `Scripts/Systems/OnlineStateManager.cs` -- new public `SaveSharedChildrenNow()` method that writes the world_state children blob with the same `childrenRaw` + dashboard summary schema WorldSimService uses, callable from any player session.
- `Scripts/Systems/IntimacySystem.cs` -- birth-time naming flow now fires `_ = OnlineStateManager.Instance.SaveSharedChildrenNow()` immediately after the player names the newborn, so a fresh baby is durable to world_state without waiting for the WorldSim tick.
- `Scripts/Locations/HomeLocation.cs` -- `InteractWithChild` rename branch fires the same fire-and-forget save, so renames don't depend on a WorldSim tick to persist.

## Server-wipe button (Danger Zone, admin dashboard)

New "Wipe Server" button at the bottom of the admin dashboard (`/admin.html`) for the May 1 beta launch. Multi-step typed-confirmation flow ("WIPE THE WORLD" exactly), 5-second cancel-able countdown after the typed confirmation, calls `/api/admin/nuke` which spawns the server-side nuke script via NOPASSWD `sudo`. Script:

1. Backs up the live DB to `/var/usurper/nuke-backups/pre-nuke-{timestamp}.db` (kept indefinitely)
2. Stops `sshd-usurper` and `usurper-mud` services
3. Wipes all gameplay tables (`players`, `world_state`, `news`, `messages`, guilds, teams, auctions, world bosses, sieges, deleted-character archive, audit log, etc.) inside a single transaction
4. Resets the `peak_online` row in `admin_config` without dropping the table
5. Runs `VACUUM` to reclaim space
6. Restarts both services
7. Logs every step to `/var/log/usurper-nuke/nuke-{timestamp}.log` (survives the wipe -- the in-DB `admin_commands` audit table is itself one of the things wiped)

**Preserved:** `balance_config` (admin password hash), `dashboard_users` (admin login records), `dashboard_sessions` (so the operator stays logged in after pressing the button). Schema preserved (DELETE FROM, not DROP).

**Server-side install (one-time, before May 1):**

```bash
# Copy script + sudoers drop-in to the AWS server
scp -i "C:/Users/jason/.ssh/usurper.pem" \
    scripts-server/nuke-server.sh \
    scripts-server/sudoers-usurper-nuke \
    ubuntu@3.150.229.205:/tmp/

# Install on server
ssh -i "C:/Users/jason/.ssh/usurper.pem" ubuntu@3.150.229.205 "
  sudo mkdir -p /opt/usurper/scripts &&
  sudo cp /tmp/nuke-server.sh /opt/usurper/scripts/nuke-server.sh &&
  sudo chown root:root /opt/usurper/scripts/nuke-server.sh &&
  sudo chmod 0755 /opt/usurper/scripts/nuke-server.sh &&
  sudo cp /tmp/sudoers-usurper-nuke /etc/sudoers.d/usurper-nuke &&
  sudo chown root:root /etc/sudoers.d/usurper-nuke &&
  sudo chmod 0440 /etc/sudoers.d/usurper-nuke &&
  sudo visudo -c -f /etc/sudoers.d/usurper-nuke &&
  rm /tmp/nuke-server.sh /tmp/sudoers-usurper-nuke
"
```

After install, deploy the updated `web/ssh-proxy.js` (which contains the `/api/admin/nuke` endpoint) and `web/admin.html` (which contains the Danger Zone section). Restart `usurper-web`.

**Recovery if the wipe was a mistake:** stop services, copy the most recent file from `/var/usurper/nuke-backups/` over `/var/usurper/usurper_online.db`, restart services. The backup files are atomic SQLite snapshots (`.backup` not `cp`), so they're safe to restore as-is.

**Files added:**
- `scripts-server/nuke-server.sh` -- root-run wipe script (~85 lines, audit-logged, idempotent)
- `scripts-server/sudoers-usurper-nuke` -- sudoers drop-in granting `usurper` user NOPASSWD access to ONLY the nuke script

**Files modified:**
- `web/ssh-proxy.js` -- new `POST /api/admin/nuke` endpoint with admin-token auth, 5-min-per-IP rate limit, `confirmPhrase` validation, 90-second timeout, structured stdout/stderr capture, in-process state reset (peak online stats) on success
- `web/admin.html` -- new Danger Zone section + 4-step nuke confirmation modal (overview → typed phrase → countdown → result)

## Deploy notes

- **Game binary + new SQLite table.** The `deleted_characters` table is created via `CREATE TABLE IF NOT EXISTS` on first server startup post-deploy. No manual migration required.
- **Online server:** publish linux-x64, tar, scp, restart `usurper-mud` and `sshd-usurper`. Standard recipe.
- **Beta wipe:** the database wipe at beta launch is unrelated to this release; statues survive because they're hardcoded in the binary, not stored in the database.
- **Non-MUD clients** (web, SSH, BBS, Electron, single-player) are entirely unaffected by GMCP -- only path they exercise is the constant-time `bool` check in `GmcpBridge.IsActive` and `GmcpBridge.Emit`'s early return.

For testing post-deploy:
- Mudlet: connect to `play.usurper-reborn.net:4000`, open Settings → Mapper → check that `Char.Vitals` shows up in the GMCP debug output. Build a status pane wired to `gmcp.Char.Vitals.hp` / `.maxHp`.
- TinTin++: `#config GMCP ON`, `#config DEBUG TELNET 5` to see incoming GMCP frames, then `#split` to enable a status pane fed by `#gmcp` event handlers.
- MUSHclient: enable Plugins → GMCP, drop in any Achaea-compatible vitals plugin.
- Founder statues: walk into Castle, Temple, or type `/founders` from any location.
- Delete safeguard + restore: delete a test character at Main Street, observe the new warning text, then run `/restore` to bring it back.
- French translation: switch in-game language to French via `/settings lang fr` (Electron) or in-game preferences (terminal); verify accents render correctly in your SSH client.
