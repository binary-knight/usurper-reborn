using System.IO;
using System.Text;
using FluentAssertions;
using UsurperRemake.Server;
using Xunit;

/// <summary>
/// 1.3.9: Char.Vitals carries the fight stamina (combatSp, maxCombatSp) for the web play page's
/// status line. sp stays the Stamina stat so existing MUD client scripts read the same value.
/// </summary>
[Collection("SharedGameSingletons")]
public class GmcpCombatStamina139Tests
{
    private static Character Hero() => new Character
    {
        Name1 = "hero", Name2 = "Hero", AI = CharacterAI.Human,
        Class = CharacterClass.Warrior, Race = CharacterRace.Human,
        Level = 10, HP = 100, MaxHP = 100, Mana = 0, MaxMana = 0, Stamina = 13,
    };

    [Fact]
    public void Vitals_CarryFightStaminaAndKeepSpAsTheStaminaStat()
    {
        var saved = SessionContext.Current;
        var stream = new MemoryStream();
        SessionContext.Current = new SessionContext { OutputStream = stream, GmcpEnabled = true };
        try
        {
            var hero = Hero();
            hero.CurrentCombatStamina = 87;
            GmcpBridge.EmitVitalsIfChanged(hero);
            string text = Encoding.UTF8.GetString(stream.ToArray());
            text.Should().Contain("\"sp\":13");
            text.Should().Contain("\"combatSp\":87");
            text.Should().Contain($"\"maxCombatSp\":{hero.MaxCombatStamina}");
        }
        finally { SessionContext.Current = saved; }
    }

    [Fact]
    public void FightStaminaAloneTriggersAnEmit_AndNoChangeDoesNot()
    {
        var saved = SessionContext.Current;
        var stream = new MemoryStream();
        SessionContext.Current = new SessionContext { OutputStream = stream, GmcpEnabled = true };
        try
        {
            var hero = Hero();
            hero.CurrentCombatStamina = 100;
            GmcpBridge.EmitVitalsIfChanged(hero); // baseline emit
            stream.SetLength(0);

            hero.CurrentCombatStamina = 85; // only fight stamina changes
            GmcpBridge.EmitVitalsIfChanged(hero);
            Encoding.UTF8.GetString(stream.ToArray()).Should().Contain("\"combatSp\":85");

            stream.SetLength(0);
            GmcpBridge.EmitVitalsIfChanged(hero);
            stream.Length.Should().Be(0, "nothing tracked changed, so no frame");
        }
        finally { SessionContext.Current = saved; }
    }

    [Fact]
    public void NoFrameWithoutGmcp()
    {
        var saved = SessionContext.Current;
        var stream = new MemoryStream();
        SessionContext.Current = new SessionContext { OutputStream = stream, GmcpEnabled = false };
        try
        {
            var hero = Hero();
            hero.CurrentCombatStamina = 50;
            GmcpBridge.EmitVitalsIfChanged(hero);
            stream.Length.Should().Be(0);
        }
        finally { SessionContext.Current = saved; }
    }
}
