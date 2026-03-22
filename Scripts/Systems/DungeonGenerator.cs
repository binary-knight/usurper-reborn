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
        private static string GetThemeKey(DungeonTheme theme) => theme switch
        {
            DungeonTheme.Catacombs => "cat",
            DungeonTheme.Sewers => "sew",
            DungeonTheme.Caverns => "cav",
            DungeonTheme.AncientRuins => "ruin",
            DungeonTheme.DemonLair => "demon",
            DungeonTheme.FrozenDepths => "ice",
            DungeonTheme.VolcanicPit => "fire",
            DungeonTheme.AbyssalVoid => "void",
            _ => "default"
        };

        private static Random random = Random.Shared;

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
                else if (i == 1 && DungeonSettlementData.IsSettlementFloor(floor.Level))
                {
                    // Settlement floors get a guaranteed settlement room near the entrance
                    roomType = RoomType.Settlement;
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
            var tk = GetThemeKey(theme);
            var roomKey = type switch
            {
                RoomType.Corridor => "corridor",
                RoomType.Chamber => "chamber",
                RoomType.Hall => "hall",
                RoomType.Shrine => "shrine",
                RoomType.Crypt => "crypt",
                RoomType.Alcove => "alcove",
                RoomType.SecretVault => null,
                RoomType.LoreLibrary => null,
                RoomType.MeditationChamber => null,
                RoomType.BossAntechamber => null,
                RoomType.ArenaRoom => null,
                RoomType.MerchantDen => "merchant_den",
                RoomType.MemoryFragment => null,
                RoomType.PuzzleRoom => null,
                RoomType.RiddleGate => null,
                RoomType.TrapGauntlet => null,
                _ => null
            };

            // Special room types delegate to their own methods
            if (roomKey == null)
            {
                return type switch
                {
                    RoomType.SecretVault => GetSecretVaultFlavor(theme),
                    RoomType.LoreLibrary => GetLoreLibraryFlavor(theme),
                    RoomType.MeditationChamber => GetMeditationChamberFlavor(theme),
                    RoomType.BossAntechamber => GetBossAntechamberFlavor(theme),
                    RoomType.ArenaRoom => GetArenaRoomFlavor(theme),
                    RoomType.MemoryFragment => GetMemoryFragmentFlavor(theme),
                    RoomType.PuzzleRoom => GetMysteriousChamberFlavor(theme),
                    RoomType.RiddleGate => GetGuardedPassageFlavor(theme),
                    RoomType.TrapGauntlet => GetDangerousCorridorFlavor(theme),
                    _ => (
                        $"{type} ({theme})",
                        Loc.Get("dg.default.d", type.ToString().ToLower(), theme.ToString().ToLower()),
                        Loc.Get("dg.default.a")
                    )
                };
            }

            // MerchantDen is theme-agnostic
            if (roomKey == "merchant_den")
            {
                return (
                    Loc.Get("dg.merchant.n"),
                    Loc.Get("dg.merchant.d"),
                    Loc.Get("dg.merchant.a")
                );
            }

            // Standard room types: look up by theme + room type
            var key = $"dg.{tk}.{roomKey}";
            var name = Loc.Get($"{key}.n");
            var desc = Loc.Get($"{key}.d");
            var atmosphere = Loc.Get($"{key}.a");

            // If the key wasn't found (returns the key itself), fall back to default
            if (name == $"{key}.n")
            {
                return (
                    $"{type} ({theme})",
                    Loc.Get("dg.default.d", type.ToString().ToLower(), theme.ToString().ToLower()),
                    Loc.Get("dg.default.a")
                );
            }

            return (name, desc, atmosphere);
        }

        #region Special Room Flavor Methods

        /// <summary>
        /// Mysterious chamber flavor - atmospheric without implying puzzle mechanics
        /// </summary>
        private static (string name, string desc, string atmosphere) GetMysteriousChamberFlavor(DungeonTheme theme)
        {
            var tk = theme switch
            {
                DungeonTheme.Catacombs => "cat",
                DungeonTheme.AncientRuins => "ruin",
                DungeonTheme.Caverns => "cav",
                DungeonTheme.DemonLair => "demon",
                _ => "default"
            };
            return (Loc.Get($"dg.mystery.{tk}.n"), Loc.Get($"dg.mystery.{tk}.d"), Loc.Get($"dg.mystery.{tk}.a"));
        }

        /// <summary>
        /// Guarded passage flavor - atmospheric without riddle mechanics
        /// </summary>
        private static (string name, string desc, string atmosphere) GetGuardedPassageFlavor(DungeonTheme theme)
        {
            var tk = theme switch
            {
                DungeonTheme.Catacombs => "cat",
                DungeonTheme.AncientRuins => "ruin",
                DungeonTheme.AbyssalVoid => "void",
                _ => "default"
            };
            return (Loc.Get($"dg.guard.{tk}.n"), Loc.Get($"dg.guard.{tk}.d"), Loc.Get($"dg.guard.{tk}.a"));
        }

        /// <summary>
        /// Dangerous corridor flavor - atmospheric without trap gauntlet mechanics
        /// </summary>
        private static (string name, string desc, string atmosphere) GetDangerousCorridorFlavor(DungeonTheme theme)
        {
            var tk = theme switch
            {
                DungeonTheme.AncientRuins => "ruin",
                DungeonTheme.DemonLair => "demon",
                _ => "default"
            };
            return (Loc.Get($"dg.danger.{tk}.n"), Loc.Get($"dg.danger.{tk}.d"), Loc.Get($"dg.danger.{tk}.a"));
        }

        private static (string name, string desc, string atmosphere) GetSecretVaultFlavor(DungeonTheme theme)
        {
            var tk = GetThemeKey(theme);
            return (Loc.Get($"dg.vault.{tk}.n"), Loc.Get($"dg.vault.{tk}.d"), Loc.Get($"dg.vault.{tk}.a"));
        }

        private static (string name, string desc, string atmosphere) GetLoreLibraryFlavor(DungeonTheme theme)
        {
            var tk = GetThemeKey(theme);
            return (Loc.Get($"dg.lore.{tk}.n"), Loc.Get($"dg.lore.{tk}.d"), Loc.Get($"dg.lore.{tk}.a"));
        }

        private static (string name, string desc, string atmosphere) GetMeditationChamberFlavor(DungeonTheme theme)
        {
            var tk = GetThemeKey(theme);
            return (Loc.Get($"dg.meditate.{tk}.n"), Loc.Get($"dg.meditate.{tk}.d"), Loc.Get($"dg.meditate.{tk}.a"));
        }

        private static (string name, string desc, string atmosphere) GetBossAntechamberFlavor(DungeonTheme theme)
        {
            var tk = GetThemeKey(theme);
            return (Loc.Get($"dg.ante.{tk}.n"), Loc.Get($"dg.ante.{tk}.d"), Loc.Get($"dg.ante.{tk}.a"));
        }

        private static (string name, string desc, string atmosphere) GetArenaRoomFlavor(DungeonTheme theme)
        {
            var tk = theme switch
            {
                DungeonTheme.DemonLair => "demon",
                DungeonTheme.Caverns => "cav",
                _ => "default"
            };
            return (Loc.Get($"dg.arena.{tk}.n"), Loc.Get($"dg.arena.{tk}.d"), Loc.Get($"dg.arena.{tk}.a"));
        }

        private static (string name, string desc, string atmosphere) GetMemoryFragmentFlavor(DungeonTheme theme)
        {
            var tk = GetThemeKey(theme);
            return (Loc.Get($"dg.memory.{tk}.n"), Loc.Get($"dg.memory.{tk}.d"), Loc.Get($"dg.memory.{tk}.a"));
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
            var tk = GetThemeKey(theme);
            // For themes without specific features, use "default"
            if (tk != "cat" && tk != "sew" && tk != "cav" && tk != "ruin" && tk != "demon")
                tk = "default";

            return tk switch
            {
                "cat" => new List<RoomFeature>
                {
                    new(Loc.Get("dg.feat.cat.1.n"), Loc.Get("dg.feat.cat.1.d"), FeatureInteraction.Examine),
                    new(Loc.Get("dg.feat.cat.2.n"), Loc.Get("dg.feat.cat.2.d"), FeatureInteraction.Open),
                    new(Loc.Get("dg.feat.cat.3.n"), Loc.Get("dg.feat.cat.3.d"), FeatureInteraction.Break),
                    new(Loc.Get("dg.feat.cat.4.n"), Loc.Get("dg.feat.cat.4.d"), FeatureInteraction.Read),
                    new(Loc.Get("dg.feat.cat.5.n"), Loc.Get("dg.feat.cat.5.d"), FeatureInteraction.Open),
                    new(Loc.Get("dg.feat.cat.6.n"), Loc.Get("dg.feat.cat.6.d"), FeatureInteraction.Open)
                },
                "sew" => new List<RoomFeature>
                {
                    new(Loc.Get("dg.feat.sew.1.n"), Loc.Get("dg.feat.sew.1.d"), FeatureInteraction.Open),
                    new(Loc.Get("dg.feat.sew.2.n"), Loc.Get("dg.feat.sew.2.d"), FeatureInteraction.Search),
                    new(Loc.Get("dg.feat.sew.3.n"), Loc.Get("dg.feat.sew.3.d"), FeatureInteraction.Use),
                    new(Loc.Get("dg.feat.sew.4.n"), Loc.Get("dg.feat.sew.4.d"), FeatureInteraction.Search),
                    new(Loc.Get("dg.feat.sew.5.n"), Loc.Get("dg.feat.sew.5.d"), FeatureInteraction.Enter)
                },
                "cav" => new List<RoomFeature>
                {
                    new(Loc.Get("dg.feat.cav.1.n"), Loc.Get("dg.feat.cav.1.d"), FeatureInteraction.Take),
                    new(Loc.Get("dg.feat.cav.2.n"), Loc.Get("dg.feat.cav.2.d"), FeatureInteraction.Examine),
                    new(Loc.Get("dg.feat.cav.3.n"), Loc.Get("dg.feat.cav.3.d"), FeatureInteraction.Enter),
                    new(Loc.Get("dg.feat.cav.4.n"), Loc.Get("dg.feat.cav.4.d"), FeatureInteraction.Take),
                    new(Loc.Get("dg.feat.cav.5.n"), Loc.Get("dg.feat.cav.5.d"), FeatureInteraction.Examine)
                },
                "ruin" => new List<RoomFeature>
                {
                    new(Loc.Get("dg.feat.ruin.1.n"), Loc.Get("dg.feat.ruin.1.d"), FeatureInteraction.Open),
                    new(Loc.Get("dg.feat.ruin.2.n"), Loc.Get("dg.feat.ruin.2.d"), FeatureInteraction.Read),
                    new(Loc.Get("dg.feat.ruin.3.n"), Loc.Get("dg.feat.ruin.3.d"), FeatureInteraction.Examine),
                    new(Loc.Get("dg.feat.ruin.4.n"), Loc.Get("dg.feat.ruin.4.d"), FeatureInteraction.Search),
                    new(Loc.Get("dg.feat.ruin.5.n"), Loc.Get("dg.feat.ruin.5.d"), FeatureInteraction.Use)
                },
                "demon" => new List<RoomFeature>
                {
                    new(Loc.Get("dg.feat.demon.1.n"), Loc.Get("dg.feat.demon.1.d"), FeatureInteraction.Examine),
                    new(Loc.Get("dg.feat.demon.2.n"), Loc.Get("dg.feat.demon.2.d"), FeatureInteraction.Examine),
                    new(Loc.Get("dg.feat.demon.3.n"), Loc.Get("dg.feat.demon.3.d"), FeatureInteraction.Take),
                    new(Loc.Get("dg.feat.demon.4.n"), Loc.Get("dg.feat.demon.4.d"), FeatureInteraction.Open),
                    new(Loc.Get("dg.feat.demon.5.n"), Loc.Get("dg.feat.demon.5.d"), FeatureInteraction.Examine)
                },
                _ => new List<RoomFeature>
                {
                    new(Loc.Get("dg.feat.default.1.n"), Loc.Get("dg.feat.default.1.d"), FeatureInteraction.Open),
                    new(Loc.Get("dg.feat.default.2.n"), Loc.Get("dg.feat.default.2.d"), FeatureInteraction.Read),
                    new(Loc.Get("dg.feat.default.3.n"), Loc.Get("dg.feat.default.3.d"), FeatureInteraction.Search)
                }
            };
        }

        private static void ConnectRooms(DungeonFloor floor)
        {
            // Place rooms on a grid so directions are spatially consistent.
            // Going North then South always returns you to the same room.
            var gridSize = (int)Math.Ceiling(Math.Sqrt(floor.Rooms.Count)) + 2;
            var grid = new string?[gridSize, gridSize];
            var positions = new Dictionary<string, (int row, int col)>();

            // Place entrance room near center
            int startRow = gridSize / 2;
            int startCol = gridSize / 2;
            grid[startRow, startCol] = floor.Rooms[0].Id;
            positions[floor.Rooms[0].Id] = (startRow, startCol);

            var connected = new HashSet<string> { floor.Rooms[0].Id };
            var unconnected = new List<string>(floor.Rooms.Skip(1).Select(r => r.Id));

            // MST phase: connect each room to the graph via spatially valid directions
            while (unconnected.Count > 0)
            {
                // Shuffle connected rooms for variety instead of always picking first
                var connectedList = floor.Rooms
                    .Where(r => connected.Contains(r.Id) && r.Exits.Count < 4)
                    .ToList();
                if (connectedList.Count == 0) break;

                bool placed = false;
                // Try connected rooms in random order
                for (int attempt = 0; attempt < connectedList.Count * 2 && !placed; attempt++)
                {
                    var fromRoom = connectedList[random.Next(connectedList.Count)];
                    var (fromRow, fromCol) = positions[fromRoom.Id];

                    var availableDirs = GetAvailableDirections(fromRoom);
                    // Shuffle directions
                    for (int i = availableDirs.Count - 1; i > 0; i--)
                    {
                        int j = random.Next(i + 1);
                        (availableDirs[i], availableDirs[j]) = (availableDirs[j], availableDirs[i]);
                    }

                    foreach (var dir in availableDirs)
                    {
                        var (dr, dc) = DirectionOffset(dir);
                        int newRow = fromRow + dr;
                        int newCol = fromCol + dc;

                        // Check grid bounds and that the cell is empty
                        if (newRow < 0 || newRow >= gridSize || newCol < 0 || newCol >= gridSize)
                            continue;
                        if (grid[newRow, newCol] != null)
                            continue;

                        // Place the next unconnected room here
                        var toRoomId = unconnected[0];
                        var toRoom = floor.Rooms.First(r => r.Id == toRoomId);

                        // Check that toRoom also has the opposite direction available
                        var oppositeDir = GetOppositeDirection(dir);
                        if (toRoom.Exits.ContainsKey(oppositeDir))
                            continue;

                        grid[newRow, newCol] = toRoomId;
                        positions[toRoomId] = (newRow, newCol);

                        fromRoom.Exits[dir] = new RoomExit(toRoomId, GetExitDescription(dir, floor.Theme));
                        toRoom.Exits[oppositeDir] = new RoomExit(fromRoom.Id, GetExitDescription(oppositeDir, floor.Theme));

                        connected.Add(toRoomId);
                        unconnected.RemoveAt(0);
                        placed = true;
                        break;
                    }
                }

                // Safety: if we couldn't place via neighbors, expand grid and force-place
                if (!placed && unconnected.Count > 0)
                {
                    var toRoomId = unconnected[0];
                    var toRoom = floor.Rooms.First(r => r.Id == toRoomId);

                    // Find any connected room with an available direction and empty neighbor cell
                    bool forcePlaced = false;
                    foreach (var fromRoom in connectedList)
                    {
                        var (fromRow, fromCol) = positions[fromRoom.Id];
                        foreach (var dir in GetAvailableDirections(fromRoom))
                        {
                            var (dr, dc) = DirectionOffset(dir);
                            int newRow = fromRow + dr;
                            int newCol = fromCol + dc;
                            if (newRow < 0 || newRow >= gridSize || newCol < 0 || newCol >= gridSize)
                                continue;
                            if (grid[newRow, newCol] != null)
                                continue;
                            var oppositeDir = GetOppositeDirection(dir);
                            if (toRoom.Exits.ContainsKey(oppositeDir))
                                continue;

                            grid[newRow, newCol] = toRoomId;
                            positions[toRoomId] = (newRow, newCol);
                            fromRoom.Exits[dir] = new RoomExit(toRoomId, GetExitDescription(dir, floor.Theme));
                            toRoom.Exits[oppositeDir] = new RoomExit(fromRoom.Id, GetExitDescription(oppositeDir, floor.Theme));
                            connected.Add(toRoomId);
                            unconnected.RemoveAt(0);
                            forcePlaced = true;
                            break;
                        }
                        if (forcePlaced) break;
                    }

                    // Last resort: place without connection (shouldn't happen with adequate grid)
                    if (!forcePlaced)
                    {
                        connected.Add(toRoomId);
                        unconnected.RemoveAt(0);
                    }
                }
            }

            // Add extra connections between adjacent rooms on the grid for variety (loops)
            int extraConnections = floor.Rooms.Count / 3;
            for (int i = 0; i < extraConnections; i++)
            {
                var room1 = floor.Rooms[random.Next(floor.Rooms.Count)];
                if (room1.Exits.Count >= 4 || !positions.ContainsKey(room1.Id)) continue;

                var (r1, c1) = positions[room1.Id];
                var availableDirs1 = GetAvailableDirections(room1);
                if (availableDirs1.Count == 0) continue;

                var dir1 = availableDirs1[random.Next(availableDirs1.Count)];
                var (dr, dc) = DirectionOffset(dir1);
                int r2 = r1 + dr;
                int c2 = c1 + dc;

                if (r2 < 0 || r2 >= gridSize || c2 < 0 || c2 >= gridSize) continue;
                var neighborId = grid[r2, c2];
                if (neighborId == null) continue;

                var room2 = floor.Rooms.First(r => r.Id == neighborId);
                var oppositeDir1 = GetOppositeDirection(dir1);

                if (room2.Exits.Count < 4 && !room2.Exits.ContainsKey(oppositeDir1))
                {
                    room1.Exits[dir1] = new RoomExit(room2.Id, GetExitDescription(dir1, floor.Theme));
                    room2.Exits[oppositeDir1] = new RoomExit(room1.Id, GetExitDescription(oppositeDir1, floor.Theme));
                }
            }
        }

        private static (int dr, int dc) DirectionOffset(Direction dir)
        {
            return dir switch
            {
                Direction.North => (-1, 0),
                Direction.South => (1, 0),
                Direction.East => (0, 1),
                Direction.West => (0, -1),
                _ => (0, 0)
            };
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
            var dirName = Loc.Get($"dg.dir.{dir.ToString().ToLower()}");
            var tk = GetThemeKey(theme);
            return Loc.Get($"dg.exit.{tk}", dirName);
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
                treasureRoom.Name = Loc.Get("dg.treasure.n");
                treasureRoom.Description = Loc.Get("dg.treasure.d");
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

            // Configure settlement rooms on settlement floors
            ConfigureSettlementRooms(floor);

            // Create secret rooms with hidden exits (10% of connections)
            CreateSecretConnections(floor);

            // Set up special room properties based on type
            ConfigureSpecialRoomTypes(floor);

            // Place lore fragments in lore libraries
            PlaceLoreFragments(floor);

            // Potentially place a secret boss on certain floors
            PlaceSecretBoss(floor);
        }

        private static void ConfigureSettlementRooms(DungeonFloor floor)
        {
            var settlement = DungeonSettlementData.GetSettlement(floor.Level);
            if (settlement == null) return;

            foreach (var room in floor.Rooms.Where(r => r.Type == RoomType.Settlement))
            {
                room.Name = settlement.Name;
                room.Description = settlement.Description;
                room.IsSafeRoom = true;
                room.HasMonsters = false;
                room.HasTrap = false;
                room.HasTreasure = false;
                room.HasEvent = true;
                room.EventType = DungeonEventType.Settlement;
                room.DangerRating = 0;
            }
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
                            exit.Description = Loc.Get("dg.hidden.vault");
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
            var tk = theme switch
            {
                DungeonTheme.Catacombs => "cat",
                DungeonTheme.Sewers => "sew",
                DungeonTheme.Caverns => "cav",
                DungeonTheme.AncientRuins => "ruin",
                DungeonTheme.DemonLair => "demon",
                _ => "default"
            };
            return Loc.Get($"dg.hidden.{tk}");
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
                        // Merchant encounter removed — sold items had missing WeaponType
                        // causing spell requirements to fail. Convert to treasure chest.
                        room.HasEvent = true;
                        room.EventType = DungeonEventType.TreasureChest;
                        room.HasMonsters = false;
                        room.HasTreasure = true;
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
                    bossRoom.HasEvent = true;
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
            var tk = GetThemeKey(theme);
            return Loc.Get($"dg.boss.{tk}.n");
        }

        private static string GetBossRoomDescription(DungeonTheme theme)
        {
            var tk = GetThemeKey(theme);
            return Loc.Get($"dg.boss.{tk}.d");
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
        MemoryFragment,     // Amnesia system reveals
        Settlement          // Safe outpost hub at theme boundaries
    }
    public enum DungeonTheme { Catacombs, Sewers, Caverns, AncientRuins, DemonLair, FrozenDepths, VolcanicPit, AbyssalVoid }
    public enum FeatureInteraction { Examine, Open, Search, Read, Take, Use, Break, Enter }
    public enum DungeonEventType { None, TreasureChest, Merchant, Shrine, Trap, NPCEncounter, Puzzle, RestSpot, MysteryEvent, Riddle, LoreDiscovery, MemoryFlash, SecretBoss, Settlement }

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
        public bool EverCleared { get; set; } = false;              // For first-clear bonus eligibility
        public bool IsPermanentlyClear { get; set; } = false;       // Boss/seal floors
        public bool BossDefeated { get; set; } = false;             // True if boss room boss was actually defeated
        public bool CompletionBonusAwarded { get; set; } = false;   // Completion XP/gold bonus already paid out
        public string CurrentRoomId { get; set; } = "";

        // Room-level state
        public Dictionary<string, DungeonRoomState> RoomStates { get; set; } = new();

        /// <summary>
        /// Hours before regular floors respawn (monsters return, but treasure stays looted)
        /// </summary>
        public const int RESPAWN_HOURS = 1;

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
