# Release Notes - v0.65.4 (Countdown)

Sixth release of the Beta -> 1.0 "Countdown" cycle. A player-experience release aimed squarely at the two worst numbers in the live data: 25% of created characters never get a first kill, and 43% of all characters stall at levels 1-3. Two design passes (one on UI/onboarding, one on character progression) independently concluded the top-ROI fixes were about visibility, not new content: the onboarding guidance all stopped at the dungeon door, and progression rewards were invisible (the game never announced an ability unlock, never showed what was coming next, and never displayed XP progress). This release surfaces all of it, and adds targeted progression content (player specializations, martial ability fills, early class tuning).

## "The First Kill": guide new players to their first fight and reward it

The single biggest funnel leak is the gap between creating a character and killing something. Every hint, banner, and journal prompt lived on Main Street; the moment a fresh character walked into the dungeon (entrance room, no monster, four unlabeled exits) they were on their own. Four changes close the loop so the whole progression cycle demonstrates itself in the first fight:

- **Guidance follows you into the dungeon.** For a level-1 character with no kills, the room now prints one diegetic line pointing at the nearest fight: "A growl echoes from the east -- your first fight waits 2 rooms away." It uses the existing map pathfinding and only fires until the first kill.
- **The first kill guarantees level 2.** Level 2 needs only a little XP; the lifetime-first kill now tops it up (stacking on the existing "First Blood" gold bonus), so kill number one always produces a level up.
- **Level-ups announce what you unlocked and what's next.** New abilities used to appear silently in the quickbar. The level-up now prints "NEW ABILITY: Battle Cry" and "Next: Thundering Roar at level 20", on both the automatic level-up and the manual Level Master path.
- **A dungeon-hint wipe bug is fixed**, and a new `entered_dungeon` telemetry event splits the funnel so we can tell whether lost players never entered the dungeon or entered and wandered.

## The Path Ahead: progression made visible

The mid-game "desert" turned out to be class-specific: casters get a new spell every ~4 levels the whole game, while martials have 12-15 level ability droughts in the 28-60 band. The highest-value fix is making the ladder visible so every wait becomes a countdown:

- **New "The Path Ahead" screen** (`/path`, `/roadmap`, and `[P] Path` at the Level Master): shows your level and XP to next, your next three ability/spell unlocks, and the next story gate (Old God / seal floor).
- **Ambient XP in the dungeon status line** so every fight visibly counts toward the next level (Main Street already showed it).
- **Journal next-step at login**: returning players used to land in the news feed with no reminder of what they were doing. After the "while you were gone" report, the game now shows the journal's next step (the pull-based `/journal` isn't discoverable to a returning player).

## Player specializations (level 25)

NPC teammates have had 24 class specializations for a while; players never could. Now they can. From level 25, `[S] Specialize` at the Level Master lets you pick one of your class's two specializations (Arms/Protection, Retribution/Holy, and so on). A specialization adds additive stat growth on future level-ups and turns on the spec's combat passives (for example, Protection Warriors get +10% damage reduction on Shield Wall Formation plus the +20% shield-equipped ability damage NPC spec-tanks already had; healer specs get +20% healing output; Guardians get the +15% shield-equipped heal bonus). The first pick is free; changing it later costs a gold respec. Growth applies to future level-ups only -- choosing a spec doesn't retroactively re-grant past levels. Landing the choice at level 25 (the first Old God floor) frames it as "you survived the first god; choose who you are."

Both reviewers ran on this feature. save-state-reviewer: round-trip complete on single-player and online (the online save rides the same player blob), pre-0.65.4 saves load as unspecialized, no cross-player contamination, death-level-loss exactly reverses spec growth (apply/reverse symmetric). combat-reviewer: no double-application (single/multi path parity holds), None-spec players unaffected everywhere; its one finding (two shield spec bonuses still NPC-gated, leaving player spec-tanks weaker than NPC ones) was fixed in-release for full parity.

## Progression tuning

- **Barbarian gets a level-1 active ("Wild Swing").** It was the only class with no level-1 ability -- its first was Rage at 5 and its first sustain not until level 36 -- and it died more than any class. Wild Swing pairs with the 0.65.3 innate heal-on-kill (kill faster, heal sooner).
- **Two Warrior ability fills** close the worst martial droughts: Crushing Blow (level 34, fills 28-40) and Bulwark (level 62, a defensive stance, fills 55-70).
- **Bard's Song of Rest moves from level 12 to 18.** Bard had a full party toolkit by level 14 while other classes were still finding their feet (0 Bard deaths in telemetry). This evens the early curve without touching Bard's signature abilities.
- **Training screen honesty.** The training menu now shows what a point actually buys -- the effect multiplier, hit modifier, and (restored after being hidden in v0.49.9) the fizzle chance -- for the current and next proficiency tier, so the auto-attack-vs-abilities choice is legible instead of a hidden trap.

