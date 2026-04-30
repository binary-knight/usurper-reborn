using System.Collections.Generic;

namespace UsurperRemake.Data
{
    /// <summary>
    /// The 11 alpha-era founders commemorated by in-world statues at beta launch
    /// (May 2026). Each founder reached one or more of: Level 100, Immortal
    /// Ascension, or Defeated Manwe and started NG+. Identified from the live
    /// online database before the beta wipe; narratives composed from their
    /// actual playstyle data (alignment, hours played, ending choices, kill
    /// counts). Two founders (Coosh, Zengazu) accidentally deleted their
    /// own characters before the wipe, and their statues are cracked, with
    /// dramatic-irony inscriptions.
    ///
    /// Each founder has a UNIQUE ASCII art piece themed to their character:
    /// harvest god, benevolent giver, speed-runner, dark conqueror, pure
    /// healer, long-walker, contradiction-bearer, cycle-wanderer, first
    /// shaman, lost immortal, and the renamed-and-erased.
    ///
    /// This list is FIXED. Post-beta-launch achievements are recognized
    /// through other systems (Hall of Fame, Steam achievements). The static
    /// nature of this data is intentional. These are historical commemorations,
    /// not a live leaderboard.
    /// </summary>
    public static class FounderStatueData
    {
        public enum StatueLocationTag
        {
            Pantheon,           // Immortal ascenders
            Castle,             // Manwe-slayers (NG+ at least 2)
            MainStreetMini,     // Level 100 founders (smaller plinths in the central square)
        }

        public class FounderStatue
        {
            public string Username { get; set; } = "";        // DB key, lowercase
            public string DisplayName { get; set; } = "";     // In-game character name
            public string ClassName { get; set; } = "";       // For plaque flavor
            public string RaceName { get; set; } = "";
            public int FinalLevel { get; set; }
            public string? DivineName { get; set; }            // For Pantheon statues
            public int CycleReached { get; set; }
            public string EndingTag { get; set; } = "";       // "Savior" / "Usurper" / "Defiant" / "Pre-NG+" / "Multiple"
            public string Inscription { get; set; } = "";     // The carved-stone narrative
            public StatueLocationTag Location { get; set; }
            public bool IsCracked { get; set; }                // Visual-marker variant for Coosh/Zengazu
            public string AchievementTag { get; set; } = "";  // Plaque header: "Immortal" / "Manwe-Slayer" / "Lv.100"
            public string[] CustomArt { get; set; } = System.Array.Empty<string>();  // Unique themed silhouette
            public string ArtColor { get; set; } = "white";   // Color hint for the art
        }

        // ─── Per-founder ASCII art ───────────────────────────────────
        //
        // 11 unique pieces, each themed to that player's narrative. All ~10-13
        // lines tall to fit a consistent visual band. ASCII-only (no Unicode)
        // so it renders cleanly on BBS / CP437 terminals as well as modern
        // UTF-8 clients.

        // spudman, "Spud of the Harvest" — Barbarian harvest god,
        // crossed scythes over a bound wheat sheaf
        private static readonly string[] ART_SPUDMAN = new[]
        {
            "             \\  |  /",
            "              \\ | /         ",
            "          ___\\_|_/___",
            "         /            \\",
            "        |  scythes  +  |",
            "         \\___________/",
            "          | bound  |",
            "          | wheat  |",
            "          |________|",
            "           |      |",
            "           |______|",
            "          ==========",
        };

        // quent, "the Benevolent" — Barbarian who hoarded gold and kept none.
        // Open hand with coins falling
        private static readonly string[] ART_QUENT = new[]
        {
            "            o   o",
            "           o  o  o",
            "         o  falling  o",
            "        ____________",
            "         \\        /",
            "          \\ open /",
            "           \\hand/",
            "            \\__/",
            "            |  |",
            "            |  |",
            "          ========",
        };

        // fastfinge, "the Unburdened" — Lv.100 Warrior who ascended in 9
        // hours without finishing the last battle. Empty pedestal.
        private static readonly string[] ART_FASTFINGE = new[]
        {
            "             ~",
            "           ~   ~",
            "          ~     ~       ",
            "           ~   ~",
            "             ~",
            "       ______________",
            "      |              |",
            "      |    (gone)    |",
            "      |              |",
            "      |______________|",
            "       |            |",
            "       |____________|",
            "      ================",
        };

        // sandoval, the dark conqueror. Lifetime darkness was 43,753 before
        // the alignment cap was hung. Skull beneath a spiked crown.
        private static readonly string[] ART_SANDOVAL = new[]
        {
            "          /\\/\\/\\/\\",
            "          | spiked|",
            "          | crown |",
            "          |_______|",
            "             ___",
            "            /. .\\",
            "           |  v  |",
            "           |_____|",
            "          |       |",
            "          | UNDER |",
            "          |_______|",
            "         |         |",
            "         |_________|",
            "         ===========",
        };

