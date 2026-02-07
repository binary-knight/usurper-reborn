using System;
using System.Collections.Generic;
using System.Linq;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Procedural dungeon generator - creates interesting, explorable dungeon floors
    /// with rooms, corridors, events, and atmosphere
    /// </summary>
    public static class DungeonGenerator
    {
        private static Random random = new Random();

        // Seal floors where Seven Seals are found
        private static readonly int[] SealFloors = { 15, 30, 45, 60, 80, 99 };

        /// <summary>
        /// Generate a complete dungeon floor with interconnected rooms.
        /// Uses deterministic seeding so the same floor level always produces the same layout.
        /// This is critical for save/restore to work correctly - room IDs must map to the same rooms.
        /// </summary>
        public static DungeonFloor GenerateFloor(int level)
        {
            // CRITICAL: Use deterministic seed based on floor level
            // This ensures the same floor layout is generated every time for a given level
            // Seed combines level with a magic number for variety across levels
            random = new Random(level * 31337 + 42);

            var floor = new DungeonFloor
            {
                Level = level,
                Theme = GetThemeForLevel(level),
                DangerLevel = CalculateDangerLevel(level)
            };

            // Determine floor size based on level - EXPANDED for epic dungeons
            // Base 15 rooms, scaling up to 25 for deeper levels
            int roomCount = 15 + (level / 8);
            roomCount = Math.Clamp(roomCount, 15, 25);

            // Generate rooms (seal floors get special treatment)
            bool isSealFloor = SealFloors.Contains(level);
            GenerateRooms(floor, roomCount, isSealFloor);

            // Connect rooms into a navigable layout
            ConnectRooms(floor);

            // Place special rooms (boss, treasure, etc.)
            PlaceSpecialRooms(floor);

            // Populate with events
            PopulateEvents(floor);

            // Set entrance
            floor.EntranceRoomId = floor.Rooms.First().Id;
            floor.CurrentRoomId = floor.EntranceRoomId;

            return floor;
        }

        private static DungeonTheme GetThemeForLevel(int level)
        {
            return level switch
            {
                <= 10 => DungeonTheme.Catacombs,
                <= 20 => DungeonTheme.Sewers,
                <= 35 => DungeonTheme.Caverns,
                <= 50 => DungeonTheme.AncientRuins,
                <= 65 => DungeonTheme.DemonLair,
                <= 80 => DungeonTheme.FrozenDepths,
                <= 90 => DungeonTheme.VolcanicPit,
                _ => DungeonTheme.AbyssalVoid
            };
        }

        private static int CalculateDangerLevel(int level)
        {
            // 1-10 danger rating
            return Math.Min(10, 1 + (level / 10));
        }

        private static void GenerateRooms(DungeonFloor floor, int count, bool isSealFloor = false)
        {
            // Standard room types (weighted for variety)
            var standardRoomTypes = new List<RoomType>
            {
                RoomType.Corridor, RoomType.Corridor, RoomType.Corridor,
                RoomType.Chamber, RoomType.Chamber, RoomType.Chamber, RoomType.Chamber,
                RoomType.Hall, RoomType.Hall,
                RoomType.Alcove, RoomType.Alcove, RoomType.Alcove,
                RoomType.Shrine,
                RoomType.Crypt, RoomType.Crypt
            };

            // Special room types (appear less frequently)
            // NOTE: Removed PuzzleRoom, RiddleGate, TrapGauntlet as they implied mechanics that weren't implemented
            var specialRoomTypes = new List<RoomType>
            {
                RoomType.LoreLibrary,
                RoomType.MeditationChamber,
                RoomType.ArenaRoom
            };

            // Calculate special room distribution based on floor level
            // Removed puzzleRooms since puzzle/riddle mechanics aren't implemented
            int secretRooms = 1 + (floor.Level / 25);       // 1-4 secret rooms
            int loreRooms = floor.Level >= 15 ? 1 : 0;      // Lore rooms after level 15
            int meditationRooms = floor.Level >= 10 ? 1 : 0; // Meditation after level 10
            int memoryRooms = floor.Level >= 20 && floor.Level % 15 == 0 ? 1 : 0; // Memory fragments on specific floors
            int arenaRooms = floor.Level >= 5 ? 1 : 0;      // Arena rooms after level 5

            // SEAL FLOORS: Guarantee extra seal-discovery rooms (Shrines and SecretVaults)
            // These room types trigger guaranteed seal discovery when entered
            int sealRooms = isSealFloor ? 3 : 0; // Add 3 extra seal-appropriate rooms

            // Generate standard rooms first
            int standardCount = count - secretRooms - loreRooms - meditationRooms - memoryRooms - arenaRooms - sealRooms;
            standardCount = Math.Max(standardCount, count / 2); // At least half are standard

            // Room types that can reveal seals (for seal floors)
            var sealRoomTypes = new List<RoomType>
            {
                RoomType.Shrine,
                RoomType.SecretVault,
                RoomType.MeditationChamber
            };

            for (int i = 0; i < count; i++)
            {
                RoomType roomType;

                if (i == 0)
                {
                    // First room is always entrance-friendly
                    roomType = RoomType.Hall;
                }
                else if (i == count - 1)
                {
                    // Last room is boss antechamber leading to boss
                    roomType = RoomType.BossAntechamber;
                }
                else if (i < standardCount)
                {
                    roomType = standardRoomTypes[random.Next(standardRoomTypes.Count)];
                }
                else
                {
                    // Distribute special rooms
                    int specialIndex = i - standardCount;
                    if (specialIndex < secretRooms)
                        roomType = RoomType.SecretVault;
                    else if (specialIndex < secretRooms + loreRooms)
                        roomType = RoomType.LoreLibrary;
                    else if (specialIndex < secretRooms + loreRooms + meditationRooms)
                        roomType = RoomType.MeditationChamber;
                    else if (specialIndex < secretRooms + loreRooms + meditationRooms + memoryRooms)
                        roomType = RoomType.MemoryFragment;
                    else if (specialIndex < secretRooms + loreRooms + meditationRooms + memoryRooms + arenaRooms)
                        roomType = RoomType.ArenaRoom;
                    else if (specialIndex < secretRooms + loreRooms + meditationRooms + memoryRooms + arenaRooms + sealRooms)
                        // Seal rooms - guaranteed seal-discovery room types for seal floors
                        roomType = sealRoomTypes[random.Next(sealRoomTypes.Count)];
                    else
                        roomType = specialRoomTypes[random.Next(specialRoomTypes.Count)];
                }

                var room = CreateRoom(floor.Theme, roomType, i, floor.Level);
                floor.Rooms.Add(room);
            }

            // Shuffle rooms (except first and last) for randomness
            var middleRooms = floor.Rooms.Skip(1).Take(floor.Rooms.Count - 2).ToList();
            for (int i = middleRooms.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                var temp = middleRooms[i];
                middleRooms[i] = middleRooms[j];
                middleRooms[j] = temp;
            }

            // Reconstruct with shuffled middle
            var first = floor.Rooms[0];
            var last = floor.Rooms[floor.Rooms.Count - 1];
            floor.Rooms.Clear();
            floor.Rooms.Add(first);
            floor.Rooms.AddRange(middleRooms);
            floor.Rooms.Add(last);

            // Re-assign IDs after shuffle
            for (int i = 0; i < floor.Rooms.Count; i++)
            {
                floor.Rooms[i].Id = $"room_{i}";
            }
        }

        private static DungeonRoom CreateRoom(DungeonTheme theme, RoomType type, int index, int level)
        {
            var room = new DungeonRoom
            {
                Id = $"room_{index}",
                Type = type,
                Theme = theme,
                IsExplored = false,
                IsCleared = false,
                DangerRating = random.Next(1, 4) // 1-3 danger per room
            };

            // Generate room name and description based on theme and type
            (room.Name, room.Description, room.AtmosphereText) = GenerateRoomFlavor(theme, type, level);

            // Determine what's in the room
            room.HasMonsters = random.NextDouble() < 0.6; // 60% chance of monsters
            room.HasTreasure = false; // Treasure only placed in special designated rooms
            room.HasEvent = random.NextDouble() < 0.3; // 30% chance of special event
            room.HasTrap = random.NextDouble() < 0.15; // 15% chance of trap

            // Generate features to examine
            room.Features = GenerateRoomFeatures(theme, type);

            return room;
        }

        private static (string name, string desc, string atmosphere) GenerateRoomFlavor(
            DungeonTheme theme, RoomType type, int level)
        {
            return (theme, type) switch
            {
                // ═══════════════════════════════════════════════════════════════
                // CATACOMBS - Ancient burial grounds, dust, bones, faded glory
                // ═══════════════════════════════════════════════════════════════
                (DungeonTheme.Catacombs, RoomType.Corridor) => (
                    "Dusty Passage",
                    "A narrow corridor winds between walls of stacked bones, each skull facing outward as if watching your passage. Centuries of dust coat every surface, disturbed only by your footsteps.",
                    "The air is thick and stale. Cobwebs brush your face like ghostly fingers."
                ),
                (DungeonTheme.Catacombs, RoomType.Chamber) => (
                    "Burial Chamber",
                    "Stone sarcophagi stand in orderly rows, their carved lids depicting the noble dead within. Faded tapestries hang in tatters from the walls, their stories lost to time.",
                    "You hear faint scratching from within one of the coffins. Probably just settling stone. Probably."
                ),
                (DungeonTheme.Catacombs, RoomType.Hall) => (
                    "Hall of the Ancestors",
                    "A grand hall where skeletal remains sit enthroned in ceremonial poses, their empty eye sockets fixed on an altar at the far end. Tarnished silver chains drape between the pillars.",
                    "Candles that should have burned out centuries ago still flicker with pale blue flame."
                ),
                (DungeonTheme.Catacombs, RoomType.Shrine) => (
                    "Forgotten Altar",
                    "A crumbling altar stands before a defaced statue - perhaps one of the Old Gods, their name scratched away by fearful hands. Seven candles once burned here, now melted to nothing.",
                    "The Ocean's whispers seem louder near old shrines. The dead remember what the living forget."
                ),
                (DungeonTheme.Catacombs, RoomType.Crypt) => (
                    "Noble's Crypt",
                    "The ornate tomb of a forgotten lord dominates this chamber. Gold leaf still clings to the walls despite centuries of decay. A broken sword lies atop the sarcophagus.",
                    "This person was important once. Now they are bones and dust, like all the rest."
                ),
                (DungeonTheme.Catacombs, RoomType.Alcove) => (
                    "Prayer Nook",
                    "A small alcove carved into the stone holds a simple shrine where mourners once knelt. Dried flowers crumble at your touch, their scent long faded to memory.",
                    "Names are carved into the walls here - hundreds of them, each a life now ended."
                ),

                // ═══════════════════════════════════════════════════════════════
                // SEWERS - Fetid darkness, dripping water, things that lurk
                // ═══════════════════════════════════════════════════════════════
                (DungeonTheme.Sewers, RoomType.Corridor) => (
                    "Waste Channel",
                    "Murky water flows sluggishly through this brick-lined tunnel. The smell is overwhelming - a cocktail of decay and things best left unnamed. Rusted grates line the floor.",
                    "Something moves in the water beside you. Probably just rats. You tell yourself it's just rats."
                ),
                (DungeonTheme.Sewers, RoomType.Chamber) => (
                    "Drainage Junction",
                    "Several massive pipes converge here, creating a domed chamber of slick brick and corroded iron. The constant dripping creates an unsettling rhythm.",
                    "The walls are coated with something that glistens in your torchlight. Best not to touch it."
                ),
                (DungeonTheme.Sewers, RoomType.Hall) => (
                    "Maintenance Hall",
                    "An old workers' area, now abandoned to darkness and decay. Rusted tools hang on pegs, and a rotted workbench lies overturned. Someone lived here once - a bedroll sits in the corner.",
                    "The city above has forgotten this place exists. So have the maps."
                ),
                (DungeonTheme.Sewers, RoomType.Shrine) => (
                    "Shrine of the Outcast",
                    "Those banished from society built a rough shrine here, deep in the filth. Crude symbols are painted on the walls in substances you'd rather not identify.",
                    "Even the forsaken need gods. Perhaps especially the forsaken."
                ),
                (DungeonTheme.Sewers, RoomType.Crypt) => (
                    "Flooded Tomb",
                    "The sewers broke through into this ancient crypt long ago. Water-logged coffins float in the murk, their contents thankfully hidden beneath the surface.",
                    "The dead here have no rest. The water keeps them company in their corruption."
                ),
                (DungeonTheme.Sewers, RoomType.Alcove) => (
                    "Smuggler's Cache",
                    "A hidden alcove where someone stored contraband. Rotted crates still hold rusted weapons and moldy cloth. A skeleton slumps in the corner, a knife in its ribs.",
                    "Whatever deal went wrong here, both parties lost."
                ),

                // ═══════════════════════════════════════════════════════════════
                // CAVERNS - Natural wonder, crystal light, ancient darkness
                // ═══════════════════════════════════════════════════════════════
                (DungeonTheme.Caverns, RoomType.Corridor) => (
                    "Winding Tunnel",
                    "The natural stone walls twist and turn, carved by water over countless millennia. Phosphorescent fungi cling to the damp surfaces, casting an ethereal blue glow.",
                    "The tunnel breathes - a gentle draft flows past you, suggesting vast spaces beyond."
                ),
                (DungeonTheme.Caverns, RoomType.Chamber) => (
                    "Crystal Grotto",
                    "Magnificent crystal formations jut from every surface - floor, walls, and ceiling alike. They catch your light and scatter it into rainbow fragments across the stone.",
                    "The crystals hum with a frequency you feel in your teeth. Something sleeps within them."
                ),
                (DungeonTheme.Caverns, RoomType.Hall) => (
                    "Vast Cavern",
                    "The ceiling vanishes into darkness far above. Stalactites hang like the teeth of some enormous beast, dripping endlessly into pools of crystal-clear water below.",
                    "Your footsteps echo for what seems like forever. You are very small here."
                ),
                (DungeonTheme.Caverns, RoomType.Shrine) => (
                    "Stone Circle",
                    "Natural pillars form a perfect circle, older than any civilization. Carvings depict waves rising from an endless ocean, and figures that might be the Old Gods before they had names.",
                    "Manwe himself once stood in places like this, before the first forgetting. The stone remembers."
                ),
                (DungeonTheme.Caverns, RoomType.Crypt) => (
                    "Petrified Remains",
                    "Figures stand frozen in stone - ancient travelers caught by some geological process and preserved forever. Their expressions are peaceful. Perhaps death was quick.",
                    "The cave has claimed them. In time, it will claim everything."
                ),
                (DungeonTheme.Caverns, RoomType.Alcove) => (
                    "Luminous Pool",
                    "A small grotto holds a pool of impossibly clear water. Glowing creatures drift lazily beneath the surface, their light painting rippling patterns on the ceiling.",
                    "The water looks pure. Refreshing. But nothing that lives here should be touched."
                ),

                // ═══════════════════════════════════════════════════════════════
                // ANCIENT RUINS - Lost civilization, forgotten magic, crumbling grandeur
                // ═══════════════════════════════════════════════════════════════
                (DungeonTheme.AncientRuins, RoomType.Corridor) => (
                    "Ruined Hallway",
                    "Crumbling pillars line what was once a grand corridor. Faded murals depict scenes of a civilization at its height - festivals, coronations, and rituals whose meaning is now lost.",
                    "Your torch illuminates fragments of former glory. Each step crunches on fallen stone."
                ),
                (DungeonTheme.AncientRuins, RoomType.Chamber) => (
                    "Ritual Chamber",
                    "Strange symbols cover the floor in concentric circles, their meaning forgotten but their power still palpable. The ceiling is painted with stars in constellations that no longer exist.",
                    "The air crackles with residual energy. Whatever was summoned here left its mark."
                ),
                (DungeonTheme.AncientRuins, RoomType.Hall) => (
                    "Great Library",
                    "Towering shelves of rotted books and crumbling scrolls stretch to the vaulted ceiling. Some texts still glow faintly with preserved enchantments.",
                    "Whispers seem to emanate from the books themselves - knowledge desperate to be remembered."
                ),
                (DungeonTheme.AncientRuins, RoomType.Shrine) => (
                    "Temple of the Old Gods",
                    "Seven alcoves once held statues of the Old Gods - Manwe's drops of essence, separated from the Great Ocean. Most lie shattered, but one remains: a figure weeping eternal stone tears.",
                    "The Seven Seals were forged in temples like this. Some say the gods' true power still sleeps within."
                ),
                (DungeonTheme.AncientRuins, RoomType.Crypt) => (
                    "Tomb of the Artificers",
                    "The builders who created this place were buried with their greatest works. Intricate automatons stand guard, frozen mid-motion, their power sources long depleted.",
                    "Such knowledge, such craft - lost to time and hubris."
                ),
                (DungeonTheme.AncientRuins, RoomType.Alcove) => (
                    "Scribe's Alcove",
                    "A small workspace where a scholar once recorded the events of their age. The desk is covered in dust, but a single unfinished manuscript remains, the ink still legible.",
                    "The last words read: 'They are breaking through the wards. May the gods have mercy on—'"
                ),

                // ═══════════════════════════════════════════════════════════════
                // DEMON LAIR - Horror, suffering, corruption, wrongness
                // ═══════════════════════════════════════════════════════════════
                (DungeonTheme.DemonLair, RoomType.Corridor) => (
                    "Bone Corridor",
                    "The walls are constructed entirely of fused bones - thousands of them, packed and melted together into a grotesque architecture. Some still have scraps of flesh attached.",
                    "Screams echo from somewhere ahead. Or is it behind? The sound seems to come from everywhere."
                ),
                (DungeonTheme.DemonLair, RoomType.Chamber) => (
                    "Torture Chamber",
                    "Hooks and chains hang from the ceiling, some still bearing their grisly cargo. The floor is sticky with blood - some of it fresh. Implements of pain line the walls.",
                    "Something terrible happened here. Something terrible is still happening here."
                ),
                (DungeonTheme.DemonLair, RoomType.Hall) => (
                    "Summoning Hall",
                    "A massive pentagram is carved into the obsidian floor, its lines still glowing with hellfire. The walls are covered in contracts written in blood - souls bargained and claimed.",
                    "The temperature drops suddenly. Your breath fogs. Something is aware of you."
                ),
                (DungeonTheme.DemonLair, RoomType.Shrine) => (
                    "Altar of Agony",
                    "This altar was built to worship pain itself. The stone is warm to the touch and pulses like a heartbeat. Offerings of suffering have stained it black.",
                    "Every moment of anguish that occurred here has been... saved. Treasured. Fed upon."
                ),
                (DungeonTheme.DemonLair, RoomType.Crypt) => (
                    "Pit of the Damned",
                    "A yawning pit descends into darkness. From below comes the sound of moaning and the scratch of countless hands against stone. The dead here do not rest.",
                    "They were thrown in alive. Some of them are still falling."
                ),
                (DungeonTheme.DemonLair, RoomType.Alcove) => (
                    "Corruption Nursery",
                    "Fleshy pods hang from the walls, pulsing with unholy life. Something is growing inside them - something that was once human. Something that remembers being human.",
                    "They can see you. They are so very hungry."
                ),

                // ═══════════════════════════════════════════════════════════════
                // FROZEN DEPTHS - Deadly cold, preserved horrors, isolation
                // ═══════════════════════════════════════════════════════════════
                (DungeonTheme.FrozenDepths, RoomType.Corridor) => (
                    "Ice Tunnel",
                    "Walls of solid ice reflect your torchlight infinitely, creating a dizzying hall of mirrors. The cold here is beyond cold - it burns.",
                    "Your breath crystallizes instantly. Each exhale falls as tiny diamonds of ice."
                ),
                (DungeonTheme.FrozenDepths, RoomType.Chamber) => (
                    "Frozen Tomb",
                    "Figures are preserved in the ice walls - adventurers, soldiers, creatures - their faces frozen in expressions of terror or determination. A gallery of the dead.",
                    "One of them blinks. You're almost certain one of them blinks."
                ),
                (DungeonTheme.FrozenDepths, RoomType.Hall) => (
                    "Glacier's Heart",
                    "A vast frozen hall stretches before you, its ceiling lost in swirling snow that never stops falling. Ice sculptures of impossible beauty line the walls - or are they frozen victims?",
                    "Something ancient sleeps beneath the ice. You can hear it breathing."
                ),
                (DungeonTheme.FrozenDepths, RoomType.Shrine) => (
                    "Altar of Winter",
                    "A shrine carved from a single massive sapphire-ice, dedicated to powers that predate the sun. The cold radiating from it would kill an unprotected mortal in minutes.",
                    "Winter is not a season here. Winter is a god, and this is its throne."
                ),
                (DungeonTheme.FrozenDepths, RoomType.Crypt) => (
                    "The Preserved",
                    "Niches carved into the ice hold perfectly preserved bodies - a king in his crown, a bride in her gown, a child clutching a toy. Centuries pass, but here nothing changes.",
                    "Death comes slowly in the cold. They had time to think, at the end."
                ),
                (DungeonTheme.FrozenDepths, RoomType.Alcove) => (
                    "Frozen Spring",
                    "Water once flowed here - you can see it frozen mid-cascade, an eternal waterfall caught in a single moment. Fish are suspended in the ice, forever swimming nowhere.",
                    "Time itself seems frozen. Perhaps it is."
                ),

                // ═══════════════════════════════════════════════════════════════
                // VOLCANIC PIT - Primordial heat, destruction, rebirth
                // ═══════════════════════════════════════════════════════════════
                (DungeonTheme.VolcanicPit, RoomType.Corridor) => (
                    "Lava Tube",
                    "This tunnel was carved by flowing magma long ago, its walls smooth as glass. Cracks in the floor glow cherry-red, and the heat makes breathing painful.",
                    "The mountain is alive. You can feel its heartbeat through the stone."
                ),
                (DungeonTheme.VolcanicPit, RoomType.Chamber) => (
                    "Magma Chamber",
                    "A river of molten rock flows through the center of this vast chamber, casting everything in hellish orange light. Stone islands float impossibly on the liquid fire.",
                    "The air shimmers with heat. One wrong step means an end more painful than any torture."
                ),
                (DungeonTheme.VolcanicPit, RoomType.Hall) => (
                    "Forge of the Mountain",
                    "Ancient smiths once worked here, using the mountain's own fire. Their tools still hang on the walls, too hot to touch. Half-finished weapons glow in the magma light.",
                    "The things made here were not meant for mortal hands."
                ),
                (DungeonTheme.VolcanicPit, RoomType.Shrine) => (
                    "Pyre Altar",
                    "A shrine to fire itself, where sacrifices were cast into the flames to ensure the mountain's favor. The ashes of offerings past coat everything.",
                    "The fire demands feeding. It always demands feeding."
                ),
                (DungeonTheme.VolcanicPit, RoomType.Crypt) => (
                    "Cremation Hall",
                    "The dead were brought here to be returned to the fire that birthed all things. Urns of volcanic glass line the walls, each containing the ashes of a life.",
                    "From fire we came. To fire we return."
                ),
                (DungeonTheme.VolcanicPit, RoomType.Alcove) => (
                    "Obsidian Shrine",
                    "A small grotto of natural obsidian, its surfaces reflecting the distant magma glow. Someone has carved prayers into the glass - prayers for survival, for mercy, for death.",
                    "The glass remembers. It was liquid once. It felt the mountain's rage."
                ),

                // ═══════════════════════════════════════════════════════════════
                // ABYSSAL VOID - Madness, unreality, cosmic horror
                // ═══════════════════════════════════════════════════════════════
                (DungeonTheme.AbyssalVoid, RoomType.Corridor) => (
                    "Void Passage",
                    "This corridor exists in defiance of geometry. The walls meet at angles that hurt to look at. The floor and ceiling sometimes switch places when you're not watching.",
                    "Gravity is more of a suggestion here. Your sanity is actively opposed."
                ),
                (DungeonTheme.AbyssalVoid, RoomType.Chamber) => (
                    "Heart of Madness",
                    "Reality has given up in this place. Up is down. Inside is outside. You can see yourself from angles that should be impossible. You wave, and you wave back.",
                    "You're not sure you're still you. You're not sure 'you' means anything here."
                ),
                (DungeonTheme.AbyssalVoid, RoomType.Hall) => (
                    "Gallery of the Forgotten",
                    "Portraits line the walls - but the subjects are concepts, not people. You see paintings of Fear, of Loss, of That Feeling When You Can't Remember A Word.",
                    "The paintings watch you. The paintings ARE watching."
                ),
                (DungeonTheme.AbyssalVoid, RoomType.Shrine) => (
                    "Altar to the Forgotten Eighth",
                    "This shrine predates the Seven. It honors an eighth Old God - one that Manwe erased from memory itself. The altar holds no statue, only an outline where existence should be.",
                    "Some waves were never meant to separate from the Ocean. Some chose to forget themselves entirely."
                ),
                (DungeonTheme.AbyssalVoid, RoomType.Crypt) => (
                    "Tomb of Meaning",
                    "The dead here are not bodies but ideas - the crypt holds the corpses of Purpose, of Hope, of the belief that things might be okay. They decompose slowly into chaos.",
                    "When enough meaning dies, even reality begins to rot."
                ),
                (DungeonTheme.AbyssalVoid, RoomType.Alcove) => (
                    "Pocket of Calm",
                    "Impossibly, this small space is normal. The walls are just walls. The floor stays down. It feels wrong now - the normalcy is the aberration.",
                    "You could stay here forever. You could become the new occupant of this tiny sanity."
                ),

                // ═══════════════════════════════════════════════════════════════
                // SPECIAL ROOM TYPES (theme-agnostic with thematic variations)
                // ═══════════════════════════════════════════════════════════════

                (_, RoomType.SecretVault) => GetSecretVaultFlavor(theme),
                (_, RoomType.LoreLibrary) => GetLoreLibraryFlavor(theme),
                (_, RoomType.MeditationChamber) => GetMeditationChamberFlavor(theme),
                (_, RoomType.BossAntechamber) => GetBossAntechamberFlavor(theme),
                (_, RoomType.ArenaRoom) => GetArenaRoomFlavor(theme),
                (_, RoomType.MerchantDen) => (
                    "Wanderer's Haven",
                    "A hidden alcove where a merchant has set up shop, seemingly unbothered by the dangers.",
                    "Exotic goods glitter in the lamplight. How does he survive down here?"
                ),
                (_, RoomType.MemoryFragment) => GetMemoryFragmentFlavor(theme),

                // Legacy room types - redirect to standard flavors
                (_, RoomType.PuzzleRoom) => GetMysteriousChamberFlavor(theme),
                (_, RoomType.RiddleGate) => GetGuardedPassageFlavor(theme),
                (_, RoomType.TrapGauntlet) => GetDangerousCorridorFlavor(theme),

                // Default fallback
                _ => (
                    $"{type} ({theme})",
                    $"A {type.ToString().ToLower()} in the {theme.ToString().ToLower()}.",
                    "The darkness presses in around you."
                )
            };
        }

        #region Special Room Flavor Methods

        /// <summary>
        /// Mysterious chamber flavor - atmospheric without implying puzzle mechanics
        /// </summary>
        private static (string name, string desc, string atmosphere) GetMysteriousChamberFlavor(DungeonTheme theme)
        {
            return theme switch
            {
                DungeonTheme.Catacombs => (
                    "Chamber of Bones",
                    "Skeletal arms protrude from the walls in strange formations.",
                    "A cryptic inscription on the wall reads: 'The dead remember.'"
                ),
                DungeonTheme.AncientRuins => (
                    "Hall of Glyphs",
                    "Faded symbols cover the walls, remnants of ancient magic long dormant.",
                    "The air feels charged with residual energy."
                ),
                DungeonTheme.Caverns => (
                    "Crystal Chamber",
                    "Massive crystals of different colors jut from the walls and floor.",
                    "They emit a soft hum that resonates in your chest."
                ),
                DungeonTheme.DemonLair => (
                    "Chamber of Sacrifice",
                    "Dark altars line the walls, stained with the offerings of ages past.",
                    "The demons who once worshipped here are long gone."
                ),
                _ => (
                    "Strange Chamber",
                    "An unusual room with ancient markings and unfamiliar architecture.",
                    "Something important happened here once."
                )
            };
        }

        /// <summary>
        /// Guarded passage flavor - atmospheric without riddle mechanics
        /// </summary>
        private static (string name, string desc, string atmosphere) GetGuardedPassageFlavor(DungeonTheme theme)
        {
            return theme switch
            {
                DungeonTheme.Catacombs => (
                    "Guardian's Gate",
                    "Carved stone guardians flank the passage, their eyes seeming to follow you.",
                    "Whatever they once protected, they have long since failed."
                ),
                DungeonTheme.AncientRuins => (
                    "Sphinx's Threshold",
                    "A weathered stone face is carved into the archway, its features worn smooth by time.",
                    "Ancient wisdom once dwelt here. Now only silence remains."
                ),
                DungeonTheme.AbyssalVoid => (
                    "The Watching Dark",
                    "The darkness here seems to have weight and presence.",
                    "You feel observed, but nothing challenges your passage."
                ),
                _ => (
                    "Ancient Gateway",
                    "An ornate archway marks the entrance to this passage.",
                    "Whatever wards once protected it have long since faded."
                )
            };
        }

        /// <summary>
        /// Dangerous corridor flavor - atmospheric without trap gauntlet mechanics
        /// </summary>
        private static (string name, string desc, string atmosphere) GetDangerousCorridorFlavor(DungeonTheme theme)
        {
            return theme switch
            {
                DungeonTheme.AncientRuins => (
                    "Corridor of Trials",
                    "Pressure plates and wall slots hint at defenses that may have once been deadly.",
                    "The mechanisms have rusted. The traps are long disabled."
                ),
                DungeonTheme.DemonLair => (
                    "Passage of Suffering",
                    "Dark stains on the walls suggest this corridor has seen much violence.",
                    "The echoes of past screams seem to linger."
                ),
                _ => (
                    "Treacherous Passage",
                    "This corridor shows signs of past dangers - broken mechanisms and scattered bones.",
                    "Whatever threats existed here have been overcome by time."
                )
            };
        }

        private static (string name, string desc, string atmosphere) GetSecretVaultFlavor(DungeonTheme theme)
        {
            return theme switch
            {
                DungeonTheme.Catacombs => (
                    "Hidden Ossuary",
                    "Behind a false wall lies a chamber where the faithful stored treasures meant for the afterlife. Ancient offerings to the Old Gods rest untouched - gold coins bearing faces of forgotten kings, chalices that once held sacramental wine.",
                    "The dead believed these treasures would follow them back to the Ocean. They were wrong about the gold, but perhaps right about the journey."
                ),
                DungeonTheme.Sewers => (
                    "Smuggler's Sanctuary",
                    "A hidden vault beneath the city's refuse, where criminals stored their ill-gotten gains. Someone carved prayers to Manwe on the walls - even thieves seek redemption.",
                    "The stench cannot reach this room. Neither, it seems, can justice."
                ),
                DungeonTheme.Caverns => (
                    "Crystal Cache",
                    "A natural geode formation conceals a chamber of breathtaking beauty. Ancient crystals pulse with the same light that illuminated the world before the first forgetting.",
                    "These crystals remember what the world has forgotten. Some say they are solidified drops of the Ocean itself."
                ),
                DungeonTheme.AncientRuins => (
                    "Vault of the Seven",
                    "This treasury was sealed before the gods fell. Seven pedestals hold artifacts of immense power - one for each Old God, each drop of Manwe's essence that chose to forget itself.",
                    "A Seal may rest here. Or perhaps just the memory of one."
                ),
                DungeonTheme.DemonLair => (
                    "Forbidden Hoard",
                    "A demon prince's personal collection - cursed artifacts, stolen souls trapped in jars, and things taken from the faithful. The corruption here is thick enough to taste.",
                    "Every treasure here was someone's hope. Every gold coin paid for someone's despair."
                ),
                DungeonTheme.FrozenDepths => (
                    "The Preserved Treasury",
                    "Frozen in eternal ice, treasures from a civilization that worshipped Winter as an Old God. They believed the cold would preserve them until the Ocean reclaimed all things.",
                    "They are still waiting. The ice does not melt. The Ocean has not come for them yet."
                ),
                DungeonTheme.VolcanicPit => (
                    "Forge-Master's Vault",
                    "The mountain's fire guards this chamber where master smiths stored their greatest works. Weapons forged in magma, blessed by prayers to the fire that burns at creation's heart.",
                    "The fire that made these treasures is the same fire that will unmake the world. Creation and destruction are the same wave."
                ),
                DungeonTheme.AbyssalVoid => (
                    "Repository of Lost Things",
                    "This vault contains treasures that no longer exist - ideas abandoned, possibilities foreclosed, paths untaken. They are here because the Void remembers everything reality forgot.",
                    "Take what you need. These things were never truly yours, but then again, nothing is."
                ),
                _ => (
                    "Secret Vault",
                    "A hidden chamber filled with treasures that someone went to great lengths to conceal. The seals bear symbols of the Old Gods.",
                    "What was worth hiding from the world? What secrets do these walls keep?"
                )
            };
        }

        private static (string name, string desc, string atmosphere) GetLoreLibraryFlavor(DungeonTheme theme)
        {
            return theme switch
            {
                DungeonTheme.Catacombs => (
                    "Chronicle of the Dead",
                    "Shelves of burial records and death rites line this ossuary annex. Among the lists of the deceased, you find fragments of older texts - prayers written before men knew what they were praying to.",
                    "Every name recorded here was once a wave in the Ocean. Every death, a return to the source."
                ),
                DungeonTheme.Sewers => (
                    "The Forgotten Archive",
                    "Records deemed too dangerous for public libraries were dumped here and forgotten. Heretical texts about the Ocean's true nature, banned histories of the Old Gods' fall.",
                    "Truth flows downward like water. The things the world discards, the sewers remember."
                ),
                DungeonTheme.Caverns => (
                    "Memory of Stone",
                    "Cave paintings older than language cover these walls - images of an endless Ocean, of waves becoming flesh, of the moment the first soul forgot it was water.",
                    "The first artists knew the truth. They painted it where the stone would remember forever."
                ),
                DungeonTheme.AncientRuins => (
                    "Archive of the First Age",
                    "Crystalline tablets preserve memories from before the Seven Seals were forged, before Manwe's sorrow split the Ocean into separate beings. Each tablet pulses with knowledge that could shatter a mind unprepared for truth.",
                    "The scholars who wrote these knew they were waves. They chose to forget anyway, so their knowledge could be separate. Could be... theirs."
                ),
                DungeonTheme.DemonLair => (
                    "Tome-Vault of Suffering",
                    "Demons collect knowledge like mortals collect gold. These books are bound in skin, written in blood, and contain truths about the corruption that seeps between the cracks of reality.",
                    "The demons learned something from the Void. Something about what happens when waves refuse to return to the Ocean."
                ),
                DungeonTheme.FrozenDepths => (
                    "The Preserved Records",
                    "Frozen scrolls and ice-encased tablets contain the wisdom of a civilization that understood the cycle of death and return. They wrote about the 'Great Thaw' - when all ice would melt back into the Ocean.",
                    "They believed cold was a form of remembering. When things freeze, they stop changing. Stop forgetting."
                ),
                DungeonTheme.VolcanicPit => (
                    "Scriptorium of Ash",
                    "Records inscribed on stone tablets that survived the mountain's fury. Fire-worshippers who knew the mountain's heart was connected to something older - the first flame of creation itself.",
                    "The volcano is a wound in the world. Through it, one can glimpse what lies beneath all reality."
                ),
                DungeonTheme.AbyssalVoid => (
                    "Library of Unwritten Truths",
                    "Books float in the void, their pages filled with words that haven't been thought yet, histories of events that never happened, futures that were abandoned. One tome catches your eye: 'What the Wave Forgot.'",
                    "In the Void, all possibilities exist. Even the ones the Ocean chose not to dream."
                ),
                _ => (
                    "Fragment Repository",
                    "Ancient texts and carved stones preserve knowledge from a forgotten age. Prayers to the Old Gods, meditations on the Ocean's nature, maps of places that no longer exist.",
                    "Here lie pieces of truth that the world has tried to forget. But forgetting is what made the world."
                )
            };
        }

        private static (string name, string desc, string atmosphere) GetMeditationChamberFlavor(DungeonTheme theme)
        {
            return theme switch
            {
                DungeonTheme.Catacombs => (
                    "Chamber of Peaceful Rest",
                    "Among all this death, someone created a place for reflection. Cushions still hold the impressions of those who sat here, contemplating mortality. A single candle burns eternally, its flame the pale blue of souls departing.",
                    "Death is not an ending, the walls seem to whisper. It is a return. Close your eyes and feel the Ocean waiting."
                ),
                DungeonTheme.Sewers => (
                    "The Hidden Sanctuary",
                    "Someone transformed this forgotten corner into a place of peace. Clean water trickles from a crack in the wall, and moss grows in patterns that seem almost deliberate - almost like waves.",
                    "Even in filth, purity exists. Even forgotten, the Ocean reaches out."
                ),
                DungeonTheme.Caverns => (
                    "Pool of Stillness",
                    "An underground spring feeds a perfectly calm pool, its surface a mirror of absolute stillness. The silence here is profound - the cave itself seems to hold its breath.",
                    "The pool remembers being the Ocean. If you look deeply enough, you might remember too. Sit. Breathe. Let the water show you what you've forgotten."
                ),
                DungeonTheme.AncientRuins => (
                    "Sanctuary of the Old Ways",
                    "A meditation circle surrounded by faded murals depicting the Old Gods in harmony - before they forgot they were one, before the separation that created suffering. Seven cushions remain, one for each god.",
                    "The gods themselves would rest here, remembering for just a moment that they were all Manwe's dreams. You can feel their peace still lingering."
                ),
                DungeonTheme.DemonLair => (
                    "Stolen Serenity",
                    "An impossible sanctuary in this realm of torment - a small bubble where demonic influence cannot reach. Someone powerful carved this peace from the surrounding corruption with their dying breath.",
                    "Even here, the Ocean's grace penetrates. The demons cannot corrupt what refuses to be separate."
                ),
                DungeonTheme.FrozenDepths => (
                    "Sanctuary of Eternal Ice",
                    "A chamber of perfect crystalline ice, so cold that even thoughts move slowly. Those who meditate here can glimpse eternity - the unchanging truth beneath all change.",
                    "In perfect stillness, the illusion of separation fades. You are not frozen. You are simply... still. As still as the Ocean before it dreamed of waves."
                ),
                DungeonTheme.VolcanicPit => (
                    "Heart-Fire Sanctuary",
                    "A chamber at the volcano's core where the heat becomes something else - not burning but purifying. The stone here is warm as living flesh, and the magma's glow soothes rather than threatens.",
                    "The fire that destroys also transforms. Close your eyes and let it burn away what you only think you are."
                ),
                DungeonTheme.AbyssalVoid => (
                    "Eye of the Storm",
                    "A bubble of absolute calm exists here, surrounded by the chaos of unreality. In this one place, you can think clearly. You can remember who you were before you forgot.",
                    "In the center of madness, you find surprising clarity. Perhaps because madness is just another wave - and at the center of every wave is stillness."
                ),
                _ => (
                    "Meditation Chamber",
                    "A peaceful alcove where weary souls can find rest and insight. The air is still, the light is soft, and the silence invites contemplation.",
                    "The walls seem to absorb your troubles. Rest here. Dream of the Ocean that dreams of you."
                )
            };
        }

        private static (string name, string desc, string atmosphere) GetBossAntechamberFlavor(DungeonTheme theme)
        {
            return theme switch
            {
                DungeonTheme.Catacombs => (
                    "Threshold of the Bone King",
                    "Skulls of a thousand warriors line the walls, their empty gazes fixed on the door ahead. Each was a hero once. Each thought they were different. Each became part of the collection.",
                    "A Seal may lie beyond this door - or just another fragment of forgotten power. The dead do not warn, they only witness."
                ),
                DungeonTheme.Sewers => (
                    "The Abomination's Threshold",
                    "The tunnels converge here, and with them, all the city's refuse - physical and spiritual. Something has grown fat on centuries of corruption, and it waits in the chamber beyond.",
                    "When waves forget they are water, they can become... anything. Even this."
                ),
                DungeonTheme.Caverns => (
                    "Heart of the Mountain",
                    "The cave narrows to a final passage, the stone walls pulsing with a rhythm like a heartbeat. Something ancient has claimed this place - something that was here before the mountain rose.",
                    "The earth remembers what walked before man. Before gods. Before the first forgetting."
                ),
                DungeonTheme.AncientRuins => (
                    "The Sealed Threshold",
                    "Seven locks once barred this door - one for each Old God. Now the locks lie broken, the seals shattered. Whatever was imprisoned here has had centuries to grow in power.",
                    "Some prisons are not meant to hold forever. Some prisoners were meant to escape, when the time was right."
                ),
                DungeonTheme.DemonLair => (
                    "Gates of Torment",
                    "The screaming reaches a crescendo here. Souls embedded in the walls reach out with translucent hands, trying to pull you back from the horror beyond. They failed. They all failed.",
                    "The demon lords are waves that refused to return - that twisted their separation into eternal hunger. What you face is a corruption of what was meant to be divine."
                ),
                DungeonTheme.FrozenDepths => (
                    "The Final Freeze",
                    "Ice so ancient it predates the world forms the walls here. Beyond the frozen portal, something stirs - something that has slept since the Ocean first dreamed of cold.",
                    "Some waves froze rather than return. They became monuments to refusal. Now one awakens."
                ),
                DungeonTheme.VolcanicPit => (
                    "Threshold of Primordial Fire",
                    "The heat here would kill lesser beings instantly. The door ahead is made of solidified magma, and beyond it, the mountain's true heart beats with ancient fury.",
                    "Fire was the first separation - heat from cold, light from dark. What dwells within remembers being part of the first flame."
                ),
                DungeonTheme.AbyssalVoid => (
                    "Edge of Understanding",
                    "Reality thins to nothing here. Through the door, you sense... yourself? Or perhaps what you will become. Or what you have always been. The Void does not distinguish between past and future.",
                    "You came seeking a Seal. You came seeking power. But power is just another form of separation, and separation is what created the Void in the first place."
                ),
                _ => (
                    "Boss Antechamber",
                    "The air grows heavy with power that radiates from beyond the door ahead. Ancient wards flicker and fail - whatever protections existed here have been overcome by what lies within.",
                    "Prepare yourself. There is no turning back. But then, there never was."
                )
            };
        }

        private static (string name, string desc, string atmosphere) GetArenaRoomFlavor(DungeonTheme theme)
        {
            return theme switch
            {
                DungeonTheme.DemonLair => (
                    "Blood Arena",
                    "A circular fighting pit with tiered stone seats surrounding it.",
                    "Dark stains cover the floor. Many battles have been fought here."
                ),
                DungeonTheme.Caverns => (
                    "Natural Amphitheater",
                    "The cave opens into a circular chamber. Bones litter the floor.",
                    "This natural formation has seen much violence over the years."
                ),
                _ => (
                    "Combat Arena",
                    "A circular chamber with high walls, clearly designed for battle.",
                    "The echoes of past conflicts still linger here."
                )
            };
        }

        private static (string name, string desc, string atmosphere) GetMemoryFragmentFlavor(DungeonTheme theme)
        {
            return theme switch
            {
                DungeonTheme.Catacombs => (
                    "Tomb of Your Past Life",
                    "A sarcophagus bears your name - but carved centuries ago. The face etched in stone is yours, yet the style predates living memory. Inside lies... nothing. The body is gone. Because you are wearing it.",
                    "You have died before. Many times. Each death was a return to the Ocean, each birth a new forgetting. But this time, something is different. This time, you're starting to remember."
                ),
                DungeonTheme.Sewers => (
                    "Echo of Disgrace",
                    "Graffiti on the wall tells a story you somehow know - a hero's fall from grace, a choice that led to exile, a name scratched out in shame. Your hand trembles. You know that name.",
                    "Not all past lives were noble. Not all waves crest high before they fall. But even disgraced waves return to the Ocean."
                ),
                DungeonTheme.Caverns => (
                    "The Dreaming Pool",
                    "A pool of perfectly still water shows not your reflection but a memory - you, in another body, another time, standing at the edge of a vast Ocean. You were about to step in. You were about to go home.",
                    "Why did you choose to forget? Why do any of us? Perhaps because forgetting is the only way to experience returning."
                ),
                DungeonTheme.AncientRuins => (
                    "Chamber of Remembrance",
                    "Faded murals depict your face - in different bodies, different eras, but unmistakably you. A warrior. A scholar. A ruler. A beggar. The same soul, wearing different masks throughout history.",
                    "The amnesia cracks. Something wants to be remembered. Someone wants to come home. And that someone... is you."
                ),
                DungeonTheme.DemonLair => (
                    "Mirror of Past Sins",
                    "A demon has captured one of your past selves - their soul imprisoned in a mirror, screaming silently. You remember now. You made a terrible choice in that life. The demons have been feeding on that guilt ever since.",
                    "Even your sins are waves returning to the Ocean. Even your darkness is part of the light."
                ),
                DungeonTheme.FrozenDepths => (
                    "The Preserved Memory",
                    "Frozen in the ice is a scene from your past - a moment of perfect happiness, crystallized forever. You remember now. You chose to freeze this moment rather than let it end. But everything ends. Everything returns.",
                    "You cannot hold onto waves. You cannot freeze the Ocean. But you can remember that you are both."
                ),
                DungeonTheme.VolcanicPit => (
                    "Forge of Rebirth",
                    "In the magma's glow, you see yourself being unmade and remade - dying in fire, being reborn from it. This is where your soul was forged, life after life. The mountain remembers every version of you.",
                    "Death is not destruction. It is transformation. And you have transformed so many times."
                ),
                DungeonTheme.AbyssalVoid => (
                    "Echo of Self",
                    "A mirror that doesn't show your reflection but something else - every version of you that ever existed, overlapping, merging, separating. You are not one wave. You are all the waves you've ever been.",
                    "In the Void, past and future collapse. You see yourself before the first forgetting, and after the final return. You have been here before. You have been everywhere before. You are the Ocean, dreaming."
                ),
                _ => (
                    "Memory Fragment",
                    "This place triggers something deep in your mind. You've seen this before. You've been this before. The walls of forgetting thin, and something ancient stirs within you.",
                    "Close your eyes. Let it come back to you. Remember what you chose to forget. Remember who you really are."
                )
            };
        }

        #endregion

        private static List<RoomFeature> GenerateRoomFeatures(DungeonTheme theme, RoomType type)
        {
            var features = new List<RoomFeature>();
            int featureCount = random.Next(1, 4);

            var possibleFeatures = GetThemeFeatures(theme);

            for (int i = 0; i < featureCount && possibleFeatures.Count > 0; i++)
            {
                var idx = random.Next(possibleFeatures.Count);
                features.Add(possibleFeatures[idx]);
                possibleFeatures.RemoveAt(idx);
            }

            return features;
        }

        private static List<RoomFeature> GetThemeFeatures(DungeonTheme theme)
        {
            return theme switch
            {
                DungeonTheme.Catacombs => new List<RoomFeature>
                {
                    new("pile of bones", "Ancient bones, picked clean long ago.", FeatureInteraction.Examine),
                    new("stone coffin", "A heavy stone lid covers this sarcophagus.", FeatureInteraction.Open),
                    new("crumbling wall", "This section of wall looks weak.", FeatureInteraction.Break),
                    new("faded inscription", "Words carved into stone, mostly illegible.", FeatureInteraction.Read),
                    new("rusted gate", "A corroded iron gate blocks a passage.", FeatureInteraction.Open),
                    new("burial urn", "A clay urn that might contain valuables. Or ashes.", FeatureInteraction.Open)
                },
                DungeonTheme.Sewers => new List<RoomFeature>
                {
                    new("drainage grate", "Something glints beneath the grate.", FeatureInteraction.Open),
                    new("suspicious pile", "A mound of refuse. Something might be hidden in it.", FeatureInteraction.Search),
                    new("rusted valve", "An old valve. Turning it might do something.", FeatureInteraction.Use),
                    new("dead body", "A recent victim. They might have supplies.", FeatureInteraction.Search),
                    new("crack in the wall", "Wide enough to squeeze through?", FeatureInteraction.Enter)
                },
                DungeonTheme.Caverns => new List<RoomFeature>
                {
                    new("crystal cluster", "Beautiful crystals. Might be valuable.", FeatureInteraction.Take),
                    new("underground pool", "Dark water. Something ripples beneath.", FeatureInteraction.Examine),
                    new("narrow crevice", "A tight squeeze, but passable.", FeatureInteraction.Enter),
                    new("glowing mushrooms", "Bioluminescent fungi. Edible?", FeatureInteraction.Take),
                    new("rock formation", "An unusual shape. Natural? Or carved?", FeatureInteraction.Examine)
                },
                DungeonTheme.AncientRuins => new List<RoomFeature>
                {
                    new("ancient chest", "A ornate chest, surprisingly intact.", FeatureInteraction.Open),
                    new("magical runes", "Glowing symbols pulse with power.", FeatureInteraction.Read),
                    new("broken statue", "Only the base remains. Something was here.", FeatureInteraction.Examine),
                    new("hidden alcove", "A concealed space behind a tapestry.", FeatureInteraction.Search),
                    new("mechanism", "Gears and levers. An ancient device.", FeatureInteraction.Use)
                },
                DungeonTheme.DemonLair => new List<RoomFeature>
                {
                    new("blood pool", "Fresh blood. Still warm.", FeatureInteraction.Examine),
                    new("torture device", "A horrific contraption. Something is strapped to it.", FeatureInteraction.Examine),
                    new("demonic altar", "An altar radiating evil. Offerings sit upon it.", FeatureInteraction.Take),
                    new("cage", "Someone is locked inside, barely alive.", FeatureInteraction.Open),
                    new("portal fragment", "A tear in reality. Looking into it hurts.", FeatureInteraction.Examine)
                },
                _ => new List<RoomFeature>
                {
                    new("old chest", "A weathered chest.", FeatureInteraction.Open),
                    new("strange markings", "Symbols you don't recognize.", FeatureInteraction.Read),
                    new("pile of debris", "Might be hiding something.", FeatureInteraction.Search)
                }
            };
        }

        private static void ConnectRooms(DungeonFloor floor)
        {
            // Create a connected graph of rooms
            // First, ensure all rooms are reachable (minimum spanning tree)
            var connected = new HashSet<string> { floor.Rooms[0].Id };
            var unconnected = new HashSet<string>(floor.Rooms.Skip(1).Select(r => r.Id));

            while (unconnected.Count > 0)
            {
                // Pick a random connected room and connect it to a random unconnected room
                var fromRoom = floor.Rooms.First(r => connected.Contains(r.Id) && r.Exits.Count < 4);
                var toRoomId = unconnected.First();
                var toRoom = floor.Rooms.First(r => r.Id == toRoomId);

                // Determine exit directions
                var availableDirs = GetAvailableDirections(fromRoom);
                if (availableDirs.Count > 0)
                {
                    var dir = availableDirs[random.Next(availableDirs.Count)];
                    var oppositeDir = GetOppositeDirection(dir);

                    fromRoom.Exits[dir] = new RoomExit(toRoomId, GetExitDescription(dir, floor.Theme));
                    toRoom.Exits[oppositeDir] = new RoomExit(fromRoom.Id, GetExitDescription(oppositeDir, floor.Theme));

                    connected.Add(toRoomId);
                    unconnected.Remove(toRoomId);
                }
            }

            // Add some extra connections for variety (loops)
            int extraConnections = floor.Rooms.Count / 3;
            for (int i = 0; i < extraConnections; i++)
            {
                var room1 = floor.Rooms[random.Next(floor.Rooms.Count)];
                var room2 = floor.Rooms[random.Next(floor.Rooms.Count)];

                if (room1.Id != room2.Id && room1.Exits.Count < 4 && room2.Exits.Count < 4)
                {
                    var availableDirs1 = GetAvailableDirections(room1);
                    var availableDirs2 = GetAvailableDirections(room2);

                    if (availableDirs1.Count > 0 && availableDirs2.Count > 0)
                    {
                        var dir = availableDirs1[random.Next(availableDirs1.Count)];
                        var oppositeDir = GetOppositeDirection(dir);

                        if (availableDirs2.Contains(oppositeDir))
                        {
                            room1.Exits[dir] = new RoomExit(room2.Id, GetExitDescription(dir, floor.Theme));
                            room2.Exits[oppositeDir] = new RoomExit(room1.Id, GetExitDescription(oppositeDir, floor.Theme));
                        }
                    }
                }
            }
        }

        private static List<Direction> GetAvailableDirections(DungeonRoom room)
        {
            var all = new List<Direction> { Direction.North, Direction.South, Direction.East, Direction.West };
            return all.Where(d => !room.Exits.ContainsKey(d)).ToList();
        }

        private static Direction GetOppositeDirection(Direction dir)
        {
            return dir switch
            {
                Direction.North => Direction.South,
                Direction.South => Direction.North,
                Direction.East => Direction.West,
                Direction.West => Direction.East,
                _ => Direction.North
            };
        }

        private static string GetExitDescription(Direction dir, DungeonTheme theme)
        {
            var dirName = dir.ToString().ToLower();
            return theme switch
            {
                DungeonTheme.Catacombs => $"A dark passage leads {dirName}.",
                DungeonTheme.Sewers => $"A tunnel continues {dirName}.",
                DungeonTheme.Caverns => $"The cave extends {dirName}.",
                DungeonTheme.AncientRuins => $"An archway opens to the {dirName}.",
                DungeonTheme.DemonLair => $"A blood-red portal flickers to the {dirName}.",
                DungeonTheme.FrozenDepths => $"An icy corridor stretches {dirName}.",
                DungeonTheme.VolcanicPit => $"A heat-warped passage leads {dirName}.",
                DungeonTheme.AbyssalVoid => $"Reality bends {dirName}ward.",
                _ => $"An exit leads {dirName}."
            };
        }

        private static void PlaceSpecialRooms(DungeonFloor floor)
        {
            // Mark the last room as boss room
            var bossRoom = floor.Rooms.Last();
            bossRoom.IsBossRoom = true;
            bossRoom.Name = GetBossRoomName(floor.Theme);
            bossRoom.Description = GetBossRoomDescription(floor.Theme);
            bossRoom.HasMonsters = true;
            floor.BossRoomId = bossRoom.Id;

            // Add a single treasure room per floor - always guarded by monsters
            var treasureRoom = floor.Rooms[floor.Rooms.Count / 2];
            if (!treasureRoom.IsBossRoom)
            {
                treasureRoom.Name = "Guarded Treasury";
                treasureRoom.Description = "A chamber filled with glittering treasure, watched over by fearsome guardians who will fight to the death to protect their hoard.";
                treasureRoom.HasTreasure = true;
                treasureRoom.HasTrap = true;
                treasureRoom.HasMonsters = true; // Treasure is ALWAYS guarded
                treasureRoom.MonsterCount = 2 + random.Next(2); // 2-3 guards
                treasureRoom.TreasureQuality = TreasureQuality.Rare; // Better quality for the single treasure room
                floor.TreasureRoomId = treasureRoom.Id;
            }

            // Add stairs down (placed in middle-ish area, not too easy to find)
            int stairsIndex = random.Next(floor.Rooms.Count / 3, (floor.Rooms.Count * 2) / 3);
            var stairsRoom = floor.Rooms[stairsIndex];
            if (stairsRoom.IsBossRoom || stairsRoom == treasureRoom)
            {
                stairsRoom = floor.Rooms.FirstOrDefault(r => !r.IsBossRoom && r != treasureRoom);
            }
            if (stairsRoom != null)
            {
                stairsRoom.HasStairsDown = true;
                floor.StairsDownRoomId = stairsRoom.Id;
            }

            // Create secret rooms with hidden exits (10% of connections)
            CreateSecretConnections(floor);

            // Set up special room properties based on type
            ConfigureSpecialRoomTypes(floor);

            // Place lore fragments in lore libraries
            PlaceLoreFragments(floor);

            // Potentially place a secret boss on certain floors
            PlaceSecretBoss(floor);
        }

        private static void CreateSecretConnections(DungeonFloor floor)
        {
            // Find all SecretVault rooms and make their entrances hidden
            foreach (var room in floor.Rooms.Where(r => r.Type == RoomType.SecretVault))
            {
                room.IsSecretRoom = true;

                // Find any exits leading TO this room and mark them hidden
                foreach (var otherRoom in floor.Rooms)
                {
                    foreach (var exit in otherRoom.Exits.Values)
                    {
                        if (exit.TargetRoomId == room.Id)
                        {
                            exit.IsHidden = true;
                            exit.IsRevealed = false;
                            exit.Description = "A faint draft suggests a hidden passage...";
                        }
                    }
                }
            }

            // Additionally, 10% of random connections become hidden passages
            int hiddenCount = Math.Max(1, floor.Rooms.Count / 10);
            var eligibleRooms = floor.Rooms
                .Where(r => !r.IsBossRoom && !r.IsSecretRoom && r.Exits.Count > 1)
                .ToList();

            for (int i = 0; i < hiddenCount && eligibleRooms.Count > 0; i++)
            {
                var room = eligibleRooms[random.Next(eligibleRooms.Count)];
                var exitDir = room.Exits.Keys.ToList()[random.Next(room.Exits.Count)];
                var exit = room.Exits[exitDir];

                if (!exit.IsHidden)
                {
                    exit.IsHidden = true;
                    exit.IsRevealed = false;
                    exit.Description = GetHiddenExitDescription(floor.Theme, exitDir);
                }
            }
        }

        private static string GetHiddenExitDescription(DungeonTheme theme, Direction dir)
        {
            return theme switch
            {
                DungeonTheme.Catacombs => "A loose stone conceals a narrow passage.",
                DungeonTheme.Sewers => "Behind the flowing water, a gap in the wall...",
                DungeonTheme.Caverns => "A crevice, barely visible in the crystal light.",
                DungeonTheme.AncientRuins => "A concealed door, marked only by faded runes.",
                DungeonTheme.DemonLair => "A portal of shadow, visible only to those who look with fear.",
                _ => "A hidden passage reveals itself to careful eyes."
            };
        }

        private static void ConfigureSpecialRoomTypes(DungeonFloor floor)
        {
            foreach (var room in floor.Rooms)
            {
                // CRITICAL: Never modify boss room properties - they are set explicitly in PlaceSpecialRooms
                if (room.IsBossRoom)
                    continue;

                switch (room.Type)
                {
                    case RoomType.PuzzleRoom:
                        room.HasEvent = true;
                        room.EventType = DungeonEventType.Puzzle;
                        room.HasMonsters = false; // Puzzles first, then maybe combat
                        room.RequiresPuzzle = true;
                        room.PuzzleDifficulty = 1 + (floor.Level / 20);
                        break;

                    case RoomType.RiddleGate:
                        room.HasEvent = true;
                        room.EventType = DungeonEventType.Riddle;
                        room.HasMonsters = false;
                        room.RequiresRiddle = true;
                        room.RiddleDifficulty = 1 + (floor.Level / 25);
                        break;

                    case RoomType.LoreLibrary:
                        room.HasEvent = true;
                        room.EventType = DungeonEventType.LoreDiscovery;
                        room.HasMonsters = random.NextDouble() < 0.3; // Guardian?
                        room.ContainsLore = true;
                        break;

                    case RoomType.MeditationChamber:
                        room.HasEvent = true;
                        room.EventType = DungeonEventType.RestSpot;
                        room.HasMonsters = false;
                        room.IsSafeRoom = true;
                        room.GrantsInsight = floor.Level >= 30;
                        break;

                    case RoomType.TrapGauntlet:
                        room.HasTrap = true;
                        room.TrapCount = 2 + random.Next(3);
                        room.HasMonsters = false;
                        break;

                    case RoomType.ArenaRoom:
                        room.HasMonsters = true;
                        room.MonsterCount = 2 + random.Next(3);
                        room.IsArena = true;
                        room.HasTreasure = true; // Reward for surviving
                        break;

                    case RoomType.MerchantDen:
                        room.HasEvent = true;
                        room.EventType = DungeonEventType.Merchant;
                        room.HasMonsters = false;
                        room.IsSafeRoom = true;
                        break;

                    case RoomType.MemoryFragment:
                        room.HasEvent = true;
                        room.EventType = DungeonEventType.MemoryFlash;
                        room.HasMonsters = false;
                        room.TriggersMemory = true;
                        room.MemoryFragmentLevel = floor.Level / 15; // Which fragment
                        break;

                    case RoomType.BossAntechamber:
                        room.HasMonsters = random.NextDouble() < 0.5; // Elite guards?
                        room.HasTrap = random.NextDouble() < 0.3;
                        room.RequiresPuzzle = floor.Level >= 50; // Deeper floors need puzzle
                        break;

                    case RoomType.SecretVault:
                        room.HasTreasure = true;
                        room.TreasureQuality = TreasureQuality.Legendary;
                        room.HasTrap = true;
                        room.HasMonsters = true; // Legendary treasure ALWAYS guarded
                        room.MonsterCount = 2 + random.Next(3); // 2-4 elite guardians
                        break;
                }
            }
        }

        private static void PlaceLoreFragments(DungeonFloor floor)
        {
            var loreRooms = floor.Rooms.Where(r => r.Type == RoomType.LoreLibrary).ToList();

            foreach (var room in loreRooms)
            {
                // Determine which lore fragment based on floor level
                room.LoreFragmentType = floor.Level switch
                {
                    <= 20 => LoreFragmentType.OceanOrigin,
                    <= 35 => LoreFragmentType.FirstSeparation,
                    <= 50 => LoreFragmentType.TheForgetting,
                    <= 65 => LoreFragmentType.ManwesChoice,
                    <= 80 => LoreFragmentType.TheCorruption,
                    <= 95 => LoreFragmentType.TheCycle,
                    _ => LoreFragmentType.TheTruth
                };
            }
        }

        private static void PlaceSecretBoss(DungeonFloor floor)
        {
            // Secret bosses on specific floors
            int[] secretBossFloors = { 25, 50, 75, 99 };

            if (secretBossFloors.Contains(floor.Level))
            {
                // Find a SecretVault or create a hidden area for the secret boss
                var bossRoom = floor.Rooms.FirstOrDefault(r => r.Type == RoomType.SecretVault);

                if (bossRoom != null)
                {
                    bossRoom.HasSecretBoss = true;
                    bossRoom.SecretBossType = floor.Level switch
                    {
                        25 => SecretBossType.TheFirstWave,
                        50 => SecretBossType.TheForgottenEighth,
                        75 => SecretBossType.EchoOfSelf,
                        99 => SecretBossType.TheOceanSpeaks,
                        _ => SecretBossType.TheFirstWave
                    };
                    bossRoom.EventType = DungeonEventType.SecretBoss;
                    floor.HasSecretBoss = true;
                    floor.SecretBossRoomId = bossRoom.Id;
                }
            }
        }

        private static string GetBossRoomName(DungeonTheme theme)
        {
            return theme switch
            {
                DungeonTheme.Catacombs => "The Bone Throne",
                DungeonTheme.Sewers => "The Abomination's Nest",
                DungeonTheme.Caverns => "Crystal Heart",
                DungeonTheme.AncientRuins => "The Sealed Sanctum",
                DungeonTheme.DemonLair => "Throne of Suffering",
                DungeonTheme.FrozenDepths => "The Frozen Core",
                DungeonTheme.VolcanicPit => "Magma Lord's Chamber",
                DungeonTheme.AbyssalVoid => "The End of All Things",
                _ => "Boss Chamber"
            };
        }

        private static string GetBossRoomDescription(DungeonTheme theme)
        {
            return theme switch
            {
                DungeonTheme.Catacombs => "A massive chamber dominated by a throne made entirely of bones. Something ancient stirs.",
                DungeonTheme.Sewers => "The stench is overwhelming. Something massive has made this place its home.",
                DungeonTheme.Caverns => "A giant crystal dominates the room, pulsing with malevolent energy.",
                DungeonTheme.AncientRuins => "The final chamber, sealed for millennia. You have awoken what sleeps within.",
                DungeonTheme.DemonLair => "Chains rattle. The floor is a carpet of tortured souls. The demon lord awaits.",
                DungeonTheme.FrozenDepths => "Ice so cold it burns. A massive figure frozen in the wall begins to crack free.",
                DungeonTheme.VolcanicPit => "A river of magma encircles an obsidian platform. A creature of fire rises.",
                DungeonTheme.AbyssalVoid => "This is it. The heart of madness. Reality itself screams.",
                _ => "A powerful presence fills this room."
            };
        }

        private static void PopulateEvents(DungeonFloor floor)
        {
            foreach (var room in floor.Rooms)
            {
                if (room.HasEvent && !room.IsBossRoom)
                {
                    room.EventType = GetRandomEventType(floor.Theme);
                }
            }
        }

        private static DungeonEventType GetRandomEventType(DungeonTheme theme)
        {
            var events = new[]
            {
                DungeonEventType.TreasureChest,
                DungeonEventType.Merchant,
                DungeonEventType.Shrine,
                DungeonEventType.Trap,
                DungeonEventType.NPCEncounter,
                DungeonEventType.Puzzle,
                DungeonEventType.RestSpot,
                DungeonEventType.MysteryEvent
            };

            return events[random.Next(events.Length)];
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // DATA STRUCTURES
    // ═══════════════════════════════════════════════════════════════════════════

    public class DungeonFloor
    {
        public int Level { get; set; }
        public DungeonTheme Theme { get; set; }
        public int DangerLevel { get; set; }
        public List<DungeonRoom> Rooms { get; set; } = new();
        public string EntranceRoomId { get; set; } = "";
        public string CurrentRoomId { get; set; } = "";
        public string BossRoomId { get; set; } = "";
        public string TreasureRoomId { get; set; } = "";
        public string StairsDownRoomId { get; set; } = "";
        public bool BossDefeated { get; set; } = false;
        public int MonstersKilled { get; set; } = 0;
        public int TreasuresFound { get; set; } = 0;
        public DateTime EnteredAt { get; set; } = DateTime.Now;

        // New properties for expanded dungeons
        public bool HasSecretBoss { get; set; } = false;
        public string SecretBossRoomId { get; set; } = "";
        public bool SecretBossDefeated { get; set; } = false;
        public int PuzzlesSolved { get; set; } = 0;
        public int RiddlesAnswered { get; set; } = 0;
        public int SecretsFound { get; set; } = 0;
        public int LoreFragmentsCollected { get; set; } = 0;
        public List<string> RevealedSecretRooms { get; set; } = new();

        // Seven Seals story integration
        public bool HasUncollectedSeal { get; set; } = false;
        public UsurperRemake.Systems.SealType? SealType { get; set; }
        public bool SealCollected { get; set; } = false;
        public string SealRoomId { get; set; } = "";

        public DungeonRoom GetCurrentRoom() => Rooms.FirstOrDefault(r => r.Id == CurrentRoomId);
        public DungeonRoom GetRoom(string id) => Rooms.FirstOrDefault(r => r.Id == id);

        /// <summary>
        /// Get all visible exits from current room (respects hidden status)
        /// </summary>
        public Dictionary<Direction, RoomExit> GetVisibleExits()
        {
            var room = GetCurrentRoom();
            if (room == null) return new Dictionary<Direction, RoomExit>();

            return room.Exits
                .Where(e => !e.Value.IsHidden || e.Value.IsRevealed)
                .ToDictionary(e => e.Key, e => e.Value);
        }

        /// <summary>
        /// Reveal a hidden exit
        /// </summary>
        public bool RevealHiddenExit(string roomId, Direction direction)
        {
            var room = GetRoom(roomId);
            if (room == null || !room.Exits.TryGetValue(direction, out var exit))
                return false;

            if (exit.IsHidden && !exit.IsRevealed)
            {
                exit.IsRevealed = true;
                SecretsFound++;
                return true;
            }
            return false;
        }
    }

    public class DungeonRoom
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string AtmosphereText { get; set; } = "";
        public RoomType Type { get; set; }
        public DungeonTheme Theme { get; set; }
        public Dictionary<Direction, RoomExit> Exits { get; set; } = new();
        public List<RoomFeature> Features { get; set; } = new();
        public bool IsExplored { get; set; } = false;
        public bool IsCleared { get; set; } = false;
        public bool HasMonsters { get; set; } = false;
        public bool HasTreasure { get; set; } = false;
        public bool HasEvent { get; set; } = false;
        public bool HasTrap { get; set; } = false;
        public bool HasStairsDown { get; set; } = false;
        public bool IsBossRoom { get; set; } = false;
        public int DangerRating { get; set; } = 1;
        public DungeonEventType EventType { get; set; }
        public List<Monster> Monsters { get; set; } = new();
        public bool TrapTriggered { get; set; } = false;
        public bool TreasureLooted { get; set; } = false;
        public bool EventCompleted { get; set; } = false;

        // ═══════════════════════════════════════════════════════════════
        // New properties for expanded dungeon system
        // ═══════════════════════════════════════════════════════════════

        // Secret room properties
        public bool IsSecretRoom { get; set; } = false;

        // Puzzle room properties
        public bool RequiresPuzzle { get; set; } = false;
        public int PuzzleDifficulty { get; set; } = 1;
        public bool PuzzleSolved { get; set; } = false;
        public PuzzleType? AssignedPuzzle { get; set; }

        // Riddle room properties
        public bool RequiresRiddle { get; set; } = false;
        public int RiddleDifficulty { get; set; } = 1;
        public bool RiddleAnswered { get; set; } = false;
        public int? AssignedRiddleId { get; set; }

        // Lore room properties
        public bool ContainsLore { get; set; } = false;
        public LoreFragmentType? LoreFragmentType { get; set; }
        public bool LoreCollected { get; set; } = false;

        // Safe room / meditation properties
        public bool IsSafeRoom { get; set; } = false;
        public bool GrantsInsight { get; set; } = false;
        public bool InsightGranted { get; set; } = false;

        // Arena properties
        public bool IsArena { get; set; } = false;
        public int MonsterCount { get; set; } = 1;

        // Trap gauntlet properties
        public int TrapCount { get; set; } = 1;
        public int TrapsDisarmed { get; set; } = 0;

        // Memory fragment properties
        public bool TriggersMemory { get; set; } = false;
        public int MemoryFragmentLevel { get; set; } = 0;
        public bool MemoryTriggered { get; set; } = false;

        // Treasure quality
        public TreasureQuality TreasureQuality { get; set; } = TreasureQuality.Normal;

        // Secret boss properties
        public bool HasSecretBoss { get; set; } = false;
        public SecretBossType? SecretBossType { get; set; }
        public bool SecretBossDefeated { get; set; } = false;

        /// <summary>
        /// Check if room is blocked by an unsolved puzzle or riddle
        /// </summary>
        public bool IsBlocked => (RequiresPuzzle && !PuzzleSolved) || (RequiresRiddle && !RiddleAnswered);

        /// <summary>
        /// Check if room has any unresolved content
        /// </summary>
        public bool HasUnresolvedContent =>
            (HasMonsters && !IsCleared) ||
            (HasTreasure && !TreasureLooted) ||
            (HasEvent && !EventCompleted) ||
            (HasTrap && !TrapTriggered) ||
            (RequiresPuzzle && !PuzzleSolved) ||
            (RequiresRiddle && !RiddleAnswered) ||
            (ContainsLore && !LoreCollected) ||
            (TriggersMemory && !MemoryTriggered) ||
            (HasSecretBoss && !SecretBossDefeated);
    }

    public class RoomExit
    {
        public string TargetRoomId { get; set; }
        public string Description { get; set; }
        public bool IsLocked { get; set; } = false;
        public bool IsHidden { get; set; } = false;
        public bool IsRevealed { get; set; } = true;

        public RoomExit(string targetId, string desc)
        {
            TargetRoomId = targetId;
            Description = desc;
        }
    }

    public class RoomFeature
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public FeatureInteraction Interaction { get; set; }
        public bool IsInteracted { get; set; } = false;

        public RoomFeature(string name, string desc, FeatureInteraction interaction)
        {
            Name = name;
            Description = desc;
            Interaction = interaction;
        }
    }

    public enum Direction { North, South, East, West }
    public enum RoomType
    {
        Corridor, Chamber, Hall, Alcove, Shrine, Crypt,
        // New room types for expanded dungeons
        PuzzleRoom,         // Logic/environmental puzzle required
        RiddleGate,         // Guardian asks riddle to pass
        SecretVault,        // Hidden room with rare treasure
        LoreLibrary,        // Wave/Ocean philosophy fragments
        BossAntechamber,    // Pre-boss puzzle challenge
        MeditationChamber,  // Rest + Ocean insights
        TrapGauntlet,       // Multiple traps in sequence
        ArenaRoom,          // Combat challenge room
        MerchantDen,        // Hidden merchant location
        MemoryFragment      // Amnesia system reveals
    }
    public enum DungeonTheme { Catacombs, Sewers, Caverns, AncientRuins, DemonLair, FrozenDepths, VolcanicPit, AbyssalVoid }
    public enum FeatureInteraction { Examine, Open, Search, Read, Take, Use, Break, Enter }
    public enum DungeonEventType { None, TreasureChest, Merchant, Shrine, Trap, NPCEncounter, Puzzle, RestSpot, MysteryEvent, Riddle, LoreDiscovery, MemoryFlash, SecretBoss }

    /// <summary>
    /// Types of puzzles that can appear in dungeon rooms
    /// </summary>
    public enum PuzzleType
    {
        LeverSequence,      // Pull levers in correct order
        SymbolAlignment,    // Rotate/align symbols
        PressurePlates,     // Step on plates in order or with weight
        LightDarkness,      // Manipulate light sources
        NumberGrid,         // Solve number puzzle
        MemoryMatch,        // Remember and repeat pattern
        ItemCombination,    // Combine items to solve
        EnvironmentChange,  // Change room state (water, fire, etc.)
        CoordinationPuzzle, // Requires companion help
        ReflectionPuzzle    // Use mirrors/reflections
    }

    /// <summary>
    /// Lore fragment types that reveal Ocean Philosophy
    /// </summary>
    public enum LoreFragmentType
    {
        OceanOrigin,        // The vast Ocean before creation
        FirstSeparation,    // The Ocean dreams of waves
        TheForgetting,      // Waves must forget to feel separate
        ManwesChoice,       // The first wave's deep forgetting
        TheSevenDrops,      // The Old Gods as fragments
        TheCorruption,      // Separation becomes pain
        TheCycle,           // Why Manwe sends fragments
        TheReturn,          // Death is returning home
        TheTruth            // "You ARE the ocean"
    }

    /// <summary>
    /// Treasure quality tiers
    /// </summary>
    public enum TreasureQuality
    {
        Poor,       // Common items
        Normal,     // Standard loot
        Good,       // Above average
        Rare,       // Uncommon finds
        Epic,       // Very rare
        Legendary   // Best possible
    }

    /// <summary>
    /// Secret boss types hidden in dungeons
    /// </summary>
    public enum SecretBossType
    {
        TheFirstWave,       // Floor 25: The first being to separate from Ocean
        TheForgottenEighth, // Floor 50: A god Manwe erased from memory
        EchoOfSelf,         // Floor 75: Fight your past life
        TheOceanSpeaks      // Floor 99: The Ocean itself manifests
    }

    /// <summary>
    /// Persistent dungeon floor state - tracks exploration progress and respawn timing
    /// Regular floors respawn after 24 hours, boss/seal floors stay cleared permanently
    /// </summary>
    public class DungeonFloorState
    {
        public int FloorLevel { get; set; }
        public DateTime LastClearedAt { get; set; } = DateTime.MinValue;
        public DateTime LastVisitedAt { get; set; } = DateTime.MinValue;
        public bool EverCleared { get; set; } = false;        // For first-clear bonus eligibility
        public bool IsPermanentlyClear { get; set; } = false; // Boss/seal floors
        public bool BossDefeated { get; set; } = false;       // True if boss room boss was actually defeated
        public string CurrentRoomId { get; set; } = "";

        // Room-level state
        public Dictionary<string, DungeonRoomState> RoomStates { get; set; } = new();

        /// <summary>
        /// Hours before regular floors respawn (monsters return, but treasure stays looted)
        /// </summary>
        public const int RESPAWN_HOURS = 6;

        /// <summary>
        /// Check if this floor should respawn (monsters return)
        /// Boss/seal floors never respawn once cleared
        /// </summary>
        public bool ShouldRespawn()
        {
            if (IsPermanentlyClear) return false;
            if (LastClearedAt == DateTime.MinValue) return false;

            var hoursSinceCleared = (DateTime.Now - LastClearedAt).TotalHours;
            return hoursSinceCleared >= RESPAWN_HOURS;
        }

        /// <summary>
        /// Get time remaining until respawn (for display)
        /// </summary>
        public TimeSpan TimeUntilRespawn()
        {
            if (IsPermanentlyClear || LastClearedAt == DateTime.MinValue)
                return TimeSpan.Zero;

            var respawnAt = LastClearedAt.AddHours(RESPAWN_HOURS);
            var remaining = respawnAt - DateTime.Now;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Persistent state for a single dungeon room
    /// </summary>
    public class DungeonRoomState
    {
        public string RoomId { get; set; } = "";
        public bool IsExplored { get; set; }
        public bool IsCleared { get; set; }           // Monsters defeated (can respawn)
        public bool TreasureLooted { get; set; }      // Permanent - doesn't respawn
        public bool TrapTriggered { get; set; }       // Permanent - doesn't respawn
        public bool EventCompleted { get; set; }      // Permanent - doesn't respawn
        public bool PuzzleSolved { get; set; }        // Permanent
        public bool RiddleAnswered { get; set; }      // Permanent
        public bool LoreCollected { get; set; }       // Permanent
        public bool InsightGranted { get; set; }      // Permanent
        public bool MemoryTriggered { get; set; }     // Permanent
        public bool SecretBossDefeated { get; set; }  // Permanent
    }
}
