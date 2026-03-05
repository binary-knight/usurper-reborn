# Usurper Reborn v0.49.9 — Loot, Combat & Balance Fixes

## Weapon Type Classification Fix

Several weapons were incorrectly classified as Swords due to the "Blade" or "Edge" substring matching the Sword catch-all in the weapon type inference system. These now have correct types:

**Now Dagger**: Throwing Knife, Trick Blade, Madcap's Razor (Jester); Alchemist's Blade, Elixir-Infused Blade (Alchemist); Chaos Edge, Fool's Edge, Philosopher's Edge (Jester/Alchemist)

**Now Rapier**: Dueling Blade, Songblade (Bard)

This affects both shop inventory and dungeon loot drops. Assassin blade abilities that require a Dagger weapon will now work with these items.

## Missing Intelligence & Constitution on Dungeon Loot

Dungeon loot with Intelligence or Constitution enchantments (e.g., "Sage" prefix, "of the Mind" suffix) was silently converting those stats into Mana and HP respectively, instead of granting actual +INT and +CON bonuses on the equipment. The Item-to-Equipment conversion in all three combat loot paths (single monster, multi-monster, group) now correctly transfers `IntelligenceBonus` and `ConstitutionBonus` from `LootEffects`. The `AllStats` enchantment also now properly includes CON, INT, and CHA bonuses that were previously lost.

## Monster Life Drain vs Companions Fix

When monsters with life drain abilities (trolls, wights, etc.) targeted NPC allies or companions, the drain message would display and the monster would heal, but no actual damage was dealt to the companion. This was because `MonsterAttacksCompanion` only handled `DirectDamage` abilities, missing `DamageMultiplier`-based abilities like Life Drain (0.7x + 50% lifesteal) and Crushing Blow (1.5x damage). Both damage and healing are now correctly applied and reported.

## Dragon's Hoard Gold Display Fix

The Dragon's Hoard rare encounter displayed a lower gold amount than was actually awarded (showed `level * 1000` but granted `level * 3000`). Now uses a shared variable so the displayed and actual amounts always match.

## Accessory Stat Theming Fix

Rings and necklaces with themed names (e.g., "Ring of Wisdom", "Necklace of Might") were generating flat base stats (STR+DEX+HP) regardless of their name. Now stats are matched to the item's theme: Wisdom items grant WIS+Mana, Strength items grant STR+HP, Protection items grant DEF+HP, Vitality items grant high HP, Might/Valor items grant STR+DEX, Luck/Fortune items grant DEX+CHA, Mage/Arcane items grant Mana+WIS, and Dragon items grant STR+DEF+HP.

## Quest Hint Specificity

Equipment purchase quests (Buy Weapon, Buy Armor, Buy Accessory) now show the specific shop name in their hint text instead of the generic "appropriate shop in town."

## Invalid Race/Class Combinations on NPCs

NPC immigrant spawning and children coming of age could produce invalid race/class combinations (e.g., Troll Paladin) because they bypassed the `InvalidCombinations` validation that player character creation enforces. Both paths now validate against the restriction table.

## Vicious Mockery Distraction Effect

The Jester's Vicious Mockery distraction was adding +15 flat defense (negligible at high levels) instead of affecting the attack roll. Now applies a -5 penalty to the monster's D20 attack roll, which is visible in the combat log as "(distracted: -5)".

## Wilderness Discovery Revisit Counter

Revisiting previously discovered wilderness locations now uses a separate daily counter (2/day) instead of consuming expedition slots. This prevents discovery revisits from eating into the main exploration budget of 4 expeditions per day.

## Level Master Class Dialogue Fix

The Level Master's ability training dialogue said "Let me show you the way of the warrior..." for every class. Now correctly says "way of the jester", "way of the assassin", etc.

## Jester Missing High-Level Abilities

Jester only had class abilities up to level 72 (Grand Finale), leaving a 28-level gap to the level cap. Two new abilities added:

- **Carnival of Chaos** (Level 82) — AoE attack that unleashes a whirlwind of tricks and mayhem on all enemies
- **Last Laugh** (Level 93) — Capstone single-target nuke with confusion effect

## Bard/Jester Grand Finale Collision Fix

The Wavecaller prestige class had a `grand_finale` ability with the same dictionary key as the Bard/Jester Grand Finale. Since the Wavecaller entry came later, it silently overwrote the Bard/Jester version, making Grand Finale invisible to both classes. Wavecaller's ability renamed to "Harmonic Crescendo" with a unique key.

## Skill Training Cleanup

Removed the "Failure Chance" display from skill upgrade confirmations — showing a negative stat when a player improves their skill was confusing and discouraging.

