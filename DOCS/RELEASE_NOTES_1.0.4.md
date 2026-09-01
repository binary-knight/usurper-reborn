# Usurper Reborn v1.0.4

Fixes six player-reported bugs, one of them the first half of GitHub issue
#115, and makes NPC names unique for the life of a world.

## Party XP split kept resetting

Reported: "The game keeps resetting the exp distribution and I can't figure out
the pattern." A player who chose an even split found ten rooms later that their
character had been taking 100% of the XP with the party getting nothing.

The stored split was being overwritten by combat itself, in three ways. Any
fight with no eligible teammate present (the arena, a street encounter, a solo
dungeon fight, an all-dead party) set the player to 100% and every other slot to
0. A dead teammate's share was moved onto the player permanently, so a revived
teammate stayed at 0. And the even split was stored as fixed numbers for that
day's party size, so a fourth recruit joined at 0%. Once the "player has set a
split" flag was on, whatever the mutation left behind was honoured forever. The
arena already cloned and restored the split around its fight, which was a patch
over one symptom of exactly this.

Combat no longer writes the setting at all. The XP screen stores your intent:
either an even split, which is recomputed every fight from whoever is alive in
the party, or custom percentages, which are applied to the party present with a
dead member's share going to you or split among the survivors depending on the
existing auto-redistribute toggle. The screen shows which mode you are in.

**One-time recovery.** If your split was already mangled by the old behaviour
you are sitting on an explicit 100/0/0/0, which the game cannot tell apart from
a deliberate keep-all-XP choice. Open the XP screen and press `[E]` once.

Saves that set a split before v0.57.2 introduced the explicit flag will read as
even mode until a slot is edited.

## High-Low dice could take you to negative gold

Reported at almost minus one million gold: "if you don't have enough to double
on a bet, you shouldn't be able to."

The stake was never taken from your purse when the game started. A win held the
winnings in a pot rather than paying them, and a loss after a double-or-nothing
subtracted the whole pot, stake plus winnings you had never received, from real
gold. Bet everything, win twice, lose once, and you were at minus 2.24 times
your purse.

The stake now leaves the purse once, before the first roll, and the pot rides as
house money. A loss or a forfeited guess costs nothing further, collecting pays
the pot, and a tie returns whatever was riding. The most any run can cost is the
stake, so no affordability check on doubling is needed and gold cannot go
negative. A tie after doubling used to return nothing at all, for the same
reason; it now returns the pot.

The fix prevents recurrence only. If you are already below zero, ask an admin
for a gold correction.

## A dead spouse came back as a stranger who still loved you

Reported online: "My wife died in the dungeon. She wasn't resurrectable. But
another npc appeared with the same name and the same level of love for me."

