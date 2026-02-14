using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using UsurperRemake.Utils;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Betrayal System - Tracks hidden betrayal points and triggers NPC betrayals
    /// Players don't see it coming - betrayals feel organic and earned
    /// </summary>
    public class BetrayalSystem
    {
        private static BetrayalSystem? _fallbackInstance;
        public static BetrayalSystem Instance
        {
            get
            {
                var ctx = UsurperRemake.Server.SessionContext.Current;
                if (ctx != null) return ctx.Betrayal;
                return _fallbackInstance ??= new BetrayalSystem();
            }
        }

        // Hidden betrayal point tracking per NPC
        private Dictionary<string, BetrayalProfile> betrayalProfiles = new();

        // Active betrayals that have triggered
        private List<ActiveBetrayal> activeBetrays = new();

        // Events
        public event Action<string, BetrayalType>? OnBetrayalTriggered;
        public event Action<string>? OnBetrayalForgiven;
        public event Action<string>? OnBetrayalRevenged;

        // Betrayal thresholds
        private const int BETRAYAL_THRESHOLD = 100;
        private const int MINOR_BETRAYAL_THRESHOLD = 50;
        private const int FORGIVENESS_THRESHOLD = -50;

        public BetrayalSystem()
        {
            InitializePotentialBetrayers();
        }

        /// <summary>
        /// Initialize betrayal profiles for key NPCs who can betray
        /// </summary>
        private void InitializePotentialBetrayers()
        {
            // The Stranger (Noctura in disguise) - complex betrayal arc
            betrayalProfiles["TheStranger"] = new BetrayalProfile
            {
                NPCId = "TheStranger",
                NPCName = "The Stranger",
                BetrayalPoints = 0,
                BetrayalType = BetrayalType.Manipulation,
                TriggerCondition = BetrayalTrigger.StoryProgression,
                CanBeForgiven = true,
                Motivations = new List<string>
                {
                    "Noctura seeks to manipulate you into freeing the Old Gods",
                    "She believes the ends justify the means",
                    "She has watched mortals for millennia and sees them as tools"
                },
                BetrayalDialogue = new[]
                {
                    "Did you truly believe I was your friend?",
                    "I am Noctura, the Shadow Weaver.",
                    "Every word I spoke was calculated. Every kindness, a manipulation.",
                    "But you... you surprised me. You are not like the others.",
                    "Perhaps that is why I hesitate now..."
                },
                RedemptionPath = "Prove that mortals can choose mercy even when betrayed"
            };

            // Team member betrayal - if player treats them poorly
            betrayalProfiles["TeamBetrayal"] = new BetrayalProfile
            {
                NPCId = "TeamBetrayal",
                NPCName = "Your most trusted ally",
                BetrayalPoints = 0,
                BetrayalType = BetrayalType.Resentment,
                TriggerCondition = BetrayalTrigger.AccumulatedGrievances,
                CanBeForgiven = true,
                Motivations = new List<string>
                {
                    "Years of being overlooked for glory",
                    "Watching you grow darker while they remained silent",
                    "The final straw was when you sacrificed innocents"
                },
                BetrayalDialogue = new[]
                {
                    "I've followed you through darkness and light.",
                    "I made excuses for your cruelty. Called it necessity.",
                    "But I cannot follow you any further.",
                    "The person I believed in... died somewhere in those dungeons.",
                    "This ends now."
                },
                RedemptionPath = "Demonstrate genuine change and sacrifice for others"
            };

            // Romantic betrayal - if player is unfaithful
            betrayalProfiles["RomanticBetrayal"] = new BetrayalProfile
            {
                NPCId = "RomanticBetrayal",
                NPCName = "Your beloved",
                BetrayalPoints = 0,
                BetrayalType = BetrayalType.HeartBroken,
                TriggerCondition = BetrayalTrigger.Infidelity,
                CanBeForgiven = false, // Some wounds don't heal
                Motivations = new List<string>
                {
                    "They gave you everything - their heart, their trust",
                    "You chose another. Or many others.",
                    "Love turned to ash, and ash to cold fury"
                },
                BetrayalDialogue = new[]
                {
                    "I would have died for you. Did you know that?",
                    "I gave you my heart. My future. My everything.",
                    "And you... you couldn't even give me honesty.",
                    "I don't hate you. That would require caring.",
                    "You're simply... nothing to me now."
                },
                RedemptionPath = null // Cannot be forgiven
            };

            // King's Advisor - political betrayal
            betrayalProfiles["KingsAdvisor"] = new BetrayalProfile
            {
                NPCId = "KingsAdvisor",
                NPCName = "Lord Malachai, the King's Advisor",
                BetrayalPoints = 0,
                BetrayalType = BetrayalType.Political,
                TriggerCondition = BetrayalTrigger.PowerStruggle,
                CanBeForgiven = true,
                Motivations = new List<string>
                {
                    "He served the crown loyally for decades",
                    "Your rise threatens his position and legacy",
                    "He believes he knows what's best for the realm"
                },
                BetrayalDialogue = new[]
                {
                    "You think yourself a hero, don't you?",
                    "Storming through our politics like a bull in crystal.",
                    "The realm needs stability, not another would-be savior.",
                    "I do this not from malice, but from duty.",
                    "History will vindicate me."
                },
                RedemptionPath = "Prove your commitment to the realm's wellbeing over personal glory"
            };

            // Companion betrayal - Lyris (tragic romance option)
            betrayalProfiles["Lyris"] = new BetrayalProfile
            {
                NPCId = "Lyris",
                NPCName = "Lyris",
                BetrayalPoints = 0,
                BetrayalType = BetrayalType.Sacrifice,
                TriggerCondition = BetrayalTrigger.ProtectingPlayer,
                CanBeForgiven = true,
                IsSacrifice = true, // Not malicious - sacrifices herself
                Motivations = new List<string>
                {
                    "She has a secret connection to Veloura",
                    "Her soul can power the Soulweaver's Loom",
                    "She knew this from the beginning but fell in love anyway"
                },
                BetrayalDialogue = new[]
                {
                    "I should have told you sooner.",
                    "Veloura is... she's part of me. A shard of her divine essence.",
                    "I was sent to guide you to this moment.",
                    "But I didn't expect... I didn't expect to love you.",
                    "Let me do this. Please. Let my death mean something."
                },
                RedemptionPath = "There is no redemption needed - only grief"
            };

            // GD.Print($"[Betrayal] Initialized {betrayalProfiles.Count} potential betrayers");
        }

        /// <summary>
        /// Add betrayal points to an NPC (hidden from player)
        /// </summary>
        public void AddBetrayalPoints(string npcId, int points, string reason)
        {
            if (!betrayalProfiles.TryGetValue(npcId, out var profile))
            {
                // Create generic profile for this NPC
                profile = new BetrayalProfile
                {
                    NPCId = npcId,
                    NPCName = npcId,
                    BetrayalPoints = 0,
                    BetrayalType = BetrayalType.Resentment,
                    TriggerCondition = BetrayalTrigger.AccumulatedGrievances,
                    CanBeForgiven = true
                };
                betrayalProfiles[npcId] = profile;
            }

            profile.BetrayalPoints += points;
            profile.Grievances.Add(new Grievance
            {
                Reason = reason,
                Points = points,
                GameDay = StoryProgressionSystem.Instance.CurrentGameDay
            });

            // Hidden from player - no notification
            // GD.Print($"[Betrayal] {npcId} gained {points} betrayal points: {reason} (Total: {profile.BetrayalPoints})");

            // Check for betrayal trigger
            CheckBetrayalTrigger(profile);
        }

        /// <summary>
        /// Remove betrayal points (acts of kindness/loyalty)
        /// </summary>
        public void ReduceBetrayalPoints(string npcId, int points, string reason)
        {
            if (betrayalProfiles.TryGetValue(npcId, out var profile))
            {
                profile.BetrayalPoints = Math.Max(0, profile.BetrayalPoints - points);
                profile.ActsOfKindness.Add(reason);
                // GD.Print($"[Betrayal] {npcId} lost {points} betrayal points: {reason} (Total: {profile.BetrayalPoints})");
            }
        }

        /// <summary>
        /// Check if conditions are met for betrayal
        /// </summary>
        private void CheckBetrayalTrigger(BetrayalProfile profile)
        {
            if (profile.HasBetrayed) return;

            bool shouldTrigger = false;
            string triggerReason = "";

            switch (profile.TriggerCondition)
            {
                case BetrayalTrigger.AccumulatedGrievances:
                    if (profile.BetrayalPoints >= BETRAYAL_THRESHOLD)
                    {
                        shouldTrigger = true;
                        triggerReason = "Too many grievances accumulated";
                    }
                    break;

                case BetrayalTrigger.Infidelity:
                    if (profile.BetrayalPoints >= MINOR_BETRAYAL_THRESHOLD)
                    {
                        shouldTrigger = true;
                        triggerReason = "Romantic betrayal";
                    }
                    break;

                case BetrayalTrigger.PowerStruggle:
                    var story = StoryProgressionSystem.Instance;
                    if (profile.BetrayalPoints >= 75 && story.CurrentChapter >= StoryChapter.RisingPower)
                    {
                        shouldTrigger = true;
                        triggerReason = "Political threat detected";
                    }
                    break;

                case BetrayalTrigger.StoryProgression:
                    // Handled separately in story events
                    break;

                case BetrayalTrigger.ProtectingPlayer:
                    // Sacrifice type - triggered by specific story events
                    break;
            }

            if (shouldTrigger)
            {
                profile.IsPendingBetrayal = true;
                profile.TriggerReason = triggerReason;
                // GD.Print($"[Betrayal] {profile.NPCId} is now pending betrayal: {triggerReason}");
            }
        }

        /// <summary>
        /// Execute a pending betrayal (called during appropriate story moment)
        /// </summary>
        public async Task<BetrayalResult> ExecuteBetrayal(string npcId, Character player, TerminalEmulator terminal)
        {
            if (!betrayalProfiles.TryGetValue(npcId, out var profile))
            {
                return new BetrayalResult { Occurred = false };
            }

            if (profile.HasBetrayed || !profile.IsPendingBetrayal)
            {
                return new BetrayalResult { Occurred = false };
            }

            profile.HasBetrayed = true;
            profile.BetrayalDay = StoryProgressionSystem.Instance.CurrentGameDay;

            // Display betrayal scene
            await DisplayBetrayalScene(profile, player, terminal);

            // Create active betrayal record
            var activeBetrayal = new ActiveBetrayal
            {
                NPCId = npcId,
                NPCName = profile.NPCName,
                Type = profile.BetrayalType,
                Day = profile.BetrayalDay,
                IsSacrifice = profile.IsSacrifice,
                CanBeResolved = profile.CanBeForgiven
            };
            activeBetrays.Add(activeBetrayal);

            // Apply betrayal effects
            ApplyBetrayalEffects(profile, player);

            // Set story flags
            StoryProgressionSystem.Instance.SetStoryFlag($"betrayed_by_{npcId}", true);
            if (profile.IsSacrifice)
            {
                StoryProgressionSystem.Instance.SetStoryFlag($"{npcId}_sacrificed", true);
            }

            // Trigger event
            OnBetrayalTriggered?.Invoke(npcId, profile.BetrayalType);

            // Ocean philosophy insight - betrayal teaches about attachment
            if (OceanPhilosophySystem.Instance.AwakeningLevel >= 3)
            {
                terminal.WriteLine("");
                terminal.WriteLine("A distant understanding stirs within you...", "dark_cyan");
                terminal.WriteLine("The wave that crashes against you was once part of the same ocean.", "cyan");
                OceanPhilosophySystem.Instance.GainInsight(5);
            }

            return new BetrayalResult
            {
                Occurred = true,
                NPCId = npcId,
                NPCName = profile.NPCName,
                Type = profile.BetrayalType,
                IsSacrifice = profile.IsSacrifice,
                CanBeForgiven = profile.CanBeForgiven,
                Motivations = profile.Motivations
            };
        }

        /// <summary>
        /// Display the betrayal scene
        /// </summary>
        private async Task DisplayBetrayalScene(BetrayalProfile profile, Character player, TerminalEmulator terminal)
        {
            terminal.Clear();
            terminal.WriteLine("");

            if (profile.IsSacrifice)
            {
                terminal.WriteLine("╔════════════════════════════════════════════════════════════════╗", "bright_yellow");
                terminal.WriteLine("║                    A   S A C R I F I C E                       ║", "bright_yellow");
                terminal.WriteLine("╚════════════════════════════════════════════════════════════════╝", "bright_yellow");
            }
            else
            {
                terminal.WriteLine("╔════════════════════════════════════════════════════════════════╗", "dark_red");
                terminal.WriteLine("║                    B E T R A Y A L                             ║", "dark_red");
                terminal.WriteLine("╚════════════════════════════════════════════════════════════════╝", "dark_red");
            }
            terminal.WriteLine("");

            await Task.Delay(1500);

            terminal.WriteLine($"  {profile.NPCName} turns to face you.", "white");
            terminal.WriteLine("  Something in their eyes has changed.", "gray");
            terminal.WriteLine("");

            await Task.Delay(1000);

            if (profile.BetrayalDialogue != null)
            {
                foreach (var line in profile.BetrayalDialogue)
                {
                    terminal.WriteLine($"  \"{line}\"", "yellow");
                    await Task.Delay(800);
                }
            }

            terminal.WriteLine("");

            if (profile.Motivations.Count > 0)
            {
                terminal.WriteLine("  ─────────────────────────────────────────────", "dark_gray");
                terminal.WriteLine("");
                terminal.WriteLine("  [You begin to understand their reasons...]", "dark_cyan");
                terminal.WriteLine("");

                foreach (var motivation in profile.Motivations)
                {
                    terminal.WriteLine($"  - {motivation}", "gray");
                    await Task.Delay(500);
                }
            }

            terminal.WriteLine("");

            if (profile.IsSacrifice)
            {
                terminal.WriteLine("  This was not malice. This was love.", "bright_yellow");
                terminal.WriteLine("  A love willing to pay the ultimate price.", "yellow");
            }
            else if (profile.CanBeForgiven)
            {
                terminal.WriteLine("  Perhaps... this doesn't have to be the end.", "cyan");
                terminal.WriteLine("  Understanding might still bridge this chasm.", "dark_cyan");
            }
            else
            {
                terminal.WriteLine("  Some wounds cut too deep to heal.", "dark_red");
                terminal.WriteLine("  What is broken cannot always be mended.", "red");
            }

            terminal.WriteLine("");
            await terminal.GetInputAsync("  Press Enter to continue...");
        }

        /// <summary>
        /// Apply the mechanical effects of betrayal
        /// </summary>
        private void ApplyBetrayalEffects(BetrayalProfile profile, Character player)
        {
            switch (profile.BetrayalType)
            {
                case BetrayalType.Manipulation:
                    // Noctura's manipulation - player loses some resources
                    player.Gold -= player.Gold / 4; // Lose 25% gold
                    break;

                case BetrayalType.Resentment:
                    // Team betrayal - lose team status
                    // player.Team = null; // Would need to handle this properly
                    break;

                case BetrayalType.HeartBroken:
                    // Romantic betrayal - emotional damage
                    player.Wisdom += 3; // Pain teaches wisdom
                    break;

                case BetrayalType.Political:
                    // Political betrayal - reputation damage
                    player.Darkness += 100; // Scandal
                    break;

                case BetrayalType.Sacrifice:
                    // Sacrifice - grief but also enlightenment
                    player.Wisdom += 5;
                    OceanPhilosophySystem.Instance.ExperienceMoment(AwakeningMoment.CompanionSacrifice);
                    break;
            }
        }

        /// <summary>
        /// Attempt to forgive a betrayer (if possible)
        /// </summary>
        public async Task<bool> AttemptForgiveness(string npcId, Character player, TerminalEmulator terminal)
        {
            var activeBetrayal = activeBetrays.FirstOrDefault(b => b.NPCId == npcId);
            if (activeBetrayal == null || !activeBetrayal.CanBeResolved)
            {
                terminal.WriteLine("Some betrayals cannot be forgiven.", "red");
                return false;
            }

            if (!betrayalProfiles.TryGetValue(npcId, out var profile))
            {
                return false;
            }

            // Check if player has met redemption conditions
            bool canForgive = CheckRedemptionConditions(profile, player);

            if (!canForgive)
            {
                terminal.WriteLine("The conditions for forgiveness have not yet been met.", "yellow");
                terminal.WriteLine($"Path to redemption: {profile.RedemptionPath ?? "Unknown"}", "gray");
                return false;
            }

            // Display forgiveness scene
            await DisplayForgivenessScene(profile, player, terminal);

            // Update state
            activeBetrayal.IsResolved = true;
            activeBetrayal.Resolution = BetrayalResolution.Forgiven;
            profile.WasForgiven = true;

            // Story flag
            StoryProgressionSystem.Instance.SetStoryFlag($"forgave_{npcId}", true);

            // Ocean philosophy - forgiveness is letting go
            OceanPhilosophySystem.Instance.ExperienceMoment(AwakeningMoment.ForgaveBetrayerMercy);
            terminal.WriteLine("");
            terminal.WriteLine("In forgiving, you release yourself from the weight of resentment.", "bright_cyan");
            terminal.WriteLine("The wave forgives the shore for breaking it.", "cyan");

            OnBetrayalForgiven?.Invoke(npcId);
            return true;
        }

        /// <summary>
        /// Check if redemption conditions are met
        /// </summary>
        private bool CheckRedemptionConditions(BetrayalProfile profile, Character player)
        {
            switch (profile.NPCId)
            {
                case "TheStranger":
                    // Spared at least 2 Old Gods
                    var godStates = StoryProgressionSystem.Instance.OldGodStates;
                    int savedGods = godStates.Values.Count(g => g.Status == GodStatus.Saved);
                    return savedGods >= 2;

                case "TeamBetrayal":
                    // Demonstrated change through positive actions
                    return player.Chivalry > player.Darkness && profile.ActsOfKindness.Count >= 5;

                case "KingsAdvisor":
                    // Proved commitment to realm
                    return player.Chivalry > 2000 || StoryProgressionSystem.Instance.HasStoryFlag("saved_the_realm");

                case "Lyris":
                    // Cannot "forgive" a sacrifice - only honor it
                    return false;

                default:
                    return profile.ActsOfKindness.Count >= 3;
            }
        }

        /// <summary>
        /// Display the forgiveness scene
        /// </summary>
        private async Task DisplayForgivenessScene(BetrayalProfile profile, Character player, TerminalEmulator terminal)
        {
            terminal.Clear();
            terminal.WriteLine("");
            terminal.WriteLine("╔════════════════════════════════════════════════════════════════╗", "bright_green");
            terminal.WriteLine("║                    F O R G I V E N E S S                       ║", "bright_green");
            terminal.WriteLine("╚════════════════════════════════════════════════════════════════╝", "bright_green");
            terminal.WriteLine("");

            await Task.Delay(1000);

            terminal.WriteLine($"  You approach {profile.NPCName}.", "white");
            terminal.WriteLine("  They flinch, expecting anger. Expecting justice.", "gray");
            terminal.WriteLine("");

            await Task.Delay(800);

            terminal.WriteLine("  \"I understand why you did what you did.\"", "bright_yellow");
            terminal.WriteLine("  \"I don't agree. I may never agree.\"", "yellow");
            terminal.WriteLine("  \"But I choose to let go of this weight.\"", "yellow");
            terminal.WriteLine("");

            await Task.Delay(1000);

            terminal.WriteLine($"  {profile.NPCName}'s eyes widen.", "white");
            terminal.WriteLine("  For a moment, neither of you speak.", "gray");
            terminal.WriteLine("");

            terminal.WriteLine("  \"I... I don't deserve this.\"", "cyan");
            terminal.WriteLine("");

            terminal.WriteLine("  \"Perhaps not. But we rarely get what we deserve.\"", "bright_yellow");
            terminal.WriteLine("  \"We get what we choose to give.\"", "yellow");
            terminal.WriteLine("");

            await Task.Delay(800);

            terminal.WriteLine("  Something shifts between you.", "bright_white");
            terminal.WriteLine("  Not reconciliation. Not yet.", "white");
            terminal.WriteLine("  But a door that was closed... is now open.", "green");

            terminal.WriteLine("");
            await terminal.GetInputAsync("  Press Enter to continue...");
        }

        /// <summary>
        /// Take revenge on a betrayer
        /// </summary>
        public async Task TakeRevenge(string npcId, Character player, TerminalEmulator terminal)
        {
            var activeBetrayal = activeBetrays.FirstOrDefault(b => b.NPCId == npcId);
            if (activeBetrayal == null)
            {
                return;
            }

            terminal.Clear();
            terminal.WriteLine("");
            terminal.WriteLine("╔════════════════════════════════════════════════════════════════╗", "dark_red");
            terminal.WriteLine("║                    R E V E N G E                               ║", "dark_red");
            terminal.WriteLine("╚════════════════════════════════════════════════════════════════╝", "dark_red");
            terminal.WriteLine("");

            await Task.Delay(1000);

            terminal.WriteLine("  You have chosen the path of vengeance.", "red");
            terminal.WriteLine("  An eye for an eye. Blood for blood.", "dark_red");
            terminal.WriteLine("");

            await Task.Delay(800);

            if (betrayalProfiles.TryGetValue(npcId, out var profile))
            {
                terminal.WriteLine($"  {profile.NPCName} falls before you.", "gray");
                terminal.WriteLine("  Their last breath carries no plea for mercy.", "dark_gray");
                terminal.WriteLine("  They knew this was coming.", "dark_gray");
            }

            terminal.WriteLine("");
            terminal.WriteLine("  The debt is paid.", "dark_red");
            terminal.WriteLine("  But the weight... the weight remains.", "gray");

            // Apply consequences
            player.Darkness += 200;
            player.MKills++; // Counts as a kill

            // Update state
            activeBetrayal.IsResolved = true;
            activeBetrayal.Resolution = BetrayalResolution.Avenged;

            // Story flag
            StoryProgressionSystem.Instance.SetStoryFlag($"killed_{npcId}", true);

            // Ocean philosophy - revenge is grasping
            terminal.WriteLine("");
            terminal.WriteLine("Violence begets violence. The wave crashes, but the ocean remains.", "dark_cyan");
            OceanPhilosophySystem.Instance.GainInsight(-5); // Negative insight

            terminal.WriteLine("");
            await terminal.GetInputAsync("  Press Enter to continue...");

            OnBetrayalRevenged?.Invoke(npcId);
        }

        /// <summary>
        /// Get all active (unresolved) betrayals
        /// </summary>
        public List<ActiveBetrayal> GetActiveBetrays()
        {
            return activeBetrays.Where(b => !b.IsResolved).ToList();
        }

        /// <summary>
        /// Check if an NPC has betrayed the player
        /// </summary>
        public bool HasBetrayed(string npcId)
        {
            return betrayalProfiles.TryGetValue(npcId, out var profile) && profile.HasBetrayed;
        }

        /// <summary>
        /// Check if there's a pending betrayal ready to trigger
        /// </summary>
        public bool HasPendingBetrayal(string npcId)
        {
            return betrayalProfiles.TryGetValue(npcId, out var profile) &&
                   profile.IsPendingBetrayal && !profile.HasBetrayed;
        }

        /// <summary>
        /// Trigger The Stranger's revelation (Noctura unmasked)
        /// </summary>
        public void TriggerStrangerRevelation()
        {
            if (betrayalProfiles.TryGetValue("TheStranger", out var profile))
            {
                profile.IsPendingBetrayal = true;
                profile.TriggerReason = "The truth can no longer be hidden";
                StoryProgressionSystem.Instance.SetFlag(StoryFlag.KnowsNocturaTruth);
            }
        }

        /// <summary>
        /// Record infidelity (for romantic betrayal tracking)
        /// </summary>
        public void RecordInfidelity(string romancePartnerId)
        {
            AddBetrayalPoints("RomanticBetrayal", 100, $"Was unfaithful to {romancePartnerId}");
        }

        /// <summary>
        /// Get betrayal summary for a save file
        /// </summary>
        public Dictionary<string, object> SaveState()
        {
            return new Dictionary<string, object>
            {
                ["Profiles"] = betrayalProfiles.ToDictionary(
                    k => k.Key,
                    v => new Dictionary<string, object>
                    {
                        ["Points"] = v.Value.BetrayalPoints,
                        ["HasBetrayed"] = v.Value.HasBetrayed,
                        ["WasForgiven"] = v.Value.WasForgiven,
                        ["BetrayalDay"] = v.Value.BetrayalDay
                    }
                ),
                ["ActiveBetrays"] = activeBetrays.Select(b => new Dictionary<string, object>
                {
                    ["NPCId"] = b.NPCId,
                    ["Type"] = (int)b.Type,
                    ["IsResolved"] = b.IsResolved,
                    ["Resolution"] = (int)b.Resolution
                }).ToList()
            };
        }
    }

    #region Betrayal Data Classes

    public enum BetrayalType
    {
        Manipulation,   // Noctura - playing a long game
        Resentment,     // Team - accumulated grievances
        HeartBroken,    // Romantic - unfaithfulness
        Political,      // Advisor - power struggle
        Sacrifice       // Lyris - not malicious, sacrifices self
    }

    public enum BetrayalTrigger
    {
        AccumulatedGrievances,  // Hit threshold from many small hurts
        Infidelity,             // Romantic unfaithfulness
        PowerStruggle,          // Political threat
        StoryProgression,       // Scripted reveal
        ProtectingPlayer        // Sacrifice type
    }

    public enum BetrayalResolution
    {
        None,
        Forgiven,
        Avenged,
        Ignored
    }

    public class BetrayalProfile
    {
        public string NPCId { get; set; } = "";
        public string NPCName { get; set; } = "";
        public int BetrayalPoints { get; set; } = 0;
        public BetrayalType BetrayalType { get; set; }
        public BetrayalTrigger TriggerCondition { get; set; }
        public bool CanBeForgiven { get; set; }
        public bool IsSacrifice { get; set; }
        public bool IsPendingBetrayal { get; set; }
        public bool HasBetrayed { get; set; }
        public bool WasForgiven { get; set; }
        public int BetrayalDay { get; set; }
        public string TriggerReason { get; set; } = "";
        public List<string> Motivations { get; set; } = new();
        public string[]? BetrayalDialogue { get; set; }
        public string? RedemptionPath { get; set; }
        public List<Grievance> Grievances { get; set; } = new();
        public List<string> ActsOfKindness { get; set; } = new();
    }

    public class Grievance
    {
        public string Reason { get; set; } = "";
        public int Points { get; set; }
        public int GameDay { get; set; }
    }

    public class ActiveBetrayal
    {
        public string NPCId { get; set; } = "";
        public string NPCName { get; set; } = "";
        public BetrayalType Type { get; set; }
        public int Day { get; set; }
        public bool IsSacrifice { get; set; }
        public bool CanBeResolved { get; set; }
        public bool IsResolved { get; set; }
        public BetrayalResolution Resolution { get; set; } = BetrayalResolution.None;
    }

    public class BetrayalResult
    {
        public bool Occurred { get; set; }
        public string NPCId { get; set; } = "";
        public string NPCName { get; set; } = "";
        public BetrayalType Type { get; set; }
        public bool IsSacrifice { get; set; }
        public bool CanBeForgiven { get; set; }
        public List<string> Motivations { get; set; } = new();
    }

    #endregion
}