## Armor Shop Accessory Removal

Rings and necklaces were purchasable in both the Armor Shop and Magic Shop. Removed Neck, Left Ring, and Right Ring slots from the Armor Shop — accessories are sold exclusively in the Magic Shop.

## Inn Bulletin Board Removed

Removed the static placeholder bulletin board from the Inn. It displayed five hardcoded fake notices that never changed.

## Fatigue Display in Dungeon

The fatigue/tiredness level was not displayed in the dungeon header, even though combat warnings about dulled reflexes would fire. Fatigue now shows in the dungeon location header when Tired or Exhausted, matching the behavior of all other locations.

## Dungeon Camping Fatigue & Time

The dungeon [R] Camp option healed HP/MP/Stamina but did not reduce fatigue or advance game time. Camping now reduces fatigue by 10 and advances the game clock by 2 hours, matching the safe haven rest behavior.

## Ability Targeting in Multi-Monster Combat

Class abilities used from the quickbar in multi-monster combat could not be targeted — they always hit a random enemy. This made abilities like Vicious Mockery (single-target debuff) impossible to aim. Single-target Attack and Debuff abilities now prompt for target selection, and AoE abilities automatically target all enemies.

## Boss Monster Phrase Fix

Dungeon boss monsters (e.g., "Crypt Warden", "Sewer Abomination") were using the taunt phrase of the base monster they were generated from, resulting in nonsense like a Crypt Warden saying "You no scare kobold!" Boss rooms now assign a theme-appropriate phrase when the boss name is set.

## Home Screen Mana/Stamina Display

The Home location stats bar showed "Mana: 0/0" for non-caster classes (Warrior, Barbarian, Assassin, etc.). Now shows "Stamina" for non-mana classes, matching the pattern used by all other locations.

## Rest Recovery Messages

Rest and sleep recovery messages at both the Home and Inn showed "mana recovered" for all classes, even non-casters who use stamina. Now correctly shows mana for caster classes and HP/stamina for non-casters.

## Double Save on Quit Fix

Quitting single-player displayed "Game saved!" twice. The quit flow called `SaveCurrentGame()` in `MainStreetLocation.QuitGame()`, then threw a `LocationExitException(NoWhere)` which `LocationManager` caught and saved again. Removed the redundant save from LocationManager.

## Settlement NPCs Appearing in Town

NPCs who migrated to the Outskirts settlement were still appearing at town locations (Inn, Main Street, etc.) because the world simulator's activity system moved them to town locations during daily activities (dungeon exploration, trading, training). Settlers now do their activities normally (earning gold, training skills) but their display location snaps back to "Settlement" afterward. The random movement system also no longer relocates settlers to town.

## Sleep Spell Fix

The Sleep spell appeared to have no effect on enemies despite showing a successful cast message. Two issues: (1) In multi-monster combat, the "Monster attacks!" message was printed before checking if the monster was sleeping, making it look like sleeping enemies were still attacking even though no damage was dealt. Incapacitated monsters (sleeping, feared, stunned, frozen) now skip the "attacks!" message entirely. (2) Sleeping monsters woke up immediately when the player attacked them on the very next turn, making Sleep effectively only skip one monster attack — nearly useless for its mana cost. Sleeping monsters now stay asleep for their full duration and take 50% bonus damage from all attacks while sleeping, making the spell a meaningful tactical choice.

## Gnoll Poisonous Bite

The Gnoll racial ability "Poisonous Bite" was described during character creation but never actually implemented in combat — only Troll Regeneration had a racial combat effect. Gnolls now have a 15% chance per melee hit to poison the target for 3 rounds of damage-over-time, applied through the post-hit enchantment pipeline.

## Pixie Blessing 1500% Damage/Defense Bug

The dungeon pixie encounter's blessing set `WellRestedBonus = 15` instead of `0.15f`, applying a 1500% damage and defense multiplier instead of 15%. Players who caught the pixie became nearly invulnerable (taking 1 damage from 700+ monster hits) while dealing 10x+ normal damage. Fixed to use the correct 0.15f float multiplier, matching how the Home hearth buff works.

## Chat Text Uppercased at Settlement & Wilderness

The `/say`, `/tell`, `/shout`, and `/broadcast` commands at Settlement and Wilderness locations converted all message text to uppercase (e.g., `/say hello` → `You say: HELLO`). Both locations passed the `.ToUpper()` version of the input to `TryProcessGlobalCommand()` instead of the original text. All other locations correctly preserved case.

## Maintenance System Output Noise

