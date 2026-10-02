using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Static data definitions for dungeon settlements — safe outpost hubs
/// at theme boundaries that make the dungeon feel like distinct regions.
/// </summary>
public static class DungeonSettlementData
{
    public static readonly int[] SettlementFloors = { 10, 20, 35, 65 };

    public static bool IsSettlementFloor(int floor) => SettlementFloors.Contains(floor);

    public static DungeonSettlement? GetSettlement(int floor) =>
        Settlements.TryGetValue(floor, out var s) ? s : null;

    public static readonly Dictionary<int, DungeonSettlement> Settlements = new()
    {
        [10] = new DungeonSettlement
        {
            Id = "bonewright_forge",
            Floor = 10,
            Name = "The Bonewright's Forge",
            NPCName = "Durgan Bonewright",
            NPCTitle = "Dwarven Smith",
            ThemeColor = "bright_yellow",
            Description = "A squat stone workshop wedged into a catacomb alcove. The ring of hammer on\nanvil echoes through the tunnels. Bones of forgotten dead have been repurposed\nas tool racks and fuel for the forge. A stocky dwarf works the bellows.",
            FirstGreeting = "\"Hah! A surface-dweller, down in my workshop. Name's Durgan.\nI make arms from what the dead leave behind. Nothing goes to waste\ndown here. You need something fixed, sharpened, or patched up -- I'm your dwarf.\"",
            ReturnGreeting = "\"Back again? Good. The forge is hot and I've got stock.\nWhat'll it be?\"",
            HasHealing = true,
            HasTrading = true,
            HasLore = true,
            HealCostMultiplier = 0.5f,
            HealEffectiveness = 0.30f,
            TradeItems = new[] { "Healing Potion", "Mana Potion", "Torch", "Antidote" },
            LoreFragments = new[]
            {
                "\"These catacombs? Old. Older than the town above. Whoever built 'em\nwasn't burying their dead -- they were sealing something in.\"",
                "\"The sewers below here used to be waterways for an underground city.\nThe dwarves remember, even if the humans forgot.\"",
                "\"I found dwarvish runes on the deeper walls. Warnings, mostly.\n'Do not dig below the third foundation.' Nobody listened, of course.\"",
                "\"There's a seal down here somewhere. Ancient magic. The kind you\ndon't break unless you want to wake something up.\"",
            },
        },

        [20] = new DungeonSettlement
        {
            Id = "rat_king_market",
            Floor = 20,
            Name = "The Rat King's Market",
            NPCName = "Nix the Fence",
            NPCTitle = "Underground Broker",
            ThemeColor = "green",
            Description = "A ramshackle bazaar in a flooded sewer junction. Lanterns hang from\npipes and chains. Stalls of scavenged goods line the walkways. A wiry\nfigure in a patched coat presides over the chaos with a sharp grin.",
            FirstGreeting = "\"Well, well. Fresh meat from upstairs. Welcome to the Market,\nfriend. Everything down here has a price, and I set them all.\nDon't touch anything you can't pay for.\"",
            ReturnGreeting = "\"My favorite customer returns. I've got new stock --\nfell off a caravan. Literally. Into the sewers.\"",
            HasHealing = false,
            HasTrading = true,
            HasLore = true,
            HealCostMultiplier = 0f,
            HealEffectiveness = 0f,
            TradeItems = new[] { "Healing Potion", "Antidote", "Lockpick", "Smoke Bomb" },
            LoreFragments = new[]
            {
                "\"The Rat King? That's me, obviously. Self-appointed. Nobody\nchallenged the title, so here we are.\"",
                "\"Below the sewers, the caverns open up into something massive.\nNatural caves. Some say there's a whole underground sea down there.\"",
                "\"I've had customers from the deep -- creatures that trade in\ngems and bones. They're not hostile if you've got coin.\"",
                "\"Word of advice: past the caverns, you hit the old ruins.\nThat's where things get... historical. And dangerous.\"",
            },
        },

        [35] = new DungeonSettlement
        {
            Id = "hermit_hollow",
            Floor = 35,
            Name = "The Hermit's Hollow",
            NPCName = "Old Maren",
            NPCTitle = "Herbalist & Seer",
            ThemeColor = "cyan",
            Description = "A natural grotto illuminated by bioluminescent fungi. Herb bundles\nhang from the ceiling. A small garden grows in soil carried down from\nthe surface long ago. An ancient woman tends the plants with gnarled hands.",
            FirstGreeting = "\"Oh... a visitor. It's been so long since anyone came this deep\nwho wasn't running from something. Sit. Rest. The fungi will\nlight your way, and I have herbs for what ails you.\"",
            ReturnGreeting = "\"Welcome back, dear. The mushrooms told me you were coming.\nThey're never wrong. What do you need?\"",
            HasHealing = true,
            HasTrading = true,
            HasLore = true,
            HealCostMultiplier = 0.4f,
            HealEffectiveness = 0.40f,
            TradeItems = new[] { "Healing Potion", "Mana Potion", "Healing Herb", "Starbloom Essence" },
            LoreFragments = new[]
            {
                "\"I came down here forty years ago, following a vision. The Old Gods\nspeak through the stone if you know how to listen.\"",
                "\"The ancient ruins below were built by a civilization that worshipped\nthe Old Gods directly. Their temples still stand. Their traps still work.\"",
                "\"The demons in the deep levels aren't natural. They were summoned.\nSomething down there holds open a gate that should have closed\naeons ago.\"",
                "\"I've seen the Seals, child. Seven of them, hidden throughout\nthese depths. Each one holds back a fragment of something terrible.\nOr perhaps something wonderful. Hard to tell the difference.\"",
            },
        },

        [65] = new DungeonSettlement
        {
            Id = "last_hearth",
            Floor = 65,
            Name = "The Last Hearth",
            NPCName = "Captain Voss",
            NPCTitle = "Expedition Commander",
            ThemeColor = "bright_red",
            Description = "A fortified camp built from demon bones and salvaged timber. Torches\nburn in iron brackets. A handful of hardened soldiers maintain the\nperimeter. Their commander stands over a crude map table.",
            FirstGreeting = "\"Stand down -- they're from the surface. I'm Captain Voss,\ncommander of the Deep Expedition. We've held this position for\nthree months. Beyond here, the frozen depths begin. If you're\nheading deeper, you'll want to stock up. This is the last\nfriendly face you'll see for a long time.\"",
            ReturnGreeting = "\"Good to see you alive. We lost two more scouts last week.\nThe frozen depths are no joke. Resupply while you can.\"",
            HasHealing = true,
            HasTrading = true,
            HasLore = true,
            HealCostMultiplier = 0.6f,
            HealEffectiveness = 0.35f,
            TradeItems = new[] { "Healing Potion", "Mana Potion", "Antidote", "Firebloom Petal" },
            LoreFragments = new[]
            {
                "\"We were sent down here by the crown to map the deep levels.\nThirty soldiers started. Twelve remain. The demons took the rest.\"",
                "\"The frozen depths below -- they shouldn't exist this far underground.\nSomething is generating that cold. Something enormous.\"",
                "\"My scouts report volcanic vents even deeper. Ice above, fire below.\nIt's as if the dungeon itself is alive and can't decide what it wants to be.\"",
                "\"There are Old Gods sleeping in these depths. I've felt them.\nThe ground hums when you stand still long enough. Whatever you're\nseeking down here -- make sure it's worth what you'll pay.\"",
            },
        },
    };
}

