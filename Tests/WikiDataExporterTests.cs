using System.Text.Json;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

public sealed class WikiDataFixture : IDisposable
{
    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "usurper-wiki-test-" + Guid.NewGuid().ToString("N"));

    public WikiDataFixture() => WikiDataExporter.Export(DirectoryPath);

    public JsonElement Data(string file)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(DirectoryPath, file + ".json")));
        Assert.Equal(WikiDataExporter.SchemaVersion, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(GameConfig.Version, document.RootElement.GetProperty("gameVersion").GetString());
        return document.RootElement.GetProperty("data").Clone();
    }

    public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
}

public class WikiDataExporterTests : IClassFixture<WikiDataFixture>
{
    private readonly WikiDataFixture _export;
    public WikiDataExporterTests(WikiDataFixture export) => _export = export;

    [Fact]
    public void ItemsContainBuiltInStatsButNoGeneratedOrModdedEntries()
    {
        var items = _export.Data("items").EnumerateArray().ToArray();
        Assert.True(items.Length > 500);
        Assert.All(items, item => Assert.InRange(item.GetProperty("id").GetInt32(), 1000, 49999));
        var dagger = Assert.Single(items.Where(item => item.GetProperty("id").GetInt32() == 1000));
        Assert.Equal("Rusty Dagger", dagger.GetProperty("name").GetProperty("en").GetString());
        Assert.Equal(2, dagger.GetProperty("stats").GetProperty("weaponPower").GetInt32());
        // v1.2.5: every language its own name (ItemNames keys), the English one is the stored name
        Assert.Equal(5, dagger.GetProperty("name").EnumerateObject().Count());
        Assert.Equal("Rozsdás Tőr", dagger.GetProperty("name").GetProperty("hu").GetString());
    }

    [Fact]
    public void MonstersIncludeTierStatsFromCombatGenerator()
    {
        var families = _export.Data("monsters").EnumerateArray().ToArray();
        Assert.Equal(15, families.Length);
        var goblinoid = Assert.Single(families.Where(f => f.GetProperty("id").GetString() == "goblinoid"));
        var goblin = goblinoid.GetProperty("tiers")[0];
        Assert.Equal("Goblin", goblin.GetProperty("name").GetProperty("en").GetString());
        Assert.Equal(1, goblin.GetProperty("normalStatsAtMinLevel").GetProperty("level").GetInt32());
        Assert.True(goblin.GetProperty("normalStatsAtMinLevel").GetProperty("hp").GetInt64() > 0);
    }

    [Fact]
    public void ClassesIncludeStartingStatsAndSpecializations()
    {
        var classes = _export.Data("classes").EnumerateArray().ToArray();
        Assert.Equal(17, classes.Length);
        var warrior = Assert.Single(classes.Where(c => c.GetProperty("id").GetString() == "Warrior"));
        Assert.Equal(4, warrior.GetProperty("startingAttributes").GetProperty("strength").GetInt32());
        Assert.Equal(3, warrior.GetProperty("growthPerLevel").GetProperty("strength").GetInt64());
        Assert.Equal(17, warrior.GetProperty("growthPerLevel").GetProperty("maxHP").GetInt64());
        Assert.NotEmpty(warrior.GetProperty("specializations").EnumerateArray());
        var shaman = Assert.Single(classes.Where(c => c.GetProperty("id").GetString() == "MysticShaman"));
        Assert.Equal(new[] { "Gnoll", "Orc", "Troll" }, shaman.GetProperty("allowedRaces").EnumerateArray()
            .Select(r => r.GetString()).OrderBy(r => r).ToArray());
    }

    private JsonElement Class(string id) =>
        Assert.Single(_export.Data("classes").EnumerateArray().Where(c => c.GetProperty("id").GetString() == id));

    private static string[] Strings(JsonElement array) => array.EnumerateArray().Select(e => e.GetString()!).ToArray();

    // 1.3.1 wiki depth: the classes table shows HP, Defence and mana per level and the roles the game lists
    [Fact]
    public void ClassesIncludeGrowthManaRolesAndTrainingPoints()
    {
        Assert.Equal(17, Class("Warrior").GetProperty("growthPerLevel").GetProperty("maxHP").GetInt64());
        Assert.Equal(3, Class("Warrior").GetProperty("growthPerLevel").GetProperty("defence").GetInt64());
        Assert.Equal(0, Class("Warrior").GetProperty("growthPerLevel").GetProperty("maxMana").GetInt64());
        Assert.Equal(15, Class("Magician").GetProperty("growthPerLevel").GetProperty("maxMana").GetInt64());
        Assert.Equal(18, Class("Tidesworn").GetProperty("growthPerLevel").GetProperty("maxHP").GetInt64());
        Assert.False(Class("Paladin").GetProperty("usesMana").GetBoolean());
        Assert.True(Class("MysticShaman").GetProperty("usesMana").GetBoolean());
        Assert.Equal(9, _export.Data("classes").EnumerateArray().Count(c => c.GetProperty("usesMana").GetBoolean()));
        Assert.Equal(new[] { "DPS", "Tank" }, Strings(Class("Warrior").GetProperty("defaultRoles")));
        Assert.Equal(new[] { "Tank", "DPS" }, Strings(Class("Tidesworn").GetProperty("defaultRoles")));
        Assert.Equal(new[] { "DPS" }, Strings(Class("Voidreaver").GetProperty("defaultRoles")));
        Assert.Equal(6, Class("Sage").GetProperty("trainingPointsPerLevel").GetInt32());
        Assert.Equal(3, Class("Warrior").GetProperty("trainingPointsPerLevel").GetInt32());
        foreach (var c in _export.Data("classes").EnumerateArray())
        {
            var id = Enum.Parse<CharacterClass>(c.GetProperty("id").GetString()!);
            Assert.Equal(UsurperRemake.Data.SpecializationData.GetDefaultRolesForClass(id).Select(r => r.ToString()).ToArray(),
                Strings(c.GetProperty("defaultRoles")));
            Assert.Equal(TrainingSystem.CalculateTrainingPointsPerLevel(new Character { Class = id }),
                c.GetProperty("trainingPointsPerLevel").GetInt32());
        }
    }

