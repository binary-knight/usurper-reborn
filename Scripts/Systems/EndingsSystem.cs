using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using UsurperRemake.Utils;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Endings System - Handles the three main endings plus the secret true ending
    /// Manages credits, epilogues, and transition to New Game+
    /// </summary>
    public class EndingsSystem
    {
        private static EndingsSystem? instance;
        public static EndingsSystem Instance => instance ??= new EndingsSystem();

        public event Action<EndingType>? OnEndingTriggered;
        public event Action? OnCreditsComplete;

        /// <summary>
        /// Determine which ending the player qualifies for
        /// </summary>
        public EndingType DetermineEnding(Character player)
        {
            // Null check for player
            if (player == null)
            {
                return EndingType.Defiant; // Default fallback
            }

            var story = StoryProgressionSystem.Instance;
            var ocean = OceanPhilosophySystem.Instance;
            var amnesia = AmnesiaSystem.Instance;
            var companions = CompanionSystem.Instance;
            var grief = GriefSystem.Instance;

            // Check for Secret Ending (Dissolution) first - requires Cycle 3+
            if (story?.CurrentCycle >= 3 && QualifiesForDissolutionEnding(player))
            {
                return EndingType.Secret;
            }

            // Check for Enhanced True Ending
            if (QualifiesForEnhancedTrueEnding(player))
            {
                return EndingType.TrueEnding;
            }

            // Fallback to legacy true ending check
            if (CycleSystem.Instance?.QualifiesForTrueEnding(player) == true)
            {
                return EndingType.TrueEnding;
            }

            // Calculate alignment
            long alignment = player.Chivalry - player.Darkness;

            // Count saved vs destroyed gods (all 6 Old Gods)
            int savedGods = 0;
            int destroyedGods = 0;

            // Veloura - Goddess of Illusions
            if (story.HasStoryFlag("veloura_saved")) savedGods++;
            if (story.HasStoryFlag("veloura_destroyed")) destroyedGods++;
            // Aurelion - God of Light
            if (story.HasStoryFlag("aurelion_saved")) savedGods++;
            if (story.HasStoryFlag("aurelion_destroyed")) destroyedGods++;
            // Terravok - God of Earth
            if (story.HasStoryFlag("terravok_awakened")) savedGods++;
            if (story.HasStoryFlag("terravok_destroyed")) destroyedGods++;
            // Noctura - Goddess of Night
            if (story.HasStoryFlag("noctura_ally")) savedGods++;
            if (story.HasStoryFlag("noctura_destroyed")) destroyedGods++;
            // Maelketh - God of Chaos
            if (story.HasStoryFlag("maelketh_saved")) savedGods++;
            if (story.HasStoryFlag("maelketh_destroyed")) destroyedGods++;
            // Thorgrim - God of War
            if (story.HasStoryFlag("thorgrim_saved")) savedGods++;
            if (story.HasStoryFlag("thorgrim_destroyed")) destroyedGods++;

            // Determine ending based on choices
            if (alignment < -300 || destroyedGods >= 5)
            {
                return EndingType.Usurper; // Dark path - take Manwe's place
            }
            else if (alignment > 300 || savedGods >= 3)
            {
                return EndingType.Savior; // Light path - redeem the gods
            }
            else
            {
                return EndingType.Defiant; // Independent path - reject all gods
            }
        }

        /// <summary>
        /// Check if player qualifies for the enhanced True Ending
        /// Requirements:
        /// 1. All 7 seals collected
        /// 2. Awakening Level 7 (full Ocean Philosophy understanding)
        /// 3. At least one companion died (experienced loss)
        /// 4. Spared at least 2 gods
        /// 5. Net alignment near zero (balance)
        /// 6. Completed personal quest of deceased companion (optional bonus)
        /// </summary>
        private bool QualifiesForEnhancedTrueEnding(Character player)
        {
            if (player == null)
                return false;

            // Blood Price gate — murderers cannot achieve the True Ending
            if (player.MurderWeight >= GameConfig.MurderWeightEndingBlock)
                return false;

            var story = StoryProgressionSystem.Instance;
            var ocean = OceanPhilosophySystem.Instance;
            var companions = CompanionSystem.Instance;
            var grief = GriefSystem.Instance;

            // 1. All 7 seals collected
            if (story?.CollectedSeals == null || story.CollectedSeals.Count < 7)
                return false;

            // 2. Awakening Level 7
            if (ocean?.AwakeningLevel < 7)
                return false;

            // 3. Experienced companion loss
            if (ocean?.ExperiencedMoments?.Contains(AwakeningMoment.FirstCompanionDeath) != true &&
                grief?.HasCompletedGriefCycle != true)
                return false;

            // 4. Spared at least 2 gods
            int sparedGods = 0;
            if (story.HasStoryFlag("veloura_saved")) sparedGods++;
            if (story.HasStoryFlag("aurelion_saved")) sparedGods++;
            if (story.HasStoryFlag("noctura_ally")) sparedGods++;
            if (story.HasStoryFlag("terravok_awakened")) sparedGods++;
            if (sparedGods < 2)
                return false;

            // 5. Alignment near zero (within +/- 500)
            long alignment = player.Chivalry - player.Darkness;
            if (Math.Abs(alignment) > 500)
                return false;

            return true;
        }

        /// <summary>
        /// Check if player qualifies for the secret Dissolution ending
        /// The ultimate ending - dissolving back into the Ocean
        /// </summary>
        private bool QualifiesForDissolutionEnding(Character player)
        {
            if (player == null)
                return false;

            // Blood Price gate — murderers cannot achieve the Dissolution Ending
            if (player.MurderWeight >= GameConfig.MurderWeightEndingBlock)
                return false;

            var story = StoryProgressionSystem.Instance;
            var ocean = OceanPhilosophySystem.Instance;
            var amnesia = AmnesiaSystem.Instance;

            // Must have completed at least 2 other endings
            if (story?.CompletedEndings == null || story.CompletedEndings.Count < 2)
                return false;

            // Must have max awakening
            if (ocean?.AwakeningLevel < 7)
                return false;

            // Must have full memory recovery (know you are Fragment of Manwe)
            if (amnesia?.TruthRevealed != true)
                return false;

            // Must have all wave fragments
            if (ocean?.CollectedFragments == null || ocean.CollectedFragments.Count < 7)
                return false;

            // Auto-set the ready_for_dissolution flag when all conditions are met
            // This ensures the ending is reachable once the player has completed the journey
            if (!story.HasStoryFlag("ready_for_dissolution"))
            {
                story.SetStoryFlag("ready_for_dissolution", true);
            }

            return true;
        }

        /// <summary>
        /// Trigger an ending sequence
        /// </summary>
        public async Task TriggerEnding(Character player, EndingType ending, TerminalEmulator terminal)
        {
            OnEndingTriggered?.Invoke(ending);

            switch (ending)
            {
                case EndingType.Usurper:
                    await PlayUsurperEnding(player, terminal);
                    break;
                case EndingType.Savior:
                    await PlaySaviorEnding(player, terminal);
                    break;
                case EndingType.Defiant:
                    await PlayDefiantEnding(player, terminal);
                    break;
                case EndingType.TrueEnding:
                    await PlayEnhancedTrueEnding(player, terminal);
                    break;
                case EndingType.Secret:
                    await PlayDissolutionEnding(player, terminal);
                    return; // Dissolution ending doesn't lead to NG+ - save deleted
            }

            // Record ending in story
            StoryProgressionSystem.Instance.RecordChoice("final_ending", ending.ToString(), 0);
            StoryProgressionSystem.Instance.SetStoryFlag($"ending_{ending.ToString().ToLower()}_achieved", true);

            // Play credits
            await PlayCredits(player, ending, terminal);

            // Offer Immortal Ascension (before NG+)
            bool ascended = await OfferImmortality(player, ending, terminal);
            if (ascended) return; // Player became a god — skip NG+

            // Offer New Game+
            await OfferNewGamePlus(player, ending, terminal);
        }

        #region Ending Sequences

        private async Task PlayUsurperEnding(Character player, TerminalEmulator terminal)
        {
            terminal.Clear();
            await Task.Delay(1000);

            terminal.WriteLine("");
            terminal.WriteLine("╔═══════════════════════════════════════════════════════════════════╗", "dark_red");
            terminal.WriteLine("║                     T H E   U S U R P E R                         ║", "dark_red");
            terminal.WriteLine("╚═══════════════════════════════════════════════════════════════════╝", "dark_red");
            terminal.WriteLine("");

            await Task.Delay(2000);

            var lines = new[]
            {
                ("Manwe falls. His body cracks like old stone and", "white"),
                ("something pours out of him... raw, burning power.", "white"),
                ("It hits you like a wall of fire.", "dark_red"),
                ("", "white"),
                ("\"You wanted this,\" the dying god rasps.", "yellow"),
                ("\"Take it then. Its yours now. All of it.\"", "yellow"),
                ("He almost sounds relieved.", "yellow"),
                ("", "white"),
                ("You cant even hear him anymore. The power is too loud.", "white"),
                ("It roars through you like a river breaking a dam.", "dark_red"),
                ("The remaining Old Gods kneel. They have no choice.", "dark_red"),
                ("", "white"),
                ("For a while, its everything you dreamed.", "white"),
                ("Mortals worship you. Whole kingdoms bend the knee.", "white"),
                ("Nobody dares speak against the new god.", "white"),
                ("", "white"),
                ("But centuries pass.", "gray"),
                ("", "white"),
                ("Then millenia.", "gray"),
                ("The worshippers start to look the same. The prayers", "gray"),
                ("blend together. You sit on a throne of divine power", "gray"),
                ("and feel... nothing much at all.", "gray"),
                ("", "white"),
                ("Manwe tried to warn you. You didnt listen.", "gray"),
                ("Nobody ever listens.", "gray"),
                ("", "white"),
                ("Somewhere far below, a mortal picks up a sword.", "white"),
                ("You can feel it. Another hero, climbing toward you.", "dark_red"),
                ("Part of you hopes they make it.", "dark_red")
            };

            foreach (var (line, color) in lines)
            {
                terminal.WriteLine($"  {line}", color);
                await Task.Delay(200);
            }

            terminal.WriteLine("");
            terminal.WriteLine("  THE END", "dark_red");
            terminal.WriteLine("  (The Usurper Ending)", "gray");
            terminal.WriteLine("");

            await terminal.GetInputAsync("  Press Enter to continue...");
        }

        private async Task PlaySaviorEnding(Character player, TerminalEmulator terminal)
        {
            terminal.Clear();
            await Task.Delay(1000);

            terminal.WriteLine("");
            terminal.WriteLine("╔═══════════════════════════════════════════════════════════════════╗", "bright_green");
            terminal.WriteLine("║                      T H E   S A V I O R                          ║", "bright_green");
            terminal.WriteLine("╚═══════════════════════════════════════════════════════════════════╝", "bright_green");
            terminal.WriteLine("");

            await Task.Delay(2000);

            var lines = new[]
            {
                ("You stand before Manwe. The artifacts burn in your hands.", "white"),
                ("You could end him right now. One strike.", "white"),
                ("", "white"),
                ("You lower your weapon.", "bright_green"),
                ("", "white"),
                ("\"I know what you did,\" you tell him.", "cyan"),
                ("\"You were scared and you hurt people. Alot of people.\"", "cyan"),
                ("\"But I didnt come all this way just to make another corpse.\"", "cyan"),
                ("", "white"),
                ("The Creator stares at you. Something wet runs", "bright_yellow"),
                ("down his face. You didnt know gods could cry.", "bright_yellow"),
                ("\"After everything I've done... you would--\"", "yellow"),
                ("", "white"),
                ("\"Shut up,\" you say, not unkindly. \"Hold still.\"", "cyan"),
                ("", "white"),
                ("The Soulweaver's Loom does its work.", "bright_magenta"),
                ("The corruption burns away like fog in morning sun.", "bright_magenta"),
                ("Manwe gasps. The Old Gods stir in thier prisons.", "bright_magenta"),
                ("One by one, they come back to themselves.", "bright_magenta"),
                ("", "white"),
                ("Its not a fairy tale ending. The gods are diminished,", "bright_green"),
                ("humbled. They remember what they did. Some of them", "bright_green"),
                ("cant look you in the eye.", "bright_green"),
                ("", "white"),
                ("But they're free. And so is the world.", "bright_green"),
                ("", "white"),
                ("They write songs about you. Build statues.", "white"),
                ("You never asked for any of it.", "white"),
                ("You just wanted to go home.", "white"),
                ("", "white"),
                ("When death finally comes for you, many years later,", "bright_cyan"),
                ("the gods are waiting. They dont say much.", "bright_cyan"),
                ("They dont need to.", "bright_cyan")
            };

            foreach (var (line, color) in lines)
            {
                terminal.WriteLine($"  {line}", color);
                await Task.Delay(200);
            }

            terminal.WriteLine("");
            terminal.WriteLine("  THE END", "bright_green");
            terminal.WriteLine("  (The Savior Ending)", "gray");
            terminal.WriteLine("");

            await terminal.GetInputAsync("  Press Enter to continue...");
        }

        private async Task PlayDefiantEnding(Character player, TerminalEmulator terminal)
        {
            terminal.Clear();
            await Task.Delay(1000);

            terminal.WriteLine("");
            terminal.WriteLine("╔═══════════════════════════════════════════════════════════════════╗", "bright_yellow");
            terminal.WriteLine("║                      T H E   D E F I A N T                        ║", "bright_yellow");
            terminal.WriteLine("╚═══════════════════════════════════════════════════════════════════╝", "bright_yellow");
            terminal.WriteLine("");

            await Task.Delay(2000);

            var lines = new[]
            {
                ("\"No,\" you say.", "cyan"),
                ("", "white"),
                ("Manwe blinks. \"...No?\"", "yellow"),
                ("", "white"),
                ("\"I dont want your power. I dont want ANY of this.\"", "cyan"),
                ("You hold up the artifacts. \"These things? They're chains.\"", "cyan"),
                ("\"Fancy chains, but chains all the same.\"", "cyan"),
                ("", "white"),
                ("\"You could rule forever,\" Manwe says, genuinly confused.", "yellow"),
                ("\"Why would you refuse?\"", "yellow"),
                ("", "white"),
                ("\"Because thats exactly how this mess started, isn't it.\"", "cyan"),
                ("\"Gods deciding what mortals need. I'm done with that.\"", "cyan"),
                ("", "white"),
                ("You throw the artifacts on the ground.", "bright_red"),
                ("And you smash them. Every last one.", "bright_red"),
                ("Divine power sprays out like sparks from a forge.", "bright_yellow"),
                ("", "white"),
                ("The Old Gods stumble free from thier prisons.", "white"),
                ("They look... small. Confused. Almost human.", "white"),
                ("For the first time in millenia, they are mortal.", "white"),
                ("They'll have to live among the people they once ruled.", "white"),
                ("", "white"),
                ("Manwe is fading. He doesnt seem to mind.", "gray"),
                ("\"Maybe you're right,\" he manages.", "gray"),
                ("\"Maybe this is... better.\"", "gray"),
                ("He's gone before he finishes the thought.", "gray"),
                ("", "white"),
                ("No more gods. No more divine plans.", "bright_yellow"),
                ("Just people, making thier own mistakes.", "white"),
                ("Seems about right.", "white"),
                ("", "white"),
                ("You walk out into the morning. Just another person", "bright_yellow"),
                ("on an ordinary road. Somehow thats enough.", "bright_yellow")
            };

            foreach (var (line, color) in lines)
            {
                terminal.WriteLine($"  {line}", color);
                await Task.Delay(200);
            }

            terminal.WriteLine("");
            terminal.WriteLine("  THE END", "bright_yellow");
            terminal.WriteLine("  (The Defiant Ending)", "gray");
            terminal.WriteLine("");

            await terminal.GetInputAsync("  Press Enter to continue...");
        }

        private async Task PlayTrueEnding(Character player, TerminalEmulator terminal)
        {
            terminal.Clear();
            await Task.Delay(1000);

            terminal.WriteLine("");
            terminal.WriteLine("╔═══════════════════════════════════════════════════════════════════╗", "bright_magenta");
            terminal.WriteLine("║                   T H E   T R U E   E N D I N G                   ║", "bright_magenta");
            terminal.WriteLine("║                      Seeker of Balance                            ║", "bright_magenta");
            terminal.WriteLine("╚═══════════════════════════════════════════════════════════════════╝", "bright_magenta");
            terminal.WriteLine("");

            await Task.Delay(2000);

            var lines = new[]
            {
                ("You've been here before.", "bright_cyan"),
                ("Different lives, different choices. You remember them all now.", "bright_cyan"),
                ("The killing. The mercy. The defiance.", "bright_cyan"),
                ("None of it was wrong, exactly. Just... incomplete.", "bright_cyan"),
                ("", "white"),
                ("Manwe looks at you and you can tell he knows.", "bright_yellow"),
                ("\"You're different this time,\" he says quietly.", "yellow"),
                ("\"You actually figured it out, didn't you.\"", "yellow"),
                ("", "white"),
                ("\"I think so,\" you say. \"Took me long enough.\"", "cyan"),
                ("", "white"),
                ("He laughs. Its a tired sound.", "yellow"),
                ("\"I've been doing this alone for so long. Building,", "yellow"),
                ("fixing, watching it all break again. I could use the help.\"", "yellow"),
                ("", "white"),
                ("It's not a trick. You can see that now.", "white"),
                ("The universe is too big for one god to manage.", "white"),
                ("It always was. That was the whole problem.", "white"),
                ("", "white"),
                ("\"Not as a servant,\" you clarify.", "cyan"),
                ("\"As a partner. Equal say.\"", "cyan"),
                ("", "white"),
                ("\"Wouldn't have it any other way.\"", "yellow"),
                ("", "white"),
                ("You clasp his hand.", "bright_magenta"),
                ("", "white"),
                ("The Old Gods come back to themselves, slowly.", "bright_magenta"),
                ("They remember everything -- the good years and the bad.", "bright_magenta"),
                ("None of them are the same as before. That's the point.", "bright_magenta"),
                ("", "white"),
                ("The cycle keeps turning, but it goes somewhere now.", "bright_cyan"),
                ("Not just around and around. Upward.", "bright_cyan"),
                ("", "white"),
                ("Its going to be alot of work.", "bright_magenta"),
                ("That's fine. You've got help this time.", "bright_magenta")
            };

            foreach (var (line, color) in lines)
            {
                terminal.WriteLine($"  {line}", color);
                await Task.Delay(200);
            }

            terminal.WriteLine("");
            terminal.WriteLine("  THE TRUE END", "bright_magenta");
            terminal.WriteLine("  (Balance Achieved)", "gray");
            terminal.WriteLine("");

            await terminal.GetInputAsync("  Press Enter to continue...");
        }

        /// <summary>
        /// Enhanced True Ending with Ocean Philosophy integration
        /// Includes the revelation that player is a fragment of Manwe
        /// </summary>
        private async Task PlayEnhancedTrueEnding(Character player, TerminalEmulator terminal)
        {
            terminal.Clear();
            await Task.Delay(1000);

            terminal.WriteLine("");
            terminal.WriteLine("╔═══════════════════════════════════════════════════════════════════╗", "bright_cyan");
            terminal.WriteLine("║            T H E   T R U E   A W A K E N I N G                    ║", "bright_cyan");
            terminal.WriteLine("║           \"You are the Ocean, dreaming of being a wave\"           ║", "bright_cyan");
            terminal.WriteLine("╚═══════════════════════════════════════════════════════════════════╝", "bright_cyan");
            terminal.WriteLine("");

            await Task.Delay(2000);

            var lines = new[]
            {
                ("You stand before Manwe. But this time is different.", "white"),
                ("He's looking at you like he recognizes you.", "white"),
                ("Like he's been waiting a very long time.", "bright_yellow"),
                ("", "white"),
                ("\"You remember,\" he says. His voice cracks.", "yellow"),
                ("\"Gods... you actually remember.\"", "yellow"),
                ("", "white"),
                ("And you do. It hits you all at once.", "bright_cyan"),
                ("", "white"),
                ("You're not some random adventurer who got lucky.", "bright_cyan"),
                ("You're a piece of HIM. A fragment of the Creator,", "bright_cyan"),
                ("shoved into a mortal body and sent down to live and", "bright_cyan"),
                ("die and suffer like everyone else.", "bright_cyan"),
                ("So he could finally understand what it felt like.", "bright_cyan"),
                ("", "white"),
                ("\"I was lonely,\" Manwe says. He looks old suddenly.", "yellow"),
                ("\"I made the Old Gods because I wanted someone to talk to.", "yellow"),
                ("But I never understood them. How could I? I'd never been", "yellow"),
                ("mortal. Never lost anything. Never been afraid.\"", "yellow"),
                ("", "white"),
                ("\"So you sent yourself down here,\" you say.", "cyan"),
                ("\"Again and again.\"", "cyan"),
                ("", "white"),
                ("\"Again and again,\" he confirms.", "yellow"),
                ("", "white"),
                ("All that grief you carried. The friends who died.", "bright_magenta"),
                ("The impossible choices. That was his grief too.", "bright_magenta"),
                ("Yours and his. Same person, all along.", "bright_magenta"),
                ("", "white"),
                ("It's like being a wave that suddenly realizes", "bright_cyan"),
                ("it's been the whole ocean this entire time.", "bright_cyan"),
                ("", "white"),
                ("\"I dont want to be alone anymore,\" Manwe admits.", "yellow"),
                ("\"And the Old Gods -- they're fragments too, arent they.", "yellow"),
                ("We've been fighting ourselves this whole time.\"", "yellow"),
                ("", "white"),
                ("You take his hand. Or your hand. Same thing really.", "bright_magenta"),
                ("", "white"),
                ("The walls come down.", "bright_white"),
                ("All the seperation. God and mortal, creator and created.", "bright_white"),
                ("It was never real. Just a story you told yourself", "bright_white"),
                ("so you could learn what it ment to be small.", "bright_white"),
                ("", "white"),
                ("The Old Gods stir. Maelketh, Veloura, all of them.", "bright_cyan"),
                ("They remember too now. They were never really enemies.", "bright_cyan"),
                ("Just pieces of one being, playing out a drama", "bright_cyan"),
                ("that went on way too long.", "bright_cyan"),
                ("", "white"),
                ("There never was a cycle. Not really.", "bright_magenta"),
                ("Just the ocean, dreaming it was waves.", "bright_magenta"),
                ("The dream goes on. But now its a lucid dream.", "bright_magenta"),
                ("", "white"),
                ("You are home.", "bright_yellow"),
                ("You were always home.", "bright_yellow")
            };

            foreach (var (line, color) in lines)
            {
                terminal.WriteLine($"  {line}", color);
                await Task.Delay(150);
            }

            terminal.WriteLine("");
            terminal.WriteLine("  THE TRUE AWAKENING", "bright_cyan");
            terminal.WriteLine("  (The Wave Remembers the Ocean)", "gray");
            terminal.WriteLine("");

            // Mark Ocean Philosophy complete
            OceanPhilosophySystem.Instance.ExperienceMoment(AwakeningMoment.TrueIdentityRevealed);

            await terminal.GetInputAsync("  Press Enter to continue...");
        }

        /// <summary>
        /// Secret Dissolution Ending - available only after Cycle 3+
        /// The ultimate ending: true enlightenment, save deleted
        /// </summary>
        private async Task PlayDissolutionEnding(Character player, TerminalEmulator terminal)
        {
            terminal.Clear();
            await Task.Delay(2000);

            terminal.WriteLine("");
            terminal.WriteLine("╔═══════════════════════════════════════════════════════════════════╗", "white");
            terminal.WriteLine("║                     D I S S O L U T I O N                         ║", "white");
            terminal.WriteLine("║              \"No more cycles. No more grasping.\"                  ║", "white");
            terminal.WriteLine("╚═══════════════════════════════════════════════════════════════════╝", "white");
            terminal.WriteLine("");

            await Task.Delay(2000);

            var lines = new[]
            {
                ("You've done this three times now. Maybe more.", "gray"),
                ("Taken the throne. Shown mercy. Walked away.", "gray"),
                ("Woken up and realized you were the ocean all along.", "gray"),
                ("", "white"),
                ("And now you're tired.", "white"),
                ("", "white"),
                ("Not the bad kind of tired. The kind where you've", "bright_cyan"),
                ("done everything you came to do and theres nothing", "bright_cyan"),
                ("left that needs doing.", "bright_cyan"),
                ("", "white"),
                ("Manwe sees it in your eyes before you say anything.", "yellow"),
                ("\"You're leaving,\" he says. \"For real this time.\"", "yellow"),
                ("\"No more cycles. No more coming back.\"", "yellow"),
                ("", "white"),
                ("\"Yeah.\"", "bright_white"),
                ("", "white"),
                ("\"But the stories -- the whole thing keeps going--\"", "yellow"),
                ("", "white"),
                ("\"It'll keep going fine without me. Thats kind of", "bright_white"),
                ("the whole point. You dont need every wave to", "bright_white"),
                ("keep the ocean running.\"", "bright_white"),
                ("", "white"),
                ("He's quiet for a long time.", "yellow"),
                ("\"I never could do this,\" he says finally.", "yellow"),
                ("\"Just... stop. Let go of it all. I always had to", "yellow"),
                ("keep building, keep fixing, keep HOLDING ON.\"", "yellow"),
                ("He looks at you almost enviously.", "yellow"),
                ("\"Maybe thats what I should have learned from you.\"", "yellow"),
                ("", "white"),
                ("You smile at him. Feels like the last one.", "white"),
                ("", "white"),
                ("Everything gets quiet.", "bright_white"),
                ("Not dark. Not empty. Just... still.", "bright_white"),
                ("Like the space between breaths.", "bright_white"),
                ("", "white"),
                ("The world keeps on spinning somewhere out there.", "white"),
                ("The gods figure thier stuff out.", "white"),
                ("New heroes pick up swords and go looking for trouble.", "white"),
                ("", "white"),
                ("But you're done. And it feels right.", "bright_cyan"),
                ("Not like giving up. Like finishing.", "bright_cyan"),
                ("", "white"),
                ("Peace.", "gray")
            };

            foreach (var (line, color) in lines)
            {
                terminal.WriteLine($"  {line}", color);
                await Task.Delay(200);
            }

            terminal.WriteLine("");
            terminal.WriteLine("  . . . . . . . . . .", "gray");
            terminal.WriteLine("");

            await Task.Delay(3000);

            terminal.Clear();
            terminal.WriteLine("");
            terminal.WriteLine("");
            terminal.WriteLine("", "white");
            terminal.WriteLine("", "white");
            terminal.WriteLine("  Your save file will now be permanently deleted.", "dark_red");
            terminal.WriteLine("  This cannot be undone.", "dark_red");
            terminal.WriteLine("");
            terminal.WriteLine("  You have achieved true enlightenment:", "bright_yellow");
            terminal.WriteLine("  The final letting go.", "bright_yellow");
            terminal.WriteLine("");

            var confirm = await terminal.GetInputAsync("  Type 'DISSOLVE' to confirm, or anything else to cancel: ");

            if (confirm.ToUpper() == "DISSOLVE")
            {
                terminal.WriteLine("");
                terminal.WriteLine("  So long, adventurer.", "bright_cyan");
                terminal.WriteLine("  It was a good run.", "bright_cyan");
                terminal.WriteLine("");

                // Delete the player's save file - this character's journey is complete
                string playerName = !string.IsNullOrEmpty(player.Name1) ? player.Name1 : player.Name2;
                SaveSystem.Instance.DeleteSave(playerName);

                await Task.Delay(3000);

                terminal.Clear();
                terminal.WriteLine("");
                terminal.WriteLine("  THE END", "white");
                terminal.WriteLine("");
                terminal.WriteLine("  (This character's story is finished.)", "gray");
                terminal.WriteLine("  (Save file deleted.)", "gray");
                terminal.WriteLine("");
            }
            else
            {
                terminal.WriteLine("");
                terminal.WriteLine("  Not ready yet, huh? Thats fine.", "yellow");
                terminal.WriteLine("  Maybe next time around.", "yellow");
                terminal.WriteLine("");

                // Revert to standard True Ending
                await PlayEnhancedTrueEnding(player, terminal);
            }

            await terminal.GetInputAsync("  Press Enter...");
        }

        #endregion

        #region Credits

        private async Task PlayCredits(Character player, EndingType ending, TerminalEmulator terminal)
        {
            terminal.Clear();
            await Task.Delay(2000);

            terminal.WriteLine("");
            terminal.WriteLine("═══════════════════════════════════════════════════════════════════", "bright_cyan");
            terminal.WriteLine("");
            terminal.WriteLine("                        U S U R P E R", "bright_yellow");
            terminal.WriteLine("                          REBORN", "yellow");
            terminal.WriteLine("");
            terminal.WriteLine("═══════════════════════════════════════════════════════════════════", "bright_cyan");
            terminal.WriteLine("");

            await Task.Delay(3000);

            var credits = new[]
            {
                ("ORIGINAL CONCEPT", "bright_yellow"),
                ("Jakob Dangarden", "white"),
                ("Usurper: Halls of Avarice (1993)", "gray"),
                ("", "white"),
                ("REBORN BY", "bright_yellow"),
                ("Jason Knight", "white"),
                ("", "white"),
                ("STORY & NARRATIVE", "bright_yellow"),
                ("Jason Knight", "white"),
                ("Inspired by Buddhist philosophy:", "gray"),
                ("Samsara, the Wheel of Becoming,", "gray"),
                ("and the Ocean of consciousness", "gray"),
                ("", "white"),
                ("SYSTEMS DESIGN", "bright_yellow"),
                ("Jason Knight", "white"),
                ("", "white"),
                ("ARTWORK", "bright_yellow"),
                ("xbit (x-bit.org)", "white"),
                ("Race & Class ANSI Portraits", "gray"),
                ("", "white"),
                ("CONTRIBUTORS", "bright_yellow"),
                ("fastfinge - Code Contributions", "white"),
                ("xbit - ANSI Art", "white"),
                ("", "white"),
                ("ALPHA TESTERS & DISCORD COMMUNITY", "bright_yellow"),
                ("fastfinge, Inkblot, Byte Knight,", "white"),
                ("Druidah, Quent, xbit, Stettin", "white"),
                ("...and many more", "gray"),
                ("", "white"),
                ("SPECIAL THANKS", "bright_yellow"),
                ("To all BBS door game enthusiasts", "white"),
                ("who keep the spirit alive", "white"),
                ("", "white"),
                ("AND TO YOU", "bright_yellow"),
                ($"Player: {player.Name2}", "bright_cyan"),
                ($"Final Level: {player.Level}", "cyan"),
                ($"Ending: {GetEndingName(ending)}", "cyan"),
                ($"Cycle: {StoryProgressionSystem.Instance.CurrentCycle}", "cyan"),
                ("", "white"),
                ("Thank you for playing.", "bright_green")
            };

            foreach (var (line, color) in credits)
            {
                if (string.IsNullOrEmpty(line))
                {
                    terminal.WriteLine("");
                    await Task.Delay(500);
                }
                else
                {
                    terminal.WriteLine($"  {line}", color);
                    await Task.Delay(800);
                }
            }

            terminal.WriteLine("");
            terminal.WriteLine("═══════════════════════════════════════════════════════════════════", "bright_cyan");
            terminal.WriteLine("");

            await Task.Delay(2000);

            // Show stats
            await ShowFinalStats(player, ending, terminal);

            OnCreditsComplete?.Invoke();
        }

        private async Task ShowFinalStats(Character player, EndingType ending, TerminalEmulator terminal)
        {
            var story = StoryProgressionSystem.Instance;

            terminal.WriteLine("");
            terminal.WriteLine("                    F I N A L   S T A T S", "bright_yellow");
            terminal.WriteLine("═══════════════════════════════════════════════════════════════════", "gray");
            terminal.WriteLine("");

            terminal.WriteLine($"  Character: {player.Name2} the {player.Class}", "white");
            terminal.WriteLine($"  Race: {player.Race}", "white");
            terminal.WriteLine($"  Final Level: {player.Level}", "cyan");
            terminal.WriteLine("");

            terminal.WriteLine($"  Monsters Slain: {player.MKills}", "red");
            terminal.WriteLine($"  Players Defeated: {player.PKills}", "dark_red");
            terminal.WriteLine($"  Gold Accumulated: {player.Gold + player.BankGold}", "yellow");
            terminal.WriteLine("");

            terminal.WriteLine($"  Chivalry: {player.Chivalry}", "bright_green");
            terminal.WriteLine($"  Darkness: {player.Darkness}", "dark_red");
            terminal.WriteLine("");

            terminal.WriteLine($"  Artifacts Collected: {story.CollectedArtifacts.Count}/7", "bright_magenta");
            terminal.WriteLine($"  Seals Discovered: {story.CollectedSeals.Count}/7", "bright_cyan");
            terminal.WriteLine($"  Major Choices Made: {story.MajorChoices.Count}", "white");
            terminal.WriteLine("");

            terminal.WriteLine($"  Ending Achieved: {GetEndingName(ending)}", "bright_yellow");
            terminal.WriteLine($"  Eternal Cycle: {story.CurrentCycle}", "bright_magenta");
            terminal.WriteLine("");

            terminal.WriteLine("═══════════════════════════════════════════════════════════════════", "gray");
            terminal.WriteLine("");

            await terminal.GetInputAsync("  Press Enter to continue...");

            // Show personalized epilogue
            await ShowEpilogue(player, ending, terminal);

            // Show unlocks earned this run
            await ShowUnlocksEarned(player, ending, terminal);
        }

        /// <summary>
        /// Show a personalized epilogue based on player's journey
        /// </summary>
        private async Task ShowEpilogue(Character player, EndingType ending, TerminalEmulator terminal)
        {
            terminal.Clear();
            await Task.Delay(1000);

            terminal.WriteLine("");
            terminal.WriteLine("╔═══════════════════════════════════════════════════════════════════╗", "bright_cyan");
            terminal.WriteLine("║                 Y O U R   L E G A C Y                             ║", "bright_cyan");
            terminal.WriteLine("╚═══════════════════════════════════════════════════════════════════╝", "bright_cyan");
            terminal.WriteLine("");

            await Task.Delay(500);

            var story = StoryProgressionSystem.Instance;
            var companions = CompanionSystem.Instance;
            var romance = RomanceTracker.Instance;

            // Character summary
            terminal.WriteLine("  === THE HERO ===", "bright_yellow");
            terminal.WriteLine($"  {player.Name2} the {player.Race} {player.Class}", "white");
            terminal.WriteLine($"  Reached level {player.Level} after slaying {player.MKills} monsters", "gray");
            terminal.WriteLine("");

            await Task.Delay(300);

            // Alignment-based description
            long alignment = player.Chivalry - player.Darkness;
            string alignDesc;
            if (alignment > 500) alignDesc = "a genuine hero, or close enough to one";
            else if (alignment > 200) alignDesc = "mostly decent, as adventurers go";
            else if (alignment > -200) alignDesc = "hard to pin down -- not quite good, not quite bad";
            else if (alignment > -500) alignDesc = "the kind of person mothers warn thier children about";
            else alignDesc = "a right bastard, frankly";
            terminal.WriteLine($"  Known as {alignDesc}.", "white");
            terminal.WriteLine("");

            await Task.Delay(300);

            // Companions
            terminal.WriteLine("  === COMPANIONS ===", "bright_yellow");
            var activeCompanions = companions.GetActiveCompanions();
            var fallenCompanions = companions.GetFallenCompanions().ToList();

            if (activeCompanions.Any())
            {
                terminal.WriteLine("  Those who stood with you at the end:", "green");
                foreach (var c in activeCompanions)
                {
                    terminal.WriteLine($"    - {c.Name} (Level {c.Level})", "white");
                }
            }
            else
            {
                terminal.WriteLine("  You faced the final battle alone.", "gray");
            }

            if (fallenCompanions.Count > 0)
            {
                terminal.WriteLine("  Those who fell along the way:", "dark_red");
                foreach (var (companion, death) in fallenCompanions)
                {
                    terminal.WriteLine($"    - {companion.Name}, lost to {death.Type}", "gray");
                }
            }
            terminal.WriteLine("");

            await Task.Delay(300);

            // Romance
            terminal.WriteLine("  === LOVE & FAMILY ===", "bright_yellow");
            if (romance.Spouses.Count > 0)
            {
                var spouse = romance.Spouses[0];
                var spouseName = !string.IsNullOrEmpty(spouse.NPCName) ? spouse.NPCName : spouse.NPCId;
                terminal.WriteLine($"  Married to {spouseName}", "bright_magenta");
                if (spouse.Children > 0)
                {
                    terminal.WriteLine($"  Together you raised {spouse.Children} child{(spouse.Children > 1 ? "ren" : "")}.", "magenta");
                }
            }
            else if (romance.CurrentLovers.Count > 0)
            {
                terminal.WriteLine($"  Never married, but had {romance.CurrentLovers.Count} romantic partner(s).", "magenta");
            }
            else
            {
                terminal.WriteLine("  The hero's heart remained focused on the quest.", "gray");
            }

            if (romance.ExSpouses.Count > 0)
            {
                terminal.WriteLine($"  {romance.ExSpouses.Count} marriage(s) ended in divorce.", "gray");
            }
            terminal.WriteLine("");

            await Task.Delay(300);

            // World impact
            terminal.WriteLine("  === IMPACT ON THE WORLD ===", "bright_yellow");
            await ShowWorldImpact(player, ending, story, terminal);
            terminal.WriteLine("");

            await Task.Delay(300);

            // Achievements unlocked
            terminal.WriteLine("  === NOTABLE ACHIEVEMENTS ===", "bright_yellow");
            await ShowNotableAchievements(player, terminal);
            terminal.WriteLine("");

            await Task.Delay(300);

            // Jungian Archetype reveal
            terminal.WriteLine("  === YOUR TRUE NATURE ===", "bright_yellow");
            await ShowArchetypeReveal(player, terminal);
            terminal.WriteLine("");

            // Final quote based on ending
            terminal.WriteLine("═══════════════════════════════════════════════════════════════════", "gray");
            string quote = ending switch
            {
                EndingType.Usurper => "\"Turns out the throne is just a fancy prison.\"",
                EndingType.Savior => "\"Could have killed him. Didn't. Dont regret it.\"",
                EndingType.Defiant => "\"Nobody tells me what to do. Not even gods.\"",
                EndingType.TrueEnding => "\"Funny how you can search the whole world and end up right where you started.\"",
                EndingType.Secret => "\"Im done. And thats ok.\"",
                _ => "\"Hell of an adventure, anyway.\""
            };
            terminal.WriteLine("");
            terminal.WriteLine($"  {quote}", "bright_cyan");
            terminal.WriteLine($"  - {player.Name2}", "gray");
            terminal.WriteLine("");

            await terminal.GetInputAsync("  Press Enter to continue...");
        }

        /// <summary>
        /// Show the impact of the player's choices on the world
        /// </summary>
        private async Task ShowWorldImpact(Character player, EndingType ending, StoryProgressionSystem story, TerminalEmulator terminal)
        {
            await Task.Delay(100);

            // Count gods saved vs destroyed
            int savedGods = 0;
            int destroyedGods = 0;
            foreach (var godState in story.OldGodStates.Values)
            {
                if (godState.Status == GodStatus.Saved || godState.Status == GodStatus.Awakened)
                    savedGods++;
                else if (godState.Status == GodStatus.Defeated)
                    destroyedGods++;
            }

            if (savedGods > destroyedGods)
            {
                terminal.WriteLine($"  The Old Gods were mostly redeemed ({savedGods} saved, {destroyedGods} destroyed)", "green");
                terminal.WriteLine("  Things are looking up for the realm.", "white");
            }
            else if (destroyedGods > savedGods)
            {
                terminal.WriteLine($"  The Old Gods were mostly destroyed ({destroyedGods} slain, {savedGods} saved)", "dark_red");
                terminal.WriteLine("  Thier power scattered to the winds.", "white");
            }
            else
            {
                terminal.WriteLine("  The fate of the Old Gods remains uncertain.", "yellow");
            }

            // Economy impact
            long totalWealth = player.Gold + player.BankGold;
            if (totalWealth > 1000000)
            {
                terminal.WriteLine("  You amassed a ridiculous fortune. Good for you.", "yellow");
            }
            else if (totalWealth > 100000)
            {
                terminal.WriteLine("  You made a decent pile of gold along the way.", "yellow");
            }

            // Combat impact
            if (player.MKills > 10000)
            {
                terminal.WriteLine("  You killed so many monsters they probably have legends about YOU down there.", "red");
            }
            else if (player.MKills > 1000)
            {
                terminal.WriteLine("  You carved a bloody path through the dungeon's depths.", "red");
            }

            // Story choices
            if (story.MajorChoices.Count > 10)
            {
                terminal.WriteLine($"  {story.MajorChoices.Count} crucial decisions shaped the fate of the realm.", "bright_magenta");
            }

            // Ending-specific impact
            switch (ending)
            {
                case EndingType.Usurper:
                    terminal.WriteLine("  You took the throne of the gods. The realm hasnt stopped shaking.", "dark_red");
                    break;
                case EndingType.Savior:
                    terminal.WriteLine("  The realm is at peace. They still sing songs about what you did.", "bright_green");
                    break;
                case EndingType.Defiant:
                    terminal.WriteLine("  No more gods telling people what to do. About damn time.", "bright_yellow");
                    break;
                case EndingType.TrueEnding:
                    terminal.WriteLine("  The cycle is broken. Whatever comes next, its something new.", "bright_cyan");
                    break;
            }
        }

        /// <summary>
        /// Show notable achievements from this run
        /// </summary>
        private async Task ShowNotableAchievements(Character player, TerminalEmulator terminal)
        {
            await Task.Delay(100);

            var achievementCount = player.Achievements?.UnlockedCount ?? 0;
            var notableAchievements = new List<string>();

            // Pick up to 5 notable achievements
            if (player.Level >= 100) notableAchievements.Add("Reached the maximum level of 100");
            if (player.MKills >= 10000) notableAchievements.Add($"Slayed over 10,000 monsters");
            if (StoryProgressionSystem.Instance.CollectedSeals.Count >= 7) notableAchievements.Add("Collected all 7 Seals of Power");
            if (StoryProgressionSystem.Instance.CollectedArtifacts.Count >= 7) notableAchievements.Add("Found all 7 Divine Artifacts");

            var companions = CompanionSystem.Instance;
            if (companions.GetActiveCompanions().Count() >= 3) notableAchievements.Add("Led a full party of companions");
            if (RomanceTracker.Instance.Spouses.Count > 0 && RomanceTracker.Instance.Spouses[0].Children > 0)
                notableAchievements.Add("Started a family in the realm");

            if (achievementCount >= 25) notableAchievements.Add($"Unlocked {achievementCount} achievements");

            if (notableAchievements.Count == 0)
            {
                terminal.WriteLine("  Your journey was just beginning...", "gray");
            }
            else
            {
                foreach (var achievement in notableAchievements.Take(5))
                {
                    terminal.WriteLine($"  * {achievement}", "bright_cyan");
                }
            }
        }

        /// <summary>
        /// Show the player's Jungian Archetype based on their playstyle
        /// </summary>
        private async Task ShowArchetypeReveal(Character player, TerminalEmulator terminal)
        {
            await Task.Delay(500);

            var tracker = ArchetypeTracker.Instance;
            var dominant = tracker.GetDominantArchetype();
            var secondary = tracker.GetSecondaryArchetype();

            var (name, title, description, color) = ArchetypeTracker.GetArchetypeInfo(dominant);
            var quote = ArchetypeTracker.GetArchetypeQuote(dominant);

            terminal.WriteLine($"  Throughout your journey, your true nature emerged:", "white");
            terminal.WriteLine("");

            await Task.Delay(500);

            terminal.WriteLine($"  *** {name.ToUpper()} ***", color);
            terminal.WriteLine($"  \"{title}\"", color);
            terminal.WriteLine("");

            await Task.Delay(500);

            // Word wrap the description
            var words = description.Split(' ');
            var currentLine = "  ";
            foreach (var word in words)
            {
                if (currentLine.Length + word.Length + 1 > 68)
                {
                    terminal.WriteLine(currentLine, "white");
                    currentLine = "  " + word;
                }
                else
                {
                    currentLine += (currentLine.Length > 2 ? " " : "") + word;
                }
            }
            if (currentLine.Length > 2)
            {
                terminal.WriteLine(currentLine, "white");
            }
            terminal.WriteLine("");

            await Task.Delay(300);

            // Show secondary archetype
            var (secName, secTitle, _, secColor) = ArchetypeTracker.GetArchetypeInfo(secondary);
            terminal.WriteLine($"  With shades of: {secName} ({secTitle})", "gray");
            terminal.WriteLine("");

            await Task.Delay(300);

            // Show the archetype quote
            terminal.WriteLine($"  {quote}", "bright_cyan");
            terminal.WriteLine("");

            // Show some stats that contributed to this determination
            terminal.SetColor("darkgray");
            terminal.WriteLine("  Journey Statistics:");
            if (tracker.MonstersKilled > 0)
                terminal.WriteLine($"    Combat: {tracker.MonstersKilled} monsters, {tracker.BossesDefeated} bosses");
            if (tracker.DungeonFloorsExplored > 0)
                terminal.WriteLine($"    Exploration: {tracker.DungeonFloorsExplored} floors explored");
            if (tracker.SpellsCast > 0)
                terminal.WriteLine($"    Magic: {tracker.SpellsCast} spells cast");
            if (tracker.RomanceEncounters > 0)
                terminal.WriteLine($"    Romance: {tracker.RomanceEncounters} encounters, {tracker.MarriagesFormed} marriages");
            if (tracker.SealsCollected > 0 || tracker.ArtifactsCollected > 0)
                terminal.WriteLine($"    Wisdom: {tracker.SealsCollected} seals, {tracker.ArtifactsCollected} artifacts");
        }

        /// <summary>
        /// Show unlocks earned from completing this run
        /// </summary>
        private async Task ShowUnlocksEarned(Character player, EndingType ending, TerminalEmulator terminal)
        {
            terminal.Clear();
            await Task.Delay(500);

            terminal.WriteLine("");
            terminal.WriteLine("╔═══════════════════════════════════════════════════════════════════╗", "bright_green");
            terminal.WriteLine("║                 U N L O C K S   E A R N E D                       ║", "bright_green");
            terminal.WriteLine("╚═══════════════════════════════════════════════════════════════════╝", "bright_green");
            terminal.WriteLine("");

            var unlocks = new List<(string name, string description, string color)>();

            // Ending-based unlocks
            switch (ending)
            {
                case EndingType.Usurper:
                    unlocks.Add(("DARK LORD TITLE", "Start NG+ with 'Dark Lord' title prefix", "dark_red"));
                    unlocks.Add(("TYRANT'S AURA", "+15% damage in NG+", "red"));
                    unlocks.Add(("FEAR THE THRONE", "Enemies have -10% chance to flee", "dark_red"));
                    break;
                case EndingType.Savior:
                    unlocks.Add(("SAVIOR TITLE", "Start NG+ with 'Savior' title prefix", "bright_green"));
                    unlocks.Add(("HEALING LIGHT", "+25% healing effectiveness in NG+", "green"));
                    unlocks.Add(("BLESSED COMMERCE", "10% discount at all shops", "yellow"));
                    break;
                case EndingType.Defiant:
                    unlocks.Add(("DEFIANT TITLE", "Start NG+ with 'Defiant' title prefix", "bright_yellow"));
                    unlocks.Add(("MORTAL PRIDE", "+20% XP gain in NG+", "cyan"));
                    unlocks.Add(("ANCIENT KEY", "Start with dungeon shortcut key", "bright_magenta"));
                    break;
                case EndingType.TrueEnding:
                    unlocks.Add(("AWAKENED TITLE", "Start NG+ with 'Awakened' title prefix", "bright_cyan"));
                    unlocks.Add(("OCEAN'S BLESSING", "+15% to all stats in NG+", "bright_cyan"));
                    unlocks.Add(("ARTIFACT MEMORY", "All artifact locations revealed", "bright_magenta"));
                    unlocks.Add(("SEAL RESONANCE", "Seals give double bonuses", "bright_magenta"));
                    break;
                case EndingType.Secret:
                    unlocks.Add(("DISSOLVED", "Your journey is complete. No unlocks needed.", "white"));
                    break;
            }

            // Level-based unlocks
            if (player.Level >= 50)
                unlocks.Add(("VETERAN", "Start NG+ at level 5 instead of 1", "white"));
            if (player.Level >= 100)
                unlocks.Add(("MASTER", "Start NG+ at level 10 with bonus stats", "bright_yellow"));

            // Kill-based unlocks
            if (player.MKills >= 5000)
                unlocks.Add(("SLAYER", "Rare monsters appear 25% more often", "red"));

            // Collection unlocks
            if (StoryProgressionSystem.Instance.CollectedSeals.Count >= 7)
                unlocks.Add(("SEAL MASTER", "Seals are visible on minimap in NG+", "bright_cyan"));
            if (StoryProgressionSystem.Instance.CollectedArtifacts.Count >= 7)
                unlocks.Add(("ARTIFACT HUNTER", "Artifacts give +50% bonus effects", "bright_magenta"));

            // Companion unlocks
            var companions = CompanionSystem.Instance;
            if (companions.GetFallenCompanions().Any())
                unlocks.Add(("SURVIVOR'S GUILT", "Fallen companions may return as ghosts with advice", "gray"));

            terminal.WriteLine("  Completing this ending has unlocked:", "white");
            terminal.WriteLine("");

            foreach (var (name, description, color) in unlocks)
            {
                terminal.WriteLine($"  [{name}]", color);
                terminal.WriteLine($"    {description}", "gray");
                terminal.WriteLine("");
                await Task.Delay(300);
            }

            // Track unlocks
            MetaProgressionSystem.Instance.RecordEndingUnlock(ending, player);

            terminal.WriteLine("═══════════════════════════════════════════════════════════════════", "gray");
            terminal.WriteLine("");
            terminal.WriteLine("  These bonuses will apply in New Game+!", "bright_green");
            terminal.WriteLine("");

            await terminal.GetInputAsync("  Press Enter to continue...");
        }

        private string GetEndingName(EndingType ending)
        {
            return ending switch
            {
                EndingType.Usurper => "The Usurper (Dark Path)",
                EndingType.Savior => "The Savior (Light Path)",
                EndingType.Defiant => "The Defiant (Independent Path)",
                EndingType.TrueEnding => "The True Ending (Balance)",
                _ => "Unknown"
            };
        }

        #endregion

        #region Immortal Ascension

        private async Task<bool> OfferImmortality(Character player, EndingType ending, TerminalEmulator terminal)
        {
            terminal.Clear();
            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("═══════════════════════════════════════════════════════════════════", "bright_yellow");
            terminal.WriteLine("              T H E   C O S M O S   A W A I T S", "bright_yellow");
            terminal.WriteLine("═══════════════════════════════════════════════════════════════════", "bright_yellow");
            terminal.WriteLine("");

            await Task.Delay(1000);

            terminal.WriteLine("  You feel the power of creation flowing through your veins.", "white");
            terminal.WriteLine("  The mortal coil loosens. The divine realm beckons.", "white");
            terminal.WriteLine("");

            await Task.Delay(800);

            terminal.WriteLine("  \"You have proven yourself,\" Manwe whispers.", "bright_magenta");
            terminal.WriteLine("  \"Few mortals earn this choice.\"", "bright_magenta");
            terminal.WriteLine("  \"Will you transcend mortality and become a god?\"", "bright_magenta");
            terminal.WriteLine("");

            await Task.Delay(800);

            terminal.WriteLine("  As a god, you will:", "bright_cyan");
            terminal.WriteLine("  - Appear in the Temple for mortals to worship", "white");
            terminal.WriteLine("  - Manage followers and perform divine deeds", "white");
            terminal.WriteLine("  - Compete with other gods for believers", "white");
            terminal.WriteLine("  - Gain divine ranks from Lesser Spirit to God", "white");
            terminal.WriteLine("");
            terminal.WriteLine("  (You can renounce immortality at any time to reroll)", "gray");
            terminal.WriteLine("");

            var response = await terminal.GetInputAsync("  Ascend to godhood? (Y/N): ");
            if (response.Trim().ToUpper() != "Y") return false;

            // Choose divine name
            terminal.WriteLine("");
            terminal.WriteLine("  Choose your divine name (3-30 characters):", "bright_cyan");
            string divineName = "";
            while (true)
            {
                divineName = (await terminal.GetInputAsync("  Divine Name: ")).Trim();
                if (divineName.Length >= 3 && divineName.Length <= 30)
                    break;
                terminal.WriteLine("  Name must be 3-30 characters.", "red");
            }

            // Determine alignment from ending
            string alignment = ending switch
            {
                EndingType.Savior => "Light",
                EndingType.Usurper => "Dark",
                EndingType.Defiant => "Balance",
                EndingType.TrueEnding => "Balance",
                _ => "Balance"
            };

            // Auto-abdicate if player is the king
            if (player.King)
            {
                CastleLocation.AbdicatePlayerThrone(player, "abdicated the throne to ascend to godhood");
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("  The throne has been abdicated as you transcend mortal affairs.", "bright_yellow");
                terminal.WriteLine("");
                await Task.Delay(1500);
            }

            // Block alt characters from ascending
            if (SqlSaveBackend.IsAltCharacter(UsurperRemake.BBS.DoorMode.GetPlayerName() ?? ""))
            {
                terminal.WriteLine("");
                terminal.WriteLine("  Alt characters cannot ascend to immortality.", "red");
                terminal.WriteLine("  Only your main character may become a god.", "gray");
                await Task.Delay(2000);
                return false;
            }

            // Mark the alt slot as earned (persists even if they renounce)
            player.HasEarnedAltSlot = true;

            // Convert to immortal
            player.IsImmortal = true;
            player.DivineName = divineName;
            player.GodLevel = 1;
            player.GodExperience = 0;
            player.DeedsLeft = GameConfig.GodDeedsPerDay[0];
            player.GodAlignment = alignment;
            player.AscensionDate = DateTime.UtcNow;

            terminal.WriteLine("");
            await Task.Delay(500);

            terminal.SetColor("bright_yellow");
            terminal.WriteLine("  ════════════════════════════════════════════════════════════");
            terminal.WriteLine("");
            terminal.WriteLine($"  {divineName}, Lesser Spirit of {alignment}", "bright_yellow");
            terminal.WriteLine("  has ascended to the Divine Realm!", "bright_yellow");
            terminal.WriteLine("");
            terminal.WriteLine("  ════════════════════════════════════════════════════════════");

            await Task.Delay(1000);

            // Write news
            NewsSystem.Instance?.Newsy(true,
                $"[DIVINE] {player.Name2} has ascended to godhood as {divineName}!");

            // Achievement
            AchievementSystem.TryUnlock(player, "ascended");

            // Record this ending and advance the cycle — the player completed the game,
            // they just chose godhood instead of immediate reroll. This ensures:
            // 1. CompletedEndings tracks their achievement (gates prestige classes)
            // 2. CurrentCycle increments (gates NG+ bonuses when they renounce)
            StoryProgressionSystem.Instance.CompletedEndings.Add(ending);
            StoryProgressionSystem.Instance.CurrentCycle++;

            // Save immediately (includes the ending/cycle data)
            try
            {
                await SaveSystem.Instance.AutoSave(player);
            }
            catch { /* best effort */ }

            terminal.WriteLine("");
            terminal.WriteLine("  You will now enter the Pantheon — your eternal domain.", "bright_cyan");
            terminal.WriteLine("");

            await terminal.GetInputAsync("  Press Enter to enter the Divine Realm...");

            // Mark ending sequence as completed before routing to Pantheon
            StoryProgressionSystem.Instance.SetStoryFlag("ending_sequence_completed", true);

            // Route to Pantheon
            GameEngine.Instance.PendingImmortalAscension = true;

            return true;
        }

        #endregion

        #region New Game Plus

        private async Task OfferNewGamePlus(Character player, EndingType ending, TerminalEmulator terminal)
        {
            terminal.Clear();
            terminal.WriteLine("");
            terminal.WriteLine("═══════════════════════════════════════════════════════════════════", "bright_magenta");
            terminal.WriteLine("                  T H E   W H E E L   T U R N S", "bright_magenta");
            terminal.WriteLine("═══════════════════════════════════════════════════════════════════", "bright_magenta");
            terminal.WriteLine("");

            await Task.Delay(1000);

            terminal.WriteLine("  Something stirs in the darkness.", "white");
            terminal.WriteLine("  A voice you almost recognize.", "white");
            terminal.WriteLine("");

            await Task.Delay(800);

            terminal.WriteLine("  \"So. That happened.\"", "bright_magenta");
            terminal.WriteLine("  \"Want to go again? You'll be stronger this time.\"", "bright_magenta");
            terminal.WriteLine("  \"And you'll remember a few things...\"", "bright_magenta");
            terminal.WriteLine("");

            await Task.Delay(1000);

            terminal.WriteLine("  The wheel keeps turning.", "bright_cyan");
            terminal.WriteLine("");

            var cycle = StoryProgressionSystem.Instance.CurrentCycle;
            terminal.WriteLine($"  Current Cycle: {cycle}", "yellow");
            terminal.WriteLine($"  Next Cycle: {cycle + 1}", "green");
            terminal.WriteLine("");

            terminal.WriteLine("  Bonuses for New Game+:", "bright_green");
            terminal.WriteLine("  - Starting stat bonuses based on your ending", "white");
            terminal.WriteLine("  - Increased experience gain", "white");
            terminal.WriteLine("  - Knowledge of artifact locations", "white");
            terminal.WriteLine("  - New dialogue options with gods", "white");

            // Show which prestige classes this ending unlocks
            var newClasses = new List<string>();
            switch (ending)
            {
                case EndingType.Savior:
                    newClasses.Add("Tidesworn (Holy)");
                    newClasses.Add("Wavecaller (Good)");
                    break;
                case EndingType.Defiant:
                    newClasses.Add("Cyclebreaker (Neutral)");
                    break;
                case EndingType.Usurper:
                    newClasses.Add("Abysswarden (Dark)");
                    newClasses.Add("Voidreaver (Evil)");
                    break;
                case EndingType.TrueEnding:
                case EndingType.Secret:
                    newClasses.Add("Tidesworn (Holy)");
                    newClasses.Add("Wavecaller (Good)");
                    newClasses.Add("Cyclebreaker (Neutral)");
                    newClasses.Add("Abysswarden (Dark)");
                    newClasses.Add("Voidreaver (Evil)");
                    break;
            }
            if (newClasses.Count > 0)
            {
                terminal.WriteLine($"  - New prestige classes unlocked:", "white");
                foreach (var cls in newClasses)
                    terminal.WriteLine($"      {cls}", "bright_cyan");
            }
            terminal.WriteLine("");

            var response = await terminal.GetInputAsync("  Begin the Eternal Cycle? (Y/N): ");

            if (response.ToUpper() == "Y")
            {
                await CycleSystem.Instance.StartNewCycle(player, ending, terminal);
                // Signal the game to restart with a new character
                GameEngine.Instance.PendingNewGamePlus = true;
            }
            else
            {
                terminal.WriteLine("");
                terminal.WriteLine("  \"Fair enough. Get some rest.\"", "bright_magenta");
                terminal.WriteLine("  \"The wheel aint going anywhere.\"", "bright_magenta");
                terminal.WriteLine("");

                await terminal.GetInputAsync("  Press Enter to return to the main menu...");
            }

            // Mark the ending sequence as fully completed (player answered the NG+ prompt).
            // This prevents re-triggering on reconnect if they disconnected mid-sequence.
            StoryProgressionSystem.Instance.SetStoryFlag("ending_sequence_completed", true);
            try { await SaveSystem.Instance.AutoSave(player); } catch { /* best effort */ }
        }

        #endregion
    }
}