## Smaller UI wins

- **Combat "?N" ability info**: type `?3` in combat to see what quickbar slot 3 does (name + description) without spending your turn. The quickbar shows names and costs but never said what an ability does.
- **The next-step ticker now shows on BBS/compact terminals too** (it previously lived only in the visual Main Street body, past the early return, so door-game and compact-mode players got no onboarding guidance).
- **The Settlement (The Outskirts) finally explains itself**: a one-time first-visit note on what it is and that donations unlock services you can use, plus a line at the donation prompt showing what the currently-funded building will give you. (Player feedback: "why would I donate gold to something I don't understand?")
- **One fewer key-press wall** in the new-character opening: the generic "Getting Started" screen no longer stacks in front of the Captain Aldric quest screen (which already says what to do) for new non-NG+ characters.

## Companion quest chains: "The Long Road"

The largest content addition of the release, driven directly by player feedback: "the concept is cool but it feels empty... a whole lot of nothing until [the final quests] -- I'd rather see a full quest chain that starts much earlier and builds from there." Every recruitable companion (Aldric, Vex, Lyris, Mira, Melodia) now has a three-beat personal story chain: two new earlier episodes that fire in the dungeon and lead into their existing final quest (which is untouched -- it remains the payoff).

Each beat is a scene: some open with a real fight (a scavenger looting a King's Guard corpse, a corrupted temple healer, a thing that eats sound), all of them crack the companion's mask a little further, and each ends with a three-way choice in how you respond -- your answer shapes their loyalty and the tone of what follows. The beats reveal, ahead of the finals: the name of the demon Aldric won't say (Malachar), the truth about Vex's illness and the bucket list he's kept since he was twelve, what actually happened to Lyris's Deepwood (and why the last seed of the mother-tree stirs toward YOU), the doctrine that turned Veloura's healers into reapers before Mira's floor-40 reckoning, and the fact that Melodia's Lost Opus is not lost but unfinished -- a funeral she never got to complete.

Between beats, companions carry the story with them: new idle lines echo what happened ("Vex has the list out again, muttering about the spelling"), a nudge line telegraphs when the next episode is near, and a town notice fires so players who park a companion know their story moved. Beats can never be missed by out-leveling -- each fires on any floor at or past its band, in order -- and at most one beat plays per companion per dungeon visit so a catch-up player gets pacing, not a backlog dump.

Two structural fixes ride along: **Vex's bucket list now unlocks the moment his second beat completes** (it is, narratively, him deciding to start the list) instead of waiting for floor 70 or the unreliable day counter -- most players will now actually see his story; and beat rewards are small but real (loyalty, a one-time gold trick from Vex, healing herbs from Lyris, a full heal from Mira, a free War March from Melodia, and quarter-scale companion stat bumps mirroring the finals' pattern), all one-shot and unfarmable.

Design notes: beats deliberately stop firing once that companion's final quest completes (a setup scene after the payoff reads as a bug); Mira's second beat has a past-tense variant if you've already resolved Veloura; beat fights are ordinary level-appropriate monsters through real combat (fleeing simply defers the scene); nothing touches the sacrifice, grief, or romance systems. New save field `PersonalQuestBeat` round-trips through both companion save DTOs with a test locking the contract.

## Deferred (documented, not this release)
- **Gear set bonuses** (a new save-backed set-detection + bonus + display system) and **faction-locked vendor gear**: the design pass recommended deferring the set-bonus system until the mid-game progression work lands (sets mostly pay off at endgame, and 43% of players stall at level 1-3). Faction gear needs new balance-relevant item definitions and vendor wiring; rather than half-ship it into an already-large release, both are held for a focused future gear slice.
- **Electron graphical-client ticker render**: the terminal paths carry the next-step ticker now; wiring the JS side to render it in the status pane is a follow-up.

