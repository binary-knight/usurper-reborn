using System;
using System.Collections.Generic;
using System.Linq;
using UsurperRemake.Data;

/// <summary>
/// Dynamic NPC Dialogue Generator
/// Creates unique, personality-driven dialogue based on:
/// - Relationship level with player
/// - NPC personality traits
/// - NPC archetype/profession
/// - Memory of past interactions
/// - Current emotional state
/// - Context (player HP, wealth, level, etc.)
///
/// Now queries the pre-generated NPCDialogueDatabase first for higher-quality lines,
/// falling back to the original template system if no suitable match is found.
/// </summary>
public static class NPCDialogueGenerator
{
    private static readonly Random _random = new();

    #region Relationship-Tiered Greetings

    private static readonly Dictionary<int, string[]> RelationshipGreetings = new()
    {
        // Married (10) - Intimate, loving
        [GameConfig.RelationMarried] = new[]
        {
            "My love, you've returned to me!",
            "Darling! I've missed you so much.",
            "There you are, my heart.",
            "Every moment apart feels like eternity, my love.",
            "Welcome home, beloved."
        },
        // Love (20) - Deeply affectionate
        [GameConfig.RelationLove] = new[]
        {
            "My heart soars seeing you!",
            "I've been thinking of you...",
            "Your presence brightens my day!",
            "I was hoping you'd come by.",
            "It's you! I'm so happy!"
        },
        // Passion (30) - Strong attraction/bond
        [GameConfig.RelationPassion] = new[]
        {
            "There's no one I'd rather see right now.",
            "I was just thinking about you...",
            "You always know when to appear.",
            "My day just got significantly better.",
            "Finally, a familiar face I actually want to see!"
        },
        // Friendship (40) - Close friends
        [GameConfig.RelationFriendship] = new[]
        {
            "My friend! Good to see you!",
            "Ah, just the person I wanted to talk to!",
            "It's always a pleasure!",
            "Welcome, welcome! What brings you by?",
            "There's my favorite {player_class}!"
        },
        // Trust (50) - Trusted acquaintance
        [GameConfig.RelationTrust] = new[]
        {
            "Good to see you again.",
            "Ah, you're back. Welcome!",
            "I was hoping you'd stop by.",
            "Always welcome here, friend.",
            "How have you been?"
        },
        // Respect (60) - Respectful acknowledgment
        [GameConfig.RelationRespect] = new[]
        {
            "Greetings, {player_title}.",
            "A pleasure to see you.",
            "Welcome. What can I do for you?",
            "Ah, we meet again.",
            "Good day to you."
        },
        // Normal (70) - Neutral/stranger
        [GameConfig.RelationNormal] = new[]
        {
            "Greetings, traveler.",
            "What brings you here?",
            "Hello there.",
            "Can I help you with something?",
            "Hmm? Oh, hello."
        },
        // Suspicious (80) - Wary
        [GameConfig.RelationSuspicious] = new[]
        {
            "Oh... it's you.",
            "What do you want?",
            "You again? What is it?",
            "State your business.",
            "I'm keeping my eye on you."
        },
        // Anger (90) - Irritated/displeased
        [GameConfig.RelationAnger] = new[]
        {
            "You've got some nerve showing up here.",
            "Make it quick.",
            "I don't have time for you.",
            "What NOW?",
            "Haven't you caused enough trouble?"
        },
        // Enemy (100) - Hostile
        [GameConfig.RelationEnemy] = new[]
        {
            "You dare show your face?",
            "Leave before I lose my patience.",
            "We have nothing to discuss.",
            "You're not welcome here.",
            "I should have known it was you."
        },
        // Hate (110) - Despised
        [GameConfig.RelationHate] = new[]
        {
            "Get out of my sight!",
            "I have nothing to say to you.",
            "You... you have some nerve!",
            "Don't speak to me.",
            "I'd sooner talk to a plague rat."
        }
    };

    #endregion

    #region Archetype Vocabularies

