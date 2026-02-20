# v0.43.0 - Kings and Queens

Major overhaul of the Castle and King system. Taking the throne is no longer trivial, holding it actually matters, and NPCs will fight to take it from you.

---

## New Features

### Real Combat Stats for Throne Challenges
The king fight was previously a joke — hardcoded random HP and STR meant any mid-level player could waltz in and claim the crown. Kings now fight with their actual stats.

- **NPC Kings** use their real STR, DEF, HP, weapon power, and armor power from the world simulation
- **Player Kings** (offline) have their stats loaded from the database so they can defend even when not logged in
- **Defender Bonus**: Kings defending the throne receive +35% HP and +20% DEF — it's their castle, after all
- **Armor matters**: Damage formula now accounts for weapon power and armor power on both sides, not just raw STR
- **Guard fights scale too**: Fallback guard stats now scale with the king's level instead of being weak randoms

### Level Requirements
- **Minimum level raised to 20** (was 10) — you need to earn your shot at the crown
- **Level proximity check**: Challengers must be within 20 levels of the current king. A level 21 player can't challenge a level 50 king — they'll be told to come back when they're stronger

### NPC Threats to Player Kings
NPCs no longer politely ignore player kings. Ambitious NPCs will now challenge you for the throne.

- **Eligibility**: NPC must be level 30+, within 15 levels of the king, and have Ambition > 0.6
- **Warning system**: Before attacking, the NPC posts a news announcement and sends you a direct message: *"THREAT: Lord X (Level 45) has declared intent to challenge for the throne!"*
- **2-tick delay**: You get advance warning before the challenge executes
- **Offline defense**: If you're not online when challenged, your saved stats are loaded with the defender bonus and combat is auto-resolved
- **Result notifications**: Win or lose, you get a message on your next login
- **Rate limited**: Maximum 1 NPC challenge per day so you're not overwhelmed

### Royal Authority Combat Buffs
Being king now gives you a tangible combat advantage everywhere in the game:

- **+10% attack damage** in all combat
- **+10% defense** in all combat
- **+5% max HP** (recalculated dynamically)
- Shown on your status screen as "Royal Authority: +10% ATK, +10% DEF, +5% HP"
- Automatically removed the moment you lose the throne (stats recalculated immediately on dethronement)

### King Daily Stipend
The throne is now personally profitable. Player kings receive a daily gold stipend:

- **500 + (level x 100) gold per day** deposited to personal gold
- A level 20 king earns 2,500g/day. A level 50 king earns 5,500g/day
- Makes holding the throne worth the risk of NPC challenges

### Royal Bodyguards (Dungeon Mercenaries)
Claiming the throne forces you to abandon your team, which left kings unable to progress dungeon content. Now the king can hire up to 3 elite mercenaries from the Castle's `[B] Royal Bodyguards` menu to serve as dungeon companions.

- **Four roles**: Tank (Warrior), Healer (Cleric), DPS (Ranger), Support (Paladin)
- **Level-appropriate**: Mercenaries are generated at your current level with scaling stats and built-in gear
- **Full combat AI**: Healers cast heal spells and use potions, tanks draw aggro, DPS uses class abilities — same AI as regular teammates
- **Hire cost**: 2,000 + (level x 300) gold from personal funds
- **Permanent death**: If a mercenary dies in the dungeon, they're gone — hire a replacement
- **HP persists**: Mercenary health carries over between dungeon runs (saved with your character)
- **Dismissed on dethronement**: Lose the throne and your bodyguards disband

---

## Balance Changes

### Economy Rebalance
The old economy was broken — a fully-staffed castle bled 7,100g/day against only 1,200g/day income. The treasury would drain in about 8 days. Now the numbers actually work.

| Change | Old | New |
|--------|-----|-----|
| Guard salary | Flat 1,000g/day | 300 + (guard level x 20) per day |
| Court maintenance | 1,000g/day | 500g/day |
| Default tax rate | 20g/citizen | 40g/citizen |

**New balance with 3 guards (default settings)**:
- Income: ~2,900g/day (taxes + sales tax)
- Expenses: ~2,900g/day (guards + court + monsters)
- Result: Breakeven with moderate staff. Treasury grows if you run lean.

---

### Establishment Closures Actually Enforced
The king could toggle 8 establishments open/closed via Royal Orders, but no location ever checked the status — closures were purely cosmetic. Now when a king closes an establishment, players are blocked from entering with a "closed by royal decree" message and bounced back to Main Street.

Affected locations: Inn, Weapon Shop, Armor Shop, Bank, Magic Shop, Healer, Marketplace, Church.