## Files Changed
- `Scripts/Core/GameConfig.cs` -- Version 0.65.4; new `SpecializationUnlockLevel`/`SpecializationRespecCost` constants
- `Scripts/Systems/ProgressionRoadmap.cs` -- (new) read-only progression lookups (all ability/spell unlocks by level, next unlocks, next story gate) powering the level-up announcement and the Path screen
- `Scripts/Locations/DungeonLocation.cs` -- first-fight guidance (`TryFindNearestFightDirection` + `ShowFirstFightGuidance`, wired into visual + BBS room views); un-wipe first-dungeon hint; `entered_dungeon` telemetry; ambient XP in `ShowQuickStatus`
- `Scripts/Server/SessionContext.cs` -- `OnboardingDungeonRecorded` session gate
- `Scripts/Systems/CombatEngine.cs` -- first-kill guaranteed level 2 (`ShowFirstKillBonus`); `TryShowQuickbarInfo` (`?N` info command, both combat paths) + quickbar hint; player-specialization combat gates relaxed from `player is NPC` to `player.Specialization` (tank passives in single + multi paths, `ApplyHealerSpecBonus`, `IsHealerClass`)
- `Scripts/Locations/BaseLocation.cs` -- level-up unlock announcements (auto path); `ShowProgressionRoadmap` + `/path` `/roadmap` commands
- `Scripts/Locations/LevelMasterLocation.cs` -- unlock announcements (manual celebration); `[P] Path` + `[S] Specialize` menu entries; `ShowSpecializationMenu` + `BuildSpecGrowthSummary`; spec stat growth in `ApplyClassStatIncreases`/`ReverseClassStatIncrease` now applies to players
- `Scripts/Core/Character.cs` -- `Specialization` moved here from NPC (players can specialize)
- `Scripts/Core/NPC.cs` -- `Specialization` removed (inherited from Character)
- `Scripts/Systems/SaveDataStructures.cs` / `Scripts/Systems/SaveSystem.cs` / `Scripts/Core/GameEngine.cs` -- player `Specialization` save round-trip; login journal next-step line; merged the redundant Getting Started + Aldric key-press walls
- `Scripts/Locations/MainStreetLocation.cs` -- extracted `ShowNextStepTicker` and called it on the BBS path too (render-path parity)
- `Scripts/Locations/SettlementLocation.cs` -- one-time first-visit explainer + payoff-at-donation-prompt
- `Scripts/Systems/ClassAbilitySystem.cs` -- Barbarian `wild_swing` (L1); Warrior `crushing_blow` (L34) + `bulwark` (L62); Bard `song_of_rest` 12 -> 18; shield spec damage/heal bonuses in `UseAbility` relaxed from NPC-only to any specialized character (combat-reviewer parity finding)
- `Scripts/Systems/TrainingSystem.cs` -- training screen shows effect multiplier / hit modifier / fizzle for current + next tier
- `Scripts/Systems/CompanionSystem.cs` -- companion quest chains: `PersonalQuestBeat` field on `Companion` + `CompanionSaveData` (serialize / restore / NG+ reset); `GetChainBeatMinFloor` floor-band table; `QueueChainNotification` town breadcrumb; idle echo / nudge lines in `GetCompanionIdleComment`
- `Scripts/Locations/DungeonLocation.cs` -- companion quest chains: `CheckCompanionChainBeats` dispatcher (per-visit guard, floor-band gate, 12% roll, Old-God-floor skip) wired ahead of the final-quest check in `MoveToRoom`; `RunChainBeat` scene renderer + `ApplyChainBeatReward`; `ChainBeats` beat table; Mira Beat 2 Veloura-resolved variant (`IsVelouraResolved`); Vex final-quest gate now also opens on `PersonalQuestBeat >= 2`
- `Scripts/Systems/SaveDataStructures.cs` / `Scripts/Systems/SaveSystem.cs` -- `PersonalQuestBeat` on `CompanionSaveInfo` + both companion save/restore mapping sites (single-player and online ride the same DTO)
- `Tests/SaveRoundTripTests.cs` -- player `Specialization` round-trip assertion; companion `PersonalQuestBeat` round-trip assertion (851 tests pass)
- `Localization/{en,es,fr,it,hu}.json` -- new keys: `dungeon.first_fight_guidance` + `dungeon.dir_*`; `base.new_ability_unlocked`/`base.new_spell_unlocked`/`base.next_unlock`; `combat.first_blood_level2`; `path.*` (roadmap screen); `status.xp_compact`/`status.xp_max`; `settlement.explainer_*`/`settlement.contribute_building_payoff`; `combat.quickbar_info_hint`/`combat.quickbar_info_empty`; `training.tier_effect_now`/`training.tier_effect_next`; `spec.*` (specialization UI) + `level_master.menu_path`/`path_desc`/`menu_specialize`/`specialize_desc`; 239 companion-chain keys (`cbeat.*` scenes + `companion.idle.*.chain*` continuity lines) translated to es/fr/it/hu by parallel translators
