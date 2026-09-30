// Shared by the static wiki and Discord. No network, model, or private game data.
(function (root) {
  "use strict";
  const stop = new Set(
    "a about an and are as at be can do does explain for help how i in is it many me much of on or tell the to what when where with work works you your".split(
      " ",
    ),
  );
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
    prayers: "prayer",
    abilities: "ability",
    spells: "spell",
    weapons: "weapon",
    potions: "potion",
    stats: "stat",
    favour: "favor",
    miracles: "miracle",
  };
  function words(text) {
    return (
      String(text)
        .toLowerCase()
        .normalize("NFKD")
        .replace(/\p{M}/gu, "")
        .match(/[\p{L}\p{N}]+/gu) || []
    ).map((word) => aliases[word] || word);
  }
  function search(pages, query, limit = 8) {
    const terms = [...new Set(words(query).filter((w) => !stop.has(w)))].slice(
      0,
      16,
    );
    if (!terms.length) return [];
    return pages
      .map((page) => {
        const title = words(page.title);
        const headings = words((page.headings || []).join(" "));
        const text = words(page.text);
        const score = terms.reduce(
          (sum, term) =>
            sum +
            (title.includes(term) ? 12 : 0) +
            (headings.includes(term) ? 5 : 0) +
            (text.includes(term) ? 1 : 0),
          0,
        );
        const matched = terms.filter(
          (term) =>
            title.includes(term) ||
            headings.includes(term) ||
            text.includes(term),
        ).length;
        const titleMatch = terms.some((term) => title.includes(term));
        return {
          ...page,
          score: score + matched * matched,
          matched,
          titleMatch,
        };
      })
      .filter((p) => p.titleMatch || p.matched >= Math.min(2, terms.length))
      .sort((a, b) => b.score - a.score || a.path.localeCompare(b.path))
      .slice(0, limit);
  }
  function excerpt(page, query, max = 360) {
    const text = String(page.text).replace(/\s+/g, " ").trim();
    const terms = words(query).filter((w) => !stop.has(w));
    const lower = text.toLowerCase();
    const found = terms.map((t) => lower.indexOf(t)).filter((n) => n >= 0);
    let start = found.length ? Math.max(0, Math.min(...found) - 80) : 0;
    if (start) {
      const space = text.indexOf(" ", start);
      start = space >= 0 ? space + 1 : start;
    }
    let chunk = text.slice(start, start + max);
    if (start + max < text.length)
      chunk = chunk.slice(0, chunk.lastIndexOf(" ")) + "...";
    return (start ? "..." : "") + chunk;
  }
  const api = { search, excerpt };
  if (typeof module !== "undefined" && module.exports) module.exports = api;
  else root.WikiSearch = api;
})(typeof globalThis !== "undefined" ? globalThis : this);
