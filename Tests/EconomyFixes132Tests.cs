using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using FluentAssertions.Execution;
using UsurperRemake;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.2: three economy faults.
/// 1. The Loan Shark's daily interest was added to both the principal and the interest owed, and the alley
///    asks for their sum, so the interest counted twice. Interest is now added to the interest owed only, on
///    the whole amount owed, so a loan still grows 20 percent a day.
/// 2. A lost bank robbery ran the full death pipeline. The fight is now a staged fight (IsExhibitionCombat),
///    so a loss reaches the bank's own outcome: 1 HP, the defeated header and the fine.
/// 3. Purifying a cursed helm, arm, hand, leg, foot, waist, face or cloak piece in the backpack kept its
///    curse boost. Every armor slot now loses the boost and takes the 20 percent purification cut.
/// All in memory.
/// </summary>
[Collection("SharedGameSingletons")]
public class EconomyFixes132Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly bool _pacingWas;
    private readonly Faction? _factionWas;

    public EconomyFixes132Tests()
    {
        _pacingWas = Pacing.Disabled;
        Pacing.Disabled = true;
        _factionWas = FactionSystem.Instance.PlayerFaction;
        SetFaction(null);   // no Faith divine favor in the robbery fight
        BankVaultSystem.StoreOverride = null;
        BankVaultSystem.Load(GameConfig.BankVaultInitial);
        BankVaultSystem.LoadRobberies(0, "");
    }

    public void Dispose()
    {
        Pacing.Disabled = _pacingWas;
        SetFaction(_factionWas);
        BankVaultSystem.StoreOverride = null;
        BankVaultSystem.Load(GameConfig.BankVaultInitial);
        BankVaultSystem.LoadRobberies(0, "");
    }

    private static void SetFaction(Faction? f) =>
        typeof(FactionSystem).GetProperty("PlayerFaction")!.SetValue(FactionSystem.Instance, f);

    private static Character Hero(int level = 10, long hp = 600, long maxHp = 600, int str = 60) => new Character
    {
        Name1 = "Tester", Name2 = "Tester", AI = CharacterAI.Human,
        Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = level,
        HP = hp, MaxHP = maxHp, BaseMaxHP = maxHp,
        Strength = str, BaseStrength = str, Defence = 20, BaseDefence = 20,
        Dexterity = 30, BaseDexterity = 30, Agility = 20, BaseAgility = 20,
        Constitution = 30, BaseConstitution = 30, Intelligence = 20, BaseIntelligence = 20,
        Wisdom = 20, BaseWisdom = 20, Stamina = 100, BaseStamina = 100,
        Mental = 80, Gold = 0, Healing = 0, AutoHeal = false,
        CombatSpeed = CombatSpeed.Instant, Resurrections = 3, BankRobberyAttempts = 3,
    };

    private static TerminalEmulator Term(IEnumerable<string> lines, MemoryStream output) =>
        new TerminalEmulator(new LineStream(lines), output);

    private static void Place(BaseLocation loc, TerminalEmulator term, Character hero)
    {
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(loc, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(loc, hero);
    }

    private static async Task Call(object target, string method, params object[] args)
    {
        var m = target.GetType().GetMethod(method, F);
        m.Should().NotBeNull(method);
        try
        {
            var task = (Task)m!.Invoke(target, args)!;
            (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(60)))).Should().BeSameAs(task, "{0} must return", method);
            await task;
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    // ---------- 1. the Loan Shark ----------

    private static long Owed(Character c) => c.LoanAmount + c.LoanInterestAccrued;

    private static async Task DailyStep(Character hero, string step)
    {
        var engine = GameEngine.Instance;
        var old = engine.CurrentPlayer;
        engine.CurrentPlayer = hero;
        try { await Call(DailySystemManager.Instance, step); }
        finally { engine.CurrentPlayer = old; }
    }

    [Theory]
    [InlineData("ProcessPlayerDailyEvents")]   // online
    [InlineData("ProcessDailyEvents")]         // single player
    public async Task Loan_Of1000_Owes1200AfterOneDay_And1440AfterTwo(string step)
    {
        var hero = Hero();
        hero.LoanAmount = 1000;
        hero.LoanDaysRemaining = 5;
        hero.LoanInterestAccrued = 0;

        await DailyStep(hero, step);
        using (new AssertionScope())
        {
            Owed(hero).Should().Be(1200, "a loan grows 20 percent a day");
            hero.LoanAmount.Should().Be(1000, "the principal stays until a payment reduces it");
            hero.LoanInterestAccrued.Should().Be(200);
            hero.LoanDaysRemaining.Should().Be(4);
        }

        await DailyStep(hero, step);
        using (new AssertionScope())
        {
            Owed(hero).Should().Be(1440, "the interest is charged on the whole amount owed");
            hero.LoanAmount.Should().Be(1000);
            hero.LoanInterestAccrued.Should().Be(440);
            hero.LoanDaysRemaining.Should().Be(3);
        }
    }

    [Fact]
    public async Task Loan_APayment_ClearsInterestFirst_ThenPrincipal()
    {
        var hero = Hero();
        hero.Achievements.Unlock("dark_alley_debt_free"); // already earned, so the payoff's gold is only the payment
        hero.Gold = 10_000;
        hero.LoanAmount = 1000;
        hero.LoanDaysRemaining = 3;
        hero.LoanInterestAccrued = 440;

        var alley = new DarkAlleyLocation();
        Place(alley, Term(new[] { "2", "100" }.Concat(Enumerable.Repeat("", 10)), new MemoryStream()), hero);
        await Call(alley, "VisitLoanShark");
        hero.LoanInterestAccrued.Should().Be(340, "a payment smaller than the interest only reduces the interest");
        hero.LoanAmount.Should().Be(1000);
        hero.Gold.Should().Be(9_900);

        Place(alley, Term(new[] { "2", "500" }.Concat(Enumerable.Repeat("", 10)), new MemoryStream()), hero);
        await Call(alley, "VisitLoanShark");
        hero.LoanInterestAccrued.Should().Be(0, "the interest is cleared first");
        hero.LoanAmount.Should().Be(840, "the rest comes off the principal");
        hero.Gold.Should().Be(9_400);
        hero.LoanDaysRemaining.Should().Be(3);

        Place(alley, Term(new[] { "1" }.Concat(Enumerable.Repeat("", 10)), new MemoryStream()), hero);
        await Call(alley, "VisitLoanShark");
        hero.LoanAmount.Should().Be(0, "a full repayment clears the loan");
        hero.Gold.Should().Be(8_560);
    }

    // ---------- 2. the bank robbery ----------

    private static IEnumerable<string> Fight(int attacks, bool deathMenuKeys) =>
        Enumerable.Repeat(new[] { "A", "" }, attacks).SelectMany(x => x)
            .Concat(Enumerable.Repeat("P", 10))
            .Concat(deathMenuKeys ? Enumerable.Repeat("1", 20) : Enumerable.Empty<string>())
            .Concat(Enumerable.Repeat("", 40));

    [Fact]
    public async Task Robbery_Lost_LeavesThePlayerAtOneHp_TakesTheFine_AndRecordsNoDeath()
    {
        // HP well under a quarter of the maximum: neither Last Stand nor Death's Door applies.
        var hero = Hero(level: 10, hp: 20);
        hero.Gold = 5_000;
        hero.BankGold = 20_000;
        int deathsBefore = hero.PlaythroughDeaths;
        int rezBefore = hero.Resurrections;
        int mentalBefore = hero.Mental;
        int levelBefore = hero.Level;

        var bank = new BankLocation();
        var output = new MemoryStream();
        Place(bank, Term(Fight(30, deathMenuKeys: true), output), hero);
        await Call(bank, "ExecuteRobbery", 4);

        using (new AssertionScope())
        {
            hero.PlaythroughDeaths.Should().Be(deathsBefore, "a lost robbery is not a death");
            hero.Resurrections.Should().Be(rezBefore, "no resurrection is used");
            hero.Mental.Should().Be(mentalBefore, "no death Mental loss");
            hero.Level.Should().Be(levelBefore, "no death penalty");
            hero.HP.Should().Be(1, "the bank leaves the robber at 1 HP");
            hero.Gold.Should().Be(0, "gold on hand is confiscated");
            hero.BankGold.Should().Be(20_000 - 10 * 500, "up to level x 500 is taken from the bank");
            hero.IsExhibitionCombat.Should().BeFalse("the staged fight flag is cleared after the fight");
        }
    }

    [Fact]
    public async Task Robbery_Won_TakesAQuarterOfTheVault_Unchanged()
    {
        var hero = Hero(level: 1, hp: 200_000, maxHp: 200_000, str: 20_000);
        hero.BankGold = 0;
        long vaultBefore = BankVaultSystem.Current;
        long take = BankVaultSystem.RobberyTake(vaultBefore, hero.BankGold);
        take.Should().BeGreaterThan(0);
        long goldBefore = hero.Gold;
        int deathsBefore = hero.PlaythroughDeaths;

        var bank = new BankLocation();
        var output = new MemoryStream();
        Place(bank, Term(Fight(60, deathMenuKeys: false), output), hero);
        await Call(bank, "ExecuteRobbery", 4);

        using (new AssertionScope())
        {
            BankVaultSystem.Current.Should().Be(vaultBefore - take, "the robbery takes a quarter of the vault");
            (hero.Gold - goldBefore).Should().BeGreaterThanOrEqualTo(take, "the take is paid to the robber");
            hero.HP.Should().BeGreaterThan(1);
            hero.PlaythroughDeaths.Should().Be(deathsBefore);
            hero.IsExhibitionCombat.Should().BeFalse();
        }
    }

    // ---------- 3. purifying a cursed item in the backpack ----------

    private static readonly ObjType[] ArmorSlots =
    {
        ObjType.Head, ObjType.Arms, ObjType.Hands, ObjType.Legs,
        ObjType.Feet, ObjType.Waist, ObjType.Face, ObjType.Abody,
    };

    private static Item Cursed(ObjType type)
    {
        var item = new Item
        {
            Name = "Cursed Test Piece", Type = type, Value = 1000,
            IsCursed = true, Cursed = true, Dungeon = true,
        };
        item.Description[1] = LootGenerator.CurseLine;
        return item;
    }

    private static async Task Purify(Item item)
    {
        var hero = Hero();
        hero.Gold = 10_000_000;
        hero.Inventory.Add(item);
        var shop = new MagicShopLocation();
        Place(shop, Term(new[] { "Y" }.Concat(Enumerable.Repeat("", 10)), new MemoryStream()), hero);
        await Call(shop, "RemoveCurseFromPlayerItem", hero, item);
        item.IsCursed.Should().BeFalse("the curse was removed for {0}", item.Type);
    }

    [Fact]
    public async Task Purify_EveryArmorSlot_LosesTheCurseBoost_AndTakesTheTwentyPercentCut()
    {
        const int uncursed = 100;
        using var scope = new AssertionScope();
        foreach (var slot in ArmorSlots)
        {
            var item = Cursed(slot);
            item.Armor = (int)(uncursed * 1.25f);   // as LootGenerator.CreateArmorFromTemplate
            int cursedArmor = item.Armor;
            await Purify(item);
            item.Armor.Should().Be((int)(cursedArmor / 1.25f * 0.80f), "{0} takes the same cut as body armor", slot);
            item.Armor.Should().BeCloseTo((int)(uncursed * 0.80f), 1, "{0} keeps 80 percent of its uncursed armor", slot);
        }
    }

    [Fact]
    public async Task Purify_WeaponBodyShieldRingAndNecklace_Unchanged()
    {
        var weapon = Cursed(ObjType.Weapon);
        weapon.Attack = 125;
        await Purify(weapon);
        weapon.Attack.Should().Be((int)(125 / 1.25f * 0.80f));

        var body = Cursed(ObjType.Body);
        body.Armor = 125;
        await Purify(body);
        body.Armor.Should().Be((int)(125 / 1.25f * 0.80f));

        var shield = Cursed(ObjType.Shield);
        shield.Armor = 125;
        shield.ShieldBonus = 40;
        await Purify(shield);
        shield.Armor.Should().Be((int)(125 / 1.25f * 0.80f));
        shield.ShieldBonus.Should().Be(40);

        foreach (var type in new[] { ObjType.Fingers, ObjType.Neck })
        {
            var acc = Cursed(type);
            acc.Strength = 130;
            acc.HP = 50;
            acc.Armor = 10;
            int penalty = Math.Max(5, acc.Attack / 10);
            await Purify(acc);
            acc.Strength.Should().Be((int)((130 + penalty / 2) / 1.3f * 0.80f), "{0}", type);
            acc.HP.Should().Be((int)(50 / 1.3f * 0.80f), "{0}", type);
            acc.Armor.Should().Be(10, "an accessory's armor is not cut, {0}", type);
        }
    }
}
