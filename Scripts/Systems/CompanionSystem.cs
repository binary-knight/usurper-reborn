using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
                Title = "The Wandering Star",
                Type = CompanionType.Romance,

                Description = "A traveler with silver-streaked hair and a habit of staring at you like she " +
                             "knows something you dont. Speaks in riddles. Knows too much.",

                BackstoryBrief = "Lyris was once a priestess of Aurelion, the god of light. When Manwe corrupted " +
                                "the Old Gods, she was cast out, neither fully mortal nor divine. She wanders " +
                                "endlessly, seeking a way to heal the gods she once served.",

                RecruitLevel = 15,
                RecruitLocation = "Dungeon Level 15 - Forgotten Shrine",

                BaseStats = new CompanionStats
                {
                    HP = 200,
                    Attack = 25,
                    Defense = 15,
                    MagicPower = 50,
                    Speed = 35,
                    HealingPower = 30
                },

                CombatRole = CombatRole.Hybrid,
                Abilities = new[] { "Starlight Heal", "Divine Smite", "Aurora Shield", "Sacrifice" },

                PersonalQuestName = "The Light That Was",
                PersonalQuestDescription = "Help Lyris recover an artifact that could restore Aurelion's true nature.",
                PersonalQuestLocationHint = "Dungeon floors 80-90 (near Aurelion's domain)",

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
                Abilities = new[] { "Shield Wall", "Taunt", "Last Stand", "Sacrifice" },

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
                Abilities = new[] { "Greater Heal", "Purify", "Sanctuary", "Final Sacrifice" },

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
                Abilities = new[] { "Backstab", "Smoke Bomb", "Disarm Trap", "Last Laugh" },

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
                .Where(c => c != null)!;
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
                terminal.WriteLine($"{companion.Name} doesn't think you're ready yet.", "yellow");
                return false;
            }

            // Display recruitment scene
            await DisplayRecruitmentScene(companion, terminal);

            companion.IsRecruited = true;
            companion.RecruitedDay = GetGameDay();

            // Initialize companion's level and scale stats
            InitializeCompanionLevel(companion);

            // Auto-add to active if room
            if (activeCompanions.Count < MaxActiveCompanions)
            {
                activeCompanions.Add(id);
                companion.IsActive = true;
                terminal.WriteLine($"{companion.Name} joins your party!", "bright_green");
            }
            else
            {
                terminal.WriteLine($"{companion.Name} will wait for you at the tavern.", "cyan");
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

                // Initialize HP if needed
                if (!companionCurrentHP.ContainsKey(companion.Id))
                {
                    companionCurrentHP[companion.Id] = companion.BaseStats.HP;
                }

                var charWrapper = new Character
                {
                    Name2 = companion.Name,
                    Level = companion.Level,
                    Healing = companion.HealingPotions,
                    Class = companion.CombatRole switch
                    {
                        CombatRole.Tank => CharacterClass.Warrior,
                        CombatRole.Healer => CharacterClass.Cleric,
                        CombatRole.Damage => CharacterClass.Assassin,
                        CombatRole.Hybrid => CharacterClass.Paladin,
                        _ => CharacterClass.Warrior
                    },
                    // Initialize Base* fields for RecalculateStats
                    BaseStrength = companion.BaseStats.Attack,
                    BaseDefence = companion.BaseStats.Defense,
                    BaseDexterity = companion.BaseStats.Speed,
                    BaseAgility = companion.BaseStats.Speed,
                    BaseIntelligence = companion.BaseStats.MagicPower,
                    BaseWisdom = companion.BaseStats.HealingPower,
                    BaseCharisma = 10,
                    BaseConstitution = 10 + companion.Level,
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

                // RecalculateStats applies equipment bonuses on top of Base* values
                charWrapper.RecalculateStats();

                // Restore tracked combat HP (don't heal to full mid-combat)
                charWrapper.HP = companionCurrentHP[companion.Id];
                charWrapper.Mana = companion.BaseStats.MagicPower * 5;

                // Store companion ID in the character for tracking
                charWrapper.CompanionId = companion.Id;
                charWrapper.IsCompanion = true;

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
                companionCurrentHP[id] = companion.BaseStats.HP;

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
                companionCurrentHP[id] = companion.BaseStats.HP;

            companionCurrentHP[id] = Math.Min(companion.BaseStats.HP, companionCurrentHP[id] + amount);
        }

        /// <summary>
        /// Get current HP for a companion
        /// </summary>
        public int GetCompanionHP(CompanionId id)
        {
            if (!companionCurrentHP.ContainsKey(id))
            {
                if (companions.TryGetValue(id, out var c))
                    companionCurrentHP[id] = c.BaseStats.HP;
                else
                    return 0;
            }
            return companionCurrentHP[id];
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
                    companionCurrentHP[companion.Id] = companion.BaseStats.HP;
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
                    companion.EquippedItems.Clear();
                    foreach (var kvp in charWrapper.EquippedItems)
                    {
                        companion.EquippedItems[kvp.Key] = kvp.Value;
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
                if (!companion.Abilities.Contains("Sacrifice")) continue;

                // Check if companion has enough loyalty/trust to sacrifice
                // Higher loyalty = more likely to sacrifice
                int sacrificeChance = companion.LoyaltyLevel;

                // Aldric always sacrifices if loyalty > 50 (his nature)
                if (companion.Id == CompanionId.Aldric && companion.LoyaltyLevel > 50)
                    sacrificeChance = 100;

                // Romance increases sacrifice chance
                if (companion.RomanceLevel > 5)
                    sacrificeChance += 30;

                var random = new Random();
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
                // Godot.GD.Print($"[Companion] {companion.Name}'s personal quest unlocked: {companion.PersonalQuestName}");

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

            companion.IsDead = true;
            companion.IsActive = false;
            companion.DeathType = type;

            // Return companion's equipment to player inventory
            if (companion.EquippedItems.Count > 0)
            {
                var player = GameEngine.Instance?.CurrentPlayer;
                if (player != null)
                {
                    int itemsReturned = 0;
                    foreach (var kvp in companion.EquippedItems)
                    {
                        if (kvp.Value <= 0) continue;
                        var equipment = EquipmentDatabase.GetById(kvp.Value);
                        if (equipment != null)
                        {
                            player.Inventory.Add(new global::Item
                            {
                                Name = equipment.Name,
                                Type = ObjType.Magic,
                                Value = equipment.Value,
                                Attack = equipment.WeaponPower,
                                Armor = equipment.ArmorClass,
                                Strength = equipment.StrengthBonus,
                                Dexterity = equipment.DexterityBonus,
                                HP = equipment.MaxHPBonus,
                                Mana = equipment.MaxManaBonus,
                                Defence = equipment.DefenceBonus,
                                IsCursed = equipment.IsCursed,
                                MinLevel = equipment.MinLevel
                            });
                            itemsReturned++;
                        }
                    }
                    companion.EquippedItems.Clear();
                    if (itemsReturned > 0 && terminal != null)
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine($"\n{companion.Name}'s equipment ({itemsReturned} item{(itemsReturned != 1 ? "s" : "")}) has been returned to your inventory.");
                    }
                }
            }

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

            // Trigger Ocean Philosophy awakening
            if (!OceanPhilosophySystem.Instance.ExperiencedMoments.Contains(AwakeningMoment.FirstCompanionDeath))
            {
                OceanPhilosophySystem.Instance.ExperienceMoment(AwakeningMoment.FirstCompanionDeath);
            }

            OnCompanionDeath?.Invoke(id, type);

            // Queue Stranger encounter for companion death
            StrangerEncounterSystem.Instance.QueueScriptedEncounter(ScriptedEncounterType.AfterCompanionDeath);
            StrangerEncounterSystem.Instance.RecordGameEvent(StrangerContextEvent.CompanionDied);
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
                int daysWithVex = GetGameDay() - vex.RecruitedDay;
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

            // Return companion's equipment to player inventory
            if (companion.EquippedItems.Count > 0)
            {
                var player = GameEngine.Instance?.CurrentPlayer;
                if (player != null)
                {
                    int itemsReturned = 0;
                    foreach (var kvp in companion.EquippedItems)
                    {
                        if (kvp.Value <= 0) continue;
                        var equipment = EquipmentDatabase.GetById(kvp.Value);
                        if (equipment != null)
                        {
                            player.Inventory.Add(new global::Item
                            {
                                Name = equipment.Name,
                                Type = ObjType.Magic,
                                Value = equipment.Value,
                                Attack = equipment.WeaponPower,
                                Armor = equipment.ArmorClass
                            });
                            itemsReturned++;
                        }
                    }
                    companion.EquippedItems.Clear();
                    if (itemsReturned > 0)
                    {
                        pendingNotifications.Enqueue($"{companion.Name}'s equipment ({itemsReturned} item{(itemsReturned != 1 ? "s" : "")}) has been returned to your inventory.");
                    }
                }
            }

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
        }

        #endregion

        #region Display Methods

        private async Task DisplayRecruitmentScene(Companion companion, TerminalEmulator terminal)
        {
            terminal.Clear();
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════╗", "bright_cyan");
            terminal.WriteLine("║                  N E W   C O M P A N I O N                       ║", "bright_cyan");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════╝", "bright_cyan");
            terminal.WriteLine("");

            terminal.WriteLine($"  {companion.Name}", "bright_white");
            terminal.WriteLine($"  \"{companion.Title}\"", "cyan");
            terminal.WriteLine("");

            terminal.WriteLine(companion.Description, "white");
            terminal.WriteLine("");

            terminal.WriteLine($"  Role: {companion.CombatRole}", "yellow");
            terminal.WriteLine($"  Abilities: {string.Join(", ", companion.Abilities)}", "yellow");
            terminal.WriteLine("");

            // Show a hint of their deeper story
            if (companion.DialogueHints.Length > 0)
            {
                terminal.WriteLine($"  \"{companion.DialogueHints[0]}\"", "dark_cyan");
            }

            await terminal.GetInputAsync("\nPress Enter to welcome them...");
        }

        private async Task DisplayDeathScene(Companion companion, DeathType type, string circumstance, TerminalEmulator terminal)
        {
            terminal.Clear();
            await Task.Delay(1500);

            // Slow, solemn header
            terminal.WriteLine("");
            await Task.Delay(500);
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════╗", "dark_red");
            terminal.WriteLine("║                                                                    ║", "dark_red");
            terminal.WriteLine("║                    F   A   L   L   E   N                          ║", "dark_red");
            terminal.WriteLine("║                                                                    ║", "dark_red");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════╝", "dark_red");
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
            terminal.WriteLine($"  {companion.Name} goes still.");
            await Task.Delay(1200);
            terminal.WriteLine("  Thats it. Theyre gone.");
            terminal.WriteLine("");

            await Task.Delay(2000);

            // Philosophical moment based on companion and death type
            await DisplayDeathPhilosophy(companion, type, terminal);

            await Task.Delay(1500);

            // Final message
            terminal.SetColor("gray");
            terminal.WriteLine($"  {companion.Name} is dead.");
            terminal.WriteLine("  No coming back from this one.");
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

            await terminal.GetInputAsync("  Press Enter when you are ready...");
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
            // Log companion levels being saved for debugging
            foreach (var c in companions.Values.Where(c => c.IsRecruited))
            {
                DebugLogger.Instance.LogDebug("COMPANION", $"Serializing {c.Name}: Level={c.Level}, XP={c.Experience}");
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
                    DeathType = c.DeathType,
                    History = c.History.ToList(),
                    Level = c.Level,
                    Experience = c.Experience,
                    BaseStatsHP = c.BaseStats.HP,
                    BaseStatsAttack = c.BaseStats.Attack,
                    BaseStatsDefense = c.BaseStats.Defense,
                    BaseStatsMagicPower = c.BaseStats.MagicPower,
                    BaseStatsSpeed = c.BaseStats.Speed,
                    BaseStatsHealingPower = c.BaseStats.HealingPower,
                    EquippedItemsSave = c.EquippedItems.ToDictionary(kvp => (int)kvp.Key, kvp => kvp.Value),
                    DisabledAbilities = c.DisabledAbilities.ToList()
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
                companion.DisabledAbilities.Clear();
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
                    companion.DeathType = save.DeathType;
                    companion.History = save.History?.ToList() ?? new List<CompanionEvent>();

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
                        // Godot.GD.Print($"[Companion] Initialized {companion.Name}'s XP to {companion.Experience} for level {companion.Level}");
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
                    }

                    // Restore disabled abilities
                    companion.DisabledAbilities.Clear();
                    if (save.DisabledAbilities != null)
                    {
                        foreach (var id in save.DisabledAbilities)
                            companion.DisabledAbilities.Add(id);
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
                terminal.WriteLine($"Companion XP (+{companionXP} each):");
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
        /// Check if a companion should level up and apply stat gains
        /// </summary>
        private void CheckCompanionLevelUp(Companion companion, TerminalEmulator? terminal)
        {
            if (companion.Level >= 100) return;

            long xpForNextLevel = GetExperienceForLevel(companion.Level + 1);

            while (companion.Experience >= xpForNextLevel && companion.Level < 100)
            {
                int oldLevel = companion.Level;
                companion.Level++;

                // Log companion level up
                DebugLogger.Instance.LogCompanionLevelUp(companion.Name, oldLevel, companion.Level);

                // Apply stat gains based on combat role
                ApplyCompanionLevelUpStats(companion);

                terminal?.SetColor("bright_green");
                terminal?.WriteLine($"  {companion.Name} has reached level {companion.Level}!");

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
                    wrapper.BaseDexterity = companion.BaseStats.Speed;
                    wrapper.BaseAgility = companion.BaseStats.Speed;
                    wrapper.BaseIntelligence = companion.BaseStats.MagicPower;
                    wrapper.BaseWisdom = companion.BaseStats.HealingPower;
                    wrapper.BaseConstitution = 10 + companion.Level;
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
            var random = new Random();

            // Base HP gain
            int hpGain = 8 + random.Next(4, 12);

            // Role-specific stat gains
            switch (companion.CombatRole)
            {
                case CombatRole.Tank:
                    // Tanks get extra HP and Defense
                    hpGain += 5;
                    companion.BaseStats.HP += hpGain;
                    companion.BaseStats.Defense += 2 + random.Next(0, 2);
                    companion.BaseStats.Attack += 1 + random.Next(0, 2);
                    break;

                case CombatRole.Damage:
                    // Damage dealers get Attack and Speed
                    companion.BaseStats.HP += hpGain;
                    companion.BaseStats.Attack += 2 + random.Next(1, 3);
                    companion.BaseStats.Speed += 1 + random.Next(0, 2);
                    companion.BaseStats.Defense += 1;
                    break;

                case CombatRole.Healer:
                    // Healers get Healing Power and Magic
                    companion.BaseStats.HP += hpGain;
                    companion.BaseStats.HealingPower += 3 + random.Next(1, 3);
                    companion.BaseStats.MagicPower += 2 + random.Next(0, 2);
                    companion.BaseStats.Defense += 1;
                    break;

                case CombatRole.Hybrid:
                    // Hybrids get balanced gains
                    companion.BaseStats.HP += hpGain;
                    companion.BaseStats.Attack += 1 + random.Next(0, 2);
                    companion.BaseStats.Defense += 1 + random.Next(0, 2);
                    companion.BaseStats.MagicPower += 2 + random.Next(0, 2);
                    companion.BaseStats.HealingPower += 1 + random.Next(0, 2);
                    break;
            }

            // Update current HP tracking to new max
            if (companionCurrentHP.ContainsKey(companion.Id))
            {
                // Heal to full on level up
                companionCurrentHP[companion.Id] = companion.BaseStats.HP;
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
        }

        /// <summary>
        /// Refill a companion's healing potions (called on recruit, rest, new day)
        /// </summary>
        public void RefillCompanionPotions(Companion companion)
        {
            // Companions get potions based on level - healers get fewer since they use spells
            int basePotions = companion.CombatRole == CombatRole.Healer ? 2 : 5;
            companion.HealingPotions = Math.Min(basePotions + companion.Level / 2, companion.MaxHealingPotions);
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
                int hpGain = 8 + random.Next(4, 12);
                switch (companion.CombatRole)
                {
                    case CombatRole.Tank:
                        companion.BaseStats.HP += hpGain + 5;
                        companion.BaseStats.Defense += 2;
                        companion.BaseStats.Attack += 1;
                        break;
                    case CombatRole.Damage:
                        companion.BaseStats.HP += hpGain;
                        companion.BaseStats.Attack += 2;
                        companion.BaseStats.Speed += 1;
                        break;
                    case CombatRole.Healer:
                        companion.BaseStats.HP += hpGain;
                        companion.BaseStats.HealingPower += 3;
                        companion.BaseStats.MagicPower += 2;
                        break;
                    case CombatRole.Hybrid:
                        companion.BaseStats.HP += hpGain;
                        companion.BaseStats.Attack += 1;
                        companion.BaseStats.MagicPower += 2;
                        companion.BaseStats.HealingPower += 1;
                        break;
                }
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

        #region Helpers

        private int GetGameDay()
        {
            // Would integrate with game's day counter
            return StoryProgressionSystem.Instance.CurrentGameDay;
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
        Vex
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
        Hybrid
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

        // Experience and leveling
        public int Level { get; set; } = 1;
        public long Experience { get; set; } = 0;

        // Healing potions (NPCs manage their own supply)
        public int HealingPotions { get; set; } = 0;
        public int MaxHealingPotions => 5 + Level;

        // Equipment system - maps slot to equipment database ID (same as Character.EquippedItems)
        public Dictionary<EquipmentSlot, int> EquippedItems { get; set; } = new();

        // Disabled abilities - player can toggle off specific abilities via Inn menu
        // Companion AI will skip any ability whose Id is in this set
        public HashSet<string> DisabledAbilities { get; set; } = new();

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
        public DeathType? DeathType { get; set; }
        public List<CompanionEvent> History { get; set; } = new();

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

        // Equipment (slot enum int -> equipment database ID)
        public Dictionary<int, int> EquippedItemsSave { get; set; } = new();

        // Disabled ability IDs
        public List<string> DisabledAbilities { get; set; } = new();
    }

    #endregion
}
