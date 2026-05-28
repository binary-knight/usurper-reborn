# v0.62.0 -- Light and Dark

The release is named for its headline: a **Light & Dark alignment pass** that makes both the Evil and the virtuous paths visibly worth committing to (a player reported that being Evil felt like all cost and no reward). It also consolidates everything authored after v0.61.7: the **Dungeon Discoveries** redesign, the **completion of the Hungarian localization** (every remaining player-facing surface translated, plus Spanish/French/Italian), and a batch of online and quality-of-life fixes -- an opt-in **auto-look** screen-refresh, a **fatigue readout** on the character sheet, **dungeon co-presence** suppression, and clearer **permadeath** wording. (Internal note: the 0.61.8 number was skipped; this work all ships as 0.62.0.)

## Dungeon Discoveries

A full redesign of the dungeon "Examine Features" system. The old system rolled one of eight generic outcomes (gold / XP / a stat check / minor damage / a heal) regardless of what you were actually looking at, so a "demonic altar," a "burial urn," and a "crystal cluster" all played out identically. With only about 29 feature names and ~73 flavor fragments spread across thousands of interactions, it was repetitive and forgettable, and the floors from 66 to 100 shared a pool of just three generic features.

This release replaces that with a data-driven Discovery system: the thing you examine IS the encounter, and each one is its own short scripted beat with an outcome that fits it.

## What changed

**96 hand-authored discoveries across all eight dungeon themes** (11 to 13 per theme), depth-gated so a floor-3 room and a floor-95 room feel nothing alike. Each is one of:

- **A choice with real stakes** -- pry the reliquary open for gold and risk the finger-bone trap, or whisper the old rite and take the relic freely; take the demon altar's power at a cost to your soul, or spit on it and walk away cleaner. Most choices trade power, gold, alignment, mercy, and risk against each other.
- **A skill test** that fits the object -- force a frozen door (Strength), slip coins from an ice reliquary without cracking it (Dexterity), reason out an ancient mechanism (Intelligence), attune to a singing crystal (Wisdom), endure the crushing pressure of the deep (Constitution).
- **A risk gamble** -- reach into the flooded grate where there is coin and also teeth; temper your gear in a dead forge that might sear your hand.
- **A trap with character** -- a breath of stasis that freezes your blood, a floor of black ice, a vent that exhales furnace-heat.
- **A narrative reveal** -- lore, a half-remembered past life, an Ocean-philosophy insight.

**One-time set-pieces.** Every theme has standout discoveries that fire only once per character, so they stay memorable rather than becoming farmable: the oldest grave in the catacombs (which you may have dug yourself, in another life), a master smith's hammer left "for whoever the Cycle sends next," the moment in the void where you stop being a wave fighting the sea and remember you are the sea. These grant lasting rewards (a permanent stat, deep lore, an awakening), recorded on your character so they never repeat. The everyday discoveries stay repeatable as you explore.

**The floors 66 to 100 gap is closed.** Frozen Depths, Volcanic Pit, and Abyssal Void now have full, theme-appropriate content instead of three generic features.

**Fully localized.** Every line of the new content is translated into Hungarian, Spanish, French, and Italian, alongside the mechanical text. The Old Gods and companions keep their names; the Ocean, the Cycle, and the Wave read naturally in each language.

