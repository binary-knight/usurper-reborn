# Usurper Reborn v1.1.0 (draft, accumulating per merged slice)

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
