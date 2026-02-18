# Release Notes — v0.42.0 "Social Emergence"

*Inspired by Project Sid (Altera.AL, 2024)*

## Overview

This update adds six interconnected systems that create emergent social dynamics among NPCs. Opinions now propagate through gossip, bystanders witness events and form impressions, cultural trends sweep through locations, and NPCs adapt their roles based on community needs. Your reputation spreads through the NPC network — NPCs you've never met may already know your name.

---

## New Feature: Opinion Propagation (Gossip With Teeth)

NPCs at social locations (Inn, Main Street, Temple, Love Street, Auction House) now share their opinions about third parties during conversation. These aren't cosmetic — they actually modify the listener's impressions, creating chains of influence.

- **3% chance per world tick** for gossip to occur
- Speaker picks their strongest opinion about someone the listener might encounter
- Influence strength depends on speaker's sociability, listener's trust of the speaker, existing opinions, and intelligence (skepticism)
- Strong opinions resist change — NPCs don't blindly believe everything they hear
- Impulsive NPCs exaggerate opinions when sharing
- Rate-limited: same gossip pair can only share once every ~10 minutes, and each subject caps at 8 shares per day

## New Feature: Witness System

NPCs at the same location now observe events happening around them and form impressions of the people involved.

- **Brawls, challenges, theft, gang wars** — all generate witness memories
- Witnesses form negative impressions of attackers and thieves, positive impressions of defenders
- Personality modifiers: aggressive NPCs are less bothered by violence, trustworthy NPCs are more offended by theft, loyal NPCs react more strongly when their friends are targeted
- Witnessed events become gossip fuel — witnesses share what they saw at social locations later

## New Feature: Cultural Meme System

Ideas and trends now sweep through the NPC population, influencing their behavior. Different from opinion propagation — memes are about concepts, not people.

- **22 predefined cultural memes** across 7 categories: Danger, Prosperity, Faith, Unrest, Social, War, Mystery
- Examples: "Dungeon Peril" (reduces dungeon exploration), "Gold Rush" (increases shopping/trading), "Love Season" (increases Love Street/Inn visits), "King's Injustice" (increases Dark Alley activity, reduces Castle visits)
- Memes spread between NPCs at social locations (5% chance per tick)
- Each meme modifies NPC activity weights — when "Temple Blessing" is trending, more NPCs visit the temple
- Memes naturally decay over time (~25% per hour) and are removed when weak
- Meme strength varies by location — a trend can be popular at the Inn but unknown at the Castle
- News feed announces when trends emerge and fade

## New Feature: Social Belief Propagation

Faction and deity conversion is no longer random. NPCs must now be socially influenced to join a faction or adopt a faith.

- Existing believers have a 2% chance per tick to proselytize nearby non-believers
- Conversion success depends on: speaker's sociability, listener's mysticism and intelligence, their existing relationship, and faction alignment
- Mystical NPCs are more susceptible (+30%), intelligent NPCs are more skeptical (-20%)
- Friends are more easily converted; enemies resist strongly
- News announces successful conversions

## New Feature: Faction Recruitment

NPCs with strong faction loyalty (standing > 200) now actively recruit for their faction at social locations.

- Crown recruiters target loyal, trustworthy NPCs
- Shadows recruiters target ambitious, greedy NPCs
- Faith recruiters target mystical, patient NPCs
- Success based on personality match and existing relationship
- News announces successful recruitments

## New Feature: Emergent Role Adaptation

NPCs observe what roles are filled or missing in their community and adapt their behavior. This doesn't change their archetype but adds an emergent role that influences activity weights.

- **Evaluated every ~30 minutes** of world simulation time
- Roles include: Defender, Merchant, Healer, Explorer, Guard, Coordinator, Socialite
- NPCs are matched to roles based on personality: high aggression + courage → Defender, high greed + sociability → Merchant, high mysticism + patience → Healer
- Each role adds a +50% weight bonus to associated activities
- After holding a role for ~30 minutes, news announces the NPC's new reputation
- Roles are persistent and saved with NPC data

## New Feature: Player Reputation Propagation

Your actions spread through the NPC network faster than NPC-NPC gossip. NPCs you've never met may have pre-formed opinions based on what they've heard.

- **5% chance per tick** — player reputation spreads 1.5x faster than NPC gossip
- Faction amplification: when a faction member shares an opinion about you, it reaches all faction members at 50% strength
- **Gossip-aware greetings**: NPCs who've heard about you but never met you react differently
  - Positive reputation: "I've heard great things about you!"
  - Negative reputation: "I know what you've done. Stay away from me."
- **Reputation-based pricing**: 5% discount from NPCs who've heard good things, 10% markup from those who've heard bad
- **Location entry whispers**: When multiple NPCs at a location have heard about you, you'll notice them whispering as you arrive

---

## Technical Details

### Activity Weight System

NPC activity selection now has 7 layers of weight modifiers (up from 5):
1. Personality weights
2. Time of day weights
3. Memory weights
4. Relationship weights
5. World event weights
6. **Cultural meme weights** (NEW)
7. **Emergent role weights** (NEW)

### Serialization

- Cultural meme state (active memes, NPC awareness, location strengths) is fully saved and restored
- Emergent roles and role stability ticks are saved per NPC
- Social influence system is stateless (cooldowns reset on load) — its effects are stored in NPC memories which are already serialized

---

## Files Changed

| File | Changes |
|------|---------|
| **NEW** `Scripts/Systems/SocialInfluenceSystem.cs` | Opinion propagation, witness recording, faction recruitment, role adaptation, player reputation spreading |
| **NEW** `Scripts/Systems/CulturalMemeSystem.cs` | Cultural meme creation, spreading, decay, activity weight integration |
| `Scripts/AI/MemorySystem.cs` | Added 5 new MemoryType values (HeardGossip, WitnessedAttack, WitnessedTheft, WitnessedGenerosity, SharedOpinion) |
| `Scripts/Core/NPC.cs` | EmergentRole/RoleStabilityTicks properties, gossip-aware greetings, reputation-based pricing |
| `Scripts/Systems/WorldSimulator.cs` | Hooked all 6 emergence systems into SimulateStep, added witness calls to brawl/challenge/theft events, added meme and role weight layers to activity processing |
| `Scripts/AI/EnhancedNPCBehaviorSystem.cs` | Replaced random 5% believer conversion with social propagation (proselytize + personality-based conversion) |
| `Scripts/Systems/SaveDataStructures.cs` | Added EmergentRole/RoleStabilityTicks to NPCData, CulturalMemeSaveData to StorySystemsData |
| `Scripts/Systems/SaveSystem.cs` | Save/restore CulturalMemeSystem state and NPC emergent roles |
| `Scripts/Core/GameEngine.cs` | Restore EmergentRole/RoleStabilityTicks when loading NPCs |
| `Scripts/Core/GameConfig.cs` | Version 0.42.0, 14 new constants for social emergence system tuning |
| `Scripts/Locations/BaseLocation.cs` | Player reputation whisper effect on location entry |
