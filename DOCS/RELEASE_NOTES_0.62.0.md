# v0.62.0 -- Light and Dark

This release is named for its headline: a **Light and Dark alignment pass** that makes both the Evil and the virtuous paths visibly worth committing to. It also consolidates everything authored after v0.61.7 -- the **Dungeon Discoveries** redesign, the **completion of Hungarian localization** (plus Spanish/French/Italian for every new surface), and a batch of online quality-of-life fixes. (Internal note: the 0.61.8 number was skipped; this work all ships as 0.62.0.)

---

## TL;DR

- **Light and Dark alignment rework** (the headline). Seven slices that turn both alignment poles into structured, rewarding playstyles -- the original Steam feedback "being Evil isn't worth it / can't leave / live as a Merc / Dark Alley is thin" is now fully addressed:
  - **Visible payoffs.** `[%]` Character Status now spells out what your alignment does in combat, prices, NPC reactions, and access. Six named alignment passives (Soul Drain / Terror / Shadow Strike for Dark, Blessed Aura / Holy Smite / Righteous Fury for Light) are real combat effects, not flavor text. You can also renounce a faction.
  - **Dread and Renown ladders.** Four-tier mirrored standing tracks (Cutthroat -> Marauder -> Terror -> Nightmare, and Defender -> Paragon -> Hero -> Legend) that escalate shop discounts and make the world react to your name -- the weak flee feared villains on sight; townsfolk cheer celebrated heroes.
  - **Reward loops.** Demand Tribute (Dark, daily-capped); named bounty hunters worth killing for guaranteed gold + loot; NPC gifts at Hero+ Renown; free daily temple blessing at Paragon+ Renown.
  - **A Mercenary career.** New `[M] Sellsword Hall` at Anchor Road -- freelance contracts from Crown / Shadows / Faith with no oath required. Five-rank ladder (Recruit -> Legend) keyed to a lifetime contracts-completed counter that survives NG+.
  - **Dark Alley depth.** Black Market rotates daily with Dread-tier-scaled gear slots; freelance evil at Marauder+ Dread can shop there without joining the Shadows (10% surcharge). The old back-fit "Loaded Dice" coin flip is replaced with **Spot the Mark**, a 3-round read-the-bias game with WIS skill checks and a real "how long do you wait before calling?" decision.
  - **The Sanctum.** New Light-pole hub off Main Street (`[+]`) with three daily-capped charity verbs that climb Chivalry + Faith faction standing, plus a statistics-driven Hall of Heroes.
  - **Tournament of Honor.** Sanctum-side mirror of the Gauntlet -- focused 3-fight ritual against named foes (a fallen knight, an undead lord, the Anonymous Champion), gated on Defender+ Renown.

- **Dungeon Discoveries.** The "Examine Features" system replaced with 96 hand-authored discoveries across all 8 dungeon themes (closes the floors 66-100 generic gap), each one a real scripted beat -- a choice with stakes, a fitting skill test, a risk gamble, a trap with character, or a narrative reveal. About 11 one-time set-pieces grant permanent stat lifts. Fully translated into HU/ES/FR/IT.

- **Localization completion.** Twelve previously-English surfaces translated: monster ability combat lines (~120 strings, the big one), stranger encounter names, home upgrade menu, settlement (town building) text, party-roster lines, dungeon feature rewards, the logout forecast, location headers, and the permadeath disconnect line. About 298 new keys per language for this batch alone.

- **Online quality of life.** Opt-in **auto-look** redraws the location screen each turn for players who prefer single-player-style refresh. **Fatigue** now shows on the `[%]` character sheet. **"Also here"** co-presence hidden in the Dungeons (each player has their own floor anyway). **Permadeath disconnect line** reworded to "You have permanently died." **Dockerfile** now includes `Localization/` (self-hosters were seeing raw keys).

---

## Headline: Light and Dark (the alignment rework)

A Steam player reported that the Evil/Darkness path felt like all cost and no reward, that they couldn't leave the Evil faction, and that they wanted a freelance/mercenary option without swearing loyalty. The diagnosis (via a new `game-designer` subagent) was that alignment already drove combat, prices, NPC reactions, and access -- the game just never told the player -- plus a couple of genuine content gaps on both poles.

Shipped as seven slices over the development window, all in this release. Designed top-to-bottom as yin/yang mirrors: every Dark mechanic has a Light counterpart that flows in the opposite direction.

### Slice 1 -- Visible payoffs

The `[%]` Character Status "Alignment and Reputation" section now spells out what your alignment actually *does*: combat attack/defense modifiers, black-market vs honest-shop pricing, how the virtuous and the wicked react to you, wanted status, holy-ground access, faction and rank. The benefits were always there; now they're visible. `FactionSystem.LeaveFaction()` existed but was never reachable; the Dark Alley's Shadows menu now lets you renounce allegiance with its real consequence (-500 standing, branded a traitor).

