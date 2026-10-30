// Studio deck: switches the landing page between the redesign directions.
// Loaded by one script tag in index.html; remove that tag and this folder at promotion.
// Keys: backtick toggles the panel. URL: ?direction=a|b|c and ?structure=s1|s2|s3 keep the
// choice across reloads. A structure is built on direction B, so picking one switches to B.
// ?copy=c1|c2|c3 swaps the hero copy candidates (subtitle, description, Steam button label).
(function () {
  'use strict';

  var DIRS = [
    { id: 'a', label: 'A', name: 'Phosphor ledger' },
    { id: 'b', label: 'B', name: 'Woodcut and iron' },
    { id: 'c', label: 'C', name: 'Illuminated chronicle' },
    { id: '', label: 'Current', name: 'Live site' }
  ];
  var VALID = { a: 1, b: 1, c: 1 };
  var STRUCTS = [
    { id: 's1', label: 'S1', name: 'Store page' },
    { id: 's2', label: 'S2', name: 'Chronicle' },
    { id: 's3', label: 'S3', name: 'Portrait led' },
    { id: '', label: 'Off', name: 'Page as it is' }
  ];
  var VALID_S = { s1: 1, s2: 1, s3: 1 };
  var COPIES = [
    { id: 'c1', label: 'C1', name: 'Copy: today' },
    { id: 'c2', label: 'C2', name: 'Copy: shorter' },
    { id: 'c3', label: 'C3', name: 'Copy: support' }
  ];
  var VALID_C = { c1: 1, c2: 1, c3: 1 };
  // Hero copy candidates, English only until one is picked (c1 is the page's own text).
  var C2_DESC = 'The town of Dorashire goes on without you: its people marry, raise children, grow old, and remember what you did to them. ' +
    'Death here is permanent, and a hundred floors below the streets, seven broken gods are waiting.';
  var COPY_TEXT = {
    c2: {
      subtitle: 'A living world. A hundred floors down. One question at the bottom.',
      desc: C2_DESC,
      button: 'Get on Steam'
    },
    c3: {
      subtitle: 'The 1993 BBS classic, rebuilt',
      desc: C2_DESC + ' The Steam edition bundles the terminal and fonts, and buying it funds development.',
      button: 'Support it on Steam'
    }
  };
  var COPY_TARGETS = { subtitle: '.hero .hero-subtitle', desc: '.hero .hero-desc', button: '.hero .btn-hero-steam' };
  var root = document.documentElement;
  var base = (document.currentScript && document.currentScript.src) || '/studio/deck.js';
  var here = base.replace(/deck\.js(\?.*)?$/, '');

  function fromUrl(name, valid) {
    var v = (new URLSearchParams(location.search).get(name) || '').toLowerCase();
    return valid[v] ? v : '';
  }

  function setAttr(name, v) {
    if (v) root.setAttribute(name, v);
    else root.removeAttribute(name);
  }
  function apply(dir) { setAttr('data-direction', dir); }
  function applyStruct(st) { setAttr('data-structure', st); }

  // Swaps the hero text. The element's data-i18n key is parked so a language load does not
  // overwrite a candidate; going back to c1 restores the key and the translated text.
  function applyCopy(c) {
    setAttr('data-copy', c);
    if (!document.body) return;
    var set = COPY_TEXT[c];
    Object.keys(COPY_TARGETS).forEach(function (part) {
      var el = document.querySelector(COPY_TARGETS[part]);
      if (!el) return;
      if (!el.hasAttribute('data-copy-key')) {
        el.setAttribute('data-copy-key', el.getAttribute('data-i18n') || '');
        el.setAttribute('data-copy-text', el.textContent);
      }
      var key = el.getAttribute('data-copy-key');
      if (set) {
        el.setAttribute('data-i18n', 'studio.copy');
        el.textContent = set[part];
      } else {
        el.setAttribute('data-i18n', key);
        var tr = typeof window.t === 'function' ? window.t(key) : key;
        el.textContent = tr && tr !== key ? tr : el.getAttribute('data-copy-text');
      }
    });
  }

  // Stylesheets go in at once so a switch never waits on the network.
  function addSheet(file) {
    var link = document.createElement('link');
    link.rel = 'stylesheet';
    link.href = here + file;
    document.head.appendChild(link);
  }
  ['dir-a.css', 'dir-b.css', 'dir-c.css', 'struct-common.css', 'struct-s1.css', 'struct-s2.css', 'struct-s3.css'].forEach(addSheet);
  apply(fromUrl('direction', VALID));
  if (root.getAttribute('data-direction') === 'b') applyStruct(fromUrl('structure', VALID_S));
  var startCopy = fromUrl('copy', VALID_C);
  if (startCopy) {
    setAttr('data-copy', startCopy);
    document.addEventListener('DOMContentLoaded', function () { applyCopy(startCopy); });
  }

  // Frames in the bench and the triptych show the page only, without the panel.
  if (window.self !== window.top) return;
  addSheet('deck.css');

  var STORE = 'usurper-studio-deck';
  function load() {
    try { return JSON.parse(localStorage.getItem(STORE) || '{}') || {}; } catch (e) { return {}; }
  }
  function save(state) {
    try { localStorage.setItem(STORE, JSON.stringify(state)); } catch (e) { /* storage blocked: position resets */ }
  }

  function setUrl() {
    var u = new URL(location.href);
    var dir = root.getAttribute('data-direction') || '';
    var st = root.getAttribute('data-structure') || '';
    if (dir) u.searchParams.set('direction', dir);
    else u.searchParams.delete('direction');
    if (st) u.searchParams.set('structure', st);
    else u.searchParams.delete('structure');
    var cp = root.getAttribute('data-copy') || '';
    if (cp && cp !== 'c1') u.searchParams.set('copy', cp);
    else u.searchParams.delete('copy');
    history.replaceState(history.state, '', u.pathname + u.search + u.hash);
  }

  function pop() {
    if (!document.body || !document.body.animate) return;
    if (window.matchMedia && matchMedia('(prefers-reduced-motion: reduce)').matches) return;
    document.body.animate(
      [{ transform: 'scale(0.992)' }, { transform: 'scale(1.006)' }, { transform: 'scale(1)' }],
      { duration: 350, easing: 'ease-out' }
    );
  }

  function build() {
    var state = load();
    var panel = document.createElement('div');
    panel.className = 'studio-deck';
    panel.setAttribute('role', 'region');
    panel.setAttribute('aria-label', 'Studio deck');

    var head = document.createElement('div');
    head.className = 'studio-deck-head';
    head.textContent = 'Studio';
    var hint = document.createElement('span');
    hint.className = 'studio-deck-hint';
    hint.textContent = 'drag; ` hides';
    head.appendChild(hint);
    panel.appendChild(head);

    var row = document.createElement('div');
    row.className = 'studio-deck-row';
    var name = document.createElement('div');
    name.className = 'studio-deck-name';
    var buttons = DIRS.map(function (d) {
      var b = document.createElement('button');
      b.type = 'button';
      b.textContent = d.label;
      b.title = d.name;
      b.addEventListener('click', function () {
        if ((root.getAttribute('data-direction') || '') === d.id) return;
        apply(d.id);
        if (d.id !== 'b') applyStruct('');
        setUrl();
        mark();
        pop();
      });
      row.appendChild(b);
      return b;
    });
    panel.appendChild(row);

    var srow = document.createElement('div');
    srow.className = 'studio-deck-row';
    var sbuttons = STRUCTS.map(function (d) {
      var b = document.createElement('button');
      b.type = 'button';
      b.textContent = d.label;
      b.title = d.name;
      b.addEventListener('click', function () {
        if ((root.getAttribute('data-structure') || '') === d.id) return;
        if (d.id) apply('b');
        applyStruct(d.id);
        setUrl();
        mark();
        pop();
      });
      srow.appendChild(b);
      return b;
    });
    panel.appendChild(srow);

    var crow = document.createElement('div');
    crow.className = 'studio-deck-row';
    var cbuttons = COPIES.map(function (d) {
      var b = document.createElement('button');
      b.type = 'button';
      b.textContent = d.label;
      b.title = d.name;
      b.addEventListener('click', function () {
        if ((root.getAttribute('data-copy') || 'c1') === d.id) return;
        applyCopy(d.id);
        setUrl();
        mark();
        pop();
      });
      crow.appendChild(b);
      return b;
    });
    panel.appendChild(crow);
    panel.appendChild(name);

    function mark() {
      var cur = root.getAttribute('data-direction') || '';
      var cs = root.getAttribute('data-structure') || '';
      var label = '';
      DIRS.forEach(function (d, i) {
        var on = d.id === cur;
        buttons[i].setAttribute('aria-pressed', on ? 'true' : 'false');
        if (on) label = d.name;
      });
      STRUCTS.forEach(function (d, i) {
        var on = d.id === cs;
        sbuttons[i].setAttribute('aria-pressed', on ? 'true' : 'false');
        if (on && d.id) label += ', ' + d.name;
      });
      var cc = root.getAttribute('data-copy') || 'c1';
      COPIES.forEach(function (d, i) {
        var on = d.id === cc;
        cbuttons[i].setAttribute('aria-pressed', on ? 'true' : 'false');
        if (on && d.id !== 'c1') label += ', ' + d.name.toLowerCase();
      });
      name.textContent = label;
    }
    mark();

    // Appended to <html>, outside <body>, so the switch pop never moves the panel.
    root.appendChild(panel);

    function clamp(x, y) {
      var w = panel.offsetWidth, h = panel.offsetHeight;
      var maxX = Math.max(0, document.documentElement.clientWidth - w);
      var maxY = Math.max(0, window.innerHeight - h);
      return [Math.min(Math.max(0, x), maxX), Math.min(Math.max(0, y), maxY)];
    }
    function place(x, y) {
      var p = clamp(x, y);
      panel.style.left = p[0] + 'px';
      panel.style.top = p[1] + 'px';
      panel.style.right = 'auto';
      panel.style.bottom = 'auto';
      return p;
    }
    if (typeof state.x === 'number' && typeof state.y === 'number') place(state.x, state.y);

    var drag = null;
    head.addEventListener('pointerdown', function (e) {
      if (e.button !== 0) return;
      var r = panel.getBoundingClientRect();
      drag = { dx: e.clientX - r.left, dy: e.clientY - r.top };
      head.setPointerCapture(e.pointerId);
      e.preventDefault();
    });
    head.addEventListener('pointermove', function (e) {
      if (!drag) return;
      place(e.clientX - drag.dx, e.clientY - drag.dy);
    });
    function end(e) {
      if (!drag) return;
      drag = null;
      if (head.hasPointerCapture(e.pointerId)) head.releasePointerCapture(e.pointerId);
      var r = panel.getBoundingClientRect();
      state.x = r.left; state.y = r.top;
      save(state);
    }
    head.addEventListener('pointerup', end);
    head.addEventListener('pointercancel', end);

    window.addEventListener('resize', function () {
      if (panel.hidden || panel.style.left === '') return;
      var r = panel.getBoundingClientRect();
      place(r.left, r.top);
    });

    document.addEventListener('keydown', function (e) {
      if (e.key !== '`' || e.ctrlKey || e.metaKey || e.altKey) return;
      var t = e.target;
      // Never steal a backtick typed into a field or into the game terminal.
      if (t && t.closest && t.closest('input, textarea, select, [contenteditable], #terminal-section, .xterm')) return;
      panel.hidden = !panel.hidden;
      if (!panel.hidden && panel.style.left !== '') {
        var r = panel.getBoundingClientRect();
        place(r.left, r.top);
      }
    });
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', build);
  else build();
})();
