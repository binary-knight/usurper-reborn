// Test harness for the web pages: a small HTML parser, a fake DOM built from the
// page markup, and a loader that runs a page's scripts in a node vm context.
// No dependencies, so `node --test web/test/` runs on a clean checkout.
'use strict';

const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const WEB = path.join(__dirname, '..');

// Values that try every context: element text, quoted attributes (both quote
// styles), a JS string inside an inline handler, and a script/img breakout.
const HOSTILE = `Bob'"\\');alert(1)//</span></td><img src=x onerror=alert(2)><script>alert(3)</script>&amp;`;
const HOSTILE_URLS = [
  'javascript:alert(1)',
  'JaVaScRiPt:alert(1)',
  ' javascript:alert(1)',
  'data:text/html,<script>alert(1)</script>',
  'data:image/svg+xml,<svg onload=alert(1)>',
  'http://example.com/plain-http',
  '//evil.example/x',
  'vbscript:msgbox(1)',
];

// ---------------------------------------------------------------- parsing

const VOID = new Set(['area', 'base', 'br', 'col', 'embed', 'hr', 'img', 'input', 'link', 'meta', 'source', 'track', 'wbr']);
const RAW_TEXT = new Set(['script', 'style']);
const RCDATA = new Set(['textarea', 'title']);
const NAMED = { amp: '&', lt: '<', gt: '>', quot: '"', apos: "'", nbsp: '\u00a0', middot: '\u00b7', times: '\u00d7', hellip: '\u2026', mdash: '\u2014', ndash: '\u2013', copy: '\u00a9' };

function decodeEntities(s) {
  return s.replace(/&(#x[0-9a-f]+|#[0-9]+|[a-z]+);/gi, (m, body) => {
    if (body[0] === '#') {
      const code = body[1] === 'x' || body[1] === 'X' ? parseInt(body.slice(2), 16) : parseInt(body.slice(1), 10);
      return String.fromCodePoint(code);
    }
    const v = NAMED[body.toLowerCase()];
    return v === undefined ? m : v;
  });
}

// Parses an HTML string into a tree of { type, tag, attrs, attrList, children, parent }.
// attrList keeps every attribute in source order (duplicates included), decoded.
function parseHtml(html) {
  const root = { type: 'root', tag: '#root', attrs: {}, attrList: [], children: [], parent: null };
  let cur = root;
  let i = 0;
  const n = html.length;
  const pushText = (text) => {
    if (text) cur.children.push({ type: 'text', text: decodeEntities(text), parent: cur });
  };
  while (i < n) {
    const lt = html.indexOf('<', i);
    if (lt === -1) { pushText(html.slice(i)); break; }
    pushText(html.slice(i, lt));
    i = lt;
    if (html.startsWith('<!--', i)) {
      const end = html.indexOf('-->', i + 4);
      i = end === -1 ? n : end + 3;
      continue;
    }
    if (html[i + 1] === '!' || html[i + 1] === '?') {
      const end = html.indexOf('>', i);
      i = end === -1 ? n : end + 1;
      continue;
    }
    if (html[i + 1] === '/') {
      const end = html.indexOf('>', i);
      const name = html.slice(i + 2, end === -1 ? n : end).trim().toLowerCase();
      i = end === -1 ? n : end + 1;
      let p = cur;
      while (p && p.tag !== name) p = p.parent;
      if (p && p.parent) cur = p.parent;
      continue;
    }
    if (!/[a-zA-Z]/.test(html[i + 1] || '')) { pushText('<'); i += 1; continue; }
    // start tag
    let j = i + 1;
    while (j < n && /[^\s/>]/.test(html[j])) j++;
    const tag = html.slice(i + 1, j).toLowerCase();
    const el = { type: 'element', tag, attrs: {}, attrList: [], children: [], parent: cur };
    let selfClose = false;
    for (;;) {
      while (j < n && /\s/.test(html[j])) j++;
      if (j >= n) break;
      if (html[j] === '>') { j++; break; }
      if (html[j] === '/' && html[j + 1] === '>') { selfClose = true; j += 2; break; }
      if (html[j] === '/') { j++; continue; }
      let k = j;
      while (k < n && /[^\s=/>]/.test(html[k])) k++;
      if (k === j) k++;
      const name = html.slice(j, k).toLowerCase();
      j = k;
      while (j < n && /\s/.test(html[j])) j++;
      let value = '';
      if (html[j] === '=') {
        j++;
        while (j < n && /\s/.test(html[j])) j++;
        const q = html[j];
        if (q === '"' || q === "'") {
          const end = html.indexOf(q, j + 1);
          value = html.slice(j + 1, end === -1 ? n : end);
          j = end === -1 ? n : end + 1;
        } else {
          let e = j;
          while (e < n && /[^\s>]/.test(html[e])) e++;
          value = html.slice(j, e);
          j = e;
        }
      }
      value = decodeEntities(value);
      el.attrList.push([name, value]);
      if (!(name in el.attrs)) el.attrs[name] = value;
    }
    i = j;
    cur.children.push(el);
    if (selfClose || VOID.has(tag)) continue;
    // <title> inside SVG is an ordinary element whose content is parsed as markup.
    const rcdata = RCDATA.has(tag) && !(tag === 'title' && insideSvg(el));
    if (RAW_TEXT.has(tag) || rcdata) {
      const close = html.toLowerCase().indexOf('</' + tag, i);
      const body = html.slice(i, close === -1 ? n : close);
      el.children.push({ type: 'text', text: rcdata ? decodeEntities(body) : body, parent: el });
      const gt = close === -1 ? n : html.indexOf('>', close);
      i = gt === -1 ? n : gt + 1;
      continue;
    }
    cur = el;
  }
  return root;
}

