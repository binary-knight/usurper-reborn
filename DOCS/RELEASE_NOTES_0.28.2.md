# Usurper Reborn - v0.28.2 Release Notes

## Bug Fix: NPCs Now Visible Everywhere

### NPC Presence in All Locations
- Added `ShowNPCsInLocation()` to 9 locations that were missing it: Weapon Shop, Armor Shop, Healer, Magic Shop, Dark Alley, Church, Marketplace, Level Master, and Castle.
- Previously, NPCs would travel to these locations via the world simulation but were invisible to the player. Now you'll see them wherever they go.

### Removed Overly Strict "Has Met" Filter
- The v0.28.1 location presence feature originally only showed NPCs the player had met (via impressions, memories, or relationships). This filtered out nearly everyone, making the world feel empty.
- Now all alive NPCs at a location are shown, up to 3 with activity descriptions.

### Fixed Location String Mismatches
- Castle: NPCs set their location to "Castle" but the lookup expected "The Royal Castle". Fixed by adding `Castle` to the location string mapping.
- Love Street, Home, Temple: Added missing mappings so NPCs at these locations are found correctly.

## Files Changed

### Modified Files
- `Scripts/Locations/BaseLocation.cs` - Removed "has met" filter from `ShowNPCsInLocation()`; added Castle, LoveStreet, Home, Temple to `GetNPCLocationString()`
- `Scripts/Locations/WeaponShopLocation.cs` - Added `ShowNPCsInLocation()` call
- `Scripts/Locations/ArmorShopLocation.cs` - Added `ShowNPCsInLocation()` call
- `Scripts/Locations/HealerLocation.cs` - Added `ShowNPCsInLocation()` call
- `Scripts/Locations/MagicShopLocation.cs` - Added `ShowNPCsInLocation()` call
- `Scripts/Locations/DarkAlleyLocation.cs` - Added `ShowNPCsInLocation()` call
- `Scripts/Locations/ChurchLocation.cs` - Added `ShowNPCsInLocation()` call
- `Scripts/Locations/MarketplaceLocation.cs` - Added `ShowNPCsInLocation()` call
- `Scripts/Locations/LevelMasterLocation.cs` - Added `ShowNPCsInLocation()` call
- `Scripts/Locations/CastleLocation.cs` - Added `ShowNPCsInLocation()` call
- `Scripts/Core/GameConfig.cs` - Version 0.28.2