    // 1.3.1 wiki depth: armour weights and usable weapon and shield templates, by the game's own equip check
    [Fact]
    public void ClassEquipmentCountsTemplatesTheClassCanEquip()
    {
        (int usable, int total) Count(string classId, string group, string type)
        {
            var row = Assert.Single(Class(classId).GetProperty("equipment").GetProperty(group).EnumerateArray()
                .Where(w => w.GetProperty("type").GetString() == type));
            return (row.GetProperty("usable").GetInt32(), row.GetProperty("total").GetInt32());
        }
        Assert.Equal(new[] { "Light" }, Strings(Class("Magician").GetProperty("equipment").GetProperty("armorWeights")));
        Assert.Equal(new[] { "Light", "Medium" }, Strings(Class("Ranger").GetProperty("equipment").GetProperty("armorWeights")));
        Assert.Equal(new[] { "Light", "Medium", "Heavy" }, Strings(Class("Warrior").GetProperty("equipment").GetProperty("armorWeights")));
        Assert.Equal((10, 10), Count("Bard", "weapons", "Instrument"));
        Assert.Equal((0, 10), Count("Jester", "weapons", "Instrument"));
        Assert.Equal((4, 4), Count("Barbarian", "weapons", "Axe"));
        Assert.Equal((2, 4), Count("Warrior", "weapons", "Axe"));
        Assert.Equal((1, 4), Count("Ranger", "weapons", "Axe"));
        Assert.Equal((4, 5), Count("Paladin", "shields", "TowerShield"));
        Assert.Equal((0, 5), Count("Magician", "shields", "TowerShield"));
        // a prestige class skips every class list
        foreach (var group in new[] { "weapons", "shields" })
            Assert.All(Class("Tidesworn").GetProperty("equipment").GetProperty(group).EnumerateArray(),
                w => Assert.Equal(w.GetProperty("total").GetInt32(), w.GetProperty("usable").GetInt32()));
        // 1.3.1: Mystic Shaman is the last class value but not a prestige class (GameConfig.IsPrestigeClass), so
        // the equip check applies its class lists, its main hand rule and its Medium limit. The export shows
        // what the check answers; this pins it so a change to the check is seen here.
        Assert.Equal(new[] { "Light", "Medium" }, Strings(Class("MysticShaman").GetProperty("equipment").GetProperty("armorWeights")));
        Assert.Equal((8, 12), Count("MysticShaman", "weapons", "Sword"));
        Assert.Equal((2, 4), Count("MysticShaman", "weapons", "Axe"));
        Assert.Equal((4, 8), Count("MysticShaman", "weapons", "Mace"));
        Assert.Equal((1, 1), Count("MysticShaman", "weapons", "Flail"));
        Assert.Equal((5, 5), Count("MysticShaman", "weapons", "Maul"));
        foreach (var refused in new[] { "Dagger", "Rapier", "Greatsword", "Greataxe", "Polearm", "Staff", "Instrument" })
            Assert.Equal(0, Count("MysticShaman", "weapons", refused).usable);
        Assert.Equal((9, 9), Count("MysticShaman", "shields", "Shield"));
        Assert.Equal((9, 9), Count("MysticShaman", "shields", "Buckler"));
        Assert.Equal((0, 5), Count("MysticShaman", "shields", "TowerShield"));
        // totals are the templates of that type, as the shop names the type; bows are left out
        var templates = LootGenerator.GetWeaponTemplates().Select(t => ShopItemGenerator.InferWeaponType(t.Name)).ToList();
        foreach (var w in Class("Warrior").GetProperty("equipment").GetProperty("weapons").EnumerateArray())
        {
            var type = Enum.Parse<WeaponType>(w.GetProperty("type").GetString()!);
            Assert.NotEqual(WeaponType.Bow, type);
            Assert.Equal(templates.Count(t => t == type), w.GetProperty("total").GetInt32());
        }
    }

    // 1.3.1 wiki depth: the weapon and shield templates with the class list of each, and armour type weights
    [Fact]
    public void CharactersListWeaponAndShieldTemplatesWithClasses()
    {
        var data = _export.Data("characters");
        var weapons = data.GetProperty("weaponTemplates").EnumerateArray().ToArray();
        JsonElement Weapon(string name) => Assert.Single(weapons.Where(w => w.GetProperty("name").GetString() == name));
        Assert.Equal(new[] { "Warrior", "Barbarian", "Ranger", "MysticShaman" }, Strings(Weapon("Hand Axe").GetProperty("classes")));
        Assert.Equal("Axe", Weapon("Hand Axe").GetProperty("weaponType").GetString());
        Assert.Equal("OneHanded", Weapon("Hand Axe").GetProperty("handedness").GetString());
        Assert.Equal("Greataxe", Weapon("Greataxe").GetProperty("weaponType").GetString());
        Assert.Equal("TwoHanded", Weapon("Greataxe").GetProperty("handedness").GetString());
        Assert.Equal(new[] { "All" }, Strings(Weapon("Dagger").GetProperty("classes")));
        Assert.Equal(new[] { "Bard" }, Strings(Weapon("Wooden Flute").GetProperty("classes")));
        Assert.Equal("TwoHanded", Weapon("Quarterstaff").GetProperty("handedness").GetString());
        Assert.DoesNotContain(weapons, w => w.GetProperty("weaponType").GetString() == "Bow");
        Assert.Equal(LootGenerator.GetWeaponTemplates().Count(t => ShopItemGenerator.InferWeaponType(t.Name) != WeaponType.Bow), weapons.Length);
        var shields = data.GetProperty("shieldTemplates").EnumerateArray().ToArray();
        Assert.Equal(LootGenerator.GetShieldTemplates().Count, shields.Length);
        var tower = Assert.Single(shields.Where(w => w.GetProperty("name").GetString() == "Tower Shield"));
        Assert.Equal(new[] { "Warrior", "Paladin", "Barbarian" }, Strings(tower.GetProperty("classes")));
        Assert.Equal("TowerShield", tower.GetProperty("weaponType").GetString());
        var weights = data.GetProperty("armorTypeWeights").EnumerateArray()
            .ToDictionary(a => a.GetProperty("armorType").GetString()!, a => a.GetProperty("weightClass").GetString());
        Assert.Equal(7, weights.Count);
        Assert.Equal("Heavy", weights["Plate"]);
        Assert.Equal("Medium", weights["Chain"]);
        Assert.Equal("Light", weights["Leather"]);
    }

    // 1.3.1 wiki depth: proficiency tiers come from the TrainingSystem functions
    [Fact]
    public void CharactersListProficiencyTiersFromTrainingSystem()
    {
        var tiers = _export.Data("characters").GetProperty("proficiency").EnumerateArray().ToArray();
        Assert.Equal(9, tiers.Length);
        foreach (var tier in tiers)
        {
            var level = Enum.Parse<TrainingSystem.ProficiencyLevel>(tier.GetProperty("id").GetString()!);
            Assert.Equal(TrainingSystem.GetEffectMultiplier(level), tier.GetProperty("effectMultiplier").GetSingle());
            Assert.Equal(TrainingSystem.GetRollModifier(level), tier.GetProperty("rollModifier").GetInt32());
            Assert.Equal(TrainingSystem.GetFailureChance(level), tier.GetProperty("failureChance").GetInt32());
            Assert.Equal(TrainingSystem.GetCombatImprovementChance(level), tier.GetProperty("improveByUseChance").GetInt32());
        }
        int Cost(string id) => tiers.Single(t => t.GetProperty("id").GetString() == id).GetProperty("trainingPointsToNext").GetInt32();
        Assert.Equal(6, Cost("Average"));
        Assert.Equal(75, Cost("Master"));
        Assert.Equal(165, new[] { "Average", "Good", "Skilled", "Expert", "Superb", "Master" }.Sum(Cost));
        Assert.Equal(JsonValueKind.Null, tiers[8].GetProperty("trainingPointsToNext").ValueKind);
        Assert.Equal(2.0f, tiers[8].GetProperty("effectMultiplier").GetSingle());
    }

    // 1.3.1 wiki depth: the experience of each level comes from GameConfig.GetExperienceForLevel
    [Fact]
    public void CharactersListExperienceForEveryLevel()
    {
        var levels = _export.Data("characters").GetProperty("experienceByLevel").EnumerateArray().ToArray();
        Assert.Equal(GameConfig.MaxLevel, levels.Length);
        for (int i = 0; i < levels.Length; i++)
        {
            Assert.Equal(i + 1, levels[i].GetProperty("level").GetInt32());
            Assert.Equal(GameConfig.GetExperienceForLevel(i + 1), levels[i].GetProperty("totalExperience").GetInt64());
        }
        Assert.Equal(0, levels[0].GetProperty("totalExperience").GetInt64());
        Assert.Equal(200, levels[1].GetProperty("totalExperience").GetInt64());
        Assert.Equal(19200, levels[9].GetProperty("totalExperience").GetInt64());
        Assert.Equal(3.0, levels[0].GetProperty("killExperienceMultiplier").GetDouble());
        Assert.Equal(2.0, levels[9].GetProperty("killExperienceMultiplier").GetDouble());
        Assert.Equal(1.0, levels[39].GetProperty("killExperienceMultiplier").GetDouble());
    }