function insideSvg(node) {
  for (let p = node.parent; p; p = p.parent) if (p.tag === 'svg') return true;
  return false;
}

function walk(node, fn) {
  for (const c of node.children || []) {
    if (c.type === 'element') { fn(c); walk(c, fn); }
  }
}

function elements(htmlOrTree) {
  const tree = typeof htmlOrTree === 'string' ? parseHtml(htmlOrTree) : htmlOrTree;
  const out = [];
  walk(tree, (e) => out.push(e));
  return out;
}

function textOf(node) {
  if (node.type === 'text') return node.text;
  return (node.children || []).map(textOf).join('');
}

// The shape of a rendering: every element with its attribute names. Hostile input
// must give the same shape as benign input, so no tag or attribute is injected.
function shape(html) {
  return elements(html).map((e) => e.tag + '[' + e.attrList.map((a) => a[0]).join(',') + ']');
}

// The checks every sink test makes on a rendering.
function assertSafe(assert, html, benignHtml, label) {
  const els = elements(html);
  const handlers = [];
  for (const e of els) for (const [name] of e.attrList) if (/^on/.test(name)) handlers.push(e.tag + ' ' + name);
  assert.deepStrictEqual(handlers, [], `${label}: inline handler attributes in rendered HTML`);
  const bad = els.filter((e) => e.tag === 'script' || e.tag === 'iframe' || e.tag === 'object');
  assert.strictEqual(bad.length, 0, `${label}: script-like element injected`);
  for (const e of els) {
    for (const [name, value] of e.attrList) {
      if ((name === 'href' || name === 'src' || name === 'action' || name === 'formaction') && /^\s*(javascript|data|vbscript):/i.test(value)) {
        assert.fail(`${label}: ${name}=${value} on ${e.tag}`);
      }
    }
  }
  if (benignHtml !== undefined) {
    assert.deepStrictEqual(shape(html), shape(benignHtml), `${label}: hostile input changed the element structure`);
  }
}

// ---------------------------------------------------------------- fake DOM

function camel(s) { return s.replace(/-([a-z])/g, (m, c) => c.toUpperCase()); }

function parseSelector(sel) {
  return sel.split(',').map((part) => {
    const s = part.trim();
    const parts = [];
    const re = /([a-zA-Z][\w-]*)|\.([\w-]+)|#([\w-]+)|\[([\w-]+)(?:=["']?([^"'\]]*)["']?)?\]/g;
    let m;
    while ((m = re.exec(s))) {
      if (m[1]) parts.push({ tag: m[1].toLowerCase() });
      else if (m[2]) parts.push({ cls: m[2] });
      else if (m[3]) parts.push({ id: m[3] });
      else parts.push({ attr: m[4].toLowerCase(), value: m[5] });
    }
    // only the last compound is matched (descendant selectors keep their tail)
    const tokens = s.split(/\s+/);
    if (tokens.length > 1) return { tail: parseSelector(tokens[tokens.length - 1])[0], ancestor: tokens.slice(0, -1).join(' ') };
    return { parts };
  });
}

