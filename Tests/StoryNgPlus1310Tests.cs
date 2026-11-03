using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.10 audit, story and New Game+ (ids A1 to A9). The New Game+ bonuses the ending screen names apply where
/// their text says; the artifact reveal shows floors in the journal and the Ancient Key lines promise no door;
/// the chapter, the story bits and the major choices survive a save and reload; Marcus and Greta read the Old
/// God states; the older True Ending rule asks the Blood Price; Mira reads Veloura's state; boss hints and the
/// Stranger step clear; an Old God's stance is the one chosen in this attempt; the secret boss wins promise
/// nothing the game does not give. State is built in memory; the meta progression lives in a temporary folder.
/// </summary>
[Collection("SharedGameSingletons")]
public class StoryNgPlus1310Tests : IDisposable
{
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags SP = BindingFlags.Public | BindingFlags.Static;
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly Type[] Swapped =
    {
        typeof(StoryProgressionSystem), typeof(OceanPhilosophySystem), typeof(GriefSystem), typeof(CycleSystem),
        typeof(TownNPCStorySystem),
    };
    private static readonly FieldInfo MetaField = typeof(MetaProgressionSystem).GetField("_fallbackInstance", SNP)!;
    private readonly List<(FieldInfo Field, object? Old)> _old;
    private readonly object? _oldMeta = MetaField.GetValue(null);
    private readonly DifficultyMode _oldDifficulty = DifficultySystem.CurrentDifficulty;
    private readonly string _language = GameConfig.Language;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"usurper-ngplus-{Guid.NewGuid():N}");

    public StoryNgPlus1310Tests()
    {
        Directory.CreateDirectory(_dir);
        GameConfig.Language = "en";
        DifficultySystem.CurrentDifficulty = DifficultyMode.Normal;
        _old = Swapped.Select(t => t.GetField("_fallbackInstance", SNP)!).Select(f => (f, f.GetValue(null))).ToList();
        foreach (var t in Swapped)
            t.GetField("_fallbackInstance", SNP)!.SetValue(null, Activator.CreateInstance(t));
        var meta = (MetaProgressionSystem)RuntimeHelpers.GetUninitializedObject(typeof(MetaProgressionSystem));
        typeof(MetaProgressionSystem).GetField("data", F)!.SetValue(meta, new MetaProgressionData());
        typeof(MetaProgressionSystem).GetField("saveFilePath", F)!.SetValue(meta, Path.Combine(_dir, "saves", "meta_progression.json"));
        MetaField.SetValue(null, meta);
    }

    public void Dispose()
    {
        foreach (var (f, v) in _old) f.SetValue(null, v);
        MetaField.SetValue(null, _oldMeta);
        DifficultySystem.CurrentDifficulty = _oldDifficulty;
        GameConfig.Language = _language;
        try { Directory.Delete(_dir, true); } catch { }
    }

    // ---------- helpers ----------

    private static StoryProgressionSystem Story => StoryProgressionSystem.Instance;

    private static void Unlock(params string[] bonuses)
    {
        foreach (var b in bonuses) MetaProgressionSystem.Instance.UnlockedBonuses.Add(b);
    }

    private static string Src(params string[] path) =>
        File.ReadAllText(Path.Combine(new[] { UsurperReborn.Tests.Localization.HardcodedTextScannerTests.RepoRoot() }.Concat(path).ToArray()));

    private static string En(string key) => Loc.GetIn("en", key);

    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    private static MethodInfo Method(Type t, string name, BindingFlags flags)
    {
        var m = t.GetMethod(name, flags);
        m.Should().NotBeNull($"{t.Name}.{name} exists");
        return m!;
    }

    private static Character Hero(CharacterAI ai = CharacterAI.Human) => new Character
    {
        Name1 = "NgPlusHero", Name2 = "NgPlusHero", AI = ai, Level = 30, Class = CharacterClass.Warrior,
        BaseStrength = 100, BaseDexterity = 100, BaseConstitution = 100, BaseIntelligence = 100, BaseWisdom = 100,
        BaseCharisma = 100, BaseDefence = 100, BaseStamina = 100, BaseAgility = 100, BaseMaxHP = 1000, HP = 1000,
    };

    // ---------- A1: the New Game+ bonuses apply ----------

    [Fact]
    public void A1_BlessedCommerce_TakesTenPercentOffTheShopPrice()
    {
        Story.CurrentCycle = 2;   // an NG+ character
        DifficultySystem.ApplyShopPriceMultiplier(1000).Should().Be(1000, "no unlock, no discount");
        Unlock("BLESSED_COMMERCE");
        DifficultySystem.ApplyShopPriceMultiplier(1000).Should().BeInRange(899, 900, "the shops that take the difficulty step");
        DifficultySystem.GetShopPriceMultiplier().Should().BeApproximately(0.9f, 0.0001f, "the getter the price steps read");
        DifficultySystem.CurrentDifficulty = DifficultyMode.Hard;
        DifficultySystem.ApplyShopPriceMultiplier(1000).Should().BeInRange(1034, 1035, "on top of the difficulty");
    }

    [Fact]
    public void A1_BlessedCommerce_ReachesTheDarkAlleyPricePaths()
    {
        var src = Src("Scripts", "Locations", "DarkAlleyLocation.cs");
        foreach (var method in new[] { "private long GetAdjustedPrice(long basePrice)", "private long ComputeBlackMarketGearPrice(" })
        {
            int start = src.IndexOf(method, StringComparison.Ordinal);
            start.Should().BePositive(method);
            int end = src.IndexOf("\n        }", start, StringComparison.Ordinal);
            src.Substring(start, end - start).Should().Contain("MetaProgressionSystem.Instance.GetShopDiscount()", method);
        }
    }

    [Fact]
    public void A1_TyrantsAura_AddsFifteenPercentDamage()
    {
        Story.CurrentCycle = 2;   // an NG+ character
        DifficultySystem.ApplyPlayerDamageMultiplier(1000, ownDamage: true).Should().Be(1000);
        Unlock("TYRANTS_AURA");
        DifficultySystem.ApplyPlayerDamageMultiplier(1000, ownDamage: true).Should().BeInRange(1149, 1150);
    }

    [Fact]
    public void A1_TyrantsAura_LeavesTeammateDamageUnchanged()
    {
        Story.CurrentCycle = 2;
        Unlock("TYRANTS_AURA");
        DifficultySystem.ApplyPlayerDamageMultiplier(1000, ownDamage: false).Should().Be(1000, "a teammate's damage");
        DifficultySystem.ApplyPlayerDamageMultiplier(1000).Should().Be(1000, "the one-argument step is the teammate's");

        var src = Src("Scripts", "Systems", "CombatEngine.cs");
        int tm = src.IndexOf("private async Task ProcessTeammateActionMultiMonster(", StringComparison.Ordinal);
        int tmEnd = src.IndexOf("\n    private ", tm + 1, StringComparison.Ordinal);
        var teammate = src.Substring(tm, tmEnd - tm);
        teammate.Should().Contain("DifficultySystem.ApplyPlayerDamageMultiplier(Math.Max(1, tmPower));");
        teammate.Should().NotContain("OwnDamage(");
        var calls = System.Text.RegularExpressions.Regex.Matches(src, @"DifficultySystem\.ApplyPlayerDamageMultiplier\((.*)\);")
            .Select(m => m.Groups[1].Value).ToList();
        calls.Should().HaveCount(17);
        calls.Where(c => c.Contains("OwnDamage(")).Should().HaveCount(16, "every other site asks whose damage it is");
        src.Should().Contain("private bool OwnDamage(Character attacker) => ReferenceEquals(attacker, currentPlayer);");
    }

    /// <summary>Every unlock the cycle gate covers, held by a cycle 1 character.</summary>
    private static readonly string[] Gated =
        { "BLESSED_COMMERCE", "TYRANTS_AURA", "HEALING_LIGHT", "OCEANS_BLESSING", "SLAYER", "SEAL_RESONANCE", "ARTIFACT_MEMORY" };

    private static void NoGatedBonus(string because)
    {
        DifficultySystem.ApplyShopPriceMultiplier(1000).Should().Be(1000, because);
        DifficultySystem.ApplyPlayerDamageMultiplier(1000, ownDamage: true).Should().Be(1000, because);
        DifficultySystem.ApplyHealingMultiplier(1000).Should().Be(1000, because);
        var hero = Hero();
        hero.RecalculateStats();
        hero.Strength.Should().Be(100, because);
        ((double)typeof(MonsterGenerator).GetMethod("EliteRollChance", SNP | SP)!.Invoke(null, new object[] { 40 })!)
            .Should().Be(GameConfig.EliteChanceFloors31Plus, because);
        var seal = SevenSealsSystem.Instance.GetAllSeals().First();
        ((long)typeof(SevenSealsSystem).GetMethod("RewardXPFor", SNP | SP)!.Invoke(null, new object[] { seal })!)
            .Should().Be(seal.RewardXP, because);
        ArtifactRows().Should().BeEmpty(because);
    }

    private static void EveryGatedBonus(string because)
    {
        DifficultySystem.ApplyShopPriceMultiplier(1000).Should().BeInRange(899, 900, because);
        DifficultySystem.ApplyPlayerDamageMultiplier(1000, ownDamage: true).Should().BeInRange(1149, 1150, because);
        DifficultySystem.ApplyHealingMultiplier(1000).Should().Be(1250, because);
        var hero = Hero();
        hero.RecalculateStats();
        hero.Strength.Should().Be(115, because);
        ((double)typeof(MonsterGenerator).GetMethod("EliteRollChance", SNP | SP)!.Invoke(null, new object[] { 40 })!)
            .Should().BeApproximately(GameConfig.EliteChanceFloors31Plus * 1.25, 1e-9, because);
        var seal = SevenSealsSystem.Instance.GetAllSeals().First();
        ((long)typeof(SevenSealsSystem).GetMethod("RewardXPFor", SNP | SP)!.Invoke(null, new object[] { seal })!)
            .Should().Be(seal.RewardXP * 2L, because);
        ArtifactRows().Should().HaveCount(7, because);
    }

    [Fact]
    public void A1_ACycle1CharacterMadeAfterAnEnding_GetsNoneOfTheGatedUnlocks_ACycle2CharacterGetsEach()
    {
        Unlock(Gated);
        Story.CurrentCycle.Should().Be(1, "a new character that is not NG+");
        NoGatedBonus("a cycle 1 character");
        Story.CurrentCycle = 2;
        EveryGatedBonus("a cycle 2 character");
    }

    [Fact]
    public void A1_Online_TheGate_ReadsTheSessionsCharacter_AndItsAccountsUnlocks()
    {
        MetaProgressionSystem AccountMeta(params string[] bonuses)
        {
            var meta = (MetaProgressionSystem)RuntimeHelpers.GetUninitializedObject(typeof(MetaProgressionSystem));
            var data = new MetaProgressionData();
            foreach (var b in bonuses) data.UnlockedBonuses.Add(b);
            typeof(MetaProgressionSystem).GetField("data", F)!.SetValue(meta, data);
            typeof(MetaProgressionSystem).GetField("saveFilePath", F)!.SetValue(meta, Path.Combine(_dir, "saves", "meta_progression_acct.json"));
            return meta;
        }
        var old = UsurperRemake.Server.SessionContext.Current;
        try
        {
            // the fallback (single-player) story is NG+ and holds every unlock: the session reads neither
            Story.CurrentCycle = 2;
            Unlock(Gated);
            var session = new UsurperRemake.Server.SessionContext { Username = "NgPlusAccount", CharacterKey = "NgPlusAccount" };
            session.InitializeSystems();
            session.Story = new StoryProgressionSystem();
            session.MetaProgression = AccountMeta(Gated);
            UsurperRemake.Server.SessionContext.Current = session;
            NoGatedBonus("the session's character is on cycle 1");
            session.Story.CurrentCycle = 2;
            EveryGatedBonus("the session's character is on cycle 2 and its account holds the unlocks");

            var other = new UsurperRemake.Server.SessionContext { Username = "OtherAccount", CharacterKey = "OtherAccount" };
            other.InitializeSystems();
            other.Story = new StoryProgressionSystem { CurrentCycle = 2 };
            other.MetaProgression = AccountMeta();
            UsurperRemake.Server.SessionContext.Current = other;
            NoGatedBonus("another account's unlocks are not this account's");
        }
        finally { UsurperRemake.Server.SessionContext.Current = old; }
    }

    [Fact]
    public void A1_HealingLight_AddsTwentyFivePercentHealing()
    {
        Story.CurrentCycle = 2;   // an NG+ character
        DifficultySystem.ApplyHealingMultiplier(1000).Should().Be(1000);
        Unlock("HEALING_LIGHT");
        DifficultySystem.ApplyHealingMultiplier(1000).Should().Be(1250);
    }

    [Fact]
    public void A1_OceansBlessing_AddsFifteenPercentToThePlayersStats_NotToAnNpc()
    {
        Story.CurrentCycle = 2;   // an NG+ character
        var before = Hero();
        before.RecalculateStats();
        before.Strength.Should().Be(100);

        Unlock("OCEANS_BLESSING");
        var hero = Hero();
        hero.RecalculateStats();
        new[] { hero.Strength, hero.Dexterity, hero.Constitution, hero.Intelligence, hero.Wisdom, hero.Charisma,
                hero.Stamina, hero.Agility }.Should().OnlyContain(v => v == 115);
        hero.Defence.Should().BeGreaterThan(before.Defence, "Defence gets the 15 percent before its Agility part");
        hero.BaseStrength.Should().Be(100, "the base is not changed, so the bonus is never stored");
        hero.RecalculateStats();
        hero.Strength.Should().Be(115, "a second recalculation does not stack it");

        var npc = Hero(CharacterAI.Computer);
        npc.RecalculateStats();
        npc.Strength.Should().Be(100, "an NPC does not hold the player's unlocks");
    }

    [Fact]
    public void A1_MortalPride_GivesTwentyFivePercentXp_WithoutDoubling()
    {
        var plain = Hero();
        CycleSystem.Instance.ApplyCycleBonusesToNewCharacter(plain, 2, EndingType.Savior);
        plain.CycleExpMultiplier.Should().BeApproximately(1.1f, 0.0001f, "the cycle base alone");

        Unlock("MORTAL_PRIDE");
        var defiant = Hero();
        CycleSystem.Instance.ApplyCycleBonusesToNewCharacter(defiant, 2, EndingType.Defiant);
        defiant.CycleExpMultiplier.Should().BeApproximately(1.35f, 0.0001f, "the Defiant +25 percent is not added twice");

        var later = Hero();
        CycleSystem.Instance.ApplyCycleBonusesToNewCharacter(later, 3, EndingType.Savior);
        later.CycleExpMultiplier.Should().BeApproximately(1.45f, 0.0001f, "a later cycle keeps the +25 percent");

        var trueEnding = Hero();
        CycleSystem.Instance.ApplyCycleBonusesToNewCharacter(trueEnding, 2, EndingType.TrueEnding);
        trueEnding.CycleExpMultiplier.Should().BeApproximately(1.6f, 0.0001f, "the larger ending bonus wins, nothing is added");

        En("ending.unlock_mortal_desc").Should().Contain("+25%");
    }

    [Fact]
    public void A1_Slayer_MakesElitesTwentyFivePercentMoreLikely()
    {
        Story.CurrentCycle = 2;   // an NG+ character
        var roll = Method(typeof(MonsterGenerator), "EliteRollChance", SNP | SP);
        double baseChance = (double)roll.Invoke(null, new object[] { 40 })!;
        baseChance.Should().Be(GameConfig.EliteChanceFloors31Plus);
        Unlock("SLAYER");
        ((double)roll.Invoke(null, new object[] { 40 })!).Should().BeApproximately(GameConfig.EliteChanceFloors31Plus * 1.25, 1e-9);
        Unlock("LEGEND_SLAYER");
        ((double)roll.Invoke(null, new object[] { 40 })!).Should().BeApproximately(GameConfig.EliteChanceFloors31Plus * 1.25, 1e-9,
            "the screen states 25 percent and nothing more");

        Src("Scripts", "Systems", "MonsterGenerator.cs").Should().Contain("random.NextDouble() < EliteRollChance(rules!.RealFloor)");
    }

    [Fact]
    public void A1_SealResonance_DoublesTheSealExperience()
    {
        Story.CurrentCycle = 2;   // an NG+ character
        var seal = SevenSealsSystem.Instance.GetAllSeals().First();
        var reward = Method(typeof(SevenSealsSystem), "RewardXPFor", SNP | SP);
        ((long)reward.Invoke(null, new object[] { seal })!).Should().Be(seal.RewardXP);
        Unlock("SEAL_RESONANCE");
        ((long)reward.Invoke(null, new object[] { seal })!).Should().Be(seal.RewardXP * 2L);
        En("ending.unlock_seal_desc").Should().Contain("experience");
    }

    [Fact]
    public void A1_TheUnlockRows_SayWhatTheCodeDoes()
    {
        foreach (var lang in Langs)
            Loc.GetIn(lang, "ending.unlock_fear_desc").Should().NotContain("10%" + " chance", lang);
        En("ending.unlock_fear_desc").Should().Contain("10%").And.Contain("damage");
        En("ending.unlock_seal_master_desc").Should().NotContainEquivalentOf("minimap");
        En("ending.unlock_key_desc").Should().NotContainEquivalentOf("shortcut");
        foreach (var lang in Langs)
            foreach (var key in new[] { "ending.unlock_fear_desc", "ending.unlock_mortal_desc", "ending.unlock_seal_desc",
                                        "ending.unlock_seal_master_desc", "ending.unlock_key_desc" })
                (Loc.GetIn(lang, key).Length + 4).Should().BeLessThanOrEqualTo(79, $"{lang} {key} fits its row");
    }

    // ---------- A2: the artifact reveal and the Ancient Key ----------

    private static List<string> ArtifactRows()
    {
        var m = Method(typeof(DungeonLocation), "ArtifactLocationRows", SNP | SP);
        return (List<string>)m.Invoke(null, new object[] { Story })!;
    }

    [Fact]
    public void A2_ArtifactReveal_ListsTheFloors_WhenTheFlagIsSet()
    {
        ArtifactRows().Should().BeEmpty("no reveal without the flag or the unlock");

        Story.SetStoryFlag("knows_artifact_locations", true);
        var rows = ArtifactRows();
        rows.Should().HaveCount(7);
        foreach (var floor in new[] { 25, 40, 55, 70, 85, 95 })
            rows.Should().Contain(r => r.Contains($" {floor}"), $"the floor {floor} artifact is listed");

        Story.CollectArtifact(ArtifactType.CreatorsEye);
        ArtifactRows().Should().HaveCount(6, "an artifact already held is not listed");
        ArtifactRows().Should().OnlyContain(r => (r.Length).CompareTo(79) <= 0);

        Src("Scripts", "Locations", "DungeonLocation.cs").Should().Contain("ArtifactLocationRows(story)");
    }

    [Fact]
    public void A2_ArtifactMemoryUnlock_AlsoReveals()
    {
        Story.CurrentCycle = 2;   // an NG+ character
        Unlock("ARTIFACT_MEMORY");
        ArtifactRows().Should().HaveCount(7);
    }

    [Fact]
    public void A2_TheKeepsArtifactKnowledgeFlag_IsNoLongerRead()
    {
        Src("Scripts", "Systems", "OpeningSequence.cs").Should().NotContain("keeps_artifact_knowledge");
    }

    [Fact]
    public void A2_TheAncientKeyLines_PromiseNoDoor()
    {
        foreach (var lang in Langs)
        {
            Loc.GetIn(lang, "dialogue.mysterious_stranger_intro.stranger_gift.text").Should()
                .NotContain("possibilit", lang).And.NotContain("posibilidad", lang).And.NotContain("lehetős", lang);
        }
        En("ending.unlock_key_desc").Should().NotContainEquivalentOf("shortcut");
        En("opening.bonus_key").Should().ContainEquivalentOf("keepsake");
        En("opening.cycle_ancient_key").Should().ContainEquivalentOf("keepsake");
    }

    // ---------- A3: the chapter, the story bits and the major choices survive a reload ----------

    private static SaveGameData Saved() => new SaveGameData
    {
        Version = GameConfig.SaveVersion,
        SaveTime = DateTime.Now,
        Player = new PlayerData { Name1 = "NgPlusHero", Name2 = "NgPlusHero", Level = 80 },
        StorySystems = SaveSystem.Instance.SerializeStorySystemsPublic(),
    };

    [Fact]
    public void A3_Chapter_Bits_AndChoices_SurviveASaveAndReload()
    {
        Story.AdvanceChapter(StoryChapter.TheWhispers);
        Story.SetFlag(StoryFlag.JoinedChurch);
        Story.SetFlag(StoryFlag.MetStranger);
        Story.RecordChoice("stranger_intro", "completed", 5);
        Story.RecordChoice("veloura_choice", "spared", -10);
        var json = System.Text.Json.JsonSerializer.Serialize(Saved());

        StoryProgressionSystem.Instance.GetType().GetField("_fallbackInstance", SNP)!.SetValue(null, new StoryProgressionSystem());
        Story.CurrentChapter.Should().Be(StoryChapter.Awakening);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<SaveGameData>(json)!;
        SaveSystem.Instance.RestoreStorySystems(loaded.StorySystems);

        Story.CurrentChapter.Should().Be(StoryChapter.TheWhispers);
        Story.CurrentAct.Should().Be(StoryAct.Act3_TheAwakening);
        Story.HasFlag(StoryFlag.JoinedChurch).Should().BeTrue();
        Story.HasFlag(StoryFlag.MetStranger).Should().BeTrue();
        Story.HasFlag(StoryFlag.JoinedGang).Should().BeFalse();
        Story.MajorChoices.Keys.Should().BeEquivalentTo(new[] { "stranger_intro", "veloura_choice" });
        Story.MajorChoices["veloura_choice"].SelectedOption.Should().Be("spared");
        Story.MajorChoices["veloura_choice"].AlignmentImpact.Should().Be(-10);
    }

    [Fact]
    public void A3_AnOldSaveWithoutTheFields_Loads_AtTheFirstChapter()
    {
        Story.AdvanceChapter(StoryChapter.TheWhispers);
        Story.RecordChoice("stranger_intro", "completed");
        var root = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(Saved()))!.AsObject();
        var story = root["StorySystems"]!.AsObject();
        foreach (var name in new[] { "CurrentChapter", "StoryFlagBits", "MajorChoices" })
            story.ContainsKey(name).Should().BeTrue($"the save writes {name}");
        foreach (var name in new[] { "CurrentChapter", "StoryFlagBits", "MajorChoices" })
            story.Remove(name);

        StoryProgressionSystem.Instance.GetType().GetField("_fallbackInstance", SNP)!.SetValue(null, new StoryProgressionSystem());
        var loaded = System.Text.Json.JsonSerializer.Deserialize<SaveGameData>(root.ToJsonString())!;
        var act = () => SaveSystem.Instance.RestoreStorySystems(loaded.StorySystems);
        act.Should().NotThrow();
        Story.CurrentChapter.Should().Be(StoryChapter.Awakening);
        Story.MajorChoices.Should().BeEmpty();
    }

    // ---------- A4: Marcus and Greta read the Old God states ----------

    private static bool Trigger(string trigger)
    {
        var m = typeof(TownNPCStorySystem).GetMethod("IsTriggerMet", F)!;
        return (bool)m.Invoke(TownNPCStorySystem.Instance, new object[] { trigger, Hero(), new MemorableNPCState() })!;
    }

    [Fact]
    public void A4_MarcusConfession_FollowsMaelkethsDefeat()
    {
        Trigger("Player defeats Maelketh").Should().BeFalse("Maelketh not yet faced");
        Story.UpdateGodState(OldGodType.Maelketh, GodStatus.Defeated);
        Trigger("Player defeats Maelketh").Should().BeTrue();
    }

    [Fact]
    public void A4_GretaClosure_FollowsNocturaAlliedOrDefeated()
    {
        Trigger("Player allies with or defeats Noctura").Should().BeFalse();
        Story.UpdateGodState(OldGodType.Noctura, GodStatus.Allied);
        Trigger("Player allies with or defeats Noctura").Should().BeTrue();
        Story.UpdateGodState(OldGodType.Noctura, GodStatus.Defeated);
        Trigger("Player allies with or defeats Noctura").Should().BeTrue();
    }

    // ---------- A5: the older True Ending rule asks the Blood Price ----------

    private static bool LegacyTrueEnding(float murderWeight)
    {
        Story.CurrentCycle = 3;
        foreach (var s in Enum.GetValues<SealType>()) Story.CollectSeal(s);
        foreach (var f in new[] { "noctura_ally", "veloura_saved", "aurelion_saved" }) Story.SetStoryFlag(f, true);
        var hero = Hero();
        hero.MurderWeight = murderWeight;
        return CycleSystem.Instance.QualifiesForTrueEnding(hero);
    }

    [Fact]
    public void A5_OlderTrueEndingRule_StillOpen_WithoutABloodPrice()
    {
        LegacyTrueEnding(0f).Should().BeTrue();
    }

    [Fact]
    public void A5_OlderTrueEndingRule_IsClosed_ToAMurderer()
    {
        LegacyTrueEnding(GameConfig.MurderWeightEndingBlock).Should().BeFalse();
    }

    // ---------- A6: Mira reacts to Veloura ----------

    [Fact]
    public void A6_Mira_SpeaksOfVeloura_AfterSheIsSaved()
    {
        Story.UpdateGodState(OldGodType.Veloura, GodStatus.Saved);
        Story.HasStoryFlag("veloura_encountered").Should().BeFalse("the game never writes that flag");
        CompanionSystem.ResetIdleCommentHistory();
        var lines = Enumerable.Range(0, 60)
            .Select(_ => CompanionSystem.GetCompanionIdleComment(CompanionId.Mira, 45, Hero(), false)).ToList();
        lines.Should().Contain(Loc.Get("companion.idle.mira.vel_saved"));
        lines.Should().NotContain(Loc.Get("companion.idle.mira.vel_defeated"));
    }

    // ---------- A7: boss hints and the Stranger step clear ----------

    [Fact]
    public void A7_GodHint_ClearsOnceTheGodIsDealtWith()
    {
        var m = Method(typeof(StoryProgressionSystem), "GodHintCleared", BindingFlags.Public | BindingFlags.Instance);
        bool Cleared(OldGodType g) => (bool)m.Invoke(Story, new object[] { g })!;
        Cleared(OldGodType.Maelketh).Should().BeFalse();
        Story.UpdateGodState(OldGodType.Maelketh, GodStatus.Defeated);
        Cleared(OldGodType.Maelketh).Should().BeTrue();
        Story.UpdateGodState(OldGodType.Veloura, GodStatus.Saved);
        Cleared(OldGodType.Veloura).Should().BeTrue();
        Story.SetStoryFlag("aurelion_encountered", true);
        Cleared(OldGodType.Aurelion).Should().BeTrue("the Temple's own flag still clears it");
        Cleared(OldGodType.Terravok).Should().BeFalse();
        Story.UpdateGodState(OldGodType.Terravok, GodStatus.Awakened);
        Cleared(OldGodType.Terravok).Should().BeFalse("a god spared in the dialogue can still be met, so its hint stays");
    }

    [Fact]
    public void A7_TheHintsAndTheStrangerStep_ReadWhatTheGameWrites()
    {
        var src = Src("Scripts", "Locations", "DungeonLocation.cs");
        foreach (var god in new[] { "maelketh", "veloura", "thorgrim", "noctura", "aurelion", "terravok" })
            src.Should().NotContain($"HasStoryFlag(\"{god}_encountered\")", god);
        src.Should().NotContain("HasStoryFlag(\"met_stranger\")");
        src.Should().Contain("HasStoryFlag(OpeningSequenceSystem.MetFlag)");
    }

    // ---------- A8: an Old God's stance is the one chosen in this attempt ----------

    [Fact]
    public void A8_ASecondAttempt_UsesTheStanceChosenThatTime()
    {
        var clear = Method(typeof(OldGodBossSystem), "ClearApproachFlags", SNP | SP);
        var apply = typeof(OldGodBossSystem).GetMethod("ApplyAurelionModifiers", F)!;
        var mods = typeof(OldGodBossSystem).GetField("activeCombatModifiers", F)!;
        var boss = new OldGodBossSystem();

        // first attempt: defiant, then a flee
        Story.SetStoryFlag("aurelion_combat_start", true);
        Story.SetStoryFlag("aurelion_defiant", true);
        // second attempt: the encounter clears the stance before the dialogue, which then sets humble
        clear.Invoke(null, new object[] { Story, OldGodType.Aurelion });
        Story.HasStoryFlag("aurelion_defiant").Should().BeFalse();
        Story.SetStoryFlag("aurelion_combat_start", true);
        Story.SetStoryFlag("aurelion_humble", true);

        mods.SetValue(boss, Activator.CreateInstance(mods.FieldType));
        var term = new TerminalEmulator(new MemoryStream(), new MemoryStream());
        apply.Invoke(boss, new object[] { Story, term });
        var type = mods.FieldType.GetProperty("ApproachType")!.GetValue(mods.GetValue(boss));
        type.Should().Be("humble");
    }

    [Fact]
    public void A8_TheClearList_CoversEveryStanceTheModifiersRead_AndKeepsOutcomes()
    {
        Story.SetStoryFlag("noctura_ally", true);
        Story.SetStoryFlag("veloura_saved", true);
        var clear = Method(typeof(OldGodBossSystem), "ClearApproachFlags", SNP | SP);
        foreach (var god in Enum.GetValues<OldGodType>())
            clear.Invoke(null, new object[] { Story, god });
        Story.HasStoryFlag("noctura_ally").Should().BeTrue("an outcome is not a stance");
        Story.HasStoryFlag("veloura_saved").Should().BeTrue();

        var src = Src("Scripts", "Systems", "OldGodBossSystem.cs");
        int start = src.IndexOf("private void ApplyDialogueModifiers(", StringComparison.Ordinal);
        int end = src.IndexOf("private async Task<BossEncounterResult> RunBossCombat(", StringComparison.Ordinal);
        var read = System.Text.RegularExpressions.Regex.Matches(src.Substring(start, end - start), "HasStoryFlag\\(\"([a-z_]+)\"\\)")
            .Select(m => m.Groups[1].Value).Where(f => f != "noctura_ally" && f != "defiant_to_stranger" && f != "willing_hero").Distinct().ToList();
        read.Should().NotBeEmpty();
        foreach (var flag in read)
        {
            Story.SetStoryFlag(flag, true);
            foreach (var god in Enum.GetValues<OldGodType>())
                clear.Invoke(null, new object[] { Story, god });
            Story.HasStoryFlag(flag).Should().BeFalse($"{flag} is cleared before the next attempt");
        }
        int dlg = src.IndexOf("DialogueSystem.Instance.StartDialogue(", StringComparison.Ordinal);
        int call = src.IndexOf("ClearApproachFlags(StoryProgressionSystem.Instance, type)", StringComparison.Ordinal);
        call.Should().BePositive().And.BeLessThan(dlg, "the stance is cleared before the dialogue sets it");
    }

    // ---------- A9: the secret boss wins promise only what they give ----------

    [Fact]
    public void A9_SecretBossWins_PromiseNoTrueEnding_AndNoFloodOfMemories()
    {
        foreach (var lang in Langs)
        {
            Loc.GetIn(lang, "secretboss.ocean_speaks.victory.21").Should()
                .NotBe(new Dictionary<string, string> {
                    ["en"] = "(The True Ending is now available)", ["es"] = "(El Final Verdadero ya está disponible)",
                    ["fr"] = "(La Vraie Fin est désormais disponible)", ["hu"] = "(Az Igazi Befejezés most már elérhető)",
                    ["it"] = "(Il Vero Finale è ora disponibile)" }[lang], lang);
        }
        En("secretboss.ocean_speaks.victory.21").Should().NotContain("now available");
        En("secretboss.echo_of_self.victory.8").Should().NotContain("MEMORIES FLOOD IN");
        En("secretboss.echo_of_self.victory.16").Should().NotContain("remember everything");
        var src = Src("Scripts", "Data", "SecretBosses.cs");
        src.Should().NotContain("(The True Ending is now available)");
        src.Should().NotContain("MEMORIES FLOOD IN.");
        src.Should().NotContain("'remember everything.'");
        foreach (var lang in new[] { "en", "hu" })
            foreach (var key in new[] { "secretboss.ocean_speaks.victory.21", "secretboss.echo_of_self.victory.8", "secretboss.echo_of_self.victory.16" })
                Loc.GetIn(lang, key).Length.Should().BeLessThanOrEqualTo(77, $"{lang} {key}");
    }
}
