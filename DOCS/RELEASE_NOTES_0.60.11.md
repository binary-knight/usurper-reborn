# v0.60.11 -- Beta

The Gauntlet patch (plus one weapon-shop fix). Four-layer overhaul on top of v0.60.10: (a) Fame rewards per wave survived; (b) real stakes on a loss (25% chance the loss is a real death that consumes a resurrection, 75% drag-out costing 5% XP and Fame); (c) quadratic entry fee scaling so endgame players actually feel the cost; (d) a complete redesign from random-monster spawns to a hand-crafted gladiatorial arena: 3 themed warmup waves + 7 named Old God champions with announced entrances, crowd ambiance, surrender option, themed equipment drops, and a 5-tier completion title system (Arena Hopeful through Grand Champion) gated on player level at moment of full clear.

## Feature: The Gauntlet now awards Fame per wave survived

Pre-fix, the Gauntlet at Anchor Road paid out gold + XP per wave and a champion bonus on a full 10-wave clear, but had no Fame component. The Gauntlet is one of the most punishing solo PvE experiences in the game -- 10 waves of escalating monsters culminating in a level+10 boss -- and "your renown" is exactly the kind of thing that should grow when you survive it.

### Curve

Fame per wave scales with the existing difficulty tiers:

- **Waves 1-3** (sub-level warmup monsters): +1 Fame each
- **Waves 4-6** (at/over-level monsters): +2 Fame each
- **Waves 7-9** (mini-boss tier): +3 Fame each
- **Wave 10** (boss): +5 Fame
- **Champion bonus** (cleared all 10): +20 Fame

Partial-clear totals:

| Waves survived | Fame earned |
| --- | --- |
| 1 | 1 |
| 3 | 3 |
| 5 | 7 |
| 7 | 12 |
| 9 | 18 |
| 10 (champion) | 43 |

For context: a single arena win gives +10 Fame, a world boss kill gives +25, an Old God defeat gives +50, and knighthood at the Castle requires Fame >= 150. A full Gauntlet clear is now ~3-4 clears away from knighthood, which fits the difficulty.

### How it lands in-game

Each wave victory now prints a yellow line after the existing wave-complete message:

```
Wave 7 complete! +5,000 gold, +35,000 XP
  The crowd remembers your name. +3 Fame.
```

The champion bonus on wave 10 also prints a Fame line:

```
THE GAUNTLET CHAMPION
  CHAMPION BONUS: +20,000 gold, +50,000 XP
  Your name is etched among the Gauntlet champions. +20 Fame.
```

Two new loc keys per language: `anchor_road.wave_fame` and `anchor_road.champion_fame_bonus`.

---

## Feature: Gauntlet loss now carries real risk (25% real death, 75% drag-out with penalty)

Pre-fix, every Gauntlet loss was a "safe" exhibition loss -- you forfeited the entry fee + a daily fight slot but kept your resurrection counter. The Gauntlet was the safest combat content in the game: low cost, no real downside, valid XP/gold farm with zero permadeath risk. That doesn't match its in-fiction framing as the city's deadliest arena.

### Curve

When a wave is lost (the player goes down, not flees), the engine rolls 25% chance of "real death":

- **25% real death** -- the loss is processed as a normal monster death. In online mode, one Resurrection is consumed; if the player had zero, permadeath fires with the usual server-wide broadcast. Single-player runs the Veil-of-Death penalty menu (Temple / Deal with Death / Accept Fate). Any Fame earned during the run is forfeited (run voided).
- **75% drag-out** -- medics restore you to 1 HP (no resurrection consumed) but you lose **5% of current XP** plus **all Fame earned this run + an additional -5 Fame**. Light penalty compared to a real death, but no longer free.

The roll is pre-rolled at the start of each wave to decide what THAT wave's loss would be. Since a Gauntlet run only ends on a single losing wave, only one of these dice is meaningful per run. The player never sees the roll directly -- they just see the consequence after the loss.

Flee outcome (player chose to retreat from a wave) is unchanged: no real death, no drag-out penalty, just the entry-fee sunk-cost. Flee remains the safe exit.

### Why this matters

- Brings the Gauntlet's risk in line with its rewards. A 10-wave clear at Lv 50 is 25,000g entry + ~25,000g winnings + ~125,000 XP + 43 Fame -- now with a real chance of permadeath if a wave goes sideways, that risk/reward starts to feel right.
- The pre-fight disclaimer message was updated in all 5 languages from "(Medics stand by: a loss costs the entry fee, not a resurrection)" to "(Warning: 25% chance of death on loss. The other 75% you're dragged out, losing 5% XP and Fame but no resurrection.)" so the new stakes are visible BEFORE the player commits gold.

