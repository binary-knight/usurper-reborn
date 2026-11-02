using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.9 (play test B3): the web page's footer reads Char.Vitals (HP), Char.Status (level, gold, XP) and Client.Footer
/// (potions, lives, XP percent). Each prompt on a web client session now sends whichever of the three changed since it
/// was last sent, so a level up, a potion drunk or found, a trap, a heal or a gold change shows on the footer before
/// the player's next key, on screens that never draw the status line. Every other session gets nothing more.
/// </summary>
[Collection("SharedGameSingletons")]
public class WebRefresh139Tests
{
    private sealed class Scope : IDisposable
    {
        private readonly SessionContext? _saved = SessionContext.Current;
        private readonly bool _pacing = Pacing.Disabled;
        private readonly string _lang = GameConfig.Language;
        public readonly MemoryStream Stream = new();
        public readonly SessionContext? Ctx;
        public Scope(string kind, Character own, bool engine = true)
        {
            Pacing.Disabled = true;
            GameConfig.Language = "en";
            Ctx = WebFlagOff200Tests.Session(kind, Stream, own);
            if (Ctx != null && engine)
            {
                var e = (GameEngine)RuntimeHelpers.GetUninitializedObject(typeof(GameEngine));
                e.CurrentPlayer = own;
                Ctx.Engine = e;
            }
            SessionContext.Current = Ctx;
        }
        public TerminalEmulator Term(string input)
        {
            var t = new TerminalEmulator(new WebFlagOff3aTests.ScriptedStream(input), Stream);
            if (Ctx != null) Ctx.Terminal = t;
            return t;
        }
        public byte[] Bytes(TerminalEmulator t) { t.StreamWriterInternal?.Flush(); return Stream.ToArray(); }
        public void Dispose()
        {
            SessionContext.Current = _saved;
            Pacing.Disabled = _pacing;
            GameConfig.Language = _lang;
        }
    }

    private static List<JsonElement> Of(byte[] bytes, string package) =>
        WebMenu200Tests.Frames(bytes).Where(f => f.Package == package).Select(f => f.Json).ToList();

    private static Character Hero() => WebFlagOff3aTests.Hero(false);

    private static string Lines(int n) => string.Concat(Enumerable.Repeat("x\n", n));

    [Fact]
    public async Task APrompt_SendsTheFooterValues_OnceEach()
    {
        var hero = Hero();
        using var s = new Scope("web", hero);
        var t = s.Term(Lines(3));
        await t.GetInput("> ");
        var b = s.Bytes(t);
        Of(b, "Char.Vitals").Should().HaveCount(1);
        Of(b, "Char.Status").Should().HaveCount(1);
        Of(b, "Client.Footer").Should().HaveCount(1);
        Of(b, "Char.Status").Single().GetProperty("level").GetInt32().Should().Be(hero.Level);
        Of(b, "Client.Footer").Single().GetProperty("potions").GetInt64().Should().Be(hero.Healing);
        // nothing changed: the next prompt sends none of them again
        await t.GetInput("> ");
        b = s.Bytes(t);
        Of(b, "Char.Vitals").Should().HaveCount(1);
        Of(b, "Char.Status").Should().HaveCount(1);
        Of(b, "Client.Footer").Should().HaveCount(1);
    }

