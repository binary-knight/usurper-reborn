# v0.60.10 -- Beta

Hotfix on top of v0.60.9. One diagnostic addition (seal-loss-on-save audit logging) plus player-reported bug fixes: Cleric/Paladin examine-feature heal goes party-wide, team NPC combat-loot equip persists across relog, Hidden-status auto-crit fires on basic `[A]ttack`, two-handed weapons no longer get silently dropped when slotting a one-handed weapon to off-hand, music shop performance buffs surface their effect descriptor, per-weapon enchant procs no longer leak across hands while dual-wielding, monster-ability messages now address the correct target when hitting companions or team NPCs, monster status applications on teammates now surface the resist outcome (Cyclebreaker / Paladin passive resists wired into the companion path, plus an explicit "shrugs off" line when the chance roll fails so the player sees why their teammate can still act), Crypt rooms no longer appear in non-tomb-themed floors (Sewers, Caverns, etc.) where the crypt flavor read as out of place, and Frostbite weapon enchant + Shaman Frostbrand totem now apply real Slow status (halving target attacks per round) instead of permanently mutating Defence by -3 per proc, and weapon enchant proc messages now attribute the attacker by name so the player can tell whose weapon fired which enchant, and monster defensive self-buff abilities (Incorporeal / Vanish / Phase / etc.) now correctly fall through to a normal attack against companions instead of giving the companion a free pass while the player still gets hit, and monsters now carry the correct `MonsterClass` value (Turn Undead works on Ghouls, Holy enchants scale properly against the Undead family, ~20 other class-keyed checks across CombatEngine work for the first time since they were written), Love Street paid encounters got a three-layer rebalance (prices doubled, darkness gains cut ~70%, hard cap of 2 visits per day) so a single trip can no longer swing a balanced player's alignment by 800+ net points, monster-attack damage display made coherent (post-multiplier `monsterAttack` clamped to >= 1 so "swings wildly! 0 vs 143 defense. 1 damage" can no longer happen, defense-calc line gated to only fire when armor actually reduces damage, and a beat is added between a monster's defensive self-buff and its followup basic attack so they don't read as one action), and action-preventing statuses (Stunned / Paralyzed / Sleeping / Frozen / Slow) are now scrubbed from both player and all teammates at combat end so stuns from late-fight monster abilities can't persist into between-combat menus or the first turn of the next fight.

## Save-seal-audit logging (diagnostic for the seal-loss report)

A Lv.72 Cleric reported "all my seals got reset" -- inspection of the save row showed `collectedSeals = [0]` (only Creation, the Temple-given one) while `clearedSpecialFloors = [15, 30, 45, 50, 60]` proved the dungeon seals (FirstWar / Corruption / Imprisonment / Prophecy) had been collected at some point, since the only path that adds floors to that hashset for seal floors is `TryDiscoverSeal` which always also writes to `story.CollectedSeals`.