Four new loc keys per language: `anchor_road.gauntlet_claims_you`, `anchor_road.gauntlet_fame_forfeit_dead`, `anchor_road.gauntlet_xp_loss`, `anchor_road.gauntlet_fame_loss`.

---

## Feature: Gauntlet entry fee now scales quadratically with level

Pre-fix entry fee was `level * 100` -- Lv 5 = 500g, Lv 50 = 5,000g, Lv 100 = 10,000g. Trivial endgame. A Lv 100 player with millions in the bank could spam the Gauntlet without thinking about cost.

### Curve

New formula: `level^2 * 10`. Sample fees:

| Level | Pre-fix entry fee | New entry fee |
| --- | --- | --- |
| 5 | 500g | 250g |
| 10 | 1,000g | 1,000g |
| 20 | 2,000g | 4,000g |
| 50 | 5,000g | 25,000g |
| 100 | 10,000g | 100,000g |

Below ~Lv 10 the new fee is actually CHEAPER (encouraging early-level players to try it). Above Lv 10 the cost scales sharply -- a Lv 100 spammer now drops 100k per run, which combined with the 25% death risk makes the Gauntlet a serious commitment instead of a routine farm.

The change uses a new `GauntletEntryFeeQuadraticCoefficient = 10` constant in `GameConfig`. The old `GauntletEntryFeePerLevel = 100` constant is removed.

---

## Feature: The Gauntlet redesigned as a gladiatorial arena (7 Old God champions)

Pre-fix the Gauntlet was 10 waves of random monsters generated by `MonsterGenerator`. Mechanically OK but had none of the gladiatorial feel the in-fiction framing of "the city's deadliest arena" promised. Player request: make it feel like a scene from Spartacus -- crowd cheering, environmental ambiance, named champions with announced entrances, lore weight.

### Structure

Run is still 10 fights total but split into two phases:

**Waves 1-3: Opening Acts.** Themed warmup against random low-tier monsters, but framed with arena flavor at the announcement: condemned criminals given rusted swords, escaped slaves fighting for freedom, dire beasts released from the city menagerie, drunk old gladiators, hooded foreign sellswords. Picks from a 6-line pool in `GauntletChampionData.WarmupWaveFlavor`. Mechanically straightforward -- gold/XP/Fame as before, no champion drops.

**Waves 4-10: The Seven Champions.** Hand-crafted named gladiators, one per Old God, in canonical difficulty order:

| Wave | Champion | Patron god | Role | Lvl bonus | HP / ATK / DEF mult |
| --- | --- | --- | --- | --- | --- |
| 4 | Vargash the Blade-Bound | Maelketh, The Broken Blade | Berserker | +2 | 1.8 / 1.4 / 0.8 |
| 5 | Selithea the Heartrender | Veloura, The Withered Heart | Duelist (charm/poison) | +4 | 1.4 / 1.2 / 1.0 |
| 6 | Korr Stonewarden | Thorgrim, The Hollow Judge | Stonewall (thorns) | +6 | 3.0 / 1.0 / 2.2 |
| 7 | The Black Twin | Noctura, The Shadow Weaver | Assassin (vanish/backstab) | +8 | 1.6 / 1.6 / 0.9 |
| 8 | Sir Aedric, the Last Lantern | Aurelion, The Fading Light | Paladin (heal/holy) | +10 | 2.0 / 1.3 / 1.8 |
| 9 | Grok of the Pack-Stone | Terravok, The Sleeping Mountain | Feral (summon/regen) | +12 | 2.6 / 1.5 / 1.3 |
| 10 | The Nameless Tyrant | Manwe, The Weary Creator | Tyrant (multi-phase) | +14 | 4.0 / 1.8 / 2.0 |