    // 1.3.1 wiki depth: the constants the Characters guides print, each by its exact GameConfig name
    [Fact]
    public void CharactersCarryNamedPassiveAndAwakeningConstants()
    {
        var constants = _export.Data("characters").GetProperty("constants");
        Assert.Equal(GameConfig.AlchemistPotionMasteryBonus, constants.GetProperty("AlchemistPotionMasteryBonus").GetSingle());
        Assert.Equal(GameConfig.MagicianArcaneSpellBonus, constants.GetProperty("MagicianArcaneSpellBonus").GetSingle());
        Assert.Equal(GameConfig.CyclebreakerPassiveEvadePercent, constants.GetProperty("CyclebreakerPassiveEvadePercent").GetInt32());
        Assert.Equal(GameConfig.BardInspirationChance, constants.GetProperty("BardInspirationChance").GetInt32());
        Assert.Equal(GameConfig.SoftTauntStickChance, constants.GetProperty("SoftTauntStickChance").GetInt32());
        Assert.Equal(GameConfig.RespecBaseGoldCost, constants.GetProperty("RespecBaseGoldCost").GetInt32());
        Assert.Equal(AwakeningBonus.Wisdom1, constants.GetProperty("AwakeningBonusWisdom1").GetInt32());
        Assert.Equal(AwakeningBonus.Stage7Extra, constants.GetProperty("AwakeningBonusStage7Extra").GetDouble());
        Assert.Equal(62, constants.EnumerateObject().Count());
    }

    // 1.3.1 wiki depth: the abilities table shows the cooldown and the base values of the template
    [Fact]
    public void AbilitiesIncludeCooldownAndBaseValues()
    {
        var abilities = _export.Data("abilities").EnumerateArray().ToArray();
        var roar = Assert.Single(abilities.Where(a => a.GetProperty("id").GetString() == "thundering_roar"));
        Assert.Equal(3, roar.GetProperty("cooldown").GetInt32());
        Assert.Equal(20, roar.GetProperty("levelRequired").GetInt32());
        Assert.Equal(40, roar.GetProperty("staminaCost").GetInt32());
        Assert.Equal("aoe_taunt", roar.GetProperty("specialEffect").GetString());
        var wall = Assert.Single(abilities.Where(a => a.GetProperty("id").GetString() == "shield_wall"));
        Assert.Equal(30, wall.GetProperty("defenseBonus").GetInt32());
        Assert.Equal(3, wall.GetProperty("duration").GetInt32());
        Assert.True(wall.GetProperty("requiresShield").GetBoolean());
        var tune = Assert.Single(abilities.Where(a => a.GetProperty("id").GetString() == "inspiring_tune"));
        Assert.Equal(25, tune.GetProperty("attackBonus").GetInt32());
    }

    // 1.3.1 wiki depth: every Mental gain the recovery table shows is an exported constant
    [Fact]
    public void MentalConstantsCarryEveryRecoverySource()
    {
        var constants = _export.Data("mental").GetProperty("constants");
        Assert.Equal(GameConfig.MentalHomeSleepGain, constants.GetProperty("MentalHomeSleepGain").GetInt32());
        Assert.Equal(GameConfig.MentalInnFriendGain, constants.GetProperty("MentalInnFriendGain").GetInt32());
        Assert.Equal(GameConfig.MentalLearningGain, constants.GetProperty("MentalLearningGain").GetInt32());
        Assert.Equal(GameConfig.MentalRehabGain, constants.GetProperty("MentalRehabGain").GetInt32());
        Assert.Equal(GameConfig.MentalGriefAcceptanceGain, constants.GetProperty("MentalGriefAcceptanceGain").GetInt32());
    }

    [Fact]
    public void RacesIncludeBuiltInBonuses()
    {
        var races = _export.Data("races").EnumerateArray().ToArray();
        Assert.Equal(10, races.Length);
        var human = Assert.Single(races.Where(r => r.GetProperty("id").GetString() == "Human"));
        Assert.Equal(14, human.GetProperty("bonusesAndCreationRanges").GetProperty("hpBonus").GetInt32());
        Assert.Equal(75, human.GetProperty("npcLifespanYears").GetInt32());
    }

    [Fact]
    public void SpellsHaveStableClassAndLevelIds()
    {
        var spells = _export.Data("spells").EnumerateArray().ToArray();
        Assert.NotEmpty(spells);
        var cure = Assert.Single(spells.Where(s => s.GetProperty("id").GetString() == "Cleric:1"));
        Assert.Equal("Cure Light", cure.GetProperty("name").GetProperty("en").GetString());
        Assert.True(cure.GetProperty("manaCost").GetInt32() > 0);
    }

    [Fact]
    public void AbilitiesIncludeCombatValues()
    {
        var abilities = _export.Data("abilities").EnumerateArray().ToArray();
        Assert.NotEmpty(abilities);
        var strike = Assert.Single(abilities.Where(a => a.GetProperty("id").GetString() == "power_strike"));
        Assert.Equal(35, strike.GetProperty("baseDamage").GetInt32());
        Assert.Equal("Power Strike", strike.GetProperty("name").GetProperty("en").GetString());
    }

    [Fact]
    public void GodsContainTenFixedDomainsAndFavorRules()
    {
        var data = _export.Data("gods");
        var gods = data.GetProperty("gods").EnumerateArray().ToArray();
        Assert.Equal(10, gods.Length);
        var solarius = Assert.Single(gods.Where(g => g.GetProperty("id").GetString() == "solarius"));
        Assert.Equal("Light", solarius.GetProperty("domain").GetString());
        Assert.Equal(15, solarius.GetProperty("boonConstants").GetProperty("GodBoonSolariusUndeadDamagePct").GetInt32());
        Assert.Equal(75, data.GetProperty("tiers")[3].GetProperty("minFavor").GetInt32());
        Assert.True(solarius.GetProperty("deedsAndTaboos").GetArrayLength() > 0);
    }

    [Fact]
    public void MentalBandsMatchGameThresholds()
    {
        var data = _export.Data("mental");
        Assert.Equal(5, data.GetProperty("bands").GetArrayLength());
        Assert.Equal(GameConfig.MentalStableThreshold, data.GetProperty("bands")[4].GetProperty("min").GetInt32());
        Assert.Equal(GameConfig.MentalDeathLoss, data.GetProperty("constants").GetProperty("MentalDeathLoss").GetInt32());
    }

    [Fact]
    public void BalanceContainsModdableAndFixedValues()
    {
        var data = _export.Data("balance");
        Assert.Equal(GameConfig.BossPotionCooldownRounds, data.GetProperty("moddableDefaults").GetProperty("bossPotionCooldownRounds").GetInt32());
        Assert.Equal(GameConfig.GodBoonTerranMaxHpPct, data.GetProperty("godConstants").GetProperty("GodBoonTerranMaxHpPct").GetInt32());
        Assert.Equal(GameConfig.SpecializationUnlockLevel, data.GetProperty("characterConstants").GetProperty("SpecializationUnlockLevel").GetInt32());
    }

    [Fact]
    public void AchievementsIncludeFixedRewards()
    {
        var achievements = _export.Data("achievements").EnumerateArray().ToArray();
        Assert.NotEmpty(achievements);
        var firstBlood = Assert.Single(achievements.Where(a => a.GetProperty("id").GetString() == "first_blood"));
        Assert.Equal(5, firstBlood.GetProperty("pointValue").GetInt32());
        Assert.False(firstBlood.GetProperty("spoiler").GetBoolean());
        Assert.All(achievements, a => Assert.Equal(a.GetProperty("isSecret").GetBoolean(), a.GetProperty("spoiler").GetBoolean()));
        Assert.Contains(achievements, a => a.GetProperty("spoiler").GetBoolean());
    }