The daily maintenance system (NPC training, world events, economy ticks) printed internal status messages directly to whatever player happened to be online at the time. Messages like "Checking NPC skills..." and "Processing world events..." would interrupt gameplay mid-interaction. All maintenance output now goes to the debug log only.

## Church Marriage Ceremony UI

The church marriage ceremony gave no feedback about why you couldn't marry someone or how to progress toward marriage. When no eligible NPCs exist, the Bishop now shows your closest romantic relationships with their current affection levels, and provides step-by-step guidance on how to reach marriage status (Love Street visits, intimacy, love potions, mutual "In Love" requirement).

## Settlement Proposal Deliberation Frozen During Construction

Settlement building proposals ("Resolves in 5 ticks") never actually resolved while any building was under construction. The proposal system's tick-counting was blocked by early-return guards meant to prevent *new* proposals during construction, but those guards also prevented existing proposals from counting down their deliberation timer. Proposals now tick down and resolve regardless of construction activity.

## Settlement Proposal Cooldowns Never Expired

When a building proposal was rejected by settlers, it was added to a cooldown list to prevent immediate re-proposal. However, the cooldown decay code was an empty block — a TODO that was never implemented. Rejected proposals were permanently blocked from being proposed again for the entire game. Cooldowns now properly decrement each world tick and expire after 20 ticks (~20 minutes), matching the `SettlementProposalCooldownTicks` constant that was already defined but unused.

## Settlement NPC Votes Compounding Every Tick

NPC settler votes on building proposals accumulated every tick during the 5-tick deliberation period. With 10 settlers where 7 support, the final tally would be 35 for (7 settlers × 5 ticks) instead of 7. This made the player's +2 endorsement vote negligible. NPC votes are now recounted each tick from current settler opinions instead of accumulating.

## Settlement Buff Overwrite Inconsistency

The Tavern and Palisade services silently overwrote any existing settlement buff without warning, while the Arena, Thieves' Den, Prison, and Library services blocked if any buff was active. All services now consistently check for any active buff and block with a message, preventing accidental buff loss.

## Council Hall Infinite Gold Exploit

The Council Hall treasury share claim had no daily limit. Players could repeatedly claim 50+ gold per visit until the communal treasury was drained. Now limited to one claim per day.

## Herbalist Hut No Daily Limit

The Herbalist Hut description said "1/day" but had no daily counter — players could collect herbs repeatedly up to carry capacity. Now limited to one herb per day as intended.

## Mystic Circle Useless for Non-Casters

The Mystic Circle's mana restoration showed "Your mana is already full" for non-caster classes since they have 0/0 mana. Non-caster classes now receive HP restoration instead.

## Settlement Proposal Vote Stacking

Players could endorse or oppose a proposal unlimited times, accumulating vote weight each time (paying 1000 gold per endorse). A wealthy player could guarantee any proposal's outcome. Players can now only vote once per proposal, with the option to switch their position.

## BBS Compact Display Missing In-Progress Buildings

The BBS/compact settlement display filtered out buildings at tier None, hiding buildings that were actively under construction with gold in their resource pool. In-progress buildings now appear in the compact display.

## Settlement Vote Display Fix

Endorsing or opposing a settlement building proposal updated the internal vote weight but the banner display showed "Votes: 0 for / 0 against" — only NPC settler votes were counted in the display. The player's vote weight is now included in both the settlement banner and the BBS compact display, matching the Proposals detail screen which already showed the correct totals.

## Settlement State Persistence (Online Mode)

Settlement state (buildings, treasury, contributions, proposals) was resetting after server restarts in online mode. Root cause: when a player logged in, their save file contained stale settlement data which overwrote the live settlement state maintained by the world simulator. Unlike NPCs and royal court (which are explicitly loaded from `world_state` on login), settlement had no such override. The stale data then got saved back to the database, causing permanent data loss. Settlement state is now loaded from the authoritative `world_state` table on player login in online mode, and player contributions and proposal votes immediately persist to the database instead of waiting for the next 5-minute world sim save cycle.

## Old God Save Quest Visibility

The Old God save/spare quest chain (choosing to save gods like Veloura instead of defeating them) was tracked entirely through internal story flags with zero player-facing visibility — no quest journal entry, no progress display, no hints. Players who chose to save a god had no idea what to do next. The `/health` command now shows Old God save quest status: how many gods have been saved, how many are awaiting rescue, and per-god hints on whether to seek the artifact or return to the god's floor. The dungeon entrance also displays reminders when save quests are active.

## Settlement Per-Player Vote Tracking (Online Mode)

