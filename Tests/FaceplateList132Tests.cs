using System.Linq;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.2: Steel Faceplate is Heavy armor by its name, which the Mystic Shaman cannot wear, so its class list
/// no longer names the Shaman. Warrior and Paladin stay. The "All" face items stay "All".
/// </summary>
[Collection("SharedGameSingletons")]
public class FaceplateList132Tests
{
    [Fact]
    public void SteelFaceplate_ClassList_DoesNotNameTheMysticShaman_AndKeepsWarriorAndPaladin()
    {
        var faceplate = LootGenerator.GetFaceArmorTemplates().Single(t => t.Name == "Steel Faceplate");
        faceplate.Classes.Should().NotContain("MysticShaman");
        faceplate.Classes.Should().Equal("Warrior", "Paladin");
    }

    [Fact]
    public void ALootRollForAMysticShaman_NeverPicksSteelFaceplate()
    {
        int faces = 0;
        foreach (int level in new[] { 40, 55, 70, 80 })
            for (int i = 0; i < 2500; i++)
            {
                var armor = LootGenerator.GenerateArmor(level, CharacterClass.MysticShaman);
                if (armor.Type != ObjType.Face) continue;
                faces++;
                armor.Name.Should().NotContain("Faceplate", $"level {level}: a Shaman roll does not pick a Heavy faceplate");
            }
        faces.Should().BeGreaterThan(100, "the rolls reached the face list");
    }
}