    [Fact]
    public void BossesSeparatePublicAndSpoilerEncounters()
    {
        var data = _export.Data("bosses");
        Assert.Equal(7, data.GetProperty("oldGods").GetArrayLength());
        Assert.Equal(8, data.GetProperty("world").GetArrayLength());
        Assert.Equal(4, data.GetProperty("secret").GetArrayLength());
        var maelketh = Assert.Single(data.GetProperty("oldGods").EnumerateArray()
            .Where(b => b.GetProperty("id").GetString() == "Maelketh"));
        Assert.Equal(22000, maelketh.GetProperty("baseHP").GetInt64());
        Assert.True(data.GetProperty("secret")[0].GetProperty("spoiler").GetBoolean());
        Assert.False(data.GetProperty("world")[0].GetProperty("spoiler").GetBoolean());
    }

    [Fact]
    public void PartyBeastsMeetTheRegionAndTheBeastLevel()
    {
        // 1.3.2: a sighting needs the region's level (WildernessLocation) and the beast's own (BeastData)
        var beasts = _export.Data("party").GetProperty("beasts").EnumerateArray().ToArray();
        Assert.Equal(8, beasts.Length);
        JsonElement Beast(string id) => Assert.Single(beasts.Where(b => b.GetProperty("id").GetString() == id));
        Assert.Equal(12, Beast("marsh_toad").GetProperty("beastMinLevel").GetInt32());
        Assert.Equal(20, Beast("marsh_toad").GetProperty("region").GetProperty("minLevel").GetInt32());
        Assert.Equal(20, Beast("marsh_toad").GetProperty("sightedFromLevel").GetInt32());
        Assert.Equal(30, Beast("tidepool_sprite").GetProperty("sightedFromLevel").GetInt32());
        Assert.Equal(10, Beast("forest_hawk").GetProperty("sightedFromLevel").GetInt32());
        Assert.Equal(50, Beast("storm_eagle").GetProperty("sightedFromLevel").GetInt32());
        Assert.Equal("Combat", Beast("dire_wolf").GetProperty("role").GetString());
        Assert.Equal(80, Beast("dire_wolf").GetProperty("combatBase").GetProperty("hp").GetInt32());
        Assert.Equal(JsonValueKind.Null, Beast("forest_hawk").GetProperty("combatBase").ValueKind);
    }

    [Fact]
    public void PartyCompanionsCarryTheirFightingClassAndWeaponRule()
    {
        var companions = _export.Data("party").GetProperty("companions").EnumerateArray().ToArray();
        Assert.Equal(new[] { "Aldric", "Lyris", "Melodia", "Mira", "Vex" },
            companions.Select(c => c.GetProperty("id").GetString()).ToArray());
        JsonElement Companion(string id) => Assert.Single(companions.Where(c => c.GetProperty("id").GetString() == id));
        string[] Types(string id) => Companion(id).GetProperty("mainHandWeaponTypes").EnumerateArray()
            .Select(t => t.GetString()!).ToArray();
        // Lyris grows as Damage but fights as a Ranger
        Assert.Equal("Damage", Companion("Lyris").GetProperty("role").GetString());
        Assert.Equal("Ranger", Companion("Lyris").GetProperty("combatClass").GetString());
        Assert.Equal(new[] { "Sword", "Dagger", "Bow" }, Types("Lyris"));
        Assert.Equal(new[] { "Mace", "Hammer", "Flail", "Staff" }, Types("Mira"));
        Assert.Equal("Cleric", Companion("Mira").GetProperty("combatClass").GetString());
        Assert.Equal(10, Companion("Aldric").GetProperty("recruitLevel").GetInt32());
        Assert.Equal(350, Companion("Aldric").GetProperty("baseStats").GetProperty("hp").GetInt32());
        Assert.True(Companion("Melodia").GetProperty("romanceAvailable").GetBoolean());
        Assert.False(Companion("Vex").GetProperty("romanceAvailable").GetBoolean());
    }

    [Fact]
    public void PartyStancesAndConstantsComeFromTheGame()
    {
        var party = _export.Data("party");
        var cautious = Assert.Single(party.GetProperty("stances").EnumerateArray()
            .Where(s => s.GetProperty("id").GetString() == "Cautious"));
        Assert.False(cautious.GetProperty("mayTaunt").GetBoolean());
        Assert.Equal(0.70, cautious.GetProperty("targetWeightMultiplier").GetDouble(), 3);
        var constants = party.GetProperty("constants");
        Assert.Equal(8, constants.GetProperty("BeastEncounterChancePercent").GetInt32());
        Assert.Equal(8, constants.GetProperty("BeastMaxRosterSize").GetInt32());
        Assert.Equal(4, constants.GetProperty("MaxActiveCompanions").GetInt32());
        Assert.Equal(4, constants.GetProperty("WildernessMaxDailyExplorations").GetInt32());
    }

    private JsonElement[] Economy(string list) => _export.Data("economy").GetProperty(list).EnumerateArray().ToArray();

    private static JsonElement Find(JsonElement[] rows, string key, string id) =>
        Assert.Single(rows.Where(r => r.GetProperty(key).ToString() == id));

    [Fact]
    public void EconomyMaterialsCarryTheirBandAndWhatSpendsThem()
    {
        // 1.3.2: the bands are GameConfig.CraftingMaterials; the uses are the Magic Shop enchant tiers and the Inn training
        var materials = Economy("materials");
        Assert.Equal(8, materials.Length);
        var shard = Find(materials, "id", "crimson_war_shard");
        Assert.Equal("Crimson War Shard", shard.GetProperty("name").GetProperty("en").GetString());
        Assert.Equal(20, shard.GetProperty("floorMin").GetInt32());
        Assert.Equal(35, shard.GetProperty("floorMax").GetInt32());
        Assert.Equal(new[] { 13 }, shard.GetProperty("enchants").EnumerateArray().Select(e => e.GetInt32()).ToArray());
        var heart = Find(materials, "id", "heart_of_the_ocean");
        Assert.Equal(new[] { 11 }, heart.GetProperty("enchants").EnumerateArray().Select(e => e.GetInt32()).ToArray());
        Assert.Equal(new[] { 4, 5 }, heart.GetProperty("statTrainings").EnumerateArray().Select(e => e.GetInt32()).ToArray());
        var dust = Find(materials, "id", "fading_starlight_dust");
        Assert.Equal(new[] { 10, 13, 14 }, dust.GetProperty("enchants").EnumerateArray().Select(e => e.GetInt32()).ToArray());
        var petal = Find(materials, "id", "withered_heart_petal");
        Assert.Equal(0, petal.GetProperty("enchants").GetArrayLength());
        Assert.Equal(0, petal.GetProperty("statTrainings").GetArrayLength());
        var gods = Economy("materialGods");
        Assert.Equal(7, gods.Length);
        Assert.Equal("crimson_war_shard", Find(gods, "god", "Maelketh").GetProperty("material").GetString());
        Assert.Equal(25, Find(gods, "god", "Maelketh").GetProperty("floor").GetInt32());
        Assert.Equal("eye_of_manwe", Find(gods, "god", "Manwe").GetProperty("material").GetString());
    }

