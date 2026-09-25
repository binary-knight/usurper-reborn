using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.13: the dungeon tutorial line about Old God floors states the real gate: ascending and leaving are
/// allowed, only going deeper waits until the god is resolved (DungeonLocation GetMaxAccessibleFloor / DescendStairs).
/// </summary>
public class GodFloorTutorial1113Tests
{
    private static Dictionary<string, string> Lang(string lang)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Localization"))) dir = dir.Parent;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(dir!.FullName, "Localization", lang + ".json")))!;
    }

    [Theory]
    [InlineData("en", "You cannot leave these floors")]
    [InlineData("es", "No puedes abandonar esos pisos")]
    [InlineData("fr", "Tu ne peux pas quitter ces étages")]
    [InlineData("it", "Non puoi lasciare questi piani")]
    [InlineData("hu", "Ezekről a szintekről nem távozhatsz")]
    public void GodFloorLine_NoLongerSaysYouCannotLeave(string lang, string oldClaim)
    {
        string line = Lang(lang)["dungeon.tut.p8.t3"];
        line.Should().NotContain(oldClaim);
        line.Should().StartWith("    ", "it continues the bullet above");
    }

    [Fact]
    public void GodFloorLine_SaysTheGateIsGoingDeeper()
    {
        Lang("en")["dungeon.tut.p8.t3"].Should().Contain("retreat").And.Contain("cannot go deeper");
    }
}
