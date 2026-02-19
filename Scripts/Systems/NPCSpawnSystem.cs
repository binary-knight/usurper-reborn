using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using UsurperRemake.Data;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// NPC Spawn System - Creates and manages classic Usurper NPCs in the game world.
    /// Thread-safe: uses ReaderWriterLockSlim for concurrent MUD mode access where
    /// WorldSim writes (add/remove/clear) and multiple player sessions read.
    /// </summary>
    public class NPCSpawnSystem
    {
        private static NPCSpawnSystem? instance;
        public static NPCSpawnSystem Instance => instance ??= new NPCSpawnSystem();

        private List<NPC> spawnedNPCs = new();
        private Random random = new();
        private bool npcsInitialized = false;

        /// <summary>
        /// Lock for thread-safe access to the NPC list.
        /// WorldSim takes write lock for add/remove/clear.
        /// Player sessions take read lock for iteration/queries.
        /// In single-player/BBS mode this is uncontended (no overhead).
        /// </summary>
        private readonly ReaderWriterLockSlim _npcLock = new(LockRecursionPolicy.SupportsRecursion);

        // Cached snapshot for read access — invalidated when list mutates
        private List<NPC>? _cachedSnapshot;
        private int _snapshotVersion;
        private int _listVersion;

        /// <summary>
        /// Get all active NPCs. In MUD mode, returns a cached snapshot for safe
        /// concurrent iteration (invalidated when the list is mutated).
        /// </summary>
        public List<NPC> ActiveNPCs
        {
            get
            {
                // Fast path: if not in MUD mode, return the list directly (no contention possible)
                if (!UsurperRemake.Server.SessionContext.IsActive &&
                    UsurperRemake.Server.MudServer.Instance == null)
                    return spawnedNPCs;

                _npcLock.EnterReadLock();
                try
                {
                    if (_cachedSnapshot == null || _snapshotVersion != _listVersion)
                    {
                        _cachedSnapshot = new List<NPC>(spawnedNPCs);
                        _snapshotVersion = _listVersion;
                    }
                    return _cachedSnapshot;
                }
                finally
                {
                    _npcLock.ExitReadLock();
                }
            }
        }

        /// <summary>
        /// Initialize all classic Usurper NPCs and spawn them into the world
        /// </summary>
        public async Task InitializeClassicNPCs()
        {
            // Check both the flag AND the actual count to be safe
            // This handles cases where the flag is set but NPCs weren't actually created
            if (npcsInitialized && spawnedNPCs.Count > 0)
            {
                return;
            }

            _npcLock.EnterWriteLock();
            try
            {
                var npcTemplates = ClassicNPCs.GetClassicNPCs();
                spawnedNPCs.Clear();

                foreach (var template in npcTemplates)
                {
                    var npc = CreateNPCFromTemplate(template);
                    spawnedNPCs.Add(npc);
                }

                // Distribute NPCs across locations
                DistributeNPCsAcrossWorld();

                npcsInitialized = true;
                _listVersion++;
            }
            finally
            {
                _npcLock.ExitWriteLock();
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Force re-initialization of NPCs (for loading saves or debugging)
        /// </summary>
        public async Task ForceReinitializeNPCs()
        {
            _npcLock.EnterWriteLock();
            try
            {
                npcsInitialized = false;
                spawnedNPCs.Clear();
                _listVersion++;
            }
            finally
            {
                _npcLock.ExitWriteLock();
            }
            await InitializeClassicNPCs();
        }

        /// <summary>
        /// Create an NPC from a template
        /// </summary>
        private NPC CreateNPCFromTemplate(NPCTemplate template)
        {
            // Map GenderIdentity to CharacterSex
            CharacterSex sex = template.Gender switch
            {
                GenderIdentity.Female => CharacterSex.Female,
                GenderIdentity.TransFemale => CharacterSex.Female,
                GenderIdentity.Male => CharacterSex.Male,
                GenderIdentity.TransMale => CharacterSex.Male,
                GenderIdentity.NonBinary => random.Next(2) == 0 ? CharacterSex.Male : CharacterSex.Female,
                GenderIdentity.Genderfluid => random.Next(2) == 0 ? CharacterSex.Male : CharacterSex.Female,
                _ => random.Next(2) == 0 ? CharacterSex.Male : CharacterSex.Female
            };

            var npc = new NPC
            {
                Name1 = template.Name,
                Name2 = template.Name,
                ID = $"npc_{template.Name.ToLower().Replace(" ", "_")}",  // Generate unique ID from name
                Class = template.Class,
                Race = template.Race,
                Level = template.StartLevel,
                Age = random.Next(18, 50),
                Sex = sex,
                AI = CharacterAI.Computer,
                // Copy story role info - story NPCs have special narrative roles
                StoryRole = template.StoryRole ?? "",
                LoreNote = template.LoreNote ?? ""
            };

            // Generate stats based on level and class
            GenerateNPCStats(npc, template);

            // Set personality and alignment (including romance traits from template)
            SetNPCPersonality(npc, template);

            // Give starting equipment
            GiveStartingEquipment(npc);

            // Set random starting location
            npc.CurrentLocation = GetRandomStartLocation();

            // Assign faction based on class, personality, alignment, and story role
            npc.NPCFaction = DetermineFactionForNPC(template);

            return npc;
        }

        /// <summary>
        /// Determine the faction affiliation for an NPC based on their template
        /// Not all NPCs belong to factions - many are just ordinary adventurers
        /// </summary>
        private Faction? DetermineFactionForNPC(NPCTemplate template)
        {
            // Story NPCs have specific faction ties
            if (!string.IsNullOrEmpty(template.StoryRole))
            {
                switch (template.StoryRole)
                {
                    case "HighPriest":      // Archpriest Aldwyn
                    case "OceanOracle":     // The Wavespeaker
                        return Faction.TheFaith;
                    case "ShadowAgent":     // Whisperwind
                    case "Mordecai":        // Mordecai Voidborne
                        return Faction.TheShadows;
                    case "Lysandra":        // Lysandra Dawnwhisper
                    case "FallenPaladin":   // Sir Darius the Lost
                    case "MaelkethChampion": // Skarn - warriors often align with Crown
                        return Faction.TheCrown;
                    case "TheStranger":     // Noctura in disguise - purposefully unaffiliated
                    case "Sylvana":         // Neutral romance option
                    case "SealScholar":     // Sera the Seeker - independent scholar
                        return null;        // Unaffiliated
                }
            }

            string personality = template.Personality.ToLower();
            string alignment = template.Alignment.ToLower();

            // Clerics are strongly associated with The Faith
            if (template.Class == CharacterClass.Cleric)
            {
                // Only evil clerics wouldn't be Faith
                if (alignment != "evil")
                    return Faction.TheFaith;
            }

            // Assassins are associated with The Shadows
            if (template.Class == CharacterClass.Assassin)
            {
                // Most assassins are Shadows members
                if (random.Next(100) < 80) // 80% chance
                    return Faction.TheShadows;
            }

            // Good-aligned Paladins are either Crown or Faith
            if (template.Class == CharacterClass.Paladin && alignment == "good")
            {
                // Paladins split between Crown (martial) and Faith (religious)
                return random.Next(2) == 0 ? Faction.TheCrown : Faction.TheFaith;
            }

            // Personality-based faction assignment
            switch (personality)
            {
                // Faith personalities
                case "pious":
                case "devout":
                case "compassionate":
                case "gentle":
                case "kind":
                    return Faction.TheFaith;

                // Crown personalities
                case "honorable":
                case "noble":
                case "righteous":
                case "loyal":
                case "resolute":
                case "zealous":
                    if (alignment == "good")
                        return Faction.TheCrown;
                    break;

                // Shadows personalities
                case "cunning":
                case "scheming":
                case "sneaky":
                case "secretive":
                case "greedy":
                case "cold":
                case "professional":
                    return Faction.TheShadows;
            }

            // Evil-aligned characters lean toward Shadows
            if (alignment == "evil" && random.Next(100) < 40)
                return Faction.TheShadows;

            // Good-aligned warriors/barbarians may be Crown
            if (alignment == "good" &&
                (template.Class == CharacterClass.Warrior || template.Class == CharacterClass.Barbarian))
            {
                if (random.Next(100) < 30) // 30% chance
                    return Faction.TheCrown;
            }

            // Most NPCs remain unaffiliated
            return null;
        }

        /// <summary>
        /// Generate stats for NPC based on level and class
        /// </summary>
        private void GenerateNPCStats(NPC npc, NPCTemplate template)
        {
            var level = template.StartLevel;

            // Base stats increase with level
            npc.Strength = 10 + (level * 5) + random.Next(-5, 6);
            npc.Defence = 10 + (level * 3) + random.Next(-3, 4);
            npc.Stamina = 10 + (level * 4) + random.Next(-4, 5);
            npc.Agility = 10 + (level * 3) + random.Next(-3, 4);
            npc.Charisma = 10 + random.Next(-5, 6);
            npc.Dexterity = 10 + (level * 2) + random.Next(-3, 4);
            npc.Wisdom = 10 + (level * 2) + random.Next(-3, 4);
            npc.Intelligence = 10 + (level * 2) + random.Next(-3, 4);

            // Class-specific stat bonuses
            switch (npc.Class)
            {
                case CharacterClass.Warrior:
                case CharacterClass.Barbarian:
                    npc.Strength += level * 2;
                    npc.HP = 100 + (level * 50);
                    break;
                case CharacterClass.Magician:
                    npc.Intelligence += level * 3;
                    npc.Mana = 50 + (level * 30);
                    npc.HP = 50 + (level * 25);
                    break;
                case CharacterClass.Cleric:
                case CharacterClass.Paladin:
                    npc.Wisdom += level * 2;
                    npc.HP = 80 + (level * 40);
                    npc.Mana = 40 + (level * 20);
                    break;
                case CharacterClass.Assassin:
                    npc.Dexterity += level * 3;
                    npc.Agility += level * 2;
                    npc.HP = 70 + (level * 35);
                    break;
                case CharacterClass.Sage:
                    npc.Agility += level * 2;
                    npc.Wisdom += level * 2;
                    npc.HP = 90 + (level * 45);
                    break;
                default:
                    npc.HP = 80 + (level * 40);
                    break;
            }

            npc.MaxHP = npc.HP;
            npc.MaxMana = npc.Mana;
            // Use same XP curve as players: level^2.0 * 50 per level
            npc.Experience = GetExperienceForLevel(level);
            npc.Gold = random.Next(level * 100, level * 500);

            // Add Constitution stat (was missing)
            npc.Constitution = 10 + (level * 2) + random.Next(-3, 4);

            // CRITICAL: Initialize base stats from current values
            // This ensures RecalculateStats() works correctly for NPCs
            npc.BaseStrength = npc.Strength;
            npc.BaseDexterity = npc.Dexterity;
            npc.BaseConstitution = npc.Constitution;
            npc.BaseIntelligence = npc.Intelligence;
            npc.BaseWisdom = npc.Wisdom;
            npc.BaseCharisma = npc.Charisma;
            npc.BaseMaxHP = npc.MaxHP;
            npc.BaseMaxMana = npc.MaxMana;
            npc.BaseDefence = npc.Defence;
            npc.BaseStamina = npc.Stamina;
            npc.BaseAgility = npc.Agility;
        }

        /// <summary>
        /// Set NPC personality based on template (including romance traits)
        /// </summary>
        private void SetNPCPersonality(NPC npc, NPCTemplate template)
        {
            string personality = template.Personality;
            string alignment = template.Alignment;

            // Create personality profile with randomized base values
            // These ensure NPCs have varying personalities for team formation
            var profile = new PersonalityProfile
            {
                Archetype = "commoner", // Default archetype, prevents null reference
                // Initialize core traits with random base values (0.3-0.7 range for variety)
                Aggression = 0.3f + (float)(random.NextDouble() * 0.4),
                Greed = 0.3f + (float)(random.NextDouble() * 0.4),
                Courage = 0.4f + (float)(random.NextDouble() * 0.4),
                Loyalty = 0.4f + (float)(random.NextDouble() * 0.4),
                Vengefulness = 0.2f + (float)(random.NextDouble() * 0.4),
                Impulsiveness = 0.3f + (float)(random.NextDouble() * 0.4),
                Sociability = 0.4f + (float)(random.NextDouble() * 0.4),
                Ambition = 0.3f + (float)(random.NextDouble() * 0.5),
                Trustworthiness = 0.4f + (float)(random.NextDouble() * 0.4),
                Caution = 0.3f + (float)(random.NextDouble() * 0.4),
                Intelligence = 0.3f + (float)(random.NextDouble() * 0.4),
                Mysticism = 0.2f + (float)(random.NextDouble() * 0.3),
                Patience = 0.3f + (float)(random.NextDouble() * 0.4)
            };

            // Modify traits based on personality type - these override the base values
            switch (personality.ToLower())
            {
                case "aggressive":
                case "fierce":
                case "brutal":
                    profile.Aggression = 0.7f + (float)(random.NextDouble() * 0.3); // 0.7-1.0
                    profile.Courage = 0.6f + (float)(random.NextDouble() * 0.3);
                    profile.Sociability = 0.5f + (float)(random.NextDouble() * 0.3); // Warriors are social
                    break;
                case "honorable":
                case "noble":
                case "righteous":
                    profile.Trustworthiness = 0.8f + (float)(random.NextDouble() * 0.2);
                    profile.Loyalty = 0.7f + (float)(random.NextDouble() * 0.3);
                    profile.Courage = 0.6f + (float)(random.NextDouble() * 0.3);
                    break;
                case "cunning":
                case "scheming":
                case "sneaky":
                    profile.Intelligence = 0.7f + (float)(random.NextDouble() * 0.3);
                    profile.Greed = 0.6f + (float)(random.NextDouble() * 0.3);
                    profile.Ambition = 0.6f + (float)(random.NextDouble() * 0.3);
                    break;
                case "wise":
                case "eccentric":
                    profile.Intelligence = 0.8f + (float)(random.NextDouble() * 0.2);
                    profile.Impulsiveness = 0.1f + (float)(random.NextDouble() * 0.2);
                    profile.Patience = 0.7f + (float)(random.NextDouble() * 0.3);
                    break;
                case "greedy":
                    profile.Greed = 0.8f + (float)(random.NextDouble() * 0.2);
                    profile.Ambition = 0.6f + (float)(random.NextDouble() * 0.3);
                    break;
                case "kind":
                case "compassionate":
                case "gentle":
                    profile.Sociability = 0.7f + (float)(random.NextDouble() * 0.3);
                    profile.Aggression = 0.1f + (float)(random.NextDouble() * 0.2);
                    profile.Loyalty = 0.6f + (float)(random.NextDouble() * 0.3);
                    break;
                case "cowardly":
                    profile.Courage = 0.1f + (float)(random.NextDouble() * 0.2);
                    profile.Caution = 0.7f + (float)(random.NextDouble() * 0.3);
                    break;
                case "ambitious":
                case "driven":
                    profile.Ambition = 0.7f + (float)(random.NextDouble() * 0.3);
                    profile.Courage = 0.5f + (float)(random.NextDouble() * 0.3);
                    break;
                case "loyal":
                case "steadfast":
                    profile.Loyalty = 0.8f + (float)(random.NextDouble() * 0.2);
                    profile.Sociability = 0.5f + (float)(random.NextDouble() * 0.3);
                    break;
                default:
                    // Neutral personality - keep base random values
                    break;
            }

            // Apply romance traits from template
            profile.Gender = template.Gender;
            profile.Orientation = template.Orientation;
            profile.IntimateStyle = template.IntimateStyle;
            profile.RelationshipPref = template.RelationshipPref;

            // Apply optional romance personality modifiers from template
            if (template.Romanticism.HasValue)
                profile.Romanticism = template.Romanticism.Value;
            else
                profile.Romanticism = 0.5f + (float)(random.NextDouble() * 0.3 - 0.15); // 0.35-0.65

            if (template.Sensuality.HasValue)
                profile.Sensuality = template.Sensuality.Value;
            else
                profile.Sensuality = 0.5f + (float)(random.NextDouble() * 0.3 - 0.15);

            if (template.Passion.HasValue)
                profile.Passion = template.Passion.Value;
            else
                profile.Passion = 0.5f + (float)(random.NextDouble() * 0.3 - 0.15);

            if (template.Adventurousness.HasValue)
                profile.Adventurousness = template.Adventurousness.Value;
            else
                profile.Adventurousness = 0.4f + (float)(random.NextDouble() * 0.3);

            // Generate remaining romance traits randomly based on personality
            profile.Flirtatiousness = profile.Sociability * 0.5f + (float)(random.NextDouble() * 0.3);
            profile.Tenderness = personality.ToLower() switch
            {
                "gentle" or "kind" or "compassionate" => 0.8f + (float)(random.NextDouble() * 0.2),
                "brutal" or "cruel" or "merciless" => 0.1f + (float)(random.NextDouble() * 0.2),
                _ => 0.4f + (float)(random.NextDouble() * 0.3)
            };
            profile.Jealousy = 0.3f + (float)(random.NextDouble() * 0.4);
            profile.Commitment = profile.RelationshipPref == RelationshipPreference.Monogamous ? 0.8f : 0.4f;
            profile.Exhibitionism = (float)(random.NextDouble() * 0.3);
            profile.Voyeurism = (float)(random.NextDouble() * 0.3);

            // Create NPCBrain with the personality profile
            npc.Brain = new NPCBrain(npc, profile);
            npc.Personality = profile;  // Also set direct reference for easy access

            // Adjust based on alignment
            switch (alignment.ToLower())
            {
                case "good":
                    npc.Chivalry = random.Next(500, 1000);
                    npc.Darkness = 0;
                    break;
                case "evil":
                    npc.Darkness = random.Next(500, 1000);
                    npc.Chivalry = 0;
                    break;
                case "neutral":
                    npc.Chivalry = random.Next(0, 300);
                    npc.Darkness = random.Next(0, 300);
                    break;
            }
        }

        /// <summary>
        /// Give starting equipment to NPC using the modern equipment system
        /// Creates actual Equipment objects that will show when equipping teammates
        /// </summary>
        private void GiveStartingEquipment(NPC npc)
        {
            // Ensure EquipmentDatabase is initialized first
            EquipmentDatabase.Initialize();

            // Initialize the EquippedItems dictionary
            if (npc.EquippedItems == null)
                npc.EquippedItems = new Dictionary<EquipmentSlot, int>();

            // Calculate gold budget based on level (NPCs have saved up gold for gear)
            long goldBudget = npc.Level * 500 + random.Next(npc.Level * 100, npc.Level * 300);

            // Equip a weapon appropriate for class and level
            EquipNPCWeapon(npc, goldBudget);

            // Equip armor appropriate for class and level
            EquipNPCArmor(npc, goldBudget);

            // Give some healing potions
            npc.Healing = random.Next(npc.Level, npc.Level * 3);

            // Initialize inventory if needed
            if (npc.Item == null)
                npc.Item = new List<int>();
            if (npc.ItemType == null)
                npc.ItemType = new List<global::ObjType>();

            // Recalculate stats to apply equipment bonuses
            npc.RecalculateStats();
        }

        /// <summary>
        /// Equip an appropriate weapon for the NPC based on class and level
        /// </summary>
        private void EquipNPCWeapon(NPC npc, long goldBudget)
        {
            EquipmentDatabase.Initialize();

            // Determine preferred weapon type based on class
            WeaponHandedness preferredHandedness = npc.Class switch
            {
                CharacterClass.Warrior or CharacterClass.Barbarian => random.Next(2) == 0
                    ? WeaponHandedness.TwoHanded : WeaponHandedness.OneHanded,
                CharacterClass.Magician => WeaponHandedness.TwoHanded, // Staves
                CharacterClass.Cleric => WeaponHandedness.OneHanded, // Maces + shield
                CharacterClass.Paladin => WeaponHandedness.OneHanded, // Sword + shield
                CharacterClass.Assassin => WeaponHandedness.OneHanded, // Daggers
                CharacterClass.Sage => WeaponHandedness.TwoHanded, // Staves
                _ => random.Next(2) == 0 ? WeaponHandedness.TwoHanded : WeaponHandedness.OneHanded
            };

            // Get best weapon within budget
            var weapons = EquipmentDatabase.GetWeaponsByHandedness(preferredHandedness)
                .Where(w => w.Value <= goldBudget * 0.4) // Spend up to 40% of budget on weapon
                .OrderByDescending(w => w.WeaponPower)
                .ToList();

            if (weapons.Count > 0)
            {
                // Pick from top 3 weapons (some randomness)
                var weapon = weapons[Math.Min(random.Next(3), weapons.Count - 1)];
                npc.EquippedItems[EquipmentSlot.MainHand] = weapon.Id;
            }

            // If using one-handed weapon, maybe add a shield
            if (preferredHandedness == WeaponHandedness.OneHanded)
            {
                // Classes that prefer shields
                bool wantsShield = npc.Class switch
                {
                    CharacterClass.Warrior or CharacterClass.Paladin or CharacterClass.Cleric => true,
                    _ => random.Next(3) == 0 // 33% chance for other classes
                };

                if (wantsShield)
                {
                    var shields = EquipmentDatabase.GetShields()
                        .Where(s => s.Value <= goldBudget * 0.2) // Spend up to 20% on shield
                        .OrderByDescending(s => s.ShieldBonus)
                        .ToList();

                    if (shields.Count > 0)
                    {
                        var shield = shields[Math.Min(random.Next(3), shields.Count - 1)];
                        npc.EquippedItems[EquipmentSlot.OffHand] = shield.Id;
                    }
                }
            }
        }

        /// <summary>
        /// Equip appropriate armor for the NPC based on class and level
        /// </summary>
        private void EquipNPCArmor(NPC npc, long goldBudget)
        {
            EquipmentDatabase.Initialize();

            // Armor slots to equip (in order of priority)
            var armorSlots = new[]
            {
                EquipmentSlot.Body,   // Most important
                EquipmentSlot.Head,
                EquipmentSlot.Hands,
                EquipmentSlot.Feet,
                EquipmentSlot.Legs,
                EquipmentSlot.Arms,
                EquipmentSlot.Waist,
                EquipmentSlot.Cloak
            };

            // Budget allocation per slot (higher priority slots get more budget)
            float[] slotBudgetPercent = { 0.20f, 0.12f, 0.08f, 0.08f, 0.10f, 0.08f, 0.06f, 0.08f };

            for (int i = 0; i < armorSlots.Length; i++)
            {
                var slot = armorSlots[i];
                long slotBudget = (long)(goldBudget * slotBudgetPercent[i]);

                var armor = EquipmentDatabase.GetBestAffordable(slot, slotBudget);
                if (armor != null)
                {
                    npc.EquippedItems[slot] = armor.Id;
                }
            }

            // Maybe add a ring or amulet for higher-level NPCs
            if (npc.Level >= 5)
            {
                var rings = EquipmentDatabase.GetBySlot(EquipmentSlot.LFinger)
                    .Where(r => r.Value <= goldBudget * 0.05)
                    .OrderByDescending(r => r.Value)
                    .ToList();

                if (rings.Count > 0)
                {
                    npc.EquippedItems[EquipmentSlot.LFinger] = rings[Math.Min(random.Next(3), rings.Count - 1)].Id;
                }
            }

            if (npc.Level >= 10)
            {
                var amulets = EquipmentDatabase.GetBySlot(EquipmentSlot.Neck)
                    .Where(a => a.Value <= goldBudget * 0.05)
                    .OrderByDescending(a => a.Value)
                    .ToList();

                if (amulets.Count > 0)
                {
                    npc.EquippedItems[EquipmentSlot.Neck] = amulets[Math.Min(random.Next(3), amulets.Count - 1)].Id;
                }
            }
        }

        /// <summary>
        /// Get a random starting location for NPCs
        /// </summary>
        private string GetRandomStartLocation()
        {
            // Place NPCs in various town locations with readable names
            // These must match the GetNPCLocationString() mapping in BaseLocation.cs
            var locations = new[]
            {
                "Main Street",
                "Main Street",   // More NPCs on Main Street (high traffic)
                "Auction House",
                "Inn",
                "Inn",           // More NPCs at the Inn
                "Temple",
                "Church",
                "Weapon Shop",
                "Armor Shop",
                "Magic Shop",
                "Castle",
                "Castle",        // More NPCs at Castle
                "Bank",
                "Healer",
                "Dark Alley"     // Some shady characters in the alley
            };

            return locations[random.Next(locations.Length)];
        }

        /// <summary>
        /// Distribute NPCs across the world
        /// </summary>
        private void DistributeNPCsAcrossWorld()
        {
            // Spread NPCs across different locations
            var locationCounts = new Dictionary<string, int>();

            foreach (var npc in spawnedNPCs)
            {
                if (!locationCounts.ContainsKey(npc.CurrentLocation))
                    locationCounts[npc.CurrentLocation] = 0;

                locationCounts[npc.CurrentLocation]++;

                // If too many NPCs in one location, move some
                if (locationCounts[npc.CurrentLocation] > 5)
                {
                    npc.CurrentLocation = GetRandomStartLocation();
                }
            }

            // GD.Print($"[NPCSpawn] NPCs distributed across {locationCounts.Count} locations");
        }

        /// <summary>
        /// Get all NPCs at a specific location (excludes dead NPCs)
        /// </summary>
        public List<NPC> GetNPCsAtLocation(string locationId)
        {
            return ActiveNPCs.Where(npc => npc.CurrentLocation == locationId && !npc.IsDead).ToList();
        }

        /// <summary>
        /// Get an NPC by name (excludes dead NPCs by default)
        /// </summary>
        public NPC? GetNPCByName(string name, bool includeDead = false)
        {
            return ActiveNPCs.FirstOrDefault(npc =>
                npc.Name2.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                (includeDead || !npc.IsDead));
        }

        /// <summary>
        /// Reset all NPCs (for new game)
        /// </summary>
        public void ResetNPCs()
        {
            _npcLock.EnterWriteLock();
            try
            {
                spawnedNPCs.Clear();
                npcsInitialized = false;
                _listVersion++;
            }
            finally
            {
                _npcLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Get all NPCs currently in prison
        /// </summary>
        public List<NPC> GetPrisoners()
        {
            return ActiveNPCs.Where(npc => npc.DaysInPrison > 0).ToList();
        }

        /// <summary>
        /// Find a prisoner by partial name match
        /// </summary>
        public NPC? FindPrisoner(string searchName)
        {
            if (string.IsNullOrWhiteSpace(searchName)) return null;

            return ActiveNPCs.FirstOrDefault(npc =>
                npc.DaysInPrison > 0 &&
                npc.Name2.Contains(searchName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Imprison an NPC for a number of days
        /// </summary>
        public void ImprisonNPC(NPC npc, int days)
        {
            if (npc == null) return;
            npc.DaysInPrison = (byte)Math.Min(255, days);
            npc.CellDoorOpen = false;
            npc.RescuedBy = "";
            npc.CurrentLocation = "Prison";
            // GD.Print($"[NPCSpawn] {npc.Name2} imprisoned for {days} days");
        }

        /// <summary>
        /// Release an NPC from prison
        /// </summary>
        public void ReleaseNPC(NPC npc, string rescuerName = "")
        {
            if (npc == null) return;
            npc.DaysInPrison = 0;
            npc.CellDoorOpen = false;
            npc.RescuedBy = rescuerName;
            npc.HP = npc.MaxHP;
            npc.CurrentLocation = "Main Street";
            // GD.Print($"[NPCSpawn] {npc.Name2} released from prison" +
            //     (string.IsNullOrEmpty(rescuerName) ? "" : $" by {rescuerName}"));
        }

        /// <summary>
        /// Mark an NPC's cell door as open (rescued)
        /// </summary>
        public void OpenCellDoor(NPC npc, string rescuerName)
        {
            if (npc == null) return;
            npc.CellDoorOpen = true;
            npc.RescuedBy = rescuerName;
            // GD.Print($"[NPCSpawn] Cell door opened for {npc.Name2} by {rescuerName}");
        }

        /// <summary>
        /// Clear all NPCs (for loading saves)
        /// </summary>
        public void ClearAllNPCs()
        {
            _npcLock.EnterWriteLock();
            try
            {
                spawnedNPCs.Clear();
                npcsInitialized = false;
                _listVersion++;
            }
            finally
            {
                _npcLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Add a restored NPC from save data
        /// </summary>
        public void AddRestoredNPC(NPC npc)
        {
            if (npc == null)
                return;

            _npcLock.EnterWriteLock();
            try
            {
                // Check for duplicate by ID or name to prevent double-adding
                bool isDuplicate = spawnedNPCs.Any(existing =>
                    (!string.IsNullOrEmpty(existing.ID) && existing.ID == npc.ID) ||
                    existing.Name2.Equals(npc.Name2, StringComparison.OrdinalIgnoreCase));

                if (!isDuplicate)
                {
                    spawnedNPCs.Add(npc);
                    _listVersion++;
                }
            }
            finally
            {
                _npcLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Mark NPCs as initialized after restoration
        /// </summary>
        public void MarkAsInitialized()
        {
            npcsInitialized = true;
        }

        /// <summary>
        /// Calculate experience points needed for a given level using same curve as players
        /// Formula: Sum of level^2.0 * 50 for each level from 2 to target
        /// </summary>
        private static long GetExperienceForLevel(int level)
        {
            if (level <= 1) return 0;
            long exp = 0;
            for (int i = 2; i <= level; i++)
            {
                exp += (long)(Math.Pow(i, 2.0) * 50);
            }
            return exp;
        }
    }
}