function matchesCompound(el, compound) {
  if (compound.tail) {
    if (!matchesCompound(el, compound.tail)) return false;
    const anc = parseSelector(compound.ancestor);
    for (let p = el.parentElement; p; p = p.parentElement) if (anc.some((c) => matchesCompound(p, c))) return true;
    return false;
  }
  return compound.parts.every((p) => {
    if (p.tag) return el.tagName.toLowerCase() === p.tag;
    if (p.cls) return el.classList.contains(p.cls);
    if (p.id) return el.getAttribute('id') === p.id;
    if (p.attr) {
      const v = el.getAttribute(p.attr);
      if (v === null) return false;
      return p.value === undefined ? true : v === p.value;
    }
    return false;
  });
}

class FakeClassList {
  constructor(el) { this.el = el; }
  _list() { return (this.el.getAttribute('class') || '').split(/\s+/).filter(Boolean); }
  _set(list) { this.el.setAttribute('class', list.join(' ')); }
  contains(c) { return this._list().includes(c); }
  add(...cs) { const l = this._list(); for (const c of cs) if (!l.includes(c)) l.push(c); this._set(l); }
  remove(...cs) { this._set(this._list().filter((c) => !cs.includes(c))); }
  toggle(c, force) {
    const has = this.contains(c);
    const want = force === undefined ? !has : !!force;
    if (want && !has) this.add(c);
    if (!want && has) this.remove(c);
    return want;
  }
}

class FakeElement {
  constructor(doc, tag, attrList) {
    this.ownerDocument = doc;
    this.tagName = String(tag).toUpperCase();
    this._attrs = {};
    for (const [k, v] of attrList || []) if (!(k in this._attrs)) this._attrs[k] = v;
    this.childNodes = [];
    this.parentElement = null;
    this.style = {};
    this.listeners = {};
    this.classList = new FakeClassList(this);
    this._html = '';
    this._text = '';
    this.value = this._attrs.value !== undefined ? this._attrs.value : '';
    this.disabled = 'disabled' in this._attrs;
    this.checked = 'checked' in this._attrs;
    this.hidden = 'hidden' in this._attrs;
    this.scrollTop = 0;
    this.scrollHeight = 0;
    const self = this;
    this.dataset = new Proxy({}, {
      get(t, k) { if (typeof k !== 'string') return undefined; const name = 'data-' + k.replace(/[A-Z]/g, (c) => '-' + c.toLowerCase()); const v = self.getAttribute(name); return v === null ? undefined : v; },
      set(t, k, v) { self.setAttribute('data-' + String(k).replace(/[A-Z]/g, (c) => '-' + c.toLowerCase()), v); return true; },
    });
  }
  get id() { return this._attrs.id || ''; }
  set id(v) { this._attrs.id = String(v); }
  get className() { return this._attrs.class || ''; }
  set className(v) { this._attrs.class = String(v); }
  get children() { return this.childNodes.filter((c) => c instanceof FakeElement); }
  get firstChild() { return this.childNodes[0] || null; }
  get lastChild() { return this.childNodes[this.childNodes.length - 1] || null; }
  getAttribute(k) { k = k.toLowerCase(); return k in this._attrs ? this._attrs[k] : null; }
  setAttribute(k, v) { this._attrs[k.toLowerCase()] = String(v); }
  removeAttribute(k) { delete this._attrs[k.toLowerCase()]; }
  hasAttribute(k) { return k.toLowerCase() in this._attrs; }
  get attributeNames() { return Object.keys(this._attrs); }
  get innerHTML() { return this._html; }
  set innerHTML(v) {
    this._html = String(v);
    this._text = '';
    this.childNodes = [];
    const tree = parseHtml(this._html);
    for (const c of tree.children) this._adopt(c);
  }
  _adopt(node) {
    if (node.type === 'text') { this.childNodes.push({ nodeType: 3, textContent: node.text, parentElement: this }); return; }
    const el = this.ownerDocument._fromParsed(node);
    el.parentElement = this;
    this.childNodes.push(el);
  }
  get textContent() {
    if (this._text) return this._text;
    return this.childNodes.map((c) => c.textContent).join('');
  }
  set textContent(v) { this._text = v === null || v === undefined ? '' : String(v); this.childNodes = []; this._html = ''; }
  appendChild(c) { if (c) { c.parentElement = this; this.childNodes.push(c); } return c; }
  insertBefore(c, ref) {
    c.parentElement = this;
    const i = this.childNodes.indexOf(ref);
    if (i === -1) this.childNodes.push(c); else this.childNodes.splice(i, 0, c);
    return c;
  }
  removeChild(c) { const i = this.childNodes.indexOf(c); if (i !== -1) this.childNodes.splice(i, 1); return c; }
  remove() { if (this.parentElement) this.parentElement.removeChild(this); }
  addEventListener(type, fn) { (this.listeners[type] = this.listeners[type] || []).push(fn); }
  removeEventListener(type, fn) { const l = this.listeners[type] || []; const i = l.indexOf(fn); if (i !== -1) l.splice(i, 1); }
  matches(sel) { return parseSelector(sel).some((c) => matchesCompound(this, c)); }
  closest(sel) {
    const compounds = parseSelector(sel);
    for (let e = this; e && e instanceof FakeElement; e = e.parentElement) if (compounds.some((c) => matchesCompound(e, c))) return e;
    return null;
  }
  querySelectorAll(sel) {
    const compounds = parseSelector(sel);
    const out = [];
    const rec = (el) => { for (const c of el.children) { if (compounds.some((cp) => matchesCompound(c, cp))) out.push(c); rec(c); } };
    rec(this);
    return out;
  }
  querySelector(sel) { return this.querySelectorAll(sel)[0] || null; }
  focus() {} blur() {} select() {} click() { this.ownerDocument.dispatch(this, 'click'); }
  scrollIntoView() {}
  getBoundingClientRect() { return { width: 800, height: 600, left: 0, top: 0, right: 800, bottom: 600 }; }
  getContext() { return makeAny(); }
}