    private static readonly Dictionary<string, ArchetypeVocabulary> ArchetypeVocabularies = new()
    {
        ["guard"] = new ArchetypeVocabulary
        {
            Titles = new[] { "Citizen", "Traveler", "Sir", "Ma'am" },
            Phrases = new[] { "Halt!", "Move along.", "Stay out of trouble.", "Keep the peace.", "By order of the crown..." },
            Topics = new[] { "patrol routes", "recent crimes", "the King's orders", "keeping order" },
            StyleModifiers = new[] { "formal", "military", "authoritative" }
        },
        ["merchant"] = new ArchetypeVocabulary
        {
            Titles = new[] { "Customer", "Valued patron", "Friend", "Good sir/madam" },
            Phrases = new[] { "What can I get for you?", "Fine quality!", "A steal at this price!", "Best deals in town!", "You won't find better anywhere!" },
            Topics = new[] { "trade routes", "supply shortages", "prices", "business", "competition" },
            StyleModifiers = new[] { "persuasive", "friendly", "calculating" }
        },
        ["thief"] = new ArchetypeVocabulary
        {
            Titles = new[] { "mate", "friend", "pal" },
            Phrases = new[] { "Keep your voice down.", "You didn't hear this from me.", "What's in it for me?", "Watch your back out there.", "Interesting times, eh?" },
            Topics = new[] { "the job", "marks", "fences", "the shadows", "scores" },
            StyleModifiers = new[] { "cryptic", "street", "cautious" }
        },
        ["assassin"] = new ArchetypeVocabulary
        {
            Titles = new[] { "stranger", "traveler" },
            Phrases = new[] { "Silence is golden.", "Some questions are better left unasked.", "Death comes for us all.", "Walk carefully.", "Shadows have ears." },
            Topics = new[] { "contracts", "targets", "the guild", "poisons" },
            StyleModifiers = new[] { "ominous", "measured", "cold" }
        },
        ["priest"] = new ArchetypeVocabulary
        {
            Titles = new[] { "Child", "Pilgrim", "Seeker", "Blessed one" },
            Phrases = new[] { "Blessings upon you.", "May the gods guide you.", "The faithful are welcome here.", "Peace be with you.", "The divine light shines upon you." },
            Topics = new[] { "faith", "blessings", "the gods", "spiritual matters", "healing" },
            StyleModifiers = new[] { "serene", "pious", "compassionate" }
        },
        ["noble"] = new ArchetypeVocabulary
        {
            Titles = new[] { "Commoner", "Peasant", "Good fellow", "Citizen" },
            Phrases = new[] { "How dreadfully common.", "In my circles...", "One must maintain standards.", "Indeed.", "How quaint." },
            Topics = new[] { "court gossip", "bloodlines", "estates", "politics", "social standing" },
            StyleModifiers = new[] { "pompous", "condescending", "refined" }
        },
        ["thug"] = new ArchetypeVocabulary
        {
            Titles = new[] { "weakling", "punk", "fool" },
            Phrases = new[] { "You looking at something?", "This is our turf.", "Pay up or else.", "Got a problem?", "Think you're tough?" },
            Topics = new[] { "territory", "fights", "respect", "the gang" },
            StyleModifiers = new[] { "aggressive", "crude", "threatening" }
        },
        ["citizen"] = new ArchetypeVocabulary
        {
            Titles = new[] { "Friend", "Stranger", "Traveler" },
            Phrases = new[] { "Hard times these days.", "Have you heard about...?", "Just trying to get by.", "Nice weather, isn't it?", "The world keeps turning." },
            Topics = new[] { "weather", "gossip", "daily life", "local news" },
            StyleModifiers = new[] { "humble", "friendly", "simple" }
        },
        ["mystic"] = new ArchetypeVocabulary
        {
            Titles = new[] { "Seeker", "Mortal", "Curious one" },
            Phrases = new[] { "The stars speak...", "I sense a disturbance.", "The arcane flows through all.", "Curious energies surround you.", "Fate has brought you here." },
            Topics = new[] { "magic", "prophecy", "the cosmos", "mystical forces" },
            StyleModifiers = new[] { "enigmatic", "mystical", "otherworldly" }
        }
    };

    #endregion

    #region Memory References

