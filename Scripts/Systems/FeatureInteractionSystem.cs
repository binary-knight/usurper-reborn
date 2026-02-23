using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UsurperRemake.UI;

namespace UsurperRemake.Systems;

/// <summary>
/// Enhanced feature interaction system that makes dungeon features meaningful
/// Features now provide: lore, meaningful choices, skill challenges, and story connections
/// </summary>
public class FeatureInteractionSystem
{
    private static FeatureInteractionSystem? _instance;
    public static FeatureInteractionSystem Instance => _instance ??= new FeatureInteractionSystem();

    private Random random = new Random();

    /// <summary>
    /// Process a feature interaction with meaningful outcomes
    /// </summary>
    public async Task<FeatureOutcome> InteractWithFeature(
        RoomFeature feature,
        Character player,
        int dungeonLevel,
        DungeonTheme theme,
        TerminalEmulator terminal)
    {
        var outcome = new FeatureOutcome();

        // Determine what kind of interaction this will be
        var interactionType = DetermineInteractionType(feature, player, dungeonLevel);

        switch (interactionType)
        {
            case FeatureInteractionType.LoreDiscovery:
                await HandleLoreDiscovery(feature, player, dungeonLevel, theme, terminal, outcome);
                break;

            case FeatureInteractionType.SkillChallenge:
                await HandleSkillChallenge(feature, player, dungeonLevel, terminal, outcome);
                break;

            case FeatureInteractionType.MoralChoice:
                await HandleMoralChoice(feature, player, dungeonLevel, terminal, outcome);
                break;

            case FeatureInteractionType.ClassSpecific:
                await HandleClassSpecific(feature, player, dungeonLevel, terminal, outcome);
                break;

            case FeatureInteractionType.RiskReward:
                await HandleRiskReward(feature, player, dungeonLevel, terminal, outcome);
                break;

            case FeatureInteractionType.MemoryTrigger:
                await HandleMemoryTrigger(feature, player, dungeonLevel, terminal, outcome);
                break;

            case FeatureInteractionType.OceanInsight:
                await HandleOceanInsight(feature, player, dungeonLevel, terminal, outcome);
                break;

            default:
                await HandleStandardInteraction(feature, player, dungeonLevel, terminal, outcome);
                break;
        }

        return outcome;
    }

    private FeatureInteractionType DetermineInteractionType(RoomFeature feature, Character player, int dungeonLevel)
    {
        // Weight different interaction types based on context
        var roll = random.Next(100);

        // Higher levels get more interesting interactions
        int loreChance = 15 + (dungeonLevel / 10);
        int skillChance = 20;
        int moralChance = 10 + (dungeonLevel / 20);
        int classChance = 15;
        int riskChance = 20;
        int memoryChance = 5 + (dungeonLevel / 15);
        int oceanChance = 5 + (dungeonLevel / 25);

        // Feature type influences what happens
        if (feature.Interaction == FeatureInteraction.Read)
        {
            loreChance += 30;
            oceanChance += 15;
        }
        else if (feature.Interaction == FeatureInteraction.Examine)
        {
            memoryChance += 10;
            loreChance += 10;
        }
        else if (feature.Interaction == FeatureInteraction.Open || feature.Interaction == FeatureInteraction.Break)
        {
            riskChance += 20;
            skillChance += 10;
        }

        // Near Old God floors, boost relevant interactions
        if (IsNearOldGodFloor(dungeonLevel))
        {
            loreChance += 20;
            oceanChance += 20;
            memoryChance += 15;
        }

        int cumulative = 0;
        if (roll < (cumulative += loreChance)) return FeatureInteractionType.LoreDiscovery;
        if (roll < (cumulative += skillChance)) return FeatureInteractionType.SkillChallenge;
        if (roll < (cumulative += moralChance)) return FeatureInteractionType.MoralChoice;
        if (roll < (cumulative += classChance)) return FeatureInteractionType.ClassSpecific;
        if (roll < (cumulative += riskChance)) return FeatureInteractionType.RiskReward;
        if (roll < (cumulative += memoryChance)) return FeatureInteractionType.MemoryTrigger;
        if (roll < (cumulative += oceanChance)) return FeatureInteractionType.OceanInsight;

        return FeatureInteractionType.Standard;
    }

    private bool IsNearOldGodFloor(int level)
    {
        int[] godFloors = { 25, 40, 55, 70, 85, 95, 100 };
        return godFloors.Any(f => Math.Abs(level - f) <= 5);
    }

    #region Lore Discovery

