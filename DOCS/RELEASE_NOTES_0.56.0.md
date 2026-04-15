# v0.56.0 - Class Balance

This release tackles the first wave of the player-feedback balance pass plus the Assassin double-crit bug that caused a single Backstab to deal 17,804 damage.

## Tank Depth (Feedback Plan A1)

A player tanking with Aldric reported that Thundering Roar (level 20, cooldown 5, duration 3) left tanks with a 2-round aggro gap every rotation — and it's the **only** taunt that Warrior / Paladin / Barbarian ever get outside of prestige. After Tidesworn got Abyssal Anchor as a second taunt, the player said tanking became fun for the first time. This release brings the same depth to the base tank classes.

- **Thundering Roar cooldown 5 → 3 rounds** (matches duration; closes the aggro gap).
- **Warrior — Shield Wall Formation (new, level 40)**: AoE taunt + 30% incoming damage reduction for 3 rounds. Requires shield. Protection spec: +10% extra damage reduction.
- **Paladin — Divine Mandate (new, level 40)**: AoE taunt + 15% thorn reflect for 4 rounds. Requires shield. Guardian spec: +5% extra reflect.
- **Barbarian — Rage Challenge (new, level 40)**: AoE taunt + 5% MaxHP regen per round for 3 rounds. No shield required (fits class identity). Juggernaut spec: +3% extra regen.
- **Abyssal Anchor bug fix**: was using `player.Name` instead of `player.DisplayName` for taunt tracking. Every other taunt uses `DisplayName`, so Abyssal Anchor's taunt could fail to route monsters correctly when the two names differed.
- **NPC tank defensive spread**: NPC tanks no longer burn all their defensive buffs on the same round after taunting. Once a defensive buff is active, a second defensive ability is skipped unless HP drops below 30%. Prevents the "taunt then die" pattern.

## Healer Onboarding (Feedback Plan A2)

The player reported that only Cleric is a real healer early-to-late — Alchemist, Bard, and Sage healing arrives too late to actually build around. Investigation confirmed big gaps:

- Sage had **no** class heal abilities at all (only the level 24 Veloura's Embrace spell).
- Alchemist's first heal was level 8, and only 50 base healing — anemic for a spec-committed healer.
- Bard's first heal (Song of Rest) was level 18 AND required an instrument.

Changes:

- **Alchemist — Curative Tincture (new, level 4)**: BaseHealing 40, single-ally. Lets Apothecary spec function from level 4 instead of level 8.
- **Alchemist — Healing Elixir bumped 50 → 60 base healing** so the level 8 ability feels meaningful.
- **Sage — Mending Meditation (new, level 10)**: BaseHealing 60, single-ally, WIS+INT scaling. Fills the 10-24 gap so Mystic spec is viable.
- **Bard — Song of Rest moved from level 18 to level 12** AND instrument requirement removed. Minstrel Bard is now playable without a weapon-shop detour.
- **Paladin — Lay on Hands now targets allies** (was self-only). The level 1 heal becomes useful for the whole party.
- **Healer spec heal bonus**: Restoration / Holy / Mystic / Minstrel / Apothecary / Spiritwalker NPC specs gain **+20% healing output** on class abilities with `Type == Heal`. Applied in both single-monster and multi-monster combat paths.

## Tidesworn Kit Cohesion (Feedback Plan A3)

The feedback's most detailed critique. Weaken mechanic introduced early but never synergized. Alethia's Ward fizzled, barely scaled, and the reflect never grew. Passive regen was useless. Sanctified Torrent was bypassing the AoE damage nerf entirely. Heal spell fizzles caused frustration deaths.

- **Weaken synergy added:**
  - **Riptide Strike** deals **+40% damage vs already-weakened enemies**. Core combo with Undertow Stance.
  - **Maelstrom of the Faithful** now **auto-applies weaken for 2 rounds** to every enemy hit. The AoE and the weaken trigger together, setting up Riptide follow-up.
- **Alethia's Ward rework** (no more fizzle, scaling properly):
  - **No longer fizzles** — it's the Tidesworn's signature buff, not a gamble.
  - Protection: was `+20 + Level/8` (peaks around +32). Now `+15 + Level*2` (peaks around +215 at level 100).
  - Thorn reflect: was flat 10%. Now `10% + INT*0.1%` (reaches ~20% at INT 100).
- **Ocean's Resilience buff**: baseline regen is now **3% MaxHP/round** always, with an **additional +2% when below 50% HP** (5% total in danger). Was 2% only below 50% — effectively never.
- **Sanctified Torrent AoE fix (bug)**: was bypassing the standard AoE damage reduction curve (100%/75%/50%/25%) that every other AoE ability uses. Now applies the reduction like Deluge and Maelstrom do. Feedback explicitly called this out as a balance outlier.
- **Healing spell fizzle removal**: spells of type `Heal` **never fizzle**. Offensive spells retain fizzle (preserves the difficulty gradient the feedback said to keep). Dying because a heal fizzled was the single most frustrating moment reported.

## Assassin Backstab Double-Crit Fix

A player posted a combat log showing a single Backstab dealing **17,804 damage** to a Spider Queen Hive Lord — a one-shot kill that highlighted a stacking bug in the ability crit logic.

**Root cause:** Backstab is described as a "guaranteed critical hit" and multiplied damage by 2.0x. Immediately after applying that multiplier, the generic ability crit logic rolled for ANOTHER critical hit and (when it succeeded) multiplied damage again by the full `GetCriticalDamageMultiplier` value — which scales with Dexterity and equipment crit-damage bonus to roughly 2-3x at Assassin levels. Stacked together, Backstab could hit ×6 total damage. The player's log confirmed the stack: both `Critical hit from the shadows!` (Backstab's guaranteed crit message) AND `CRITICAL ABILITY!` (the generic crit-roll message) fired on the same hit.

