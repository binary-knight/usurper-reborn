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