### Royal Bounties Create Real Quests
Placing a bounty via Royal Orders or reporting a crime in the Audience Chamber now creates a real bounty quest in the Quest System. Previously these just deducted gold and posted news — the bounty was never trackable or completable.

### Designated Heir Succession
When a king dies or is dethroned, the designated heir (set via Royal Orders) is now checked first. If the heir is alive, not imprisoned, and meets the minimum level requirement, they're crowned automatically. If the heir can't be found or isn't eligible, the system falls through to the existing score-based selection with a news post explaining why.

### Interactive Court Politics
The Court Politics screen was view-only — you could see your court members and their plots, but couldn't do anything about it. Now the king gets an action menu:

- **[D]ismiss** — Remove a court member. Their faction loses loyalty, and the dismissed NPC becomes hostile
- **[A]rrest** — Requires a discovered plot. Arrest the conspirators and imprison them. Costs 500g for trial expenses
- **[B]ribe** — Pay gold to increase a court member's loyalty. If they're plotting and loyalty rises above 60, they abandon the plot
- **[P]romote** — Promote a court member's role. Increases loyalty and influence. Costs 1,000g

### Royal Loan Enforcement
Borrowing from the royal treasury had zero consequences for non-payment. Now overdue loans trigger escalating penalties:

- **Days 1-7 overdue**: -5 Chivalry per day, warning in news feed
- **Days 8-14 overdue**: -10 Chivalry per day, a bounty is posted on the borrower
- **Days 15+ overdue**: -20 Chivalry per day

### Magic Budget Replenishment
The Court Magician's magic budget now replenishes daily from the treasury (500g/day, capped at 20,000). Previously the budget only decreased as spells were cast and never recovered.

### Prison NPC Filtering
Imprisoned NPCs no longer appear at their original locations. Defense-in-depth filter added to `GetNPCsAtLocation()` — even if a system resets an imprisoned NPC's location string, the `DaysInPrison > 0` check prevents them from leaking back into the world.

### Player Guard Salary Payment
Players who join the Royal Guard now actually receive their daily salary. Previously the gold was deducted from the treasury but never deposited to the player's gold.

### Tax Alignment Actually Works Now
The tax alignment system had two bugs. First, the "Neutrals only" option secretly taxed everyone because there was no Neutral enum value — it silently mapped to "All". Second, the tax alignment setting was never actually consulted during tax collection — all NPCs were taxed regardless of the king's chosen alignment. Both fixed: added a proper `Neutral` enum value, and `CalculateDailyIncome()` now filters NPCs by alignment (Good/Holy, Evil/Dark, or Neutral) before calculating citizen tax revenue.

### Royal Blessing Description Fixed
The Royal Blessing claimed to grant "+25 to maximum HP and Mana" and "improved luck" — neither of which were implemented. The actual effect is +10% attack, +10% defense, and improved accuracy in combat. The description now accurately reflects the real bonuses. Duration also reduced from infinite (999 turns) to 50 turns.

