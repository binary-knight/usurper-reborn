# Usurper Reborn

## ALPHA v0.28.0 - PvP Arena & Living World

**FREE AND OPEN SOURCE SOFTWARE - GPL v2 Licensed**

A faithful recreation of the classic 1993 BBS door game "Usurper" by Jakob Dangarden, transformed with revolutionary AI systems, deep narrative systems, and philosophical storytelling while maintaining 100% Pascal source compatibility.

### Quick Start for Alpha Testers

**Download the latest build for your platform:**
- Go to [Actions](https://github.com/binary-knight/usurper-reborn/actions) → Latest successful run → Download artifact
- Or check [Releases](https://github.com/binary-knight/usurper-reborn/releases) for packaged builds

**Run the game:**
- **Windows**: Run `UsurperReborn.bat` or `UsurperReborn.exe`
- **Linux**: Run `./usurper-reborn.sh` or `./UsurperReborn`
- **macOS**: Run `usurper-reborn-mac.command` or the app bundle

**Build from source:**
```bash
git clone https://github.com/binary-knight/usurper-reborn.git
cd usurper-reborn
dotnet publish usurper-reloaded.csproj -c Release -o publish
./publish/UsurperReborn.exe  # or ./publish/UsurperReborn on Linux/Mac
```

**Play online**: https://usurper-reborn.net (browser) or `ssh usurper@play.usurper-reborn.net -p 4000` (password: play)

**Report bugs**: https://discord.gg/EZhwgDT6Ta (or GitHub Issues)

## About

Usurper Reborn brings the brutal medieval world of the original BBS classic to modern platforms. Every formula, every stat, every quirk from the original Pascal source has been meticulously preserved—and then we added layers upon layers of new content.

What began as a faithful port has evolved into something more: a game that explores themes of **memory, identity, loss, and transcendence** through the lens of a Buddhist-inspired philosophy: *"You are not a wave fighting the ocean. You ARE the ocean, dreaming of being a wave."*

**Original Creator**: Jakob Dangarden (1993)
**Source Preservation**: Rick Parrish, Daniel Zingaro
**Modern Recreation**: Built with C# and .NET 8.0
**License**: GNU General Public License v2 (GPL v2)
**Privacy**: See [PRIVACY.md](PRIVACY.md) - Telemetry is opt-in only

## The Story

You wake in a dormitory with no memory of who you are. A letter in your own handwriting warns you:

> *"The gods are broken. Corrupted. There are SEVEN SEALS hidden in the dungeons below. Collect them. Break the cycle. End the suffering. Trust no one. Especially not the Stranger. And remember this: You are not what you think you are. You never were."*

The letter crumbles to dust. But the words burn in your mind.

**Your Journey:**
- Explore the town of Dorashire and its many locations
- Descend into dungeons that grow darker and more dangerous
- Encounter the Old Gods—some imprisoned, some corrupted, all remembering
- Collect the Seven Seals to unlock the truth
- Discover what you really are... and what the "cycle" truly means

## Features

### Core Gameplay (Pascal Compatible)
- **Classic BBS Dungeon Crawler** - 50+ dungeon levels with original Pascal combat formulas
- **10 Races, 11 Classes** - All original options preserved (Human, Elf, Dwarf, Hobbit, Half-Elf, Orc, Gnome, Troll, Gnoll, Mutant + Warrior, Magician, Assassin, Paladin, Ranger, Cleric, Barbarian, Bard, Jester, Alchemist, Sage)
- **Turn-Based Combat** - Attack, Defend, Power Attack, Precise Strike, Rage, Smite
- **37 Combat Spells** - Complete magic system across Cleric, Magician, and Sage classes
- **Persistent World** - NPCs act, trade, fight, and die even when you're not playing

### Combat Stamina System (NEW)
Resource management for special abilities:
- **Stamina Bar** - Combat resource separate from HP/Mana
- **Per-Round Regeneration** - Stamina recovers each combat round
- **Ability Costs** - All 44 class abilities require stamina (prevents spam)
- **Stat-Based Scaling** - Stamina stat affects max pool and regen rate

### Meaningful Character Stats (NEW)
Every stat now has real mechanical effects like a modern RPG:
- **Strength** - Bonus melee damage (+STR/4)
- **Dexterity** - Hit chance bonus, critical hit chance (5%+), ranged damage
- **Constitution** - Bonus HP, poison/disease resistance (up to 75%)
- **Intelligence** - Bonus mana, spell damage multiplier, spell critical chance, XP bonus
- **Wisdom** - Mana cost reduction (up to 50%), magic resistance, healing power
- **Charisma** - Shop discounts, team size limit, NPC reactions
- **Stamina** - Combat stamina pool and regeneration rate
- **Agility** - Dodge chance (up to 35%), extra attack chance, initiative bonus

### The Ocean Philosophy System (NEW)
A subtle narrative layer that tracks your awakening to deeper truths:
- **7 Awakening Levels** - From ignorance to full enlightenment
- **Wave Fragments** - Cryptic lore pieces scattered throughout dungeons
- **Ocean Insights** - Revelations triggered by key story events
- **Memory Flashes** - Dreams that hint at your forgotten past

### The Amnesia System (NEW)
Your forgotten past holds the key to everything:
- **Memory Fragments** - Recovered through dreams, dungeon exploration, and god encounters
- **The Truth** - You are more than you appear... but what?
- **NG+ Integration** - Each cycle, you remember a little more

### Companion System (NEW)
Recruit allies who travel, fight, and **can die permanently**:
- **4 Unique Companions** - Lyris (tragic love interest), Aldric (loyal shield), Mira (broken healer), Vex (doomed trickster)
- **Relationship Building** - Trust, loyalty, and romance levels
- **Personal Quests** - Each companion has their own story
- **Permanent Death** - When they die, they stay dead. This makes grief real.

### Grief System (NEW)
Death has consequences. When companions fall:
- **5 Grief Stages** - Denial, Anger, Bargaining, Depression, Acceptance
- **Mechanical Effects** - Stats change based on grief stage
- **World Reactions** - NPCs respond to your mourning
- **Bargaining Failures** - No resurrection. No tricks. Death is final.

### The Seven Seals (NEW)
Hidden throughout the dungeon depths:
- **7 Ancient Seals** - Each reveals part of the truth
- **Old God Connections** - Finding seals awakens imprisoned deities
- **Multiple Endings** - Your choices and seals determine your fate

### The Old Gods (NEW)
Six forgotten deities, each with their own tragedy:
- **Veloura** - Goddess of sorrow, trapped in eternal grief
- **Thorgrim** - God of war, broken by endless battle
- **Maelketh** - God of shadows, consumed by darkness
- **Noctura** - The Stranger, mysterious and dangerous
- **Aurelion** - God of light, blinded by his own radiance
- **Terravok** - God of earth, sleeping in stone
- **Manwe** - The Creator, and the key to everything

### Enhanced Dungeon System (NEW)
- **15-25 Rooms Per Floor** - Expanded from original 6-16
- **Secret Rooms** - Hidden chambers with rare treasures
- **Puzzle Rooms** - Logic, riddles, and environmental challenges
- **Lore Libraries** - Discover Wave/Ocean philosophy fragments
- **Meditation Chambers** - Rest and gain Ocean insights
- **Secret Bosses** - The First Wave, The Forgotten Eighth, Echo of Self, The Ocean Speaks

### Multiple Endings (NEW)
Your journey can end in many ways:
- **Conqueror Ending** - Embrace darkness and rule
- **Savior Ending** - Choose light and sacrifice
- **Defiant Ending** - Reject all gods
- **True Ending** - Achieve balance and remember what you are
- **Secret Ending** - For those who truly understand... dissolution

### 50+ Living NPCs
- **Classic Usurper NPCs** - Warriors, Mages, Thieves, Paladins, and more
- **AI-Driven Behavior** - Personality, goals, memories, emotions
- **World Simulation** - NPCs wander, trade, fight, form relationships
- **Talk System** - Chat, ask for rumors, learn about dungeons, challenge to duels

### 30+ Playable Locations
Main Street, Inn, Bank, Weapon Shop, Armor Shop, Magic Shop, Healer, Temple, Church, Dark Alley, Level Master, Marketplace, Anchor Road, Dormitory, Castle, Prison, Love Corner, Team Corner, News, Quest Hall, God World, Dungeons, Home, Gym, Prison Walk, and more.

## Building from Source

This is free and open source software - you can build it yourself!

### Prerequisites
- [.NET SDK 8.0+](https://dotnet.microsoft.com/download/dotnet/8.0)
- Git

### Quick Build
```bash
# Clone the repository
git clone https://github.com/binary-knight/usurper-reborn.git
cd usurper-reborn

# Build and run (framework-dependent, requires .NET runtime)
dotnet build usurper-reloaded.csproj -c Release
dotnet run --project usurper-reloaded.csproj -c Release
```

### Self-Contained Builds (No .NET Runtime Required)

Build a standalone executable that includes the .NET runtime:

#### Windows (64-bit)
```bash
dotnet publish usurper-reloaded.csproj -c Release -r win-x64 -o publish/win-x64 \
  -p:PublishSingleFile=true -p:SelfContained=true
# Run: publish/win-x64/UsurperReborn.exe
```

#### Windows (32-bit)
```bash
dotnet publish usurper-reloaded.csproj -c Release -r win-x86 -o publish/win-x86 \
  -p:PublishSingleFile=true -p:SelfContained=true
```

#### Linux (x64)
```bash
dotnet publish usurper-reloaded.csproj -c Release -r linux-x64 -o publish/linux-x64 \
  -p:PublishSingleFile=true -p:SelfContained=true
chmod +x publish/linux-x64/UsurperReborn
# Run: ./publish/linux-x64/UsurperReborn
```

#### Linux (ARM64 - Raspberry Pi, etc.)
```bash
dotnet publish usurper-reloaded.csproj -c Release -r linux-arm64 -o publish/linux-arm64 \
  -p:PublishSingleFile=true -p:SelfContained=true
chmod +x publish/linux-arm64/UsurperReborn
```

#### macOS (Intel)
```bash
dotnet publish usurper-reloaded.csproj -c Release -r osx-x64 -o publish/osx-x64 \
  -p:PublishSingleFile=true -p:SelfContained=true
chmod +x publish/osx-x64/UsurperReborn
```

#### macOS (Apple Silicon)
```bash
dotnet publish usurper-reloaded.csproj -c Release -r osx-arm64 -o publish/osx-arm64 \
  -p:PublishSingleFile=true -p:SelfContained=true
chmod +x publish/osx-arm64/UsurperReborn
```

## Technical Details

- **Runtime**: .NET 8.0 (LTS)
- **Lines of Code**: 100,000+ across 150+ C# files
- **Pascal Compatibility**: 100% formula accuracy from original source
- **New Systems**: 20+ major systems added (Companions, Grief, Ocean Philosophy, Seals, Old Gods, etc.)
- **Platforms**: Windows, Linux, macOS
- **Architecture**: Single-player local + online multiplayer via SSH
- **Save System**: JSON (local) / SQLite (online) with autosave
- **Online Play**: Browser terminal at usurper-reborn.net or SSH on port 4000

### Project Structure
```
usurper-reborn/
├── Scripts/
│   ├── Core/           # Character, NPC, Item, Monster, GameEngine
│   ├── Systems/        # 40+ game systems
│   │   ├── OceanPhilosophySystem.cs
│   │   ├── AmnesiaSystem.cs
│   │   ├── CompanionSystem.cs
│   │   ├── GriefSystem.cs
│   │   ├── SevenSealsSystem.cs
│   │   ├── StoryProgressionSystem.cs
│   │   ├── PuzzleSystem.cs
│   │   ├── EndingsSystem.cs
│   │   └── ... (many more)
│   ├── BBS/            # BBS door mode support
│   │   ├── DoorMode.cs
│   │   ├── DropFileParser.cs
│   │   ├── SocketTerminal.cs
│   │   └── BBSTerminalAdapter.cs
│   ├── Locations/      # 30+ game locations
│   ├── AI/             # NPC AI systems (Brain, Memory, Goals, Emotions)
│   ├── Data/           # Game data (NPCs, Equipment, Monsters, Old Gods)
│   └── UI/             # Terminal emulator interface
├── Console/            # Console bootstrap and terminal
├── Data/               # JSON game data
├── DOCS/               # Documentation and examples
└── .github/            # CI/CD workflows
```

### Quest & Bounty System (NEW)
Dynamic quest content for single-player progression:
- **Quest Hall** - Central hub for viewing quests and bounties
- **Starter Quests** - 11 pre-made quests spanning levels 1-100
- **Open Contract Bounties** - Kill any NPC with a bounty and get paid immediately (no claiming required)
- **King's Bounties** - The NPC King posts bounties on criminals and NPCs
- **Auto-Refresh** - Completed bounties automatically replaced with new targets
- **Difficulty Scaling** - Easy, Medium, Hard, Extreme quest tiers

### Achievement System (NEW)
Track your progress with 50+ achievements:
- **Combat** - Monster kills, boss defeats, combat milestones
- **Progression** - Level milestones, stat achievements
- **Economy** - Gold earned, items purchased
- **Exploration** - Dungeon depths, locations visited
- **Social** - NPC interactions, relationships formed
- **Challenge** - Special accomplishments

### Statistics Tracking (NEW)
Comprehensive gameplay statistics:
- Total monsters killed, gold earned, time played
- Deepest dungeon floor reached
- Quests completed, achievements unlocked
- Combat statistics and records

### Difficulty Modes (NEW)
Choose your challenge level:
- **Easy** - 150% XP, 50% monster damage, 150% gold
- **Normal** - Standard balanced experience
- **Hard** - 75% XP, 150% monster damage, 75% gold
- **Nightmare** - 50% XP, 200% monster damage, 50% gold

### Family System (NEW)
Marriage and children with real consequences:
- **Marriage** - Court NPCs through the relationship system, marry at the Church
- **Polyamory Support** - Multiple marriages allowed for those who prefer it
- **Children** - Have children who inherit traits from both parents
- **Child Bonuses** - Children under 18 provide stat boosts to parents:
  - +2% XP per child (up to +10% for 5+ children)
  - +50 Max HP, +5 Strength, +3 Charisma per child
  - +100 Gold/day per child
  - Alignment bonuses based on children's behavior
- **Aging System** - Children grow up over time (1 week real time = 1 year in-game)
- **Coming of Age** - At 18, children become adult NPCs who join the world
- **Custody & Divorce** - Family drama with real mechanical effects

### Game Preferences (NEW)
Quick settings accessible from anywhere via `[~]Prefs`:
- **Combat Speed** - Normal, Fast, or Instant text display
- **Auto-heal** - Toggle automatic healing potion use in combat
- **Skip Intimate Scenes** - "Fade to black" option for romantic content

## Estimated Playtime

How long to complete Usurper Reborn:

| Playstyle | Hours | Description |
|-----------|-------|-------------|
| **Casual** | 40-60 | Main story, reach level 50-60, see one ending |
| **Full Playthrough** | 100-150 | All seals, all gods defeated, multiple endings |
| **Completionist** | 200-400 | All achievements, all companions, all quests, level 100 |

*Note: Playtime varies based on difficulty mode and exploration style.*

### Online Multiplayer (NEW)
Play with others in a shared persistent world:
- **Browser Play** - Play instantly at [usurper-reborn.net](https://usurper-reborn.net) with an embedded terminal
- **SSH Access** - Connect via `ssh usurper@play.usurper-reborn.net -p 4000` (password: play)
- **In-Game Client** - Select `[O]nline Play` from the main menu (uses built-in SSH.NET)
- **Shared World** - All online players share NPCs, economy, politics, and news
- **In-Game Auth** - Register/login with your own account and password
- **Live Stats** - See who's online, leaderboards, and world stats on the website
- **Cross-Player Chat** - `/say`, `/tell`, `/who` commands
- **Server Deployment** - Host your own server (see [SERVER_DEPLOYMENT.md](DOCS/SERVER_DEPLOYMENT.md))

### BBS Door Mode
Run Usurper Reborn as a door game on modern BBS software:
- **Auto-Detection** - Game reads DOOR32.SYS and auto-configures for your BBS. No special flags needed.
- **Fully Tested** - Synchronet (Standard I/O), EleBBS (Socket), Mystic BBS (Socket + SSH)
- **Should Work** - WWIV, GameSrv, ENiGMA (auto-detected by name)
- **SSH Support** - Auto-detects encrypted transports and switches to Standard I/O mode
- **DOOR32.SYS & DOOR.SYS** - Both drop file formats supported
- **Multi-Node Support** - Each node gets isolated session handling
- **BBS-Isolated Saves** - Saves stored per-BBS to prevent user conflicts
- **SysOp Console** - In-game admin console for player management, difficulty settings, MOTD, and auto-updates
- **In-Game Bug Reports** - Players can press `!` to submit bug reports directly from a BBS session
- **Cross-Platform** - Works on Windows x64/x86, Linux x64/ARM64, and macOS

**Quick Setup for Sysops:**
```bash
UsurperReborn --door32 <path>      # Just point it to your DOOR32.SYS - that's it!
UsurperReborn --verbose            # Enable verbose debug output for troubleshooting
```

For detailed BBS setup instructions, see [DOCS/BBS_DOOR_SETUP.md](DOCS/BBS_DOOR_SETUP.md).

## What's Still In Development

### Future Enhancements
- Audio and enhanced ANSI art
- Additional companion personal quest storylines
- Expanded faction recruitment ceremonies

### Completed in v0.28.0 - PvP Arena & Living World

**PvP Combat Arena (Online Multiplayer):**
- **Arena Location** - New location accessible from Main Street in online mode. Challenge other players to asynchronous PvP combat.
- **Opponent Selection** - Browse online and offline players within ±20 levels. See name, level, class, and online status. Can't attack the same player twice per day.
- **Asynchronous Combat** - Defender is loaded from their saved data at full HP and AI-controlled. Uses the existing `CombatEngine.PlayerVsPlayer()` system.
- **Persistent Damage** - Attacker's HP/mana damage persists after combat (classic Usurper risk/reward). Attacker is autosaved after every fight.
- **Gold Theft (Bidirectional)** - Winner steals 10% of the loser's gold on hand. Defender's gold atomically updated via SQL. Bank gold is safe.
- **XP Rewards** - Attacker earns XP scaled by level difference (25-5000 XP range).
- **Daily Limits** - Maximum 5 PvP attacks per day. Level 5 minimum to enter the Arena.
- **PvP Death Handling** - Deaths handled in-Arena with proper messaging, XP loss, gold penalty, and resurrection at Inn.
- **Defender Notifications** - Defenders receive in-game messages about attacks (win or loss, gold stolen).
- **PvP Leaderboard** - Top players ranked by wins, with gold/white/yellow highlighting for top 3.
- **Fight History** - View the last 10 PvP fights with winner highlighting.
- **Personal Stats** - View your PvP kills, deaths, and win rate.
- **While You Were Gone** - Login summary shows PvP attacks, unread messages, and recent world news since last session.
- **News Integration** - PvP victories and defeats posted to the shared news feed.
- **PvP Achievements** - "Arena Initiate" (first win), "Arena Veteran" (10 wins).
- **Website PvP Leaderboard** - PvP rankings on the website landing page with top-3 styling.
- **Stats API** - PvP leaderboard added to the `/api/stats` endpoint for the website dashboard.

**NPC Children, Aging & Natural Death:**
- **NPC Pregnancies** - Married NPC couples can become pregnant (1% chance per world tick for eligible females age 18-45). Dynamic rate adjusts based on population (3% if underpopulated, 0.5% if overpopulated). Max 4 children per couple.
- **NPC Children** - Children are born after ~7 hours of gestation and grow up using the existing Child aging system. When they reach age 18, they automatically become full NPCs and join the realm.
- **Accelerated Aging** - NPCs age at 9.6 real hours per game-year (~30 real days for a full Human lifespan). Race-specific lifespans: Human 75, Elf 200, Dwarf 150, Orc 55, Gnoll 50, etc.
- **Natural Death** - When an NPC exceeds their race's lifespan, they die permanently. No respawn, no resurrection. "The soul moves on." Widowed spouses have their marriage state cleared.
- **Birthday Announcements** - NPCs receive birthday announcements when their age increments, visible in the news feed.
- **NPC Level-Up News** - NPC level-ups posted to news feed with race and class info.
- **NPC Divorce** - Married NPC couples have a 0.3% base chance per world tick to divorce, with personality-driven modifiers (low commitment, high flirtatiousness, alignment mismatch increase chance; both high-commitment reduces by 80%). Divorce cancels pregnancies and frees both NPCs to remarry.
- **Polyamorous Relationships** - NPCs with Polyamorous or OpenRelationship personality can seek additional partners while already married. Both parties must be poly/open for a polyamorous union to form.
- **Affairs** - Flirtatious married NPCs (>0.6) have a 15% chance of conceiving with someone other than their spouse. Unmarried flirtatious NPCs can conceive casually. Affair babies track the actual father separately from the spouse.
- **Story NPC Protection** - Story NPCs are exempt from age-death to prevent breaking narrative quests.
- **Legacy Save Migration** - Existing NPCs without birth dates are assigned random ages 18-50 with computed birth dates.

**The Living World (Website):**
- **SSE Live Feed** - Real-time Server-Sent Events replace polling for instant delivery of world events to the website.
- **Lifecycle Color Coding** - Natural death (purple), pregnancy (soft pink), birth (bright pink), birthday (gold), coming-of-age (teal), respawn (green), divorce (dark red), affair (hot pink), polyamory (violet).
- **Side-by-Side Layout** - NPC activity and player news feeds displayed in a CSS grid layout on the website.

**Bug Fixes:**
- **Backspace in SSH/Online Mode** - Fixed garbage characters from backspace key in SSH mode with custom `ReadLineWithBackspace()`.
- **Duplicate Players in PvP & Leaderboards** - Emergency autosave rows filtered from all player queries.
- **Website "Most Popular Class" Showing Unknown** - NULL class_id from empty accounts filtered out.
- **PvP Double Kill/XP/Gold Counting** - Removed duplicate calls from ArenaLocation.
- **PvP Gold Theft Never Worked** - Fixed missing `Gold = playerData.Gold` in character builder.
- **PvP Death Shows "Monster Defeats"** - Death handled entirely in ArenaLocation.
- **Bidirectional Gold Theft** - Added defender-wins gold theft via `AddGoldToPlayer()`.

### Completed in v0.27.1 - Bug Fix & Balance

**Major Bug Fixes:**
- Seth Able exploit fix (generic NPC talk menu bypass)
- Armor/Weapon shop duplicate items fix (Studded Belt x200)
- Old God floor progression gates (5 bypass methods fixed)
- Veloura (Floor 40) Old God spawn fix and save quest chain
- Purify spell now works (cure_disease handler was missing)
- Dungeon ascending restriction fix (couldn't go up past level-10)
- Online Play color fix (ANSI output for SSH/stdio mode)
- Duplicate login prevention (same character in two sessions)
- Unidentified weapon equip exploit blocked
- Auto-combat now uses healing potions at low HP
- Troll racial regeneration (1-3 HP/round in combat)
- Boss room monster HP display fix (HP > MaxHP)
- Spell failure now shows roll details
- Linux auto-updater crash fix (CRLF, pgrep, cp bugs)
- Hall of Recruitment removed (Godot-era crash)
- Quick `/potion` command from any location

### Completed in v0.27.0 - Online Multiplayer

**Online Server:**
- Full online multiplayer via SSH with shared SQLite database
- In-game authentication (PBKDF2 password hashing, constant-time comparison)
- Online player tracking with heartbeat system
- Inter-player chat and messaging
- Automated server setup script, systemd services, backup scripts

**Website (usurper-reborn.net):**
- Browser-based play via xterm.js WebSocket terminal
- Live stats dashboard with online players, aggregate stats, highlights, news feed
- Stats REST API (`GET /api/stats`) with 30-second cache
- Comprehensive landing page with game lore, classes, races, companions, Old Gods, endings
- SSL via Let's Encrypt, nginx reverse proxy

### Completed in v0.26.1 - Inn Overhaul & Balance

**Drinking Contest - Full Minigame Rewrite:**
- Complete rewrite from passive auto-loss into interactive minigame based on original Pascal DRINKING.PAS
- Up to 5 NPC opponents drawn from living town NPCs with their own soberness values
- Drink choice: Ale (mild), Stout (medium), Seth's Bomber (rocket fuel) - affects elimination speed
- Soberness system based on Constitution + Strength + Stamina, drunk commentary from the original game
- Player input each round: drink or try to bow out gracefully (Constitution check)
- Opponents visibly stagger and drop out, soberness evaluation between rounds
- Winner gets level-scaled XP reward (level * 700), losers get consolation XP

**Seth Able Balance Nerf:**
- Seth now scales with player level (always 3 levels above, min 15, max 80) - no longer a static punching bag
- Daily fight limit: 3 challenges per game day, Seth refuses after that
- Fixed inflated XP exploit: Seth was using monster nr:99 giving ~15,800 XP per fight from level-based formula
- Rewards now flat and modest: level * 200 XP + level * 5 gold (down from ~15,800 XP + ~12,300 gold)
- Diminishing returns: rewards halve after 3rd lifetime victory, fame reduced to +1 after first win
- Seth stays knocked out for the rest of the day when defeated (no re-enter exploit)

**Bug Fixes:**
- Fixed character creation stat display bug: HP/Mana now correct from creation (RecalculateStats was missing)
- Fixed ! key conflict: Bug report no longer intercepts resurrect command in Home and Team Corner
- Added BBS List to main menu showing BBSes running Usurper Reborn with SysOp contact info

### Completed in v0.26.0 - Magic Shop Overhaul

**Magic Shop - Complete Overhaul:**
- **Equipment Enchanting** - Enchant equipped weapons and armor with 9 tiers of enchantments (+stat, Divine Blessing, Ocean's Touch, Ward, Predator, Lifedrinker). Max 3 enchantments per item. Persists across save/load.
- **Enchantment Removal** - Strip enchantments to re-enchant with different stats (50% of original cost)
- **Modern Accessory Shop** - Buy rings, necklaces, and belts from the Equipment system with paginated category browser, rarity-colored listings, and upgrade/downgrade indicators
- **Love Spells** - Cast enchantments on NPCs to improve relationships (4 tiers, respects daily cap, mana cost)
- **Death Spells (Dark Arts)** - Kill NPCs via dark magic with alignment consequences (3 tiers, INT scaling with diminishing returns, protected NPCs immune)
- **Mana Potions** - New consumable unique to Magic Shop. Combat integration lets players choose between healing and mana potions.
- **Scrying Service** - Pay gold to reveal NPC details (class, level, alive/dead, relationship, personality traits)
- **Wave Fragment: The Corruption** - New story content earned through the Magic Shop (requires Awakening 3+, 3+ fragments, death spell usage)
- **Enhanced Shopkeeper Dialogue** - Class-specific, story-aware, alignment-reactive, awakening-aware conversation with Ravanella
- **Loyalty Discount System** - 5%/10%/15% discounts based on total gold spent at Magic Shop
- **8 New Achievements** - Apprentice Enchanter, Master Enchanter, Lovelorn Mage, Shadow Caster, Angel of Death, Magical Patron, The Corruption Remembered, Bejeweled
- **5 New Statistics** - Enchantments applied, love/death spells cast, magic shop gold spent, accessories purchased

**Unidentified Items System:**
- Rare+ dungeon loot may drop unidentified (40% Rare, 60% Epic, 80% Legendary, 100% Artifact)
- Mystery names by power level ("Ornate Unidentified Armor", "Shimmering Unidentified Ring")
- Hidden stats until identified at Magic Shop (100 + level*50 gold)
- Inventory display in magenta with "???" for unidentified items

**Bug Fixes & Polish:**
- Unicode rendering fix for BBS terminal compatibility
- Healing potion duplicate display and price inconsistency fixes
- Blocking Thread.Sleep converted to async Task.Delay for BBS compatibility
- Dead code cleanup (~350 lines removed)
- Cursed item exploit fix (can no longer drop, unequip, or sell cursed items)

### Completed in v0.25.14 - BBS Door Mode

**BBS Compatibility:**
- **EleBBS socket mode working** - Fixed overlapped I/O handle init, SSH-safe socket verification
- **Mystic BBS SSH support** - Auto-detects redirected I/O for encrypted transports
- **ANSI color fix** - Bright colors (bright_red, bright_green, etc.) now render correctly over sockets
- **Output routing fix** - Game output now properly routes through SocketTerminal in socket mode
- **Telnet negotiation** - Proper WILL ECHO/SGA for keystroke visibility
- **In-game bug reports** - Press `!` to submit reports via Discord webhook (works on BBS)

### Completed in v0.25.7/v0.25.8 - SysOp Console

**SysOp Administration Console:**
- **BBS Door Welcome Screen Access** - SysOps can press `%` at the BBS welcome screen (before character selection) to access admin console
- **Configurable Security Level** - New `--sysop-level <number>` flag allows SysOps to set their BBS's security threshold (saved to config)
- **Player Management** - View all players, delete player saves, full game reset with double confirmation
- **Difficulty Settings** - Adjust XP/Gold/Monster HP/Damage multipliers (0.1x to 10.0x)
- **Message of the Day** - Set announcements for all players on login
- **Monitoring** - View game statistics, debug log viewer, active NPC list with pagination
- **Auto-Update System** - Background update check with notification banner, one-click install
- **Persistent Config** - All settings saved to `sysop_config.json` per-BBS

**Enhanced Dungeon Journal:**
- Ocean Philosophy awakening level (0-7) with descriptions
- Wave Fragment collection progress (X/10)
- Old God encounter status for all 7 gods
- Town NPC story progress for memorable NPCs
- Companion recruitment and status tracking
- Seal locations show "hidden somewhere in town" instead of "floor 0"

### Completed in v0.25.1 - BBS Compatibility

**BBS Socket Handle Improvements:**
- **Windows Handle Diagnostics** - New `GetFileType()` and `GetHandleInformation()` calls identify handle types
- **Raw Handle Fallback** - FileStream fallback for BBSes where .NET Socket class fails to wrap inherited handles
- **Handle Type Detection** - Identifies if handle is socket/pipe, console device, or invalid
- **Enhanced Verbose Output** - Better diagnostic messages for BBS sysops troubleshooting connections

### Completed in v0.25.0 - Quest System Overhaul

**Quest System Overhaul:**
- **Level-Appropriate Monsters** - Quests now use MonsterFamilies system with level capping
- **Equipment Purchase Quests** - New Merchant Guild quests for weapons (35%), armor (30%), accessories (20%), shields (15%)
- **Dungeon Quest Improvements** - All dungeon objectives capped to accessible floor range

**Dungeon Floor Locking Rework:**
- Players can always leave to town from special floors to gear up
- Players can always descend to deeper floors, even from uncleared special floors
- Ascending blocked until floor cleared - must defeat Old God or claim Seal

**Mini-Boss Champion System:**
- 10% of random encounters spawn Champion monsters (elite versions)
- Champions have 1.5x HP/stats with unique names per monster family
- Champions ALWAYS drop equipment (Uncommon+ rarity)

**Dynamic NPC Dialogue System:**
- 11 relationship tiers from Married to Hate with unique greetings
- Archetype vocabularies (guards, merchants, thieves, priests)
- Memory-aware dialogue (20% chance to reference past interactions)
- Context-aware comments (30% chance to notice player state)
- Emotional indicators and personality modifiers

**Critical Bug Fixes:**
- Backstab/mercy escape capped at 75% (was exploitable to 100%)
- Critical hit clamped to 5-50% (was uncapped)
- Dissolution ending now reachable (flag was never set)
- Ocean's Memory spell now works (effect was never implemented)
- Bloodlust duration fixed (was 999 turns, now 5)
- NPC conversation menu stays open after interactions

**Balance Improvements:**
- Boss HP/stats multiplier increased from 1.5x to 2.0x
- Sage class buffed, Troll rebalanced, Gnoll buffed, Mutant given bonuses
- Epic equipment now requires level 45+, Legendary requires 65+
- Marriage system rebalanced (7+ day minimum, personality-based NPC acceptance)

### Completed in v0.21 - Steam Native Libraries

**Steam Native Library Fix:**
- **Missing DLL Fix** - Steam builds were failing with "Dll was not found" because native Steam libraries were not bundled
- **CI/CD Native Library Extraction** - Build pipeline now automatically extracts steam_api64.dll and libsteam_api.so from Steamworks.NET NuGet package
- **Depot Bundling** - Native libraries are copied to Steam depot folders during CI/CD build

**Local Development Support:**
- **SteamNative Folder Structure** - Added csproj support for local Steam native libraries in SteamNative/win64/, linux64/, etc.
- **Conditional Inclusion** - Native libraries only copied when files exist (won't break builds if missing)

### Completed in v0.20 - Steam Achievements Fix

**Critical Steam Build Fix:**
- **CI/CD Steam Configuration** - Fixed Steam builds to use `-c Steam` configuration instead of `-c Release`
- **STEAM_BUILD Flag** - Steam builds now properly define STEAM_BUILD preprocessor directive
- **Achievement Unlocking** - Manual achievements (married, first_friend, no_death_10, etc.) now sync to Steam
- **Stat-to-Achievement Linking** - Stats continue to auto-trigger linked achievements via Steam's server-side processing

**Dev Menu Steam Tools:**
- **Reset Steam Stats** - New `[R]` option in Dev Menu to reset all Steam stats and achievements for testing
- **Steam Status Logging** - Enhanced debug logging to show Steam initialization state and achievement sync attempts

**Build System Improvements:**
- **Explicit Steam Configurations** - Added Steam and SteamRelease to project configurations with Release optimizations
- **Separate Steam Builds** - CI/CD now builds Steam-specific executables for Steam depot upload

**Achievement System Enhancement:**
- **SyncUnlockedToSteam** - New method to sync previously-unlocked achievements to Steam on game load
- **Diagnostic Logging** - Added detailed logging for Steam availability and achievement unlock attempts

### Completed in v0.19 - Steam Integration

**Steam Platform Support:**
- **Steam Build Configuration** - Conditional compilation for Steam vs non-Steam builds
- **Steamworks.NET Integration** - Achievement syncing with Steam when running through Steam client
- **Steam Achievement Unlocking** - All 47 achievements sync to Steam profile
- **Steam Stats Tracking** - Player statistics tracked via Steam Stats API
- **Graceful Fallback** - Game works identically when not launched through Steam

**Achievement System Fixes:**
- **Chest Opening Tracking** - `treasure_hunter` achievement now properly tracks chests opened
- **Secret Discovery Tracking** - `secret_finder` achievement now properly tracks secrets found
- **Friendship Tracking** - `social_butterfly` achievement now tracks new friendships formed
- **Easter Egg Implementation** - Hidden `easter_egg_1` achievement now discoverable in Dark Alley
- **Flawless Victory Detection** - Fixed bug where `flawless_victory` checked total damage instead of combat damage

**Team System Fix:**
- **Gang Encounter Bug** - Fixed bug where joining a gang via street encounter resulted in empty team
- **Real Team Integration** - Gang encounters now use actual existing teams from world simulation
- **Proper Team Joining** - Players now correctly join teams with existing NPC members

**BBS Door Mode Improvements:**
- **FOSSIL Driver Guidance** - Clear error message when FOSSIL mode fails explaining .NET limitations
- **--stdio Recommendation** - Automatic suggestion to use Standard I/O mode for FOSSIL-based BBSes
- **EleBBS Compatibility** - Specific guidance for EleBBS users in error messages

**Test Suite Improvements:**
- **Streamlined Test Suite** - Removed redundant validation tests, keeping focused unit tests
- **Deterministic Spell Tests** - Fixed flaky CastSpell test by setting proficiency level
- **212 Passing Tests** - Clean test suite with no flaky tests

### Completed in v0.18 - NPC Relationships / Systems / Verbose Mode

**Five New Narrative Systems:**
- **Dream System** - Prophetic dreams during rest at the Inn that become more vivid as you approach the truth. 20+ unique dreams tied to player level and awakening progress.
- **Stranger Encounter System** - Mysterious encounters with Noctura in 10 different disguises throughout the game, dropping cryptic hints about your true nature.
- **Town NPC Story System** - Memorable NPCs with personal story arcs (Marcus the Wounded Soldier, Elena the Grieving Widow, Brother Aldric, and more) that unfold over time.
- **Cycle Dialogue System** - NG+ aware dialogue where NPCs become increasingly aware of the cyclical nature of existence across playthroughs.
- **Faction System** - Three joinable factions (The Crown, The Shadows, The Faith) with unique benefits, reputation cascades, and bitter rivalries.

**NPC Relationship Overhaul:**
- **NPC-to-NPC Marriage** - NPCs autonomously find compatible partners and marry based on attraction, class, alignment, and faction compatibility.
- **Player Affair System** - Pursue married NPCs, progress through affair milestones, and potentially convince them to leave their spouse for you.
- **Scandal News** - Marriage and affair drama generates realm-wide news announcements.

**Accessibility & Quality of Life:**
- **Screen Reader Mode** - Full screen reader support for dungeon combat, PvP combat menus, and all game interfaces
- **Verbose BBS Debug Mode** - New `--verbose` flag shows raw drop file contents and connection debugging
- **Combat UI Improvements** - PvP combat menu redesigned to match dungeon combat quality

**Bug Fixes:**
- Fixed NPC stats showing 0 in faction ambush encounters (corrupted base stats)
- Fixed constant faction ambushes (was rolling per-NPC instead of per-travel)
- Fixed NPC marriage state not persisting across save/load
- Fixed affair system issues (dead spouse checks, value caps, divorce logic)

### Completed in v0.8
- **NPC Teammate Combat AI** - NPCs now cast offensive spells, use class abilities, and heal more intelligently
  - Spell-casters (Magician, Sage, Cleric, Paladin, Bard) now cast attack spells in combat
  - Class abilities: Warrior Power Attack, Ranger Multi-Shot, Assassin Backstab, Paladin Smite, Bard Inspire
  - Improved healing logic: healers heal at 70% HP, others at 50%, self-preservation at 25%
- **Royal Blessing System** - King can now bestow Royal Blessing status effect (+10% combat stats for a day)
- **Enhanced Castle Throne Room** - Expanded royal court with audiences, blessings, and political intrigue
- **Team Equipment System** - NPCs now spawn with proper equipment based on class and level
- **Companion XP Display** - Companions now show XP progress during combat like NPC teammates
- **Old God Boss Improvements** - Boss abilities now scale with phase (100%/150%/200% strength)
- **Dungeon Entry Fee System** - Overleveled teammates pay entry fees based on level difference
- **Street Encounter Fix** - Romantic partners no longer appear as hostile random encounters

### Completed in v0.7
- **Screen Reader Accessibility** - Full screen reader support for visually impaired players
- **Dungeon Reward Balancing** - XP/gold from events now scales with dungeon level using `level^1.5` formula
- **Quest Level Fix** - Dungeon expedition quests now correctly validate player level requirements
- **NPC Combat Level Fix** - Town NPC encounters now display correct level in combat
- **Equipment Persistence** - Dynamic equipment (dungeon loot) now properly saves and loads
- **Multi-Level XP Sharing** - Sharing XP now correctly grants multiple levels at once
- **Potion Use Fix** - Using potions at full health no longer wastes your turn
- **Monster HP Display** - Monster MaxHP now displays correctly in combat

### Completed in v0.6
- **BBS Door Mode** - Full support for running as a BBS door game (DOOR32.SYS, DOOR.SYS)
- **Linux ARM64 Build** - Added support for Raspberry Pi and ARM servers
- **Save Isolation** - BBS saves stored per-BBS to prevent user conflicts across different BBSes
- **Character Name Locking** - In door mode, character names match BBS usernames
- **Socket I/O** - Direct socket communication via inherited handles from BBS software
- **Console Fallback** - Automatic fallback to console I/O when socket unavailable

### Completed in v0.5
- **Resurrection System** - Return to Temple of Light after death; resurrect spouses/lovers from Home
- **Dungeon Level Restrictions** - Floors limited to player level +/- 10 for balanced progression
- **Enhanced Training** - Spend multiple training points at once to reach next proficiency level
- **Team Healing** - Heal individual teammates or entire party with potions during exploration
- **Statistics Tracking** - Now tracks Potions Used, Health Restored, Resurrections, Diseases Cured
- **Shop Improvements** - Better selling interface showing all sellable items
- **Exit Handling** - Clean quit via menu no longer shows emergency save warning
- **Puzzle Fixes** - Lever puzzles now use intuitive 1-indexed numbers

### Previously Completed
- **Team System** - Full team management with Team Corner location
- **Tournament System** - Tug-of-war, single elimination, round robin competitions
- **Betrayal System** - Hidden betrayal tracking with organic NPC betrayals
- **Moral Paradox System** - Complex moral choices with no clear answers
- **Companion Personal Quests** - Auto-trigger at loyalty 50
- **New Game+ / Cycle System** - Carry bonuses forward, scaling difficulty, artifact knowledge

## License & Your Rights

**Usurper Reborn is FREE SOFTWARE licensed under GPL v2**

### Your Rights
- **Use** - Run the game for any purpose
- **Study** - Examine the complete source code
- **Share** - Distribute copies to anyone
- **Modify** - Change the game and distribute improvements
- **Commercial Use** - Even sell your versions (under GPL v2)

### Source Code
- Complete source included with every download
- GitHub: https://github.com/binary-knight/usurper-reborn
- All build tools and scripts included

## Community

Join our Discord server for discussions, feedback, and updates:
**https://discord.gg/EZhwgDT6Ta**

## Acknowledgments

- **Jakob Dangarden**: For creating the original 1993 masterpiece
- **Rick Parrish**: For preserving the Pascal source code
- **Daniel Zingaro**: For tremendous help with the Pascal source
- **The BBS Community**: For keeping the spirit of door games alive
- **All Contributors**: Everyone who has tested, improved, and believed

---

*"You are not a wave fighting the ocean. You ARE the ocean, dreaming of being a wave."*

*In the realm of Usurper, death is not the end—it's just another beginning. The cycle continues until you remember. Will you find the Seven Seals? Will you discover what you truly are? Will you finally... wake up?*

## Alpha Testing Notes

### What to Test
- Character creation and early game flow
- Combat balance across difficulty modes
- Dungeon exploration (floors 1-100)
- NPC interactions and relationships
- Quest system and achievements
- Romance/marriage/family systems
- Story progression and endings

### Known Issues (Alpha v0.28.0)
- Some edge cases in combat may cause unexpected behavior
- NPC AI occasionally makes suboptimal decisions
- Save files from earlier alpha versions may not be fully compatible
- BBS FOSSIL mode not supported (use `--stdio` flag for FOSSIL-based BBSes)
- Some Town NPC stories may not trigger if the NPC hasn't spawned in your game
- Steam features only work when game is launched through Steam client

### How to Report Bugs
1. **In-game**: Press `!` from any location to submit a bug report (works on BBS too!)
2. **Discord**: https://discord.gg/EZhwgDT6Ta
3. **GitHub Issues**: https://github.com/binary-knight/usurper-reborn/issues

---

**Status**: ALPHA v0.28.0 - PvP Arena & Living World