    private async Task HandleLoreDiscovery(RoomFeature feature, Character player, int level,
        DungeonTheme theme, TerminalEmulator terminal, FeatureOutcome outcome)
    {
        var lore = GetLoreForLevel(level, theme, feature);

        terminal.SetColor("cyan");
        terminal.WriteLine($"You {feature.Interaction.ToString().ToLower()} the {feature.Name}...");
        terminal.WriteLine("");
        await Task.Delay(800);

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("Something catches your attention:");
        terminal.WriteLine("");
        terminal.SetColor("white");

        foreach (var line in lore.Text)
        {
            terminal.WriteLine($"  {line}");
            await Task.Delay(400);
        }

        terminal.WriteLine("");
        await Task.Delay(500);

        // Apply lore effects
        if (lore.GrantsExperience)
        {
            long xp = CalculateScaledReward(level, 50, 150);
            player.Experience += xp;
            terminal.SetColor("yellow");
            terminal.WriteLine($"This knowledge enlightens you. (+{xp} XP)");
            outcome.ExperienceGained = xp;
        }

        if (lore.AffectsAlignment)
        {
            // Use ChivNr (good deeds) and DarkNr (dark deeds) for alignment
            if (lore.AlignmentShift > 0)
                player.ChivNr += Math.Abs(lore.AlignmentShift);
            else
                player.DarkNr += Math.Abs(lore.AlignmentShift);
            string direction = lore.AlignmentShift > 0 ? "light" : "darkness";
            terminal.SetColor(lore.AlignmentShift > 0 ? "bright_yellow" : "dark_magenta");
            terminal.WriteLine($"You feel drawn toward the {direction}...");
        }

        if (lore.RevealsOldGod != null)
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"You sense {lore.RevealsOldGod}'s presence in these depths...");
            // Could trigger story flag here
        }

        // Track for achievements
        outcome.LoreDiscovered = true;
        outcome.Success = true;

        await terminal.PressAnyKey();
    }

    private LoreFragment GetLoreForLevel(int level, DungeonTheme theme, RoomFeature feature)
    {
        // Near Old God floors, give god-specific lore
        if (level >= 20 && level <= 30)
            return GetMaelkethLore(feature);
        if (level >= 35 && level <= 45)
            return GetVelouraLore(feature);
        if (level >= 50 && level <= 60)
            return GetThorgrimLore(feature);
        if (level >= 65 && level <= 75)
            return GetNocturaLore(feature);
        if (level >= 80 && level <= 90)
            return GetAurelionLore(feature);
        if (level >= 90 && level <= 100)
            return GetTerravokLore(feature);

        // Theme-based lore for other levels
        return GetThemeLore(theme, level, feature);
    }

    private LoreFragment GetMaelkethLore(RoomFeature feature)
    {
        var fragments = new List<LoreFragment>
        {
            new()
            {
                Text = new[]
                {
                    "\"Before the breaking, Maelketh was the shield of mortals.\"",
                    "\"He stood against the void when others fled.\"",
                    "\"But endless war corrodes even divine steel...\""
                },
                GrantsExperience = true,
                RevealsOldGod = "Maelketh"
            },
            new()
            {
                Text = new[]
                {
                    "Ancient battle records, written in dried blood:",
                    "\"The Broken Blade did not break in battle.\"",
                    "\"He broke himself, to spare his followers.\""
                },
                GrantsExperience = true,
                RevealsOldGod = "Maelketh"
            },
            new()
            {
                Text = new[]
                {
                    "A warrior's oath, carved into stone:",
                    "\"I swear by Maelketh's unwounded heart,\"",
                    "\"that I shall never raise blade against the innocent.\"",
                    "",
                    "The stone is cracked. The oath was broken."
                },
                GrantsExperience = true,
                AffectsAlignment = true,
                AlignmentShift = 5
            }
        };
        return fragments[random.Next(fragments.Count)];
    }

    private LoreFragment GetVelouraLore(RoomFeature feature)
    {
        var fragments = new List<LoreFragment>
        {
            new()
            {
                Text = new[]
                {
                    "Rose petals, somehow still fragrant after centuries:",
                    "\"She loved so deeply that her heart became the world.\"",
                    "\"When the world broke, so did she.\""
                },
                GrantsExperience = true,
                RevealsOldGod = "Veloura"
            },
            new()
            {
                Text = new[]
                {
                    "A love letter, never delivered:",
                    "\"The Fading Heart taught us that love is not weakness.\"",
                    "\"It is the only strength that matters.\"",
                    "\"Remember this when you face her.\""
                },
                GrantsExperience = true,
                AffectsAlignment = true,
                AlignmentShift = 10
            }
        };
        return fragments[random.Next(fragments.Count)];
    }

    private LoreFragment GetThorgrimLore(RoomFeature feature)
    {
        var fragments = new List<LoreFragment>
        {
            new()
            {
                Text = new[]
                {
                    "Court records, brittle with age:",
                    "\"The Unjust Judge was once called The Righteous.\"",
                    "\"But justice without mercy becomes cruelty.\"",
                    "\"And he had forgotten mercy long before the breaking.\""
                },
                GrantsExperience = true,
                RevealsOldGod = "Thorgrim"
            }
        };
        return fragments[random.Next(fragments.Count)];
    }

    private LoreFragment GetNocturaLore(RoomFeature feature)
    {
        var fragments = new List<LoreFragment>
        {
            new()
            {
                Text = new[]
                {
                    "The shadows seem to whisper:",
                    "\"She was not always the Shadow Queen.\"",
                    "\"Once, she was the Light's own sister.\"",
                    "\"What changed her? The same thing that will change you.\""
                },
                GrantsExperience = true,
                RevealsOldGod = "Noctura",
                AffectsAlignment = true,
                AlignmentShift = -5
            }
        };
        return fragments[random.Next(fragments.Count)];
    }

    private LoreFragment GetAurelionLore(RoomFeature feature)
    {
        var fragments = new List<LoreFragment>
        {
            new()
            {
                Text = new[]
                {
                    "Light dances across ancient script:",
                    "\"The Dimming Light burns brightest before going dark.\"",
                    "\"He still believes he can save everyone.\"",
                    "\"That belief may be his salvation... or his doom.\""
                },
                GrantsExperience = true,
                RevealsOldGod = "Aurelion",
                AffectsAlignment = true,
                AlignmentShift = 15
            }
        };
        return fragments[random.Next(fragments.Count)];
    }

    private LoreFragment GetTerravokLore(RoomFeature feature)
    {
        var fragments = new List<LoreFragment>
        {
            new()
            {
                Text = new[]
                {
                    "The stone itself seems to speak:",
                    "\"The Worldbreaker sleeps, but dreams.\"",
                    "\"In his dreams, mountains rise and fall.\"",
                    "\"Pray he does not dream of you.\""
                },
                GrantsExperience = true,
                RevealsOldGod = "Terravok"
            }
        };
        return fragments[random.Next(fragments.Count)];
    }

    private LoreFragment GetThemeLore(DungeonTheme theme, int level, RoomFeature feature)
    {
        var fragments = theme switch
        {
            DungeonTheme.Catacombs => new List<LoreFragment>
            {
                new()
                {
                    Text = new[]
                    {
                        "An epitaph, surprisingly well-preserved:",
                        "\"Here lies one who remembered.\"",
                        "\"May the next cycle treat them kinder.\""
                    },
                    GrantsExperience = true
                },
                new()
                {
                    Text = new[]
                    {
                        "Scratched into the wall by desperate fingers:",
                        "\"They told me I had lived before.\"",
                        "\"I did not believe them.\"",
                        "\"Now I remember everything.\""
                    },
                    GrantsExperience = true
                }
            },
            DungeonTheme.AncientRuins => new List<LoreFragment>
            {
                new()
                {
                    Text = new[]
                    {
                        "Ancient text, partially translated:",
                        "\"The cycle was not always a prison.\"",
                        "\"Once, it was a gift - a chance to learn.\"",
                        "\"Manwe twisted it when he lost faith in us.\""
                    },
                    GrantsExperience = true
                },
                new()
                {
                    Text = new[]
                    {
                        "A scholar's final notes:",
                        "\"Seven Seals. Seven Truths. Seven chances.\"",
                        "\"But the eighth truth was hidden from us.\"",
                        "\"Find it, and the cycle ends.\""
                    },
                    GrantsExperience = true
                }
            },
            _ => new List<LoreFragment>
            {
                new()
                {
                    Text = new[]
                    {
                        "Someone has been here before you.",
                        "They left a message: \"Trust your instincts.\"",
                        "\"The answer is always in the question.\""
                    },
                    GrantsExperience = true
                }
            }
        };

        return fragments[random.Next(fragments.Count)];
    }

    #endregion

    #region Skill Challenge

    private async Task HandleSkillChallenge(RoomFeature feature, Character player, int level,
        TerminalEmulator terminal, FeatureOutcome outcome)
    {
        // Determine which stat to test based on feature type
        var (stat, statName, statValue) = GetRelevantStat(feature, player);

        terminal.SetColor("cyan");
        terminal.WriteLine($"You {feature.Interaction.ToString().ToLower()} the {feature.Name}...");
        terminal.WriteLine("");
        await Task.Delay(600);

        // Determine difficulty based on dungeon level
        // DC scales gently: 8 at floor 1, 20 at floor 50, 33 at floor 100
        int difficulty = 8 + (level / 4);
        int roll = random.Next(1, 21);
        // Cap stat bonus at 20 to keep checks meaningful at endgame
        // (raw stat / 10 gives 50+ at high stats, trivializing all checks)
        int statBonus = Math.Min(statValue / 10, 20);
        int total = roll + statBonus;
        bool success = total >= difficulty;

        terminal.SetColor("yellow");
        terminal.WriteLine($"[{statName} Check - DC {difficulty}]");
        terminal.SetColor("white");
        terminal.WriteLine($"You rolled {roll} + {statBonus} ({statName} bonus) = {total}");
        await Task.Delay(800);
        terminal.WriteLine("");

        if (success)
        {
            await HandleSkillSuccess(feature, player, level, terminal, outcome, statName);
        }
        else
        {
            await HandleSkillFailure(feature, player, level, terminal, outcome, statName);
        }

        outcome.Success = success;
        await terminal.PressAnyKey();
    }

    private (string stat, string name, int value) GetRelevantStat(RoomFeature feature, Character player)
    {
        return feature.Interaction switch
        {
            FeatureInteraction.Open => ("STR", "Strength", (int)player.Strength),
            FeatureInteraction.Search => ("INT", "Intelligence", (int)player.Intelligence),
            FeatureInteraction.Read => ("INT", "Intelligence", (int)player.Intelligence),
            FeatureInteraction.Take => ("DEX", "Dexterity", (int)player.Dexterity),
            FeatureInteraction.Break => ("STR", "Strength", (int)player.Strength),
            FeatureInteraction.Use => ("WIS", "Wisdom", (int)player.Wisdom),
            FeatureInteraction.Enter => ("DEX", "Dexterity", (int)player.Dexterity),
            _ => ("WIS", "Wisdom", (int)player.Wisdom)
        };
    }

    private async Task HandleSkillSuccess(RoomFeature feature, Character player, int level,
        TerminalEmulator terminal, FeatureOutcome outcome, string stat)
    {
        terminal.SetColor("bright_green");
        terminal.WriteLine("SUCCESS!");
        terminal.WriteLine("");
        await Task.Delay(500);

        // Better rewards for skill successes
        var rewardType = random.Next(100);

        if (rewardType < 30)
        {
            // Stat boost (temporary but useful)
            int boost = 2 + (level / 20);
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"Your expertise reveals hidden knowledge!");
            terminal.WriteLine($"+{boost} {stat} for the rest of this floor!");

            // Apply temporary stat boost based on stat name
            switch (stat)
            {
                case "Strength": player.TempAttackBonus += boost; break;
                case "Dexterity": player.TempDefenseBonus += boost; break;
                case "Intelligence": player.Mana = Math.Min(player.MaxMana, player.Mana + boost * 5); break;
            }
        }
        else if (rewardType < 60)
        {
            // Good loot
            long gold = CalculateScaledReward(level, 100, 300);
            player.Gold += gold;
            terminal.SetColor("yellow");
            terminal.WriteLine($"Your skill is rewarded handsomely! +{gold} gold!");
            outcome.GoldGained = gold;
        }
        else if (rewardType < 80)
        {
            // Healing potions
            int potions = 1 + (level / 30);
            player.Healing = Math.Min(player.MaxPotions, player.Healing + potions);
            terminal.SetColor("green");
            terminal.WriteLine($"You find {potions} healing potion{(potions > 1 ? "s" : "")} hidden inside!");
        }
        else
        {
            // XP bonus
            long xp = CalculateScaledReward(level, 75, 200);
            player.Experience += xp;
            terminal.SetColor("yellow");
            terminal.WriteLine($"The challenge itself teaches you something. +{xp} XP!");
            outcome.ExperienceGained = xp;
        }
    }

    private async Task HandleSkillFailure(RoomFeature feature, Character player, int level,
        TerminalEmulator terminal, FeatureOutcome outcome, string stat)
    {
        terminal.SetColor("red");
        terminal.WriteLine("FAILED!");
        terminal.WriteLine("");
        await Task.Delay(500);

        var failureType = random.Next(100);

        if (failureType < 40)
        {
            // Minor consequence - small damage
            long damage = level + random.Next(1, 10);
            player.HP -= damage;
            terminal.SetColor("red");
            terminal.WriteLine($"Your failure costs you. -{damage} HP");
            outcome.DamageTaken = damage;
        }
        else if (failureType < 70)
        {
            // No consequence, just failure
            terminal.SetColor("gray");
            terminal.WriteLine("Nothing happens. Perhaps another approach would work.");
        }
        else
        {
            // Learn from failure
            long xp = CalculateScaledReward(level, 20, 50);
            player.Experience += xp;
            terminal.SetColor("cyan");
            terminal.WriteLine($"You learn from your mistake. +{xp} XP");
            outcome.ExperienceGained = xp;
        }
    }

    #endregion

    #region Moral Choice

    private async Task HandleMoralChoice(RoomFeature feature, Character player, int level,
        TerminalEmulator terminal, FeatureOutcome outcome)
    {
        var choice = GetMoralChoice(feature, level);

        terminal.SetColor("cyan");
        terminal.WriteLine($"You {feature.Interaction.ToString().ToLower()} the {feature.Name}...");
        terminal.WriteLine("");
        await Task.Delay(600);

        terminal.SetColor("white");
        foreach (var line in choice.Situation)
        {
            terminal.WriteLine(line);
            await Task.Delay(300);
        }

        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine("What do you do?");
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"[1] {choice.Option1.Description}");
        terminal.SetColor("bright_magenta");
        terminal.WriteLine($"[2] {choice.Option2.Description}");
        terminal.SetColor("gray");
        terminal.WriteLine("[3] Walk away");
        terminal.WriteLine("");

        var input = await terminal.GetInput("> ");
        terminal.WriteLine("");

        switch (input.Trim())
        {
            case "1":
                await ApplyMoralChoice(player, choice.Option1, terminal, outcome, level);
                break;
            case "2":
                await ApplyMoralChoice(player, choice.Option2, terminal, outcome, level);
                break;
            default:
                terminal.SetColor("gray");
                terminal.WriteLine("You decide this is not your concern.");
                terminal.WriteLine("Sometimes wisdom is knowing when to walk away.");
                break;
        }

        outcome.ChoiceMade = true;
        await terminal.PressAnyKey();
    }

    private async Task ApplyMoralChoice(Character player, MoralOption option, TerminalEmulator terminal,
        FeatureOutcome outcome, int level)
    {
        terminal.SetColor(option.IsLight ? "bright_yellow" : "dark_magenta");
        terminal.WriteLine(option.ResultText);
        terminal.WriteLine("");
        await Task.Delay(500);

        // Apply alignment shift using ChivNr (good) and DarkNr (dark)
        if (option.AlignmentShift != 0)
        {
            if (option.AlignmentShift > 0)
                player.ChivNr += option.AlignmentShift;
            else
                player.DarkNr += Math.Abs(option.AlignmentShift);
            string direction = option.AlignmentShift > 0 ? "light" : "darkness";
            terminal.WriteLine($"Your soul shifts toward the {direction}...");
        }

        // Apply rewards/consequences
        if (option.GoldReward != 0)
        {
            player.Gold += option.GoldReward;
            terminal.SetColor(option.GoldReward > 0 ? "yellow" : "red");
            terminal.WriteLine(option.GoldReward > 0 ? $"+{option.GoldReward} gold" : $"{option.GoldReward} gold");
            outcome.GoldGained = option.GoldReward;
        }

        if (option.ExperienceReward != 0)
        {
            long scaledXP = CalculateScaledReward(level, (int)option.ExperienceReward, (int)(option.ExperienceReward * 2));
            player.Experience += scaledXP;
            terminal.SetColor("yellow");
            terminal.WriteLine($"+{scaledXP} experience");
            outcome.ExperienceGained = scaledXP;
        }

        if (option.HealthChange != 0)
        {
            player.HP += option.HealthChange;
            player.HP = Math.Clamp(player.HP, 0, player.MaxHP);
            terminal.SetColor(option.HealthChange > 0 ? "green" : "red");
            terminal.WriteLine(option.HealthChange > 0 ? $"+{option.HealthChange} HP" : $"{option.HealthChange} HP");
        }

        outcome.Success = true;
    }

    private MoralChoice GetMoralChoice(RoomFeature feature, int level)
    {
        var choices = new List<MoralChoice>
        {
            new()
            {
                Situation = new[]
                {
                    "Inside, you find a dying creature - neither monster nor man.",
                    "It begs for mercy, but its kind has slaughtered many innocents.",
                    "Its eyes hold genuine fear... and perhaps regret."
                },
                Option1 = new MoralOption
                {
                    Description = "End its suffering quickly",
                    ResultText = "You grant it a swift death. It thanks you with its last breath.",
                    AlignmentShift = 5,
                    ExperienceReward = 30,
                    IsLight = true
                },
                Option2 = new MoralOption
                {
                    Description = "Let it suffer for its crimes",
                    ResultText = "You leave it to die slowly. Justice? Or cruelty? The line blurs.",
                    AlignmentShift = -10,
                    GoldReward = 50,
                    IsLight = false
                }
            },
            new()
            {
                Situation = new[]
                {
                    "A chest filled with gold coins... and a note.",
                    "\"This was stolen from orphans. I hid it here in shame.\"",
                    "\"If you find this, return it to the Temple of Light.\"",
                    "No one would ever know if you kept it."
                },
                Option1 = new MoralOption
                {
                    Description = "Take only what you need",
                    ResultText = "You take a small portion. Survival requires compromise.",
                    AlignmentShift = -3,
                    GoldReward = 100,
                    IsLight = false
                },
                Option2 = new MoralOption
                {
                    Description = "Leave it for the rightful owners",
                    ResultText = "You leave the gold untouched. Your conscience is worth more.",
                    AlignmentShift = 15,
                    ExperienceReward = 50,
                    IsLight = true
                }
            },
            new()
            {
                Situation = new[]
                {
                    "A magical trap guards this passage. You could disarm it...",
                    "Or leave it active to harm whatever follows you.",
                    "You hear distant footsteps. Something is tracking you."
                },
                Option1 = new MoralOption
                {
                    Description = "Disarm it - traps are dishonorable",
                    ResultText = "You carefully disable the mechanism. Honor matters, even here.",
                    AlignmentShift = 10,
                    ExperienceReward = 40,
                    IsLight = true
                },
                Option2 = new MoralOption
                {
                    Description = "Leave it armed - survival first",
                    ResultText = "You slip past carefully. What follows you deserves no mercy.",
                    AlignmentShift = -5,
                    HealthChange = 20, // Easier path forward
                    IsLight = false
                }
            },
            new()
            {
                Situation = new[]
                {
                    "A spirit appears - the ghost of a fallen adventurer.",
                    "\"I cannot rest until my family knows my fate.\"",
                    "\"Carry my ring to them... but the depths are dangerous.\"",
                    "The ring is worth a fortune. No one would know."
                },
                Option1 = new MoralOption
                {
                    Description = "Swear to deliver the ring",
                    ResultText = "The spirit blesses you and fades peacefully.",
                    AlignmentShift = 20,
                    ExperienceReward = 75,
                    HealthChange = 50, // Blessing
                    IsLight = true
                },
                Option2 = new MoralOption
                {
                    Description = "Take the ring, ignore the plea",
                    ResultText = "The spirit wails and vanishes. The ring feels cold.",
                    AlignmentShift = -20,
                    GoldReward = 300,
                    IsLight = false
                }
            }
        };

        return choices[random.Next(choices.Count)];
    }

    #endregion

    #region Class Specific

    private async Task HandleClassSpecific(RoomFeature feature, Character player, int level,
        TerminalEmulator terminal, FeatureOutcome outcome)
    {
        terminal.SetColor("cyan");
        terminal.WriteLine($"You {feature.Interaction.ToString().ToLower()} the {feature.Name}...");
        terminal.WriteLine("");
        await Task.Delay(600);

        var className = player.Class.ToString();
        terminal.SetColor("bright_magenta");
        var article = "aeiouAEIOU".Contains(className[0]) ? "an" : "a";
        terminal.WriteLine($"Your training as {article} {className} reveals something others would miss:");
        terminal.WriteLine("");
        await Task.Delay(400);

        await ApplyClassSpecificBonus(player, level, terminal, outcome);

        outcome.Success = true;
        await terminal.PressAnyKey();
    }

    private async Task ApplyClassSpecificBonus(Character player, int level,
        TerminalEmulator terminal, FeatureOutcome outcome)
    {
        switch (player.Class)
        {
            case CharacterClass.Warrior:
            case CharacterClass.Barbarian:
                terminal.SetColor("bright_red");
                terminal.WriteLine("You spot weaknesses in the dungeon's defenses.");
                terminal.WriteLine("The next battle will be easier.");
                player.TempAttackBonus += 5 + (level / 10);
                terminal.SetColor("yellow");
                terminal.WriteLine($"+{5 + (level / 10)} Attack Power for next combat!");
                break;

            case CharacterClass.Magician:
            case CharacterClass.Sage:
                terminal.SetColor("bright_cyan");
                terminal.WriteLine("Magical residue clings to this place.");
                terminal.WriteLine("You absorb what you can.");
                int manaGain = 10 + (level / 5);
                player.Mana = Math.Min(player.MaxMana, player.Mana + manaGain);
                terminal.SetColor("cyan");
                terminal.WriteLine($"+{manaGain} Mana restored!");
                break;

            case CharacterClass.Cleric:
            case CharacterClass.Paladin:
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("Divine presence lingers here. You offer a prayer.");
                long heal = player.MaxHP / 4;
                player.HP = Math.Min(player.MaxHP, player.HP + heal);
                terminal.SetColor("green");
                terminal.WriteLine($"+{heal} HP from divine blessing!");
                break;

            case CharacterClass.Assassin:
                terminal.SetColor("dark_gray");
                terminal.WriteLine("You notice hidden passages others would miss.");
                terminal.WriteLine("Someone left supplies for those who know where to look.");
                int potions = 1 + (level / 25);
                player.Healing = Math.Min(player.MaxPotions, player.Healing + potions);
                terminal.SetColor("green");
                terminal.WriteLine($"+{potions} hidden poison/potion{(potions > 1 ? "s" : "")}!");
                break;

            case CharacterClass.Ranger:
                terminal.SetColor("green");
                terminal.WriteLine("Your wilderness training helps you find resources.");
                long gold = CalculateScaledReward(level, 50, 150);
                player.Gold += gold;
                int healing = 1;
                player.Healing = Math.Min(player.MaxPotions, player.Healing + healing);
                terminal.SetColor("yellow");
                terminal.WriteLine($"+{gold} gold in sellable materials!");
                terminal.SetColor("green");
                terminal.WriteLine("+1 herbal remedy!");
                outcome.GoldGained = gold;
                break;

            case CharacterClass.Bard:
                terminal.SetColor("bright_magenta");
                terminal.WriteLine("You remember an old song about this place...");
                terminal.WriteLine("The melody reveals ancient secrets.");
                long xp = CalculateScaledReward(level, 75, 175);
                player.Experience += xp;
                terminal.SetColor("yellow");
                terminal.WriteLine($"+{xp} XP from bardic knowledge!");
                outcome.ExperienceGained = xp;
                break;

            default:
                terminal.SetColor("white");
                terminal.WriteLine("You find something useful.");
                long defaultGold = CalculateScaledReward(level, 30, 100);
                player.Gold += defaultGold;
                terminal.SetColor("yellow");
                terminal.WriteLine($"+{defaultGold} gold!");
                outcome.GoldGained = defaultGold;
                break;
        }
    }

    #endregion

    #region Risk Reward

    private async Task HandleRiskReward(RoomFeature feature, Character player, int level,
        TerminalEmulator terminal, FeatureOutcome outcome)
    {
        terminal.SetColor("cyan");
        terminal.WriteLine($"You {feature.Interaction.ToString().ToLower()} the {feature.Name}...");
        terminal.WriteLine("");
        await Task.Delay(600);

        terminal.SetColor("yellow");
        terminal.WriteLine("This looks dangerous, but potentially rewarding.");
        terminal.WriteLine("");

        // Show the risk/reward proposition
        long potentialGold = CalculateScaledReward(level, 200, 500);
        long potentialDamage = (level * 3) + random.Next(10, 30);
        int successChance = 50 + (int)(player.Dexterity / 5) + (int)(player.Intelligence / 10);
        successChance = Math.Clamp(successChance, 20, 85);

        terminal.SetColor("white");
        terminal.WriteLine($"Potential reward: {potentialGold} gold");
        terminal.SetColor("red");
        terminal.WriteLine($"Potential risk: {potentialDamage} damage");
        terminal.SetColor("cyan");
        terminal.WriteLine($"Success chance: {successChance}% (based on DEX/INT)");
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine("Do you take the risk? (Y/N)");

        var input = await terminal.GetInput("> ");

        if (input.Trim().ToUpper().StartsWith("Y"))
        {
            int roll = random.Next(100);
            terminal.WriteLine("");

            if (roll < successChance)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine("SUCCESS!");
                terminal.WriteLine($"You carefully extract the treasure!");
                player.Gold += potentialGold;
                terminal.SetColor("yellow");
                terminal.WriteLine($"+{potentialGold} gold!");
                outcome.GoldGained = potentialGold;
                outcome.Success = true;
            }
            else
            {
                terminal.SetColor("bright_red");
                terminal.WriteLine("FAILURE!");
                terminal.WriteLine("A trap springs!");
                player.HP -= potentialDamage;
                terminal.SetColor("red");
                terminal.WriteLine($"-{potentialDamage} HP!");
                outcome.DamageTaken = potentialDamage;
                outcome.Success = false;

                // But you might still get something
                if (random.Next(100) < 30)
                {
                    long partialGold = potentialGold / 3;
                    player.Gold += partialGold;
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"But you managed to grab {partialGold} gold!");
                    outcome.GoldGained = partialGold;
                }
            }
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("Wisdom is knowing when to walk away.");
            terminal.WriteLine("You leave the treasure undisturbed.");
        }

        await terminal.PressAnyKey();
    }

    #endregion

    #region Memory Trigger

    private async Task HandleMemoryTrigger(RoomFeature feature, Character player, int level,
        TerminalEmulator terminal, FeatureOutcome outcome)
    {
        terminal.SetColor("cyan");
        terminal.WriteLine($"You {feature.Interaction.ToString().ToLower()} the {feature.Name}...");
        terminal.WriteLine("");
        await Task.Delay(600);

        terminal.SetColor("bright_magenta");
        terminal.WriteLine("Something about this feels... familiar.");
        terminal.WriteLine("");
        await Task.Delay(800);

        // Trigger amnesia system memory
        var memory = GetMemoryFragment(level);

        terminal.SetColor("dark_magenta");
        terminal.WriteLine("A fragment of memory surfaces:");
        terminal.WriteLine("");
        terminal.SetColor("white");

        foreach (var line in memory.Text)
        {
            terminal.WriteLine($"  \"{line}\"");
            await Task.Delay(500);
        }

        terminal.WriteLine("");
        await Task.Delay(500);

        // The memory system uses enum-based fragments, so just display the thematic text
        // This creates atmosphere without requiring tight integration
        player.Mental = Math.Min(100, player.Mental + 1); // Slight mental boost from memory recovery

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("The memory fades, but something remains...");

        // Grant XP for memory discovery
        long xp = CalculateScaledReward(level, 50, 150);
        player.Experience += xp;
        terminal.SetColor("yellow");
        terminal.WriteLine($"+{xp} XP");
        outcome.ExperienceGained = xp;
        outcome.MemoryTriggered = true;
        outcome.Success = true;

        await terminal.PressAnyKey();
    }

    private FeatureMemoryFragment GetMemoryFragment(int level)
    {
        var fragments = new List<FeatureMemoryFragment>
        {
            new()
            {
                FragmentId = "past_life_1",
                Text = new[]
                {
                    "You've been here before.",
                    "Not in this life. But before.",
                    "How many times have you walked these halls?"
                }
            },
            new()
            {
                FragmentId = "the_letter",
                Text = new[]
                {
                    "The letter in your handwriting...",
                    "You remember writing it now.",
                    "But you don't remember why."
                }
            },
            new()
            {
                FragmentId = "the_stranger",
                Text = new[]
                {
                    "There was someone with you.",
                    "Someone who promised to help you remember.",
                    "Where did they go?"
                }
            },
            new()
            {
                FragmentId = "the_cycle",
                Text = new[]
                {
                    "Death is not the end.",
                    "You know this because you remember dying.",
                    "And waking. And dying again."
                }
            },
            new()
            {
                FragmentId = "manwe_truth",
                Text = new[]
                {
                    "The Creator did not create the cycle.",
                    "He is as trapped as you are.",
                    "But he has forgotten this."
                }
            }
        };

        // Higher level = deeper memories
        int maxIndex = Math.Min(fragments.Count - 1, level / 20);
        return fragments[random.Next(0, maxIndex + 1)];
    }

    #endregion

    #region Ocean Insight

    private async Task HandleOceanInsight(RoomFeature feature, Character player, int level,
        TerminalEmulator terminal, FeatureOutcome outcome)
    {
        terminal.SetColor("cyan");
        terminal.WriteLine($"You {feature.Interaction.ToString().ToLower()} the {feature.Name}...");
        terminal.WriteLine("");
        await Task.Delay(600);

        terminal.SetColor("bright_blue");
        terminal.WriteLine("For a moment, everything stops.");
        terminal.WriteLine("");
        await Task.Delay(1000);

        var insight = GetOceanInsight(level);

        terminal.SetColor("bright_cyan");
        foreach (var line in insight.Text)
        {
            terminal.WriteLine($"  {line}");
            await Task.Delay(600);
        }

        terminal.WriteLine("");
        await Task.Delay(800);

        // Trigger ocean philosophy awakening with insight points
        try
        {
            OceanPhilosophySystem.Instance?.GainInsight(5); // 5 insight points from feature discovery
        }
        catch { /* System may not be initialized */ }

        terminal.SetColor("bright_magenta");
        terminal.WriteLine("You feel... different. More awake.");

        // Ocean insights grant significant XP
        long xp = CalculateScaledReward(level, 100, 250);
        player.Experience += xp;
        terminal.SetColor("yellow");
        terminal.WriteLine($"+{xp} XP");
        outcome.ExperienceGained = xp;
        outcome.OceanInsightGained = true;
        outcome.Success = true;

        await terminal.PressAnyKey();
    }

    private FeatureOceanInsight GetOceanInsight(int level)
    {
        var insights = new List<FeatureOceanInsight>
        {
            new()
            {
                InsightType = "wave_self",
                Text = new[]
                {
                    "You are not a wave fighting the ocean.",
                    "You ARE the ocean, dreaming of being a wave.",
                    "When you understand this, truly understand...",
                    "The dream ends."
                }
            },
            new()
            {
                InsightType = "cycle_nature",
                Text = new[]
                {
                    "The cycle is not punishment.",
                    "It is opportunity.",
                    "Each life, you remember a little more.",
                    "One day, you will remember everything."
                }
            },
            new()
            {
                InsightType = "god_truth",
                Text = new[]
                {
                    "The Old Gods are not your enemies.",
                    "They are your forgotten family.",
                    "Broken, yes. Corrupted, yes.",
                    "But still... family."
                }
            },
            new()
            {
                InsightType = "identity",
                Text = new[]
                {
                    "Who are you?",
                    "Not your name. Names are waves.",
                    "Who are you beneath the name?",
                    "Beneath the memories?",
                    "Beneath the self?"
                }
            },
            new()
            {
                InsightType = "final_truth",
                Text = new[]
                {
                    "There is no hero.",
                    "There is no villain.",
                    "There is only the ocean,",
                    "dreaming itself into waves,",
                    "and waking up."
                }
            }
        };

        // Higher level = deeper insights
        int maxIndex = Math.Min(insights.Count - 1, level / 20);
        return insights[random.Next(0, maxIndex + 1)];
    }

    #endregion

    #region Standard Interaction

    private async Task HandleStandardInteraction(RoomFeature feature, Character player, int level,
        TerminalEmulator terminal, FeatureOutcome outcome)
    {
        terminal.SetColor("cyan");
        terminal.WriteLine($"You {feature.Interaction.ToString().ToLower()} the {feature.Name}...");
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine(feature.Description);
        terminal.WriteLine("");
        await Task.Delay(800);

        // Standard outcomes but still level-scaled
        var roll = random.Next(100);

        if (roll < 35)
        {
            long gold = CalculateScaledReward(level, 30, 100);
            player.Gold += gold;
            terminal.SetColor("yellow");
            terminal.WriteLine($"You find {gold} gold!");
            outcome.GoldGained = gold;
        }
        else if (roll < 55)
        {
            long xp = CalculateScaledReward(level, 25, 75);
            player.Experience += xp;
            terminal.SetColor("yellow");
            terminal.WriteLine($"You learn something. +{xp} XP");
            outcome.ExperienceGained = xp;
        }
        else if (roll < 70)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("Nothing of interest here.");
        }
        else if (roll < 85)
        {
            int potion = 1;
            player.Healing = Math.Min(player.MaxPotions, player.Healing + potion);
            terminal.SetColor("green");
            terminal.WriteLine("You find a healing potion!");
        }
        else
        {
            long damage = level + random.Next(5, 15);
            player.HP -= damage;
            terminal.SetColor("red");
            terminal.WriteLine($"A trap! You take {damage} damage!");
            outcome.DamageTaken = damage;
        }

        outcome.Success = roll < 85; // Not a trap = success
        await terminal.PressAnyKey();
    }

    #endregion

    #region Utility

    private long CalculateScaledReward(int level, int baseMin, int baseMax)
    {
        // Rewards scale with level^1.5 to stay relevant
        double multiplier = Math.Pow(level, 1.5) / 10.0;
        multiplier = Math.Max(1.0, multiplier);

        int baseReward = random.Next(baseMin, baseMax + 1);
        return (long)(baseReward * multiplier);
    }

    #endregion
}