Code review of the v0.60.9 changes ruled out the map-reveal seal-discovery fix and the Dungeon Reset Scroll (PR #101 in v0.60.8) as direct causes -- neither path mutates `CollectedSeals` in a way that loses entries. The contamination point is somewhere upstream, possibly in a save that fires without `SessionContext.Current` propagated (falling back to the empty process-wide `StoryProgressionSystem._fallbackInstance` singleton and persisting an empty seal list).

This release adds runtime audit logging at the save path so the next occurrence captures the data we need to pin the cause.

### What this ships

`SaveSystem.SerializeStorySystems` now logs a `SAVE_SEAL_AUDIT` line on every online save with:

- Player username (from `SessionContext.Current.Username`, or `<no-ctx>` if missing)
- `ctxPresent` -- whether the per-session story state was reachable, or we fell back to the process-wide singleton
- Save cycle (so NG+ doesn't read as a regression)
- Seal count and the actual seal list

If a save fires with `SessionContext.Current == null`, an additional `SUSPICIOUS_FALLBACK_SAVE` warning fires with the seal list it's about to persist. That's the smoking-gun line: any time it appears with `seals=[]` or `seals=[0]` for a player who had more, we've caught the fallback-save path mid-write.

Online-mode only -- single-player saves are not logged. Saves are throttled to once per 60s in MUD mode so this is at worst a couple of lines per minute per active player. Cheap.

The actual fix for the seal-loss bug will land in a follow-up once the audit log catches the contamination path. The affected player's seals were manually restored in the live DB before this build.

---

## Bug fix: Cleric / Paladin "examine feature" bonus now heals the whole party

Player report (Lv.20 cleric): *"Can the cleric heal from examining dungeon features hit the whole party? If it hits only the cleric it seems redundant -- if the cleric needs healing, something went bad from fighting. This observation is around lvl 20 or so, maybe later this will become handy."*

Confirmed and the player's reasoning lands. When you `[E]xamine` a class-specific dungeon feature (statue, mural, altar, etc. -- the per-room interactive objects), `FeatureInteractionSystem.ApplyClassSpecificBonus` runs a class-themed reward. Every other class's reward is high-utility for that class:

- Warrior / Barbarian: temporary attack bonus
- Magician / Sage: mana restore (so the caster can keep casting)
- Assassin: poison vials
- Ranger: gold + healing potion
- Bard: song charges
- ...

But Cleric / Paladin got a self-heal of `MaxHP / 4`. Cleric is back-row -- they rarely need healing themselves, and when they do it's because something went badly. The bonus was effectively a no-op at low levels and only occasionally relevant at high levels.

### Fix

Cleric / Paladin's `feature.divine_presence` now radiates `MaxHP / 4` to **every alive teammate** in the party in addition to the cleric. Each ally heals based on their own MaxHP (not the cleric's). When everyone is already at full HP, a flavor line acknowledges the radiance without spamming "+0 HP" lines per teammate. Solo cleric still gets the same self-heal as before, so single-player progression isn't affected.

Magician / Sage mana restore is intentionally unchanged -- mana on the caster IS the high-utility outcome for those classes (they convert mana to damage). Same for the others.

---

## Bug fix: team NPC combat-loot equip didn't persist across relog

Player report (Lv.72 Cleric): *"Team NPC still not saving equipped gear. She equips in dungeon when I pass on gear, but doesn't seem to save it when relogging. Confirmed relogging reset her ring. Double-checked equipping from team corner, and that worked fine after relog. Lastly, checked equip from bags in dungeon party menu, and that worked. Seems like it just when you pass on gear and the teammate picks it up."*

Confirmed -- the reporter's diagnostic narrowed it perfectly. Three combat-loot equip paths in `CombatEngine` only call `CompanionSystem.SyncCompanionEquipment(teammate)` after the equip -- which is a no-op for non-companion teammates (Aldric / Mira / Lyris / Vex / Melodia are companions; team NPCs recruited from Team Corner are NOT). For non-companion teammates the equipment mutation stayed on the orphaned combat-side reference and never propagated to `NPCSpawnSystem.ActiveNPCs`, so when `SaveAllSharedState` serialized `world_state.npcs` it persisted the stale equipment.

The [T] (transfer to bag) path at `CombatEngine.cs:8339` already correctly calls `SyncNPCTeammateToActiveNPCs` -- that's why bag transfers persisted. Team Corner equip and dungeon-party-menu equip go through different code paths that mutate the canonical NPC directly, which is why those persisted too. The bug was specifically the combat-loot equip paths.

### Fix

Three sites in `CombatEngine.cs` updated -- combat [E] equip-on-ally manual path (~line 8281), combat auto-equip on pass-loot single-monster (~line 8405), combat auto-equip on pass-loot grouped multi-monster (~line 8879). Each now branches on `teammate.IsCompanion`: companions go through `SyncCompanionEquipment` as before, non-companion team NPCs go through `SyncNPCTeammateToActiveNPCs` which copies the equipment + base stats + bag contents from the orphaned combat reference to the canonical NPC in `ActiveNPCs`.

The save calls that follow (`AutoSave` + `SaveAllSharedState`) were already in place; they just needed the canonical NPC to actually have the new equipment when serialization ran.

---

## Bug fix: Hidden-status auto-crit didn't fire on basic [A]ttack

Player report (Lv.9 Half-Elf Cyclebreaker): *"Cyclebreaker, skill Temporal Feint does not seem to be auto-critting, but it may be a misunderstanding of the function on my part. Maybe training it increases the auto-crit chance? not sure yet."*

Not Cyclebreaker-specific, not training-gated. The Hidden status (set by Cyclebreaker Temporal Feint, Abysswarden Umbral Step, Assassin shadow / vanish, basic [H]ide, and Bard Blood Pact) is what delivers the "next attack auto-crits" payoff. Power Strike, class abilities, and PvP basic attacks all consume Hidden correctly. The PvE basic-swing code path at `ProcessPlayerActionMultiMonster` never checked `HasStatus(StatusEffect.Hidden)` -- it only rolled natural-20, dex-crit, and the Wavecaller Ocean's Voice bonus. So Hidden ticked down from 2 to 1 to 0 over two rounds without ever firing the auto-crit if the player followed up with a plain `[A]ttack`. Auto-hit (`TempAttackBonus += 999`) DID fire correctly, so swings landed -- they just never crit.

Affected every class with an auto-crit-setup ability:

- Cyclebreaker -- Temporal Feint
- Abysswarden -- Umbral Step
- Assassin -- shadow, vanish
- Bard -- Blood Pact
- Universal -- basic [H]ide

All worked when followed up with Power Strike / Backstab / class ability. None worked when followed up with a basic swing.

`PlayerVsMonster` delegates to `PlayerVsMonsters` for single-monster combat, so this single fix covers both single and multi-monster PvE.

### Fix

Added a `stealthCrit` check at the top of the multi-monster basic-attack crit logic, mirroring the Power Strike pattern. Hidden status removed and auto-crit applied immediately. Subsequent natural-20 and dex-crit rolls short-circuit on `stealthCrit` so we don't double-apply (the v0.57.2 ability path lesson -- guaranteed crits should not stack with rolled crits).

---

## Bug fix: silently swapping a two-handed weapon when picking off-hand for a one-handed item

Player report (Lv.9 Half-Elf Cleric, online): *"Aren't staves and quarterstaves two-handed? I somehow could equip a dagger in my off hand while wielding a quarterstaff."*

Investigation: the reporter's save and combat logs show MainHand was a Quarterstaff of Flames with `Handedness = TwoHanded, WeaponType = Staff` (correctly flagged), and every `[COMBAT_ATTACK]` log entry has `offhand=False` -- nothing had actually been in the off-hand during combat. So nothing was actually broken about staves OR the player's state. What they saw was the equip prompt offering Off-Hand as a slot choice, picking it, and the game **silently auto-unequipping the Quarterstaff** to the inventory before slotting the dagger. The "Moved Quarterstaff to inventory" line printed in gray after the equip and was easy to miss.

The auto-unequip code at `Character.EquipItem:916` had a comment "Handle shields/off-hand" -- the original intent was clearly the sword-and-board swap (player wielding 2H weapon picks up a shield, game auto-unequips the 2H to allow the shield in). For shields that's a one-click operation everyone wants. But the same code fired for one-handed weapons going to off-hand too, producing the surprising silent staff drop.

### Fix

Split the check by handedness:

- **`OffHandOnly`** (shields, bucklers, tower shields) -> keep the auto-unequip-2H. Sword-and-board swap remains one click.
- **`OneHanded`** (daggers, swords, etc.) -> **refuse** with a clear message naming both weapons: "Cannot equip Steel Dagger to off-hand while wielding Quarterstaff of Flames. Unequip Quarterstaff of Flames first, or equip Steel Dagger to main hand." Player has to deliberately unequip the 2H if they want to set up dual-wield. No more silent drops.

Defense in depth on the prompts too: both `InventorySystem.PromptForWeaponSlot` and `HomeLocation.PromptForWeaponSlotHome` now display a yellow `(unavailable while wielding two-handed)` annotation next to the `(O) Off Hand: empty` line when MainHand has a 2H weapon, so the player sees the constraint before picking the slot. The prompts still let you press [O] (the EquipItem refusal handles it cleanly with the descriptive message), but the visual warning means picking [M] or [C] is the obvious move.

Two new loc keys per language (en/es/fr/it/hu): `equip.cannot_offhand_with_2h` and `equip.offhand_blocked_2h`.

---

## Bug fix: music shop buff message and /health line don't say what the song actually does

Player report (Lv.9 Half-Elf Cleric, online): *"When you buy a performance, the game says something like: 'The performance stirs something in you. +15% for 5 combats.' The game doesn't write it explicitly what's that 15%. I mean we could select it from the menu and there we see what we select, but the game should write '15% defense for 5 combats' instead."*

Confirmed. Each Music Shop song affects a different stat: War March = +15% attack damage, Lullaby of Iron = +15% defense, Fortune's Tune = +25% gold from kills, Battle Hymn = +10% attack damage AND +10% defense. The buy-time message at `MusicShopLocation:992` and the `/health` Active Buffs line at `BaseLocation:6011` both omitted what the percent applied to. The pre-purchase shop menu has the descriptions but once the player committed and read the post-purchase confirmation, they had to remember which one they bought.

### Fix

Added a per-song effect descriptor (`song_effect_war_march` / `_iron` / `_fortune` / `_hymn`) and threaded it through:

- The buy-time message now reads: `The War March stirs something in you. (+15% attack damage for 5 combats)`
- The `/health` Active Buffs line now reads: `War March: +15% attack damage (5 combats)` instead of the previous `War March (5 combats)`

`music_shop.buff_gained` template gained a `{3}` slot for the effect descriptor, and four new descriptor keys per language. Battle Hymn shows `+10% attack damage and defense` since it stacks both effects.

---

## Bug fix: dual-wield ability follow-up strike printed "Off-hand strike at You!"

Player report (Lv.9 Half-Elf Cleric, online): *"When you use Divine Shite [Cleric Holy Smite] with weapons in both hands, the game writes 'offhand strike at you!' instead of the enemy. It hits the spider correctly though."*

Confirmed. `ApplyAbilityEffectsMultiMonster` at `CombatEngine.cs:12964` was passing the wrong arguments for the player branch:

```csharp
Loc.Get(isPlayer ? "combat.off_hand_strike_at" : "combat.off_hand_strike_npc", actorName, offHandTarget.Name)
```

The two loc templates have different placeholder counts:

- `combat.off_hand_strike_at` -> `"Off-hand strike at {0}!"` (one placeholder)
- `combat.off_hand_strike_npc` -> `"{0} follows up with an off-hand strike at {1}!"` (two placeholders)

The call passed both args unconditionally. For the player branch, `actorName` (which is the literal string `"You"` per the actor-name init at line 12752) filled `{0}`, and `offHandTarget.Name` was silently dropped. Result: "Off-hand strike at You!". The damage path was unaffected -- the spider took the hit correctly -- so this was a pure text bug.

Affected every melee class ability that triggers an off-hand follow-up while dual-wielding: Holy Shite (Cleric), Backstab (Assassin), Power Strike, Riptide Strike, all of them. The single-monster path at `CombatEngine.cs:20704` was already correct (single-arg call to `combat.offhand_strike_at`). But `PlayerVsMonster` delegates to `PlayerVsMonsters` for single-enemy fights, so even one-on-one combat hit the buggy multi-monster path.

### Fix

Branched the call cleanly so each loc key gets its expected argument count:

```csharp
if (isPlayer)
    terminal.WriteLine(Loc.Get("combat.off_hand_strike_at", offHandTarget.Name));
else
    terminal.WriteLine(Loc.Get("combat.off_hand_strike_npc", actorName, offHandTarget.Name));
```

Player now sees "Off-hand strike at Forest Spider!" as expected. NPC teammate behavior unchanged.

(Side note: there are two near-duplicate loc keys -- `combat.offhand_strike_at` and `combat.off_hand_strike_at` -- with identical text, used by different code paths. Not deduplicating in this fix; out of scope.)

---

## Bug fix: weapon enchant procs leak across hands during dual-wield

Player report (Lv.9 Half-Elf Cleric, online): *"If you wield a staff and a siphoning dagger, both the main hand and the off hand attacks siphon mana."*

Confirmed and broader than mana steal. Three weapon-source enchant categories were querying equipment incorrectly:

1. **Mana Steal (Siphoning)** -- `ApplyPostHitEnchantments` called `attacker.GetEquipmentManaSteal()` which sums `ManaSteal` across **every** equipped slot. So an off-hand Siphoning dagger added its 8% to the sum, and `EveryHit * 8% = mana siphoned every swing` regardless of which hand actually struck.

2. **Lifedrinker (Equipment Lifesteal)** -- same shape via `GetEquipmentLifeSteal()`. Off-hand Lifedrinker proc'd on main-hand swings too. Stacked Lifedrinker on both hands also double-applied (capped at 60% per swing summed, but every swing got the full sum).

3. **Elemental enchants (Fire / Frost / Lightning / Poison / Holy / Shadow)** -- inverse bug. `CheckElementalEnchantProcs` hardcoded `attacker.GetEquipment(EquipmentSlot.MainHand)`, so off-hand elemental enchants **never procced at all**, and main-hand enchants procced on every swing including off-hand strikes.

The reporter's specific scenario (staff + siphoning dagger) triggered #1. The 2H/off-hand UX fix earlier in this same release prevents the staff+dagger config going forward, but a player with a Lifedrinker greatsword + later swap to dual one-handers would still hit the same bug.

### Fix

Threaded a new `EquipmentSlot weaponSlot = EquipmentSlot.MainHand` parameter through `ApplyPostHitEnchantments` and `CheckElementalEnchantProcs`. Inside both, the weapon-source enchant reads now use `attacker.GetEquipment(weaponSlot)` instead of `GetEquipmentManaSteal()` / `GetEquipmentLifeSteal()` / hardcoded MainHand:

- **Mana Steal**: `weapon.ManaSteal` (single weapon)
- **Lifedrinker**: `Math.Min(MaxEquipmentLifeStealPercent, sourceWeapon.LifeSteal)` (single weapon, per-weapon cap matches previous sum-cap semantics)
- **Elemental enchants**: `weapon.HasFireEnchant`, `weapon.HasFrostEnchant`, etc. now read from the source weapon

Armor-side passives (Thorns, HP regen, Mana regen, magic resist, crit chance / crit damage stacking) still sum across all slots -- those aren't per-strike so the multi-slot accumulation is correct.

Six call sites updated to pass the slot explicitly when off-hand context is in scope:
- `ExecuteSingleAttack` (PvP / standard combat) -- passes `isOffHandAttack ? OffHand : MainHand`
- Player basic-attack swing loop in `ProcessPlayerActionMultiMonster` -- same
- Multi-monster ability off-hand follow-up -- passes `OffHand`
- Single-monster ability off-hand follow-up -- passes `OffHand`
- Two teammate basic-attack swing loops -- pass `isOffHandAttack ? OffHand : MainHand`

Other call sites use the default (`MainHand`), which is correct for them. Backward-compatible signature change -- existing callers don't need updates unless they want off-hand routing.

Net effect for the reporter's scenario: each strike now only proc's the enchants on the weapon that actually swung. Main-hand staff strikes ignore the off-hand Siphoning. Off-hand dagger strikes proc Siphoning normally. Same for any future Lifedrinker / elemental enchant builds.

Latent gain: off-hand fire / frost / lightning enchants will now actually proc on off-hand strikes for the first time. Previously they were silent (only main-hand checked).

---

## Bug fix: monster ability messages addressed the player when the target was a teammate

Player report (Lv.11 Cleric): *"if sticky webbing hits a companion, the game writes: 'the spider traps you in sticky webbing!'"*

Confirmed and broader than the one ability the reporter happened to spot. `MonsterAbilities.ExecuteAbility(AbilityType, Monster, Character)` is called from two combat paths: the player-targeted attack path (`CombatEngine.cs:4891`) and the companion/team-NPC-targeted path inside `MonsterAttacksCompanion` (`CombatEngine.cs:17846`). The function builds a `result.Message` string up front, and 30+ ability messages hardcoded "you"/"your" without consulting the target. Player path read fine, companion path read like the monster was hitting the player.

Affected abilities (anything with a written `you`/`your` line): WebTrap (the reported one), PetrifyingGaze, Silence, Enfeeble, Devour, SoulReap, Paralyze, DragonFear, Teleport, Hellfire, Corruption, Dominate, Burn, Immolate, Corrosion, Engulf, Absorb, Madness, Poison, Sleep, Charm, RootEntangle, DivineJudgment, StrengthDrain, Terror, Possess, Nightmare, DevourSoul, plus the LifeDrain "drains your life force" line. ~30 messages total.

### Fix

At the top of `ExecuteAbility`, two locals derive from the target:

```csharp
bool isPlayerTarget = target is Player;
string you = isPlayerTarget ? "you" : target.Name;
string your = isPlayerTarget ? "your" : $"{target.Name}'s";
```

Every hardcoded "you"/"your" in the ability messages was swapped to `{you}` / `{your}` interpolation. The Possess case (which previously read "possesses you, you strike yourself") gets a conditional branch so the NPC variant reads "possesses {name}, who strikes themself" with correct verb conjugation. Reflexive pronouns aren't fully gendered (we'd need NPC pronoun data for him/her/them), but "themself" reads neutrally and matches MUD/RPG convention.

