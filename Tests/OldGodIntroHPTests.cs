using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.10: the intro screen before a god fight printed the god's data HP (55,000 for Maelketh)
/// while the fight used the scaled HP (123,750 at no artifacts). Both now come from one method.
/// </summary>
[Collection("SharedGameSingletons")]
public class OldGodIntroHPTests
{
    [Theory]
    [InlineData(OldGodType.Maelketh)]
    [InlineData(OldGodType.Veloura)]
    [InlineData(OldGodType.Thorgrim)]
    public void TheIntroShows_TheHPTheGodFightsWith(OldGodType god)
    {
        var data = UsurperRemake.Data.OldGodsData.GetGodBossData(god);
        var monster = (Monster)typeof(OldGodBossSystem).GetMethod("CreateBossMonster", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(OldGodBossSystem.Instance, new object[] { data })!;
        OldGodBossSystem.FightHP(data).Should().Be(monster.MaxHP, "the intro and the fight read the same number");
        OldGodBossSystem.FightHP(data).Should().BeGreaterThan(data.HP, "the fight's HP is scaled up from the data");
    }

    [Fact]
    public async Task TheRealIntroScreen_PrintsTheFightHP()
    {
        var data = UsurperRemake.Data.OldGodsData.GetGodBossData(OldGodType.Maelketh);
        var output = new MemoryStream();
        var term = new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes("\n")), output);
        var hero = new Character { Name1 = "intro", Name2 = "Intro", Class = CharacterClass.Warrior, Level = 25, HP = 900, MaxHP = 900 };
        await (Task)typeof(OldGodBossSystem).GetMethod("PlayBossIntroduction", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(OldGodBossSystem.Instance, new object[] { data, hero, term })!;
        term.StreamWriterInternal!.Flush();
        string shown = Encoding.UTF8.GetString(output.ToArray());
        shown.Should().Contain(OldGodBossSystem.FightHP(data).ToString("N0"));
        shown.Should().NotContain(data.HP.ToString("N0"), "the unscaled data HP misled players by more than half");
    }
}
