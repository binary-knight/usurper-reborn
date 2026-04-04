using System.Text.Json;
using Xunit;
using FluentAssertions;
using UsurperRemake.Utils;

namespace UsurperReborn.Tests;

/// <summary>
/// Unit tests for TolerantEnumConverter — the tolerant JSON enum converter
/// that returns defaults instead of throwing on unknown values.
/// </summary>
public class TolerantEnumConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new TolerantEnumConverterFactory() }
    };

    #region Helper Types

    private class TestModel
    {
        public CharacterClass Class { get; set; }
        public CharacterClass? NullableClass { get; set; }
    }

    #endregion

    [Fact]
    public void Deserialize_ValidString_ReturnsCorrectValue()
    {
        var json = """{"Class":"Warrior"}""";
        var result = JsonSerializer.Deserialize<TestModel>(json, Options)!;

        result.Class.Should().Be(CharacterClass.Warrior);
    }

    [Fact]
    public void Deserialize_CaseInsensitive_ReturnsCorrectValue()
    {
        var json = """{"Class":"warrior"}""";
        var result = JsonSerializer.Deserialize<TestModel>(json, Options)!;

        result.Class.Should().Be(CharacterClass.Warrior);
    }

    [Fact]
    public void Deserialize_UnknownString_ReturnsDefault()
    {
        var json = """{"Class":"InvalidClass"}""";
        var result = JsonSerializer.Deserialize<TestModel>(json, Options)!;

        // default(CharacterClass) is Alchemist (first value = 0)
        result.Class.Should().Be(CharacterClass.Alchemist);
    }

    [Fact]
    public void Serialize_EnumValue_WritesString()
    {
        var model = new TestModel { Class = CharacterClass.Warrior };
        var json = JsonSerializer.Serialize(model, Options);

        json.Should().Contain("\"Warrior\"");
    }

    [Fact]
    public void Deserialize_NullableEnum_Null_ReturnsNull()
    {
        var json = """{"NullableClass":null}""";
        var result = JsonSerializer.Deserialize<TestModel>(json, Options)!;

        result.NullableClass.Should().BeNull();
    }

    [Fact]
    public void Deserialize_NullableEnum_ValidValue_ReturnsValue()
    {
        var json = """{"NullableClass":"Warrior"}""";
        var result = JsonSerializer.Deserialize<TestModel>(json, Options)!;

        result.NullableClass.Should().Be(CharacterClass.Warrior);
    }

    [Fact]
    public void RoundTrip_AllCharacterClasses_Preserved()
    {
        var allClasses = Enum.GetValues<CharacterClass>();
        allClasses.Length.Should().Be(17, "there should be 17 CharacterClass values");

        foreach (var cls in allClasses)
        {
            var model = new TestModel { Class = cls };
            var json = JsonSerializer.Serialize(model, Options);
            var deserialized = JsonSerializer.Deserialize<TestModel>(json, Options)!;

            deserialized.Class.Should().Be(cls, $"round-trip should preserve {cls}");
        }
    }
}
