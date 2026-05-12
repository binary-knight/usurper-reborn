using System.Collections.Generic;

namespace UsurperRemake.Data;

/// <summary>
/// The seven gladiator champions of the Anchor Road Gauntlet -- mortal vessels chosen by
/// each of the Old Gods to fight in their name. v0.60.11 redesign: replaced random-monster
/// waves 4-10 with hand-crafted named champions in canonical Old God difficulty order
/// (Maelketh -> Veloura -> Thorgrim -> Noctura -> Aurelion -> Terravok -> Manwe), each with
/// announced entrance, god-specific lore reveal, themed combat role, and themed equipment
/// drop on defeat.
///
/// Stats scale with player level at runtime in AnchorRoadLocation.SpawnChampion; the
/// values here are RELATIVE multipliers / role markers, not absolute stat blocks.
///
/// Order matters: champions are fought sequentially in array order. Champion 0 is the
/// first the player meets (after the 3 warmup waves), champion 6 (Manwe's Nameless Tyrant)
/// is the final fight.
/// </summary>
public static class GauntletChampionData
{
    /// <summary>Five tiers of "Gauntlet completion" gated on player level at moment of
    /// full clear. Each grants a different title (replaces knighthood prefix in display),
    /// a different achievement tier, and dramatically different gold/XP/Fame rewards.
    /// Top tier (GrandChampion) also unlocks a permanent +3% damage / +3% defense passive.</summary>
    public enum ArenaTier
    {
        None = 0,           // Hasn't beaten the Gauntlet.
        Hopeful = 1,        // Beat at Lv 5-19.   Title: "Arena Hopeful".   Achievement: Bronze.
        Veteran = 2,        // Beat at Lv 20-39.  Title: "Arena Veteran".   Achievement: Silver.
        Master = 3,         // Beat at Lv 40-59.  Title: "Arena Master".    Achievement: Gold.
        Champion = 4,       // Beat at Lv 60-79.  Title: "Arena Champion".  Achievement: Platinum.
        GrandChampion = 5   // Beat at Lv 80-100. Title: "Grand Champion".  Achievement: Diamond.
    }

    /// <summary>Map player level to the arena tier they earn on a full clear.</summary>
    public static ArenaTier GetTierForLevel(int playerLevel) => playerLevel switch
    {
        <= 19 => ArenaTier.Hopeful,
        <= 39 => ArenaTier.Veteran,
        <= 59 => ArenaTier.Master,
        <= 79 => ArenaTier.Champion,
        _     => ArenaTier.GrandChampion
    };

    /// <summary>Display title for a tier. Replaces Sir/Dame knighthood prefix in /who,
    /// Main Street citizen lists, website leaderboard, and /health.</summary>
    public static string GetTierTitle(ArenaTier tier) => tier switch
    {
        ArenaTier.Hopeful       => "Arena Hopeful",
        ArenaTier.Veteran       => "Arena Veteran",
        ArenaTier.Master        => "Arena Master",
        ArenaTier.Champion      => "Arena Champion",
        ArenaTier.GrandChampion => "Grand Champion",
        _ => ""
    };

    /// <summary>Achievement ID per tier. Each is its own achievement entry so re-clears
    /// at higher level brackets unlock new achievements without invalidating earlier ones.</summary>
    public static string GetTierAchievementId(ArenaTier tier) => tier switch
    {
        ArenaTier.Hopeful       => "arena_hopeful",
        ArenaTier.Veteran       => "arena_veteran",
        ArenaTier.Master        => "arena_master",
        ArenaTier.Champion      => "arena_champion",
        ArenaTier.GrandChampion => "grand_champion",
        _ => ""
    };

    public class TierRewards
    {
        public int GoldMultiplierPerLevel { get; init; }   // Gold reward = lvl * this
        public int XpMultiplierPerLevel { get; init; }     // XP reward   = lvl * this
        public int FameBonus { get; init; }                // Flat Fame on full clear
    }

