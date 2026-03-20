# Usurper Reborn v0.53.2 Release Notes

**Version Name:** Ancestral Spirits

## Corrosive Cloud AoE Fix

The Alchemist's Corrosive Cloud ability (level 68) was only dealing damage to a single target despite being described as AoE. The corrode debuff (armor dissolve) was correctly applied to all enemies, but the actual damage only hit one. Now deals full damage to every living monster in multi-monster combat, plus applies the corrode effect.

## Shield Wall Detection Fix

The Paladin/Warrior Shield Wall ability didn't recognize shields that were equipped before the v0.53.0 shield conversion fix. Those legacy shields had `WeaponType = None` (created via `CreateArmor` instead of `CreateShield`), so the `RequiresShield` check failed. Players had to unequip and re-equip the shield to fix it. The check now also accepts off-hand items with `ShieldBonus > 0` or `OffHandOnly` handedness, handling all shield types regardless of how they were created.

## Interactive Throne Challenge

The "Infiltrate (Challenge Throne)" system has been completely refactored from auto-resolved simulated combat to full interactive PvP using CombatEngine. Players now have access to all combat actions during throne challenges: abilities, spells, potions, herbs, defend, power attack, precise strike, disarm, taunt, hide, and retreat. All three phases (monster guards, NPC guards, king) use interactive combat. HP carries between phases. King retains the +50% HP/DEF defender bonus.

## Unidentified Items Removed from Loot Drops

New dungeon loot drops are now always fully identified with their real name, stats, and rarity visible immediately. The identify service at the Magic Shop remains for any existing unidentified items in player inventories.

## Quest Localization Fix

All quest creation paths (bounty board, starter quests, king bounties, expedition quests) now use English hardcoded strings for titles, initiators, and objective descriptions. Previously, quest data was created with `Loc.Get()` which baked the creating player's language — causing Hungarian quest titles like "Mély Felfedezés" and "Királyi Tanács" to appear for English players. Cleaned up 25 existing Hungarian quest strings on the server.

## Precise Strike Message Fix

Precise strike combat message was showing raw format placeholders `{0}` and `{1}` instead of the target name and damage amount. Missing parameter in `Loc.Get` call.

## Dual Wielding Display Fix

Combat style incorrectly showed "Dual Wielding" when a player had a weapon + shield equipped. Added `!HasShieldEquipped` check to `IsDualWielding` property.

## Two-Handed Weapon Detection Fix

`IsTwoHanding` now checks by `WeaponType` (Bow, Staff, Maul, Polearm) as a fallback in addition to `Handedness`. Legacy equipment from old conversion paths had `Handedness = None` instead of `TwoHanded`, allowing companions like Lyris to equip off-hand items alongside bows.

## Establishment Status Display Fix

The localization key `castle.open` was set to "CLOSED" in English, Spanish, Hungarian, and Italian — making all establishments appear closed even when open. Fixed to "OPEN"/"ABIERTO"/"NYITVA"/"APERTO" in all 4 affected languages. French was already correct.

## King Bounty Payout Fix

King bounty rewards were capped at 25,500 gold due to the `Reward` field being stored as a `byte` (max 255, multiplied by 100). A 1 million gold bounty from the treasury only paid out 25k. Added `BountyGold` (long) field for direct gold amounts. King bounties now pay the full treasury-deducted amount. Existing bounty increases stack correctly.

## Mystic Shaman /who Display

The Mystic Shaman class showed as `????` in the `/who` list because it was missing from the `WhoClassAbbrev` switch. Now displays as `Sham`.

## AoE Damage Rebalance

All AoE abilities and spells now use diminishing damage across targets instead of dealing full damage to each. The first target takes 100%, second 75%, third 50%, and fourth+ targets take 25% (floor). This prevents AoE from being strictly superior to single-target abilities in multi-monster encounters. Affected abilities: generic AoE (Whirlwind, Primal Scream, Blade Storm, Arrow Storm, etc.), Holy AoE (Judgment Day, Holy Nova), Corrosive Cloud, Carnival of Chaos, Chain Lightning, Crescendo (Wavecaller), Entropy (Cyclebreaker), Overflow (Abysswarden), and all multi-target spells.

## Blood Price Escalation

Murder consequences now escalate in three tiers based on accumulated blood weight. Tier 1 (weight 5+): 20% shop price markup. Tier 2 (weight 8+): 50% markup and -10% combat damage. Tier 3 (weight 15+): 100% markup (double prices), -20% combat damage, and +50% healer costs. Blood price penalties are displayed in `/health` under Active Debuffs. NPCs remember your crimes and adjust prices accordingly.

## Save Cheesing Prevention

Negative outcomes (combat losses, companion deaths, bounty failures, prison sentences, negative street encounters, chest operations) now trigger an immediate auto-save, preventing players from force-quitting to avoid consequences. Seven save points added across combat, companions, castle, home, magic shop, and street encounters.

## MetaProgression Perks Wired In

Three meta-progression perks that were defined but never connected to gameplay are now functional:

- **Fear the Throne** (Usurper ending): Monsters deal 10% less damage to you, reflecting your tyrannical aura.
- **Artifact Hunter** (7+ artifacts collected): All artifact stat bonuses increased by 50%.
- **Survivor's Guilt** (companion died in a previous run): Ghost companions occasionally appear at dungeon rest spots, offering cryptic advice and temporary buffs.

## Title Selection

Players can now choose their display title from the Preferences menu (`[T] Title`). Available titles include knighthood titles (Sir/Dame) and any meta-progression titles earned across playthroughs (Dark Lord, Savior, Defiant, Awakened, Dissolved, The Eternal). Selected titles appear before the player's name in all displays.

## Assassin Ability Description Rewrite

