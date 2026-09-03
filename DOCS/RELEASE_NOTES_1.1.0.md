# Usurper Reborn v1.1.0 "Regalia"

The 1.0 line was Coronation. Regalia is what the crown wears. This release is
the gear and reward loop the 1.0 audit named as the thin midgame track: items
that know what they are, eight gear sets, a Black Market that sells to your
standing, and the gold sink between the shops and the home upgrades that the
July retention analysis found missing. It was built as five reviewed slices
(PRs #119 through #125) and measured where it could be, with the numbers below.

## Items keep their rarity and know their family

Every loot generator used to leave an item's stored rarity at Common and carry
the tier only in the name prefix ("Legendary Flaming Sword"). The game then
guessed the tier back from the name at display time, in whatever language was
active, or from raw power if the name did not match. An item named under one
language and read under another silently lost its tier. Generated items now
store the rolled rarity; the guess survives only for old items whose stored
value is Common, and it is never written back.

Items also record the English template family they came from ("Reinforced
Helm") at creation. This has no effect yet. It is the language-independent key
the gear set bonuses later in 1.1 will use.

Visible changes: backpack lines are coloured by rarity once an item is above
Common; the Black Market gear list is coloured by rarity instead of flat
magenta; the `/gear` screen now shows Rare as cyan and Artifact as bright
yellow, matching every other screen (it had its own table). Adding an item
from a template in the save editor keeps the template's rarity. Stealing gear
from a dormitory sleeper keeps the item's rarity.

## The Black Market sells to your standing

The rotating gear at the Black Market rolled the ordinary dungeon curve at your
level, so a Nightmare-tier cutthroat could walk in and find the same Common
gear a beginner finds in the first dungeon room. Each Dread tier now sets a
floor: Cutthroat sees nothing below Fine, Marauder nothing below Superior,
Terror and Nightmare nothing below Exquisite, and Nightmare is guaranteed one
Legendary piece per refresh (and never more than one Legendary-or-better in a
rotation). The merchandise header says what the floor is.

Prices carry a premium by rarity on top of the existing markup. At level 30,
before Shadows rank and Dread discounts, a Superior piece lands near 5,000
gold, an Exquisite near 17,000, a Legendary near 80,000, and a Mythic near
330,000. This is where the gold you earn between levels 20 and 40 is meant to
go; until now nothing between the shops and the quarter-million home upgrades
could absorb it. Common and Fine prices are unchanged.

## Gear sets

Wearing pieces from the same family now gives set bonuses at two, four, and
six pieces. Eight families are sets in this release, covering levels 1 to 100:
Leather, Chain, Silk, Shadow, Steel, Reinforced, Forged, and Mithril. Shops and
dungeon drops both produce these families, so a set can be assembled from
either. The equipment screen and `/gear` show every set you are wearing, how
many pieces you have, and which tiers are active; an item's detail view says
which set it belongs to.

Bonuses are modest at two pieces, meaningful at four, and build-defining at
six, and they are first-pass numbers tuned per level band. Two examples:
Reinforced (levels 20 to 50, any class) gives +3 armor at two pieces, +30 HP
and +2 Constitution at four, +6 Defence and +5 armor at six. Shadow (rogues)
gives +3 Dexterity, then +3 Agility and +2 weapon power, then +5 Dexterity, +5
Agility and +4 weapon power.

Two things to know. Sets count gear generated after this update; an item you
found or bought before it does not know its family and will not count until it
is replaced. Nobody had a set before, so this is a delayed start rather than a
loss. And sets apply to everyone who wears the gear: NPCs, companions, your
dungeon echo, and your saved character when it defends in the arena or while
asleep all get the same bonuses you do. About half of newly spawned NPCs now
dress in a matching set where they can afford one, so expect some of them to
hit harder or hold up longer than their level alone suggests. A Constitution bonus from a set also
raises HP the usual way, on top of the HP figure listed.

Not yet sets: Runed, Plate, Titan's, Dragon, and Holy. They are the next slice.

## The NPC market no longer strips what it holds

Selling an item to an NPC's market stock or listing it on the marketplace ran
it through an eight-field record and handed it back without its rarity, level
requirement, loot effects, identification, or shield bonus. A reforged
Legendary sold and bought back came back Common. The record now carries the
complete item alongside the old fields, which are still written so an older
binary reads the same save.

## Two races in the equipment catalog

Both are old and both were found by the new tests, which made them easy to
hit. The catalog's first-time build ran without the registry lock and started
by clearing the dictionary, so a session registering loot on another thread
while the first caller was still building it lost its items. And thirteen
catalog queries enumerated the dictionary without the lock, so an NPC spawn
overlapping a loot drop could throw. Every read and the initial build now take
the lock, and the initialized flag is volatile for the ARM builds.

## Balance numbers and what is still open

Every number in this release is a first pass. The set bonuses are tuned per
level band and marked as such in code. The Black Market prices were measured
at level 30 (medians over 200 rolls per tier, before discounts) and are
listed above. The one figure that could not be measured from a development
machine is gold earned per day at level 30, which decides whether a Nightmare
player's roughly 150,000 gold of possible daily spend actually absorbs their
income. If it falls short, the lever is slot count or refresh cadence, not the
premium. Set bonuses beyond plain stats (critical chance, life steal, regen)
and the Runed, Plate, Titan's, Dragon, and Holy sets are the next slice.

## Tests

1,009 passing, up from 977 at v1.0.6. New: `MarketItemDataTests`,
`RarityPlumbingTests`, `BlackMarketFloorTests`, `GearSetTests`,
`NPCSetOutfittingTests`.

## Files Changed

**New**

- `DOCS/RELEASE_NOTES_1.1.0.md`
- `Scripts/Systems/GearSetSystem.cs`
- `Tests/BlackMarketFloorTests.cs`
- `Tests/GearSetTests.cs`
- `Tests/MarketItemDataTests.cs`
- `Tests/NPCSetOutfittingTests.cs`
- `Tests/RarityPlumbingTests.cs`
- `DOCS/STEAM_RELEASE_NOTES_1.1.0.txt`

**Modified**

- `.gitignore`
- `Localization/en.json`
- `Localization/es.json`
- `Localization/fr.json`
- `Localization/hu.json`
- `Localization/it.json`
- `README.md`
- `Scripts/Core/Character.cs`
- `Scripts/Core/GameConfig.cs`
- `Scripts/Core/GameEngine.cs`
- `Scripts/Core/Items.cs`
- `Scripts/Data/EquipmentData.cs`
- `Scripts/Editor/PlayerSaveEditor.cs`
- `Scripts/Locations/BaseLocation.cs`
- `Scripts/Locations/DarkAlleyLocation.cs`
- `Scripts/Locations/DormitoryLocation.cs`
- `Scripts/Systems/InventorySystem.cs`
- `Scripts/Systems/LootGenerator.cs`
- `Scripts/Systems/MarketplaceSystem.cs`
- `Scripts/Systems/NPCSpawnSystem.cs`
- `Scripts/Systems/OnlineStateManager.cs`
- `Scripts/Systems/PlayerCharacterLoader.cs`
- `Scripts/Systems/SaveDataStructures.cs`
- `Scripts/Systems/SaveSystem.cs`
- `Scripts/Systems/ShopItemGenerator.cs`
- `Scripts/Systems/WorldSimService.cs`