Two defects. Relationship records are keyed by display name, and every lookup
ignored the NPC id the record already stores. The most likely path, which the
report's wording fits but the log tail does not show directly: the spouse
permanently died, her name became available again once the corpse was pruned,
the immigrant name pool was small enough to hand it out ("Lucinda Copperfield
VII" appears in live logs, which proves the pool was too small), and the
newcomer inherited the whole record, which bereavement had left at Love. The
marriage itself had been correctly ended, hence "same love, not married."

Separately, the v1.0.2 login heals that retire a dead partner tested the flag
that every death sets and the ten-minute respawn clears. Logging back in inside
that window widowed you, and your spouse then respawned as a stranger.

Relationship lookups now honour the stored NPC id, so a namesake starts as a
stranger and the same NPC keeps their history. Partners are retired only on a
permanent or old-age death, or when they are missing from a settled roster.

## NPC names never repeat

Follows from the above. A name is now reserved for the life of the world the
moment any NPC, child, or royal orphan receives it, and the reservation survives
corpse pruning and server restarts: it is saved with the world in single player
and in the shared world state online. Every generator consults it: immigrants,
NPC children, graduating children, adopted orphans, the new-world history
simulation, and mod-loaded NPCs. Immigrant name pools were doubled so the
generator can pick a fresh name instead of stacking numerals. When a pool is
genuinely exhausted a numeral is still appended, and that suffixed name is
reserved too. The child-name migration that runs on load used to strip numerals
and would have undone this; it now leaves a correctly-surnamed name alone.

Existing duplicated names on the live server stay as they are.

The registry covers NPCs that exist in the world. Encounter-only characters
(street thieves, bounty hunters, guards, mercenaries, pets, companions) never
enter the roster and draw from separate pools. Player character names are not
consulted.

## Catching the dungeon pixie

Reported in Italian: pressing S at the pixie encounter was read as "leave her
alone." The menu offers `[C]` catch and `[L]` leave, and every other key fell
into "leave." Italian confirmations use S/N throughout the game, so S meant
"yes." The prompt now re-asks on any key other than C or L.

## /who was English only

Reported by a Hungarian player. The whole `/who` screen was hard-coded. The
header, empty state, summary counts, title prefix, screen-reader tag, and tip
line are localized in all five languages. Class abbreviations and wizard and god
rank names stay as fixed codes; neither is localized anywhere else in the game.
Hungarian titles take no article.

## Login typing was invisible on direct connections (issue #115)

The server tells direct telnet and MUD-client connections that it will echo, so
compliant clients stop echoing locally. In-game input on those connections has
been server-echoed since v0.47.1, but the login menu was not, so players typed
blind until they were in. Login echo had been added in v0.52.4 and removed the
next day as "unnecessary now that the newline handling is correct." That was a
misdiagnosis; the newline bug was real but fixing it did not restore local echo.

Login input now echoes, passwords as `*`, unless the client explicitly refuses
server echo. A client that ignores the negotiation was already showing the
password in plain text at this prompt; the mask is real only for clients that
honour it. SSH, browser, and BBS-gateway logins never see this screen.

Not adopted from the same report: defaulting unknown terminals to CP437, which
would send single-byte box glyphs to every UTF-8 client, and shrinking some
boxes to 78 columns, which would leave mixed widths across the 113 lines of
full-width box art the game draws. The double-spacing claim needs a named terminal to
reproduce; see the issue.

## Housekeeping

- Local agent configuration under `.codex/` is no longer tracked.

## Known issues

- Naming a child after a retired NPC name gives the child a numeral suffix with
  no explanation on screen.
- The `/who` summary line is assembled from fragments, so translators cannot
  reorder its clauses.
- The 80-column double-spacing report in issue #115 is open pending a
  reproduction.

## Tests

958 passing, up from 929 at the branch point. New coverage: relationship
namesake and login-heal cases (`RelationshipNamesakeTests`, additions to
`RomanceDeadPartnerHealingTests`), the name registry and child-name migration
(`NPCNameRegistryTests`), and the party XP resolver (`TeamXPSharesTests`, with
the two v0.65.3 reclaim tests in `CharacterTests` ported to the resolver). The
pixie prompt, the dice loop, and the login gate are terminal-driven and have no
input seam; they were verified by inspection and the full suite.

## Files Changed

**New**

- `DOCS/RELEASE_NOTES_1.0.4.md`
- `DOCS/STEAM_RELEASE_NOTES_1.0.4.txt`
- `Scripts/Systems/NPCNameRegistry.cs`
- `Tests/NPCNameRegistryTests.cs`
- `Tests/RelationshipNamesakeTests.cs`
- `Tests/TeamXPSharesTests.cs`

**Modified**

- `.gitignore`
- `Localization/en.json`
- `Localization/es.json`
- `Localization/fr.json`
- `Localization/hu.json`
- `Localization/it.json`
- `README.md`
- `Scripts/Core/Character.cs`
- `Scripts/Core/GameConfig.cs`
- `Scripts/Core/GameEngine.cs`
- `Scripts/Locations/CastleLocation.cs`
- `Scripts/Locations/DormitoryLocation.cs`
- `Scripts/Locations/DungeonLocation.cs`
- `Scripts/Locations/InnLocation.cs`
- `Scripts/Server/MudChatSystem.cs`
- `Scripts/Server/MudServer.cs`
- `Scripts/Systems/CombatEngine.cs`
- `Scripts/Systems/FamilySystem.cs`
- `Scripts/Systems/IntimacySystem.cs`
- `Scripts/Systems/NPCSpawnSystem.cs`
- `Scripts/Systems/RelationshipSystem.cs`
- `Scripts/Systems/RomanceTracker.cs`
- `Scripts/Systems/SaveDataStructures.cs`
- `Scripts/Systems/SaveSystem.cs`
- `Scripts/Systems/WorldInitializerSystem.cs`
- `Scripts/Systems/WorldSimService.cs`
- `Scripts/Systems/WorldSimulator.cs`
- `Tests/CharacterTests.cs`
- `Tests/RomanceDeadPartnerHealingTests.cs`

**Removed from tracking**

- `.codex/agents/combat-reviewer.toml`
- `.codex/agents/game-designer.toml`
- `.codex/agents/loc-bug-triage.toml`
- `.codex/agents/npc-system-reviewer.toml`
- `.codex/agents/relationship-system-reviewer.toml`
- `.codex/agents/save-state-reviewer.toml`