    /// <summary>Per-tier reward scaling on a full Gauntlet clear. Multiplies by player
    /// level so a Lv 80 Grand Champion clears 120,000g / 56,000 XP / +150 Fame, while a
    /// Lv 10 Hopeful clears 2,000g / 1,000 XP / +30 Fame. ~60x range top-to-bottom.</summary>
    public static TierRewards GetTierRewards(ArenaTier tier) => tier switch
    {
        ArenaTier.Hopeful       => new TierRewards { GoldMultiplierPerLevel =  200, XpMultiplierPerLevel = 100, FameBonus =  30 },
        ArenaTier.Veteran       => new TierRewards { GoldMultiplierPerLevel =  400, XpMultiplierPerLevel = 200, FameBonus =  50 },
        ArenaTier.Master        => new TierRewards { GoldMultiplierPerLevel =  700, XpMultiplierPerLevel = 350, FameBonus =  75 },
        ArenaTier.Champion      => new TierRewards { GoldMultiplierPerLevel = 1100, XpMultiplierPerLevel = 500, FameBonus = 100 },
        ArenaTier.GrandChampion => new TierRewards { GoldMultiplierPerLevel = 1500, XpMultiplierPerLevel = 700, FameBonus = 150 },
        _ => new TierRewards()
    };

    public enum ChampionRole
    {
        /// <summary>Pure damage. High STR / WP, low DEF. Race forward, hit hard.</summary>
        Berserker,
        /// <summary>Graceful duelist. Crit / poison / charm. Hit-and-evade.</summary>
        Duelist,
        /// <summary>Immovable tank. Massive DEF / HP / thorn reflect. Doesn't die quickly.</summary>
        Stonewall,
        /// <summary>Shadow assassin. Stealth, guaranteed crits, hit-and-run.</summary>
        Assassin,
        /// <summary>Holy paladin. Heal / shield / radiant damage.</summary>
        Paladin,
        /// <summary>Beast-master / wild mage. Summons, regen, high HP.</summary>
        Feral,
        /// <summary>Divine tyrant. Multi-phase. Everything.</summary>
        Tyrant
    }

    public class GauntletChampion
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string Title { get; init; } = "";
        public string GodPatron { get; init; } = "";   // Display name of the Old God they serve.
        public ChampionRole Role { get; init; }

        /// <summary>HP multiplier applied to base scaled stats. ~2.0x means twice a normal
        /// at-level monster of equivalent tier.</summary>
        public float HpMultiplier { get; init; }
        /// <summary>Attack multiplier on base scaled stats.</summary>
        public float AttackMultiplier { get; init; }
        /// <summary>Defense multiplier on base scaled stats.</summary>
        public float DefenseMultiplier { get; init; }
        /// <summary>Player-level offset for this champion. Champion's effective combat level
        /// = playerLevel + LevelBonus. Climbs from +2 to +14 across the seven slots so the
        /// final fight is meaningfully harder than the wave-10 boss the old gauntlet had.</summary>
        public int LevelBonus { get; init; }

        /// <summary>The herald's announcement of the champion's arrival. Read aloud before
        /// combat begins. Should land in 2-4 lines. Use {0} for the player's display name
        /// where it adds drama; leave plain otherwise.</summary>
        public string[] EntranceAnnouncement { get; init; } = System.Array.Empty<string>();

        /// <summary>A one-line god-lore reveal printed in italics after the entrance. Connects
        /// the champion to their patron's canonical story.</summary>
        public string LoreLine { get; init; } = "";

        /// <summary>What the crowd does when this champion walks out -- ambiance flavor.</summary>
        public string CrowdReaction { get; init; } = "";

        /// <summary>Themed equipment drop on defeat. Generated at runtime by
        /// AnchorRoadLocation.GenerateChampionDrop; this struct just declares the slot,
        /// name, and the kind of bonuses it should carry.</summary>
        public ChampionDrop Drop { get; init; } = new();