    private static readonly Dictionary<MemoryType, string[]> MemoryReferences = new()
    {
        [MemoryType.Helped] = new[]
        {
            "I haven't forgotten how you helped me {time_ref}.",
            "I still owe you for what you did {time_ref}.",
            "Your kindness {time_ref} meant a lot to me."
        },
        [MemoryType.Attacked] = new[]
        {
            "After what you did {time_ref}... I can't forget that easily.",
            "You attacked me {time_ref}. I haven't forgotten.",
            "The wounds from {time_ref} haven't fully healed."
        },
        [MemoryType.Betrayed] = new[]
        {
            "Your betrayal {time_ref} still stings.",
            "After what you did... I'm not sure I can trust you.",
            "You betrayed me once. Never again."
        },
        [MemoryType.Traded] = new[]
        {
            "Looking for another deal? The last one worked out.",
            "Our trade {time_ref} was profitable. More business?",
            "I remember our transaction {time_ref}. Fair dealing."
        },
        [MemoryType.SharedDrink] = new[]
        {
            "We should share another drink sometime, like {time_ref}.",
            "I remember that night at the tavern {time_ref}...",
            "Good times at the inn {time_ref}, eh?"
        },
        [MemoryType.Defended] = new[]
        {
            "I haven't forgotten how you stood up for me {time_ref}.",
            "You defended me {time_ref}. That means something.",
            "We fought together {time_ref}. I respect that."
        },
        [MemoryType.Saved] = new[]
        {
            "You saved my life {time_ref}. I owe you everything.",
            "I'd be dead if not for you {time_ref}.",
            "I'll never forget what you did for me {time_ref}."
        },
        [MemoryType.Insulted] = new[]
        {
            "Your words {time_ref} still sting, you know.",
            "I haven't forgotten what you said {time_ref}.",
            "Those insults {time_ref}... they hurt."
        },
        [MemoryType.Complimented] = new[]
        {
            "Your kind words {time_ref} still make me smile.",
            "I remember what you said {time_ref}. Thank you.",
            "The compliment {time_ref} meant a lot."
        },
        [MemoryType.SharedItem] = new[]
        {
            "I still have what you gave me {time_ref}.",
            "Your gift {time_ref} was very thoughtful.",
            "That item you shared {time_ref}... I treasure it."
        }
    };

    private static readonly string[] TimeReferences = new[]
    {
        "just yesterday", "the other day", "recently", "some time ago", "a while back", "long ago"
    };

    #endregion

    #region Context Comments

    private static readonly Dictionary<string, string[]> ContextComments = new()
    {
        ["low_hp"] = new[]
        {
            "You look terrible! Are you wounded?",
            "You should see a healer, you don't look well.",
            "By the gods, what happened to you?",
            "You're in rough shape. Are you alright?"
        },
        ["high_level"] = new[]
        {
            "Your reputation precedes you.",
            "I've heard tales of your exploits.",
            "A legendary {player_class}, if I'm not mistaken?",
            "Your presence commands respect."
        },
        ["low_level"] = new[]
        {
            "New to these parts?",
            "Be careful out there, youngster.",
            "The world can be dangerous for the inexperienced.",
            "Just starting your journey, I see."
        },
        ["rich"] = new[]
        {
            "Business must be good!",
            "Coin weighing you down, I see.",
            "You look prosperous today.",
            "The glint of gold follows you."
        },
        ["poor"] = new[]
        {
            "Times are tough for all of us.",
            "Not much coin in your purse, eh?",
            "I'd offer gold if I had any to spare.",
            "The economy's been hard on everyone."
        },
        ["is_king"] = new[]
        {
            "Your Majesty! An honor!",
            "The throne suits you, my liege.",
            "My King/Queen, how may I serve?",
            "All hail! The ruler graces us!"
        },
        ["has_companion"] = new[]
        {
            "I see you've got company.",
            "Traveling with friends? Smart.",
            "Who's your companion there?",
            "Good to see you're not alone."
        },
        ["diseased"] = new[]
        {
            "You don't look well... keep your distance.",
            "Is that... a plague symptom? Stay back!",
            "There's sickness in you. I can tell.",
            "You should seek a healer for that affliction."
        },
        ["powerful_weapon"] = new[]
        {
            "That's quite the weapon you carry.",
            "Fine blade! Where'd you get it?",
            "I wouldn't want to be on the wrong end of that.",
            "An impressive armament you have there."
        },
        ["morning"] = new[]
        {
            "Early riser, I see.",
            "Up with the dawn? Good habits.",
            "Morning air suits you."
        },
        ["evening"] = new[]
        {
            "Out late, aren't you?",
            "The night beckons to you as well?",
            "Careful after dark."
        },
        ["night"] = new[]
        {
            "Dangerous to be wandering at this hour.",
            "The shadows are treacherous at night.",
            "Most sensible folk are asleep by now."
        }
    };

