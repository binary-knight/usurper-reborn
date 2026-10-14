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
using UsurperRemake.Data;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.2 combat fixes.
/// Fault 3: the difficulty potion multiplier (Easy 1.25, Normal and Hard 1.0, Nightmare 0.75) reached only the
/// auto-combat Heal and Quick Heal. It now applies, through DifficultySystem.ApplyHealingMultiplier after the base and
/// before the owner bonuses and the cap, to every potion the player or the party drinks, whatever the base, and to
/// every potion estimate. A computer opponent's potion is unchanged.
/// Fault 5: a Mystic Shaman has mana (IsManaClass) but no spell menu (HasSpells false). Its round end mana
/// regeneration, as leader or party member, and its auto mana potion now follow IsManaClass.
/// The heals here use the scaled floor: level 1 and 10,000 MaxHP make every BaseHeal 2,000 whatever the roll, so
/// the paths that draw from Random.Shared are exact too.
/// </summary>
[Collection("SharedGameSingletons")]
public class CombatFixes132Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags FS = BindingFlags.NonPublic | BindingFlags.Static;

    private const long Max = 10_000;
    private const long Base = 2_000;            // 20 percent of 10,000; the flat heal at level 1 is at most 64
    private const long Quarter = Max / 4;       // the MaxHP / 4 potions
    private const long WorldBoss = 3_000;       // the world boss potion, MaxHP x 0.3

    public static IEnumerable<object[]> Modes() => new[]
    {
        new object[] { DifficultyMode.Easy, 1.25 },
        new object[] { DifficultyMode.Normal, 1.0 },
        new object[] { DifficultyMode.Nightmare, 0.75 },
    };

    private static long Expected(long baseHeal, double mult) => (long)Math.Round(baseHeal * mult);

    private static Character Drinker(string name = "Drinker", int potions = 1) => new Character
    {
        Name1 = name, Name2 = name, Class = CharacterClass.Warrior, Race = CharacterRace.Human, AI = CharacterAI.Human,
        Level = 1, HP = 1, MaxHP = Max, Healing = potions, ManaPotions = 0, CombatSpeed = CombatSpeed.Instant,
    };

    private static async Task<T> OnDifficulty<T>(DifficultyMode mode, Func<Task<T>> body)
    {
        var savedMode = DifficultySystem.CurrentDifficulty;
        bool savedScaled = GameConfig.ScaledPotionsEnabled;
        try
        {
            DifficultySystem.CurrentDifficulty = mode;
            GameConfig.ScaledPotionsEnabled = true;
            return await body();
        }
        finally
        {
            DifficultySystem.CurrentDifficulty = savedMode;
            GameConfig.ScaledPotionsEnabled = savedScaled;
        }
    }

    private static (TerminalEmulator term, MemoryStream output) Term(params string[] lines)
    {
        var output = new MemoryStream();
        return (new TerminalEmulator(new LineStream(lines.Concat(Enumerable.Repeat("", 20))), output), output);
    }

    private static string Text(TerminalEmulator term, MemoryStream output)
    {
        term.StreamWriterInternal?.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "").Replace("\r", "");
    }

    private static CombatEngine Engine(Character current, TerminalEmulator term, params Character[] teammates)
    {
        var engine = new CombatEngine(term);
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, new Random(7));
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, current);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, new List<Character>(teammates));
        return engine;
    }

    private static async Task Call(object target, Type type, string method, params object[] args) =>
        await (Task)type.GetMethod(method, F)!.Invoke(target, args)!;

    private static T At<T>(T location, Character player, TerminalEmulator term) where T : BaseLocation
    {
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(location, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(location, player);
        return location;
    }

    // ---------------- Fault 3: every player and party potion ----------------

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task QuickHeal_AndHeal_KeepTheMultiplier(DifficultyMode mode, double mult)
    {
        long quick = await OnDifficulty(mode, async () =>
        {
            var p = Drinker();
            var (term, _) = Term();
            await Call(Engine(p, term), typeof(CombatEngine), "ExecuteHeal", p, new CombatResult(), true);
            p.Healing.Should().Be(0);
            return p.HP - 1;
        });
        quick.Should().Be(Expected(Base, mult));

        long heal = await OnDifficulty(mode, async () =>
        {
            var p = Drinker();
            var (term, _) = Term("1");
            await Call(Engine(p, term), typeof(CombatEngine), "ExecuteHeal", p, new CombatResult(), false);
            p.Healing.Should().Be(0);
            return p.HP - 1;
        });
        heal.Should().Be(Expected(Base, mult));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task UseItemPotion_GetsTheMultiplier(DifficultyMode mode, double mult)
    {
        long healed = await OnDifficulty(mode, async () =>
        {
            var p = Drinker();
            var (term, _) = Term();
            await Call(Engine(p, term), typeof(CombatEngine), "ExecuteUseItem", p, new CombatResult());
            p.Healing.Should().Be(0);
            return p.HP - 1;
        });
        healed.Should().Be(Expected(Base, mult));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task AutoHealAfterAFight_GetsTheMultiplier(DifficultyMode mode, double mult)
    {
        long healed = await OnDifficulty(mode, () =>
        {
            var p = Drinker();
            p.AutoHeal = true;
            var (term, _) = Term();
            typeof(CombatEngine).GetMethod("AutoHealWithPotions", F)!.Invoke(Engine(p, term), new object[] { p });
            p.Healing.Should().Be(0);
            return Task.FromResult(p.HP - 1);
        });
        healed.Should().Be(Expected(Base, mult));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task APotionGivenToAnAlly_GetsTheMultiplier(DifficultyMode mode, double mult)
    {
        long healed = await OnDifficulty(mode, async () =>
        {
            var p = Drinker();
            var ally = Drinker("Ally", 0);
            var (term, _) = Term("1", "1", "1");
            var engine = Engine(p, term, ally);
            await (Task<CombatAction?>)typeof(CombatEngine).GetMethod("HandleHealAlly", F)!
                .Invoke(engine, new object[] { p, new List<Monster>() })!;
            p.Healing.Should().Be(0);
            return ally.HP - 1;
        });
        healed.Should().Be(Expected(Base, mult));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task ATeammatesOwnPotion_AndOneFromTheBelt_GetTheMultiplier(DifficultyMode mode, double mult)
    {
        long own = await OnDifficulty(mode, async () =>
        {
            var p = Drinker("Leader", 0);
            var mate = Drinker("Mate");
            var (term, _) = Term();
            await (Task<bool>)typeof(CombatEngine).GetMethod("TeammateHealWithPotion", F)!
                .Invoke(Engine(p, term, mate), new object[] { mate, mate, new CombatResult(), false })!;
            mate.Healing.Should().Be(0);
            return mate.HP - 1;
        });
        own.Should().Be(Expected(Base, mult));

        long belt = await OnDifficulty(mode, async () =>
        {
            var p = Drinker("Leader", 5);
            p.HP = p.MaxHP;
            p.SharedPotionBelt = true;
            var mate = Drinker("Mate", 0);
            var (term, _) = Term();
            var engine = Engine(p, term, mate);
            bool drank = await (Task<bool>)typeof(CombatEngine).GetMethod("TeammateHealWithPotion", F)!
                .Invoke(engine, new object[] { mate, mate, new CombatResult(), true })!;
            drank.Should().BeTrue("the belt lends a potion here");
            p.Healing.Should().Be(4);
            return mate.HP - 1;
        });
        belt.Should().Be(Expected(Base, mult));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task TownQuickPotion_GetsTheMultiplier(DifficultyMode mode, double mult)
    {
        long healed = await OnDifficulty(mode, async () =>
        {
            var p = Drinker();
            var (term, _) = Term();
            await Call(At(new DungeonLocation(), p, term), typeof(BaseLocation), "UseQuickPotion");
            p.Healing.Should().Be(0);
            return p.HP - 1;
        });
        healed.Should().Be(Expected(Base, mult));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task DungeonPotionsOnTheParty_GetTheMultiplier(DifficultyMode mode, double mult)
    {
        long toMate = await OnDifficulty(mode, async () =>
        {
            var p = Drinker();
            p.HP = p.MaxHP;
            var mate = Drinker("Mate", 0);
            var (term, _) = Term("1", "1");
            await Call(At(new DungeonLocation(), p, term), typeof(DungeonLocation), "HealTeammate", p, new List<Character> { mate });
            p.Healing.Should().Be(0);
            return mate.HP - 1;
        });
        toMate.Should().Be(Expected(Base, mult));

        var (self, companion) = await OnDifficulty(mode, async () =>
        {
            // the player alone, one potion
            var p = Drinker("Leader", 1);
            var (term, _) = Term("Y");
            await Call(At(new DungeonLocation(), p, term), typeof(DungeonLocation), "HealEntireParty", p, new List<Character>());
            long selfHeal = p.HP - 1;

            // a full player gives the one potion to a companion
            var mate = Drinker("Mate", 0);
            var giver = Drinker("Giver", 1);
            giver.HP = giver.MaxHP;
            var (term2, _) = Term("Y");
            await Call(At(new DungeonLocation(), giver, term2), typeof(DungeonLocation), "HealEntireParty", giver, new List<Character> { mate });
            return (selfHeal, mate.HP - 1);
        });
        self.Should().Be(Expected(Base, mult));
        companion.Should().Be(Expected(Base, mult));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task AFollowersPotion_GetsTheMultiplier(DifficultyMode mode, double mult)
    {
        long healed = await OnDifficulty(mode, async () =>
        {
            var p = Drinker();
            var (term, _) = Term();
            await (Task)typeof(DungeonLocation).GetMethod("UseFollowerPotion", FS)!.Invoke(null, new object[] { p, term })!;
            p.Healing.Should().Be(0);
            return p.HP - 1;
        });
        healed.Should().Be(Expected(Base, mult));
    }

    // The four MaxHP based potions.

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task DungeonUseHealingPotion_AndHealToFull_GetTheMultiplier(DifficultyMode mode, double mult)
    {
        long one = await OnDifficulty(mode, async () =>
        {
            var p = Drinker();
            var (term, _) = Term();
            await (Task)typeof(DungeonLocation).GetMethod("UseHealingPotion", F, null, new[] { typeof(Character) }, null)!
                .Invoke(At(new DungeonLocation(), p, term), new object[] { p })!;
            p.Healing.Should().Be(0);
            return p.HP - 1;
        });
        one.Should().Be(Expected(Quarter, mult));

        long full = await OnDifficulty(mode, async () =>
        {
            var p = Drinker();
            var (term, _) = Term("Y");
            await Call(At(new DungeonLocation(), p, term), typeof(DungeonLocation), "HealToFull", p);
            p.Healing.Should().Be(0);
            return p.HP - 1;
        });
        full.Should().Be(Expected(Quarter, mult));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task HomePotion_GetsTheMultiplier(DifficultyMode mode, double mult)
    {
        long healed = await OnDifficulty(mode, async () =>
        {
            var p = Drinker();
            var (term, _) = Term();
            await Call(At(new HomeLocation(), p, term), typeof(HomeLocation), "UseHealingPotion");
            p.Healing.Should().Be(0);
            return p.HP - 1;
        });
        healed.Should().Be(Expected(Quarter, mult));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task WorldBossPotion_GetsTheMultiplier(DifficultyMode mode, double mult)
    {
        long healed = await OnDifficulty(mode, async () =>
        {
            var p = Drinker();
            var (term, _) = Term("1");
            var boss = WorldBossDatabase.GetBossById("abyssal_leviathan")!;
            await (Task)typeof(WorldBossSystem).GetMethod("DrinkHealingPotions", F)!
                .Invoke(new WorldBossSystem(), new object[] { p, term, boss })!;
            p.Healing.Should().Be(0);
            return p.HP - 1;
        });
        healed.Should().Be(Expected(WorldBoss, mult));
    }

    // The estimates match the heal.

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task TheHowManyEstimate_CountsTheMultipliedHeal(DifficultyMode mode, double mult)
    {
        string shown = await OnDifficulty(mode, async () =>
        {
            var p = Drinker(potions: 10);
            var (term, output) = Term("1");
            await Call(Engine(p, term), typeof(CombatEngine), "ExecuteHeal", p, new CombatResult(), false);
            p.HP.Should().Be(1 + Expected(Base, mult), "one potion drunk");
            return Text(term, output);
        });
        int count = (int)Math.Ceiling((Max - 1) / (double)Expected(Base, mult));   // Easy 4, Normal 5, Nightmare 7
        shown.Should().Contain(Loc.Get("combat.potions_missing_hp", Max - 1, count));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task TheDungeonEachPotionHeals_ShowsTheMultipliedHeal(DifficultyMode mode, double mult)
    {
        string shown = await OnDifficulty(mode, async () =>
        {
            var p = Drinker();
            p.HP = p.MaxHP;
            var mate = Drinker("Mate", 0);
            var (term, output) = Term("1", "1");
            await Call(At(new DungeonLocation(), p, term), typeof(DungeonLocation), "HealTeammate", p, new List<Character> { mate });
            (mate.HP - 1).Should().Be(Expected(Base, mult));
            return Text(term, output);
        });
        shown.Should().Contain(Loc.Get("dungeon.each_potion_heals", Expected(Base, mult)));
    }

    // A computer opponent's potion is unchanged (pin).

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task AComputerOpponentsPotion_IsTheSameOnEveryDifficulty(DifficultyMode mode, double mult)
    {
        _ = mult;
        long healed = await OnDifficulty(mode, async () =>
        {
            var computer = Drinker("Computer");
            computer.AI = CharacterAI.Computer;
            var opponent = Drinker("Opponent", 0);
            opponent.HP = opponent.MaxHP;
            var (term, _) = Term();
            await Call(Engine(opponent, term), typeof(CombatEngine), "ProcessComputerPlayerAction", computer, opponent, new CombatResult());
            computer.Healing.Should().Be(0);
            return computer.HP - 1;
        });
        healed.Should().Be(Base);
    }

    // A new potion path cannot miss the multiplier: every method that applies the owner bonuses applies the
    // multiplier as often, except the computer opponent's.

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test must run inside the repository");
        return dir!.FullName;
    }

    private static readonly Regex MethodSignature = new(@"^\s*(?:public|private|internal|protected)\b[^=;]*?(\w+)\s*(?:<[^>]*>)?\s*\(");

    [Fact]
    public void EveryMethodWithTheOwnerBonuses_AppliesTheMultiplier_ExceptTheComputerOpponent()
    {
        var seen = new List<string>();
        var problems = new List<string>();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(RepoRoot(), "Scripts"), "*.cs", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(path) == "PotionBonus.cs") continue;
            var lines = File.ReadAllLines(path);
            string method = "?";
            var counts = new Dictionary<string, (int bonus, int mult)>();
            foreach (var line in lines)
            {
                var m = MethodSignature.Match(line);
                if (m.Success) method = m.Groups[1].Value;
                string code = line.TrimStart();
                if (code.StartsWith("//")) continue;
                int b = Regex.Matches(line, Regex.Escape("PotionBonus.ApplyOwnerBonuses(")).Count;
                int d = Regex.Matches(line, Regex.Escape("DifficultySystem.ApplyHealingMultiplier(")).Count;
                if (b == 0 && d == 0) continue;
                counts.TryGetValue(method, out var c);
                counts[method] = (c.bonus + b, c.mult + d);
            }
            string file = Path.GetFileNameWithoutExtension(path);
            foreach (var (name, (bonus, mult)) in counts)
            {
                if (bonus == 0) continue;
                seen.Add($"{file}.{name}");
                int want = $"{file}.{name}" == "CombatEngine.ProcessComputerPlayerAction" ? 0 : bonus;
                if (mult != want) problems.Add($"{file}.{name}: {bonus} owner bonus calls, {mult} multiplier calls, want {want}");
            }
        }
        seen.Should().Contain(new[]
        {
            "CombatEngine.ExecuteHeal", "CombatEngine.ExecuteUseItem", "CombatEngine.AutoHealWithPotions", "CombatEngine.HandleHealAlly",
            "CombatEngine.TeammateHealWithPotion", "CombatEngine.ProcessComputerPlayerAction", "BaseLocation.UseQuickPotion",
            "HomeLocation.UseHealingPotion", "DungeonLocation.UsePotions", "DungeonLocation.HealTeammate", "DungeonLocation.HealEntireParty",
            "DungeonLocation.UseHealingPotion", "DungeonLocation.HealToFull", "DungeonLocation.UseFollowerPotion",
            "WorldBossSystem.DrinkHealingPotions",
        });
        problems.Should().BeEmpty();
    }

    // ---------------- Fault 5: Mystic Shaman mana ----------------

    // Wisdom 50: 2 + 50/10 = 7 mana a round.
    private static Character Caster(CharacterClass cls, string name) => new Character
    {
        Name1 = name, Name2 = name, Class = cls, Race = CharacterRace.Human, AI = CharacterAI.Human,
        Level = 10, HP = 200, MaxHP = 200, Stamina = 30, Wisdom = 50,
        CurrentCombatStamina = 20, Mana = 10, MaxMana = 100,
    };

    private static void RoundEnd(CombatEngine engine, Character leader) =>
        typeof(CombatEngine).GetMethod("ProcessEndOfRoundAbilityEffects", F)!.Invoke(engine, new object[] { leader });

    private static CombatEngine Quiet(Character current, params Character[] teammates)
    {
        var (term, _) = Term();
        return Engine(current, term, teammates);
    }

    [Fact]
    public void AMysticShaman_RegainsManaAtRoundEnd_AsLeader()
    {
        var shaman = Caster(CharacterClass.MysticShaman, "Shaman");
        RoundEnd(Quiet(shaman), shaman);
        shaman.Mana.Should().Be(17, "2 + 50/10");
    }

    [Fact]
    public void AMysticShaman_RegainsManaAtRoundEnd_AsAPartyMember()
    {
        var leader = Caster(CharacterClass.Warrior, "Leader");
        var shaman = Caster(CharacterClass.MysticShaman, "Shaman");
        RoundEnd(Quiet(leader, shaman), leader);
        shaman.Mana.Should().Be(17, "2 + 50/10");
    }

    [Fact]
    public void AWarrior_RegainsNoMana_AsLeaderOrPartyMember()
    {
        var leader = Caster(CharacterClass.Warrior, "Leader");
        var mate = Caster(CharacterClass.Warrior, "Mate");
        RoundEnd(Quiet(leader, mate), leader);
        leader.Mana.Should().Be(10);
        mate.Mana.Should().Be(10);
    }

    [Fact]
    public void AMysticShaman_StillHasNoSpellMenu()
    {
        var shaman = Caster(CharacterClass.MysticShaman, "Shaman");
        shaman.IsManaClass.Should().BeTrue();
        SpellSystem.HasSpells(shaman).Should().BeFalse();
    }

    private static Character ManaDrinker(CharacterClass cls)
    {
        var c = Caster(cls, cls.ToString());
        c.AutoHeal = true;
        c.Mana = 0;
        c.MaxMana = 500;
        c.ManaPotions = 1;
        return c;
    }

    [Fact]
    public void AMysticShaman_AutoDrinksAManaPotion()
    {
        var shaman = ManaDrinker(CharacterClass.MysticShaman);
        typeof(CombatEngine).GetMethod("AutoRestoreManaWithPotions", F)!.Invoke(Quiet(shaman), new object[] { shaman });
        shaman.ManaPotions.Should().Be(0);
        shaman.Mana.Should().Be(80, "30 + 5 x level 10");
    }

    [Fact]
    public void AWarrior_DoesNotAutoDrinkAManaPotion()
    {
        var warrior = ManaDrinker(CharacterClass.Warrior);
        typeof(CombatEngine).GetMethod("AutoRestoreManaWithPotions", F)!.Invoke(Quiet(warrior), new object[] { warrior });
        warrior.ManaPotions.Should().Be(1);
        warrior.Mana.Should().Be(0);
    }
}
