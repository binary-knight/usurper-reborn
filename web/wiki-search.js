// Shared by the static wiki and Discord. No network, model, or private game data.
(function (root) {
  "use strict";
  const stop = new Set(
    "a about all an and any are as at be can do does explain for get got have help how i in is it many me much my of on or s tell the there to what when where which who why will with work works you your".split(
      " ",
    ),
  );
  // Word forms a suffix rule cannot join. Values are the normalized search term.
  const aliases = {
    specialize: "specialization",
    specialise: "specialization",
    specialization: "specialization",
    specialisation: "specialization",
    specializations: "specialization",
    specialisations: "specialization",
    classes: "class",
    races: "race",
    gods: "god",
    boons: "boon",
    wards: "ward",
    abilities: "ability",
    spells: "spell",
    weapons: "weapon",
    potions: "potion",
    stats: "stat",
    favour: "favor",
    miracles: "miracle",
    pray: "pray",
    prays: "pray",
    prayed: "pray",
    praying: "pray",
    prayer: "pray",
    prayers: "pray",
    bought: "buy",
    buying: "buy",
    buys: "buy",
    armour: "armor",
    armours: "armor",
    defense: "defence",
    choosing: "choose",
    chose: "choose",
  };
  const fold = (word) =>
    word.normalize("NFKD").replace(/\p{M}/gu, "").toLowerCase();
  // Light suffix handling: plurals and -ing only, so "Chosen" and "Cleric" stay intact.
  function stem(word) {
    if (aliases[word]) return aliases[word];
    const n = word.length;
    if (n >= 5 && word.endsWith("ies")) return word.slice(0, -3) + "y";
    if (n >= 6 && word.endsWith("ing")) {
      let base = word.slice(0, -3);
      if (/([b-df-hj-np-tv-z])\1$/.test(base) && !/(ll|ss)$/.test(base))
        base = base.slice(0, -1);
      return base.length >= 3 ? base : word;
    }
    if (n >= 5 && /(sses|shes|ches|xes)$/.test(word)) return word.slice(0, -2);
    if (n >= 4 && word.endsWith("s") && !/(ss|us|is)$/.test(word))
      return word.slice(0, -1);
    return word;
  }
  function tokens(text) {
    return (fold(String(text)).match(/[\p{L}\p{N}]+/gu) || []).filter(
      (w) => w !== "s",
    );
  }
  function words(text) {
    return tokens(text).map(stem);
  }
  function queryTerms(query) {
    return [
      ...new Set(
        tokens(query)
          .filter((w) => !stop.has(w))
          .map(stem)
          .filter((w) => !stop.has(w)),
      ),
    ].slice(0, 16);
  }
  // Prepared once per page object and reused across queries.
  const prepared = new WeakMap();
  function prepare(page) {
    let p = prepared.get(page);
    if (p) return p;
    const body = new Map();
    for (const w of words(page.text)) body.set(w, (body.get(w) || 0) + 1);
    const titleWords = words(page.title);
    p = {
      title: new Set(titleWords),
      titleWords,
      headings: new Set(words((page.headings || []).join(" "))),
      body,
    };
    prepared.set(page, p);
    return p;
  }
  const frequencies = new WeakMap();
  function documentFrequency(pages) {
    let df = frequencies.get(pages);
    if (df) return df;
    df = new Map();
    for (const page of pages) {
      const p = prepare(page);
      for (const w of new Set([...p.title, ...p.headings, ...p.body.keys()]))
        df.set(w, (df.get(w) || 0) + 1);
    }
    frequencies.set(pages, df);
    return df;
  }
  // True when every word of the title appears, in order and adjacent, in the query.
  function namesTitle(queryWords, titleWords) {
    if (!titleWords.length || titleWords.length > queryWords.length) return false;
    for (let i = 0; i + titleWords.length <= queryWords.length; i++)
      if (titleWords.every((w, j) => queryWords[i + j] === w)) return true;
    return false;
  }
  // Field weights: title above headings above body. Hand-written guides outrank generated
  // entity pages, whose title only counts fully when the query names the whole title.
  const TITLE_WEIGHT = 6;
  const ENTITY_TITLE_WEIGHT = 3;
  const HEADING_WEIGHT = 3;
  const GUIDE_WEIGHT = 2.5;
  function search(pages, query, limit = 8) {
    const terms = queryTerms(query);
    if (!terms.length) return [];
    const df = documentFrequency(pages);
    const total = pages.length;
    const queryWords = words(query);
    const meaningful = queryWords.filter((w) => !stop.has(w)).length;
    return pages
      .map((page) => {
        const p = prepare(page);
        const named = namesTitle(queryWords, p.titleWords);
        const titleWeight =
          page.guide || named ? TITLE_WEIGHT : ENTITY_TITLE_WEIGHT;
        let score = 0;
        let matched = 0;
        let fieldMatch = named;
        for (const term of terms) {
          const inTitle = p.title.has(term);
          const inHeading = p.headings.has(term);
          const count = p.body.get(term) || 0;
          if (!inTitle && !inHeading && !count) continue;
          matched++;
          if (inTitle || inHeading) fieldMatch = true;
          const idf = Math.log(1 + total / (df.get(term) || 1));
          score +=
            idf *
            ((inTitle ? titleWeight : 0) +
              (inHeading ? HEADING_WEIGHT : 0) +
              (count ? (count >= 3 ? 1.5 : 1) : 0));
        }
        score *= 0.5 + (0.5 * matched) / terms.length;
        if (page.guide) score *= GUIDE_WEIGHT;
        // Naming a whole title ranks that page first; a title named inside a longer
        // question still outranks pages that only share its words.
        if (named)
          score +=
            p.titleWords.length >= meaningful ? 1000 : 25 * p.titleWords.length;
        return { ...page, score, matched, titleMatch: fieldMatch };
      })
      .filter(
        (p) =>
          p.matched &&
          (p.titleMatch || p.matched >= Math.min(2, terms.length)),
      )
      .sort((a, b) => b.score - a.score || a.path.localeCompare(b.path))
      .slice(0, limit);
  }
  // Picks the window of the page text that covers the most distinct query terms.
  function excerpt(page, query, max = 360) {
    const text = String(page.text).replace(/\s+/g, " ").trim();
    const terms = new Set(queryTerms(query));
    const hits = [];
    for (const m of text.matchAll(/[\p{L}\p{N}]+/gu)) {
      const w = stem(fold(m[0]));
      if (terms.has(w)) hits.push({ at: m.index, w });
    }
    let start = 0;
    if (hits.length) {
      const span = Math.max(40, max - 120);
      const counts = new Map();
      let best = { distinct: 0, hits: 0, at: hits[0].at };
      for (let lo = 0, hi = 0; lo < hits.length; lo++) {
        while (hi < hits.length && hits[hi].at - hits[lo].at <= span) {
          counts.set(hits[hi].w, (counts.get(hits[hi].w) || 0) + 1);
          hi++;
        }
        const distinct = counts.size;
        const n = hi - lo;
        if (distinct > best.distinct || (distinct === best.distinct && n > best.hits))
          best = { distinct, hits: n, at: hits[lo].at };
        const c = counts.get(hits[lo].w) - 1;
        if (c) counts.set(hits[lo].w, c);
        else counts.delete(hits[lo].w);
      }
      const sentence = Math.max(
        text.lastIndexOf(". ", best.at),
        text.lastIndexOf("! ", best.at),
        text.lastIndexOf("? ", best.at),
      );
      if (best.at <= 120) start = 0;
      else if (sentence >= 0 && best.at - sentence <= 120) start = sentence + 2;
      else {
        // Start on a word boundary a little before the first hit.
        const space = text.indexOf(" ", best.at - 80);
        start = space >= 0 && space < best.at ? space + 1 : best.at;
      }
    }
    let chunk = text.slice(start, start + max);
    if (start + max < text.length)
      chunk = chunk.slice(0, chunk.lastIndexOf(" ")) + "...";
    return (start ? "..." : "") + chunk;
  }
  const api = { search, excerpt, words, stem };
  if (typeof module !== "undefined" && module.exports) module.exports = api;
  else root.WikiSearch = api;
})(typeof globalThis !== "undefined" ? globalThis : this);