Net effect for the reporter's scenario: when the spider's WebTrap lands on the player's companion, the line now reads "the spider traps Mira in sticky webbing!" instead of falsely addressing the player. Same correction across every other targetable monster ability.

---

## Bug fix: monster status-application on a teammate now surfaces the resist outcome

Player report (Lv.11 Cleric): *"If a spider traps a companion in webbing, it seems the entrapment doesn't respect companion abilities, Aldric still could use their abilities in the next round."*

Two layered issues. (1) WebTrap (and most monster status abilities) prints a flavor message at the top of `MonsterAttacksCompanion` ("the spider traps Aldric in sticky webbing!") and only AFTER that rolls the `StatusChance` to decide whether the Stunned status actually applies. WebTrap is 45%, so over half the time the flavor reads as if the bind landed but the stun never actually applied. The player then watches Aldric act normally next round and concludes the engine ignored the stun. (2) The companion-side status-resist chain at `CombatEngine.cs:17925` only checked `HasStatusImmunity` and `CalmWatersRounds`, but the player-equivalent chain at `:5002` also checks Cyclebreaker Probability Manipulation (25% debuff resist) and Paladin Divine Resolve (15% status resist). Cyclebreaker / Paladin / Paladin-companion teammates (Aldric is a Paladin companion) were missing their class passive on the monster-targeted side.

