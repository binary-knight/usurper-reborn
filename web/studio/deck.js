// Studio deck: switches the landing page between the redesign directions.
// Loaded by one script tag in index.html; remove that tag and this folder at promotion.
// Keys: backtick toggles the panel. URL: ?direction=a|b|c keeps the choice across reloads.
(function () {
  'use strict';

  var DIRS = [
    { id: 'a', label: 'A', name: 'Phosphor ledger' },
    { id: 'b', label: 'B', name: 'Woodcut and iron' },
    { id: 'c', label: 'C', name: 'Illuminated chronicle' },
    { id: '', label: 'Current', name: 'Live site' }
  ];
  var VALID = { a: 1, b: 1, c: 1 };
  var root = document.documentElement;
  var base = (document.currentScript && document.currentScript.src) || '/studio/deck.js';
  var here = base.replace(/deck\.js(\?.*)?$/, '');

  function fromUrl() {
    var v = (new URLSearchParams(location.search).get('direction') || '').toLowerCase();
    return VALID[v] ? v : '';
  }

  function apply(dir) {
    if (dir) root.setAttribute('data-direction', dir);
    else root.removeAttribute('data-direction');
  }

  // Stylesheets go in at once so a switch never waits on the network.
  function addSheet(file) {
    var link = document.createElement('link');
    link.rel = 'stylesheet';
    link.href = here + file;
    document.head.appendChild(link);
  }
  ['dir-a.css', 'dir-b.css', 'dir-c.css'].forEach(addSheet);
  apply(fromUrl());

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

  function setUrl(dir) {
    var u = new URL(location.href);
    if (dir) u.searchParams.set('direction', dir);
    else u.searchParams.delete('direction');
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
        setUrl(d.id);
        mark();
        pop();
      });
      row.appendChild(b);
      return b;
    });
    panel.appendChild(row);
    panel.appendChild(name);

    function mark() {
      var cur = root.getAttribute('data-direction') || '';
      DIRS.forEach(function (d, i) {
        var on = d.id === cur;
        buttons[i].setAttribute('aria-pressed', on ? 'true' : 'false');
        if (on) name.textContent = d.name;
      });
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
