# Usurper Reborn - v0.30.8 The Living World

NPCs now actively seek out players with problems, requests, and opportunities based on what's happening in the world simulation. They also remember what you've done to them and come back for payback. The living world now pulls you in — and pushes back.

---

## New Feature: NPC Petition System

NPCs will now approach you with petitions based on actual world events. Every petition offers meaningful choices with real consequences — your decisions affect NPC relationships, faction standing, alignment, and the news feed.

### Relationship Drama
- **Troubled Marriage**: A married NPC whose spouse is cheating or hostile asks for your help. Counsel them, confront the spouse, exploit their vulnerability, or refuse.
- **Matchmaker Request**: A friendly NPC confesses feelings for someone who doesn't notice them. Play wingman, sabotage the romance, give honest advice, or stay out of it.
- **Custody Dispute**: A divorced NPC claims their ex-spouse won't let them see their children. Mediate between parents, take sides, or stay out of it.

### Faction & Political
- **Faction Mission**: A faction representative approaches with a specific task tied to world events. Crown wants criminals brought to justice, Shadows need retrieval work, Faith seeks to reclaim lost souls. Rewards include gold and faction standing.
- **Royal Petition**: When you're King, subjects approach with grievances — tax relief, justice for crimes, monster threats, marriage blessings. Your rulings affect the treasury, your reputation, and NPC loyalty.

### World Events
- **Dying Wish**: An elderly NPC near the end of their lifespan seeks you out for a final request — deliver a message, hear a confession, or promise to protect their family.
- **Missing Person**: A worried NPC asks if you've seen their missing spouse or friend (who may have been killed). Break the news gently, lie, or investigate.
- **Rivalry Report**: A friendly NPC warns you about growing threats — court plots against your throne, rival NPC teams gaining power, or cross-player conflicts.

### Pacing
- Maximum 3 petitions per play session
- At least 8 location changes and 5 real minutes between petitions
- Each NPC only petitions once per session
- Safe zones (Home, Bank, Church) are petition-free

### Consequences
Every choice ripples through existing systems:
- NPC memories and impressions update based on your actions
- Relationships change (grateful NPCs become friends, wronged NPCs become enemies)
- News feed broadcasts your decisions to all online players
- Alignment shifts (Chivalry for noble acts, Darkness for exploitation)
- Faction standing changes for politically-relevant choices
- Gold and XP rewards for helping NPCs

---

## New Feature: NPC Consequence Encounters

NPCs now remember what you've done to them and come back for payback. These reactive encounters fire based on actual NPC memories and world state — not random chance.

### Four Encounter Types
- **Grudge Confrontation**: An NPC you defeated in combat tracks you down for revenge. Fight them again, intimidate them into backing down, pay them off, or try to flee.
- **Jealous Spouse**: The spouse of an NPC you're having an affair with confronts you. Face them in combat, deny everything (CHA check), confess and apologize, or run.
- **Throne Challenge**: An ambitious NPC challenges your claim to the throne. Defend your crown in combat, use royal authority to dismiss them (CHA check), offer them a position at court, or abdicate.
- **City Control Contest**: A rival NPC team contests your control over city territory. Fight their champion, negotiate a deal, pay tribute, or surrender the territory.

### How It Works
- Consequence encounters fire BEFORE random encounters at each location
- NPCs need actual grudges (defeated in combat within the last 7 days, impression below -0.5)
- Spouse suspicion must reach 40+ before jealous spouse encounters trigger
- Shared cooldown with petition system prevents encounter stacking
- Minimum 5 location changes and 3 real minutes between consequence encounters

### Memory Recording
- Defeating NPCs in tavern duels and street combat now records defeat memories
- Affairs that raise spouse suspicion past the threshold record betrayal memories
- These memories drive future consequence encounters naturally

---

## Main Menu Redesign

The main menu now clearly separates play modes from information:

