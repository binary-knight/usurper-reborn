using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake.UI;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.4: every row of every ANSIArt piece fits 79 visible columns, so none wraps on an
/// 80-column terminal (LevelUp was 81, DungeonEntrance 80). No piece is exempt.
/// </summary>
public class ArtWidth124Tests
{
    [Fact]
    public void EveryArtRow_FitsSeventyNineColumns()
    {
        var pieces = typeof(ANSIArt).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(string[]))
            .ToList();
        pieces.Should().HaveCountGreaterThanOrEqualTo(12, "all twelve art pieces are measured");

        foreach (var piece in pieces)
        {
            var rows = (string[])piece.GetValue(null)!;
            for (int i = 0; i < rows.Length; i++)
            {
                int width = Regex.Replace(rows[i], @"\[[^\]]*\]", "").Length;
                width.Should().BeLessThanOrEqualTo(79, $"{piece.Name} row {i} must fit 79 columns");
            }
        }
    }
}
