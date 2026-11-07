# Usurper Reborn v1.3.11

## Changelist

### Characters

• Fixed new Quick Start characters starting weaker than custom ones and losing Stamina, Agility and Mana at their first fight (#240)
• Fixed saved Quick Start characters that lost their starting HP, Mana, Defense, Stamina and Agility, restored at their next load (#242)

### Town and places

• Added the Faith's Healer discount, 15 to 35 percent by rank, on healing, cures and potions (#239)
• Fixed Healer screen lines showing alignment and world event price changes that were never applied (#239)
• Adjusted the dungeon merchant's healing potion to the town price for your level minus 15 (#239)
• Fixed the street merchant's Smoke Bomb text to say it gives Darkness +2 and no escape (#239)
• Improved the outpost Enchanted Torch (+10 HP) and Lockpick Set (+2 Dexterity) names so they say what each does (#239)
• Fixed Arena, throne room, Dormitory and Love Street menu rows running into the next key or past 79 columns in any language (#241)
• Fixed the French higher or lower prompt in the Inn dice game (#241)
• Fixed Magic Shop identify, curse removal and dungeon reset scroll pages opening under the shop menu (#241)

### Dungeon and events

• Fixed the Ancient Scroll event's result vanishing before it could be read (#239)
• Fixed floor bosses being drawn with the picture of the family rolled for their level; they are drawn as their theme's boss (#241)

### Quests and the living world

• Fixed an invalid key at the murder revenge grudge starting the fight (#239)
• Fixed the arrest message saying imprisoned when no conspirator went to jail (#239)
• Fixed a dying wish message staying with you after its recipient died permanently (#239)
• Fixed a dying wish message not reaching a recipient who holds a family grudge (#239)
• Fixed a quest the world reset failed coming back after a server restart (#241)

### Play page

• Fixed the Continue key missing on Enter screens in Hungarian, Spanish, French and Italian (#243)
• Fixed 256 colour and true colour art drawing in plain ink (#243)
• Fixed the Main Street text window cutting its last line (#243)
• Fixed key buttons missing on choices written (E)quip or (P)ray, (D)esecrate, or (L)eave (#243)
• Fixed long key labels breaking a word on a phone (#243)
• Fixed the target question showing nothing on the fight page (#243)
• Added a scroll bar on the right page when it runs past the window (#243)

### Online server

• Improved the web service to listen on loopback only unless WEB_BIND_HOST is set (#244)
• Improved the web service to log at startup which of its six settings are set, names only (#244)
• Improved the deployment guide to move the web environment file out of the web folder (#244)
• Improved the game server to require a password on connections forwarded through a proxy, and to accept a forwarded client address only from a local connection (#246)
• Adjusted password-free relay logins to be off unless the server is started with --allow-trusted-auth (#246)
