using System.IO;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.15: menu-alt-label. The startup menu's [M] entry read "Create Mortal Alt Character"; it now
/// reads "Create Alt Character". This checks the label in all five Localization/*.json files and
/// that no language still carries its word for "mortal".
/// </summary>
public class MenuAltLabel1115Tests
{
    [Theory]
    [InlineData("en", "Create Alt Character", "Mortal")]
    [InlineData("es", "Crear Personaje Alt", "Mortal")]
    [InlineData("fr", "Créer un Personnage Alt", "Mortel")]
    [InlineData("it", "Crea personaggio alternativo", "mortale")]
    [InlineData("hu", "Másodlagos karakter létrehozása", "Halandó")]
    public void CreateAltLabel_HasNoMortal(string lang, string expected, string mortalWord)
    {
        string root = FloorAndStun1113Tests.RepoRoot();
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "Localization", lang + ".json")));
        string label = doc.RootElement.GetProperty("engine.menu_create_mortal_alt").GetString()!;
        label.Should().Be(expected);
        label.Should().NotContainEquivalentOf(mortalWord);
    }
}
