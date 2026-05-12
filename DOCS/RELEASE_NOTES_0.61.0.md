# v0.61.0 -- Beta (Wilderness Reborn)

The Wilderness was the dimmest spot on the map -- combat encounters thinner than the dungeon's, foraging that copied home herb-gathering, and one-shot lore fragments with no replay value. This release turns it into a daily-essential destination with two new systems that don't exist anywhere else in the game: **Druid's Shrines** for 24-hour attunement buffs tied to the Old Gods, and **Beast Taming** for permanent wild-creature companions that include a brand-new 5th party slot for combat pets. Also folds in a small newborn-notification fix that was originally planned for v0.60.12, and patches the street-encounter NPC murder-cap bypass that surfaced just before release.

## Feature: Druid's Shrines (5 shrines, one per Old God)

Five ancient shrines stand in the wild lands, each tied to one of the surviving Old Gods whose domain bleeds out into the wilderness. Players make pilgrimage to one shrine per day and gain a 24-hour passive buff. Each visit also shifts player alignment and accumulates per-shrine "favor" that, at milestones (10/25/50 visits), unlocks unique encounters.

### The Shrines

| Shrine | Region | Passive (24h) | Alignment |
|---|---|---|---|
| Stone Circle of Terravok | Iron Mountains | +5 HP regen per combat round | Neutral |
| Broken Blade Altar of Maelketh | Iron Mountains | +8% melee damage | -2 Chivalry / +2 Darkness |
| Moonwell of Noctura | Blackmire Swamp | +5% crit chance, +10% backstab damage | -3 Chivalry / +3 Darkness |
| Lantern Shrine of Aurelion | Whispering Forest | +5% holy ability damage, +25% holy enchant proc damage | +5 Chivalry / -5 Darkness |
| Heart of the Tide (Veloura) | Stormbreak Coast | +5 Charisma, +15% NPC reaction modifier | Neutral |

Manwe and Thorgrim deliberately have no shrine -- Manwe is the endgame Creator, Thorgrim's thematic location is the Prison rather than the wild.

### Mechanics

- **One attunement at a time.** Visiting a new shrine breaks the previous attunement and starts a new 24-hour timer. Real choice every day.
- **Per-shrine favor counter** persists across attunements and survives NG+ via `Character.ShrineFavor` dictionary. Visit milestones (10, 25, 50) print a special "the god has noticed you" line on the next visit -- milestone-specific reward encounters are stubbed for a future polish pass.
- **Alignment shifts** route through `AlignmentSystem.ChangeAlignment` so paired-movement (the v0.57.0 design) applies cleanly.
- **Active attunement displays** on `/health` Active Buffs and on the wilderness main menu so the player always knows what's running.
- Access is **outside the expedition flow** -- visiting a shrine doesn't consume one of the 4 daily expedition slots. Pilgrimage is its own daily ritual.

### How it lands in-game

New `[P] Pilgrimage` option on the wilderness main menu. Selecting it shows all 5 shrines with their region, passive summary, favor count, and active highlight. Pick one, read the flavor description, confirm, get attuned. Pre-attunement screen warns about the alignment cost.

## Feature: Beast Taming (8 tameable wild creatures, permanent roster, 5th party slot)

Wandering the wilderness, players now encounter wild creatures that can be tamed and added to a permanent pet roster (cap 8). Each beast has a region requirement, a player-level requirement, a tame-difficulty skill check, and a passive bonus -- two of the eight are **combat-ready** and occupy a new 5th party slot in dungeon combat.

### The Eight Beasts

| Beast | Region | Min Lvl | Role | Effect |
|---|---|---|---|---|
| Forest Hawk | Whispering Forest | 10 | Passive | +5% dungeon map reveal on floor entry |
| Mountain Goat | Iron Mountains | 15 | Passive | -1 fatigue per dungeon descend (TODO: wired in v0.61.1) |
| Marsh Toad | Blackmire Swamp | 12 | Passive | +25% poison resist + free antidote 1/day (TODO: wired in v0.61.1) |
| Tidepool Sprite | Stormbreak Coast | 20 | Passive | +5% out-of-combat mana regen (TODO: wired in v0.61.1) |
| Dire Wolf | Whispering Forest | 30 | **Combat (5th slot)** | Basic-attack pet, 80 HP / 18 atk / 8 def base, scales with player level |
| Cave Spider | Iron Mountains | 35 | Passive | +5% crit chance, -10% NPC reaction (creepy) |
| Bog Wisp | Blackmire Swamp | 40 | Passive | +shadow damage + free Hide 1/fight (TODO: wired in v0.61.1) |
| Storm Eagle | Stormbreak Coast | 50 | **Combat (5th slot)** | Lightning-themed pet, 110 HP / 30 atk / 12 def base |

