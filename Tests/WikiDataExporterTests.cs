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
            Assert.Equal(baseMana, Entry(changed, "spells", spellId).GetProperty("manaCost").GetInt32());
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
