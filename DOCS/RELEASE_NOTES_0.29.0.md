# Usurper Reborn - v0.29.0 "The Living World"

Three major systems that transform NPC interactions from static templates into a living, breathing world. NPCs now speak with distinct voices, behave according to their personalities, and generate news and gossip that ripple through the community.

---

## Combat Quickbar System

Players now equip up to 9 spells or abilities onto a combat quickbar at the Trainer's [A]bilities menu. Only equipped skills are available in combat -- press [1]-[9] directly to use them. No more scrolling through full spell/ability lists mid-fight. This forces strategic loadout decisions: a healer-focused caster equips different spells than a damage-focused one.

### Quickbar Slots (1-9)
- Unified quickbar for all classes. Spells and abilities share the same 9 slots.
- Spells stored as `spell:{level}` (e.g. `spell:3`), abilities stored by ID (e.g. `power_strike`).
- Quickbar persists across save/load. Old saves auto-migrate via `AutoPopulateQuickbar()` so combat works immediately without visiting the Trainer.

### Trainer Equip/Unequip UI
- **Martial classes** (Warrior, Paladin, Ranger, etc.): The [A]bilities menu at the Level Master now shows your quickbar, available unequipped abilities, and locked abilities. Press [1]-[9] to assign an ability to a slot, [C] to clear a slot, [A] to auto-fill.
- **Spellcaster classes** (Magician, Cleric, Sage): Same quickbar UI, but also retains [L#] to learn new spells and [F#] to forget spells. Forgetting a spell also removes it from the quickbar.

### Combat Menus
- **Removed** `[C] Cast Spell`, `[B] Abilities`, and `[S] Cast Spell` from all combat menus.
- **Added** `[1]-[9]` quickbar slots to all combat menus (standard and screen reader variants).
- Unavailable slots show the reason: `(SILENCED)`, `(No Mana)`, `(CD:3)`, etc.
- Spell quickbar slots in dungeon combat support full target selection: AoE prompts, heal-ally targeting, and single-target selection all work as before.

### Automatic Quickbar Population
- New characters: Quickbar auto-filled with starting spells/abilities at creation.
- Level-up: Newly unlocked spells/abilities auto-added to empty quickbar slots.
- Learning spells: Learned spells via `L#` auto-slot into the first empty quickbar position.
- Save migration: Existing characters get their quickbar auto-populated on first load.

---

## The Stranger/Noctura Storyline Overhaul

The Stranger -- Noctura (Goddess of Shadows, Floor 70 boss) in disguise -- has been completely reworked from generic cryptic dialogue into a meaningful narrative arc about death and rebirth. Every encounter now builds toward the player accepting death as transformation, culminating in the Floor 70 boss fight.

### Receptivity System
- New **Receptivity** metric (-100 to +100) tracks how open the player has been to Noctura's teachings across all encounters.
- Replaces dark alignment as the Floor 70 alliance gate. Engaging with her teachings earns the right to ally peacefully.
- Player responses scored by type: Accepted (+15), Reflective (+12), Engaged (+8), Challenged (+5), Silent (0), Dismissed (-5), Hostile (-10).

### 6 Scripted Story Encounters
Guaranteed, event-triggered encounters that fire exactly once at key narrative beats:
- **After First Death**: The old beggar at the temple asks what you saw on the other side.
- **After Companion Death**: The wounded knight reframes loss -- "Is the river cruel when it reaches the sea?"
- **After First Old God**: The tavern patron explains how death feeds life, destruction feeds creation.
- **The Midgame Lesson** (Level 40+): The dream visitor teaches the cocoon metaphor.
- **The Revelation** (Level 55+): Noctura drops all disguises and reveals her identity and yours.
- **Pre-Floor 70** (Floors 60-69): Your reflection speaks -- preparation for the final encounter.

### Rewritten Floor 70 Dialogue
- **Receptivity 50+**: Full reunion and alliance without combat. Grants Shadow Cloak, ManwesChoice fragment, +30 Ocean Insight.
- **Receptivity 25-49**: Teaching path. Noctura asks "What is death?" Answer correctly for alliance; partially correct triggers a teaching fight at 50% boss power.
- **Receptivity < 0**: Scorned teacher. Enraged Noctura, +25% boss damage.
- **Default (0-24)**: Standard encounter with fight/teach/alliance paths.

### Backward Compatible
- Old saves with Stranger encounters but no receptivity data get estimated receptivity.
- Existing `noctura_ally` flags still work.

---

## Pre-Generated Dialogue Database (562 Lines)

NPC dialogue has been completely overhauled. Instead of the old template-concatenation system that stitched together personality prefixes, archetype vocabularies, and relationship-tiered phrases, all NPC dialogue is now drawn from a database of 562 pre-generatedlines.

At runtime, the existing NPC AI brain determines context (personality, emotion, relationship, memories), and a scoring algorithm selects the best-matching line from the database.

### How It Works
1. **Scoring Algorithm**: Each candidate line is scored based on NPC name match (+10), personality type (+5), relationship tier (+3), emotion match (+3), context match (+2), memory type match (+2), and freshness (+4 for unused lines).
2. **Personality Mapping**: All 60 classic NPCs' personality strings mapped to 8 dialogue personality types: aggressive, noble, cunning, pious, scholarly, cynical, charming, stoic.
3. **Fallback**: If no database match scores high enough, the old template system is used as a seamless fallback.
4. **Repetition Prevention**: Per-NPC tracking of the last 20 used line IDs. Persisted across save/load.
5. **Placeholder Substitution**: `{player_name}`, `{player_class}`, `{npc_name}`, `{player_title}`, `{time_of_day}`.

### Dialogue Categories
- **Greetings** (~205 lines): 8 personality types x 10 relationship tiers with emotion overlays and context variants.
- **Small Talk** (~80 lines): Personality-flavored conversation topics.
- **Farewells** (~51 lines): Organized by personality and relationship tier.
- **Reactions** (~51 lines): Combat event reactions (victory, defeat, flee, ally death) flavored by personality.
- **Mood Prefixes** (~26 lines): Rich narrative shopkeeper greetings based on NPC emotion.
- **Memory References** (~45 lines): NPCs reference past interactions with personality-appropriate flavor.
- **Story NPC Overrides** (~104 lines): Unique dialogue for 14 named NPCs (Grok the Destroyer, Sir Galahad, Lady Morgana, Lysandra, Mordecai, and more).

---

## NPC Living World AI

Major NPC AI overhaul that makes each NPC behave distinctly based on their personality, memories, emotions, and relationships. NPCs now generate visible consequences -- news events, gossip, and emotional ripple effects -- that make the world feel truly alive.

### Personality-Driven Activity Weights
- NPC activities are now influenced by personality traits instead of being mostly random.
- Aggressive NPCs spend more time in the dungeon and training.
- Sociable NPCs wander more and visit Love Street.
- Greedy NPCs shop, bank, and visit the marketplace more frequently.
- Scholarly/intelligent NPCs prioritize training.
- Mystical NPCs visit the temple more often.
- Cautious NPCs seek healing and avoid the dungeon.
- Courageous NPCs dive deeper into dungeons and visit the castle.
- Impatient NPCs are restless -- always moving, rarely at the temple.

### Time-of-Day Activity Modifiers
- NPCs now follow daily rhythms.
- Morning: Training, shopping, temple worship.
- Afternoon: Dungeon exploration, marketplace visits.
- Evening: Socializing at the inn, Love Street, heading home.
- Night: Most NPCs go home; dungeon and shop activity drops sharply.

### Memory-Driven Behavior
- NPCs who were recently attacked seek healing and avoid the dungeon.
- Betrayed NPCs stay home and avoid social locations.
- NPCs who were helped or saved feel more confident to explore.
- NPCs who recently traded return to shops and the marketplace.
- NPCs who witnessed death visit the temple more and avoid the dungeon.

### Enhanced NPC-to-NPC Conflicts
- Rivalry encounters now come in 3 types based on the instigator's personality:
  - **Brawl**: Aggressive NPCs fight directly (existing combat system).
  - **Theft**: Cunning/greedy NPCs steal 5-15% of their rival's gold.
  - **Challenge**: Noble/courageous NPCs issue public challenges -- winner gains confidence, loser loses face.
- All conflict types generate news events and gossip.

### Emotional Cascades
- Strong emotions (intensity > 0.7) spread to nearby NPCs at the same location.
- Anger causes fear in others (or anger in fellow aggressive NPCs).
- Fear spreads as panic. Joy is infectious. Sadness resonates with empathetic NPCs.
- Rate-limited to prevent runaway cascades.
- Dramatic cascades (3+ NPCs affected) generate gossip.

### Goal Completion Consequences
- When NPCs achieve their goals, visible things happen:
  - Wealth goals: News announcement, joy and confidence boost.
  - Power/ruler goals: Confidence and pride boost, gossip.
  - Alliance goals: News announcement, joy for both allies.
  - Revenge goals: News announcement, catharsis (anger clears), confidence boost.
  - Social goals: Joy boost, loneliness clears.

### Gossip System
- A pool of up to 20 gossip items accumulates from game events.
- Every tick, there's a 10% chance a sociable NPC at a social location spreads gossip to the news feed.
- Gossip expires after being shared 2-3 times -- old news fades naturally.

---

## Quality of Life

### People Nearby Shows All NPCs
The "People Nearby" talk menu no longer caps at 10 NPCs. All NPCs at the current location are listed and selectable.

### Class Name on Save File Screen
The Save File Management screen now shows each character's class next to their name:
```
[1] Cleric (Cleric) - Level 1 | Autosave | 2026-02-10 18:28:28
[2] Jack (Warrior) - Level 40 | Manual Save | 2026-02-10 13:57:03
```

---

## New Files
- `Scripts/Data/NPCDialogueDatabase.cs` - Dialogue database with scoring algorithm and tracking
- `Scripts/Data/DialogueLines_Greetings.cs` - 205 greeting lines
- `Scripts/Data/DialogueLines_SmallTalk.cs` - 80 small talk lines
- `Scripts/Data/DialogueLines_Farewells.cs` - 51 farewell lines
- `Scripts/Data/DialogueLines_Reactions.cs` - 51 reaction lines
- `Scripts/Data/DialogueLines_MoodPrefixes.cs` - 26 mood prefix lines
- `Scripts/Data/DialogueLines_Memory.cs` - 45 memory reference lines
- `Scripts/Data/DialogueLines_StoryNPCs.cs` - 104 story NPC-specific lines

## Modified Files
- `Scripts/Core/GameConfig.cs` - Version 0.29.0 "The Living World"
- `Scripts/Core/Character.cs` - Added `Quickbar` property
- `Scripts/Core/NPC.cs` - Added `RecentDialogueIds`; `GetMoodPrefix()` queries database
- `Scripts/Core/GameEngine.cs` - Quickbar migration, restore dialogue IDs, clear tracking on new game
- `Scripts/Systems/WorldSimulator.cs` - Personality weights, time-of-day, memory modifiers, enhanced conflicts, emotional cascades, gossip system
- `Scripts/AI/GoalSystem.cs` - Goal completion consequences with news, emotions, and gossip
- `Scripts/Systems/NPCDialogueGenerator.cs` - All 4 methods query database first with template fallback
- `Scripts/Systems/CombatEngine.cs` - Quickbar combat integration, scripted encounter triggers
- `Scripts/Systems/ClassAbilitySystem.cs` - Quickbar equip/unequip UI for martial classes
- `Scripts/Systems/SpellLearningSystem.cs` - Merged learn + equip quickbar UI for spellcasters
- `Scripts/Systems/SpellSystem.cs` - Quickbar ID helpers
- `Scripts/Systems/SaveSystem.cs` - Serialize quickbar and dialogue IDs
- `Scripts/Systems/SaveDataStructures.cs` - Added `Quickbar`, `ClassName`, `RecentDialogueIds`
- `Scripts/Systems/StrangerEncounterSystem.cs` - Complete Noctura storyline rewrite
- `Scripts/Systems/DialogueSystem.cs` - Receptivity-branching Floor 70 dialogue
- `Scripts/Systems/OldGodBossSystem.cs` - Receptivity-based combat modifiers
- `Scripts/Locations/BaseLocation.cs` - Scripted encounters, all NPCs in talk menu
- `Scripts/Locations/LevelMasterLocation.cs` - Quickbar auto-fill on level-up
- `Scripts/Locations/DungeonLocation.cs` - Pre-Floor 70 encounter checks
- `Scripts/Systems/CompanionSystem.cs` - Scripted encounter triggers
- `Scripts/Systems/FileSaveBackend.cs` - ClassName in SaveInfo
- `Scripts/Systems/SqlSaveBackend.cs` - ClassName in SaveInfo