Under the hood this is a moddable, data-driven catalog (consistent with the game's other moddable data), so the discovery set can be extended without engine changes. A `--export-discoveries` developer flag dumps the catalog's text for translation.

## Files changed

- `Scripts/Core/GameConfig.cs` -- version 0.61.7 -> 0.62.0; VersionName -> "Light and Dark".
- `Scripts/Data/DiscoveryData.cs` (new) -- the Discovery schema (`DiscoveryDefinition`, `DiscOutcome`, `DiscChoice`, `DiscEffect` with terse builder helpers) and `DiscoveryCatalog` (96 discoveries across all 8 themes, weighted, floor-gated, with one-time set-pieces; English text as source/fallback).
- `Scripts/Systems/DiscoverySystem.cs` (new) -- the interpreter: renders a discovery's intro/choice/skill-test/risk/trap, applies its effects through the same Character APIs the old system used, returns a `FeatureOutcome` so the existing party reward-split keeps working. Resolves every text slot via a deterministic `discovery.{id}.*` loc key with English-from-code fallback. Includes `ExportLocKeys()` for the export flag.
- `Scripts/Systems/DungeonGenerator.cs` -- `RoomFeature.DiscoveryId`; `GenerateRoomFeatures(theme, type, level)` now draws ~70% from the catalog (theme + floor gated, weighted, unique per room) and falls back to the legacy theme features as filler.
- `Scripts/Locations/DungeonLocation.cs` -- the examine path routes catalog discoveries to `DiscoverySystem`, shows a localized name + one-line teaser, hides one-time discoveries already found, and records one-time discoveries after they run; legacy features still use the old `FeatureInteractionSystem`.
- `Scripts/Core/Character.cs` -- `DiscoveredFeatureIds` (HashSet<string>) of one-time discoveries found.
- `Scripts/Systems/SaveDataStructures.cs` / `Scripts/Systems/SaveSystem.cs` / `Scripts/Core/GameEngine.cs` -- full save round-trip for `DiscoveredFeatureIds` (List<string> in the DTO, HashSet on the character; pre-v0.62.0 saves default to empty; online persists it in the same PlayerData blob).
- `Console/Bootstrap/Program.cs` -- new `--export-discoveries [path]` developer flag that emits the catalog's loc source keys (English) for translation.
- `Localization/*.json` -- 615 new `discovery.*` keys per language (582 per-discovery flavor + 33 shared mechanic/effect/stat keys) in all 5 languages; arg parity and no-em-dash validated; hu/es/fr/it translated via parallel agents with accent self-checks.

## Fix (same-day): discovery boons and afflictions now persist and are visible

A first-play report found that boons and afflictions from discoveries did not seem to do anything. Cause: they were applied to per-combat fields that the next fight's setup resets -- combat buffs went into `TempAttackBonus`/`TempDefenseBonus` (zeroed at combat start), and afflictions went into the transient `ActiveStatuses` dict (not shown on `/health`, and the control statuses Frozen/Stunned are scrubbed at combat start). So a boon you earned examining a feature was wiped before your next swing, and an affliction was invisible. Fix: discovery combat boons now route through the `WellRested` buff vehicle (a percent bonus to attack and defense for the next few fights that survives combat-start, ticks down per fight, persists in the save, and shows on `/health` as "Well-Rested"); and `/health` now lists lingering afflictions (the damage-over-time ones already carried into the next fight, they just weren't visible). New `status.*` and `base.lingering_affliction*` loc keys in all 5 languages (which also localize the affliction line shown on examine).

- `Scripts/Systems/DiscoverySystem.cs` -- `GrantCombatBlessing` routes TempAtk/TempDef boons through `WellRestedCombats`/`WellRestedBonus` (Math.Max so it never downgrades an inn-rested buff) instead of the combat-start-reset `TempAttackBonus`.
- `Scripts/Locations/BaseLocation.cs` -- `/health` now shows a "Lingering Afflictions" section listing active negative combat statuses with remaining duration.
- `Localization/*.json` -- 15 new keys per language (`base.lingering_afflictions`, `base.lingering_affliction_line`, and 13 `status.*` affliction names; the status keys also localize the on-examine affliction message).

## Fix (same-day): the Examine action no longer appears when there is nothing to examine

