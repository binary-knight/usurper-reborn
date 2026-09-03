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
