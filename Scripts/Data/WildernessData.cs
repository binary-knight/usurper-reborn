using System;
using System.Collections.Generic;

/// <summary>
/// Static data for the Wilderness exploration system — 4 themed regions
/// with encounter pools, discoveries, and monster families.
/// </summary>
public static class WildernessData
{
    public static readonly WildernessRegion[] Regions = new[]
    {
        new WildernessRegion
        {
            Id = "forest",
            Name = "Whispering Forest",
            Direction = "North",
            DirectionKey = "N",
            MinLevel = 1,
            ThemeColor = "green",
            Description = "Ancient trees tower overhead, their canopy filtering the sunlight\ninto shifting patterns on the forest floor. Bird calls echo through\nthe undergrowth.",
            MonsterNames = new[] {
                "Timber Wolf", "Forest Spider", "Wild Boar", "Bandit Scout",
                "Black Bear", "Giant Wasp", "Feral Cat", "Forest Troll"
            },
            ForagingResults = new[] {
                ("Healing Herb", "You find a patch of healing herbs growing by a stream.", "herb_healing"),
                ("Wild Berries", "You gather edible berries from a bush.", "heal_small"),
                ("Mushrooms", "You find some edible mushrooms.", "heal_small"),
                ("Nothing", "You search but find nothing useful.", "nothing"),
                ("Ironbark Root", "You discover rare ironbark root at the base of an old tree.", "herb_ironbark"),
            },
            RuinsEncounters = new[] {
                "You find an overgrown stone well. Peering inside, you spot something glinting at the bottom.",
                "A crumbling watchtower rises from the trees. Inside, someone left supplies long ago.",
                "You discover a hunter's cache hidden beneath a fallen log.",
            },
            TravelerEncounters = new[] {
                ("A woodcutter rests by a stump, sharpening his axe.", "Woodcutter"),
                ("A herbalist wanders the paths, collecting plants.", "Herbalist"),
                ("A lost merchant begs for directions back to town.", "Merchant"),
            },
            Discoveries = new[]
            {
                new WildernessDiscovery { Id = "forest_cave", Name = "Hidden Cave", Description = "A concealed cave entrance behind a waterfall. Inside, old mining equipment and mineral deposits.", EncounterType = "combat", MinLevel = 3 },
                new WildernessDiscovery { Id = "forest_grove", Name = "Sacred Grove", Description = "A ring of ancient oaks surrounding a moss-covered shrine. The air hums with old magic.", EncounterType = "shrine", MinLevel = 1 },
            },
        },

        new WildernessRegion
        {
            Id = "mountains",
            Name = "Iron Mountains",
            Direction = "East",
            DirectionKey = "E",
            MinLevel = 10,
            ThemeColor = "gray",
            Description = "Jagged peaks claw at the sky. The mountain paths are narrow and\ntreacherous, with sheer drops on either side. The wind howls\nthrough the passes.",
            MonsterNames = new[] {
                "Mountain Lion", "Stone Golem", "Wyvern Hatchling", "Hill Giant",
                "Rock Basilisk", "Mountain Bandit", "Cliff Raptor", "Ice Troll"
            },
            ForagingResults = new[] {
                ("Iron Ore", "You chip some iron ore from an exposed vein.", "gold_small"),
                ("Mountain Crystal", "A glittering crystal juts from the rock face.", "gold_medium"),
                ("Nothing", "The barren rock yields nothing useful.", "nothing"),
                ("Firebloom", "You spot a rare firebloom growing in a volcanic crack.", "herb_firebloom"),
                ("Eagle Feather", "You find a large eagle feather caught on a ledge.", "gold_small"),
            },
            RuinsEncounters = new[] {
                "A dwarven outpost, abandoned long ago. Rusted tools and empty barrels remain.",
                "You find a collapsed mineshaft. Something glints in the rubble.",
                "An ancient stone waymarker bears an inscription in a forgotten tongue.",
            },
            TravelerEncounters = new[] {
                ("A grizzled prospector pans for gold in a mountain stream.", "Prospector"),
                ("A dwarven trader leads a pack mule along the narrow path.", "Dwarf Trader"),
                ("A hermit monk meditates on a high ledge.", "Mountain Monk"),
            },
            Discoveries = new[]
            {
                new WildernessDiscovery { Id = "mountain_mine", Name = "Abandoned Mine", Description = "A deep mine shaft with veins of precious ore still visible in the walls.", EncounterType = "combat", MinLevel = 12 },
                new WildernessDiscovery { Id = "mountain_eyrie", Name = "Eagle's Eyrie", Description = "A windswept peak where giant eagles nest. From here, you can see the entire realm.", EncounterType = "shrine", MinLevel = 15 },
            },
        },

        new WildernessRegion
        {
            Id = "swamp",
            Name = "Blackmire Swamp",
            Direction = "South",
            DirectionKey = "S",
            MinLevel = 20,
            ThemeColor = "dark_green",
            Description = "Murky water stretches between twisted trees draped in moss.\nThe air is thick with insects and the smell of decay.\nSomething large ripples the surface nearby.",
            MonsterNames = new[] {
                "Bog Lurker", "Swamp Hag", "Poison Toad", "Mire Zombie",
                "Giant Leech", "Marsh Wraith", "Crocodile", "Fungal Horror"
            },
            ForagingResults = new[] {
                ("Swamp Moss", "You collect valuable medicinal swamp moss.", "heal_medium"),
                ("Rare Mushroom", "A glowing mushroom grows on a rotting log.", "gold_medium"),
                ("Nothing", "You search the muck but find only leeches.", "nothing"),
                ("Starbloom", "Incredibly, a starbloom grows here, nourished by the decay.", "herb_starbloom"),
                ("Swiftthistle", "You spot swiftthistle growing in a drier patch.", "herb_swift"),
            },
            RuinsEncounters = new[] {
                "A sunken temple protrudes from the mire, its entrance half-submerged.",
                "You find a witch's abandoned hut on stilts. Potions still line the shelves.",
                "An ancient stone bridge crosses a deep channel. Something guards the far side.",
            },
            TravelerEncounters = new[] {
                ("A witch stirs a bubbling cauldron beside the path.", "Swamp Witch"),
                ("A treasure hunter wades through the muck, looking miserable.", "Treasure Hunter"),
                ("A will-o-wisp dances ahead, beckoning you deeper.", "Will-o-Wisp"),
            },
            Discoveries = new[]
            {
                new WildernessDiscovery { Id = "swamp_temple", Name = "Sunken Temple", Description = "A half-submerged temple from a forgotten civilization. Strange murals depict the Old Gods.", EncounterType = "ruins", MinLevel = 22 },
                new WildernessDiscovery { Id = "swamp_witch", Name = "Witch's Hollow", Description = "A secluded clearing where a reclusive witch trades potions and secrets.", EncounterType = "traveler", MinLevel = 20 },
            },
        },

        new WildernessRegion
        {
            Id = "coast",
            Name = "Stormbreak Coast",
            Direction = "West",
            DirectionKey = "W",
            MinLevel = 30,
            ThemeColor = "bright_cyan",
            Description = "Waves crash against jagged cliffs. Seabirds wheel overhead.\nThe remains of old shipwrecks dot the shoreline, half-buried\nin sand and seaweed.",
            MonsterNames = new[] {
                "Sea Serpent", "Shore Pirate", "Giant Crab", "Siren",
                "Drowned Sailor", "Reef Shark", "Kraken Spawn", "Storm Elemental"
            },
            ForagingResults = new[] {
                ("Driftwood", "You salvage useful driftwood from the shore.", "gold_small"),
                ("Pearl", "You find a pearl in an oyster washed ashore!", "gold_large"),
                ("Nothing", "The tide has washed the shore clean.", "nothing"),
                ("Sea Salt", "You collect valuable sea salt from tidal pools.", "gold_small"),
                ("Shipwreck Salvage", "You pry open a barnacle-covered chest from a wreck.", "gold_large"),
            },
            RuinsEncounters = new[] {
                "A lighthouse stands dark and abandoned on the point. Its door hangs open.",
                "You discover a smuggler's cave at the base of the cliffs.",
                "A beached longship from an unknown civilization lies half-buried in sand.",
            },
            TravelerEncounters = new[] {
                ("An old sailor mends nets on the beach, muttering about sea monsters.", "Old Sailor"),
                ("A merchant captain surveys the wreckage of his ship.", "Captain"),
                ("A fisherwoman offers you a portion of her catch.", "Fisherwoman"),
            },
            Discoveries = new[]
            {
                new WildernessDiscovery { Id = "coast_cave", Name = "Smuggler's Cove", Description = "A hidden sea cave accessible only at low tide. Crates of contraband line the walls.", EncounterType = "combat", MinLevel = 32 },
                new WildernessDiscovery { Id = "coast_lighthouse", Name = "Stormwatch Lighthouse", Description = "The abandoned lighthouse. From the top, you can see ships on the horizon and something moving beneath the waves.", EncounterType = "shrine", MinLevel = 30 },
            },
        },
    };