    #endregion

    #region Emotional Indicators

    private static readonly Dictionary<EmotionType, string[]> EmotionalIndicators = new()
    {
        [EmotionType.Joy] = new[] { "*smiles warmly*", "*beams happily*", "*chuckles*", "*grins*" },
        [EmotionType.Sadness] = new[] { "*sighs deeply*", "*looks downcast*", "*speaks softly*", "*trails off...*" },
        [EmotionType.Anger] = new[] { "*scowls*", "*narrows eyes*", "*speaks curtly*", "*clenches fists*" },
        [EmotionType.Fear] = new[] { "*looks around nervously*", "*whispers*", "*eyes dart about*", "*trembles slightly*" },
        [EmotionType.Confidence] = new[] { "*stands tall*", "*speaks firmly*", "*nods confidently*", "*meets your gaze*" },
        [EmotionType.Loneliness] = new[] { "*looks relieved to see you*", "*perks up*", "*seems eager to talk*" },
        [EmotionType.Hope] = new[] { "*eyes brighten*", "*speaks hopefully*", "*smiles faintly*" },
        [EmotionType.Peace] = new[] { "*speaks calmly*", "*seems serene*", "*nods peacefully*" }
    };

    #endregion

    #region Personality Modifiers

    private static readonly Dictionary<string, (string prefix, string suffix)[]> PersonalityModifiers = new()
    {
        ["high_aggression"] = new[]
        {
            ("Look, ", ""), ("", " Now get to the point."), ("", " I don't have all day.")
        },
        ["low_aggression"] = new[]
        {
            ("Oh, ", ""), ("", ", if you don't mind."), ("", " I hope that's alright.")
        },
        ["high_intelligence"] = new[]
        {
            ("Interestingly, ", ""), ("If I may observe, ", ""), ("From my perspective, ", "")
        },
        ["high_greed"] = new[]
        {
            ("", " Speaking of which, got any coin?"), ("", " Business opportunity, perhaps?"), ("", " Gold talks, you know.")
        },
        ["high_romanticism"] = new[]
        {
            ("Ah, ", ""), ("", ", my dear."), ("", " Your presence is enchanting.")
        },
        ["high_humor"] = new[]
        {
            ("Ha! ", ""), ("", " Just kidding... mostly."), ("", " Don't take that too seriously!")
        },
        ["high_loyalty"] = new[]
        {
            ("", " You can count on me."), ("As always, ", ""), ("", " My word is my bond.")
        },
        ["high_bravery"] = new[]
        {
            ("Fearlessly speaking, ", ""), ("", " Nothing scares me."), ("", " Let them come, I say!")
        },
        ["high_deceitfulness"] = new[]
        {
            ("Between you and me... ", ""), ("", " But who knows what's really true?"), ("", " Or so I've heard...")
        }
    };

    #endregion

    #region Main Generation Methods

    /// <summary>
    /// Generate a context-aware greeting for the player
    /// </summary>
    public static string GenerateGreeting(NPC npc, Player player)
    {
        if (npc == null || player == null) return "Hello there.";

        // Try pre-generated dialogue database first
        var dbLine = NPCDialogueDatabase.GetBestLine("greeting", npc, player);
        if (dbLine != null) return dbLine;

        // Fall back to template system
        // Get relationship level
        int relationshipLevel = GetRelationshipLevel(npc, player);

        // Get base greeting
        string greeting = GetBaseGreeting(relationshipLevel, player);

        // Apply archetype vocabulary (30% chance to modify)
        if (_random.NextDouble() < 0.30)
        {
            greeting = ApplyArchetypeStyle(greeting, npc.Archetype, player);
        }

        // Apply personality modifiers (40% chance)
        if (npc.Personality != null && _random.NextDouble() < 0.40)
        {
            greeting = ApplyPersonalityModifiers(greeting, npc.Personality);
        }

        // Add memory reference (20% chance)
        if (npc.Memory != null && _random.NextDouble() < 0.20)
        {
            var memoryRef = GetMemoryReference(npc.Memory, player);
            if (!string.IsNullOrEmpty(memoryRef))
            {
                greeting = $"{greeting} {memoryRef}";
            }
        }

        // Add context comment (30% chance)
        if (_random.NextDouble() < 0.30)
        {
            var contextComment = GetContextComment(player);
            if (!string.IsNullOrEmpty(contextComment))
            {
                greeting = $"{greeting} {contextComment}";
            }
        }

        // Add emotional indicator (25% chance)
        if (npc.EmotionalState != null && _random.NextDouble() < 0.25)
        {
            var indicator = GetEmotionalIndicator(npc.EmotionalState);
            if (!string.IsNullOrEmpty(indicator))
            {
                greeting = $"{indicator} {greeting}";
            }
        }

        return greeting;
    }