    [Fact]
    public void EconomyEnchantsReadTheShopTiersAndWhatANamedEnchantAdds()
    {
        var enchants = Economy("enchants");
        Assert.Equal(14, enchants.Length);
        var godforged = Find(enchants, "number", "12");
        Assert.Equal("Godforged", godforged.GetProperty("name").GetProperty("en").GetString());
        Assert.True(godforged.GetProperty("oneChosenStat").GetBoolean());
        Assert.Equal(38, godforged.GetProperty("statBonus").GetInt32());
        Assert.Equal(500000, godforged.GetProperty("baseCost").GetInt64());
        Assert.Equal(5000, godforged.GetProperty("costPerLevel").GetInt32());
        Assert.Equal(75, godforged.GetProperty("minLevel").GetInt32());
        Assert.Equal(new[] { "eye_of_manwe", "terravoks_heartstone" },
            godforged.GetProperty("materials").EnumerateArray().Select(m => m.GetProperty("id").GetString()).ToArray());
        var blessing = Find(enchants, "number", "5");
        Assert.False(blessing.GetProperty("oneChosenStat").GetBoolean());
        var effects = blessing.GetProperty("effects").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetDouble());
        Assert.Equal(new[] { "ArmorClass", "DefenceBonus", "DexterityBonus", "StrengthBonus", "WeaponPower", "WisdomBonus" },
            effects.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.All(effects.Values, v => Assert.Equal(3, v));
        var ocean = Find(enchants, "number", "6").GetProperty("effects");
        Assert.Equal(6, ocean.GetProperty("IntelligenceBonus").GetDouble());
        Assert.Equal(4, ocean.GetProperty("WisdomBonus").GetDouble());
        var phoenix = Find(enchants, "number", "13").GetProperty("effects");
        Assert.True(phoenix.GetProperty("HasFireEnchant").GetBoolean());
        Assert.Equal(20, phoenix.GetProperty("WeaponPower").GetDouble());
        Assert.Equal(0, Find(enchants, "number", "1").GetProperty("materials").GetArrayLength());
    }

    [Fact]
    public void EconomyGearSetsRarityHerbsAndHomeComeFromTheGame()
    {
        var sets = Economy("gearSets");
        Assert.Equal(8, sets.Length);
        var silk = Find(sets, "id", "silk");
        Assert.Equal(new[] { 2, 4, 5 }, silk.GetProperty("tiers").EnumerateArray().Select(t => t.GetProperty("pieces").GetInt32()).ToArray());
        var leather = Find(sets, "id", "leather").GetProperty("tiers")[0].GetProperty("bonuses")[0];
        Assert.Equal("+10 Max HP", leather.GetProperty("text").GetString());

        var rarities = Economy("lootRarities");
        Assert.Equal(6, rarities.Length);
        var legendary = Find(rarities, "id", "Legendary");
        Assert.Equal(3.0, legendary.GetProperty("powerMultiplier").GetDouble(), 3);
        Assert.Equal(20.0, legendary.GetProperty("valueMultiplier").GetDouble(), 3);
        Assert.Equal(0.10, legendary.GetProperty("curseChance").GetDouble(), 3);
        Assert.Equal("Legendary", legendary.GetProperty("namePrefix").GetString());
        Assert.Equal("Mythic", Find(rarities, "id", "Artifact").GetProperty("namePrefix").GetString());
        Assert.Equal(JsonValueKind.Null, Find(rarities, "id", "Common").GetProperty("namePrefix").ValueKind);

        var herbs = Economy("herbs");
        Assert.Equal(5, herbs.Length);
        Assert.Equal(10, Find(herbs, "id", "HealingHerb").GetProperty("maxCarry").GetInt32());
        Assert.Equal(4, Find(herbs, "id", "Swiftthistle").GetProperty("gardenLevel").GetInt32());

        var home = _export.Data("economy").GetProperty("homeUpgrades");
        var levels = home.GetProperty("levels").EnumerateArray().ToArray();
        Assert.Equal(6, levels.Length);
        Assert.Equal(JsonValueKind.Null, levels[0].GetProperty("livingQuartersCost").ValueKind);
        Assert.Equal(25000, levels[1].GetProperty("livingQuartersCost").GetInt64());
        Assert.Equal(2000000, levels[5].GetProperty("gardenCost").GetInt64());
        Assert.Equal(100, levels[5].GetProperty("chestCapacity").GetInt32());
        Assert.Equal(15, levels[5].GetProperty("hearthFights").GetInt32());
        var training = home.GetProperty("trainingRoom").EnumerateArray().ToArray();
        Assert.Equal(10, training.Length);
        Assert.Equal(3500000, training[9].GetProperty("cost").GetInt64());
    }

    [Fact]
    public void EconomyArtifactsGrantWhatTheGameGrants()
    {
        // 1.3.2: ArtifactSystem.ArtifactStatGrants: Intelligence is granted as half Wisdom and half Dexterity
        var artifacts = Economy("artifacts");
        Assert.Equal(7, artifacts.Length);
        var eye = Find(artifacts, "id", "CreatorsEye");
        Assert.Equal("Maelketh", eye.GetProperty("oldGod").GetString());
        Assert.Equal(25, eye.GetProperty("floor").GetInt32());
        var grants = eye.GetProperty("grants").EnumerateArray()
            .ToDictionary(g => g.GetProperty("stat").GetString()!, g => g.GetProperty("amount").GetInt64());
        Assert.Equal(new Dictionary<string, long> { ["Wisdom"] = 35, ["Dexterity"] = 25 }, grants);
        var key = Find(artifacts, "id", "VoidKey");
        Assert.Equal(JsonValueKind.Null, key.GetProperty("oldGod").ValueKind);
        Assert.Equal(7, key.GetProperty("grants").GetArrayLength());
        var blade = Find(artifacts, "id", "SunforgedBlade");
        Assert.Equal(50, blade.GetProperty("weaponPower").GetInt32());
        Assert.Equal(100, blade.GetProperty("chivalry").GetInt32());
        Assert.Equal(85, blade.GetProperty("floor").GetInt32());
        var constants = _export.Data("economy").GetProperty("constants");
        Assert.Equal(0.03, constants.GetProperty("MaterialDropChanceRegular").GetDouble(), 3);
        Assert.Equal(5, constants.GetProperty("MaxEnchantments").GetInt32());
        Assert.Equal(0.20, constants.GetProperty("LootBaseDropChance").GetDouble(), 3);
    }

    /// <summary>1.3.2: the balance export leaves out the six values no live code reads (CriticalHitChance and
    /// CriticalHitMultiplier: StatEffectsSystem decides crits; BackstabMultiplier: read only by the dead
    /// ExecuteBackstab; Berserk, Doom rounds and the tank's AoE share: no reader).</summary>
    [Fact]
    public void BalanceLeavesOutTheValuesNoCodeReads()
    {
        var data = _export.Data("balance");
        var moddable = data.GetProperty("moddableDefaults");
        foreach (var gone in new[] { "criticalHitChance", "criticalHitMultiplier", "backstabMultiplier", "berserkMultiplier",
                     "bossDoomDefaultRounds", "bossTankAoEAbsorption" })
            Assert.False(moddable.TryGetProperty(gone, out _), gone);
        var combat = data.GetProperty("combatConstants");
        foreach (var gone in new[] { "CriticalHitChance", "CriticalHitMultiplier", "BackstabMultiplier", "BerserkMultiplier" })
            Assert.False(combat.TryGetProperty(gone, out _), gone);
        // every other moddable default is still there, under the property's own name
        Assert.Equal(typeof(UsurperRemake.Data.BalanceConfig).GetProperties().Length - 6, moddable.EnumerateObject().Count());
        Assert.Equal(GameConfig.BossEnrageExtraAttacks, moddable.GetProperty("bossEnrageExtraAttacks").GetInt32());
    }

    private JsonElement[] Combat(string list) => _export.Data("combat").GetProperty(list).EnumerateArray().ToArray();

