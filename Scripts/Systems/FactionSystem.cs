using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Faction System - Manages the three major factions in Dorashire,
    /// their beliefs, conflicts, and consequences for player alignment.
    ///
    /// The factions represent different responses to the corruption of the gods:
    /// - The Crown (Order) - believes structure and law will save humanity
    /// - The Shadows (Freedom) - believes only independence from gods matters
    /// - The Faith (Devotion) - believes in restoring the gods to their pure forms
    /// </summary>
    public class FactionSystem
    {
        private static FactionSystem? _fallbackInstance;
        public static FactionSystem Instance
        {
            get
            {
                var ctx = UsurperRemake.Server.SessionContext.Current;
                if (ctx != null) return ctx.Factions;
                return _fallbackInstance ??= new FactionSystem();
            }
        }

        // Player faction state
        public Faction? PlayerFaction { get; private set; }
        public int FactionRank { get; private set; } = 0;
        public int FactionReputation { get; private set; } = 0;

        // Standing with each faction (can be negative)
        public Dictionary<Faction, int> FactionStanding { get; private set; } = new()
        {
            [Faction.TheCrown] = 0,
            [Faction.TheShadows] = 0,
            [Faction.TheFaith] = 0
        };

        // Track faction quests completed
        public HashSet<string> CompletedFactionQuests { get; private set; } = new();

        // Track if player has betrayed their faction
        public bool HasBetrayedFaction { get; private set; } = false;
        public Faction? BetrayedFaction { get; private set; }

        /// <summary>
        /// All faction data
        /// </summary>
        public static readonly Dictionary<Faction, FactionData> Factions = new()
        {
            [Faction.TheCrown] = new FactionData
            {
                Name = "The Crown",
                Title = "The Royal Order",
                Leader = "King Aldric III",
                Location = "Castle",
                Symbol = "A golden crown over crossed swords",

                Philosophy = "Order is the foundation of civilization. Without law, without structure, " +
                           "humanity descends into chaos. The gods abandoned us - we must rule ourselves.",

                Beliefs = new[] {
                    "Law must be absolute and apply to all equally",
                    "The strong must protect the weak through duty, not choice",
                    "The gods have failed; mortal kings must guide humanity",
                    "Sacrifice of the individual for the good of the realm is noble"
                },

                Conflicts = new Dictionary<Faction, string>
                {
                    [Faction.TheShadows] = "Criminals and anarchists who would tear down everything we've built",
                    [Faction.TheFaith] = "Well-meaning fools who trust beings that abandoned us"
                },

                JoinRequirements = "Level 10+, Chivalry > 500, No criminal record",

                Ranks = new[] {
                    "Commoner", "Citizen", "Knight-Errant", "Knight", "Knight-Captain",
                    "Baron", "Count", "Duke", "Prince-Regent"
                },

                Bonuses = new FactionBonuses
                {
                    ShopDiscount = 10,
                    GuardFavor = true,
                    CastleAccess = true,
                    TaxExemption = 20
                },

                AssociatedGod = OldGodType.Thorgrim // The god of law, now corrupted
            },

            [Faction.TheShadows] = new FactionData
            {
                Name = "The Shadows",
                Title = "The Free Folk",
                Leader = "The Faceless One",
                Location = "DarkAlley",
                Symbol = "A dagger piercing a broken chain",

                Philosophy = "Freedom is the only truth. Gods, kings, laws - all chains. " +
                           "The only loyalty is to yourself and those who've earned your trust.",

                Beliefs = new[] {
                    "No being - mortal or divine - has the right to rule another",
                    "Survival belongs to the clever, not the obedient",
                    "Information is the greatest weapon",
                    "The system is rigged; we simply refuse to play by their rules"
                },

                Conflicts = new Dictionary<Faction, string>
                {
                    [Faction.TheCrown] = "Tyrants who confuse oppression with protection",
                    [Faction.TheFaith] = "Slaves begging to be chained by new masters"
                },

                JoinRequirements = "Level 10+, Darkness > 200 OR completed a 'favor'",

                Ranks = new[] {
                    "Nobody", "Informant", "Runner", "Operative", "Shadow",
                    "Whisper", "Specter", "Phantom", "Faceless"
                },

                Bonuses = new FactionBonuses
                {
                    BlackMarketAccess = true,
                    FenceBonus = 20,
                    EscapeChance = 30,
                    InformationNetwork = true
                },

                AssociatedGod = OldGodType.Noctura // The god of shadows
            },

            [Faction.TheFaith] = new FactionData
            {
                Name = "The Faith",
                Title = "The Awakened Light",
                Leader = "High Priestess Mirael",
                Location = "Temple",
                Symbol = "Seven flames forming a circle",

                Philosophy = "The gods are not evil - they are corrupted. Wounded. " +
                           "Through devotion, sacrifice, and love, we can heal them and restore balance.",

                Beliefs = new[] {
                    "The corruption can be cleansed through faith and sacrifice",
                    "Mortals and gods are meant to exist in harmony",
                    "Every soul carries a spark of the divine",
                    "The Seven Seals hold the key to restoration"
                },

                Conflicts = new Dictionary<Faction, string>
                {
                    [Faction.TheCrown] = "Faithless rulers who mistake their pride for strength",
                    [Faction.TheShadows] = "Lost souls who fear the light they desperately need"
                },

                JoinRequirements = "Level 10+, Visited Temple 3+ times, Made an offering",

                Ranks = new[] {
                    "Seeker", "Acolyte", "Initiate", "Priest/Priestess", "Elder",
                    "Confessor", "Illuminated", "Archon", "Voice of the Seven"
                },

                Bonuses = new FactionBonuses
                {
                    HealingDiscount = 25,
                    BlessingDuration = 50,
                    TempleAccess = true,
                    DivineFavor = true
                },

                AssociatedGod = OldGodType.Aurelion // The god of light, dying
            }
        };

        /// <summary>
        /// Faction-specific dialogue about the Old Gods
        /// </summary>
        public static readonly Dictionary<Faction, Dictionary<OldGodType, string>> FactionGodViews = new()
        {
            [Faction.TheCrown] = new Dictionary<OldGodType, string>
            {
                [OldGodType.Maelketh] = "A cautionary tale. Even gods fall to madness. We must be stronger.",
                [OldGodType.Veloura] = "Love is a weakness exploited by the powerful. Duty is more reliable.",
                [OldGodType.Thorgrim] = "The Hollow Judge proves that even law can be corrupted. Human law must be better.",
                [OldGodType.Noctura] = "The enemy of order. Her shadow touches every criminal in this city.",
                [OldGodType.Aurelion] = "A dying light. We cannot wait for gods to save us.",
                [OldGodType.Terravok] = "Let the mountain sleep. We have endured without divine endurance.",
                [OldGodType.Manwe] = "If the Creator cared, he would act. His silence is our mandate to rule."
            },
            [Faction.TheShadows] = new Dictionary<OldGodType, string>
            {
                [OldGodType.Maelketh] = "Endless war serves the powerful. We fight only when necessary.",
                [OldGodType.Veloura] = "Love freely given is beautiful. Love demanded by gods is manipulation.",
                [OldGodType.Thorgrim] = "Justice? His 'justice' filled our prisons. We remember.",
                [OldGodType.Noctura] = "She understands that shadows are where freedom lives. We honor her... carefully.",
                [OldGodType.Aurelion] = "Truth? The truth is that those in power decide what's true.",
                [OldGodType.Terravok] = "Even gods need sleep to escape their responsibilities.",
                [OldGodType.Manwe] = "The ultimate ruler. The ultimate target. Someday."
            },
            [Faction.TheFaith] = new Dictionary<OldGodType, string>
            {
                [OldGodType.Maelketh] = "His blade once defended the innocent. We pray for his healing.",
                [OldGodType.Veloura] = "She taught us that love is the strongest force. We will not abandon her.",
                [OldGodType.Thorgrim] = "True justice still exists within him. The corruption is not HIM.",
                [OldGodType.Noctura] = "Even shadow serves a purpose. But we must be wary of her manipulations.",
                [OldGodType.Aurelion] = "Our Lord of Light fades. Every prayer, every candle - we keep him alive.",
                [OldGodType.Terravok] = "When he wakes, the world will shake. We must be ready.",
                [OldGodType.Manwe] = "The Father suffers more than any of us know. We must reach him."
            }
        };

        public FactionSystem()
        {
            _fallbackInstance = this;
        }

        /// <summary>
        /// Check if player meets requirements to join a faction
        /// </summary>
        public (bool canJoin, string reason) CanJoinFaction(Faction faction, Character player)
        {
            if (PlayerFaction != null)
            {
                if (PlayerFaction == faction)
                    return (false, "You are already a member of this faction.");
                return (false, $"You must leave {Factions[PlayerFaction.Value].Name} first.");
            }

            if (player.Level < 10)
                return (false, "You must reach Level 10 before joining any faction.");

            // Check if player has a criminal record (high Darkness indicates criminal activity)
            bool isCriminal = player.Darkness > 500;

            return faction switch
            {
                Faction.TheCrown => player.Chivalry > 500 && !isCriminal
                    ? (true, "")
                    : (false, isCriminal
                        ? "Those with a dark reputation cannot join The Crown."
                        : "You need higher Chivalry (500+) to join The Crown."),

                Faction.TheShadows => player.Darkness > 200 || CompletedFactionQuests.Contains("shadows_favor")
                    ? (true, "")
                    : (false, "The Shadows require proof of your... flexibility. Darkness 200+ or complete a favor."),

                Faction.TheFaith => FactionStanding[Faction.TheFaith] >= 100
                    ? (true, "")
                    : (false, "The Faith requires devotion. Visit the Temple, make offerings, prove your faith."),

                _ => (false, "Unknown faction.")
            };
        }

        /// <summary>
        /// Join a faction
        /// </summary>
        public void JoinFaction(Faction faction, Character player)
        {
            var (canJoin, _) = CanJoinFaction(faction, player);
            if (!canJoin) return;

            PlayerFaction = faction;
            FactionRank = 0;
            FactionReputation = 0;

            // Set standing with joined faction
            FactionStanding[faction] = Math.Max(FactionStanding[faction], 100) + 100;

            // Apply faction-specific hostility effects
            switch (faction)
            {
                case Faction.TheFaith:
                    // Faith and Shadows are mortal enemies
                    FactionStanding[Faction.TheShadows] = Math.Min(FactionStanding[Faction.TheShadows], -100);
                    // Crown is neutral-ish to Faith
                    FactionStanding[Faction.TheCrown] -= 25;
                    break;

                case Faction.TheShadows:
                    // Shadows and Faith are mortal enemies
                    FactionStanding[Faction.TheFaith] = Math.Min(FactionStanding[Faction.TheFaith], -100);
                    // Shadows hate the Crown (anti-government)
                    FactionStanding[Faction.TheCrown] = Math.Min(FactionStanding[Faction.TheCrown], -75);
                    break;

                case Faction.TheCrown:
                    // Crown and Shadows don't get along (anti-government)
                    FactionStanding[Faction.TheShadows] = Math.Min(FactionStanding[Faction.TheShadows], -75);
                    // Crown is relatively neutral to Faith
                    FactionStanding[Faction.TheFaith] -= 25;
                    break;
            }

            // Set story flag
            switch (faction)
            {
                case Faction.TheCrown:
                    StoryProgressionSystem.Instance?.SetStoryFlag("joined_crown", true);
                    break;
                case Faction.TheShadows:
                    StoryProgressionSystem.Instance?.SetFlag(StoryFlag.JoinedGang);
                    break;
                case Faction.TheFaith:
                    StoryProgressionSystem.Instance?.SetFlag(StoryFlag.JoinedChurch);
                    break;
            }

            GD.Print($"[Faction] Player joined {faction}");
        }

        /// <summary>
        /// Leave current faction
        /// </summary>
        public void LeaveFaction()
        {
            if (PlayerFaction == null) return;

            var oldFaction = PlayerFaction.Value;
            HasBetrayedFaction = true;
            BetrayedFaction = oldFaction;

            // Severe reputation hit
            FactionStanding[oldFaction] -= 500;

            PlayerFaction = null;
            FactionRank = 0;
            FactionReputation = 0;

            GD.Print($"[Faction] Player betrayed {oldFaction}");
        }

        /// <summary>
        /// Modify reputation with a faction - includes cascade effects to related factions
        /// </summary>
        public void ModifyReputation(Faction faction, int amount)
        {
            FactionStanding[faction] += amount;

            // If this is player's faction, also modify internal reputation
            if (faction == PlayerFaction)
            {
                FactionReputation += amount;
                CheckRankUp();
            }

            // Apply cascade effects based on faction relationships
            ApplyReputationCascade(faction, amount);
        }

        /// <summary>
        /// Apply reputation cascade effects to other factions based on relationships
        /// - Faith ↔ Shadows: Direct opposition (50% inverse effect)
        /// - Crown → Faith: Mild positive correlation (20% same direction)
        /// - Crown → Shadows: Moderate negative (50% inverse effect - anti-government)
        /// </summary>
        private void ApplyReputationCascade(Faction sourceFaction, int amount)
        {
            switch (sourceFaction)
            {
                case Faction.TheFaith:
                    // Faith and Shadows are direct opposites
                    FactionStanding[Faction.TheShadows] -= amount / 2;
                    // Faith has mild positive relation with Crown (both establishment)
                    FactionStanding[Faction.TheCrown] += amount / 5;
                    break;

                case Faction.TheShadows:
                    // Shadows and Faith are direct opposites
                    FactionStanding[Faction.TheFaith] -= amount / 2;
                    // Shadows are anti-government, so Crown dislikes them
                    FactionStanding[Faction.TheCrown] -= amount / 3;
                    break;

                case Faction.TheCrown:
                    // Crown has mild positive with Faith (both establishment)
                    FactionStanding[Faction.TheFaith] += amount / 5;
                    // Crown is strongly opposed to Shadows (anti-government)
                    FactionStanding[Faction.TheShadows] -= amount / 2;
                    break;
            }
        }

        /// <summary>
        /// Check if a faction is hostile to the player based on standing
        /// </summary>
        public bool IsFactionHostile(Faction faction)
        {
            return FactionStanding[faction] <= -50;
        }

        /// <summary>
        /// Check if an NPC with a given faction would be hostile to the player
        /// </summary>
        public bool IsNPCHostileToPlayer(Faction? npcFaction)
        {
            if (npcFaction == null) return false;

            // Check standing with that faction
            if (IsFactionHostile(npcFaction.Value)) return true;

            // If player is in a faction, check faction rivalries
            if (PlayerFaction != null)
            {
                // Faith and Shadows are mortal enemies
                if ((PlayerFaction == Faction.TheFaith && npcFaction == Faction.TheShadows) ||
                    (PlayerFaction == Faction.TheShadows && npcFaction == Faction.TheFaith))
                {
                    return true;
                }

                // Shadows are hostile to Crown members (anti-government)
                if (PlayerFaction == Faction.TheCrown && npcFaction == Faction.TheShadows)
                {
                    return FactionStanding[Faction.TheShadows] <= -25; // Slightly more tolerant threshold
                }
            }

            return false;
        }

        /// <summary>
        /// Get the chance (0-100) that an NPC from a faction will ambush the player
        /// Note: This is now rolled ONCE per travel, not per NPC, so chances are reasonable
        /// </summary>
        public int GetAmbushChance(Faction? npcFaction)
        {
            if (npcFaction == null) return 0;

            int standing = FactionStanding[npcFaction.Value];
            int baseChance = 0;

            // Standing-based chance (tuned for single roll per travel, with 12-travel cooldown)
            if (standing <= -100)
                baseChance = 8;   // Hated: 8% base chance
            else if (standing <= -50)
                baseChance = 4;   // Hostile: 4% base chance
            else if (standing <= -25)
                baseChance = 1;   // Unfriendly: 1% base chance

            // Faction rivalry bonus
            if (PlayerFaction != null)
            {
                // Faith vs Shadows - mortal enemies, extra aggressive
                if ((PlayerFaction == Faction.TheFaith && npcFaction == Faction.TheShadows) ||
                    (PlayerFaction == Faction.TheShadows && npcFaction == Faction.TheFaith))
                {
                    baseChance += 4;
                }
                // Shadows vs Crown - anti-government aggression
                else if (PlayerFaction == Faction.TheCrown && npcFaction == Faction.TheShadows)
                {
                    baseChance += 2;
                }
            }

            return Math.Min(15, baseChance); // Cap at 15%
        }

        /// <summary>
        /// Get an ambush dialogue line based on faction relationships
        /// </summary>
        public string GetAmbushDialogue(Faction npcFaction, string npcName)
        {
            if (PlayerFaction == null)
            {
                return npcFaction switch
                {
                    Faction.TheFaith => $"{npcName} blocks your path. \"You reek of darkness. The Light demands purification!\"",
                    Faction.TheShadows => $"{npcName} emerges from the shadows. \"You've been asking too many questions...\"",
                    Faction.TheCrown => $"{npcName} steps forward. \"Halt! You're under suspicion of crimes against the Crown!\"",
                    _ => $"{npcName} attacks!"
                };
            }

            // Faction-specific rivalry dialogues
            return (PlayerFaction.Value, npcFaction) switch
            {
                (Faction.TheFaith, Faction.TheShadows) =>
                    $"{npcName} hisses from the darkness. \"A servant of the blind faith... Your light will be snuffed out!\"",
                (Faction.TheShadows, Faction.TheFaith) =>
                    $"{npcName} raises their holy symbol. \"Shadow-walker! The Light will cleanse your corruption!\"",
                (Faction.TheCrown, Faction.TheShadows) =>
                    $"{npcName} sneers. \"A lapdog of the Crown... Freedom has no place for your kind!\"",
                (Faction.TheShadows, Faction.TheCrown) =>
                    $"{npcName} draws their weapon. \"Criminal scum! In the name of the Crown, you'll face justice!\"",
                (Faction.TheFaith, Faction.TheCrown) =>
                    $"{npcName} frowns. \"Your devotion to mortal kings offends the Light...\"",
                (Faction.TheCrown, Faction.TheFaith) =>
                    $"{npcName} approaches menacingly. \"Zealots like you threaten the peace of the realm!\"",
                _ => $"{npcName} attacks you!"
            };
        }

        /// <summary>
        /// Check if player should rank up
        /// </summary>
        private void CheckRankUp()
        {
            if (PlayerFaction == null) return;

            var maxRank = Factions[PlayerFaction.Value].Ranks.Length - 1;
            var targetRank = FactionReputation switch
            {
                >= 5000 => 8,
                >= 3000 => 7,
                >= 2000 => 6,
                >= 1200 => 5,
                >= 800 => 4,
                >= 500 => 3,
                >= 250 => 2,
                >= 100 => 1,
                _ => 0
            };

            if (targetRank > FactionRank && FactionRank < maxRank)
            {
                FactionRank = Math.Min(targetRank, maxRank);
                GD.Print($"[Faction] Player promoted to rank {FactionRank}: {GetCurrentRankTitle()}");
            }
        }

        /// <summary>
        /// Get current rank title
        /// </summary>
        public string GetCurrentRankTitle()
        {
            if (PlayerFaction == null) return "Unaffiliated";
            return Factions[PlayerFaction.Value].Ranks[FactionRank];
        }

        /// <summary>
        /// Get faction-appropriate greeting
        /// </summary>
        public string GetFactionGreeting(Faction npcFaction, Character player)
        {
            if (PlayerFaction == null)
            {
                return npcFaction switch
                {
                    Faction.TheCrown => "Citizen. Mind your business and obey the laws.",
                    Faction.TheShadows => "Another face in the crowd. That's smart.",
                    Faction.TheFaith => "May the Seven guide your path, stranger.",
                    _ => "Greetings."
                };
            }

            if (PlayerFaction == npcFaction)
            {
                return npcFaction switch
                {
                    Faction.TheCrown => $"Hail, {GetCurrentRankTitle()}. The Crown endures.",
                    Faction.TheShadows => $"Shadow guide you, {GetCurrentRankTitle()}. What needs doing?",
                    Faction.TheFaith => $"Blessings upon you, {GetCurrentRankTitle()}. The Light persists.",
                    _ => "Brother/Sister."
                };
            }

            // Hostile faction
            return npcFaction switch
            {
                Faction.TheCrown => $"A {Factions[PlayerFaction.Value].Name} sympathizer. Watch yourself.",
                Faction.TheShadows => $"One of the {Factions[PlayerFaction.Value].Name}? Interesting...",
                Faction.TheFaith => $"Even those who follow {Factions[PlayerFaction.Value].Name} may find redemption.",
                _ => "I know who you serve."
            };
        }

        /// <summary>
        /// Get faction's view on a specific god
        /// </summary>
        public string GetFactionGodView(OldGodType god)
        {
            if (PlayerFaction == null) return "The gods are beyond mortal understanding.";
            return FactionGodViews[PlayerFaction.Value][god];
        }

        /// <summary>
        /// Get faction bonuses for current faction
        /// </summary>
        public FactionBonuses? GetCurrentBonuses()
        {
            if (PlayerFaction == null) return null;
            return Factions[PlayerFaction.Value].Bonuses;
        }

        /// <summary>
        /// Get shop price modifier for Crown faction membership
        /// Crown members get 10% discount at legitimate shops
        /// </summary>
        public float GetShopPriceModifier()
        {
            if (PlayerFaction != Faction.TheCrown) return 1.0f;
            var bonuses = GetCurrentBonuses();
            if (bonuses == null || bonuses.ShopDiscount == 0) return 1.0f;
            return 1.0f - (bonuses.ShopDiscount / 100f);  // 10% = 0.90 multiplier
        }

        /// <summary>
        /// Get healing price modifier for Faith faction membership
        /// Faith members get 25% discount at healers
        /// </summary>
        public float GetHealingPriceModifier()
        {
            if (PlayerFaction != Faction.TheFaith) return 1.0f;
            var bonuses = GetCurrentBonuses();
            if (bonuses == null || bonuses.HealingDiscount == 0) return 1.0f;
            return 1.0f - (bonuses.HealingDiscount / 100f);  // 25% = 0.75 multiplier
        }

        /// <summary>
        /// Get fence/sell price modifier for Shadows faction membership
        /// Shadows members get 20% better prices when selling to fences
        /// </summary>
        public float GetFencePriceModifier()
        {
            if (PlayerFaction != Faction.TheShadows) return 1.0f;
            var bonuses = GetCurrentBonuses();
            if (bonuses == null || bonuses.FenceBonus == 0) return 1.0f;
            return 1.0f + (bonuses.FenceBonus / 100f);  // 20% = 1.20 multiplier (better sell price)
        }

        /// <summary>
        /// Check if player has black market access (Shadows faction)
        /// </summary>
        public bool HasBlackMarketAccess()
        {
            if (PlayerFaction != Faction.TheShadows) return false;
            return GetCurrentBonuses()?.BlackMarketAccess ?? false;
        }

        /// <summary>
        /// Serialize for save
        /// </summary>
        public FactionSaveData Serialize()
        {
            return new FactionSaveData
            {
                PlayerFaction = PlayerFaction.HasValue ? (int)PlayerFaction.Value : -1,
                FactionRank = FactionRank,
                FactionReputation = FactionReputation,
                FactionStanding = FactionStanding.ToDictionary(k => (int)k.Key, v => v.Value),
                CompletedFactionQuests = CompletedFactionQuests.ToList(),
                HasBetrayedFaction = HasBetrayedFaction,
                BetrayedFaction = BetrayedFaction.HasValue ? (int)BetrayedFaction.Value : -1
            };
        }

        /// <summary>
        /// Deserialize from save
        /// </summary>
        public void Deserialize(FactionSaveData? data)
        {
            if (data == null) return;

            PlayerFaction = data.PlayerFaction >= 0 ? (Faction)data.PlayerFaction : null;
            FactionRank = data.FactionRank;
            FactionReputation = data.FactionReputation;
            FactionStanding = data.FactionStanding.ToDictionary(k => (Faction)k.Key, v => v.Value);
            CompletedFactionQuests = new HashSet<string>(data.CompletedFactionQuests);
            HasBetrayedFaction = data.HasBetrayedFaction;
            BetrayedFaction = data.BetrayedFaction >= 0 ? (Faction)data.BetrayedFaction : null;
        }

        /// <summary>
        /// Reset faction system to default state (for new games)
        /// </summary>
        public void Reset()
        {
            PlayerFaction = null;
            FactionRank = 0;
            FactionReputation = 0;
            FactionStanding = new Dictionary<Faction, int>
            {
                [Faction.TheCrown] = 0,
                [Faction.TheShadows] = 0,
                [Faction.TheFaith] = 0
            };
            CompletedFactionQuests = new HashSet<string>();
            HasBetrayedFaction = false;
            BetrayedFaction = null;
            GD.Print("[Faction] System reset for new game");
        }
    }

    #region Data Classes

    public enum Faction
    {
        TheCrown,   // Order/Law faction - associated with Castle
        TheShadows, // Freedom/Chaos faction - associated with Dark Alley
        TheFaith    // Devotion/Restoration faction - associated with Temple
    }

    public class FactionData
    {
        public string Name { get; set; } = "";
        public string Title { get; set; } = "";
        public string Leader { get; set; } = "";
        public string Location { get; set; } = "";
        public string Symbol { get; set; } = "";
        public string Philosophy { get; set; } = "";
        public string[] Beliefs { get; set; } = Array.Empty<string>();
        public Dictionary<Faction, string> Conflicts { get; set; } = new();
        public string JoinRequirements { get; set; } = "";
        public string[] Ranks { get; set; } = Array.Empty<string>();
        public FactionBonuses Bonuses { get; set; } = new();
        public OldGodType AssociatedGod { get; set; }
    }

    public class FactionBonuses
    {
        // The Crown bonuses
        public int ShopDiscount { get; set; }
        public bool GuardFavor { get; set; }
        public bool CastleAccess { get; set; }
        public int TaxExemption { get; set; }

        // The Shadows bonuses
        public bool BlackMarketAccess { get; set; }
        public int FenceBonus { get; set; }
        public int EscapeChance { get; set; }
        public bool InformationNetwork { get; set; }

        // The Faith bonuses
        public int HealingDiscount { get; set; }
        public int BlessingDuration { get; set; }
        public bool TempleAccess { get; set; }
        public bool DivineFavor { get; set; }
    }

    public class FactionSaveData
    {
        public int PlayerFaction { get; set; }
        public int FactionRank { get; set; }
        public int FactionReputation { get; set; }
        public Dictionary<int, int> FactionStanding { get; set; } = new();
        public List<string> CompletedFactionQuests { get; set; } = new();
        public bool HasBetrayedFaction { get; set; }
        public int BetrayedFaction { get; set; }
    }

    #endregion
}
