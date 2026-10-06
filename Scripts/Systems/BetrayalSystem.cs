using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UsurperRemake.UI;
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
                Keyed = true
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
                Keyed = true
            };

            // Romantic betrayal - if player is unfaithful
            betrayalProfiles["RomanticBetrayal"] = new BetrayalProfile
            {
                NPCId = "RomanticBetrayal",
                NPCName = "Your beloved",
                BetrayalPoints = 0,
                BetrayalType = BetrayalType.HeartBroken,
                TriggerCondition = BetrayalTrigger.Infidelity,
                CanBeForgiven = false, // Some wounds don't heal; no redemption path
                Keyed = true
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
                Keyed = true
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
                Keyed = true
            };

            // GD.Print($"[Betrayal] Initialized {betrayalProfiles.Count} potential betrayers");
        }

        /// <summary>
        /// v1.2.5: the key of a story betrayer's shown text (name, dialogue, motivations, redemption), from its id.
        /// </summary>
        internal static string TextKey(string npcId, string part) => $"betrayal.{npcId}.{part}";

        /// <summary>v1.2.5: the betrayer's name as shown; NPCName stays the stored English (Lyris is a name).</summary>
        internal static string DisplayName(BetrayalProfile profile) =>
            profile.Keyed && Loc.Has(TextKey(profile.NPCId, "name")) ? Loc.Get(TextKey(profile.NPCId, "name")) : profile.NPCName;

        /// <summary>v1.2.5: the betrayer's spoken lines in the reader's language, or the profile's own (none for a generic one).</summary>
        internal static string[]? DialogueRows(BetrayalProfile profile) =>
            profile.Keyed ? Loc.Get(TextKey(profile.NPCId, "dialogue")).Split('\n') : profile.BetrayalDialogue;

        /// <summary>v1.2.5: the betrayer's motivations in the reader's language, or the profile's own.</summary>
        internal static List<string> MotivationRows(BetrayalProfile profile) =>
            profile.Keyed ? Loc.Get(TextKey(profile.NPCId, "motivations")).Split('\n').ToList() : profile.Motivations;

        /// <summary>v1.2.5: the path to redemption in the reader's language, or null when there is none.</summary>
        internal static string? RedemptionText(BetrayalProfile profile) =>
            profile.Keyed ? (Loc.Has(TextKey(profile.NPCId, "redemption")) ? Loc.Get(TextKey(profile.NPCId, "redemption")) : null)
                          : profile.RedemptionPath;

        /// <summary>
        /// Add betrayal points to an NPC (hidden from player)
        /// </summary>
        public void AddBetrayalPoints(string npcId, int points, string reason)
        {
            // Create generic profile for this NPC if needed
            var profile = GetOrCreateProfile(npcId);

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
                profile.ActsOfKindness.Add(new KindnessAct { Reason = reason, GameDay = StoryProgressionSystem.Instance.CurrentGameDay });
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

            // 1.2.8: the record, the effects and the flags are written before the scene, so a
            // disconnect during the scene cannot leave HasBetrayed saved without them.
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
            bool oceanInsight = OceanPhilosophySystem.Instance.AwakeningLevel >= 3;
            if (oceanInsight)
                OceanPhilosophySystem.Instance.GainInsight("betrayal:" + npcId); // v1.1.12

            // Display betrayal scene
            await DisplayBetrayalScene(profile, player, terminal);

            if (oceanInsight)
            {
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("betrayal.ocean_understanding"), "dark_cyan");
                terminal.WriteLine(Loc.Get("betrayal.ocean_wave_crashes"), "cyan");
            }

            return new BetrayalResult
            {
                Occurred = true,
                NPCId = npcId,
                NPCName = profile.NPCName,
                Type = profile.BetrayalType,
                IsSacrifice = profile.IsSacrifice,
                CanBeForgiven = profile.CanBeForgiven,
                Motivations = MotivationRows(profile)
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
                UIHelper.WriteBoxHeader(terminal, Loc.Get("betrayal.header_sacrifice"), "bright_yellow", 64);
            }
            else
            {
                UIHelper.WriteBoxHeader(terminal, Loc.Get("betrayal.header_betrayal"), "dark_red", 64);
            }
            terminal.WriteLine("");

            await Pacing.Wait(1500);

            terminal.WriteLine($"  {Loc.Get("betrayal.turns_to_face", DisplayName(profile))}", "white");
            terminal.WriteLine($"  {Loc.Get("betrayal.eyes_changed")}", "gray");
            terminal.WriteLine("");

            await Pacing.Wait(1000);

            var dialogue = DialogueRows(profile);
            if (dialogue != null)
            {
                foreach (var line in dialogue)
                {
                    terminal.WriteLine($"  \"{line}\"", "yellow");
                    await Pacing.Wait(800);
                }
            }

            terminal.WriteLine("");

            var motivations = MotivationRows(profile);
            if (motivations.Count > 0)
            {
                if (!GameConfig.ScreenReaderMode)
                    terminal.WriteLine("  ─────────────────────────────────────────────", "dark_gray");
                terminal.WriteLine("");
                terminal.WriteLine($"  {Loc.Get("betrayal.understand_reasons")}", "dark_cyan");
                terminal.WriteLine("");

                foreach (var motivation in motivations)
                {
                    terminal.WriteLine($"  - {motivation}", "gray");
                    await Pacing.Wait(500);
                }
            }

            terminal.WriteLine("");

            if (profile.IsSacrifice)
            {
                terminal.WriteLine($"  {Loc.Get("betrayal.sacrifice_not_malice")}", "bright_yellow");
                terminal.WriteLine($"  {Loc.Get("betrayal.sacrifice_ultimate_price")}", "yellow");
            }
            else if (profile.CanBeForgiven)
            {
                terminal.WriteLine($"  {Loc.Get("betrayal.perhaps_not_end")}", "cyan");
                terminal.WriteLine($"  {Loc.Get("betrayal.understanding_bridge")}", "dark_cyan");
            }
            else
            {
                terminal.WriteLine($"  {Loc.Get("betrayal.wounds_too_deep")}", "dark_red");
                terminal.WriteLine($"  {Loc.Get("betrayal.broken_not_mended")}", "red");
            }

            terminal.WriteLine("");
            await terminal.PressAnyKey();
        }

        /// <summary>
        /// Apply the mechanical effects of betrayal
        /// </summary>
        private void ApplyBetrayalEffects(BetrayalProfile profile, Character player)
        {
            switch (profile.BetrayalType)
            {
                case BetrayalType.Manipulation:
                    // Noctura's manipulation. 1.2.8: no gold loss; her fight already settles the account.
                    break;

                case BetrayalType.Resentment:
                    // Team betrayal - lose team status
                    // player.Team = null; // Would need to handle this properly
                    break;

                case BetrayalType.HeartBroken:
                    // Romantic betrayal - emotional damage
                    player.GrantPermanentStat(StatKind.Wisdom, 3); // Pain teaches wisdom (1.2.0: lasting, written to Base)
                    break;

                case BetrayalType.Political:
                    // Political betrayal - reputation damage — v0.57.12: paired movement
                    AlignmentSystem.Instance.ChangeAlignment(player, 100, isGood: false, "betrayal.political");
                    break;

                case BetrayalType.Sacrifice:
                    // Sacrifice. 1.2.8: no effect here; the paradox gives the Wisdom +5 and her death
                    // records the CompanionSacrifice moment, so the scene grants nothing twice.
                    break;
            }
        }

        /// <summary>
        /// Attempt to forgive a betrayer (if possible)
        /// </summary>
        public async Task<bool> AttemptForgiveness(string npcId, Character player, TerminalEmulator terminal)
        {
            var activeBetrayal = activeBetrays.FirstOrDefault(b => b.NPCId == npcId);
            // 1.2.8: a resolved betrayal (forgiven or avenged) is not forgiven a second time
            if (activeBetrayal == null || !activeBetrayal.CanBeResolved || activeBetrayal.IsResolved)
            {
                terminal.WriteLine(Loc.Get("betrayal.cannot_forgive"), "red");
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
                terminal.WriteLine(Loc.Get("betrayal.conditions_not_met"), "yellow");
                // v1.2.5: the row ran past 79 columns in every language; it wraps at spaces
                foreach (var row in GameEngine.WrapRows(Loc.Get("betrayal.path_to_redemption", RedemptionText(profile) ?? Loc.Get("ui.none"))))
                    terminal.WriteLine(row, "gray");
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
            terminal.WriteLine(Loc.Get("betrayal.forgive_release"), "bright_cyan");
            terminal.WriteLine(Loc.Get("betrayal.wave_forgives_shore"), "cyan");

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
                    // Demonstrated change through positive actions.
                    // v0.57.0 — Balanced players qualify even with chivalry == darkness; what matters
                    // for forgiveness is the kindness record, not which scale is numerically higher.
                    bool netGoodOrBalanced = player.Chivalry > player.Darkness
                        || UsurperRemake.Systems.AlignmentSystem.Instance.IsBalanced(player);
                    return netGoodOrBalanced && profile.ActsOfKindness.Count >= 5;

                case "KingsAdvisor":
                    // v0.57.12: was `> 2000` — unreachable with AlignmentCap=1000. Changed to Holy-tier threshold (>= 800)
                    // to preserve the original "very high chivalry proves commitment to realm" intent.
                    return player.Chivalry >= 800 || StoryProgressionSystem.Instance.HasStoryFlag("saved_the_realm");

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
            UIHelper.WriteBoxHeader(terminal, Loc.Get("betrayal.header_forgiveness"), "bright_green", 64);
            terminal.WriteLine("");

            await Pacing.Wait(1000);

            terminal.WriteLine($"  {Loc.Get("betrayal.approach_npc", DisplayName(profile))}", "white");
            terminal.WriteLine($"  {Loc.Get("betrayal.flinch_expecting")}", "gray");
            terminal.WriteLine("");

            await Pacing.Wait(800);

            terminal.WriteLine($"  \"{Loc.Get("betrayal.forgive_understand")}\"", "bright_yellow");
            terminal.WriteLine($"  \"{Loc.Get("betrayal.forgive_disagree")}\"", "yellow");
            terminal.WriteLine($"  \"{Loc.Get("betrayal.forgive_let_go")}\"", "yellow");
            terminal.WriteLine("");

            await Pacing.Wait(1000);

            terminal.WriteLine($"  {Loc.Get("betrayal.eyes_widen", DisplayName(profile))}", "white");
            terminal.WriteLine($"  {Loc.Get("betrayal.neither_speak")}", "gray");
            terminal.WriteLine("");

            terminal.WriteLine($"  \"{Loc.Get("betrayal.dont_deserve")}\"", "cyan");
            terminal.WriteLine("");

            terminal.WriteLine($"  \"{Loc.Get("betrayal.perhaps_not")}\"", "bright_yellow");
            terminal.WriteLine($"  \"{Loc.Get("betrayal.choose_to_give")}\"", "yellow");
            terminal.WriteLine("");

            await Pacing.Wait(800);

            terminal.WriteLine($"  {Loc.Get("betrayal.something_shifts")}", "bright_white");
            terminal.WriteLine($"  {Loc.Get("betrayal.not_reconciliation")}", "white");
            terminal.WriteLine($"  {Loc.Get("betrayal.door_now_open")}", "green");

            terminal.WriteLine("");
            await terminal.PressAnyKey();
        }

        /// <summary>
        /// Take revenge on a betrayer
        /// </summary>
        public async Task TakeRevenge(string npcId, Character player, TerminalEmulator terminal)
        {
            // 1.2.8: revenge is only on the Stranger
            if (npcId != "TheStranger")
            {
                return;
            }

            var activeBetrayal = activeBetrays.FirstOrDefault(b => b.NPCId == npcId);
            // 1.2.8: once; a resolved betrayal (forgiven or avenged) takes no revenge
            if (activeBetrayal == null || activeBetrayal.IsResolved)
            {
                return;
            }

            terminal.Clear();
            terminal.WriteLine("");
            UIHelper.WriteBoxHeader(terminal, Loc.Get("betrayal.header_revenge"), "dark_red", 64);
            terminal.WriteLine("");

            await Pacing.Wait(1000);

            terminal.WriteLine($"  {Loc.Get("betrayal.path_vengeance")}", "red");
            terminal.WriteLine($"  {Loc.Get("betrayal.eye_for_eye")}", "dark_red");
            terminal.WriteLine("");

            await Pacing.Wait(800);

            if (betrayalProfiles.TryGetValue(npcId, out var profile))
            {
                terminal.WriteLine($"  {Loc.Get("betrayal.falls_before_you", DisplayName(profile))}", "gray");
                terminal.WriteLine($"  {Loc.Get("betrayal.no_plea_mercy")}", "dark_gray");
                terminal.WriteLine($"  {Loc.Get("betrayal.knew_coming")}", "dark_gray");
            }

            terminal.WriteLine("");
            terminal.WriteLine($"  {Loc.Get("betrayal.debt_paid")}", "dark_red");
            terminal.WriteLine($"  {Loc.Get("betrayal.weight_remains")}", "gray");

            // Apply consequences — v0.57.12: paired movement
            AlignmentSystem.Instance.ChangeAlignment(player, 200, isGood: false, "betrayal.final");
            // 1.2.8: not counted as a monster kill

            // Update state
            activeBetrayal.IsResolved = true;
            activeBetrayal.Resolution = BetrayalResolution.Avenged;

            // Story flag
            StoryProgressionSystem.Instance.SetStoryFlag($"killed_{npcId}", true);

            // Ocean philosophy - revenge is grasping
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("betrayal.violence_begets"), "dark_cyan");

            terminal.WriteLine("");
            await terminal.PressAnyKey();

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
        /// 1.2.8: the companion who left after a team betrayal, held until forgiveness lets them rejoin.
        /// </summary>
        public CompanionId? DepartedCompanionId { get; set; }

        /// <summary>
        /// 1.2.8: back to a new character's state: the five story betrayers untouched, no records, nobody departed.
        /// </summary>
        public void Reset()
        {
            betrayalProfiles = new Dictionary<string, BetrayalProfile>();
            activeBetrays = new List<ActiveBetrayal>();
            DepartedCompanionId = null;
            InitializePotentialBetrayers();
        }

        /// <summary>1.2.8: the saved betrayal state (profiles, records, the departed companion).</summary>
        public BetrayalSaveData Serialize()
        {
            return new BetrayalSaveData
            {
                Profiles = betrayalProfiles.Values.Select(p => new BetrayalProfileSaveData
                {
                    NPCId = p.NPCId,
                    BetrayalPoints = p.BetrayalPoints,
                    IsPendingBetrayal = p.IsPendingBetrayal,
                    TriggerReason = p.TriggerReason,
                    HasBetrayed = p.HasBetrayed,
                    WasForgiven = p.WasForgiven,
                    BetrayalDay = p.BetrayalDay,
                    WarningShown = p.WarningShown,
                    ActsOfKindness = p.ActsOfKindness.Select(k => new KindnessAct { Reason = k.Reason, GameDay = k.GameDay }).ToList()
                }).ToList(),
                Records = activeBetrays.Select(b => new ActiveBetrayalSaveData
                {
                    NPCId = b.NPCId,
                    Type = (int)b.Type,
                    Day = b.Day,
                    IsSacrifice = b.IsSacrifice,
                    CanBeResolved = b.CanBeResolved,
                    IsResolved = b.IsResolved,
                    Resolution = (int)b.Resolution
                }).ToList(),
                DepartedCompanionId = DepartedCompanionId.HasValue ? (int)DepartedCompanionId.Value : null
            };
        }

        /// <summary>
        /// 1.2.8: restore from a save; a load replaces the state, it never merges with the one before.
        /// A save written before 1.2.8 has no betrayal state (null): the state starts clean and only
        /// finished story beats are taken from the story flags, so points and pending betrayals
        /// count from the update forward. Call after the story flags are restored.
        /// </summary>
        public void RestoreFromSave(BetrayalSaveData? data)
        {
            Reset();
            if (data == null)
            {
                SeedFromFlags(StoryProgressionSystem.Instance);
                return;
            }

            foreach (var d in data.Profiles ?? new List<BetrayalProfileSaveData>())
            {
                if (string.IsNullOrEmpty(d.NPCId)) continue;
                var profile = GetOrCreateProfile(d.NPCId);
                profile.BetrayalPoints = d.BetrayalPoints;
                profile.IsPendingBetrayal = d.IsPendingBetrayal;
                profile.TriggerReason = d.TriggerReason ?? "";
                profile.HasBetrayed = d.HasBetrayed;
                profile.WasForgiven = d.WasForgiven;
                profile.BetrayalDay = d.BetrayalDay;
                profile.WarningShown = d.WarningShown;
                profile.ActsOfKindness = (d.ActsOfKindness ?? new List<KindnessAct>())
                    .Select(k => new KindnessAct { Reason = k.Reason ?? "", GameDay = k.GameDay }).ToList();
            }

            foreach (var r in data.Records ?? new List<ActiveBetrayalSaveData>())
            {
                if (string.IsNullOrEmpty(r.NPCId)) continue;
                activeBetrays.Add(new ActiveBetrayal
                {
                    NPCId = r.NPCId,
                    NPCName = betrayalProfiles.TryGetValue(r.NPCId, out var p) ? p.NPCName : r.NPCId,
                    Type = (BetrayalType)r.Type,
                    Day = r.Day,
                    IsSacrifice = r.IsSacrifice,
                    CanBeResolved = r.CanBeResolved,
                    IsResolved = r.IsResolved,
                    Resolution = (BetrayalResolution)r.Resolution
                });
            }

            DepartedCompanionId = data.DepartedCompanionId.HasValue ? (CompanionId)data.DepartedCompanionId.Value : null;
        }

        /// <summary>
        /// 1.2.8: an old save's finished betrayals, from its story flags, for the five story betrayers only.
        /// Never sets points or a pending betrayal.
        /// </summary>
        internal void SeedFromFlags(StoryProgressionSystem story)
        {
            foreach (var profile in betrayalProfiles.Values.ToList())
            {
                string id = profile.NPCId;
                bool betrayed = story.HasStoryFlag($"betrayed_by_{id}");
                bool forgave = story.HasStoryFlag($"forgave_{id}");
                bool killed = story.HasStoryFlag($"killed_{id}");
                if (!betrayed && !forgave && !killed) continue;

                profile.HasBetrayed = true;
                if (!forgave && !killed) continue;

                profile.WasForgiven = forgave;
                activeBetrays.Add(new ActiveBetrayal
                {
                    NPCId = id,
                    NPCName = profile.NPCName,
                    Type = profile.BetrayalType,
                    Day = 0,
                    IsSacrifice = profile.IsSacrifice,
                    CanBeResolved = profile.CanBeForgiven,
                    IsResolved = true,
                    Resolution = killed ? BetrayalResolution.Avenged : BetrayalResolution.Forgiven
                });
            }

            // Finished story beats: Lyris gave her life in the paradox; Noctura turned in her fight (no record, Q10)
            if (story.HasStoryFlag("lyris_sacrificed_for_veloura") && betrayalProfiles.TryGetValue("Lyris", out var lyris))
                lyris.HasBetrayed = true;
            if (story.HasStoryFlag("noctura_betrayed") && betrayalProfiles.TryGetValue("TheStranger", out var stranger))
                stranger.HasBetrayed = true;
        }

        private BetrayalProfile GetOrCreateProfile(string npcId)
        {
            if (!betrayalProfiles.TryGetValue(npcId, out var profile))
            {
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
            return profile;
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
        /// <summary>v1.2.5: a story betrayer whose dialogue, motivations and redemption are shown from keys built from NPCId.</summary>
        public bool Keyed { get; set; }
        public List<Grievance> Grievances { get; set; } = new();
        public List<KindnessAct> ActsOfKindness { get; set; } = new();
        /// <summary>1.2.8: the warning before this betrayer turns has been shown (saved).</summary>
        public bool WarningShown { get; set; }
    }

    /// <summary>1.2.8: an act of kindness toward a betrayer, with the game day it happened.</summary>
    public class KindnessAct
    {
        public string Reason { get; set; } = "";
        public int GameDay { get; set; }
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