    public static WildernessRegion? GetRegion(string id) =>
        Array.Find(Regions, r => r.Id == id);

    public static WildernessRegion? GetRegionByKey(string key) =>
        Array.Find(Regions, r => r.DirectionKey.Equals(key, StringComparison.OrdinalIgnoreCase));

    // Localized accessors. The region/discovery data is authored in English; these
    // resolve `wilderness.region.{id}.*` / `wilderness.direction.{key}` /
    // `wilderness.discovery.{id}.*` loc keys with English fallback so the wilderness
    // menu, region screens, and discoveries display in the player's session
    // language. Player report: region names and directions showed in English.
    private static string LocOrFallback(string key, string fallback)
    {
        var v = UsurperRemake.Systems.Loc.Get(key);
        return v == key ? fallback : v;
    }
    public static string GetRegionName(WildernessRegion r) => LocOrFallback($"wilderness.region.{r.Id}.name", r.Name);
    public static string GetRegionDirection(WildernessRegion r) => LocOrFallback($"wilderness.direction.{r.DirectionKey}", r.Direction);
    public static string GetRegionDescription(WildernessRegion r) => LocOrFallback($"wilderness.region.{r.Id}.desc", r.Description);
    public static string GetDiscoveryName(WildernessDiscovery d) => LocOrFallback($"wilderness.discovery.{d.Id}.name", d.Name);
    public static string GetDiscoveryDescription(WildernessDiscovery d) => LocOrFallback($"wilderness.discovery.{d.Id}.desc", d.Description);