Each champion has:
- A 3-5 line herald entrance announcement read with timing beats (600ms between lines)
- A one-line god-lore reveal in dark italics
- A crowd reaction (cheering / silence / pack-noise / weeping depending on the champion's nature)
- A themed equipment drop on defeat, stats scaled to the player's level so every tier gets a relevant item
- A signature MonsterAbilities kit (Vargash gets CriticalStrike+Cleave+Rage; the Black Twin gets Backstab+Vanish+PhaseShift; the Tyrant gets DivineJudgment+DragonFear+Enrage+CrushingBlow+DevourSoul)

Champion stats scale with player level via `SpawnChampionMonster(champion, playerLevel)` -- effective level = playerLevel + champion.LevelBonus, then HP/ATK/DEF multiplied by the champion's role multipliers. The Nameless Tyrant at Lv 80 player = Lv 94 monster with 4x HP and 1.8x attack. Designed to be possible at high level with good gear and a full party; legitimately deadly otherwise.

### Crowd ambiance

Between every fight (except wave 1), a random line from `GauntletChampionData.CrowdAmbiance` prints in dark gray: vendors shouting bets, slaves raking blood out of the sand, drums starting in the upper tiers, nobles smiling unsettlingly, priests praying loudly to nobody. 12-line pool keeps the arena alive across runs without feeling repetitive.

### Surrender option

Between every fight (except before wave 1), the player is prompted: "You have survived X of 10 fights. The gate has not yet opened. Yield and leave the arena alive? (y/N)". Surrender is a clean exit -- no real-death roll, no XP/Fame penalty, no resurrection consumed. The player keeps all gold/XP/Fame earned in waves already completed, plus any champion drops they've collected. Strategic option for partial-clear farming.

### Per-champion equipment drops

Each champion drops a themed item on defeat, stats scaled to player level:

- Vargash's Cleaver (MainHand)
- Selithea's Ribbon (Cloak)
- Stonewarden's Plate (Body)
- Twin-Step Veil (Face)
- Aedric's Lantern-Blade (MainHand)
- Pack-Stone Talisman (Neck)
- Tyrant's Crown (Head)

Rarity scales by player level at moment of drop: Common (Lv 1-19) -> Uncommon (20-39) -> Rare (40-59) -> Epic (60-79) -> Legendary (80+). Same item name, same slot, different stat tier. Lv 80 player gets a Legendary Tyrant's Crown; Lv 25 player gets an Uncommon Tyrant's Crown. Every tier completion is loot-rewarding.

### Five-tier completion title and achievement system

On a full 10-fight clear, tier is determined by player level at that moment:

| Level | Title | Achievement | Gold | XP | Fame |
| --- | --- | --- | --- | --- | --- |
| 5-19 | Arena Hopeful | Bronze (`arena_hopeful`) | lvl x 200 | lvl x 100 | +30 |
| 20-39 | Arena Veteran | Silver (`arena_veteran`) | lvl x 400 | lvl x 200 | +50 |
| 40-59 | Arena Master | Gold (`arena_master`) | lvl x 700 | lvl x 350 | +75 |
| 60-79 | Arena Champion | Platinum (`arena_champion`) | lvl x 1100 | lvl x 500 | +100 |
| 80-100 | Grand Champion | Diamond (`grand_champion`) | lvl x 1500 | lvl x 700 | +150 |

A Lv 80 Grand Champion clear earns 120,000g + 56,000 XP + 150 Fame, versus a Lv 10 Hopeful clear at 2,000g + 1,000 XP + 30 Fame. ~60x range from bottom to top, making the difficulty payoff meaningful.

The player's stored `ArenaChampionTier` upgrades only -- a Lv 80 re-clear from a Lv 25 Veteran becomes Grand Champion permanently. A Lv 25 re-clear from a Lv 80 Grand Champion does NOT downgrade. Each tier achievement is individually earnable for completionists.

### Champion title display

The earned title displays as a prefix in:
- `/who` (MUD chat command)
- Online players who list (`OnlineChatSystem`)
- Main Street citizen list and Fame board
- `/health` Active Buffs section (Grand Champion only -- shows the passive bonus line)

Champion title REPLACES the knighthood "Sir / Dame" prefix when present. A knighted Grand Champion shows as "Grand Champion Rage" in /who -- the higher single honor wins the display slot. Knighthood combat bonus (+5% damage / +5% defense) still applies under the hood via `IsKnighted`.

### Grand Champion permanent passive

Diamond-tier completion grants `GrandChampionDamageBonus = +3%` damage / `GrandChampionDefenseBonus = +3%` defense permanently. Stacks with knighthood, so a knighted Grand Champion gets +8% / +8% lifetime. Applied at three combat sites alongside the existing Knight bonus checks (`ExecuteSingleAttack`, multi-monster basic attack, player defense computation).

### News broadcast on full clear

On every full clear, a news entry is posted ("Grand Champion Rage has conquered the Anchor Road Gauntlet!") and -- in online mode -- a server-wide bright-yellow broadcast goes out to all connected players. Same flavor as the knighting ceremony broadcast.

### Arena titles are configurable, not stuck

Player feedback after the initial Gauntlet ship: "the arena master titles should be configurable and not stuck on a player if they beat the arena, just like all the other titles." Confirmed -- the first cut of arena tiers prepended the tier prefix unconditionally to /who, the Main Street Fame board, the citizen list, and the online who-list directly from `ArenaChampionTier`. The player had no way to opt out, swap back to Sir/Dame, or display no title at all. That broke the existing title-picker design (Preferences > Title) where knighthood and meta-progression titles route through the player-controlled `NobleTitle` field.

Fix: arena tiers now flow through `NobleTitle` like every other title in the game. Display sites (`MudChatSystem` `/who`, `OnlineChatSystem` online who-list, Main Street Fame board, Main Street citizen list) stop reading `ArenaChampionTier` directly and prepend `NobleTitle` only -- single source of truth. The earned tier is offered as a pickable entry in `BaseLocation`'s Preferences > Title menu alongside Sir/Dame and meta-progression titles. On gauntlet completion at a new tier, `NobleTitle` is auto-set to the new tier name (same UX as the knighting ceremony) and the new title also lands in `MetaProgressionSystem.UnlockedTitles` so it survives NG+ cycles, but the player can change it to any other earned title -- or pick "(None)" to display no prefix -- at any time. Post-completion the screen now adds a dark-gray line: "(You can change or remove this title in Preferences > Title.)" so the player knows the control exists.

The Grand Champion combat bonus (+3% damage / +3% defense, applied at three combat sites) stays gated on `ArenaChampionTier`, not on the display title. Hiding the title doesn't cost the bonus -- the tier is the achievement record, the title is the display preference. Same architectural split as knighthood (`IsKnighted` for the bonus, `NobleTitle = "Sir"/"Dame"` for display). New loc key `anchor_road.tier_title_change_hint`.

---

## Fix: Reforge cancel exploit at the Weapon Shop

Player feedback: weapon reforging at the shop was risk-free. The flow let you (1) confirm you want to reforge and pay the cost, (2) see the rerolled stats, then (3) choose whether to keep the new version or revert to the original. So a player could pay, peek at the result, revert if the roll was worse, and try again -- the only downside ever was the gold cost, and there was no point in NOT spamming reforge on every weapon you owned. That undercut the whole design of reforging as a gamble.

Root cause in `WeaponShopLocation.ReforgeWeapon`: a second prompt after the reroll asked "Accept new stats? (Y/N)" and the N branch left the original weapon intact while still pocketing the gold. The pre-confirm warning text didn't make it clear that the existing prompt was the actual commit point.

Fix: removed the post-reroll accept/keep prompt entirely. The pre-confirm prompt is now the only decision point. Once the player pays, the reroll is final -- whatever stats came out, those are the new stats. The reforged values are copied onto the weapon (`WeaponPower`, `Rarity`, all stat bonuses, `LootEffects` for procs / enchants) and `RecalculateStats()` runs. A green "the reforge takes hold" line confirms it landed.

Rewrote the warning + confirm prompts in all 5 languages (en/es/fr/it/hu) to make the commitment explicit: "the result is final once you pay -- you cannot revert to the original." Players who want to scout the outcome can no longer do so for free; the gamble is real.

---

## Files Changed

- `Scripts/Core/GameConfig.cs` -- Version 0.60.11. Replaced `GauntletEntryFeePerLevel` (linear 100/level) with `GauntletEntryFeeQuadraticCoefficient = 10`. Added `GauntletDeathChancePercent = 25`, `GauntletDragoutXPLossPercent = 0.05f`, `GauntletDragoutFameLossPenalty = 5`, `GrandChampionDamageBonus = 0.03f`, `GrandChampionDefenseBonus = 0.03f`.
- `Scripts/Data/GauntletChampionData.cs` -- NEW. Defines the 7 Old God champions (id, name, title, patron, role, stat multipliers, level bonus, entrance announcement lines, lore line, crowd reaction, drop spec, ability kit). Includes `ArenaTier` enum + tier helpers (`GetTierForLevel`, `GetTierTitle`, `GetTierAchievementId`, `GetTierRewards`). Plus 12 `CrowdAmbiance` lines and 6 `WarmupWaveFlavor` lines.
- `Scripts/Locations/AnchorRoadLocation.cs` -- Quadratic entry fee. Per-wave Fame reward tracked via `fameEarnedThisRun`. Per-wave pre-rolled `deathRollThisWave` gates `IsExhibitionCombat`. Loss branch split into three paths (flee / real death / drag-out). Spawn logic split between warmup (waves 1-3, random monster + `AnnounceWarmupWave`) and champion (waves 4-10, `SpawnChampionMonster` + `AnnounceChampionEntrance`). Per-champion drop via `GenerateAndAwardChampionDrop` after victory. Wave-10 full-clear rewards rewritten as tier-based: `GetTierForLevel` -> `GetTierRewards` -> upgrade `ArenaChampionTier` (only if higher) + tier-keyed achievement + tier-aware news/broadcast. Between-fight `OfferGauntletSurrender` prompt + `PrintGauntletCrowdAmbiance` random line. Five new private helper methods.
- `Scripts/Core/Character.cs` -- New `ArenaChampionTier` int property (0=None..5=GrandChampion).
- `Scripts/Systems/SaveDataStructures.cs` -- `ArenaChampionTier` field on `PlayerData`.
- `Scripts/Systems/SaveSystem.cs` -- Serialize `ArenaChampionTier`.
- `Scripts/Core/GameEngine.cs` -- Deserialize `ArenaChampionTier` on load.
- `Scripts/Systems/IOnlineSaveBackend.cs` -- `ArenaChampionTier` field on `PlayerSummary` DTO so the Fame board / citizen list can show the title for other players.
- `Scripts/Systems/SqlSaveBackend.cs` -- `GetAllPlayerSummaries` SQL extracts `arenaChampionTier` from `player_data` JSON.
- `Scripts/Server/MudChatSystem.cs` -- `/who` prepends arena tier title (replaces Sir/Dame when present).
- `Scripts/Systems/OnlineChatSystem.cs` -- Online-mode who-list prepends arena tier title.
- `Scripts/Locations/MainStreetLocation.cs` -- Fame board + citizen list use arena tier title (replaces Sir/Dame when present).
- `Scripts/Locations/BaseLocation.cs` -- `/health` Active Buffs section shows Grand Champion's Mantle passive bonus when tier >= GrandChampion. `hasAnyBuff` includes the check so the section renders even when that's the only buff active.
- `Scripts/Systems/CombatEngine.cs` -- Grand Champion +3% damage applied at `ExecuteSingleAttack` and multi-monster basic-attack swing; +3% defense at player defense computation. Stacks with the existing Knight bonus.
- `Scripts/Systems/AchievementSystem.cs` -- Replaced single `gauntlet_champion` (Gold) achievement with five tier-keyed entries: `arena_hopeful` (Bronze, 100g), `arena_veteran` (Silver, 250g), `arena_master` (Gold, 500g), `arena_champion` (Platinum, 1000g), `grand_champion` (Diamond, 5000g). Tier-appropriate point values (20/35/50/75/150).
- `Localization/en.json` -- Eleven new keys for the gauntlet rework: `anchor_road.wave_fame`, `champion_fame_bonus`, `gauntlet_claims_you`, `gauntlet_fame_forfeit_dead`, `gauntlet_xp_loss`, `gauntlet_fame_loss`, `gauntlet_full_clear`, `tier_reward`, `tier_title_earned`, `tier_title_already_held`, `grand_champion_passive`, `champion_drop`, `surrender_prompt_header`, `surrender_prompt_keep`, `surrender_prompt_question`, `gauntlet_surrender_taken`. Rewritten `anchor_road.gauntlet_desc_safety` to warn about the 25% death chance. (Other-language translations fall back to English until added.)
- `Scripts/Locations/WeaponShopLocation.cs` -- `ReforgeWeapon` now commits unconditionally after the pre-confirm + gold deduction. Removed the post-reroll accept/keep-original prompt that let players pay, peek at the result, and revert. The pre-confirm prompt is the only decision point; whatever rolls, the player keeps.
- `Localization/{en,es,fr,it,hu}.json` -- Rewrote `weapon_shop.reforge_warning` and `weapon_shop.reforge_confirm` in all five languages to make the commit-on-pay rule explicit ("the result is final once you pay").
- `Scripts/Server/MudChatSystem.cs` -- `/who` no longer reads `ArenaChampionTier` for the name prefix. Prepends `NobleTitle` only (single source of truth).
- `Scripts/Systems/OnlineChatSystem.cs` -- Online who-list same fix as `/who`.
- `Scripts/Locations/MainStreetLocation.cs` -- Fame board (own row and other-player rows) and citizen list now read `NobleTitle` instead of computing an arena prefix.
- `Scripts/Locations/BaseLocation.cs` -- Preferences > Title picker now includes the player's earned arena tier title in `availableTitles` whenever `ArenaChampionTier > 0`.
- `Scripts/Locations/AnchorRoadLocation.cs` -- On Gauntlet tier upgrade, auto-set `NobleTitle = tierTitle` and add it to `MetaProgressionSystem.UnlockedTitles`. Post-completion screen prints the "change in Preferences > Title" hint.
- `Localization/en.json` -- New key `anchor_road.tier_title_change_hint`. (Other languages fall back to English, consistent with the other 16 v0.60.11 gauntlet keys.)
