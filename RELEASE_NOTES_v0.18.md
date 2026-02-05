# Usurper Reborn v0.18.0-alpha - NPC Relationships / Systems / Verbose Mode

**Release Date:** February 2026
**Codename:** NPC Relationships / Systems / Verbose Mode

This is a major narrative and systems update that adds five new storytelling systems and a complete NPC relationship overhaul. NPCs now live richer lives - they marry each other, have affairs, and react to the player's faction affiliations. The world feels more alive than ever.

---

##  Highlights

- **5 New Narrative Systems** - Dreams, mysterious strangers, town NPC story arcs, NG+ cycle awareness, and joinable factions
- **NPC-to-NPC Marriage** - NPCs autonomously find compatible partners and marry
- **Player Affair System** - Pursue romances with married NPCs (with consequences!)
- **Faction System** - Join The Crown, The Shadows, or The Faith for unique benefits

---

##  New Narrative Systems

### Dream System
Rest at the Inn to experience vivid dreams that become more prophetic as you approach the truth about your identity.

- 20+ unique dreams organized by player level and awakening stage
- Dreams provide philosophical hints and foreshadowing about the Ocean Philosophy
- Some dreams grant awakening points toward enlightenment
- Dreams avoid repetition and scale with story progression

### Stranger Encounter System
A mysterious figure (Noctura in disguise) appears throughout your journey, dropping cryptic hints.

- 10 unique disguises: Hooded Traveler, Old Beggar, Mysterious Merchant, and more
- Location-specific appearances across the game world
- Escalating encounters as you progress through the story
- Suspicion tracking - piece together the clues to uncover the Stranger's identity
- Full revelation scene in the late game

### Town NPC Story System
Memorable NPCs with personal story arcs that unfold over time:

| NPC | Story Arc |
|-----|-----------|
| **Marcus the Wounded Soldier** | Survivor of an Old God encounter, haunted by what he saw |
| **Elena the Grieving Widow** | Lost her husband to the dungeon, seeking closure |
| **Brother Aldric** | A priest experiencing a crisis of faith |
| **The Merchant's Daughter** | Caught in a forbidden love story |
| **The Old Adventurer** | Ready to pass the torch to a worthy successor |

Each NPC has 4-5 story stages triggered by player progress, with meaningful dialogue and potential rewards.

### Cycle Dialogue System (NG+ Aware)
NPCs become subtly aware of the cyclical nature of existence across New Game+ runs:

- **Cycle 1:** Normal greetings
- **Cycle 2:** Subtle deja vu ("Have we met before?")
- **Cycle 3:** NPCs notice something is wrong with the world
- **Cycle 4:** Growing awareness of the repeating pattern
- **Cycle 5+:** Full acknowledgment ("Again. You're back again.")

### Faction System
Three joinable factions with unique benefits and rivalries:

| Faction | Location | Benefit | Requirement |
|---------|----------|---------|-------------|
| **The Crown** | Castle | 10% shop discount | Chivalry > 500 |
| **The Shadows** | Dark Alley | 20% better fence prices | Darkness > 200 |
| **The Faith** | Temple | 25% healing discount | None |

- Faction reputation with cascade effects (helping one may hurt another)
- NPC faction affiliations affect greetings and hostility
- Faction-based ambush mechanics for hostile faction members

---

## NPC Relationship Overhaul

### NPC-to-NPC Marriage
NPCs now autonomously find compatible partners and marry each other!

- Compatibility scoring based on:
  - Attraction (orientation and gender preferences)
  - Character class compatibility
  - Alignment similarity
  - Faction affiliation
- 2% chance per world tick for eligible NPCs (level 5+) to seek marriage
- Wedding announcements appear in the news system
- All marriages tracked in centralized `NPCMarriageRegistry`

### Player Affair System
For those who like drama, players can now pursue romantic relationships with married NPCs.

**Affair Progression Milestones:**
1. Flirting
2. Emotional Connection
3. Secret Rendezvous
4. Became Lovers

