using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.2: the damage-over-time and status lines in combat read in the player's language: a monster
/// dying of poison or fire, the status ticks and expiries, disease, corruption and doom, the
/// "cannot act" status word, and the leader's death line sent to the group.
/// </summary>
[Collection("SharedGameSingletons")]
public class StatusLoc122Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static (CombatEngine engine, TerminalEmulator term, MemoryStream output) Engine()
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new MemoryStream(), output);
        return (new CombatEngine(term), term, output);
    }

    private static string Shown(TerminalEmulator term, MemoryStream output)
    {
        term.StreamWriterInternal?.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");
    }

    /// <summary>Writes a render to USURPER_EVIDENCE_DIR when set, for a by-eye check.</summary>
    private static void Capture(string name, string text)
    {
        var dir = Environment.GetEnvironmentVariable("USURPER_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, name), text);
    }

    private static Character Hero() => new()
    {
        Name1 = "tester", Name2 = "Tester", Class = CharacterClass.Warrior, Level = 10, HP = 500, MaxHP = 500,
        AI = CharacterAI.Human,
    };

    private static async Task<T> WithLanguage<T>(string lang, Func<Task<T>> body)
    {
        var prev = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            return await body();
        }
        finally { GameConfig.Language = prev; }
    }

    private static Task<T> WithLanguage<T>(string lang, Func<T> body) => WithLanguage(lang, () => Task.FromResult(body()));

    // ---------- 1. a monster dying of poison or fire ----------

    private static async Task<string> RenderMonsterDotDeath(bool burn)
    {
        var (engine, term, output) = Engine();
        var monster = new Monster { Name = "Kobold", Level = 3, HP = 1, MaxHP = 10, IsActive = true };
        if (burn) { monster.BurnRounds = 2; monster.IsBurning = true; }
        else { monster.PoisonRounds = 2; monster.Poisoned = true; }
        var result = new CombatResult();
        await (Task)typeof(CombatEngine).GetMethod("ProcessMonsterAction", F)!
            .Invoke(engine, new object?[] { monster, Hero(), result, null })!;
        monster.IsAlive.Should().BeFalse("the tick kills a one hit point monster");
        return Shown(term, output);
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("fr")]
    public async Task MonsterPoisonDeath_IsLocalized(string lang)
    {
        string text = await WithLanguage(lang, async () =>
        {
            string t = await RenderMonsterDotDeath(burn: false);
            Capture($"status-poison-death-{lang}.txt", t);
            t.Should().Contain(Loc.Get("combat.monster_poison_death", "Kobold"));
            return t;
        });
        text.Should().NotContain("succumbs to poison");
        if (lang == "hu") text.Should().Contain("Kobold belepusztul a méregbe!");
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("it")]
    public async Task MonsterBurnDeath_IsLocalized(string lang)
    {
        string text = await WithLanguage(lang, async () =>
        {
            string t = await RenderMonsterDotDeath(burn: true);
            Capture($"status-burn-death-{lang}.txt", t);
            t.Should().Contain(Loc.Get("combat.monster_burn_death", "Kobold"));
            return t;
        });
        text.Should().NotContain("consumed by flames");
    }

    // ---------- 2. status ticks and expiries ----------

    [Theory]
    [InlineData("hu")]
    [InlineData("es")]
    public async Task StatusTicksAndExpiries_AreLocalized(string lang)
    {
        var text = await WithLanguage(lang, () =>
        {
            var hero = Hero();
            foreach (var s in new[] { StatusEffect.Poisoned, StatusEffect.Bleeding, StatusEffect.Burning, StatusEffect.Stunned, StatusEffect.Raging })
                hero.ApplyStatus(s, 1);
            var lines = hero.ProcessStatusEffects().Select(m => m.message).ToList();
            string t = string.Join("\n", lines);
            Capture($"status-ticks-{lang}.txt", t);
            t.Should().Contain(Loc.Get("status.end_poisoned", "Tester"));
            t.Should().Contain(Loc.Get("status.end_bleeding", "Tester"));
            t.Should().Contain(Loc.Get("status.end_stun", "Tester"));
            t.Should().Contain(Loc.Get("status.end_rage", "Tester"));
            return t;
        });
        text.Should().NotContain("poison damage").And.NotContain("bleeds for").And.NotContain("fire damage")
            .And.NotContain("is no longer").And.NotContain("rage subsides").And.NotContain("can act again");
        if (lang == "hu") text.Should().Contain("méregsebzést szenved").And.Contain("Tester már nincs megmérgezve.");
    }

    // ---------- 3. disease ----------

    [Theory]
    [InlineData("hu")]
    [InlineData("fr")]
    public async Task DiseaseTick_IsLocalized(string lang)
    {
        string text = await WithLanguage(lang, async () =>
        {
            var (engine, term, output) = Engine();
            var hero = Hero();
            hero.Plague = true;
            await (Task)typeof(CombatEngine).GetMethod("ProcessPlagueDamage", F)!
                .Invoke(engine, new object?[] { hero, new CombatResult() })!;
            string t = Shown(term, output);
            Capture($"status-disease-{lang}.txt", t);
            long lost = 500 - hero.HP;
            lost.Should().BeGreaterThan(0);
            t.Should().Contain(Loc.Get("combat.disease_plague", lost));
            return t;
        });
        text.Should().NotContain("ravages your body").And.NotContain(" HP)");
    }

    // ---------- 4. corruption and doom ----------

    [Theory]
    [InlineData("hu")]
    [InlineData("it")]
    public async Task CorruptionAndDoom_AreLocalized(string lang)
    {
        string text = await WithLanguage(lang, () =>
        {
            var (engine, term, output) = Engine();
            var hero = Hero();
            typeof(CombatEngine).GetMethod("ApplyCorruptionStacks", F)!.Invoke(engine, new object?[] { hero, 2 });
            typeof(CombatEngine).GetMethod("ProcessCorruptionTick", F)!.Invoke(engine, new object?[] { hero, new CombatResult() });
            hero.DoomCountdown = 3;
            typeof(CombatEngine).GetMethod("ProcessDoomTick", F)!.Invoke(engine, new object?[] { hero, new CombatResult() });
            hero.DoomCountdown = 1;
            typeof(CombatEngine).GetMethod("ProcessDoomTick", F)!.Invoke(engine, new object?[] { hero, new CombatResult() });
            string t = Shown(term, output);
            Capture($"status-corruption-doom-{lang}.txt", t);
            t.Should().Contain(Loc.Get("combat.corruption_gain", "Tester", 2, 2));
            t.Should().Contain(Loc.Get("combat.doom_tick", "Tester", 2));
            t.Should().Contain(Loc.Get("combat.doom_claims", "Tester"));
            return t;
        });
        text.Should().NotContain("Corruption burns").And.NotContain("gains 2 corruption")
            .And.NotContain("Doom ticks on").And.NotContain("DOOM claims");
    }

    // ---------- 5. the status word in "cannot act" ----------

    [Fact]
    public async Task StatusWord_IsTheLocalizedStatusName()
    {
        var hu = await WithLanguage("hu", () => CombatEngine.StatusWord(StatusEffect.Stunned));
        hu.Should().Be("kábult");
        var fr = await WithLanguage("fr", () => CombatEngine.StatusWord(StatusEffect.Frozen));
        fr.Should().Be("gelé");
        var en = await WithLanguage("en", () => CombatEngine.StatusWord(StatusEffect.Sleeping));
        en.Should().Be("asleep");
    }

    // ---------- 6. the leader's death line sent to the group ----------

    [Fact]
    public void GroupDeathLine_IsInTheRecipientsLanguage()
    {
        string hu = CombatEngine.GroupDeathLine("hu", "combat.group_succumbed_status", "Tester");
        hu.Should().Contain("Tester belehalt az állapothatásokba!").And.NotContain("succumbed");
        string it = CombatEngine.GroupDeathLine("it", "combat.group_fallen_dark", "Tester");
        it.Should().Contain(Loc.GetIn("it", "combat.group_fallen_dark", "Tester")).And.NotContain("fights on");
        CombatEngine.GroupDeathLine("en", "combat.group_succumbed_status", "Tester")
            .Should().Be("\u001b[1;31m  Tester has succumbed to status effects!\u001b[0m", "the colour and indent are unchanged");
    }

    [Fact]
    public void TheDeathBroadcasts_GoThroughTheLocalizedGroupSend()
    {
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Systems", "CombatEngine.cs"));
        foreach (var english in new[] { "has succumbed to status effects!", "has fallen to status effects!",
                     "has been consumed by dark powers!", "has fallen to dark powers!" })
            src.Should().NotContain(english);
        Regex.Matches(src, "BroadcastGroupDeathLine\\(result, \"combat\\.group_").Count.Should().Be(4);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj"))) dir = dir.Parent;
        return dir!.FullName;
    }
}