A player reported that `[X] Examine features` sometimes showed in a room but did nothing when pressed. Cause: the room-menu gates checked only "any feature not yet interacted with," while the examine flow also hides one-time discoveries the character has already found -- so a room whose only remaining feature was an already-found one-time discovery offered the action but then found nothing to show. The menu gates, the "you notice" line, and the input handler now all use one shared `ExaminableFeatures` check that matches the examine flow exactly, so `[X]` only appears when there is genuinely something to examine. (Also routed the "you notice" feature names through the localized name resolver so they read in the player's language.)

- `Scripts/Locations/DungeonLocation.cs` -- new `ExaminableFeatures`/`HasExaminableFeatures` (single source of truth: not-yet-interacted AND not an already-found one-time discovery); all room-menu gates (visual, screen-reader, BBS), the "you notice" display, and the `[X]` input handler routed through it; `ExamineFeatures` uses it too.

## Same-day Hungarian localization batch (12 surfaces, 298 keys x5 languages)

A player working through a Hungarian session reported a dozen surfaces still rendering in English. All localized into Hungarian (and Spanish, French, and Italian), adding 298 new keys per language. The headline item is monster combat-ability messages, which were entirely hardcoded English.

**Monster ability combat messages.** Every monster special-ability line ("breathes a cone of fire!", "delivers a CRUSHING BLOW!", "drains your life force!", and ~110 more) was hardcoded English in `MonsterAbilities.cs` and now resolves through the localization system. Messages that name a target are split into a "vs you" variant and a "vs an ally" variant, so each language can phrase the player case and the companion case naturally (the monster name and any ally name always sit in a position that reads correctly, which matters a great deal for Hungarian grammar -- case-endings/suffixes depend on the word's vowels, so inserted variables are kept in suffix-free positions).

**Mysterious-stranger encounter names and descriptions.** The titles and one-line descriptions of the hooded traveler, old beggar, quiet patron, and the stranger's other disguises now translate.

**Player-home upgrade menu.** The bonus descriptions ("50% rest, 3x/day", "+5 all stats", etc.), the Training Room label, and the special-purchase names and descriptions (Trophy Room, Study/Library, Servants' Quarters, Reinforced Door, Legendary Armory, Fountain of Vitality) now translate.

**Town settlement (the Outskirts).** Building names and descriptions, construction-tier labels, the "X has been upgraded" announcements, the proposal/vote news lines, the service-menu entries, and the NPC-proposed building names/descriptions/effects (Arena, Thieves' Den, Mystic Circle, and the others) now translate.

**Logout forecast.** The "Tomorrow's Forecast" teaser shown on quit now translates.

**Smaller surfaces.** The partner-time menu's status tag ("spouse"/"lover"), the dungeon party-roster lines (the "(You)" / "Level" / "[Player]" / "[Companion]" / "[Ally]" markers), and dungeon feature reward notifications ("+50 experience", "+N gold", "+N HP") now translate.

**Location name in the on-screen header.** The location title at the top of each screen (and the breadcrumb fallback) rendered each location's hardcoded English `Name` field; it now uses the localized `GetLocationName(LocationId)` (the `location.name.*` keys), so the header reads in the player's language. The Dungeons header keeps its raw name because it carries the floor number.

**Permadeath disconnect wording.** The permadeath disconnect line read "*** You have been permadied. ***"; reworded to "You have permanently died." and localized (`permadeath.disconnect_msg`, 5 langs) -- it was a hardcoded English literal passed to `DisconnectAsync`.

**Three quick Hungarian corrections from the same report:** the dungeon location name now reads "Kazamatak", a typo in the monster-hit damage line is fixed, and the beggar-encounter "give gold" option now shows the correct hotkey letter (G, matching the code).

- `Scripts/Systems/MonsterAbilities.cs` -- all ~120 `result.Message` ability strings routed through `Loc.Get("mability.*", ...)`; target-referencing abilities branch into `.you` / `.ally` keys on `isPlayerTarget`; added `using UsurperRemake.Systems`.
- `Scripts/Locations/BaseLocation.cs` -- stranger-encounter name/description rendered via `stranger.disguise.{disguise}.name|desc` keyed on the disguise enum; location header (`DisplayLocation`, both single-player and online paths) and breadcrumb default now use `GetLocationName(LocationId)` instead of the raw English `Name` field (Dungeons header keeps `Name` for the floor number).
- `Scripts/Locations/HomeLocation.cs` -- partner-time status tag localized; home-upgrade bonus strings, Training Room label, and all six special-purchase names/descriptions routed through `home.*` keys (display + purchase-confirmation paths).
- `Scripts/Locations/DungeonLocation.cs` -- party-roster lines (player, member, member-short, typed) and the player/companion/ally tags routed through `dungeon.party_*` keys.
- `Scripts/Systems/FeatureInteractionSystem.cs` -- feature reward gold/experience/HP notifications routed through `feature.reward_*` keys.
- `Scripts/Locations/MainStreetLocation.cs` -- `ShowTomorrowForecast` header, context lines, and generic-forecast pool routed through `forecast.*` keys.
- `Scripts/Systems/SettlementSystem.cs` -- `GetBuildingDisplayName` / `GetBuildingDescription` / `GetTierDisplayName` resolve `settlement.building.*` / `settlement.tier.*`; construction + proposal + settler-joined news lines routed through `settlement.news_*`; core and proposed service-menu labels routed through `settlement.svc.*` / `settlement.psvc.*`; `ProposalTemplate` gains `LocName` / `LocDescription` / `LocEffectDescription` accessors resolving `settlement.pbuilding.{id}.*` with English-field fallback.
- `Scripts/Locations/SettlementLocation.cs` -- all proposal-template display sites routed through the new Loc accessors.
- `Localization/*.json` -- 298 new keys per language (151 `mability.*` + 147 across stranger/home/dungeon/feature/forecast/settlement) in all 5 languages; en + hu authored directly, es/fr/it via parallel translation agents (all self-validated: 298/298 present, arg parity, no em-dash, accents intact). Loc-integrity audit clean across all 5 languages.

## Addition: Fatigue on the Character Status sheet (single-player)

The `[%]` Character Status screen now shows a Fatigue line in the COMBAT STATISTICS section ("Fatigue: <tier> (N/100)"), so the fatigue level is visible from the stat sheet rather than only flashing in the location header when Tired/Exhausted. Single-player only -- online mode doesn't use fatigue, so the line is gated on `!IsOnlineMode` and never appears online. The four fatigue tier labels (Well-Rested / Normal / Tired / Exhausted) were also localized into all 5 languages and `Character.GetFatigueTier()` now returns localized labels, which incidentally localizes the existing location-header and `/health` fatigue readouts (they were hardcoded English).

- `Scripts/Core/Character.cs` -- `GetFatigueTier()` returns localized labels via `status.fatigue_rested|tired|exhausted`.
- `Scripts/Locations/BaseLocation.cs` -- `ShowStatus()` COMBAT STATISTICS section adds a single-player-gated Fatigue line (uses the existing `status.fatigue` label + a `status.fatigue_normal` fallback for the no-display Normal band).
- `Localization/*.json` -- 4 new `status.fatigue_*` tier-label keys per language.

## Dockerfile fix (self-hosters): Localization files missing from the container

The Docker build stage copied `Scripts/`, `Console/`, `Data/`, `Assets/`, and `app.ico` but never `Localization/`, so the published container image shipped without language files and every string rendered as a raw key (e.g. `engine.menu_story_full`). The `.csproj` already copies `Localization\*.json` into the publish output; the build context just never included the directory. Added `COPY Localization/ Localization/` after `COPY Assets/ Assets/`. (`.dockerignore` does not exclude it; the AWS server deploys from a tarball rather than the Dockerfile, which is why it never surfaced server-side.)

- `Dockerfile` -- added `COPY Localization/ Localization/` to the build stage.

## Addition: Auto-look (online auto-redraw setting)

Single-player redraws the full location screen after every action; online/MUD mode deliberately streams continuously and waits for you to type `look`. A player coming from single-player missed the auto-refresh, so there's now an opt-in **auto-look** setting that makes online mode redraw the location screen after each action, single-player style. **Default off** (MUD purists and existing players are unaffected); new online characters are **prompted at creation** to enable it or leave it off. Toggle anytime in Preferences (`L`, online only) or with `/autolook`. When on, `ClearScreen` emits a real ANSI clear for capable clients (SSH, web terminal); plain-text / raw-MUD / screen-reader sessions keep their scroll buffer untouched. Single-player ignores the setting (it already redraws every loop). New `Character.AutoLook` round-trips through the save (default off on pre-existing saves).

- `Scripts/Core/Character.cs` / `Scripts/Systems/SaveDataStructures.cs` / `Scripts/Systems/SaveSystem.cs` / `Scripts/Core/GameEngine.cs` -- `AutoLook` field + full save round-trip + login sync to `GameConfig.AutoLook`.
- `Scripts/Server/SessionContext.cs` / `Scripts/Core/GameConfig.cs` -- per-session `AutoLook` (SessionContext-backed `GameConfig.AutoLook`, mirroring `CompactMode`).
- `Scripts/Locations/BaseLocation.cs` -- `LocationLoop` redraw gate honors `AutoLook` in MUD mode; Preferences `L` toggle (online-only) + `/autolook` command + help entries.
- `Scripts/UI/TerminalEmulator.cs` -- `ClearScreen` emits a real ANSI clear in MUD mode when `AutoLook` is on and the client isn't plain-text.
- `Scripts/Systems/CharacterCreationSystem.cs` -- new online characters are prompted to enable auto-look (default off; accepts localized yes: Y/I/S/O).
- `Localization/*.json` -- 13 new `prefs.auto_look` / `base.*autolook*` / `creation.autolook_*` keys per language.

## Fix: "Also here" co-presence hidden in the Dungeons

The online "Also here: X" co-presence line (other players at the same location) showed in the Dungeons, where it's misleading -- each player explores their own floors/instance, so two players both "in the Dungeons" aren't actually together. Both display sites (location-entry `RoomRegistry` lookup and the in-loop co-presence cache) plus the cache refresh now skip `GameLocation.Dungeons`. Town locations (genuinely shared) are unchanged.

- `Scripts/Locations/BaseLocation.cs` -- the two `base.also_here` display sites and the co-presence cache refresh are gated on `LocationId != GameLocation.Dungeons`.

## Feature: Alignment payoff pass (player feedback -- "being Evil isn't worth it")

A Steam player reported that the Evil/Darkness path felt like all cost (attacked everywhere, barred from temples) and no reward, that they couldn't leave the Evil faction, and that they wanted a freelance/mercenary option without swearing loyalty. A design pass (the new `game-designer` agent) found the root cause was largely a **visibility** problem -- alignment already drove combat, prices, reactions, and access, but the game never told the player -- plus a couple of genuine gaps. First slice of the fix (Light and Dark designed as mirrored yin/yang):

- **Reputation readout.** The `[%]` Character Status "Alignment & Reputation" section now spells out what your alignment actually *does* -- combat attack/defense modifiers, black-market vs honest-shop pricing, how the virtuous and the wicked react to you, wanted status, holy-ground access, and your faction + rank -- for both the Dark and Light poles. The benefits were always there; now they're visible.
- **You can leave a faction.** `FactionSystem.LeaveFaction()` existed but was never reachable; the Dark Alley's Shadows menu now lets you renounce your allegiance (any faction) with its real consequence (-500 standing, branded a traitor).
- **The named alignment "powers" are now real.** They were display-only text. Implemented as alignment-tier passives that reward committing to a path: Dark gets **Soul Drain** (heal a share of melee damage, 3%->6%), **Terror Incarnate / Fear Aura** (5%->10% chance to rout a foe), and **Shadow Strike** (+3->+5 crit); Light gets the mirror -- **Blessed Aura** (regen 2%->4% MaxHP/round), **Holy Smite** (+5%->+10% damage vs undead/demonic foes), and **Righteous Fury** (+3->+5 crit). Dark Pact (+20% attack / -10% defense) and Divine Protection (+10% defense) were already live in combat -- the readout just surfaces them now.

Numbers are playtest starting points. Soul Drain shares the existing total-lifesteal budget (no stacking abuse), Terror skips bosses and champions, Holy Smite can't touch Old Gods (they aren't class-flagged undead/demon), and the crit bonuses ride the existing DEX-scaled cap. Combat-reviewed clean. Still to come (later slices): Dread/Renown notoriety ladders, gambling/black-market depth, a Light activity hub, and a mercenary job board.

- `Scripts/Systems/AlignmentSystem.cs` -- magnitude helpers (`GetAlignmentCritBonus`, `GetSoulDrainPercent`, `GetFearOnHitChance`, `GetBlessedRegenPercent`, `GetHolySmiteBonus`).
- `Scripts/Systems/StatEffectsSystem.cs` -- alignment crit bonus in `RollCriticalHit` (capped).
- `Scripts/Systems/CombatEngine.cs` -- Soul Drain + Terror + Holy Smite in the shared `ApplyPostHitEnchantments` (player-gated); Blessed Aura in the shared per-round regen hook.
- `Scripts/Locations/BaseLocation.cs` -- alignment-effects readout in the `[%]` status screen.
- `Scripts/Locations/DarkAlleyLocation.cs` -- renounce-allegiance flow in `ShowShadowsRecruitment`.
- `Localization/*.json` -- 24 new keys per language (`reputation.*`, `faction.name_*`, `dark_alley.faction_*`, `combat.soul_drain|terror_incarnate|holy_smite_passive|blessed_aura`).
- `.claude/agents/game-designer.md` (new) -- reusable creative-design subagent.