**Mechanics:**
- Affair progress tracking with milestone notifications
- Spouse suspicion mechanics (20% chance spouse notices failed attempts)
- Low-commitment personality NPCs are more susceptible
- If affair succeeds, NPC may leave spouse to either marry player or become their lover
- Jilted spouses become hostile to the player
- Full scandal news generation for juicy gossip

---

## Bug Fixes

- **NPCMarriageRegistry not reset on new game** - Starting a new game now properly clears the marriage registry
- **ProcessNPCRelationships never called** - The relationship processing system was implemented but never wired into the game loop; now properly called in WorldSimulator
- **RelationshipSystem.PerformMarriage not synced with registry** - NPC-NPC marriages now properly register in the central tracking system
- **becomeSpouse parameter unused** - The affair divorce logic now properly implements the marry vs. lover branching
- **Dead spouse not checked in affairs** - Added death checks before processing affair interactions
- **Affair values uncapped** - AffairProgress now capped at 200, SpouseSuspicion at 100 to prevent runaway values
- **NPC marriage state not saved** - Added `IsMarried`, `Married`, `SpouseName`, `MarriedTimes` to NPC save data serialization

---

## BBS Door Mode Improvements

### Verbose Debug Mode
New `--verbose` (or `-v`) flag for troubleshooting BBS connection issues:

```bash
UsurperReborn --door32 door32.sys --verbose
```

**Verbose mode provides:**
- Raw drop file contents dumped line-by-line
- Parsed session info (CommType, SocketHandle, ComPort, UserName, etc.)
- Socket/serial initialization details
- Full exception info with stack traces
- Pause points at key stages for reviewing output

This makes it much easier to diagnose why a door game isn't connecting properly.

---

## Save System Updates

- Full serialization support for `NPCMarriageRegistry` (marriages and affairs)
- NPC marriage properties now persist correctly across save/load cycles
- All 5 new narrative systems properly serialize their state
- Existing saves are compatible - new systems will initialize on first load

---

## New Files

| File | Description |
|------|-------------|
| `Scripts/Systems/DreamSystem.cs` | Dream narrative system |
| `Scripts/Systems/StrangerEncounterSystem.cs` | Mysterious stranger encounters |
| `Scripts/Systems/TownNPCStorySystem.cs` | Town NPC story arcs |
| `Scripts/Systems/CycleDialogueSystem.cs` | NG+ cycle-aware dialogue |
| `Scripts/Systems/FactionSystem.cs` | Faction reputation and benefits |

---

## Modified Files

- `Scripts/Core/GameEngine.cs` - New system initialization and save/load integration
- `Scripts/Core/NPC.cs` - Marriage property support
- `Scripts/AI/EnhancedNPCBehaviors.cs` - Affair processing fixes
- `Scripts/Systems/WorldSimulator.cs` - Relationship processing integration
- `Scripts/Systems/RelationshipSystem.cs` - Registry synchronization
- `Scripts/Systems/SaveDataStructures.cs` - NPC marriage data structures
- `Scripts/Systems/SaveSystem.cs` - Marriage/affair serialization
- `Scripts/Locations/*Location.cs` - Faction integration at various locations

---

## How to Experience the New Content

1. **Dreams:** Rest at the Inn and choose to sleep
2. **Stranger Encounters:** Explore different locations - the Stranger may find you
3. **Town NPC Stories:** Talk to named NPCs repeatedly as you level up
4. **Faction System:** Visit the Castle, Dark Alley, or Temple to join a faction
5. **NPC Marriages:** Watch the news system for wedding announcements
6. **Affairs:** Interact romantically with married NPCs (if you dare!)

---

## Upgrade Notes

- Saves from v0.17 and earlier are compatible
- New systems will initialize with default states on first load
- Existing NPC marriages will not be retroactively created; new marriages occur over time

---

## Known Issues

- Faction bonuses are fully implemented but faction recruitment UI is still being refined
- Some Town NPC stories may not trigger if the NPC hasn't spawned in your game

---

## Thank You

Thank you for playing Usurper Reborn! This update adds significant depth to the world simulation. NPCs are no longer just combat targets or quest givers - they have lives, relationships, and stories of their own.


