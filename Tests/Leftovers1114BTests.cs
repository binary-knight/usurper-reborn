using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>v1.1.14: leftover fixes (localization, names, teams, dungeon events, combat holds, combat seeding).</summary>
public class Leftovers1114BTests
{
    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        return dir!.FullName;
    }

    private static Dictionary<string, string> Lang(string lang) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(RepoRoot(), "Localization", lang + ".json")))!;

    [Theory]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("it")]
    [InlineData("hu")]
    public void ShadowCloak_AndCloakOfShadows_HaveDistinctNames(string lang)
    {
        var en = Lang("en");
        en["item.shadow_cloak"].Should().NotBe(en["item.cloak_of_shadows"]);
        var d = Lang(lang);
        d["item.shadow_cloak"].Should().NotBe(d["item.cloak_of_shadows"], "two different items must not share a name");
    }

    [Fact]
    public void French_ShadowCloak_IsSingular_CloakOfShadows_IsPlural()
    {
        var fr = Lang("fr");
        fr["item.shadow_cloak"].Should().Be("Cape d'Ombre");
        fr["item.cloak_of_shadows"].Should().Be("Cape des Ombres");
    }
}