### Encounter mechanic

After every wilderness expedition's normal encounter resolves, there's an **8% chance** to also encounter a tameable beast. The pool is filtered by:
- Region the player is currently exploring
- Player meets the beast's MinPlayerLevel
- Beast is NOT already in the player's roster (each tame is a new addition)

If the pool is empty after filtering, no encounter fires. If the player's roster is at cap (8), they see a flavor "you spot one but your hands are full" line and walk away.

### Taming skill check

When an encounter fires, the player gets **3 attempts** to tame the beast. Each attempt rolls:

```
d20 + (CHA / 4) + (DEX / 8)  vs  TameDifficulty
```

CHA (effective, including Veloura attunement) contributes more than DEX (taming is mostly a charisma act). Success = beast added to permanent roster. Three failures = beast despawns for the rest of this expedition (try again next time).

Tame difficulties range from 12 (Mountain Goat, very chill) to 21 (Storm Eagle, apex predator). Most adventurers can reliably tame the low-end beasts by level 20-30; the high-end Bog Wisp / Storm Eagle / Dire Wolf require either high stats or repeated attempts.

### Pet roster management (Home)

New `[Y] Tamed Beasts` option at Home opens the menagerie:
- Lists all tamed beasts with their level, role tag (Combat / Passive), and passive description
- Shows current active beast with a green highlight
- Pick a number to set that beast as your active companion
- `[U]` to unset (no active companion)

**One active pet at a time.** Combat beasts (Dire Wolf, Storm Eagle) occupy the 5th party slot when active and enter combat alongside companions / NPC teammates. Passive beasts ride along quietly and apply their bonus globally.

### Combat integration (Dire Wolf, Storm Eagle)

On dungeon entry, if the player's active pet is a Combat-role beast, it gets added to the teammates list as a Character wrapper with `IsPet = true`. Stat scaling formula:
- Effective level = max(pet level, player level / 2)
- HP / Atk / Def scale 10% / 8% / 5% per effective level

Combat pets **cannot permadie** -- at the end of every combat their HP restores to MaxHP automatically. On victory, they earn ~5% of the combat's XP toward their own pet level (separate from the player's XP). Pet level-up notification prints in yellow.

Pets are filtered out of social / relationship / NPC flows via the `IsPet` flag.

### All eight passive pet effects wired