class FakeDocument {
  constructor(markup) {
    this.listeners = {};
    this.documentElement = new FakeElement(this, 'html', []);
    this.body = this.documentElement;
    if (markup) {
      const tree = parseHtml(markup);
      for (const c of tree.children) this.documentElement._adopt(c);
    }
  }
  _fromParsed(node) {
    const el = new FakeElement(this, node.tag, node.attrList);
    for (const c of node.children) el._adopt(c);
    return el;
  }
  getElementById(id) {
    const sel = '[id="' + id + '"]';
    const found = this.documentElement.querySelector(sel);
    if (found) return found;
    return null;
  }
  createElement(tag) { return new FakeElement(this, tag, []); }
  querySelectorAll(sel) { return this.documentElement.querySelectorAll(sel); }
  querySelector(sel) { return this.documentElement.querySelector(sel); }
  addEventListener(type, fn) { (this.listeners[type] = this.listeners[type] || []).push(fn); }
  removeEventListener() {}
  // Bubbles an event from target to the document, honouring stopPropagation.
  dispatch(target, type, extra) {
    let stopped = false;
    const ev = Object.assign({ type, target, stopPropagation() { stopped = true; }, preventDefault() {}, clientX: 0, clientY: 0 }, extra || {});
    for (let el = target; el && !stopped; el = el.parentElement) {
      ev.currentTarget = el;
      for (const fn of (el.listeners[type] || []).slice()) fn.call(el, ev);
    }
    if (!stopped) for (const fn of (this.listeners[type] || []).slice()) fn.call(this, ev);
    return ev;
  }
}

// A value that absorbs any use: property reads, calls and `new` return itself.
// When handlers is an array, every .on(name, fn) call is recorded in it (d3 style).
function makeAny(handlers) {
  const fn = function () {};
  const proxy = new Proxy(fn, {
    get(t, k) {
      if (k === 'on' && handlers) return (name, h) => { handlers.push([name, h]); return proxy; };
      if (k === Symbol.toPrimitive) return () => 0;
      if (k === 'then') return undefined;
      if (k === Symbol.iterator) return function* () {};
      if (k === 'length') return 0;
      return proxy;
    },
    apply() { return proxy; },
    construct() { return proxy; },
    set() { return true; },
  });
  return proxy;
}

// ---------------------------------------------------------------- page loader

function readPage(name) { return fs.readFileSync(path.join(WEB, name), 'utf8'); }

