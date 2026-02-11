# Usurper Reborn - v0.28.1 Release Notes

## NPCs Feel Alive

The world simulation has always been running behind the scenes — NPCs shopping, training, exploring, socializing — but players never saw it. This release surfaces that simulation directly to the player, making the world feel genuinely inhabited.

### Location Presence
- When entering any location, you now see which NPCs are currently there and what they're doing.
- Activity descriptions come from the live world simulation: "examining a blade on the rack", "browsing the armor on display", "deep in prayer at the altar", "nursing a drink at the bar", etc.
- Only NPCs you've actually met are shown (based on memory, impressions, or relationship history). Strangers blend into the crowd.
- Up to 3 NPCs displayed per location. If more are present, a count of the others is shown.
- Fallback flavor text for NPCs without a recent simulation activity, based on the location type (e.g., "warming themselves by the fire" at the Inn, "perusing the stalls" at the Marketplace).

### Gossip System
- The Inn's "Listen to rumors" option is now "Listen to gossip" and draws from real simulation events.
- Gossip pulls from the news feed — marriages, divorces, births, deaths, affairs, scandals, coming-of-age events, and more.
- Overheard snippets are wrapped in conversational flavor: "Did you hear?", "Word around town is that...", "People are whispering about...", etc.
- Falls back to the classic static rumor pool when the simulation hasn't generated enough events yet.

### Mood-Aware Shopkeeper Dialogue
- Shopkeepers now greet you based on their current emotional state and how they feel about you.
- A shopkeeper who likes you and is in a good mood: "My favorite customer! What can I do for you?"
- A shopkeeper who dislikes you and is angry: "You've got nerve showing your face here."
- A grieving NPC who recently lost their spouse: "Sorry, I... what did you need?"
- Neutral NPCs cycle through emotion-appropriate greetings (fearful, sad, hopeful, joyful).
- Mood also subtly affects transaction prices (±5-15%), rewarding players who build good relationships.
- Integrated into three shops: Tully (Weapon Shop), Reese (Armor Shop), and Jadu (Healer).

### NPC-Initiated Interactions
- NPCs with strong feelings about you now occasionally approach you unprompted (5% chance per turn).
- **Friendly NPCs** (impression > 0.5) may: gift you gold, share useful information/tips, offer a compliment, or cast a minor healing spell.
- **Hostile NPCs** (impression < -0.5) may: threaten you, warn you to watch your back, or attempt to intimidate you.
- Cooldown system prevents the same NPC from approaching more than once every 10 turns.
- Story NPCs are prioritized when multiple NPCs have strong feelings.

## Website Updates

### GitHub Sponsors Section
- New "Support the Project" section on the landing page displays GitHub Sponsors.
- Sponsor avatars, names, and tier information pulled from the GitHub GraphQL API.
- 1-hour server-side cache to minimize API calls.
- Graceful degradation when no sponsors exist or PAT is not configured.

### Marketing Copy Refresh
- README and website landing page rewritten to better reflect the game's living world and narrative depth.
- New ARCHITECTURE.md document added for developers.

### Pascal Reference Files Removed
- Removed 115 legacy Pascal source files from `PascalReference/` directory. These were from the original 1993 Usurper source used as implementation reference and are no longer needed.

## Files Changed

### Modified Files
- `Scripts/Core/NPC.cs` - `CurrentActivity` property, `GetMoodPrefix()`, `GetMoodPriceModifier()`
- `Scripts/Locations/BaseLocation.cs` - Enhanced `ShowNPCsInLocation()` with activity display, `GetLocationContextActivity()`, `ShowShopkeeperMood()`, `TryNPCApproach()`, `ShowFriendlyApproach()`, `ShowHostileApproach()`
- `Scripts/Locations/InnLocation.cs` - Gossip system replacing static rumors
- `Scripts/Locations/WeaponShopLocation.cs` - Mood-aware greeting for Tully
- `Scripts/Locations/ArmorShopLocation.cs` - Mood-aware greeting for Reese
- `Scripts/Locations/HealerLocation.cs` - Mood-aware greeting for Jadu
- `Scripts/Systems/NewsSystem.cs` - `GossipKeywords` array, `GetRecentGossip()` method
- `Scripts/Systems/WorldSimulator.cs` - Sets `CurrentActivity` on NPCs during simulation ticks
- `web/index.html` - Sponsors section, marketing copy refresh
- `web/ssh-proxy.js` - `/api/sponsors` endpoint with GitHub GraphQL integration
- `scripts-server/usurper-web.service` - `EnvironmentFile` for GitHub PAT

### Deleted Files
- `PascalReference/*.PAS` - 115 legacy Pascal reference files removed
