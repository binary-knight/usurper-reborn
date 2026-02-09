# Usurper Reborn - v0.26.1 Release Notes

## Inn Overhaul & Balance

### Drinking Contest - Full Minigame Rewrite

The drinking contest has been completely rewritten from a non-interactive auto-loss into a full minigame faithful to the original Pascal `DRINKING.PAS`.

**Old behavior**: Passive loop with `maxRounds = Stamina / 10` and 30% random failure per round. Low-stamina characters got 0 rounds. No player interaction, no opponents, no choices.

**New behavior**:
- **Up to 5 NPC opponents** drawn from living town NPCs, each with their own soberness value
- **Drink choice**: Ale (strength 2), Stout (strength 3), Seth's Bomber (strength 6) - stronger drinks eliminate contestants faster
- **Soberness system**: Each contestant starts at `(Stamina + Strength + Constitution + 10) / 10`, capped at 100. Each round reduces by `random(1, 22 + drinkStrength)`.
- **Player input each round**: `[D]rink` to keep going, `[Q]uit` to try bowing out gracefully (Constitution check - fail and the crowd forces another drink on you)
- **Visual soberness bar**: `[########------------] 40% - Tipsy`
- **Drunk commentary**: Classic lines from the original game based on soberness level ("Stand still you rats! Why is the room spinning!?", "Sober as a rock...", etc.)
- **Opponents drop out**: NPCs visibly stagger and fall when their soberness hits 0
- **Favourite prediction**: Shows who the crowd expects to win based on stats
- **XP rewards**: Winner gets `level * 700` XP + gold prize. Losers get `level * 100` consolation XP.

### Seth Able Balance Nerf (Exploit Fix)

A player reported "cheesing" Seth Able at the Inn to rapidly boost level and gold. Investigation revealed multiple compounding issues:

**The Exploit**:
- Seth was a static level 15 NPC with fixed stats (150 HP, 35 STR, 20 DEF)
- Monster was created with `nr: 99` which set `Level = 99` for reward calculation
- `GetExperienceReward()` formula: `pow(99, 1.5) * 15` = ~14,800 XP per kill from combat engine
- `GetGoldReward()` formula: `pow(99, 1.5) * 12` = ~11,800 gold per kill from combat engine
- Plus 1,000 XP and 500 gold flat bonus on top
- **Total**: ~16,300 XP and ~12,300 gold per fight
- `sethAbleAvailable` reset on save/load (instance field, not persisted)
- No daily cooldown - unlimited farming

**Nerfs Applied**:

1. **Seth scales with player level** - Always 3 levels above the player (min 15, max 80). HP, STR, DEF, punch, armor power, and weapon power all scale with level. He's always a genuine challenge.
   - HP: `100 + level * 12`
   - STR: `20 + level`
   - DEF: `10 + level / 2`
   - Punch: `20 + level`
   - WeapPow: `15 + level / 2`

2. **Daily fight limit: 3 per day** - After 3 challenges in a single game day, Seth refuses to fight. Resets on new day. Seth also recovers from knockout on new day.

3. **Fixed inflated XP formula** - Changed `nr: 99` to `nr: 1` so the combat engine's level-based formula yields minimal base rewards (~30 XP). The real reward is a controlled flat amount.

4. **Modest flat rewards** - `level * 200 XP` + `50 + level * 5 gold` (down from ~16,300 XP and ~12,300 gold)

5. **Diminishing returns** - After the 3rd lifetime victory:
   - XP and gold rewards halve
   - Fame gain reduced from +10 to +1
   - No more +5 chivalry per win

6. **Seth stays down** - When beaten, Seth is knocked out for the rest of the game day (not just until re-entering the Inn)

### Bug Fixes

- **Character creation stat display bug**: HP went from 34/34 at creation to 34/41 on first equipment purchase. `RecalculateStats()` was never called during character creation, so CON-derived HP bonus and INT/WIS-derived Mana bonus weren't applied until the first equipment purchase triggered recalculation. Now called at end of character creation with HP/Mana set to new maximums.

- **Bug report ! key conflict**: The global `!` handler (bug report) was intercepting the local `!` (resurrect teammate) in both Team Corner and Home locations. `TryProcessGlobalCommand()` was called before `ProcessChoice()`, so the global handler always won. Fixed by checking `!` locally first in both locations before falling through to global command processing.

### New Features

- **BBS List**: New `[B]` menu option on the main menu showing BBSes currently running Usurper Reborn. Includes BBS name, software, address, and port. Invites SysOps to contact on GitHub to be added to the list. Currently lists Shurato's Heavenly Sphere (EleBBS, shsbbs.net).

### Files Changed
- `InnLocation.cs` - Complete drinking contest rewrite (~250 lines), Seth Able nerf with level scaling, daily limits, diminishing returns
- `GameConfig.cs` - Version 0.26.1-alpha, "Inn Overhaul & Balance"
- `CharacterCreationSystem.cs` - Added RecalculateStats() at end of character creation
- `TeamCornerLocation.cs` - Fixed ! resurrect priority over global bug report handler
- `HomeLocation.cs` - Fixed ! resurrect priority over global bug report handler
- `GameEngine.cs` - Added BBS List menu option and ShowBBSList() method
