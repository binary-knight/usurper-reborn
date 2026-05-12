using System;
using System.Collections.Generic;
using System.Linq;

namespace UsurperRemake.Data;

/// <summary>
/// Druid's Shrines (v0.61.0). Five ancient shrines scattered through the
/// wilderness, one per Old God whose domain bleeds out into the wild lands.
/// Players make pilgrimage to one shrine per day and gain a 24-hour
/// "attunement" passive tied to that god's domain. The choice carries Old
/// God favor consequences (each visit shifts the player toward that god)
/// and -- at favor milestones (10, 25, 50 visits) -- unlocks unique
/// encounters at that shrine.
///
/// Manwe and Thorgrim deliberately have no shrine: Manwe is the endgame
/// Creator and Thorgrim's thematic location is the Prison, not the wild.
/// </summary>
public static class DruidShrineData
{
    /// <summary>How many in-game hours a shrine attunement lasts.</summary>
    public const int AttunementHours = 24;

    /// <summary>Favor milestone thresholds. Reaching one fires a unique encounter the
    /// next time the player visits that shrine.</summary>
    public static readonly int[] FavorMilestones = new[] { 10, 25, 50 };

    public class DruidShrine
    {
        public string Id { get; init; } = "";                  // Stable key, also the favor-dictionary key.
        public string Name { get; init; } = "";                // Display name ("Lantern Shrine of Aurelion").
        public string GodPatron { get; init; } = "";           // Display name of the Old God ("Aurelion, The Fading Light").
        public string RegionDirectionKey { get; init; } = "";  // Which WildernessRegion houses this shrine (N/E/S/W).
        public string FlavorDescription { get; init; } = "";   // Read on first sighting.
        public string AttunementDescription { get; init; } = ""; // Read when the player picks this shrine.
        public string PassiveSummary { get; init; } = "";      // One-line "what this buff does" summary.
        public int ChivalryShift { get; init; }                // Alignment shift per attunement (positive = +Chivalry, negative = +Darkness).
    }

    public static readonly DruidShrine[] Shrines = new[]
    {
        new DruidShrine
        {
            Id = "terravok",
            Name = "Stone Circle of Terravok",
            GodPatron = "Terravok, The Sleeping Mountain",
            RegionDirectionKey = "E",  // Iron Mountains
            FlavorDescription =
                "A ring of standing stones older than any city. Each stone is shaped to a slightly different\n" +
                "weight, and yet they balance perfectly. The wind here does not blow -- it waits.",
            AttunementDescription =
                "You kneel at the center stone. The mountain breathes once, slowly, beneath you.\n" +
                "Terravok's strength settles into your bones.",
            PassiveSummary = "+10% Max HP, +5 HP regen per combat round (24h)",
            ChivalryShift = 0
        },
        new DruidShrine
        {
            Id = "maelketh",
            Name = "Broken Blade Altar of Maelketh",
            GodPatron = "Maelketh, The Broken Blade",
            RegionDirectionKey = "E",  // Iron Mountains
            FlavorDescription =
                "An altar of rust-eaten iron, half-buried in mountain stone. Hundreds of broken weapons\n" +
                "are driven into the rock around it, hilt-first. None of the blades match. None are sheathed.",
            AttunementDescription =
                "You drive your own blade hilt-first into the altar's stone, then pull it free.\n" +
                "The edge sings with the memory of every weapon that came before.",
            PassiveSummary = "+8% melee damage, +5% rage gain (24h)",
            ChivalryShift = -2
        },
        new DruidShrine
        {
            Id = "noctura",
            Name = "Moonwell of Noctura",
            GodPatron = "Noctura, The Shadow Weaver",
            RegionDirectionKey = "S",  // Blackmire Swamp
            FlavorDescription =
                "A pool of water blacker than night, set in a clearing where the swamp parts unnaturally.\n" +
                "Even at noon, the pool's surface reflects only the moon -- and the moon is always full here.",
            AttunementDescription =
                "You cup the black water and let it run between your fingers. It feels like silk and silence.\n" +
                "Your shadow stretches a little longer than it should.",
            PassiveSummary = "+5% crit chance, +10% backstab damage (24h)",
            ChivalryShift = -3
        },
        new DruidShrine
        {
            Id = "aurelion",
            Name = "Lantern Shrine of Aurelion",
            GodPatron = "Aurelion, The Fading Light",
            RegionDirectionKey = "N",  // Whispering Forest
            FlavorDescription =
                "A weathered lantern hung from the lowest branch of an old oak, its flame guttering but never going out.\n" +
                "Beneath it, a knight's helm rests on a flat stone. No one has touched the helm in living memory.",
            AttunementDescription =
                "You lift the helm and look through its eye-slits at the lantern's failing light.\n" +
                "Warmth fills your chest. A vow that is not yours settles into the air around you.",
            PassiveSummary = "+5% holy damage, +25% holy enchant proc damage (24h)",
            ChivalryShift = 5
        },
        new DruidShrine
        {
            Id = "veloura",
            Name = "Heart of the Tide",
            GodPatron = "Veloura, The Withered Heart",
            RegionDirectionKey = "W",  // Stormbreak Coast
            FlavorDescription =
                "Surf has carved a heart-shape into the cliff face here, and the tide leaves coral and shell offerings\n" +
                "in its hollow. They are arranged. Not by hands -- by Veloura's patient, withered hand.",
            AttunementDescription =
                "You leave something small at the hollow's center -- a coin, a strand of hair, a private thought.\n" +
                "Veloura's old, careful warmth slides between your ribs.",
            PassiveSummary = "+5 Charisma, +15% NPC reaction bonus (24h)",
            ChivalryShift = 0
        }
    };

    public static DruidShrine? GetById(string id) =>
        Shrines.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<DruidShrine> GetByRegionKey(string directionKey) =>
        Shrines.Where(s => string.Equals(s.RegionDirectionKey, directionKey, StringComparison.OrdinalIgnoreCase));
}