### Fix

`MonsterAttacksCompanion`'s status block now mirrors the player path's full resist chain (`HasStatusImmunity` -> Cyclebreaker -> Paladin -> Calm Waters -> chance roll -> apply OR fail message). When the chance roll fails, a new `combat.companion_shrugs_off_status` line prints in gray so the player sees "Aldric shrugs off the stunned!" right after the flavor line, instead of silently moving on. The player-target path at `:5024` gets the same treatment via a new `combat.shrug_off_status` line ("You shrug off the stunned!"). Both Cyclebreaker / Paladin passive resists land in the companion path too via two new loc keys (`combat.companion_probability_negates`, `combat.companion_divine_resolve`).

After fix: a 45% WebTrap that misses on Aldric now prints "the spider traps Aldric in sticky webbing!" immediately followed by "Aldric shrugs off the stunned!", so the player understands why Aldric acts next round. A 45% WebTrap that lands prints the existing "Aldric is afflicted with Stunned!" line and Aldric skips next round (which was already working correctly).

---

## Bug fix: Crypt rooms no longer appear in non-tomb-themed floors

Player report (Lv.15 Cleric, floor 15): *"should the epitaphs and stuff from the crypt level seep through to the sewer theme?"*

`DungeonGenerator.GenerateRooms` had `RoomType.Crypt` x2 hardcoded into the standard room pool used by every theme. So every Sewers floor (levels 11-20) consistently surfaced two "Flooded Tomb" rooms whose description read "The sewers broke through into this ancient crypt long ago. Water-logged coffins float in the murk." The localized strings tried to bridge the theme ("crypt that sewers broke into"), but the bridge was weak and the crypt language jumped out. Same kind of mismatch was technically present for Caverns, DemonLair, FrozenDepths, VolcanicPit, and AbyssalVoid, but their existing per-theme bridges were stronger ("Cremation Hall" in fire, "Petrified Remains" in caverns) and players hadn't flagged them.

### Fix

Crypt rooms now only appear in **Catacombs** and **AncientRuins** floors -- the two themes where tomb/crypt content lands naturally ("Noble's Crypt" and "Tomb of the Artificers"). For all other themes, the two Crypt slots in the standard pool are replaced with one Chamber + one Alcove, so floor density and room count stay constant. The localized Crypt flavor for the deprecated themes is left in place untouched (no string changes in any of the 5 languages) -- those branches are simply unreachable from generation now.

Existing Sewer floors already generated for a save persist as-is (DungeonFloor states are stored per-player); new sewer floor generations from this point forward won't have Crypt rooms. Floors auto-respawn an hour after last visit (see v0.57.10 fix), but the deterministic seed will produce a different layout under the new pool, so players who retreat-and-return will see the change on their next visit after the respawn timer expires.

---

## Bug fix: Frostbite enchant applies real Slow instead of a permanent Defence shave

Player report (Lv.83 Troll Warrior): *"Frostbite says chance to apply slow but instead it just decreases defences, and by a small amount."*

Confirmed and the bug was bigger than it read. The Magic Shop description for the Frostbite enchant is "+20 power + chance to slow enemies". The actual implementation at 4 sites in CombatEngine (`CheckElementalEnchantProcs`, `CheckElementalEnchantProcsMonster`, plus two Shaman Frostbrand totem proc sites that were copy-pasted from the same broken pattern) ran `target.Defence = Math.Max(0, target.Defence - 3)` per proc. Three problems compounding:

- **Permanent stat mutation, not a status.** The Defence field was being mutated directly with no restoration on combat end or status tick. A 15%-per-attack proc against multi-attack / dual-wield builds stacked into double-digit permanent Defence reductions over a single fight.
- **Wrong stat.** The constant was named `FrostEnchantAgiReduction` (= 3) -- the original design intent was to lower AGI, which would feel like a slow. Someone implemented it as Defence shave by mistake. AGI affects monster speed; Defence affects how much damage they take.
- **No actual slow.** Monster.IsSlowed / SlowDuration exist and are properly consumed by `CombatEngine.cs:4266` to halve the monster's attack count per round. The slow status was never being applied -- the engine had the plumbing all along, just no caller for the enchant path.

### Fix

All 4 sites now run `target.IsSlowed = true; target.SlowDuration = Math.Max(target.SlowDuration, GameConfig.FrostEnchantDuration)`. Repeat procs refresh the duration to 2 rounds; don't shorten an existing longer slow. This mirrors the pattern the `"frost"` spell case at `:15665` and various ability paths (Riptide Strike, etc.) already use. `Monster.IsSlowed` consumption in the action-processing path was already wired and ticking correctly -- nothing else needed to change.

The `FrostEnchantAgiReduction = 3` constant is removed (was only referenced by the 4 broken sites). Localized flavor strings updated in all 5 languages: `combat.enchant_frost` and `combat.enchant_frost_multi` now read "{target} is slowed!" instead of "{target}'s defence reduced!" to match the new behavior.