    /// <summary>
    /// Generate a farewell for the player
    /// </summary>
    public static string GenerateFarewell(NPC npc, Player player)
    {
        if (npc == null || player == null) return "Farewell.";

        // Try pre-generated dialogue database first
        var dbLine = NPCDialogueDatabase.GetBestLine("farewell", npc, player);
        if (dbLine != null) return dbLine;

        // Fall back to template system
        int relationshipLevel = GetRelationshipLevel(npc, player);
        string farewell = GetBaseFarewell(relationshipLevel);

        // Apply archetype flavor
        farewell = ApplyArchetypeFarewell(farewell, npc.Archetype);

        // Add emotional indicator
        if (npc.EmotionalState != null && _random.NextDouble() < 0.25)
        {
            var indicator = GetEmotionalIndicator(npc.EmotionalState);
            if (!string.IsNullOrEmpty(indicator))
            {
                farewell = $"{indicator} {farewell}";
            }
        }

        return farewell;
    }

    /// <summary>
    /// Generate small talk/conversation topic
    /// </summary>
    public static string GenerateSmallTalk(NPC npc, Player player)
    {
        if (npc == null) return "Interesting times we live in.";

        // Try pre-generated dialogue database first
        var dbLine = NPCDialogueDatabase.GetBestLine("smalltalk", npc, player);
        if (dbLine != null) return dbLine;

        // Get archetype-appropriate topics
        var vocab = GetArchetypeVocabulary(npc.Archetype);
        if (vocab != null && vocab.Topics.Length > 0)
        {
            var topic = vocab.Topics[_random.Next(vocab.Topics.Length)];
            return GenerateTopicComment(topic, npc.Personality);
        }

        // Fallback generic small talk
        var genericTopics = new[]
        {
            "The weather's been strange lately, hasn't it?",
            "Have you heard the latest news from the castle?",
            "The dungeon's been particularly dangerous these days.",
            "Trade's been slow lately. Economy's rough.",
            "More adventurers coming through than usual.",
            "Strange happenings in town, they say."
        };

        return genericTopics[_random.Next(genericTopics.Length)];
    }

    /// <summary>
    /// Generate a reaction to an event
    /// </summary>
    public static string GenerateReaction(NPC npc, Player player, string eventType)
    {
        if (npc?.Personality == null) return "Interesting.";

        // Try pre-generated dialogue database first
        var dbLine = NPCDialogueDatabase.GetBestLine("reaction", npc, player, eventType);
        if (dbLine != null) return dbLine;

        return eventType.ToLower() switch
        {
            "combat_victory" => GenerateCombatVictoryReaction(npc.Personality),
            "combat_defeat" => GenerateCombatDefeatReaction(npc.Personality),
            "combat_flee" => GenerateFleeReaction(npc.Personality),
            "ally_death" => GenerateAllyDeathReaction(npc.Personality),
            "gift_received" => GenerateGiftReaction(npc.Personality, GetRelationshipLevel(npc, player)),
            "insult" => GenerateInsultReaction(npc.Personality),
            "compliment" => GenerateComplimentReaction(npc.Personality),
            "threat" => GenerateThreatReaction(npc.Personality),
            _ => "Hmm, interesting."
        };
    }

    #endregion

    #region Helper Methods

    private static int GetRelationshipLevel(NPC npc, Player player)
    {
        try
        {
            return RelationshipSystem.GetRelationshipStatus(npc, player);
        }
        catch
        {
            return GameConfig.RelationNormal;
        }
    }

    private static string GetBaseGreeting(int relationLevel, Player player)
    {
        // Find the closest relationship tier
        int closestTier = GameConfig.RelationNormal;
        foreach (var tier in RelationshipGreetings.Keys.OrderBy(k => k))
        {
            if (relationLevel <= tier)
            {
                closestTier = tier;
                break;
            }
            closestTier = tier;
        }

        var greetings = RelationshipGreetings.GetValueOrDefault(closestTier, RelationshipGreetings[GameConfig.RelationNormal]);
        var greeting = greetings[_random.Next(greetings.Length)];

        // Replace placeholders
        greeting = ReplacePlaceholders(greeting, player);

        return greeting;
    }