- **Cave Spider** -- +5% crit chance, -10% NPC reaction multiplier (in `StatEffectsSystem.RollCriticalHit` and `AlignmentSystem.GetNPCReactionModifier`).
- **Forest Hawk** -- ~5% of dungeon floor rooms revealed on first floor entry (in `DungeonLocation` first-floor-enter block).
- **Mountain Goat** -- -1 fatigue per dungeon room explored (in `DungeonLocation`'s room-fatigue calculation, floored at 0).
- **Marsh Toad** -- 25% chance to fully resist incoming poison applications via new `Character.TryResistPoison()` helper applied at all 4 dungeon poison-trap sites (darts, chest gas, room traps, pixie curse). PLUS one free antidote per day handed to the player on first dungeon entry, gated by new `Character.MarshToadAntidoteClaimedToday` flag with daily reset.
- **Tidepool Sprite** -- +5% MaxMana regenerated per in-game hour crossed (in `DailySystemManager.AdvanceGameTime`). Out-of-combat trickle that keeps spellcasters topped up between delves.
- **Bog Wisp** -- 12% per-hit independent shadow-damage proc (~10% bonus damage, demons still immune) in `ApplyPostHitEnchantments`. PLUS auto-applies `StatusEffect.Hidden` at combat start so the player gets a free guaranteed crit on the opening swing.

The two combat pets (Dire Wolf, Storm Eagle) fight as 5th-slot teammates; the six passives all fire correctly. Save plumbing complete for the daily-antidote flag (`PlayerData.MarshToadAntidoteClaimedToday`, daily-reset in `DailySystemManager`).

## Feature: Shrine favor milestone rewards (10-visit gifts)

Crossing 10 visits to any one shrine now grants a one-time tangible reward themed to that god's domain:

- **Terravok** (Stone Circle): permanent +25 Max HP -- mountain endurance settles into your bones.
- **Maelketh** (Broken Blade Altar): permanent +3 Strength -- the mark burns into your shoulder.
- **Noctura** (Moonwell): permanent +3 Dexterity -- shadow-handed reflexes.
- **Aurelion** (Lantern Shrine): permanent +5 Wisdom -- Sir Aedric's vow settles in.
- **Veloura** (Heart of the Tide): permanent +5 Charisma -- Veloura's withered hand cups your jaw.

Rewards write to the `Base*` stat field directly so `RecalculateStats()` preserves them across loads. Each shrine's reward fires exactly once at favor=10. Favor milestones at 25 and 50 still print the "the god has noticed you" recognition line; their specific reward content lands in a future polish pass.

## Fix: "Your family has grown" message pointed to wrong location

Player report: after a newborn is registered, the notification line read "Your family has grown. Visit Love Corner to see your children." -- but children live at the player's **home**, not Love Corner. Love Corner is the dating / paid-encounter / marriage venue; the Home location's family / spouse / children flows are where the player actually views and interacts with their kids.

Fix: `intimacy.family_grown` rewritten in en/es/fr/it to point at the home instead. Hungarian was already correct ("Látogasd meg otthonodat" -- "visit your home"), no change needed. (This was originally cut as v0.60.12; folded into v0.61.0 since we're shipping the big Wilderness release together.)

## Fix: Street-encounter NPC kills bypassed the daily murder cap

Player report: a Lv.71 Tidesworn killed 8 named NPCs in 20 minutes via street encounters (Aldric Stormcaller, Slick Rick, Master Wu, Calista Quickwater, Wildwood, Burly Mercenary, plus repeats) for ~30k stacked XP. The v0.57.6 `MaxMurdersPerDay = 3` cap was enforced at `BaseLocation.AttackNPC` (player-initiated kills) but NOT at `StreetEncounterSystem.FightNPC` (the path random street encounters take). Combat resolved, NPC's HP dropped to 0, full XP/gold paid out, counter never incremented.

Fix: in `FightNPC` after a victory, look up the victim in `NPCSpawnSystem.ActiveNPCs`. If they're a real persistent NPC (not a throwaway random brawler), the kill counts against `MurdersToday`. Throwaway mobs from `CreateRandomHostileNPC` (Burly Mercenary, Drunk Sailor, Mugger, Thug, etc.) don't exist in the world roster and stay uncapped -- self-defense in a tavern brawl shouldn't burn your daily limit. Honor duels and inn brawls (`isHonorDuel` / `isBrawl` flags) also exempt.

Once the player is over the cap on real-NPC kills, the combat engine still pays out XP/gold at first -- the encounter was hostile-initiated so the player couldn't refuse -- but `FightNPC` immediately claws back 90% of both. Net effect: 10% rewards on the 4th+ named-NPC kill of the day. Red flavor line: "*Your blade trembles. The townsfolk have seen too much of your work today. (Rewards reduced -- daily murder cap reached.)*" Two new loc keys (`street.fight.over_cap_diminished`, `street.fight.over_cap_hint`); other languages fall back to English.

The grind goes from unlimited ~30k XP / 20 min to ~12k XP / 20 min absolute ceiling (3 full kills + the rest at 10%) -- and that's only achievable if the player can keep triggering random encounters with real NPCs, which is rate-limited by world location-change cadence.

## Files Changed

- `Scripts/Core/GameConfig.cs` -- Version 0.61.0. 8 new constants for shrine passive bonuses (Terravok HP regen, Maelketh melee bonus, Noctura crit/backstab, Aurelion holy damage / proc, Veloura CHA / reaction).
- `Scripts/Data/DruidShrineData.cs` -- NEW. 5 shrine definitions + `DruidShrine` class + favor milestone array + region/id lookup helpers.
- `Scripts/Data/BeastData.cs` -- NEW. 8 beast definitions + `Pet` class + `BeastDefinition`, `BeastRole` enum + roster cap / attempt count / encounter chance constants + `PickEligibleBeast` helper.
- `Scripts/Core/Character.cs` -- New fields: `AttunedShrineId`, `AttunedShrineExpiresUtc`, `ShrineFavor`, `PetRoster`, `ActivePetId`. New helpers: `HasActiveShrineAttunement`, `IsAttunedTo`, `GetEffectiveCharisma`, `GetActivePet`, `HasActivePet`. New `IsPet` flag for combat-pet Character wrappers.
- `Scripts/Systems/SaveDataStructures.cs` -- New `PetSaveData` class. PlayerData gains shrine + pet roster fields.
- `Scripts/Systems/SaveSystem.cs` -- Serialize shrine state + pet roster.
- `Scripts/Core/GameEngine.cs` -- Deserialize shrine state + pet roster on load.
- `Scripts/Locations/WildernessLocation.cs` -- `[P] Pilgrimage` menu + `ShowPilgrimageMenu` (shrine list, favor display, alignment confirmation, milestone tease). `TryBeastEncounter` after each expedition with full 3-attempt taming skill-check flow.
- `Scripts/Locations/HomeLocation.cs` -- `[Y] Tamed Beasts` menu + `ShowPetRoster` (list, set active, unset).
- `Scripts/Locations/DungeonLocation.cs` -- `AddActivePetToParty` adds combat pets to teammates list on dungeon entry. Forest Hawk active pet reveals ~5% of floor rooms on entry.
- `Scripts/Systems/CombatEngine.cs` -- Maelketh +8% melee damage at both Knight/GC bonus sites. Terravok HP regen at end-of-round status block. Aurelion holy enchant proc and holy ability damage bonuses. End-of-combat HP restore for pet teammates + pet XP/level-up grant.
- `Scripts/Systems/StatEffectsSystem.cs` -- Noctura +5% crit attunement bonus. Cave Spider +5% crit pet bonus. New `GetCriticalHitChanceCapped` helper for consistent cap math.
- `Scripts/Systems/AlignmentSystem.cs` -- `ApplyVeloraReactionBonus` helper applied at all 5 NPC reaction-modifier branches. Adds Veloura +15% and Cave Spider -10% multipliers.
- `Scripts/Locations/BaseLocation.cs` -- `/health` Active Buffs shows current shrine attunement + hours remaining.
- `Localization/en.json` -- ~50 new keys (pilgrimage flow, beast encounter, tame attempts, pet roster UI, combat shrine messages, dungeon hawk scout, pet level-up, toad poison resist + antidote gift, bog wisp procs, all five shrine milestone-10 gift lines).
- `Localization/{es,fr,it,hu}.json` -- All new keys translated by parallel translation agents (Spanish, French, Italian, Hungarian). Old God proper nouns and `[X]` menu markers preserved across all four languages.
- `Scripts/Core/Character.cs` -- New `MarshToadAntidoteClaimedToday` daily-flag property. New `TryResistPoison()` helper. New `HasActivePet(id)` helper.
- `Scripts/Systems/SaveDataStructures.cs` + `SaveSystem.cs` + `GameEngine.cs` -- `MarshToadAntidoteClaimedToday` save plumbing.
- `Scripts/Systems/DailySystemManager.cs` -- Daily reset for `MarshToadAntidoteClaimedToday`. Tidepool Sprite +5%/hr MaxMana regen wired into `AdvanceGameTime`.
- `Scripts/Locations/DungeonLocation.cs` -- Mountain Goat fatigue reduction in room-fatigue calc. Marsh Toad poison resist at 4 trap sites. Marsh Toad daily antidote on first dungeon entry. Forest Hawk room reveal.
- `Scripts/Systems/CombatEngine.cs` -- Bog Wisp independent shadow proc in `ApplyPostHitEnchantments`. Bog Wisp Hidden status applied at combat start.
- `Scripts/Locations/WildernessLocation.cs` -- `ApplyShrineMilestone10Reward` helper applied on favor-10 milestone hit; permanent stat lifts per shrine via `Base*` field mutation + `RecalculateStats()`.
- `Localization/{en,es,fr,it}.json` -- Rewrote `intimacy.family_grown` to point at "your home" instead of "Love Corner". Hungarian was already correct.
- `Scripts/Systems/StreetEncounterSystem.cs` -- `FightNPC` victory branch now distinguishes real persistent NPCs from throwaway random brawlers via `NPCSpawnSystem.GetNPCByName` lookup. Real-NPC kills increment `MurdersToday`; over-cap kills claw back 90% of XP/gold the combat engine just awarded. Honor duels and inn brawls exempt.
- `Localization/en.json` -- Two new keys for the over-cap diminish: `street.fight.over_cap_diminished`, `street.fight.over_cap_hint`.
