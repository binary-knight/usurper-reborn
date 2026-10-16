using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Data;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;
using G = UsurperReborn.Tests.EliteAffixes128Golden;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.8 difficulty pass, DESIGN item 3, what five elite affixes do (rows F6 to F10): Frenzied, Venomous,
/// Commanding, Mending and Swift, the four lines they add, how they sit with the monster tactics, and the
/// proof that a fight with no elite draws the random numbers it drew before the piece. The sixth affix,
/// Warded, was never built: from 1.3.0 the generator never picks it, and an elite given it by hand does
/// nothing with it; a row holds both.
/// </summary>
[Collection("SharedGameSingletons")]
public class EliteAffixes128Tests
{
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };
    private static readonly string[] Affixes = { "frenzied", "warded", "venomous", "commanding", "mending", "swift" };
    private static readonly string[] Keys = { "combat.elite_frenzy", "combat.elite_mend", "combat.elite_command", "combat.elite_swift" };

    // ---------------------------------------------------------------- harness

    private static T InEnglish<T>(Func<T> body)
    {
        string saved = GameConfig.Language;
        try { GameConfig.Language = "en"; return body(); }
        finally { GameConfig.Language = saved; }
    }

    /// <summary>A monster of a real tier name, with no abilities and nothing to say.</summary>
    private static Monster Foe(string tier, long hp, long strength, int level = 8) => new Monster
    {
        Name = tier, TierName = tier, Level = level, HP = hp, MaxHP = hp, Strength = strength, Defence = 5, Experience = 1, Gold = 0,
        IsActive = true, CanSpeak = false,
    };

    /// <summary>The same monster as an elite with the affixes, named as PromoteElite names it.</summary>
    private static Monster Elite(string tier, long hp, long strength, params string[] affixes)
    {
        var m = Foe(tier, hp, strength);
        m.IsElite = true;
        m.EliteAffixes.AddRange(affixes);
        for (int i = affixes.Length - 1; i >= 0; i--) m.Name = MonsterNames.FromKey("monster.elite." + affixes[i], m.Name);
        return m;
    }

    private static string Plain(string transcript) => Regex.Replace(transcript, "\u001b\\[[0-9;?]*[A-Za-z]", "").Replace("\r", "");

    /// <summary>The screen as (round, row); round 0 is everything before the first round header.</summary>
    private static List<(int Round, string Row)> Rows(string transcript)
    {
        var rows = new List<(int, string)>();
        int round = 0;
        foreach (string raw in Plain(transcript).Split('\n'))
        {
            var m = Regex.Match(raw, @"^\s*Round (\d+)\s*$");
            if (m.Success) { round = int.Parse(m.Groups[1].Value); continue; }
            rows.Add((round, raw.TrimEnd()));
        }
        return rows;
    }

    private sealed record Hit(int Round, string Attacker, long Damage, int Index);

    /// <summary>Each basic hit on the leader: the attacker of the last "X attacks you!" row and the damage of
    /// the "N damage gets through your defenses!" row that follows it.</summary>
    private static List<Hit> Hits(string transcript)
    {
        var hits = new List<Hit>();
        string attacker = "";
        var rows = Rows(transcript);
        for (int i = 0; i < rows.Count; i++)
        {
            var a = Regex.Match(rows[i].Row, @"^(.+) attacks you!$");
            if (a.Success) { attacker = a.Groups[1].Value; continue; }
            var d = Regex.Match(rows[i].Row, @"^(\d+) damage gets through your defenses!$");
            if (d.Success) hits.Add(new Hit(rows[i].Round, attacker, long.Parse(d.Groups[1].Value), i));
        }
        return hits;
    }

    private static int Count(string transcript, string row) => Rows(transcript).Count(r => r.Row == row);

    private static string Script(int attacks, string target = "") => string.Concat(Enumerable.Repeat("A\n" + target + "\n", attacks));

    // ---------------------------------------------------------------- F6 Frenzied

    [Fact]
    public void F6_TheNumbers_AreTheDesigns()
    {
        EliteAffixes.FrenzyBelowHpPercent.Should().Be(50);
        EliteAffixes.FrenzyStrengthMultiplier.Should().Be(1.25);
        EliteAffixes.VenomChancePercent.Should().Be(35);
        EliteAffixes.CommandDamageMultiplier.Should().Be(1.10);
        EliteAffixes.MendBelowHpPercent.Should().Be(40);
        EliteAffixes.MendHealPercent.Should().Be(15);
        EliteAffixes.MendEveryRounds.Should().Be(3);
    }

    [Fact]
    public void F6_Frenzied_RisesOnce_TheFirstTimeItIsBelowHalf()
    {
        var e = Elite("Orc", 1000, 200, "frenzied");
        e.HP = 500;
        EliteAffixes.TryFrenzy(e).Should().BeFalse("exactly half is not below half");
        e.Strength.Should().Be(200);
        e.HP = 499;
        EliteAffixes.TryFrenzy(e).Should().BeTrue();
        e.Strength.Should().Be(250);
        for (int i = 0; i < 5; i++) EliteAffixes.TryFrenzy(e).Should().BeFalse("once");
        e.HP = 10;
        EliteAffixes.TryFrenzy(e).Should().BeFalse("once, however low it falls");
        e.Strength.Should().Be(250);

        // an odd Strength truncates, as every stat multiplier of the elite does
        var odd = Elite("Orc", 1000, 203, "frenzied");
        odd.HP = 1;
        EliteAffixes.TryFrenzy(odd).Should().BeTrue();
        odd.Strength.Should().Be((long)(203 * 1.25));

        // not for another affix, not for a monster that is not an elite, not for the dead
        foreach (string other in Affixes.Where(a => a != "frenzied"))
        {
            var o = Elite("Orc", 1000, 200, other);
            o.HP = 1;
            EliteAffixes.TryFrenzy(o).Should().BeFalse(other);
            o.Strength.Should().Be(200);
        }
        var plain = Foe("Orc", 1000, 200);
        plain.EliteAffixes.Add("frenzied");   // the list alone is not enough: IsElite is false
        plain.HP = 1;
        EliteAffixes.TryFrenzy(plain).Should().BeFalse();
        var dead = Elite("Orc", 1000, 200, "frenzied");
        dead.HP = 0;
        EliteAffixes.TryFrenzy(dead).Should().BeFalse();
    }

    [Fact]
    public async Task F6_ASeededFight_StrengthTimes1Point25_Once_AndTheLineOnce()
    {
        // the elite starts at 45% of a very large pool and stays below half for the whole fight
        var elite = Elite("Orc", 1_000_000, 200, "frenzied");
        elite.HP = 450_000;
        var run = await InEnglish(() => G.Fight(G.Hero(), new List<Monster> { elite }, script: Script(12)));
        string line = Loc.GetIn("en", "combat.elite_frenzy", "Frenzied Orc");
        line.Should().Be("Frenzied Orc flies into a frenzy!");
        var rows = Rows(run.Transcript);
        rows.Max(r => r.Round).Should().BeGreaterThanOrEqualTo(10, "the fight ran for many rounds below half");
        rows.Count(r => r.Row == line).Should().Be(1, run.Transcript);
        rows.First(r => r.Row == line).Round.Should().Be(1, "its first turn below half");
        elite.Strength.Should().Be(250, "x1.25 once, not once a round");
        elite.EliteFrenzyUsed.Should().BeTrue();
        // the line comes before its attack of that turn, so the blow already carries the rise
        int at = rows.FindIndex(r => r.Row == line);
        rows.FindIndex(r => r.Row == "Frenzied Orc attacks you!").Should().BeGreaterThan(at);
    }

    [Fact]
    public async Task F6_ASeededFight_NothingAboveHalf_AndTheLineWhenItCrosses()
    {
        // 60% of 2,000: the hero needs a few blows to bring it below half
        var elite = Elite("Orc", 2000, 100, "frenzied");
        elite.HP = 1200;
        var run = await InEnglish(() => G.Fight(G.Hero(), new List<Monster> { elite }, script: Script(14)));
        var rows = Rows(run.Transcript);
        string line = "Frenzied Orc flies into a frenzy!";
        rows.Count(r => r.Row == line).Should().Be(1, run.Transcript);
        int round = rows.First(r => r.Row == line).Round;
        round.Should().BeGreaterThan(1, "it was above half on its first turn");
        elite.Strength.Should().Be(125);

        // the same elite kept above half: no line and no rise
        var healthy = Elite("Orc", 1_000_000, 100, "frenzied");
        var calm = await InEnglish(() => G.Fight(G.Hero(), new List<Monster> { healthy }, script: Script(10)));
        Rows(calm.Transcript).Should().NotContain(r => r.Row == line);
        healthy.Strength.Should().Be(100);
        healthy.EliteFrenzyUsed.Should().BeFalse();
    }

    // ---------------------------------------------------------------- F7 Venomous

    [Fact]
    public void F7_Venomous_PoisonsOnABasicHit_At35Percent()
    {
        var elite = Elite("Giant Spider", 500, 50, "venomous");
        var rng = new G.RecordingRandom(7);
        int landed = 0;
        for (int i = 0; i < 2000; i++)
            if (EliteAffixes.VenomLands(elite, G.Hero(), rng)) landed++;
        rng.Calls.Count.Should().Be(2000, "one draw a hit");
        rng.Calls.Should().OnlyContain(c => c.StartsWith("Next(100)="));
        (landed / 2000.0).Should().BeInRange(0.32, 0.38, "{0} of 2000", landed);
    }

    [Fact]
    public void F7_NoDraw_ForAnotherMonster_OrATargetAlreadyPoisoned()
    {
        var rng = new G.RecordingRandom(7);
        foreach (string other in Affixes.Where(a => a != "venomous"))
            EliteAffixes.VenomLands(Elite("Giant Spider", 500, 50, other), G.Hero(), rng).Should().BeFalse(other);
        EliteAffixes.VenomLands(Foe("Giant Spider", 500, 50), G.Hero(), rng).Should().BeFalse();
        var notElite = Foe("Giant Spider", 500, 50);
        notElite.EliteAffixes.Add("venomous");
        EliteAffixes.VenomLands(notElite, G.Hero(), rng).Should().BeFalse("the list alone is not enough");
        var poisoned = G.Hero();
        poisoned.ApplyStatus(StatusEffect.Poisoned, 3);
        EliteAffixes.VenomLands(Elite("Giant Spider", 500, 50, "venomous"), poisoned, rng).Should().BeFalse("nothing new to do");
        rng.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task F7_ASeededFight_TheExistingStatus_AndItsExistingLine_OnBasicHitsOnly()
    {
        var elite = Elite("Giant Spider", 1_000_000, 200, "venomous");
        var hero = G.Hero();
        var run = await InEnglish(() => G.Fight(hero, new List<Monster> { elite }, script: Script(25)));
        string line = Loc.GetIn("en", "combat.afflicted_with", Loc.GetIn("en", "status.poisoned"));
        line.Should().Be("You are afflicted with Poisoned!");
        var rows = Rows(run.Transcript);
        int afflicted = rows.Count(r => r.Row == line);
        afflicted.Should().BeGreaterThanOrEqualTo(2, run.Transcript);
        // every poisoning follows a hit that did damage, in the same round
        var hits = Hits(run.Transcript);
        foreach (var (round, row) in rows.Where(r => r.Row == line))
            hits.Should().Contain(h => h.Round == round && h.Attacker == "Venomous Giant Spider" && h.Damage > 0);
        // the existing status does its existing work: it ticks
        rows.Should().Contain(r => Regex.IsMatch(r.Row, "Tester takes \\d+ poison damage!$"), run.Transcript);
        // the draws: one Next(100) a landed hit while the hero is not poisoned, so fewer than the hits
        afflicted.Should().BeLessThan(hits.Count);

        // the same monster without the affix never poisons
        var twin = Foe("Giant Spider", 1_000_000, 200);
        var plain = await InEnglish(() => G.Fight(G.Hero(), new List<Monster> { twin }, script: Script(25)));
        Rows(plain.Transcript).Should().NotContain(r => r.Row == line);
    }

    [Fact]
    public async Task F7_ATeammate_IsPoisonedToo_AndAnImmuneTargetResists()
    {
        var elite = Elite("Giant Spider", 1_000_000, 200, "venomous");
        var mate = G.Mate("Ally One", 60_000);
        var run = await InEnglish(() => G.Fight(G.Hero(), new List<Monster> { elite }, new List<Character> { mate }, script: Script(30)));
        string line = Loc.GetIn("en", "combat.companion_afflicted", "Ally One", "Poisoned");
        Rows(run.Transcript).Should().Contain(r => r.Row == line, run.Transcript);

        // an immune target resists with the existing line and no draw (a fight clears the leader's immunity at
        // its start, so the engine's own step is called here)
        string Venom(Character target, bool leader, out int draws)
        {
            var output = new MemoryStream();
            var term = new TerminalEmulator(new G.ScriptedStream(""), output);
            var engine = new CombatEngine(term);
            var rng = new G.RecordingRandom(1);
            typeof(CombatEngine).GetField("random", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(engine, rng);
            InEnglish(() => typeof(CombatEngine).GetMethod("ApplyEliteVenom", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(engine, new object[] { target, leader }));
            term.StreamWriterInternal?.Flush();
            draws = rng.Calls.Count;
            return Plain(System.Text.Encoding.UTF8.GetString(output.ToArray())).Trim();
        }
        var immune = G.Hero();
        immune.HasStatusImmunity = true;
        Venom(immune, true, out int d1).Should().Be(Loc.GetIn("en", "combat.iron_will_resists", "Poisoned"));
        immune.HasStatus(StatusEffect.Poisoned).Should().BeFalse();
        d1.Should().Be(0);
        var immuneMate = G.Mate("Ally Two");
        immuneMate.HasStatusImmunity = true; immuneMate.StatusImmunityDuration = 2;
        Venom(immuneMate, false, out _).Should().Be(Loc.GetIn("en", "combat.companion_iron_will_resists", "Ally Two", "Poisoned").Trim());
        immuneMate.HasStatus(StatusEffect.Poisoned).Should().BeFalse();
        var open = G.Hero();
        Venom(open, true, out int d2).Should().Be("You are afflicted with Poisoned!");
        open.HasStatus(StatusEffect.Poisoned).Should().BeTrue();
        d2.Should().Be(0, "a Warrior has no resist roll");
        // a Paladin keeps the resist roll a monster ability's status gives it
        var paladin = G.Hero(cls: CharacterClass.Paladin);
        Venom(paladin, true, out int d3);
        d3.Should().Be(1);
    }

    [Fact]
    public async Task F7_TheStatusName_IsInTheReadersLanguage()
    {
        string saved = GameConfig.Language;
        try
        {
            GameConfig.Language = "hu";
            var run = await G.Fight(G.Hero(), new List<Monster> { Elite("Giant Spider", 1_000_000, 200, "venomous") }, script: Script(25));
            Plain(run.Transcript).Should().Contain(Loc.GetIn("hu", "combat.afflicted_with", Loc.GetIn("hu", "status.poisoned")));
            Loc.GetIn("hu", "status.poisoned").Should().Be("Mérgezett");
        }
        finally { GameConfig.Language = saved; }
    }

    // ---------------------------------------------------------------- F8 Commanding

    [Fact]
    public void F8_Commanding_ItsLivingAllies_Deal10PercentMore_NotItself_NotAfterItsDeath()
    {
        var elite = Elite("Orc", 1000, 50, "commanding");
        var ally = Foe("Goblin", 500, 40);
        var group = new List<Monster> { ally, elite };
        EliteAffixes.CommandedDamage(ally, group, 100).Should().Be(110);
        EliteAffixes.CommandedDamage(ally, group, 280).Should().Be(308);
        EliteAffixes.CommandedDamage(ally, group, 5).Should().Be(6, "5.5 rounds to the nearest, half up");
        EliteAffixes.CommandedDamage(ally, group, 4).Should().Be(4);
        EliteAffixes.CommandedDamage(ally, group, 1).Should().Be(1);
        EliteAffixes.CommandedDamage(ally, group, 0).Should().Be(0);
        EliteAffixes.CommandedDamage(elite, group, 100).Should().Be(100, "the elite does not command itself");
        EliteAffixes.IsCommanded(ally, group).Should().BeTrue();

        elite.HP = 0;
        EliteAffixes.IsCommanded(ally, group).Should().BeFalse("the aura ends with it");
        EliteAffixes.CommandedDamage(ally, group, 100).Should().Be(100);

        // no elite, another affix, a monster that is not an elite, no list
        EliteAffixes.CommandedDamage(ally, new List<Monster> { ally, Foe("Orc", 1000, 50) }, 100).Should().Be(100);
        foreach (string other in Affixes.Where(a => a != "commanding"))
            EliteAffixes.CommandedDamage(ally, new List<Monster> { ally, Elite("Orc", 1000, 50, other) }, 100).Should().Be(100, other);
        var notElite = Foe("Orc", 1000, 50);
        notElite.EliteAffixes.Add("commanding");
        EliteAffixes.CommandedDamage(ally, new List<Monster> { ally, notElite }, 100).Should().Be(100);
        EliteAffixes.CommandedDamage(ally, null, 100).Should().Be(100);
        // two Commanding elites command each other; it does not stack on a third
        var second = Elite("Orc", 1000, 50, "commanding");
        var three = new List<Monster> { ally, Elite("Orc", 1000, 50, "commanding"), second };
        EliteAffixes.CommandedDamage(second, three, 100).Should().Be(110);
        EliteAffixes.CommandedDamage(ally, three, 100).Should().Be(110);
    }

    /// <summary>An elite first in the list (the hero's target 1) that dies after a few blows, and a strong ally.</summary>
    private static async Task<(G.Run Run, Monster Elite, Monster Ally)> CommandFight(bool commanding, long eliteHp = 700)
    {
        var elite = commanding ? Elite("Orc", eliteHp, 1, "commanding") : Foe("Orc", eliteHp, 1);
        elite.Name = "Commanding Orc";   // the same name on both sides, so the screens differ by the affix alone
        var ally = Foe("Goblin", 4000, 300);
        var run = await InEnglish(() => G.Fight(G.Hero(60_000), new List<Monster> { elite, ally }, script: Script(60, "1") + G.Attacks(0)));
        return (run, elite, ally);
    }

    /// <summary>The same pair with the ally first in the list, so its blow is the first a teammate takes in a
    /// round (a later blow of the round is cut after the aura); the elite is the hero's target 2. With a
    /// teammate in the party, or with an ability for the ally.</summary>
    private static async Task<(G.Run Run, Monster Elite, Monster Ally)> CommandFightAllyFirst(bool commanding, long eliteHp, bool teammate, string? ability = null)
    {
        var elite = commanding ? Elite("Orc", eliteHp, 1, "commanding") : Foe("Orc", eliteHp, 1);
        elite.Name = "Commanding Orc";
        var ally = Foe("Goblin", 4000, 300);
        if (ability != null) ally.SpecialAbilities = new List<string> { ability };
        var team = teammate ? new List<Character> { G.Mate("Ally One", 60_000) } : null;
        var run = await InEnglish(() => G.Fight(G.Hero(60_000), new List<Monster> { ally, elite }, team, script: Script(150, "2") + G.Attacks(0)));
        return (run, elite, ally);
    }

    /// <summary>Each basic hit on the teammate: the attacker of the last "X attacks Ally One!" row and the
    /// damage of the "N damage gets through Ally One's defenses!" row that follows it.</summary>
    private static List<Hit> HitsOnTheTeammate(string transcript)
    {
        var hits = new List<Hit>();
        string attacker = "";
        var rows = Rows(transcript);
        for (int i = 0; i < rows.Count; i++)
        {
            var a = Regex.Match(rows[i].Row, @"^(?:The )?(.+) attacks Ally One!$");
            if (a.Success) { attacker = a.Groups[1].Value; continue; }
            var d = Regex.Match(rows[i].Row, @"^(\d+) damage gets through Ally One's defenses! \(\d+/\d+ HP\)$");
            if (d.Success) hits.Add(new Hit(rows[i].Round, attacker, long.Parse(d.Groups[1].Value), i));
        }
        return hits;
    }

    /// <summary>Each direct ability hit on the leader: the "You take N damage!" row that follows the ally's
    /// ability row, with no attack row in between (a basic hit is another row, "N damage gets through").</summary>
    private static List<Hit> AbilityHits(string transcript, string abilityRow)
    {
        var hits = new List<Hit>();
        bool open = false;
        var rows = Rows(transcript);
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Row == abilityRow) { open = true; continue; }
            if (Regex.IsMatch(rows[i].Row, @" attacks you!$")) { open = false; continue; }
            var d = Regex.Match(rows[i].Row, @"^You take (\d+) damage!$");
            if (d.Success && open) { hits.Add(new Hit(rows[i].Round, "Goblin", long.Parse(d.Groups[1].Value), i)); open = false; }
        }
        return hits;
    }

    /// <summary>Blow k of the fight with the aura against blow k of the same seeded fight without it: x1.10
    /// (to the nearest point) while the elite lives, the same number after its death.</summary>
    private static void TheBlowsAreCommandedUntilItDies(G.Run with, G.Run without, List<Hit> a, List<Hit> b, int atLeastAfter)
    {
        a.Count.Should().Be(b.Count, with.Transcript);
        DeathIndex(without).Should().BeGreaterThan(0, without.Transcript);
        int before = b.Count(h => h.Index < DeathIndex(without));
        before.Should().BeGreaterThanOrEqualTo(2, "the ally struck while the elite lived: {0}", without.Transcript);
        (b.Count - before).Should().BeGreaterThanOrEqualTo(atLeastAfter, "and after its death: {0}", without.Transcript);
        a.Count(h => h.Index < DeathIndex(with)).Should().Be(before);
        for (int k = 0; k < b.Count; k++)
        {
            a[k].Round.Should().Be(b[k].Round, "blow {0} is the same blow in both fights", k);
            long expected = k < before ? (long)Math.Round(b[k].Damage * 1.10, MidpointRounding.AwayFromZero) : b[k].Damage;
            a[k].Damage.Should().Be(expected, "blow {0} ({1} without the aura), {2} its death", k, b[k].Damage, k < before ? "before" : "after");
            if (k < before) a[k].Damage.Should().BeGreaterThan(b[k].Damage);
        }
    }

    [Fact]
    public async Task F8_ATeammate_TakesTheAllysBlows_10PercentMoreWhileItLives_AndTodaysAfterItsDeath()
    {
        var (with, eliteA, _) = await CommandFightAllyFirst(true, 1500, teammate: true);
        var (without, eliteB, _) = await CommandFightAllyFirst(false, 1500, teammate: true);
        with.Error.Should().BeNull(with.Transcript);
        without.Error.Should().BeNull();
        with.Draws.Should().Equal(without.Draws, "the aura draws nothing, so both fights are the same fight");
        eliteA.IsAlive.Should().BeFalse(with.Transcript);
        eliteB.IsAlive.Should().BeFalse();

        var a = HitsOnTheTeammate(with.Transcript).Where(h => h.Attacker == "Goblin").ToList();
        var b = HitsOnTheTeammate(without.Transcript).Where(h => h.Attacker == "Goblin").ToList();
        TheBlowsAreCommandedUntilItDies(with, without, a, b, atLeastAfter: 3);
        // the elite's own blows on the teammate are not raised
        HitsOnTheTeammate(with.Transcript).Where(h => h.Attacker == "Commanding Orc").Select(h => h.Damage)
            .Should().Equal(HitsOnTheTeammate(without.Transcript).Where(h => h.Attacker == "Commanding Orc").Select(h => h.Damage));
    }

    [Fact]
    public async Task F8_AnAbilityOnTheLeader_Does10PercentMoreWhileItLives_AndTodaysAfterItsDeath()
    {
        string breath = Loc.GetIn("en", "mability.fire_breath", "Goblin");
        breath.Should().Be("Goblin breathes a cone of fire!");
        var (with, eliteA, _) = await CommandFightAllyFirst(true, 2500, teammate: false, ability: "FireBreath");
        var (without, eliteB, _) = await CommandFightAllyFirst(false, 2500, teammate: false, ability: "FireBreath");
        with.Error.Should().BeNull(with.Transcript);
        without.Error.Should().BeNull();
        with.Draws.Should().Equal(without.Draws, "the aura draws nothing, so both fights are the same fight");
        eliteA.IsAlive.Should().BeFalse(with.Transcript);
        eliteB.IsAlive.Should().BeFalse();

        var a = AbilityHits(with.Transcript, breath);
        var b = AbilityHits(without.Transcript, breath);
        // every use of the ability is a direct hit that was read
        b.Count.Should().Be(Count(without.Transcript, breath));
        TheBlowsAreCommandedUntilItDies(with, without, a, b, atLeastAfter: 2);
    }

    [Fact]
    public async Task F8_ASeededFight_TheAllysBlows_Are10PercentMoreWhileItLives_AndTodaysAfterItsDeath()
    {
        var (with, eliteA, _) = await CommandFight(true);
        var (without, eliteB, _) = await CommandFight(false);
        // the aura draws nothing: both fights make the same number of draws from the same seed, so blow k of
        // one is blow k of the other
        with.Draws.Count.Should().Be(without.Draws.Count);
        with.Error.Should().BeNull(with.Transcript);
        without.Error.Should().BeNull();
        eliteA.IsAlive.Should().BeFalse(with.Transcript);
        eliteB.IsAlive.Should().BeFalse();

        var a = Hits(with.Transcript).Where(h => h.Attacker == "Goblin").ToList();
        var b = Hits(without.Transcript).Where(h => h.Attacker == "Goblin").ToList();
        a.Count.Should().Be(b.Count);
        // the elite's death row in the screen of each fight splits the ally's blows
        DeathIndex(without).Should().BeGreaterThan(0, without.Transcript);
        int before = b.Count(h => h.Index < DeathIndex(without));
        before.Should().BeGreaterThanOrEqualTo(2, "the ally struck while the elite lived: {0}", without.Transcript);
        (b.Count - before).Should().BeGreaterThanOrEqualTo(3, "and after its death");
        a.Count(h => h.Index < DeathIndex(with)).Should().Be(before);
        for (int k = 0; k < b.Count; k++)
        {
            long expected = k < before ? (long)Math.Round(b[k].Damage * 1.10, MidpointRounding.AwayFromZero) : b[k].Damage;
            a[k].Damage.Should().Be(expected, "blow {0} ({1} without the aura), {2} its death", k, b[k].Damage, k < before ? "before" : "after");
            if (k < before) a[k].Damage.Should().BeGreaterThan(b[k].Damage);
        }
        // the elite's own blows are not raised
        Hits(with.Transcript).Where(h => h.Attacker == "Commanding Orc").Select(h => h.Damage)
            .Should().Equal(Hits(without.Transcript).Where(h => h.Attacker == "Commanding Orc").Select(h => h.Damage));
    }

    /// <summary>The row of the hero's killing blow on the elite (target 1 until it dies), which comes before
    /// the ally's turn of that round. The death row itself is flavour drawn outside the engine.</summary>
    private static int DeathIndex(G.Run run) => Rows(run.Transcript).FindLastIndex(r => r.Row == "You attack Commanding Orc!");

    [Fact]
    public async Task F8_TheLine_IsShownOnce_WhenItFirstActs_NeverAgain_AndNotWhenItDies()
    {
        string line = Loc.GetIn("en", "combat.elite_command", "Commanding Orc");
        line.Should().Be("Commanding Orc drives its allies on!");

        // a long fight: the elite lives for every round
        var elite = Elite("Orc", 1_000_000, 50, "commanding");
        var ally = Foe("Goblin", 1_000_000, 50);
        var longRun = await InEnglish(() => G.Fight(G.Hero(), new List<Monster> { elite, ally }, script: Script(15, "1")));
        var rows = Rows(longRun.Transcript);
        rows.Max(r => r.Round).Should().BeGreaterThanOrEqualTo(12);
        rows.Count(r => r.Row == line).Should().Be(1, longRun.Transcript);
        rows.First(r => r.Row == line).Round.Should().Be(1);
        int at = rows.FindIndex(r => r.Row == line);
        rows.FindIndex(r => r.Row == "Commanding Orc attacks you!").Should().BeGreaterThan(at, "the line opens its first action");
        elite.EliteCommandShown.Should().BeTrue();

        // it dies: the one line of its first turn and nothing at its death
        var (dying, dead, _) = await CommandFight(true);
        dead.IsAlive.Should().BeFalse();
        var dyingRows = Rows(dying.Transcript);
        dyingRows.Count(r => r.Row == line).Should().Be(1);
        dyingRows.Count(r => r.Row.Contains("drives its allies")).Should().Be(1);
        dyingRows.FindIndex(r => r.Row == line).Should().BeLessThan(DeathIndex(dying));

        // alone it has no one to drive on: no line
        var alone = Elite("Orc", 1_000_000, 50, "commanding");
        var soloRun = await InEnglish(() => G.Fight(G.Hero(), new List<Monster> { alone }, script: Script(8)));
        Rows(soloRun.Transcript).Should().NotContain(r => r.Row.Contains("drives its allies"));
        alone.EliteCommandShown.Should().BeFalse();
    }

    // ---------------------------------------------------------------- F9 Mending

    [Fact]
    public void F9_Mending_PicksTheMostWoundedLivingAllyBelow40Percent()
    {
        var elite = Elite("Orc", 1000, 50, "mending");
        Monster At(string tier, long max, long hp) { var m = Foe(tier, max, 10); m.HP = hp; return m; }
        var a = At("Goblin", 1000, 399);    // 39.9%
        var b = At("Wolf", 200, 30);        // 15%
        var c = At("Kobold", 5000, 1000);   // 20%
        var healthy = At("Rat", 100, 40);   // exactly 40%: not below
        var dead = At("Bat", 100, 0);
        var group = new List<Monster> { elite, a, healthy, c, b, dead };
        EliteAffixes.MendTarget(elite, group, 1).Should().BeSameAs(b, "the smallest share of its MaxHP");
        b.HP = 0;
        EliteAffixes.MendTarget(elite, group, 1).Should().BeSameAs(c);
        c.HP = 5000;
        EliteAffixes.MendTarget(elite, group, 1).Should().BeSameAs(a);
        a.HP = 400;
        EliteAffixes.MendTarget(elite, group, 1).Should().BeNull("no one below 40%");

        // never itself, however wounded
        elite.HP = 1;
        EliteAffixes.MendTarget(elite, new List<Monster> { elite }, 1).Should().BeNull();
        EliteAffixes.MendTarget(elite, group, 1).Should().BeNull();
        elite.HP = 1000;

        // a tie goes to the first in the group
        var t1 = At("Goblin", 1000, 100);
        var t2 = At("Wolf", 2000, 200);
        EliteAffixes.MendTarget(elite, new List<Monster> { elite, t1, t2 }, 1).Should().BeSameAs(t1);
        EliteAffixes.MendTarget(elite, new List<Monster> { t2, elite, t1 }, 1).Should().BeSameAs(t2);

        // only a Mending elite, alive
        foreach (string other in Affixes.Where(x => x != "mending"))
            EliteAffixes.MendTarget(Elite("Orc", 1000, 50, other), new List<Monster> { t1 }, 1).Should().BeNull(other);
        var notElite = Foe("Orc", 1000, 50);
        notElite.EliteAffixes.Add("mending");
        EliteAffixes.MendTarget(notElite, new List<Monster> { t1 }, 1).Should().BeNull();
        elite.HP = 0;
        EliteAffixes.MendTarget(elite, new List<Monster> { elite, t1 }, 1).Should().BeNull("the dead mend no one");
    }

    [Fact]
    public void F9_Mending_Heals15PercentOfTheAllysMaxHP_AndThenWaitsThreeRounds()
    {
        var elite = Elite("Orc", 1000, 50, "mending");
        var ally = Foe("Goblin", 1000, 10);
        ally.HP = 100;
        var group = new List<Monster> { elite, ally };
        EliteAffixes.MendTarget(elite, group, 4).Should().BeSameAs(ally);
        EliteAffixes.Mend(elite, ally, 4).Should().Be(150);
        ally.HP.Should().Be(250);
        elite.EliteMendReadyRound.Should().Be(7);
        EliteAffixes.MendTarget(elite, group, 5).Should().BeNull("one round later");
        EliteAffixes.MendTarget(elite, group, 6).Should().BeNull("two rounds later");
        EliteAffixes.MendTarget(elite, group, 7).Should().BeSameAs(ally, "three rounds later");

        // never past MaxHP, and the number shown is what was restored
        var small = Foe("Rat", 7, 1);
        small.HP = 1;
        EliteAffixes.Mend(elite, small, 1).Should().Be(1, "15% of 7 is 1");
        var full = Foe("Rat", 100, 1);
        full.HP = 95;
        EliteAffixes.Mend(elite, full, 1).Should().Be(5);
        full.HP.Should().Be(100);
    }

    [Fact]
    public async Task F9_ASeededFight_MendsSpacedThreeRounds_TheMendIsItsAction()
    {
        // the ally is parked at 1% of a very large pool: 16%, 31% and 46% after three mends, then no more
        var elite = Elite("Orc", 1_000_000, 200, "mending");
        var ally = Foe("Goblin", 1_000_000, 1);
        ally.HP = 10_000;
        var run = await InEnglish(() => G.Fight(G.Hero(), new List<Monster> { elite, ally }, script: Script(14, "1")));
        string line = Loc.GetIn("en", "combat.elite_mend", "Mending Orc", "Goblin", 150_000L);
        line.Should().Be("Mending Orc mends Goblin for 150000 HP.");
        var rows = Rows(run.Transcript);
        rows.Max(r => r.Round).Should().BeGreaterThanOrEqualTo(12);
        var mendRounds = rows.Where(r => r.Row == line).Select(r => r.Round).ToList();
        mendRounds.Should().Equal(new[] { 1, 4, 7 }, run.Transcript);
        ally.HP.Should().BeGreaterThan(400_000, "three mends of 150,000 less the hero's blows on the elite only");
        ally.HP.Should().Be(10_000 + 3 * 150_000);
        // a mend is the elite's action of that round: it attacks in every other round, never in those
        var attackRounds = rows.Where(r => r.Row == "Mending Orc attacks you!").Select(r => r.Round).ToList();
        attackRounds.Should().NotContain(mendRounds);
        attackRounds.Should().Contain(new[] { 2, 3, 5, 6, 8, 9, 10 });
        // the ally fights on as before
        rows.Should().Contain(r => r.Row == "Goblin attacks you!");
    }

    [Fact]
    public async Task F9_NoOneBelow40Percent_NoMend_AndAHeldEliteDoesNotMend()
    {
        var elite = Elite("Orc", 1_000_000, 200, "mending");
        var ally = Foe("Goblin", 1_000_000, 1);
        ally.HP = 400_000;   // exactly 40%
        var run = await InEnglish(() => G.Fight(G.Hero(), new List<Monster> { elite, ally }, script: Script(8, "1")));
        Rows(run.Transcript).Should().NotContain(r => r.Row.Contains(" mends "));

        // stunned for the first four rounds: its first mend is on the first turn it can act
        var held = Elite("Orc", 1_000_000, 200, "mending");
        held.StunRounds = 4;
        var hurt = Foe("Goblin", 1_000_000, 1);
        hurt.HP = 10_000;
        var heldRun = await InEnglish(() => G.Fight(G.Hero(), new List<Monster> { held, hurt }, script: Script(10, "1")));
        var mends = Rows(heldRun.Transcript).Where(r => r.Row.Contains(" mends ")).Select(r => r.Round).ToList();
        mends.Should().NotBeEmpty(heldRun.Transcript);
        mends[0].Should().BeGreaterThan(4);
    }

    // ---------------------------------------------------------------- F10 Swift

    private static (G.Run Run, Monster Elite, Monster Ally) Swiftly(bool swift, bool ambush, Character hero, int script = 6)
    {
        var elite = swift ? Elite("Wolf", 1_000_000, 200, "swift") : Foe("Wolf", 1_000_000, 200);
        elite.Name = "Swift Wolf";
        var ally = Foe("Goblin", 1_000_000, 200);
        var run = InEnglish(() => G.Fight(hero, new List<Monster> { elite, ally }, ambush: ambush, script: Script(script, "1"))).GetAwaiter().GetResult();
        return (run, elite, ally);
    }

    private static readonly string[] AmbushHeadlines = { "combat.ambush_sensed", "combat.full_ambush" };

    [Fact]
    public void F10_APlainFight_TheSwiftEliteActsBeforeRound1_WithoutARoll_TheOthersDoNot()
    {
        var (run, _, _) = Swiftly(true, false, G.Hero());
        var rows = Rows(run.Transcript);
        string line = Loc.GetIn("en", "combat.elite_swift", "Swift Wolf");
        line.Should().Be("Swift Wolf strikes before you can act!");
        rows.Count(r => r.Row == line).Should().Be(1, run.Transcript);
        rows.First(r => r.Row == line).Round.Should().Be(0, "before the first round");
        // one free attack by the elite, none by its ally, before the player's first action
        rows.Count(r => r.Round == 0 && r.Row == "Swift Wolf attacks you!").Should().Be(1);
        rows.Count(r => r.Round == 0 && r.Row == "Goblin attacks you!").Should().Be(0);
        int firstPlayer = rows.FindIndex(r => r.Row.StartsWith("You attack "));
        rows.FindIndex(r => r.Row == "Swift Wolf attacks you!").Should().BeLessThan(firstPlayer);
        // no ambush headline: this is not an ambush
        foreach (string key in AmbushHeadlines) rows.Should().NotContain(r => r.Row == Loc.GetIn("en", key));
        rows.Should().NotContain(r => r.Row.Contains("get the drop on you"));

        // the same fight without the affix starts with the player
        var (plain, _, _) = Swiftly(false, false, G.Hero());
        var plainRows = Rows(plain.Transcript);
        plainRows.Should().NotContain(r => r.Round == 0 && r.Row.EndsWith(" attacks you!"));
        plainRows.Should().NotContain(r => r.Row == line);
    }

    [Fact]
    public async Task F10_TheTally_SaysTheMonstersActedFirst()
    {
        // a fight the hero wins, so the result carries its tally
        async Task<CombatResult> Win(bool swift)
        {
            var elite = swift ? Elite("Wolf", 300, 50, "swift") : Foe("Wolf", 300, 50);
            var run = await InEnglish(() => G.Fight(G.Hero(), new List<Monster> { elite, Foe("Goblin", 200, 50) }));
            run.Error.Should().BeNull(run.Transcript);
            run.Result!.Outcome.Should().Be(CombatOutcome.Victory);
            return run.Result;
        }
        (await Win(true)).Tally.FirstActor.Should().Be(1);
        (await Win(false)).Tally.FirstActor.Should().Be(0);
    }

    [Fact]
    public void F10_InAnAmbush_ItJoinsWithoutItsRoll_AndGetsOneFreeAttack_NotTwo()
    {
        // a hero no monster of level 8 can surprise: the ally fails its roll, the Swift elite has none
        Character Wary() { var h = G.Hero(); h.BaseAgility = 4000; h.BaseDexterity = 4000; return h; }
        var (run, _, _) = Swiftly(true, true, Wary());
        var rows = Rows(run.Transcript);
        rows.Count(r => r.Round == 0 && r.Row == "Swift Wolf attacks you!").Should().Be(1, "one free attack: {0}", run.Transcript);
        rows.Count(r => r.Round == 0 && r.Row == "Goblin attacks you!").Should().Be(0);
        rows.Count(r => r.Row == "Swift Wolf strikes before you can act!").Should().Be(1);
        rows.Should().Contain(r => r.Row == Loc.GetIn("en", "combat.partial_ambush", 1, 2), "the ambush keeps its headline");
        // the same ambush without the affix: both fail their roll, no one attacks first
        var (plain, _, _) = Swiftly(false, true, Wary());
        Rows(plain.Transcript).Should().NotContain(r => r.Round == 0 && r.Row.EndsWith(" attacks you!"));
        Rows(plain.Transcript).Should().Contain(r => r.Row == Loc.GetIn("en", "combat.ambush_sensed"));

        // a hero everything surprises: a full ambush, and still one free attack for the elite
        Character Careless() { var h = G.Hero(); h.BaseAgility = 1; h.BaseDexterity = 1; h.Level = 1; return h; }
        var elite = Elite("Wolf", 1_000_000, 200, "swift");
        var ally = Foe("Goblin", 1_000_000, 200);
        elite.Level = ally.Level = 60;
        var full = InEnglish(() => G.Fight(Careless(), new List<Monster> { elite, ally }, ambush: true, script: Script(4, "1"))).GetAwaiter().GetResult();
        var fullRows = Rows(full.Transcript);
        fullRows.Should().Contain(r => r.Row == Loc.GetIn("en", "combat.full_ambush"), full.Transcript);
        fullRows.Count(r => r.Round == 0 && r.Row == "Swift Wolf attacks you!").Should().Be(1);
        fullRows.Count(r => r.Round == 0 && r.Row == "Goblin attacks you!").Should().Be(1);
    }

    [Fact]
    public void F10_TheOpposedRoll_IsDrawnForTheOthersOnly_AndForNoOneInAPlainFight()
    {
        // The fight stops at the first prompt (an empty script), so the draws are those before the player's
        // first action. The elite is stunned for one turn: its free action shows a row and draws nothing,
        // which leaves the opposed rolls (two d20 a monster) as the only draws of the opening.
        Character Wary() { var h = G.Hero(); h.BaseAgility = 4000; h.BaseDexterity = 4000; return h; }
        (List<string> Draws, List<(int Round, string Row)> Rows) Open(bool swift, bool ambush)
        {
            var elite = swift ? Elite("Wolf", 1_000_000, 200, "swift") : Foe("Wolf", 1_000_000, 200);
            elite.Name = "Swift Wolf";
            elite.StunRounds = 1;
            var run = InEnglish(() => G.Fight(Wary(), new List<Monster> { elite, Foe("Goblin", 1_000_000, 200) }, ambush: ambush, script: "")).GetAwaiter().GetResult();
            return (run.Draws, Rows(run.Transcript));
        }
        string stunned = Loc.GetIn("en", "combat.stunned", "Swift Wolf");
        var plain = Open(false, false);
        var plainSwift = Open(true, false);
        var ambush = Open(false, true);
        var ambushSwift = Open(true, true);

        plain.Draws.Should().BeEmpty("a plain fight draws nothing before the first prompt");
        plainSwift.Draws.Should().BeEmpty("the Swift elite is the one ambusher and no one rolls");
        plainSwift.Rows.Count(r => r.Row == stunned).Should().Be(1, "its one free action");
        plainSwift.Rows.Count(r => r.Row == "Swift Wolf strikes before you can act!").Should().Be(1);

        ambush.Draws.Should().HaveCount(4, "two monsters, two d20 each");
        ambush.Draws.Should().OnlyContain(d => d.StartsWith("Next(1,21)="));
        ambushSwift.Draws.Should().HaveCount(2, "the ally rolls, the elite does not");
        ambushSwift.Draws.Should().OnlyContain(d => d.StartsWith("Next(1,21)="));
        ambushSwift.Rows.Count(r => r.Row == stunned).Should().Be(1, "one free action, not two");
        ambush.Rows.Count(r => r.Row == stunned).Should().Be(0, "without the affix it failed its roll");
    }

    // ---------------------------------------------------------------- the sixth affix, and the switch

    [Fact]
    public async Task Warded_IsNeverPicked_AndAnEliteGivenItByHand_DoesNothingWithIt()
    {
        // 1.3.0: no generated elite is Warded (EliteMonsters128Tests rows V1 to V4 hold the draw itself)
        bool savedElites = GameConfig.EliteMonstersEnabled;
        try
        {
            GameConfig.EliteMonstersEnabled = true;
            int elites = 0;
            foreach (int floor in new[] { 7, 30, 60, 100 })
                for (int seed = 0; seed < 4000; seed++)
                    foreach (var m in MonsterGenerator.GenerateMonsterGroup(floor, new Random(seed), MonsterGenerator.OldGodApproachScale(floor), EncounterRules.Dungeon(floor)))
                    {
                        if (!m.IsElite) continue;
                        elites++;
                        EliteAffixes.Has(m, "warded").Should().BeFalse("floor {0} seed {1}", floor, seed);
                        m.Name.Should().NotContain("Warded");
                        m.EliteAffixes.Should().OnlyContain(a => a == EliteAffixes.Frenzied || a == EliteAffixes.Venomous || a == EliteAffixes.Commanding || a == EliteAffixes.Mending || a == EliteAffixes.Swift);
                    }
            elites.Should().BeGreaterThan(1000);
        }
        finally { GameConfig.EliteMonstersEnabled = savedElites; }

        // and the fight code still knows nothing of it: an elite given the affix by hand fights like its twin
        // below half, with a wounded ally, hitting the hero for many rounds: everything another affix would act on
        async Task<G.Run> Run(bool warded)
        {
            var elite = warded ? Elite("Orc", 1_000_000, 200, "warded") : Foe("Orc", 1_000_000, 200);
            elite.Name = "Warded Orc";
            elite.HP = 300_000;
            var ally = Foe("Goblin", 1_000_000, 100);
            ally.HP = 50_000;
            return await InEnglish(() => G.Fight(G.Hero(), new List<Monster> { elite, ally }, script: Script(12, "1")));
        }
        var with = await Run(true);
        var without = await Run(false);
        with.Draws.Should().Equal(without.Draws);
        Rows(with.Transcript).Select(r => r.Row).Should().Equal(Rows(without.Transcript).Select(r => r.Row));
        Elite("Orc", 10, 1, "warded").Name.Should().Be("Warded Orc");

        // No fight code names the affix: neither its id as text ("warded") nor a constant of that name
        // (EliteAffixes.Warded). The oath-mark code of CombatEngine.cs has a local called warded, which is
        // not the affix, so the scan reads the two forms the affix could take and not the bare word.
        string root = Leftovers1114BTests.RepoRoot();
        foreach (string file in new[] { "CombatEngine.cs", "EliteAffixes.cs" })
            Regex.IsMatch(File.ReadAllText(Path.Combine(root, "Scripts", "Systems", file)), WardedAffixInCode, RegexOptions.IgnoreCase).Should().BeFalse(file);
        Regex.IsMatch("if (EliteAffixes.Has(m, \"warded\")) x();", WardedAffixInCode, RegexOptions.IgnoreCase).Should().BeTrue();
        Regex.IsMatch("if (EliteAffixes.Has(m, EliteAffixes.Warded)) x();", WardedAffixInCode, RegexOptions.IgnoreCase).Should().BeTrue();
        Regex.IsMatch("bool caughtByWard = warded && !wasPermadeath;", WardedAffixInCode, RegexOptions.IgnoreCase).Should().BeFalse();
    }

    /// <summary>The Warded affix as fight code could name it: the id as a string literal, or a member called
    /// Warded of the affix class. Not the bare word (the oath-mark handler has a local of that name).</summary>
    internal const string WardedAffixInCode = "\"warded\"|EliteAffixes\\s*\\.\\s*Warded\\b";

    [Fact]
    public void WithTheSwitchOff_NoMonsterHasAnAffix_AndTheEngineDoesNotReadTheSwitch()
    {
        bool saved = GameConfig.EliteMonstersEnabled;
        try
        {
            GameConfig.EliteMonstersEnabled = false;
            foreach (int floor in new[] { 3, 7, 30, 60, 100 })
                for (int seed = 0; seed < 1500; seed++)
                    foreach (var m in MonsterGenerator.GenerateMonsterGroup(floor, new Random(seed), MonsterGenerator.OldGodApproachScale(floor), EncounterRules.Dungeon(floor)))
                    {
                        m.IsElite.Should().BeFalse();
                        m.EliteAffixes.Should().BeEmpty();
                        foreach (string a in Affixes) EliteAffixes.Has(m, a).Should().BeFalse();
                    }
        }
        finally { GameConfig.EliteMonstersEnabled = saved; }
        string root = Leftovers1114BTests.RepoRoot();
        foreach (string file in new[] { "CombatEngine.cs", "EliteAffixes.cs" })
            File.ReadAllText(Path.Combine(root, "Scripts", "Systems", file)).Should().NotContain("EliteMonstersEnabled", file);
        // the fight state of an affix is run time only
        var e = Elite("Orc", 100, 10, "frenzied");
        e.EliteFrenzyUsed = true; e.EliteCommandShown = true; e.EliteMendReadyRound = 9;
        string json = JsonSerializer.Serialize(e);
        json.Should().NotContain("EliteFrenzyUsed").And.NotContain("EliteCommandShown").And.NotContain("EliteMendReadyRound");
    }

    [Fact]
    public async Task AnOldGodFight_GetsNothing()
    {
        // elites never appear in a BossContext fight; if one were put there, its affixes would stay silent
        var elite = Elite("Wolf", 1_000_000, 200, "swift", "frenzied");
        elite.HP = 400_000;
        var run = await InEnglish(() => G.Fight(G.Hero(), new List<Monster> { elite }, script: Script(5),
            setup: engine => engine.BossContext = new BossCombatContext()));
        var rows = Rows(run.Transcript);
        rows.Should().NotContain(r => r.Row.Contains("strikes before you can act") || r.Row.Contains("flies into a frenzy"));
        rows.Should().NotContain(r => r.Round == 0 && r.Row.EndsWith(" attacks you!"));
        elite.Strength.Should().Be(200);
    }

    // ---------------------------------------------------------------- golden draws

    /// <summary>Written by this harness (Tests/EliteAffixes128Golden.cs, the same file) on release-1.2.8 at
    /// 3a8dd9dc, the code before the piece: the draws up to the last foe's death, a hash of them, the outcome
    /// and the rounds.</summary>
    private static readonly Dictionary<string, string> GoldenFights = new()
    {
        // 1.3.2: the basic attack improves by use, which changes this fight's draws
        // 1.3.2: the class combat modifiers apply, which changes this fight's draws
        ["floor 7 pair, solo"] = "194:A57481E5A1449B5A:Victory in 15",
        // 1.3.2: the basic attack improves by use, which changes this fight's draws
        ["floor 7 pair, solo, ambush"] = "192:190A3A98554F5796:Victory in 14",
        // 1.3.2: the basic attack improves by use, which changes this fight's draws
        // 1.3.3: monsters from floor 25 grow with the deep ramp, which changes this fight's draws
        ["floor 35 trio, level 35 hero, two teammates"] = "408:4ADF1FF79FED3AF2:Victory in 16",
        // 1.3.2: the basic attack improves by use, which changes this fight's draws
        // 1.3.3: the group size grows each floor from 25, which changes these draws
        // 1.3.3: monsters from floor 25 grow with the deep ramp, which changes this fight's draws
        ["floor 35 trio, level 35 hero, one teammate, ambush"] = "343:0DA6079253B0AC74:Victory in 16",
        // 1.3.2: the basic attack improves by use, which changes this fight's draws
        // 1.3.3: monsters from floor 25 grow with the deep ramp, which changes this fight's draws (the hero has more HP)
        ["floor 50 four, level 50 hero, two teammates"] = "580:A97BECA568D361D7:Victory in 26",
        // 1.3.2: the basic attack improves by use, which changes this fight's draws
        // 1.3.3: monsters past floor 50 grow a full level a floor (the hero has more HP), which changes this fight's draws
        // 1.3.3: monsters from floor 25 grow with the deep ramp, which changes this fight's draws (the hero has more HP)
        ["floor 80 five, level 80 cleric, ambush"] = "856:9B6C9E9065A5E227:Victory in 39",
        // 1.3.2: the basic attack improves by use, which changes this fight's draws
        ["floor 3 single"] = "51:25D2351E15E63BF7:Victory in 6",
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AFightWithNoElite_DrawsExactlyTheNumbersOfBeforeThePiece_SwitchOnAndOff(bool elitesOn)
    {
        // The list was written at 3a8dd9dc, before the player's to-hit roll existed. The roll is a later
        // piece with draws of its own, so it is off here (as DualWieldPerHand128Tests turns it off): what is
        // left is the fight of before the affixes, and its draws must be the listed ones.
        bool saved = GameConfig.EliteMonstersEnabled;
        bool savedRoll = GameConfig.PlayerHitRollEnabled;
        string lang = GameConfig.Language;
        try
        {
            GameConfig.EliteMonstersEnabled = elitesOn;
            GameConfig.PlayerHitRollEnabled = false;
            GameConfig.Language = "en";
            G.Fights.Select(f => f.Name).Should().BeEquivalentTo(GoldenFights.Keys);
            foreach (var (name, fight) in G.Fights)
            {
                var run = await fight();
                run.Error.Should().BeNull(name);
                G.Signature(run).Should().Be(GoldenFights[name], name);
            }
        }
        finally { GameConfig.EliteMonstersEnabled = saved; GameConfig.PlayerHitRollEnabled = savedRoll; GameConfig.Language = lang; }
    }

    // ---------------------------------------------------------------- with the monster tactics (P4)

    [Fact]
    public void WithTactics_AMendedAllysSelfHeal_BecomesWasted_AndChooseDrawsWhatItDrew()
    {
        // the ally's own self-heal is used below 40% (MonsterTactics.SelfHealBelowHpPercent), the same line the
        // mend reads: a mend that lifts it to 40% or more makes the self-heal a wasted pick
        MonsterTactics.SelfHealBelowHpPercent.Should().Be(EliteAffixes.MendBelowHpPercent);
        var elite = Elite("Orc", 1000, 50, "mending");
        var troll = Foe("Troll", 1000, 50);
        troll.SpecialAbilities = new List<string> { "Regeneration" };
        troll.HP = 300;
        var hero = G.Hero();
        MonsterTactics.Wasted("Regeneration", troll, hero).Should().BeFalse("30%: its own heal is useful");
        EliteAffixes.Mend(elite, EliteAffixes.MendTarget(elite, new List<Monster> { elite, troll }, 1)!, 1);
        troll.HP.Should().Be(450);
        MonsterTactics.Wasted("Regeneration", troll, hero).Should().BeTrue("45% after the mend");
        MonsterTactics.Choose(troll, hero, 1, new List<Monster> { elite, troll }, new Random(1)).Should().BeNull("nothing useful left: its normal attack");

        // Choose reads the monster's own lists only: an elite beside it changes neither the pick nor the draw
        foreach (string affix in new[] { "mending", "commanding" })
        {
            var shaman = Foe("Kobold", 1000, 50);
            shaman.SpecialAbilities = new List<string> { "Poison", "Curse", "Heal" };
            var a = new G.RecordingRandom(5);
            var b = new G.RecordingRandom(5);
            for (int round = 1; round <= 20; round++)
                MonsterTactics.Choose(shaman, hero, round, new List<Monster> { shaman, Foe("Orc", 1000, 50) }, a)
                    .Should().Be(MonsterTactics.Choose(shaman, hero, round, new List<Monster> { shaman, Elite("Orc", 1000, 50, affix) }, b));
            a.Calls.Should().Equal(b.Calls);
            // the elite's own pick is not touched by its affix either
            var e = Elite("Kobold", 1000, 50, affix);
            e.SpecialAbilities = new List<string> { "Poison", "Curse", "Heal" };
            var c = new G.RecordingRandom(5);
            for (int round = 1; round <= 20; round++) MonsterTactics.Choose(e, hero, round, null, c);
            c.Calls.Should().Equal(a.Calls);
        }
        // no affix is an ability: none can be picked, so none can be wasted
        foreach (string affix in Affixes)
            Enum.TryParse<MonsterAbilities.AbilityType>(affix, true, out _).Should().BeFalse(affix);
    }

    [Fact]
    public async Task WithTactics_AMendRound_MakesNoAbilityRoll_AndACommandedAbilityStillRuns()
    {
        bool saved = GameConfig.MonsterTacticsEnabled;
        try
        {
            GameConfig.MonsterTacticsEnabled = true;
            // a Mending elite with an ability of its own: on a mend round it neither rolls for the ability nor attacks
            var elite = Elite("Orc", 1_000_000, 200, "mending");
            elite.SpecialAbilities = new List<string> { "CrushingBlow" };
            var ally = Foe("Goblin", 1_000_000, 1);
            ally.HP = 10_000;
            var run = await InEnglish(() => G.Fight(G.Hero(), new List<Monster> { elite, ally }, script: Script(9, "1")));
            var rows = Rows(run.Transcript);
            var mendRounds = rows.Where(r => r.Row.Contains(" mends ")).Select(r => r.Round).ToList();
            mendRounds.Should().Equal(1, 4, 7);
            rows.Where(r => r.Row == "Mending Orc attacks you!").Select(r => r.Round).Should().NotContain(mendRounds);
            rows.Should().Contain(r => r.Row == "Mending Orc attacks you!");

            // an ally of a Commanding elite still picks and uses its ability (the pick is MonsterTactics.Choose's)
            var commander = Elite("Orc", 1_000_000, 1, "commanding");
            var brute = Foe("Goblin", 1_000_000, 300, level: 40);
            brute.SpecialAbilities = new List<string> { "CrushingBlow" };
            var cmd = await InEnglish(() => G.Fight(G.Hero(60_000), new List<Monster> { commander, brute }, script: Script(20, "1")));
            cmd.Draws.Should().Contain(d => d.StartsWith("Next(1)="), "Choose drew its pick among one useful ability");
        }
        finally { GameConfig.MonsterTacticsEnabled = saved; }
    }

    // ---------------------------------------------------------------- the four lines

    private static Dictionary<string, string> File_(string lang) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(Leftovers1114BTests.RepoRoot(), "Localization", lang + ".json")))!;

    [Fact]
    public void TheFourKeys_AreInFiveLanguages_Translated_WithTheirArguments_AndPlainText()
    {
        var en = File_("en");
        en["combat.elite_frenzy"].Should().Be("{0} flies into a frenzy!");
        en["combat.elite_mend"].Should().Be("{0} mends {1} for {2} HP.");
        en["combat.elite_command"].Should().Be("{0} drives its allies on!");
        en["combat.elite_swift"].Should().Be("{0} strikes before you can act!");
        foreach (string lang in Langs)
        {
            var file = File_(lang);
            foreach (string key in Keys)
            {
                file.Should().ContainKey(key, lang);
                string text = file[key];
                if (lang != "en") text.Should().NotBe(en[key], "{0} {1} is translated", lang, key);
                Regex.Matches(text, @"\{\d\}").Select(m => m.Value).OrderBy(x => x)
                    .Should().Equal(Regex.Matches(en[key], @"\{\d\}").Select(m => m.Value).OrderBy(x => x), "{0} {1}", lang, key);
                // screen reader and BBS: letters, digits, spaces and plain punctuation only (no markup, glyph or dash)
                text.Should().MatchRegex(@"^[\p{L}\p{N} {}.,:!¡]+$", "{0} {1}", lang, key);
                text.Should().NotContain("[").And.NotContain("\u2014").And.NotContain("\u2013");
            }
        }
    }

    /// <summary>Every elite name the generator can build: each tier, each affix, each ordered pair, and the
    /// caster of an early family inside one affix.</summary>
    private static List<Monster> EveryEliteName()
    {
        var all = new List<Monster>();
        Monster Named(string member, params string[] affixes) => Elite(member, 100, 10, affixes);
        foreach (var family in MonsterFamilies.AllFamilies)
            foreach (var tier in family.Tiers)
                foreach (string a in Affixes)
                {
                    all.Add(Named(tier.Name, a));
                    if (MonsterGenerator.CasterSpells.ContainsKey(family.FamilyName) && tier.MinLevel <= 10)
                    {
                        string caster = MonsterNames.FromKey("monster.role.caster", tier.Name);
                        all.Add(Named(caster, a));
                        foreach (string b in Affixes.Where(b => b != a)) all.Add(Named(caster, a, b));   // wider than the game builds
                    }
                    foreach (string b in Affixes.Where(b => b != a)) all.Add(Named(tier.Name, a, b));
                }
        return all;
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("it")]
    public void TheFourLines_FitIn79Columns_WithTheLongestEliteNames_ForThePlayerAndAFollower(string lang)
    {
        var elites = EveryEliteName();
        // the ally of a mend: the five longest plain names and the longest caster in this language
        var allies = MonsterFamilies.AllFamilies.SelectMany(f => f.Tiers.Select(t => t.Name)
                .Concat(MonsterGenerator.CasterSpells.ContainsKey(f.FamilyName) ? f.Tiers.Where(t => t.MinLevel <= 10).Select(t => MonsterNames.FromKey("monster.role.caster", t.Name)) : Enumerable.Empty<string>()))
            .OrderByDescending(n => MonsterNames.DisplayIn(lang, n).Length).Take(6).ToList();
        int widest = 0, mostRows = 0, longest = 0;
        void Check(string key, params object[] args)
        {
            string text = Loc.GetIn(lang, key, args);
            longest = Math.Max(longest, text.Length);
            var player = UIHelper.MessageRows(text);   // UIHelper.WriteRow writes these
            string.Join(" ", player).Should().Be(text, "wrapping keeps every word");
            var follower = CombatEngine.GroupLineWrapped(lang, "\u001b[1;31m", key, args).Split('\n');
            mostRows = Math.Max(mostRows, Math.Max(player.Count, follower.Length));
            foreach (string row in player.Concat(follower))
            {
                int width = UIHelper.VisibleLength(row);
                widest = Math.Max(widest, width);
                width.Should().BeLessThanOrEqualTo(79, "{0} {1}: [{2}]", lang, key, row);
            }
        }
        foreach (var e in elites)
        {
            string name = MonsterNames.DisplayIn(lang, e);
            Check("combat.elite_frenzy", name);
            Check("combat.elite_command", name);
            Check("combat.elite_swift", name);
            foreach (string ally in allies) Check("combat.elite_mend", name, MonsterNames.DisplayIn(lang, ally), 123_456_789L);
        }
        widest.Should().BeGreaterThan(55);
        mostRows.Should().BeLessThanOrEqualTo(2, "the longest line of {0} characters wraps once at most", longest);
    }

    [Fact]
    public async Task AFollower_ReadsEachLine_InTheirOwnLanguage()
    {
        var prevSink = CombatEngine.GroupBroadcastSink;
        string saved = GameConfig.Language;
        try
        {
            GameConfig.Language = "en";   // the leader reads English
            var sent = new List<Func<string, string>>();
            CombatEngine.GroupBroadcastSink = (_, build) => sent.Add(build);
            // one elite with the three affixes that speak on its turn, a wounded ally, and Swift for the opening
            // the caster's name is built as the generator builds it (a stored name is shown through its key)
            var elite = Elite(MonsterNames.FromKey("monster.role.caster", "Kobold"), 1_000_000, 200, "frenzied", "commanding");
            elite.Name.Should().Be("Frenzied Commanding Kobold Shaman");
            elite.EliteAffixes.Add("mending"); elite.EliteAffixes.Add("swift");
            elite.HP = 400_000;
            var ally = Foe("Wolf", 1_000_000, 1);
            ally.HP = 10_000;
            var run = await G.Fight(G.Hero(), new List<Monster> { elite, ally }, script: Script(3, "1"));
            string Text(string lang) => string.Join("\n", sent.Select(b => Regex.Replace(b(lang), "\u001b\\[[0-9;]*m", "")));
            foreach (string lang in new[] { "es", "hu", "fr", "it", "en" })
            {
                string name = MonsterNames.DisplayIn(lang, elite);
                string text = Text(lang);
                text.Should().Contain(Loc.GetIn(lang, "combat.elite_swift", name), lang);
                text.Should().Contain(Loc.GetIn(lang, "combat.elite_frenzy", name), lang);
                text.Should().Contain(Loc.GetIn(lang, "combat.elite_command", name), lang);
                text.Should().Contain(Loc.GetIn(lang, "combat.elite_mend", name, MonsterNames.DisplayIn(lang, ally), 150_000L), lang);
                foreach (var b in sent)
                    foreach (string row in b(lang).Split('\n'))
                        UIHelper.VisibleLength(row).Should().BeLessThanOrEqualTo(79, "{0}: [{1}]", lang, row);
            }
            MonsterNames.DisplayIn("es", elite).Should().NotBe(elite.Name, "the name is the follower's too");
            Text("es").Should().Contain("¡Chamán Kobold al Mando en Frenesí entra en frenesí!");
            Text("hu").Should().NotContain("flies into a frenzy");
            // the leader reads English, once each
            var rows = Rows(run.Transcript);
            foreach (string key in new[] { "combat.elite_swift", "combat.elite_frenzy", "combat.elite_command" })
                rows.Count(r => r.Row == Loc.GetIn("en", key, elite.Name)).Should().Be(1, key);
            GameConfig.Language.Should().Be("en");
        }
        finally { CombatEngine.GroupBroadcastSink = prevSink; GameConfig.Language = saved; }
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("es")]
    [InlineData("fr")]
    public async Task TheEngine_WrapsTheLongestLine_ForThePlayerAndTheFollowers(string lang)
    {
        // the longest mend line of this language: a two-affix elite (Mending inside) and the longest ally name.
        // In hu, es and fr it is longer than 79 columns; in en (75 at most) and it no line ever needs a second row.
        var elite = EveryEliteName().Where(e => e.EliteAffixes.Count == 2 && e.EliteAffixes.Contains("mending"))
            .OrderByDescending(e => MonsterNames.DisplayIn(lang, e).Length).First();
        elite.HP = elite.MaxHP = 1_000_000; elite.Strength = 50;
        string allyTier = MonsterFamilies.AllFamilies.SelectMany(f => f.Tiers.Select(t => t.Name)).OrderByDescending(n => MonsterNames.DisplayIn(lang, n).Length).First();
        var ally = Foe(allyTier, 1_000_000, 1);
        ally.HP = 10_000;
        var prevSink = CombatEngine.GroupBroadcastSink;
        string saved = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            var sent = new List<Func<string, string>>();
            CombatEngine.GroupBroadcastSink = (_, build) => sent.Add(build);
            var run = await G.Fight(G.Hero(), new List<Monster> { elite, ally }, script: Script(2, "1"));
            string text = Loc.GetIn(lang, "combat.elite_mend", MonsterNames.DisplayIn(lang, elite), MonsterNames.DisplayIn(lang, ally), 150_000L);
            text.Length.Should().BeGreaterThan(79, "the case is a line that must wrap: {0}", text);
            var expected = UIHelper.MessageRows(text);
            expected.Count.Should().Be(2);
            var rows = Plain(run.Transcript).Split('\n').Select(r => r.TrimEnd()).ToList();
            foreach (string row in expected) rows.Should().Contain(row, run.Transcript);
            rows.Should().NotContain(text, "the player's line is wrapped, not written whole");
            foreach (string reader in Langs)
            {
                string mend = Loc.GetIn(reader, "combat.elite_mend", MonsterNames.DisplayIn(reader, elite), MonsterNames.DisplayIn(reader, ally), 150_000L);
                var followerRows = sent.SelectMany(b => b(reader).Split('\n')).Select(r => Regex.Replace(r, "\u001b\\[[0-9;]*m", "")).ToList();
                foreach (string row in followerRows) row.Length.Should().BeLessThanOrEqualTo(79, "{0}: [{1}]", reader, row);
                string.Join(" ", followerRows.Select(r => r.Trim())).Should().Contain(mend, reader);
            }
        }
        finally { CombatEngine.GroupBroadcastSink = prevSink; GameConfig.Language = saved; }
    }

    [Fact]
    public void InEnglish_NoLine_EverNeedsASecondRow()
    {
        var allies = MonsterFamilies.AllFamilies.SelectMany(f => f.Tiers.Select(t => t.Name)).OrderByDescending(n => n.Length).Take(3).ToList();
        int longest = 0;
        foreach (var e in EveryEliteName().Where(e => e.EliteAffixes.Count <= 2 && !(e.EliteAffixes.Count == 2 && e.Name.Contains("Shaman"))))
        {
            longest = Math.Max(longest, Loc.GetIn("en", "combat.elite_swift", e.Name).Length);
            foreach (string ally in allies) longest = Math.Max(longest, Loc.GetIn("en", "combat.elite_mend", e.Name, ally, 150_000L).Length);
        }
        longest.Should().BeInRange(60, 79);
    }
}