In online multiplayer, all players shared a single vote weight integer on proposals. If Player A endorsed a proposal, Player B's oppose vote would overwrite Player A's vote entirely. Votes are now tracked per-player in a dictionary, so each player's endorsement or opposition is independent and correctly summed.

## Settlement Shrine & Mystic Circle Daily Limits

The Settlement Shrine (XP buff) and Mystic Circle (mana/HP restore) had no daily usage limits, allowing unlimited free buffs and healing. Both are now limited to once per day, matching the Council Hall and Herbalist Hut daily limits.

## TrapResist Buff Consumed by Combat

The Settlement Prison's Trap Resistance buff (−50% trap damage) was counted down per combat encounter rather than per trap encounter. A player with 5 combats worth of TrapResist would lose it entirely from normal monster fights without ever encountering a trap. The buff now only counts down when an actual trap or puzzle failure deals damage.

## Settlement Proposal Cooldown Off-By-One

Proposal cooldowns expired one tick early. A 20-tick cooldown would allow re-proposals after 19 ticks because the code removed cooldowns at value 1 before decrementing, instead of decrementing first and removing at value 0.

## Settlement Library XP Buff Description

The Library's effect description claimed "+5% XP buff (5 combats, stacks with Tavern)" but the settlement buff system uses a single buff slot — Library and Tavern buffs cannot coexist. Misleading "stacks with Tavern" removed from description.

## Settlement Council Hall Treasury Not Persisted Online

When a player claimed gold from the Council Hall treasury in online mode, the reduced treasury amount was not immediately persisted to the world_state database. A server crash before the next world sim save cycle would restore the treasury to its pre-claim value while the player kept the gold — a duplication exploit. Treasury changes now persist immediately.

## Settlement Vote Direction Change Exploit

Players could endorse a proposal (costing 1000 gold) and then oppose it (free), effectively wasting their gold and flipping their vote. The check only prevented re-voting in the same direction — endorsing twice or opposing twice — but not switching. Players now cannot change their vote once cast in either direction.

## Lever Puzzle Roman Numeral Confusion

Dungeon lever puzzles used Roman numerals (I, II, III, IV, V) to label step positions in the pull sequence, but these looked identical to lever labels, causing players to misinterpret "I: Seasons cycle in this count" as "Lever 1 = 4" instead of "Pull first: lever 4." Step labels changed from Roman numerals to ordinal words ("Pull First:", "Pull Second:", etc.).

---

## Files Changed