All Assassin class abilities in the Level Master now show technical descriptions with exact scaling information: base damage values, stat scaling formulas, proc chances, duration, cooldowns, and weapon requirements. Example: "Backstab: Guaranteed critical hit from stealth. Requires dagger. 40 base damage, scales with STR+DEX."

## Shadow Step & Vanish DEF Scaling

Shadow Step and Vanish defense bonuses now scale with the character's CON stat instead of providing flat values. Shadow Step grants a moderate DEF bonus for 2 rounds; Vanish grants a larger DEF bonus plus Hidden status. Both scale meaningfully into endgame.

## Frostbrand Slow Effect

The Frostbrand weapon enchant now applies a slow debuff on proc (15% chance): reduces target's Defence by 3 for 2 rounds. Works in both single-monster and multi-monster combat paths.

## Tavern Stranger XP Exploit Fix

The dungeon tavern stranger encounter could be repeated infinitely for free XP by re-entering the room. Now limited to one conversation per day via `TavernStrangerTalkedToday` flag, reset during daily maintenance.

## Cleansing Melody Feedback

The Mystic Shaman's Cleansing Melody ability now shows clear feedback messages: "No ailments to cleanse" when the target has no debuffs, or "Cleansed X ailments" with the count when successful.

## Noctura Shadow Message Fix

The Noctura shadow combat ability message had an unfilled `{0}` placeholder. Rewritten to not require a parameter.

## Bug Report Discord Integration

Bug reports submitted in-game are now posted to a Discord webhook for faster developer notification.

## macOS Steam Support

Added macOS (x64 + arm64) as a Steam depot target in the CI/CD pipeline. Universal macOS launchers (`play-mac.sh`, `play-mac-accessible.sh`) included. Icon patching removed for macOS builds to preserve code signatures.

## macOS WezTerm Icon Fix (v0.53.1)

Removed macOS WezTerm icon patching from the CI/CD pipeline. Modifying any file inside a `.app` bundle invalidates the macOS code signature, causing Gatekeeper to block it with "WezTerm is damaged and can't be opened." Windows icon patching (rcedit on .exe) is unaffected and continues to work.

---

## Files Changed

- `Scripts/Core/GameConfig.cs` — Version 0.53.2; blood price escalation tier constants; Frostbrand enchant constants
- `Scripts/Core/Character.cs` — `IsDualWielding` excludes shields; `IsTwoHanding` checks by WeaponType fallback; `ThroneChallengedToday`; `TavernStrangerTalkedToday`; totem/enchant combat properties
- `Scripts/Core/NPC.cs` — Tiered blood price shop markup (20%/50%/100%)
- `Scripts/Core/Quest.cs` — `BountyGold` (long) field for king bounties
- `Scripts/Systems/CombatEngine.cs` — Corrosive Cloud AoE damage; precise strike message fix; AoE diminishing returns (7 handlers); blood price damage penalty; Fear the Throne monster damage reduction; Frostbrand slow effect; Shadow Step/Vanish scaled DEF; save cheesing immediate saves
- `Scripts/Systems/ClassAbilitySystem.cs` — Shield Wall detection accepts legacy shields; Assassin technical descriptions; Frostbrand ability; mana cost display for Shaman abilities
- `Scripts/Systems/MetaProgressionSystem.cs` — `GetMonsterFleeReduction()`; `GetArtifactMultiplier()`; `HasSurvivorsGuilt`; `SaveData()` made public
- `Scripts/Systems/ArtifactSystem.cs` — Artifact Hunter perk: +50% artifact stat bonuses
- `Scripts/Systems/QuestSystem.cs` — All quest creation uses English hardcoded strings; bounty payout uses BountyGold
- `Scripts/Systems/LootGenerator.cs` — `ShouldBeUnidentified` always returns false; MysticShaman in armor templates
- `Scripts/Systems/RareEncounters.cs` — Tavern stranger daily limit
- `Scripts/Systems/BugReportSystem.cs` — Discord webhook integration
- `Scripts/Systems/CompanionSystem.cs` — Save cheesing saves on companion death
- `Scripts/Locations/CastleLocation.cs` — Throne challenge refactored to interactive PvP via CombatEngine; bounty passes long amount; throne daily limit; knight title MetaProgression persistence; save cheesing saves
- `Scripts/Locations/BaseLocation.cs` — Title selection in preferences `[T]`; blood price display in `/health`; Shaman passive display
- `Scripts/Locations/DungeonLocation.cs` — Survivor's Guilt ghost companion encounters; equipment persistence saves; Shaman totem healing fix
- `Scripts/Locations/HomeLocation.cs` — Save cheesing saves on chest operations
- `Scripts/Locations/MagicShopLocation.cs` — Save cheesing saves on negative outcomes
- `Scripts/Systems/StreetEncounterSystem.cs` — Save cheesing saves on negative encounters
- `Scripts/Systems/DailySystemManager.cs` — `TavernStrangerTalkedToday` daily reset
- `Scripts/Server/MudChatSystem.cs` — MysticShaman "Sham" abbreviation in /who
- `Localization/en.json` — `castle.open` fixed; cleansing melody keys; Noctura shadow message fix; Shaman ability keys
- `Localization/es.json` — `castle.open` fixed to "ABIERTO"
- `Localization/hu.json` — `castle.open` fixed to "NYITVA"
- `Localization/it.json` — `castle.open` fixed to "APERTO"
- `.github/workflows/ci-cd.yml` — Removed macOS WezTerm icon patch step (v0.53.1); macOS Steam depot
- `launchers/play-mac.sh` — macOS launcher
- `launchers/play-mac-accessible.sh` — macOS accessible launcher
- `launchers/play-mac.sh` — macOS launcher
- `launchers/play-mac-accessible.sh` — macOS accessible launcher
