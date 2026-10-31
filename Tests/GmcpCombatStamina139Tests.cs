using System.IO;
using System.Text;
using System.Text.Json;
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

    // The JSON of every Char.Vitals frame written to the stream, in order.
    private static JsonElement[] VitalsFrames(MemoryStream stream)
    {
        var bytes = stream.ToArray();
        var frames = new System.Collections.Generic.List<JsonElement>();
        var pkg = Encoding.UTF8.GetBytes("Char.Vitals ");
        for (int i = 0; i + 3 < bytes.Length; i++)
        {
            if (bytes[i] != 0xFF || bytes[i + 1] != 0xFA || bytes[i + 2] != 0xC9) continue;
            int start = i + 3, end = start;
            while (end + 1 < bytes.Length && !(bytes[end] == 0xFF && bytes[end + 1] == 0xF0)) end++;
            var body = new byte[end - start];
            System.Array.Copy(bytes, start, body, 0, body.Length);
            if (body.Length < pkg.Length || Encoding.UTF8.GetString(body, 0, pkg.Length) != "Char.Vitals ") continue;
            frames.Add(JsonDocument.Parse(Encoding.UTF8.GetString(body, pkg.Length, body.Length - pkg.Length)).RootElement.Clone());
            i = end + 1;
        }
        return frames.ToArray();
    }

    private static T WithGmcp<T>(bool enabled, System.Func<MemoryStream, T> run)
    {
        var saved = SessionContext.Current;
        var stream = new MemoryStream();
        SessionContext.Current = new SessionContext { OutputStream = stream, GmcpEnabled = enabled };
        try { return run(stream); }
        finally { SessionContext.Current = saved; }
    }

    [Fact]
    public void Vitals_CarryFightStaminaAndKeepEveryExistingField()
    {
        WithGmcp(true, stream =>
        {
            var hero = Hero();
            hero.CurrentCombatStamina = 87;
            GmcpBridge.EmitVitalsIfChanged(hero);
            var f = VitalsFrames(stream);
            f.Should().HaveCount(1);
            f[0].GetProperty("hp").GetInt64().Should().Be(100);
            f[0].GetProperty("maxHp").GetInt64().Should().Be(100);
            f[0].GetProperty("mp").GetInt64().Should().Be(0);
            f[0].GetProperty("maxMp").GetInt64().Should().Be(0);
            f[0].GetProperty("sp").GetInt64().Should().Be(13, "sp stays the Stamina stat");
            f[0].GetProperty("mental").GetInt32().Should().Be(hero.Mental);
            f[0].GetProperty("maxMental").GetInt32().Should().Be(GameConfig.MaxMentalStability);
            f[0].GetProperty("combatSp").GetInt64().Should().Be(87);
            f[0].GetProperty("maxCombatSp").GetInt64().Should().Be(hero.MaxCombatStamina);
            return 0;
        });
    }

    [Fact]
    public void FightStaminaAloneTriggersAnEmit_AndNoChangeDoesNot()
    {
        WithGmcp(true, stream =>
        {
            var hero = Hero();
            hero.CurrentCombatStamina = 100;
            GmcpBridge.EmitVitalsIfChanged(hero); // baseline emit
            stream.SetLength(0);

            hero.CurrentCombatStamina = 85; // only fight stamina changes
            GmcpBridge.EmitVitalsIfChanged(hero);
            VitalsFrames(stream).Should().ContainSingle().Which.GetProperty("combatSp").GetInt64().Should().Be(85);

            stream.SetLength(0);
            GmcpBridge.EmitVitalsIfChanged(hero);
            stream.Length.Should().Be(0, "nothing tracked changed, so no frame");
            return 0;
        });
    }

    [Fact]
    public void MaxFightStaminaAloneTriggersAnEmit()
    {
        WithGmcp(true, stream =>
        {
            var hero = Hero();
            GmcpBridge.EmitVitalsIfChanged(hero); // baseline emit
            long before = hero.MaxCombatStamina;
            stream.SetLength(0);

            hero.Level += 1; // MaxCombatStamina = 50 + Stamina * 2 + Level * 3 + armor bonus
            hero.MaxCombatStamina.Should().NotBe(before);
            GmcpBridge.EmitVitalsIfChanged(hero);
            VitalsFrames(stream).Should().ContainSingle().Which.GetProperty("maxCombatSp").GetInt64().Should().Be(hero.MaxCombatStamina);

            stream.SetLength(0);
            GmcpBridge.EmitVitalsIfChanged(hero);
            stream.Length.Should().Be(0);
            return 0;
        });
    }

    [Fact]
    public void NoFrameWithoutGmcp()
    {
        WithGmcp(false, stream =>
        {
            var hero = Hero();
            hero.CurrentCombatStamina = 50;
            GmcpBridge.EmitVitalsIfChanged(hero);
            stream.Length.Should().Be(0);
            return 0;
        });
    }

    [Fact]
    public void MultiMonsterActionPromptSendsVitals()
    {
        // GetPlayerActionMultiMonster needs a live terminal to run, so this reads its source: the
        // vitals send must sit inside its prompt loop, as it does in GetPlayerAction.
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Systems", "CombatEngine.cs"));
        int head = src.IndexOf("private async Task<(CombatAction action, bool enableAutoCombat)> GetPlayerActionMultiMonster(");
        head.Should().BeGreaterThan(0);
        int loop = src.IndexOf("while (true)", head);
        int next = src.IndexOf("if (!player.CanAct())", loop);
        src.Substring(loop, next - loop).Should().Contain("GmcpBridge.EmitVitalsIfChanged(player);");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj"))) dir = dir.Parent;
        return dir!.FullName;
    }
}