Knock-on benefit: the Shaman Frostbrand temporary-weapon-enchant ability also gets the fix (its two proc sites were copy-pasted from the regular Frostbite enchant with a comment that explicitly said "matching regular frost enchant"). Shamans casting Frostbrand now apply real Slow to their targets.

---

## Bug fix: weapon enchant proc messages now attribute the attacker

Player report (Lv.20 Cleric, party with Aldric / Lyris / Mira / Vex / Melodia): *"I have a frost staff, Lyris a lightning bow, and Aldric a darkness sword. Somehow Lyris can use the frost effect sometimes, and Aldric can use holy radiance."*

Not a real leak. Both fire and lightning enchant proc messages already differentiated player from teammate (`isPlayer ? Loc.Get("combat.enchant_fire") : "Flames erupt from {name}'s weapon!"`), but the other four enchants (frost, poison, holy, shadow) didn't. So when a teammate's weapon procced frost / poison / holy / shadow, the message read "Frost spreads from the impact!" / "Divine light strikes true! (Radiance)" / etc. with no attribution, and the player saw those messages during a teammate's attack phase and assumed their own enchant had leaked across characters.

The actual mechanic was working correctly: `CheckElementalEnchantProcs` was reading `attacker.GetEquipment(weaponSlot)` -- the attacker's own weapon. Teammates fire their own enchants from their own weapons. The player's confusion came from teammates having stacked enchants on their weapons (multi-effect dungeon loot drops can carry several elemental flags, and Magic Shop enchanting can stack additional enchants on existing weapons) combined with no attacker attribution in 4 of the 6 proc messages.

### Fix

The four inconsistent enchant types (frost, poison, holy, shadow) now mirror the fire/lightning pattern. When the attacker is a teammate, the message uses a new `_tm` variant that includes `{attacker_name}` in the flavor:

- Frost: "Frost spreads from Lyris's strike! Goblin is slowed! (Frostbite)"
- Poison: "Lyris's blade weeps venom! +12 poison damage! (Venom)"
- Holy: "Divine light strikes from Aldric's weapon! +18 holy damage! (Radiance)"
- Holy vs undead: "Holy light sears the undead from Aldric's weapon! +36 radiant damage! (Smite)"
- Shadow: "Shadows coil from Aldric's blade! +15 shadow damage! (Darkstrike)"

Five new loc keys per language (`combat.enchant_frost_tm`, `_venom_tm`, `_holy_tm`, `_holy_undead_tm`, `_shadow_tm`). The existing player-facing keys (`combat.enchant_frost`, etc.) rewritten slightly to use "your strike" / "your blade" wording so the player vs teammate split reads naturally.

After fix, the player can immediately tell whose weapon procced which enchant. If frost procs while Lyris attacks, the message names Lyris -- making it clear Lyris's bow has the frost enchant (in addition to whatever else), not that the player's staff is leaking. Same for Aldric's sword carrying holy alongside shadow.

---

## Bug fix: monster defensive self-buff abilities now fall through to a normal attack against companions

Player report (Lv.20 Cleric, floor 20): *"When a shadow uses its ability which makes it flicker between planes, that one doesn't hit companions. As far as I recall that's a damaging ability."*

"Flicker between planes" is `AbilityType.Incorporeal` (undead self-buff: 30% chance to phase out and avoid all damage; "X flickers between planes!" / "X becomes incorporeal -- attacks pass through!"). It's not a damaging ability -- it sets `DamageMultiplier = 0`. But the design intent is that defensive self-buffs are layered ON TOP of a normal attack: the monster phases out AND swings normally. Many monster abilities are written this way (Vanish, Phase, Flight, Invisibility, TreeMeld, Stoneskin, ArmorHarden, plus Regeneration / Heal which mark themselves IsSelfOnly) -- they buff the monster without consuming its turn, so the normal attack still fires.

The player-targeted path at `TryMonsterSpecialAbility:5159` returns a `usedSpecialAbility` flag that's true only when the ability "consumed the turn" (`SkipNormalAttack` set, or it dealt damage / drained mana / stole life). The caller falls through to the basic-attack code when that returns false. So when Incorporeal fires on the player: phase out, then normal attack hits the player.

The companion-targeted path at `MonsterAttacksCompanion:17890` was structurally different: after the ability block resolved, it unconditionally ran `if (companion.IsAlive) return;`. That short-circuits the normal-attack section that follows in the same function -- so for every defensive-self-buff ability the monster picked, the companion took zero damage that turn while the player got hit.

### Fix

Mirror the player-path "did the ability consume the turn?" check in `MonsterAttacksCompanion`. Replace the unconditional return with:

```csharp
bool abilityConsumedTurn = abilityResult.SkipNormalAttack
    || abilityResult.DirectDamage > 0
    || abilityResult.ManaDrain > 0
    || abilityResult.LifeStealPercent > 0
    || (abilityResult.DamageMultiplier > 0 && abilityResult.LifeStealPercent == 0);
if (abilityConsumedTurn) return;
// Otherwise fall through to the normal attack below.
```

Now an Incorporeal-using shadow correctly phases out AND swings at the companion, matching the player path. The "monster attacks companion!" header (already tracked via `shownAttackMessage`) prints once, the ability message prints, then the normal damage line follows. Status checks at the normal-attack section (Distracted miss roll, dodge, defense computation) all run correctly for the fall-through case.

The boss-fallback branch (line 18016+) intentionally still returns -- bosses use custom ability strings that map to bespoke damage handlers, not the `MonsterAbilities.ExecuteAbility` path. Death short-circuit (`if (!companion.IsAlive) goto CompanionDeathCheck;`) preserved so dead companions don't take a duplicate normal attack.

Affected monster abilities (defensive self-buffs, no SkipNormalAttack, no damage): Incorporeal, PhaseShift, Phase, Vanish, Flight, Invisibility, TreeMeld, ArmorHarden, Stoneskin, Regeneration, Thorns, and several Old-God-themed entries. Player-side coverage was already correct; this fixes the companion side.

---

## Bug fix: monsters now carry the correct MonsterClass (Turn Undead works on Ghouls)

Player report (Lv.22 Cleric, floor 22): *"I used turn undead on a ghoul and the game says ghoul is not an undead."*

Root cause was an audit gap, not a Ghoul-specific bug. `MonsterGenerator` set `monster.FamilyName = family.FamilyName` ("Undead", "Demonic", "Beast", etc.) but never assigned `monster.MonsterClass` or the legacy `monster.Undead` int. Both fields stayed at their defaults (`MonsterClass.Normal`, `Undead = 0`). The Turn Undead gate at `CombatEngine:15609` reads them:

```csharp
bool turnUndeadOnLiving = spellResult.SpecialEffect == "turn_undead"
    && target.MonsterClass != MonsterClass.Undead
    && target.MonsterClass != MonsterClass.Demon
    && target.Undead == 0;
```