        // amaranth, "the Unstained" — Tidesworn with zero darkness. A single
        // lily floating on calm water.
        private static readonly string[] ART_AMARANTH = new[]
        {
            "              *",
            "             /|\\",
            "            / | \\",
            "           '..+..'",
            "         ~ ~ ~ ~ ~ ~",
            "        ~~~~~~~~~~~~~",
            "       _______________",
            "      |               |",
            "      |   undimmed    |",
            "      |_______________|",
            "       |             |",
            "       |_____________|",
            "       ===============",
        };

        // lueldora, "Long-Walker" — 610 hours played, all seven seals,
        // every artifact. Seven stars around a long road.
        private static readonly string[] ART_LUELDORA = new[]
        {
            "        *   *   *",
            "          \\ | /",
            "       *--- + ---*",
            "          / | \\",
            "        *   |   *",
            "            |",
            "            |",
            "       _____|_____",
            "      |  the long |",
            "      |   road    |",
            "      |___________|",
            "       |         |",
            "       |_________|",
            "       ===========",
        };

        // mystic, the Cyclebreaker who stayed faithful but chose Usurper.
        // A broken halo / split circle.
        private static readonly string[] ART_MYSTIC = new[]
        {
            "          .--.--.",
            "         /   |   \\",
            "        |    |    |",
            "         \\___|___/",
            "             |",
            "         the |split",
            "             |",
            "       ______|______",
            "      | contra-     |",
            "      |   diction   |",
            "      |_____________|",
            "       |           |",
            "       |___________|",
            "       =============",
        };

        // biaxin, "Cycle-Walker" Voidreaver, walked the cycle four times
        // and earned two endings. Four nested rings.
        private static readonly string[] ART_BIAXIN = new[]
        {
            "         .-------.",
            "        / .-----. \\",
            "       | / .---. \\ |",
            "       || / . . \\ ||",
            "       || \\  .  / ||",
            "       | \\ '---' / |",
            "        \\ '-----' /",
            "         '-------'",
            "        IV cycles",
            "       _____________",
            "      |             |",
            "      |_____________|",
            "       =============",
        };

        // sedz, the First Shaman. A spiral totem with spirit wisps.
        private static readonly string[] ART_SEDZ = new[]
        {
            "           ~ ~ ~",
            "          ~     ~",
            "         ~  /\\   ~",
            "            \\/",
            "          /====\\",
            "         |  o   |",
            "         | spi- |",
            "         |  ral |",
            "         |  o   |",
            "          \\====/",
            "        ____| |____",
            "       |           |",
            "       |___________|",
            "       =============",
        };

        // coosh, ascended then deleted by misadventure. Empty halo with cracks
        // running through it. Visual punchline supports the "Pantheon does not
        // refund" inscription.
        private static readonly string[] ART_COOSH_CRACKED = new[]
        {
            "           .---.",
            "          /  / \\",
            "         |  /   |",
            "          \\/   /",
            "          /\\  /",
            "         /  \\/",
            "        /   /\\",
            "       (   /  )",
            "      ( no  refund )",
            "       \\  /    \\",
            "       _\\/______\\__",
            "      | /        / |",
            "      |/________/__|",
            "      ===/=========",
        };

        // zengazu, NG+ veteran who renamed in jest and deleted by accident.
        // A scratched-out name plate where his second name used to be.
        private static readonly string[] ART_ZENGAZU_CRACKED = new[]
        {
            "        ___________",
            "       |2 ze   2   |",
            "       |n##zu fu###|",
            "       |  ##   us  |",
            "       |___________|",
            "         /\\    /\\",
            "        /  \\  /  \\",
            "       /    \\/    \\",
            "      |  beware    |",
            "      |  the rename|",
            "      |__/__\\__/___|",
            "       /         \\",
            "      |     XX    |",
            "      |___________|",
            "      ====/========",
        };

        // ─── Statue Placements ───────────────────────────────────────

