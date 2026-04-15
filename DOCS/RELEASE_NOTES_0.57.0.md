# v0.57.0 - Alignment and Shields

v0.56.0 closed the class-completeness gaps; v0.56.1 tuned the difficulty curve; this release addresses two systemic issues the same playtester called out: **alignment feels one-way** (nothing naturally moves you back toward neutral) and **shields feel like a tax** (tank classes don't meaningfully reward investment in good shields).

## Alignment: Paired Movement, Confession, and the Balanced Path

Feedback: "Keeping both Chivalry and Darkness neutral is hard because nothing reduces one when the other increases." Investigation: true — most alignment-change sites adjusted only one scale. Good deeds raised Chivalry but left Darkness untouched, so a player who wanted to stay neutral had to actively commit evil acts to offset accumulated Chivalry. The only chivalry-cleansing path in the game was bank robbery, which is thematically absurd for someone just trying to be balanced.

- **Paired alignment movement**: new `AlignmentSystem.ChangeAlignment(amount, isGood)` helper. Good deeds raise Chivalry by `amount` AND lower Darkness by `amount/2`. Evil deeds mirror the other way. Key call sites now use this helper: murder, bank robbery, altar desecration. Many existing sites (temple donations, merciful kills, holy quests) were already using paired movement — the helper formalizes it.
- **Evil-deed rewards bumped**: murder 25 → **50 Darkness** (paired with -25 Chivalry), altar desecration 10-25 → **15-30 Darkness** (paired). Previously these were too stingy to make dedicated Darkness builds feel viable.
- **Temple Confession** (`[O]` at the Temple): pay **100g per 1 Chivalry cleansed**, cap **100 chivalry per visit**. Counterpart to altar desecration's Darkness cleanse. Lets players over-accumulated in Chivalry (via paired movement, temple donations, honorable deeds) bring themselves back toward neutral without needing to commit evil acts they don't want to.
- **New "Balanced" alignment tag**: when both Chivalry and Darkness are > 100 AND within 100 of each other, the player is classified `Balanced` rather than `Neutral`. Balanced characters get:
  - A dedicated alignment display (bright magenta, "Balanced") and a `Walks Both Paths` ability listing.
  - 5% discount at both legitimate AND shady shops (neither side treats them as a pariah).
  - +30% NPC reaction bonus from both good-aligned and evil-aligned NPCs (where each side normally gives -50% to their opposite).
  - `AlignmentSystem.IsBalanced(character)` helper for dialogue/faction code that wants to open both branches to balanced players.

Neutral (both scales ≤ 100) stays a distinct state — the "haven't done anything yet" zone. Balanced is the explicit reward for walking both paths meaningfully.

## Shields: Meaningfulness for Tanks

Feedback: "Shields are unnecessary for Tidesworn — taunts give defense, healing covers the rest, damage matters more." Investigation: shield-requiring abilities existed but weren't consistently enforced, shield block chance was a small dodge chance that didn't scale anything else, and no tank ability scaled its damage or defense with the equipped shield's stats.

- **Shield requirement enforcement**: `RequiresShield = true` now set on all Warrior/Paladin tank abilities it should apply to:
  - Warrior: `shield_wall` (already had it), `shield_wall_formation` (already), `iron_fortress` (new).
  - Paladin: `divine_shield` (new), `divine_mandate` (already), `aura_of_protection` (new).
  - Barbarian `rage_challenge` intentionally stays no-shield (rage-based, fits class fantasy).
  - Tidesworn abilities stay flexible (staff OR shield — the caster-tank archetype).
- **New Warrior Shield Bash (L16)**: damage = `weapon + BlockChance + ArmPow/2`. Gives Protection-spec Warriors a damage ability that rewards investment in good shields. 25 stamina, 3-round cooldown.
- **New Paladin Holy Shield Slam (L28)**: damage + minor self-heal, damage scales with shield stats. 35 stamina, 4-round cooldown. Fills the mid-level damage gap between Smite (L6) and Judgment Day (L56) for sword-and-board Paladins.
- **Spec tank shield damage bonus**: when a shield is equipped, **Protection Warrior** gets **+20% class-ability damage**, **Guardian Paladin** gets **+15% class-ability damage**. Juggernaut Barbarian gets nothing — they're rage-based, no-shield by design. This applies to ALL class-ability damage (not just the new shield abilities) so spec tanks are meaningfully better at everything they do while sword-and-board.
- **Guardian Paladin shield heal bonus**: +15% heal on class abilities while shield equipped. Rewards the hybrid tank/support spec for sword-and-board play on the healing side too.
- **`DefenseBonus × ShieldBlockChance`**: tank ability `DefenseBonus` values are now multiplied by `(1 + ShieldBlockChance / 100)` when a shield is equipped. A shield with 20% BlockChance gives a +20% DEF boost to every Defense-type tank ability. Higher-tier shields directly boost defensive ability values, making "upgrade your shield" a concrete tank progression path.

---

## Bugfixes

- **Balanced alignment missing from combat modifier table**: `GetCombatModifiers()` switch didn't include `AlignmentType.Balanced`, so balanced players fell through to the `_ => (1.0f, 1.0f)` default — zero combat scaling while Good/Dark/Evil got +/- bonuses. Now returns `(1.05f, 1.05f)` (Good-tier symmetric bonus).
- **Failed murder attempt bypassed paired movement**: the failed-attempt path at `BaseLocation.cs:4635` still did `currentPlayer.Darkness += 10` directly, skipping the v0.57.0 `ChangeAlignment` helper so no paired chivalry loss fired and no news was generated. Now routed through `AlignmentSystem.ChangeAlignment`.
- **Temple Confession had no daily limit**: a patient player could visit Temple indefinitely in one session to cleanse thousands of chivalry at 100g each (trivial at endgame). Now capped at 2 confessions per day (matches desecration cadence) via new `Character.ConfessionsToday` counter with save/load/daily-reset plumbing.
- **Temple Confession directly mutated Chivalry**: bypassed `AlignmentSystem.ModifyAlignment` and skipped the news pipeline, so large confessions were silent. Now routed through `ModifyAlignment` so significant confessions (≥20 chivalry) generate news.
- **`HasShieldEquipped` operator precedence**: missing parens meant the expression was `(TryGetValue && offId > 0 && WeaponType == Shield) || WeaponType == Buckler || WeaponType == TowerShield`. Benign in practice (empty off-hand returns null, all checks fail) but incorrect-by-reading. Added explicit parens.

- **`CheckAlignmentEvent` missing Balanced case**: Balanced players never triggered the random 5% alignment events (good-aligned gold drop, dark-aligned XP whisper, etc.) — they silently fell through the switch. Now Balanced gets a coin-flip between the Good merchant event and the Dark whispers event, preserving feature parity.
- **Items with `RequiresGood`/`RequiresEvil` locked Balanced players out of BOTH**: the checks `Chivalry <= Darkness` and `Darkness <= Chivalry` fail for a character whose scales are roughly equal. Balanced characters (by definition 100 apart or less) could be blocked from both good-aligned gear AND evil-aligned gear. Now Balanced bypasses both checks — walking both paths grants access to both sides' gear, per the plan.
- **Castle knighting gate excluded Balanced**: `notEvil = Darkness < Chivalry` meant a Balanced character with darkness ≥ chivalry couldn't be knighted, even though they aren't evil by the alignment system. Now `IsBalanced` short-circuits the gate.
- **Dialogue `AlignmentAbove`/`AlignmentBelow` conditions ignored Balanced**: the conditions used raw `Chivalry - Darkness` net value, so Balanced (diff ≈ 0) failed both the "good" and "evil" dialogue gates — the opposite of the plan's "access to both NPC dialogue paths" intent. Now Balanced players satisfy both conditions, opening both branches.
- **Betrayal forgiveness (`TeamBetrayal`) excluded Balanced**: required `Chivalry > Darkness`. Now accepts Balanced characters with sufficient ActsOfKindness.
- **King tax logic missing Balanced NPCs**: `GetRevenueToday`'s tax-alignment filter had no Balanced case, so Balanced NPCs silently fell through to "not taxable" when the king taxed Good or Evil. Now Balanced NPCs count for all three tax categories (Good/Evil/Neutral), since a character walking both paths fits any bucket.
- **Test coverage gaps for Balanced**: `AlignmentSystemTests` theory data didn't include Balanced for display, price modifier (both shop types), or combat modifier tests. Added `[InlineData]` rows for each to lock in the new behavior.

- **`GetAlignmentBonusDamage` (additive combat bonus) missing Balanced**: parallel to the earlier `GetCombatModifiers` miss — Holy/Good/Evil/Dark all granted small flat damage bonuses under specific conditions, but Balanced fell through to `(0, "")`. Now Balanced gets +10% damage vs evil targets (matching Good's flat-bonus tier) plus a 10% chance of a "path-between" insight bonus on any hit. Preserves the combat-bonus parity with Good that was established in the first-pass `GetCombatModifiers` fix.
- **Main Street alignment display used only `Chivalry`**: the title shown next to the Chivalry bar on Main Street was bucketed on `Chivalry` alone (`>= 100` → "Paragon", etc.), so a Balanced player with high chivalry AND high darkness would still read as "Paragon" with no indication that they're walking both paths. Now checks `IsBalanced` first and renders "Balanced" in bright magenta before falling through to the chivalry-based buckets.
- **`GetAlignment(null)` would NullReferenceException**: tax logic, knighting gate, dialogue conditions, betrayal forgiveness, and `IsBalanced` all call `GetAlignment` and several of them pass NPCs that can theoretically be null during teardown / dead-reference edge cases. Added an early null guard returning `Neutral`.

### Files Changed

- `Scripts/Core/GameConfig.cs` — Version 0.57.0 "Alignment and Shields"; `MurderDarknessGain` 25 → 50; `TempleMenuConfess = "O"`, `ConfessionGoldPerChivalry = 100`, `ConfessionMaxChivalryPerVisit = 100`
- `Scripts/Systems/AlignmentSystem.cs` — New `AlignmentType.Balanced`; new `ChangeAlignment(amount, isGood, reason)` helper for paired movement; new `IsBalanced()` query helper; Balanced entry in `GetAlignment()`, `GetAlignmentDisplay()`, `GetAlignmentAbilities()`, `GetPriceModifier()` (both shop types), `GetNPCReactionModifier()`, `GetCombatModifiers()` (audit fix), and `CheckAlignmentEvent()` (audit fix — coin-flip between good/dark event)
- `Scripts/Core/Items.cs` — `RequiresGood`/`RequiresEvil` gates now bypass for Balanced characters
- `Scripts/Core/King.cs` — Tax alignment filter recognizes Balanced NPCs for all three categories
- `Scripts/Locations/CastleLocation.cs` — Knighting `notEvil` gate accepts Balanced players
- `Scripts/Systems/DialogueSystem.cs` — `ConditionType.AlignmentAbove`/`AlignmentBelow` return true for Balanced so both good and evil NPC dialogue branches open
- `Scripts/Systems/BetrayalSystem.cs` — `TeamBetrayal` forgiveness gate accepts Balanced players
- `Tests/AlignmentSystemTests.cs` — Added Balanced `[InlineData]` rows to display, price modifier, and combat modifier theory tests
- `Scripts/Systems/CombatEngine.cs` — `GetAlignmentBonusDamage` now handles Balanced (+10% vs evil targets, 10% chance "path-between" insight bonus otherwise)
- `Scripts/Locations/MainStreetLocation.cs` — Alignment section checks `IsBalanced` first before falling through to chivalry-based display bucketing
- `Scripts/Systems/AlignmentSystem.cs` — `GetAlignment(null)` returns `Neutral` instead of NullReferenceException
- `Scripts/Core/Character.cs` — New `ConfessionsToday` daily counter; `HasShieldEquipped` parens added around shield-type disjunction
- `Scripts/Locations/BaseLocation.cs` — Murder (success AND failed attempt) now uses `ChangeAlignment` (paired chivalry loss alongside darkness gain)
- `Scripts/Systems/DailySystemManager.cs` — `ConfessionsToday` daily reset
- `Scripts/Systems/SaveDataStructures.cs` + `SaveSystem.cs` + `Core/GameEngine.cs` — `ConfessionsToday` save/load plumbing
- `Scripts/Locations/BankLocation.cs` — Bank robbery uses `ModifyAlignment` helper (goes through news/centralized logic)
- `Scripts/Locations/TempleLocation.cs` — Altar desecration uses `ChangeAlignment` with bumped 15-30 darkness (was 10-25); new `[O] Confess Sins` menu option and `ProcessConfession()` method
- `Scripts/Systems/ClassAbilitySystem.cs` — `iron_fortress`, `aura_of_protection`, `divine_shield` gained `RequiresShield = true`; new Warrior `shield_bash` (L16) and Paladin `holy_shield_slam` (L28) abilities with shield-scaling damage; `UseAbility` now applies shield-scaling for `shield_bash`/`holy_shield_slam`, spec tank shield damage/heal bonuses, and multiplies `DefenseBonus` by `(1 + BlockChance/100)` when a shield is equipped
- `Scripts/Data/SpecializationData.cs` — New `GetShieldDamageBonus(spec)` and `GetShieldHealBonus(spec)` helpers (Protection +20% damage, Guardian +15% damage/heal, Juggernaut 0% — no-shield identity preserved)
- `Tests/AlignmentSystemTests.cs` — Updated `GetAlignment_Neutral_WhenValuesAreBalanced` → `GetAlignment_Balanced_WhenBothAreHighAndCloseTogether` for the new semantics; `CreateCharacterWithAlignment(Neutral)` uses 50/50 (both low) instead of 300/300 (now Balanced)
- `Localization/en.json`, `es.json`, `fr.json`, `hu.json`, `it.json` — New `alignment.balanced`, `alignment.ability_walks_both_paths`, `temple.sr_confess`, `temple.menu_confess`, and 10 `temple.confess_*` keys translated across all 5 languages