#region Data Classes

public enum FeatureInteractionType
{
    Standard,
    LoreDiscovery,
    SkillChallenge,
    MoralChoice,
    ClassSpecific,
    RiskReward,
    MemoryTrigger,
    OceanInsight
}

public class FeatureOutcome
{
    public bool Success { get; set; }
    public long GoldGained { get; set; }
    public long ExperienceGained { get; set; }
    public long DamageTaken { get; set; }
    public bool LoreDiscovered { get; set; }
    public bool MemoryTriggered { get; set; }
    public bool OceanInsightGained { get; set; }
    public bool ChoiceMade { get; set; }
}

public class LoreFragment
{
    public string[] Text { get; set; } = Array.Empty<string>();
    public bool GrantsExperience { get; set; }
    public bool AffectsAlignment { get; set; }
    public int AlignmentShift { get; set; }
    public string? RevealsOldGod { get; set; }
}

public class MoralChoice
{
    public string[] Situation { get; set; } = Array.Empty<string>();
    public MoralOption Option1 { get; set; } = new();
    public MoralOption Option2 { get; set; } = new();
}

public class MoralOption
{
    public string Description { get; set; } = "";
    public string ResultText { get; set; } = "";
    public int AlignmentShift { get; set; }
    public long GoldReward { get; set; }
    public long ExperienceReward { get; set; }
    public long HealthChange { get; set; }
    public bool IsLight { get; set; }
}

public class FeatureMemoryFragment
{
    public string FragmentId { get; set; } = "";
    public string[] Text { get; set; } = Array.Empty<string>();
}

public class FeatureOceanInsight
{
    public string InsightType { get; set; } = "";
    public string[] Text { get; set; } = Array.Empty<string>();
}

#endregion