### Siege Fight Uses Real King Stats
Team sieges used random stats for the king fight (500-1000 HP regardless of the king's actual power). Now uses the same real stat lookup as solo throne challenges — NPC stats, offline player database lookup, or level-scaled fallback with defender bonus.

### Knighthood Persists Across Save/Load
Noble titles earned through the knighthood ceremony (Sir/Dame) were never serialized — they vanished on save/load. Now properly saved and restored.

### Old God Boss Phase Abilities Fixed
When Old God bosses transitioned to Phase 2 or Phase 3, the dialogue played and the display showed new ability names, but the actual combat abilities stayed locked to Phase 1. Phase transitions now properly unlock the higher-phase abilities with their increased damage scaling. Additionally, phase transition HP thresholds now use the per-boss values from OldGodsData instead of hardcoded 50%/20% — Manwe's unique Phase 3 threshold (10% HP) now works correctly.

---

## Serialization

New fields are saved and restored across all persistence layers (local saves, online state, world sim):

- **Coronation Date** — tracks when the current king took power (ISO 8601)
- **Tax Alignment** — tax targeting preference
- **Monarch History** — historical record of all past kings (name, title, days reigned, coronation date, how their reign ended)
- **Prisoners** — full prison records (name, crime, sentence, days served, bail amount)
- **Orphans** — royal orphanage records (name, age, sex, background, happiness, mother/father names and IDs, race, birth date, soul, real orphan flag)
- **Magic Budget** — court magician's remaining spell budget
- **Establishment Status** — which establishments the king has closed/opened
- **Last Proclamation** — the king's most recent proclamation text and date
- **Royal Loan Amount/Due Day** — player loan tracking (on PlayerData)
- **Royal Loan Bounty Posted** — prevents duplicate bounties for overdue loans
- **Noble Title** — knighthood (Sir/Dame) persists across save/load

### Royal Orphanage Overhaul
The Royal Orphanage was a dead-end gold sink — adopt a random child, raise their happiness, but nothing meaningful ever came of it. Now orphans are real children from the world simulation.

**Real orphans from NPC permadeath**: When both parents of a child die (combat permadeath or natural aging death), the child is automatically placed in the Royal Orphanage. The orphan record preserves the child's real name, parents' names, race, birth date, and soul value. In single-player with fewer NPC deaths, manually adopted orphans (generated) still work as a fallback.

**Orphans age and grow up**: Real orphans age using the same NPC lifecycle clock as the rest of the world (9.6 hours per year). When a real orphan turns 18, the world sim automatically graduates them:
- **30% chance**: Becomes a Royal Guard (if slots available) with loyalty 85 and free recruitment
- **70% chance**: Released as a citizen NPC with soul-based class, race inherited from parents, and registered with the world

**Enhanced orphanage UI**:
- Orphan list now shows Race, Type (Adopted/Orphaned), and a coming-of-age indicator for ages 16+
- **[V] View Details** — See an orphan's full background: parents, race, age, soul, and whether they're a real orphan or adopted
- **[C] Commission** — For real orphans age 16+, the king can spend 1,000g from the treasury to recruit them early:
  - **Guard**: Creates a Royal Guard with loyalty 90
  - **Mercenary**: Generates a Royal Mercenary at the king's level (half the normal hire cost), role based on soul value
  - **Release as NPC**: Creates a citizen NPC immediately

**Orphans transfer on king succession**: Orphans are under royal protection, not personal ownership. When a new king is crowned (challenge, succession, abdication), all orphans automatically transfer to the new monarch.

**No-king gap handling**: If both parents die when there's no king, the child's location is set to "orphanage" in the family system. When a new king is eventually crowned, all pending orphans are picked up automatically.

---

## Bug Fixes

### Player King Treated as NPC After Save/Load
When a human player was king and saved/loaded the game, the king's AI type was always reset to "Computer" because `SetCurrentKing()` hardcodes `AI = CharacterAI.Computer`. The saved AI and Sex values are now properly restored from save data, so player kings remain recognized as human after reload.

### Royal Treasury Resets to 50,000 When Player Takes Throne
When a player seized the throne — either by defeating the king in combat or through a team siege — the royal treasury always reset to the default 50,000 gold, regardless of how much the previous king had accumulated. The new monarch now inherits the full treasury from the previous ruler.

### Dead Code Removed
- Removed unused `King.AvailableSpells` property and `InitializeDefaultSpells()` method — the Court Magician menu hardcodes its own spell list and never referenced this
- Removed dead "Level Masters" menu option from Royal Orders (was a stub that just displayed a message)

---

## Files Changed

| File | Changes |
|------|---------|
| `Scripts/Core/GameConfig.cs` | Version 0.43.0 "Kings and Queens", raised `MinLevelKing` 10 → 20, `BaseGuardSalary` 1000 → 300, 15+ new constants for king combat, defender bonus, Royal Authority, NPC challenges, economy; magic budget constants; royal loan enforcement constants; court politics constants; `TaxAlignment.Neutral` enum value; `RoyalBlessingDuration = 50`; `MaxRoyalMercenaries`, `MercenaryBaseCost`, `MercenaryCostPerLevel`; `OrphanCommissionAge = 16`, `OrphanCommissionCost = 1000` |
| `Scripts/Locations/CastleLocation.cs` | King fight uses real NPC/player/scaled stats with defender bonus and armor formula; guard fight fallback scales with king level; level proximity check; `PlaceBounty()` wired to QuestSystem; `AudienceReportCrime()` creates real bounty; `TriggerNPCSuccession()` checks DesignatedHeir; `ViewCourtPolitics()` interactive menu; siege fight uses real king stats; Royal Blessing UI text fixed; tax alignment Neutral mapping; ManageLevelMasters removed; blessing duration uses GameConfig constant; `[B] Royal Bodyguards` menu; orphanage UI overhaul (enhanced list with Race/Type columns, `ViewOrphanDetails()`, `CommissionOrphan()` with Guard/Mercenary/NPC options, `MarkOrphanChildDeleted()`); king transition passes inherited orphans to `CreateNewKing()` |
| `Scripts/Locations/DungeonLocation.cs` | `AddRoyalMercenariesToParty()` creates Character objects from king's mercenaries and adds to dungeon party |
| `Scripts/Systems/ChallengeSystem.cs` | NPC threat system; `_lastDesignatedHeir` field; `ClaimEmptyThrone()` checks designated heir first; clear mercenaries on dethronement; `CrownNewKing()` and `ClaimEmptyThrone()` pass inherited orphans to `CreateNewKing()` |
| `Scripts/Core/King.cs` | Default tax rate uses `GameConfig.DefaultTaxRateNew` (40); court maintenance uses `GameConfig.BaseCourtMaintenance` (500); magic budget daily replenishment; removed dead `AvailableSpells` property and `InitializeDefaultSpells()`; `CalculateDailyIncome()` filters NPCs by TaxAlignment; RoyalOrphan class enhanced with MotherName/FatherName/MotherID/FatherID/Race/BirthDate/Soul/IsRealOrphan/ComputedAge; `CreateNewKing()` accepts inherited orphans and calls `PickUpOrphanedChildren()` |
| `Scripts/Core/Character.cs` | Royal Authority +5% max HP; `RoyalLoanBountyPosted` bool; `RoyalMercenary` class; `RoyalMercenaries` list; `IsMercenary` bool; `MercenaryName` string |
| `Scripts/Systems/CombatEngine.cs` | Royal Authority +10% ATK/DEF; `PlayBossPhaseTransition()` now updates `monster.SpecialAbilities` to include Phase 2/3 abilities; `CheckPhase()` uses per-boss thresholds from OldGodsData; `HandleMercenaryDeath()` for bodyguard permanent death; mercenary HP/Mana sync after combat |
| `Scripts/Systems/DailySystemManager.cs` | King daily stipend; royal loan enforcement; player guard salary payment |
| `Scripts/Systems/WorldSimulator.cs` | Guard salary references updated to scale with NPC level; `CheckForOrphanedChildren()` called from `ProcessNPCAging()` and `MarkNPCDead()`; `IsParentDeadOrMissing()`, `DetermineOrphanRace()` helpers; `ProcessOrphanAging()` in `SimulateStep()`; `ProcessOrphanComingOfAge()` auto-graduates at 18 (30% guard, 70% NPC); `OrphanBecomesRoyalGuard()`, `OrphanBecomesNPC()` graduation paths; `PickUpOrphanedChildren()` static method for king transitions |
| `Scripts/Systems/WorldSimService.cs` | Serialize/restore CoronationDate, TaxAlignment, MonarchHistory, Prisoners, Orphans (with expanded fields), MagicBudget, EstablishmentStatus, LastProclamation, LastProclamationDate |
| `Scripts/Systems/SaveDataStructures.cs` | `CoronationDate`, `TaxAlignment`, `MonarchHistory` on `RoyalCourtSaveData`; `MonarchRecordSaveData`, `PrisonRecordSaveData`, `RoyalOrphanSaveData` classes; `RoyalOrphanSaveData` enhanced with MotherName/FatherName/MotherID/FatherID/Race/BirthDate/Soul/IsRealOrphan; 6 new king fields; `RoyalLoanAmount`/`RoyalLoanDueDay`/`RoyalLoanBountyPosted` on `PlayerData`; `NobleTitle` on `PlayerData`; `RoyalMercenarySaveData` class and `RoyalMercenaries` on `PlayerData` |
| `Scripts/Systems/SaveSystem.cs` | Serialize/restore all new king fields (including expanded orphan fields), player loan fields, NobleTitle, king AI/Sex, and RoyalMercenaries |
| `Scripts/Systems/OnlineStateManager.cs` | Serialize/restore all new king fields (including expanded orphan fields) |
| `Scripts/Locations/BaseLocation.cs` | Royal Authority display; `EstablishmentTypeMap` and `IsClosedByRoyalDecree()`; clear mercenaries on king sync dethronement |
| `Scripts/Systems/StreetEncounterSystem.cs` | `RecalculateStats()` on street dethronement; clear mercenaries |
| `Scripts/Systems/FamilySystem.cs` | Guard in `ProcessDailyAging()` skips orphanage children (orphanage handles their coming-of-age) |
| `Scripts/Systems/NPCSpawnSystem.cs` | `GetNPCsAtLocation()` filters imprisoned NPCs |
| `Scripts/Core/GameEngine.cs` | Restore player loan fields, NobleTitle, and RoyalMercenaries on load |
