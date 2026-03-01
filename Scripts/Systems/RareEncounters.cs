using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UsurperRemake.Utils;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Rare special encounters for dungeons - LORD-style memorable events
    /// These are uncommon discoveries that make exploration exciting
    /// </summary>
    public static class RareEncounters
    {
        private static Random random = new Random();

        // Encounter chance: 5% per room exploration
        public const double RareEncounterChance = 0.05;

        /// <summary>
        /// Check if a rare encounter should occur and run it
        /// </summary>
        public static async Task<bool> TryRareEncounter(
            TerminalEmulator terminal,
            Character player,
            DungeonTheme theme,
            int level)
        {
            if (random.NextDouble() > RareEncounterChance)
                return false;

            // Get themed encounter
            var encounter = GetThemedEncounter(theme, level);
            await encounter(terminal, player, level);
            return true;
        }

        /// <summary>
        /// Get a random encounter appropriate for the dungeon theme
        /// </summary>
        private static Func<TerminalEmulator, Character, int, Task> GetThemedEncounter(DungeonTheme theme, int level)
        {
            // Universal encounters (can happen anywhere)
            var universal = new List<Func<TerminalEmulator, Character, int, Task>>
            {
                HiddenTavernEncounter,
                WanderingMinstrelEncounter,
                OldHermitEncounter,
                FairyCircleEncounter,
                GamblingDemonsEncounter,
                DamselInDistressEncounter,
                MysteriousMerchantEncounter,
                TimeWarpEncounter,
                UsurperGhostEncounter,  // Easter egg!
                AncientLibraryEncounter,
                WishingWellEncounter,
                ArenaPortalEncounter
            };

            // Theme-specific encounters
            var themed = theme switch
            {
                DungeonTheme.Catacombs => new List<Func<TerminalEmulator, Character, int, Task>>
                {
                    BoneOracleEncounter,
                    RestlessSpiritsEncounter,
                    CryptKeeperEncounter,
                    AncientTombEncounter
                },
                DungeonTheme.Sewers => new List<Func<TerminalEmulator, Character, int, Task>>
                {
                    RatKingEncounter,
                    LostChildEncounter,
                    AlchemistLabEncounter,
                    TreasureHoardEncounter
                },
                DungeonTheme.Caverns => new List<Func<TerminalEmulator, Character, int, Task>>
                {
                    CrystalCaveEncounter,
                    DragonHoardEncounter,
                    DwarvenOutpostEncounter,
                    UndergroundLakeEncounter
                },
                DungeonTheme.AncientRuins => new List<Func<TerminalEmulator, Character, int, Task>>
                {
                    AncientGolemEncounter,
                    TimeCapsuleEncounter,
                    MagicFountainEncounter,
                    LostCivilizationEncounter
                },
                DungeonTheme.DemonLair => new List<Func<TerminalEmulator, Character, int, Task>>
                {
                    DemonBargainEncounter,
                    TorturedSoulsEncounter,
                    InfernalForgeEncounter,
                    SuccubusEncounter
                },
                DungeonTheme.FrozenDepths => new List<Func<TerminalEmulator, Character, int, Task>>
                {
                    FrozenAdventurerEncounter,
                    IceQueenEncounter,
                    YetiDenEncounter,
                    AuroraVisionEncounter
                },
                DungeonTheme.VolcanicPit => new List<Func<TerminalEmulator, Character, int, Task>>
                {
                    FireElementalEncounter,
                    LavaBoatEncounter,
                    PhoenixNestEncounter,
                    ObsidianMirrorEncounter
                },
                DungeonTheme.AbyssalVoid => new List<Func<TerminalEmulator, Character, int, Task>>
                {
                    VoidWhisperEncounter,
                    RealityTearEncounter,
                    CosmicEntityEncounter,
                    MadnessPoolEncounter
                },
                _ => new List<Func<TerminalEmulator, Character, int, Task>>()
            };

            // 70% chance for universal, 30% for themed (if available)
            if (themed.Count > 0 && random.NextDouble() < 0.3)
            {
                return themed[random.Next(themed.Count)];
            }
            return universal[random.Next(universal.Count)];
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // UNIVERSAL ENCOUNTERS (LORD-style memorable events)
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Hidden tavern in the dungeon - rest, drink, gamble
        /// </summary>
        private static async Task HiddenTavernEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("yellow");
            terminal.WriteLine("╔═══════════════════════════════════════════════════════╗");
            terminal.WriteLine("║            * THE WAYWARD WANDERER *                   ║");
            terminal.WriteLine("║              A Hidden Tavern                          ║");
            terminal.WriteLine("╚═══════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("You push aside a hidden stone door and discover...");
            terminal.WriteLine("an underground tavern! Torches flicker warmly and");
            terminal.WriteLine("the smell of ale fills the air.");
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.WriteLine("A grizzled bartender nods at you.");
            terminal.WriteLine("\"Welcome, traveler. What'll it be?\"");
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("D");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Drink Ale (50 gold - recover HP)");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("G");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Gamble with patrons (risk gold)");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("T");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Talk to mysterious stranger");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("R");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Rest in back room (full heal, 200 gold)");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("L");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Leave");
            terminal.WriteLine("");

            bool inTavern = true;
            while (inTavern)
            {
                var choice = await terminal.GetInput("Your choice: ");
                switch (choice.ToUpper())
                {
                    case "D":
                        if (player.Gold >= 50)
                        {
                            player.Gold -= 50;
                            long heal = player.MaxHP / 3;
                            player.HP = Math.Min(player.MaxHP, player.HP + heal);
                            terminal.SetColor("green");
                            terminal.WriteLine($"The ale warms your soul. +{heal} HP!");
                            terminal.WriteLine($"HP: {player.HP}/{player.MaxHP}");
                        }
                        else
                        {
                            terminal.WriteLine("\"No coin, no drink.\" the bartender grunts.", "red");
                        }
                        break;

                    case "G":
                        await TavernGambling(terminal, player);
                        break;

                    case "T":
                        await TavernStranger(terminal, player, level);
                        break;

                    case "R":
                        if (player.Gold >= 200)
                        {
                            player.Gold -= 200;
                            player.HP = player.MaxHP;
                            if (player.Poison > 0) player.Poison = 0;
                            terminal.SetColor("bright_green");
                            terminal.WriteLine("You sleep deeply in the back room.");
                            terminal.WriteLine("You awake fully refreshed!");
                            terminal.WriteLine($"HP: {player.HP}/{player.MaxHP}");
                        }
                        else
                        {
                            terminal.WriteLine("\"200 gold for a room.\" You don't have enough.", "red");
                        }
                        break;

                    case "L":
                        inTavern = false;
                        terminal.WriteLine("You slip back into the dungeon.", "gray");
                        break;
                }

                if (inTavern)
                {
                    terminal.WriteLine("");
                    terminal.WriteLine("[D]rink [G]amble [T]alk [R]est [L]eave", "bright_yellow");
                }
            }

            await terminal.PressAnyKey();
        }

        private static async Task TavernGambling(TerminalEmulator terminal, Character player)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("");
            terminal.WriteLine("A hooded figure shuffles cards.");
            terminal.WriteLine("\"Simple game. High card wins. Double or nothing.\"");
            terminal.WriteLine("");

            long bet = Math.Min(player.Gold / 4, 500);
            if (bet < 10)
            {
                terminal.WriteLine("\"You've got nothing worth betting.\"", "gray");
                return;
            }

            terminal.Write($"Bet {bet} gold? (Y/N): ", "white");
            var choice = await terminal.GetInput("");

            if (choice.ToUpper() == "Y")
            {
                player.Gold -= bet;
                await Task.Delay(1000);

                int playerCard = random.Next(1, 14);
                int dealerCard = random.Next(1, 14);

                string[] cardNames = { "", "Ace", "2", "3", "4", "5", "6", "7", "8", "9", "10", "Jack", "Queen", "King" };

                terminal.WriteLine($"You draw: {cardNames[playerCard]}", "cyan");
                terminal.WriteLine($"Dealer draws: {cardNames[dealerCard]}", "red");

                if (playerCard > dealerCard)
                {
                    player.Gold += bet * 2;
                    terminal.SetColor("bright_green");
                    terminal.WriteLine($"You win {bet * 2} gold!");
                }
                else if (playerCard < dealerCard)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine("You lose! The dealer grins.");
                }
                else
                {
                    player.Gold += bet;
                    terminal.SetColor("yellow");
                    terminal.WriteLine("Tie! You get your bet back.");
                }
            }
        }

        private static async Task TavernStranger(TerminalEmulator terminal, Character player, int level)
        {
            terminal.SetColor("magenta");
            terminal.WriteLine("");
            terminal.WriteLine("A cloaked stranger sits in the corner.");
            terminal.WriteLine("As you approach, they look up...");
            terminal.WriteLine("");

            var strangerType = random.Next(5);
            switch (strangerType)
            {
                case 0:
                    terminal.SetColor("cyan");
                    terminal.WriteLine("\"I was once like you. Seeking glory in these depths.\"");
                    terminal.WriteLine("\"Take this. It saved my life once.\"");
                    player.Healing = Math.Min(player.MaxPotions, player.Healing + 3);
                    terminal.SetColor("green");
                    terminal.WriteLine("Received 3 healing potions!");
                    break;

                case 1:
                    terminal.SetColor("yellow");
                    terminal.WriteLine("\"I've mapped these halls. Here, take my notes.\"");
                    long expGain = level * 100;
                    player.Experience += expGain;
                    terminal.SetColor("green");
                    terminal.WriteLine($"+{expGain} experience from studying the map!");
                    break;

                case 2:
                    terminal.SetColor("red");
                    terminal.WriteLine("\"The boss ahead... it fears fire. Remember that.\"");
                    terminal.SetColor("gray");
                    terminal.WriteLine("You nod, storing this information away.");
                    break;

                case 3:
                    terminal.SetColor("bright_white");
                    terminal.WriteLine("The stranger pulls back their hood...");
                    terminal.WriteLine("It's a beautiful face, but their eyes are empty.");
                    terminal.WriteLine("\"Would you trade a year of life for power?\"");
                    terminal.WriteLine("");
                    terminal.Write("Accept? (Y/N): ", "white");
                    var accept = await terminal.GetInput("");
                    if (accept.ToUpper() == "Y")
                    {
                        player.Strength += 5;
                        player.Defence += 5;
                        terminal.SetColor("magenta");
                        terminal.WriteLine("Your soul aches, but power flows through you!");
                        terminal.WriteLine("+5 Strength, +5 Defence!");
                    }
                    else
                    {
                        terminal.WriteLine("\"Wise... or foolish. Time will tell.\"", "gray");
                    }
                    break;

                case 4:
                    terminal.SetColor("gray");
                    terminal.WriteLine("The stranger is just a drunk adventurer.");
                    terminal.WriteLine("They ramble about treasure and then pass out.");
                    break;
            }
        }

        /// <summary>
        /// Wandering minstrel - songs that grant buffs
        /// </summary>
        private static async Task WanderingMinstrelEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("♪♫ A WANDERING MINSTREL ♫♪");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("A bard sits on a rock, tuning a lute.");
            terminal.WriteLine("\"Ah, a fellow traveler! Care for a song?\"");
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("1");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("cyan");
            terminal.WriteLine("Song of Valor (+Strength for next combat)");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("2");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("cyan");
            terminal.WriteLine("Song of Warding (+Defence for next combat)");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("3");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("cyan");
            terminal.WriteLine("Song of Healing (Restore HP)");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("4");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("cyan");
            terminal.WriteLine("Request a ballad about yourself (costs 100 gold)");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("5");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("cyan");
            terminal.WriteLine("Just listen and leave");
            terminal.WriteLine("");

            var choice = await terminal.GetInput("Your choice: ");

            switch (choice)
            {
                case "1":
                    terminal.SetColor("yellow");
                    terminal.WriteLine("The bard plays a rousing battle hymn!");
                    terminal.WriteLine("Your blood pumps faster, your grip tightens!");
                    // TODO: Add temporary buff system
                    player.Strength += 3; // Permanent for now
                    terminal.WriteLine("+3 Strength!", "green");
                    break;

                case "2":
                    terminal.SetColor("blue");
                    terminal.WriteLine("A soothing melody wraps around you like armor.");
                    player.Defence += 3;
                    terminal.WriteLine("+3 Defence!", "green");
                    break;

                case "3":
                    terminal.SetColor("green");
                    terminal.WriteLine("The healing song washes over you...");
                    player.HP = Math.Min(player.MaxHP, player.HP + player.MaxHP / 2);
                    terminal.WriteLine($"HP restored to {player.HP}/{player.MaxHP}!");
                    break;

                case "4":
                    if (player.Gold >= 100)
                    {
                        player.Gold -= 100;
                        player.Chivalry += 50;
                        terminal.SetColor("bright_yellow");
                        terminal.WriteLine($"\"The Ballad of {player.DisplayName}!\"");
                        terminal.WriteLine("The bard composes an epic tale of your adventures.");
                        terminal.WriteLine("Your fame spreads! +50 Chivalry!");
                    }
                    else
                    {
                        terminal.WriteLine("\"No gold, no ballad, friend.\"", "gray");
                    }
                    break;

                default:
                    terminal.SetColor("gray");
                    terminal.WriteLine("You listen to a pleasant tune, then continue on.");
                    player.Experience += level * 10;
                    break;
            }

            await terminal.PressAnyKey();
        }

        /// <summary>
        /// Fairy circle - blessings or curses
        /// </summary>
        private static async Task FairyCircleEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("✧ FAIRY CIRCLE ✧");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("You stumble upon a ring of glowing mushrooms.");
            terminal.WriteLine("Tiny winged figures dance within, trailing sparkles.");
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.WriteLine("The fairies notice you and flutter closer...");
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("A");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Ask for a blessing (risky)");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("S");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Try to steal fairy dust (very risky)");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("D");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Dance with them (???)");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("L");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Leave them alone");
            terminal.WriteLine("");

            var choice = await terminal.GetInput("Your choice: ");

            switch (choice.ToUpper())
            {
                case "A":
                    if (random.NextDouble() < 0.7) // 70% good outcome
                    {
                        terminal.SetColor("bright_green");
                        terminal.WriteLine("The fairies giggle and shower you with sparkles!");
                        var blessing = random.Next(4);
                        switch (blessing)
                        {
                            case 0:
                                player.HP = player.MaxHP;
                                terminal.WriteLine("Full health restored!");
                                break;
                            case 1:
                                player.Mana = player.MaxMana;
                                terminal.WriteLine("Full mana restored!");
                                break;
                            case 2:
                                player.Healing = player.MaxPotions;
                                terminal.WriteLine("Potions refilled!");
                                break;
                            case 3:
                                player.Experience += level * 200;
                                terminal.WriteLine($"+{level * 200} experience!");
                                break;
                        }
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine("The fairies are offended by your tone!");
                        terminal.WriteLine("They curse you with bad luck...");
                        player.Gold = player.Gold * 9 / 10;
                        terminal.WriteLine("10% of your gold vanishes!");
                    }
                    break;

                case "S":
                    if (random.NextDouble() < 0.3) // Only 30% success
                    {
                        terminal.SetColor("bright_yellow");
                        terminal.WriteLine("You snatch a handful of fairy dust!");
                        long dustValue = level * 500;
                        player.Gold += dustValue;
                        terminal.WriteLine($"Worth {dustValue} gold!");

                        player.Darkness += 20;
                        terminal.SetColor("magenta");
                        terminal.WriteLine("Your darkness increases from the theft...");
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine("The fairies swarm you in rage!");
                        int damage = (int)(player.MaxHP / 4);
                        player.HP -= damage;
                        terminal.WriteLine($"You take {damage} damage fleeing!");

                        // Random curse
                        if (random.NextDouble() < 0.5)
                        {
                            player.Strength = Math.Max(1, player.Strength - 2);
                            terminal.WriteLine("Cursed! -2 Strength!");
                        }
                    }
                    break;

                case "D":
                    terminal.SetColor("bright_magenta");
                    terminal.WriteLine("You join the fairy dance!");
                    await Task.Delay(1000);
                    terminal.WriteLine("Round and round you spin...");
                    await Task.Delay(1000);
                    terminal.WriteLine("Time seems to blur...");
                    await Task.Delay(1000);

                    // Weird effects
                    var effect = random.Next(5);
                    switch (effect)
                    {
                        case 0:
                            terminal.SetColor("green");
                            terminal.WriteLine("You emerge feeling decades younger!");
                            player.HP = player.MaxHP;
                            player.Mana = player.MaxMana;
                            player.Experience += level * 300;
                            break;
                        case 1:
                            terminal.SetColor("yellow");
                            terminal.WriteLine("Gold coins fall from your pockets as you dance!");
                            player.Gold += level * 900;  // Increased from 300 for economic balance  // Increased from 100 for economic balance
                            break;
                        case 2:
                            terminal.SetColor("cyan");
                            terminal.WriteLine("The fairy queen kisses your forehead!");
                            player.Charisma += 5;
                            terminal.WriteLine("+5 Charisma!");
                            break;
                        case 3:
                            terminal.SetColor("gray");
                            terminal.WriteLine("Hours pass... or was it days?");
                            terminal.WriteLine("You're not sure what happened.");
                            break;
                        case 4:
                            terminal.SetColor("bright_white");
                            terminal.WriteLine("You learn the secret fairy language!");
                            player.Intelligence += 3;
                            terminal.WriteLine("+3 Intelligence!");
                            break;
                    }
                    break;

                default:
                    terminal.SetColor("gray");
                    terminal.WriteLine("You back away slowly.");
                    terminal.WriteLine("The fairies wave goodbye and return to their dance.");
                    break;
            }

            await terminal.PressAnyKey();
        }

        /// <summary>
        /// Damsel in distress - classic rescue scenario
        /// </summary>
        private static async Task DamselInDistressEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("magenta");
            terminal.WriteLine("♀ DAMSEL IN DISTRESS ♀");
            terminal.WriteLine("");

            // Randomize the scenario
            var scenario = random.Next(4);

            switch (scenario)
            {
                case 0: // Classic rescue
                    terminal.SetColor("white");
                    terminal.WriteLine("A young woman is cornered by goblins!");
                    terminal.WriteLine("She cries out for help as they close in.");
                    terminal.WriteLine("");

                    terminal.SetColor("darkgray");
                    terminal.Write("[");
                    terminal.SetColor("bright_yellow");
                    terminal.Write("R");
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor("white");
                    terminal.WriteLine("Rush to her rescue!");
                    terminal.SetColor("darkgray");
                    terminal.Write("[");
                    terminal.SetColor("bright_yellow");
                    terminal.Write("W");
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor("white");
                    terminal.WriteLine("Watch and wait");
                    terminal.SetColor("darkgray");
                    terminal.Write("[");
                    terminal.SetColor("bright_yellow");
                    terminal.Write("I");
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor("white");
                    terminal.WriteLine("Ignore and continue");
                    terminal.WriteLine("");

                    var choice = await terminal.GetInput("Your choice: ");

                    if (choice.ToUpper() == "R")
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine("You charge at the goblins!");
                        await Task.Delay(1000);

                        // Auto-win the fight for dramatic effect
                        terminal.SetColor("green");
                        terminal.WriteLine("The goblins scatter before your fury!");
                        terminal.WriteLine("");

                        terminal.SetColor("cyan");
                        terminal.WriteLine("The woman throws her arms around you.");
                        terminal.WriteLine("\"My hero! Please, take this family heirloom!\"");

                        long goldReward = level * 200;
                        player.Gold += goldReward;
                        player.Chivalry += 75;
                        terminal.SetColor("bright_yellow");
                        terminal.WriteLine($"+{goldReward} gold!");
                        terminal.WriteLine("+75 Chivalry!");

                        if (random.NextDouble() < 0.3)
                        {
                            terminal.SetColor("magenta");
                            terminal.WriteLine("");
                            terminal.WriteLine("She blushes. \"Perhaps we'll meet again...\"");
                            // TODO: Add romance subplot tracking
                        }
                    }
                    else if (choice.ToUpper() == "W")
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine("You hide in the shadows and watch...");
                        await Task.Delay(1500);
                        terminal.SetColor("cyan");
                        terminal.WriteLine("Suddenly she kicks the lead goblin and draws a hidden blade!");
                        terminal.WriteLine("She dispatches them with deadly efficiency.");
                        terminal.WriteLine("");
                        terminal.WriteLine("She notices you watching and winks.");
                        terminal.WriteLine("\"Thanks for not interfering. Here's for the show.\"");
                        player.Gold += level * 150;  // Increased from 50 for economic balance
                        player.Experience += level * 100;
                        terminal.SetColor("yellow");
                        terminal.WriteLine($"+{level * 150} gold, +{level * 100} exp!");
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine("You walk away. Her screams fade behind you.");
                        player.Darkness += 30;
                        terminal.WriteLine("+30 Darkness");
                    }
                    break;

                case 1: // It's a trap!
                    terminal.SetColor("white");
                    terminal.WriteLine("A beautiful woman lies injured on the ground.");
                    terminal.WriteLine("\"Please... help me... I'm hurt...\"");
                    terminal.WriteLine("");

                    terminal.SetColor("darkgray");
                    terminal.Write("[");
                    terminal.SetColor("bright_yellow");
                    terminal.Write("H");
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor("white");
                    terminal.WriteLine("Help her up");
                    terminal.SetColor("darkgray");
                    terminal.Write("[");
                    terminal.SetColor("bright_yellow");
                    terminal.Write("C");
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor("white");
                    terminal.WriteLine("Cautiously approach");
                    terminal.SetColor("darkgray");
                    terminal.Write("[");
                    terminal.SetColor("bright_yellow");
                    terminal.Write("L");
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor("white");
                    terminal.WriteLine("Leave");
                    terminal.WriteLine("");

                    choice = await terminal.GetInput("Your choice: ");

                    if (choice.ToUpper() == "H")
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine("As you reach down, she grabs your arm!");
                        terminal.WriteLine("Her face twists into a demonic grin!");
                        terminal.WriteLine("\"FOOLISH MORTAL!\"");
                        await Task.Delay(1000);

                        int damage = (int)(player.MaxHP / 3);
                        player.HP -= damage;
                        long goldStolen = player.Gold / 5;
                        player.Gold -= goldStolen;

                        terminal.WriteLine($"A succubus! You take {damage} damage!");
                        terminal.WriteLine($"She steals {goldStolen} gold before vanishing!");
                    }
                    else if (choice.ToUpper() == "C")
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine("You approach carefully, hand on weapon...");
                        terminal.WriteLine("Her eyes flash red - she hisses and vanishes!");
                        terminal.SetColor("green");
                        terminal.WriteLine("Your caution saved you from a succubus trap!");
                        player.Experience += level * 75;
                    }
                    else
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine("Something feels wrong. You leave.");
                    }
                    break;

                case 2: // Princess!
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine("A woman in tattered royal garments hides behind a pillar.");
                    terminal.WriteLine("\"You! Adventurer! I am Princess Althea!\"");
                    terminal.WriteLine("\"I was kidnapped! Please, escort me to safety!\"");
                    terminal.WriteLine("");

                    terminal.SetColor("darkgray");
                    terminal.Write("[");
                    terminal.SetColor("bright_yellow");
                    terminal.Write("E");
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor("white");
                    terminal.WriteLine("Escort her to safety");
                    terminal.SetColor("darkgray");
                    terminal.Write("[");
                    terminal.SetColor("bright_yellow");
                    terminal.Write("R");
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor("white");
                    terminal.WriteLine("Ransom her yourself");
                    terminal.SetColor("darkgray");
                    terminal.Write("[");
                    terminal.SetColor("bright_yellow");
                    terminal.Write("L");
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor("white");
                    terminal.WriteLine("\"Sorry, too dangerous\"");
                    terminal.WriteLine("");

                    choice = await terminal.GetInput("Your choice: ");

                    if (choice.ToUpper() == "E")
                    {
                        terminal.SetColor("green");
                        terminal.WriteLine("You guide the princess through the dungeon.");
                        await Task.Delay(1000);
                        terminal.WriteLine("After a harrowing journey, you reach the exit.");
                        terminal.WriteLine("");
                        terminal.SetColor("bright_yellow");
                        terminal.WriteLine("\"The kingdom will reward you handsomely!\"");

                        long royalReward = level * 1000;
                        player.Gold += royalReward;
                        player.Chivalry += 200;
                        player.Experience += level * 500;

                        terminal.WriteLine($"+{royalReward} gold!");
                        terminal.WriteLine("+200 Chivalry!");
                        terminal.WriteLine($"+{level * 500} experience!");
                    }
                    else if (choice.ToUpper() == "R")
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine("You see opportunity in her captivity...");
                        terminal.WriteLine("\"Actually, princess, I think I'LL ransom you.\"");
                        terminal.WriteLine("");

                        player.Gold += level * 5000;  // Increased from 2000 for economic balance (major jackpot)
                        player.Darkness += 100;
                        player.Chivalry = Math.Max(0, player.Chivalry - 100);

                        terminal.SetColor("yellow");
                        terminal.WriteLine($"+{level * 5000} gold from ransom!");
                        terminal.SetColor("magenta");
                        terminal.WriteLine("+100 Darkness, -100 Chivalry");
                        terminal.WriteLine("Your reputation suffers...");
                    }
                    else
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine("\"I wish you luck, princess.\"");
                        terminal.WriteLine("She looks at you with despair as you leave.");
                    }
                    break;

                case 3: // Warrior woman
                    terminal.SetColor("cyan");
                    terminal.WriteLine("A female warrior battles a horde of undead!");
                    terminal.WriteLine("She's holding her own but clearly outnumbered.");
                    terminal.WriteLine("");

                    terminal.SetColor("darkgray");
                    terminal.Write("[");
                    terminal.SetColor("bright_yellow");
                    terminal.Write("J");
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor("cyan");
                    terminal.WriteLine("Join the fight!");
                    terminal.SetColor("darkgray");
                    terminal.Write("[");
                    terminal.SetColor("bright_yellow");
                    terminal.Write("W");
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor("cyan");
                    terminal.WriteLine("Watch (she's doing fine)");
                    terminal.WriteLine("");

                    choice = await terminal.GetInput("Your choice: ");

                    if (choice.ToUpper() == "J")
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine("You leap into battle beside her!");
                        await Task.Delay(1000);
                        terminal.WriteLine("Together you destroy the undead horde!");
                        terminal.WriteLine("");

                        terminal.SetColor("cyan");
                        terminal.WriteLine("She wipes her blade clean.");
                        terminal.WriteLine("\"Well fought! I am Valeria, knight-errant.\"");
                        terminal.WriteLine("\"Share the spoils?\"");

                        long loot = level * 150;
                        player.Gold += loot;
                        player.Experience += level * 100;

                        terminal.SetColor("green");
                        terminal.WriteLine($"+{loot} gold, +{level * 100} exp!");

                        if (random.NextDouble() < 0.2)
                        {
                            terminal.SetColor("bright_cyan");
                            terminal.WriteLine("");
                            terminal.WriteLine("\"You fight well. Perhaps we should team up sometime.\"");
                            // TODO: Add companion system
                        }
                    }
                    else
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine("You watch as she finishes off the last skeleton.");
                        terminal.WriteLine("She notices you and glares.");
                        terminal.WriteLine("\"Coward.\" She spits at your feet and leaves.");
                        player.Chivalry = Math.Max(0, player.Chivalry - 20);
                    }
                    break;
            }

            await terminal.PressAnyKey();
        }

        /// <summary>
        /// Usurper ghost - Easter egg from the original game
        /// </summary>
        private static async Task UsurperGhostEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_white");
            terminal.WriteLine("╔═══════════════════════════════════════════════════════╗");
            terminal.WriteLine("║              * GHOSTLY APPARITION *                    ║");
            terminal.WriteLine("╚═══════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.WriteLine("A translucent figure materializes before you...");
            terminal.WriteLine("It wears ancient armor and carries a spectral sword.");
            terminal.WriteLine("");

            await Task.Delay(1500);

            terminal.SetColor("bright_yellow");
            terminal.WriteLine("\"Greetings, adventurer...\"");
            terminal.WriteLine("\"I am the ghost of an Usurper past.\"");
            terminal.WriteLine("\"In my time, we sought the throne of Dovania.\"");
            terminal.WriteLine("\"I see you walk the same path...\"");
            terminal.WriteLine("");

            await Task.Delay(2000);

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("L");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("\"Tell me of the old days\"");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("A");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("\"Any advice for a fellow Usurper?\"");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("F");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("\"Are you friend or foe?\"");
            terminal.WriteLine("");

            var choice = await terminal.GetInput("Your choice: ");

            switch (choice.ToUpper())
            {
                case "L":
                    terminal.SetColor("cyan");
                    terminal.WriteLine("");
                    terminal.WriteLine("The ghost's eyes grow distant...");
                    terminal.WriteLine("\"Ah, the old BBS days... connecting at 2400 baud...\"");
                    terminal.WriteLine("\"Players from across the realm, competing for glory...\"");
                    terminal.WriteLine("\"The witch doctors were truly fearsome then...\"");
                    terminal.WriteLine("\"Many tried to claim the throne. Few succeeded.\"");
                    terminal.WriteLine("");
                    terminal.SetColor("gray");
                    terminal.WriteLine("The ghost seems lost in nostalgia.");

                    player.Experience += level * 200;
                    terminal.SetColor("green");
                    terminal.WriteLine($"+{level * 200} experience from ancient wisdom!");
                    break;

                case "A":
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine("");
                    terminal.WriteLine("The ghost nods sagely...");
                    terminal.WriteLine("\"Always visit the Abbey for healing.\"");
                    terminal.WriteLine("\"The dungeons hold treasure, but also death.\"");
                    terminal.WriteLine("\"Make allies before you make enemies.\"");
                    terminal.WriteLine("\"And never, NEVER trust a witch doctor.\"");
                    terminal.WriteLine("");

                    player.Intelligence += 2;
                    player.Wisdom += 2;
                    terminal.SetColor("green");
                    terminal.WriteLine("+2 Intelligence, +2 Wisdom!");
                    break;

                case "F":
                    terminal.SetColor("white");
                    terminal.WriteLine("");
                    terminal.WriteLine("The ghost chuckles, a hollow sound.");
                    terminal.WriteLine("\"Neither. I am simply... a memory.\"");
                    terminal.WriteLine("\"But here, take this. It served me well.\"");
                    terminal.WriteLine("");

                    // Give a nice reward
                    long goldGift = level * 300;
                    player.Gold += goldGift;
                    player.Strength += 3;

                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine($"The ghost gives you {goldGift} spectral gold!");
                    terminal.WriteLine("+3 Strength from ancestral blessing!");
                    break;

                default:
                    terminal.SetColor("gray");
                    terminal.WriteLine("The ghost begins to fade...");
                    break;
            }

            terminal.SetColor("cyan");
            terminal.WriteLine("");
            terminal.WriteLine("\"Remember... the throne awaits...\"");
            terminal.WriteLine("The ghost fades into nothingness.");

            await terminal.PressAnyKey();
        }

        /// <summary>
        /// Gambling with demons - high risk, high reward
        /// </summary>
        private static async Task GamblingDemonsEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("red");
            terminal.WriteLine("🎲 INFERNAL GAME OF CHANCE 🎲");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("Three demons sit around a table of bones.");
            terminal.WriteLine("They're playing some sort of game with human skulls.");
            terminal.WriteLine("");

            terminal.SetColor("red");
            terminal.WriteLine("\"A mortal! How delightful!\"");
            terminal.WriteLine("\"Care to play? The stakes are... interesting.\"");
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("P");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Play their game");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("L");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Leave quickly");
            terminal.WriteLine("");

            var choice = await terminal.GetInput("Your choice: ");

            if (choice.ToUpper() == "P")
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("\"Excellent! The game is simple.\"");
                terminal.WriteLine("\"We each roll three skulls. Highest total wins.\"");
                terminal.WriteLine("\"But what shall we wager...?\"");
                terminal.WriteLine("");

                terminal.SetColor("darkgray");
                terminal.Write("[");
                terminal.SetColor("bright_yellow");
                terminal.Write("G");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("red");
                terminal.WriteLine("Wager gold (1000)");
                terminal.SetColor("darkgray");
                terminal.Write("[");
                terminal.SetColor("bright_yellow");
                terminal.Write("S");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("red");
                terminal.WriteLine("Wager your soul (permanent stat changes)");
                terminal.SetColor("darkgray");
                terminal.Write("[");
                terminal.SetColor("bright_yellow");
                terminal.Write("Y");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("red");
                terminal.WriteLine("Wager years of life (experience)");
                terminal.WriteLine("");

                var wager = await terminal.GetInput("Your wager: ");

                int demonRoll = random.Next(1, 7) + random.Next(1, 7) + random.Next(1, 7);
                int playerRoll = random.Next(1, 7) + random.Next(1, 7) + random.Next(1, 7);

                await Task.Delay(1000);
                terminal.WriteLine("");
                terminal.WriteLine($"The demons roll: {demonRoll}", "red");
                terminal.WriteLine($"You roll: {playerRoll}", "cyan");
                terminal.WriteLine("");

                bool won = playerRoll > demonRoll;

                switch (wager.ToUpper())
                {
                    case "G":
                        if (won)
                        {
                            player.Gold += 1000;
                            terminal.SetColor("green");
                            terminal.WriteLine("The demons hiss in frustration!");
                            terminal.WriteLine("+1000 gold!");
                        }
                        else
                        {
                            player.Gold = Math.Max(0, player.Gold - 1000);
                            terminal.SetColor("red");
                            terminal.WriteLine("The demons cackle with glee!");
                            terminal.WriteLine("-1000 gold!");
                        }
                        break;

                    case "S":
                        if (won)
                        {
                            player.Strength += 10;
                            player.Intelligence += 10;
                            terminal.SetColor("bright_green");
                            terminal.WriteLine("Demonic power flows into you!");
                            terminal.WriteLine("+10 Strength, +10 Intelligence!");
                        }
                        else
                        {
                            player.Strength = Math.Max(1, player.Strength - 5);
                            player.Charisma = Math.Max(1, player.Charisma - 5);
                            player.Darkness += 50;
                            terminal.SetColor("red");
                            terminal.WriteLine("Part of your soul is ripped away!");
                            terminal.WriteLine("-5 Strength, -5 Charisma, +50 Darkness!");
                        }
                        break;

                    case "Y":
                        if (won)
                        {
                            player.Experience += level * 1000;
                            terminal.SetColor("bright_yellow");
                            terminal.WriteLine("The demons grant you centuries of knowledge!");
                            terminal.WriteLine($"+{level * 1000} experience!");
                        }
                        else
                        {
                            player.Experience = Math.Max(0, player.Experience - level * 500);
                            terminal.SetColor("red");
                            terminal.WriteLine("Years of your life drain away!");
                            terminal.WriteLine($"-{level * 500} experience!");
                        }
                        break;
                }
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine("You back away slowly.");
                terminal.WriteLine("\"Coward!\" the demons shout. \"Come back anytime!\"");
            }

            await terminal.PressAnyKey();
        }

        /// <summary>
        /// Old hermit with wisdom
        /// </summary>
        private static async Task OldHermitEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("gray");
            terminal.WriteLine("=== AN OLD HERMIT ===");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("An ancient man sits by a small fire.");
            terminal.WriteLine("His eyes are clouded, but he seems to sense you.");
            terminal.WriteLine("\"Sit, child. Rest your weary bones.\"");
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("S");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("cyan");
            terminal.WriteLine("Sit and listen to his wisdom");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("A");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("cyan");
            terminal.WriteLine("Ask about the dungeon ahead");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("G");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("cyan");
            terminal.WriteLine("Give him food/gold");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("L");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("cyan");
            terminal.WriteLine("Leave");
            terminal.WriteLine("");

            var choice = await terminal.GetInput("Your choice: ");

            switch (choice.ToUpper())
            {
                case "S":
                    terminal.SetColor("yellow");
                    terminal.WriteLine("You sit by the fire...");
                    await Task.Delay(1500);
                    terminal.WriteLine("The hermit speaks of the old world...");
                    await Task.Delay(1500);
                    terminal.WriteLine("Of heroes who came before...");
                    await Task.Delay(1500);
                    terminal.SetColor("green");
                    terminal.WriteLine("His words fill you with peace.");
                    player.HP = player.MaxHP;
                    player.Mana = player.MaxMana;
                    terminal.WriteLine("HP and Mana fully restored!");
                    break;

                case "A":
                    terminal.SetColor("cyan");
                    terminal.WriteLine("\"The path ahead is treacherous...\"");
                    terminal.WriteLine("\"Beware the third room from here.\"");
                    terminal.WriteLine("\"And the boss... it fears silver.\"");
                    player.Intelligence += 1;
                    terminal.SetColor("green");
                    terminal.WriteLine("+1 Intelligence from his wisdom!");
                    break;

                case "G":
                    if (player.Gold >= 50)
                    {
                        player.Gold -= 50;
                        terminal.SetColor("bright_yellow");
                        terminal.WriteLine("The hermit's blind eyes seem to sparkle.");
                        terminal.WriteLine("\"Kindness... so rare in these depths.\"");
                        terminal.WriteLine("He presses something into your hand.");
                        terminal.WriteLine("");

                        // Random reward
                        var reward = random.Next(3);
                        switch (reward)
                        {
                            case 0:
                                player.Strength += 5;
                                terminal.WriteLine("An ancient amulet! +5 Strength!");
                                break;
                            case 1:
                                player.Healing = player.MaxPotions;
                                terminal.WriteLine("Your potions are magically refilled!");
                                break;
                            case 2:
                                player.Experience += level * 300;
                                terminal.WriteLine($"+{level * 300} experience!");
                                break;
                        }

                        player.Chivalry += 25;
                        terminal.WriteLine("+25 Chivalry!");
                    }
                    else
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine("You have nothing to give.");
                    }
                    break;

                default:
                    terminal.SetColor("gray");
                    terminal.WriteLine("\"Go then. May fortune favor you.\"");
                    break;
            }

            await terminal.PressAnyKey();
        }

        /// <summary>
        /// Mysterious merchant with rare items
        /// </summary>
        private static async Task MysteriousMerchantEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("magenta");
            terminal.WriteLine("=== MYSTERIOUS MERCHANT ===");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("A cloaked figure stands before a floating carpet");
            terminal.WriteLine("covered with strange and wondrous items.");
            terminal.WriteLine("\"Ah, a customer! I have rare wares for you...\"");
            terminal.WriteLine("");

            int potionPrice = level * 50;
            int buffPrice = level * 200;
            int secretPrice = level * 500;

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("1");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("yellow");
            terminal.WriteLine($"Mega Healing Potion ({potionPrice}g) - Full heal");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("2");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("yellow");
            terminal.WriteLine($"Elixir of Power ({buffPrice}g) - +5 random stat");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("3");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("yellow");
            terminal.WriteLine($"Mystery Box ({secretPrice}g) - ???");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("4");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("yellow");
            terminal.WriteLine($"Information (100g)");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("L");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("yellow");
            terminal.WriteLine("Leave");
            terminal.WriteLine("");
            terminal.WriteLine($"Your gold: {player.Gold}", "cyan");
            terminal.WriteLine("");

            var choice = await terminal.GetInput("Your choice: ");

            switch (choice)
            {
                case "1":
                    if (player.Gold >= potionPrice)
                    {
                        player.Gold -= potionPrice;
                        player.HP = player.MaxHP;
                        terminal.SetColor("green");
                        terminal.WriteLine("You drink the mega potion. Full health restored!");
                    }
                    else
                    {
                        terminal.WriteLine("\"Not enough gold, friend.\"", "red");
                    }
                    break;

                case "2":
                    if (player.Gold >= buffPrice)
                    {
                        player.Gold -= buffPrice;
                        var stat = random.Next(6);
                        switch (stat)
                        {
                            case 0:
                                player.Strength += 5;
                                terminal.WriteLine("+5 Strength!", "green");
                                break;
                            case 1:
                                player.Intelligence += 5;
                                terminal.WriteLine("+5 Intelligence!", "green");
                                break;
                            case 2:
                                player.Wisdom += 5;
                                terminal.WriteLine("+5 Wisdom!", "green");
                                break;
                            case 3:
                                player.Dexterity += 5;
                                terminal.WriteLine("+5 Dexterity!", "green");
                                break;
                            case 4:
                                player.Constitution += 5;
                                terminal.WriteLine("+5 Constitution!", "green");
                                break;
                            case 5:
                                player.Charisma += 5;
                                terminal.WriteLine("+5 Charisma!", "green");
                                break;
                        }
                    }
                    else
                    {
                        terminal.WriteLine("\"Not enough gold, friend.\"", "red");
                    }
                    break;

                case "3":
                    if (player.Gold >= secretPrice)
                    {
                        player.Gold -= secretPrice;
                        terminal.SetColor("bright_magenta");
                        terminal.WriteLine("You open the mystery box...");
                        await Task.Delay(1500);

                        var mystery = random.Next(5);
                        switch (mystery)
                        {
                            case 0:
                                player.Gold += secretPrice * 3;
                                terminal.WriteLine($"JACKPOT! {secretPrice * 3} gold inside!", "bright_yellow");
                                break;
                            case 1:
                                player.Strength += 10;
                                player.Defence += 10;
                                terminal.WriteLine("Ancient power! +10 Strength, +10 Defence!", "bright_green");
                                break;
                            case 2:
                                terminal.WriteLine("Empty! The merchant cackles and vanishes!", "red");
                                break;
                            case 3:
                                player.Experience += level * 500;
                                terminal.WriteLine($"A tome of knowledge! +{level * 500} exp!", "cyan");
                                break;
                            case 4:
                                player.Healing = player.MaxPotions;
                                player.Mana = player.MaxMana;
                                terminal.WriteLine("Rare elixirs! Full potions and mana!", "green");
                                break;
                        }
                    }
                    else
                    {
                        terminal.WriteLine("\"Not enough gold, friend.\"", "red");
                    }
                    break;

                case "4":
                    if (player.Gold >= 100)
                    {
                        player.Gold -= 100;
                        terminal.SetColor("cyan");
                        terminal.WriteLine("\"The treasure room is guarded by mimics.\"");
                        terminal.WriteLine("\"The boss is weak to holy magic.\"");
                        terminal.WriteLine("\"There's a secret passage in the third hall.\"");
                    }
                    else
                    {
                        terminal.WriteLine("\"No gold, no info.\"", "red");
                    }
                    break;

                default:
                    terminal.SetColor("gray");
                    terminal.WriteLine("\"Come back when you have gold!\"");
                    break;
            }

            await terminal.PressAnyKey();
        }

        /// <summary>
        /// Time warp - strange temporal effects
        /// </summary>
        private static async Task TimeWarpEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("⌛ TIME WARP ⌛");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("The air shimmers and distorts...");
            terminal.WriteLine("You feel yourself pulled through time!");
            terminal.WriteLine("");

            await Task.Delay(2000);

            var warp = random.Next(5);
            switch (warp)
            {
                case 0:
                    terminal.SetColor("green");
                    terminal.WriteLine("You glimpse your future self!");
                    terminal.WriteLine("They hand you a bag of gold and wink.");
                    player.Gold += level * 1500;  // Increased from 500 for economic balance
                    terminal.WriteLine($"+{level * 1500} gold from your future self!");
                    break;

                case 1:
                    terminal.SetColor("yellow");
                    terminal.WriteLine("You witness a great battle of the past...");
                    terminal.WriteLine("The strategies you observe are enlightening.");
                    player.Experience += level * 300;
                    player.Strength += 2;
                    terminal.WriteLine($"+{level * 300} exp, +2 Strength!");
                    break;

                case 2:
                    terminal.SetColor("red");
                    terminal.WriteLine("You age rapidly for a moment...");
                    terminal.WriteLine("Then return to normal, but weakened.");
                    player.HP = player.HP / 2;
                    player.Experience = Math.Max(0, player.Experience - level * 100);
                    terminal.WriteLine("HP halved, some experience lost!");
                    break;

                case 3:
                    terminal.SetColor("bright_green");
                    terminal.WriteLine("You become younger for an instant!");
                    terminal.WriteLine("The vitality lingers...");
                    player.HP = player.MaxHP;
                    player.Mana = player.MaxMana;
                    player.Constitution += 3;
                    terminal.WriteLine("Full restore, +3 Constitution!");
                    break;

                case 4:
                    terminal.SetColor("magenta");
                    terminal.WriteLine("You see yourself dying in a possible future...");
                    terminal.WriteLine("A warning? The vision fades...");
                    terminal.WriteLine("You feel more cautious.");
                    player.Defence += 5;
                    terminal.WriteLine("+5 Defence!");
                    break;
            }

            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine("Time returns to normal...");

            await terminal.PressAnyKey();
        }

        /// <summary>
        /// Ancient library with knowledge
        /// </summary>
        private static async Task AncientLibraryEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("cyan");
            terminal.WriteLine("📚 ANCIENT LIBRARY 📚");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("Towering bookshelves stretch into darkness.");
            terminal.WriteLine("Dust motes dance in beams of ethereal light.");
            terminal.WriteLine("Knowledge from ages past awaits...");
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("1");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("yellow");
            terminal.WriteLine("Study combat techniques (+Strength)");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("2");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("yellow");
            terminal.WriteLine("Read arcane texts (+Intelligence/Mana)");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("3");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("yellow");
            terminal.WriteLine("Learn ancient history (+Experience)");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("4");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("yellow");
            terminal.WriteLine("Search for treasure maps (+Gold)");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("L");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("yellow");
            terminal.WriteLine("Leave");
            terminal.WriteLine("");

            var choice = await terminal.GetInput("Your choice: ");

            switch (choice)
            {
                case "1":
                    terminal.SetColor("green");
                    terminal.WriteLine("You study ancient fighting styles...");
                    player.Strength += 3;
                    player.Dexterity += 2;
                    terminal.WriteLine("+3 Strength, +2 Dexterity!");
                    break;

                case "2":
                    terminal.SetColor("bright_magenta");
                    terminal.WriteLine("The arcane texts glow as you read them...");
                    player.Intelligence += 3;
                    player.MaxMana += 10;
                    player.Mana = player.MaxMana;
                    terminal.WriteLine("+3 Intelligence, +10 Max Mana!");
                    break;

                case "3":
                    terminal.SetColor("yellow");
                    terminal.WriteLine("Hours pass as you absorb ancient wisdom...");
                    player.Experience += level * 400;
                    player.Wisdom += 2;
                    terminal.WriteLine($"+{level * 400} experience, +2 Wisdom!");
                    break;

                case "4":
                    if (random.NextDouble() < 0.6)
                    {
                        terminal.SetColor("bright_yellow");
                        terminal.WriteLine("You find a map with a treasure marked!");
                        long treasure = level * 600;
                        player.Gold += treasure;
                        terminal.WriteLine($"+{treasure} gold from following the map!");
                    }
                    else
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine("You find nothing but moth-eaten pages.");
                    }
                    break;

                default:
                    terminal.SetColor("gray");
                    terminal.WriteLine("You leave the library undisturbed.");
                    break;
            }

            await terminal.PressAnyKey();
        }

        /// <summary>
        /// Wishing well - gamble for wishes
        /// </summary>
        private static async Task WishingWellEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("=== WISHING WELL ===");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("A crystal-clear well glimmers with magic.");
            terminal.WriteLine("Ancient coins glitter at the bottom.");
            terminal.WriteLine("\"Make a wish,\" whispers the wind...");
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("T");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("yellow");
            terminal.WriteLine("Throw in 100 gold and make a wish");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("D");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("yellow");
            terminal.WriteLine("Dive in for the coins (risky!)");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("L");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("yellow");
            terminal.WriteLine("Leave");
            terminal.WriteLine("");

            var choice = await terminal.GetInput("Your choice: ");

            switch (choice.ToUpper())
            {
                case "T":
                    if (player.Gold >= 100)
                    {
                        player.Gold -= 100;
                        terminal.SetColor("cyan");
                        terminal.WriteLine("You throw a coin and close your eyes...");
                        await Task.Delay(1500);

                        var wish = random.Next(6);
                        switch (wish)
                        {
                            case 0:
                                terminal.SetColor("bright_green");
                                terminal.WriteLine("Your wish is granted!");
                                player.HP = player.MaxHP;
                                player.Mana = player.MaxMana;
                                terminal.WriteLine("Full health and mana restored!");
                                break;
                            case 1:
                                terminal.SetColor("bright_yellow");
                                terminal.WriteLine("Gold rains from the well!");
                                player.Gold += 500;
                                terminal.WriteLine("+500 gold!");
                                break;
                            case 2:
                                terminal.SetColor("green");
                                terminal.WriteLine("You feel stronger!");
                                player.Strength += 3;
                                terminal.WriteLine("+3 Strength!");
                                break;
                            case 3:
                                terminal.SetColor("gray");
                                terminal.WriteLine("...nothing happens.");
                                break;
                            case 4:
                                terminal.SetColor("bright_magenta");
                                terminal.WriteLine("Ancient wisdom fills your mind!");
                                player.Experience += level * 250;
                                terminal.WriteLine($"+{level * 250} experience!");
                                break;
                            case 5:
                                terminal.SetColor("red");
                                terminal.WriteLine("The well rejects your wish!");
                                terminal.WriteLine("Your coin is returned with interest?");
                                player.Gold += 150;
                                break;
                        }
                    }
                    else
                    {
                        terminal.WriteLine("You don't have enough gold.", "gray");
                    }
                    break;

                case "D":
                    terminal.SetColor("yellow");
                    terminal.WriteLine("You dive into the well!");
                    await Task.Delay(1000);

                    if (random.NextDouble() < 0.4)
                    {
                        long coins = level * 200 + random.Next(500);
                        player.Gold += coins;
                        terminal.SetColor("bright_yellow");
                        terminal.WriteLine($"You gather {coins} gold from the bottom!");
                        terminal.WriteLine("You climb out, dripping but rich!");
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine("The well spirits are ANGRY!");
                        int damage = (int)(player.MaxHP / 3);
                        player.HP -= damage;
                        terminal.WriteLine($"They attack you! -{damage} HP!");
                        terminal.WriteLine("You barely escape with your life!");
                    }
                    break;

                default:
                    terminal.SetColor("gray");
                    terminal.WriteLine("You walk away from the well.");
                    break;
            }

            await terminal.PressAnyKey();
        }

        /// <summary>
        /// Portal to arena - fight for glory (real interactive combat)
        /// </summary>
        private static async Task ArenaPortalEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("red");
            terminal.WriteLine("=== ARENA PORTAL ===");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("A swirling portal crackles with energy.");
            terminal.WriteLine("Through it, you see a gladiatorial arena!");
            terminal.WriteLine("A voice booms: \"ENTER AND PROVE YOUR WORTH!\"");
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("E");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("yellow");
            terminal.WriteLine("Enter the arena");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("L");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("yellow");
            terminal.WriteLine("Leave");
            terminal.WriteLine("");

            var choice = await terminal.GetInput("Your choice: ");

            if (choice.ToUpper() == "E")
            {
                terminal.SetColor("red");
                terminal.WriteLine("You step through the portal!");
                await Task.Delay(1000);
                terminal.WriteLine("The crowd roars as you enter the arena!");
                terminal.WriteLine("");

                // Generate an arena champion from the dungeon level's monster pool
                var monsters = MonsterGenerator.GenerateMonsterGroup(level);
                if (monsters.Count > 0)
                {
                    var champion = monsters[0];
                    champion.Name = $"Arena {champion.Name}";
                    champion.IsMiniBoss = true;
                    // Mini-boss bonuses applied by Monster class

                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine($"Your opponent: {champion.Name}!");
                    terminal.WriteLine("");
                    await Task.Delay(500);

                    // Real interactive combat
                    var combatEngine = new CombatEngine(terminal);
                    var result = await combatEngine.PlayerVsMonster(player, champion, offerMonkEncounter: false);

                    if (result.Outcome == CombatOutcome.Victory)
                    {
                        terminal.WriteLine("");
                        terminal.SetColor("bright_yellow");
                        terminal.WriteLine("The arena master approaches...");
                        terminal.WriteLine("\"Magnificent! A true gladiator!\"");
                        player.Chivalry += 25;
                        terminal.SetColor("green");
                        terminal.WriteLine("+25 Chivalry!");
                    }
                    else if (result.Outcome == CombatOutcome.PlayerDied)
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine("You are dragged from the arena...");
                        // Prevent permadeath from arena — leave at 1 HP
                        if (player.HP <= 0)
                            player.HP = 1;
                    }
                    else
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine("You retreat from the arena. The crowd boos.");
                    }
                }
                else
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine("The arena is empty today. No challenger appears.");
                }
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine("You step away from the portal.");
                terminal.WriteLine("The voice sighs: \"Coward...\"");
            }

            await terminal.PressAnyKey();
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // THEME-SPECIFIC ENCOUNTERS
        // ═══════════════════════════════════════════════════════════════════════════

        // CATACOMBS
        private static async Task BoneOracleEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("gray");
            terminal.WriteLine("=== THE BONE ORACLE ===");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("A skeleton sits cross-legged, bones arranged in patterns.");
            terminal.WriteLine("Its jaw clacks: \"Ask... and I shall answer...\"");
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("A");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Ask about your future");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("D");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Ask about the dungeon");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("L");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Leave");

            var choice = await terminal.GetInput("Your choice: ");

            if (choice.ToUpper() == "A")
            {
                terminal.SetColor("cyan");
                var prophecy = random.Next(4);
                switch (prophecy)
                {
                    case 0:
                        terminal.WriteLine("\"Great treasure awaits... but at great cost.\"");
                        player.Experience += level * 100;
                        break;
                    case 1:
                        terminal.WriteLine("\"Death walks beside you... but not for you. Not yet.\"");
                        player.Defence += 2;
                        break;
                    case 2:
                        terminal.WriteLine("\"The throne... I see you upon it... or beneath it.\"");
                        player.Chivalry += 25;
                        break;
                    case 3:
                        terminal.WriteLine("\"Beware the one who smiles. They are not your friend.\"");
                        player.Wisdom += 2;
                        break;
                }
            }
            else if (choice.ToUpper() == "D")
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("\"The boss room lies at the dungeon's heart.\"");
                terminal.WriteLine("\"It fears the light. Bring flames.\"");
                terminal.WriteLine("\"Treasure hides behind the third skull on the left.\"");
                player.Intelligence += 1;
            }

            await terminal.PressAnyKey();
        }

        private static async Task RestlessSpiritsEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_white");
            terminal.WriteLine("=== RESTLESS SPIRITS ===");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("Translucent figures drift through the walls.");
            terminal.WriteLine("They seem lost, confused, reaching out...");
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("H");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Help them find peace");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("A");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Attack the spirits");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("I");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Ignore them");

            var choice = await terminal.GetInput("Your choice: ");

            if (choice.ToUpper() == "H")
            {
                terminal.SetColor("bright_cyan");
                terminal.WriteLine("You speak words of comfort...");
                terminal.WriteLine("The spirits smile and fade into light.");
                terminal.WriteLine("\"Thank you...\" they whisper.");

                player.Chivalry += 50;
                player.Experience += level * 150;
                terminal.SetColor("green");
                terminal.WriteLine("+50 Chivalry, +" + (level * 150) + " experience!");
            }
            else if (choice.ToUpper() == "A")
            {
                terminal.SetColor("red");
                terminal.WriteLine("Your weapons pass through them!");
                terminal.WriteLine("They turn angry, clawing at you!");

                int damage = (int)(player.MaxHP / 4);
                player.HP -= damage;
                player.Darkness += 20;
                terminal.WriteLine($"-{damage} HP, +20 Darkness!");
            }

            await terminal.PressAnyKey();
        }

        private static async Task CryptKeeperEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("gray");
            terminal.WriteLine("🔑 THE CRYPT KEEPER 🔑");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("A hooded figure tends to the tombs.");
            terminal.WriteLine("\"Visitors are rare. What brings you?\"");
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("T");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Trade (buy potions)");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("I");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Information about the catacombs");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("L");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Leave");

            var choice = await terminal.GetInput("Your choice: ");

            if (choice.ToUpper() == "T")
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("\"I have... special potions.\"");
                if (player.Gold >= 150)
                {
                    player.Gold -= 150;
                    player.Healing = Math.Min(player.MaxPotions, player.Healing + 2);
                    terminal.SetColor("green");
                    terminal.WriteLine("Purchased 2 healing potions!");
                }
                else
                {
                    terminal.WriteLine("\"Not enough gold.\"");
                }
            }
            else if (choice.ToUpper() == "I")
            {
                terminal.SetColor("cyan");

                // Give dynamic, useful information based on current floor
                int[] sealFloors = { 15, 30, 45, 60, 80, 99 };
                int[] godFloors = { 25, 40, 55, 70, 85, 95, 100 };
                string[] godNames = { "Maelketh", "Veloura", "Thorgrim", "Noctura", "Aurelion", "Terravok", "Manwe" };

                // Find nearest seal floor above current level
                int nearestSeal = sealFloors.FirstOrDefault(f => f > level);
                // Find nearest god floor above current level
                int nearestGodIdx = -1;
                for (int i = 0; i < godFloors.Length; i++)
                {
                    if (godFloors[i] > level) { nearestGodIdx = i; break; }
                }

                var rng = new Random();
                var hints = new List<(string line1, string line2)>();

                if (nearestSeal > 0 && nearestSeal - level <= 15)
                {
                    hints.Add(($"\"I've felt ancient power sealed nearby... around floor {nearestSeal}.\"",
                               "\"Something waits to be claimed there.\""));
                }
                if (nearestGodIdx >= 0 && godFloors[nearestGodIdx] - level <= 20)
                {
                    hints.Add(($"\"A presence stirs below — {godNames[nearestGodIdx]} dwells near floor {godFloors[nearestGodIdx]}.\"",
                               "\"Tread carefully. The old ones do not forgive trespassers.\""));
                }
                if (level >= 10 && level <= 30)
                {
                    hints.Add(("\"The deeper you go, the richer the dead become.\"",
                               "\"But so do the things that guard them.\""));
                }
                if (level >= 40 && level <= 60)
                {
                    hints.Add(("\"These middle depths are treacherous. The monsters here have learned to hunt.\"",
                               $"\"A warrior of your caliber should be cautious below floor {level + 10}.\""));
                }
                if (level >= 70)
                {
                    hints.Add(("\"Few return from where you tread. The catacombs remember every soul they take.\"",
                               "\"The deepest floors hold secrets that predate the kingdom itself.\""));
                }

                // Always have at least one generic hint
                if (hints.Count == 0)
                {
                    hints.Add(("\"The dead whisper of treasure deeper below.\"",
                               "\"Explore carefully — not every room reveals its secrets at first glance.\""));
                }

                var hint = hints[rng.Next(hints.Count)];
                terminal.WriteLine(hint.line1);
                terminal.WriteLine(hint.line2);
                player.Experience += level * 50;
            }

            await terminal.PressAnyKey();
        }

        private static async Task AncientTombEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("⚰ ANCIENT TOMB ⚰");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("An ornate sarcophagus dominates the room.");
            terminal.WriteLine("Gold and jewels are scattered around it.");
            terminal.WriteLine("Warnings in ancient text cover the walls...");
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("O");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Open the sarcophagus");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("T");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Take only the loose treasure");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("L");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Leave it all");

            var choice = await terminal.GetInput("Your choice: ");

            if (choice.ToUpper() == "O")
            {
                terminal.SetColor("red");
                terminal.WriteLine("You push open the heavy lid...");
                await Task.Delay(1500);

                if (random.NextDouble() < 0.5)
                {
                    terminal.WriteLine("A mummy BURSTS OUT!");
                    int damage = (int)(player.MaxHP / 3);
                    player.HP -= damage;
                    terminal.WriteLine($"-{damage} HP!");
                    terminal.SetColor("yellow");
                    terminal.WriteLine("But you find ancient treasures within!");
                    player.Gold += level * 1500;  // Increased from 500 for economic balance
                    terminal.WriteLine($"+{level * 1500} gold!");
                }
                else
                {
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine("The occupant has long since turned to dust.");
                    terminal.WriteLine("But their treasures remain!");
                    player.Gold += level * 2400;  // Increased from 800 for economic balance
                    player.Strength += 2;
                    terminal.WriteLine($"+{level * 2400} gold, +2 Strength!");
                }
            }
            else if (choice.ToUpper() == "T")
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("You carefully gather the loose coins and gems.");
                player.Gold += level * 600;  // Increased from 200 for economic balance
                terminal.WriteLine($"+{level * 600} gold!");
            }

            await terminal.PressAnyKey();
        }

        // SEWERS
        private static async Task RatKingEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("gray");
            terminal.WriteLine("🐀 THE RAT KING 🐀");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("Thousands of rats form a writhing mass...");
            terminal.WriteLine("At the center, one massive rat wears a tiny crown.");
            terminal.WriteLine("It speaks with a thousand voices: \"TRIBUTE!\"");
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("P");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Pay tribute (500 gold)");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("F");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Fight the swarm");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("R");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Run!");

            var choice = await terminal.GetInput("Your choice: ");

            if (choice.ToUpper() == "P" && player.Gold >= 500)
            {
                player.Gold -= 500;
                terminal.SetColor("yellow");
                terminal.WriteLine("The rats accept your offering.");
                terminal.WriteLine("\"YOU MAY PASS, SURFACE DWELLER.\"");
                terminal.WriteLine("They part, revealing a hidden treasure cache!");
                player.Gold += level * 900;  // Increased from 300 for economic balance
                terminal.SetColor("green");
                terminal.WriteLine($"+{level * 900} gold found!");
            }
            else if (choice.ToUpper() == "F")
            {
                terminal.SetColor("red");
                terminal.WriteLine("You fight through the endless swarm!");
                int damage = level * 5;
                player.HP -= damage;
                terminal.WriteLine($"-{damage} HP from countless bites!");

                if (random.NextDouble() < 0.5)
                {
                    terminal.SetColor("green");
                    terminal.WriteLine("You slay the Rat King!");
                    player.Gold += level * 1200;  // Increased from 400 for economic balance
                    player.Experience += level * 200;
                }
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine("You flee the chittering horde!");
            }

            await terminal.PressAnyKey();
        }

        private static async Task LostChildEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("cyan");
            terminal.WriteLine("👦 LOST CHILD 👦");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("A small child huddles in the corner, crying.");
            terminal.WriteLine("\"I... I got lost... please help me...\"");
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("H");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Help the child find the exit");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("I");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Ignore them");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("C");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Check if it's a trap");

            var choice = await terminal.GetInput("Your choice: ");

            if (choice.ToUpper() == "H")
            {
                terminal.SetColor("green");
                terminal.WriteLine("You take the child's hand and guide them to safety.");
                terminal.WriteLine("\"Thank you! My family will reward you!\"");

                player.Gold += level * 600;  // Increased from 200 for economic balance
                player.Chivalry += 100;
                terminal.WriteLine($"+{level * 600} gold, +100 Chivalry!");
            }
            else if (choice.ToUpper() == "C")
            {
                if (random.NextDouble() < 0.3)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine("The 'child' grins with too many teeth!");
                    terminal.WriteLine("It's a changeling! It attacks!");
                    int damage = (int)(player.MaxHP / 5);
                    player.HP -= damage;
                    terminal.WriteLine($"-{damage} HP!");
                }
                else
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine("Just a scared kid. You help them anyway.");
                    player.Chivalry += 50;
                }
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine("You walk past the crying child.");
                player.Darkness += 10;
            }

            await terminal.PressAnyKey();
        }

        private static async Task AlchemistLabEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_green");
            terminal.WriteLine("⚗ ABANDONED ALCHEMY LAB ⚗");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("Bubbling vials and dusty tomes fill this space.");
            terminal.WriteLine("Whoever worked here left in a hurry...");
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("D");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Drink a random potion");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("R");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Read the notes");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("S");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Steal valuable supplies");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("L");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Leave");

            var choice = await terminal.GetInput("Your choice: ");

            if (choice.ToUpper() == "D")
            {
                terminal.SetColor("bright_magenta");
                terminal.WriteLine("You drink a bubbling green liquid...");
                await Task.Delay(1500);

                var effect = random.Next(5);
                switch (effect)
                {
                    case 0:
                        player.Strength += 5;
                        terminal.SetColor("green");
                        terminal.WriteLine("POWER! +5 Strength!");
                        break;
                    case 1:
                        player.HP = player.MaxHP;
                        terminal.SetColor("green");
                        terminal.WriteLine("Full health restored!");
                        break;
                    case 2:
                        terminal.SetColor("red");
                        terminal.WriteLine("POISON! You feel terrible!");
                        player.Poison += 3;
                        break;
                    case 3:
                        player.Intelligence += 5;
                        terminal.SetColor("cyan");
                        terminal.WriteLine("Enlightenment! +5 Intelligence!");
                        break;
                    case 4:
                        terminal.SetColor("yellow");
                        terminal.WriteLine("You turn briefly invisible. Neat!");
                        player.Dexterity += 3;
                        break;
                }
            }
            else if (choice.ToUpper() == "R")
            {
                terminal.SetColor("cyan");
                terminal.WriteLine("The notes describe potion recipes...");
                player.Intelligence += 2;
                player.Experience += level * 100;
                terminal.WriteLine("+2 Intelligence, +" + (level * 100) + " exp!");
            }
            else if (choice.ToUpper() == "S")
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("You grab valuable alchemical supplies!");
                player.Gold += level * 900;  // Increased from 300 for economic balance
                player.Healing = Math.Min(player.MaxPotions, player.Healing + 2);
                terminal.WriteLine($"+{level * 900} gold, +2 potions!");
            }

            await terminal.PressAnyKey();
        }

        private static async Task TreasureHoardEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("=== TREASURE HOARD ===");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("Coins and gems are piled high!");
            terminal.WriteLine("This must be a thieves' guild cache...");
            terminal.WriteLine("But is anyone guarding it?");
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("T");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Take it all!");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("S");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Take some carefully");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("L");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Leave (it's a trap)");

            var choice = await terminal.GetInput("Your choice: ");

            if (choice.ToUpper() == "T")
            {
                if (random.NextDouble() < 0.4)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine("TRAP! Thieves emerge from the shadows!");
                    int damage = (int)(player.MaxHP / 4);
                    player.HP -= damage;
                    terminal.WriteLine($"-{damage} HP!");
                    terminal.SetColor("yellow");
                    terminal.WriteLine("But you grab what you can!");
                    player.Gold += level * 900;  // Increased from 300 for economic balance
                }
                else
                {
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine("Jackpot! You take everything!");
                    player.Gold += level * 2100;  // Increased from 700 for economic balance
                    terminal.WriteLine($"+{level * 2100} gold!");
                }
            }
            else if (choice.ToUpper() == "S")
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("You carefully pocket some valuables.");
                player.Gold += level * 600;  // Increased from 200 for economic balance
                terminal.WriteLine($"+{level * 600} gold!");
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine("Your caution was wise. Or paranoid.");
            }

            await terminal.PressAnyKey();
        }

        // Quick stub implementations for remaining themed encounters
        // (These can be expanded later)

        private static async Task CrystalCaveEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("=== CRYSTAL CAVE ===");
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine("Massive crystals hum with power.");
            terminal.WriteLine("Touching one might grant power... or pain.");

            if (random.NextDouble() < 0.6)
            {
                player.Mana = player.MaxMana;
                player.Intelligence += 3;
                terminal.SetColor("green");
                terminal.WriteLine("The crystal's energy flows into you!");
                terminal.WriteLine("Full mana restored, +3 Intelligence!");
            }
            else
            {
                player.HP -= player.MaxHP / 5;
                terminal.SetColor("red");
                terminal.WriteLine("The crystal shocks you!");
            }
            await terminal.PressAnyKey();
        }

        private static async Task DragonHoardEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("red");
            terminal.WriteLine("=== DRAGON'S HOARD ===");
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine("Gold piled higher than you've ever seen!");
            terminal.WriteLine("The dragon seems to be away...");

            player.Gold += level * 3000;  // Increased from 1000 for economic balance
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"You grab {level * 1000} gold and RUN!");
            await terminal.PressAnyKey();
        }

        private static async Task DwarvenOutpostEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("yellow");
            terminal.WriteLine("⛏ DWARVEN OUTPOST ⛏");
            terminal.WriteLine("");
            terminal.WriteLine("A small dwarven trading post still operates here.", "white");
            terminal.WriteLine("\"Welcome, surfacer! Need anything?\"");

            player.Healing = Math.Min(player.MaxPotions, player.Healing + 3);
            terminal.SetColor("green");
            terminal.WriteLine("The dwarves share their healing supplies!");
            terminal.WriteLine("+3 healing potions!");
            await terminal.PressAnyKey();
        }

        private static async Task UndergroundLakeEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("blue");
            terminal.WriteLine("🌊 UNDERGROUND LAKE 🌊");
            terminal.WriteLine("");
            terminal.WriteLine("A serene lake glows with bioluminescence.", "white");
            terminal.WriteLine("The water looks... magical.");

            player.HP = player.MaxHP;
            player.Mana = player.MaxMana;
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("You drink from the lake. Full restoration!");
            await terminal.PressAnyKey();
        }

        private static async Task AncientGolemEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("gray");
            terminal.WriteLine("🗿 ANCIENT GOLEM 🗿");
            terminal.WriteLine("");
            terminal.WriteLine("A stone guardian blocks your path.", "white");
            terminal.WriteLine("\"Answer the riddle or face destruction.\"");
            terminal.WriteLine("");
            terminal.WriteLine("\"What has keys but no locks?\"");

            var answer = await terminal.GetInput("Your answer: ");
            if (answer.ToLower().Contains("piano") || answer.ToLower().Contains("keyboard"))
            {
                terminal.SetColor("green");
                terminal.WriteLine("\"Correct. You may pass.\"");
                player.Experience += level * 200;
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine("\"Wrong.\" The golem attacks!");
                player.HP -= player.MaxHP / 4;
            }
            await terminal.PressAnyKey();
        }

        private static async Task TimeCapsuleEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("cyan");
            terminal.WriteLine("📦 TIME CAPSULE 📦");
            terminal.WriteLine("");
            terminal.WriteLine("A sealed container from an ancient civilization.", "white");
            terminal.WriteLine("Inside: treasures from another age!");

            player.Gold += level * 1200;  // Increased from 400 for economic balance
            player.Experience += level * 150;
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"+{level * 400} gold, +{level * 150} exp!");
            await terminal.PressAnyKey();
        }

        private static async Task MagicFountainEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("⛲ MAGIC FOUNTAIN ⛲");
            terminal.WriteLine("");
            terminal.WriteLine("Pure magical water flows from ancient stone.", "white");

            player.HP = player.MaxHP;
            player.Mana = player.MaxMana;
            player.Poison = 0;
            terminal.SetColor("green");
            terminal.WriteLine("You drink deeply. All ailments cured!");
            await terminal.PressAnyKey();
        }

        private static async Task LostCivilizationEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("yellow");
            terminal.WriteLine("🏛 LOST CIVILIZATION 🏛");
            terminal.WriteLine("");
            terminal.WriteLine("Remnants of a great society surround you.", "white");
            terminal.WriteLine("Their technology was far beyond our own...");

            player.Intelligence += 5;
            player.Experience += level * 300;
            terminal.SetColor("cyan");
            terminal.WriteLine("You study their writings. +5 Int, +" + (level * 300) + " exp!");
            await terminal.PressAnyKey();
        }

        // Demon Lair encounters
        private static async Task DemonBargainEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("red");
            terminal.WriteLine("👿 DEMON BARGAIN 👿");
            terminal.WriteLine("");
            terminal.WriteLine("A demon offers you power... for a price.", "white");
            terminal.WriteLine("\"Your soul is valuable. Let's make a deal.\"");
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("A");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Accept (power but +Darkness)");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("R");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Refuse");

            var choice = await terminal.GetInput("Your choice: ");
            if (choice.ToUpper() == "A")
            {
                player.Strength += 10;
                player.Darkness += 100;
                terminal.SetColor("red");
                terminal.WriteLine("+10 Strength, but +100 Darkness!");
            }
            else
            {
                terminal.SetColor("green");
                terminal.WriteLine("The demon hisses and vanishes.");
            }
            await terminal.PressAnyKey();
        }

        private static async Task TorturedSoulsEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("red");
            terminal.WriteLine("😱 TORTURED SOULS 😱");
            terminal.WriteLine("");
            terminal.WriteLine("Souls in chains beg for release.", "white");

            player.Chivalry += 30;
            player.Experience += level * 100;
            terminal.WriteLine("You free them. +30 Chivalry!", "green");
            await terminal.PressAnyKey();
        }

        private static async Task InfernalForgeEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_red");
            terminal.WriteLine("🔥 INFERNAL FORGE 🔥");
            terminal.WriteLine("");
            terminal.WriteLine("You stumble upon the smoldering remains of a", "white");
            terminal.WriteLine("demonic forge, its hellfire still burning bright.", "white");
            terminal.WriteLine("An anvil pulses with infernal energy. You hold", "white");
            terminal.WriteLine("your weapon to the flames and feel it grow stronger.", "white");
            terminal.WriteLine("");

            player.BonusWeapPow += 5;
            player.RecalculateStats();
            terminal.SetColor("yellow");
            terminal.WriteLine("Your weapon has been permanently tempered! +5 Weapon Power!");
            await terminal.PressAnyKey();
        }

        private static async Task SuccubusEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("magenta");
            terminal.WriteLine("💋 SUCCUBUS 💋");
            terminal.WriteLine("");
            terminal.WriteLine("A beautiful demon appears...", "white");
            terminal.WriteLine("\"Hello, handsome. Lonely in these depths?\"");
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("R");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Resist her charms");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("S");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Succumb...");

            var choice = await terminal.GetInput("Your choice: ");
            if (choice.ToUpper() == "R")
            {
                player.Wisdom += 5;
                terminal.SetColor("green");
                terminal.WriteLine("Your willpower impresses her. +5 Wisdom!");
            }
            else
            {
                player.HP -= player.MaxHP / 3;
                player.Gold = player.Gold * 8 / 10;
                terminal.SetColor("red");
                terminal.WriteLine("She drains your life and steals your gold!");
            }
            await terminal.PressAnyKey();
        }

        // Frozen Depths encounters
        private static async Task FrozenAdventurerEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("🧊 FROZEN ADVENTURER 🧊");
            terminal.WriteLine("");
            terminal.WriteLine("An adventurer is frozen solid in ice.", "white");
            terminal.WriteLine("Their equipment looks valuable...");

            player.Gold += level * 900;  // Increased from 300 for economic balance
            player.Healing = Math.Min(player.MaxPotions, player.Healing + 2);
            terminal.SetColor("yellow");
            terminal.WriteLine("You take their supplies. They won't need them.");
            await terminal.PressAnyKey();
        }

        private static async Task IceQueenEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_white");
            terminal.WriteLine("*** THE ICE QUEEN ***");
            terminal.WriteLine("");
            terminal.WriteLine("A beautiful but cold figure sits on a throne of ice.", "white");
            terminal.WriteLine("\"Bow before me, mortal.\"");

            player.Charisma += 3;
            player.Mana = player.MaxMana;
            terminal.SetColor("cyan");
            terminal.WriteLine("She grants you her blessing. +3 Charisma, full mana!");
            await terminal.PressAnyKey();
        }

        private static async Task YetiDenEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("white");
            terminal.WriteLine("🐻 YETI DEN 🐻");
            terminal.WriteLine("");
            terminal.WriteLine("A family of yetis! The babies are cute.", "white");
            terminal.WriteLine("The parents are NOT happy to see you.");

            if (random.NextDouble() < 0.5)
            {
                player.HP -= player.MaxHP / 4;
                terminal.SetColor("red");
                terminal.WriteLine("You're mauled! But escape with treasure.");
                player.Gold += level * 1500;  // Increased from 500 for economic balance
            }
            else
            {
                terminal.SetColor("green");
                terminal.WriteLine("You back away slowly. They let you go.");
            }
            await terminal.PressAnyKey();
        }

        private static async Task AuroraVisionEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("=== AURORA VISION ===");
            terminal.WriteLine("");
            terminal.WriteLine("The northern lights dance even underground here.", "white");
            terminal.WriteLine("You feel at peace...");

            player.HP = player.MaxHP;
            player.Mana = player.MaxMana;
            player.Wisdom += 3;
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("Full restoration, +3 Wisdom!");
            await terminal.PressAnyKey();
        }

        // Volcanic Pit encounters
        private static async Task FireElementalEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_red");
            terminal.WriteLine("🔥 FIRE ELEMENTAL 🔥");
            terminal.WriteLine("");
            terminal.WriteLine("A being of pure flame blocks your path.", "white");
            terminal.WriteLine("It offers you a gift of fire magic.");

            player.Intelligence += 3;
            player.MaxMana += 20;
            terminal.SetColor("yellow");
            terminal.WriteLine("+3 Intelligence, +20 Max Mana!");
            await terminal.PressAnyKey();
        }

        private static async Task LavaBoatEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("red");
            terminal.WriteLine("🚣 LAVA BOAT 🚣");
            terminal.WriteLine("");
            terminal.WriteLine("A fireproof boat sits by a river of lava.", "white");
            terminal.WriteLine("\"500 gold to cross,\" says the boatman.");

            if (player.Gold >= 500)
            {
                player.Gold -= 500;
                player.Experience += level * 300;
                terminal.SetColor("yellow");
                terminal.WriteLine("You cross to find treasure! +" + (level * 300) + " exp!");
            }
            else
            {
                terminal.WriteLine("You can't afford the crossing.", "gray");
            }
            await terminal.PressAnyKey();
        }

        private static async Task PhoenixNestEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("🦅 PHOENIX NEST 🦅");
            terminal.WriteLine("");
            terminal.WriteLine("A phoenix feather lies in an empty nest.", "white");
            terminal.WriteLine("Such a feather has great power...");

            player.HP = player.MaxHP;
            player.Constitution += 5;
            terminal.SetColor("bright_red");
            terminal.WriteLine("The feather restores you! Full HP, +5 Constitution!");
            await terminal.PressAnyKey();
        }

        private static async Task ObsidianMirrorEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("gray");
            terminal.WriteLine("🪞 OBSIDIAN MIRROR 🪞");
            terminal.WriteLine("");
            terminal.WriteLine("A mirror of volcanic glass shows your reflection.", "white");
            terminal.WriteLine("But something is different about it...");

            player.Strength += 2;
            player.Intelligence += 2;
            player.Dexterity += 2;
            terminal.SetColor("cyan");
            terminal.WriteLine("You learn from your shadow self. +2 to Str/Int/Dex!");
            await terminal.PressAnyKey();
        }

        // Abyssal Void encounters
        private static async Task VoidWhisperEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("magenta");
            terminal.WriteLine("🌀 VOID WHISPERS 🌀");
            terminal.WriteLine("");
            terminal.WriteLine("Voices from beyond space whisper secrets...", "white");

            player.Intelligence += 5;
            player.Wisdom += 5;
            player.Darkness += 30;
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("Forbidden knowledge! +5 Int, +5 Wis, +30 Darkness!");
            await terminal.PressAnyKey();
        }

        private static async Task RealityTearEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_white");
            terminal.WriteLine("⚡ REALITY TEAR ⚡");
            terminal.WriteLine("");
            terminal.WriteLine("A crack in reality shows another world.", "white");
            terminal.WriteLine("Reaching through might be dangerous...");

            if (random.NextDouble() < 0.5)
            {
                player.Gold += level * 3000;  // Increased from 1000 for economic balance
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("You grab treasure from another dimension!");
            }
            else
            {
                player.HP -= player.MaxHP / 3;
                terminal.SetColor("red");
                terminal.WriteLine("Something grabs you back!");
            }
            await terminal.PressAnyKey();
        }

        private static async Task CosmicEntityEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("👁 COSMIC ENTITY 👁");
            terminal.WriteLine("");
            terminal.WriteLine("Something vast and incomprehensible notices you.", "white");
            terminal.WriteLine("Its attention is... uncomfortable.");

            player.Experience += level * 500;
            player.Wisdom += 10;
            terminal.SetColor("cyan");
            terminal.WriteLine("You survive the experience with new wisdom!");
            await terminal.PressAnyKey();
        }

        private static async Task MadnessPoolEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("red");
            terminal.WriteLine("🌊 POOL OF MADNESS 🌊");
            terminal.WriteLine("");
            terminal.WriteLine("A pool of swirling, impossible colors.", "white");
            terminal.WriteLine("Looking into it is... tempting.");
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("L");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Look into the pool");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("A");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Avoid it");

            var choice = await terminal.GetInput("Your choice: ");
            if (choice.ToUpper() == "L")
            {
                if (random.NextDouble() < 0.5)
                {
                    player.Intelligence += 10;
                    player.Darkness += 50;
                    terminal.SetColor("magenta");
                    terminal.WriteLine("ENLIGHTENMENT! +10 Int, +50 Darkness!");
                }
                else
                {
                    player.Intelligence = Math.Max(1, player.Intelligence - 5);
                    terminal.SetColor("red");
                    terminal.WriteLine("MADNESS! -5 Intelligence!");
                }
            }
            else
            {
                terminal.SetColor("green");
                terminal.WriteLine("Wise choice. Some things are best left unseen.");
            }
            await terminal.PressAnyKey();
        }
    }
}
