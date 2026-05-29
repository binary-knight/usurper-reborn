using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UsurperRemake.UI;
using UsurperRemake.Utils;
using static UsurperRemake.Systems.Loc;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Intimacy System - Generates and displays detailed romance novel-style intimate scenes
    /// with player agency, personality-based variations, and meaningful consequences.
    /// </summary>
    public class IntimacySystem
    {
        private static IntimacySystem? _fallbackInstance;
        public static IntimacySystem Instance
        {
            get
            {
                var ctx = UsurperRemake.Server.SessionContext.Current;
                if (ctx != null) return ctx.Intimacy;
                return _fallbackInstance ??= new IntimacySystem();
            }
        }

        private TerminalEmulator? terminal;
        private Character? player;
        private Random random = new();
        private int _matchCount = 0;

        public IntimacySystem()
        {
            _fallbackInstance = this;
        }

        /// <summary>
        /// Start an intimate scene with one or more partners
        /// </summary>
        public async Task StartIntimateScene(Character player, NPC partner, TerminalEmulator term)
        {
            if (partner.IsDead) return;

            this.player = player;
            this.terminal = term;

            if (!await EnforceDailyIntimateCap()) return;

            var partners = new List<NPC> { partner };
            await RunIntimateScene(partners, IntimacyMood.Passionate, false);
            player.IntimateEncountersToday++;
        }

        /// <summary>
        /// Initiate an intimate scene (called from HomeLocation)
        /// </summary>
        public async Task InitiateIntimateScene(Character player, NPC partner, TerminalEmulator term)
        {
            if (partner.IsDead) return;
            await StartIntimateScene(player, partner, term);
        }

        /// <summary>
        /// Start a group intimate scene
        /// </summary>
        public async Task StartGroupScene(Character player, List<NPC> partners, TerminalEmulator term)
        {
            partners.RemoveAll(p => p.IsDead);
            if (partners.Count == 0) return;

            this.player = player;
            this.terminal = term;

            if (!await EnforceDailyIntimateCap()) return;

            await RunIntimateScene(partners, IntimacyMood.Playful, false);
            player.IntimateEncountersToday++;
        }

        /// <summary>
        /// Apply all intimacy benefits without showing any scene content
        /// Used when player chooses to skip explicit content but still wants the encounter to happen
        /// </summary>
        public async Task ApplyIntimacyBenefitsOnly(Character player, NPC partner, TerminalEmulator term)
        {
            if (partner.IsDead) return;

            this.player = player;
            this.terminal = term;

            if (!await EnforceDailyIntimateCap()) return;
            player.IntimateEncountersToday++;

            var partners = new List<NPC> { partner };
            bool isFirstTime = !RomanceTracker.Instance.EncounterHistory.Any(e => e.PartnerIds.Contains(partner.ID));

            // Record the encounter
            var encounter = new IntimateEncounter
            {
                Date = DateTime.Now,
                Location = "Private quarters",
                PartnerIds = partners.Select(p => p.ID).ToList(),
                Type = EncounterType.Solo,
                Mood = IntimacyMood.Tender,
                IsFirstTime = isFirstTime
            };
            RomanceTracker.Instance.RecordEncounter(encounter);

            // Relationship boost from intimacy (fade-to-black gets fewer steps than full scene)
            int baseSteps = 2;
            foreach (var p in partners)
            {
                RelationshipSystem.UpdateRelationship(player, p, 1, baseSteps, false, true);
                RelationshipSystem.UpdateRelationship(p, player, 1, baseSteps, false, true);
            }

            // Check for pregnancy
            await CheckForPregnancy(partner);
        }

        /// <summary>
        /// Main scene runner
        /// </summary>
        private async Task RunIntimateScene(List<NPC> partners, IntimacyMood mood, bool isFirstTime)
        {
            var primaryPartner = partners.First();
            var profile = primaryPartner.Brain?.Personality;

            // Reset personality match tracking for this scene
            _matchCount = 0;

            // Determine if this is their first time together
            isFirstTime = !RomanceTracker.Instance.EncounterHistory.Any(e => e.PartnerIds.Contains(primaryPartner.ID));

            terminal!.ClearScreen();

            // Check if player wants to skip intimate scenes
            if (player?.SkipIntimateScenes == true)
            {
                // Show "fade to black" version - simple, tasteful summary
                await PlayFadeToBlackScene(primaryPartner, isFirstTime);
            }
            else
            {
                // Full scene with all phases
                // Scene header
                await ShowSceneHeader(primaryPartner, mood);

                // Phase 1: Anticipation / Setting the mood
                await PlayAnticipationPhase(primaryPartner, profile, mood);

                // Phase 2: Exploration
                await PlayExplorationPhase(primaryPartner, profile, mood, isFirstTime);

                // Phase 3: Escalation
                await PlayEscalationPhase(primaryPartner, profile, mood);

                // Phase 4: Climax
                await PlayClimaxPhase(primaryPartner, profile, mood);

                // Phase 5: Afterglow
                await PlayAfterglowPhase(primaryPartner, profile, mood);
            }

            // Record the encounter (always happens regardless of skip setting)
            var encounter = new IntimateEncounter
            {
                Date = DateTime.Now,
                Location = "Private quarters",
                PartnerIds = partners.Select(p => p.ID).ToList(),
                Type = partners.Count > 1 ? EncounterType.Group : EncounterType.Solo,
                Mood = mood,
                IsFirstTime = isFirstTime
            };
            RomanceTracker.Instance.RecordEncounter(encounter);

            // Relationship boost from intimacy — varies by personality match quality
            // 0 matches = 2 steps, 1 = 3, 2 = 5, 3 (perfect) = 7 + Lover's Bliss combat buff
            int baseSteps = _matchCount switch
            {
                0 => 2,
                1 => 3,
                2 => 5,
                3 => 7,
                _ => 3
            };
            // Note: don't apply difficulty multiplier to steps — daily cap in RelationshipSystem
            // handles progression rate. Multiplying steps before the cap bypasses it.

            // Show connection quality summary (only for full scenes, not fade-to-black)
            if (player?.SkipIntimateScenes != true && _matchCount >= 0)
            {
                terminal.WriteLine("");
                if (!GameConfig.ScreenReaderMode)
                {
                    terminal.SetColor("bright_cyan");
                    terminal.WriteLine("  ════════════════════════════════════════════════════════════════");
                }
                switch (_matchCount)
                {
                    case 3:
                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.connection_perfect", primaryPartner.Name2))}");
                        terminal.SetColor("bright_yellow");
                        terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.lovers_bliss"))}");
                        player!.LoversBlissCombats = 5;
                        player.LoversBlissBonus = 0.10f;
                        break;
                    case 2:
                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.connection_strong", primaryPartner.Name2))}");
                        break;
                    case 1:
                        terminal.SetColor("yellow");
                        terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.connection_pleasant", primaryPartner.Name2))}");
                        break;
                    default:
                        terminal.SetColor("gray");
                        terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.connection_awkward", primaryPartner.Name2))}");
                        break;
                }
                if (!GameConfig.ScreenReaderMode)
                {
                    terminal.SetColor("bright_cyan");
                    terminal.WriteLine("  ════════════════════════════════════════════════════════════════");
                }
                terminal.WriteLine("");
                await terminal.GetInput($"  {Get("ui.press_enter")}");
            }

            foreach (var partner in partners)
            {
                // Both directions can deepen past Friendship through intimacy
                RelationshipSystem.UpdateRelationship(player!, partner, 1, baseSteps, false, true);
                RelationshipSystem.UpdateRelationship(partner, player!, 1, baseSteps, false, true);
            }

            // Check for pregnancy (only for opposite-sex spouse encounters)
            await CheckForPregnancy(primaryPartner);
        }

        /// <summary>
        /// "Fade to black" version of intimate scene for players who prefer to skip details
        /// Still provides all the mechanical benefits (relationship boost, pregnancy chance)
        /// </summary>
        private async Task PlayFadeToBlackScene(NPC partner, bool isFirstTime)
        {
            string gender = GameConfig.GetLocalizedSubjectPronoun(partner.Sex).ToLowerInvariant();
            string their = GameConfig.GetLocalizedPossessivePronoun(partner.Sex);

            terminal!.SetColor("dark_magenta");
            terminal.WriteLine("====================================================================");
            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"                         {GameConfig.CleanFormat(Get("intimacy.header_moment"))}                            ");
            terminal.SetColor("dark_magenta");
            terminal.WriteLine("====================================================================");
            terminal.WriteLine("");

            terminal.SetColor("gray");
            if (isFirstTime)
            {
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.fade_first_time", partner.Name2))}");
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.fade_first_time2", their.Substring(0, 1).ToUpper() + their.Substring(1)))}");
            }
            else
            {
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.fade_familiar", partner.Name2))}");
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.fade_familiar2"))}");
            }
            terminal.WriteLine("");

            await Task.Delay(1500);

            terminal.SetColor("dark_magenta");
            terminal.WriteLine("  . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . .");
            terminal.WriteLine("");

            await Task.Delay(1000);

            terminal.SetColor("white");
            terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.fade_later"))}");
            terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.fade_later2", partner.Name2, their))}");
            terminal.WriteLine("");

            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.fade_bond_stronger"))}");
            terminal.WriteLine("");

            await terminal.GetInput($"  {Get("ui.press_enter")}");
        }

        /// <summary>
        /// v0.61.x: enforce the daily intimate-scene cap. Returns true if the
        /// scene may proceed, false if the player has hit MaxIntimateEncountersPerDay
        /// and the caller should bail. Prints a localised flavor line on the
        /// rejection path so the player understands why nothing is happening.
        /// </summary>
        private async Task<bool> EnforceDailyIntimateCap()
        {
            if (player == null) return true; // Should not happen; permissive.
            if (player.IntimateEncountersToday < GameConfig.MaxIntimateEncountersPerDay) return true;

            if (terminal != null)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("");
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.daily_cap_reached"))}");
                terminal.WriteLine("");
                await Task.Delay(1500);
            }
            return false;
        }

        /// <summary>
        /// Check if this intimate encounter results in pregnancy
        /// </summary>
        private async Task CheckForPregnancy(NPC partner)
        {
            // Only opposite-sex couples with spouse can have biological children
            if (player!.Sex == partner.Sex)
                return;

            // Only married couples have pregnancy chance (or lovers for bastard children)
            var romanceType = RomanceTracker.Instance.GetRelationType(partner.ID);
            if (romanceType != RomanceRelationType.Spouse && romanceType != RomanceRelationType.Lover)
                return;

            // v0.63.0 slice 1: defense-in-depth incest gate. The dialogue-side
            // flirt / confess / intimate flows already hide for blood relatives
            // once the gate is wired in, but the romance type (Spouse / Lover)
            // could have been established BEFORE the lineage data existed
            // (legacy saves with adult-child NPCs accidentally married pre-fix).
            // Refuse pregnancy here so the legacy state can't produce a child.
            var family = FamilySystem.Instance;
            if (family != null
                && FamilySystem.IsBlockingRelation(family.GetFamilyRelation(player!, partner)))
                return;

            // v0.61.x: hard cap on living biological children. Once the player
            // has MaxPlayerChildren non-deleted entries in the family registry,
            // pregnancy rolls quietly stop firing. Slot reopens when a child
            // grows to adult (ConvertChildToNPC marks Deleted=true) or dies.
            int livingChildren = FamilySystem.Instance.GetChildrenOf(player!).Count;
            if (livingChildren >= GameConfig.MaxPlayerChildren)
                return;

            // Base pregnancy chance: 15% for spouses, 5% for lovers
            float pregnancyChance = romanceType == RomanceRelationType.Spouse ? 0.15f : 0.05f;

            // Bed quality modifier (v0.44.0): level 0 = -50%, level 5 = +50%
            int bedLevel = Math.Clamp(player!.BedLevel, 0, 5);
            float bedModifier = GameConfig.BedFertilityModifier[bedLevel];
            pregnancyChance *= (1f + bedModifier);

            float roll = (float)random.NextDouble();

            if (roll < pregnancyChance)
            {
                // Pregnancy!
                await AnnouncePregnancy(partner, romanceType == RomanceRelationType.Lover);
            }
        }

        /// <summary>
        /// Announce pregnancy and create child
        /// </summary>
        private async Task AnnouncePregnancy(NPC partner, bool isBastard)
        {
            terminal!.ClearScreen();

            UIHelper.WriteBoxHeader(terminal, GameConfig.CleanFormat(Get("intimacy.blessed_news")), "bright_yellow");
            terminal.WriteLine("");

            await Task.Delay(500);

            string gender = GameConfig.GetLocalizedSubjectPronoun(partner.Sex).ToLowerInvariant();
            string their = GameConfig.GetLocalizedPossessivePronoun(partner.Sex);
            bool partnerIsPregnant = partner.Sex == CharacterSex.Female;

            terminal.SetColor("white");
            terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.weeks_later"))}");
            terminal.WriteLine("");
            await Task.Delay(1000);

            if (partnerIsPregnant)
            {
                terminal.SetColor("bright_magenta");
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.pregnancy_partner_tells", partner.Name2, their))}");
                terminal.WriteLine($"  \"{GameConfig.CleanFormat(Get("intimacy.pregnancy_partner_tells2", player!.Name))}\"");
                terminal.WriteLine("");
                await Task.Delay(1000);
                terminal.SetColor("white");
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.pregnancy_partner_belly", their))}");
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"  \"{GameConfig.CleanFormat(Get("intimacy.pregnancy_announcement"))}\"");
            }
            else
            {
                terminal.SetColor("bright_magenta");
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.pregnancy_player_feeling"))}");
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.pregnancy_player_sickness", partner.Name2, gender))}");
                terminal.WriteLine("");
                await Task.Delay(1000);
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"  \"{GameConfig.CleanFormat(Get("intimacy.pregnancy_partner_asks"))}\"");
                terminal.SetColor("white");
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.pregnancy_player_nods"))}");
            }

            terminal.WriteLine("");
            await Task.Delay(1500);

            // Create the child
            Character mother = partnerIsPregnant ? partner : player!;
            Character father = partnerIsPregnant ? player! : partner;

            var child = Child.CreateChild(mother, father, isBastard);
            child.GenerateNewbornName();

            // Register child with the family system so they can age and become NPCs
            FamilySystem.Instance.RegisterChild(child);

            // Update player child count
            player!.Kids++;

            // Update spouse's child count in RomanceTracker
            RomanceTracker.Instance.AddChildToSpouse(partner.ID);

            // v0.63.0 slice 4 (audit M6): populate the NPC partner's
            // PregnancyFatherName + PregnancyDueDate when the partner is
            // female. The v0.54.0 affair-pregnancy preservation hinges on
            // PregnancyFatherName being non-empty -- without it,
            // WorldSimulator's divorce-and-spouse-cleanup pass at
            // RemoveMarriagesWithDeadOrMissingSpouse clears the pregnancy
            // entirely. Affair child (player is not the spouse) flagged
            // with the player's name; legitimate child also gets the
            // attribution so display sites can tell who the father was.
            if (partnerIsPregnant && partner is NPC npcPartner)
            {
                npcPartner.PregnancyFatherName = player!.Name2 ?? player.Name1 ?? player.Name;
                if (!npcPartner.PregnancyDueDate.HasValue)
                {
                    // Approximate gestation = NpcLifecycleHoursPerYear * 0.75
                    // (3/4 of a year in game time). Real birth fires from the
                    // world sim daily aging tick when the due date passes.
                    npcPartner.PregnancyDueDate = DateTime.UtcNow.AddHours(
                        GameConfig.NpcLifecycleHoursPerYear * 0.75);
                }
            }

            string babyGender = child.Sex == CharacterSex.Male ? GameConfig.CleanFormat(Get("intimacy.baby_boy")) : GameConfig.CleanFormat(Get("intimacy.baby_girl"));

            if (!GameConfig.ScreenReaderMode)
            {
                terminal.SetColor("bright_cyan");
                terminal.WriteLine("  ════════════════════════════════════════════════════════════════");
            }
            terminal.SetColor("bright_green");
            terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.baby_born", babyGender))}");

            // Let the player name their child
            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            string babyPronoun = child.Sex == CharacterSex.Male ? GameConfig.CleanFormat(Get("intimacy.pronoun_him")) : GameConfig.CleanFormat(Get("intimacy.pronoun_her"));
            terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.name_prompt", babyPronoun, child.Name))}");
            terminal.SetColor("white");
            string nameInput = (await terminal.GetInput("  Name: ")).Trim();
            if (!string.IsNullOrEmpty(nameInput) && nameInput.Length <= 20)
            {
                // Extract surname from auto-generated name (everything after first space)
                string surname = "";
                int spaceIdx = child.Name.IndexOf(' ');
                if (spaceIdx > 0) surname = child.Name.Substring(spaceIdx);
                child.Name = nameInput + surname;
            }

            // Persist the new child to world_state immediately. Without this the
            // baby lives only in the in-memory FamilySystem singleton until WorldSim's
            // next tick. If a second session logs in during that window its
            // RestoreStorySystems used to clobber the singleton with stale data and
            // the newborn vanished. RestoreStorySystems now skips children in online
            // mode (v0.60.1) but writing here also makes the birth durable across an
            // unexpected MUD server restart.
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode && OnlineStateManager.Instance != null)
            {
                _ = OnlineStateManager.Instance.SaveSharedChildrenNow();
            }

            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.baby_named", babyPronoun, child.Name))}");
            if (!GameConfig.ScreenReaderMode)
            {
                terminal.SetColor("bright_cyan");
                terminal.WriteLine("  ════════════════════════════════════════════════════════════════");
            }
            terminal.WriteLine("");

            // Generate birth news for the realm
            bool motherIsNPC = partnerIsPregnant;
            NewsSystem.Instance?.WriteBirthNews(mother.Name, father.Name, child.Name, motherIsNPC);

            terminal.SetColor("white");
            terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.family_grown"))}");
            terminal.WriteLine("");

            await terminal.GetInput($"  {Get("ui.press_enter")}");
        }

        /// <summary>
        /// Evaluate whether the player's choice matches the NPC's personality preferences.
        /// Returns true if matched, and increments _matchCount.
        /// </summary>
        private bool EvaluateChoice(NPC partner, int phase, string choice)
        {
            var profile = partner.Brain?.Personality;
            if (profile == null) return false;

            bool matched = false;

            // Trait threshold for matching — lower means more forgiving.
            // Base traits are 0.2-0.8 (mean 0.5). At 0.35f, most NPCs have clear
            // preferences while archetype-reduced traits (e.g. thug tenderness 0.08-0.32)
            // still correctly fail, reflecting the NPC's personality.
            const float t = 0.35f;

            switch (phase)
            {
                case 1: // Anticipation: How do you begin?
                    matched = choice switch
                    {
                        "1" => profile.Tenderness > t || profile.Romanticism > t,              // Take it slow
                        "2" => profile.Passion > t || profile.Sensuality > t,                   // Pull them close
                        "3" => profile.IntimateStyle == RomanceStyle.Dominant ||                 // Let them lead
                               profile.IntimateStyle == RomanceStyle.Switch,
                        _ => false
                    };
                    break;

                case 3: // Escalation: What do you whisper?
                    matched = choice switch
                    {
                        "1" => profile.Romanticism > t || profile.Tenderness > t,              // "You're so beautiful"
                        "2" => profile.Passion > t || profile.Adventurousness > t,              // "I need you. Now."
                        "3" => profile.Sensuality > t ||                                        // "Tell me what you want"
                               profile.IntimateStyle == RomanceStyle.Dominant,
                        _ => false
                    };
                    break;

                case 5: // Afterglow: What do you say?
                    matched = choice switch
                    {
                        "1" => profile.Commitment > t || profile.Romanticism > t,              // "Stay with me tonight"
                        "2" => profile.Passion > t || profile.Sensuality > t,                   // "That was amazing"
                        "3" => profile.Tenderness > t || profile.Patience > t,                  // Hold them close
                        _ => false
                    };
                    break;
            }

            if (matched) _matchCount++;
            return matched;
        }

        /// <summary>
        /// Show colored feedback after a player choice so they learn the NPC's preferences.
        /// </summary>
        private void ShowChoiceReaction(NPC partner, bool matched)
        {
            string their = GameConfig.GetLocalizedSubjectPronoun(partner.Sex).ToLowerInvariant();
            terminal!.WriteLine("");

            if (matched)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine($"  * {GameConfig.CleanFormat(Get("intimacy.reaction_matched", partner.Name2, their))}");
            }
            else
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"  * {GameConfig.CleanFormat(Get("intimacy.reaction_unmatched", partner.Name2))}");
            }

            terminal.SetColor("white");
        }

        /// <summary>
        /// Display scene header
        /// </summary>
        private async Task ShowSceneHeader(NPC partner, IntimacyMood mood)
        {
            UIHelper.WriteBoxHeader(terminal!, GameConfig.CleanFormat(Get("intimacy.header_encounter")), "dark_red");
            terminal.WriteLine("");

            terminal.SetColor("gray");
            string moodDesc = mood switch
            {
                IntimacyMood.Tender => GameConfig.CleanFormat(Get("intimacy.mood_tender")),
                IntimacyMood.Passionate => GameConfig.CleanFormat(Get("intimacy.mood_passionate")),
                IntimacyMood.Rough => GameConfig.CleanFormat(Get("intimacy.mood_rough")),
                IntimacyMood.Playful => GameConfig.CleanFormat(Get("intimacy.mood_playful")),
                IntimacyMood.Kinky => GameConfig.CleanFormat(Get("intimacy.mood_kinky")),
                IntimacyMood.Romantic => GameConfig.CleanFormat(Get("intimacy.mood_romantic")),
                IntimacyMood.Quick => GameConfig.CleanFormat(Get("intimacy.mood_quick")),
                _ => GameConfig.CleanFormat(Get("intimacy.mood_default"))
            };
            terminal.WriteLine($"  {moodDesc}");
            terminal.WriteLine("");

            await Task.Delay(1500);
        }

        /// <summary>
        /// Phase 1: Building anticipation
        /// </summary>
        private async Task PlayAnticipationPhase(NPC partner, PersonalityProfile? profile, IntimacyMood mood)
        {
            terminal!.SetColor("bright_cyan");
            terminal.WriteLine($"  --- {GameConfig.CleanFormat(Get("intimacy.phase_anticipation"))} ---");
            terminal.WriteLine("");

            string gender = GameConfig.GetLocalizedSubjectPronoun(partner.Sex).ToLowerInvariant();
            string genderCap = GameConfig.GetLocalizedSubjectPronoun(partner.Sex);
            string their = GameConfig.GetLocalizedPossessivePronoun(partner.Sex);
            string them = partner.Sex == CharacterSex.Female ? "her" : "him";

            // Setting description
            terminal.SetColor("white");
            terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.anticipation_alone", partner.Name2))}");
            terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.anticipation_turns", genderCap))}");
            terminal.WriteLine("");

            await Task.Delay(1000);

            // Player choice for pacing
            terminal.SetColor("cyan");
            terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.how_begin"))}");
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("1");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(GameConfig.CleanFormat(Get("intimacy.choice_slow")));

            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("2");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(GameConfig.CleanFormat(Get("intimacy.choice_pull_close")));

            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("3");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(GameConfig.CleanFormat(Get("intimacy.choice_let_lead")));
            terminal.WriteLine("");

            string choice = await terminal.GetInput($"  {Get("ui.your_choice")}");

            bool phase1Match = EvaluateChoice(partner, 1, choice);
            ShowChoiceReaction(partner, phase1Match);

            terminal.ClearScreen();
            await ShowSceneHeader(partner, mood);

            string theirCap = their.Substring(0, 1).ToUpper() + their.Substring(1);
            switch (choice)
            {
                case "1":
                    terminal.SetColor("white");
                    terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.anticipation.slow_l1", their))}");
                    terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.anticipation.slow_l2", genderCap))}");
                    terminal.WriteLine("");
                    terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.anticipation.slow_l3", player!.Name, gender))}");
                    terminal.WriteLine("");
                    terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.anticipation.slow_l4", their))}");
                    terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.anticipation.slow_l5", their))}");
                    break;

                case "2":
                    terminal.SetColor("white");
                    terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.anticipation.urgent_l1", partner.Name2))}");
                    terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.anticipation.urgent_l2", their))}");
                    terminal.WriteLine("");
                    terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.anticipation.urgent_l3", genderCap, their))}");
                    terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.anticipation.urgent_l4"))}");
                    terminal.WriteLine("");
                    terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.anticipation.urgent_l5", gender))}");
                    break;

                case "3":
                default:
                    terminal.SetColor("white");
                    terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.anticipation.passive_l1", partner.Name2))}");
                    terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.anticipation.passive_l2", genderCap))}");
                    terminal.WriteLine("");
                    terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.anticipation.passive_l3", theirCap))}");
                    terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.anticipation.passive_l4", gender))}");
                    break;
            }

            terminal.WriteLine("");
            await terminal.GetInput($"  {Get("ui.press_enter")}");
        }

        /// <summary>
        /// Phase 2: Physical exploration
        /// </summary>
        private async Task PlayExplorationPhase(NPC partner, PersonalityProfile? profile, IntimacyMood mood, bool isFirstTime)
        {
            terminal!.ClearScreen();
            await ShowSceneHeader(partner, mood);

            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"  --- {GameConfig.CleanFormat(Get("intimacy.phase_exploration"))} ---");
            terminal.WriteLine("");

            string gender = GameConfig.GetLocalizedSubjectPronoun(partner.Sex).ToLowerInvariant();
            string genderCap = GameConfig.GetLocalizedSubjectPronoun(partner.Sex);
            string their = GameConfig.GetLocalizedPossessivePronoun(partner.Sex);
            string them = partner.Sex == CharacterSex.Female ? "her" : "him";

            string theirCap = their.Substring(0, 1).ToUpper() + their.Substring(1);

            // The kiss
            terminal.SetColor("white");
            if (isFirstTime)
            {
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.exploration.kiss_first_l1", partner.Name2))}");
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.exploration.kiss_first_l2", genderCap))}");
            }
            else
            {
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.exploration.kiss_familiar_l1"))}");
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.exploration.kiss_familiar_l2", partner.Name2))}");
            }
            terminal.WriteLine("");

            await Task.Delay(1500);

            // Undressing
            terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.exploration.undress_intro"))}");

            float sensuality = profile?.Sensuality ?? 0.6f;
            if (sensuality > 0.7f)
            {
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.exploration.undress_sensual_l1", partner.Name2))}");
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.exploration.undress_sensual_l2"))}");
            }
            else if (sensuality > 0.4f)
            {
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.exploration.undress_eager_l1"))}");
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.exploration.undress_eager_l2"))}");
            }
            else
            {
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.exploration.undress_shy_l1", genderCap, gender))}");
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.exploration.undress_shy_l2"))}");
            }
            terminal.WriteLine("");

            await Task.Delay(1000);

            // Physical description based on NPC
            terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.exploration.skin_meet"))}");

            string physicalDesc = partner.Race switch
            {
                CharacterRace.Elf => $"  {GameConfig.CleanFormat(Get("intimacy.exploration.body_elf", theirCap))}",
                CharacterRace.Dwarf => $"  {GameConfig.CleanFormat(Get("intimacy.exploration.body_dwarf", theirCap))}",
                CharacterRace.Orc => $"  {GameConfig.CleanFormat(Get("intimacy.exploration.body_orc", theirCap))}",
                CharacterRace.Hobbit => $"  {GameConfig.CleanFormat(Get("intimacy.exploration.body_hobbit", theirCap))}",
                _ => $"  {GameConfig.CleanFormat(Get("intimacy.exploration.body_default", theirCap))}"
            };
            terminal.WriteLine(physicalDesc);
            terminal.WriteLine("");

            await terminal.GetInput($"  {Get("ui.press_enter")}");
        }

        /// <summary>
        /// Phase 3: Building intensity
        /// </summary>
        private async Task PlayEscalationPhase(NPC partner, PersonalityProfile? profile, IntimacyMood mood)
        {
            terminal!.ClearScreen();
            await ShowSceneHeader(partner, mood);

            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"  --- {GameConfig.CleanFormat(Get("intimacy.phase_escalation"))} ---");
            terminal.WriteLine("");

            string gender = GameConfig.GetLocalizedSubjectPronoun(partner.Sex).ToLowerInvariant();
            string genderCap = GameConfig.GetLocalizedSubjectPronoun(partner.Sex);
            string their = GameConfig.GetLocalizedPossessivePronoun(partner.Sex);
            string them = partner.Sex == CharacterSex.Female ? "her" : "him";

            // Foreplay descriptions based on personality
            float passion = profile?.Passion ?? 0.6f;
            float tenderness = profile?.Tenderness ?? 0.5f;

            terminal.SetColor("white");
            terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.escalation.explore_intro", partner.Name2, them))}");
            terminal.WriteLine("");

            if (passion > 0.7f)
            {
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.escalation.passion_l1", genderCap))}");
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.escalation.passion_l2"))}");
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.escalation.passion_l3", gender))}");
            }
            else if (tenderness > 0.7f)
            {
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.escalation.tender_l1", genderCap))}");
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.escalation.tender_l2", their))}");
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.escalation.tender_l3", gender))}");
            }
            else
            {
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.escalation.neutral_l1", genderCap))}");
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.escalation.neutral_l2"))}");
            }
            terminal.WriteLine("");

            await Task.Delay(1500);

            // Verbal intimacy
            terminal.SetColor("cyan");
            terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.what_whisper"))}");
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("1");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(GameConfig.CleanFormat(Get("intimacy.whisper_beautiful")));

            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("2");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(GameConfig.CleanFormat(Get("intimacy.whisper_need_you")));

            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("3");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(GameConfig.CleanFormat(Get("intimacy.whisper_tell_me")));
            terminal.WriteLine("");

            string choice = await terminal.GetInput($"  {Get("ui.your_choice")}");

            bool phase3Match = EvaluateChoice(partner, 3, choice);
            ShowChoiceReaction(partner, phase3Match);

            terminal.ClearScreen();
            await ShowSceneHeader(partner, mood);

            switch (choice)
            {
                case "1":
                    terminal.SetColor("white");
                    terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.escalation.whisper_beautiful_l1", their))}");
                    terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.escalation.whisper_beautiful_l2", partner.Name2))}");
                    terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.escalation.whisper_beautiful_l3", gender))}");
                    break;

                case "2":
                    terminal.SetColor("white");
                    terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.escalation.whisper_need_l1"))}");
                    terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.escalation.whisper_need_l2", partner.Name2, genderCap))}");
                    terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.escalation.whisper_need_l3", gender))}");
                    break;

                case "3":
                default:
                    terminal.SetColor("white");
                    terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.escalation.whisper_tell_l1", their))}");
                    terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.escalation.whisper_tell_l2", partner.Name2))}");
                    terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.escalation.whisper_tell_l3", gender))}");
                    break;
            }

            terminal.WriteLine("");
            await terminal.GetInput($"  {Get("ui.press_enter")}");
        }

        /// <summary>
        /// Phase 4: The climax
        /// </summary>
        private async Task PlayClimaxPhase(NPC partner, PersonalityProfile? profile, IntimacyMood mood)
        {
            terminal!.ClearScreen();
            await ShowSceneHeader(partner, mood);

            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"  --- {GameConfig.CleanFormat(Get("intimacy.phase_climax"))} ---");
            terminal.WriteLine("");

            string gender = GameConfig.GetLocalizedSubjectPronoun(partner.Sex).ToLowerInvariant();
            string genderCap = GameConfig.GetLocalizedSubjectPronoun(partner.Sex);
            string their = GameConfig.GetLocalizedPossessivePronoun(partner.Sex);
            string them = partner.Sex == CharacterSex.Female ? "her" : "him";

            terminal.SetColor("white");
            terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.climax.intertwine_l1"))}");
            terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.climax.intertwine_l2", partner.Name2))}");
            terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.climax.intertwine_l3"))}");
            terminal.WriteLine("");

            await Task.Delay(1500);

            terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.climax.tension_l1"))}");
            terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.climax.tension_l2"))}");
            terminal.WriteLine("");

            float passion = profile?.Passion ?? 0.6f;

            if (passion > 0.7f)
            {
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.climax.loud_l1", partner.Name2))}");
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.climax.loud_l2", genderCap))}");
            }
            else
            {
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.climax.soft_l1", partner.Name2))}");
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.climax.soft_l2", genderCap, their))}");
            }
            terminal.WriteLine("");

            await Task.Delay(1500);

            terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.climax.crest_l1", partner.Name2))}");
            terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.climax.crest_l2", them))}");
            terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.climax.crest_l3"))}");
            terminal.WriteLine("");

            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.climax.timeless_l1"))}");
            terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.climax.timeless_l2"))}");
            terminal.WriteLine("");

            await terminal.GetInput($"  {Get("ui.press_enter")}");
        }

        /// <summary>
        /// Phase 5: The aftermath
        /// </summary>
        private async Task PlayAfterglowPhase(NPC partner, PersonalityProfile? profile, IntimacyMood mood)
        {
            terminal!.ClearScreen();
            await ShowSceneHeader(partner, mood);

            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"  --- {GameConfig.CleanFormat(Get("intimacy.phase_afterglow"))} ---");
            terminal.WriteLine("");

            string gender = GameConfig.GetLocalizedSubjectPronoun(partner.Sex).ToLowerInvariant();
            string genderCap = GameConfig.GetLocalizedSubjectPronoun(partner.Sex);
            string their = GameConfig.GetLocalizedPossessivePronoun(partner.Sex);
            string them = partner.Sex == CharacterSex.Female ? "her" : "him";

            terminal.SetColor("white");
            terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.afterglow.tangled_l1"))}");
            terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.afterglow.tangled_l2", partner.Name2))}");
            terminal.WriteLine("");

            await Task.Delay(1000);

            float romanticism = profile?.Romanticism ?? 0.5f;
            var romanceType = RomanceTracker.Instance.GetRelationType(partner.ID);

            if (romanticism > 0.7f || romanceType == RomanceRelationType.Spouse)
            {
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.afterglow.spouse_l1", gender))}");
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.afterglow.spouse_l2"))}");
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.afterglow.spouse_l3"))}");
            }
            else if (romanceType == RomanceRelationType.Lover)
            {
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.afterglow.lover_l1", gender))}");
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.afterglow.lover_l2"))}");
            }
            else
            {
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.afterglow.casual_l1", genderCap, their))}");
                terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.afterglow.casual_l2", gender))}");
            }
            terminal.WriteLine("");

            await Task.Delay(1000);

            // Pillow talk options
            terminal.SetColor("cyan");
            terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.what_say"))}");
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("1");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(GameConfig.CleanFormat(Get("intimacy.say_stay")));

            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("2");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(GameConfig.CleanFormat(Get("intimacy.say_amazing")));

            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("3");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(GameConfig.CleanFormat(Get("intimacy.say_hold_close")));
            terminal.WriteLine("");

            string choice = await terminal.GetInput($"  {Get("ui.your_choice")}");

            bool phase5Match = EvaluateChoice(partner, 5, choice);
            ShowChoiceReaction(partner, phase5Match);

            terminal.WriteLine("");

            switch (choice)
            {
                case "1":
                    terminal.SetColor("white");
                    terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.afterglow.stay_l1", them))}");
                    terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.afterglow.stay_l2", partner.Name2))}");
                    terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.afterglow.stay_l3"))}");
                    terminal.WriteLine("");
                    terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.afterglow.stay_l4"))}");
                    break;

                case "2":
                    terminal.SetColor("white");
                    terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.afterglow.amazing_l1", their))}");
                    terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.afterglow.amazing_l2", partner.Name2))}");
                    terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.afterglow.amazing_l3"))}");
                    break;

                case "3":
                default:
                    terminal.SetColor("white");
                    terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.afterglow.silent_l1", partner.Name2))}");
                    terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.afterglow.silent_l2"))}");
                    terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.afterglow.silent_l3", genderCap))}");
                    break;
            }

            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine($"  {GameConfig.CleanFormat(Get("intimacy.time_passes"))}");
            terminal.WriteLine("");

            await terminal.GetInput($"  {Get("ui.press_enter")}");
        }
    }
}
