using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// v0.62.0 interpreter for the data-driven Discovery catalog (see DiscoveryData.cs and
    /// memory/project_dungeon_discoveries.md). Renders a discovery's scripted beat, handles the
    /// player's choice / skill test / risk gamble / trap, applies the resulting effects through the
    /// same proven Character APIs the old FeatureInteractionSystem used, and returns a FeatureOutcome
    /// so the existing party reward-split in DungeonLocation keeps working unchanged.
    /// </summary>
    public class DiscoverySystem
    {
        private static DiscoverySystem _instance;
        public static DiscoverySystem Instance => _instance ??= new DiscoverySystem();
        private readonly Random random = Random.Shared;

        // v0.62.0: emit the per-discovery loc source keys (English) for the whole catalog, using the
        // EXACT key scheme RunDiscovery resolves at runtime (Resolve/ResolveArray below). Used by the
        // `--export-discoveries` CLI flag to produce the translation source. Keeping derivation here
        // (not a separate parser) guarantees the exported keys never drift from what the engine reads.
        public static Dictionary<string, string> ExportLocKeys()
        {
            var map = new Dictionary<string, string>();
            foreach (var def in DiscoveryCatalog.All)
            {
                void Add(string suffix, string val) { if (!string.IsNullOrEmpty(val)) map[$"discovery.{def.Id}.{suffix}"] = val; }
                void AddArr(string suffix, List<string> lines)
                {
                    if (lines == null) return;
                    for (int i = 0; i < lines.Count; i++)
                        if (!string.IsNullOrEmpty(lines[i])) map[$"discovery.{def.Id}.{suffix}.{i}"] = lines[i];
                }
                Add("name", def.Name);
                Add("desc", def.Desc);
                AddArr("intro", def.Root.Intro);
                switch (def.Root.Kind)
                {
                    case DiscoveryKind.Choice:
                        for (int n = 0; n < def.Root.Choices.Count; n++)
                        {
                            Add($"choice{n + 1}.label", def.Root.Choices[n].Label);
                            AddArr($"choice{n + 1}.result", def.Root.Choices[n].ResultLines);
                        }
                        break;
                    case DiscoveryKind.SkillTest:
                        Add("test.prompt", def.Root.Prompt);
                        AddArr("test.success", def.Root.SuccessLines);
                        AddArr("test.fail", def.Root.FailLines);
                        break;
                    case DiscoveryKind.Risk:
                        Add("risk.prompt", def.Root.Prompt);
                        AddArr("risk.success", def.Root.SuccessLines);
                        AddArr("risk.fail", def.Root.FailLines);
                        break;
                    // Narrative / Trap: intro lines only (effects use shared discovery.effect.* keys).
                }
            }
            return map;
        }

        public async Task<FeatureOutcome> RunDiscovery(DiscoveryDefinition def, Character player, int floor,
            TerminalEmulator terminal, List<Character> teammates = null)
        {
            var outcome = new FeatureOutcome { Success = true };
            if (def == null || player == null || terminal == null) return outcome;

            var o = def.Root;

            // Intro flavor
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            foreach (var line in ResolveArray(def.Id, "intro", o.Intro))
                terminal.WriteLine("  " + line);
            terminal.WriteLine("");

            switch (o.Kind)
            {
                case DiscoveryKind.Narrative:
                    await ApplyEffects(o.Effects, player, floor, terminal, outcome, teammates);
                    break;
                case DiscoveryKind.Choice:
                    await RunChoice(def, player, floor, terminal, outcome, teammates);
                    break;
                case DiscoveryKind.SkillTest:
                    await RunSkillTest(def, player, floor, terminal, outcome, teammates);
                    break;
                case DiscoveryKind.Risk:
                    await RunRisk(def, player, floor, terminal, outcome, teammates);
                    break;
                case DiscoveryKind.Trap:
                    await RunTrap(def, player, floor, terminal, outcome, teammates);
                    break;
            }

            await terminal.PressAnyKey();
            return outcome;
        }

        // ---------- kind handlers ----------

        private async Task RunChoice(DiscoveryDefinition def, Character player, int floor,
            TerminalEmulator terminal, FeatureOutcome outcome, List<Character> teammates)
        {
            var choices = def.Root.Choices;
            for (int i = 0; i < choices.Count; i++)
            {
                terminal.SetColor("white");
                terminal.Write($"  [{i + 1}] ");
                terminal.SetColor(choices[i].IsWalkAway ? "gray" : "bright_white");
                terminal.WriteLine(Resolve(def.Id, $"choice{i + 1}.label", choices[i].Label));
            }
            terminal.WriteLine("");
            var input = await terminal.GetInput(Loc.Get("ui.choice"));
            if (!int.TryParse(input, out int idx) || idx < 1 || idx > choices.Count)
            {
                // Treat invalid / empty as walking away.
                terminal.SetColor("gray");
                terminal.WriteLine("  " + Loc.Get("ui.cancelled"));
                return;
            }

            var chosen = choices[idx - 1];
            outcome.ChoiceMade = true;
            terminal.WriteLine("");
            terminal.SetColor("white");
            foreach (var line in ResolveArray(def.Id, $"choice{idx}.result", chosen.ResultLines))
                terminal.WriteLine("  " + line);
            if (!chosen.IsWalkAway)
                await ApplyEffects(chosen.Effects, player, floor, terminal, outcome, teammates);
        }

        private async Task RunSkillTest(DiscoveryDefinition def, Character player, int floor,
            TerminalEmulator terminal, FeatureOutcome outcome, List<Character> teammates)
        {
            var o = def.Root;
            terminal.SetColor("yellow");
            terminal.WriteLine("  " + Resolve(def.Id, "test.prompt", o.Prompt));
            terminal.SetColor("white");
            terminal.WriteLine("  [1] " + Loc.Get("discovery.attempt"));
            terminal.WriteLine("  [0] " + Loc.Get("discovery.leave"));
            terminal.WriteLine("");
            var input = await terminal.GetInput(Loc.Get("ui.choice"));
            if (input?.Trim() != "1")
            {
                terminal.SetColor("gray");
                terminal.WriteLine("  " + Loc.Get("discovery.walk_away"));
                return;
            }

            int dc = 8 + floor / 4;
            int statVal = GetStat(player, o.TestStat);
            int statBonus = Math.Min(statVal / 10, 40);
            int roll = random.Next(1, 21);
            int total = roll + statBonus;
            terminal.SetColor("cyan");
            terminal.WriteLine("  " + Loc.Get("discovery.roll_result", o.TestStat, roll, statBonus, total, dc));
            terminal.WriteLine("");

            if (total >= dc)
            {
                terminal.SetColor("bright_green");
                foreach (var line in ResolveArray(def.Id, "test.success", o.SuccessLines))
                    terminal.WriteLine("  " + line);
                await ApplyEffects(o.SuccessEffects, player, floor, terminal, outcome, teammates);
            }
            else
            {
                terminal.SetColor("red");
                foreach (var line in ResolveArray(def.Id, "test.fail", o.FailLines))
                    terminal.WriteLine("  " + line);
                await ApplyEffects(o.FailEffects, player, floor, terminal, outcome, teammates);
                outcome.Success = false;
            }
        }

        private async Task RunRisk(DiscoveryDefinition def, Character player, int floor,
            TerminalEmulator terminal, FeatureOutcome outcome, List<Character> teammates)
        {
            var o = def.Root;
            int chance = o.RiskBasePercent > 0
                ? o.RiskBasePercent
                : Math.Clamp(50 + GetStat(player, "DEX") / 5 + GetStat(player, "INT") / 10, 20, 85);

            terminal.SetColor("yellow");
            terminal.WriteLine("  " + Resolve(def.Id, "risk.prompt", o.Prompt));
            terminal.SetColor("gray");
            terminal.WriteLine("  " + Loc.Get("discovery.risk_odds", chance));
            terminal.WriteLine("");
            var input = await terminal.GetInput(Loc.Get("discovery.risk_confirm"));
            if (!IsYes(input))
            {
                terminal.SetColor("gray");
                terminal.WriteLine("  " + Loc.Get("discovery.walk_away"));
                return;
            }

            terminal.WriteLine("");
            if (random.Next(100) < chance)
            {
                terminal.SetColor("bright_green");
                foreach (var line in ResolveArray(def.Id, "risk.success", o.SuccessLines))
                    terminal.WriteLine("  " + line);
                await ApplyEffects(o.SuccessEffects, player, floor, terminal, outcome, teammates);
            }
            else
            {
                terminal.SetColor("red");
                foreach (var line in ResolveArray(def.Id, "risk.fail", o.FailLines))
                    terminal.WriteLine("  " + line);
                await ApplyEffects(o.FailEffects, player, floor, terminal, outcome, teammates);
                outcome.Success = false;
            }
        }

        private async Task RunTrap(DiscoveryDefinition def, Character player, int floor,
            TerminalEmulator terminal, FeatureOutcome outcome, List<Character> teammates)
        {
            terminal.SetColor("red");
            // Intro already shown; apply the trap's effects.
            await ApplyEffects(def.Root.Effects, player, floor, terminal, outcome, teammates);
            outcome.Success = false;
        }

        // ---------- effect application ----------

        private async Task ApplyEffects(List<DiscEffect> effects, Character player, int floor,
            TerminalEmulator terminal, FeatureOutcome outcome, List<Character> teammates)
        {
            if (effects == null) return;
            foreach (var e in effects)
            {
                try { ApplyEffect(e, player, floor, terminal, outcome, teammates); }
                catch (Exception ex) { DebugLogger.Instance?.LogError("DISCOVERY", $"effect {e.Type} failed: {ex.Message}"); }
                await Task.Delay(250);
            }
        }

        private void ApplyEffect(DiscEffect e, Character player, int floor,
            TerminalEmulator terminal, FeatureOutcome outcome, List<Character> teammates)
        {
            switch (e.Type)
            {
                case DiscEffectType.Gold:
                {
                    // v0.65.3: dungeon features are now much rarer (see DungeonGenerator), so each
                    // gold/xp payout is bumped to make the ones you do find feel worth examining.
                    long amt = (long)(Scaled(floor, e.A, e.B) * GameConfig.DungeonFeatureRewardMultiplier);
                    player.Gold += amt; outcome.GoldGained += amt;
                    Msg(terminal, "bright_yellow", Loc.Get("discovery.effect.gold", amt));
                    break;
                }
                case DiscEffectType.Xp:
                {
                    long amt = (long)(Scaled(floor, e.A, e.B) * GameConfig.DungeonFeatureRewardMultiplier);
                    player.Experience += amt; outcome.ExperienceGained += amt;
                    Msg(terminal, "yellow", Loc.Get("discovery.effect.xp", amt));
                    break;
                }
                case DiscEffectType.Heal:
                {
                    long heal = Math.Max(1, player.MaxHP * e.A / 100);
                    long before = player.HP;
                    player.HP = Math.Min(player.MaxHP, player.HP + heal);
                    Msg(terminal, "green", Loc.Get("discovery.effect.heal", player.HP - before));
                    break;
                }
                case DiscEffectType.Damage:
                {
                    long dmg = floor + random.Next(e.A, e.B + 1);
                    player.HP = Math.Max(1, player.HP - dmg); // features never kill outright
                    outcome.DamageTaken += dmg;
                    Msg(terminal, "red", Loc.Get("discovery.effect.damage", dmg));
                    break;
                }
                case DiscEffectType.Mana:
                {
                    long before = player.Mana;
                    player.Mana = Math.Min(player.MaxMana, player.Mana + e.A);
                    if (player.Mana > before)
                        Msg(terminal, "cyan", Loc.Get("discovery.effect.mana", player.Mana - before));
                    break;
                }
                case DiscEffectType.TempAtk:
                    GrantCombatBlessing(player, e.A);
                    Msg(terminal, "bright_red", Loc.Get("discovery.effect.tempatk", e.A));
                    break;
                case DiscEffectType.TempDef:
                    GrantCombatBlessing(player, e.A);
                    Msg(terminal, "bright_cyan", Loc.Get("discovery.effect.tempdef", e.A));
                    break;
                case DiscEffectType.Potion:
                {
                    long before = player.Healing;
                    player.Healing = Math.Min(player.MaxPotions, player.Healing + e.A);
                    if (player.Healing > before)
                        Msg(terminal, "bright_green", Loc.Get("discovery.effect.potion", player.Healing - before));
                    break;
                }
                case DiscEffectType.ManaPotion:
                {
                    long before = player.ManaPotions;
                    player.ManaPotions = Math.Min(player.MaxManaPotions, player.ManaPotions + e.A);
                    if (player.ManaPotions > before)
                        Msg(terminal, "bright_blue", Loc.Get("discovery.effect.mana_potion", player.ManaPotions - before));
                    break;
                }
                case DiscEffectType.PoisonVial:
                {
                    long before = player.PoisonVials;
                    player.PoisonVials = Math.Min(GameConfig.MaxPoisonVials, player.PoisonVials + e.A);
                    if (player.PoisonVials > before)
                        Msg(terminal, "bright_green", Loc.Get("discovery.effect.poison_vial", player.PoisonVials - before));
                    break;
                }
                case DiscEffectType.PermStat:
                    ApplyPermStat(player, e.Arg, e.A);
                    Msg(terminal, "bright_magenta", Loc.Get("discovery.effect.permstat", e.A, LocStatName(e.Arg)));
                    break;
                case DiscEffectType.Alignment:
                    AlignmentSystem.Instance.ChangeAlignment(player, e.A, e.Good, "Dungeon discovery");
                    Msg(terminal, e.Good ? "bright_white" : "dark_red",
                        Loc.Get(e.Good ? "discovery.effect.align_light" : "discovery.effect.align_dark", e.A));
                    break;
                case DiscEffectType.Status:
                    if (Enum.TryParse<StatusEffect>(e.Arg, true, out var status))
                    {
                        int statusDur = e.B > 0 ? e.B : 2;
                        // v0.65.0 (Darowin report): route a discovery-applied Poisoned status to the
                        // canonical out-of-combat poison field (Poison/PoisonTurns) -- exactly like the
                        // dungeon poison-dart trap -- INSTEAD OF ApplyStatus(Poisoned), not in addition.
                        // The antidote/healer cures all read Character.Poison, so the old ApplyStatus-only
                        // path showed the affliction on /health yet every cure reported "you are not
                        // poisoned". Crucially, combat ticks Character.Poison and ActiveStatuses[Poisoned]
                        // as SEPARATE DoT sources (the CombatEngine poison-counter tick is explicitly
                        // "separate from StatusEffect.Poisoned"), so setting BOTH would double-tick poison
                        // in combat. One canonical representation = cured by every antidote, shown on
                        // /health, ticked once. Other statuses (Frozen/Stunned/Bleeding/...) have no
                        // canonical field, so they keep going through ApplyStatus.
                        if (status == StatusEffect.Poisoned)
                        {
                            player.Poison = Math.Max(player.Poison, 1);
                            player.PoisonTurns = Math.Max(player.PoisonTurns, statusDur);
                        }
                        else
                        {
                            player.ApplyStatus(status, statusDur);
                        }
                        Msg(terminal, "magenta", Loc.Get("discovery.effect.status", LocStatusName(status)));
                    }
                    break;
                case DiscEffectType.Lore:
                    outcome.LoreDiscovered = true;
                    Msg(terminal, "bright_cyan", Loc.Get("discovery.effect.lore"));
                    break;
                case DiscEffectType.Ocean:
                    OceanPhilosophySystem.Instance.GainInsight(e.A);
                    outcome.OceanInsightGained = true;
                    Msg(terminal, "bright_blue", Loc.Get("discovery.effect.ocean"));
                    break;
                case DiscEffectType.Awakening:
                    OceanPhilosophySystem.Instance.GainInsight(Math.Max(5, e.A * 5));
                    outcome.OceanInsightGained = true;
                    Msg(terminal, "bright_magenta", Loc.Get("discovery.effect.awakening"));
                    break;
                case DiscEffectType.Memory:
                    outcome.MemoryTriggered = true;
                    Msg(terminal, "magenta", Loc.Get("discovery.effect.memory"));
                    break;
                case DiscEffectType.Loot:
                    GrantLoot(player, floor, terminal, outcome);
                    break;
                case DiscEffectType.Nothing:
                    Msg(terminal, "gray", Loc.Get("discovery.effect.nothing"));
                    break;
            }
        }

        private void GrantLoot(Character player, int floor, TerminalEmulator terminal, FeatureOutcome outcome)
        {
            try
            {
                if (player.Inventory != null && player.Inventory.Count < 50)
                {
                    var item = LootGenerator.GenerateDungeonLoot(floor, player.Class);
                    if (item != null)
                    {
                        player.Inventory.Add(item);
                        Msg(terminal, "bright_green", Loc.Get("discovery.effect.loot", item.Name));
                        return;
                    }
                }
            }
            catch (Exception ex) { DebugLogger.Instance?.LogError("DISCOVERY", $"loot grant failed: {ex.Message}"); }
            // Fallback: pack full or loot gen failed -> scaled gold so the reward is never lost.
            long gold = Scaled(floor, 80, 160);
            player.Gold += gold; outcome.GoldGained += gold;
            Msg(terminal, "bright_yellow", Loc.Get("discovery.effect.loot_gold", gold));
        }

        // v0.62.0 fix: discovery combat boons must SURVIVE into the next fight. TempAttackBonus /
        // TempDefenseBonus are reset to 0 at combat start (CombatEngine combat-init), so a boon
        // applied while examining a feature was wiped before it could matter. Route it through the
        // WellRested vehicle instead: combat applies WellRestedBonus% to attack AND defense, ticks
        // it down per fight, does NOT reset it at combat start, persists in the save, and already
        // shows on /health ("Well-Rested: +X% dmg/def (N combats)"). A flat boon amount (~6-12)
        // becomes that percent, capped, for a few fights; Max() so it never downgrades an existing
        // (e.g. inn-rested) buff.
        private void GrantCombatBlessing(Character player, int amount)
        {
            float bonus = Math.Clamp(amount / 100f, 0.05f, 0.20f);
            player.WellRestedBonus = Math.Max(player.WellRestedBonus, bonus);
            player.WellRestedCombats = Math.Max(player.WellRestedCombats, 3);
        }

        private void ApplyPermStat(Character player, string stat, int amt)
        {
            switch ((stat ?? "").ToUpperInvariant())
            {
                case "STR": player.BaseStrength += amt; break;
                case "DEX": player.BaseDexterity += amt; break;
                case "CON": player.BaseConstitution += amt; break;
                case "INT": player.BaseIntelligence += amt; break;
                case "WIS": player.BaseWisdom += amt; break;
                case "CHA": player.BaseCharisma += amt; break;
            }
            player.RecalculateStats();
        }

        // ---------- helpers ----------

        private long Scaled(int floor, int baseMin, int baseMax)
        {
            double mult = Math.Max(1.0, Math.Pow(Math.Max(1, floor), 1.5) / 10.0);
            int baseReward = random.Next(baseMin, baseMax + 1);
            return (long)(baseReward * mult);
        }

        private int GetStat(Character p, string stat) => (stat ?? "").ToUpperInvariant() switch
        {
            "STR" => (int)p.Strength,
            "DEX" => (int)p.Dexterity,
            "CON" => (int)p.Constitution,
            "INT" => (int)p.Intelligence,
            "WIS" => (int)p.Wisdom,
            "CHA" => (int)p.Charisma,
            _ => (int)p.Wisdom
        };

        private string LocStatName(string stat)
        {
            string key = $"discovery.stat.{(stat ?? "").ToUpperInvariant()}";
            string v = Loc.Get(key);
            return (string.IsNullOrEmpty(v) || v == key) ? (stat ?? "") : v;
        }
        private string LocStatusName(StatusEffect s)
        {
            string key = $"status.{s.ToString().ToLowerInvariant()}";
            string v = Loc.Get(key);
            return (string.IsNullOrEmpty(v) || v == key) ? s.ToString() : v;
        }

        private static bool IsYes(string input)
        {
            var v = input?.Trim().ToUpperInvariant();
            return GameConfig.IsAffirmative(input) || v == "1";
        }

        private static void Msg(TerminalEmulator terminal, string color, string text)
        {
            terminal.SetColor(color);
            terminal.WriteLine("  " + text);
        }

        // Resolve a single loc key with the authored English as fallback (Loc.Get returns the raw
        // key when missing, so we detect that and fall back to the inline source string).
        private static string Resolve(string id, string suffix, string fallback)
        {
            string key = $"discovery.{id}.{suffix}";
            string v = Loc.Get(key);
            return (string.IsNullOrEmpty(v) || v == key) ? (fallback ?? "") : v;
        }

        private static List<string> ResolveArray(string id, string suffix, List<string> fallback)
        {
            var result = new List<string>();
            for (int i = 0; i < (fallback?.Count ?? 0); i++)
                result.Add(Resolve(id, $"{suffix}.{i}", fallback[i]));
            return result;
        }
    }
}
