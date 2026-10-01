"use strict";
const fs = require("node:fs");
const path = require("node:path");
const MarkdownIt = require("markdown-it");
const root = path.resolve(__dirname, "../..");
const sections = [
  ["getting-started", "Getting started"],
  ["characters", "Characters"],
  ["combat", "Combat"],
  ["gods", "Gods and faith"],
  ["world", "World"],
  ["items", "Items and economy"],
  ["monsters", "Monsters and bosses"],
  ["online", "Online world"],
  ["reference", "Reference"],
];
const escape = (value) =>
  String(value ?? "")
    .replace(/[\u2013\u2014]/g, "-")
    .replace(/\p{Extended_Pictographic}/gu, "")
    .replace(
      /[&<>"']/g,
      (c) =>
        ({
          "&": "&amp;",
          "<": "&lt;",
          ">": "&gt;",
          '"': "&quot;",
          "'": "&#39;",
        })[c],
    );
const name = (e) => e.name?.en || e.id;
const label = (value) =>
  String(value)
    .replace(/([a-z\d])([A-Z])/g, "$1 $2")
    .replace(/_/g, " ")
    .replace(/\bhp\b/gi, "HP")
    .replace(/^./, (c) => c.toUpperCase());
const slug = (value) =>
  String(value)
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-|-$/g, "");
const url = (kind, id) => {
  // Class:slot spell keys become Class-slot URLs so directory names also work on Windows.
  const segment = String(id).replace(/:/g, "-");
  if (!/^[A-Za-z0-9_-]+$/.test(segment)) throw Error(`Unsafe entity ID: ${id}`);
  return `/wiki/en/${kind}/${segment}/`;
};
const link = (kind, e) => `<a href="${url(kind, e.id)}">${escape(name(e))}</a>`;
function publicHtml(html) {
  let depth = 0,
    position = 0,
    publicText = "";
  for (const match of html.matchAll(/<\/?details\b[^>]*>/gi)) {
    if (depth === 0) publicText += html.slice(position, match.index);
    depth += /^<\//.test(match[0]) ? -1 : 1;
    if (depth < 0) throw Error("Unbalanced spoiler HTML");
    position = match.index + match[0].length;
  }
  if (depth) throw Error("Unclosed spoiler HTML");
  return publicText + html.slice(position);
}
const textOf = (html) =>
  publicHtml(html)
    .replace(/<[^>]*>/g, " ")
    .replace(
      /&(?:amp|lt|gt|quot|#39);/g,
      (s) =>
        ({
          "&amp;": "&",
          "&lt;": "<",
          "&gt;": ">",
          "&quot;": '"',
          "&#39;": "'",
        })[s],
    )
    .replace(/\s+/g, " ")
    .trim();
function table(headers, rows, caption) {
  return `<div class="table-wrap" role="region" aria-label="${escape(caption)}" tabindex="0"><table><caption>${escape(caption)}</caption><thead><tr>${headers.map((h) => `<th scope="col">${escape(h)}</th>`).join("")}</tr></thead><tbody>${rows.map((row) => `<tr>${row.map((c, i) => (i === 0 ? `<th scope="row">${c}</th>` : `<td>${c}</td>`)).join("")}</tr>`).join("")}</tbody></table></div>`;
}
function values(obj, caption) {
  return table(
    ["Rule", "Value"],
    Object.entries(obj).map(([k, v]) => [
      escape(label(k)),
      escape(Array.isArray(v) ? v.join(", ") : v),
    ]),
    caption,
  );
}
const spoiler = (summary, body) =>
  `<details class="spoiler"><summary>${escape(summary)}</summary>${body}</details>`;
function loadData(dir) {
  const meta = JSON.parse(fs.readFileSync(path.join(dir, "meta.json"), "utf8"));
  if (meta.schemaVersion !== 1 || !meta.gameVersion)
    throw Error("Unsupported wiki metadata");
  const data = {};
  for (const kind of [
    "items",
    "monsters",
    "classes",
    "races",
    "spells",
    "abilities",
    "gods",
    "mental",
    "balance",
    "achievements",
    "bosses",
  ]) {
    const file = JSON.parse(
      fs.readFileSync(path.join(dir, kind + ".json"), "utf8"),
    );
    if (file.schemaVersion !== 1 || file.gameVersion !== meta.gameVersion)
      throw Error(`Mismatched schema/version: ${kind}`);
    data[kind] = file.data;
  }
  for (const kind of [
    "items",
    "monsters",
    "classes",
    "races",
    "spells",
    "abilities",
    "achievements",
  ]) {
    if (!Array.isArray(data[kind]) || !data[kind].length)
      throw Error(`Empty dataset: ${kind}`);
    const ids = new Set();
    for (const e of data[kind]) {
      if (e.id == null || !name(e) || ids.has(String(e.id)))
        throw Error(`Invalid or duplicate ID: ${kind}/${e.id}`);
      ids.add(String(e.id));
    }
  }
  const classes = new Set(data.classes.map((e) => e.id));
  const races = new Set(data.races.map((e) => e.id));
  for (const c of data.classes) {
    if (!Array.isArray(c.allowedRaces) || !c.allowedRaces.length)
      throw Error(`Missing race compatibility: ${c.id}`);
    for (const race of c.allowedRaces)
      if (!races.has(race)) throw Error(`Unknown race: ${race}`);
  }
  for (const spell of data.spells)
    if (!classes.has(spell.classId))
      throw Error(`Unknown spell class: ${spell.classId}`);
  for (const e of [...data.items, ...data.abilities])
    for (const c of e.classes)
      if (!classes.has(c)) throw Error(`Unknown class: ${c}`);
  return { meta, data };
}
function renderers(data) {
  const find = (list, id) => {
    const e = list.find(
      (e) =>
        String(e.id).toLowerCase() === id.toLowerCase() ||
        name(e).toLowerCase() === id.toLowerCase(),
    );
    if (!e) throw Error(`Unknown entity: ${id}`);
    return e;
  };
  const constants = {
    ...data.balance.moddableDefaults,
    ...data.balance.characterConstants,
    ...data.balance.combatConstants,
    ...data.balance.godConstants,
    ...data.mental.constants,
    ...data.gods.favorRules,
  };
  function god(e) {
    const boons = Object.fromEntries(
      Object.entries(e.boonConstants).map(([k, v]) => [
        k.replace("GodBoon" + name(e), ""),
        v,
      ]),
    );
    return (
      `<p>Domain: ${escape(e.domainName.en)}. Wards: ${escape(e.wards.join(", ") || "None")}.</p>` +
      "<p>Boon constants below are base values. Favor strength and an active prayer change their effective size. Percent fields are percentages, not additional flat attributes.</p>" +
      values(boons, "Base boon rules") +
      table(
        ["Deed or taboo", "Favor change"],
        e.deedsAndTaboos.map((d) => [escape(label(d.act)), escape(d.favor)]),
        "Deeds and taboos",
      ) +
      spoiler(
        "Spoiler: divine lore",
        `<p>${escape(e.description)}</p><p>Old God echo: ${escape(e.echoesOldGod || "None")}.</p>`,
      )
    );
  }
  function monster(e) {
    return (
      `<p>Attack type: ${escape(e.attackType)}. Samples are normal non-boss monsters at the tier endpoints. NG+, server mods and special encounters change live values.</p>` +
      table(
        [
          "Tier",
          "Level range",
          "HP at min / max",
          "Strength at min / max",
          "Defence at min / max",
          "Abilities",
        ],
        e.tiers.map((t) => [
          escape(name(t)),
          `${t.minLevel} - ${t.maxLevel}`,
          `${t.normalStatsAtMinLevel.hp} / ${t.normalStatsAtMaxLevel.hp}`,
          `${t.normalStatsAtMinLevel.strength} / ${t.normalStatsAtMaxLevel.strength}`,
          `${t.normalStatsAtMinLevel.defence} / ${t.normalStatsAtMaxLevel.defence}`,
          escape(t.abilities.join(", ")),
        ]),
        "Monster tier samples",
      ) +
      spoiler("Spoiler: family lore", `<p>${escape(e.description)}</p>`)
    );
  }
  function boss(e, heading = 2) {
    const fields = Object.fromEntries(
      Object.entries(e).filter(
        ([k]) =>
          k.startsWith("base") ||
          ["attacksPerRound", "dungeonFloor", "floorLevel", "element"].includes(
            k,
          ),
      ),
    );
    const phases = e.phases
      ? e.phases
          .map(
            (p, i) =>
              `<h${heading}>Phase ${i + 1}</h${heading}><ul>${p.map((a) => `<li>${escape(typeof a === "string" ? a : a.name + ": " + a.description)}</li>`).join("")}</ul>`,
          )
          .join("")
      : `<pre>${escape(JSON.stringify(e.abilities, null, 2))}</pre>`;
    return (
      `<p>${escape(e.title)}. These are base definitions, not a guarantee of encounter stats.</p>` +
      values(fields, "Boss base definition") +
      phases
    );
  }
  function directive(raw) {
    const match = raw.trim().match(/^([\w-]+):([\w:-]+)(?:\s+(.+))?$/);
    if (!match) throw Error(`Invalid directive: ${raw}`);
    const [, kind, id, filter] = match;
    if (filter && kind !== "table") throw Error(`Unexpected filter: ${raw}`);
    if (kind === "balance") {
      if (!(id in constants)) throw Error(`Unknown balance constant: ${id}`);
      return escape(constants[id]);
    }
    if (kind === "god") return god(find(data.gods.gods, id));
    if (kind === "monster-family") return monster(find(data.monsters, id));
    if (kind !== "table") throw Error(`Unknown directive: ${kind}`);
    let rows = data[id];
    if (filter) {
      const m = filter.match(/^(slot|classId)=([\w]+)$/);
      if (!m || !Array.isArray(rows) || !rows.some((r) => r[m[1]] === m[2]))
        throw Error(`Invalid table filter: ${raw}`);
      rows = rows.filter((r) => r[m[1]] === m[2]);
    }
    if (id === "items")
      return table(
        ["Item", "Slot", "Level", "Rarity", "Base value", "Weapon / armor"],
        rows.map((e) => [
          link("items", e),
          escape(e.slot),
          e.minLevel,
          escape(e.rarity),
          e.value,
          `${e.stats.weaponPower} / ${e.stats.armorClass}`,
        ]),
        "Built-in equipment",
      );
    if (id === "classes")
      return table(
        ["Class", "Type", "Specializations"],
        rows.map((e) => [
          link("characters/classes", e),
          e.prestige ? "Prestige" : "Starting",
          escape(e.specializations.map(name).join(", ")),
        ]),
        "Classes",
      );
    if (id === "races")
      return table(
        [
          "Race",
          "HP bonus",
          "Strength bonus",
          "Defence bonus",
          "Stamina bonus",
        ],
        rows.map((e) => [
          link("characters/races", e),
          ...["hpBonus", "strengthBonus", "defenceBonus", "staminaBonus"].map(
            (k) => e.bonusesAndCreationRanges[k],
          ),
        ]),
        "Race creation bonuses",
      );
    if (id === "monsters")
      return table(
        ["Family", "Attack type", "Tiers"],
        rows.map((e) => [
          link("monsters/families", e),
          escape(e.attackType),
          e.tiers.length,
        ]),
        "Monster families",
      );
    if (id === "gods")
      return table(
        ["God", "Domain", "Wards"],
        data.gods.gods.map((e) => [
          link("gods", e),
          escape(e.domainName.en),
          escape(e.wards.join(", ")),
        ]),
        "Canonical gods",
      );
    if (id === "favor")
      return table(
        ["Tier", "Minimum Favor", "Boon strength (%)"],
        data.gods.tiers.map((e) => [
          escape(e.name),
          e.minFavor,
          e.strengthPercent,
        ]),
        "Favor tiers",
      );
    if (id === "mental")
      return table(
        ["Band", "Minimum", "Maximum"],
        data.mental.bands.map((e) => [escape(e.name), e.min, e.max]),
        "Mental bands",
      );
    if (id === "spells")
      return table(
        ["Spell", "Class", "Required level", "Mana", "Effect"],
        rows.map((e) => [
          link("characters/spells", e),
          escape(e.classId),
          e.levelRequired,
          e.manaCost,
          escape(e.description.en),
        ]),
        "Spell definitions",
      );
    if (id === "abilities")
      return table(
        ["Ability", "Classes", "Required level", "Stamina / mana", "Effect"],
        rows.map((e) => [
          link("characters/abilities", e),
          escape(e.classes.join(", ")),
          e.levelRequired,
          `${e.staminaCost} / ${e.manaCost}`,
          escape(e.description.en),
        ]),
        "Ability definitions",
      );
    if (id === "achievements")
      return (
        table(
          ["Achievement", "Category", "Tier", "Points", "Goal"],
          rows
            .filter((e) => !e.isSecret)
            .map((e) => [
              escape(name(e)),
              escape(e.category),
              escape(e.tier),
              e.pointValue,
              escape(e.description.en),
            ]),
          "Public achievements",
        ) +
        spoiler(
          "Spoiler: secret achievements",
          table(
            ["Achievement", "Goal"],
            rows
              .filter((e) => e.isSecret)
              .map((e) => [escape(name(e)), escape(e.description.en)]),
            "Secret achievements",
          ),
        )
      );
    if (id === "bosses")
      return table(
        ["Boss", "Element", "Base level"],
        data.bosses.world.map((e) => [
          link("monsters/bosses", e),
          escape(e.element),
          e.baseLevel,
        ]),
        "World bosses",
      );
    if (id === "secret-bosses")
      return spoiler(
        "Spoiler: story and secret bosses",
        [...data.bosses.oldGods, ...data.bosses.secret]
          .map((e) => `<h2>${escape(name(e))}</h2>${boss(e, 3)}`)
          .join(""),
      );
    if (id === "balance")
      return (
        values(data.balance.moddableDefaults, "Moddable balance defaults") +
        spoiler(
          "Spoiler: exact combat and hidden-effect rules",
          values(data.balance.combatConstants, "Combat constants") +
            values(data.balance.godConstants, "Faith constants") +
            values(data.mental.constants, "Mental constants"),
        )
      );
    throw Error(`Unknown table: ${id}`);
  }
  return { directive, god, monster, boss };
}
function compareVersions(a, b) {
  const pa = String(a).split(".").map(Number);
  const pb = String(b).split(".").map(Number);
  if ([...pa, ...pb].some((n) => !Number.isInteger(n)))
    throw Error(`Not a version: ${a} or ${b}`);
  for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
    const d = (pa[i] || 0) - (pb[i] || 0);
    if (d) return Math.sign(d);
  }
  return 0;
}
// One `history:` line per version: "<x.y.z> | <one sentence>" or "<x.y.z> | none".
function parseHistory(lines, file, gameVersion) {
  const history = [];
  const seen = new Map();
  for (const raw of lines) {
    const m = raw.match(/^(\S+)\s*\|\s*(.*)$/);
    if (!m) throw Error(`Invalid history line in ${file}: expected "<x.y.z> | <sentence>" or "<x.y.z> | none": ${raw}`);
    const [, version, text] = m;
    const sentence = text.trim();
    if (!/^\d+\.\d+\.\d+$/.test(version))
      throw Error(`Invalid history version in ${file}: ${version} is not x.y.z`);
    if (!sentence)
      throw Error(`Empty history sentence in ${file} for ${version}`);
    const none = sentence === "none";
    if (seen.has(version))
      throw Error(
        seen.get(version) !== none
          ? `History for ${version} in ${file} has both none and a sentence`
          : `Two history lines for ${version} in ${file}`,
      );
    seen.set(version, none);
    if (gameVersion && compareVersions(version, gameVersion) > 0)
      throw Error(`History version ${version} in ${file} is newer than the game version ${gameVersion}`);
    history.push({ version, sentence: none ? null : sentence });
  }
  return history;
}
function readPages(dir, options = {}) {
  const result = [];
  for (const entry of fs
    .readdirSync(dir, { withFileTypes: true })
    .sort((a, b) => a.name.localeCompare(b.name))) {
    const file = path.join(dir, entry.name);
    if (entry.isDirectory()) result.push(...readPages(file, options));
    else if (entry.name.endsWith(".md")) {
      const source = fs.readFileSync(file, "utf8");
      const match = source.match(/^---\n([\s\S]*?)\n---\n([\s\S]*)$/);
      if (!match) throw Error(`Missing frontmatter: ${file}`);
      const fields = {};
      const historyLines = [];
      for (const line of match[1].split("\n")) {
        const h = line.match(/^history: (.*)$/);
        if (h) {
          historyLines.push(h[1]);
          continue;
        }
        const m = line.match(/^(title|path|checked|sources): (.+)$/);
        if (!m) throw Error(`Unknown frontmatter: ${line}`);
        fields[m[1]] = m[2];
      }
      if (
        !fields.title ||
        !fields.checked ||
        !fields.sources ||
        !/^\/wiki\/en\/(?:[a-z0-9-]+\/)*$/.test(fields.path)
      )
        throw Error(`Invalid page metadata: ${file}`);
      result.push({
        ...fields,
        history: parseHistory(historyLines, file, options.gameVersion),
        markdown: match[2],
        sourceFile: path.relative(root, file).split(path.sep).join("/"),
      });
    }
  }
  return result;
}
// Shown history entries, newest version first; `none` markers are never shown.
const shownHistory = (history = []) =>
  history
    .filter((h) => h.sentence)
    .sort((a, b) => compareVersions(b.version, a.version));
