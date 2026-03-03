# Usurper Reborn

## A Persistent Online Text RPG with a Living World

**ALPHA v0.49.3** | **FREE AND OPEN SOURCE** | **GPL v2**

60+ autonomous NPCs wake up, go to work, visit taverns, fall in love, get married, have children, age, and eventually die of old age — all while you're offline. Log back in, read the news feed, and discover that the blacksmith married the barmaid, the king was assassinated, or a new generation just came of age. The world doesn't wait for you.

**Play now**: https://usurper-reborn.net (browser) or `ssh usurper@play.usurper-reborn.net -p 4000` (password: play)

**Download**: [Latest Release](https://github.com/binary-knight/usurper-reborn/releases) | **Report bugs**: [Discord](https://discord.gg/EZhwgDT6Ta) or [GitHub Issues](https://github.com/binary-knight/usurper-reborn/issues)

---

## The Living World

The core of Usurper Reborn is a 24/7 agent-based simulation. NPCs aren't quest dispensers standing in place — they're goal-driven agents with personalities, memories, and opinions about each other and about you.

**Autonomous NPCs** — Each NPC has 13 personality traits, a memory system (100 memories, 7-day decay), and a goal-based AI brain. They choose careers, form gangs, visit shops, train at the guild, and develop relationships with each other independently of player action.

**Full Lifecycles** — Married NPCs can become pregnant, have children, and raise them. Children grow up over real time and eventually join the realm as new adult NPCs. Adults age according to their race's lifespan (Human ~30 days, Elf ~80 days, Orc ~22 days) and die permanently when their time comes. The population turns over. No one is permanent.

**Emergent Events** — Marriages, divorces, affairs, births, coming-of-age ceremonies, natural deaths, gang wars, and political upheavals all happen organically and appear in the live news feed on the website and in-game.

**Persistent Multiplayer** — Connect via browser or SSH to a shared world backed by SQLite. Your actions affect other players. PvP arena, cross-player chat, leaderboards, and a news feed that captures everything happening in the realm.

---

## The Game

Beyond the simulation, there's a deep RPG with 100+ hours of content.

### Character Building
- **11 Classes** — Warrior, Paladin, Assassin, Magician, Cleric, Ranger, Bard, Sage, Barbarian, Alchemist, Jester — each with unique abilities and combat stamina mechanics
- **10 Races** — Human, Elf, Dwarf, Hobbit, Half-Elf, Orc, Gnome, Troll, Gnoll, Mutant — with race-specific lifespans, stats, and lore
- **75 Spells** across 3 caster classes, 44 class abilities, meaningful stat scaling

### 100-Floor Dungeon
- Deterministically generated floors with boss encounters, treasure rooms, traps, and hidden secrets
- 7 corrupted Old Gods sealed in the depths, each with multi-phase combat and meaningful dialogue choices
- 7 Ancient Seals to collect, unlocking the truth about who you are
- 5 endings based on your choices: Conqueror, Savior, Defiant, True, and a secret Dissolution ending

### Story & Narrative
You wake with no memory. A letter in your own handwriting warns you: *"The gods are broken. Collect the Seven Seals. Break the cycle. You are not what you think you are."*

- **Ocean Philosophy** — A Buddhist-inspired awakening system with 7 levels: *"You are not a wave fighting the ocean. You ARE the ocean, dreaming of being a wave."*
- **4 Companions** who can die permanently — Lyris, Aldric, Mira, Vex — each with personal quests and real grief consequences
- **NG+ Cycle System** — Each playthrough, you remember more. NPCs notice.
- **6 Town NPCs with story arcs**, dream sequences, stranger encounters, and faction politics

### Relationships & Politics
- Romance, marriage, children, divorce, affairs, polyamory
- Challenge the throne, recruit guards, manage treasury, navigate court factions
- 3 joinable factions: The Crown, The Shadows, The Faith
- PvP arena with daily limits, gold theft, and leaderboards

### 30+ Locations
Main Street, Inn, Bank, Weapon Shop, Armor Shop, Magic Shop, Healer, Temple, Church, Dark Alley, Level Master, Marketplace, Castle, Prison, Dungeons, Home, Arena, and more.

---

## Origins

Originally inspired by *Usurper* (1993) by Jakob Dangarden, a classic BBS door game. The original Pascal source was preserved by Rick Parrish and Daniel Zingaro. Usurper Reborn maintains compatibility with the original formulas while building an entirely new simulation layer on top.

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

- **Runtime**: .NET 8.0 (LTS) | **Language**: C# 12
- **Codebase**: 100,000+ lines across 150+ files, 68+ game systems
- **NPC Simulation**: Goal-based AI with 13 personality traits, memory system, lifecycle aging
- **Platforms**: Windows (x64/x86), Linux (x64/ARM64), macOS (Intel/Apple Silicon)
- **Multiplayer**: SQLite shared backend, SSH gateway, WebSocket browser terminal
- **Save System**: JSON (local) / SQLite (online) with autosave
- **Website**: Live stats API, SSE event feed, xterm.js terminal

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

### Quest & Bounty SystemDynamic quest content for single-player progression:
- **Quest Hall** - Central hub for viewing quests and bounties
- **Starter Quests** - 11 pre-made quests spanning levels 1-100
- **Open Contract Bounties** - Kill any NPC with a bounty and get paid immediately (no claiming required)
- **King's Bounties** - The NPC King posts bounties on criminals and NPCs
- **Auto-Refresh** - Completed bounties automatically replaced with new targets
- **Difficulty Scaling** - Easy, Medium, Hard, Extreme quest tiers

### Achievement SystemTrack your progress with 50+ achievements:
- **Combat** - Monster kills, boss defeats, combat milestones
- **Progression** - Level milestones, stat achievements
- **Economy** - Gold earned, items purchased
- **Exploration** - Dungeon depths, locations visited
- **Social** - NPC interactions, relationships formed
- **Challenge** - Special accomplishments

### Statistics TrackingComprehensive gameplay statistics:
- Total monsters killed, gold earned, time played
- Deepest dungeon floor reached
- Quests completed, achievements unlocked
- Combat statistics and records

### Difficulty ModesChoose your challenge level:
- **Easy** - 150% XP, 50% monster damage, 150% gold
- **Normal** - Standard balanced experience
- **Hard** - 75% XP, 150% monster damage, 75% gold
- **Nightmare** - 50% XP, 200% monster damage, 50% gold

### Family SystemMarriage and children with real consequences:
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

### Game PreferencesQuick settings accessible from anywhere via `[~]Prefs`:
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

### How to Connect
- **Browser** — Play instantly at [usurper-reborn.net](https://usurper-reborn.net) with an embedded web terminal
- **SSH** — `ssh usurper@play.usurper-reborn.net -p 4000` (password: play)
- **In-Game Client** — Select `[O]nline Play` from the main menu (uses built-in SSH.NET)
- **Self-hosted** — Host your own server (see [SERVER_DEPLOYMENT.md](DOCS/SERVER_DEPLOYMENT.md))

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

### v0.49.3 - Player Experience: Onboarding, Power & Hooks
First combat class tips, weaker floor 1 monsters, God Slayer buff (+20% damage/+10% defense after Old Gods), straggler encounters, next god breadcrumb, NPC story notifications, active buff display, wake-at-sleep-location, Reinforced Door home upgrade, quit menu overhaul, idle timeout warning. Bug fixes: AI Echo equipment/messages, splash screen clipping, `/tell` display names, news feed caps.

### v0.49.1 - Fatigue, Equipment & Combat
Fatigue system (single-player), sleep/rest separation, `/time` command, Power Strike stamina cost, dual-wield off-hand for abilities, weapon handedness fixes, Sell All in shops.

### v0.49.0 - Swords and Lutes
Music Shop with Melodia companion, Compact Mode for small screens, weapon/armor shop procedural overhaul, Bow and Instrument weapon types, weapon requirements for abilities/spells.

### v0.48.x
**v0.48.5** — Power Strike rework, 2H damage buff, shield block overhaul, time-of-day system, herb garden with 5 herb types, 20 new dreams. **v0.48.4** — Per-slot XP distribution, dungeon event splitting, lightning enchant fix. **v0.48.2** — World Boss raid system (8 bosses, 3 phases, shared HP pool, contribution rewards). **v0.48.0** — Bundled WezTerm terminal for desktop/Steam play.

### v0.47.x — Prestige & MUD Polish
5 NG+ prestige classes (55 abilities), MUD client support (Mudlet, TinTin++), enchantments on abilities, combat balance overhaul (+45% monster HP), progressive onboarding, all-slot dungeon loot (93 templates), BBS online bridge, cooperative group dungeons, spectator mode.

### v0.44.x-v0.45.0
Home upgrade system (5 systems × 5 levels), ending/NG+ pipeline fix, ANSI art portraits (all 10 races), cooperative group dungeons for MUD mode, spectator mode.

### v0.40.0-v0.43.x
Rare crafting materials, 5 new enchantment tiers, boss difficulty tuning, stat training, Gambling Den, NPC permadeath, Blood Price murder consequences, Kings and Queens castle overhaul, relationship/quest fixes, BBS compatibility.

### v0.28.0-v0.30.x
PvP Combat Arena, NPC lifecycle (pregnancies, aging, natural death), player teams, mail, trading, bounties, auction house, team wars, castle siege, NPC analytics dashboard, spell/ability effects overhaul, quest system rebuilt, server optimization.

### v0.25.x-v0.27.x
Online multiplayer via SSH, website with browser play and stats dashboard, Magic Shop overhaul (enchanting, accessories, love/death spells), quest system overhaul, BBS door mode, SysOp console, Steam integration with achievements.

### v0.5-v0.21
BBS door mode foundation, Steam integration, dream/stranger/faction systems, NPC relationships, screen reader accessibility, NPC combat AI, resurrection system, team/tournament/betrayal systems, companion quests, New Game+.

*Full release notes for each version in `DOCS/RELEASE_NOTES_*.md`.*

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

- **Jakob Dangarden** — Created the original *Usurper* (1993), the seed this grew from
- **Rick Parrish** — Preserved the Pascal source code
- **Daniel Zingaro** — Tremendous help with the Pascal source
- **The BBS Community** — For keeping the spirit alive
- **All Contributors** — Everyone who has tested, reported bugs, and believed

---

*"You are not a wave fighting the ocean. You ARE the ocean, dreaming of being a wave."*

## Known Issues (Alpha v0.49.3)
- Save files from earlier alpha versions may not be fully compatible
- BBS FOSSIL mode not supported (use `--stdio` flag for FOSSIL-based BBSes)
- Steam features only work when game is launched through Steam client

**Report bugs**: Press `!` in-game, [Discord](https://discord.gg/EZhwgDT6Ta), or [GitHub Issues](https://github.com/binary-knight/usurper-reborn/issues)

---

**Status**: ALPHA v0.49.3 — The world is running. [Watch it live.](https://usurper-reborn.net)