    /// <summary>
    /// v0.61.2 (player report: "Feral Cat takes flight, becoming harder to hit!"):
    /// Wilderness combat called MonsterGenerator.GenerateMonster which picks a
    /// random dungeon family/tier (Celestial Angel, Construct Golem, etc.) and
    /// then overwrote only the Name field -- so the underlying abilities, family,
    /// MonsterClass, color, and combat dialogue all leaked through from whatever
    /// random monster was generated. A Feral Cat could use Angel's Flight ability.
    ///
    /// This lookup table provides a deterministic family/class/abilities profile
    /// for every named wilderness monster so the generated creature actually
    /// matches its name. Names not in the table fall back to a plain Beast with
    /// no special abilities. Ability strings must match values in the
    /// MonsterAbilities.AbilityType enum since combat parses them with Enum.TryParse.
    /// </summary>
    public static readonly Dictionary<string, WildernessMonsterProfile> MonsterProfiles = new(StringComparer.OrdinalIgnoreCase)
    {
        // Forest
        { "Timber Wolf",      new() { Family = "Beast",      MonsterClass = MonsterClass.Beast,    AttackType = "physical", Color = "gray",         CanSpeak = false, Abilities = new() { "BleedingWound" } } },
        { "Forest Spider",    new() { Family = "Insectoid",  MonsterClass = MonsterClass.Beast,    AttackType = "poison",   Color = "dark_green",   CanSpeak = false, Abilities = new() { "VenomousBite" } } },
        { "Wild Boar",        new() { Family = "Beast",      MonsterClass = MonsterClass.Beast,    AttackType = "physical", Color = "yellow",       CanSpeak = false, Abilities = new() { "CrushingBlow" } } },
        { "Bandit Scout",     new() { Family = "Humanoid",   MonsterClass = MonsterClass.Humanoid, AttackType = "physical", Color = "yellow",       CanSpeak = true,  Abilities = new() { "Backstab" } } },
        { "Black Bear",       new() { Family = "Beast",      MonsterClass = MonsterClass.Beast,    AttackType = "physical", Color = "dark_gray",    CanSpeak = false, Abilities = new() { "CrushingBlow" } } },
        { "Giant Wasp",       new() { Family = "Insectoid",  MonsterClass = MonsterClass.Beast,    AttackType = "poison",   Color = "yellow",       CanSpeak = false, Abilities = new() { "Poison" } } },
        { "Feral Cat",        new() { Family = "Beast",      MonsterClass = MonsterClass.Beast,    AttackType = "physical", Color = "gray",         CanSpeak = false, Abilities = new() { } } },
        { "Forest Troll",     new() { Family = "Giant",      MonsterClass = MonsterClass.Beast,    AttackType = "physical", Color = "green",        CanSpeak = true,  Abilities = new() { "Regeneration" } } },

        // Mountains
        { "Mountain Lion",    new() { Family = "Beast",      MonsterClass = MonsterClass.Beast,    AttackType = "physical", Color = "yellow",       CanSpeak = false, Abilities = new() { "BleedingWound" } } },
        { "Stone Golem",      new() { Family = "Construct",  MonsterClass = MonsterClass.Construct,AttackType = "physical", Color = "gray",         CanSpeak = false, Abilities = new() { "Stoneskin", "CrushingBlow" } } },
        { "Wyvern Hatchling", new() { Family = "Draconic",   MonsterClass = MonsterClass.Dragon,   AttackType = "fire",     Color = "red",          CanSpeak = false, Abilities = new() { "FireBreath" } } },
        { "Hill Giant",       new() { Family = "Giant",      MonsterClass = MonsterClass.Humanoid, AttackType = "physical", Color = "bright_yellow",CanSpeak = true,  Abilities = new() { "CrushingBlow", "Boulder" } } },
        { "Rock Basilisk",    new() { Family = "Beast",      MonsterClass = MonsterClass.Beast,    AttackType = "physical", Color = "gray",         CanSpeak = false, Abilities = new() { "PetrifyingGaze" } } },
        { "Mountain Bandit",  new() { Family = "Humanoid",   MonsterClass = MonsterClass.Humanoid, AttackType = "physical", Color = "yellow",       CanSpeak = true,  Abilities = new() { "Backstab" } } },
        { "Cliff Raptor",     new() { Family = "Beast",      MonsterClass = MonsterClass.Beast,    AttackType = "physical", Color = "bright_yellow",CanSpeak = false, Abilities = new() { "Multiattack" } } },
        { "Ice Troll",        new() { Family = "Giant",      MonsterClass = MonsterClass.Beast,    AttackType = "cold",     Color = "cyan",         CanSpeak = true,  Abilities = new() { "Regeneration", "FrostBreath" } } },

        // Swamp
        { "Bog Lurker",       new() { Family = "Aquatic",    MonsterClass = MonsterClass.Beast,    AttackType = "physical", Color = "dark_green",   CanSpeak = false, Abilities = new() { "TentacleGrab" } } },
        { "Swamp Hag",        new() { Family = "Fey",        MonsterClass = MonsterClass.Plant,    AttackType = "magic",    Color = "magenta",      CanSpeak = true,  Abilities = new() { "Curse", "Spellcasting" } } },
        { "Poison Toad",      new() { Family = "Beast",      MonsterClass = MonsterClass.Beast,    AttackType = "poison",   Color = "green",        CanSpeak = false, Abilities = new() { "Poison" } } },
        { "Mire Zombie",      new() { Family = "Undead",     MonsterClass = MonsterClass.Undead,   AttackType = "necrotic", Color = "dark_green",   CanSpeak = false, Abilities = new() { "BleedingWound" } } },
        { "Giant Leech",      new() { Family = "Insectoid",  MonsterClass = MonsterClass.Beast,    AttackType = "physical", Color = "red",          CanSpeak = false, Abilities = new() { "LifeDrain" } } },
        { "Marsh Wraith",     new() { Family = "Undead",     MonsterClass = MonsterClass.Undead,   AttackType = "necrotic", Color = "blue",         CanSpeak = true,  Abilities = new() { "LifeDrain", "Incorporeal" } } },
        { "Crocodile",        new() { Family = "Aquatic",    MonsterClass = MonsterClass.Beast,    AttackType = "physical", Color = "dark_green",   CanSpeak = false, Abilities = new() { "CrushingBlow", "BleedingWound" } } },
        { "Fungal Horror",    new() { Family = "Aberration", MonsterClass = MonsterClass.Plant,    AttackType = "poison",   Color = "magenta",      CanSpeak = false, Abilities = new() { "PoisonCloud" } } },

        // Coast
        { "Sea Serpent",      new() { Family = "Aquatic",    MonsterClass = MonsterClass.Beast,    AttackType = "physical", Color = "blue",         CanSpeak = false, Abilities = new() { "CrushingBlow", "VenomousBite" } } },
        { "Shore Pirate",     new() { Family = "Humanoid",   MonsterClass = MonsterClass.Humanoid, AttackType = "physical", Color = "yellow",       CanSpeak = true,  Abilities = new() { "Backstab" } } },
        { "Giant Crab",       new() { Family = "Aquatic",    MonsterClass = MonsterClass.Beast,    AttackType = "physical", Color = "red",          CanSpeak = false, Abilities = new() { "ArmorHarden" } } },
        { "Siren",            new() { Family = "Fey",        MonsterClass = MonsterClass.Plant,    AttackType = "magic",    Color = "cyan",         CanSpeak = true,  Abilities = new() { "Charm" } } },
        { "Drowned Sailor",   new() { Family = "Undead",     MonsterClass = MonsterClass.Undead,   AttackType = "necrotic", Color = "blue",         CanSpeak = true,  Abilities = new() { "LifeDrain" } } },
        { "Reef Shark",       new() { Family = "Aquatic",    MonsterClass = MonsterClass.Beast,    AttackType = "physical", Color = "gray",         CanSpeak = false, Abilities = new() { "BleedingWound", "Multiattack" } } },
        { "Kraken Spawn",     new() { Family = "Aquatic",    MonsterClass = MonsterClass.Beast,    AttackType = "physical", Color = "bright_blue",  CanSpeak = false, Abilities = new() { "TentacleGrab", "InkCloud" } } },
        { "Storm Elemental",  new() { Family = "Elemental",  MonsterClass = MonsterClass.Elemental,AttackType = "lightning",Color = "bright_cyan",  CanSpeak = false, Abilities = new() { "Lightning" } } },
    };