    private static string GetBaseFarewell(int relationLevel)
    {
        return relationLevel switch
        {
            <= GameConfig.RelationMarried => new[] {
                "Hurry back to me, my love.",
                "Be safe, darling.",
                "I'll be waiting for you.",
                "Until we meet again, beloved."
            }[_random.Next(4)],
            <= GameConfig.RelationLove => new[] {
                "I'll miss you!",
                "Come back soon!",
                "Take care of yourself!",
                "My heart goes with you."
            }[_random.Next(4)],
            <= GameConfig.RelationFriendship => new[] {
                "See you around, friend!",
                "Take care out there!",
                "Until next time!",
                "Safe travels, my friend!"
            }[_random.Next(4)],
            <= GameConfig.RelationNormal => new[] {
                "Farewell.",
                "Safe travels.",
                "Until we meet again.",
                "Good luck out there."
            }[_random.Next(4)],
            <= GameConfig.RelationAnger => new[] {
                "Just go.",
                "Finally.",
                "Good riddance.",
                "Don't let the door hit you."
            }[_random.Next(4)],
            _ => new[] {
                "Leave. Now.",
                "Get out of my sight!",
                "I hope we never meet again.",
                "And stay away!"
            }[_random.Next(4)]
        };
    }

    private static string ReplacePlaceholders(string text, Player player)
    {
        if (player == null) return text;

        text = text.Replace("{player_name}", player.Name2 ?? player.Name1 ?? "Adventurer");
        text = text.Replace("{player_class}", player.Class.ToString());
        text = text.Replace("{player_title}", player.King ? "Your Majesty" : player.Class.ToString());

        return text;
    }

    private static ArchetypeVocabulary GetArchetypeVocabulary(string archetype)
    {
        if (string.IsNullOrEmpty(archetype)) return null;
        return ArchetypeVocabularies.GetValueOrDefault(archetype.ToLower());
    }

    private static string ApplyArchetypeStyle(string greeting, string archetype, Player player)
    {
        var vocab = GetArchetypeVocabulary(archetype);
        if (vocab == null) return greeting;

        // Sometimes use archetype-specific title
        if (_random.NextDouble() < 0.5 && vocab.Titles.Length > 0)
        {
            var title = vocab.Titles[_random.Next(vocab.Titles.Length)];
            greeting = $"{greeting}, {title}.";
        }

        // Sometimes add archetype phrase
        if (_random.NextDouble() < 0.3 && vocab.Phrases.Length > 0)
        {
            var phrase = vocab.Phrases[_random.Next(vocab.Phrases.Length)];
            greeting = $"{greeting} {phrase}";
        }

        return greeting;
    }

    private static string ApplyArchetypeFarewell(string farewell, string archetype)
    {
        return archetype?.ToLower() switch
        {
            "guard" => _random.NextDouble() < 0.5 ? $"{farewell} Stay out of trouble." : farewell,
            "priest" => _random.NextDouble() < 0.5 ? $"{farewell} May the gods watch over you." : farewell,
            "merchant" => _random.NextDouble() < 0.5 ? $"{farewell} Come back when you need more goods!" : farewell,
            "thief" => _random.NextDouble() < 0.5 ? $"{farewell} Watch your coin purse out there." : farewell,
            "noble" => _random.NextDouble() < 0.5 ? $"{farewell} One must maintain proper farewells." : farewell,
            _ => farewell
        };
    }

    private static string ApplyPersonalityModifiers(string greeting, PersonalityProfile personality)
    {
        // Select modifier based on dominant personality trait
        string modifierKey = null;

        if (personality.Aggression > 0.7f) modifierKey = "high_aggression";
        else if (personality.Aggression < 0.3f) modifierKey = "low_aggression";
        else if (personality.Intelligence > 0.7f) modifierKey = "high_intelligence";
        else if (personality.Greed > 0.7f) modifierKey = "high_greed";
        else if (personality.Romanticism > 0.7f) modifierKey = "high_romanticism";
        else if (personality.Sociability > 0.7f) modifierKey = "high_humor";
        else if (personality.Loyalty > 0.7f) modifierKey = "high_loyalty";
        else if (personality.Courage > 0.7f) modifierKey = "high_bravery";
        else if (personality.Trustworthiness < 0.3f) modifierKey = "high_deceitfulness";

        if (modifierKey != null && PersonalityModifiers.TryGetValue(modifierKey, out var modifiers))
        {
            var (prefix, suffix) = modifiers[_random.Next(modifiers.Length)];
            greeting = $"{prefix}{greeting}{suffix}";
        }

        return greeting;
    }

