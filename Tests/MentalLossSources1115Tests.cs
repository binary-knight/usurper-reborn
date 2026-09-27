using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.15 Mental piece 4: loss sources. New dungeon rooms, monster fight end (strain, flee, near
/// death, boss, Old God) as one net change and one announcement, death, the PvP exemption, the
/// story-companion cut and the grouped-follower share.
/// </summary>
[Collection("SharedGameSingletons")]
public class MentalLossSources1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        return dir!.FullName;
    }

    private static Character Hero(string name = "Hero", int mental = 100, long hp = 100, long maxHp = 100) => new Character
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Human, Class = CharacterClass.Warrior, Race = CharacterRace.Human,
        Mental = mental, HP = hp, MaxHP = maxHp, MentalHintShown = true,
    };

    private static TerminalEmulator Term(MemoryStream output) => new TerminalEmulator(new LineStream(Array.Empty<string>()), output);

    private static string Text(MemoryStream output) =>
        Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");

    private static Character Follower(string name, int mental = 100)
    {
        var f = Hero(name, mental);
        f.RemoteTerminal = Term(new MemoryStream());   // a grouped human player
        return f;
    }

    private static Character Companion()
    {
        var c = Hero("Lyris");
        c.IsCompanion = true;
        c.CompanionId = CompanionId.Lyris;
        return c;
    }

    private static Character NpcMate()
    {
        var n = Hero("Hired Blade");
        n.AI = CharacterAI.Computer;
        return n;
    }

    private static CombatResult Fight(Character leader, List<Character>? mates = null, params Monster[] monsters) => new CombatResult
    {
        Player = leader,
        Teammates = mates ?? new List<Character>(),
        Monsters = monsters.ToList(),
    };

    private static Monster Rat() => new Monster { Name = "Rat", Level = 1, HP = 0, MaxHP = 1 };

    // Pure rules

    [Theory]
    [InlineData(15, true)]
    [InlineData(16, false)]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void Near_death_is_at_or_below_15_percent_and_alive(long hp, bool nearDeath) =>
        MentalSystem.IsNearDeath(Hero(hp: hp, maxHp: 100)).Should().Be(nearDeath);

    [Fact]
    public void Old_god_replaces_the_boss_loss()
    {
        MentalSystem.GetFightEndFlatLoss(false, false, true, false).Should().Be(3);
        MentalSystem.GetFightEndFlatLoss(false, false, false, true).Should().Be(8);
        MentalSystem.GetFightEndFlatLoss(false, false, true, true).Should().Be(8);
        MentalSystem.GetFightEndFlatLoss(true, true, true, false).Should().Be(9);
    }

    [Fact]
    public void Dead_companions_and_non_story_allies_do_not_count()
    {
        var dead = Companion(); dead.HP = 0;
        var party = new List<Character> { Companion(), dead, NpcMate(), Follower("Ally") };
        MentalSystem.CountStoryCompanions(party).Should().Be(1);
    }

    // Room strain

    [Fact]
    public void A_new_room_strains_floor_times_four_per_mille()
    {
        var (d, _, _) = DungeonDangerLabel1114Tests.Rig(new DungeonRoom { Id = "r1", Name = "Hall" });
        var hero = (Character)typeof(BaseLocation).GetField("currentPlayer", F)!.GetValue(d)!;
        typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.SetValue(d, 250);   // 1000 per mille
        d.ApplyRoomMentalStrain();
        hero.Mental.Should().Be(99);
        hero.MentalStrainRemainder.Should().Be(0);

        typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.SetValue(d, 6);     // 24 per mille
        d.ApplyRoomMentalStrain();
        hero.Mental.Should().Be(99);
        hero.MentalStrainRemainder.Should().Be(2_400);
    }

    [Fact]
    public void Room_strain_is_shared_by_grouped_followers_only()
    {
        var (d, _, _) = DungeonDangerLabel1114Tests.Rig(new DungeonRoom { Id = "r1", Name = "Hall" });
        typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.SetValue(d, 250);
        var follower = Follower("Ally");
        var npc = NpcMate();
        d.teammates.AddRange(new[] { follower, npc });
        d.ApplyRoomMentalStrain();
        follower.Mental.Should().Be(99);
        npc.Mental.Should().Be(100);
        npc.MentalStrainRemainder.Should().Be(0);
    }

    [Fact]
    public void A_story_companion_in_the_party_cuts_room_strain()
    {
        var (d, _, _) = DungeonDangerLabel1114Tests.Rig(new DungeonRoom { Id = "r1", Name = "Hall" });
        var hero = (Character)typeof(BaseLocation).GetField("currentPlayer", F)!.GetValue(d)!;
        typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.SetValue(d, 250);
        d.teammates.Add(Companion());
        d.ApplyRoomMentalStrain();
        hero.Mental.Should().Be(100);
        hero.MentalStrainRemainder.Should().Be(90_000);
    }

    [Fact]
    public void Entering_an_unexplored_room_applies_the_room_strain()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Locations", "DungeonLocation.cs"));
        int move = src.IndexOf("private async Task MoveToRoom(", StringComparison.Ordinal);
        int block = src.IndexOf("if (!targetRoom.IsExplored)\n", move, StringComparison.Ordinal);
        if (block < 0) block = src.IndexOf("if (!targetRoom.IsExplored)\r\n", move, StringComparison.Ordinal);
        int end = src.IndexOf("TryDiscoverSeal", block, StringComparison.Ordinal);
        move.Should().BeGreaterThan(0);
        block.Should().BeGreaterThan(move);
        src.Substring(block, end - block).Should().Contain("ApplyRoomMentalStrain();");
    }

    // Fight end

    [Fact]
    public void Fight_end_losses_stack_into_one_net_change_and_one_line()
    {
        var output = new MemoryStream();
        var hero = Hero(mental: 80, hp: 10, maxHp: 100);   // near death
        var boss = new Monster { Name = "Warden", Level = 1, HP = 0, MaxHP = 1, IsBoss = true };
        CombatEngine.ApplyMentalFightEnd(Fight(hero, null, boss), 100, false, false, Term(output), 80);
        // 100 x 12 = 1200 per mille: 1 point and 200 carried; near death 4; boss 3
        hero.Mental.Should().Be(72);
        hero.MentalStrainRemainder.Should().Be(20_000);
        Regex.Matches(Text(output), "Mental Health worsens").Count.Should().Be(1);
    }

    [Fact]
    public void Fight_end_outside_the_dungeon_has_no_strain()
    {
        var hero = Hero();
        CombatEngine.ApplyMentalFightEnd(Fight(hero, null, Rat()), 0, false, false, null, 100);
        hero.Mental.Should().Be(100);
        hero.MentalStrainRemainder.Should().Be(0);
    }

    [Fact]
    public void Fight_end_strain_uses_the_race_and_class_multiplier()
    {
        var hero = Hero();
        hero.Race = CharacterRace.Elf; hero.Class = CharacterClass.Bard;   // 132
        CombatEngine.ApplyMentalFightEnd(Fight(hero, null, Rat()), 100, false, false, null, 100);
        hero.Mental.Should().Be(99);
        hero.MentalStrainRemainder.Should().Be(58_400);   // 1200 x 132 = 158_400
    }

    [Fact]
    public void Fight_end_old_god_costs_eight()
    {
        var hero = Hero();
        CombatEngine.ApplyMentalFightEnd(Fight(hero, null, new Monster { Name = "God", HP = 0, MaxHP = 1, IsBoss = true }), 0, false, true, null, 100);
        hero.Mental.Should().Be(92);
    }

    [Fact]
    public void A_leader_who_died_is_not_charged_flee_or_near_death_again()
    {
        var hero = Hero(hp: 1, maxHp: 100);
        var result = Fight(hero, null, Rat());
        result.PlayerActuallyDied = true;
        CombatEngine.ApplyMentalFightEnd(result, 0, true, false, null, 100);
        hero.Mental.Should().Be(100);
    }

    [Fact]
    public void Pvp_results_are_exempt()
    {
        var hero = Hero(hp: 5, maxHp: 100);
        var result = Fight(hero, null, Rat());
        result.Opponent = Hero("Rival");
        CombatEngine.ApplyMentalFightEnd(result, 100, true, true, null, 100);
        hero.Mental.Should().Be(100);
        hero.MentalStrainRemainder.Should().Be(0);
    }

    [Fact]
    public void Grouped_followers_share_the_fight_end_loss_and_npcs_and_companions_do_not()
    {
        var hero = Hero();
        var follower = Follower("Ally", 90);
        var deadFollower = Follower("Fallen", 90); deadFollower.HP = 0;
        var companion = Companion();
        var npc = NpcMate();
        var mates = new List<Character> { follower, deadFollower, companion, npc };
        CombatEngine.ApplyMentalFightEnd(Fight(hero, mates, Rat()), 0, true, false, null, 100);
        hero.Mental.Should().Be(98);
        follower.Mental.Should().Be(88);
        deadFollower.Mental.Should().Be(90);
        companion.Mental.Should().Be(100);
        npc.Mental.Should().Be(100);
    }

    [Fact]
    public void A_follower_near_death_takes_its_own_near_death_loss()
    {
        var hero = Hero();
        var follower = Follower("Ally");
        follower.HP = 10;
        CombatEngine.ApplyMentalFightEnd(Fight(hero, new List<Character> { follower }, Rat()), 0, false, false, null, 100);
        hero.Mental.Should().Be(100);
        follower.Mental.Should().Be(96);
    }

    // End to end through real combat (scripted input, out of the dungeon so no strain)

    private sealed class ScriptedStream : Stream
    {
        private readonly byte[] _data; private int _pos;
        public ScriptedStream(string script) { _data = Encoding.UTF8.GetBytes(script); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos >= _data.Length) throw new IOException("script drained");
            int n = Math.Min(count, _data.Length - _pos);
            Array.Copy(_data, _pos, buffer, offset, n); _pos += n; return n;
        }
        public override Task<int> ReadAsync(byte[] b, int o, int c, CancellationToken ct) => Task.FromResult(Read(b, o, c));
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    private static (CombatEngine engine, MemoryStream output) Engine(string script)
    {
        var output = new MemoryStream();
        var engine = new CombatEngine(new TerminalEmulator(new ScriptedStream(script), output));
        engine.SeedRandomForTests(1115);
        return (engine, output);
    }

    private static Character Fighter(string name, long hp, long maxHp) => new Character
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Human, Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 10,
        HP = hp, MaxHP = maxHp, BaseMaxHP = maxHp, Strength = 80, BaseStrength = 80, Defence = 40, BaseDefence = 40,
        Dexterity = 30, BaseDexterity = 30, Agility = 25, BaseAgility = 25, Constitution = 30, BaseConstitution = 30,
        Stamina = 100, Gold = 100, CombatSpeed = CombatSpeed.Instant, MentalHintShown = true,
    };

    private static string Tail(MemoryStream output) { var t = Text(output); return t.Length > 1500 ? t[^1500..] : t; }

    [Fact]
    public async Task Fleeing_a_monster_fight_costs_two()
    {
        var (engine, output) = Engine(string.Concat(Enumerable.Repeat("R\n", 8)));
        var hero = Fighter("Runner", 500, 500);
        hero.SmokeBombs = 1;
        var dummy = new Monster { Name = "Training Dummy", Level = 1, HP = 5_000_000, MaxHP = 5_000_000, Strength = 0, Defence = 0, Experience = 1 };
        var result = await engine.PlayerVsMonsters(hero, new List<Monster> { dummy }, offerMonkEncounter: false);
        result.Outcome.Should().Be(CombatOutcome.PlayerEscaped, Tail(output));
        hero.Mental.Should().Be(98, Tail(output));
    }

    [Fact]
    public async Task Dying_in_a_monster_fight_costs_twelve_once()
    {
        var (engine, output) = Engine(string.Concat(Enumerable.Repeat("A\n", 6)) + "\n\n\n1\n" + string.Concat(Enumerable.Repeat("\n", 12)));
        var hero = Fighter("Doomed", 5, 500);   // under 25% at round start: no Last Stand or Death's Door
        var doom = new Monster { Name = "Doom Engine", Level = 60, HP = 5_000_000, MaxHP = 5_000_000, Strength = 5_000_000, Defence = 0, Experience = 1 };
        var difficulty = DifficultySystem.CurrentDifficulty;
        DifficultySystem.CurrentDifficulty = DifficultyMode.Normal;
        CombatResult? result = null;
        try { result = await engine.PlayerVsMonsters(hero, new List<Monster> { doom }, offerMonkEncounter: false); }
        catch (IOException) { }
        finally { DifficultySystem.CurrentDifficulty = difficulty; }
        hero.Mental.Should().Be(88, Tail(output));
        result?.PlayerActuallyDied.Should().BeTrue(Tail(output));
    }

    [Fact]
    public async Task A_pvp_duel_leaves_mental_alone()
    {
        var (engine, output) = Engine(string.Concat(Enumerable.Repeat("A\n", 10)) + string.Concat(Enumerable.Repeat("\n", 10)));
        var attacker = Fighter("Duelist", 10, 100);   // near death all duel long
        var defender = Fighter("Rival", 1, 100);
        defender.AI = CharacterAI.Computer;
        try { await engine.PlayerVsPlayer(attacker, defender, allowSurrender: false, lethal: false); }
        catch (IOException) { }
        attacker.HP.Should().BeGreaterThan(0, Tail(output));
        attacker.Mental.Should().Be(100, Tail(output));
    }
}
