using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using UsurperRemake.Utils;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Artifact System - Manages the seven divine artifacts needed to defeat the Old Gods
    /// Each artifact grants unique powers and is required for specific endings
    /// </summary>
    public class ArtifactSystem
    {
        private static ArtifactSystem? instance;
        public static ArtifactSystem Instance => instance ??= new ArtifactSystem();

        private Dictionary<ArtifactType, ArtifactData> artifacts = new();

        public event Action<ArtifactType>? OnArtifactCollected;
        public event Action? OnAllArtifactsCollected;

        public ArtifactSystem()
        {
            InitializeArtifacts();
        }

        /// <summary>
        /// Initialize all artifact data
        /// </summary>
        private void InitializeArtifacts()
        {
            // The Creator's Eye - Dropped by Maelketh (or found after defeating him)
            artifacts[ArtifactType.CreatorsEye] = new ArtifactData
            {
                Type = ArtifactType.CreatorsEye,
                Name = "The Creator's Eye",
                Description = "A crystalline orb that sees through all illusions and reveals hidden truths.",
                LoreText = new[]
                {
                    "Forged from the first light of creation, this artifact",
                    "was Manwe's gift to mortals - the ability to perceive truth.",
                    "It was corrupted by Maelketh, who used it to anticipate",
                    "every attack, every strategy, every hope of his enemies.",
                    "",
                    "Now cleansed, it grants the bearer insight beyond mortal ken."
                },
                ObtainedFrom = "Maelketh, God of War",
                DungeonFloor = 25,
                RequiredLevel = 30,
                StatBonuses = new Dictionary<string, int>
                {
                    { "Wisdom", 25 },
                    { "Intelligence", 20 },
                    { "Dexterity", 15 }
                },
                SpecialAbility = "True Sight - Immune to illusions and blindness. +50% critical hit chance.",
                IconColor = "bright_yellow"
            };

            // The Soulweaver's Loom - Can save Veloura
            artifacts[ArtifactType.SoulweaversLoom] = new ArtifactData
            {
                Type = ArtifactType.SoulweaversLoom,
                Name = "The Soulweaver's Loom",
                Description = "A delicate device that can untangle even the most corrupted souls.",
                LoreText = new[]
                {
                    "Before time had meaning, the Soulweaver's Loom shaped",
                    "the very essence of being. With it, destinies were written",
                    "and rewritten, souls crafted and refined.",
                    "",
                    "Manwe hid it when he realized its power could undo",
                    "even his own divine corruption. Find it, and you may",
                    "save those who seem beyond redemption."
                },
                ObtainedFrom = "Hidden in the Shadow Realm (Noctura's Domain)",
                DungeonFloor = 40,
                RequiredLevel = 45,
                StatBonuses = new Dictionary<string, int>
                {
                    { "Charisma", 30 },
                    { "Wisdom", 20 },
                    { "MaxMana", 100 }
                },
                SpecialAbility = "Soul Mending - Can attempt to save corrupted gods instead of destroying them. Heals 25% HP after each battle.",
                IconColor = "bright_magenta"
            };

            // The Scales of Absolute Law - From Thorgrim
            artifacts[ArtifactType.ScalesOfLaw] = new ArtifactData
            {
                Type = ArtifactType.ScalesOfLaw,
                Name = "The Scales of Absolute Law",
                Description = "Perfect balance made manifest. Weighs all actions against cosmic justice.",
                LoreText = new[]
                {
                    "Thorgrim carried these scales for ten thousand years,",
                    "using them to judge all who stood before him.",
                    "But the scales were never meant for a god.",
                    "",
                    "In mortal hands, they restore the original purpose:",
                    "to ensure that power serves justice, not the reverse.",
                    "With them, even the mightiest must answer for their deeds."
                },
                ObtainedFrom = "Thorgrim, God of Law",
                DungeonFloor = 50,
                RequiredLevel = 55,
                StatBonuses = new Dictionary<string, int>
                {
                    { "Defence", 25 },
                    { "Wisdom", 15 },
                    { "Stamina", 20 }
                },
                SpecialAbility = "Karmic Balance - Enemies deal reduced damage based on their 'evil' level. Reflects 15% of damage taken.",
                IconColor = "gray"
            };

            // The Shadow Crown - From Noctura (if allied) or her defeat
            artifacts[ArtifactType.ShadowCrown] = new ArtifactData
            {
                Type = ArtifactType.ShadowCrown,
                Name = "The Shadow Crown",
                Description = "A crown of living darkness that grants dominion over shadow.",
                LoreText = new[]
                {
                    "Noctura wove this crown from the spaces between stars,",
                    "from the darkness that existed before light was born.",
                    "It grants its wearer power over shadow itself.",
                    "",
                    "But beware: the crown does not distinguish between",
                    "the shadows outside and the shadows within.",
                    "Wear it long enough, and the darkness becomes you."
                },
                ObtainedFrom = "Noctura, Goddess of Shadows",
                DungeonFloor = 60,
                RequiredLevel = 65,
                StatBonuses = new Dictionary<string, int>
                {
                    { "Agility", 30 },
                    { "Dexterity", 25 },
                    { "Darkness", 100 }
                },
                SpecialAbility = "Shadow Step - 30% chance to dodge any attack. Can strike twice per round. -50 Chivalry cap.",
                IconColor = "dark_magenta"
            };

            // The Sunforged Blade - From Aurelion
            artifacts[ArtifactType.SunforgedBlade] = new ArtifactData
            {
                Type = ArtifactType.SunforgedBlade,
                Name = "The Sunforged Blade",
                Description = "A weapon of pure radiance, burning with the light of creation.",
                LoreText = new[]
                {
                    "Aurelion forged this blade from captured sunlight,",
                    "intending it to be a beacon of hope in dark times.",
                    "But Manwe twisted its purpose, making it a weapon",
                    "of blinding, merciless judgment.",
                    "",
                    "Freed from corruption, the blade remembers its purpose:",
                    "to illuminate, to protect, to inspire."
                },
                ObtainedFrom = "Aurelion, God of Light",
                DungeonFloor = 70,
                RequiredLevel = 75,
                StatBonuses = new Dictionary<string, int>
                {
                    { "Strength", 35 },
                    { "Chivalry", 100 },
                    { "WeapPow", 50 }
                },
                SpecialAbility = "Radiant Strike - +100% damage vs undead and demons. Attacks cannot miss. Heals allies for 10% of damage dealt.",
                IconColor = "bright_yellow"
            };

            // The Worldstone - From Terravok
            artifacts[ArtifactType.Worldstone] = new ArtifactData
            {
                Type = ArtifactType.Worldstone,
                Name = "The Worldstone",
                Description = "The heart of the earth itself, pulsing with primordial power.",
                LoreText = new[]
                {
                    "When the world was young, Terravok placed his heart",
                    "at the core of creation, giving life to stone and soil.",
                    "The Worldstone is that heart, given form.",
                    "",
                    "With it, one commands the very earth.",
                    "Mountains bow. Chasms open. The land itself fights",
                    "for the one who holds the Worldstone."
                },
                ObtainedFrom = "Terravok, God of Earth",
                DungeonFloor = 80,
                RequiredLevel = 85,
                StatBonuses = new Dictionary<string, int>
                {
                    { "Stamina", 50 },
                    { "Defence", 40 },
                    { "MaxHP", 500 }
                },
                SpecialAbility = "Earth's Embrace - Take 50% reduced damage. Immune to knockback and stun. Earthquake attack hits all enemies.",
                IconColor = "dark_green"
            };

            // The Void Key - Required to face Manwe
            artifacts[ArtifactType.VoidKey] = new ArtifactData
            {
                Type = ArtifactType.VoidKey,
                Name = "The Void Key",
                Description = "The final artifact. Opens the way to the Creator's prison.",
                LoreText = new[]
                {
                    "This key was forged from the moment before existence,",
                    "from the void that preceded all creation.",
                    "It unlocks not doors, but possibilities.",
                    "",
                    "Only when all six other artifacts are gathered,",
                    "only when all six Old Gods have been faced,",
                    "will the Void Key reveal itself.",
                    "",
                    "And then... you may challenge the Creator himself."
                },
                ObtainedFrom = "Appears when all other artifacts are collected",
                DungeonFloor = 100,
                RequiredLevel = 95,
                StatBonuses = new Dictionary<string, int>
                {
                    { "AllStats", 20 }
                },
                SpecialAbility = "Void Gate - Grants access to Manwe's Prison. All other artifact powers are doubled during the final battle.",
                IconColor = "white"
            };

            // GD.Print($"[Artifacts] Initialized {artifacts.Count} divine artifacts");
        }

        /// <summary>
        /// Get artifact data by type
        /// </summary>
        public ArtifactData? GetArtifact(ArtifactType type)
        {
            return artifacts.TryGetValue(type, out var artifact) ? artifact : null;
        }

        /// <summary>
        /// Get all artifacts
        /// </summary>
        public IEnumerable<ArtifactData> GetAllArtifacts()
        {
            return artifacts.Values;
        }

        /// <summary>
        /// Collect an artifact for the player
        /// </summary>
        public async Task<bool> CollectArtifact(Character player, ArtifactType type, TerminalEmulator terminal)
        {
            if (!artifacts.TryGetValue(type, out var artifact))
            {
                return false;
            }

            var story = StoryProgressionSystem.Instance;

            if (story.CollectedArtifacts.Contains(type))
            {
                terminal.WriteLine($"You already possess {artifact.Name}.", "yellow");
                return false;
            }

            // Display artifact collection sequence
            await DisplayArtifactCollection(artifact, terminal);

            // Add to story progression
            story.CollectArtifact(type);

            // Track archetype - Artifacts are Explorer/Sage items
            ArchetypeTracker.Instance.RecordArtifactCollected();

            // Apply stat bonuses
            ApplyArtifactBonuses(player, artifact);

            OnArtifactCollected?.Invoke(type);

            // Check if all artifacts collected
            if (story.CollectedArtifacts.Count >= 6 && !story.CollectedArtifacts.Contains(ArtifactType.VoidKey))
            {
                // Trigger Void Key appearance
                await TriggerVoidKeyAppearance(player, terminal);
            }

            if (story.CollectedArtifacts.Count >= 7)
            {
                OnAllArtifactsCollected?.Invoke();
            }

            return true;
        }

        /// <summary>
        /// Display the artifact collection sequence
        /// </summary>
        private async Task DisplayArtifactCollection(ArtifactData artifact, TerminalEmulator terminal)
        {
            terminal.Clear();
            terminal.WriteLine("");
            terminal.WriteLine("╔═══════════════════════════════════════════════════════════════════╗", artifact.IconColor);
            terminal.WriteLine("║              A R T I F A C T   A C Q U I R E D                    ║", artifact.IconColor);
            terminal.WriteLine("╚═══════════════════════════════════════════════════════════════════╝", artifact.IconColor);
            terminal.WriteLine("");

            await Task.Delay(500);

            terminal.WriteLine($"  {artifact.Name}", "bright_white");
            terminal.WriteLine("");

            await Task.Delay(300);

            terminal.WriteLine($"  \"{artifact.Description}\"", "cyan");
            terminal.WriteLine("");

            await Task.Delay(500);

            terminal.WriteLine("  --- LORE ---", "dark_cyan");
            foreach (var line in artifact.LoreText)
            {
                terminal.WriteLine($"  {line}", "white");
                await Task.Delay(100);
            }
            terminal.WriteLine("");

            await Task.Delay(500);

            terminal.WriteLine("  --- POWERS GRANTED ---", "bright_green");
            foreach (var bonus in artifact.StatBonuses)
            {
                terminal.WriteLine($"  +{bonus.Value} {bonus.Key}", "green");
                await Task.Delay(100);
            }
            terminal.WriteLine("");
            terminal.WriteLine($"  Special: {artifact.SpecialAbility}", "bright_yellow");
            terminal.WriteLine("");

            await terminal.GetInputAsync("  Press Enter to continue...");
        }

        /// <summary>
        /// Apply artifact stat bonuses to player
        /// </summary>
        private void ApplyArtifactBonuses(Character player, ArtifactData artifact)
        {
            foreach (var bonus in artifact.StatBonuses)
            {
                switch (bonus.Key.ToLower())
                {
                    case "strength":
                        player.Strength += bonus.Value;
                        break;
                    case "defence":
                        player.Defence += bonus.Value;
                        break;
                    case "stamina":
                        player.Stamina += bonus.Value;
                        break;
                    case "agility":
                        player.Agility += bonus.Value;
                        break;
                    case "charisma":
                        player.Charisma += bonus.Value;
                        break;
                    case "dexterity":
                        player.Dexterity += bonus.Value;
                        break;
                    case "wisdom":
                        player.Wisdom += bonus.Value;
                        break;
                    case "intelligence":
                        // Map to an appropriate stat
                        player.Wisdom += bonus.Value / 2;
                        player.Dexterity += bonus.Value / 2;
                        break;
                    case "maxhp":
                        player.MaxHP += bonus.Value;
                        player.HP += bonus.Value;
                        break;
                    case "maxmana":
                        player.MaxMana += bonus.Value;
                        player.Mana += bonus.Value;
                        break;
                    case "weappow":
                        player.BonusWeapPow += bonus.Value;
                        break;
                    case "chivalry":
                        player.Chivalry += bonus.Value;
                        break;
                    case "darkness":
                        player.Darkness += bonus.Value;
                        break;
                    case "allstats":
                        player.Strength += bonus.Value;
                        player.Defence += bonus.Value;
                        player.Stamina += bonus.Value;
                        player.Agility += bonus.Value;
                        player.Charisma += bonus.Value;
                        player.Dexterity += bonus.Value;
                        player.Wisdom += bonus.Value;
                        break;
                }
            }

            player.RecalculateStats();
        }

        /// <summary>
        /// Trigger the appearance of the Void Key
        /// </summary>
        private async Task TriggerVoidKeyAppearance(Character player, TerminalEmulator terminal)
        {
            terminal.Clear();
            terminal.WriteLine("");
            terminal.WriteLine("Something stirs in the depths of reality...", "dark_magenta");
            await Task.Delay(2000);

            terminal.WriteLine("");
            terminal.WriteLine("The six artifacts you carry begin to resonate.", "bright_cyan");
            await Task.Delay(1500);

            terminal.WriteLine("Light and shadow dance around you.", "white");
            await Task.Delay(1500);

            terminal.WriteLine("");
            terminal.WriteLine("A tear opens in the fabric of existence...", "bright_magenta");
            await Task.Delay(2000);

            terminal.WriteLine("");
            terminal.WriteLine("╔═══════════════════════════════════════════════════════════════════╗", "white");
            terminal.WriteLine("║           T H E   V O I D   K E Y   A P P E A R S                 ║", "white");
            terminal.WriteLine("╚═══════════════════════════════════════════════════════════════════╝", "white");
            terminal.WriteLine("");

            await Task.Delay(1000);

            terminal.WriteLine("From the void between worlds, a key of pure nothing", "white");
            terminal.WriteLine("materializes before you. It is absence given form,", "white");
            terminal.WriteLine("the shape of possibility itself.", "white");
            terminal.WriteLine("");

            terminal.WriteLine("\"The way is open.\"", "bright_yellow");
            terminal.WriteLine("A voice that is no voice whispers in your mind.", "gray");
            terminal.WriteLine("");

            terminal.WriteLine("\"Seek the Creator's Prison at dungeon level 100.\"", "bright_yellow");
            terminal.WriteLine("\"There, your journey will end. One way or another.\"", "bright_yellow");
            terminal.WriteLine("");

            // Collect the Void Key
            StoryProgressionSystem.Instance.CollectArtifact(ArtifactType.VoidKey);
            ApplyArtifactBonuses(player, artifacts[ArtifactType.VoidKey]);

            StoryProgressionSystem.Instance.SetStoryFlag("void_key_obtained", true);
            StoryProgressionSystem.Instance.AdvanceChapter(StoryChapter.TheFinalConfrontation);

            await terminal.GetInputAsync("Press Enter to continue...");
        }

        /// <summary>
        /// Check if player has a specific artifact
        /// </summary>
        public bool HasArtifact(ArtifactType type)
        {
            return StoryProgressionSystem.Instance.CollectedArtifacts.Contains(type);
        }

        /// <summary>
        /// Get the number of collected artifacts
        /// </summary>
        public int GetCollectedCount()
        {
            return StoryProgressionSystem.Instance.CollectedArtifacts.Count;
        }

        /// <summary>
        /// Get artifact power level (for combat calculations)
        /// </summary>
        public int GetTotalArtifactPower()
        {
            int power = 0;
            foreach (var type in StoryProgressionSystem.Instance.CollectedArtifacts)
            {
                if (artifacts.TryGetValue(type, out var artifact))
                {
                    power += artifact.StatBonuses.Values.Sum();
                }
            }
            return power;
        }

        // ==================== COMBAT QUERY HELPERS ====================

        /// <summary>Quick checks for combat code — avoids verbose HasArtifact(ArtifactType.X) everywhere</summary>
        public bool HasCreatorsEye() => HasArtifact(ArtifactType.CreatorsEye);
        public bool HasSoulweaversLoom() => HasArtifact(ArtifactType.SoulweaversLoom);
        public bool HasScalesOfLaw() => HasArtifact(ArtifactType.ScalesOfLaw);
        public bool HasShadowCrown() => HasArtifact(ArtifactType.ShadowCrown);
        public bool HasSunforgedBlade() => HasArtifact(ArtifactType.SunforgedBlade);
        public bool HasWorldstone() => HasArtifact(ArtifactType.Worldstone);
        public bool HasVoidKey() => HasArtifact(ArtifactType.VoidKey);

        /// <summary>
        /// Get special ability text for display
        /// </summary>
        public List<string> GetActiveArtifactAbilities()
        {
            var abilities = new List<string>();
            foreach (var type in StoryProgressionSystem.Instance.CollectedArtifacts)
            {
                if (artifacts.TryGetValue(type, out var artifact))
                {
                    abilities.Add($"{artifact.Name}: {artifact.SpecialAbility}");
                }
            }
            return abilities;
        }
    }

    /// <summary>
    /// Data class for artifact information
    /// </summary>
    public class ArtifactData
    {
        public ArtifactType Type { get; set; }
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string[] LoreText { get; set; } = Array.Empty<string>();
        public string ObtainedFrom { get; set; } = "";
        public int DungeonFloor { get; set; }
        public int RequiredLevel { get; set; }
        public Dictionary<string, int> StatBonuses { get; set; } = new();
        public string SpecialAbility { get; set; } = "";
        public string IconColor { get; set; } = "white";
    }
}
