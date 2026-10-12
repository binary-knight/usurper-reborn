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
        // The equip check (Items.cs:1100) treats every class from Tidesworn up in the enum as prestige, and
        // Mystic Shaman is the last value, so the game applies no class list, weight limit or main hand rule to
        // it. The export shows what the check answers; this pins it so a change to the check is seen here.
        foreach (var group in new[] { "weapons", "shields" })
            Assert.All(Class("MysticShaman").GetProperty("equipment").GetProperty(group).EnumerateArray(),
                w => Assert.Equal(w.GetProperty("total").GetInt32(), w.GetProperty("usable").GetInt32()));
        Assert.Equal(new[] { "Light", "Medium", "Heavy" }, Strings(Class("MysticShaman").GetProperty("equipment").GetProperty("armorWeights")));
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
        Assert.Equal(GameConfig.CriticalHitChance, data.GetProperty("moddableDefaults").GetProperty("criticalHitChance").GetInt32());
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