- `Scripts/Core/GameConfig.cs` — Version 0.49.9; `WildernessMaxDailyRevisits = 2`
- `Scripts/Core/Character.cs` — `WildernessRevisitsToday` property
- `Scripts/Systems/ShopItemGenerator.cs` — `InferWeaponType()`: added Knife, Razor, Edge, Trick, Alchemist's Blade, Elixir-Infused to Dagger check; Dueling Blade and Songblade to Rapier check
- `Scripts/Systems/CombatEngine.cs` — Three Item-to-Equipment loot conversion sites now transfer `SpecialEffect.Constitution` → `ConstitutionBonus`, `SpecialEffect.Intelligence` → `IntelligenceBonus`, and CON/INT/CHA parts of `AllStats`; `MonsterAttacksCompanion` now handles `DamageMultiplier` abilities with life steal against companions; Vicious Mockery distraction changed from +15 defense to -5 attack roll penalty; `HandleQuickbarAction` now prompts for target on Attack/Debuff abilities in multi-monster combat; Gnoll Poisonous Bite racial ability (15% chance, 3 rounds poison); Sleep spell fix: incapacitated monsters skip "attacks!" message in multi-monster loop; sleeping monsters take 50% bonus damage instead of waking on hit; TrapResist settlement buff skipped in per-combat decrement (counts down per trap instead)
- `Scripts/Systems/RareEncounters.cs` — Dragon's Hoard gold display uses shared variable
- `Scripts/Systems/LootGenerator.cs` — Accessory name-based stat theming in `CreateAccessoryFromTemplate`
- `Scripts/Locations/QuestHallLocation.cs` — Specific shop names in buy-quest hints
- `Scripts/Systems/NPCSpawnSystem.cs` — Race/class validation for immigrant NPCs
- `Scripts/Systems/FamilySystem.cs` — Race/class validation for children coming of age
- `Scripts/Locations/WildernessLocation.cs` — Discovery revisits use separate `WildernessRevisitsToday` counter (2/day); fixed `TryProcessGlobalCommand(upper)` → `TryProcessGlobalCommand(choice)` to preserve chat text case
- `Scripts/Systems/SettlementSystem.cs` — Proposal deliberation no longer blocked by active construction; `ProposalCooldowns` converted from `HashSet<string>` to `Dictionary<string, int>` with proper tick-based decay (off-by-one fix: decrement then remove at 0); NPC votes recounted each tick instead of accumulating; `ActiveProposal.PlayerVotes` changed from single int to `Dictionary<string, int>` for per-player tracking in online mode; Library description removed misleading "stacks with Tavern"
- `Scripts/Locations/SettlementLocation.cs` — Fixed `TryProcessGlobalCommand(upper)` → `TryProcessGlobalCommand(choice)` to preserve chat text case; vote display includes player weight in banner and BBS compact views; `PersistSettlementIfOnline()` immediately saves settlement to `world_state` after contributions, proposal votes, and Council Hall gold claims; all buff services now consistently check `HasSettlementBuff`; Council Hall gold claim limited to 1/day; Herbalist Hut herb claim limited to 1/day; Shrine limited to 1/day; Mystic Circle limited to 1/day, restores HP for non-casters; per-player vote tracking in online mode; vote direction change blocked (cannot switch from endorse to oppose or vice versa); BBS compact display includes in-progress buildings at tier None
- `Scripts/Core/Character.cs` — `SettlementGoldClaimedToday`, `SettlementHerbClaimedToday`, `SettlementShrineUsedToday`, `SettlementCircleUsedToday` daily flags
- `Scripts/Systems/SaveDataStructures.cs` — `WildernessRevisitsToday` field; `SettlementGoldClaimedToday`, `SettlementHerbClaimedToday`, `SettlementShrineUsedToday`, `SettlementCircleUsedToday` fields
- `Scripts/Systems/SaveSystem.cs` — `WildernessRevisitsToday` serialization; settlement daily claim fields serialization (gold, herb, shrine, circle)
- `Scripts/Core/GameEngine.cs` — `WildernessRevisitsToday` restore on load; online mode now loads settlement from `world_state` instead of stale player save (two `LoadSaveByFileName` paths); `RestoreWorldState()` skips settlement in online mode; settlement daily claim fields restored (gold, herb, shrine, circle)
- `Scripts/Systems/DailySystemManager.cs` — `WildernessRevisitsToday` reset on new day; settlement daily claim resets (gold, herb, shrine, circle)
- `Scripts/Locations/LevelMasterLocation.cs` — Training dialogue uses actual class name instead of hardcoded "warrior"
- `Scripts/Systems/ClassAbilitySystem.cs` — Two new Jester abilities (Carnival of Chaos lv82, Last Laugh lv93); Wavecaller `grand_finale` key renamed to `harmonic_crescendo` to fix collision with Bard/Jester Grand Finale
- `Scripts/Systems/TrainingSystem.cs` — Removed "Failure Chance" display from skill upgrade confirmation
- `Scripts/Locations/ArmorShopLocation.cs` — Removed Neck/LFinger/RFinger from armor slot list and BBS menu
- `Scripts/Locations/BaseLocation.cs` — Fatigue display added to dungeon/online location header when Tired or Exhausted; Old God save quest status and per-god hints in `/health`
- `Scripts/Locations/DungeonLocation.cs` — `RestInRoom()` now reduces fatigue (-10) and advances game time (+2 hours); boss `GetBossPhrase()` assigns theme-appropriate taunt; save quest reminders at dungeon entrance; pixie blessing `WellRestedBonus` fixed from `15` to `0.15f`; TrapResist buff decremented per trap/puzzle failure instead of per combat
- `Scripts/Locations/InnLocation.cs` — Removed bulletin board menu option, handler, and method; rest message shows stamina for non-casters
- `Scripts/Locations/HomeLocation.cs` — Mana/Stamina display fix for non-casters; rest recovery messages show correct resource type
- `Scripts/Systems/LocationManager.cs` — Removed redundant save on quit (double save fix)
- `Scripts/Systems/OnlineStateManager.cs` — `LoadSettlementFromWorldState()` and `SaveSettlementToWorldState()` methods for authoritative settlement persistence in online mode
- `Scripts/Systems/DailySystemManager.cs` — `WildernessRevisitsToday` daily reset; maintenance output redirected to debug log only
- `Scripts/Locations/ChurchLocation.cs` — Marriage ceremony shows closest romantic relationships with affection levels and step-by-step guidance when no eligible candidates exist
- `Scripts/Systems/WorldSimulator.cs` — Settlement NPC location snap-back after activities; settlers excluded from random town relocation
- `Scripts/Systems/PuzzleSystem.cs` — Lever puzzle step labels changed from Roman numerals (I, II, III) to ordinal words ("Pull First:", "Pull Second:", etc.) to avoid confusion with lever numbers
