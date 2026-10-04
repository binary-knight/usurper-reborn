using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UsurperRemake.Data;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// v1.2.5: the one place a monster's name is turned into the reader's language for display.
    ///
    /// A monster's Name is stored and matched: the combat engine's name checks (undead, demon, boss words),
    /// quest kills (QuestSystem.OnMonsterKilled matches the English name and tier), the castle's monster
    /// guards (dismissed and fought by name), pets, news and the wiki. So Name stays the English the tables
    /// write, whatever the reader's language, and only the screen shows it translated.
    ///
    /// The tables' English names are the stable ids. A name shows through monster.name.{id} (id: the English
    /// name in lower case, every run of other characters an underscore), used only when that key's English
    /// text is the stored name. A champion is its tier's name in its family's champion template
    /// (monster.champion.{family}, or monster.champion.alpha for a tier that already holds a rank), read from
    /// the monster's stored TierName and FamilyName; a group leader is monster.leader around its member's
    /// name. A name built from a Loc key at run time (FromKey) is written in English and remembered with its
    /// key and arguments. Old God names show through oldgod.{key}.name. Anything else shows as stored.
    ///
    /// The result is for the screen only: never assign it to Name or to anything saved or compared.
    /// </summary>
    public static class MonsterNames
    {
        private static readonly ConcurrentDictionary<string, (string Key, string[] Args)> Built = new(StringComparer.Ordinal);

        private static readonly Lazy<Dictionary<string, string>> OldGodNames = new(() =>
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var god in OldGodsData.GetAllOldGodsWithVariants())
            {
                string key = $"oldgod.{god.NameKeyPart}.name";
                if (!map.ContainsKey(god.Name) && Loc.HasIn("en", key) && Loc.GetIn("en", key) == god.Name)
                    map[god.Name] = key;
            }
            return map;
        });

        /// <summary>The id part of a key for an English name: lower case, other characters as one underscore.</summary>
        public static string IdOf(string englishName) =>
            Regex.Replace(englishName.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');

        /// <summary>The key a stored English name shows through, or null when it has none.</summary>
        public static string? KeyOf(string? englishName)
        {
            if (string.IsNullOrEmpty(englishName)) return null;
            string key = "monster.name." + IdOf(englishName);
            if (Loc.HasIn("en", key) && Loc.GetIn("en", key) == englishName) return key;
            return OldGodNames.Value.TryGetValue(englishName, out var godKey) ? godKey : null;
        }

        /// <summary>The key a family's English name shows through, or null.</summary>
        public static string? FamilyKeyOf(string? familyName)
        {
            if (string.IsNullOrEmpty(familyName)) return null;
            string key = "monster.family." + IdOf(familyName);
            return Loc.HasIn("en", key) && Loc.GetIn("en", key) == familyName ? key : null;
        }

        /// <summary>
        /// A monster name built from a Loc key (a quest scene's foe, a summoned thrall): returns the English
        /// text, to be stored as the Name, and remembers how to show it in any language. String arguments
        /// are shown through this helper too (a thrall's master is a monster name).
        /// </summary>
        public static string FromKey(string key, params string[] args)
        {
            string english = args.Length == 0 ? Loc.GetIn("en", key) : Loc.GetIn("en", key, args.Cast<object>().ToArray());
            if (!string.IsNullOrEmpty(english)) Built[english] = (key, args);
            return english;
        }

        /// <summary>The monster's name in the reader's language (the session's, or an open RenderLanguage scope).</summary>
        public static string Display(Monster? monster) =>
            monster == null ? "" : Recorded(DisplayIn(GameConfig.Language, monster), lang => DisplayIn(lang, monster));

        /// <summary>A stored monster name in the reader's language.</summary>
        public static string Display(string? storedName) =>
            Recorded(DisplayIn(GameConfig.Language, storedName), lang => DisplayIn(lang, storedName));

        /// <summary>A family's name in the reader's language.</summary>
        public static string Family(string? familyName) =>
            Recorded(FamilyIn(GameConfig.Language, familyName), lang => FamilyIn(lang, familyName));

        /// <summary>A family's name in the given language; an unknown family shows as stored.</summary>
        public static string FamilyIn(string lang, string? familyName) =>
            FamilyKeyOf(familyName) is string key ? Loc.GetIn(lang, key) : familyName ?? "";

        /// <summary>
        /// v1.2.5: like ItemNames, a shown name written while combat output is recorded for group members who
        /// read another language is noted with how to show it in theirs; it is only used as an argument.
        /// </summary>
        private static string Recorded(string shown, Func<string, string> renderIn)
        {
            Loc.RecordArgument(shown, renderIn);
            return shown;
        }

        /// <summary>The monster's name in the given language: a champion through its tier and family.</summary>
        public static string DisplayIn(string lang, Monster monster)
        {
            string name = monster.Name ?? "";
            if (!string.IsNullOrEmpty(monster.TierName) && name != monster.TierName
                && ChampionIn(lang, name, monster.TierName, monster.FamilyName) is string champion)
                return champion;
            return DisplayIn(lang, name);
        }

        /// <summary>A stored monster name in the given language.</summary>
        public static string DisplayIn(string lang, string? storedName)
        {
            if (string.IsNullOrEmpty(storedName)) return storedName ?? "";
            if (KeyOf(storedName) is string key) return Loc.GetIn(lang, key);
            if (Built.TryGetValue(storedName, out var built))
                return built.Args.Length == 0
                    ? Loc.GetIn(lang, built.Key)
                    : Loc.GetIn(lang, built.Key, built.Args.Select(a => (object)DisplayIn(lang, a)).ToArray());
            string leaderSuffix = Loc.GetIn("en", "monster.leader", "");
            if (storedName.EndsWith(leaderSuffix, StringComparison.Ordinal) && storedName.Length > leaderSuffix.Length)
            {
                string member = storedName.Substring(0, storedName.Length - leaderSuffix.Length);
                if (KeyOf(member) is string memberKey) return Loc.GetIn(lang, "monster.leader", Loc.GetIn(lang, memberKey));
            }
            return storedName;
        }

        /// <summary>The champion templates a family's mini-boss can be named by, as MonsterGenerator names it.</summary>
        internal static IEnumerable<string> ChampionKeys(string? familyName)
        {
            if (!string.IsNullOrEmpty(familyName)) yield return "monster.champion." + IdOf(familyName);
            yield return "monster.champion.alpha";
            yield return "monster.champion.default";
        }

        /// <summary>The champion's name in the given language when the stored name is its tier in one of its
        /// family's templates (English), else null.</summary>
        private static string? ChampionIn(string lang, string storedName, string tierName, string? familyName)
        {
            if (KeyOf(tierName) is not string tierKey) return null;
            foreach (var template in ChampionKeys(familyName))
            {
                if (!Loc.HasIn("en", template)) continue;
                if (Loc.GetIn("en", template, tierName) == storedName)
                    return Loc.GetIn(lang, template, Loc.GetIn(lang, tierKey));
            }
            return null;
        }

        /// <summary>
        /// The monster as a sentence subject: English keeps Monster.TheNameOrName ("The Drake", a proper name
        /// bare); another language shows the name alone, its templates carry their own article. Before 1.2.5
        /// the English "The" showed in every language.
        /// </summary>
        public static string TheNameIn(string lang, Monster monster) =>
            string.IsNullOrEmpty(lang) || lang == "en" ? monster.TheNameOrName : DisplayIn(lang, monster);

        /// <summary>TheNameIn in the reader's language, recorded for a group capture.</summary>
        public static string TheName(Monster monster) =>
            Recorded(TheNameIn(GameConfig.Language, monster), lang => TheNameIn(lang, monster));

        /// <summary>
        /// A count of monsters of one name, as an encounter line shows it: English keeps its plural rules
        /// ("3 Wolves"); another language uses the name's plural key when it has one, else the name.
        /// </summary>
        public static string CountIn(string lang, int count, string storedName, Func<string, string> englishPlural)
        {
            if (count <= 1) return DisplayIn(lang, storedName);
            if (lang == "en") return englishPlural(storedName);
            if (KeyOf(storedName) is string key && key.StartsWith("monster.name.", StringComparison.Ordinal))
            {
                string pluralKey = "monster.plural." + key.Substring("monster.name.".Length);
                if (Loc.HasIn(lang, pluralKey)) return Loc.GetIn(lang, pluralKey);
            }
            return DisplayIn(lang, storedName);
        }

        /// <summary>CountIn for a group of monsters sharing one stored name, shown through one of them (a
        /// champion's name is built from its tier and family).</summary>
        public static string CountIn(string lang, int count, Monster sample, Func<string, string> englishPlural)
        {
            if (count <= 1) return DisplayIn(lang, sample);
            if (lang == "en") return englishPlural(sample.Name);
            string shown = CountIn(lang, count, sample.Name, englishPlural);
            return shown == sample.Name ? DisplayIn(lang, sample) : shown;
        }

        /// <summary>CountIn for a group, in the reader's language, recorded for a group capture.</summary>
        public static string Count(int count, Monster sample, Func<string, string> englishPlural) =>
            Recorded(CountIn(GameConfig.Language, count, sample, englishPlural), lang => CountIn(lang, count, sample, englishPlural));

        /// <summary>CountIn in the reader's language, recorded for a group capture.</summary>
        public static string Count(int count, string storedName, Func<string, string> englishPlural) =>
            Recorded(CountIn(GameConfig.Language, count, storedName, englishPlural), lang => CountIn(lang, count, storedName, englishPlural));
    }
}
