using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UsurperRemake.BBS;
using UsurperRemake.UI;
using UsurperRemake.Utils;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Companion System - Manages NPC companions who can join the player's party
    /// Handles recruitment, loyalty, romance, personal quests, and permanent death
    ///
    /// Companions:
    /// - Lyris: Tragic romance interest, connected to Old Gods
    /// - Aldric: Loyal shield, may sacrifice himself if player becomes too dark
    /// - Mira: Broken healer seeking meaning, sacrifices herself to save player
    /// - Vex: Trickster thief with hidden depth, dying of wasting disease
    /// </summary>
    public class CompanionSystem
    {
        private static CompanionSystem? _fallbackInstance;
        public static CompanionSystem Instance
        {
            get
            {
                var ctx = UsurperRemake.Server.SessionContext.Current;
                if (ctx != null) return ctx.Companions;
                return _fallbackInstance ??= new CompanionSystem();
            }
        }

        // All available companions
        private Dictionary<CompanionId, Companion> companions = new();

        // Currently active companions (max 4 in dungeon, same as party size)
        private List<CompanionId> activeCompanions = new();

        // Fallen companions (permanent death)
        private Dictionary<CompanionId, CompanionDeath> fallenCompanions = new();

        // Queued notifications for players (displayed next time they check status)
        private Queue<string> pendingNotifications = new();

        public const int MaxActiveCompanions = 4;

        public event Action<CompanionId>? OnCompanionRecruited;
        public event Action<CompanionId, DeathType>? OnCompanionDeath;
        public event Action<CompanionId>? OnCompanionRomanceAdvanced;
        public event Action<CompanionId>? OnCompanionQuestCompleted;

        public CompanionSystem()
        {
            _fallbackInstance = this;
            InitializeCompanions();
        }

        private void InitializeCompanions()
        {
            // ═══════════════════════════════════════════════════════════════
            // LYRIS - The Tragic Love Interest
            // ═══════════════════════════════════════════════════════════════
            companions[CompanionId.Lyris] = new Companion
            {
                Id = CompanionId.Lyris,
                Name = "Lyris",
                Title = "The Silent Arrow",
                Type = CompanionType.Romance,

                Description = "A traveler with silver-streaked hair and a habit of staring at you like she " +
                             "knows something you dont. Speaks in riddles. Knows too much.",

                BackstoryBrief = "Lyris once served as a warden of the Deepwood, an ancient forest that bordered " +
                                "the Old Gods' domain. When Manwe's corruption spread, the Deepwood withered and " +
                                "its creatures twisted into monsters. Lyris alone survived, driven underground " +
                                "into the dungeon, tracking the source of the corruption.",

                RecruitLevel = 15,
                RecruitLocation = "Dungeon Level 15 - Forgotten Shrine",

                BaseStats = new CompanionStats
                {
                    HP = 220,
                    Attack = 30,
                    Defense = 18,
                    MagicPower = 10,
                    Speed = 40,
                    HealingPower = 15
                },

                CombatRole = CombatRole.Damage,
                Abilities = new[] { "Precise Shot", "Hunter's Mark", "Nature's Blessing", "Camouflage" },

                PersonalQuestName = "The Deepwood's Heart",
                PersonalQuestDescription = "Help Lyris recover a seed from the Deepwood's mother-tree, buried deep in the dungeon.",
                PersonalQuestLocationHint = "Dungeon floors 80-90",

                RomanceAvailable = true,
                CanDiePermanently = true,

                DeathTriggers = new Dictionary<DeathType, string>
                {
                    [DeathType.Sacrifice] = "Lyris may sacrifice herself to save you from a killing blow",
                    [DeathType.ChoiceBased] = "You may be forced to choose between Lyris and saving Veloura",
                    [DeathType.Combat] = "Lyris can die in combat if not protected"
                },

                DialogueHints = new[]
                {
                    "I feel like Ive seen you before. Somewhere.",
                    "You remind me of someone. Cant place it though.",
                    "You ever get the feeling youve forgotten something big?"
                },

                OceanPhilosophyAwareness = 4 // She senses the player's true nature early
            };

            // ═══════════════════════════════════════════════════════════════
            // ALDRIC - The Loyal Shield
            // ═══════════════════════════════════════════════════════════════
            companions[CompanionId.Aldric] = new Companion
            {
                Id = CompanionId.Aldric,
                Name = "Aldric",
                Title = "The Unbroken Shield",
                Type = CompanionType.Combat,

                Description = "A scarred old soldier who doesnt talk much about where the scars came from. " +
                             "Lost his whole unit once. Still hasnt forgiven himself.",

                BackstoryBrief = "Once the captain of the King's Guard, Aldric lost his entire unit to a demonic " +
                                "incursion he blames himself for. He now wanders, seeking redemption through " +
                                "protecting those who need it most.",

                RecruitLevel = 10,
                RecruitLocation = "Tavern - After defending you from bandits",

                BaseStats = new CompanionStats
                {
                    HP = 350,
                    Attack = 45,
                    Defense = 40,
                    MagicPower = 5,
                    Speed = 20,
                    HealingPower = 0
                },

                CombatRole = CombatRole.Tank,
                Abilities = new[] { "Power Strike", "Shield Wall", "Battle Cry", "Last Stand" },

                PersonalQuestName = "Ghosts of the Guard",
                PersonalQuestDescription = "Help Aldric find closure by confronting the demon that killed his unit.",
                PersonalQuestLocationHint = "Dungeon floors 55-65 (demonic territory)",

                RomanceAvailable = false,
                CanDiePermanently = true,

                DeathTriggers = new Dictionary<DeathType, string>
                {
                    [DeathType.MoralTrigger] = "If your Darkness exceeds 5000 AND loyalty > 80, he will turn against you",
                    [DeathType.Sacrifice] = "Will throw himself in front of any attack targeting you",
                    [DeathType.Combat] = "Can die in combat, especially if protecting you"
                },

                DialogueHints = new[]
                {
                    "I let people down before. Not again.",
                    "Winning doesnt matter if nobody survives.",
                    "You sure we're on the right side of this?"
                },

                LoyaltyThreshold = 80, // High loyalty required for moral trigger death
                DarknessThreshold = 5000 // Darkness level that triggers confrontation
            };

            // ═══════════════════════════════════════════════════════════════
            // MIRA - The Broken Healer
            // ═══════════════════════════════════════════════════════════════
            companions[CompanionId.Mira] = new Companion
            {
                Id = CompanionId.Mira,
                Name = "Mira",
                Title = "The Faded Light",
                Type = CompanionType.Support,

                Description = "A former priestess whose faith shattered when her temple was destroyed. " +
                             "Her healing powers remain, but her spirit is hollow. She helps others because " +
                             "she no longer knows what else to do.",

                BackstoryBrief = "Mira devoted her life to healing in Veloura's temple. When Veloura was corrupted, " +
                                "the temple turned on itself - healers became reapers. Mira escaped, but left her " +
                                "faith behind. She seeks to understand if healing still has meaning.",

                RecruitLevel = 20,
                RecruitLocation = "Temple Ruins - Found praying to an empty altar",

                BaseStats = new CompanionStats
                {
                    HP = 180,
                    Attack = 10,
                    Defense = 12,
                    MagicPower = 35,
                    Speed = 25,
                    HealingPower = 60
                },

                CombatRole = CombatRole.Healer,
                Abilities = new[] { "Prayer of Mending", "Holy Smite", "Sanctuary", "Greater Heal" },

                PersonalQuestName = "The Meaning of Mercy",
                PersonalQuestDescription = "Help Mira decide if healing is worth continuing, culminating in a choice.",
                PersonalQuestLocationHint = "Dungeon floors 40-50 (where suffering is greatest)",

                RomanceAvailable = false,
                CanDiePermanently = true,

                DeathTriggers = new Dictionary<DeathType, string>
                {
                    [DeathType.Sacrifice] = "Will sacrifice herself to cure you of a fatal curse",
                    [DeathType.QuestRelated] = "May die completing her personal quest, finding meaning in the act",
                    [DeathType.Inevitable] = "Her story arc leads toward sacrifice - it cannot be prevented"
                },

                DialogueHints = new[]
                {
                    "I just heal people. Its all I know how to do.",
                    "Some people cant be saved. Took me a while to learn that.",
                    "Does it even matter? Saving one person?"
                },

                TeachesLettingGo = true // Her arc is about acceptance
            };

            // ═══════════════════════════════════════════════════════════════
            // VEX - The Trickster
            // ═══════════════════════════════════════════════════════════════
            companions[CompanionId.Vex] = new Companion
            {
                Id = CompanionId.Vex,
                Name = "Vex",
                Title = "The Laughing Shadow",
                Type = CompanionType.Utility,

                Description = "A thief with fast hands and a faster mouth. Jokes about everything, " +
                             "even dying. Hes been sick his whole life and he doesnt care who knows it.",

                BackstoryBrief = "Vex was born with a wasting disease - he's been dying his whole life, one day " +
                                "at a time. Rather than despair, he chose to laugh. He steals to survive, jokes " +
                                "to cope, and refuses to take anything seriously - including his own mortality.",

                RecruitLevel = 25,
                RecruitLocation = "Prison - Helps you escape, decides to tag along",

                BaseStats = new CompanionStats
                {
                    HP = 150,
                    Attack = 35,
                    Defense = 15,
                    MagicPower = 10,
                    Speed = 50,
                    HealingPower = 0
                },

                CombatRole = CombatRole.Damage,
                Abilities = new[] { "Backstab", "Poison Blade", "Shadow Step", "Death Mark" },

                PersonalQuestName = "One More Sunrise",
                PersonalQuestDescription = "Help Vex accomplish everything on his 'before I die' list.",
                PersonalQuestLocationHint = "Any dungeon floor (after 10+ days together)",

                RomanceAvailable = false,
                CanDiePermanently = true,

                DeathTriggers = new Dictionary<DeathType, string>
                {
                    [DeathType.Inevitable] = "The disease WILL claim him - this cannot be prevented",
                    [DeathType.Sacrifice] = "May choose to go out fighting instead of fading away",
                    [DeathType.TimeBased] = "After ~30 in-game days with him, symptoms worsen"
                },

                DialogueHints = new[]
                {
                    "Life's too short to worry about it. Literally, in my case.",
                    "Why not? Could be dead tomorrow. Probably will be.",
                    "Everybody dies. I just know roughly when."
                },

                HasTimedDeath = true,
                DaysUntilDeath = 30, // Approximately 30 in-game days
                TeachesAcceptance = true
            };

            // ═══════════════════════════════════════════════════════════════
            // MELODIA - The Songweaver (Music Shop companion)
            // ═══════════════════════════════════════════════════════════════
            companions[CompanionId.Melodia] = new Companion
            {
                Id = CompanionId.Melodia,
                Name = "Melodia",
                Title = "The Songweaver",
                Type = CompanionType.Support,

                Description = "A master bard who runs the Music Shop. Her songs can heal wounds, " +
                             "inspire courage, and some say even stir the dreams of sleeping gods.",

                BackstoryBrief = "Melodia once traveled with a legendary adventuring party, chronicling " +
                                "their deeds in song. When they fell one by one to the dungeon's depths, " +
                                "she opened a shop in town — but the call of adventure never truly faded. " +
                                "She knows more about the Old Gods than she lets on.",

                RecruitLevel = 20,
                RecruitLocation = "Music Shop — after sharing your story",

                BaseStats = new CompanionStats
                {
                    HP = 200,
                    Attack = 20,
                    Defense = 18,
                    MagicPower = 45,
                    Speed = 30,
                    HealingPower = 40
                },

                CombatRole = CombatRole.Bard,
                Abilities = new[] { "Healing Melody", "War March", "Lullaby of Iron", "Battle Hymn" },

                PersonalQuestName = "The Lost Opus",
                PersonalQuestDescription = "Help Melodia recover a legendary musical score from the dungeon depths.",
                PersonalQuestLocationHint = "Dungeon floors 50-60 (ancient music chamber)",

                RomanceAvailable = true,
                CanDiePermanently = true,

                DeathTriggers = new Dictionary<DeathType, string>
                {
                    [DeathType.Sacrifice] = "Shields you with a final, impossibly beautiful song that echoes long after she falls",
                    [DeathType.Combat] = "Can fall in battle, her last notes lingering in the air"
                },

                DialogueHints = new[]
                {
                    "Every song tells a truth the singer doesn't know yet.",
                    "I've played for kings and beggars. The beggars listen better.",
                    "Music is the only honest language.",
                    "Some melodies haunt you because they're trying to tell you something."
                },

                OceanPhilosophyAwareness = 2,
                TeachesLettingGo = true
            };

        }

        #region Companion Management

        /// <summary>
        /// Get a companion by ID
        /// </summary>
        public Companion? GetCompanion(CompanionId id)
        {
            return companions.TryGetValue(id, out var companion) ? companion : null;
        }

        /// <summary>
        /// Get all companions
        /// </summary>
        public IEnumerable<Companion> GetAllCompanions()
        {
            return companions.Values;
        }

        /// <summary>
        /// Get companions that can be recruited at the player's current level
        /// </summary>
        public IEnumerable<Companion> GetRecruitableCompanions(int playerLevel)
        {
            return companions.Values
                .Where(c => !c.IsRecruited && !c.IsDead && playerLevel >= c.RecruitLevel);
        }

        /// <summary>
        /// Get currently active companions
        /// </summary>
        public IEnumerable<Companion> GetActiveCompanions()
        {
            return activeCompanions
                .Select(id => companions.TryGetValue(id, out var c) ? c : null)
                .Where(c => c != null && !c.IsDead)!;
        }

        /// <summary>
        /// Get the IDs of currently active companions.
        /// </summary>
        public List<CompanionId> GetActiveCompanionIds()
        {
            return activeCompanions.ToList();
        }

        /// <summary>
        /// Get all recruited companions (active or not, but alive)
        /// </summary>
        public IEnumerable<Companion> GetRecruitedCompanions()
        {
            return companions.Values.Where(c => c.IsRecruited && !c.IsDead);
        }

        /// <summary>
        /// Get inactive companions (recruited but not currently in active party)
        /// </summary>
        public IEnumerable<Companion> GetInactiveCompanions()
        {
            return companions.Values.Where(c => c.IsRecruited && !c.IsDead && !c.IsActive);
        }

        /// <summary>
        /// Get fallen (dead) companions
        /// </summary>
        public IEnumerable<(Companion Companion, CompanionDeath Death)> GetFallenCompanions()
        {
            foreach (var kvp in fallenCompanions)
            {
                if (companions.TryGetValue(kvp.Key, out var companion))
                {
                    yield return (companion, kvp.Value);
                }
            }
        }

        /// <summary>
        /// Queue a notification when a companion's personal quest becomes available
        /// </summary>
        private void QueueQuestUnlockNotification(Companion companion)
        {
            string notification = $"[COMPANION] {companion.Name}'s personal quest '{companion.PersonalQuestName}' is now available!\n" +
                                  $"            Visit the Inn and talk to {companion.Name} to begin.\n" +
                                  $"            Location: {companion.PersonalQuestLocationHint}";
            pendingNotifications.Enqueue(notification);
        }

        /// <summary>
        /// Check if there are pending notifications
        /// </summary>
        public bool HasPendingNotifications => pendingNotifications.Count > 0;

        /// <summary>
        /// Get and clear all pending notifications
        /// </summary>
        public List<string> GetAndClearNotifications()
        {
            var notifications = pendingNotifications.ToList();
            pendingNotifications.Clear();
            return notifications;
        }

        /// <summary>
        /// Recruit a companion
        /// </summary>
        public async Task<bool> RecruitCompanion(CompanionId id, Character player, TerminalEmulator terminal)
        {
            if (!companions.TryGetValue(id, out var companion))
                return false;

            if (companion.IsRecruited || companion.IsDead)
                return false;

            if (player.Level < companion.RecruitLevel)
            {
                terminal.WriteLine(Loc.Get("companion.not_ready", companion.Name), "yellow");
                return false;
            }

            // Display recruitment scene
            await DisplayRecruitmentScene(companion, terminal);

            companion.IsRecruited = true;
            companion.RecruitedDay = GetGameDay();
            companion.RecruitedDate = DateTime.UtcNow;

            // Initialize companion's level and scale stats
            InitializeCompanionLevel(companion);

            // Auto-add to active if room
            if (activeCompanions.Count < MaxActiveCompanions)
            {
                activeCompanions.Add(id);
                companion.IsActive = true;
                terminal.WriteLine(Loc.Get("companion.joins_party", companion.Name), "bright_green");
            }
            else
            {
                terminal.WriteLine(Loc.Get("companion.waits_tavern", companion.Name), "cyan");
            }

            OnCompanionRecruited?.Invoke(id);

            // Log companion recruitment
            DebugLogger.Instance.LogCompanionRecruit(companion.Name, player.Level);

            // Track for Ocean Philosophy
            OceanPhilosophySystem.Instance.ExperienceMoment(AwakeningMoment.SacrificedForAnother);

            // Auto-save after recruiting a companion - this is a major milestone
            await SaveSystem.Instance.AutoSave(player);

            return true;
        }

        /// <summary>
        /// Check if a companion has been recruited
        /// </summary>
        public bool IsCompanionRecruited(CompanionId id)
        {
            return companions.TryGetValue(id, out var c) && c.IsRecruited;
        }

        /// <summary>
        /// Check if a companion is alive
        /// </summary>
        public bool IsCompanionAlive(CompanionId id)
        {
            return companions.TryGetValue(id, out var c) && !c.IsDead;
        }

        /// <summary>
        /// Check if a companion is recruited, alive, and currently in the active party
        /// </summary>
        public bool IsCompanionActive(CompanionId id)
        {
            return activeCompanions.Contains(id) &&
                   companions.TryGetValue(id, out var c) && c.IsRecruited && !c.IsDead;
        }

        /// <summary>
        /// Set active companions for dungeon
        /// </summary>
        public bool SetActiveCompanions(List<CompanionId> companionIds)
        {
            if (companionIds.Count > MaxActiveCompanions)
                return false;

            foreach (var id in companionIds)
            {
                if (!companions.TryGetValue(id, out var c) || !c.IsRecruited || c.IsDead)
                    return false;
            }

            // Deactivate current
            foreach (var id in activeCompanions)
            {
                if (companions.TryGetValue(id, out var c))
                    c.IsActive = false;
            }

            // Activate new
            activeCompanions.Clear();
            activeCompanions.AddRange(companionIds);

            foreach (var id in companionIds)
            {
                if (companions.TryGetValue(id, out var c))
                    c.IsActive = true;
            }

            return true;
        }

        /// <summary>
        /// Deactivate a single companion (remove from active party but keep recruited)
        /// </summary>
        public bool DeactivateCompanion(CompanionId id)
        {
            if (!companions.TryGetValue(id, out var companion))
                return false;

            if (!companion.IsRecruited || companion.IsDead)
                return false;

            companion.IsActive = false;
            activeCompanions.Remove(id);
            return true;
        }

        /// <summary>
        /// Activate a single companion (add to active party if room and recruited)
        /// </summary>
        public bool ActivateCompanion(CompanionId id)
        {
            if (!companions.TryGetValue(id, out var companion))
                return false;

            if (!companion.IsRecruited || companion.IsDead)
                return false;

            if (activeCompanions.Count >= MaxActiveCompanions)
                return false;

            if (!activeCompanions.Contains(id))
            {
                activeCompanions.Add(id);
                companion.IsActive = true;
            }
            return true;
        }

        #endregion

        #region Combat Integration

        // Track companion HP during combat (companions use BaseStats.HP as max, this tracks current)
        private Dictionary<CompanionId, int> companionCurrentHP = new();

        /// <summary>
        /// Get active companions as Character objects for the combat system
        /// Creates lightweight Character wrappers that the CombatEngine can use
        /// </summary>
        public List<Character> GetCompanionsAsCharacters()
        {
            var result = new List<Character>();

            foreach (var companion in GetActiveCompanions())
            {
                if (companion == null || companion.IsDead) continue;

                var charWrapper = new Character
                {
                    Name2 = companion.Name,
                    Level = companion.Level,
                    Healing = companion.HealingPotions,
                    ManaPotions = companion.ManaPotions,
                    // Map companion combat role to a character class for abilities/equipment.
                    // Lyris is special: she uses CombatRole.Damage for stat growth but is a Ranger.
                    Class = companion.Id == CompanionId.Lyris ? CharacterClass.Ranger :
                    companion.CombatRole switch
                    {
                        CombatRole.Tank => CharacterClass.Warrior,
                        CombatRole.Healer => CharacterClass.Cleric,
                        CombatRole.Damage => CharacterClass.Assassin,
                        CombatRole.Hybrid => CharacterClass.Ranger,
                        CombatRole.Bard => CharacterClass.Bard,
                        _ => CharacterClass.Warrior
                    },
                    // Initialize Base* fields for RecalculateStats — use secondary stats if scaled
                    BaseStrength = companion.BaseStats.Attack,
                    BaseDefence = companion.BaseStats.Defense,
                    BaseDexterity = Math.Max(companion.BaseStats.Speed, companion.Dexterity),
                    BaseAgility = Math.Max(companion.BaseStats.Speed, companion.Agility),
                    BaseIntelligence = Math.Max(companion.BaseStats.MagicPower, companion.Intelligence),
                    BaseWisdom = Math.Max(companion.BaseStats.HealingPower, companion.Wisdom),
                    BaseCharisma = Math.Max(10, companion.Charisma),
                    BaseConstitution = Math.Max(10 + companion.Level, companion.Constitution),
                    BaseStamina = 10 + companion.Level,
                    BaseMaxHP = companion.BaseStats.HP,
                    BaseMaxMana = companion.BaseStats.MagicPower * 5
                };

                // Copy companion's equipment to the character wrapper
                if (companion.EquippedItems.Count > 0)
                {
                    foreach (var kvp in companion.EquippedItems)
                        charWrapper.EquippedItems[kvp.Key] = kvp.Value;
                }

                // Share inventory by reference — edits to the wrapper's Inventory (combat [T]
                // transfer, Home/Team Corner/dungeon viewer take-back) mutate the companion's
                // persistent bag, so items survive save/reload.
                charWrapper.Inventory = companion.Inventory;

                // RecalculateStats applies equipment bonuses on top of Base* values
                charWrapper.RecalculateStats();

                // Initialize HP if needed — AFTER RecalculateStats so MaxHP includes equipment bonuses
                if (!companionCurrentHP.ContainsKey(companion.Id))
                {
                    companionCurrentHP[companion.Id] = (int)charWrapper.MaxHP;
                }

                // Restore tracked combat HP (don't heal to full mid-combat)
                charWrapper.HP = companionCurrentHP[companion.Id];
                charWrapper.Mana = companion.BaseStats.MagicPower * 5;

                // Store companion ID in the character for tracking
                charWrapper.CompanionId = companion.Id;
                charWrapper.IsCompanion = true;

                // Copy proficiency from Companion to Character wrapper
                if (companion.SkillProficiencies.Count > 0)
                {
                    foreach (var kvp in companion.SkillProficiencies)
                        charWrapper.SkillProficiencies[kvp.Key] = (TrainingSystem.ProficiencyLevel)kvp.Value;
                }
                if (companion.SkillTrainingProgress.Count > 0)
                {
                    foreach (var kvp in companion.SkillTrainingProgress)
                        charWrapper.SkillTrainingProgress[kvp.Key] = kvp.Value;
                }

                result.Add(charWrapper);
            }

            return result;
        }

        /// <summary>
        /// Apply damage to a companion (called from CombatEngine)
        /// Returns true if companion died
        /// </summary>
        public bool DamageCompanion(CompanionId id, int damage, out bool triggeredSacrifice)
        {
            triggeredSacrifice = false;

            if (!companions.TryGetValue(id, out var companion) || companion.IsDead)
                return false;

            if (!companionCurrentHP.ContainsKey(id))
                companionCurrentHP[id] = GetCompanionMaxHP(companion);

            companionCurrentHP[id] = Math.Max(0, companionCurrentHP[id] - damage);

            if (companionCurrentHP[id] <= 0)
            {
                // Companion would die from combat
                return true;
            }

            return false;
        }

        /// <summary>
        /// Heal a companion
        /// </summary>
        public void HealCompanion(CompanionId id, int amount)
        {
            if (!companions.TryGetValue(id, out var companion) || companion.IsDead)
                return;

            if (!companionCurrentHP.ContainsKey(id))
                companionCurrentHP[id] = GetCompanionMaxHP(companion);

            int maxHP = GetCompanionMaxHP(companion);
            companionCurrentHP[id] = Math.Min(maxHP, companionCurrentHP[id] + amount);
        }

        /// <summary>
        /// Get current HP for a companion
        /// </summary>
        public int GetCompanionHP(CompanionId id)
        {
            if (!companionCurrentHP.ContainsKey(id))
            {
                if (companions.TryGetValue(id, out var c))
                    companionCurrentHP[id] = GetCompanionMaxHP(c);
                else
                    return 0;
            }
            return companionCurrentHP[id];
        }

        /// <summary>
        /// Get the actual MaxHP for a companion including equipment bonuses.
        /// BaseStats.HP is the unequipped base — this builds a temporary Character wrapper
        /// with equipment and RecalculateStats() to get the true MaxHP.
        /// </summary>
        public int GetCompanionMaxHP(Companion companion)
        {
            if (companion == null) return 0;

            // If companion has no equipment, base HP is accurate
            if (companion.EquippedItems.Count == 0)
                return companion.BaseStats.HP;

            // Build a temporary wrapper to calculate equipment bonuses
            var temp = new Character
            {
                BaseMaxHP = companion.BaseStats.HP,
                BaseConstitution = Math.Max(10 + companion.Level, companion.Constitution),
                Level = companion.Level
            };
            foreach (var kvp in companion.EquippedItems)
                temp.EquippedItems[kvp.Key] = kvp.Value;
            temp.RecalculateStats();
            return (int)temp.MaxHP;
        }

        /// <summary>
        /// Overload that looks up companion by ID
        /// </summary>
        public int GetCompanionMaxHP(CompanionId id)
        {
            if (companions.TryGetValue(id, out var companion))
                return GetCompanionMaxHP(companion);
            return 0;
        }

        /// <summary>
        /// Restore all active companions to full HP (after dungeon exit, rest, etc.)
        /// </summary>
        public void RestoreCompanionHP()
        {
            foreach (var companion in GetActiveCompanions())
            {
                if (companion != null && !companion.IsDead)
                {
                    companionCurrentHP[companion.Id] = GetCompanionMaxHP(companion);
                }
            }
        }

        /// <summary>
        /// Sync companion HP from Character wrapper after combat
        /// </summary>
        public void SyncCompanionHP(Character charWrapper)
        {
            if (charWrapper.IsCompanion && charWrapper.CompanionId.HasValue)
            {
                companionCurrentHP[charWrapper.CompanionId.Value] = (int)charWrapper.HP;
            }
        }

        /// <summary>
        /// Sync companion potions from Character wrapper after combat
        /// </summary>
        public void SyncCompanionPotions(Character charWrapper)
        {
            if (charWrapper.IsCompanion && charWrapper.CompanionId.HasValue)
            {
                if (companions.TryGetValue(charWrapper.CompanionId.Value, out var companion))
                {
                    companion.HealingPotions = (int)charWrapper.Healing;
                    companion.ManaPotions = (int)charWrapper.ManaPotions;
                }
            }
        }

        /// <summary>
        /// Sync all companion state from Character wrappers after combat
        /// </summary>
        public void SyncCompanionState(Character charWrapper)
        {
            SyncCompanionHP(charWrapper);
            SyncCompanionPotions(charWrapper);
            SyncCompanionEquipment(charWrapper);
            SyncCompanionProficiency(charWrapper);
        }

        /// <summary>
        /// Sync companion proficiency from Character wrapper back to Companion object
        /// </summary>
        private void SyncCompanionProficiency(Character charWrapper)
        {
            if (charWrapper.IsCompanion && charWrapper.CompanionId.HasValue)
            {
                if (companions.TryGetValue(charWrapper.CompanionId.Value, out var companion))
                {
                    companion.SkillProficiencies = charWrapper.SkillProficiencies.ToDictionary(
                        kvp => kvp.Key, kvp => (int)kvp.Value);
                    companion.SkillTrainingProgress = new Dictionary<string, int>(charWrapper.SkillTrainingProgress);
                }
            }
        }

        /// <summary>
        /// Sync companion equipment from Character wrapper after combat/loot pickup
        /// </summary>
        public void SyncCompanionEquipment(Character charWrapper)
        {
            if (charWrapper.IsCompanion && charWrapper.CompanionId.HasValue)
            {
                if (companions.TryGetValue(charWrapper.CompanionId.Value, out var companion))
                {
                    // Log equipment changes for debugging companion equipment reversion issues
                    var oldEquip = new Dictionary<EquipmentSlot, int>(companion.EquippedItems);
                    companion.EquippedItems.Clear();
                    foreach (var kvp in charWrapper.EquippedItems)
                    {
                        companion.EquippedItems[kvp.Key] = kvp.Value;
                    }

                    // Detect and log any equipment differences
                    foreach (var kvp in companion.EquippedItems)
                    {
                        if (!oldEquip.TryGetValue(kvp.Key, out int oldId) || oldId != kvp.Value)
                        {
                            DebugLogger.Instance.LogInfo("COMPANION_EQUIP",
                                $"SyncEquipment {companion.Name}: slot {kvp.Key} changed {(oldEquip.ContainsKey(kvp.Key) ? oldEquip[kvp.Key].ToString() : "empty")} -> {kvp.Value}");
                        }
                    }
                    foreach (var kvp in oldEquip)
                    {
                        if (!companion.EquippedItems.ContainsKey(kvp.Key))
                        {
                            DebugLogger.Instance.LogInfo("COMPANION_EQUIP",
                                $"SyncEquipment {companion.Name}: slot {kvp.Key} removed (was {kvp.Value})");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Check if any companion can sacrifice to save the player
        /// Returns the companion willing to sacrifice, or null
        /// </summary>
        public Companion? CheckForSacrifice(Character player, int incomingDamage)
        {
            // Only trigger if damage would kill the player
            if (player.HP - incomingDamage > 0)
                return null;

            foreach (var companion in GetActiveCompanions())
            {
                if (companion == null || companion.IsDead) continue;

                // Check if companion has Sacrifice ability
                bool canSacrifice = companion.Id == CompanionId.Aldric || companion.LoyaltyLevel >= 90;
                if (!canSacrifice) continue;

                // Check if companion has enough loyalty/trust to sacrifice
                // Higher loyalty = more likely to sacrifice
                int sacrificeChance = companion.LoyaltyLevel;

                // Aldric always sacrifices if loyalty > 50 (his nature)
                if (companion.Id == CompanionId.Aldric && companion.LoyaltyLevel > 50)
                    sacrificeChance = 100;

                // Romance increases sacrifice chance
                if (companion.RomanceLevel > 5)
                    sacrificeChance += 30;

                var random = Random.Shared;
                if (random.Next(100) < sacrificeChance)
                {
                    return companion;
                }
            }

            return null;
        }

        #endregion

        #region Relationship Management

        /// <summary>
        /// Modify loyalty for a companion
        /// Loyalty gains are affected by difficulty: Easy = faster, Hard/Nightmare = slower
        /// </summary>
        public void ModifyLoyalty(CompanionId id, int amount, string reason = "")
        {
            if (!companions.TryGetValue(id, out var companion))
                return;

            // Apply difficulty multiplier to positive loyalty changes
            int adjustedAmount = amount > 0
                ? DifficultySystem.ApplyCompanionLoyaltyMultiplier(amount)
                : amount; // Negative changes (loyalty loss) are not affected

            int previousLoyalty = companion.LoyaltyLevel;
            companion.LoyaltyLevel = Math.Clamp(companion.LoyaltyLevel + adjustedAmount, 0, 100);

            if (!string.IsNullOrEmpty(reason))
            {
                companion.AddHistory(new CompanionEvent
                {
                    Type = CompanionEventType.LoyaltyChange,
                    Description = reason,
                    LoyaltyChange = amount,
                    Timestamp = DateTime.Now
                });
            }

            // Notify when personal quest becomes available at loyalty 50
            // Quest is NOT auto-started - player must talk to companion at Inn
            if (previousLoyalty < 50 && companion.LoyaltyLevel >= 50 &&
                !companion.PersonalQuestStarted && !companion.PersonalQuestCompleted)
            {
                companion.PersonalQuestAvailable = true;
                // DebugLogger: {companion.Name}'s personal quest unlocked: {companion.PersonalQuestName}

                // Queue a notification for the player
                QueueQuestUnlockNotification(companion);
            }
        }

        /// <summary>
        /// Modify trust for a companion
        /// </summary>
        public void ModifyTrust(CompanionId id, int amount, string reason = "")
        {
            if (!companions.TryGetValue(id, out var companion))
                return;

            companion.TrustLevel = Math.Clamp(companion.TrustLevel + amount, 0, 100);
        }

        /// <summary>
        /// Advance romance with a companion (if available)
        /// </summary>
        public bool AdvanceRomance(CompanionId id, int amount = 1)
        {
            if (!companions.TryGetValue(id, out var companion))
                return false;

            if (!companion.RomanceAvailable)
                return false;

            // Already at max romance
            if (companion.RomanceLevel >= 10)
                return false;

            companion.RomanceLevel = Math.Clamp(companion.RomanceLevel + amount, 0, 10);

            companion.AddHistory(new CompanionEvent
            {
                Type = CompanionEventType.RomanceAdvanced,
                Description = GetRomanceMilestone(companion.RomanceLevel),
                Timestamp = DateTime.Now
            });

            OnCompanionRomanceAdvanced?.Invoke(id);
            return true;
        }

        private string GetRomanceMilestone(int level)
        {
            return level switch
            {
                1 => "You caught her looking at you",
                2 => "She told you something personal",
                3 => "Getting comfortable around each other",
                4 => "She smiled when she saw you",
                5 => "One of you said it first",
                6 => "Made a promise",
                7 => "Cant imagine the road without her",
                8 => "Always together",
                9 => "The real thing",
                10 => "Til death do you part",
                _ => "Unknown milestone"
            };
        }

        #endregion

        #region Death System

        /// <summary>
        /// Kill a companion permanently
        /// </summary>
        public async Task KillCompanion(CompanionId id, DeathType type, string circumstance, TerminalEmulator terminal)
        {
            if (!companions.TryGetValue(id, out var companion))
                return;

            if (companion.IsDead)
                return;

            // Display death scene
            await DisplayDeathScene(companion, type, circumstance, terminal);

            // Moment of silence — let the weight of loss sink in
            await UsurperRemake.UI.UIHelper.MomentOfSilence(terminal, 5000);

            companion.IsDead = true;
            companion.IsActive = false;
            companion.DeathType = type;

            // Return companion's equipment AND bag contents to player inventory
            ReturnCompanionGearOnDeath(companion, terminal);

            activeCompanions.Remove(id);

            fallenCompanions[id] = new CompanionDeath
            {
                CompanionId = id,
                Type = type,
                Circumstance = circumstance,
                LastWords = GetLastWords(companion, type),
                DeathDay = GetGameDay(),
                PlayerLevel = GetPlayerLevel()
            };

            // Trigger grief system
            GriefSystem.Instance.BeginGrief(id, companion.Name, type);

            if (terminal != null)
            {
                terminal.SetColor("dark_magenta");
                terminal.WriteLine(Loc.Get("companion.grief_onset"));
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("companion.grief_combat_warning"));
                terminal.WriteLine("");
            }

            // Trigger Ocean Philosophy awakening
            if (!OceanPhilosophySystem.Instance.ExperiencedMoments.Contains(AwakeningMoment.FirstCompanionDeath))
            {
                OceanPhilosophySystem.Instance.ExperienceMoment(AwakeningMoment.FirstCompanionDeath);
            }

            OnCompanionDeath?.Invoke(id, type);

            // Queue Stranger encounter for companion death
            StrangerEncounterSystem.Instance.QueueScriptedEncounter(ScriptedEncounterType.AfterCompanionDeath);
            StrangerEncounterSystem.Instance.RecordGameEvent(StrangerContextEvent.CompanionDied);

            // Prevent save cheesing — persist negative outcomes immediately
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
                _ = GameEngine.Instance.SaveCurrentGame();
        }

        private string GetLastWords(Companion companion, DeathType type)
        {
            return (companion.Id, type) switch
            {
                (CompanionId.Lyris, DeathType.Sacrifice) =>
                    "I knew... when I met you... I knew this is how it ends...",

                (CompanionId.Lyris, DeathType.ChoiceBased) =>
                    "Tell Aurelion... I tried...",

                (CompanionId.Aldric, DeathType.MoralTrigger) =>
                    "I wont... let you... not like this...",

                (CompanionId.Aldric, DeathType.Sacrifice) =>
                    "Got em... this time I... got em...",

                (CompanionId.Mira, DeathType.Sacrifice) =>
                    "One more... let me heal... one more...",

                (CompanionId.Mira, DeathType.QuestRelated) =>
                    "It mattered... right? Tell me it mattered...",

                (CompanionId.Vex, DeathType.Inevitable) =>
                    "Heh... beat the schedule... by a few hours... not bad...",

                (CompanionId.Vex, DeathType.Sacrifice) =>
                    "Always wanted... to go out... on a good joke... was it funny...?",

                _ => "Hey... not bad... for a... last day..."
            };
        }

        /// <summary>
        /// Check if any companions should trigger their death conditions
        /// </summary>
        public DeathTriggerCheck CheckDeathTriggers(Character player)
        {
            var result = new DeathTriggerCheck();

            // Check Aldric's moral trigger
            var aldric = GetCompanion(CompanionId.Aldric);
            if (aldric != null && aldric.IsRecruited && !aldric.IsDead)
            {
                if (player.Darkness >= aldric.DarknessThreshold && aldric.LoyaltyLevel >= aldric.LoyaltyThreshold)
                {
                    result.TriggeredCompanion = CompanionId.Aldric;
                    result.TriggerType = DeathType.MoralTrigger;
                    result.TriggerReason = "Your darkness has grown too great. Aldric cannot stand by.";
                }
            }

            // Check Vex's timed death
            var vex = GetCompanion(CompanionId.Vex);
            if (vex != null && vex.IsRecruited && !vex.IsDead && vex.HasTimedDeath)
            {
                int daysWithVex = vex.RecruitedDate != DateTime.MinValue
                    ? Math.Max(0, (int)(DateTime.UtcNow - vex.RecruitedDate).TotalDays)
                    : Math.Max(0, GetGameDay() - vex.RecruitedDay);
                if (daysWithVex >= vex.DaysUntilDeath)
                {
                    result.TriggeredCompanion = CompanionId.Vex;
                    result.TriggerType = DeathType.Inevitable;
                    result.TriggerReason = "The disease has finally claimed Vex.";
                }
            }

            return result;
        }

        #endregion

        #region Personal Quests

        /// <summary>
        /// Start a companion's personal quest
        /// </summary>
        public bool StartPersonalQuest(CompanionId id)
        {
            if (!companions.TryGetValue(id, out var companion))
                return false;

            if (companion.PersonalQuestStarted || companion.PersonalQuestCompleted)
                return false;

            if (companion.LoyaltyLevel < 50)
                return false; // Need sufficient loyalty

            companion.PersonalQuestStarted = true;
            companion.AddHistory(new CompanionEvent
            {
                Type = CompanionEventType.QuestStarted,
                Description = $"Began personal quest: {companion.PersonalQuestName}",
                Timestamp = DateTime.Now
            });

            return true;
        }

        /// <summary>
        /// Complete a companion's personal quest
        /// </summary>
        public void CompletePersonalQuest(CompanionId id, bool success)
        {
            if (!companions.TryGetValue(id, out var companion))
                return;

            // On failure, reset quest state to allow retry; on success, mark complete
            if (!success)
            {
                companion.PersonalQuestStarted = false;
                companion.PersonalQuestCompleted = false;
                companion.PersonalQuestSuccess = false;
            }
            else
            {
                companion.PersonalQuestCompleted = true;
                companion.PersonalQuestSuccess = true;
            }

            companion.AddHistory(new CompanionEvent
            {
                Type = CompanionEventType.QuestCompleted,
                Description = success
                    ? $"Successfully completed: {companion.PersonalQuestName}"
                    : $"Quest failed: {companion.PersonalQuestName}",
                Timestamp = DateTime.Now
            });

            OnCompanionQuestCompleted?.Invoke(id);
        }

        /// <summary>
        /// Trigger a companion's death from a moral paradox choice
        /// This is a simplified version that doesn't require terminal for async display
        /// </summary>
        public void TriggerCompanionDeathByParadox(string companionName)
        {
            // Find companion by name
            var companion = companions.Values.FirstOrDefault(c =>
                c.Name.Equals(companionName, StringComparison.OrdinalIgnoreCase));

            if (companion == null || companion.IsDead)
                return;

            companion.IsDead = true;
            companion.IsActive = false;
            companion.DeathType = DeathType.ChoiceBased;

            // Return companion's equipment AND bag contents to player inventory
            // (TerminalEmulator is null in this paradox path — helper falls back
            // to pendingNotifications so the player still learns about it).
            ReturnCompanionGearOnDeath(companion, terminal: null);

            activeCompanions.Remove(companion.Id);

            fallenCompanions[companion.Id] = new CompanionDeath
            {
                CompanionId = companion.Id,
                Type = DeathType.ChoiceBased,
                Circumstance = "Died as a consequence of a moral choice",
                LastWords = GetLastWords(companion, DeathType.ChoiceBased),
                DeathDay = GetGameDay(),
                PlayerLevel = GetPlayerLevel()
            };

            // Trigger grief system
            GriefSystem.Instance.BeginGrief(companion.Id, companion.Name, DeathType.ChoiceBased);

            // Trigger Ocean Philosophy awakening for sacrifice
            OceanPhilosophySystem.Instance.ExperienceMoment(AwakeningMoment.CompanionSacrifice);

            OnCompanionDeath?.Invoke(companion.Id, DeathType.ChoiceBased);

            // Prevent save cheesing — persist negative outcomes immediately
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
                _ = GameEngine.Instance.SaveCurrentGame();
        }

        #endregion

        #region Display Methods

        private async Task DisplayRecruitmentScene(Companion companion, TerminalEmulator terminal)
        {
            terminal.Clear();
            UIHelper.WriteBoxHeader(terminal, Loc.Get("companion.new_companion_header"), "bright_cyan", 66);
            terminal.WriteLine("");

            terminal.WriteLine($"  {companion.Name}", "bright_white");
            terminal.WriteLine($"  \"{companion.Title}\"", "cyan");
            terminal.WriteLine("");

            terminal.WriteLine(companion.Description, "white");
            terminal.WriteLine("");

            terminal.WriteLine(Loc.Get("companion.role_label", companion.CombatRole), "yellow");
            terminal.WriteLine(Loc.Get("companion.abilities_label", string.Join(", ", companion.Abilities)), "yellow");
            terminal.WriteLine("");

            // Show a hint of their deeper story
            if (companion.DialogueHints.Length > 0)
            {
                terminal.WriteLine($"  \"{companion.DialogueHints[0]}\"", "dark_cyan");
            }

            await terminal.GetInputAsync(Loc.Get("companion.press_enter_welcome"));
        }

        private async Task DisplayDeathScene(Companion companion, DeathType type, string circumstance, TerminalEmulator terminal)
        {
            terminal.Clear();
            await Task.Delay(1500);

            // Slow, solemn header
            terminal.WriteLine("");
            await Task.Delay(500);
            if (GameConfig.ScreenReaderMode)
            {
                terminal.WriteLine(Loc.Get("companion.fallen_header"), "dark_red");
            }
            else
            {
                terminal.WriteLine("╔══════════════════════════════════════════════════════════════════╗", "dark_red");
                terminal.WriteLine("║                                                                    ║", "dark_red");
                terminal.WriteLine("║                    F   A   L   L   E   N                          ║", "dark_red");
                terminal.WriteLine("║                                                                    ║", "dark_red");
                terminal.WriteLine("╚══════════════════════════════════════════════════════════════════╝", "dark_red");
            }
            terminal.WriteLine("");

            await Task.Delay(2000);

            terminal.WriteLine($"  {companion.Name}", "bright_white");
            terminal.WriteLine($"  \"{companion.Title}\"", "cyan");
            terminal.WriteLine("");

            await Task.Delay(1000);

            // The circumstance of death
            terminal.WriteLine($"  {circumstance}", "white");
            terminal.WriteLine("");

            await Task.Delay(1500);

            // Their final words
            string lastWords = GetLastWords(companion, type);
            terminal.SetColor("dark_cyan");
            terminal.WriteLine($"  \"{lastWords}\"");
            terminal.WriteLine("");

            await Task.Delay(2000);

            // The moment of passing
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("companion.goes_still", companion.Name));
            await Task.Delay(1200);
            terminal.WriteLine(Loc.Get("companion.gone"));
            terminal.WriteLine("");

            await Task.Delay(2000);

            // Philosophical moment based on companion and death type
            await DisplayDeathPhilosophy(companion, type, terminal);

            await Task.Delay(1500);

            // Final message
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("companion.is_dead", companion.Name));
            terminal.WriteLine(Loc.Get("companion.no_coming_back"));
            terminal.WriteLine("");

            await Task.Delay(1500);

            // Memory persists - varied by companion
            terminal.SetColor("bright_cyan");
            string memoryLine = companion.Id switch
            {
                CompanionId.Lyris => "  You wont forget her. You know that much.",
                CompanionId.Aldric => "  He died doing what he always did. Standing in front of someone.",
                CompanionId.Mira => "  She healed you. More times than you can count.",
                CompanionId.Vex => "  You can still hear him laughing. Somehow.",
                _ => "  You wont forget them."
            };
            terminal.WriteLine(memoryLine);
            terminal.SetColor("white");
            terminal.WriteLine("");

            await terminal.GetInputAsync(Loc.Get("companion.press_enter_ready"));
        }

        /// <summary>
        /// Display philosophical content unique to each companion's death
        /// Each companion embodies a different metaphor/lesson
        /// </summary>
        private async Task DisplayDeathPhilosophy(Companion companion, DeathType type, TerminalEmulator terminal)
        {
            terminal.WriteLine("");

            switch (companion.Id)
            {
                case CompanionId.Lyris:
                    terminal.SetColor("bright_magenta");
                    terminal.WriteLine("  She knew things she shouldnt have known.");
                    terminal.WriteLine("  About you. About the gods. About all of it.");
                    terminal.WriteLine("");
                    await Task.Delay(1500);
                    terminal.SetColor("cyan");
                    terminal.WriteLine("  You never found out how much she really knew.");
                    terminal.WriteLine("  Now you never will.");
                    terminal.WriteLine("");
                    await Task.Delay(1000);
                    terminal.SetColor("bright_white");
                    terminal.WriteLine("  Funny how much you miss someone");
                    terminal.WriteLine("  who never told you the whole truth.");
                    break;

                case CompanionId.Aldric:
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine("  He lost his whole unit once. Every single one of them.");
                    terminal.WriteLine("  Carried that around for years.");
                    await Task.Delay(1000);
                    terminal.SetColor("white");
                    terminal.WriteLine("");
                    terminal.WriteLine("  This time he didnt lose anyone.");
                    terminal.WriteLine("  Just himself.");
                    terminal.WriteLine("");
                    await Task.Delay(1000);
                    terminal.SetColor("bright_green");
                    terminal.WriteLine("  Maybe thats what he wanted all along.");
                    terminal.WriteLine("  One fight where he didnt have to watch someone else die.");
                    break;

                case CompanionId.Mira:
                    terminal.SetColor("bright_green");
                    terminal.WriteLine("  She lost her faith when the temple fell.");
                    terminal.WriteLine("  Kept healing people anyway. Said she didnt know why.");
                    terminal.WriteLine("");
                    await Task.Delay(1500);
                    terminal.SetColor("white");
                    terminal.WriteLine("  Maybe she did know why.");
                    terminal.WriteLine("  Maybe she just didnt want to admit it.");
                    terminal.WriteLine("");
                    await Task.Delay(1000);
                    terminal.SetColor("bright_cyan");
                    terminal.WriteLine("  She healed you because she cared.");
                    terminal.WriteLine("  Thats it. Thats the whole reason.");
                    break;

                case CompanionId.Vex:
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine("  He was dying the whole time you knew him.");
                    terminal.WriteLine("  Never shut up about it either. Made jokes.");
                    terminal.WriteLine("  Drove you crazy sometimes.");
                    terminal.WriteLine("");
                    await Task.Delay(1500);
                    terminal.SetColor("white");
                    terminal.WriteLine("  Turns out thats how he dealt with it.");
                    terminal.WriteLine("  If you cant beat it, laugh at it.");
                    terminal.WriteLine("");
                    await Task.Delay(1000);
                    terminal.SetColor("cyan");
                    terminal.WriteLine("  The dungeon is quieter now.");
                    terminal.WriteLine("  You keep expecting to hear a bad joke around the next corner.");
                    break;
            }

            terminal.WriteLine("");
        }

        #endregion

        #region Serialization

        /// <summary>
        /// Serialize companion state for saving
        /// </summary>
        public CompanionSystemData Serialize()
        {
            // Log companion levels and equipment being saved for debugging
            foreach (var c in companions.Values.Where(c => c.IsRecruited))
            {
                DebugLogger.Instance.LogDebug("COMPANION", $"Serializing {c.Name}: Level={c.Level}, XP={c.Experience}");
                if (c.EquippedItems.Count > 0)
                {
                    var slots = string.Join(",", c.EquippedItems.Select(kv => $"{kv.Key}={kv.Value}"));
                    DebugLogger.Instance.LogInfo("COMPANION_EQUIP",
                        $"Serialize SAVING {c.Name} equipment: {slots}");
                }
                // v0.57.4: track bag contents on save so "my item is missing" reports
                // can trace the item through save → load without needing a repro.
                // Only logs when bag is non-empty to avoid flooding on every autosave.
                if (c.Inventory != null && c.Inventory.Count > 0)
                {
                    var bagSummary = string.Join(", ", c.Inventory.Take(5).Select(i => i?.Name ?? "?"));
                    if (c.Inventory.Count > 5) bagSummary += $", +{c.Inventory.Count - 5} more";
                    DebugLogger.Instance.LogInfo("COMPANION_BAG",
                        $"Serialize SAVING {c.Name} bag ({c.Inventory.Count}): {bagSummary}");
                }
            }

            return new CompanionSystemData
            {
                CompanionStates = companions.Values.Select(c => new CompanionSaveData
                {
                    Id = c.Id,
                    IsRecruited = c.IsRecruited,
                    IsActive = c.IsActive,
                    IsDead = c.IsDead,
                    LoyaltyLevel = c.LoyaltyLevel,
                    TrustLevel = c.TrustLevel,
                    RomanceLevel = c.RomanceLevel,
                    PersonalQuestStarted = c.PersonalQuestStarted,
                    PersonalQuestCompleted = c.PersonalQuestCompleted,
                    PersonalQuestSuccess = c.PersonalQuestSuccess,
                    RecruitedDay = c.RecruitedDay,
                    RecruitedDate = c.RecruitedDate,
                    DeathType = c.DeathType,
                    History = c.History.ToList(),
                    HealingPotions = c.HealingPotions,
                    ManaPotions = c.ManaPotions,
                    Level = c.Level,
                    Experience = c.Experience,
                    BaseStatsHP = c.BaseStats.HP,
                    BaseStatsAttack = c.BaseStats.Attack,
                    BaseStatsDefense = c.BaseStats.Defense,
                    BaseStatsMagicPower = c.BaseStats.MagicPower,
                    BaseStatsSpeed = c.BaseStats.Speed,
                    BaseStatsHealingPower = c.BaseStats.HealingPower,
                    Constitution = c.Constitution,
                    Intelligence = c.Intelligence,
                    Wisdom = c.Wisdom,
                    Charisma = c.Charisma,
                    Dexterity = c.Dexterity,
                    Agility = c.Agility,
                    EquippedItemsSave = c.EquippedItems.ToDictionary(kvp => (int)kvp.Key, kvp => kvp.Value),
                    DisabledAbilities = c.DisabledAbilities.ToList(),
                    DisabledSpells = c.DisabledSpells.ToList(),
                    SkillProficiencies = c.SkillProficiencies?.Count > 0 ? new Dictionary<string, int>(c.SkillProficiencies) : new(),
                    SkillTrainingProgress = c.SkillTrainingProgress?.Count > 0 ? new Dictionary<string, int>(c.SkillTrainingProgress) : new(),
                    Inventory = c.Inventory?.Where(i => i != null).Select(ItemToData).ToList() ?? new List<InventoryItemData>()
                }).ToList(),
                ActiveCompanions = activeCompanions.ToList(),
                FallenCompanions = fallenCompanions.Values.ToList()
            };
        }

        /// <summary>
        /// Reset daily companion flags (called on daily reset)
        /// </summary>
        public void ResetDailyFlags()
        {
            foreach (var companion in companions.Values)
            {
                companion.RomancedToday = false;
            }
        }

        /// <summary>
        /// Reset all companions to their initial state (not recruited, not dead)
        /// Called before loading a save to prevent state bleeding between characters
        /// </summary>
        public void ResetAllCompanions()
        {
            foreach (var companion in companions.Values)
            {
                companion.IsRecruited = false;
                companion.IsActive = false;
                companion.IsDead = false;
                companion.DeathType = null;
                companion.LoyaltyLevel = 50;
                companion.TrustLevel = 50;
                companion.RomanceLevel = 0;
                companion.RomancedToday = false;
                companion.PersonalQuestStarted = false;
                companion.PersonalQuestCompleted = false;
                companion.PersonalQuestSuccess = false;
                companion.RecruitedDay = 0;
                companion.History.Clear();
                companion.EquippedItems.Clear();
                companion.Inventory.Clear();
                companion.DisabledAbilities.Clear();
                companion.DisabledSpells.Clear();
                companion.Level = Math.Max(1, companion.RecruitLevel + 5);
                companion.Experience = GetExperienceForLevel(companion.Level);
            }

            activeCompanions.Clear();
            fallenCompanions.Clear();
            companionCurrentHP.Clear();
            pendingNotifications.Clear();
        }

        /// <summary>
        /// Restore companion state from save
        /// </summary>
        public void Deserialize(CompanionSystemData data)
        {
            // Log state BEFORE reset for bug diagnosis
            foreach (var comp in companions.Values)
            {
                if (comp.IsRecruited && comp.EquippedItems.Count > 0)
                {
                    var slots = string.Join(",", comp.EquippedItems.Select(kv => $"{kv.Key}={kv.Value}"));
                    DebugLogger.Instance.LogInfo("COMPANION_EQUIP",
                        $"Deserialize BEFORE reset: {comp.Name} had equipment: {slots}");
                }
            }

            // Always reset first to prevent state bleeding from previous saves
            ResetAllCompanions();

            if (data == null) return;

            foreach (var save in data.CompanionStates)
            {
                if (companions.TryGetValue(save.Id, out var companion))
                {
                    // Log incoming data for debugging
                    if (save.IsRecruited)
                    {
                        DebugLogger.Instance.LogDebug("COMPANION", $"Deserializing {companion.Name}: SavedLevel={save.Level}, SavedXP={save.Experience}");
                    }

                    companion.IsRecruited = save.IsRecruited;
                    companion.IsActive = save.IsActive;
                    companion.IsDead = save.IsDead;
                    companion.LoyaltyLevel = save.LoyaltyLevel;
                    companion.TrustLevel = save.TrustLevel;
                    companion.RomanceLevel = save.RomanceLevel;
                    companion.PersonalQuestStarted = save.PersonalQuestStarted;
                    companion.PersonalQuestCompleted = save.PersonalQuestCompleted;
                    companion.PersonalQuestSuccess = save.PersonalQuestSuccess;
                    companion.RecruitedDay = save.RecruitedDay;
                    companion.RecruitedDate = save.RecruitedDate != DateTime.MinValue ? save.RecruitedDate : DateTime.UtcNow;
                    companion.DeathType = save.DeathType;
                    companion.History = save.History?.ToList() ?? new List<CompanionEvent>();
                    companion.HealingPotions = save.HealingPotions;
                    companion.ManaPotions = save.ManaPotions;

                    // Restore level and experience
                    companion.Level = save.Level > 0 ? save.Level : Math.Max(1, companion.RecruitLevel + 5);

                    // If Experience is 0 or missing, initialize it to the proper value for their level
                    // This handles legacy saves that didn't track companion XP
                    if (save.Experience > 0)
                    {
                        companion.Experience = save.Experience;
                    }
                    else
                    {
                        companion.Experience = GetExperienceForLevel(companion.Level);
                        // DebugLogger: Initialized {companion.Name}'s XP to {companion.Experience} for level {companion.Level}
                    }

                    // Restore base stats if saved (otherwise scale from defaults)
                    if (save.BaseStatsHP > 0)
                    {
                        companion.BaseStats.HP = save.BaseStatsHP;
                        companion.BaseStats.Attack = save.BaseStatsAttack;
                        companion.BaseStats.Defense = save.BaseStatsDefense;
                        companion.BaseStats.MagicPower = save.BaseStatsMagicPower;
                        companion.BaseStats.Speed = save.BaseStatsSpeed;
                        companion.BaseStats.HealingPower = save.BaseStatsHealingPower;

                        // Secondary stats: restore if saved with non-default values (level > 1 companions
                        // should have values above 10). If all are still 10, this is a legacy save
                        // that predates secondary stat tracking — scale them from level now.
                        bool hasScaledSecondaryStats = save.Constitution > 10 || save.Intelligence > 10 ||
                                                       save.Wisdom > 10 || save.Charisma > 10 ||
                                                       save.Dexterity > 10 || save.Agility > 10;
                        if (hasScaledSecondaryStats)
                        {
                            companion.Constitution = save.Constitution;
                            companion.Intelligence = save.Intelligence;
                            companion.Wisdom = save.Wisdom;
                            companion.Charisma = save.Charisma;
                            companion.Dexterity = save.Dexterity;
                            companion.Agility = save.Agility;
                        }
                        else if (companion.Level > 1)
                        {
                            // Legacy save — secondary stats were never tracked; scale them now
                            ScaleCompanionSecondaryStatsToLevel(companion);
                        }
                    }
                    else if (companion.IsRecruited && companion.Level > 1)
                    {
                        // Legacy save without stats - scale from default base stats
                        // First reset to original defaults, then scale
                        ResetCompanionToBaseStats(companion);
                        ScaleCompanionStatsToLevel(companion);
                    }

                    // Restore equipment (with ID remapping for MUD mode collision avoidance)
                    companion.EquippedItems.Clear();
                    if (save.EquippedItemsSave != null)
                    {
                        var remap = GameEngine.Instance?.DynamicEquipIdRemap;
                        foreach (var kvp in save.EquippedItemsSave)
                        {
                            int equipId = kvp.Value;
                            if (remap != null && remap.TryGetValue(equipId, out int newId))
                                equipId = newId;
                            companion.EquippedItems[(EquipmentSlot)kvp.Key] = equipId;
                        }
                        if (save.IsRecruited && companion.EquippedItems.Count > 0)
                        {
                            var slots = string.Join(",", companion.EquippedItems.Select(kv => $"{kv.Key}={kv.Value}"));
                            DebugLogger.Instance.LogInfo("COMPANION_EQUIP",
                                $"Deserialize RESTORED from save: {companion.Name} equipment: {slots}");
                        }
                    }

                    // Restore disabled abilities
                    companion.DisabledAbilities.Clear();
                    if (save.DisabledAbilities != null)
                    {
                        foreach (var id in save.DisabledAbilities)
                            companion.DisabledAbilities.Add(id);
                    }

                    // v0.57.11: restore disabled spells (parallel to abilities).
                    // Pre-v0.57.11 saves won't have this field; the empty default
                    // is correct (all spells allowed, matching old behavior).
                    companion.DisabledSpells.Clear();
                    if (save.DisabledSpells != null)
                    {
                        foreach (var id in save.DisabledSpells)
                            companion.DisabledSpells.Add(id);
                    }

                    // Restore skill proficiency
                    companion.SkillProficiencies = save.SkillProficiencies?.Count > 0
                        ? new Dictionary<string, int>(save.SkillProficiencies)
                        : new();
                    companion.SkillTrainingProgress = save.SkillTrainingProgress?.Count > 0
                        ? new Dictionary<string, int>(save.SkillTrainingProgress)
                        : new();

                    // Restore companion's personal inventory (items transferred to them in combat,
                    // Home, Team Corner, or dungeon). Must be a fresh list so GetCompanionsAsCharacters
                    // shares a stable reference with the wrapper.
                    companion.Inventory = save.Inventory?.Select(DataToItem).ToList() ?? new List<Item>();
                    // v0.57.4: pair with the save-side COMPANION_BAG log so the full
                    // save → load round-trip is visible in logs. Confirms items that
                    // were written actually come back.
                    if (companion.Inventory.Count > 0)
                    {
                        var bagSummary = string.Join(", ", companion.Inventory.Take(5).Select(i => i?.Name ?? "?"));
                        if (companion.Inventory.Count > 5) bagSummary += $", +{companion.Inventory.Count - 5} more";
                        DebugLogger.Instance.LogInfo("COMPANION_BAG",
                            $"Deserialize RESTORED {companion.Name} bag ({companion.Inventory.Count}): {bagSummary}");
                    }
                }
            }

            activeCompanions = data.ActiveCompanions?.ToList() ?? new List<CompanionId>();

            fallenCompanions.Clear();
            if (data.FallenCompanions != null)
            {
                foreach (var death in data.FallenCompanions)
                {
                    fallenCompanions[death.CompanionId] = death;
                }
            }

            // Log final companion levels after deserialization for debugging
            foreach (var c in companions.Values.Where(c => c.IsRecruited))
            {
                DebugLogger.Instance.LogDebug("COMPANION", $"After restore: {c.Name} Level={c.Level}, XP={c.Experience}");
            }
        }

        #endregion

        #region Experience and Leveling

        /// <summary>
        /// XP formula matching the player's curve (level^2.0 * 50)
        /// </summary>
        public static long GetExperienceForLevel(int level)
        {
            if (level <= 1) return 0;
            long exp = 0;
            for (int i = 2; i <= level; i++)
            {
                exp += (long)(Math.Pow(i, 2.0) * 50);
            }
            return exp;
        }

        /// <summary>
        /// Award experience to all active companions (typically 50% of what player earns)
        /// Companions auto-level when they hit the threshold
        /// </summary>
        public void AwardCompanionExperience(long baseXP, TerminalEmulator? terminal = null)
        {
            if (baseXP <= 0) return;

            // Companions get 50% of player's XP
            long companionXP = baseXP / 2;
            if (companionXP <= 0) return;

            var activeCompanions = GetActiveCompanions().Where(c => c != null && !c.IsDead && c.Level < 100).ToList();
            if (activeCompanions.Count == 0) return;

            // Show companion XP header if we have a terminal
            if (terminal != null && activeCompanions.Count > 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("companion.xp_header", companionXP));
            }

            foreach (var companion in activeCompanions)
            {
                long previousXP = companion.Experience;
                companion.Experience += companionXP;

                // Show XP gain
                if (terminal != null)
                {
                    long xpNeeded = GetExperienceForLevel(companion.Level + 1);
                    terminal.SetColor("bright_magenta");
                    terminal.WriteLine($"  {companion.Name}: {companion.Experience:N0}/{xpNeeded:N0}");
                }

                // Check for level up
                CheckCompanionLevelUp(companion, terminal);
            }
        }

        /// <summary>
        /// Award XP to a specific companion by name (used by per-slot XP distribution)
        /// </summary>
        public void AwardSpecificCompanionXP(string companionName, long xp, TerminalEmulator? terminal)
        {
            if (xp <= 0) return;
            var companion = GetActiveCompanions()?.FirstOrDefault(c => c.Name == companionName);
            if (companion == null || companion.IsDead || companion.Level >= 100) return;
            companion.Experience += xp;
            CheckCompanionLevelUp(companion, terminal);
        }

        /// <summary>
        /// Check if a companion should level up and apply stat gains
        /// </summary>
        private void CheckCompanionLevelUp(Companion companion, TerminalEmulator? terminal)
        {
            if (companion.Level >= 100) return;

            long xpForNextLevel = GetExperienceForLevel(companion.Level + 1);

            while (companion.Experience >= xpForNextLevel && companion.Level < 100)
            {
                int oldLevel = companion.Level;

                // Snapshot stats before this level
                int bHP = companion.BaseStats.HP, bAtk = companion.BaseStats.Attack, bDef = companion.BaseStats.Defense;
                int bSpd = companion.BaseStats.Speed, bMag = companion.BaseStats.MagicPower, bHeal = companion.BaseStats.HealingPower;
                int bCon = companion.Constitution, bDex = companion.Dexterity;
                int bAgi = companion.Agility, bInt = companion.Intelligence, bWis = companion.Wisdom, bCha = companion.Charisma;

                companion.Level++;

                // Log companion level up
                DebugLogger.Instance.LogCompanionLevelUp(companion.Name, oldLevel, companion.Level);

                // Apply stat gains based on combat role
                ApplyCompanionLevelUpStats(companion);

                terminal?.SetColor("bright_green");
                terminal?.WriteLine(Loc.Get("companion.reached_level", companion.Name, companion.Level));

                // Show stat changes
                if (terminal != null)
                {
                    var sc = new List<string>();
                    int dAtk = companion.BaseStats.Attack - bAtk;
                    int dDef = companion.BaseStats.Defense - bDef;
                    int dSpd = companion.BaseStats.Speed - bSpd;
                    int dMag = companion.BaseStats.MagicPower - bMag;
                    int dHeal = companion.BaseStats.HealingPower - bHeal;
                    int dHP = companion.BaseStats.HP - bHP;
                    int dCon = companion.Constitution - bCon;
                    int dDex = companion.Dexterity - bDex;
                    int dAgi = companion.Agility - bAgi;
                    int dInt = companion.Intelligence - bInt;
                    int dWis = companion.Wisdom - bWis;
                    int dCha = companion.Charisma - bCha;
                    if (dAtk > 0) sc.Add($"ATK +{dAtk}");
                    if (dDef > 0) sc.Add($"DEF +{dDef}");
                    if (dSpd > 0) sc.Add($"SPD +{dSpd}");
                    if (dMag > 0) sc.Add($"MAG +{dMag}");
                    if (dHeal > 0) sc.Add($"HEAL +{dHeal}");
                    if (dCon > 0) sc.Add($"CON +{dCon}");
                    if (dDex > 0) sc.Add($"DEX +{dDex}");
                    if (dAgi > 0) sc.Add($"AGI +{dAgi}");
                    if (dInt > 0) sc.Add($"INT +{dInt}");
                    if (dWis > 0) sc.Add($"WIS +{dWis}");
                    if (dCha > 0) sc.Add($"CHA +{dCha}");
                    if (dHP > 0) sc.Add($"HP +{dHP}");
                    if (sc.Count > 0)
                    {
                        terminal.SetColor("bright_green");
                        terminal.WriteLine("  " + string.Join("  ", sc));
                    }
                }

                // Update loyalty slightly on level up (bonding through shared experience)
                ModifyLoyalty(companion.Id, 1, "Leveled up through shared combat");

                // Calculate next threshold
                xpForNextLevel = GetExperienceForLevel(companion.Level + 1);
            }
        }

        /// <summary>
        /// Sync companion level and stats to active Character wrappers used in combat.
        /// Call after AwardCompanionExperience to ensure level-ups are reflected immediately.
        /// </summary>
        public void SyncCompanionLevelToWrappers(List<Character>? teammates)
        {
            if (teammates == null) return;

            foreach (var wrapper in teammates)
            {
                if (!wrapper.IsCompanion || !wrapper.CompanionId.HasValue) continue;

                if (companions.TryGetValue(wrapper.CompanionId.Value, out var companion))
                {
                    if (wrapper.Level == companion.Level) continue;

                    // Level changed — update the wrapper's base stats and recalculate
                    wrapper.Level = companion.Level;
                    wrapper.BaseStrength = companion.BaseStats.Attack;
                    wrapper.BaseDefence = companion.BaseStats.Defense;
                    wrapper.BaseDexterity = Math.Max(companion.BaseStats.Speed, companion.Dexterity);
                    wrapper.BaseAgility = Math.Max(companion.BaseStats.Speed, companion.Agility);
                    wrapper.BaseIntelligence = Math.Max(companion.BaseStats.MagicPower, companion.Intelligence);
                    wrapper.BaseWisdom = Math.Max(companion.BaseStats.HealingPower, companion.Wisdom);
                    wrapper.BaseConstitution = Math.Max(10 + companion.Level, companion.Constitution);
                    wrapper.BaseCharisma = Math.Max(10, companion.Charisma);
                    wrapper.BaseStamina = 10 + companion.Level;
                    wrapper.BaseMaxHP = companion.BaseStats.HP;
                    wrapper.BaseMaxMana = companion.BaseStats.MagicPower * 5;

                    // Recalculate with equipment bonuses
                    long hpBefore = wrapper.MaxHP;
                    wrapper.RecalculateStats();

                    // Grant the HP increase (don't reset current HP, just add the gain)
                    long hpGain = wrapper.MaxHP - hpBefore;
                    if (hpGain > 0)
                    {
                        wrapper.HP += hpGain;
                        // Update tracked combat HP too
                        companionCurrentHP[companion.Id] = (int)wrapper.HP;
                    }

                    wrapper.Mana = companion.BaseStats.MagicPower * 5;
                }
            }
        }

        /// <summary>
        /// Apply stat gains when a companion levels up
        /// </summary>
        private void ApplyCompanionLevelUpStats(Companion companion)
        {
            var random = Random.Shared;

            // Base HP gain: 12-24 per level, +1 for every 10 levels (tiered scaling)
            int hpGain = 12 + random.Next(0, 13) + (companion.Level / 10);

            // Role-specific stat gains (BaseStats for combat + Character properties for depth)
            switch (companion.CombatRole)
            {
                case CombatRole.Tank:
                    companion.BaseStats.HP += hpGain + 8;
                    companion.BaseStats.Defense += 2 + random.Next(0, 2);
                    companion.BaseStats.Attack += 1 + random.Next(0, 2);
                    companion.Constitution += 3;
                    companion.Wisdom += 1;
                    break;

                case CombatRole.Damage:
                    companion.BaseStats.HP += hpGain;
                    companion.BaseStats.Attack += 2 + random.Next(1, 3);
                    companion.BaseStats.Speed += 1 + random.Next(0, 2);
                    companion.BaseStats.Defense += 1;
                    companion.Dexterity += 3;
                    companion.Agility += 2;
                    break;

                case CombatRole.Healer:
                    companion.BaseStats.HP += hpGain + 2;
                    companion.BaseStats.HealingPower += 3 + random.Next(1, 3);
                    companion.BaseStats.MagicPower += 2 + random.Next(0, 2);
                    companion.BaseStats.Defense += 1;
                    companion.Wisdom += 3;
                    companion.Intelligence += 2;
                    companion.Constitution += 1;
                    break;

                case CombatRole.Hybrid:
                    companion.BaseStats.HP += hpGain + 4;
                    companion.BaseStats.Attack += 1 + random.Next(0, 2);
                    companion.BaseStats.Defense += 2 + random.Next(0, 2);
                    companion.BaseStats.MagicPower += 2 + random.Next(0, 2);
                    companion.BaseStats.HealingPower += 1 + random.Next(0, 2);
                    companion.Intelligence += 2;
                    companion.Wisdom += 2;
                    companion.Charisma += 2;
                    companion.Constitution += 2;
                    break;

                case CombatRole.Bard:
                    companion.BaseStats.HP += hpGain + 1;
                    companion.BaseStats.MagicPower += 3 + random.Next(0, 2);
                    companion.BaseStats.HealingPower += 2 + random.Next(0, 2);
                    companion.BaseStats.Speed += 1 + random.Next(0, 2);
                    companion.BaseStats.Attack += 1;
                    companion.Charisma += 3;
                    companion.Intelligence += 2;
                    companion.Dexterity += 1;
                    break;
            }

            // Update current HP tracking to new max (with equipment bonuses)
            if (companionCurrentHP.ContainsKey(companion.Id))
            {
                // Heal to full on level up
                companionCurrentHP[companion.Id] = GetCompanionMaxHP(companion);
            }

            // GD.Print($"[Companion] {companion.Name} leveled up to {companion.Level}! HP: {companion.BaseStats.HP}, ATK: {companion.BaseStats.Attack}, DEF: {companion.BaseStats.Defense}");
        }

        /// <summary>
        /// Level up a companion by applying stat gains for each level gained.
        /// Call this when a companion gains enough XP to level up (e.g., from shared experience).
        /// Returns the number of levels gained.
        /// </summary>
        public int LevelUpCompanion(CompanionId id, int levelsToGain)
        {
            if (!companions.TryGetValue(id, out var companion) || companion.IsDead)
                return 0;

            int levelsGained = 0;
            int maxLevel = GameConfig.MaxLevel;

            for (int i = 0; i < levelsToGain && companion.Level < maxLevel; i++)
            {
                companion.Level++;
                ApplyCompanionLevelUpStats(companion);
                levelsGained++;

                // Update loyalty slightly on level up
                ModifyLoyalty(companion.Id, 1, "Leveled up through shared training");
            }

            return levelsGained;
        }

        /// <summary>
        /// Initialize a companion's level and XP when recruited
        /// </summary>
        private void InitializeCompanionLevel(Companion companion)
        {
            // Start at RecruitLevel + 5 (as before, but now tracked)
            companion.Level = Math.Max(1, companion.RecruitLevel + 5);
            companion.Experience = GetExperienceForLevel(companion.Level);

            // Scale base stats to match level
            ScaleCompanionStatsToLevel(companion);

            // Initialize healing potions based on level
            RefillCompanionPotions(companion);

            // Give starting equipment appropriate to their level and role
            EquipStartingGear(companion);
        }

        /// <summary>
        /// Equip a companion with level-appropriate gear from the shop database
        /// </summary>
        private void EquipStartingGear(Companion companion)
        {
            int level = companion.Level;

            // Pick a weapon type based on combat role/class
            var preferredWeaponType = companion.CombatRole switch
            {
                CombatRole.Tank => WeaponType.Sword,        // Aldric (Warrior) - swords
                CombatRole.Damage => WeaponType.Dagger,     // Vex (Assassin) - daggers for backstab
                CombatRole.Healer => WeaponType.Mace,       // Mira (Cleric) - maces
                CombatRole.Hybrid => WeaponType.Bow,        // Lyris (Ranger) - bows
                CombatRole.Bard => WeaponType.Instrument,   // Melodia (Bard) - instruments for songs
                _ => WeaponType.Sword
            };

            // Get weapons of the correct type
            var weapons = EquipmentDatabase.GetShopWeaponsByType(preferredWeaponType);
            var bestWeapon = weapons
                .Where(w => w.Value <= level * 1000 + 500) // level-appropriate by value
                .OrderByDescending(w => w.WeaponPower)
                .FirstOrDefault();

            // Fallback to any one-handed weapon if preferred type not available
            if (bestWeapon == null)
            {
                var allWeapons = EquipmentDatabase.GetShopWeapons(WeaponHandedness.OneHanded);
                bestWeapon = allWeapons
                    .Where(w => w.Value <= level * 1000 + 500)
                    .OrderByDescending(w => w.WeaponPower)
                    .FirstOrDefault();
            }

            if (bestWeapon != null)
            {
                companion.EquippedItems[EquipmentSlot.MainHand] = bestWeapon.Id;
            }

            // Tanks get a shield in off-hand
            if (companion.CombatRole == CombatRole.Tank)
            {
                var shields = EquipmentDatabase.GetShopShields();
                var bestShield = shields
                    .Where(s => s.MinLevel <= level)
                    .OrderByDescending(s => s.MinLevel)
                    .ThenByDescending(s => s.Value)
                    .FirstOrDefault();
                if (bestShield != null)
                {
                    companion.EquippedItems[EquipmentSlot.OffHand] = bestShield.Id;
                }
            }

            // Give body armor
            var bodyArmor = EquipmentDatabase.GetShopArmor(EquipmentSlot.Body);
            var bestBody = bodyArmor
                .Where(a => a.MinLevel <= level)
                .OrderByDescending(a => a.MinLevel)
                .ThenByDescending(a => a.ArmorClass)
                .FirstOrDefault();
            if (bestBody != null)
            {
                companion.EquippedItems[EquipmentSlot.Body] = bestBody.Id;
            }

            // Give head armor
            var headArmor = EquipmentDatabase.GetShopArmor(EquipmentSlot.Head);
            var bestHead = headArmor
                .Where(a => a.MinLevel <= level)
                .OrderByDescending(a => a.MinLevel)
                .ThenByDescending(a => a.ArmorClass)
                .FirstOrDefault();
            if (bestHead != null)
            {
                companion.EquippedItems[EquipmentSlot.Head] = bestHead.Id;
            }
        }

        /// <summary>
        /// Refill a companion's healing potions (called on recruit, rest, new day)
        /// </summary>
        public void RefillCompanionPotions(Companion companion)
        {
            // Companions get potions based on level - healers get fewer since they use spells
            int basePotions = (companion.CombatRole == CombatRole.Healer || companion.CombatRole == CombatRole.Bard) ? 2 : 5;
            companion.HealingPotions = Math.Min(basePotions + companion.Level / 2, companion.MaxHealingPotions);

            // Caster companions also stock mana potions
            if (companion.CombatRole == CombatRole.Healer || companion.CombatRole == CombatRole.Hybrid ||
                companion.CombatRole == CombatRole.Bard)
            {
                int baseMana = 3 + companion.Level / 3;
                companion.ManaPotions = Math.Min(baseMana, companion.MaxManaPotions);
            }
        }

        /// <summary>
        /// Refill potions for all active companions (called on rest/new day)
        /// </summary>
        public void RefillAllCompanionPotions()
        {
            foreach (var id in activeCompanions)
            {
                if (companions.TryGetValue(id, out var companion) && !companion.IsDead)
                {
                    RefillCompanionPotions(companion);
                }
            }
        }

        /// <summary>
        /// Scale companion base stats based on their current level
        /// </summary>
        private void ScaleCompanionStatsToLevel(Companion companion)
        {
            // Only scale if above level 1
            int levelsAboveBase = companion.Level - 1;
            if (levelsAboveBase <= 0) return;

            var random = new Random(companion.Id.GetHashCode()); // Deterministic per companion

            for (int i = 0; i < levelsAboveBase; i++)
            {
                int currentLevel = i + 2; // Level they're scaling to (starts at 2)
                // HP gain scales with level — companions need to keep pace with monster damage
                // Base: 12-20, plus 1 extra per 10 levels (so level 50 gets 16-24, level 100 gets 21-29)
                int hpGain = 12 + random.Next(4, 12) + (currentLevel / 10);
                switch (companion.CombatRole)
                {
                    case CombatRole.Tank:
                        companion.BaseStats.HP += hpGain + 8;
                        companion.BaseStats.Defense += 3;
                        companion.BaseStats.Attack += 1;
                        companion.Constitution += 3;
                        companion.Wisdom += 1;
                        break;
                    case CombatRole.Damage:
                        companion.BaseStats.HP += hpGain;
                        companion.BaseStats.Attack += 2;
                        companion.BaseStats.Defense += 1;
                        companion.BaseStats.Speed += 1;
                        companion.Dexterity += 3;
                        companion.Agility += 2;
                        break;
                    case CombatRole.Healer:
                        companion.BaseStats.HP += hpGain + 2;
                        companion.BaseStats.HealingPower += 3;
                        companion.BaseStats.MagicPower += 2;
                        companion.BaseStats.Defense += 1;
                        companion.Wisdom += 3;
                        companion.Intelligence += 2;
                        companion.Constitution += 1;
                        break;
                    case CombatRole.Hybrid:
                        companion.BaseStats.HP += hpGain + 4;
                        companion.BaseStats.Attack += 1;
                        companion.BaseStats.MagicPower += 2;
                        companion.BaseStats.HealingPower += 1;
                        companion.BaseStats.Defense += 2;
                        companion.Intelligence += 2;
                        companion.Wisdom += 2;
                        companion.Charisma += 2;
                        companion.Constitution += 2;
                        break;
                    case CombatRole.Bard:
                        companion.BaseStats.HP += hpGain + 1;
                        companion.BaseStats.MagicPower += 3;
                        companion.BaseStats.HealingPower += 2;
                        companion.BaseStats.Speed += 1;
                        companion.BaseStats.Attack += 1;
                        companion.BaseStats.Defense += 1;
                        companion.Charisma += 3;
                        companion.Intelligence += 2;
                        companion.Dexterity += 1;
                        break;
                }
            }
        }

        /// <summary>
        /// Scale ONLY the secondary stats (Constitution, Intelligence, etc.) to the companion's
        /// current level. Used for legacy saves that predate secondary stat tracking.
        /// Does NOT touch BaseStats (HP/Attack/Defense) since those are already restored from save.
        /// </summary>
        private void ScaleCompanionSecondaryStatsToLevel(Companion companion)
        {
            int levelsAboveBase = companion.Level - 1;
            if (levelsAboveBase <= 0) return;

            // Secondary stats start at 10 (default)
            companion.Constitution = 10;
            companion.Intelligence = 10;
            companion.Wisdom = 10;
            companion.Charisma = 10;
            companion.Dexterity = 10;
            companion.Agility = 10;

            switch (companion.CombatRole)
            {
                case CombatRole.Tank:
                    companion.Constitution += levelsAboveBase * 3;
                    companion.Wisdom += levelsAboveBase;
                    break;
                case CombatRole.Damage:
                    companion.Dexterity += levelsAboveBase * 3;
                    companion.Agility += levelsAboveBase * 2;
                    break;
                case CombatRole.Healer:
                    companion.Wisdom += levelsAboveBase * 3;
                    companion.Intelligence += levelsAboveBase * 2;
                    companion.Constitution += levelsAboveBase;
                    break;
                case CombatRole.Hybrid:
                    companion.Intelligence += levelsAboveBase * 2;
                    companion.Wisdom += levelsAboveBase * 2;
                    companion.Charisma += levelsAboveBase * 2;
                    break;
                case CombatRole.Bard:
                    companion.Charisma += levelsAboveBase * 3;
                    companion.Intelligence += levelsAboveBase * 2;
                    companion.Dexterity += levelsAboveBase;
                    break;
            }
        }

        /// <summary>
        /// Reset a companion's base stats to their original default values
        /// Used when loading legacy saves that didn't include stat data
        /// </summary>
        private void ResetCompanionToBaseStats(Companion companion)
        {
            // Get the original default stats for each companion
            switch (companion.Id)
            {
                case CompanionId.Lyris:
                    companion.BaseStats.HP = 200;
                    companion.BaseStats.Attack = 25;
                    companion.BaseStats.Defense = 15;
                    companion.BaseStats.MagicPower = 50;
                    companion.BaseStats.Speed = 35;
                    companion.BaseStats.HealingPower = 30;
                    break;
                case CompanionId.Aldric:
                    companion.BaseStats.HP = 350;
                    companion.BaseStats.Attack = 45;
                    companion.BaseStats.Defense = 40;
                    companion.BaseStats.MagicPower = 5;
                    companion.BaseStats.Speed = 20;
                    companion.BaseStats.HealingPower = 0;
                    break;
                case CompanionId.Mira:
                    companion.BaseStats.HP = 180;
                    companion.BaseStats.Attack = 10;
                    companion.BaseStats.Defense = 12;
                    companion.BaseStats.MagicPower = 35;
                    companion.BaseStats.Speed = 25;
                    companion.BaseStats.HealingPower = 60;
                    break;
                case CompanionId.Vex:
                    companion.BaseStats.HP = 150;
                    companion.BaseStats.Attack = 35;
                    companion.BaseStats.Defense = 15;
                    companion.BaseStats.MagicPower = 10;
                    companion.BaseStats.Speed = 50;
                    companion.BaseStats.HealingPower = 0;
                    break;
            }
        }

        #endregion

        #region Dungeon Idle Comments

        private static HashSet<string>? _recentIdleComments;

        /// <summary>
        /// Clear the idle comment history — call on dungeon entry to allow repeats on new runs.
        /// </summary>
        public static void ResetIdleCommentHistory()
        {
            _recentIdleComments = new HashSet<string>();
        }

        /// <summary>
        /// Get a context-sensitive idle comment from a companion, or null if nothing to say.
        /// Returns null ~85% of the time (15% trigger rate is handled by caller).
        /// </summary>
        public static string? GetCompanionIdleComment(CompanionId id, int dungeonLevel, Character player, bool afterCombat)
        {
            _recentIdleComments ??= new HashSet<string>();

            var pool = new List<string>();

            // Base comments per companion
            switch (id)
            {
                case CompanionId.Aldric:
                    pool.AddRange(new[]
                    {
                        "Aldric checks the walls for structural weaknesses, old habits from Fort Ashwall.",
                        "Aldric adjusts his shield. \"Tight corridors. Good for defense.\"",
                        "\"I've held worse positions than this,\" Aldric murmurs, scanning ahead.",
                        "Aldric runs a hand along the stone. \"Dwarven work. Old, but solid.\"",
                        "\"Stay close. I don't like these blind corners.\"",
                        "Aldric pauses to tighten a strap. \"Ready when you are.\"",
                        "\"Fort Ashwall had tunnels like these. We lost good men in them.\"",
                        "Aldric nods to you. \"I've got your back. Always.\"",
                        "\"These walls have seen worse than us,\" Aldric says quietly.",
                        "Aldric peers into the darkness ahead. \"Something moved. Maybe.\"",
                    });
                    if (afterCombat) pool.Add("Aldric cleans his blade. \"Not bad. Could've been cleaner.\"");
                    if (dungeonLevel >= 40) pool.Add("\"We're deep now. The air feels... wrong.\"");
                    if (dungeonLevel >= 70) pool.Add("\"I can feel them. The Old Gods. Even the stone is afraid.\"");
                    if (player.HP < player.MaxHP / 3) pool.Add("\"You're hurt. We should find somewhere safe.\"");
                    break;

                case CompanionId.Vex:
                    pool.AddRange(new[]
                    {
                        "Vex is rummaging through a pile of rubble. \"Nope. Junk. More junk.\"",
                        "\"You know what this dungeon needs? Better loot. I'm just saying.\"",
                        "Vex coughs quietly, then waves it off. \"Dust. It's the dust.\"",
                        "\"Did you know that ancient treasure vaults always smell like this? Me neither.\"",
                        "Vex flips a coin. Catches it. Grins.",
                        "\"I'm not dying down here. That would be embarrassing.\"",
                        "Vex picks a lock on a chest that doesn't exist. \"Practice.\"",
                        "\"My disease doesn't define me. But it does have excellent timing.\"",
                        "Vex leans against a wall. \"Just... resting my eyes. One second.\"",
                        "\"Think there's treasure behind this wall? There's always treasure behind walls.\"",
                    });
                    if (afterCombat) pool.Add("\"Dibs on anything shiny. I called it first.\"");
                    if (dungeonLevel >= 50) pool.Add("Vex is unusually quiet. You catch him staring at nothing.");
                    if (player.Gold > 10000) pool.Add("\"You know, for a hero, you're sitting on a lot of gold. Just observing.\"");
                    break;

                case CompanionId.Lyris:
                    pool.AddRange(new[]
                    {
                        "Lyris tilts her head, listening to something only she can hear.",
                        "\"The air currents here... they flow deeper than they should.\"",
                        "Lyris kneels, studying scratch marks on the stone. \"Something nests nearby.\"",
                        "\"Nature doesn't reach down here. That's what makes it sad.\"",
                        "Lyris nocks an arrow without looking up. \"Movement. Two corridors ahead.\"",
                        "\"These tracks are fresh. We're not the only hunters down here.\"",
                        "Lyris adjusts her bow. \"Something ahead. I can sense it.\"",
                        "\"The Deepwood used to stretch above these tunnels. I can still smell the roots.\"",
                        "Lyris tests the air with two fingers. \"Stale. Nothing's moved through here in days.\"",
                        "\"The deeper we go, the closer to the source of the corruption.\"",
                    });
                    if (afterCombat) pool.Add("Lyris retrieves her arrows from the fallen. \"Waste nothing.\"");
                    if (dungeonLevel >= 60) pool.Add("\"The corruption is stronger here. We're getting close to its heart.\"");
                    if (dungeonLevel >= 80) pool.Add("Lyris draws her bowstring tight. \"I can feel the Deepwood's pain from here.\"");
                    break;

                case CompanionId.Mira:
                    pool.AddRange(new[]
                    {
                        "Mira presses her hand against the wall. \"So much suffering in these stones.\"",
                        "\"Let me know if anyone needs healing. That's... what I'm here for.\"",
                        "Mira whispers a prayer. The shadows pull back, just slightly.",
                        "\"Every room we clear, someone doesn't have to die in it.\"",
                        "\"I used to wonder if healing was enough. Down here, it has to be.\"",
                        "Mira checks her bandages. Always prepared. Always worried.",
                        "\"Faith isn't about certainty. It's about walking into the dark anyway.\"",
                        "Mira looks at her hands. \"I've healed so many. Why can't I heal myself?\"",
                        "\"Stay safe. I only have so many miracles left today.\"",
                        "Mira smiles, barely. \"We're alive. That's today's victory.\"",
                    });
                    if (afterCombat) pool.Add("Mira kneels beside a wound. \"Hold still. This will sting.\"");
                    if (player.HP < player.MaxHP / 2) pool.Add("\"You're hurt — come here, let me look at that.\"");
                    if (dungeonLevel >= 50) pool.Add("\"The deeper we go, the more I question. Is any god worth this?\"");
                    // Veloura connection — Mira's tension builds near floor 40
                    if (dungeonLevel >= 35 && dungeonLevel < 40)
                    {
                        pool.Add("Mira's hands are trembling. She notices you looking and clasps them together.");
                        pool.Add("\"I can feel her. Veloura. The closer we get, the more I remember.\"");
                        pool.Add("\"I used to sing hymns to her every morning. Now I can barely say her name.\"");
                    }
                    if (dungeonLevel == 40)
                    {
                        pool.Clear(); // Only Veloura-specific comments on her floor
                        pool.Add("Mira has gone pale. \"She's here. I can feel the corruption like a fever.\"");
                        pool.Add("\"I spent ten years in her temple. Ten years of prayers she never answered.\"");
                        pool.Add("\"Whatever we find down here... whatever she's become... I need to see it.\"");
                    }
                    var story = StoryProgressionSystem.Instance;
                    if (dungeonLevel > 40 && story.HasStoryFlag("veloura_encountered"))
                    {
                        if (story.OldGodStates.TryGetValue(OldGodType.Veloura, out var vState))
                        {
                            if (vState.Status == GodStatus.Saved)
                                pool.Add("\"She's free now. Veloura is free.\" Mira wipes her eyes. \"Maybe I am too.\"");
                            else if (vState.Status == GodStatus.Defeated)
                                pool.Add("Mira is quiet. \"I thought killing her would feel like justice. It just feels like grief.\"");
                        }
                    }
                    break;

                case CompanionId.Melodia:
                    pool.AddRange(new[]
                    {
                        "Melodia hums softly. The acoustics down here are surprisingly good.",
                        "\"Every dungeon has a rhythm. Listen — can you hear it?\"",
                        "Melodia taps a wall. \"E flat. This whole corridor is in E flat.\"",
                        "\"My old party used to sing in places like this. Before...\"",
                        "Melodia adjusts her instrument. \"Music soothes monsters too. Sometimes.\"",
                        "\"I'm composing something. In my head. For when we get out.\"",
                        "Melodia pauses at an echo. \"Beautiful. Terrible. Both.\"",
                        "\"They say Thorgrim once played a war drum that shook mountains.\"",
                        "Melodia strums a quiet chord. The darkness seems less heavy.",
                        "\"A song for the fallen. I keep adding verses.\"",
                    });
                    if (afterCombat) pool.Add("Melodia plays a brief requiem. \"For those who fell here before us.\"");
                    if (dungeonLevel >= 50) pool.Add("\"The Lost Opus... I can almost hear it echoing from deeper down.\"");
                    if (dungeonLevel >= 70) pool.Add("\"The Old Gods had their own songs. Terrible, beautiful songs.\"");
                    break;
            }

            if (pool.Count == 0) return null;

            // Filter out recently used comments
            var available = pool.Where(c => !_recentIdleComments.Contains(c)).ToList();
            if (available.Count == 0)
            {
                // All used — reset and allow all again
                _recentIdleComments.Clear();
                available = pool;
            }

            var comment = available[Random.Shared.Next(available.Count)];
            _recentIdleComments.Add(comment);
            return comment;
        }

        #endregion

        #region Helpers

        private int GetGameDay()
        {
            // Would integrate with game's day counter
            return StoryProgressionSystem.Instance.CurrentGameDay;
        }

        /// <summary>
        /// v0.57.4: when a companion dies permanently (either via combat KillCompanion
        /// or the Moral Paradox trigger), hand back both their equipped gear AND
        /// anything in their personal bag to the player. Pre-fix, only equipped
        /// items transferred — bag contents (potions, herbs, unused loot) were
        /// lost forever.
        ///
        /// Overflow past the player's MaxInventoryItems cap is reported but not
        /// saved anywhere (treat as "dropped on the battlefield"). Clears both
        /// collections on the companion after transfer so the paths that persist
        /// dead companions don't keep ghost inventory.
        /// </summary>
        private void ReturnCompanionGearOnDeath(Companion companion, TerminalEmulator? terminal)
        {
            var player = GameEngine.Instance?.CurrentPlayer;
            int equipmentReturned = 0;
            int bagReturned = 0;
            int dropped = 0;

            // 1. Equipment (same logic as the original KillCompanion path, but
            // centralized). Iterate a snapshot so we can Clear afterwards safely.
            if (player != null && companion.EquippedItems.Count > 0)
            {
                foreach (var kvp in companion.EquippedItems.ToList())
                {
                    if (kvp.Value <= 0) continue;
                    var equipment = EquipmentDatabase.GetById(kvp.Value);
                    if (equipment == null) continue;
                    if (player.Inventory.Count >= GameConfig.MaxInventoryItems)
                    {
                        dropped++;
                        continue;
                    }
                    player.Inventory.Add(player.ConvertEquipmentToLegacyItem(equipment));
                    equipmentReturned++;
                }
            }
            companion.EquippedItems.Clear();

            // 2. Personal bag (NEW in v0.57.4 — was never returned).
            if (player != null && companion.Inventory != null && companion.Inventory.Count > 0)
            {
                foreach (var item in companion.Inventory.ToList())
                {
                    if (item == null) continue;
                    if (player.Inventory.Count >= GameConfig.MaxInventoryItems)
                    {
                        dropped++;
                        continue;
                    }
                    player.Inventory.Add(item);
                    bagReturned++;
                }
            }
            companion.Inventory?.Clear();

            // 3. Report. The loc key `companion.equipment_returned` takes two args
            // (name, count) and has always meant "gear returned" — reuse it, summing
            // equipment + bag items, since from the player's POV it's one pile.
            int total = equipmentReturned + bagReturned;
            if (total > 0)
            {
                string msg = Loc.Get("companion.equipment_returned", companion.Name, total);
                if (terminal != null)
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine(msg);
                }
                else
                {
                    pendingNotifications.Enqueue(Loc.Get("companion.equipment_returned_notify", companion.Name, total));
                }
            }
            if (dropped > 0)
            {
                string drop = $"{dropped} item(s) dropped on the battlefield — your inventory was full.";
                if (terminal != null)
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine(drop);
                }
                else
                {
                    pendingNotifications.Enqueue(drop);
                }
            }
            // v0.57.4: single forensic line covering the whole permadeath bag return.
            // If a player reports "I lost X legendary items when Vex died", this log
            // line answers the question immediately — how many equipped, how many
            // were in the bag, how many dropped from the player's cap.
            DebugLogger.Instance.LogInfo("COMPANION_BAG",
                $"PERMADEATH {companion.Name} → player: equipped={equipmentReturned}, bag={bagReturned}, dropped={dropped}");
        }

        /// <summary>
        /// Convert in-memory Item to serializable InventoryItemData (same shape the player
        /// uses, so companion bags round-trip through JSON like the player's does).
        /// </summary>
        private static InventoryItemData ItemToData(Item item)
        {
            return new InventoryItemData
            {
                Name = item.Name,
                Value = item.Value,
                Type = item.Type,
                Attack = item.Attack,
                Armor = item.Armor,
                Strength = item.Strength,
                Dexterity = item.Dexterity,
                Wisdom = item.Wisdom,
                Defence = item.Defence,
                BlockChance = item.BlockChance,
                ShieldBonus = item.ShieldBonus,
                HP = item.HP,
                Mana = item.Mana,
                Charisma = item.Charisma,
                Agility = item.Agility,
                Stamina = item.Stamina,
                MinLevel = item.MinLevel,
                IsCursed = item.IsCursed,
                Cursed = item.Cursed,
                IsIdentified = item.IsIdentified,
                Shop = item.Shop,
                Dungeon = item.Dungeon,
                Description = item.Description?.ToList() ?? new List<string>(),
                LootEffects = item.LootEffects?.Count > 0
                    ? item.LootEffects.Select(e => new LootEffectData { EffectType = e.EffectType, Value = e.Value }).ToList()
                    : null
            };
        }

        private static Item DataToItem(InventoryItemData d)
        {
            var item = new Item
            {
                Name = d.Name,
                Value = d.Value,
                Type = d.Type,
                Attack = d.Attack,
                Armor = d.Armor,
                Strength = d.Strength,
                Dexterity = d.Dexterity,
                Wisdom = d.Wisdom,
                Defence = d.Defence,
                BlockChance = d.BlockChance,
                ShieldBonus = d.ShieldBonus,
                HP = d.HP,
                Mana = d.Mana,
                Charisma = d.Charisma,
                Agility = d.Agility,
                Stamina = d.Stamina,
                MinLevel = d.MinLevel,
                IsCursed = d.IsCursed,
                Cursed = d.Cursed,
                IsIdentified = d.IsIdentified,
                Shop = d.Shop,
                Dungeon = d.Dungeon,
                Description = d.Description?.ToList() ?? new List<string>()
            };
            if (d.LootEffects != null)
                item.LootEffects = d.LootEffects.Select(e => (e.EffectType, e.Value)).ToList();
            return item;
        }

        private int GetPlayerLevel()
        {
            // Get actual player level from GameEngine
            var player = GameEngine.Instance?.CurrentPlayer;
            return player?.Level ?? 1;
        }

        #endregion
    }

    #region Companion Data Classes

    public enum CompanionId
    {
        Lyris,
        Aldric,
        Mira,
        Vex,
        Melodia
    }

    public enum CompanionType
    {
        Combat,
        Support,
        Romance,
        Utility
    }

    public enum CombatRole
    {
        Tank,
        Damage,
        Healer,
        Hybrid,
        Bard
    }

    public enum DeathType
    {
        Combat,         // Died in battle
        Sacrifice,      // Chose to die for player
        ChoiceBased,    // Player made a choice
        MoralTrigger,   // Triggered by player's alignment
        QuestRelated,   // Died completing quest
        Inevitable,     // Scripted/unavoidable
        TimeBased       // Time-based trigger (disease, curse)
    }

    public enum CompanionEventType
    {
        Recruited,
        LoyaltyChange,
        TrustChange,
        RomanceAdvanced,
        QuestStarted,
        QuestCompleted,
        NearDeath,
        Saved,
        Conflict,
        Conversation
    }

    public class Companion
    {
        public CompanionId Id { get; set; }
        public string Name { get; set; } = "";
        public string Title { get; set; } = "";
        public CompanionType Type { get; set; }
        public string Description { get; set; } = "";
        public string BackstoryBrief { get; set; } = "";

        public int RecruitLevel { get; set; }
        public string RecruitLocation { get; set; } = "";

        public CompanionStats BaseStats { get; set; } = new();
        public CombatRole CombatRole { get; set; }
        public string[] Abilities { get; set; } = Array.Empty<string>();

        public string PersonalQuestName { get; set; } = "";
        public string PersonalQuestDescription { get; set; } = "";
        public bool PersonalQuestAvailable { get; set; } // Unlocks at 50 loyalty
        public bool PersonalQuestStarted { get; set; }   // Player accepted the quest
        public bool PersonalQuestCompleted { get; set; }
        public bool PersonalQuestSuccess { get; set; }
        public string PersonalQuestLocationHint { get; set; } = ""; // Where to complete

        public bool RomanceAvailable { get; set; }
        public bool CanDiePermanently { get; set; }

        public Dictionary<DeathType, string> DeathTriggers { get; set; } = new();
        public string[] DialogueHints { get; set; } = Array.Empty<string>();

        public int OceanPhilosophyAwareness { get; set; } = 0;
        public int LoyaltyThreshold { get; set; } = 80;
        public int DarknessThreshold { get; set; } = 5000;
        public bool HasTimedDeath { get; set; }
        public int DaysUntilDeath { get; set; }
        public bool TeachesLettingGo { get; set; }
        public bool TeachesAcceptance { get; set; }

        // Runtime state
        public bool IsRecruited { get; set; }
        public bool IsActive { get; set; }
        public bool IsDead { get; set; }
        public DeathType? DeathType { get; set; }
        public int LoyaltyLevel { get; set; } = 50;
        public int TrustLevel { get; set; } = 50;
        public int RomanceLevel { get; set; } = 0;
        public bool RomancedToday { get; set; } = false;
        public int RecruitedDay { get; set; }
        public DateTime RecruitedDate { get; set; } = DateTime.MinValue;

        // Experience and leveling
        public int Level { get; set; } = 1;
        public long Experience { get; set; } = 0;

        // Secondary stats (tracked separately from BaseStats, used in combat wrapper)
        public int Constitution { get; set; } = 10;
        public int Intelligence { get; set; } = 10;
        public int Wisdom { get; set; } = 10;
        public int Charisma { get; set; } = 10;
        public int Dexterity { get; set; } = 10;
        public int Agility { get; set; } = 10;

        // Healing potions (NPCs manage their own supply)
        public int HealingPotions { get; set; } = 0;
        public int MaxHealingPotions => 5 + Level;

        // Mana potions (caster companions manage their own supply)
        public int ManaPotions { get; set; } = 0;
        public int MaxManaPotions => 3 + Level / 2;

        // Equipment system - maps slot to equipment database ID (same as Character.EquippedItems)
        public Dictionary<EquipmentSlot, int> EquippedItems { get; set; } = new();

        // Companion's personal inventory ("bag"). Shared by reference with the Character wrapper
        // returned from GetCompanionsAsCharacters so items transferred to the companion in combat
        // (via <>/T transfer), Home/Team Corner/dungeon viewers, and loot persist on the companion.
        public List<Item> Inventory { get; set; } = new();

        // Disabled abilities - player can toggle off specific abilities via Inn menu
        // Companion AI will skip any ability whose Id is in this set
        public HashSet<string> DisabledAbilities { get; set; } = new();

        // v0.57.11: disabled spells — parallel to DisabledAbilities but for
        // the spell system. Player reported (Glamdring) that Mira was spamming
        // Mass Cure even with every ability turned OFF at the Inn, because the
        // Inn UI only manages class abilities, not spells. The spell-selection
        // helpers `GetBestHealSpell` / `GetBestOffensiveSpell` in CombatEngine
        // filter candidates against this set by spell Name. Empty by default.
        public HashSet<string> DisabledSpells { get; set; } = new();

        // Skill proficiency (stored as int values of TrainingSystem.ProficiencyLevel)
        public Dictionary<string, int> SkillProficiencies { get; set; } = new();
        public Dictionary<string, int> SkillTrainingProgress { get; set; } = new();

        public List<CompanionEvent> History { get; set; } = new();

        public void AddHistory(CompanionEvent evt)
        {
            History.Add(evt);
            if (History.Count > 100)
                History.RemoveAt(0);
        }
    }

    public class CompanionStats
    {
        public int HP { get; set; }
        public int Attack { get; set; }
        public int Defense { get; set; }
        public int MagicPower { get; set; }
        public int Speed { get; set; }
        public int HealingPower { get; set; }
    }

    public class CompanionEvent
    {
        public CompanionEventType Type { get; set; }
        public string Description { get; set; } = "";
        public int LoyaltyChange { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class CompanionDeath
    {
        public CompanionId CompanionId { get; set; }
        public DeathType Type { get; set; }
        public string Circumstance { get; set; } = "";
        public string LastWords { get; set; } = "";
        public int DeathDay { get; set; }
        public int PlayerLevel { get; set; }
    }

    public class DeathTriggerCheck
    {
        public CompanionId? TriggeredCompanion { get; set; }
        public DeathType? TriggerType { get; set; }
        public string TriggerReason { get; set; } = "";
    }

    public class CompanionSystemData
    {
        public List<CompanionSaveData> CompanionStates { get; set; } = new();
        public List<CompanionId> ActiveCompanions { get; set; } = new();
        public List<CompanionDeath> FallenCompanions { get; set; } = new();
    }

    public class CompanionSaveData
    {
        public CompanionId Id { get; set; }
        public bool IsRecruited { get; set; }
        public bool IsActive { get; set; }
        public bool IsDead { get; set; }
        public int LoyaltyLevel { get; set; }
        public int TrustLevel { get; set; }
        public int RomanceLevel { get; set; }
        public bool PersonalQuestStarted { get; set; }
        public bool PersonalQuestCompleted { get; set; }
        public bool PersonalQuestSuccess { get; set; }
        public int RecruitedDay { get; set; }
        public DateTime RecruitedDate { get; set; }
        public DeathType? DeathType { get; set; }
        public List<CompanionEvent> History { get; set; } = new();
        public int HealingPotions { get; set; }
        public int ManaPotions { get; set; }

        // Level and experience
        public int Level { get; set; }
        public long Experience { get; set; }

        // Base stats (to preserve level-up gains)
        public int BaseStatsHP { get; set; }
        public int BaseStatsAttack { get; set; }
        public int BaseStatsDefense { get; set; }
        public int BaseStatsMagicPower { get; set; }
        public int BaseStatsSpeed { get; set; }
        public int BaseStatsHealingPower { get; set; }

        // Secondary stats
        public int Constitution { get; set; } = 10;
        public int Intelligence { get; set; } = 10;
        public int Wisdom { get; set; } = 10;
        public int Charisma { get; set; } = 10;
        public int Dexterity { get; set; } = 10;
        public int Agility { get; set; } = 10;

        // Equipment (slot enum int -> equipment database ID)
        public Dictionary<int, int> EquippedItemsSave { get; set; } = new();

        // Disabled ability IDs
        public List<string> DisabledAbilities { get; set; } = new();

        // v0.57.11: disabled spell names (parallel to DisabledAbilities)
        public List<string> DisabledSpells { get; set; } = new();

        // Skill proficiency (stored as int values of TrainingSystem.ProficiencyLevel)
        public Dictionary<string, int> SkillProficiencies { get; set; } = new();
        public Dictionary<string, int> SkillTrainingProgress { get; set; } = new();

        // Companion's personal inventory ("bag"). Items transferred to the companion via
        // combat loot, Home/Team Corner equip, or Party Inventory viewer.
        public List<InventoryItemData> Inventory { get; set; } = new();
    }

    #endregion
}
