# Usurper Reborn v1.3.10

## Changelist

### Play page

• Fixed the game's boxes on the play page drawing without their edges and titles (#223)
• Fixed long room descriptions being cut off and the fight log showing box titles (#223)
• Improved the play page text size on desktop and set earlier screens quieter (#223)
• Adjusted the play page to show and send the key the screen prints, one key per action (#223, #231)
• Fixed achievement tags and plain text lines becoming buttons on the play page (#223)
• Added a line under the play page keys saying whether one press or Enter picks an option (#223)
• Added a low health warning in red with a Low HP chip on the play page (#223)
• Added the enemy rank (Boss, Champion) to the play page fight card (#223, #227)
• Fixed places with no scene showing a class portrait, and monsters with no fitting picture (#223)
• Added the district name to the play page in the district layout (#231)

### Combat

• Adjusted new characters to start with Fast combat speed (#231)
• Fixed the combat menus missing Power Attack, Precise Strike, Taunt, Disarm, Hide and Rage (#227)
• Adjusted the combat menus to list only what can be used this round (#227)
• Adjusted healing potions and single heals to ask first when little health is missing, and to refuse at full health with nothing spent (#227)
• Fixed fight stamina going above its maximum after a load or a rest (#227)
• Improved the death page with the lives left, shown again at the Temple (#227)
• Fixed the loot comparison for a two-handed weapon leaving out the off hand weapon (#231)
• Fixed an empty Enter at the loot prompt counting as an invalid choice (#231)
• Fixed the dungeon map legend sitting far from the map (#231)

### Quests and contracts

• Fixed online quests aging with every player's daily reset instead of once a day (#225)
• Fixed Sellsword contracts being claimed and turned in outside the Sellsword Hall (#225)
• Fixed the Bounty Board and the Sellsword board staying empty for low level players (#225)
• Fixed one player's NPC kill paying or ending another player's bounty online (#225)
• Fixed "Survive N floors" quests counting floors from before the quest was taken (#225)
• Improved the quest and contract texts to state their gold, days and what counts (#225)
• Adjusted bandits to show up 3 times as often in the forest and mountains while you hold a bandit contract or a Bandit Raid runs (#235)
• Improved the royal quest to recover a stolen artifact with a Relic Thief waiting on a named dungeon floor (#235)
• Improved Crown criminal bounties to place the named criminal on a dungeon floor, where killing it completes the bounty (#235)
• Improved bounty board champion quests to place the champion on a dungeon floor within your reach (#235)

### Story

• Fixed the New Game+ unlocks shown after an ending not applying in the next cycle (#228)
• Fixed the journal saying Awakening after a reload (#228)
• Fixed Marcus and Greta's town stories never reaching their endings (#228)
• Fixed Old God boss hints staying after the god is resolved (#228)
• Fixed a second try at an Old God using the first try's stance (#228)
• Fixed secret boss wins promising a True Ending or memories the game does not give (#228)
• Adjusted the moral paradoxes on floors 25, 65 and 80 to be offered on any visit once their seal, artifact or Old God condition is met (#234)
• Fixed waking Terravok on floor 80 leaving him impossible to meet; he is still met on floor 95 (#234)
• Adjusted "Walk away from creation" with Manwe to give the Defiant ending (#234)
• Adjusted "Let me take the burden" with Manwe to give the Savior ending, or the Usurper ending on the Usurper path (#234)
• Fixed an alliance with Manwe not counting as saving him (#234)
• Improved the ending screen with a line for each moral paradox answer and for Mira's choice (#234)

### Town and places

• Fixed dungeon event results disappearing before they could be read (#226)
• Adjusted Armor auto-buy to show every pick and the total with tax, then ask once (#226)
• Fixed an invalid key at a street encounter starting the fight (#226)
• Fixed a new life losing the screen reader, menu key and orientation settings (#226)
• Fixed a shop opened from a district leading back to Main Street instead of the district (#226)
• Fixed paying bail ending the session (#229)
• Fixed declining the Lodging bunk ending the session (#229)
• Fixed the dungeon tutorial teaching keys the game does not use (#229)
• Fixed good and dark deed counts refilling on load (#229)
• Added Good Deeds (G) to the classic Main Street menu (#233)
• Improved MUD client maps with the town's real exits (#229)

### The living world

• Fixed a murdered NPC's family forgetting the murder (#230)
• Fixed discovered plots leaving the court's list before they could be arrested, and added a 7 day lapse (#230)
• Fixed jailed NPCs taking part in town life and weddings (#230)
• Fixed an NPC married to a player marrying another NPC (#230)
• Fixed the king executing an NPC the player already rescued (#230)
• Fixed NPC gangs no longer forming after a restart (#230)
• Added grief when your spouse dies outside your fights (#237)
• Fixed the murder news and warning to say the victim will be back (#237)
• Fixed Blessed Rain to make the Healer's healing free, and a Holy Day to make the Cloister and atonement free (#237)
• Adjusted a dying wish message to be delivered by talking to the named person, with the reward paid on delivery (#237)
• Fixed Detect Threats to have a 50 percent chance to uncover an active plot (#237)
• Fixed the world event descriptions to state what each event does (#237)

### Economy

• Fixed the bank paying a hidden 3 percent a day; it pays its stated rate (#233)
• Fixed the Fountain of Vitality to give a real 10 percent max health at every level (#233)
• Fixed online team wages to be charged per player (#233)
• Adjusted giving gold to the poor to cost level x 20 gold (#233)
• Fixed the bank ban not being enforced (#233)
• Fixed the Reinforced Door being sold offline (#233)
• Fixed shop auto-buy and the shop list using a different price from the direct buy (#233)
• Fixed the outpost and street merchant taking gold for a potion you cannot carry (#233)
• Added a way to take back an offline Auction House listing, and unsold listings return after 30 days (#233)
• Fixed the Crown discount, Merchant's Favor, alignment ability and inheritance texts to state what they do (#233)
• Fixed the daily maintenance screen listing an alive bonus and a town pot that paid nothing; both are removed (#233)

### Settlements and outposts

• Adjusted outpost and street merchant potions to cost the town price x 1.25 for your level (#232)
• Added a fifth dungeon outpost, the Cinder Post, on floor 85 (#232)
• Fixed the settlement Barracks to send a militia fighter into your next 3 fights (#232)
• Fixed the Smugglers' Cache to take 10 percent off your next 5 shop purchases (#232)
• Fixed the settlement Market Stall to trade potions and buy backpack gear (#232)
• Fixed the outpost Smoke Bomb's name; it is Smoke Powder (+2 Agility) (#232)

### Characters

• Added a class portrait for all 17 classes in the class preview (#224)

### Online server and docs

• Fixed the online list restarting a player's connected time every world save (#222)
• Added play page screenshots to the README (#220)
• Added a play page link to the Docker landing page (#221)
