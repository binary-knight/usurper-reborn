using System.Text.Json;
using Xunit;

namespace UsurperReborn.Tests;

public class DivineArmourField135Tests : IClassFixture<WikiDataFixture>
{
    private readonly WikiDataFixture _export;
    public DivineArmourField135Tests(WikiDataFixture export) => _export = export;

    [Fact]
    public void OldGodMechanicsCarryTheDivineArmorCutUnderItsOwnName()
    {
        var gods = _export.Data("realm").GetProperty("oldGodMechanics").EnumerateArray().ToArray();
        int Cut(string id)
        {
            var god = gods.Single(g => g.GetProperty("id").GetString() == id);
            return god.TryGetProperty("divineArmorCutPercent", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
        }
        Assert.Equal((int)Math.Round(GameConfig.AurelionDivineShield * 100), Cut("Aurelion"));
        Assert.Equal((int)Math.Round(GameConfig.TerravokStoneSkin * 100), Cut("Terravok"));
        Assert.Equal((int)Math.Round(GameConfig.ManweCreatorsWard * 100), Cut("Manwe"));
        Assert.Equal(0, Cut("Maelketh"));
        Assert.All(gods, g => Assert.False(g.TryGetProperty("unenchantedDamageCutPercent", out _),
            "no Old God row carries the stale unenchantedDamageCutPercent field"));
    }
}