    [Fact]
    public void CombatStatusesCarryTheGamesFlagsAndTickDamage()
    {
        var statuses = Combat("statuses");
        Assert.Equal(Enum.GetValues<StatusEffect>().Length - 1, statuses.Length);
        foreach (var s in statuses)
        {
            var status = Enum.Parse<StatusEffect>(s.GetProperty("id").GetString()!);
            Assert.Equal(status.PreventsAction(), s.GetProperty("preventsAction").GetBoolean());
            Assert.Equal(status.PreventsSpellcasting(), s.GetProperty("preventsSpellcasting").GetBoolean());
            Assert.Equal(status.GetCategory().ToString(), s.GetProperty("category").GetString());
            // Stoneskin and Shielded are the two a round's tick does not count down
            Assert.Equal(status is not (StatusEffect.Stoneskin or StatusEffect.Shielded), s.GetProperty("countsDownEachRound").GetBoolean());
            var ticks = s.GetProperty("damagePerRound").EnumerateArray().ToArray();
            if (Character.StatusDamagePerTurn(status, 1) == null) { Assert.Empty(ticks); continue; }
            Assert.Equal(new[] { 1, 25, 50, 100 }, ticks.Select(t => t.GetProperty("level").GetInt32()));
            foreach (var t in ticks)
            {
                var (min, max) = Character.StatusDamagePerTurn(status, t.GetProperty("level").GetInt32())!.Value;
                Assert.Equal(min, t.GetProperty("min").GetInt32());
                Assert.Equal(max, t.GetProperty("max").GetInt32());
            }
        }
        var poison = statuses.Single(s => s.GetProperty("id").GetString() == "Poisoned").GetProperty("damagePerRound")[3];
        Assert.Equal(12, poison.GetProperty("min").GetInt32());   // 2 + 100 / 10
        Assert.Equal(15, poison.GetProperty("max").GetInt32());
    }

    [Fact]
    public void CombatTargetWeightsAreTheGamesClassWeights()
    {
        var weights = Combat("targetWeights").ToDictionary(w => w.GetProperty("classId").GetString()!);
        Assert.Equal(Enum.GetValues<CharacterClass>().Length, weights.Count);
        Assert.Equal(180, weights["Paladin"].GetProperty("weight").GetInt32());
        Assert.Equal(190, weights["Tidesworn"].GetProperty("weight").GetInt32());
        Assert.Equal(60, weights["Magician"].GetProperty("weight").GetInt32());
        Assert.Equal(120, weights["MysticShaman"].GetProperty("weight").GetInt32());
        foreach (var w in weights.Values)
            Assert.Equal(w.GetProperty("weight").GetInt32() + 40, w.GetProperty("weightWhileDefending").GetInt32());
    }

    [Fact]
    public void CombatRegenerationFollowsTheRoundEndRules()
    {
        var rows = Combat("regeneration");
        Assert.NotEmpty(rows);
        foreach (var r in rows)
        {
            int stat = r.GetProperty("stat").GetInt32();
            Assert.Equal(StatEffectsSystem.GetManaRegenPerRound(stat), r.GetProperty("manaPerRound").GetInt32());
            Assert.Equal(Math.Min(15, 2 + stat / 10), r.GetProperty("manaPerRound").GetInt32());
            Assert.Equal(3 + stat / 15 + GameConfig.LightArmorStaminaRegen, r.GetProperty("staminaPerRoundLight").GetInt32());
            Assert.Equal(3 + stat / 15 + GameConfig.MediumArmorStaminaRegen, r.GetProperty("staminaPerRoundMedium").GetInt32());
            Assert.Equal(3 + stat / 15 + GameConfig.HeavyArmorStaminaRegen, r.GetProperty("staminaPerRoundHeavy").GetInt32());
        }
    }

    [Fact]
    public void CombatConstantsComeFromTheGame()
    {
        var constants = _export.Data("combat").GetProperty("constants");
        Assert.Equal(GameConfig.PlayerHitMinNeeded, constants.GetProperty("PlayerHitMinNeeded").GetInt32());
        Assert.Equal(GameConfig.MonsterAccuracyMaxNeededAtLevel, constants.GetProperty("MonsterAccuracyMaxNeededAtLevel").GetInt32());
        Assert.Equal(EliteAffixes.VenomChancePercent, constants.GetProperty("VenomChancePercent").GetInt32());
        Assert.Equal(MonsterTactics.PredatorBonus, constants.GetProperty("PredatorBonus").GetInt32());
        Assert.Equal(GameConfig.RiteOfReturnBaseCost, constants.GetProperty("RiteOfReturnBaseCost").GetInt64());
    }

    // ---------- 1.3.2 wiki depth: the dungeon and the world beyond the town (world.json) ----------

    private JsonElement[] World(string list) => _export.Data("world").GetProperty(list).EnumerateArray().ToArray();

    private static JsonElement One(JsonElement[] rows, string id, string field = "id") =>
        Assert.Single(rows.Where(r => r.GetProperty(field).ToString() == id));

    [Fact]
    public void WorldMonsterAbilitiesCoverEveryAbilityAMonsterCarries()
    {
        var abilities = World("monsterAbilities");
        var ids = abilities.Select(a => a.GetProperty("id").GetString()!).ToHashSet();
        var carried = MonsterFamilies.GetBuiltInFamilies().SelectMany(f => f.Tiers).SelectMany(t => t.SpecialAbilities.Concat(t.SignatureAbilities))
            .Concat(MonsterGenerator.CasterSpells.Values)
            .Concat(WildernessData.MonsterProfiles.Values.SelectMany(p => p.Abilities))
            .Concat(UsurperRemake.Data.GauntletChampionData.Champions.SelectMany(c => c.SpecialAbilities))
            .Select(a => Enum.Parse<MonsterAbilities.AbilityType>(a, true).ToString()).ToHashSet();
        Assert.Equal(carried.OrderBy(x => x), ids.OrderBy(x => x));

        // the numbers MonsterAbilities.ExecuteAbility gives
        var crushing = One(abilities, "CrushingBlow").GetProperty("effects");
        Assert.Equal(2.0, crushing.GetProperty("attackPowerMultiplier").GetDouble());
        Assert.Equal("Stunned", crushing.GetProperty("status").GetString());
        Assert.Equal(25, crushing.GetProperty("statusChance").GetInt32());
        Assert.Equal(2, crushing.GetProperty("statusRounds").GetInt32());
        var venom = One(abilities, "VenomousBite").GetProperty("effects");
        Assert.Equal(0.8, venom.GetProperty("attackPowerMultiplier").GetDouble());
        Assert.Equal(60, venom.GetProperty("statusChance").GetInt32());
        Assert.Equal(50, One(abilities, "LifeDrain").GetProperty("effects").GetProperty("lifeStealPercent").GetInt32());
        // a breath: (level x 3 + Strength / 2) x 1.5 + 5 to 14, from the level 20 sample monster
        var stats = MonsterGenerator.GetWikiStats(WikiDataExporter.AbilityProbeLevel, 1.0f);
        long breath = (long)((WikiDataExporter.AbilityProbeLevel * 3 + stats.Strength / 2) * 1.5f);
        var fire = One(abilities, "FireBreath").GetProperty("effects").GetProperty("directDamage");
        Assert.InRange(fire.GetProperty("min").GetInt64(), breath + 5, breath + 14);
        Assert.InRange(fire.GetProperty("max").GetInt64(), breath + 5, breath + 14);
        // extra attacks are not read by any fight, so Multiattack is one plain hit
        Assert.Equal(1.0, One(abilities, "Multiattack").GetProperty("effects").GetProperty("attackPowerMultiplier").GetDouble());
        Assert.False(One(abilities, "Multiattack").GetProperty("effects").TryGetProperty("extraAttacks", out _));
        // the conditional ones are marked
        Assert.True(One(abilities, "Devour").GetProperty("changesWhenTargetHurt").GetBoolean());
        Assert.True(One(abilities, "Rebirth").GetProperty("changesWhenMonsterHurt").GetBoolean());
        Assert.True(One(abilities, "Stoneskin").GetProperty("changesOnSecondUse").GetBoolean());
        Assert.False(One(abilities, "CrushingBlow").GetProperty("changesOnSecondUse").GetBoolean());
        // who uses it
        var users = One(abilities, "FireBreath").GetProperty("users").EnumerateArray()
            .Select(u => u.GetProperty("name").GetString() + "/" + u.GetProperty("kind").GetString()).ToArray();
        Assert.Contains("Drake/monster", users);
        Assert.Contains("Wyvern Hatchling/wilderness", users);
        Assert.Contains("Goblin/signature", One(abilities, "Rally").GetProperty("users").EnumerateArray()
            .Select(u => u.GetProperty("name").GetString() + "/" + u.GetProperty("kind").GetString()));
    }