public class DungeonSettlement
{
    public string Id { get; set; } = "";
    public int Floor { get; set; }
    public string Name { get; set; } = "";
    public string NPCName { get; set; } = "";
    public string NPCTitle { get; set; } = "";
    public string ThemeColor { get; set; } = "white";
    public string Description { get; set; } = "";
    public string FirstGreeting { get; set; } = "";
    public string ReturnGreeting { get; set; } = "";

    public bool HasHealing { get; set; }
    public bool HasTrading { get; set; }
    public bool HasLore { get; set; }

    public float HealCostMultiplier { get; set; } = 0.5f;
    public float HealEffectiveness { get; set; } = 0.30f;

    public string[] TradeItems { get; set; } = System.Array.Empty<string>();
    public string[] LoreFragments { get; set; } = System.Array.Empty<string>();

    // v1.2.3: localized accessors. The English fields above are the source and fallback; these
    // resolve dungeon.settlement.{id}.* keys so the text renders in the session language.
    private string LocOr(string field, string fallback)
    {
        string key = $"dungeon.settlement.{Id}.{field}";
        var v = UsurperRemake.Systems.Loc.Get(key);
        return v == key ? fallback : v;
    }
    public string LocNPCTitle() => LocOr("npc_title", NPCTitle);
    public string LocDescription() => LocOr("description", Description);
    public string LocFirstGreeting() => LocOr("greeting_first", FirstGreeting);
    public string LocReturnGreeting() => LocOr("greeting_return", ReturnGreeting);
    public string LocLore(int index) => LocOr($"lore.{index}", LoreFragments[index]);
}