    [Fact]
    public async Task ALevelUp_ShowsTheNewLevel_XpAndPotionCap_AtTheNextPrompt()
    {
        var hero = Hero();
        using var s = new Scope("web", hero);
        var t = s.Term(Lines(2));
        await t.GetInput("> ");
        int before = hero.Level;
        hero.Level = before + 1;
        hero.Experience = GameConfig.GetExperienceForLevel(before + 1);
        hero.MaxHP += 25; hero.HP = hero.MaxHP;
        await t.PressAnyKey();   // the level up screen's pause
        var b = s.Bytes(t);
        Of(b, "Char.Status").Last().GetProperty("level").GetInt32().Should().Be(before + 1);
        Of(b, "Char.Status").Last().GetProperty("xp").GetInt64().Should().Be(hero.Experience);
        Of(b, "Char.Vitals").Last().GetProperty("maxHp").GetInt64().Should().Be(hero.MaxHP);
        Of(b, "Client.Footer").Last().GetProperty("maxPotions").GetInt64().Should().Be(hero.MaxPotions);
        Of(b, "Client.Footer").Last().GetProperty("xpPercent").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task APotionDrunkOrFound_ShowsTheNewCount_AtTheNextPrompt()
    {
        var hero = Hero();
        hero.Healing = 5;
        using var s = new Scope("web", hero);
        var t = s.Term(Lines(3));
        await t.GetInput("> ");
        hero.Healing = 4;
        await t.GetInput("> ");
        Of(s.Bytes(t), "Client.Footer").Last().GetProperty("potions").GetInt64().Should().Be(4);
        hero.Healing = 6;
        await t.GetInput("> ");
        var b = s.Bytes(t);
        Of(b, "Client.Footer").Should().HaveCount(3);
        Of(b, "Client.Footer").Last().GetProperty("potions").GetInt64().Should().Be(6);
        Of(b, "Char.Status").Should().HaveCount(1, "only the footer values changed");
    }

    [Fact]
    public async Task ATrapOrAHeal_ShowsTheNewHp_AtTheNextPrompt()
    {
        var hero = Hero();
        using var s = new Scope("web", hero);
        var t = s.Term(Lines(3));
        await t.GetInput("> ");
        hero.HP -= 35;   // a trap
        await t.PressAnyKey();
        Of(s.Bytes(t), "Char.Vitals").Last().GetProperty("hp").GetInt64().Should().Be(hero.HP);
        hero.HP = hero.MaxHP;   // a heal
        await t.GetInput("> ");
        var b = s.Bytes(t);
        Of(b, "Char.Vitals").Should().HaveCount(3);
        Of(b, "Char.Vitals").Last().GetProperty("hp").GetInt64().Should().Be(hero.MaxHP);
    }

    [Fact]
    public async Task AGoldChange_ShowsTheNewGold_AtTheNextPrompt()
    {
        var hero = Hero();
        using var s = new Scope("web", hero);
        var t = s.Term(Lines(2));
        await t.GetInput("> ");
        hero.Gold += 2336;
        await t.GetInput("> ");
        var b = s.Bytes(t);
        Of(b, "Char.Status").Should().HaveCount(2);
        Of(b, "Char.Status").Last().GetProperty("gold").GetInt64().Should().Be(hero.Gold);
    }

    [Theory]
    [InlineData("door")]
    [InlineData("door_compact")]
    [InlineData("mud_gmcp")]
    [InlineData("ssh")]
    [InlineData("screenreader")]
    [InlineData("web_nogmcp")]
    public async Task ASessionThatIsNotTheWebClient_GetsNothingFromAPrompt(string kind)
    {
        var hero = Hero();
        using var s = new Scope(kind, hero);
        var t = s.Term(Lines(3));
        await t.GetInput("> ");
        hero.HP -= 10; hero.Healing = 1; hero.Gold += 5;
        await t.PressAnyKey();
        await t.GetInput("> ");
        WebFlagOff200Tests.Packages(s.Bytes(t)).Should().BeEmpty("a prompt sends no GMCP frame to a {0} session", kind);
    }

    [Fact]
    public async Task AFollowersTerminal_ReadOnTheLeadersSession_SendsNothing()
    {
        var hero = Hero();
        using var s = new Scope("web", hero);
        s.Term(Lines(1));   // the session's own terminal
        var follower = new TerminalEmulator(new WebFlagOff3aTests.ScriptedStream(Lines(1)), s.Stream);
        await follower.GetInput("> ");
        follower.StreamWriterInternal?.Flush();
        WebFlagOff200Tests.Packages(s.Stream.ToArray()).Should().BeEmpty();
    }

    [Fact]
    public async Task ASessionWithNoCharacterInPlay_SendsNothing()
    {
        var hero = Hero();
        using var s = new Scope("web", hero, engine: false);
        var t = s.Term(Lines(1));
        await t.GetInput("> ");
        WebFlagOff200Tests.Packages(s.Bytes(t)).Should().BeEmpty("before a character is loaded there is no footer to fill");
    }
}
