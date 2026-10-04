using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.5: the width close-out. Every message row that was over 79 columns at its widest arguments (a 30-character
/// name, the longest names, 9,999,999) is printed through a wrap, in all five languages: each print site of its key
/// goes through a wrap helper (or stores the text for a reader that wraps), and the wrapped rows fit 79. The fixed
/// rows (Inn trainer, Pantheon budget, ally cast) fit as well. A row that fits is written unchanged.
/// </summary>
[Collection("SharedGameSingletons")]
public class Width125Tests
{
    private const int MaxWidth = 79;
    private static readonly string[] AllLanguages = { "en", "es", "fr", "hu", "it" };
    private static readonly string LongName = new string('W', GameConfig.MaxNameLength);
    // the longest shown monster or item name is 31 columns; every text argument is held at that width
    private static readonly string LongArg = new string('W', 31);

    /// <summary>The message keys found over 79 at their widest arguments (receipts of the 1.2.5 data pieces).</summary>
    internal static readonly string[] MessageKeys =
    {
        "armor_shop.autobuy_already_best", "base.auction_list_confirm", "base.auction_listed",
        "base.auction_push_listed", "castle.monster_added", "castle.news_monster_guard",
        "castle.siege_monster_blocks", "castle.siege_monster_strikes", "castle.siege_overwhelmed",
        "castle.team_strikes_monster", "combat.ability_abyssal_eruption", "combat.ability_abyss_unchained",
        "combat.ability_annihilation", "combat.ability_annihilation_kill", "combat.ability_causality_loop",
        "combat.ability_consume_soul", "combat.ability_corrupting_dot", "combat.ability_crescendo_aoe",
        "combat.ability_cycles_end", "combat.ability_devour", "combat.ability_echo_25",
        "combat.ability_entropic_blade", "combat.ability_entropy_aoe", "combat.ability_execute_critical",
        "combat.ability_execute_finishing", "combat.ability_grand_finale", "combat.ability_lifesteal_capped",
        "combat.ability_lifesteal_drain", "combat.ability_marked", "combat.ability_overflow_aoe",
        "combat.ability_overflow_aoe_spread", "combat.ability_prison_wardens_command",
        "combat.ability_resonance_cascade", "combat.ability_riptide", "combat.ability_temporal_prison",
        "combat.ability_undertow", "combat.ability_void_rupture", "combat.ability_void_rupture_explode",
        "combat.ability_wrath_deep", "combat.ability_wrath_deep_kill", "combat.armor_absorbed",
        "combat.armor_absorbed_pierced", "combat.berserker_critical_fury", "combat.berserker_hp_status",
        "combat.berserker_monster_strikes", "combat.berserker_savage_attack", "combat.biaxin_apply",
        "combat.boss_channeling_ability", "combat.boss_channeling_continues", "combat.boss_channel_interrupted",
        "combat.boss_confused", "combat.boss_defeated_broadcast", "combat.boss_enrage_countdown",
        "combat.boss_power_builds", "combat.boss_summary_line", "combat.boss_unleashed", "combat.boss_unleashes",
        "combat.cast_spell_on_ally", "combat.companion_dodges", "combat.companion_hits",
        "combat.companion_takes_damage_monster_heals", "combat.confusion_self_damage", "combat.corrosive_cloud_hits",
        "combat.corrupting_kill", "combat.corrupting_tick", "combat.corruption_harvest", "combat.creation_reshape",
        "combat.cyclebreaker_evade", "combat.deal_damage", "combat.dealt_damage_to", "combat.deathbane_ravages",
        "combat.disarm_success", "combat.discordia_first_action_fails", "combat.distracted",
        "combat.distracted_miss", "combat.divine_mandate_reflect", "combat.enchant_fire_multi",
        "combat.enchant_frost", "combat.enchant_frost_multi", "combat.enchant_frost_tm",
        "combat.enchant_holy_immune_angel", "combat.enchant_holy_multi", "combat.enchant_holy_undead_multi",
        "combat.enchant_lightning_resist", "combat.enchant_lightning_resist_multi",
        "combat.enchant_lightning_resist_tm", "combat.enchant_lightning_stun", "combat.enchant_lightning_stun_multi",
        "combat.enchant_lightning_stun_tm", "combat.enchant_shadow_immune_demon", "combat.enchant_shadow_multi",
        "combat.enchant_venom_multi", "combat.enemy_hp", "combat.evasion_miss", "combat.fire_burn",
        "combat.frost_bites_no_freeze", "combat.future_echo", "combat.group_fallen_dark_powers",
        "combat.harmonic_reflect", "combat.hemlock_weakened", "combat.holy_smite_passive",
        "combat.legendary_shot_staggers", "combat.legendary_shot_withstands", "combat.lethal_precision_poison",
        "combat.loot_ally_picks_up", "combat.loot_ally_upgrade_prompt", "combat.loses_defense",
        "combat.magical_immunity_absorbs", "combat.monster_ability_resisted", "combat.monster_ability_stand_firm",
        "combat.monster_critical", "combat.monster_heals", "combat.monster_power_surges", "combat.monster_roll",
        "combat.monster_roll_distracted", "combat.monster_uses_ability", "combat.news_boss_defeated",
        "combat.nightshade_sleep", "combat.off_hand_strike_npc", "combat.other_takes",
        "combat.other_takes_to_inventory", "combat.physical_immunity_absorbs", "combat.poison_burn",
        "combat.power_attack_hit", "combat.power_attack_smash", "combat.precise_strike_aim",
        "combat.precise_strike_hit", "combat.probability_shift", "combat.protections_stripped", "combat.ranged_hit",
        "combat.resolve_crumbles", "combat.sage_compel", "combat.sage_marked", "combat.sage_marked_library",
        "combat.scales_destroy", "combat.scales_reflect", "combat.shadow_dodge", "combat.shadow_harvest_feast",
        "combat.shaman_chain_lightning", "combat.shaman_lightning_bolt", "combat.share_boss",
        "combat.share_boss_allies", "combat.slain_by_corruption", "combat.sleep_bonus_damage", "combat.smite_damage",
        "combat.smite_evil", "combat.soul_eater", "combat.soul_shattered", "combat.soul_strike_damage",
        "combat.spell_death_kill", "combat.spell_disintegrate", "combat.spell_dominate", "combat.spell_drain",
        "combat.spell_holy_bonus", "combat.spell_turn_undead_unaffected", "combat.steals_gold",
        "combat.storm_eagle_lightning", "combat.storm_eagle_stun", "combat.target_damage",
        "combat.target_takes_damage", "combat.target_takes_damage_flat", "combat.target_takes_damage_markup",
        "combat.taunt_target", "combat.teammate_casts_on", "combat.teammate_offhand_strike", "combat.thorns_reflect",
        "combat.tidal_barrier_reflects", "combat.tidesworn_weaken_bonus", "combat.tm_offhand_strike",
        "combat.void_hunger", "combat.void_shroud_reflect", "combat.wave_echo_resonates", "combat.you_dodge_chance",
        "combat.you_hit", "combat.you_take_damage_heals", "dungeon.scout_monster", "equip.cannot_offhand_with_2h",
        "home.took_item", "inventory.cursed_cant_drop", "mability.cocoon", "mability.corrosion.you",
        "mability.dragon_fear.you", "mability.flight", "mability.heal", "mability.incorporeal",
        "mability.madness.you", "mability.mana_drain", "mability.moonlight_heal", "mability.overload",
        "mability.phylactery", "mability.possess.ally", "mability.purify", "mability.rally", "mability.regeneration",
        "mability.sanctuary", "mability.self_repair", "mability.thorns", "mability.tree_meld_avoid",
        "magic_shop.curse_confirm", "magic_shop.curse_confirm_team", "magic_shop.cursed_team_entry",
        "magic_shop.cursed_worn_entry", "magic_shop.curse_scene_1", "magic_shop.curse_team_scene_1",
        "magic_shop.curse_team_success", "magic_shop.enchant_anvil", "magic_shop.old_enchant_confirm",
        "magic_shop.remove_enchant_confirm", "marketplace.news_npc_bought", "marketplace.news_npc_listed",
        "marketplace.news_purchased", "miracle.banish", "miracle.banish_resist", "miracle.bind", "party_inv.taken",
        "secretboss.group_share", "settlement.scouts_floor", "shop.couldnt_equip", "shop.purchased_inventory",
        "shop.purchased_inventory_alt", "street_encounter.bounty_hunter.victory_loot",
        "street_encounter.bounty_hunter.victory_loot_dropped", "temple.sacrifice_refused_item",
        "weapon_shop.autobuy_already_best", "wilderness.beast_encounter_header", "wilderness.beast_flees",
        "wilderness.beast_roster_full", "wilderness.beast_tame_success", "wilderness.beast_walk_away",
        "wilderness.monster_emerges",
    };

    private static string Root => UsurperReborn.Tests.Localization.HardcodedTextScannerTests.RepoRoot();
    private static string Src(string rel) => File.ReadAllText(Path.Combine(Root, "Scripts", rel));

    private static readonly Regex WrapHelper = new(
        @"\b(WriteRow|WriteWrapped|WordWrap|WrapAfterPrefix|GroupLineWrapped|WriteItemRow|MessageRows|AnsiRows|PromptRows|NpcReactionRows|MonsterSaysRows|WrappedLine)\(|CombatMessages\.Rows\(");

    // text stored for a reader that wraps it; each reader is checked in StoredText_IsWrappedWhereItIsRead
    private static readonly Regex StoredForAReader = new(
        @"^(result\.Message =|message =|attackMessage =|string shareLine =|NewsSystem\.Instance\??\.Newsy\(|_ = OnlineStateManager\.Instance!\.AddNews\(|UsurperRemake\.Server\.RoomRegistry\.BroadcastActionLocalized\()");

    private static bool EndsStatement(string line) =>
        Regex.IsMatch(line, @"[;{}]\s*(//.*)?$") || line.TrimStart().StartsWith("//");

    /// <summary>Every statement in Scripts that names the key, with its file and line.</summary>
    internal static List<(string File, int Line, string Statement)> SitesOf(string key)
    {
        var sites = new List<(string, int, string)>();
        foreach (var path in Directory.GetFiles(Path.Combine(Root, "Scripts"), "*.cs", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains("\"" + key + "\"")) continue;
                int start = i;
                while (start > 0 && !EndsStatement(lines[start - 1]) && i - start < 8) start--;
                int end = i;
                while (end < lines.Length - 1 && !Regex.IsMatch(lines[end], @"[;{]\s*(//.*)?$") && end - i < 8) end++;
                string statement = string.Join(" ", lines[start..(end + 1)].Select(l => l.Trim())).Trim();
                sites.Add((Path.GetRelativePath(Path.Combine(Root, "Scripts"), path), i + 1, statement));
            }
        }
        return sites;
    }

    private static bool Wrapped(string file, string statement) =>
        WrapHelper.IsMatch(statement)
        || (file.EndsWith("MagicShopLocation.cs") && statement.Contains("DisplayMessage("))   // the shop's DisplayMessage wraps
        || StoredForAReader.IsMatch(Regex.Replace(statement, @"^(if \([^)]*\)\s*)", ""));

    [Fact]
    public void EveryPrintSite_OfAnOverWideMessage_GoesThroughAWrap()
    {
        MessageKeys.Should().HaveCountGreaterThan(200);
        var raw = new List<string>();
        foreach (var key in MessageKeys)
        {
            Loc.HasIn("en", key).Should().BeTrue(key);
            var sites = SitesOf(key);
            sites.Should().NotBeEmpty($"{key} is printed somewhere");
            foreach (var (file, line, statement) in sites)
                if (!Wrapped(file, statement)) raw.Add($"{file}:{line} {key}: {statement}");
        }
        raw.Should().BeEmpty("every row of an over-wide message is wrapped at 79 columns");
    }

    [Fact]
    public void StoredText_IsWrappedWhereItIsRead()
    {
        string combat = Src("Systems/CombatEngine.cs");
        Regex.IsMatch(combat, @"terminal\.WriteLine\((abilityResult|spellResult)\.Message\)").Should().BeFalse("ability and spell messages are wrapped");
        Regex.IsMatch(combat, @"terminal\.WriteLine\(attackMessage").Should().BeFalse("attack messages are wrapped");
        Src("Server/RoomRegistry.cs").Should().Contain("UIHelper.AnsiRows(\"\u001b[90m\", $\"  {buildMessage(lang)}\")", "room actions are wrapped");
        Src("Locations/LoveStreetLocation.cs").Should().Contain("UIHelper.WrapAfterPrefix(\" - \", news)").And.NotContain("terminal.WriteLine($\" - {news}\")");
        Src("Locations/PantheonLocation.cs").Should().Contain("UIHelper.WriteRow(terminal, $\"  {item}\")").And.NotContain("terminal.WriteLine($\"  {item}\")");
        Src("Locations/NewsLocation.cs").Should().Contain("WrapAfterPrefix(");
        Src("Systems/OnlineChatSystem.cs").Should().Contain("WriteNewsEntry(terminal");
        // the equip refusal (message / equipMsg) is printed wrapped by every screen that equips
        foreach (var file in new[] { "Locations/ArmorShopLocation.cs", "Systems/InventorySystem.cs", "Locations/MusicShopLocation.cs",
                     "Locations/TeamCornerLocation.cs", "Locations/WeaponShopLocation.cs", "Locations/HomeLocation.cs", "Locations/InnLocation.cs",
                     "Locations/CastleLocation.cs", "Locations/DungeonLocation.cs" })
            Regex.Matches(Src(file), @"\b(terminal|term)\.WriteLine\((\$""\s*\{)?(message|equipMsg)\}?""?\)|\b(terminal|term)\.WriteLine\(\$?""?\s*\{?Loc\.Get\(""[a-z_.]*(equip|cannot|failed)[a-z_.]*"", (message|equipMsg)\)").Select(m => m.Value)
                .Should().BeEmpty($"{file} prints the equip message wrapped");
    }

    private static object[] WidestArgs(string template) =>
        Enumerable.Range(0, Regex.Matches(template, @"\{(\d+)").Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(-1).Max() + 1)
            .Select(i => Regex.IsMatch(template, "\\{" + i + ":N") ? (object)9_999_999 : LongArg).ToArray();

    private static int Visible(string row) => UIHelper.VisibleLength(Regex.Replace(row, @"\[/?[a-z_]*\]", ""));

    [Fact]
    public void EveryOverWideMessage_WrapsTo79_InFiveLanguages_AtItsWidestArguments()
    {
        LongName.Length.Should().Be(GameConfig.MaxNameLength);
        var tooWide = new List<string>();
        foreach (var key in MessageKeys)
            foreach (var lang in AllLanguages)
            {
                string template = Loc.GetIn(lang, key);
                string row = "  " + Loc.GetIn(lang, key, WidestArgs(template));
                var rows = UIHelper.MessageRows(row);
                Regex.Replace(string.Join(" ", rows), @"\s+", " ").Trim().Should().Be(Regex.Replace(row, @"\s+", " ").Trim(), $"{lang} {key} keeps every word");
                foreach (var r in rows.Where(r => Visible(r) > MaxWidth)) tooWide.Add($"{lang} {key} {Visible(r)}: {r}");
            }
        tooWide.Should().BeEmpty();
    }

    [Fact]
    public void ARowThatFits_IsWrittenUnchanged_AndAPromptKeepsItsTrailingSpace()
    {
        UIHelper.MessageRows("  You hit the goblin for 12 damage!").Should().Equal("  You hit the goblin for 12 damage!");
        var rows = UIHelper.MessageRows("    " + string.Join(" ", Enumerable.Repeat("word", 30)));
        rows.Should().HaveCountGreaterThan(1);
        rows.Should().OnlyContain(r => r.StartsWith("    ") && r.Length <= MaxWidth);
        UIHelper.AnsiRows("\u001b[93m", "  short").Should().Be("\u001b[93m  short\u001b[0m");
        UIHelper.AnsiRows("\u001b[93m", "  " + string.Join(" ", Enumerable.Repeat("word", 30))).Split('\n')
            .Should().OnlyContain(r => r.StartsWith("\u001b[93m  ") && r.EndsWith("\u001b[0m") && UIHelper.VisibleLength(r) <= MaxWidth);
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void InnTrainerIntro_Fits79(string lang)
    {
        foreach (var key in new[] { "inn.trainer_intro1", "inn.trainer_intro2", "inn.trainer_quote1", "inn.trainer_quote2", "inn.trainer_quote3" })
            Loc.GetIn(lang, key).Length.Should().BeLessOrEqualTo(MaxWidth, $"{lang} {key}: \"{Loc.GetIn(lang, key)}\"");
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void PantheonBudgetRow_Fits79_AtTheWidestBudget(string lang)
    {
        int level = GameConfig.MaxGodLevel, top = level * GameConfig.GodBoonBudgetPerLevel + GameConfig.GodBoonConcentrationMax;
        var (label, value, breakdown, ownRow) = PantheonLocation.BudgetRows(Loc.GetIn(lang, "pantheon.budget_label"),
            Loc.GetIn(lang, "pantheon.budget_value", top, top),
            Loc.GetIn(lang, "pantheon.budget_breakdown", level * GameConfig.GodBoonBudgetPerLevel, level, GameConfig.GodBoonConcentrationMax, 99_999));
        var rows = !ownRow ? new List<string> { label + value + breakdown[0] } : new List<string> { label + value }.Concat(breakdown).ToList();
        rows.Should().OnlyContain(r => r.Length <= MaxWidth, $"{lang}: {string.Join(" | ", rows)}");
    }

    [Fact]
    public void PantheonBudgetRow_StaysOneRow_WhenItFits()
    {
        var (label, value, breakdown, ownRow) = PantheonLocation.BudgetRows(Loc.GetIn("en", "pantheon.budget_label"), Loc.GetIn("en", "pantheon.budget_value", 40, 50),
            Loc.GetIn("en", "pantheon.budget_breakdown", 30, 3, 20, 4));
        ownRow.Should().BeFalse();
        breakdown.Should().Equal(Loc.GetIn("en", "pantheon.budget_breakdown", 30, 3, 20, 4));
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void AllyCastLine_Fits79_WithTheLongestSpellAndA30CharacterAlly(string lang)
    {
        var prev = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            string spell = Enum.GetValues<CharacterClass>().SelectMany(c => SpellSystem.GetAllSpellsForClass(c) ?? new List<SpellSystem.SpellInfo>())
                .Select(s => s.DisplayName).OrderByDescending(s => s.Length).First();
            UIHelper.MessageRows(Loc.GetIn(lang, "combat.cast_spell_on_ally", spell, LongName)).Should().OnlyContain(r => r.Length <= MaxWidth);
        }
        finally { GameConfig.Language = prev; }
        Src("Systems/CombatEngine.cs").Should().Contain("UIHelper.WriteRow(terminal, Loc.Get(\"combat.cast_spell_on_ally\"");
    }

    [Theory]
    [InlineData("en", "Agi:+3", "Wis:+4")] [InlineData("hu", "Moz:+3", "Böl:+4")] [InlineData("es", "Agi:+3", "Sab:+4")]
    public void StatusScreenStatLabels_AreInTheReadersLanguage(string lang, string agility, string wisdom)
    {
        var item = new Equipment { Name = "Test Ring", Slot = EquipmentSlot.LFinger, AgilityBonus = 3, WisdomBonus = 4 };
        var method = typeof(BaseLocation).GetMethod("GetEquipmentStatSummary", BindingFlags.NonPublic | BindingFlags.Static)!;
        var prev = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            string summary = (string)method.Invoke(null, new object[] { item })!;
            summary.Should().Be($"{agility}, {wisdom}");
        }
        finally { GameConfig.Language = prev; }
    }
}