        /// <summary>Monster-ability strings the champion has access to. Picked at random
        /// per round by the existing MonsterAbilities pipeline. Empty = basic attacks only.</summary>
        public string[] SpecialAbilities { get; init; } = System.Array.Empty<string>();
    }

    public class ChampionDrop
    {
        public string ItemName { get; init; } = "";
        public string SlotHint { get; init; } = "";       // "MainHand" / "Body" / "Neck" etc.
        public string FlavorDescription { get; init; } = "";
    }

    public static readonly GauntletChampion[] Champions = new[]
    {
        // ============================================================================
        // CHAMPION 1 -- Maelketh's: brutal orc-warrior. Pure damage, no subtlety.
        // ============================================================================
        new GauntletChampion
        {
            Id = "vargash",
            Name = "Vargash the Blade-Bound",
            Title = "Champion of Maelketh",
            GodPatron = "Maelketh, The Broken Blade",
            Role = ChampionRole.Berserker,
            HpMultiplier = 1.8f,
            AttackMultiplier = 1.4f,
            DefenseMultiplier = 0.8f,
            LevelBonus = 2,
            EntranceAnnouncement = new[]
            {
                "The herald raises his hand. The crowd quiets.",
                "\"From the Broken Coast -- Vargash, blade-bound and tireless! He has not",
                " laid down his sword in twelve years. The Broken Blade's mark burns on",
                " his shoulder. He fights for Maelketh!\""
            },
            LoreLine = "He fights as the Broken Blade fights: forward, always forward, until the enemy is gone.",
            CrowdReaction = "The crowd roars. Spears clash against shields in the upper tiers.",
            Drop = new ChampionDrop
            {
                ItemName = "Vargash's Cleaver",
                SlotHint = "MainHand",
                FlavorDescription = "A heavy one-handed cleaver, blade still warm from the previous fight. Maelketh's mark is etched into the haft."
            },
            SpecialAbilities = new[] { "CriticalStrike", "Cleave", "Rage" }
        },

        // ============================================================================
        // CHAMPION 2 -- Veloura's: graceful duelist. Charm, poison, hit-and-evade.
        // ============================================================================
        new GauntletChampion
        {
            Id = "selithea",
            Name = "Selithea the Heartrender",
            Title = "Champion of Veloura",
            GodPatron = "Veloura, The Withered Heart",
            Role = ChampionRole.Duelist,
            HpMultiplier = 1.4f,
            AttackMultiplier = 1.2f,
            DefenseMultiplier = 1.0f,
            LevelBonus = 4,
            EntranceAnnouncement = new[]
            {
                "Velvet curtains part. The torches dim. A woman walks the sand alone.",
                "\"The Withered Heart sends a dancer. Selithea moves like water and ends",
                " like grief. She has broken more vows than she has bones. She fights",
                " for Veloura!\""
            },
            LoreLine = "Love is a wound. She is here to remind you.",
            CrowdReaction = "A hushed murmur runs through the stands. Coins clink as bets are quietly placed.",
            Drop = new ChampionDrop
            {
                ItemName = "Selithea's Ribbon",
                SlotHint = "Cloak",
                FlavorDescription = "A long silken ribbon, dyed deep red. Worn at the waist by Veloura's dancers. Wards off charms and graces the wearer with poise."
            },
            SpecialAbilities = new[] { "VenomousBite", "Charm", "Vanish" }
        },

        // ============================================================================
        // CHAMPION 3 -- Thorgrim's: stonewall. Cannot be moved. Endurance fight.
        // ============================================================================
        new GauntletChampion
        {
            Id = "korr",
            Name = "Korr Stonewarden",
            Title = "Champion of Thorgrim",
            GodPatron = "Thorgrim, The Hollow Judge",
            Role = ChampionRole.Stonewall,
            HpMultiplier = 3.0f,
            AttackMultiplier = 1.0f,
            DefenseMultiplier = 2.2f,
            LevelBonus = 6,
            EntranceAnnouncement = new[]
            {
                "The gate grinds open. The shape that emerges is barely a man.",
                "\"Korr Stonewarden walks where he is told to walk. The Hollow Judge has",
                " weighed him and found him sufficient. He has held his ground in fights",
                " longer than most lives. He fights for Thorgrim!\""
            },
            LoreLine = "He does not advance. He does not retreat. The verdict is law.",
            CrowdReaction = "The crowd settles in. They've seen Korr before. They know this is going to take a while.",
            Drop = new ChampionDrop
            {
                ItemName = "Stonewarden's Plate",
                SlotHint = "Body",
                FlavorDescription = "Massive plate armor, the metal cold to the touch. Pressure points are etched with Thorgrim's law-runes. Feels heavier than it should."
            },
            SpecialAbilities = new[] { "Stoneskin", "Thorns", "ArmorHarden" }
        },

        // ============================================================================
        // CHAMPION 4 -- Noctura's: shadow twin. Hides, strikes from impossible angles.
        // ============================================================================
        new GauntletChampion
        {
            Id = "black_twin",
            Name = "The Black Twin",
            Title = "Champion of Noctura",
            GodPatron = "Noctura, The Shadow Weaver",
            Role = ChampionRole.Assassin,
            HpMultiplier = 1.6f,
            AttackMultiplier = 1.6f,
            DefenseMultiplier = 0.9f,
            LevelBonus = 8,
            EntranceAnnouncement = new[]
            {
                "Every torch in the arena dies at once. When they relight, he is already on the sand.",
                "\"From the Weaver's loom of silence -- the Black Twin. He has no face. He",
                " has no name his mother kept. They say he has stepped from shadow into",
                " seventeen throats tonight, and not one of you saw. He fights for Noctura!\""
            },
            LoreLine = "Noctura sends her children, and her children remember nothing.",
            CrowdReaction = "Total silence. The kind of silence that comes after a bad omen.",
            Drop = new ChampionDrop
            {
                ItemName = "Twin-Step Veil",
                SlotHint = "Face",
                FlavorDescription = "A black silk veil that swallows the wearer's features. Noctura's gift to her chosen -- the wearer's name becomes harder to remember."
            },
            SpecialAbilities = new[] { "Backstab", "Vanish", "PhaseShift" }
        },

        // ============================================================================
        // CHAMPION 5 -- Aurelion's: fading paladin. Holy fire from a dying god.
        // ============================================================================
        new GauntletChampion
        {
            Id = "aedric",
            Name = "Sir Aedric, the Last Lantern",
            Title = "Champion of Aurelion",
            GodPatron = "Aurelion, The Fading Light",
            Role = ChampionRole.Paladin,
            HpMultiplier = 2.0f,
            AttackMultiplier = 1.3f,
            DefenseMultiplier = 1.8f,
            LevelBonus = 10,
            EntranceAnnouncement = new[]
            {
                "Sunlight streams through a high arena window -- a beam that has not been there before.",
                "\"Sir Aedric was the last knight to swear his oath when the sun still rose.",
                " He kneels for a god who has stopped answering. His blade still burns.",
                " He has not noticed his god is dying. He fights for Aurelion!\""
            },
            LoreLine = "The light fades. The vow does not.",
            CrowdReaction = "Old patrons rise to their feet. Some weep. Most don't know why.",
            Drop = new ChampionDrop
            {
                ItemName = "Aedric's Lantern-Blade",
                SlotHint = "MainHand",
                FlavorDescription = "A longsword whose edge glows faintly, as if remembering a sunrise. Aurelion's last knight wielded it; now it passes to you."
            },
            SpecialAbilities = new[] { "HolySmite", "Heal", "DivineJudgment" }
        },

        // ============================================================================
        // CHAMPION 6 -- Terravok's: feral pack-father. Beast magic, regen, brutal.
        // ============================================================================
        new GauntletChampion
        {
            Id = "grok",
            Name = "Grok of the Pack-Stone",
            Title = "Champion of Terravok",
            GodPatron = "Terravok, The Sleeping Mountain",
            Role = ChampionRole.Feral,
            HpMultiplier = 2.6f,
            AttackMultiplier = 1.5f,
            DefenseMultiplier = 1.3f,
            LevelBonus = 12,
            EntranceAnnouncement = new[]
            {
                "The gate doesn't open. Grok comes over it.",
                "\"From the deep places, where the Sleeping Mountain dreams -- Grok of the",
                " Pack-Stone. He runs with wolves who remember the first names. They say",
                " he has eaten the hearts of three champions who came before. He fights",
                " for Terravok!\""
            },
            LoreLine = "The Mountain sleeps. The pack does not.",
            CrowdReaction = "The crowd makes pack-noise -- howling, stomping, beating shields. Some join Grok's side just for the spectacle.",
            Drop = new ChampionDrop
            {
                ItemName = "Pack-Stone Talisman",
                SlotHint = "Neck",
                FlavorDescription = "A pendant of cracked stone, warm against the chest. Wearer feels the breathing of the Mountain. STR and CON come easier."
            },
            SpecialAbilities = new[] { "SummonMinions", "Regeneration", "Bite", "Howl" }
        },

        // ============================================================================
        // CHAMPION 7 -- Manwe's: The Nameless Tyrant. The fight nobody is supposed to win.
        // ============================================================================
        new GauntletChampion
        {
            Id = "nameless_tyrant",
            Name = "The Nameless Tyrant",
            Title = "Champion of Manwe",
            GodPatron = "Manwe, The Weary Creator",
            Role = ChampionRole.Tyrant,
            HpMultiplier = 4.0f,
            AttackMultiplier = 1.8f,
            DefenseMultiplier = 2.0f,
            LevelBonus = 14,
            EntranceAnnouncement = new[]
            {
                "The herald does not speak. He cannot. The arena lights die one by one until only",
                "the sand is lit, and even the sand goes gray.",
                "",
                "A man walks out. He has no name because Manwe does not give names to",
                "executioners. He has fought in this arena since before any of you drew breath.",
                "Every champion before him was a warm-up.",
                "",
                "(The crowd does not cheer. The crowd is afraid.)"
            },
            LoreLine = "The Creator's verdict walks the sand.",
            CrowdReaction = "Silence. Long, ringing silence. Somewhere far above, a child starts crying.",
            Drop = new ChampionDrop
            {
                ItemName = "Tyrant's Crown",
                SlotHint = "Head",
                FlavorDescription = "A jagged circlet of unworked black metal. The Tyrant wore it but did not need it. It carries weight no inventory can quite hold."
            },
            SpecialAbilities = new[] { "DivineJudgment", "DragonFear", "Enrage", "CrushingBlow", "DevourSoul" }
        }
    };

    /// <summary>Crowd ambiance lines printed between fights -- random pick to keep the arena
    /// feeling alive. Mix of cheering, jeering, and arena flavor.</summary>
    public static readonly string[] CrowdAmbiance = new[]
    {
        "Vendors shout above the noise. \"Boiled eggs! Cool wine! Bets! Last bets before the next gate!\"",
        "A drum starts somewhere in the upper tiers. By the second beat the whole arena has joined.",
        "Slaves rake the sand smooth. The dark patches don't come out.",
        "The crowd chants something rhythmic. It sounds like your name. It is not.",
        "Torches gutter and are relit. The shadows move wrong for a moment.",
        "A noble in the front row leans forward, smiling. You don't like her smile.",
        "Children sit on parents' shoulders, eating sweet bread, staring.",
        "Somewhere a priest of one of the gods is praying very loudly. Nobody listens.",
        "Two guards drag a body off the sand. They have done this many times today.",
        "A herald walks the perimeter, calling out the next bet odds. They've gotten longer.",
        "The arena master watches from his box, unmoving, expressionless.",
        "Old fighters in the cheap seats murmur to each other. Some of them know what's coming."
    };

    /// <summary>Warmup wave flavor for the first 3 fights -- generic "opening acts" before
    /// the champion lineup begins. Each wave picks one of these themes at random.</summary>
    public static readonly string[] WarmupWaveFlavor = new[]
    {
        "A condemned criminal is shoved onto the sand, given a rusted sword, and told to make it interesting.",
        "An escaped slave is offered freedom if they can survive. They charge with nothing to lose.",
        "A dire beast is released from the city's menagerie -- starving, half-mad, and very large.",
        "Two prisoners are pushed out. Only one is given a weapon. They scrap in front of you first; the survivor turns your way.",
        "A washed-up old gladiator, drunk on cheap wine, stumbles into the ring. He used to be somebody. He still has hands.",
        "A foreign sellsword, hooded, paid in silver. No name announced. The crowd boos when they realize."
    };
}