function build(options = {}) {
  const dataDir = options.dataDir || path.join(root, "wiki-data");
  const contentDir = options.contentDir || path.join(root, "DOCS/wiki");
  const outDir = options.outDir || path.join(root, "web/wiki");
  const { meta, data } = loadData(dataDir);
  let sourceRef = meta.commit || process.env.GITHUB_SHA;
  if (!sourceRef) {
    try {
      sourceRef = require("node:child_process")
        .execFileSync("git", ["rev-parse", "HEAD"], {
          cwd: root,
          encoding: "utf8",
        })
        .trim();
    } catch (_) {
      sourceRef = "v" + meta.gameVersion;
    }
  }
  const render = renderers(data);
  const md = new MarkdownIt({
    html: false,
    linkify: false,
    typographer: false,
  });
  md.renderer.rules.heading_open = (tokens, idx, _opts, _env, self) => {
    tokens[idx].attrSet("id", slug(tokens[idx + 1].content));
    return self.renderToken(tokens, idx, _opts);
  };
  const pages = readPages(contentDir, { gameVersion: meta.gameVersion });
  for (const page of pages) {
    const disclosureLines = page.markdown
      .split("\n")
      .filter((line) => line.startsWith(":::"));
    let inside = false;
    for (const line of disclosureLines) {
      if (line.startsWith(":::spoiler ")) {
        if (inside) throw Error(`Nested spoiler: ${page.path}`);
        inside = true;
      } else if (line.trim() === ":::") {
        if (!inside) throw Error(`Unexpected spoiler end: ${page.path}`);
        inside = false;
      } else throw Error(`Invalid spoiler syntax: ${page.path}`);
    }
    if (inside) throw Error(`Unclosed spoiler: ${page.path}`);
    if (
      /[\u2013\u2014]|\p{Extended_Pictographic}/u.test(
        page.markdown +
          page.title +
          page.history.map((h) => h.sentence || "").join(" "),
      )
    )
      throw Error(`Forbidden punctuation or emoji: ${page.path}`);
    const blocks = [];
    let source = page.markdown.replace(
      /\{\{([^{}]+)\}\}/g,
      (_all, directive) => {
        const html = render.directive(directive);
        if (!html.startsWith("<")) return html;
        const token = `WIKIBLOCK${blocks.length}TOKEN`;
        blocks.push(html);
        return "\n\n" + token + "\n\n";
      },
    );
    if (/\{\{|\}\}/.test(source))
      throw Error(`Malformed directive: ${page.path}`);
    // Explicit Markdown spoiler blocks are rendered separately; raw HTML stays disabled.
    source = source.replace(
      /^:::spoiler ([^\n]+)\n([\s\S]*?)^:::\s*$/gm,
      (_all, summary, body) => {
        const token = `WIKIBLOCK${blocks.length}TOKEN`;
        blocks.push(
          spoiler(
            summary,
            md
              .render(body)
              .replace(/<p>WIKIBLOCK(\d+)TOKEN<\/p>/g, (_m, n) => blocks[n]),
          ),
        );
        return "\n\n" + token + "\n\n";
      },
    );
    if (source.includes(":::")) throw Error(`Unclosed spoiler: ${page.path}`);
    page.body = md
      .render(source)
      .replace(/<p>WIKIBLOCK(\d+)TOKEN<\/p>/g, (_m, n) => blocks[n]);
  }
  const add = (kind, e, body, sources) =>
    pages.push({
      path: url(kind, e.id),
      title: name(e),
      checked: meta.gameVersion,
      sources,
      body,
    });
  for (const e of data.items)
    add(
      "items",
      e,
      `<p>Built-in template ID ${e.id}. Randomly generated shop stock, modifiers and server mods are not included. Acquisition is not inferred from rarity or value.</p>` +
        (e.description
          ? spoiler("Spoiler: item lore", `<p>${escape(e.description)}</p>`)
          : "") +
        values(
          {
            slot: e.slot,
            handedness: e.handedness,
            weaponType: e.weaponType,
            armorType: e.armorType,
            weightClass: e.weightClass,
            rarity: e.rarity,
            minLevel: e.minLevel,
            strengthRequired: e.strengthRequired,
            baseValue: e.value,
            baseSellValue: e.sellValue,
            classes: e.classes.length
              ? e.classes
              : "No template class restriction",
            requiresGood: e.requiresGood,
            requiresEvil: e.requiresEvil,
          },
          "Equipment requirements and base values",
        ) +
        values(
          Object.fromEntries(
            Object.entries(e.stats).filter(([, v]) => v !== 0),
          ),
          "Nonzero equipment stats",
        ) +
        values(
          Object.fromEntries(
            Object.entries(e).filter(
              ([k]) => k.startsWith("has") || k.startsWith("is"),
            ),
          ),
          "Equipment flags",
        ),
      "Scripts/Data/EquipmentData.cs",
    );
  for (const e of data.classes)
    add(
      "characters/classes",
      e,
      `<p>${e.prestige ? "Prestige class. Unlock it through play, not the initial character selector." : "Starting class available at character creation when compatible with your race."} Starting class values combine with race creation bonuses; they are not the final equipped character sheet.</p>` +
        `<p>Compatible races: ${e.allowedRaces
          .map((id) =>
            link(
              "characters/races",
              data.races.find((r) => r.id === id),
            ),
          )
          .join(", ")}.</p>` +
        values(e.startingAttributes, "Class creation values") +
        values(e.growthPerLevel, "Base class growth per level") +
        e.specializations
          .map(
            (s) =>
              `<h2>${escape(name(s))}</h2><p>${escape(s.description)} Role: ${escape(s.role)}.</p>${values(s.bonusesPerLevel, "Specialization growth per level")}`,
          )
          .join("") +
        '<p><a href="/wiki/en/characters/specializations/">Choosing a specialization</a> | <a href="/wiki/en/characters/spells/">Spells</a> | <a href="/wiki/en/characters/abilities/">Abilities</a></p>' +
        values(
          Object.fromEntries(
            Object.entries(e.name).filter(([k]) => k !== "en"),
          ),
          "Verified translated class names",
        ),
      "Scripts/Core/GameConfig.cs, Scripts/Locations/LevelMasterLocation.cs, Scripts/Data/SpecializationData.cs",
    );
  for (const e of data.races)
    add(
      "characters/races",
      e,
      values(e.bonusesAndCreationRanges, "Race bonuses and creation ranges") +
        `<p>NPC lifespan setting: ${e.npcLifespanYears} years. This is an NPC simulation setting, not a promise about player survival.</p>` +
        values(
          Object.fromEntries(
            Object.entries(e.name).filter(([k]) => k !== "en"),
          ),
          "Verified translated race names",
        ),
      "Scripts/Core/GameConfig.cs",
    );
  for (const e of data.monsters)
    add(
      "monsters/families",
      e,
      render.monster(e),
      "Scripts/Data/MonsterFamilies.cs, Scripts/Systems/MonsterGenerator.cs",
    );
  for (const e of data.spells)
    add(
      "characters/spells",
      e,
      `<p>${escape(e.description.en)}</p><p>Class: ${link(
        "characters/classes",
        data.classes.find((c) => c.id === e.classId),
      )}. This is a fixed spell definition; your character must still learn it and meet the combat requirements.</p>` +
        values(
          {
            catalogSlot: e.level,
            levelRequired: e.levelRequired,
            manaCost: e.manaCost,
            magicWords: e.magicWords,
            isMultiTarget: e.isMultiTarget,
            spellType: e.spellType,
          },
          "Spell template",
        ) +
        '<p><a href="/wiki/en/characters/spells/">All spells</a></p>',
      "Scripts/Systems/SpellSystem.cs",
    );
  for (const e of data.abilities)
    add(
      "characters/abilities",
      e,
      `<p>${escape(e.description.en)}</p><p>Classes: ${e.classes
        .map((id) =>
          link(
            "characters/classes",
            data.classes.find((c) => c.id === id),
          ),
        )
        .join(
          ", ",
        )}. Template numbers are not a prediction of final damage or healing after your attributes, equipment and combat effects.</p>` +
        values(
          Object.fromEntries(
            Object.entries(e).filter(
              ([key]) =>
                !["id", "name", "description", "classes"].includes(key),
            ),
          ),
          "Ability template and requirements",
        ) +
        '<p><a href="/wiki/en/characters/abilities/">All abilities</a></p>',
      "Scripts/Systems/ClassAbilitySystem.cs",
    );
  for (const e of data.gods.gods)
    add(
      "gods",
      e,
      render.god(e) +
        '<p><a href="/wiki/en/gods/favor/">Favor, prayer and sacrifice</a> | <a href="/wiki/en/characters/mental/">Mental and wards</a></p>',
      "Scripts/Systems/GodBoonSystem.cs, Scripts/Systems/GodDeedSystem.cs, Scripts/Core/GameConfig.cs",
    );
  for (const e of data.bosses.world)
    add("monsters/bosses", e, render.boss(e), "Scripts/Data/WorldBossData.cs");
  const urls = new Set();
  for (const p of pages) {
    if (urls.has(p.path)) throw Error(`Duplicate URL: ${p.path}`);
    urls.add(p.path);
  }
  const anchors = new Map(
    pages.map((p) => [
      p.path,
      new Set([...p.body.matchAll(/\bid="([^"]+)"/g)].map((m) => m[1])),
    ]),
  );
  for (const page of pages) {
    if (/<h1\b/i.test(page.body))
      throw Error(`Use level-two headings below the page title: ${page.path}`);
    for (const m of page.body.matchAll(/href="([^"]+)"/g)) {
      const href = m[1];
      if (/^(https?:\/\/|mailto:)/.test(href)) continue;
      const resolved = new URL(
        href.replace(/&amp;/g, "&"),
        "https://wiki.invalid" + page.path,
      );
      if (resolved.pathname.startsWith("/wiki/")) {
        if (!urls.has(resolved.pathname))
          throw Error(`Broken wiki link in ${page.path}: ${href}`);
        if (
          resolved.hash &&
          !anchors
            .get(resolved.pathname)
            .has(decodeURIComponent(resolved.hash.slice(1)))
        )
          throw Error(`Broken anchor: ${href}`);
      } else if (!["/", "/#connect"].includes(href))
        throw Error(`Non-wiki local link: ${href}`);
    }
    for (const source of page.sources.split(/,\s*/))
      if (
        !(
          source === "README.md" ||
          /^(Scripts|DOCS|web)\/[\w./-]+\.(cs|md|js)$/.test(source)
        ) ||
        source.includes("..") ||
        !fs.existsSync(path.join(root, source))
      )
        throw Error(`Missing or invalid source: ${source}`);
  }
  for (const [section] of sections)
    if (!urls.has(`/wiki/en/${section}/`))
      throw Error(`Missing section landing page: ${section}`);
  const secretNames = [
    ...data.bosses.secret,
    ...data.bosses.oldGods,
    ...data.achievements.filter((e) => e.isSecret),
  ]
    .map(name)
    .filter((n) => n.length > 3);
  const redact = (text) =>
    secretNames.reduce(
      (t, n) =>
        t.replace(
          new RegExp(n.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"), "gi"),
          "[spoiler]",
        ),
      text,
    );
  const search = pages.map((p) => ({
    title: redact(p.title),
    path: p.path,
    headings: [
      ...publicHtml(p.body).matchAll(/<h[23][^>]*>([\s\S]*?)<\/h[23]>/g),
    ].map((m) => redact(textOf(m[1]))),
    text: redact(textOf(p.body)),
    // Hand-written guides from DOCS/wiki rank above generated entity pages.
    ...(p.sourceFile ? { guide: true } : {}),
  }));
  const strings = JSON.parse(
    fs.readFileSync(path.join(root, "web/lang/en.json"), "utf8"),
  );
  const t = (key) => {
    if (!strings["wiki." + key]) throw Error(`Missing wiki label: ${key}`);
    return escape(strings["wiki." + key]);
  };
  const nav = sections
    .map(([s]) => `<a href="/wiki/en/${s}/">${t("section." + s)}</a>`)
    .join("");
  function layout(p, preview) {
    const group = sections.find(([s]) => p.path.startsWith(`/wiki/en/${s}/`));
    const sourceLinks = p.sources
      .split(/,\s*/)
      .map(
        (s) =>
          `<a href="https://github.com/binary-knight/usurper-reborn/blob/${encodeURIComponent(sourceRef)}/${s}">${escape(s)}</a>`,
      )
      .join(", ");
    const entries = shownHistory(p.history);
    const history = entries.length
      ? `<section class="history" aria-labelledby="changes-by-version"><h2 id="changes-by-version">${t("history")}</h2><dl>${entries.map((h) => `<dt>${escape(h.version)}</dt><dd>${escape(h.sentence)}</dd>`).join("")}</dl></section>`
      : "";
    return `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>${escape(p.title)} | Usurper Reborn Wiki</title><meta name="description" content="${escape(preview.slice(0, 180))}"><link rel="stylesheet" href="/wiki/wiki.css"><script defer src="/wiki/wiki-labels.js"></script><script defer src="/wiki/wiki-search.js"></script><script defer src="/wiki/wiki.js"></script></head><body><a class="skip" href="#main">${t("skip")}</a><header><a class="logo" href="/">USURPER REBORN</a><nav aria-label="Site"><a href="/wiki/en/">${t("home")}</a><a href="/#connect">${t("play")}</a><button id="theme" type="button" aria-pressed="false">${t("theme")}</button></nav></header><div class="layout"><aside><form role="search" id="search-form"><label for="search">${t("search")}</label><input type="search" id="search" placeholder="${t("searchPlaceholder")}" maxlength="200" autocomplete="off"><button type="submit">${t("find")}</button></form><p id="search-status" role="status"></p><div id="search-results"></div><nav aria-label="Wiki sections" class="sections">${nav}</nav></aside><main id="main" tabindex="-1"><nav class="breadcrumbs" aria-label="Breadcrumb"><a href="/wiki/en/">${t("home")}</a>${group ? ` / <a href="/wiki/en/${group[0]}/">${escape(group[1])}</a>` : ""}</nav><h1>${escape(p.title)}</h1><p class="version">${t("version")} ${escape(meta.gameVersion)}. ${t("checked")} ${escape(p.checked)}.</p>${p.body}${history}<footer><p>${t("fixedRules")}</p><p>${t("sources")}: ${sourceLinks}</p><p>${t("languageNote")}</p></footer></main></div></body></html>`;
  }
  // Remove only obsolete generated page files listed by our previous manifest.
  // No recursive output cleanup and no removal of hand-written assets.
  fs.mkdirSync(outDir, { recursive: true });
  const oldManifest = path.join(outDir, "manifest.json");
  if (fs.existsSync(oldManifest)) {
    const previous = JSON.parse(fs.readFileSync(oldManifest, "utf8"));
    for (const oldPath of previous.paths || []) {
      if (!/^\/wiki\/en\/(?:[\w%:-]+\/)*$/.test(oldPath))
        throw Error("Invalid prior wiki manifest path");
      if (!urls.has(oldPath)) {
        const obsolete = path.join(
          outDir,
          oldPath.slice("/wiki/".length),
          "index.html",
        );
        if (fs.existsSync(obsolete)) {
          if (
            !fs
              .realpathSync(path.dirname(obsolete))
              .startsWith(fs.realpathSync(outDir) + path.sep)
          )
            throw Error("Output path escaped wiki directory");
          fs.unlinkSync(obsolete);
        }
      }
    }
  }
  for (let i = 0; i < pages.length; i++) {
    const dest = path.join(
      outDir,
      pages[i].path.slice("/wiki/".length),
      "index.html",
    );
    fs.mkdirSync(path.dirname(dest), { recursive: true });
    fs.writeFileSync(dest, layout(pages[i], search[i].text));
  }
  fs.writeFileSync(
    path.join(outDir, "search-index.json"),
    JSON.stringify({
      schemaVersion: 1,
      gameVersion: meta.gameVersion,
      pages: search,
    }),
  );
  const clientLabels = Object.fromEntries(
    ["searching", "results", "noResults", "searchUnavailable"].map((key) => {
      t(key);
      return [key, strings["wiki." + key]];
    }),
  );
  fs.writeFileSync(
    path.join(outDir, "wiki-labels.js"),
    "globalThis.WikiLabels = " +
      JSON.stringify(clientLabels).replace(/</g, "\\u003c") +
      ";",
  );
  fs.writeFileSync(
    path.join(outDir, "index.html"),
    '<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Usurper Reborn Wiki</title><meta http-equiv="refresh" content="0;url=/wiki/en/"></head><body><a href="/wiki/en/">Open the English player wiki</a></body></html>',
  );
  for (const asset of ["wiki.css", "wiki.js"])
    fs.copyFileSync(path.join(__dirname, asset), path.join(outDir, asset));
  fs.copyFileSync(
    path.join(root, "web/wiki-search.js"),
    path.join(outDir, "wiki-search.js"),
  );
  fs.writeFileSync(
    path.join(outDir, "manifest.json"),
    JSON.stringify(
      { gameVersion: meta.gameVersion, paths: pages.map((p) => p.path) },
      null,
      2,
    ),
  );
  return { pages, search, meta };
}
if (require.main === module) {
  try {
    const args = process.argv.slice(2);
    const result = build({
      dataDir: args[0] && path.resolve(args[0]),
      outDir: args[1] && path.resolve(args[1]),
    });
    console.log(
      `Built ${result.pages.length} wiki pages for ${result.meta.gameVersion}`,
    );
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
module.exports = {
  build,
  loadData,
  readPages,
  renderers,
  textOf,
  compareVersions,
  parseHistory,
  shownHistory,
};
