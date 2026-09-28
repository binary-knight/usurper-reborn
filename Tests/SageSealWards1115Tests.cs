using System;
using System.Collections.Generic;
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
/// v1.1.15: the Old God seal bonus (5% a seal, at most 35%) reaches every Sage ward: the party wards,
/// Veloura's Embrace, a Sage teammate's wards (the leader's seals), a grouped online Sage's own casts
/// (their own seals), the PvP spell and the world boss spell. Every path goes through one helper.
/// </summary>
[Collection("SharedGameSingletons")]
public class SageSealWards1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int FogSlot = 1;
    private const int VelourasEmbraceSlot = 24;

    private static int StaffId() => EquipmentDatabase.GetAll().First(e => e.WeaponType == WeaponType.Staff).Id;

    private static Character Sage(int level, string name = "Sage") => new Character
    {
        Name1 = name, Name2 = name, Class = CharacterClass.Sage, Level = level,
        Wisdom = 50, Intelligence = 50, HP = 1000, MaxHP = 100_000, Mana = 100_000, MaxMana = 100_000,
        CombatSpeed = CombatSpeed.Instant,
        EquippedItems = new Dictionary<EquipmentSlot, int> { [EquipmentSlot.MainHand] = StaffId() },
    };

    private static Character Ally(string name, CharacterClass cls = CharacterClass.Ranger) => new Character
    {
        Name1 = name, Name2 = name, Class = cls, Level = 40, HP = 1000, MaxHP = 100_000,
        CombatSpeed = CombatSpeed.Instant,
    };

    private static void SetSeals(int seals)
    {
        var story = StoryProgressionSystem.Instance.CollectedSeals;
        story.Clear();
        foreach (var s in Enum.GetValues<SealType>().Take(seals)) story.Add(s);
    }

    private static CombatEngine Engine(Character current, List<Character> teammates, string input = "")
    {
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes(input)), new MemoryStream()));
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, new Random(7));
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, current);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, teammates);
        return engine;
    }

    private static SpellSystem.SpellResult Ward(int bonus) =>
        new SpellSystem.SpellResult { Success = true, ProtectionBonus = bonus, Duration = 999, SpecialEffect = "fog" };

    private static int WithSeven(int bonus) => bonus + bonus * 35 / 100;

    /// <summary>
    /// A real cast lands at one of a few strengths (a plain, great or critical roll). With no seals
    /// the path lands only at those; with seven every landed ward is off that set, and at least one is
    /// a plain strength plus 35%.
    /// </summary>
    private static void SevenSealsStrengthen(Func<int, int> landedWard)
    {
        var plain = Enumerable.Range(0, 30).Select(_ => landedWard(0)).ToHashSet();
        plain.Should().OnlyContain(w => w > 0);
        var sealed7 = Enumerable.Range(0, 10).Select(_ => landedWard(7)).ToList();
        sealed7.Should().OnlyContain(w => !plain.Contains(w), "seven seals strengthen every cast");
        sealed7.Should().Contain(w => plain.Select(WithSeven).Contains(w), "by 35%");
    }

    // ---- a Sage teammate: the leader's seals ----

    [Theory]
    [InlineData("companion")]
    [InlineData("npc")]
    [InlineData("echo")]
    public void ASageTeammatesWard_UsesTheLeadersSeals(string kind)
    {
        try
        {
            SetSeals(7);
            var leader = Ally("Leader");
            Character teammate = kind == "npc"
                ? new NPC { Name1 = "Teammate", Name2 = "Teammate", Class = CharacterClass.Sage, Level = 60, HP = 1000, MaxHP = 1000 }
                : Sage(60, "Teammate");
            if (kind == "companion") teammate.IsCompanion = true;
            if (kind == "echo") teammate.IsEcho = true;
            var tank = Ally("Tank", CharacterClass.Warrior);
            var engine = Engine(leader, new List<Character> { teammate, tank });
            engine.ApplySagePartyWard(teammate, Ward(100), new CombatResult { Player = leader });
            leader.MagicACBonus.Should().Be(135, "the leader's seven seals strengthen the teammate's ward");
            tank.MagicACBonus.Should().Be(135);
        }
        finally { SetSeals(0); }
    }

    [Fact]
    public void TheLeadersSeals_StillCapAtThirtyFive()
    {
        try
        {
            SetSeals(Enum.GetValues<SealType>().Length);
            var leader = Ally("Leader");
            var teammate = Sage(60, "Teammate");
            var engine = Engine(leader, new List<Character> { teammate });
            engine.ApplySagePartyWard(teammate, Ward(100), new CombatResult { Player = leader });
            leader.MagicACBonus.Should().Be(135);
        }
        finally { SetSeals(0); }
    }

    // ---- a grouped online Sage: their own seals ----

    [Fact]
    public void AGroupedSage_UsesTheirOwnSeals_NotTheLeaders()
    {
        var saved = CombatEngine.GroupedPlayerSealCount;
        try
        {
            SetSeals(7);                                        // the leader's session story
            CombatEngine.GroupedPlayerSealCount = c => c.GroupPlayerUsername == "follower" ? 2 : 0;
            var leader = Ally("Leader");
            var follower = Sage(60, "Follower");
            follower.RemoteTerminal = new TerminalEmulator(new MemoryStream(), new MemoryStream());
            follower.GroupPlayerUsername = "follower";
            // during a follower's turn currentPlayer is the follower
            var engine = Engine(follower, new List<Character> { follower });
            engine.ApplySagePartyWard(follower, Ward(100), new CombatResult { Player = leader });
            leader.MagicACBonus.Should().Be(110, "two seals of their own, not the leader's seven");
            follower.MagicACBonus.Should().Be(110);
        }
        finally { CombatEngine.GroupedPlayerSealCount = saved; SetSeals(0); }
    }

    // ---- Veloura's Embrace ----

    [Fact]
    public void VelourasEmbrace_FromThePlayer_TakesTheSealBonus()
    {
        try
        {
            SetSeals(7);
            var sage = Sage(100);
            var ally = Ally("Ally");
            var engine = Engine(sage, new List<Character> { ally });
            var heal = Ward(100);
            heal.Healing = 50;
            engine.WardPartyFromHeal(sage, heal, new CombatResult { Player = sage });
            ally.MagicACBonus.Should().Be(135);
            sage.MagicACBonus.Should().Be(135);
        }
        finally { SetSeals(0); }
    }

    [Fact]
    public void ANonSagesPartyHeal_TakesNoSealBonus()
    {
        try
        {
            SetSeals(7);
            var cleric = Ally("Cleric", CharacterClass.Cleric);
            var ally = Ally("Ally");
            var engine = Engine(cleric, new List<Character> { ally });
            engine.WardPartyFromHeal(cleric, Ward(100), new CombatResult { Player = cleric });
            ally.MagicACBonus.Should().Be(100, "the seals strengthen a Sage's wards only");
        }
        finally { SetSeals(0); }
    }

    /// <summary>The ward a Sage teammate's Veloura's Embrace puts on the leader, with this many seals.</summary>
    private static int TeammateVelouraWard(int seals)
    {
        SetSeals(seals);
        for (int attempt = 0; attempt < 100; attempt++)
        {
            var sage = Sage(100, "Teammate");
            sage.Spell[VelourasEmbraceSlot - 1][0] = true;
            var leader = Ally("Leader");
            var engine = Engine(leader, new List<Character> { sage });
            var result = new CombatResult { Player = leader };
            ((Task<bool>)typeof(CombatEngine).GetMethod("TeammateHealWithSpell", F)!
                .Invoke(engine, new object[] { sage, leader, result })!).GetAwaiter().GetResult();
            if (leader.MagicACBonus > 0) return leader.MagicACBonus;
        }
        throw new Exception("Veloura's Embrace never landed");
    }

    [Fact]
    public void VelourasEmbrace_FromASageTeammate_UsesTheLeadersSeals()
    {
        try
        {
            SevenSealsStrengthen(TeammateVelouraWard);
        }
        finally { SetSeals(0); }
    }

    /// <summary>The ward the player's own Veloura's Embrace puts on an ally through the spell menu.</summary>
    private static int PlayerVelouraWard(int seals)
    {
        SetSeals(seals);
        for (int attempt = 0; attempt < 100; attempt++)
        {
            var sage = Sage(100);
            var ally = Ally("Ally");
            var engine = Engine(sage, new List<Character> { ally });
            var action = new CombatAction { Type = CombatActionType.CastSpell, SpellIndex = VelourasEmbraceSlot, FromAidMenu = true };
            ((Task)typeof(CombatEngine).GetMethod("ExecuteSpellMultiMonster", F)!
                .Invoke(engine, new object[] { sage, new List<Monster>(), action, new CombatResult { Player = sage } })!).GetAwaiter().GetResult();
            if (ally.MagicACBonus > 0) return ally.MagicACBonus;
        }
        throw new Exception("Veloura's Embrace never landed");
    }

    [Fact]
    public void VelourasEmbrace_FromTheHealAllyMenuCast_TakesTheSealBonus()
    {
        try
        {
            SevenSealsStrengthen(PlayerVelouraWard);
        }
        finally { SetSeals(0); }
    }

    // ---- PvP ----

    private static int PvPFogWard(int seals)
    {
        SetSeals(seals);
        for (int attempt = 0; attempt < 100; attempt++)
        {
            var sage = Sage(100);
            sage.Spell[FogSlot - 1][0] = true;                  // Fog of War is the only spell known
            var foe = Ally("Foe");
            var engine = Engine(sage, new List<Character>());
            var action = new CombatAction { Type = CombatActionType.CastSpell, SpellIndex = FogSlot };
            ((Task)typeof(CombatEngine).GetMethod("ExecutePvPSpell", F)!
                .Invoke(engine, new object[] { sage, foe, action, new CombatResult { Player = sage } })!).GetAwaiter().GetResult();
            if (sage.MagicACBonus > 0) return sage.MagicACBonus;
        }
        throw new Exception("Fog of War never landed");
    }

    [Fact]
    public void ASagesPvPWard_TakesTheSealBonus()
    {
        try
        {
            SevenSealsStrengthen(PvPFogWard);
        }
        finally { SetSeals(0); }
    }

    // ---- the world boss ----

    private static int WorldBossFogWard(int seals)
    {
        SetSeals(seals);
        var method = typeof(WorldBossSystem).GetMethod("ProcessSpellCast", F)!;
        for (int attempt = 0; attempt < 100; attempt++)
        {
            var sage = Sage(100);
            sage.Spell[FogSlot - 1][0] = true;                  // Fog of War is the only spell known
            var term = new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes("1\n")), new MemoryStream());
            ((Task<long>)method.Invoke(WorldBossSystem.Instance, new object?[] { sage, term, null, null, new Random(attempt) })!)
                .GetAwaiter().GetResult();
            if (sage.TempDefenseBonus > 0) return sage.TempDefenseBonus;
        }
        throw new Exception("Fog of War never landed");
    }

    [Fact]
    public void ASagesWorldBossWard_TakesTheSealBonus()
    {
        try
        {
            SevenSealsStrengthen(WorldBossFogWard);
        }
        finally { SetSeals(0); }
    }

    // ---- one helper for every path ----

    /// <summary>
    /// Every method outside SpellSystem.cs that reads a spell's ProtectionBonus must pass it through
    /// the seal helper, or hand it to one of the two Sage ward routes that do, or be listed here as a
    /// path no Sage ward reaches.
    /// </summary>
    private static readonly Dictionary<string, string> NotASageWardPath = new()
    {
        // Single-target heals only: a Sage has none, and the Heal Ally menu turns Veloura's Embrace
        // into a CastSpell action that ExecuteSpellMultiMonster wards through WardPartyFromHeal.
        ["HandleHealAlly"] = "single-target heal",
        // Self or single-target buffs; the caster argument can be the ally (ApplyBuffTo), so a seal
        // bonus here would reach a Sage ally warded by someone else. Every Sage ward is a party spell.
        ["ApplySpellEffects"] = "self or single-target buff",
    };

    private static readonly string[] Routes = { "SageWardStrength(", "SageWardWithSeals(", "ApplySagePartyWard(", "WardPartyFromHeal(" };

    private static readonly Regex Member = new(@"^\s*(?:(?:private|internal|public|protected|static|async|override|virtual)\s+)+[^=;(]*?\b(\w+)\s*(?:<[^>]*>)?\s*\(", RegexOptions.Compiled);

    /// <summary>The name and body of the member that encloses line i.</summary>
    private static (string name, string body) Enclosing(string[] lines, int i)
    {
        int start = i;
        while (start >= 0 && !Member.IsMatch(lines[start])) start--;
        if (start < 0) return ("", "");
        string name = Member.Match(lines[start]).Groups[1].Value;
        int depth = 0; bool opened = false;
        var body = new StringBuilder();
        for (int j = start; j < lines.Length; j++)
        {
            body.AppendLine(lines[j]);
            foreach (char ch in lines[j])
            {
                if (ch == '{') { depth++; opened = true; }
                else if (ch == '}') depth--;
            }
            if (opened && depth <= 0) break;
            if (!opened && lines[j].TrimEnd().EndsWith(";")) break; // expression-bodied member
        }
        return (name, body.ToString());
    }

    [Fact]
    public void EverySageWardPath_GoesThroughTheSealHelper()
    {
        string root = Path.Combine(Leftovers1114BTests.RepoRoot(), "Scripts");
        var offenders = new List<string>();
        var seen = new HashSet<string>();
        foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file) == "SpellSystem.cs") continue;
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains(".ProtectionBonus")) continue;
                var (name, body) = Enclosing(lines, i);
                seen.Add(name);
                if (NotASageWardPath.ContainsKey(name)) continue;
                if (!Routes.Any(r => body.Contains(r)))
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1} {name}");
            }
        }
        offenders.Distinct().Should().BeEmpty("a new path that applies a spell's ward must go through the Sage seal helper");
        seen.Should().Contain(new[] { "ApplySagePartyWard", "WardPartyFromHeal", "ExecutePvPSpell", "ProcessSpellCast" });
    }

    [Theory]
    [InlineData("ApplySagePartyWard")]
    [InlineData("WardPartyFromHeal")]
    [InlineData("ExecutePvPSpell")]
    public void TheSageWardRoutes_CallTheHelperThemselves(string method)
    {
        var lines = File.ReadAllLines(Path.Combine(Leftovers1114BTests.RepoRoot(), "Scripts", "Systems", "CombatEngine.cs"));
        var bodies = Enumerable.Range(0, lines.Length)
            .Where(i => Member.IsMatch(lines[i]) && Member.Match(lines[i]).Groups[1].Value == method)
            .Select(i => Enclosing(lines, i).body).ToList();
        bodies.Should().NotBeEmpty();
        bodies.Where(b => b.Contains(".ProtectionBonus")).Should().NotBeEmpty();
        bodies.Where(b => b.Contains(".ProtectionBonus")).Should().OnlyContain(b => b.Contains("SageWardStrength("), method);
    }
}
