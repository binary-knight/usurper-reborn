using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.14: no two different items share a display name in any language. A shared name hides which item
/// a player has, and a name the gear set resolver reads cannot say which template it came from. Item
/// names are the one-level keys item.xxx; item.effect.*, item.set.*, item.slot.* and the like are not
/// item names.
/// </summary>
public class ItemNameUniqueness1114Tests
{
    private static readonly Regex ItemNameKey = new(@"^item\.[a-z0-9_]+$");

    public static IEnumerable<object[]> Languages() =>
        new[] { "en", "fr", "es", "it", "hu" }.Select(l => new object[] { l });

    [Theory]
    [MemberData(nameof(Languages))]
    public void NoTwoItems_ShareADisplayName(string lang)
    {
        var path = Path.Combine(Leftovers1114BTests.RepoRoot(), "Localization", lang + ".json");
        var d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
        var names = d.Where(kv => ItemNameKey.IsMatch(kv.Key)).ToList();
        names.Count.Should().BeGreaterThan(300, "every item name in the file, not an empty scan");
        var shared = names
            .GroupBy(kv => kv.Value.Trim().ToLowerInvariant())
            .Where(g => g.Count() > 1)
            .Select(g => $"{lang} \"{g.First().Value}\": {string.Join(", ", g.Select(kv => kv.Key).OrderBy(k => k))}")
            .ToList();
        shared.Should().BeEmpty("each item needs its own name");
    }
}
