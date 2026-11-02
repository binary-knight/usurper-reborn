# Usurper Reborn v1.3.9

## Changelist

### Website

• Improved the website with a new design, class portraits and Steam first (#178)
• Improved the landing page with larger type and new copy (#180)
• Added the Observatory page with public world data and hourly world history (#191, #192, #193)
• Fixed the Observatory sending private fields for children and NPCs (#189, #190)
• Fixed unknown Observatory data paths answering with the page instead of a 404 (#195)
• Adjusted the web server to refuse its own settings, backup and script files (#209)

### Play page

• Added the play page at /play/ with a full page terminal and status panels (#181)
• Adjusted the landing page Play buttons to open /play/ in a new tab (#182)
• Fixed grouped players not getting their own health and mana updates in the web panels (#184)
• Adjusted the play page to hide the Combat and Chat buttons until the game sends panel data (#186)
• Added a tap pad and whole screen fit to the play page on phones and tablets (#196)
• Fixed the play page compass missing in a dungeon level's first room, after a teleport or a load, and after a fight (#197)
• Fixed the play page showing raw text keys after an update until a hard refresh (#203)
• Added a Discord link to the play page top bar (#204)
• Added the View button before login on the play page (#213)
• Fixed the web footer showing old health, level, gold and potion numbers (#215)
• Added single key menus on the web, on by default and changed in Settings (#215, #217)

### Chronicle view

• Added the Chronicle view to the play page, a book page layout for fights, dungeon rooms and Main Street chosen with the View button, with the Classic view still the default (#200, #201)
• Added monster pictures, dungeon floor scenes, a room map, the Character Status page and the full status footer to the Chronicle view (#208)
• Fixed the Main Street text being covered by the keys in the Chronicle view (#210)
• Fixed the Chronicle fight page not showing what happened each round, and its combat log staying empty (#213)
• Fixed Chronicle pages opening scrolled to the middle, combat keys breaking words, words split at line ends, the splash art, and phone keys covering the text (#213)
• Added the combat round to the Chronicle fight page and combat log (#217)

### The game

• Adjusted Main Street to open in the classic layout by default, with the districts layout a choice in Settings (#211, #212)
• Fixed the combat tips naming keys the combat menu does not use (#215)
• Fixed a street encounter taking keys typed before it appeared (#215)
• Adjusted Enter at the target prompt to attack the last foe you picked while it lives (#215)
• Adjusted Auto-Combat and Speed in combat to the single keys O and X (#216)
• Improved the login box colors with a purple border and title, a green menu and a red Quit (#185)
• Fixed the relay's startup lines showing to players (#187)
• Added the game window opening at the size it was last left at (#214)