Same stacking bug existed in the multi-monster combat path.

**Fix:**
- Added an `abilityAlreadyCrit` flag in both single-monster and multi-monster ability damage paths. When a guaranteed-crit ability (Backstab) applies its multiplier, the follow-up crit roll is skipped. Defensive — any future "guaranteed crit" ability will be safe automatically.
- **Backstab multiplier 2.0x → 1.75x** — still a strong guaranteed crit, but not the outlier.
- **Backstab stamina cost 20 → 30** and **cooldown 2 → 3 rounds** — at level 1 with 20 stamina / 2-round cooldown it was essentially spammable.

---

### Files Changed

- `Scripts/Core/GameConfig.cs` — Version 0.56.0 "Class Balance"; `HealerSpecHealBonus = 0.20f`; `TideswornOceansResiliencePercent` 0.02 → 0.03; new `TideswornOceansResilienceBelowHalfBonus = 0.02f`
- `Scripts/Core/Character.cs` — New transient combat properties: `TempDamageReductionPercent/Duration`, `TempThornReflectPercent/Duration`, `TempPercentRegenPerRound/Duration`
- `Scripts/Systems/ClassAbilitySystem.cs` — Thundering Roar cooldown 5 → 3; new Shield Wall Formation / Divine Mandate / Rage Challenge abilities at level 40; new Curative Tincture (Alchemist L4) and Mending Meditation (Sage L10); Alchemist Healing Elixir BaseHealing 50 → 60; Bard Song of Rest level 18 → 12 and instrument requirement removed; Paladin Lay on Hands gains `CanTargetAlly = true`; Tidesworn Maelstrom of the Faithful special effect changed from `aoe` to `maelstrom_faithful` (adds weaken on hit); Backstab stamina 20 → 30, cooldown 2 → 3
- `Scripts/Systems/CombatEngine.cs` — `abilityAlreadyCrit` flag to prevent Backstab double-crit (single + multi-monster paths); Backstab multiplier 2.0x → 1.75x; `ApplyTankTauntAndBuff` helper + `ProcessTankBuff` round-end helper; tank ability handlers (shield_wall_formation, divine_mandate, rage_challenge) in both combat paths; Shield Wall Formation damage reduction applied in `MonsterAttacksPlayer` and `MonsterAttacksCompanion`; Divine Mandate thorn reflect applied post-damage; two Abyssal Anchor `player.Name` → `player.DisplayName` fixes; NPC tank defensive-spread AI in `TryTeammateClassAbility`; `ApplyHealerSpecBonus` helper + applied at both heal application sites; Tidesworn Riptide +40% damage vs weakened in both paths; Sanctified Torrent now applies standard AoE damage reduction curve; Maelstrom of the Faithful new handler applies weaken AoE; Ocean's Resilience regen uses baseline + below-half bonus
- `Scripts/Systems/SpellSystem.cs` — Alethia's Ward rework: no fizzle, scaling protection (`+15 + Level*2`) and scaling reflect (`10% + INT*0.1%`), applied via `TempThornReflectPercent/Duration`; heal-type spells never fizzle
- `Scripts/Locations/BaseLocation.cs` — Ocean's Resilience `/health` description updated for new regen values
- `Localization/en.json`, `es.json`, `fr.json`, `hu.json`, `it.json` — 8 new keys for tank ability buffs, thorn reflect, regen, Tidesworn weaken bonus, Maelstrom of the Faithful message