- **PLAY section**: `[S] Single-Player` and `[O] Online Multiplayer` side by side
- **INFO section**: Story, History, Server List, Credits grouped together
- `[E]` still works as a legacy key for Single-Player
- BBS List renamed to "BBS & Online Server List" for clarity

---

## World Simulation Tuning

### NPC Orientation & Demographics
- NPC sexual orientation distribution rebalanced to realistic proportions: ~96% straight, ~1.5% gay/lesbian, ~1.5% bisexual, ~1% asexual
- All 60 classic NPCs normalized: 57 straight, 1 gay (Thorn Blackblade), 1 lesbian (Dagger Dee), 1 asexual (Zen Master Lotus)
- Non-heterosexual relationships now serve as rare flavor encounters rather than being commonplace
- Default NPC orientation changed from Bisexual to Straight

### Marriage & Pregnancy Rates
- NPC marriage attempt rate reduced from 2% to 0.2% per world tick — marriages now develop gradually over hours rather than minutes
- Pregnancy rates reduced ~4x across all population levels to prevent baby booms

---

## Bug Fixes

- **NPC "formed a powerful new alliance" news spam**: The GoalSystem's "Support Gang" goal (created every tick for NPCs already in gangs) matched the generic "Gang" completion check, immediately completing and firing alliance news every world sim tick. Fixed by narrowing `IsGoalCompleted` and `OnGoalCompleted` to only match "Join"/"Find Gang" goals.
- **Online player saves bloated to 9MB each**: Every player save in online mode included a full serialization of all 60 NPCs (~9MB of brain/memory/goal data) even though the `world_state` table is the authoritative source. Player saves now skip NPC serialization in online mode. Database shrunk from 112MB to 12MB.

---

## Files Changed

| File | Changes |
|------|---------|
| `Scripts/Systems/NPCPetitionSystem.cs` | **NEW** — Complete NPC Petition System with 8 petition types, world-state-driven triggers, rate limiting, and consequence ripples |
| `Scripts/Systems/StreetEncounterSystem.cs` | Added `CheckForConsequenceEncounter()` with 4 encounter types (grudge, jealous spouse, throne challenge, city contest), defeat memory recording in street combat, rate limiting with shared cooldown |
| `Scripts/Locations/BaseLocation.cs` | Wire petition system after narrative encounters; wire consequence encounters BEFORE random encounters with priority ordering |
| `Scripts/Locations/InnLocation.cs` | Record defeat memories when player wins tavern duels |
| `Scripts/AI/EnhancedNPCBehaviors.cs` | Record betrayal memories on spouse when affair suspicion crosses confrontation threshold; marriage attempt rate reduced from 2% to 0.2% per tick |
| `Scripts/AI/PersonalityProfile.cs` | Orientation distribution rebalanced (96% straight, 1.5% gay/lesbian, 1.5% bisexual, 1% asexual); default orientation changed to Straight |
| `Scripts/Data/ClassicNPCs.cs` | All 60 NPCs normalized to realistic orientation distribution (57 straight, 1 gay, 1 lesbian, 1 asexual) |
| `Scripts/Systems/WorldSimulator.cs` | Pregnancy rates reduced ~4x across all population levels |
| `Scripts/Core/GameConfig.cs` | Version 0.30.8, petition pacing constants, consequence encounter constants (`MinMovesBetweenConsequences`, `GrudgeConfrontationChance`, `SpouseConfrontationChance`, `ThroneEncounterChance`, `CityContestChance`, `MinSuspicionForConfrontation`) |
| `Scripts/Core/GameEngine.cs` | Redesigned main menu with PLAY/INFO sections, `[S]` for Single-Player, `[O]` promoted to PLAY section |
| `Scripts/AI/GoalSystem.cs` | Fixed "formed a powerful new alliance" news spam — `IsGoalCompleted` and `OnGoalCompleted` now only match gang-joining goals, not "Support Gang" |
| `Scripts/Systems/SaveSystem.cs` | Skip NPC serialization in online mode (saves ~9MB per player) |
