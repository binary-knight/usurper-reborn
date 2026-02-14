# Usurper Reborn - v0.30.5 Spell Effects Overhaul & Equipment Fix

Every caster spell's SpecialEffect now works in multi-monster dungeon combat. Previously, the multi-monster spell handler only recognized 4 of 41 effect types (sleep, fear, stun, slow) — spells like Fireball, Ice Storm, Freeze, Poison Touch, Dominate, Steal Life, and many more dealt raw damage only with no status effects. AoE spells never applied any effects at all. Several self-buff spells (Mirror Image, Divine Avatar, Wish, Time Stop, Mind Blank) silently did nothing.

Also fixes a critical equipment corruption bug in online MUD mode where equipment would disappear from slots or wrong items would appear in wrong slots.

---

## Bug Fixes

- **[CRITICAL] Equipment disappearing / wrong items in slots (online MUD)**: In MUD mode, all player sessions share a single static `EquipmentDatabase`. When any player logged in, `ClearDynamicEquipment()` wiped the entire dynamic equipment registry — including equipment belonging to other currently-online players — and reset the ID counter to 100000. This caused: (1) other players' equipment lookups returning null, making items vanish from Body, Neck, Ring, and other slots; (2) newly registered equipment reusing IDs that other players' `EquippedItems` dictionaries still referenced, causing wrong items to appear in wrong slots (e.g., another player's longbow appearing in your ring slot). Fixed by skipping `ClearDynamicEquipment()` in online mode — each player's save data registers/overwrites only their own equipment IDs. Also added thread-safe locking to all `EquipmentDatabase` methods for concurrent MUD session safety.

- **~37 spell effects did nothing in dungeon combat**: `HandleSpecialSpellEffectOnMonster` only handled 4 of 41 effect types. Attack/debuff spells like Fireball (fire), Frost Touch (frost), Poison Touch (poison), Freeze (freeze), Web (web), Confusion (confusion), Dominate (dominate), Steal Life (drain), Power Word: Kill (death), Disintegrate (disintegrate), Mind Spike (psychic), Dispel Evil (dispel), and all holy spells silently applied no special effect — just base damage. All missing effects are now implemented.

- **AoE spells never applied effects**: Ice Storm, Chain Lightning, Holy Explosion, Meteor Swarm, Psychic Scream, Mass Confusion, and God's Finger all went through `ApplyAoEDamage` which had no spell effect parameter. These spells now apply their SpecialEffect to each surviving monster after dealing AoE damage.

- **Self-buff spells missing from handler**: Mirror Image, Invisibility, Shadow Cloak, Noctura's Veil, Divine Avatar, Wish, Time Stop, and Mind Blank all fell through the switch statement with no action. Their effects are now implemented.

- **Legacy handler used outdated monster properties**: Sleep set `StunRounds` instead of `IsSleeping`/`SleepDuration`. Fear set `WeakenRounds` and reduced STR instead of `IsFeared`/`FearDuration`. Slow set `WeakenRounds` instead of `IsSlowed`/`SlowDuration`. Convert used `StunRounds` for pacify instead of `IsStunned`/`StunDuration`. All now use the modern status properties added in v0.30.4.

---

## New Spell Effects (Multi-Monster Combat)

### Damage Over Time
- **Poison** (Sage: Poison Touch): Poisons target for 5 rounds
- **Fire** (Magician: Fireball, Pillar of Fire; Sage: Roast): Burns target for 2 rounds (DoT)
- **Frost** (Magician: Frost Touch, Ice Storm AoE): Slows target for 2 rounds

### Crowd Control
- **Freeze** (Sage: Freeze): Freezes target solid — cannot act
- **Web** (Magician: Web): Entangles target in webs — stunned for 2 rounds
- **Confusion** (Sage: Confusion): Target stumbles around confused for 3 rounds
- **Mass Confusion** (Sage: Mass Confusion AoE): All surviving monsters confused
- **Dominate** (Sage: Dominate): Breaks target's will — charmed and friendly
- **Convert** (Cleric: Dispel Evil): Holy conversion — monster may flee, become pacified, or fight for you

### Damage Amplifiers
- **Holy** (Cleric: Turn Undead, Holy Smite, Holy Word, Judgment, Holy Explosion AoE, God's Finger AoE): +50% bonus damage vs Undead and Demon monsters
- **Psychic** (Sage: Mind Spike, Psychic Scream AoE, Hit Self): 25% chance to confuse target for 1 round
- **Disintegrate** (Magician: Disintegrate): Reduces target defense by 25% for rest of combat

### Life Drain
- **Drain** (Sage: Steal Life, Energy Drain, Death Kiss): Heals caster for 50% of spell damage

### Instant Kill
- **Death** (Magician: Power Word: Kill; Sage: Death Kiss): 30% instant kill chance if target below 20% HP (never works on bosses)

### Utility
- **Dispel** (Cleric: Dispel Evil): Strips dark enchantments from target
- **Escape** (Sage: Escape): Flee combat instantly

---

## New Self-Buff Effects

- **Mirror/Invisible/Shadow** (Magician: Mirror Image; Cleric: Invisibility; Sage: Shadow Cloak, Noctura's Veil): Blur effect — 20% chance to avoid attacks
- **Avatar** (Cleric: Divine Avatar): +55 ATK/DEF for the rest of combat
- **Wish** (Magician: Wish): Doubles all combat stats
- **Time Stop** (Magician: Time Stop): Dodge next attack + 35 ATK/DEF bonus
- **Mind Blank** (Sage: Mind Blank): Full status immunity for rest of combat

---

## AoE Spell Effects Now Working

These AoE spells now apply their SpecialEffect to all surviving monsters:

| Spell | Effect | Result |
|-------|--------|--------|
| Ice Storm | frost | Slows all surviving monsters |
| Chain Lightning | lightning | Stuns all surviving monsters |
| Holy Explosion | holy | +50% bonus damage vs Undead/Demons per target |
| Meteor Swarm | fire | Burns all surviving monsters (DoT) |
| Psychic Scream | psychic | 25% chance to confuse each monster |
| Mass Confusion | mass_confusion | Confuses all surviving monsters |
| Aurelion's Radiance | holy | +50% bonus damage vs Undead/Demons per target |
| God's Finger | holy | +50% bonus damage vs Undead/Demons per target |

---

## Caster Class Identity

Each caster class now has a distinct combat identity:

- **Cleric**: Holy healer/protector. Powerful healing, holy damage with undead bonus, divine buffs, disease curing, conversion, ultimate avatar form (+55 all stats)
- **Magician**: Elemental devastator. Lightning (stuns), Fire (burn DoT), Frost (slows), crowd control (Sleep, Web, Fear), time magic (Haste, Time Stop), ultimate power (Wish doubles all stats)
- **Sage**: Mind/nature controller. Poison DoT, crowd control (Freeze, Confusion, Dominate, Mass Confusion AoE), life drain, psychic damage with confuse chance, shadow defense (Blur), mind immunity (Mind Blank), combat escape

---

## Files Changed

| File | Changes |
|------|---------|
| `Scripts/Core/GameConfig.cs` | Version bump to 0.30.5 |
| `Scripts/Core/GameEngine.cs` | Gated `EquipmentDatabase.ClearDynamicEquipment()` with `!IsOnlineMode` check in `RestorePlayerFromSaveData()` to prevent wiping other players' equipment in MUD mode |
| `Scripts/Data/EquipmentData.cs` | Added thread-safe locking (`lock`) to all `EquipmentDatabase` methods (`GetById`, `GetByName`, `RegisterDynamic`, `RegisterDynamicWithId`, `GetDynamicEquipment`, `ClearDynamicEquipment`) for concurrent MUD session safety |
| `Scripts/Systems/CombatEngine.cs` | Expanded `HandleSpecialSpellEffectOnMonster` from 4 to 18 effects (added poison, freeze, frost, web, confusion, mass_confusion, dominate, holy, fire, drain, death, disintegrate, psychic, dispel, convert, escape); added `player`, `spellDamage`, `result` parameters to signature; added AoE spell effect loop after `ApplyAoEDamage` in `ExecuteSpellMultiMonster`; added missing self-buff cases to `HandleSpecialSpellEffect` (mirror, invisible, shadow, avatar, wish, timestop, mindblank); fixed legacy handler to use modern status properties (IsSleeping/IsFrozen/IsFeared/IsSlowed/IsStunned instead of StunRounds/WeakenRounds) |