With all three fields at default, the spell concluded EVERY monster was "non-undead" and dealt zero damage with the unaffected message -- regardless of whether the FamilyName was "Undead" or anything else. Ghoul / Zombie / Wight / Wraith / Lich (the Undead family tiers) all read as "not undead" because the enum was never populated. The bug had broader knock-on impact too: ~20 other sites across CombatEngine check `MonsterClass == Undead || MonsterClass == Demon` for Holy enchant scaling, Paladin alignment bonus, Divine Smite, fire-vulnerability checks, Holy Spell handlers, and Plant/Beast checks for fire damage bonuses. All of them silently answered "no" for every monster.

### Fix

New `ApplyMonsterClassification(monster, familyName)` helper in `MonsterGenerator` that maps family names to `MonsterClass` enum values. Called from both monster-generation entry points (`GenerateMonster` and the multi-monster generation site). The full mapping:

- Undead -> Undead (+ legacy `Undead = 1`)
- Demonic -> Demon
- Draconic -> Dragon
- Beast / Insectoid / Aquatic -> Beast
- Elemental -> Elemental
- Construct -> Construct
- Aberration / Shadow -> Aberration
- Fey -> Plant
- Celestial / Giant / Goblinoid / Orcish -> Humanoid
- default -> Normal

Now Turn Undead on a Ghoul reads `MonsterClass == Undead` -> true -> spell deals full damage and prints the holy-fire kill flavor. Same fix unblocks ~20 other undead/demon-targeted mechanics that were silently no-ops since monsters never carried their class. Existing already-spawned monsters on a player's current floor stay misclassified (state was already serialized), but every new monster generation (floor entry, respawn, world boss, NG+ etc.) gets the correct classification.

The mapping uses the "best fit" for non-Undead/Demon families opportunistically -- some of the choices (Fey -> Plant) are loose, but they're only consulted by a single fire-vulnerability check today and no class-keyed behavior was depending on those values pre-fix. The load-bearing ones (Undead, Demon, Dragon) match D&D-style conventions.

---

## Bug fix: Love Street paid encounters rebalanced (darkness gain + prices)

Player report (Lv.23 Cyclebreaker): *"On the love corner, sleeping with the most expensive lady of the night gives +555 darkness, for like... 10,000 gold. Haven't tried it twice in a row yet, but that's basically a full alignment swap for 20,000."* (Actual top-tier price was 100,000 gold, not 10,000 -- but the point stands: a single visit could move alignment ~800 net points combining paired chivalry/darkness movement.)

The alignment plumbing was already correct -- `LoveStreetLocation.GiveDarkness` routes through `AlignmentSystem.ChangeAlignment` with paired movement (the v0.57.12 audit). The actual issues were that the darkness ranges per visit were too large and the prices were too cheap relative to the alignment shift they produced.

Pre-fix darkness brackets by price tier:
- <= 1,000 gold: 15-45 darkness
- <= 5,000: 25-85
- <= 20,000: 50-200
- <= 50,000: 100-300
- > 50,000: 150-650

Top-tier visit at 100,000 gold could roll up to 650 darkness with paired -325 chivalry penalty, swinging a balanced player's alignment by 975 net points in one transaction. With AlignmentCap = 1000, two visits put a Saint character into Dark Lord territory for 200,000 gold total -- trivial endgame.

### Fix (two layers)

**Prices doubled** across the Courtesans and Gigolos rosters. Lowest tier (Elly / Signori) 500 -> 1,000; middle (Elynthia / Merson at 10k) -> 20,000; top tier (Loretta / Banco) 100,000 -> 200,000. Every tier scaled the same way.

**Darkness ranges cut sharply** in `ShowIntimateEncounter`, with brackets re-tiered to match the doubled prices:

| New price tier | Old darkness | New darkness |
| --- | --- | --- |
| <= 2,000 | 15-45 | 5-15 |
| <= 10,000 | 25-85 | 8-25 |
| <= 40,000 | 50-200 | 15-50 |
| <= 100,000 | 100-300 | 25-80 |
| > 100,000 | 150-650 | **40-120** |

Top-tier visit at 200,000 gold now rolls up to 120 darkness with paired -60 chivalry, net 180-point alignment swing. ~10x more gold per net alignment point compared to pre-fix. Player can still use Love Street for RP / romance / completeness without it being the most cost-efficient evil-alignment lever in the game.

XP reward brackets re-tiered the same way so XP-per-visit is preserved at each tier. Scene-quality flavor tiers (Premium / High-End / Standard / Basic) also re-tiered to match the doubled prices so the same partners get their original scene flavor -- Loretta and Banco still get Premium, Arabella and Jake still get High-End, etc.

