using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;
using G = UsurperReborn.Tests.HitRoll128Golden;
using P = UsurperReborn.Tests.PartyReach136Tests;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.6 X1: the stamina cost a player is shown is the cost charged (ClassAbilitySystem.GetEffectiveStaminaCost: 25
/// percent more from level 50, 40 percent more from level 75, rounded down) on the use line, the quickbar label, the
/// ability menus, the teammate line and the quickbar refusal lines; the charge itself does not change. The weapon
/// requirement text is the ability.requires_weapon key with the game's own weapon type names. The wiki's class roles
/// table states the tier rule, and its ability table shows the rounds Cutting Words and Legendary Shot hold.
/// </summary>
[Collection("SharedGameSingletons")]
public class ShownCosts136Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>Warrior rows on each side of a tier: Savage Charge (level 47, 45), Whirlwind (level 55, 60), Champion
    /// Strike (level 100, 100), and the stamina each is charged.</summary>
    public static IEnumerable<object[]> Tiers() => new[]
    {
        new object[] { "savage_charge", 45 },
        new object[] { "whirlwind", 75 },
        new object[] { "champion_strike", 140 },
    };

    public static IEnumerable<object[]> AllLanguages() => new[] { "en", "es", "fr", "hu", "it" }.Select(l => new object[] { l });

    private static string Strip(string s) => Regex.Replace(s, "\u001b\\[[0-9;]*[A-Za-z]", "");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj"))) dir = dir.Parent;
        return dir!.FullName;
    }

    private static Character Warrior(long stamina, params string[] quickbar)
    {
        var hero = G.Hero(hp: 1_000_000, level: 100);
        hero.CurrentCombatStamina = stamina;
        for (int i = 0; i < quickbar.Length; i++) hero.Quickbar[i] = quickbar[i];
        return hero;
    }

    private static ClassAbilitySystem.ClassAbility Made(int level, int stamina, int mana = 0) => new()
    {
        Id = "shown_costs_136", Name = "Test Row", LevelRequired = level, StaminaCost = stamina, ManaCost = mana,
        AvailableToClasses = new[] { CharacterClass.Warrior },
    };

    private static List<(string key, string slotId, string displayName, bool available)> Quickbar(Character c)
    {
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        return (List<(string, string, string, bool)>)typeof(CombatEngine).GetMethod("GetQuickbarActions", F)!.Invoke(engine, new object[] { c })!;
    }

    // ---------------------------------------------------------------- the charge, unchanged

    [Theory]
    [InlineData(49, 90, 90)]
    [InlineData(50, 90, 112)]
    [InlineData(74, 90, 112)]
    [InlineData(75, 90, 125)]
    [InlineData(75, 85, 118)]
    [InlineData(75, 100, 140)]
    [InlineData(50, 100, 125)]
    public void ShownCosts136_TheCharge_IsUnchanged_AtEachTierEdge(int level, int template, int charged)
    {
        ClassAbilitySystem.GetEffectiveStaminaCost(Made(level, template)).Should().Be(charged, "the tier multiply rounds down");
    }

    [Theory]
    [MemberData(nameof(Tiers))]
    public void ShownCosts136_TheRealRows_AreChargedTheTierCost(string id, int charged)
    {
        ClassAbilitySystem.GetEffectiveStaminaCost(ClassAbilitySystem.GetAbility(id)!).Should().Be(charged);
    }

    // ---------------------------------------------------------------- the menu cost text

    [Theory]
    [InlineData("en", 49, 90, 90)]
    [InlineData("en", 50, 90, 112)]
    [InlineData("hu", 75, 90, 125)]
    [InlineData("fr", 75, 85, 118)]
    public void ShownCosts136_AbilityCostText_ShowsTheCharge(string lang, int level, int template, int charged)
    {
        using (Loc.RenderLanguage(lang))
        {
            CombatEngine.AbilityCostText(Made(level, template)).Should().Be(Loc.GetIn(lang, "combat.ability_cost_stamina", charged));
            CombatEngine.AbilityCostText(Made(level, template, mana: 20))
                .Should().Be(Loc.GetIn(lang, "combat.ability_cost_stamina_mana", charged, 20));
        }
    }

    // ---------------------------------------------------------------- the quickbar label

    [Theory]
    [MemberData(nameof(Tiers))]
    public void ShownCosts136_QuickbarLabel_ShowsTheCharge(string id, int charged)
    {
        var ability = ClassAbilitySystem.GetAbility(id)!;
        var slot = Quickbar(Warrior(10_000, id)).Single(a => a.slotId == id);
        slot.displayName.Should().Be(Loc.Get("combat.qb_stamina", ability.DisplayName, charged));
    }

    // ---------------------------------------------------------------- the ability menus

    [Fact]
    public async Task ShownCosts136_AbilityMenu_ShowsTheCharge()
    {
        var hero = Warrior(0);
        var output = new MemoryStream();
        var term = new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes("\n")), output);
        var engine = new CombatEngine(term);
        await (Task)typeof(CombatEngine).GetMethod("ShowAbilityMenuAndExecute", F)!
            .Invoke(engine, new object?[] { hero, new List<Monster> { G.Dummy() }, new CombatResult { Player = hero } })!;
        term.StreamWriterInternal?.Flush();
        string text = Strip(Encoding.UTF8.GetString(output.ToArray()));
        foreach (var row in Tiers())
        {
            var ability = ClassAbilitySystem.GetAbility((string)row[0])!;
            int charged = (int)row[1];
            text.Should().Contain($"{ability.DisplayName} - {Loc.Get("combat.ability_cost_stamina", charged)}{Loc.Get("combat.ability_tag_need_stamina", charged)}",
                ability.Id);
        }
    }

    [Fact]
    public async Task ShownCosts136_PvpAbilityMenu_ShowsTheCharge()
    {
        var hero = Warrior(0);
        var foe = G.Hero(hp: 1_000_000, level: 100, name: "Foe");
        var output = new MemoryStream();
        var term = new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes("\n")), output);
        var engine = new CombatEngine(term);
        await (Task)typeof(CombatEngine).GetMethod("ExecutePvPAbility", F)!
            .Invoke(engine, new object?[] { hero, foe, new CombatAction(), new CombatResult { Player = hero } })!;
        term.StreamWriterInternal?.Flush();
        string text = Strip(Encoding.UTF8.GetString(output.ToArray()));
        foreach (var row in Tiers())
        {
            var ability = ClassAbilitySystem.GetAbility((string)row[0])!;
            int charged = (int)row[1];
            text.Should().Contain($"{ability.DisplayName} - {Loc.Get("combat.ability_cost_stamina", charged)}{Loc.Get("combat.ability_tag_need_stamina_have", charged, 0)}",
                ability.Id);
        }
    }

    // ---------------------------------------------------------------- the use line

    [Theory]
    [MemberData(nameof(Tiers))]
    public async Task ShownCosts136_UseLine_ShowsTheCharge_AndChargesIt(string id, int charged)
    {
        var hero = Warrior(10_000);
        var rig = P.NewRig(hero, new List<Character>(), monsters: G.Dummy());
        var ability = ClassAbilitySystem.GetAbility(id)!;
        bool saved = GameConfig.PlayerHitRollEnabled;
        try
        {
            GameConfig.PlayerHitRollEnabled = false;
            await (Task)typeof(CombatEngine).GetMethod("ExecuteUseAbilityMultiMonster", F)!
                .Invoke(rig.Engine, new object?[] { hero, rig.Monsters, new CombatAction { Type = CombatActionType.UseAbility, AbilityId = id }, rig.Result })!;
        }
        finally { GameConfig.PlayerHitRollEnabled = saved; }
        hero.CurrentCombatStamina.Should().Be(10_000 - charged, "the charge does not change");
        Strip(rig.Shown()).Should().Contain(Loc.Get("combat.ability_use", hero.Name2, ability.DisplayName, charged));
    }

    // ---------------------------------------------------------------- the teammate line

    [Theory]
    [MemberData(nameof(Tiers))]
    public void ShownCosts136_TeammateLine_ShowsTheCharge_AndChargesIt(string id, int charged)
    {
        var hero = Warrior(10_000);
        var mate = G.Mate("Mate", hp: 1_000_000, level: 100);
        var rig = P.NewRig(hero, new List<Character> { mate }, monsters: G.Dummy());
        hero.TeammateDisabledAbilities[mate.GetSkillToggleKey()] =
            ClassAbilitySystem.GetAvailableAbilities(mate).Select(a => a.Id).Where(a => a != id).ToList();
        var ability = ClassAbilitySystem.GetAbility(id)!;
        bool used = false;
        for (int i = 0; i < 200 && !used; i++)
        {
            mate.CurrentCombatStamina = 10_000;
            ((System.Collections.IDictionary)typeof(CombatEngine).GetField("teammateCooldowns", F)!.GetValue(rig.Engine)!).Clear();
            used = ((Task<bool>)typeof(CombatEngine).GetMethod("TryTeammateClassAbility", F)!
                .Invoke(rig.Engine, new object?[] { mate, rig.Monsters, rig.Result })!).GetAwaiter().GetResult();
        }
        used.Should().BeTrue("the teammate used its one allowed ability");
        mate.CurrentCombatStamina.Should().Be(10_000 - charged, "the charge does not change");
        Strip(rig.Shown()).Should().Contain(Loc.Get("combat.teammate_uses_ability_stamina", mate.DisplayName, ability.DisplayName, charged));
    }

    // ---------------------------------------------------------------- the quickbar refusal lines

    /// <summary>The refusal text of quickbar key 1 holding <paramref name="id"/>, on the multi-monster and the
    /// single-monster quickbar.</summary>
    private static (string multi, string single) Refusals(Character hero)
    {
        string Run(string method, object target)
        {
            var output = new MemoryStream();
            var term = new TerminalEmulator(new MemoryStream(), output);
            var engine = new CombatEngine(term);
            typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, hero);
            var task = (Task)typeof(CombatEngine).GetMethod(method, F)!.Invoke(engine, new object[] { hero, "1", target })!;
            task.GetAwaiter().GetResult();
            term.StreamWriterInternal?.Flush();
            return Strip(Encoding.UTF8.GetString(output.ToArray()));
        }
        return (Run("HandleQuickbarAction", new List<Monster> { G.Dummy() }), Run("HandleQuickbarActionSingleMonster", G.Dummy()));
    }

    [Theory]
    [MemberData(nameof(Tiers))]
    public void ShownCosts136_RefusalLine_ShowsTheCharge(string id, int charged)
    {
        // one short of the charge: at or above the template from level 50, so the old line printed nothing there
        var hero = Warrior(charged - 1, id);
        var (multi, single) = Refusals(hero);
        string want = Loc.Get("combat.not_enough_stamina", charged, charged - 1);
        multi.Should().Contain(want);
        single.Should().Contain(want);
    }

    // ---------------------------------------------------------------- the weapon requirement text

    private static readonly WeaponType[] ShamanWeapons =
        { WeaponType.Sword, WeaponType.Axe, WeaponType.Mace, WeaponType.Hammer, WeaponType.Flail, WeaponType.Maul };

    private static Character Shaman(params string[] quickbar)
    {
        var c = G.Hero(hp: 1_000_000, level: 100, cls: CharacterClass.MysticShaman, name: "Shaman");
        c.CurrentCombatStamina = 10_000;
        c.MaxMana = 10_000; c.Mana = 10_000;
        for (int i = 0; i < quickbar.Length; i++) c.Quickbar[i] = quickbar[i];
        return c;
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void ShownCosts136_WeaponRequirement_IsInThePlayersLanguage(string lang)
    {
        using (Loc.RenderLanguage(lang))
        {
            string names = string.Join("/", ShamanWeapons.Select(w => Loc.GetIn(lang, $"weapon_type.{w.ToString().ToLowerInvariant()}")));
            string want = Loc.GetIn(lang, "ability.requires_weapon", names);
            foreach (var id in new[] { "flametongue", "rockbiter", "frostbrand", "stormstrike" })
            {
                var ability = ClassAbilitySystem.GetAbility(id)!;
                ability.RequiredWeaponTypes.Should().Equal(ShamanWeapons, id);
                var shaman = Shaman(id);
                string reason = ClassAbilitySystem.GetWeaponRequirementReason(shaman, ability)!;
                reason.Should().Be(want, id);
                Quickbar(shaman).Single(a => a.slotId == id).displayName.Should().Be($"{ability.DisplayName} ({want})", id);
                var (multi, single) = Refusals(shaman);
                string refusal = Loc.GetIn(lang, "combat.ability_weapon_reason", ability.DisplayName, want);
                multi.Should().Contain(refusal, id);
                single.Should().Contain(refusal, id);
                if (lang != "en")
                    (reason + multi).Should().NotContain("Requires").And.NotContain("Sword").And.NotContain("Hammer");
            }
            var warrior = G.Hero(hp: 1_000_000, level: 100);
            ClassAbilitySystem.GetWeaponRequirementReason(warrior, ClassAbilitySystem.GetAbility("shield_wall")!)
                .Should().Be(Loc.GetIn(lang, "ability.requires_weapon", Loc.GetIn(lang, "weapon_type.shield")));
            if (lang != "en")
                ClassAbilitySystem.GetWeaponRequirementReason(warrior, ClassAbilitySystem.GetAbility("shield_wall")!)
                    .Should().NotContain("Requires").And.NotContain("Shield");
        }
    }

    [Fact]
    public void ShownCosts136_WeaponRequirement_HasNoEnglishInTheSource()
    {
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Systems", "ClassAbilitySystem.cs"));
        src.Should().NotContain("$\"Requires ").And.NotContain("\"Requires Shield\"");
    }

    /// <summary>Every requirement row, in English and Hungarian: the boxed quickbar row ("║ [9] " plus the label padded
    /// to 34, then "║") and the refusal line fit 79 columns.</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void ShownCosts136_RequirementRows_Fit79Columns(string lang)
    {
        using (Loc.RenderLanguage(lang))
        {
            var bare = new Dictionary<CharacterClass, Character>();
            int checkedRows = 0;
            foreach (var ability in ClassAbilitySystem.BuiltInTemplate().Select(v => ClassAbilitySystem.GetAbility(v.Id)!))
            {
                if ((ability.RequiredWeaponTypes == null || ability.RequiredWeaponTypes.Length == 0) && !ability.RequiresShield) continue;
                var who = G.Hero(hp: 1_000_000, level: 100, cls: ability.AvailableToClasses[0]);
                string reason = ClassAbilitySystem.GetWeaponRequirementReason(who, ability)!;
                reason.Should().NotBeNull(ability.Id);
                string box = $"║ [9] {$"{ability.DisplayName} ({reason})",-34}║";
                box.Length.Should().BeLessThanOrEqualTo(79, box);
                string refusal = Loc.Get("combat.ability_weapon_reason", ability.DisplayName, reason);
                refusal.Length.Should().BeLessThanOrEqualTo(79, refusal);
                checkedRows++;
            }
            checkedRows.Should().BeGreaterThan(20);
        }
    }
}
