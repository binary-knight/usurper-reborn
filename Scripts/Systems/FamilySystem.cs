using System;
using System.Collections.Generic;
using System.Linq;
using UsurperRemake.Utils;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Family System - Manages children, family relationships, and child aging
    /// Children age over time and eventually grow into adult NPCs
    /// </summary>
    public class FamilySystem
    {
        private static FamilySystem? _instance;
        public static FamilySystem Instance => _instance ??= new FamilySystem();

        // All children in the game world
        private List<Child> _children = new();

        // Age at which children become adults (and turn into NPCs)
        public const int ADULT_AGE = 18;

        // Days per year of aging (children age faster than real time)
        public const int DAYS_PER_YEAR = 7; // 1 week real time = 1 year in-game

        public FamilySystem()
        {
            _instance = this;
        }

        /// <summary>
        /// Get all children
        /// </summary>
        public List<Child> AllChildren => _children;

        /// <summary>
        /// Add a new child to the registry
        /// </summary>
        public void RegisterChild(Child child)
        {
            if (child == null) return;
            if (_children.Any(c => c.BirthDate == child.BirthDate && c.MotherID == child.MotherID && c.FatherID == child.FatherID))
                return; // Prevent duplicates

            _children.Add(child);
            // GD.Print($"[Family] Registered child: {child.Name}");
        }

        /// <summary>
        /// Get children belonging to a specific character (as parent)
        /// </summary>
        public List<Child> GetChildrenOf(Character parent)
        {
            if (parent == null) return new List<Child>();

            return _children.Where(c =>
                !c.Deleted &&
                (c.MotherID == parent.ID || c.FatherID == parent.ID ||
                 c.Mother == parent.Name || c.Father == parent.Name)
            ).ToList();
        }

        /// <summary>
        /// Get children of a married couple
        /// </summary>
        public List<Child> GetChildrenOfCouple(Character parent1, Character parent2)
        {
            if (parent1 == null || parent2 == null) return new List<Child>();

            return _children.Where(c =>
                !c.Deleted &&
                (((c.MotherID == parent1.ID || c.Mother == parent1.Name) &&
                  (c.FatherID == parent2.ID || c.Father == parent2.Name)) ||
                 ((c.MotherID == parent2.ID || c.Mother == parent2.Name) &&
                  (c.FatherID == parent1.ID || c.Father == parent1.Name)))
            ).ToList();
        }

        #region Child Bonuses

        /// <summary>
        /// Get the stat bonuses a player receives from their children
        /// Children under 18 provide bonuses that scale with the number of children
        /// Bonuses are removed when children come of age
        /// </summary>
        public ChildBonuses CalculateChildBonuses(Character parent)
        {
            var children = GetChildrenOf(parent);
            var minorChildren = children.Where(c => c.Age < ADULT_AGE).ToList();

            if (minorChildren.Count == 0)
                return new ChildBonuses();

            // Base bonus per child (diminishing returns after 5 children)
            int childCount = minorChildren.Count;
            float effectiveChildren = childCount <= 5 ? childCount : 5 + (childCount - 5) * 0.5f;

            var bonuses = new ChildBonuses
            {
                ChildCount = childCount,
                // +2% XP bonus per effective child (max ~15% at 10 children)
                XPMultiplier = 1.0f + (effectiveChildren * 0.02f),
                // +50 max HP per effective child (motivation to protect family)
                BonusMaxHP = (int)(effectiveChildren * 50),
                // +5 strength per effective child (parental determination)
                BonusStrength = (int)(effectiveChildren * 5),
                // +3 charisma per effective child (family status)
                BonusCharisma = (int)(effectiveChildren * 3),
                // +100 gold income per child (family enterprise, child labor in medieval times)
                DailyGoldBonus = childCount * 100
            };

            // Royal children provide additional prestige bonuses
            int royalChildren = minorChildren.Count(c => c.Royal > 0);
            if (royalChildren > 0)
            {
                bonuses.BonusCharisma += royalChildren * 5; // Extra charisma for royal heirs
                bonuses.DailyGoldBonus += royalChildren * 500; // Royal stipend
            }

            // Children's moral influence on the parent is handled as a one-shot
            // alignment event when the child is born / reaches a milestone, NOT as a
            // continuously-applied bonus. The previous Chivalry/Darkness fields here
            // were applied via += on every RecalculateStats() call without being reset
            // to base, so they accumulated permanently and slammed both alignment scales
            // into the v0.57.12 cap. See v0.57.17 release notes: a player report of
            // Darkness racing to the 1000 cap within hours of NG+ start surfaced this.

            return bonuses;
        }

        /// <summary>
        /// Apply child bonuses to a player's stats
        /// Should be called during stat recalculation
        /// </summary>
        public void ApplyChildBonuses(Character player)
        {
            if (player == null) return;

            var bonuses = CalculateChildBonuses(player);
            if (bonuses.ChildCount == 0) return;

            // Apply stat bonuses. These ARE safe under repeated RecalculateStats()
            // calls because MaxHP/Strength/Charisma are reset to their Base* values at
            // the top of RecalculateStats() before this method runs.
            // Chivalry/Darkness are NOT reset by RecalculateStats() — they're persistent
            // alignment, not derived stats — so we no longer touch them here. Children
            // affect alignment through one-shot events, not a recalc bonus.
            player.MaxHP += bonuses.BonusMaxHP;
            player.Strength += bonuses.BonusStrength;
            player.Charisma += bonuses.BonusCharisma;

            // Note: XP multiplier and daily gold bonus are applied elsewhere
            // XP: in CombatEngine when awarding XP
            // Gold: in DailyManager during daily income calculation
        }

        /// <summary>
        /// Get the XP multiplier from children (for use in combat XP calculation)
        /// </summary>
        public float GetChildXPMultiplier(Character player)
        {
            if (player == null) return 1.0f;
            return CalculateChildBonuses(player).XPMultiplier;
        }

        /// <summary>
        /// Get daily gold bonus from children (for use in daily income)
        /// </summary>
        public int GetChildDailyGoldBonus(Character player)
        {
            if (player == null) return 0;
            return CalculateChildBonuses(player).DailyGoldBonus;
        }

        /// <summary>
        /// Disown every child currently parented by the named character. Used when
        /// the player NG+'s in online mode — the new character is a fresh life and
        /// shouldn't see kids from the previous cycle waiting at home.
        ///
        /// In single-player mode FamilySystem.Reset() wipes _children entirely, so
        /// disown is unnecessary there. In online mode children are world data
        /// shared across players (settled, registered with NPC parents, possibly
        /// adopted by other players) — we can't just delete them. Instead we clear
        /// the matching parent slot. If both parent slots are now empty, the child
        /// moves to the orphanage so the existing orphan pipeline picks them up.
        ///
        /// OriginalMother / OriginalFather are immutable (used by orphan stories
        /// for narrative continuity) so the child's biological history is preserved.
        ///
        /// Returns the number of children disowned for logging.
        /// </summary>
        public int DisownChildrenOf(string playerName)
        {
            if (string.IsNullOrEmpty(playerName)) return 0;

            var matches = _children
                .Where(c => !c.Deleted && (c.Mother == playerName || c.Father == playerName))
                .ToList();

            int disowned = 0;
            int orphaned = 0;
            foreach (var child in matches)
            {
                bool wasMother = child.Mother == playerName;
                bool wasFather = child.Father == playerName;

                if (wasMother)
                {
                    child.Mother = "";
                    child.MotherID = "";
                }
                if (wasFather)
                {
                    child.Father = "";
                    child.FatherID = "";
                }

                disowned++;

                // If both parent slots are empty AND child is still a minor, send to orphanage.
                // Adult children (Age >= ADULT_AGE) are already independent — disown is just bookkeeping.
                if (string.IsNullOrEmpty(child.Mother) && string.IsNullOrEmpty(child.Father)
                    && child.Age < ADULT_AGE
                    && child.Location != GameConfig.ChildLocationOrphanage)
                {
                    child.Location = GameConfig.ChildLocationOrphanage;
                    orphaned++;
                }
            }

            if (disowned > 0)
            {
                DebugLogger.Instance?.LogInfo("FAMILY",
                    $"NG+ disown: {disowned} children disowned by {playerName}, {orphaned} sent to orphanage");
            }

            return disowned;
        }

        #endregion

        /// <summary>
        /// Process daily aging for all children
        /// Called from MaintenanceSystem or WorldSimulator
        /// </summary>
        public void ProcessDailyAging()
        {
            var childrenToAge = _children.Where(c => !c.Deleted && c.Age < ADULT_AGE).ToList();

            foreach (var child in childrenToAge)
            {
                int previousAge = child.Age;

                // Calculate age based on hours since birth (accelerated lifecycle rate)
                var hoursSinceBirth = (DateTime.Now - child.BirthDate).TotalHours;
                child.Age = (int)(hoursSinceBirth / GameConfig.NpcLifecycleHoursPerYear);

                // Check if child just came of age
                if (previousAge < ADULT_AGE && child.Age >= ADULT_AGE)
                {
                    // Orphanage children are handled by the orphanage coming-of-age system
                    if (child.Location == GameConfig.ChildLocationOrphanage)
                        continue;
                    ConvertChildToNPC(child);
                }
            }
        }

        /// <summary>
        /// Convert a child who has come of age into an NPC
        /// </summary>
        private void ConvertChildToNPC(Child child)
        {
            // Idempotency: skip if already converted (prevents duplicate NPCs across save/load)
            if (child.Deleted) return;

            // Don't exceed population cap
            if ((NPCSpawnSystem.Instance?.ActiveNPCs.Count ?? 0) >= GameConfig.MaxNPCPopulation) return;
            var existingAdult = NPCSpawnSystem.Instance?.ActiveNPCs
                ?.FirstOrDefault(n => n.Name2 == child.Name && n.BirthDate == child.BirthDate);
            if (existingAdult != null) { child.Deleted = true; return; }

            // Create NPC from child
            var npc = new NPC
            {
                // Assign unique ID (critical for relationship/family tracking)
                ID = $"npc_{child.Name.ToLower().Replace(" ", "_")}_{Guid.NewGuid().ToString("N").Substring(0, 8)}",
                Name1 = child.Name,
                Name2 = child.Name,
                Sex = child.Sex,
                Age = ADULT_AGE,
                Race = DetermineChildRace(child),
                Class = DetermineChildClass(child),
                Level = 1,
                HP = 100,
                MaxHP = 100,
                Strength = 10 + Random.Shared.Next(5),
                Defence = 10 + Random.Shared.Next(5),
                Stamina = 10 + Random.Shared.Next(5),
                Agility = 10 + Random.Shared.Next(5),
                Intelligence = 10 + Random.Shared.Next(5),
                Charisma = 10 + Random.Shared.Next(5),
                Gold = 100,
                Experience = 0,
                CurrentLocation = "Main Street",
                AI = CharacterAI.Computer,
                BirthDate = child.BirthDate // Carry over birth date for lifecycle aging
            };

            // Set HP directly since IsAlive is computed from HP
            npc.HP = npc.MaxHP;

            // Inherit some traits based on soul
            if (child.Soul > 200)
            {
                npc.Chivalry = 50 + Random.Shared.Next(50);
                npc.Darkness = 0;
            }
            else if (child.Soul < -200)
            {
                npc.Chivalry = 0;
                npc.Darkness = 50 + Random.Shared.Next(50);
            }
            else
            {
                npc.Chivalry = 25;
                npc.Darkness = 25;
            }

            // Royal blood gives bonuses
            if (child.Royal > 0)
            {
                npc.Charisma += 10 * child.Royal;
                npc.Gold += 1000 * child.Royal;
            }

            // Create personality based on child's soul - use GenerateForArchetype to get romance traits
            var personality = PersonalityProfile.GenerateForArchetype("commoner");
            personality.Gender = child.Sex == CharacterSex.Female ? GenderIdentity.Female : GenderIdentity.Male;
            if (child.Soul > 100)
            {
                personality.Aggression = 0.2f;
                personality.Tenderness = 0.8f;  // Good kids are tender
                personality.Greed = 0.2f;
            }
            else if (child.Soul < -100)
            {
                personality.Aggression = 0.7f;
                personality.Tenderness = 0.2f;  // Bad kids are less tender
                personality.Greed = 0.7f;
            }

            npc.Personality = personality;
            npc.Brain = new NPCBrain(npc, personality);

            // Assign faction based on class, alignment, and personality
            npc.NPCFaction = NPCSpawnSystem.Instance?.DetermineFactionForNPC(npc);

            // Register with NPC system
            NPCSpawnSystem.Instance?.AddRestoredNPC(npc);

            // Mark child as "graduated" to adult
            child.Deleted = true;

            // Generate news
            NewsSystem.Instance?.WriteComingOfAgeNews(child.Name, child.Mother, child.Father);

            // GD.Print($"[Family] Created adult NPC: {npc.Name2} ({npc.Class})");
        }

        /// <summary>
        /// Determine race based on parents
        /// </summary>
        private CharacterRace DetermineChildRace(Child child)
        {
            // Try to find parents and inherit race
            var mother = FindParentByID(child.MotherID) ?? FindParentByName(child.Mother);
            var father = FindParentByID(child.FatherID) ?? FindParentByName(child.Father);

            if (mother != null && father != null)
            {
                // 50/50 chance of inheriting either parent's race
                return Random.Shared.Next(2) == 0 ? mother.Race : father.Race;
            }
            else if (mother != null)
            {
                return mother.Race;
            }
            else if (father != null)
            {
                return father.Race;
            }

            return CharacterRace.Human; // Default
        }

        /// <summary>
        /// Determine class based on parents and soul
        /// </summary>
        private CharacterClass DetermineChildClass(Child child)
        {
            var random = Random.Shared;
            var race = DetermineChildRace(child);
            GameConfig.InvalidCombinations.TryGetValue(race, out var invalid);

            CharacterClass[] candidates;
            if (child.Soul > 200)
            {
                // Good children become paladins, clerics, warriors
                candidates = new[] { CharacterClass.Paladin, CharacterClass.Cleric, CharacterClass.Warrior };
            }
            else if (child.Soul < -200)
            {
                // Evil children become assassins, magicians, barbarians
                candidates = new[] { CharacterClass.Assassin, CharacterClass.Magician, CharacterClass.Barbarian };
            }
            else
            {
                // Neutral children - any base class
                var baseClasses = new List<CharacterClass> {
                    CharacterClass.Warrior, CharacterClass.Magician, CharacterClass.Assassin,
                    CharacterClass.Ranger, CharacterClass.Bard, CharacterClass.Sage,
                    CharacterClass.Cleric, CharacterClass.Alchemist, CharacterClass.Jester,
                    CharacterClass.Barbarian, CharacterClass.Paladin
                };
                // MysticShaman available for Troll, Orc, Gnoll children
                if (race == CharacterRace.Troll || race == CharacterRace.Orc || race == CharacterRace.Gnoll)
                    baseClasses.Add(CharacterClass.MysticShaman);
                candidates = baseClasses.ToArray();
            }

            // Filter out invalid race/class combos
            if (invalid != null)
                candidates = candidates.Where(c => !invalid.Contains(c)).ToArray();

            // Fallback to Warrior if all candidates invalid for this race
            return candidates.Length > 0 ? candidates[random.Next(candidates.Length)] : CharacterClass.Warrior;
        }

        /// <summary>
        /// Find parent character by ID
        /// </summary>
        private Character? FindParentByID(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            // Check NPCs
            var npc = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == id);
            if (npc != null) return npc;

            // Check current player
            var player = GameEngine.Instance?.CurrentPlayer;
            if (player?.ID == id) return player;

            return null;
        }

        /// <summary>
        /// Find parent character by name
        /// </summary>
        private Character? FindParentByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            // Check NPCs
            var npc = NPCSpawnSystem.Instance?.GetNPCByName(name);
            if (npc != null) return npc;

            // Check current player
            var player = GameEngine.Instance?.CurrentPlayer;
            if (player?.Name == name || player?.Name2 == name) return player;

            return null;
        }

        /// <summary>
        /// Handle custody transfer after divorce
        /// </summary>
        public void HandleDivorceCustody(Character parent1, Character parent2, Character custodialParent)
        {
            var children = GetChildrenOfCouple(parent1, parent2);
            var losingParent = custodialParent.ID == parent1.ID ? parent2 : parent1;

            foreach (var child in children)
            {
                child.HandleDivorceCustody(losingParent, custodialParent);
            }

            if (children.Count > 0)
            {
                NewsSystem.Instance?.Newsy(true,
                    $"{custodialParent.Name} has been granted custody of {children.Count} child(ren) in the divorce.");
            }
        }

        #region NPC-NPC Children

        // Fantasy name pools for NPC children
        private static readonly string[] MaleNames = new[]
        {
            "Aldric", "Bram", "Caelum", "Dorin", "Eldric", "Fenris", "Gareth", "Hadwin",
            "Ivar", "Jorund", "Kael", "Leoric", "Magnus", "Noric", "Osric", "Perrin",
            "Quillan", "Rowan", "Soren", "Theron", "Ulric", "Varen", "Wulfric", "Xander",
            "Yorick", "Zephyr", "Alaric", "Brandt", "Cedric", "Darian", "Erland", "Finnian",
            "Gideon", "Halvar", "Iskander", "Jarek", "Korbin", "Lysander", "Malakai", "Nolan"
        };

        private static readonly string[] FemaleNames = new[]
        {
            "Aelara", "Brielle", "Calista", "Darina", "Elara", "Freya", "Gwyneth", "Helena",
            "Isolde", "Jocelyn", "Kira", "Lyria", "Mirena", "Nessa", "Orina", "Petra",
            "Rhiannon", "Seraphina", "Thalia", "Ursula", "Vesper", "Wren", "Ysolde", "Zara",
            "Astrid", "Brianna", "Celeste", "Dahlia", "Elowen", "Fiora", "Guinevere", "Hilda",
            "Ingrid", "Juliana", "Katarina", "Lucinda", "Morgana", "Nerissa", "Ondine", "Rosalind"
        };

        // Fantasy surnames for children whose fathers have no extractable surname
        // (alias NPCs, single-name NPCs, title-only NPCs, etc.)
        private static readonly string[] GeneratedSurnames = new[]
        {
            "Ashford", "Blackthorn", "Copperfield", "Dunmore", "Everhart",
            "Fairwind", "Greymane", "Holloway", "Ironwood", "Kettleburn",
            "Larkwood", "Mossgrove", "Northgate", "Oakshield", "Pennywhistle",
            "Quickwater", "Ravenswood", "Silverbrook", "Thornbury", "Underhill",
            "Whitmore", "Yarrow", "Ashwick", "Bramblewood", "Coldstream",
            "Dewberry", "Emberglow", "Foxglove", "Greystone", "Hawkridge",
            "Ivywood", "Juniper", "Kingsmill", "Larkspur", "Meadowbrook",
            "Nighthollow", "Oldfield", "Pinecrest", "Ravenscroft", "Stoneleigh"
        };

        /// <summary>
        /// Create a child from two NPC parents. Uses the existing Child class and aging system.
        /// When the child reaches ADULT_AGE (18 game-years), ConvertChildToNPC() will
        /// automatically turn them into a full NPC that joins the realm.
        /// </summary>
        public void CreateNPCChild(Character mother, Character father)
        {
            var rng = Random.Shared;
            var sex = rng.Next(2) == 0 ? CharacterSex.Male : CharacterSex.Female;

            var firstName = GenerateNPCChildFirstName(sex);
            var fatherSurname = ExtractSurname(father.Name2 ?? father.Name);
            // If father has no extractable surname, generate one deterministically
            if (fatherSurname == null)
                fatherSurname = GenerateSurnameForParent(father.Name2 ?? father.Name);
            var childName = $"{firstName} {fatherSurname}";

            var child = new Child
            {
                Name = childName,
                Mother = mother.Name2 ?? mother.Name,
                Father = father.Name2 ?? father.Name,
                MotherID = mother.ID ?? "",
                FatherID = father.ID ?? "",
                Sex = sex,
                Age = 0,
                BirthDate = DateTime.Now,
                Health = 100,
                Soul = rng.Next(-200, 201),
                Location = GameConfig.ChildLocationHome,
                Named = true,
            };

            RegisterChild(child);

            NewsSystem.Instance?.WriteBirthNews(
                mother.Name2 ?? mother.Name,
                father.Name2 ?? father.Name,
                child.Name, true);

            DebugLogger.Instance.LogInfo("LIFECYCLE",
                $"NPC child born: {child.Name} ({child.Sex}), parents: {mother.Name2} & {father.Name2}");
        }

        /// <summary>
        /// Pick a random first name for an NPC child.
        /// Surnames provide uniqueness, so first names can repeat across families.
        /// </summary>
        private string GenerateNPCChildFirstName(CharacterSex sex)
        {
            var rng = Random.Shared;
            var names = sex == CharacterSex.Female ? FemaleNames : MaleNames;
            return names[rng.Next(names.Length)];
        }

        /// <summary>
        /// Generate a deterministic surname for a parent who has no extractable one.
        /// Uses a stable hash (not GetHashCode which is randomized per-process in .NET Core).
        /// Same parent name always produces the same surname across restarts.
        /// </summary>
        private static string GenerateSurnameForParent(string parentName)
        {
            // DJB2 hash — stable and deterministic across all runs
            uint hash = 5381;
            foreach (char c in parentName)
                hash = ((hash << 5) + hash) + c;
            return GeneratedSurnames[hash % (uint)GeneratedSurnames.Length];
        }

        /// <summary>
        /// Extract a surname from an NPC name for children to inherit.
        /// Returns null for single names, titles ("The X"), epithets ("X the Y"),
        /// and alias-style names ("Nimble Nick", "Scarface Sam").
        /// Examples: "Borin Hammerhand" → "Hammerhand", "Shadow" → null,
        /// "The Stranger" → null, "Nimble Nick" → null.
        /// </summary>
        private static string? ExtractSurname(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName)) return null;

            // Alias-style names where the last word is a first name, not a surname.
            // These are criminal/rogue NPCs known by nicknames, not real names.
            if (AliasNames.Contains(fullName)) return null;

            var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return null;

            // Skip title-style names: "The Stranger", "The Executioner"
            if (parts[0].Equals("The", StringComparison.OrdinalIgnoreCase)) return null;

            // Skip epithet-style names: "Vex the Merciless", "Sir Cedric the Pure"
            for (int i = 1; i < parts.Length - 1; i++)
            {
                if (parts[i].Equals("the", StringComparison.OrdinalIgnoreCase)) return null;
            }

            // Skip "Sir/Lady/Brother/Sister/Mother/Father/Master" prefix + single word
            // e.g. "Sir Galahad" has no surname, but "Aldric Stormcaller" does
            var titlePrefixes = new[] { "Sir", "Lady", "Brother", "Sister", "Mother", "Father", "Master", "Lord", "Archpriest", "Zen" };
            if (titlePrefixes.Any(t => parts[0].Equals(t, StringComparison.OrdinalIgnoreCase)))
            {
                // "Sir Darius the Lost" already caught above by "the" check
                // "Archpriest Aldwyn" — only 2 parts with title prefix, no surname
                if (parts.Length == 2) return null;
                // "Sir Borin Hammerhand" → "Hammerhand"
                return parts[^1];
            }

            // Standard "FirstName LastName" → take the last part
            return parts[^1];
        }

        // NPC names that are aliases/nicknames — the last word is a first name, not a surname.
        private static readonly HashSet<string> AliasNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Nimble Nick", "Gold Tooth Gary", "Scarface Sam", "Quicksilver Quinn",
            "Pickpocket Pete", "Slick Rick", "Lucky Lou", "Dagger Dee"
        };

        #endregion

        /// <summary>
        /// Serialize children for save system
        /// </summary>
        public List<ChildData> SerializeChildren()
        {
            return _children.Where(c => !c.Deleted).Select(c => new ChildData
            {
                Name = c.Name,
                Mother = c.Mother,
                Father = c.Father,
                MotherID = c.MotherID,
                FatherID = c.FatherID,
                OriginalMother = c.OriginalMother,
                OriginalFather = c.OriginalFather,
                Sex = (int)c.Sex,
                Age = c.Age,
                BirthDate = c.BirthDate,
                Named = c.Named,
                Location = c.Location,
                Health = c.Health,
                Soul = c.Soul,
                MotherAccess = c.MotherAccess,
                FatherAccess = c.FatherAccess,
                Kidnapped = c.Kidnapped,
                KidnapperName = c.KidnapperName,
                RansomDemanded = c.RansomDemanded,
                CursedByGod = c.CursedByGod,
                Royal = c.Royal,
                LastParentingDay = c.LastParentingDay,
                LastParentingTime = c.LastParentingTime
            }).ToList();
        }

        /// <summary>
        /// Deserialize children from save system
        /// </summary>
        public void DeserializeChildren(List<ChildData> childDataList)
        {
            _children.Clear();

            if (childDataList == null) return;

            foreach (var data in childDataList)
            {
                var child = new Child
                {
                    Name = data.Name,
                    Mother = data.Mother,
                    Father = data.Father,
                    MotherID = data.MotherID,
                    FatherID = data.FatherID,
                    OriginalMother = data.OriginalMother,
                    OriginalFather = data.OriginalFather,
                    Sex = (CharacterSex)data.Sex,
                    Age = data.Age,
                    BirthDate = data.BirthDate,
                    Named = data.Named,
                    Location = data.Location,
                    Health = data.Health,
                    Soul = data.Soul,
                    MotherAccess = data.MotherAccess,
                    FatherAccess = data.FatherAccess,
                    Kidnapped = data.Kidnapped,
                    KidnapperName = data.KidnapperName,
                    RansomDemanded = data.RansomDemanded,
                    CursedByGod = data.CursedByGod,
                    Royal = data.Royal,
                    LastParentingDay = data.LastParentingDay,
                    LastParentingTime = data.LastParentingTime
                };

                // Prevent duplicates (same birth date + same parents)
                if (!_children.Any(c => c.BirthDate == child.BirthDate && c.MotherID == child.MotherID && c.FatherID == child.FatherID))
                    _children.Add(child);
            }

            // GD.Print($"[Family] Loaded {_children.Count} children from save");

            // Migration: add father's surname to children who don't have one yet
            MigrateChildSurnames();
        }

        /// <summary>
        /// Migration: compute correct name for each child — first name + father's
        /// surname (extracted or generated). Strips all Roman numerals. Every child
        /// ends up with a two-part name. Safe to run multiple times (idempotent).
        /// </summary>
        private void MigrateChildSurnames()
        {
            foreach (var child in _children)
            {
                if (child.Deleted) continue;
                if (string.IsNullOrWhiteSpace(child.Father)) continue;

                // Extract first name (always the first word)
                var nameParts = child.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (nameParts.Length == 0) continue;
                var firstName = nameParts[0];

                // Compute the correct surname from father (extract or generate)
                var surname = ExtractSurname(child.Father) ?? GenerateSurnameForParent(child.Father);

                var correctName = $"{firstName} {surname}";

                if (!child.Name.Equals(correctName, StringComparison.Ordinal))
                {
                    var oldName = child.Name;
                    child.Name = correctName;
                    DebugLogger.Instance.LogInfo("FAMILY",
                        $"Migrated child name: '{oldName}' → '{child.Name}' (father: {child.Father})");
                }
            }
        }

        /// <summary>
        /// Reset family system (for new game)
        /// </summary>
        public void Reset()
        {
            _children.Clear();
        }

        /// <summary>
        /// Get family summary for a character
        /// </summary>
        public string GetFamilySummary(Character character)
        {
            var children = GetChildrenOf(character);
            if (children.Count == 0)
            {
                return $"{character.Name} has no children.";
            }

            var summary = $"{character.Name} has {children.Count} child(ren):\n";
            foreach (var child in children)
            {
                summary += $"  - {child.Name}, age {child.Age}, {child.GetSoulDescription()}\n";
            }
            return summary;
        }
    }

    #region Parenting Scenario System

    public enum ChildAgeGroup { Toddler, Child, Teen }

    public class ParentingChoice
    {
        public string LabelKey { get; init; } = "";
        public int SoulChange { get; init; }
        public string ResultKey { get; init; } = "";
        public bool IsVirtuous { get; init; }
        public bool IsNeutral { get; init; }
    }

    public class ParentingScenario
    {
        public string Id { get; init; } = "";
        public ChildAgeGroup AgeGroup { get; init; }
        public string DescriptionKey { get; init; } = "";
        public ParentingChoice[] Choices { get; init; } = Array.Empty<ParentingChoice>();
    }

    /// <summary>
    /// Static pool of 24 CK-style parenting scenarios (8 per age group).
    /// Each presents a moral dilemma with 3 choices that affect child soul.
    /// </summary>
    public static class ParentingScenarios
    {
        public static ChildAgeGroup GetAgeGroup(int age)
        {
            if (age <= GameConfig.ParentingToddlerMaxAge) return ChildAgeGroup.Toddler;
            if (age <= GameConfig.ParentingChildMaxAge) return ChildAgeGroup.Child;
            return ChildAgeGroup.Teen;
        }

        public static ParentingScenario GetRandomScenario(Child child)
        {
            var group = GetAgeGroup(child.Age);
            var candidates = AllScenarios.Where(s => s.AgeGroup == group).ToArray();
            return candidates[Random.Shared.Next(candidates.Length)];
        }

        /// <summary>
        /// Calculate how parent alignment modifies the soul change.
        /// Virtuous parents amplify virtuous lessons; dark parents amplify cruel ones.
        /// Mismatched alignment reduces effectiveness.
        /// </summary>
        public static int CalculateAlignmentModifier(Character parent, ParentingChoice choice)
        {
            if (choice.IsNeutral) return 0;

            int alignmentBalance = (int)Math.Clamp(parent.Chivalry - parent.Darkness, -1000, 1000);

            if (choice.IsVirtuous)
            {
                if (alignmentBalance > 0)
                    return Math.Min(GameConfig.ParentingAlignmentBonusMax, alignmentBalance / 20);
                else
                    return -Math.Min(GameConfig.ParentingAlignmentPenaltyMax, Math.Abs(alignmentBalance) / 30);
            }
            else // cruel choice
            {
                if (alignmentBalance < 0)
                    return -Math.Min(GameConfig.ParentingAlignmentBonusMax, Math.Abs(alignmentBalance) / 20); // amplify negative
                else
                    return Math.Min(GameConfig.ParentingAlignmentPenaltyMax, alignmentBalance / 30); // reduce negative
            }
        }

        // ──────────── TODDLER SCENARIOS (ages 0-4) ────────────

        private static readonly ParentingScenario[] ToddlerScenarios = new[]
        {
            new ParentingScenario
            {
                Id = "toddler_toy_stolen", AgeGroup = ChildAgeGroup.Toddler,
                DescriptionKey = "home.parenting.toddler_toy_stolen.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.toddler_toy_stolen.c1", SoulChange = 20, ResultKey = "home.parenting.toddler_toy_stolen.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.toddler_toy_stolen.c2", SoulChange = -15, ResultKey = "home.parenting.toddler_toy_stolen.r2" },
                    new ParentingChoice { LabelKey = "home.parenting.toddler_toy_stolen.c3", SoulChange = -10, ResultKey = "home.parenting.toddler_toy_stolen.r3" },
                }
            },
            new ParentingScenario
            {
                Id = "toddler_crying_night", AgeGroup = ChildAgeGroup.Toddler,
                DescriptionKey = "home.parenting.toddler_crying_night.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.toddler_crying_night.c1", SoulChange = 20, ResultKey = "home.parenting.toddler_crying_night.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.toddler_crying_night.c2", SoulChange = -15, ResultKey = "home.parenting.toddler_crying_night.r2" },
                    new ParentingChoice { LabelKey = "home.parenting.toddler_crying_night.c3", SoulChange = 5, ResultKey = "home.parenting.toddler_crying_night.r3", IsNeutral = true },
                }
            },
            new ParentingScenario
            {
                Id = "toddler_sharing", AgeGroup = ChildAgeGroup.Toddler,
                DescriptionKey = "home.parenting.toddler_sharing.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.toddler_sharing.c1", SoulChange = 20, ResultKey = "home.parenting.toddler_sharing.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.toddler_sharing.c2", SoulChange = -10, ResultKey = "home.parenting.toddler_sharing.r2" },
                    new ParentingChoice { LabelKey = "home.parenting.toddler_sharing.c3", SoulChange = -20, ResultKey = "home.parenting.toddler_sharing.r3" },
                }
            },
            new ParentingScenario
            {
                Id = "toddler_animal", AgeGroup = ChildAgeGroup.Toddler,
                DescriptionKey = "home.parenting.toddler_animal.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.toddler_animal.c1", SoulChange = 25, ResultKey = "home.parenting.toddler_animal.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.toddler_animal.c2", SoulChange = -20, ResultKey = "home.parenting.toddler_animal.r2" },
                    new ParentingChoice { LabelKey = "home.parenting.toddler_animal.c3", SoulChange = 0, ResultKey = "home.parenting.toddler_animal.r3", IsNeutral = true },
                }
            },
            new ParentingScenario
            {
                Id = "toddler_tantrum", AgeGroup = ChildAgeGroup.Toddler,
                DescriptionKey = "home.parenting.toddler_tantrum.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.toddler_tantrum.c1", SoulChange = 15, ResultKey = "home.parenting.toddler_tantrum.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.toddler_tantrum.c2", SoulChange = -15, ResultKey = "home.parenting.toddler_tantrum.r2" },
                    new ParentingChoice { LabelKey = "home.parenting.toddler_tantrum.c3", SoulChange = -5, ResultKey = "home.parenting.toddler_tantrum.r3", IsNeutral = true },
                }
            },
            new ParentingScenario
            {
                Id = "toddler_stranger", AgeGroup = ChildAgeGroup.Toddler,
                DescriptionKey = "home.parenting.toddler_stranger.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.toddler_stranger.c1", SoulChange = 15, ResultKey = "home.parenting.toddler_stranger.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.toddler_stranger.c2", SoulChange = -20, ResultKey = "home.parenting.toddler_stranger.r2" },
                    new ParentingChoice { LabelKey = "home.parenting.toddler_stranger.c3", SoulChange = 5, ResultKey = "home.parenting.toddler_stranger.r3", IsNeutral = true },
                }
            },
            new ParentingScenario
            {
                Id = "toddler_drawing", AgeGroup = ChildAgeGroup.Toddler,
                DescriptionKey = "home.parenting.toddler_drawing.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.toddler_drawing.c1", SoulChange = 20, ResultKey = "home.parenting.toddler_drawing.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.toddler_drawing.c2", SoulChange = -20, ResultKey = "home.parenting.toddler_drawing.r2" },
                    new ParentingChoice { LabelKey = "home.parenting.toddler_drawing.c3", SoulChange = 0, ResultKey = "home.parenting.toddler_drawing.r3", IsNeutral = true },
                }
            },
            new ParentingScenario
            {
                Id = "toddler_curse_word", AgeGroup = ChildAgeGroup.Toddler,
                DescriptionKey = "home.parenting.toddler_curse_word.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.toddler_curse_word.c1", SoulChange = 15, ResultKey = "home.parenting.toddler_curse_word.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.toddler_curse_word.c2", SoulChange = -10, ResultKey = "home.parenting.toddler_curse_word.r2" },
                    new ParentingChoice { LabelKey = "home.parenting.toddler_curse_word.c3", SoulChange = -20, ResultKey = "home.parenting.toddler_curse_word.r3" },
                }
            },
        };

        // ──────────── CHILD SCENARIOS (ages 5-12) ────────────

        private static readonly ParentingScenario[] ChildScenarios = new[]
        {
            new ParentingScenario
            {
                Id = "child_stealing", AgeGroup = ChildAgeGroup.Child,
                DescriptionKey = "home.parenting.child_stealing.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.child_stealing.c1", SoulChange = 20, ResultKey = "home.parenting.child_stealing.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.child_stealing.c2", SoulChange = -15, ResultKey = "home.parenting.child_stealing.r2" },
                    new ParentingChoice { LabelKey = "home.parenting.child_stealing.c3", SoulChange = -20, ResultKey = "home.parenting.child_stealing.r3" },
                }
            },
            new ParentingScenario
            {
                Id = "child_bully", AgeGroup = ChildAgeGroup.Child,
                DescriptionKey = "home.parenting.child_bully.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.child_bully.c1", SoulChange = 20, ResultKey = "home.parenting.child_bully.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.child_bully.c2", SoulChange = -10, ResultKey = "home.parenting.child_bully.r2" },
                    new ParentingChoice { LabelKey = "home.parenting.child_bully.c3", SoulChange = 5, ResultKey = "home.parenting.child_bully.r3", IsNeutral = true },
                }
            },
            new ParentingScenario
            {
                Id = "child_lie", AgeGroup = ChildAgeGroup.Child,
                DescriptionKey = "home.parenting.child_lie.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.child_lie.c1", SoulChange = 20, ResultKey = "home.parenting.child_lie.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.child_lie.c2", SoulChange = -15, ResultKey = "home.parenting.child_lie.r2" },
                    new ParentingChoice { LabelKey = "home.parenting.child_lie.c3", SoulChange = -25, ResultKey = "home.parenting.child_lie.r3" },
                }
            },
            new ParentingScenario
            {
                Id = "child_stray", AgeGroup = ChildAgeGroup.Child,
                DescriptionKey = "home.parenting.child_stray.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.child_stray.c1", SoulChange = 20, ResultKey = "home.parenting.child_stray.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.child_stray.c2", SoulChange = 0, ResultKey = "home.parenting.child_stray.r2", IsNeutral = true },
                    new ParentingChoice { LabelKey = "home.parenting.child_stray.c3", SoulChange = -25, ResultKey = "home.parenting.child_stray.r3" },
                }
            },
            new ParentingScenario
            {
                Id = "child_fight", AgeGroup = ChildAgeGroup.Child,
                DescriptionKey = "home.parenting.child_fight.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.child_fight.c1", SoulChange = 15, ResultKey = "home.parenting.child_fight.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.child_fight.c2", SoulChange = -10, ResultKey = "home.parenting.child_fight.r2" },
                    new ParentingChoice { LabelKey = "home.parenting.child_fight.c3", SoulChange = -15, ResultKey = "home.parenting.child_fight.r3" },
                }
            },
            new ParentingScenario
            {
                Id = "child_beggar", AgeGroup = ChildAgeGroup.Child,
                DescriptionKey = "home.parenting.child_beggar.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.child_beggar.c1", SoulChange = 25, ResultKey = "home.parenting.child_beggar.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.child_beggar.c2", SoulChange = -20, ResultKey = "home.parenting.child_beggar.r2" },
                    new ParentingChoice { LabelKey = "home.parenting.child_beggar.c3", SoulChange = 15, ResultKey = "home.parenting.child_beggar.r3", IsVirtuous = true },
                }
            },
            new ParentingScenario
            {
                Id = "child_broken", AgeGroup = ChildAgeGroup.Child,
                DescriptionKey = "home.parenting.child_broken.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.child_broken.c1", SoulChange = 20, ResultKey = "home.parenting.child_broken.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.child_broken.c2", SoulChange = -20, ResultKey = "home.parenting.child_broken.r2" },
                    new ParentingChoice { LabelKey = "home.parenting.child_broken.c3", SoulChange = -15, ResultKey = "home.parenting.child_broken.r3" },
                }
            },
            new ParentingScenario
            {
                Id = "child_death_question", AgeGroup = ChildAgeGroup.Child,
                DescriptionKey = "home.parenting.child_death_question.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.child_death_question.c1", SoulChange = 20, ResultKey = "home.parenting.child_death_question.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.child_death_question.c2", SoulChange = 5, ResultKey = "home.parenting.child_death_question.r2", IsNeutral = true },
                    new ParentingChoice { LabelKey = "home.parenting.child_death_question.c3", SoulChange = -20, ResultKey = "home.parenting.child_death_question.r3" },
                }
            },
        };

        // ──────────── TEEN SCENARIOS (ages 13-17) ────────────

        private static readonly ParentingScenario[] TeenScenarios = new[]
        {
            new ParentingScenario
            {
                Id = "teen_guard", AgeGroup = ChildAgeGroup.Teen,
                DescriptionKey = "home.parenting.teen_guard.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.teen_guard.c1", SoulChange = 20, ResultKey = "home.parenting.teen_guard.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.teen_guard.c2", SoulChange = 5, ResultKey = "home.parenting.teen_guard.r2", IsNeutral = true },
                    new ParentingChoice { LabelKey = "home.parenting.teen_guard.c3", SoulChange = -15, ResultKey = "home.parenting.teen_guard.r3" },
                }
            },
            new ParentingScenario
            {
                Id = "teen_romance", AgeGroup = ChildAgeGroup.Teen,
                DescriptionKey = "home.parenting.teen_romance.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.teen_romance.c1", SoulChange = 20, ResultKey = "home.parenting.teen_romance.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.teen_romance.c2", SoulChange = -15, ResultKey = "home.parenting.teen_romance.r2" },
                    new ParentingChoice { LabelKey = "home.parenting.teen_romance.c3", SoulChange = -20, ResultKey = "home.parenting.teen_romance.r3" },
                }
            },
            new ParentingScenario
            {
                Id = "teen_faith", AgeGroup = ChildAgeGroup.Teen,
                DescriptionKey = "home.parenting.teen_faith.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.teen_faith.c1", SoulChange = 20, ResultKey = "home.parenting.teen_faith.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.teen_faith.c2", SoulChange = -15, ResultKey = "home.parenting.teen_faith.r2" },
                    new ParentingChoice { LabelKey = "home.parenting.teen_faith.c3", SoulChange = 10, ResultKey = "home.parenting.teen_faith.r3", IsNeutral = true },
                }
            },
            new ParentingScenario
            {
                Id = "teen_drinking", AgeGroup = ChildAgeGroup.Teen,
                DescriptionKey = "home.parenting.teen_drinking.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.teen_drinking.c1", SoulChange = 15, ResultKey = "home.parenting.teen_drinking.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.teen_drinking.c2", SoulChange = -20, ResultKey = "home.parenting.teen_drinking.r2" },
                    new ParentingChoice { LabelKey = "home.parenting.teen_drinking.c3", SoulChange = -15, ResultKey = "home.parenting.teen_drinking.r3" },
                }
            },
            new ParentingScenario
            {
                Id = "teen_runaway", AgeGroup = ChildAgeGroup.Teen,
                DescriptionKey = "home.parenting.teen_runaway.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.teen_runaway.c1", SoulChange = 25, ResultKey = "home.parenting.teen_runaway.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.teen_runaway.c2", SoulChange = -20, ResultKey = "home.parenting.teen_runaway.r2" },
                    new ParentingChoice { LabelKey = "home.parenting.teen_runaway.c3", SoulChange = -10, ResultKey = "home.parenting.teen_runaway.r3" },
                }
            },
            new ParentingScenario
            {
                Id = "teen_dark_alley", AgeGroup = ChildAgeGroup.Teen,
                DescriptionKey = "home.parenting.teen_dark_alley.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.teen_dark_alley.c1", SoulChange = 20, ResultKey = "home.parenting.teen_dark_alley.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.teen_dark_alley.c2", SoulChange = 5, ResultKey = "home.parenting.teen_dark_alley.r2", IsNeutral = true },
                    new ParentingChoice { LabelKey = "home.parenting.teen_dark_alley.c3", SoulChange = -25, ResultKey = "home.parenting.teen_dark_alley.r3" },
                }
            },
            new ParentingScenario
            {
                Id = "teen_injustice", AgeGroup = ChildAgeGroup.Teen,
                DescriptionKey = "home.parenting.teen_injustice.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.teen_injustice.c1", SoulChange = 25, ResultKey = "home.parenting.teen_injustice.r1", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.teen_injustice.c2", SoulChange = -10, ResultKey = "home.parenting.teen_injustice.r2" },
                    new ParentingChoice { LabelKey = "home.parenting.teen_injustice.c3", SoulChange = -25, ResultKey = "home.parenting.teen_injustice.r3" },
                }
            },
            new ParentingScenario
            {
                Id = "teen_inheritance", AgeGroup = ChildAgeGroup.Teen,
                DescriptionKey = "home.parenting.teen_inheritance.desc",
                Choices = new[]
                {
                    new ParentingChoice { LabelKey = "home.parenting.teen_inheritance.c1", SoulChange = -10, ResultKey = "home.parenting.teen_inheritance.r1" },
                    new ParentingChoice { LabelKey = "home.parenting.teen_inheritance.c2", SoulChange = 20, ResultKey = "home.parenting.teen_inheritance.r2", IsVirtuous = true },
                    new ParentingChoice { LabelKey = "home.parenting.teen_inheritance.c3", SoulChange = -20, ResultKey = "home.parenting.teen_inheritance.r3" },
                }
            },
        };

        public static ParentingScenario[] AllScenarios { get; } =
            ToddlerScenarios.Concat(ChildScenarios).Concat(TeenScenarios).ToArray();
    }

    #endregion
}