**Daily cap added** (`GameConfig.MaxLoveStreetVisitsPerDay = 2`). Counts Courtesan + Gigolo visits combined. New `Character.LoveStreetVisitsToday` counter with full save / load / daily-reset plumbing (mirrors `MurdersToday`, `TeamWarsToday`, `DrinkingGamesToday` from prior versions). Both `EngageWithCourtesan` and `EngageWithGigolo` check the cap before showing the price prompt; the counter increments inside `ShowIntimateEncounter` only after the transaction actually consummates (cancellations and "can't afford" paths don't tick). `ShowCourtesanMenu` / `ShowGigoloMenu` display the remaining-visits count up front so players see the cap before clicking through a partner intro. Two new loc keys per language (`love_street.daily_visits_remaining`, `love_street.daily_cap_reached`).

Combined cap behavior: max 2 visits/day at the top tier = up to 240 darkness + 120 chivalry penalty net per day from paid encounters, comparable to the murder cap's 750 + 375 ceiling. Endgame players can no longer rinse-and-repeat their alignment via 5-minute Love Street sprees.

---

## Bug fix: monster-attack damage display read as three contradictory messages

Player report (Lv.22 Cleric, floor 22): *"The angel swings wild! 0 damage vs 143 defense. 1 damage got through. Something is amiss here but I don't know why. The angel flow upwards, becoming harder to hit just before the attack. The dodge and the attack was the same action against me."*

Two underlying bugs feeding one confusing user experience.

**(1) `monsterAttack` could truncate to 0 after damage multipliers.** `Monster.GetAttackPower()` floors at 1, but the post-roll pipeline runs `monsterAttack = (long)(monsterAttack * 0.80)` for Earthbind Totem and a similar `(long)(monsterAttack * difficultyMultiplier)` from `DifficultySystem.ApplyMonsterDamageMultiplier` on Easy difficulty. `(long)(1 * 0.80)` = 0 via integer truncation. So a weak monster (low STR / Strength) hitting on a successful D20 roll could end up with `monsterAttack == 0` after the multiplier chain.

**(2) Three message paths fired off the same attack, contradicting each other.** When `monsterAttack` hit 0:
- The flavor verb message at line 4473 calls `GetMonsterAttackMessage(monster, monsterAttack, playerMaxHP)`. `GetDamageTier` returns `Miss` for damage <= 0 -> "swings wildly!" verb.
- The `[combat.damage_vs_defense]` line at 4632 prints unconditionally -> "[0 damage vs 143 defense]".
- `actualDamage = Math.Max(GetMinIncomingDamage(player, 0), 0 - 143)` -> minimum floor of 1 lands -> "Angel hits you for 1 damage".

Player sees the three lines in sequence and reasonably concludes the engine is broken: "miss" + "0 vs 143" + "1 damage got through" is nonsense.

**(3) The Flight ability fired as part of the same monster turn.** Per the v0.60.10 design (separate fix in this same release), defensive self-buff abilities like Flight (`DamageMultiplier=0`, no `SkipNormalAttack`) layer ON TOP of the basic attack. Player sees "The Angel takes flight, becoming harder to hit!" immediately followed by the basic-attack messages. Without visual separation between the two, they read as one action -- the player thought the Flight ability was the attack.

### Fix (three layers)

**Clamp monsterAttack to >= 1 after multipliers** (`CombatEngine.cs:4466`). A successful D20 hit can no longer produce a 0 monsterAttack via integer truncation. The basic-attack pipeline now always has at least 1 damage to work with, so the tier-flavor message is at least Graze ("scratches you" / "barely touches you") which matches the 1-damage outcome.

**Gate `damage_vs_defense` on `playerDefense > 0 && playerDefense < monsterAttack`** (line 4632). Mirrors the player-attacks-monster path at `ApplySingleMonsterDamage:11321` which has had this gate forever. Now the line only fires when armor actually reduced the damage and didn't fully absorb it. The misleading "[0 damage vs 143 defense]" case goes away.

**Brief pause between defensive ability and followup basic attack** (`TryMonsterSpecialAbility:5159` and `MonsterAttacksCompanion:18029`). When a defensive self-buff ability fires but doesn't consume the turn (so the basic attack still happens), insert a 600ms beat between the ability flavor and the basic-attack messages. The Flight ability message now reads as a separate beat from the followup swing instead of running together.

After all three fixes, the player sees:
```
The Angel takes flight, becoming harder to hit!
  (brief pause)
The Angel scratches you!
The Angel hits you for 1 damage!
```

Coherent across all messages.

---

## Bug fix: action-preventing statuses cleared on combat end (player + teammates)

Player report (Lv.22 Cleric, floor 22): *"Stunning from monster abilities doesn't clear upon ending combat on companions."*

The end-of-combat cleanup at `PlayerVsMonsters` only removed BUFF-style statuses (`Protected`, `Blessed`, `Haste`, `Reflecting`) -- it didn't touch the action-preventing control effects (`Stunned`, `Paralyzed`, `Sleeping`, `Frozen`) or `Slow`. Three knock-on issues:

1. If a monster stunned a teammate near the end of a fight and the duration hadn't fully ticked (e.g., a 2-round Stunned applied in round 5 of a 6-round fight), the status persisted past combat end. Player saw Aldric still flagged as Stunned in `/health` between combats and watched him lose his first turn in the next fight to a stun that landed in the previous one.

2. The teammate cleanup loop existed at combat START for transient buff fields (TempAttackBonus / MagicACBonus / DodgeNextAttack / etc.) but did NOT touch the `ActiveStatuses` dictionary either. So even with combat-start init, the leaked Stunned/etc. entries survived.

3. Same gap applied to the player path -- their stuns also leaked into between-combat menus / next combats, but players usually saw the effect resolve on their own next turn (they take more turns than teammates in a given fight, so their stun ticks completed more often).

### Fix

End-of-combat cleanup at `PlayerVsMonsters` now also removes `Stunned`, `Paralyzed`, `Sleeping`, `Frozen`, `Slow` from the player, and iterates `result.Teammates` to do the same plus the full buff-status set (`Protected` / `Blessed` / `Haste` / `Reflecting`) on each teammate. Combat-start init mirrors the same scrub on both player and teammates as defense in depth -- so even if a status somehow leaks past end cleanup (mid-combat disconnect, save/load edge case), the next fight begins with a clean slate.

DoT statuses (`Poisoned`, `Bleeding`, `Burning`, `Diseased`) are deliberately left alone. They have separate persistent fields (`Character.Poison`, `PoisonTurns`, `HasDisease`) for out-of-combat tracking; the in-status dictionary entries expire on next-combat ticks via `ProcessStatusEffects` and don't cause visible leak symptoms.

After fix: combat ends -> all action-preventing statuses scrubbed from player + every alive teammate -> `/health` reads clean -> next combat starts with no leftover stuns.

---

## Files Changed

- `Scripts/Core/GameConfig.cs` -- Version 0.60.10.
- `Scripts/Systems/SaveSystem.cs` -- `SAVE_SEAL_AUDIT` log line per online save plus `SUSPICIOUS_FALLBACK_SAVE` warning when `SessionContext.Current` is null at the moment we read seals to persist.
- `Scripts/Systems/FeatureInteractionSystem.cs` -- `InteractWithFeature`, `HandleClassSpecific`, and `ApplyClassSpecificBonus` now accept an optional `List<Character>? teammates` parameter (default null, back-compatible). Cleric / Paladin case heals each alive teammate for 25% of their own MaxHP and emits a per-ally flavor line; falls back to the original self-heal when teammates is null or empty.
- `Scripts/Core/Character.cs` -- `EquipItem` off-hand path now branches on `item.Handedness`. Shields (`OffHandOnly`) keep the existing auto-unequip-2H behavior so sword-and-board swaps stay one click; one-handed weapons being slotted to off-hand while a two-handed weapon is wielded are now refused with a descriptive message instead of silently dropping the 2H.
- `Scripts/Locations/DungeonLocation.cs` -- Feature-interaction call site at the dungeon room menu now passes the active `teammates` list through.
- `Scripts/Locations/HomeLocation.cs` -- `PromptForWeaponSlotHome` shows an `(unavailable while wielding two-handed)` annotation next to the `(O) Off Hand` line when MainHand has a 2H weapon, mirroring `InventorySystem.PromptForWeaponSlot`.
- `Scripts/Locations/MusicShopLocation.cs` -- Performance buff message threads a per-song effect descriptor so the post-purchase confirmation and `/health` line read like "+15% attack damage" instead of just "+15%".
- `Scripts/Locations/BaseLocation.cs` -- `/health` Active Buffs section's song-buff line now shows the effect descriptor and percent alongside the song name + remaining combats.
- `Scripts/Systems/InventorySystem.cs` -- `PromptForWeaponSlot` shows the same 2H-conflict annotation when applicable.
- `Scripts/Systems/CombatEngine.cs` -- Three combat-loot equip-on-teammate paths now call `SyncNPCTeammateToActiveNPCs` for non-companion teammates so the canonical NPC carries the equipment forward through `SaveAllSharedState`. Companion teammates still route through `SyncCompanionEquipment` as before. Multi-monster basic-attack path now consumes `StatusEffect.Hidden` for an auto-crit, mirroring the Power Strike / class-ability pattern -- fixes Temporal Feint / Umbral Step / Backstab-via-stealth / vanish / shadow / Blood Pact / [H]ide all dropping their auto-crit when followed by a basic swing.
- `Scripts/Systems/MonsterAbilities.cs` -- `ExecuteAbility` derives `you` / `your` locals from `target is Player` and threads them into every ability message that previously hardcoded "you"/"your". Possess branches on `isPlayerTarget` for correct verb conjugation. Fixes ~30 monster-ability messages misaddressing the player when the actual target was a companion or team NPC.
- `Scripts/Systems/CombatEngine.cs` (status resist chain) -- `MonsterAttacksCompanion` status-application now mirrors the player-path chain (immunity, Cyclebreaker, Paladin, Calm Waters, roll, fail message). Adds explicit chance-roll-fail message to both companion path and player path so the resist outcome is visible after the ability's flavor line.
- `Localization/{en,es,fr,it,hu}.json` (additional keys) -- Four new keys per language: `combat.companion_probability_negates`, `combat.companion_divine_resolve`, `combat.companion_shrugs_off_status`, `combat.shrug_off_status`.
- `Scripts/Systems/DungeonGenerator.cs` -- `GenerateRooms` standard-room pool is now theme-aware: Catacombs and AncientRuins still get two Crypt rooms, all other themes get an extra Chamber + Alcove instead. Sewers / Caverns / DemonLair / FrozenDepths / VolcanicPit / AbyssalVoid floors no longer surface "Flooded Tomb" / "Cremation Hall" / etc. -- the existing localized flavor strings for those branches stay in place but are unreachable from generation.
- `Scripts/Systems/CombatEngine.cs` (Frostbite slow) -- 4 sites (`CheckElementalEnchantProcs`, `CheckElementalEnchantProcsMonster`, single-monster and multi-monster Shaman Frostbrand totem procs) now apply `IsSlowed` / `SlowDuration` instead of mutating `Defence` by -3.
- `Scripts/Core/GameConfig.cs` -- Removed `FrostEnchantAgiReduction` constant (orphaned by the Frostbite slow fix). Updated comment on `FrostEnchantDuration` to note the migration.
- `Localization/{en,es,fr,it,hu}.json` (Frostbite flavor) -- `combat.enchant_frost` and `combat.enchant_frost_multi` rewritten to say "X is slowed!" instead of "X's defence reduced!" in all 5 languages.
- `Scripts/Systems/CombatEngine.cs` (enchant attribution) -- Frost, poison, holy, shadow enchant proc messages now differentiate player vs teammate attacker, mirroring the fire/lightning pattern that was already in place. When a teammate procs, the new `_tm` loc variant includes the attacker's name so the player can tell whose weapon fired the enchant.
- `Localization/{en,es,fr,it,hu}.json` (enchant attribution) -- Five new keys per language: `combat.enchant_frost_tm`, `combat.enchant_venom_tm`, `combat.enchant_holy_tm`, `combat.enchant_holy_undead_tm`, `combat.enchant_shadow_tm`. Existing `combat.enchant_frost` rewritten to use "your strike" wording for player attribution symmetry.
- `Scripts/Systems/CombatEngine.cs` (companion ability follow-through) -- `MonsterAttacksCompanion` ability block no longer unconditionally returns after the ability resolves. Mirrors the player-path "did the ability consume the turn?" gate so defensive self-buff abilities (Incorporeal, PhaseShift, Vanish, etc.) correctly fall through to the monster's basic attack against the companion, instead of giving the companion a free pass while the player gets hit.
- `Scripts/Systems/MonsterGenerator.cs` -- New `ApplyMonsterClassification(monster, familyName)` helper mapping family name string onto the `MonsterClass` enum (+ legacy `Undead` int for Undead family). Wired into both monster-generation entry points. Pre-fix every monster shipped with `MonsterClass.Normal` regardless of FamilyName, so Turn Undead, Holy enchant scaling, Divine Smite, Paladin alignment bonus, fire-vulnerability checks, and ~20 other class-keyed mechanics all silently no-op'd.
- `Scripts/Locations/LoveStreetLocation.cs` -- Courtesan and Gigolo prices doubled (500-100,000 gold -> 1,000-200,000 gold). Darkness ranges in `ShowIntimateEncounter` cut by ~65-80% per tier (top bracket 150-650 -> 40-120) and re-tiered to match the new prices. XP brackets and scene-quality flavor tiers re-tiered the same way so existing partners get their original scene flavor. New 2-visit daily cap (`MaxLoveStreetVisitsPerDay`) with full save / load / daily-reset plumbing; both Engage* methods check the cap up front, `ShowCourtesanMenu` / `ShowGigoloMenu` show the remaining count.
- `Scripts/Core/Character.cs`, `Scripts/Core/GameConfig.cs`, `Scripts/Systems/SaveDataStructures.cs`, `Scripts/Systems/SaveSystem.cs`, `Scripts/Core/GameEngine.cs`, `Scripts/Systems/DailySystemManager.cs`, `Scripts/Editor/PlayerSaveEditor.cs` -- `LoveStreetVisitsToday` daily counter wired through the standard daily-counter pattern.
- `Localization/{en,es,fr,it,hu}.json` (Love Street caps) -- Two new keys per language: `love_street.daily_visits_remaining`, `love_street.daily_cap_reached`.
- `Scripts/Systems/CombatEngine.cs` (monster-attack display coherence) -- `monsterAttack` clamped to >= 1 after damage multipliers so successful hits can't truncate to 0 via Earthbind / difficulty multiplier. `damage_vs_defense` line gated on `playerDefense > 0 && playerDefense < monsterAttack` (mirroring the player-attacks-monster path). 600ms pause added between a defensive self-buff ability and the basic-attack fall-through in both player-targeted (`TryMonsterSpecialAbility`) and companion-targeted (`MonsterAttacksCompanion`) paths.
- `Scripts/Systems/CombatEngine.cs` (status cleanup on combat end) -- `PlayerVsMonsters` end-of-combat cleanup now also removes Stunned / Paralyzed / Sleeping / Frozen / Slow from the player, and iterates `result.Teammates` to do the same plus the buff-status set on each teammate. Combat-start init mirrors the same scrub on both player and teammates as defense in depth. DoT statuses (Poisoned/Bleeding/Burning/Diseased) deliberately left alone -- they have separate persistent fields.
- `Localization/{en,es,fr,it,hu}.json` -- Eight new keys per language plus one updated template: `feature.divine_heal_ally`, `feature.divine_heal_all_full`, `equip.cannot_offhand_with_2h`, `equip.offhand_blocked_2h`, `music_shop.song_effect_war_march`, `music_shop.song_effect_iron`, `music_shop.song_effect_fortune`, `music_shop.song_effect_hymn`. `music_shop.buff_gained` template extended with a `{3}` placeholder for the effect descriptor.
