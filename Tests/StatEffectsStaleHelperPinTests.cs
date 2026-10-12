using System;
using System.IO;
using FluentAssertions;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// Source pin: the dead stat tooltip helper and its stale stamina formula must not come back.
/// Real per-round combat stamina regen lives in Character.RegenerateCombatStamina.
/// </summary>
public class StatEffectsStaleHelperPinTests
{
    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, Path.Combine(parts)));
    }

    [Fact]
    public void StatEffectsSystem_HasNoStatDescriptionOrStaminaRegenCopy()
    {
        var src = Read("Scripts", "Systems", "StatEffectsSystem.cs");
        src.Should().NotContain("GetStatDescription");
        src.Should().NotContain("GetStaminaRegenPerRound");
        src.Should().NotContain("GetStaminaRegenBonus"); // second stale copy (Constitution / 15)
    }

    [Fact]
    public void Character_RegenerateCombatStamina_KeepsRealFormula()
    {
        var src = Read("Scripts", "Core", "Character.cs");
        src.Should().Contain("3 + (int)(Stamina / 15)");
    }
}
