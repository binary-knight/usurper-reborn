# v0.44.2 - ANSI Art Portraits

New ANSI art race portraits and removal of broken faction missions.

---

## New Features

### Complete Race Portrait Set
Added ANSI art portraits for Gnoll, Gnome, Human, Hobbit, and Mutant races during character creation, and replaced the Elf portrait with improved art. The side-by-side preview now shows a detailed portrait on the left with race stats on the right. All 10 playable races now have portraits.

### New ANSI Art Splash Screen
Replaced the hand-drawn ASCII title screen with a full-color ANSI art splash screen featuring a detailed illustration with embedded "USURPER REBORN" title text. Credits and version info displayed below the art.

---

## Bug Fixes

### Removed Broken Faction Redeem Missions
Faction redeem missions (offered as random NPC petitions when entering locations) never worked correctly and have been removed entirely. The faction standing system, bounty blood price, and quest completion standing boosts remain functional. This removes ~300 lines of dead code across the petition, quest, and quest hall systems.

---

## Files Changed

| File | Changes |
|------|---------|
| `Scripts/Core/GameConfig.cs` | Version 0.44.2 "ANSI Art Portraits"; removed AssassinContractMinRank, AssassinContractChance, AssassinClassGoldBonus constants |
| `Scripts/UI/RacePortraits.cs` | Added GnollPortrait, GnomePortrait, HumanPortrait, HobbitPortrait, and MutantPortrait arrays; replaced ElfPortrait with new art; added Gnoll, Gnome, Human, Hobbit, and Mutant entries to Portraits dictionary |
| `Scripts/Systems/NPCPetitionSystem.cs` | Removed FactionMission from PetitionType enum; removed TryFactionMission() and ExecuteFactionMission() methods (~200 lines) |
| `Scripts/Systems/QuestSystem.cs` | Removed CreateFactionMission() method (~95 lines); removed Faith "Redeem" TalkToNPC hack |
| `Scripts/Locations/QuestHallLocation.cs` | Simplified DefeatNPC quest hint (removed special Redeem/TalkToNPC branch) |
| `Scripts/UI/SplashScreen.cs` | Replaced hand-drawn ASCII art with full-color 80-column ANSI art splash screen |