    private static string GetMemoryReference(MemorySystem memory, Player player)
    {
        if (memory == null || player == null) return null;

        var playerName = player.Name2 ?? player.Name1;
        if (string.IsNullOrEmpty(playerName)) return null;

        // Get recent memories about this player
        var memories = memory.GetMemoriesAboutCharacter(playerName)
            .Where(m => MemoryReferences.ContainsKey(m.Type))
            .OrderByDescending(m => m.Importance)
            .Take(3)
            .ToList();

        if (!memories.Any()) return null;

        var selectedMemory = memories[_random.Next(memories.Count)];
        var references = MemoryReferences[selectedMemory.Type];
        var reference = references[_random.Next(references.Length)];

        // Determine time reference
        var age = selectedMemory.GetAge();
        string timeRef = age.TotalDays switch
        {
            < 1 => "just yesterday",
            < 3 => "the other day",
            < 10 => "recently",
            < 30 => "some time ago",
            < 60 => "a while back",
            _ => "long ago"
        };

        return reference.Replace("{time_ref}", timeRef);
    }

    private static string GetContextComment(Player player)
    {
        if (player == null) return null;

        // Check various player conditions
        var possibleComments = new List<string>();

        // Low HP
        if (player.HP < player.MaxHP * 0.25f)
        {
            possibleComments.AddRange(ContextComments["low_hp"]);
        }

        // High level (50+)
        if (player.Level >= 50)
        {
            possibleComments.AddRange(ContextComments["high_level"]);
        }
        // Low level (<5)
        else if (player.Level < 5)
        {
            possibleComments.AddRange(ContextComments["low_level"]);
        }

        // Rich (>10000 gold)
        if (player.Gold > 10000)
        {
            possibleComments.AddRange(ContextComments["rich"]);
        }
        // Poor (<100 gold)
        else if (player.Gold < 100)
        {
            possibleComments.AddRange(ContextComments["poor"]);
        }

        // Is King/Queen
        if (player.King)
        {
            possibleComments.AddRange(ContextComments["is_king"]);
        }

        // Check time of day
        var hour = DateTime.Now.Hour;
        if (hour >= 5 && hour < 9)
        {
            possibleComments.AddRange(ContextComments["morning"]);
        }
        else if (hour >= 18 && hour < 22)
        {
            possibleComments.AddRange(ContextComments["evening"]);
        }
        else if (hour >= 22 || hour < 5)
        {
            possibleComments.AddRange(ContextComments["night"]);
        }

        if (!possibleComments.Any()) return null;

        var comment = possibleComments[_random.Next(possibleComments.Count)];
        return ReplacePlaceholders(comment, player);
    }

    private static string GetEmotionalIndicator(EmotionalState emotionalState)
    {
        if (emotionalState == null) return null;

        // Get current dominant emotion
        var activeEmotions = emotionalState.GetActiveEmotions();
        if (activeEmotions == null || !activeEmotions.Any()) return null;

        // Find strongest emotion (activeEmotions is Dictionary<EmotionType, Emotion>)
        var strongestPair = activeEmotions
            .OrderByDescending(e => e.Value.Intensity)
            .FirstOrDefault();

        if (strongestPair.Value == null || strongestPair.Value.Intensity < 0.3f) return null;

        if (EmotionalIndicators.TryGetValue(strongestPair.Key, out var indicators))
        {
            return indicators[_random.Next(indicators.Length)];
        }

        return null;
    }

    private static string GenerateTopicComment(string topic, PersonalityProfile personality)
    {
        var templates = new[]
        {
            $"Have you heard anything about {topic} lately?",
            $"I've been thinking about {topic} a lot.",
            $"What's your take on {topic}?",
            $"They say {topic} is important these days.",
            $"Interesting developments regarding {topic}, wouldn't you say?"
        };

        return templates[_random.Next(templates.Length)];
    }