        public static readonly List<FounderStatue> Statues = new()
        {
            // ─── Pantheon (Immortal Ascenders) ─────────────────────────

            new()
            {
                Username = "spudman",
                DisplayName = "spudman",
                ClassName = "Barbarian",
                RaceName = "Half-Elf",
                FinalLevel = 100,
                DivineName = "Spud of the Harvest",
                CycleReached = 2,
                EndingTag = "Savior",
                Location = StatueLocationTag.Pantheon,
                AchievementTag = "Immortal",
                CustomArt = ART_SPUDMAN,
                ArtColor = "yellow",
                Inscription = "Once a brawler of these halls. Now a god of fields and feast-days. He raised his axe against Manwe and chose to mend the broken pantheon. Every harvest hence belongs to him."
            },
            new()
            {
                Username = "quent",
                DisplayName = "Quent",
                ClassName = "Barbarian",
                RaceName = "Human",
                FinalLevel = 100,
                DivineName = "Quent the Benevolent",
                CycleReached = 2,
                EndingTag = "Savior",
                Location = StatueLocationTag.Pantheon,
                AchievementTag = "Immortal",
                CustomArt = ART_QUENT,
                ArtColor = "bright_yellow",
                Inscription = "He gathered more gold than any soul before him and kept none of it. Ascended in silence, he is remembered by those who never knew his name."
            },
            new()
            {
                Username = "fastfinge",
                DisplayName = "fastfinge",
                ClassName = "Warrior",
                RaceName = "Half-Elf",
                FinalLevel = 100,
                DivineName = "fastfinge the Unburdened",
                CycleReached = 1,
                EndingTag = "Pre-NG+",
                Location = StatueLocationTag.Pantheon,
                AchievementTag = "Immortal",
                CustomArt = ART_FASTFINGE,
                ArtColor = "cyan",
                Inscription = "Some say he reached the throne of gods without ever finishing the last battle. He slipped past Manwe in the dark, and the heavens admitted him anyway. The shortest path ever walked."
            },
            new()
            {
                Username = "sandoval",
                DisplayName = "Sandoval",
                ClassName = "Warrior",
                RaceName = "Half-Elf",
                FinalLevel = 93,
                DivineName = "Sandoval",
                CycleReached = 2,
                EndingTag = "Usurper",
                Location = StatueLocationTag.Pantheon,
                AchievementTag = "Immortal",
                CustomArt = ART_SANDOVAL,
                ArtColor = "red",
                Inscription = "Bathed in shadow long before the Cap was hung. He devoured the pantheon to claim its empty throne, and named no other god above himself."
            },
            new()
            {
                Username = "coosh",
                DisplayName = "Coosh",
                ClassName = "Unknown",
                RaceName = "Unknown",
                FinalLevel = 0,
                DivineName = "Coosh",
                CycleReached = 2,
                EndingTag = "Lost",
                Location = StatueLocationTag.Pantheon,
                IsCracked = true,
                AchievementTag = "Immortal (Lost)",
                CustomArt = ART_COOSH_CRACKED,
                ArtColor = "dark_gray",
                Inscription = "Coosh, ascended of the second cycle, godhood freshly upon him. He sought a finer raiment, pressed the wrong rune, and was undone. The Pantheon does not refund."
            },

            // ─── Castle Courtyard (Manwe-Slayers, NG+ at least 2) ─────

            new()
            {
                Username = "spudman",
                DisplayName = "spudman",
                ClassName = "Barbarian",
                RaceName = "Half-Elf",
                FinalLevel = 100,
                CycleReached = 2,
                EndingTag = "Savior",
                Location = StatueLocationTag.Castle,
                AchievementTag = "Manwe-Slayer",
                CustomArt = ART_SPUDMAN,
                ArtColor = "yellow",
                Inscription = "Once a brawler of these halls. Now a god of fields and feast-days. He raised his axe against Manwe and chose to mend the broken pantheon. Every harvest hence belongs to him."
            },
            new()
            {
                Username = "quent",
                DisplayName = "Quent",
                ClassName = "Barbarian",
                RaceName = "Human",
                FinalLevel = 100,
                CycleReached = 2,
                EndingTag = "Savior",
                Location = StatueLocationTag.Castle,
                AchievementTag = "Manwe-Slayer",
                CustomArt = ART_QUENT,
                ArtColor = "bright_yellow",
                Inscription = "He gathered more gold than any soul before him and kept none of it. Ascended in silence, he is remembered by those who never knew his name."
            },
            new()
            {
                Username = "sandoval",
                DisplayName = "Sandoval",
                ClassName = "Warrior",
                RaceName = "Half-Elf",
                FinalLevel = 93,
                CycleReached = 2,
                EndingTag = "Usurper",
                Location = StatueLocationTag.Castle,
                AchievementTag = "Manwe-Slayer",
                CustomArt = ART_SANDOVAL,
                ArtColor = "red",
                Inscription = "Bathed in shadow long before the Cap was hung. He devoured the pantheon to claim its empty throne, and named no other god above himself."
            },
            new()
            {
                Username = "amaranth",
                DisplayName = "Amaranth",
                ClassName = "Tidesworn",
                RaceName = "Elf",
                FinalLevel = 57,
                CycleReached = 2,
                EndingTag = "Savior",
                Location = StatueLocationTag.Castle,
                AchievementTag = "Manwe-Slayer",
                CustomArt = ART_AMARANTH,
                ArtColor = "bright_white",
                Inscription = "Her blade was the tide and her hand never closed on a shadowed coin. She slew Manwe without hatred and walked the cycle again before dawn."
            },
            new()
            {
                Username = "lueldora",
                DisplayName = "Lumina Starbloom",
                ClassName = "Tidesworn",
                RaceName = "Half-Elf",
                FinalLevel = 51,
                CycleReached = 2,
                EndingTag = "Savior",
                Location = StatueLocationTag.Castle,
                AchievementTag = "Long-Walker",
                CustomArt = ART_LUELDORA,
                ArtColor = "bright_cyan",
                Inscription = "Six hundred hours she walked these halls. She gathered every seal, knew every god by name, and chose mortality over a throne. The longest road."
            },
            new()
            {
                Username = "mystic",
                DisplayName = "Mystic",
                ClassName = "Cyclebreaker",
                RaceName = "Elf",
                FinalLevel = 34,
                CycleReached = 2,
                EndingTag = "Usurper",
                Location = StatueLocationTag.Castle,
                AchievementTag = "Manwe-Slayer",
                CustomArt = ART_MYSTIC,
                ArtColor = "bright_magenta",
                Inscription = "A faith-hearted soul who chose to consume the heavens regardless. The contradiction is carved here in stone, as it lived in her."
            },
            new()
            {
                Username = "biaxin",
                DisplayName = "Alistar The Almighty",
                ClassName = "Voidreaver",
                RaceName = "Mutant",
                FinalLevel = 10,
                CycleReached = 4,
                EndingTag = "Multiple",
                Location = StatueLocationTag.Castle,
                AchievementTag = "Cycle-Walker",
                CustomArt = ART_BIAXIN,
                ArtColor = "bright_magenta",
                Inscription = "Four times he walked the wheel. Once he ate the gods, once he denied them, and twice more he simply went again, to see what he had not yet seen."
            },
            new()
            {
                Username = "sedz",
                DisplayName = "Sedz",
                ClassName = "Mystic Shaman",
                RaceName = "Half-Elf",
                FinalLevel = 10,
                CycleReached = 2,
                EndingTag = "Savior",
                Location = StatueLocationTag.Castle,
                AchievementTag = "First Shaman",
                CustomArt = ART_SEDZ,
                ArtColor = "green",
                Inscription = "Among the first to learn the totem-craft. She mended what she could, and the spirits remember."
            },
            new()
            {
                Username = "zengazu",
                DisplayName = "Zengazu",
                ClassName = "Unknown",
                RaceName = "Unknown",
                FinalLevel = 0,
                CycleReached = 2,
                EndingTag = "Lost",
                Location = StatueLocationTag.Castle,
                IsCracked = true,
                AchievementTag = "Manwe-Slayer (Lost)",
                CustomArt = ART_ZENGAZU_CRACKED,
                ArtColor = "dark_gray",
                Inscription = "Zengazu, victorious of the second cycle, took a new name in jest. The name was \"2zengazu2furious\". He hated it. He sought its undoing, and undid all things. Beware the rename."
            },

            // ─── Main Street Mini-Plinths (Level 100 Trio) ─────────────

            new()
            {
                Username = "spudman",
                DisplayName = "spudman",
                ClassName = "Barbarian",
                RaceName = "Half-Elf",
                FinalLevel = 100,
                Location = StatueLocationTag.MainStreetMini,
                AchievementTag = "Lv.100 Founder",
                CustomArt = ART_SPUDMAN,
                ArtColor = "yellow",
                Inscription = "First to the summit. Cycle II Savior."
            },
            new()
            {
                Username = "quent",
                DisplayName = "Quent",
                ClassName = "Barbarian",
                RaceName = "Human",
                FinalLevel = 100,
                Location = StatueLocationTag.MainStreetMini,
                AchievementTag = "Lv.100 Founder",
                CustomArt = ART_QUENT,
                ArtColor = "bright_yellow",
                Inscription = "Climbed the summit in silence. Cycle II Savior."
            },
            new()
            {
                Username = "fastfinge",
                DisplayName = "fastfinge",
                ClassName = "Warrior",
                RaceName = "Half-Elf",
                FinalLevel = 100,
                Location = StatueLocationTag.MainStreetMini,
                AchievementTag = "Lv.100 Founder",
                CustomArt = ART_FASTFINGE,
                ArtColor = "cyan",
                Inscription = "The shortest path ever walked."
            },
        };

        /// <summary>
        /// Get all statues at a given location.
        /// </summary>
        public static IEnumerable<FounderStatue> GetStatuesAt(StatueLocationTag location)
        {
            foreach (var s in Statues)
            {
                if (s.Location == location) yield return s;
            }
        }

        /// <summary>
        /// Get total count of unique founders (deduped across multi-placement statues).
        /// </summary>
        public static int GetUniqueFounderCount()
        {
            var seen = new HashSet<string>();
            foreach (var s in Statues) seen.Add(s.Username);
            return seen.Count;
        }
    }
}