The named alignment "powers" became real combat effects, not display-only text:

- **Dark** -- Soul Drain (heal a share of melee damage, 3% -> 6% by alignment intensity), Terror Incarnate (5% -> 10% chance to rout a foe on hit, skips bosses and champions), Shadow Strike (+3 -> +5 crit chance).
- **Light** -- Blessed Aura (regen 2% -> 4% MaxHP/round), Holy Smite (+5% -> +10% damage vs undead/demonic foes), Righteous Fury (+3 -> +5 crit chance).

Dark Pact (+20% attack / -10% defense) and Divine Protection (+10% defense) were already live in PvE; the readout just surfaces them now. All effects respect the existing total-lifesteal budget, the DEX-scaled crit cap, and the boss-protection layer (Old Gods aren't class-flagged undead/demon, so Holy Smite can't touch them).

### Slice 2 -- Dread and Renown ladders

Two mirrored standing tracks, derived live from Darkness/Chivalry (no new save fields):

- **Dread** (Dark pole): Cutthroat 250 -> Marauder 450 -> Terror 650 -> Nightmare 800.
- **Renown** (Light pole): Defender 250 -> Paragon 450 -> Hero 650 -> Legend 800.

The `[%]` screen leads with your active standing and a one-line summary. Standing also **deepens the shop discount you already had** -- a feared Dark player intimidates shady merchants harder as Dread climbs (5% off at Cutthroat down to 20% off at Nightmare, on top of the existing alignment band); a celebrated Light player earns deeper goodwill at honest shops on the same curve. Pole-gated so Balanced line-walkers don't double-dip. And the world reacts: at Terror+ Dread, ordinary NPCs more than 5 levels below you flee on sight before you can speak (story NPCs, the king, and near-peers are exempt so nothing soft-locks); at Paragon+ Renown, townsfolk occasionally cheer or bow.

### Slice 3 -- Reward loops

Four mirrored payoff loops layered onto the ladders:

- **Demand Tribute** (Dark, Cutthroat+). New `[7]` action on the NPC interaction menu. Shake an ordinary non-story townsfolk for gold. Success scales with Dread tier (45% at Cutthroat -> 90% at Nightmare); gold scales to NPC level (NOT player wealth -- can't snowball). Daily-capped at 3 attempts. Each demand pays paired alignment (+5 Darkness / -2 Chivalry).
- **Bounty hunters worth killing** (Dark, Marauder+). 18% per encounter at Marauder+ Dread to draw a named hunter (Hex the Hound, Greymark, Black Iris, Vance the Stalker, Iron Lyra, Cold Mercy, Sable Roan, The Silent Maw, Vendrik the Patient, Ash Wren) at champion-tier stats. On victory: guaranteed gold purse + a real `LootGenerator` drop on top of FightNPC's reward. Hunters aren't `NPCSpawnSystem`-registered, so killing them isn't murder.
- **NPC gifts** (Light, Hero+ Renown). The existing Good/Holy alignment-event flow now layers in a class-appropriate loot drop at Hero+. Mirror of the Dread bounty-hunter loot drop.
- **Free temple blessing** (Light, Paragon+ Renown). A pure-of-soul player who used to hit the Church's "your soul is already pure" dead-end now gets one free `DivineBlessing` (7-day combat buff) per day instead. Daily-flagged, reset only via `ApplyDailyReset` (no relog-reroll).

### Slice 4 -- Mercenary / Sellsword job board

Closes the loop on the original "live as a Merc" feedback. A new `[M] Sellsword Hall` posting station at Anchor Road offers freelance contracts from all three factions (Crown / Shadows / Faith) **with no oath required**. Faction membership was already optional in code; this slice makes the freelance path a named, supported career.

**Rank ladder** via lifetime `MercContractsCompleted`: Recruit (1) -> Sellsword (10) -> Veteran (30, +5% pay) -> Ironbound (75, +10% pay) -> Legend (150, +15% pay). Lifetime counter, never decays, survives NG+.

**Six contracts at tier 1**, two per faction with thematic objectives via the existing Quest/QuestObjective infrastructure: Crown bandit purge + guard relief, Shadows fence run + jailbreak, Faith purge undead + escort pilgrim. Faction standing on completion routes through the existing cascade (Crown work hurts Shadows -50%, helps Faith +20%), and Faith/Shadows contracts also shift alignment by ~3 per completion through paired-movement `ChangeAlignment` so a long career drifts naturally without forcing it on the first contract.

**Anti-exploit bounds:** `MaxMercContractsPerDay = 3` (separate from `RoyQuestsToday` so dungeon + merc are parallel tracks); `MaxDailyMercStandingGain = 30` per faction (so 50 Crown contracts in a session don't immediately unlock Crown membership); gold scales on player level only.

The `[%]` reputation block also gains a "Sellsword Hall: {rank} ({count} contracts)" line whenever `MercContractsCompleted >= 1` -- the freelance path is alignment-agnostic and stacks with whatever pole the player is committed to, which is exactly correct for the yin/yang centerline.

### Slice 5 -- Dark Alley depth

Closes "gambling = 3 identical RNG games, Black Market = 3 static items."

**Black Market rebuilt.** Two sections: the original utility floor (forged papers / poison vials / smoke bombs, unchanged price math) + a new **rotating merchandise** section with gear slots scaling by Dread tier (None=0, Cutthroat=2, Marauder=3, Terror=4, Nightmare=5). Per-player daily rotation. `FactionSystem.HasBlackMarketAccess` now also admits Marauder+ Dread freelance evil without Shadows membership (with a 10% surcharge so Shadows membership stays the better deal). Layered pricing: a maxed Shadows-Nightmare player pays ~0.45x list; freelance Marauder pays ~0.85x. Inventory cap honored.

**"Spot the Mark" gambling replaces back-fit Loaded Dice.** The old game was a `45% + CHA/200` coin flip with the dice back-fit to the secretly-determined outcome (the over/under decision was a no-op). Rebuilt as a 3-round read-the-bias game: the dealer secretly biases HIGH (8-12) or LOW (2-6), 70% of rolls fall on the biased side. Each round you decide `[1] Call now` or `[2] Wait`; earlier calls pay more (3.0x / 2.0x / 1.4x) but face a harder skill check (DC 14 / 12 / 10). On call you pick a direction (HIGH/LOW) and roll `d20 + WIS/15 vs DC`. Two distinct loss flavors -- wrong direction vs failed read -- both pay nothing and grant +5 Darkness. WIS-keyed, so INT/WIS-build classes finally have a real Dark Alley use case.

### Slice 6 -- The Sanctum

The yin/yang completion. The Dark pole had a deeply structured hub (Dark Alley) since Phase 5; the Light pole had its reputation effects + a handful of one-off rewards but **no structural home**. The Sanctum is a new top-level location off Main Street (`[+]` symbol), with **inverted resource flow** vs Dark Alley -- gold flows OUT (the player spends to climb Renown), non-fungible rewards (Faith standing, alignment shifts) flow back.

**Three Acts of Mercy** at Tier 1, each daily-capped, each scaling cost on player level (NOT player wealth):

| Verb | Daily cap | Cost (per level) | Chivalry pre-DR | Faith standing |
|---|---|---|---|---|
| Alms to the poor | 3/day | level * 50 | +5 | +3 |
| Fund the Orphanage | 1/day | level * 200 | +10 | +5 |
| Hospice tithe | 1/day | level * 150 | +8 | +4 |

All Chivalry climbs route through `AlignmentSystem.ChangeAlignment` so the v0.60.0 DR curve fires; Faith climbs through `FactionSystem.ModifyReputation` so the cascade fires (Faith +X dings Shadows -X/2 -- heavy charity has a real opposing cost). Faith faction members get a 10% discount, mirror of Phase 5's Black Market Shadows-rank discount.

**Hall of Heroes** (read-only feedback wall) shows lifetime gold donated, current Renown standing, current Faith standing, and today's contribution counts. The "the world remembers you" payoff in tangible form.

Access: Holy/Good/Neutral/Balanced welcomed; Dark admitted with a cold-welcome flavor line (a Dark player washing darkness via paired-movement should be able to); Evil wards-barred at the door.

### Slice 7 -- Tournament of Honor

The Light-pole answer to Anchor Road's Gauntlet, accessible at `[T]` on the Sanctum's "Other" menu and hidden (not greyed) below Defender Renown. Deliberately shorter and more ceremonial -- where the Gauntlet is 10 waves of escalating monsters, the Tournament is a focused 3-fight ritual against named foes a Light hero would specifically be called to confront:

- **Sir Aedric the Lapsed** (HP x1.6, +3 levels) -- once-knight of Aurelion whose sister was lost to Noctura. Kit: CriticalStrike, Cleave, Regeneration.
- **Marrowking Vex** (HP x2.0, +5 levels) -- undead lord, former magistrate who sentenced a hundred to die. Kit: LifeDrain, Curse, Phylactery.
- **The Anonymous Champion** (HP x2.5, +7 levels) -- the reigning ritual winner. Never speaks. No colors. Always wins. Kit: CriticalStrike, Multiattack, Vanish.

**Zero new save fields** -- shares `ArenaChampionTier` (so a Tournament-only player can earn Gauntlet tier achievements), `GauntletRunsToday` (so it's Tournament OR Gauntlet today, not both), and `PFights`. Same quadratic entry fee (`level^2 * 10`), same 25% per-wave death-roll model, same `IsExhibitionCombat` plumbing as the Gauntlet. Each wave: per-wave gold + XP + Fame + Chivalry (5/10/15 / 10/15/20 respectively, routed through `ChangeAlignment` with before/after snapshot so the displayed delta reflects DR). Between waves: 20% MaxHP + 15% mana heal (Gauntlet parity). Full clear: bonus rewards + tier-title upgrade + tier achievement unlock.

### What still isn't built (out of scope for v0.62.0)

- **Rally-to-your-fights ally** (Renown mirror of bounty hunters) -- temp 5th-slot combat ally for a single dungeon delve. Genuinely new combat work; its own focused slice.
- **Slice 4b/5b/6b extras** -- tier 2-5 merc contracts, Legend's Pick weekly, Legend cosmetic title; Black Market gear rarity-floor; Monte/Skull gambling redesigns; Sanctum's Sponsor a Pilgrim + Bail a Debtor; Hall of Heroes NPC enrichment; Crown Commissions as a Mercenary-Hall filter.
- **Honor-flavored tier title series** for Tournament-only players.
- **`SummonMonsters` engine fix** -- pre-existing engine dead code where `MonsterAbilities` sets the flag but `CombatEngine` never reads it. Affects Old Gods + dungeon undead beyond just Vex. Marrowking Vex's kit deliberately works around it (`Phylactery` + `Curse` + `LifeDrain` instead of `SummonMinions`). A separate hotfix.

---

## Dungeon Discoveries

The old "Examine Features" system rolled one of eight generic outcomes (gold / XP / a stat check / minor damage / a heal) regardless of what you were actually looking at, so a "demonic altar," a "burial urn," and a "crystal cluster" all played out identically. With only about 29 feature names and ~73 flavor fragments spread across thousands of interactions, it was repetitive and forgettable, and floors 66-100 shared a pool of just three generic features.

Replaced with a data-driven Discovery system: the thing you examine **is** the encounter, and each one is its own short scripted beat.

**96 hand-authored discoveries across all eight dungeon themes** (11-13 per theme), depth-gated so floor-3 and floor-95 rooms feel nothing alike. Each is one of:

- **A choice with real stakes** -- pry the reliquary open for gold and risk the finger-bone trap, or whisper the old rite and take the relic freely; take the demon altar's power at a cost to your soul, or spit on it and walk away cleaner.
- **A skill test that fits the object** -- force a frozen door (Strength), slip coins from an ice reliquary without cracking it (Dexterity), reason out an ancient mechanism (Intelligence), attune to a singing crystal (Wisdom), endure the crushing pressure of the deep (Constitution).
- **A risk gamble** -- reach into the flooded grate where there is coin and also teeth.
- **A trap with character** -- a breath of stasis that freezes your blood, a vent that exhales furnace-heat.
- **A narrative reveal** -- lore, a half-remembered past life, an Ocean-philosophy insight.

**About 11 one-time set-pieces** fire only once per character so they stay memorable: the oldest grave in the catacombs (which you may have dug yourself, in another life), a master smith's hammer left "for whoever the Cycle sends next," the moment in the void where you stop being a wave fighting the sea and remember you are the sea. These grant permanent stat lifts, deep lore, or an awakening, and are recorded on your character so they never repeat.

**The floors 66-100 gap is closed.** Frozen Depths, Volcanic Pit, and Abyssal Void now have full, theme-appropriate content.

**Fully localized.** Every line is translated into Hungarian, Spanish, French, and Italian. Old Gods and companions keep their names; the Ocean, the Cycle, and the Wave read naturally in each language.

Under the hood this is a moddable, data-driven catalog (consistent with the game's other moddable data), so the discovery set can be extended without engine changes. A `--export-discoveries` developer flag dumps the catalog text for translation.

### Two same-day fixes after first play

**Boons and afflictions now persist and are visible.** First-play report found that discovery boons (next-fight attack/defense buffs) and afflictions (status DoTs) seemed to do nothing. Cause: they landed in per-combat fields the next fight's setup resets. Combat boons now route through the `WellRested` buff vehicle (survives combat-start, ticks per fight, persists in saves, shows on `/health`). `/health` now lists lingering afflictions (the DoTs already carried over -- they just weren't visible).

**The `[X] Examine` action no longer appears with nothing to examine.** Room-menu gates checked "any feature not yet interacted with," but the examine flow ALSO hides one-time discoveries already found -- so a room whose only remaining feature was an already-found one-time discovery offered the action then found nothing. Both paths now use a single `ExaminableFeatures` check, and the "you notice" line is localized.

---

## Localization completion (Hungarian + Spanish/French/Italian)

A player working through a Hungarian session reported a dozen surfaces still rendering in English. All localized into all four target languages, adding 298 new keys per language for this batch alone (on top of the 615 Discovery keys and the 240+ per-phase keys from the alignment rework, which the parallel translation agents handled inline with each slice).

The headline item is monster combat-ability messages -- every monster special-ability line ("breathes a cone of fire!", "delivers a CRUSHING BLOW!", "drains your life force!", and ~120 more) was hardcoded English in `MonsterAbilities.cs` and now resolves through the localization system. Messages that name a target are split into a "vs you" variant and a "vs an ally" variant so each language phrases the player case and the companion case naturally (Hungarian grammar in particular -- case-endings depend on the word's vowels, so inserted variables are kept in suffix-free positions).

Other surfaces brought into the localization layer this release:

- **Mysterious-stranger encounter names and descriptions.** The hooded traveler, old beggar, quiet patron, and the stranger's other disguises.
- **Player-home upgrade menu.** Bonus descriptions ("50% rest, 3x/day", "+5 all stats"), the Training Room label, and all six special-purchase names and descriptions (Trophy Room, Study/Library, Servants' Quarters, Reinforced Door, Legendary Armory, Fountain of Vitality).
- **Town settlement (the Outskirts).** Building names and descriptions, construction-tier labels, "X has been upgraded" announcements, proposal/vote news lines, service-menu entries, NPC-proposed building names/descriptions/effects (Arena, Thieves' Den, Mystic Circle, etc.).
- **Logout forecast.** The "Tomorrow's Forecast" teaser shown on quit.
- **Smaller surfaces.** Partner-time status tag (spouse/lover), dungeon party-roster lines ("(You)" / "Level" / `[Player]` / `[Companion]` / `[Ally]` markers), feature reward notifications ("+50 experience", "+N gold", "+N HP").
- **Location name in the on-screen header.** The location title at the top of each screen (and the breadcrumb fallback) rendered each location's hardcoded English `Name` field; it now uses the localized `GetLocationName(LocationId)` keys. (Dungeons header keeps its raw name because it carries the floor number.)
- **Permadeath disconnect wording.** "*** You have been permadied. ***" -> "You have permanently died." (it was a hardcoded English literal passed to `DisconnectAsync`).
- **Three Hungarian corrections** from the same report: dungeon location name reads "Kazamatak"; a typo in the monster-hit damage line fixed; beggar "give gold" option shows the correct hotkey letter (G, matching the code).

---

## Online quality of life

- **Auto-look (opt-in online auto-redraw).** Single-player redraws the full location screen after every action; online/MUD mode deliberately streams continuously and waits for `look`. New `AutoLook` setting (default OFF) makes online mode redraw single-player style. New online characters prompted at creation; toggle anytime in Preferences (`L`, online only) or `/autolook`. SSH and web terminal get a real ANSI clear when on; plain-text / raw-MUD / screen-reader sessions keep their scroll buffer untouched. Per-session, save-round-tripped.
- **Fatigue on the `[%]` Character Status sheet.** Single-player only (online doesn't use fatigue). The `Character.GetFatigueTier()` labels were also localized, which incidentally fixes the existing location-header and `/health` fatigue readouts (they were hardcoded English).
- **"Also here" co-presence hidden in the Dungeons.** Each player explores their own floors/instance, so two players both "in the Dungeons" aren't actually together -- the line was misleading. Town locations (genuinely shared) are unchanged.
- **Dockerfile fix for self-hosters.** The build stage copied `Scripts/`, `Console/`, `Data/`, `Assets/`, and `app.ico` but never `Localization/`, so the container image shipped without language files and every string rendered as a raw key. Added `COPY Localization/ Localization/` to the build stage.

---

## Same-day audit fixes

Comprehensive v0.62.0 audit at the end of the development window surfaced two real issues; both fixed in this release.

- **NG+ lifetime carryover.** `Character.MercContractsCompleted` (the freelance Sellsword rank counter) and `Character.LifetimeCharityGoldDonated` were documented (inline comments + Slice 4 and Slice 6 sections above) as "survives NG+ / never decays" -- but `CreateNewGame`'s NG+ branch built a fresh `Character` via `CreateNewPlayer`, and `ApplyCycleBonusesToNewCharacter` only touched stats/level/EXP/Gold. So on cycle 2, freelance mercs lost their entire rank ladder and the lifetime charity counter reset to 0. Fix: snapshot both values from the previous-life `currentPlayer` BEFORE creation (same seam where `previousLifeName` is already captured for the child-disown step), then restore onto the fresh character after `ApplyCycleBonusesToNewCharacter` + `RecalculateStats`. (`GameEngine.cs:4549-4570` + `:4660-4680`)
- **Holy Smite IsAlive gate.** The Light-pole alignment passive at `CombatEngine.cs:7542` gated on `target.IsAlive`. When the base hit one-shot an Undead/Demon at endgame, the IsAlive check failed and both the bonus damage AND the flavor line silently skipped -- the Light passive became invisible on kill-shots, the exact case where players most expect to see it fire. Same bug-class as Shadow Harvest (v0.57.2) / Wave Echo (v0.61.2) / Hungering Strike (v0.60.10). Fix: dropped the IsAlive clause. `Math.Max(0, target.HP - smite)` already prevents underflow on a corpse; the flavor line now fires consistently.

---

## Files changed by section

### Dungeon Discoveries

- `Scripts/Core/GameConfig.cs` -- version 0.61.7 -> 0.62.0; VersionName -> "Light and Dark".
- `Scripts/Data/DiscoveryData.cs` (new) -- 96 discoveries across 8 themes; `DiscoveryDefinition`, `DiscOutcome`, `DiscChoice`, `DiscEffect` schema.
- `Scripts/Systems/DiscoverySystem.cs` (new) -- interpreter; resolves every text slot via `discovery.{id}.*` loc key with English fallback. Includes `ExportLocKeys()`.
- `Scripts/Systems/DungeonGenerator.cs` -- `RoomFeature.DiscoveryId`; `GenerateRoomFeatures(theme, type, level)` draws ~70% from catalog (theme + floor gated, weighted, unique per room).
- `Scripts/Locations/DungeonLocation.cs` -- examine path routes catalog discoveries to `DiscoverySystem`; one-time discoveries already found are hidden; `ExaminableFeatures`/`HasExaminableFeatures` is the single source of truth for the menu gate and the `[X]` input handler.
- `Scripts/Core/Character.cs` -- `DiscoveredFeatureIds` (HashSet<string>).
- `Scripts/Systems/SaveDataStructures.cs` / `SaveSystem.cs` / `Scripts/Core/GameEngine.cs` -- save round-trip for `DiscoveredFeatureIds`.
- `Console/Bootstrap/Program.cs` -- new `--export-discoveries [path]` developer flag.
- `Scripts/Locations/BaseLocation.cs` -- `/health` lists lingering afflictions; discovery boons route through `WellRested` so they survive combat-start.
- `Localization/*.json` -- 615 new `discovery.*` keys per language (582 per-discovery + 33 shared) + 15 `status.*` / `base.lingering_affliction*` keys for the same-day fix.

### Light and Dark (alignment rework, slices 1-7)

- `Scripts/Systems/AlignmentSystem.cs` -- magnitude helpers, `DreadTier` / `RenownTier` enums, `GetDreadTier` / `GetRenownTier`, `GetDreadPriceMultiplier` / `GetRenownPriceMultiplier`, `GetNotorietyStandingLine`, tiered multiplier folded into `GetPriceModifier`, `TryGrantRenownGift` helper, `MercRank` enum + `GetMercRank` + `GetMercStandingLine`, `CanAccessLocation` extended for `GameLocation.Sanctum`.
- `Scripts/Systems/StatEffectsSystem.cs` -- alignment crit bonus in `RollCriticalHit` (capped).
- `Scripts/Systems/CombatEngine.cs` -- Soul Drain + Terror + Holy Smite in shared `ApplyPostHitEnchantments` (player-gated, IsAlive-corrected for Holy Smite); Blessed Aura in shared per-round regen hook.
- `Scripts/Systems/StreetEncounterSystem.cs` -- `EncounterType.BountyHunter`, gated pre-check, `ProcessBountyHunterEncounter`, `CreateBountyHunter` (champion-tier stats, 10-name pool).
- `Scripts/Systems/QuestSystem.cs` -- merc-board logic: `MERC_INITIATOR_*` sentinels, `MercTemplate` struct, `MercTemplatesSlice1` 6-contract pool, `RefreshMercBoard` / `CreateMercContract` / `GetAvailableMercContracts` / `GetClaimedMercContracts` / `CompleteMercContract`; `OnRoomExplored` and `OnLocationVisited` objective hooks; quest-restore handles 3 new Quest fields across all 3 restore paths.
- `Scripts/Systems/FactionSystem.cs` -- `HasBlackMarketAccess` relaxed for Marauder+ Dread freelance; new `IsBlackMarketFreelance(Character)`.
- `Scripts/Locations/DarkAlleyLocation.cs` -- renounce-allegiance flow; `VisitBlackMarket` rewrite (utility floor + Dread-scaled gear + layered pricing + refresh hint + inventory cap); `PlayLoadedDice` rebuilt as 3-round Spot the Mark with WIS skill check.
- `Scripts/Locations/AnchorRoadLocation.cs` -- `[M] Sellsword Hall` entry (all 3 display modes), `ShowSellswordHall` + `ShowMercContractDetails` + `TurnInReadyMercContracts` + `FormatMercFactionTag`.
- `Scripts/Locations/ChurchLocation.cs` -- free-Renown-blessing branch in `ProcessBlessingPurchase`, replacing the pure-of-soul dead-end at Paragon+.
- `Scripts/Locations/SanctumLocation.cs` (new) -- the Sanctum location: visual/BBS/SR display modes; three charity verbs each with daily-cap + paired `ChangeAlignment` + Faith standing cascade; Hall of Heroes; `[T]` Tournament of Honor entry hidden below Defender Renown; `StartHonorTournament` 3-wave loop with per-wave reward awards using actual-Chivalry-delta tracking, between-wave heal, victory summary with tier-upgrade + achievement unlock; `SpawnHonorChampion` helper mirroring `AnchorRoadLocation.SpawnChampionMonster`.
- `Scripts/Data/HonorTournamentData.cs` (new) -- 3 named champion definitions (Sir Aedric the Lapsed, Marrowking Vex, The Anonymous Champion) with role, multipliers, entrance theater, lore, themed drops, ability kits; `TierRewards` + `GetTierRewards`.
- `Scripts/Locations/MainStreetLocation.cs` -- new `[+] The Sanctum` entry across all 3 display modes and `case "+"` dispatch.
- `Scripts/Systems/LocationManager.cs` -- Sanctum location registration + bidirectional navigation.
- `Scripts/Locations/BaseLocation.cs` -- alignment-effects readout in `[%]`; notoriety standing line; merc standing line; automatic flee-on-sight (Dark/Evil + Terror+, weak non-story NPC) and Renown cheer/bow in `InteractWithNPC`; new `[7] Demand Tribute` NPC menu option + `DemandTribute(NPC)`; `EnterLocation` calls `QuestSystem.OnLocationVisited`.
- `Scripts/Locations/DungeonLocation.cs` -- `MoveToRoom` calls `QuestSystem.OnRoomExplored` on first-time room entry.
- `Scripts/Core/Character.cs` -- 12 new persisted fields plus `CachedBlackMarketStock` transient: Phase 3 (`TributeDemandsToday`, `FreeBlessingClaimedToday`); Phase 4 (`MercContractsCompleted`, `MercContractsClaimedToday`, `LastMercBoardRefreshUtc`, `DailyMercStandingGain`); Phase 5 (`BlackMarketStockSeed`, `LastBlackMarketRefreshUtc`, `CachedBlackMarketStock` transient); Phase 6 (`AlmsGivenToday`, `OrphanageGiftsToday`, `HospiceTithesToday`, `LifetimeCharityGoldDonated`).
- `Scripts/Core/Quest.cs` -- `IsMercContract`, `IssuingFaction` (`Faction?`), `MercContractTier` on the Quest partial.
- `Scripts/Core/GameConfig.cs` -- ~30 new constants (DreadFleeLevelGap, tribute caps + rewards, DreadBountyHunter spawn/gold, FreeBlessingMinRenownChivalry, merc caps + payouts + rank arrays, BlackMarket markup + freelance surcharge + Dread-tier slot table, Sanctum charity caps + costs + rewards + Faith-member discount).
- `Scripts/Core/GameConfig.cs:GameLocation` -- new `Sanctum = 506` enum value.
- `Scripts/Systems/SaveDataStructures.cs` -- PlayerData mirror fields for all 12 persisted Character fields + 3 Quest fields (`IssuingFaction` as int with -1 sentinel).
- `Scripts/Systems/SaveSystem.cs` -- write path for all new fields including dictionary serialization for `DailyMercStandingGain`.
- `Scripts/Systems/OnlineStateManager.cs` -- online write path for Quest fields in `SerializeCurrentQuests`.
- `Scripts/Core/GameEngine.cs` -- restore path for all 12 Character fields; **NG+ lifetime carryover** for `MercContractsCompleted` + `LifetimeCharityGoldDonated`.
- `Scripts/Systems/DailySystemManager.cs` -- daily reset for 7 daily counters; lifetime fields and wall-clock rotation timestamps deliberately untouched (with comment).
- `Scripts/Editor/PlayerSaveEditor.cs` -- "Reset daily counters" admin action mirrors `DailySystemManager`; lifetime fields deliberately untouched (with comment).
- `Localization/*.json` -- approximately 240 new keys per language across the alignment rework (24 Slice 1 reputation/passives + 18 Slice 2 Dread/Renown standing + 16 Slice 3 tribute/free blessing/bounty hunter/renown gift + 62 Slice 4 merc + 19 Slice 5 Black Market + Spot the Mark + 43 Slice 6 Sanctum + 44 Slice 7 Tournament + 14 cross-cutting `reputation.*`).
- `Tests/SaveRoundTripTests.cs` -- four new round-trip tests locking the Phase 3, Phase 4 (Character + Quest), Phase 5, and Phase 6 save contracts. 714/714 pass.
- `Tests/AlignmentSystemTests.cs` -- tier breakpoints, price-multiplier scaling, standing-line pole gating; the two `GetPriceModifier` theories updated for the tiered discount.
- `.claude/agents/game-designer.md` (new) -- reusable creative-design subagent.

### Localization completion (Hungarian batch + cross-language)

- `Scripts/Systems/MonsterAbilities.cs` -- all ~120 `result.Message` ability strings routed through `Loc.Get("mability.*", ...)`; target-referencing abilities branch into `.you` / `.ally` keys on `isPlayerTarget`.
- `Scripts/Locations/BaseLocation.cs` -- stranger-encounter name/description via `stranger.disguise.{disguise}.name|desc`; location header (single-player and online) and breadcrumb default use `GetLocationName(LocationId)` instead of the raw English `Name` field.
- `Scripts/Locations/HomeLocation.cs` -- partner-time status tag; home-upgrade bonus strings, Training Room label, and six special-purchase names/descriptions routed through `home.*` keys.
- `Scripts/Locations/DungeonLocation.cs` -- party-roster lines and player/companion/ally tags routed through `dungeon.party_*` keys.
- `Scripts/Systems/FeatureInteractionSystem.cs` -- feature reward gold/experience/HP notifications routed through `feature.reward_*` keys.
- `Scripts/Locations/MainStreetLocation.cs` -- `ShowTomorrowForecast` routed through `forecast.*` keys.
- `Scripts/Systems/SettlementSystem.cs` -- `GetBuildingDisplayName` / `GetBuildingDescription` / `GetTierDisplayName` resolve `settlement.building.*` / `settlement.tier.*`; construction + proposal + settler-joined news lines through `settlement.news_*`; service-menu labels through `settlement.svc.*` / `settlement.psvc.*`; `ProposalTemplate` gains `LocName` / `LocDescription` / `LocEffectDescription` accessors.
- `Scripts/Locations/SettlementLocation.cs` -- proposal-template display sites routed through the new accessors.
- `Localization/*.json` -- 298 new keys per language for this batch (151 `mability.*` + 147 across stranger/home/dungeon/feature/forecast/settlement); HU + EN authored directly, ES/FR/IT via 4 parallel translation agents (all self-validated: 298/298 present, arg parity, no em-dash, accents intact).

### Online quality of life

- `Scripts/Core/Character.cs` / `Scripts/Systems/SaveDataStructures.cs` / `Scripts/Systems/SaveSystem.cs` / `Scripts/Core/GameEngine.cs` -- `AutoLook` field + save round-trip + login sync to `GameConfig.AutoLook`.
- `Scripts/Server/SessionContext.cs` / `Scripts/Core/GameConfig.cs` -- per-session `AutoLook` (SessionContext-backed, mirroring `CompactMode`).
- `Scripts/Locations/BaseLocation.cs` -- `LocationLoop` redraw gate honors `AutoLook` in MUD mode; Preferences `L` toggle (online-only) + `/autolook` + help entries; `[%]` `ShowStatus()` adds single-player-gated Fatigue line; co-presence `base.also_here` display sites gated on `LocationId != GameLocation.Dungeons`.
- `Scripts/UI/TerminalEmulator.cs` -- `ClearScreen` emits real ANSI clear in MUD mode when `AutoLook` is on and the client isn't plain-text.
- `Scripts/Systems/CharacterCreationSystem.cs` -- new online characters prompted to enable auto-look (default OFF; accepts localized yes: Y/I/S/O).
- `Scripts/Core/Character.cs` -- `GetFatigueTier()` returns localized labels via `status.fatigue_*`.
- `Dockerfile` -- added `COPY Localization/ Localization/` to the build stage.
- `Localization/*.json` -- 13 new auto-look keys per language + 4 fatigue tier-label keys + 5-lang permadeath disconnect line (`permadeath.disconnect_msg`).
