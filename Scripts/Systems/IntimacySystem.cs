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

            var partners = new List<NPC> { partner };
            await RunIntimateScene(partners, IntimacyMood.Passionate, false);
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

            await RunIntimateScene(partners, IntimacyMood.Playful, false);
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
                        terminal.WriteLine($"  {Get("intimacy.connection_perfect", primaryPartner.Name2)}");
                        terminal.SetColor("bright_yellow");
                        terminal.WriteLine($"  {Get("intimacy.lovers_bliss")}");
                        player!.LoversBlissCombats = 5;
                        player.LoversBlissBonus = 0.10f;
                        break;
                    case 2:
                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"  {Get("intimacy.connection_strong", primaryPartner.Name2)}");
                        break;
                    case 1:
                        terminal.SetColor("yellow");
                        terminal.WriteLine($"  {Get("intimacy.connection_pleasant", primaryPartner.Name2)}");
                        break;
                    default:
                        terminal.SetColor("gray");
                        terminal.WriteLine($"  {Get("intimacy.connection_awkward", primaryPartner.Name2)}");
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
            string gender = partner.Sex == CharacterSex.Female ? "she" : "he";
            string their = partner.Sex == CharacterSex.Female ? "her" : "his";

            terminal!.SetColor("dark_magenta");
            terminal.WriteLine("====================================================================");
            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"                         {Get("intimacy.header_moment")}                            ");
            terminal.SetColor("dark_magenta");
            terminal.WriteLine("====================================================================");
            terminal.WriteLine("");

            terminal.SetColor("gray");
            if (isFirstTime)
            {
                terminal.WriteLine($"  {Get("intimacy.fade_first_time", partner.Name2)}");
                terminal.WriteLine($"  {Get("intimacy.fade_first_time2", their.Substring(0, 1).ToUpper() + their.Substring(1))}");
            }
            else
            {
                terminal.WriteLine($"  {Get("intimacy.fade_familiar", partner.Name2)}");
                terminal.WriteLine($"  {Get("intimacy.fade_familiar2")}");
            }
            terminal.WriteLine("");

            await Task.Delay(1500);

            terminal.SetColor("dark_magenta");
            terminal.WriteLine("  . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . .");
            terminal.WriteLine("");

            await Task.Delay(1000);

            terminal.SetColor("white");
            terminal.WriteLine($"  {Get("intimacy.fade_later")}");
            terminal.WriteLine($"  {Get("intimacy.fade_later2", partner.Name2, their)}");
            terminal.WriteLine("");

            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"  {Get("intimacy.fade_bond_stronger")}");
            terminal.WriteLine("");

            await terminal.GetInput($"  {Get("ui.press_enter")}");
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

            UIHelper.WriteBoxHeader(terminal, Get("intimacy.blessed_news"), "bright_yellow");
            terminal.WriteLine("");

            await Task.Delay(500);

            string gender = partner.Sex == CharacterSex.Female ? "she" : "he";
            string their = partner.Sex == CharacterSex.Female ? "her" : "his";
            bool partnerIsPregnant = partner.Sex == CharacterSex.Female;

            terminal.SetColor("white");
            terminal.WriteLine($"  {Get("intimacy.weeks_later")}");
            terminal.WriteLine("");
            await Task.Delay(1000);

            if (partnerIsPregnant)
            {
                terminal.SetColor("bright_magenta");
                terminal.WriteLine($"  {Get("intimacy.pregnancy_partner_tells", partner.Name2, their)}");
                terminal.WriteLine($"  \"{Get("intimacy.pregnancy_partner_tells2", player!.Name)}\"");
                terminal.WriteLine("");
                await Task.Delay(1000);
                terminal.SetColor("white");
                terminal.WriteLine($"  {Get("intimacy.pregnancy_partner_belly", their)}");
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"  \"{Get("intimacy.pregnancy_announcement")}\"");
            }
            else
            {
                terminal.SetColor("bright_magenta");
                terminal.WriteLine($"  {Get("intimacy.pregnancy_player_feeling")}");
                terminal.WriteLine($"  {Get("intimacy.pregnancy_player_sickness", partner.Name2, gender)}");
                terminal.WriteLine("");
                await Task.Delay(1000);
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"  \"{Get("intimacy.pregnancy_partner_asks")}\"");
                terminal.SetColor("white");
                terminal.WriteLine($"  {Get("intimacy.pregnancy_player_nods")}");
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

            if (!GameConfig.ScreenReaderMode)
            {
                terminal.SetColor("bright_cyan");
                terminal.WriteLine("  ════════════════════════════════════════════════════════════════");
            }
            terminal.SetColor("bright_green");
            string babyGender = child.Sex == CharacterSex.Male ? Get("intimacy.baby_boy") : Get("intimacy.baby_girl");
            terminal.WriteLine($"  {Get("intimacy.baby_born", babyGender)}");
            terminal.SetColor("bright_yellow");
            string babyPronoun = child.Sex == CharacterSex.Male ? Get("intimacy.pronoun_him") : Get("intimacy.pronoun_her");
            terminal.WriteLine($"  {Get("intimacy.baby_named", babyPronoun, child.Name)}");
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
            terminal.WriteLine($"  {Get("intimacy.family_grown")}");
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
            string their = partner.Sex == CharacterSex.Female ? "she" : "he";
            terminal!.WriteLine("");

            if (matched)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine($"  * {Get("intimacy.reaction_matched", partner.Name2, their)}");
            }
            else
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"  * {Get("intimacy.reaction_unmatched", partner.Name2)}");
            }

            terminal.SetColor("white");
        }

        /// <summary>
        /// Display scene header
        /// </summary>
        private async Task ShowSceneHeader(NPC partner, IntimacyMood mood)
        {
            UIHelper.WriteBoxHeader(terminal!, Get("intimacy.header_encounter"), "dark_red");
            terminal.WriteLine("");

            terminal.SetColor("gray");
            string moodDesc = mood switch
            {
                IntimacyMood.Tender => Get("intimacy.mood_tender"),
                IntimacyMood.Passionate => Get("intimacy.mood_passionate"),
                IntimacyMood.Rough => Get("intimacy.mood_rough"),
                IntimacyMood.Playful => Get("intimacy.mood_playful"),
                IntimacyMood.Kinky => Get("intimacy.mood_kinky"),
                IntimacyMood.Romantic => Get("intimacy.mood_romantic"),
                IntimacyMood.Quick => Get("intimacy.mood_quick"),
                _ => Get("intimacy.mood_default")
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
            terminal.WriteLine($"  --- {Get("intimacy.phase_anticipation")} ---");
            terminal.WriteLine("");

            string gender = partner.Sex == CharacterSex.Female ? "she" : "he";
            string genderCap = partner.Sex == CharacterSex.Female ? "She" : "He";
            string their = partner.Sex == CharacterSex.Female ? "her" : "his";
            string them = partner.Sex == CharacterSex.Female ? "her" : "him";

            // Setting description
            terminal.SetColor("white");
            terminal.WriteLine($"  {Get("intimacy.anticipation_alone", partner.Name2)}");
            terminal.WriteLine($"  {Get("intimacy.anticipation_turns", genderCap)}");
            terminal.WriteLine("");

            await Task.Delay(1000);

            // Player choice for pacing
            terminal.SetColor("cyan");
            terminal.WriteLine($"  {Get("intimacy.how_begin")}");
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("1");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Get("intimacy.choice_slow"));

            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("2");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Get("intimacy.choice_pull_close"));

            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("3");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Get("intimacy.choice_let_lead"));
            terminal.WriteLine("");

            string choice = await terminal.GetInput($"  {Get("ui.your_choice")}");

            bool phase1Match = EvaluateChoice(partner, 1, choice);
            ShowChoiceReaction(partner, phase1Match);

            terminal.ClearScreen();
            await ShowSceneHeader(partner, mood);

            switch (choice)
            {
                case "1":
                    terminal.SetColor("white");
                    terminal.WriteLine($"  You reach out slowly, fingertips brushing {their} cheek with feather-light");
                    terminal.WriteLine($"  touch. {genderCap} shivers at the contact, eyes fluttering closed.");
                    terminal.WriteLine("");
                    terminal.WriteLine($"  \"{player!.Name}...\" {gender} breathes, voice barely a whisper.");
                    terminal.WriteLine("");
                    terminal.WriteLine($"  You trace the line of {their} jaw, then cup {their} face gently,");
                    terminal.WriteLine($"  tilting {their} chin up. Your lips hover just a breath apart.");
                    break;

                case "2":
                    terminal.SetColor("white");
                    terminal.WriteLine($"  You can't wait any longer. You pull {partner.Name2} against you,");
                    terminal.WriteLine($"  feeling the heat of {their} body through your clothes.");
                    terminal.WriteLine("");
                    terminal.WriteLine($"  {genderCap} gasps at your urgency, but {their} arms wrap around you");
                    terminal.WriteLine($"  immediately, fingers digging into your back.");
                    terminal.WriteLine("");
                    terminal.WriteLine($"  \"I've wanted this...\" {gender} murmurs against your neck.");
                    break;

                case "3":
                default:
                    terminal.SetColor("white");
                    terminal.WriteLine($"  You stand still, letting {partner.Name2} come to you.");
                    terminal.WriteLine($"  {genderCap} approaches with predatory grace, eyes never leaving yours.");
                    terminal.WriteLine("");
                    terminal.WriteLine($"  {their.Substring(0, 1).ToUpper() + their.Substring(1)} hands find your chest, sliding up slowly.");
                    terminal.WriteLine($"  \"Let me show you what I've been thinking about...\" {gender} whispers.");
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
            terminal.WriteLine($"  --- {Get("intimacy.phase_exploration")} ---");
            terminal.WriteLine("");

            string gender = partner.Sex == CharacterSex.Female ? "she" : "he";
            string genderCap = partner.Sex == CharacterSex.Female ? "She" : "He";
            string their = partner.Sex == CharacterSex.Female ? "her" : "his";
            string them = partner.Sex == CharacterSex.Female ? "her" : "him";

            // The kiss
            terminal.SetColor("white");
            if (isFirstTime)
            {
                terminal.WriteLine($"  Your lips finally meet, and it's electric. {partner.Name2} tastes like");
                terminal.WriteLine($"  honey and desire. {genderCap} pulls you closer, deepening the kiss.");
            }
            else
            {
                terminal.WriteLine($"  You know each other's rhythm by now. The kiss is deep and familiar,");
                terminal.WriteLine($"  but no less intoxicating. {partner.Name2} moans softly against your mouth.");
            }
            terminal.WriteLine("");

            await Task.Delay(1500);

            // Undressing
            terminal.WriteLine($"  Hands find clasps and ties. Clothes begin to fall away.");

            float sensuality = profile?.Sensuality ?? 0.6f;
            if (sensuality > 0.7f)
            {
                terminal.WriteLine($"  {partner.Name2} undresses slowly, teasingly, watching your reaction");
                terminal.WriteLine($"  with a knowing smile. Every reveal is deliberate, tantalizing.");
            }
            else if (sensuality > 0.4f)
            {
                terminal.WriteLine($"  There's an eager fumbling, both of you too hungry to be graceful.");
                terminal.WriteLine($"  Fabric tears slightly - neither of you cares.");
            }
            else
            {
                terminal.WriteLine($"  {genderCap} seems almost shy, turning slightly as {gender} undresses.");
                terminal.WriteLine($"  You find the vulnerability endearing, touching.");
            }
            terminal.WriteLine("");

            await Task.Delay(1000);

            // Physical description based on NPC
            terminal.WriteLine($"  Bare skin meets bare skin. The sensation is overwhelming.");

            string physicalDesc = partner.Race switch
            {
                CharacterRace.Elf => $"  {their.Substring(0, 1).ToUpper() + their.Substring(1)} elven form is lithe and graceful, skin almost luminous in the dim light.",
                CharacterRace.Dwarf => $"  {their.Substring(0, 1).ToUpper() + their.Substring(1)} sturdy frame is surprisingly soft in your arms, skin warm against yours.",
                CharacterRace.Orc => $"  {their.Substring(0, 1).ToUpper() + their.Substring(1)} powerful body is impressive, green-tinged skin warm with desire.",
                CharacterRace.Hobbit => $"  {their.Substring(0, 1).ToUpper() + their.Substring(1)} smaller frame fits perfectly against you, soft and inviting.",
                _ => $"  {their.Substring(0, 1).ToUpper() + their.Substring(1)} body is warm and welcoming, curves and planes you want to memorize."
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
            terminal.WriteLine($"  --- {Get("intimacy.phase_escalation")} ---");
            terminal.WriteLine("");

            string gender = partner.Sex == CharacterSex.Female ? "she" : "he";
            string genderCap = partner.Sex == CharacterSex.Female ? "She" : "He";
            string their = partner.Sex == CharacterSex.Female ? "her" : "his";
            string them = partner.Sex == CharacterSex.Female ? "her" : "him";

            // Foreplay descriptions based on personality
            float passion = profile?.Passion ?? 0.6f;
            float tenderness = profile?.Tenderness ?? 0.5f;

            terminal.SetColor("white");
            terminal.WriteLine($"  Your hands explore {partner.Name2}'s body, learning what makes {them} gasp.");
            terminal.WriteLine("");

            if (passion > 0.7f)
            {
                terminal.WriteLine($"  {genderCap} is insatiable, pulling you closer, demanding more.");
                terminal.WriteLine($"  Nails rake down your back. Teeth find your shoulder.");
                terminal.WriteLine($"  \"Don't hold back,\" {gender} growls. \"I can take it.\"");
            }
            else if (tenderness > 0.7f)
            {
                terminal.WriteLine($"  Every touch is deliberate, worshipful. {genderCap} traces patterns");
                terminal.WriteLine($"  on your skin, leaving trails of fire in {their} wake.");
                terminal.WriteLine($"  \"You're beautiful,\" {gender} whispers. \"Let me show you how much I want you.\"");
            }
            else
            {
                terminal.WriteLine($"  {genderCap} responds eagerly, matching your rhythm, finding yours.");
                terminal.WriteLine($"  Breath quickens. Hearts pound. The world narrows to just the two of you.");
            }
            terminal.WriteLine("");

            await Task.Delay(1500);

            // Verbal intimacy
            terminal.SetColor("cyan");
            terminal.WriteLine($"  {Get("intimacy.what_whisper")}");
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("1");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Get("intimacy.whisper_beautiful"));

            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("2");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Get("intimacy.whisper_need_you"));

            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("3");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Get("intimacy.whisper_tell_me"));
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
                    terminal.WriteLine($"  \"You're so beautiful,\" you murmur against {their} skin.");
                    terminal.WriteLine($"  {partner.Name2} shudders, pulling you closer.");
                    terminal.WriteLine($"  \"And you make me feel beautiful,\" {gender} replies, voice thick with emotion.");
                    break;

                case "2":
                    terminal.SetColor("white");
                    terminal.WriteLine($"  \"I need you. Now,\" you say, voice rough with desire.");
                    terminal.WriteLine($"  {partner.Name2}'s breath catches. {genderCap} nods, eyes blazing.");
                    terminal.WriteLine($"  \"Then take me,\" {gender} says. \"I'm yours.\"");
                    break;

                case "3":
                default:
                    terminal.SetColor("white");
                    terminal.WriteLine($"  \"Tell me what you want,\" you say, pausing to meet {their} eyes.");
                    terminal.WriteLine($"  {partner.Name2} considers, then leans close to whisper in your ear.");
                    terminal.WriteLine($"  What {gender} describes makes your heart race faster.");
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
            terminal.WriteLine($"  --- {Get("intimacy.phase_climax")} ---");
            terminal.WriteLine("");

            string gender = partner.Sex == CharacterSex.Female ? "she" : "he";
            string genderCap = partner.Sex == CharacterSex.Female ? "She" : "He";
            string their = partner.Sex == CharacterSex.Female ? "her" : "his";
            string them = partner.Sex == CharacterSex.Female ? "her" : "him";

            terminal.SetColor("white");
            terminal.WriteLine($"  Bodies intertwine, moving together in a rhythm as old as time.");
            terminal.WriteLine($"  {partner.Name2}'s eyes lock with yours, and in that gaze is everything -");
            terminal.WriteLine($"  trust, desire, vulnerability, need.");
            terminal.WriteLine("");

            await Task.Delay(1500);

            terminal.WriteLine($"  The tension builds, an inexorable tide. Breath comes faster.");
            terminal.WriteLine($"  Whispered words become moans, then cries of pleasure.");
            terminal.WriteLine("");

            float passion = profile?.Passion ?? 0.6f;

            if (passion > 0.7f)
            {
                terminal.WriteLine($"  {partner.Name2} is loud and unashamed, urging you on with breathless demands.");
                terminal.WriteLine($"  \"Yes! More! Don't stop!\" {genderCap} arches against you, lost in sensation.");
            }
            else
            {
                terminal.WriteLine($"  {partner.Name2}'s sounds are softer but no less intense - gasps and sighs");
                terminal.WriteLine($"  that speak volumes. {genderCap} clings to you like you're {their} anchor.");
            }
            terminal.WriteLine("");

            await Task.Delay(1500);

            terminal.WriteLine($"  The wave crests and breaks. {partner.Name2} cries out your name as");
            terminal.WriteLine($"  pleasure overwhelms {them}. You follow moments later, the world");
            terminal.WriteLine($"  dissolving into pure sensation.");
            terminal.WriteLine("");

            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"  For a timeless moment, nothing exists but the two of you,");
            terminal.WriteLine($"  suspended in shared ecstasy.");
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
            terminal.WriteLine($"  --- {Get("intimacy.phase_afterglow")} ---");
            terminal.WriteLine("");

            string gender = partner.Sex == CharacterSex.Female ? "she" : "he";
            string genderCap = partner.Sex == CharacterSex.Female ? "She" : "He";
            string their = partner.Sex == CharacterSex.Female ? "her" : "his";
            string them = partner.Sex == CharacterSex.Female ? "her" : "him";

            terminal.SetColor("white");
            terminal.WriteLine($"  You lie tangled together, hearts gradually slowing, skin cooling.");
            terminal.WriteLine($"  {partner.Name2}'s head rests on your chest, fingers tracing lazy patterns.");
            terminal.WriteLine("");

            await Task.Delay(1000);

            float romanticism = profile?.Romanticism ?? 0.5f;
            var romanceType = RomanceTracker.Instance.GetRelationType(partner.ID);

            if (romanticism > 0.7f || romanceType == RomanceRelationType.Spouse)
            {
                terminal.WriteLine($"  \"I love you,\" {gender} murmurs, voice drowsy and content.");
                terminal.WriteLine($"  \"Every time with you feels like the first time. And the last time.");
                terminal.WriteLine($"  Like something precious I never want to lose.\"");
            }
            else if (romanceType == RomanceRelationType.Lover)
            {
                terminal.WriteLine($"  \"That was...\" {gender} starts, then laughs softly.");
                terminal.WriteLine($"  \"I don't have words. You render me speechless every time.\"");
            }
            else
            {
                terminal.WriteLine($"  {genderCap} stretches languidly, a satisfied smile on {their} face.");
                terminal.WriteLine($"  \"Not bad at all,\" {gender} teases. \"Same time next week?\"");
            }
            terminal.WriteLine("");

            await Task.Delay(1000);

            // Pillow talk options
            terminal.SetColor("cyan");
            terminal.WriteLine($"  {Get("intimacy.what_say")}");
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("1");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Get("intimacy.say_stay"));

            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("2");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Get("intimacy.say_amazing"));

            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("3");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Get("intimacy.say_hold_close"));
            terminal.WriteLine("");

            string choice = await terminal.GetInput($"  {Get("ui.your_choice")}");

            bool phase5Match = EvaluateChoice(partner, 5, choice);
            ShowChoiceReaction(partner, phase5Match);

            terminal.WriteLine("");

            switch (choice)
            {
                case "1":
                    terminal.SetColor("white");
                    terminal.WriteLine($"  \"Stay with me tonight,\" you say, pulling {them} closer.");
                    terminal.WriteLine($"  {partner.Name2} smiles, snuggling deeper into your embrace.");
                    terminal.WriteLine($"  \"Wild horses couldn't drag me away.\"");
                    terminal.WriteLine("");
                    terminal.WriteLine($"  You drift off to sleep together, warm and content.");
                    break;

                case "2":
                    terminal.SetColor("white");
                    terminal.WriteLine($"  \"That was amazing,\" you say, kissing the top of {their} head.");
                    terminal.WriteLine($"  {partner.Name2} looks up at you with a grin.");
                    terminal.WriteLine($"  \"Flatterer. But I'll allow it.\"");
                    break;

                case "3":
                default:
                    terminal.SetColor("white");
                    terminal.WriteLine($"  No words are needed. You hold {partner.Name2} close,");
                    terminal.WriteLine($"  letting your heartbeat say everything.");
                    terminal.WriteLine($"  {genderCap} sighs contentedly, and you both rest.");
                    break;
            }

            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Get("intimacy.time_passes")}");
            terminal.WriteLine("");

            await terminal.GetInput($"  {Get("ui.press_enter")}");
        }
    }
}