// Returns the page's scripts in order: { src } for external, { code } for inline.
function pageScripts(html) {
  const out = [];
  for (const e of elements(html)) {
    if (e.tag !== 'script') continue;
    if (e.attrs.src !== undefined) out.push({ src: e.attrs.src });
    else out.push({ code: textOf(e) });
  }
  return out;
}

// Loads a page into a vm context. options.fetch(url, init) returns a body (object)
// or { status, body }. options.storage seeds localStorage.
async function loadPage(name, options = {}) {
  const html = readPage(name);
  const bodyStart = html.search(/<body[\s>]/i);
  const doc = new FakeDocument(bodyStart === -1 ? html : html.slice(bodyStart));
  const errors = [];
  const storage = Object.assign({}, options.storage || {});
  const calls = { fetch: [], alert: [], confirm: [], open: [], interval: [] };
  const d3Handlers = [];
  const ctx = {
    console: { log() {}, warn() {}, info() {}, error: (...a) => errors.push(a.map(String).join(' ')) },
    document: doc,
    localStorage: {
      getItem: (k) => (k in storage ? storage[k] : null),
      setItem: (k, v) => { storage[k] = String(v); },
      removeItem: (k) => { delete storage[k]; },
    },
    location: { protocol: 'https:', host: 'usurper.test', href: 'https://usurper.test/' + name, pathname: '/' + name },
    navigator: { clipboard: { writeText: async () => {} }, language: 'en' },
    fetch: async (url, init) => {
      calls.fetch.push({ url: String(url), init });
      let r = options.fetch ? await options.fetch(String(url), init) : {};
      if (r && r.__raw) r = r.__raw;
      const status = r && typeof r === 'object' && 'status' in r && 'body' in r ? r.status : 200;
      const body = r && typeof r === 'object' && 'status' in r && 'body' in r ? r.body : r;
      return { status, ok: status >= 200 && status < 300, json: async () => JSON.parse(JSON.stringify(body === undefined ? {} : body)), text: async () => (typeof body === 'string' ? body : JSON.stringify(body)) };
    },
    EventSource: class { constructor(url) { this.url = url; this.listeners = {}; } addEventListener(t, fn) { this.listeners[t] = fn; } close() {} },
    WebSocket: class { constructor() {} send() {} close() {} },
    Chart: makeAny(),
    d3: makeAny(d3Handlers),
    Terminal: makeAny(),
    FitAddon: makeAny(),
    requestAnimationFrame: () => 0,
    setInterval: (fn, ms) => { calls.interval.push({ fn, ms }); return calls.interval.length; },
    clearInterval: () => {},
    setTimeout: () => 0,
    clearTimeout: () => {},
    alert: (m) => calls.alert.push(String(m)),
    confirm: (m) => { calls.confirm.push(String(m)); return options.confirm === undefined ? true : options.confirm; },
    scrollTo: () => {},
    scrollY: 0,
    addEventListener: () => {},
    open: (u) => { calls.open.push(u); return null; },
    URL,
    URLSearchParams,
    TextEncoder,
  };
  ctx.window = ctx;
  ctx.self = ctx;
  vm.createContext(ctx);
  const loaded = [];
  for (const s of pageScripts(html)) {
    if (s.src !== undefined) {
      if (s.src === '/escape.js' || s.src === 'escape.js') {
        vm.runInContext(fs.readFileSync(path.join(WEB, 'escape.js'), 'utf8'), ctx, { filename: 'escape.js' });
        loaded.push('escape.js');
      }
      continue; // CDN scripts are replaced by the stubs above
    }
    vm.runInContext(s.code, ctx, { filename: name });
  }
  for (let k = 0; k < 5; k++) await new Promise((r) => setImmediate(r));
  return {
    ctx,
    doc,
    errors,
    storage,
    calls,
    loaded,
    d3Handlers,
    run: (code) => vm.runInContext(code, ctx),
    el: (id) => doc.getElementById(id),
    settle: async () => { for (let k = 0; k < 5; k++) await new Promise((r) => setImmediate(r)); },
    click: (el) => doc.dispatch(el, 'click'),
  };
}

module.exports = {
  WEB, HOSTILE, HOSTILE_URLS, parseHtml, elements, textOf, shape, assertSafe,
  decodeEntities, readPage, pageScripts, loadPage, makeAny, FakeDocument,
};
