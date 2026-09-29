"use strict";
const theme = document.getElementById("theme");
function setTheme(light) {
  document.documentElement.dataset.theme = light ? "light" : "dark";
  theme.setAttribute("aria-pressed", String(light));
}
try {
  setTheme(localStorage.getItem("wiki-theme") === "light");
} catch (_) {
  setTheme(false);
}
theme.addEventListener("click", () => {
  const light = document.documentElement.dataset.theme !== "light";
  setTheme(light);
  try {
    localStorage.setItem("wiki-theme", light ? "light" : "dark");
  } catch (_) {}
});
const input = document.getElementById("search");
const status = document.getElementById("search-status");
const results = document.getElementById("search-results");
let indexPromise;
let revision = 0;
async function searchWiki() {
  const current = ++revision;
  const query = input.value.trim();
  results.replaceChildren();
  if (!query) {
    status.textContent = "";
    return;
  }
  status.textContent = WikiLabels.searching;
  try {
    indexPromise ||= fetch("/wiki/search-index.json")
      .then((r) => {
        if (!r.ok) throw Error("Unavailable");
        return r.json();
      })
      .catch((e) => {
        indexPromise = undefined;
        throw e;
      });
    const index = await indexPromise;
    if (current !== revision) return;
    const matches = WikiSearch.search(index.pages, query);
    status.textContent = matches.length
      ? WikiLabels.results.replace("{count}", matches.length)
      : WikiLabels.noResults;
    for (const match of matches) {
      const item = document.createElement("div");
      item.className = "search-result";
      const a = document.createElement("a");
      a.href = match.path;
      a.textContent = match.title;
      const p = document.createElement("p");
      p.textContent = WikiSearch.excerpt(match, query, 180);
      item.append(a, p);
      results.append(item);
    }
  } catch (_) {
    if (current === revision) status.textContent = WikiLabels.searchUnavailable;
  }
}
let timer;
input.addEventListener("input", () => {
  clearTimeout(timer);
  timer = setTimeout(searchWiki, 180);
});
document.getElementById("search-form").addEventListener("submit", (e) => {
  e.preventDefault();
  clearTimeout(timer);
  searchWiki();
});