    [Fact]
    public void WorldFloorsAreTheGeneratedFloorsWithTheirBoss()
    {
        var floors = World("floors");
        Assert.Equal(GameConfig.MaxDungeonLevel, floors.Length);
        for (int f = 1; f <= floors.Length; f++)
        {
            var row = floors[f - 1];
            Assert.Equal(f, row.GetProperty("floor").GetInt32());
            var generated = DungeonGenerator.GenerateFloor(f);
            Assert.Equal(generated.Theme.ToString(), row.GetProperty("theme").GetString());
            Assert.Equal(generated.Rooms.Count, row.GetProperty("rooms").GetInt32());
        }
        Assert.Equal(15, floors[0].GetProperty("rooms").GetInt32());
        Assert.Equal(20, floors[39].GetProperty("rooms").GetInt32());
        Assert.Equal(25, floors[99].GetProperty("rooms").GetInt32());
        // the boss name is the theme's list at floor % 5
        Assert.Equal("Skull Revenant", floors[6].GetProperty("boss").GetString());
        Assert.Equal("Bone Lord", floors[9].GetProperty("boss").GetString());
        Assert.Equal("Sludge Abomination", floors[19].GetProperty("boss").GetString());
        Assert.Equal("Void Horror", floors[99].GetProperty("boss").GetString());
    }

    [Fact]
    public void WorldOutpostsPriceAsTheDungeonDoes()
    {
        var outposts = World("outposts");
        Assert.Equal(new[] { 10, 20, 35, 65 }, outposts.Select(o => o.GetProperty("floor").GetInt32()));
        // (10 + floor x 5) x the outpost's factor
        Assert.Equal(30, One(outposts, "10", "floor").GetProperty("healCost").GetInt64());
        Assert.Equal(JsonValueKind.Null, One(outposts, "20", "floor").GetProperty("healCost").ValueKind);
        Assert.Equal(74, One(outposts, "35", "floor").GetProperty("healCost").GetInt64());
        Assert.Equal(201, One(outposts, "65", "floor").GetProperty("healCost").GetInt64());
        Assert.Equal(0.4, One(outposts, "35", "floor").GetProperty("healShareOfMissing").GetDouble(), 3);
        // base price x (1 + floor / 10)
        var goods = One(outposts, "65", "floor").GetProperty("goods").EnumerateArray().ToArray();
        Assert.Equal(25 * 7, One(goods, "heal_potion").GetProperty("price").GetInt64());
        Assert.Equal(60 * 7, One(goods, "firebloom").GetProperty("price").GetInt64());
    }

    [Fact]
    public void WorldWildernessRegionsAndShrinesComeFromTheData()
    {
        var regions = World("wildernessRegions");
        Assert.Equal(new[] { 1, 10, 20, 30 }, regions.Select(r => r.GetProperty("minLevel").GetInt32()));
        var forest = One(regions, "forest");
        Assert.Equal(5, forest.GetProperty("foraging").GetArrayLength());
        Assert.Contains(forest.GetProperty("foraging").EnumerateArray(), f => f.GetProperty("effect").GetString() == "herb_ironbark");
        Assert.Equal(new[] { 1, 3 }, forest.GetProperty("discoveries").EnumerateArray().Select(d => d.GetProperty("minLevel").GetInt32()));
        Assert.Equal(8, forest.GetProperty("monsters").GetArrayLength());
        var shrines = World("druidShrines");
        Assert.Equal(5, shrines.Length);
        Assert.Equal("mountains", One(shrines, "terravok").GetProperty("region").GetString());
        Assert.Equal(-3, One(shrines, "noctura").GetProperty("chivalryShift").GetInt32());
        Assert.Equal(5, One(shrines, "aurelion").GetProperty("chivalryShift").GetInt32());
        Assert.Equal(UsurperRemake.Systems.Loc.GetIn("en", "shrine.maelketh.passive"), One(shrines, "maelketh").GetProperty("passive").GetString());
    }

    [Fact]
    public void WorldGauntletTiersAndChampions()
    {
        var tiers = World("gauntletTiers");
        Assert.Equal(5, tiers.Length);
        var hopeful = One(tiers, "Hopeful");
        Assert.Equal(GameConfig.GauntletMinLevel, hopeful.GetProperty("minLevel").GetInt32());
        Assert.Equal(19, hopeful.GetProperty("maxLevel").GetInt32());
        Assert.Equal(200, hopeful.GetProperty("goldPerLevel").GetInt32());
        var grand = One(tiers, "GrandChampion");
        Assert.Equal(80, grand.GetProperty("minLevel").GetInt32());
        Assert.Equal(150, grand.GetProperty("fame").GetInt32());
        var champions = World("gauntletChampions");
        Assert.Equal(new[] { 4, 5, 6, 7, 8, 9, 10 }, champions.Select(c => c.GetProperty("wave").GetInt32()));
        Assert.Equal(14, champions[6].GetProperty("levelBonus").GetInt32());
        Assert.Equal(4.0, champions[6].GetProperty("hpMultiplier").GetDouble(), 3);
    }

    [Fact]
    public void WorldSellswordRanksAndContracts()
    {
        var ranks = World("mercRanks");
        Assert.Equal(new[] { 0, 1, 10, 30, 75, 150 }, ranks.Select(r => r.GetProperty("contractsRequired").GetInt32()));
        Assert.Equal("Legend", One(ranks, "Legend").GetProperty("name").GetProperty("en").GetString());
        Assert.Equal(1.15, One(ranks, "Legend").GetProperty("payMultiplier").GetDouble(), 3);
        var contracts = World("mercContracts");
        Assert.Equal(12, contracts.Length);
        Assert.Equal(6, contracts.Count(c => c.GetProperty("tier").GetInt32() == 2));
        var undead = One(contracts, "faith_purge_undead");
        Assert.Equal("Unhallowed Dead", undead.GetProperty("title").GetProperty("en").GetString());
        Assert.Equal("Zombie", undead.GetProperty("target").GetString());
        Assert.Equal(3, undead.GetProperty("baseCount").GetInt32());
    }

