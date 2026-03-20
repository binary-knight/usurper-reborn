using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UsurperRemake.UI;
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
            if (!GameConfig.ScreenReaderMode)
            {
                terminal.WriteLine("╔═══════════════════════════════════════════════════════╗");
                terminal.WriteLine("║            * " + Loc.Get("encounter.tavern.title") + " *                   ║");
                terminal.WriteLine("║              " + Loc.Get("encounter.tavern.subtitle") + "                          ║");
                terminal.WriteLine("╚═══════════════════════════════════════════════════════╝");
            }
            else
            {
                terminal.WriteLine(Loc.Get("encounter.tavern.title") + " - " + Loc.Get("encounter.tavern.subtitle"));
            }
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.tavern.desc_1"));
            terminal.WriteLine(Loc.Get("encounter.tavern.desc_2"));
            terminal.WriteLine(Loc.Get("encounter.tavern.desc_3"));
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("encounter.tavern.bartender_greet"));
            terminal.WriteLine(Loc.Get("encounter.tavern.bartender_ask"));
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("D");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.tavern.option_drink"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("G");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.tavern.option_gamble"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("T");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.tavern.option_talk"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("R");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.tavern.option_rest"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("L");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.option_leave"));
            terminal.WriteLine("");

            bool inTavern = true;
            int strangerTalks = 0;
            const int maxStrangerTalks = 1; // One talk per tavern visit

            while (inTavern)
            {
                var choice = await terminal.GetInput(Loc.Get("ui.your_choice"));
                switch (choice.ToUpper())
                {
                    case "D":
                        if (player.Gold >= 50)
                        {
                            player.Gold -= 50;
                            long heal = player.MaxHP / 3;
                            player.HP = Math.Min(player.MaxHP, player.HP + heal);
                            terminal.SetColor("green");
                            terminal.WriteLine(Loc.Get("encounter.tavern.ale_heal", heal));
                            terminal.WriteLine($"{Loc.Get("combat.bar_hp")}: {player.HP}/{player.MaxHP}");
                        }
                        else
                        {
                            terminal.WriteLine(Loc.Get("encounter.tavern.no_coin"), "red");
                        }
                        break;

                    case "G":
                        await TavernGambling(terminal, player);
                        break;

                    case "T":
                        if (player.TavernStrangerTalkedToday)
                        {
                            terminal.SetColor("gray");
                            terminal.WriteLine("The stranger waves you off. \"We've talked enough for today. Come back tomorrow.\"");
                        }
                        else
                        {
                            await TavernStranger(terminal, player, level);
                            player.TavernStrangerTalkedToday = true;
                        }
                        break;

                    case "R":
                        if (player.Gold >= 200)
                        {
                            player.Gold -= 200;
                            player.HP = player.MaxHP;
                            if (player.Poison > 0) { player.Poison = 0; player.PoisonTurns = 0; }
                            terminal.SetColor("bright_green");
                            terminal.WriteLine(Loc.Get("encounter.tavern.rest_sleep"));
                            terminal.WriteLine(Loc.Get("encounter.tavern.rest_refreshed"));
                            terminal.WriteLine($"{Loc.Get("combat.bar_hp")}: {player.HP}/{player.MaxHP}");
                        }
                        else
                        {
                            terminal.WriteLine(Loc.Get("encounter.tavern.rest_no_gold"), "red");
                        }
                        break;

                    case "L":
                        inTavern = false;
                        terminal.WriteLine(Loc.Get("encounter.tavern.leave_msg"), "gray");
                        break;
                }

                if (inTavern)
                {
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("encounter.tavern.shortcut_menu"), "bright_yellow");
                }
            }

            await terminal.PressAnyKey();
        }

        private static async Task TavernGambling(TerminalEmulator terminal, Character player)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("encounter.tavern.gamble_intro_1"));
            terminal.WriteLine(Loc.Get("encounter.tavern.gamble_intro_2"));
            terminal.WriteLine("");

            long bet = Math.Min(player.Gold / 4, 500);
            if (bet < 10)
            {
                terminal.WriteLine(Loc.Get("encounter.tavern.gamble_nothing"), "gray");
                return;
            }

            terminal.Write(Loc.Get("encounter.tavern.gamble_bet_prompt", bet), "white");
            var choice = await terminal.GetInput("");

            if (choice.ToUpper() == "Y")
            {
                player.Gold -= bet;
                await Task.Delay(1000);

                int playerCard = random.Next(1, 14);
                int dealerCard = random.Next(1, 14);

                string[] cardNames = { "", Loc.Get("encounter.tavern.card_ace"), "2", "3", "4", "5", "6", "7", "8", "9", "10", Loc.Get("encounter.tavern.card_jack"), Loc.Get("encounter.tavern.card_queen"), Loc.Get("encounter.tavern.card_king") };

                terminal.WriteLine(Loc.Get("encounter.tavern.gamble_you_draw", cardNames[playerCard]), "cyan");
                terminal.WriteLine(Loc.Get("encounter.tavern.gamble_dealer_draw", cardNames[dealerCard]), "red");

                if (playerCard > dealerCard)
                {
                    player.Gold += bet * 2;
                    terminal.SetColor("bright_green");
                    terminal.WriteLine(Loc.Get("encounter.tavern.gamble_win", bet * 2));
                }
                else if (playerCard < dealerCard)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("encounter.tavern.gamble_lose"));
                }
                else
                {
                    player.Gold += bet;
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("encounter.tavern.gamble_tie"));
                }
            }
        }

        private static async Task TavernStranger(TerminalEmulator terminal, Character player, int level)
        {
            terminal.SetColor("magenta");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("encounter.tavern.stranger_intro_1"));
            terminal.WriteLine(Loc.Get("encounter.tavern.stranger_intro_2"));
            terminal.WriteLine("");

            var strangerType = random.Next(5);
            switch (strangerType)
            {
                case 0:
                    terminal.SetColor("cyan");
                    terminal.WriteLine(Loc.Get("encounter.tavern.stranger_potion_1"));
                    terminal.WriteLine(Loc.Get("encounter.tavern.stranger_potion_2"));
                    player.Healing = Math.Min(player.MaxPotions, player.Healing + 3);
                    terminal.SetColor("green");
                    terminal.WriteLine(Loc.Get("encounter.tavern.stranger_potion_reward"));
                    break;

                case 1:
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("encounter.tavern.stranger_map"));
                    long expGain = level * 100;
                    player.Experience += expGain;
                    terminal.SetColor("green");
                    terminal.WriteLine(Loc.Get("encounter.tavern.stranger_map_reward", expGain));
                    break;

                case 2:
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("encounter.tavern.stranger_tip"));
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("encounter.tavern.stranger_tip_nod"));
                    break;

                case 3:
                    terminal.SetColor("bright_white");
                    terminal.WriteLine(Loc.Get("encounter.tavern.stranger_deal_1"));
                    terminal.WriteLine(Loc.Get("encounter.tavern.stranger_deal_2"));
                    terminal.WriteLine(Loc.Get("encounter.tavern.stranger_deal_3"));
                    terminal.WriteLine("");
                    terminal.Write(Loc.Get("encounter.tavern.stranger_deal_prompt"), "white");
                    var accept = await terminal.GetInput("");
                    if (accept.ToUpper() == "Y")
                    {
                        player.Strength += 5;
                        player.Defence += 5;
                        terminal.SetColor("magenta");
                        terminal.WriteLine(Loc.Get("encounter.tavern.stranger_deal_accept"));
                        terminal.WriteLine(Loc.Get("encounter.tavern.stranger_deal_stats"));
                    }
                    else
                    {
                        terminal.WriteLine(Loc.Get("encounter.tavern.stranger_deal_refuse"), "gray");
                    }
                    break;

                case 4:
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("encounter.tavern.stranger_drunk_1"));
                    terminal.WriteLine(Loc.Get("encounter.tavern.stranger_drunk_2"));
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
            terminal.WriteLine(GameConfig.ScreenReaderMode ? Loc.Get("encounter.minstrel.title") : "♪♫ " + Loc.Get("encounter.minstrel.title") + " ♫♪");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.minstrel.desc_1"));
            terminal.WriteLine(Loc.Get("encounter.minstrel.desc_2"));
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("1");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("encounter.minstrel.song_valor"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("2");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("encounter.minstrel.song_warding"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("3");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("encounter.minstrel.song_healing"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("4");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("encounter.minstrel.song_ballad"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("5");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("encounter.minstrel.song_listen"));
            terminal.WriteLine("");

            var choice = await terminal.GetInput(Loc.Get("ui.your_choice"));

            switch (choice)
            {
                case "1":
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("encounter.minstrel.valor_play"));
                    terminal.WriteLine(Loc.Get("encounter.minstrel.valor_effect"));
                    // TODO: Add temporary buff system
                    player.Strength += 3; // Permanent for now
                    terminal.WriteLine(Loc.Get("encounter.minstrel.plus_strength"), "green");
                    break;

                case "2":
                    terminal.SetColor("blue");
                    terminal.WriteLine(Loc.Get("encounter.minstrel.warding_play"));
                    player.Defence += 3;
                    terminal.WriteLine(Loc.Get("encounter.minstrel.plus_defence"), "green");
                    break;

                case "3":
                    terminal.SetColor("green");
                    terminal.WriteLine(Loc.Get("encounter.minstrel.healing_play"));
                    player.HP = Math.Min(player.MaxHP, player.HP + player.MaxHP / 2);
                    terminal.WriteLine(Loc.Get("encounter.minstrel.hp_restored", player.HP, player.MaxHP));
                    break;

                case "4":
                    if (player.Gold >= 100)
                    {
                        player.Gold -= 100;
                        player.Chivalry += 50;
                        terminal.SetColor("bright_yellow");
                        terminal.WriteLine(Loc.Get("encounter.minstrel.ballad_title", player.DisplayName));
                        terminal.WriteLine(Loc.Get("encounter.minstrel.ballad_compose"));
                        terminal.WriteLine(Loc.Get("encounter.minstrel.ballad_fame"));
                    }
                    else
                    {
                        terminal.WriteLine(Loc.Get("encounter.minstrel.ballad_no_gold"), "gray");
                    }
                    break;

                default:
                    long listenXP = level * 10;
                    player.Experience += listenXP;
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("encounter.minstrel.listen_leave"));
                    terminal.SetColor("cyan");
                    terminal.WriteLine($"  (+{listenXP} XP)");
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
            terminal.WriteLine("✧ " + Loc.Get("encounter.fairy.title") + " ✧");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.fairy.desc_1"));
            terminal.WriteLine(Loc.Get("encounter.fairy.desc_2"));
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("encounter.fairy.desc_3"));
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("A");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.fairy.option_blessing"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("S");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.fairy.option_steal"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("D");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.fairy.option_dance"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("L");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.fairy.option_leave"));
            terminal.WriteLine("");

            var choice = await terminal.GetInput(Loc.Get("ui.your_choice"));

            switch (choice.ToUpper())
            {
                case "A":
                    if (random.NextDouble() < 0.7) // 70% good outcome
                    {
                        terminal.SetColor("bright_green");
                        terminal.WriteLine(Loc.Get("encounter.fairy.blessing_good"));
                        var blessing = random.Next(4);
                        switch (blessing)
                        {
                            case 0:
                                player.HP = player.MaxHP;
                                terminal.WriteLine(Loc.Get("encounter.fairy.blessing_hp"));
                                break;
                            case 1:
                                player.Mana = player.MaxMana;
                                terminal.WriteLine(Loc.Get("encounter.fairy.blessing_mana"));
                                break;
                            case 2:
                                player.Healing = player.MaxPotions;
                                terminal.WriteLine(Loc.Get("encounter.fairy.blessing_potions"));
                                break;
                            case 3:
                                player.Experience += level * 200;
                                terminal.WriteLine(Loc.Get("encounter.fairy.blessing_exp", level * 200));
                                break;
                        }
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine(Loc.Get("encounter.fairy.blessing_bad_1"));
                        terminal.WriteLine(Loc.Get("encounter.fairy.blessing_bad_2"));
                        player.Gold = player.Gold * 9 / 10;
                        terminal.WriteLine(Loc.Get("encounter.fairy.blessing_bad_gold"));
                    }
                    break;

                case "S":
                    if (random.NextDouble() < 0.3) // Only 30% success
                    {
                        terminal.SetColor("bright_yellow");
                        terminal.WriteLine(Loc.Get("encounter.fairy.steal_success"));
                        long dustValue = level * 500;
                        player.Gold += dustValue;
                        terminal.WriteLine(Loc.Get("encounter.fairy.steal_value", dustValue));

                        player.Darkness += 20;
                        terminal.SetColor("magenta");
                        terminal.WriteLine(Loc.Get("encounter.fairy.steal_darkness"));
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine(Loc.Get("encounter.fairy.steal_fail"));
                        int damage = (int)(player.MaxHP / 4);
                        player.HP -= damage;
                        terminal.WriteLine(Loc.Get("encounter.fairy.steal_damage", damage));

                        // Random curse
                        if (random.NextDouble() < 0.5)
                        {
                            player.Strength = Math.Max(1, player.Strength - 2);
                            terminal.WriteLine(Loc.Get("encounter.fairy.steal_curse"));
                        }
                    }
                    break;

                case "D":
                    terminal.SetColor("bright_magenta");
                    terminal.WriteLine(Loc.Get("encounter.fairy.dance_join"));
                    await Task.Delay(1000);
                    terminal.WriteLine(Loc.Get("encounter.fairy.dance_spin"));
                    await Task.Delay(1000);
                    terminal.WriteLine(Loc.Get("encounter.fairy.dance_blur"));
                    await Task.Delay(1000);

                    // Weird effects
                    var effect = random.Next(5);
                    switch (effect)
                    {
                        case 0:
                            terminal.SetColor("green");
                            terminal.WriteLine(Loc.Get("encounter.fairy.dance_younger"));
                            player.HP = player.MaxHP;
                            player.Mana = player.MaxMana;
                            player.Experience += level * 300;
                            break;
                        case 1:
                            terminal.SetColor("yellow");
                            terminal.WriteLine(Loc.Get("encounter.fairy.dance_gold"));
                            player.Gold += level * 900;  // Increased from 300 for economic balance  // Increased from 100 for economic balance
                            break;
                        case 2:
                            terminal.SetColor("cyan");
                            terminal.WriteLine(Loc.Get("encounter.fairy.dance_queen_kiss"));
                            player.Charisma += 5;
                            terminal.WriteLine(Loc.Get("encounter.fairy.dance_plus_cha"));
                            break;
                        case 3:
                            terminal.SetColor("gray");
                            terminal.WriteLine(Loc.Get("encounter.fairy.dance_time_1"));
                            terminal.WriteLine(Loc.Get("encounter.fairy.dance_time_2"));
                            break;
                        case 4:
                            terminal.SetColor("bright_white");
                            terminal.WriteLine(Loc.Get("encounter.fairy.dance_language"));
                            player.Intelligence += 3;
                            terminal.WriteLine(Loc.Get("encounter.fairy.dance_plus_int"));
                            break;
                    }
                    break;

                default:
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("encounter.fairy.leave_1"));
                    terminal.WriteLine(Loc.Get("encounter.fairy.leave_2"));
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
            terminal.WriteLine(GameConfig.ScreenReaderMode ? Loc.Get("encounter.damsel.title") : "♀ " + Loc.Get("encounter.damsel.title") + " ♀");
            terminal.WriteLine("");

            // Randomize the scenario
            var scenario = random.Next(4);

            switch (scenario)
            {
                case 0: // Classic rescue
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("encounter.damsel.rescue_desc_1"));
                    terminal.WriteLine(Loc.Get("encounter.damsel.rescue_desc_2"));
                    terminal.WriteLine("");

                    terminal.SetColor("darkgray");
                    terminal.Write("[");
                    terminal.SetColor("bright_yellow");
                    terminal.Write("R");
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("encounter.damsel.option_rescue"));
                    terminal.SetColor("darkgray");
                    terminal.Write("[");
                    terminal.SetColor("bright_yellow");
                    terminal.Write("W");
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("encounter.damsel.option_watch"));
                    terminal.SetColor("darkgray");
                    terminal.Write("[");
                    terminal.SetColor("bright_yellow");
                    terminal.Write("I");
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("encounter.damsel.option_ignore"));
                    terminal.WriteLine("");

                    var choice = await terminal.GetInput(Loc.Get("ui.your_choice"));

                    if (choice.ToUpper() == "R")
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine(Loc.Get("encounter.damsel.rescue_charge"));
                        await Task.Delay(1000);

                        // Auto-win the fight for dramatic effect
                        terminal.SetColor("green");
                        terminal.WriteLine(Loc.Get("encounter.damsel.rescue_scatter"));
                        terminal.WriteLine("");

                        terminal.SetColor("cyan");
                        terminal.WriteLine(Loc.Get("encounter.damsel.rescue_thanks_1"));
                        terminal.WriteLine(Loc.Get("encounter.damsel.rescue_thanks_2"));

                        long goldReward = level * 200;
                        player.Gold += goldReward;
                        player.Chivalry += 75;
                        terminal.SetColor("bright_yellow");
                        terminal.WriteLine(Loc.Get("encounter.reward_gold", goldReward));
                        terminal.WriteLine(Loc.Get("encounter.reward_chivalry", 75));

                        if (random.NextDouble() < 0.3)
                        {
                            terminal.SetColor("magenta");
                            terminal.WriteLine("");
                            terminal.WriteLine(Loc.Get("encounter.damsel.rescue_blush"));
                            // TODO: Add romance subplot tracking
                        }
                    }
                    else if (choice.ToUpper() == "W")
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine(Loc.Get("encounter.damsel.watch_hide"));
                        await Task.Delay(1500);
                        terminal.SetColor("cyan");
                        terminal.WriteLine(Loc.Get("encounter.damsel.watch_kick"));
                        terminal.WriteLine(Loc.Get("encounter.damsel.watch_dispatch"));
                        terminal.WriteLine("");
                        terminal.WriteLine(Loc.Get("encounter.damsel.watch_wink"));
                        terminal.WriteLine(Loc.Get("encounter.damsel.watch_thanks"));
                        player.Gold += level * 150;  // Increased from 50 for economic balance
                        player.Experience += level * 100;
                        terminal.SetColor("yellow");
                        terminal.WriteLine(Loc.Get("encounter.reward_gold_exp", level * 150, level * 100));
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine(Loc.Get("encounter.damsel.ignore_walk"));
                        player.Darkness += 30;
                        terminal.WriteLine(Loc.Get("encounter.reward_darkness", 30));
                    }
                    break;

                case 1: // It's a trap!
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("encounter.damsel.trap_desc_1"));
                    terminal.WriteLine(Loc.Get("encounter.damsel.trap_desc_2"));
                    terminal.WriteLine("");

                    terminal.SetColor("darkgray");
                    terminal.Write("[");
                    terminal.SetColor("bright_yellow");
                    terminal.Write("H");
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("encounter.damsel.trap_option_help"));
                    terminal.SetColor("darkgray");
                    terminal.Write("[");
                    terminal.SetColor("bright_yellow");
                    terminal.Write("C");
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("encounter.damsel.trap_option_caution"));
                    terminal.SetColor("darkgray");
                    terminal.Write("[");
                    terminal.SetColor("bright_yellow");
                    terminal.Write("L");
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("encounter.option_leave"));
                    terminal.WriteLine("");

                    choice = await terminal.GetInput(Loc.Get("ui.your_choice"));

                    if (choice.ToUpper() == "H")
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine(Loc.Get("encounter.damsel.trap_grab"));
                        terminal.WriteLine(Loc.Get("encounter.damsel.trap_grin"));
                        terminal.WriteLine(Loc.Get("encounter.damsel.trap_fool"));
                        await Task.Delay(1000);

                        int damage = (int)(player.MaxHP / 3);
                        player.HP -= damage;
                        long goldStolen = player.Gold / 5;
                        player.Gold -= goldStolen;

                        terminal.WriteLine(Loc.Get("encounter.damsel.trap_succubus_damage", damage));
                        terminal.WriteLine(Loc.Get("encounter.damsel.trap_succubus_steal", goldStolen));
                    }
                    else if (choice.ToUpper() == "C")
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine(Loc.Get("encounter.damsel.trap_caution_1"));
                        terminal.WriteLine(Loc.Get("encounter.damsel.trap_caution_2"));
                        terminal.SetColor("green");
                        terminal.WriteLine(Loc.Get("encounter.damsel.trap_caution_saved"));
                        player.Experience += level * 75;
                    }
                    else
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine(Loc.Get("encounter.damsel.trap_leave"));
                    }
                    break;

                case 2: // Princess!
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine(Loc.Get("encounter.damsel.princess_desc_1"));
                    terminal.WriteLine(Loc.Get("encounter.damsel.princess_desc_2"));
                    terminal.WriteLine(Loc.Get("encounter.damsel.princess_desc_3"));
                    terminal.WriteLine("");

                    terminal.SetColor("darkgray");
                    terminal.Write("[");
                    terminal.SetColor("bright_yellow");
                    terminal.Write("E");
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("encounter.damsel.princess_option_escort"));
                    terminal.SetColor("darkgray");
                    terminal.Write("[");
                    terminal.SetColor("bright_yellow");
                    terminal.Write("R");
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("encounter.damsel.princess_option_ransom"));
                    terminal.SetColor("darkgray");
                    terminal.Write("[");
                    terminal.SetColor("bright_yellow");
                    terminal.Write("L");
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("encounter.damsel.princess_option_leave"));
                    terminal.WriteLine("");

                    choice = await terminal.GetInput(Loc.Get("ui.your_choice"));

                    if (choice.ToUpper() == "E")
                    {
                        terminal.SetColor("green");
                        terminal.WriteLine(Loc.Get("encounter.damsel.princess_escort_1"));
                        await Task.Delay(1000);
                        terminal.WriteLine(Loc.Get("encounter.damsel.princess_escort_2"));
                        terminal.WriteLine("");
                        terminal.SetColor("bright_yellow");
                        terminal.WriteLine(Loc.Get("encounter.damsel.princess_escort_reward"));

                        long royalReward = level * 1000;
                        player.Gold += royalReward;
                        player.Chivalry += 200;
                        player.Experience += level * 500;

                        terminal.WriteLine(Loc.Get("encounter.reward_gold", royalReward));
                        terminal.WriteLine(Loc.Get("encounter.reward_chivalry", 200));
                        terminal.WriteLine(Loc.Get("encounter.reward_exp", level * 500));
                    }
                    else if (choice.ToUpper() == "R")
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine(Loc.Get("encounter.damsel.princess_ransom_1"));
                        terminal.WriteLine(Loc.Get("encounter.damsel.princess_ransom_2"));
                        terminal.WriteLine("");

                        player.Gold += level * 5000;  // Increased from 2000 for economic balance (major jackpot)
                        player.Darkness += 100;
                        player.Chivalry = Math.Max(0, player.Chivalry - 100);

                        terminal.SetColor("yellow");
                        terminal.WriteLine(Loc.Get("encounter.damsel.princess_ransom_gold", level * 5000));
                        terminal.SetColor("magenta");
                        terminal.WriteLine(Loc.Get("encounter.damsel.princess_ransom_penalty"));
                        terminal.WriteLine(Loc.Get("encounter.damsel.princess_ransom_rep"));
                    }
                    else
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine(Loc.Get("encounter.damsel.princess_leave_1"));
                        terminal.WriteLine(Loc.Get("encounter.damsel.princess_leave_2"));
                    }
                    break;

                case 3: // Warrior woman
                    terminal.SetColor("cyan");
                    terminal.WriteLine(Loc.Get("encounter.damsel.warrior_desc_1"));
                    terminal.WriteLine(Loc.Get("encounter.damsel.warrior_desc_2"));
                    terminal.WriteLine("");

                    terminal.SetColor("darkgray");
                    terminal.Write("[");
                    terminal.SetColor("bright_yellow");
                    terminal.Write("J");
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor("cyan");
                    terminal.WriteLine(Loc.Get("encounter.damsel.warrior_option_join"));
                    terminal.SetColor("darkgray");
                    terminal.Write("[");
                    terminal.SetColor("bright_yellow");
                    terminal.Write("W");
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor("cyan");
                    terminal.WriteLine(Loc.Get("encounter.damsel.warrior_option_watch"));
                    terminal.WriteLine("");

                    choice = await terminal.GetInput(Loc.Get("ui.your_choice"));

                    if (choice.ToUpper() == "J")
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine(Loc.Get("encounter.damsel.warrior_join_1"));
                        await Task.Delay(1000);
                        terminal.WriteLine(Loc.Get("encounter.damsel.warrior_join_2"));
                        terminal.WriteLine("");

                        terminal.SetColor("cyan");
                        terminal.WriteLine(Loc.Get("encounter.damsel.warrior_join_3"));
                        terminal.WriteLine(Loc.Get("encounter.damsel.warrior_join_4"));
                        terminal.WriteLine(Loc.Get("encounter.damsel.warrior_join_5"));

                        long loot = level * 150;
                        player.Gold += loot;
                        player.Experience += level * 100;

                        terminal.SetColor("green");
                        terminal.WriteLine(Loc.Get("encounter.reward_gold_exp", loot, level * 100));

                        if (random.NextDouble() < 0.2)
                        {
                            terminal.SetColor("bright_cyan");
                            terminal.WriteLine("");
                            terminal.WriteLine(Loc.Get("encounter.damsel.warrior_team_hint"));
                            // TODO: Add companion system
                        }
                    }
                    else
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine(Loc.Get("encounter.damsel.warrior_watch_1"));
                        terminal.WriteLine(Loc.Get("encounter.damsel.warrior_watch_2"));
                        terminal.WriteLine(Loc.Get("encounter.damsel.warrior_watch_3"));
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
            UIHelper.WriteBoxHeader(terminal, "* " + Loc.Get("encounter.ghost.title") + " *", "bright_white", 55);
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("encounter.ghost.desc_1"));
            terminal.WriteLine(Loc.Get("encounter.ghost.desc_2"));
            terminal.WriteLine("");

            await Task.Delay(1500);

            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("encounter.ghost.greet_1"));
            terminal.WriteLine(Loc.Get("encounter.ghost.greet_2"));
            terminal.WriteLine(Loc.Get("encounter.ghost.greet_3"));
            terminal.WriteLine(Loc.Get("encounter.ghost.greet_4"));
            terminal.WriteLine("");

            await Task.Delay(2000);

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("L");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.ghost.option_lore"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("A");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.ghost.option_advice"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("F");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.ghost.option_friend"));
            terminal.WriteLine("");

            var choice = await terminal.GetInput(Loc.Get("ui.your_choice"));

            switch (choice.ToUpper())
            {
                case "L":
                    terminal.SetColor("cyan");
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("encounter.ghost.lore_1"));
                    terminal.WriteLine(Loc.Get("encounter.ghost.lore_2"));
                    terminal.WriteLine(Loc.Get("encounter.ghost.lore_3"));
                    terminal.WriteLine(Loc.Get("encounter.ghost.lore_4"));
                    terminal.WriteLine(Loc.Get("encounter.ghost.lore_5"));
                    terminal.WriteLine("");
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("encounter.ghost.lore_nostalgia"));

                    player.Experience += level * 200;
                    terminal.SetColor("green");
                    terminal.WriteLine(Loc.Get("encounter.ghost.lore_reward", level * 200));
                    break;

                case "A":
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("encounter.ghost.advice_1"));
                    terminal.WriteLine(Loc.Get("encounter.ghost.advice_2"));
                    terminal.WriteLine(Loc.Get("encounter.ghost.advice_3"));
                    terminal.WriteLine(Loc.Get("encounter.ghost.advice_4"));
                    terminal.WriteLine(Loc.Get("encounter.ghost.advice_5"));
                    terminal.WriteLine("");

                    player.Intelligence += 2;
                    player.Wisdom += 2;
                    terminal.SetColor("green");
                    terminal.WriteLine(Loc.Get("encounter.ghost.advice_reward"));
                    break;

                case "F":
                    terminal.SetColor("white");
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("encounter.ghost.friend_1"));
                    terminal.WriteLine(Loc.Get("encounter.ghost.friend_2"));
                    terminal.WriteLine(Loc.Get("encounter.ghost.friend_3"));
                    terminal.WriteLine("");

                    // Give a nice reward
                    long goldGift = level * 300;
                    player.Gold += goldGift;
                    player.Strength += 3;

                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine(Loc.Get("encounter.ghost.friend_gold", goldGift));
                    terminal.WriteLine(Loc.Get("encounter.ghost.friend_str"));
                    break;

                default:
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("encounter.ghost.fade"));
                    break;
            }

            terminal.SetColor("cyan");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("encounter.ghost.farewell_1"));
            terminal.WriteLine(Loc.Get("encounter.ghost.farewell_2"));

            await terminal.PressAnyKey();
        }

        /// <summary>
        /// Gambling with demons - high risk, high reward
        /// </summary>
        private static async Task GamblingDemonsEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("red");
            terminal.WriteLine("🎲 " + Loc.Get("encounter.demons.title") + " 🎲");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.demons.desc_1"));
            terminal.WriteLine(Loc.Get("encounter.demons.desc_2"));
            terminal.WriteLine("");

            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("encounter.demons.greet_1"));
            terminal.WriteLine(Loc.Get("encounter.demons.greet_2"));
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("P");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.demons.option_play"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("L");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.demons.option_leave"));
            terminal.WriteLine("");

            var choice = await terminal.GetInput(Loc.Get("ui.your_choice"));

            if (choice.ToUpper() == "P")
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("encounter.demons.play_1"));
                terminal.WriteLine(Loc.Get("encounter.demons.play_2"));
                terminal.WriteLine(Loc.Get("encounter.demons.play_3"));
                terminal.WriteLine("");

                terminal.SetColor("darkgray");
                terminal.Write("[");
                terminal.SetColor("bright_yellow");
                terminal.Write("G");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("encounter.demons.wager_gold"));
                terminal.SetColor("darkgray");
                terminal.Write("[");
                terminal.SetColor("bright_yellow");
                terminal.Write("S");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("encounter.demons.wager_soul"));
                terminal.SetColor("darkgray");
                terminal.Write("[");
                terminal.SetColor("bright_yellow");
                terminal.Write("Y");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("encounter.demons.wager_years"));
                terminal.WriteLine("");

                var wager = await terminal.GetInput(Loc.Get("encounter.demons.wager_prompt"));

                int demonRoll = random.Next(1, 7) + random.Next(1, 7) + random.Next(1, 7);
                int playerRoll = random.Next(1, 7) + random.Next(1, 7) + random.Next(1, 7);

                await Task.Delay(1000);
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("encounter.demons.roll_demons", demonRoll), "red");
                terminal.WriteLine(Loc.Get("encounter.demons.roll_player", playerRoll), "cyan");
                terminal.WriteLine("");

                bool won = playerRoll > demonRoll;

                switch (wager.ToUpper())
                {
                    case "G":
                        if (won)
                        {
                            player.Gold += 1000;
                            terminal.SetColor("green");
                            terminal.WriteLine(Loc.Get("encounter.demons.gold_win"));
                            terminal.WriteLine(Loc.Get("encounter.demons.gold_win_amount"));
                        }
                        else
                        {
                            player.Gold = Math.Max(0, player.Gold - 1000);
                            terminal.SetColor("red");
                            terminal.WriteLine(Loc.Get("encounter.demons.gold_lose"));
                            terminal.WriteLine(Loc.Get("encounter.demons.gold_lose_amount"));
                        }
                        break;

                    case "S":
                        if (won)
                        {
                            player.Strength += 10;
                            player.Intelligence += 10;
                            terminal.SetColor("bright_green");
                            terminal.WriteLine(Loc.Get("encounter.demons.soul_win"));
                            terminal.WriteLine(Loc.Get("encounter.demons.soul_win_stats"));
                        }
                        else
                        {
                            player.Strength = Math.Max(1, player.Strength - 5);
                            player.Charisma = Math.Max(1, player.Charisma - 5);
                            player.Darkness += 50;
                            terminal.SetColor("red");
                            terminal.WriteLine(Loc.Get("encounter.demons.soul_lose"));
                            terminal.WriteLine(Loc.Get("encounter.demons.soul_lose_stats"));
                        }
                        break;

                    case "Y":
                        if (won)
                        {
                            player.Experience += level * 1000;
                            terminal.SetColor("bright_yellow");
                            terminal.WriteLine(Loc.Get("encounter.demons.years_win"));
                            terminal.WriteLine(Loc.Get("encounter.reward_exp", level * 1000));
                        }
                        else
                        {
                            player.Experience = Math.Max(0, player.Experience - level * 500);
                            terminal.SetColor("red");
                            terminal.WriteLine(Loc.Get("encounter.demons.years_lose"));
                            terminal.WriteLine(Loc.Get("encounter.demons.years_lose_amount", level * 500));
                        }
                        break;
                }
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("encounter.demons.leave_1"));
                terminal.WriteLine(Loc.Get("encounter.demons.leave_2"));
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
            terminal.WriteLine("=== " + Loc.Get("encounter.hermit.title") + " ===");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.hermit.desc_1"));
            terminal.WriteLine(Loc.Get("encounter.hermit.desc_2"));
            terminal.WriteLine(Loc.Get("encounter.hermit.desc_3"));
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("S");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("encounter.hermit.option_sit"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("A");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("encounter.hermit.option_ask"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("G");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("encounter.hermit.option_give"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("L");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("encounter.option_leave"));
            terminal.WriteLine("");

            var choice = await terminal.GetInput(Loc.Get("ui.your_choice"));

            switch (choice.ToUpper())
            {
                case "S":
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("encounter.hermit.sit_1"));
                    await Task.Delay(1500);
                    terminal.WriteLine(Loc.Get("encounter.hermit.sit_2"));
                    await Task.Delay(1500);
                    terminal.WriteLine(Loc.Get("encounter.hermit.sit_3"));
                    await Task.Delay(1500);
                    terminal.SetColor("green");
                    terminal.WriteLine(Loc.Get("encounter.hermit.sit_peace"));
                    player.HP = player.MaxHP;
                    player.Mana = player.MaxMana;
                    terminal.WriteLine(Loc.Get("encounter.hermit.sit_restore"));
                    break;

                case "A":
                    terminal.SetColor("cyan");
                    terminal.WriteLine(Loc.Get("encounter.hermit.ask_1"));
                    terminal.WriteLine(Loc.Get("encounter.hermit.ask_2"));
                    terminal.WriteLine(Loc.Get("encounter.hermit.ask_3"));
                    player.Intelligence += 1;
                    terminal.SetColor("green");
                    terminal.WriteLine(Loc.Get("encounter.hermit.ask_reward"));
                    break;

                case "G":
                    if (player.Gold >= 50)
                    {
                        player.Gold -= 50;
                        terminal.SetColor("bright_yellow");
                        terminal.WriteLine(Loc.Get("encounter.hermit.give_1"));
                        terminal.WriteLine(Loc.Get("encounter.hermit.give_2"));
                        terminal.WriteLine(Loc.Get("encounter.hermit.give_3"));
                        terminal.WriteLine("");

                        // Random reward
                        var reward = random.Next(3);
                        switch (reward)
                        {
                            case 0:
                                player.Strength += 5;
                                terminal.WriteLine(Loc.Get("encounter.hermit.give_amulet"));
                                break;
                            case 1:
                                player.Healing = player.MaxPotions;
                                terminal.WriteLine(Loc.Get("encounter.hermit.give_potions"));
                                break;
                            case 2:
                                player.Experience += level * 300;
                                terminal.WriteLine(Loc.Get("encounter.reward_exp", level * 300));
                                break;
                        }

                        player.Chivalry += 25;
                        terminal.WriteLine(Loc.Get("encounter.reward_chivalry", 25));
                    }
                    else
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine(Loc.Get("encounter.hermit.give_nothing"));
                    }
                    break;

                default:
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("encounter.hermit.leave_msg"));
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
            terminal.WriteLine("=== " + Loc.Get("encounter.merchant.title") + " ===");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.merchant.desc_1"));
            terminal.WriteLine(Loc.Get("encounter.merchant.desc_2"));
            terminal.WriteLine(Loc.Get("encounter.merchant.desc_3"));
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
            terminal.WriteLine(Loc.Get("encounter.merchant.item_potion", potionPrice));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("2");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("encounter.merchant.item_elixir", buffPrice));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("3");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("encounter.merchant.item_mystery", secretPrice));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("4");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("encounter.merchant.item_info"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("L");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("encounter.option_leave"));
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("encounter.merchant.your_gold", player.Gold), "cyan");
            terminal.WriteLine("");

            var choice = await terminal.GetInput(Loc.Get("ui.your_choice"));

            switch (choice)
            {
                case "1":
                    if (player.Gold >= potionPrice)
                    {
                        player.Gold -= potionPrice;
                        player.HP = player.MaxHP;
                        terminal.SetColor("green");
                        terminal.WriteLine(Loc.Get("encounter.merchant.potion_drink"));
                    }
                    else
                    {
                        terminal.WriteLine(Loc.Get("ui.not_enough_gold_friend"), "red");
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
                                terminal.WriteLine(Loc.Get("encounter.merchant.elixir_str"), "green");
                                break;
                            case 1:
                                player.Intelligence += 5;
                                terminal.WriteLine(Loc.Get("encounter.merchant.elixir_int"), "green");
                                break;
                            case 2:
                                player.Wisdom += 5;
                                terminal.WriteLine(Loc.Get("encounter.merchant.elixir_wis"), "green");
                                break;
                            case 3:
                                player.Dexterity += 5;
                                terminal.WriteLine(Loc.Get("encounter.merchant.elixir_dex"), "green");
                                break;
                            case 4:
                                player.Constitution += 5;
                                terminal.WriteLine(Loc.Get("encounter.merchant.elixir_con"), "green");
                                break;
                            case 5:
                                player.Charisma += 5;
                                terminal.WriteLine(Loc.Get("encounter.merchant.elixir_cha"), "green");
                                break;
                        }
                    }
                    else
                    {
                        terminal.WriteLine(Loc.Get("ui.not_enough_gold_friend"), "red");
                    }
                    break;

                case "3":
                    if (player.Gold >= secretPrice)
                    {
                        player.Gold -= secretPrice;
                        terminal.SetColor("bright_magenta");
                        terminal.WriteLine(Loc.Get("encounter.merchant.mystery_open"));
                        await Task.Delay(1500);

                        var mystery = random.Next(5);
                        switch (mystery)
                        {
                            case 0:
                                player.Gold += secretPrice * 3;
                                terminal.WriteLine(Loc.Get("encounter.merchant.mystery_jackpot", secretPrice * 3), "bright_yellow");
                                break;
                            case 1:
                                player.Strength += 10;
                                player.Defence += 10;
                                terminal.WriteLine(Loc.Get("encounter.merchant.mystery_power"), "bright_green");
                                break;
                            case 2:
                                terminal.WriteLine(Loc.Get("encounter.merchant.mystery_empty"), "red");
                                break;
                            case 3:
                                player.Experience += level * 500;
                                terminal.WriteLine(Loc.Get("encounter.merchant.mystery_tome", level * 500), "cyan");
                                break;
                            case 4:
                                player.Healing = player.MaxPotions;
                                player.Mana = player.MaxMana;
                                terminal.WriteLine(Loc.Get("encounter.merchant.mystery_elixirs"), "green");
                                break;
                        }
                    }
                    else
                    {
                        terminal.WriteLine(Loc.Get("ui.not_enough_gold_friend"), "red");
                    }
                    break;

                case "4":
                    if (player.Gold >= 100)
                    {
                        player.Gold -= 100;
                        terminal.SetColor("cyan");
                        terminal.WriteLine(Loc.Get("encounter.merchant.info_1"));
                        terminal.WriteLine(Loc.Get("encounter.merchant.info_2"));
                        terminal.WriteLine(Loc.Get("encounter.merchant.info_3"));
                    }
                    else
                    {
                        terminal.WriteLine(Loc.Get("encounter.merchant.no_gold_info"), "red");
                    }
                    break;

                default:
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("encounter.merchant.leave_msg"));
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
            terminal.WriteLine("⌛ " + Loc.Get("encounter.timewarp.title") + " ⌛");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.timewarp.desc_1"));
            terminal.WriteLine(Loc.Get("encounter.timewarp.desc_2"));
            terminal.WriteLine("");

            await Task.Delay(2000);

            var warp = random.Next(5);
            switch (warp)
            {
                case 0:
                    terminal.SetColor("green");
                    terminal.WriteLine(Loc.Get("encounter.timewarp.future_1"));
                    terminal.WriteLine(Loc.Get("encounter.timewarp.future_2"));
                    player.Gold += level * 1500;  // Increased from 500 for economic balance
                    terminal.WriteLine(Loc.Get("encounter.timewarp.future_reward", level * 1500));
                    break;

                case 1:
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("encounter.timewarp.battle_1"));
                    terminal.WriteLine(Loc.Get("encounter.timewarp.battle_2"));
                    player.Experience += level * 300;
                    player.Strength += 2;
                    terminal.WriteLine(Loc.Get("encounter.timewarp.battle_reward", level * 300));
                    break;

                case 2:
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("encounter.timewarp.age_1"));
                    terminal.WriteLine(Loc.Get("encounter.timewarp.age_2"));
                    player.HP = player.HP / 2;
                    player.Experience = Math.Max(0, player.Experience - level * 100);
                    terminal.WriteLine(Loc.Get("encounter.timewarp.age_penalty"));
                    break;

                case 3:
                    terminal.SetColor("bright_green");
                    terminal.WriteLine(Loc.Get("encounter.timewarp.young_1"));
                    terminal.WriteLine(Loc.Get("encounter.timewarp.young_2"));
                    player.HP = player.MaxHP;
                    player.Mana = player.MaxMana;
                    player.Constitution += 3;
                    terminal.WriteLine(Loc.Get("encounter.timewarp.young_reward"));
                    break;

                case 4:
                    terminal.SetColor("magenta");
                    terminal.WriteLine(Loc.Get("encounter.timewarp.death_1"));
                    terminal.WriteLine(Loc.Get("encounter.timewarp.death_2"));
                    terminal.WriteLine(Loc.Get("encounter.timewarp.death_3"));
                    player.Defence += 5;
                    terminal.WriteLine(Loc.Get("encounter.timewarp.death_reward"));
                    break;
            }

            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("encounter.timewarp.end"));

            await terminal.PressAnyKey();
        }

        /// <summary>
        /// Ancient library with knowledge
        /// </summary>
        private static async Task AncientLibraryEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("cyan");
            terminal.WriteLine("📚 " + Loc.Get("encounter.library.title") + " 📚");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.library.desc_1"));
            terminal.WriteLine(Loc.Get("encounter.library.desc_2"));
            terminal.WriteLine(Loc.Get("encounter.library.desc_3"));
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("1");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("encounter.library.option_combat"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("2");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("encounter.library.option_arcane"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("3");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("encounter.library.option_history"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("4");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("encounter.library.option_maps"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("L");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("encounter.option_leave"));
            terminal.WriteLine("");

            var choice = await terminal.GetInput(Loc.Get("ui.your_choice"));

            switch (choice)
            {
                case "1":
                    terminal.SetColor("green");
                    terminal.WriteLine(Loc.Get("encounter.library.combat_study"));
                    player.Strength += 3;
                    player.Dexterity += 2;
                    terminal.WriteLine(Loc.Get("encounter.library.combat_reward"));
                    break;

                case "2":
                    terminal.SetColor("bright_magenta");
                    terminal.WriteLine(Loc.Get("encounter.library.arcane_study"));
                    player.Intelligence += 3;
                    player.MaxMana += 10;
                    player.Mana = player.MaxMana;
                    terminal.WriteLine(Loc.Get("encounter.library.arcane_reward"));
                    break;

                case "3":
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("encounter.library.history_study"));
                    player.Experience += level * 400;
                    player.Wisdom += 2;
                    terminal.WriteLine(Loc.Get("encounter.library.history_reward", level * 400));
                    break;

                case "4":
                    if (random.NextDouble() < 0.6)
                    {
                        terminal.SetColor("bright_yellow");
                        terminal.WriteLine(Loc.Get("encounter.library.map_found"));
                        long treasure = level * 600;
                        player.Gold += treasure;
                        terminal.WriteLine(Loc.Get("encounter.library.map_reward", treasure));
                    }
                    else
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine(Loc.Get("encounter.library.map_nothing"));
                    }
                    break;

                default:
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("encounter.library.leave_msg"));
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
            terminal.WriteLine("=== " + Loc.Get("encounter.well.title") + " ===");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.well.desc_1"));
            terminal.WriteLine(Loc.Get("encounter.well.desc_2"));
            terminal.WriteLine(Loc.Get("encounter.well.desc_3"));
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("T");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("encounter.well.option_throw"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("D");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("encounter.well.option_dive"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("L");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("encounter.option_leave"));
            terminal.WriteLine("");

            bool validChoice = false;
            while (!validChoice)
            {
                var choice = await terminal.GetInput(Loc.Get("ui.your_choice"));

                switch (choice.ToUpper())
                {
                    case "T":
                        validChoice = true;
                        if (player.Gold >= 100)
                        {
                            player.Gold -= 100;
                            terminal.SetColor("cyan");
                            terminal.WriteLine(Loc.Get("encounter.well.throw_coin"));
                            await Task.Delay(1500);

                            var wish = random.Next(6);
                            switch (wish)
                            {
                                case 0:
                                    terminal.SetColor("bright_green");
                                    terminal.WriteLine(Loc.Get("encounter.well.wish_granted"));
                                    player.HP = player.MaxHP;
                                    player.Mana = player.MaxMana;
                                    terminal.WriteLine(Loc.Get("encounter.well.wish_restore"));
                                    break;
                                case 1:
                                    terminal.SetColor("bright_yellow");
                                    terminal.WriteLine(Loc.Get("encounter.well.wish_gold"));
                                    player.Gold += 500;
                                    terminal.WriteLine(Loc.Get("encounter.well.wish_gold_amount"));
                                    break;
                                case 2:
                                    terminal.SetColor("green");
                                    terminal.WriteLine(Loc.Get("encounter.well.wish_strength"));
                                    player.Strength += 3;
                                    terminal.WriteLine(Loc.Get("encounter.well.wish_str_amount"));
                                    break;
                                case 3:
                                    terminal.SetColor("gray");
                                    terminal.WriteLine(Loc.Get("encounter.well.wish_nothing"));
                                    break;
                                case 4:
                                    terminal.SetColor("bright_magenta");
                                    terminal.WriteLine(Loc.Get("encounter.well.wish_wisdom"));
                                    player.Experience += level * 250;
                                    terminal.WriteLine(Loc.Get("encounter.reward_exp", level * 250));
                                    break;
                                case 5:
                                    terminal.SetColor("red");
                                    terminal.WriteLine(Loc.Get("encounter.well.wish_reject"));
                                    terminal.WriteLine(Loc.Get("encounter.well.wish_returned"));
                                    player.Gold += 150;
                                    break;
                            }
                        }
                        else
                        {
                            terminal.WriteLine(Loc.Get("ui.not_enough_gold_plain"), "gray");
                        }
                        break;

                    case "D":
                        validChoice = true;
                        terminal.SetColor("yellow");
                        terminal.WriteLine(Loc.Get("encounter.well.dive"));
                        await Task.Delay(1000);

                        if (random.NextDouble() < 0.4)
                        {
                            long coins = level * 200 + random.Next(500);
                            player.Gold += coins;
                            terminal.SetColor("bright_yellow");
                            terminal.WriteLine(Loc.Get("encounter.well.dive_success", coins));
                            terminal.WriteLine(Loc.Get("encounter.well.dive_rich"));
                        }
                        else
                        {
                            terminal.SetColor("red");
                            terminal.WriteLine(Loc.Get("encounter.well.dive_angry"));
                            int damage = (int)(player.MaxHP / 3);
                            player.HP -= damage;
                            terminal.WriteLine(Loc.Get("encounter.well.dive_damage", damage));
                            terminal.WriteLine(Loc.Get("encounter.well.dive_escape"));
                        }
                        break;

                    case "L":
                        validChoice = true;
                        terminal.SetColor("gray");
                        terminal.WriteLine(Loc.Get("encounter.well.leave_msg"));
                        break;

                    default:
                        terminal.WriteLine(Loc.Get("ui.invalid_choice"), "red");
                        break;
                }
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
            terminal.WriteLine("=== " + Loc.Get("encounter.arena.title") + " ===");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.arena.desc_1"));
            terminal.WriteLine(Loc.Get("encounter.arena.desc_2"));
            terminal.WriteLine(Loc.Get("encounter.arena.desc_3"));
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("E");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("encounter.arena.option_enter"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("L");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("encounter.option_leave"));
            terminal.WriteLine("");

            var choice = await terminal.GetInput(Loc.Get("ui.your_choice"));

            if (choice.ToUpper() == "E")
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("encounter.arena.enter_portal"));
                await Task.Delay(1000);
                terminal.WriteLine(Loc.Get("encounter.arena.crowd_roars"));
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
                    terminal.WriteLine(Loc.Get("encounter.arena.opponent", champion.Name));
                    terminal.WriteLine("");
                    await Task.Delay(500);

                    // Real interactive combat
                    var combatEngine = new CombatEngine(terminal);
                    var result = await combatEngine.PlayerVsMonster(player, champion, offerMonkEncounter: false);

                    if (result.Outcome == CombatOutcome.Victory)
                    {
                        terminal.WriteLine("");
                        terminal.SetColor("bright_yellow");
                        terminal.WriteLine(Loc.Get("encounter.arena.victory_1"));
                        terminal.WriteLine(Loc.Get("encounter.arena.victory_2"));
                        player.Chivalry += 25;
                        terminal.SetColor("green");
                        terminal.WriteLine(Loc.Get("encounter.reward_chivalry", 25));
                    }
                    else if (result.Outcome == CombatOutcome.PlayerDied)
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine(Loc.Get("encounter.arena.defeat"));
                        // Prevent permadeath from arena — leave at 1 HP
                        if (player.HP <= 0)
                            player.HP = 1;
                    }
                    else
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine(Loc.Get("encounter.arena.fled"));
                    }
                }
                else
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("encounter.arena.empty"));
                }
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("encounter.arena.leave_1"));
                terminal.WriteLine(Loc.Get("encounter.arena.leave_2"));
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
            terminal.WriteLine("=== " + Loc.Get("encounter.bone_oracle.title") + " ===");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.bone_oracle.desc_1"));
            terminal.WriteLine(Loc.Get("encounter.bone_oracle.desc_2"));
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("A");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.bone_oracle.option_future"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("D");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.bone_oracle.option_dungeon"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("L");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.option_leave"));

            var choice = await terminal.GetInput(Loc.Get("ui.your_choice"));

            if (choice.ToUpper() == "A")
            {
                terminal.SetColor("cyan");
                var prophecy = random.Next(4);
                switch (prophecy)
                {
                    case 0:
                        terminal.WriteLine(Loc.Get("encounter.bone_oracle.prophecy_0"));
                        player.Experience += level * 100;
                        break;
                    case 1:
                        terminal.WriteLine(Loc.Get("encounter.bone_oracle.prophecy_1"));
                        player.Defence += 2;
                        break;
                    case 2:
                        terminal.WriteLine(Loc.Get("encounter.bone_oracle.prophecy_2"));
                        player.Chivalry += 25;
                        break;
                    case 3:
                        terminal.WriteLine(Loc.Get("encounter.bone_oracle.prophecy_3"));
                        player.Wisdom += 2;
                        break;
                }
            }
            else if (choice.ToUpper() == "D")
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("encounter.bone_oracle.dungeon_1"));
                terminal.WriteLine(Loc.Get("encounter.bone_oracle.dungeon_2"));
                terminal.WriteLine(Loc.Get("encounter.bone_oracle.dungeon_3"));
                player.Intelligence += 1;
            }

            await terminal.PressAnyKey();
        }

        private static async Task RestlessSpiritsEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_white");
            terminal.WriteLine("=== " + Loc.Get("encounter.spirits.title") + " ===");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.spirits.desc_1"));
            terminal.WriteLine(Loc.Get("encounter.spirits.desc_2"));
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("H");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.spirits.option_help"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("A");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.spirits.option_attack"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("I");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.spirits.option_ignore"));

            var choice = await terminal.GetInput(Loc.Get("ui.your_choice"));

            if (choice.ToUpper() == "H")
            {
                terminal.SetColor("bright_cyan");
                terminal.WriteLine(Loc.Get("encounter.spirits.help_1"));
                terminal.WriteLine(Loc.Get("encounter.spirits.help_2"));
                terminal.WriteLine(Loc.Get("encounter.spirits.help_3"));

                player.Chivalry += 50;
                player.Experience += level * 150;
                terminal.SetColor("green");
                terminal.WriteLine(Loc.Get("encounter.spirits.help_reward", level * 150));
            }
            else if (choice.ToUpper() == "A")
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("encounter.spirits.attack_1"));
                terminal.WriteLine(Loc.Get("encounter.spirits.attack_2"));

                int damage = (int)(player.MaxHP / 4);
                player.HP -= damage;
                player.Darkness += 20;
                terminal.WriteLine(Loc.Get("encounter.spirits.attack_penalty", damage));
            }

            await terminal.PressAnyKey();
        }

        private static async Task CryptKeeperEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("encounter.crypt.title_icon"));
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.crypt.desc_1"));
            terminal.WriteLine(Loc.Get("encounter.crypt.desc_2"));
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("T");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.crypt.option_trade"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("I");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.crypt.option_info"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("L");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.option_leave"));

            var choice = await terminal.GetInput(Loc.Get("ui.your_choice"));

            if (choice.ToUpper() == "T")
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("encounter.crypt.trade_1"));
                if (player.Gold >= 150)
                {
                    player.Gold -= 150;
                    player.Healing = Math.Min(player.MaxPotions, player.Healing + 2);
                    terminal.SetColor("green");
                    terminal.WriteLine(Loc.Get("encounter.crypt.trade_success"));
                }
                else
                {
                    terminal.WriteLine(Loc.Get("ui.not_enough_gold_friend"));
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
                    hints.Add((Loc.Get("encounter.crypt.hint_seal_1", nearestSeal),
                               Loc.Get("encounter.crypt.hint_seal_2")));
                }
                if (nearestGodIdx >= 0 && godFloors[nearestGodIdx] - level <= 20)
                {
                    hints.Add((Loc.Get("encounter.crypt.hint_god_1", godNames[nearestGodIdx], godFloors[nearestGodIdx]),
                               Loc.Get("encounter.crypt.hint_god_2")));
                }
                if (level >= 10 && level <= 30)
                {
                    hints.Add((Loc.Get("encounter.crypt.hint_low_1"),
                               Loc.Get("encounter.crypt.hint_low_2")));
                }
                if (level >= 40 && level <= 60)
                {
                    hints.Add((Loc.Get("encounter.crypt.hint_mid_1"),
                               Loc.Get("encounter.crypt.hint_mid_2", level + 10)));
                }
                if (level >= 70)
                {
                    hints.Add((Loc.Get("encounter.crypt.hint_deep_1"),
                               Loc.Get("encounter.crypt.hint_deep_2")));
                }

                // Always have at least one generic hint
                if (hints.Count == 0)
                {
                    hints.Add((Loc.Get("encounter.crypt.hint_generic_1"),
                               Loc.Get("encounter.crypt.hint_generic_2")));
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
            terminal.WriteLine(Loc.Get("encounter.tomb.title_icon"));
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.tomb.desc_1"));
            terminal.WriteLine(Loc.Get("encounter.tomb.desc_2"));
            terminal.WriteLine(Loc.Get("encounter.tomb.desc_3"));
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("O");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.tomb.option_open"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("T");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.tomb.option_take"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("L");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.tomb.option_leave"));

            var choice = await terminal.GetInput(Loc.Get("ui.your_choice"));

            if (choice.ToUpper() == "O")
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("encounter.tomb.open_push"));
                await Task.Delay(1500);

                if (random.NextDouble() < 0.5)
                {
                    terminal.WriteLine(Loc.Get("encounter.tomb.open_mummy"));
                    int damage = (int)(player.MaxHP / 3);
                    player.HP -= damage;
                    terminal.WriteLine($"-{damage} HP!");
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("encounter.tomb.open_mummy_treasure"));
                    player.Gold += level * 1500;  // Increased from 500 for economic balance
                    terminal.WriteLine(Loc.Get("encounter.reward_gold", level * 1500));
                }
                else
                {
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine(Loc.Get("encounter.tomb.open_dust_1"));
                    terminal.WriteLine(Loc.Get("encounter.tomb.open_dust_2"));
                    player.Gold += level * 2400;  // Increased from 800 for economic balance
                    player.Strength += 2;
                    terminal.WriteLine(Loc.Get("encounter.tomb.open_dust_reward", level * 2400));
                }
            }
            else if (choice.ToUpper() == "T")
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("encounter.tomb.take_careful"));
                player.Gold += level * 600;  // Increased from 200 for economic balance
                terminal.WriteLine(Loc.Get("encounter.reward_gold", level * 600));
            }

            await terminal.PressAnyKey();
        }

        // SEWERS
        private static async Task RatKingEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("encounter.ratking.title_icon"));
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.ratking.desc_1"));
            terminal.WriteLine(Loc.Get("encounter.ratking.desc_2"));
            terminal.WriteLine(Loc.Get("encounter.ratking.desc_3"));
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("P");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.ratking.option_pay"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("F");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.ratking.option_fight"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("R");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.ratking.option_run"));

            var choice = await terminal.GetInput(Loc.Get("ui.your_choice"));

            if (choice.ToUpper() == "P" && player.Gold >= 500)
            {
                player.Gold -= 500;
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("encounter.ratking.pay_accept"));
                terminal.WriteLine(Loc.Get("encounter.ratking.pay_pass"));
                terminal.WriteLine(Loc.Get("encounter.ratking.pay_cache"));
                player.Gold += level * 900;  // Increased from 300 for economic balance
                terminal.SetColor("green");
                terminal.WriteLine(Loc.Get("encounter.reward_gold", level * 900));
            }
            else if (choice.ToUpper() == "F")
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("encounter.ratking.fight_swarm"));
                int damage = level * 5;
                player.HP -= damage;
                terminal.WriteLine(Loc.Get("encounter.ratking.fight_damage", damage));

                if (random.NextDouble() < 0.5)
                {
                    terminal.SetColor("green");
                    terminal.WriteLine(Loc.Get("encounter.ratking.fight_slay"));
                    player.Gold += level * 1200;  // Increased from 400 for economic balance
                    player.Experience += level * 200;
                }
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("encounter.ratking.run_flee"));
            }

            await terminal.PressAnyKey();
        }

        private static async Task LostChildEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("encounter.child.title_icon"));
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.child.desc_1"));
            terminal.WriteLine(Loc.Get("encounter.child.desc_2"));
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("H");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.child.option_help"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("I");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.spirits.option_ignore"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("C");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.child.option_check"));

            var choice = await terminal.GetInput(Loc.Get("ui.your_choice"));

            if (choice.ToUpper() == "H")
            {
                terminal.SetColor("green");
                terminal.WriteLine(Loc.Get("encounter.child.help_guide"));
                terminal.WriteLine(Loc.Get("encounter.child.help_thanks"));

                player.Gold += level * 600;  // Increased from 200 for economic balance
                player.Chivalry += 100;
                terminal.WriteLine(Loc.Get("encounter.child.help_reward", level * 600));
            }
            else if (choice.ToUpper() == "C")
            {
                if (random.NextDouble() < 0.3)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("encounter.child.check_trap_1"));
                    terminal.WriteLine(Loc.Get("encounter.child.check_trap_2"));
                    int damage = (int)(player.MaxHP / 5);
                    player.HP -= damage;
                    terminal.WriteLine($"-{damage} HP!");
                }
                else
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("encounter.child.check_safe"));
                    player.Chivalry += 50;
                }
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("encounter.child.ignore_walk"));
                player.Darkness += 10;
            }

            await terminal.PressAnyKey();
        }

        private static async Task AlchemistLabEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_green");
            terminal.WriteLine(GameConfig.ScreenReaderMode ? Loc.Get("encounter.alchemy.title") : Loc.Get("encounter.alchemy.title_icon"));
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.alchemy.desc_1"));
            terminal.WriteLine(Loc.Get("encounter.alchemy.desc_2"));
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("D");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.alchemy.option_drink"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("R");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.alchemy.option_read"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("S");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.alchemy.option_steal"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("L");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.option_leave"));

            var choice = await terminal.GetInput(Loc.Get("ui.your_choice"));

            if (choice.ToUpper() == "D")
            {
                terminal.SetColor("bright_magenta");
                terminal.WriteLine(Loc.Get("encounter.alchemy.drink_gulp"));
                await Task.Delay(1500);

                var effect = random.Next(5);
                switch (effect)
                {
                    case 0:
                        player.Strength += 5;
                        terminal.SetColor("green");
                        terminal.WriteLine(Loc.Get("encounter.alchemy.drink_power"));
                        break;
                    case 1:
                        player.HP = player.MaxHP;
                        terminal.SetColor("green");
                        terminal.WriteLine(Loc.Get("encounter.alchemy.drink_health"));
                        break;
                    case 2:
                        terminal.SetColor("red");
                        terminal.WriteLine(Loc.Get("encounter.alchemy.drink_poison"));
                        player.Poison += 3;
                        player.PoisonTurns = Math.Max(player.PoisonTurns, 10 + player.Level / 5);
                        break;
                    case 3:
                        player.Intelligence += 5;
                        terminal.SetColor("cyan");
                        terminal.WriteLine(Loc.Get("encounter.alchemy.drink_enlighten"));
                        break;
                    case 4:
                        terminal.SetColor("yellow");
                        terminal.WriteLine(Loc.Get("encounter.alchemy.drink_invisible"));
                        player.Dexterity += 3;
                        break;
                }
            }
            else if (choice.ToUpper() == "R")
            {
                terminal.SetColor("cyan");
                terminal.WriteLine(Loc.Get("encounter.alchemy.read_notes"));
                player.Intelligence += 2;
                player.Experience += level * 100;
                terminal.WriteLine(Loc.Get("encounter.alchemy.read_reward", level * 100));
            }
            else if (choice.ToUpper() == "S")
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("encounter.alchemy.steal_grab"));
                player.Gold += level * 900;  // Increased from 300 for economic balance
                player.Healing = Math.Min(player.MaxPotions, player.Healing + 2);
                terminal.WriteLine(Loc.Get("encounter.alchemy.steal_reward", level * 900));
            }

            await terminal.PressAnyKey();
        }

        private static async Task TreasureHoardEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("encounter.hoard.title"));
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.hoard.desc_1"));
            terminal.WriteLine(Loc.Get("encounter.hoard.desc_2"));
            terminal.WriteLine(Loc.Get("encounter.hoard.desc_3"));
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("T");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.hoard.option_take_all"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("S");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.hoard.option_take_some"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("L");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.hoard.option_leave"));

            var choice = await terminal.GetInput(Loc.Get("ui.your_choice"));

            if (choice.ToUpper() == "T")
            {
                if (random.NextDouble() < 0.4)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("encounter.hoard.trap_thieves"));
                    int damage = (int)(player.MaxHP / 4);
                    player.HP -= damage;
                    terminal.WriteLine($"-{damage} HP!");
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("encounter.hoard.trap_grab"));
                    player.Gold += level * 900;  // Increased from 300 for economic balance
                }
                else
                {
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine(Loc.Get("encounter.hoard.jackpot"));
                    player.Gold += level * 2100;  // Increased from 700 for economic balance
                    terminal.WriteLine(Loc.Get("encounter.reward_gold", level * 2100));
                }
            }
            else if (choice.ToUpper() == "S")
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("encounter.hoard.some_pocket"));
                player.Gold += level * 600;  // Increased from 200 for economic balance
                terminal.WriteLine(Loc.Get("encounter.reward_gold", level * 600));
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("encounter.hoard.leave_caution"));
            }

            await terminal.PressAnyKey();
        }

        // Quick stub implementations for remaining themed encounters
        // (These can be expanded later)

        private static async Task CrystalCaveEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("encounter.crystal.title"));
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.crystal.desc_1"));
            terminal.WriteLine(Loc.Get("encounter.crystal.desc_2"));

            if (random.NextDouble() < 0.6)
            {
                player.Mana = player.MaxMana;
                player.Intelligence += 3;
                terminal.SetColor("green");
                terminal.WriteLine(Loc.Get("encounter.crystal.good_1"));
                terminal.WriteLine(Loc.Get("encounter.crystal.good_2"));
            }
            else
            {
                player.HP -= player.MaxHP / 5;
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("encounter.crystal.bad"));
            }
            await terminal.PressAnyKey();
        }

        private static async Task DragonHoardEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("encounter.dragon_hoard.title"));
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.dragon_hoard.desc_1"));
            terminal.WriteLine(Loc.Get("encounter.dragon_hoard.desc_2"));

            long hoardGold = level * 3000;
            player.Gold += hoardGold;
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("encounter.dragon_hoard.grab", hoardGold.ToString("N0")));
            await terminal.PressAnyKey();
        }

        private static async Task DwarvenOutpostEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("encounter.dwarven.title_icon"));
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("encounter.dwarven.desc_1"), "white");
            terminal.WriteLine(Loc.Get("encounter.dwarven.desc_2"));

            player.Healing = Math.Min(player.MaxPotions, player.Healing + 3);
            terminal.SetColor("green");
            terminal.WriteLine(Loc.Get("encounter.dwarven.reward_1"));
            terminal.WriteLine(Loc.Get("encounter.dwarven.reward_2"));
            await terminal.PressAnyKey();
        }

        private static async Task UndergroundLakeEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("blue");
            terminal.WriteLine(Loc.Get("encounter.lake.title_icon"));
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("encounter.lake.desc_1"), "white");
            terminal.WriteLine(Loc.Get("encounter.lake.desc_2"));

            player.HP = player.MaxHP;
            player.Mana = player.MaxMana;
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("encounter.lake.reward"));
            await terminal.PressAnyKey();
        }

        private static async Task AncientGolemEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("encounter.golem.title_icon"));
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("encounter.golem.desc_1"), "white");
            terminal.WriteLine(Loc.Get("encounter.golem.desc_2"));
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("encounter.golem.riddle"));

            var answer = await terminal.GetInput(Loc.Get("encounter.golem.prompt"));
            if (answer.ToLower().Contains("piano") || answer.ToLower().Contains("keyboard"))
            {
                terminal.SetColor("green");
                terminal.WriteLine(Loc.Get("encounter.golem.correct"));
                player.Experience += level * 200;
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("encounter.golem.wrong"));
                player.HP -= player.MaxHP / 4;
            }
            await terminal.PressAnyKey();
        }

        private static async Task TimeCapsuleEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("encounter.capsule.title_icon"));
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("encounter.capsule.desc_1"), "white");
            terminal.WriteLine(Loc.Get("encounter.capsule.desc_2"));

            player.Gold += level * 1200;  // Increased from 400 for economic balance
            player.Experience += level * 150;
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("encounter.reward_gold_exp", level * 400, level * 150));
            await terminal.PressAnyKey();
        }

        private static async Task MagicFountainEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("encounter.fountain.title_icon"));
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("encounter.fountain.desc"), "white");

            player.HP = player.MaxHP;
            player.Mana = player.MaxMana;
            player.Poison = 0;
            player.PoisonTurns = 0;
            terminal.SetColor("green");
            terminal.WriteLine(Loc.Get("encounter.fountain.reward"));
            await terminal.PressAnyKey();
        }

        private static async Task LostCivilizationEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("encounter.civilization.title_icon"));
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("encounter.civilization.desc_1"), "white");
            terminal.WriteLine(Loc.Get("encounter.civilization.desc_2"));

            player.Intelligence += 5;
            player.Experience += level * 300;
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("encounter.civilization.reward", level * 300));
            await terminal.PressAnyKey();
        }

        // Demon Lair encounters
        private static async Task DemonBargainEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("encounter.demon_bargain.title_icon"));
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("encounter.demon_bargain.desc_1"), "white");
            terminal.WriteLine(Loc.Get("encounter.demon_bargain.desc_2"));
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("A");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.demon_bargain.option_accept"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("R");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.demon_bargain.option_refuse"));

            var choice = await terminal.GetInput(Loc.Get("ui.your_choice"));
            if (choice.ToUpper() == "A")
            {
                player.Strength += 10;
                player.Darkness += 100;
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("encounter.demon_bargain.accept_result"));
            }
            else
            {
                terminal.SetColor("green");
                terminal.WriteLine(Loc.Get("encounter.demon_bargain.refuse_result"));
            }
            await terminal.PressAnyKey();
        }

        private static async Task TorturedSoulsEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("encounter.tortured.title_icon"));
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("encounter.tortured.desc"), "white");

            player.Chivalry += 30;
            player.Experience += level * 100;
            terminal.WriteLine(Loc.Get("encounter.tortured.reward"), "green");
            await terminal.PressAnyKey();
        }

        private static async Task InfernalForgeEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_red");
            terminal.WriteLine(GameConfig.ScreenReaderMode ? Loc.Get("encounter.forge.title") : Loc.Get("encounter.forge.title_icon"));
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("encounter.forge.desc_1"), "white");
            terminal.WriteLine(Loc.Get("encounter.forge.desc_2"), "white");
            terminal.WriteLine(Loc.Get("encounter.forge.desc_3"), "white");
            terminal.WriteLine(Loc.Get("encounter.forge.desc_4"), "white");
            terminal.WriteLine("");

            player.BonusWeapPow += 5;
            player.RecalculateStats();
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("encounter.forge.reward"));
            await terminal.PressAnyKey();
        }

        private static async Task SuccubusEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("magenta");
            terminal.WriteLine(Loc.Get("encounter.succubus.title_icon"));
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("encounter.succubus.desc_1"), "white");
            terminal.WriteLine(Loc.Get("encounter.succubus.desc_2"));
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("R");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.succubus.option_resist"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("S");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.succubus.option_succumb"));

            var choice = await terminal.GetInput(Loc.Get("ui.your_choice"));
            if (choice.ToUpper() == "R")
            {
                player.Wisdom += 5;
                terminal.SetColor("green");
                terminal.WriteLine(Loc.Get("encounter.succubus.resist_result"));
            }
            else
            {
                player.HP -= player.MaxHP / 3;
                player.Gold = player.Gold * 8 / 10;
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("encounter.succubus.succumb_result"));
            }
            await terminal.PressAnyKey();
        }

        // Frozen Depths encounters
        private static async Task FrozenAdventurerEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("encounter.frozen.title_icon"));
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("encounter.frozen.desc_1"), "white");
            terminal.WriteLine(Loc.Get("encounter.frozen.desc_2"));

            player.Gold += level * 900;  // Increased from 300 for economic balance
            player.Healing = Math.Min(player.MaxPotions, player.Healing + 2);
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("encounter.frozen.reward"));
            await terminal.PressAnyKey();
        }

        private static async Task IceQueenEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_white");
            terminal.WriteLine(Loc.Get("encounter.ice_queen.title"));
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("encounter.ice_queen.desc_1"), "white");
            terminal.WriteLine(Loc.Get("encounter.ice_queen.desc_2"));

            player.Charisma += 3;
            player.Mana = player.MaxMana;
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("encounter.ice_queen.reward"));
            await terminal.PressAnyKey();
        }

        private static async Task YetiDenEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.yeti.title_icon"));
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("encounter.yeti.desc_1"), "white");
            terminal.WriteLine(Loc.Get("encounter.yeti.desc_2"));

            if (random.NextDouble() < 0.5)
            {
                player.HP -= player.MaxHP / 4;
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("encounter.yeti.mauled"));
                player.Gold += level * 1500;  // Increased from 500 for economic balance
            }
            else
            {
                terminal.SetColor("green");
                terminal.WriteLine(Loc.Get("encounter.yeti.back_away"));
            }
            await terminal.PressAnyKey();
        }

        private static async Task AuroraVisionEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("encounter.aurora.title"));
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("encounter.aurora.desc_1"), "white");
            terminal.WriteLine(Loc.Get("encounter.aurora.desc_2"));

            player.HP = player.MaxHP;
            player.Mana = player.MaxMana;
            player.Wisdom += 3;
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("encounter.aurora.reward"));
            await terminal.PressAnyKey();
        }

        // Volcanic Pit encounters
        private static async Task FireElementalEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_red");
            terminal.WriteLine(GameConfig.ScreenReaderMode ? Loc.Get("encounter.fire_elemental.title") : Loc.Get("encounter.fire_elemental.title_icon"));
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("encounter.fire_elemental.desc_1"), "white");
            terminal.WriteLine(Loc.Get("encounter.fire_elemental.desc_2"));

            player.Intelligence += 3;
            player.MaxMana += 20;
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("encounter.fire_elemental.reward"));
            await terminal.PressAnyKey();
        }

        private static async Task LavaBoatEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("encounter.lava_boat.title_icon"));
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("encounter.lava_boat.desc_1"), "white");
            terminal.WriteLine(Loc.Get("encounter.lava_boat.desc_2"));

            if (player.Gold >= 500)
            {
                player.Gold -= 500;
                player.Experience += level * 300;
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("encounter.lava_boat.cross", level * 300));
            }
            else
            {
                terminal.WriteLine(Loc.Get("encounter.lava_boat.no_gold"), "gray");
            }
            await terminal.PressAnyKey();
        }

        private static async Task PhoenixNestEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("encounter.phoenix.title_icon"));
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("encounter.phoenix.desc_1"), "white");
            terminal.WriteLine(Loc.Get("encounter.phoenix.desc_2"));

            player.HP = player.MaxHP;
            player.Constitution += 5;
            terminal.SetColor("bright_red");
            terminal.WriteLine(Loc.Get("encounter.phoenix.reward"));
            await terminal.PressAnyKey();
        }

        private static async Task ObsidianMirrorEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("encounter.mirror.title_icon"));
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("encounter.mirror.desc_1"), "white");
            terminal.WriteLine(Loc.Get("encounter.mirror.desc_2"));

            player.Strength += 2;
            player.Intelligence += 2;
            player.Dexterity += 2;
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("encounter.mirror.reward"));
            await terminal.PressAnyKey();
        }

        // Abyssal Void encounters
        private static async Task VoidWhisperEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("magenta");
            terminal.WriteLine(Loc.Get("encounter.void.title_icon"));
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("encounter.void.desc"), "white");

            player.Intelligence += 5;
            player.Wisdom += 5;
            player.Darkness += 30;
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("encounter.void.reward"));
            await terminal.PressAnyKey();
        }

        private static async Task RealityTearEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_white");
            terminal.WriteLine(GameConfig.ScreenReaderMode ? Loc.Get("encounter.reality.title") : Loc.Get("encounter.reality.title_icon"));
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("encounter.reality.desc_1"), "white");
            terminal.WriteLine(Loc.Get("encounter.reality.desc_2"));

            if (random.NextDouble() < 0.5)
            {
                player.Gold += level * 3000;  // Increased from 1000 for economic balance
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("encounter.reality.good"));
            }
            else
            {
                player.HP -= player.MaxHP / 3;
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("encounter.reality.bad"));
            }
            await terminal.PressAnyKey();
        }

        private static async Task CosmicEntityEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("encounter.cosmic.title_icon"));
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("encounter.cosmic.desc_1"), "white");
            terminal.WriteLine(Loc.Get("encounter.cosmic.desc_2"));

            player.Experience += level * 500;
            player.Wisdom += 10;
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("encounter.cosmic.reward"));
            await terminal.PressAnyKey();
        }

        private static async Task MadnessPoolEncounter(TerminalEmulator terminal, Character player, int level)
        {
            terminal.ClearScreen();
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("encounter.madness.title_icon"));
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("encounter.madness.desc_1"), "white");
            terminal.WriteLine(Loc.Get("encounter.madness.desc_2"));
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("L");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.madness.option_look"));
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("A");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("encounter.madness.option_avoid"));

            var choice = await terminal.GetInput(Loc.Get("ui.your_choice"));
            if (choice.ToUpper() == "L")
            {
                if (random.NextDouble() < 0.5)
                {
                    player.Intelligence += 10;
                    player.Darkness += 50;
                    terminal.SetColor("magenta");
                    terminal.WriteLine(Loc.Get("encounter.madness.look_good"));
                }
                else
                {
                    player.Intelligence = Math.Max(1, player.Intelligence - 5);
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("encounter.madness.look_bad"));
                }
            }
            else
            {
                terminal.SetColor("green");
                terminal.WriteLine(Loc.Get("encounter.madness.avoid"));
            }
            await terminal.PressAnyKey();
        }
    }
}