    private static string GenerateCombatVictoryReaction(PersonalityProfile personality)
    {
        if (personality.Aggression > 0.7f)
            return "Ha! That's what I like to see! Crush them all!";
        if (personality.Courage > 0.7f)
            return "Well fought! Victory is yours!";
        if (personality.Sociability > 0.7f)
            return "Amazing! You were incredible out there!";
        return "Well done. You won.";
    }

    private static string GenerateCombatDefeatReaction(PersonalityProfile personality)
    {
        if (personality.Aggression > 0.7f)
            return "Get up and fight! Don't let them win!";
        if (personality.Sociability > 0.7f)
            return "Oh no! Are you alright? That was terrible!";
        return "A defeat. It happens to the best of us.";
    }

    private static string GenerateGiftReaction(PersonalityProfile personality, int relationship)
    {
        if (relationship <= GameConfig.RelationLove)
            return "*blushes deeply* You... you remembered me. Thank you, my love.";
        if (relationship <= GameConfig.RelationFriendship)
            return "For me? You're too kind! Thank you, friend!";
        if (personality.Greed > 0.7f)
            return "Hmm, this will do nicely. What do you want in return?";
        return "A gift? How... unexpected. Thank you.";
    }

    private static string GenerateInsultReaction(PersonalityProfile personality)
    {
        if (personality.Aggression > 0.7f)
            return "*clenches fists* Say that again. I dare you!";
        if (personality.Vengefulness > 0.7f)
            return "*narrows eyes* I won't forget this. Mark my words.";
        if (personality.Courage < 0.3f)
            return "*looks down* That... that hurt.";
        return "*scowls* Watch your tongue.";
    }

    private static string GenerateComplimentReaction(PersonalityProfile personality)
    {
        if (personality.Romanticism > 0.7f)
            return "*blushes* Oh my... you're too kind!";
        if (personality.Courage > 0.7f)
            return "*smiles confidently* I know, but thank you for noticing.";
        if (personality.Sociability > 0.7f)
            return "*beams* That's so sweet of you to say!";
        return "*nods* Thank you, I appreciate that.";
    }

    private static string GenerateThreatReaction(PersonalityProfile personality)
    {
        if (personality.Courage > 0.7f)
            return "*stands firm* You don't scare me. Try me.";
        if (personality.Aggression > 0.7f)
            return "*draws closer* Is that a threat? Because I can make threats too.";
        if (personality.Courage < 0.3f)
            return "*backs away* P-please, I don't want any trouble...";
        return "*tenses* Let's not do anything we'll regret.";
    }

    private static string GenerateFleeReaction(PersonalityProfile personality)
    {
        if (personality.Aggression > 0.7f)
            return "*growls* Running away? We should have fought to the end!";
        if (personality.Courage > 0.7f)
            return "A tactical retreat. Sometimes discretion is the better part of valor.";
        if (personality.Loyalty > 0.7f)
            return "I'm with you, whatever you decide. Let's get out of here!";
        if (personality.Courage < 0.3f)
            return "*relieved* Thank goodness we're getting away from that!";
        return "Probably wise. We can always come back stronger.";
    }

    private static string GenerateAllyDeathReaction(PersonalityProfile personality)
    {
        if (personality.Aggression > 0.7f)
            return "*enraged* No! Get up! We're not done fighting!";
        if (personality.Sociability > 0.7f)
            return "*cries out* No, please no! Stay with me!";
        if (personality.Loyalty > 0.7f)
            return "*kneels beside you* Don't you dare leave me! Not like this!";
        if (personality.Courage > 0.7f)
            return "*grabs your hand* Hold on! You're stronger than this!";
        if (personality.Romanticism > 0.7f)
            return "*tears streaming* My heart... please, don't leave me!";
        return "*gasps* No... this can't be happening!";
    }

    #endregion
}

/// <summary>
/// Container for archetype-specific vocabulary and speech patterns
/// </summary>
public class ArchetypeVocabulary
{
    public string[] Titles { get; set; } = Array.Empty<string>();
    public string[] Phrases { get; set; } = Array.Empty<string>();
    public string[] Topics { get; set; } = Array.Empty<string>();
    public string[] StyleModifiers { get; set; } = Array.Empty<string>();
}