    /// <summary>
    /// Returns the wilderness profile for a monster name, or a plain Beast
    /// default if the name isn't in the table. Used by WildernessLocation
    /// after MonsterGenerator to rewrite family-specific fields so the
    /// creature behaves like its name implies.
    /// </summary>
    public static WildernessMonsterProfile GetMonsterProfile(string name)
    {
        if (MonsterProfiles.TryGetValue(name ?? "", out var profile))
            return profile;
        // Default: plain physical Beast with no special abilities.
        return new WildernessMonsterProfile
        {
            Family = "Beast",
            MonsterClass = MonsterClass.Beast,
            AttackType = "physical",
            Color = "gray",
            CanSpeak = false,
            Abilities = new List<string>()
        };
    }
}

/// <summary>
/// v0.61.2: per-name profile used to rewrite the random dungeon-monster
/// fields from MonsterGenerator into a wilderness-appropriate creature.
/// </summary>
public class WildernessMonsterProfile
{
    public string Family { get; set; } = "Beast";
    public MonsterClass MonsterClass { get; set; } = MonsterClass.Beast;
    public string AttackType { get; set; } = "physical";
    public string Color { get; set; } = "gray";
    public bool CanSpeak { get; set; } = false;
    public List<string> Abilities { get; set; } = new();
}

public class WildernessRegion
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Direction { get; set; } = "";
    public string DirectionKey { get; set; } = "";
    public int MinLevel { get; set; }
    public string ThemeColor { get; set; } = "white";
    public string Description { get; set; } = "";
    public string[] MonsterNames { get; set; } = Array.Empty<string>();
    public (string name, string text, string effect)[] ForagingResults { get; set; } = Array.Empty<(string, string, string)>();
    public string[] RuinsEncounters { get; set; } = Array.Empty<string>();
    public (string text, string name)[] TravelerEncounters { get; set; } = Array.Empty<(string, string)>();
    public WildernessDiscovery[] Discoveries { get; set; } = Array.Empty<WildernessDiscovery>();
}

public class WildernessDiscovery
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string EncounterType { get; set; } = "combat";
    public int MinLevel { get; set; }
}