    [Fact]
    public void SecretBossesCarryTheirRewardAndOnlyTheStatsAFightReads()
    {
        var secret = _export.Data("bosses").GetProperty("secret").EnumerateArray().ToArray();
        var first = One(secret, "TheFirstWave");
        Assert.Equal(10000, first.GetProperty("rewardXP").GetInt32());
        Assert.Equal(5000, first.GetProperty("rewardGold").GetInt32());
        Assert.False(first.GetProperty("asksQuestion").GetBoolean());
        Assert.True(One(secret, "TheForgottenEighth").GetProperty("asksQuestion").GetBoolean());
        Assert.Equal(0, One(secret, "TheOceanSpeaks").GetProperty("rewardGold").GetInt32());
        foreach (var gone in new[] { "baseMagicPower", "baseSpeed", "baseCritChance", "abilities" })
            Assert.False(first.TryGetProperty(gone, out _), gone);
    }

    [Fact]
    public void WorldConstantsComeFromTheGame()
    {
        var constants = _export.Data("world").GetProperty("constants");
        Assert.Equal(GameConfig.EliteChanceFloors3To10, constants.GetProperty("EliteChanceFloors3To10").GetDouble());
        Assert.Equal(GameConfig.GauntletDeathChancePercent, constants.GetProperty("GauntletDeathChancePercent").GetInt32());
        Assert.Equal(EliteAffixes.MendHealPercent, constants.GetProperty("MendHealPercent").GetInt32());
        Assert.Equal(GameConfig.Floor2MonsterStatMultiplier, constants.GetProperty("Floor2MonsterStatMultiplier").GetSingle());
        Assert.Equal(RareEncounters.RareEncounterChance, constants.GetProperty("RareEncounterChance").GetDouble());
        Assert.Equal(DungeonFloorState.RESPAWN_HOURS, constants.GetProperty("FloorRESPAWN_HOURS").GetInt32());
    }

    [Fact]
    public void MetaIdentifiesSchemaAndRelease()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(_export.DirectoryPath, "meta.json")));
        Assert.Equal(WikiDataExporter.SchemaVersion, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(GameConfig.Version, document.RootElement.GetProperty("gameVersion").GetString());
        Assert.True(DateTimeOffset.TryParse(document.RootElement.GetProperty("generatedAtUtc").GetString(), out _));
        Assert.Equal(5, document.RootElement.GetProperty("languages").GetArrayLength());
    }
}

[Collection("SharedGameSingletons")]
public class WikiDataExporterIsolationTests
{
    private static JsonElement Data(string dir, string file)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, file + ".json")));
        return document.RootElement.GetProperty("data").Clone();
    }

    private static long GoblinHp(string dir) => Data(dir, "monsters").EnumerateArray()
        .Single(f => f.GetProperty("id").GetString() == "goblinoid").GetProperty("tiers")[0]
        .GetProperty("normalStatsAtMinLevel").GetProperty("hp").GetInt64();

    private static JsonElement Entry(string dir, string file, string id) => Data(dir, file).EnumerateArray()
        .Single(e => e.GetProperty("id").GetString() == id);

    [Fact]
    public void ExportIgnoresDifficultyServerMultiplierAndLoadedOverrides()
    {
        var baseline = Path.Combine(Path.GetTempPath(), "usurper-wiki-base-" + Guid.NewGuid().ToString("N"));
        var changed = Path.Combine(Path.GetTempPath(), "usurper-wiki-changed-" + Guid.NewGuid().ToString("N"));
        var oldHp = GameConfig.MonsterHPMultiplier;
        var oldDifficulty = DifficultySystem.CurrentDifficulty;
        var spell = SpellSystem.BuiltInTemplate()[0];
        var ability = ClassAbilitySystem.BuiltInTemplate().Single(a => a.Id == "power_strike");
        var spellId = spell.Class + ":" + spell.Level;
        var baseMana = spell.ManaCost!.Value;
        var baseCooldown = ability.Cooldown!.Value;
        try
        {
            WikiDataExporter.Export(baseline);

            GameConfig.MonsterHPMultiplier = 3.0f;
            DifficultySystem.CurrentDifficulty = DifficultyMode.Nightmare;
            SpellSystem.ApplyOverrides(new[] { new UsurperRemake.Data.SpellOverride
                { Class = spell.Class, Level = spell.Level, ManaCost = baseMana + 77 } });
            ClassAbilitySystem.ApplyOverrides(new[] { new UsurperRemake.Data.AbilityOverride
                { Id = ability.Id, Cooldown = baseCooldown + 7 } });
            Assert.Equal(baseMana + 77, SpellSystem.GetSpellInfo(spell.Class, spell.Level).ManaCost);
            Assert.Equal(baseCooldown + 7, ClassAbilitySystem.GetAbility(ability.Id)!.Cooldown);

            WikiDataExporter.Export(changed);

            Assert.Equal(GoblinHp(baseline), GoblinHp(changed));
            // 1.2.8: the mana of the export is what a cast charges before Wisdom, 10 + 5 per slot; no override reaches it
            Assert.Equal(SpellSystem.BaseManaCost(spell.Level), Entry(changed, "spells", spellId).GetProperty("manaCost").GetInt32());
            Assert.Equal(baseCooldown, Entry(changed, "abilities", ability.Id).GetProperty("cooldown").GetInt32());
        }
        finally
        {
            GameConfig.MonsterHPMultiplier = oldHp;
            DifficultySystem.CurrentDifficulty = oldDifficulty;
            SpellSystem.ApplyOverrides(new[] { spell });
            ClassAbilitySystem.ApplyOverrides(new[] { ability });
            foreach (var dir in new[] { baseline, changed })
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ExportReadsCompanionDefinitionsAndLeavesTheSharedCompanionsAlone()
    {
        // 1.3.2: the export reads the five companions as the game defines them, not the shared system a
        // recruitment has scaled, and does not replace that shared system
        var dir = Path.Combine(Path.GetTempPath(), "usurper-wiki-companions-" + Guid.NewGuid().ToString("N"));
        var shared = CompanionSystem.Instance;
        var aldric = shared.GetCompanion(CompanionId.Aldric)!;
        var oldHp = aldric.BaseStats.HP;
        try
        {
            aldric.BaseStats.HP = oldHp + 999;
            WikiDataExporter.Export(dir);
            Assert.Same(shared, CompanionSystem.Instance);
            var exported = Entry(dir, "party", "companions", "Aldric");
            Assert.Equal(350, exported.GetProperty("baseStats").GetProperty("hp").GetInt32());
        }
        finally
        {
            aldric.BaseStats.HP = oldHp;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    private static JsonElement Entry(string dir, string file, string list, string id) => Data(dir, file)
        .GetProperty(list).EnumerateArray().Single(e => e.GetProperty("id").GetString() == id);

    [Theory]
    [InlineData(new object[] { new[] { "--export-wiki" } })]
    [InlineData(new object[] { new[] { "--export-wiki", "--local" } })]
    [InlineData(new object[] { new[] { "--export-wiki", " " } })]
    public void CommandLineWithoutDirectoryPrintsUsageAndFails(string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = WikiDataExporter.RunCommandLine(args, stdout, stderr);
        Assert.Equal(2, exit);
        Assert.Contains("Usage: UsurperReborn --export-wiki <output directory>", stderr.ToString());
        Assert.Equal("", stdout.ToString());
    }

    [Fact]
    public void CommandLineWithoutFlagIsIgnoredAndWithDirectoryExports()
    {
        Assert.Null(WikiDataExporter.RunCommandLine(new[] { "--local" }, TextWriter.Null, TextWriter.Null));
        var dir = Path.Combine(Path.GetTempPath(), "usurper-wiki-cli-" + Guid.NewGuid().ToString("N"));
        try
        {
            var stdout = new StringWriter();
            Assert.Equal(0, WikiDataExporter.RunCommandLine(new[] { "--export-wiki", dir }, stdout, TextWriter.Null));
            Assert.True(File.Exists(Path.Combine(dir, "meta.json")));
            Assert.Contains(dir, stdout.ToString());
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
