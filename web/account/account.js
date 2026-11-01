// 1.3.4: shared by the account pages. The pages carry their English text; this puts the visitor's
// language in place (web/lang), shows one message at a time, takes a link's token out of the address
// bar and posts to the account API without cookies or a Referer.
(function () {
  'use strict';
  var LANGS = ['en', 'es', 'fr', 'hu', 'it'];
  var TOKEN_RE = /^[A-Za-z0-9_-]{43}$/;

  function pickLang() {
    var l = null;
    try { l = window.localStorage.getItem('lang'); } catch (e) { l = null; }
    if (LANGS.indexOf(l) !== -1) return l;
    var nav = String((window.navigator && window.navigator.language) || '').slice(0, 2).toLowerCase();
    return LANGS.indexOf(nav) !== -1 ? nav : 'en';
  }

  var lang = pickLang();

  function apply(strings) {
    var els = document.querySelectorAll('[data-i18n]');
    for (var i = 0; i < els.length; i++) {
      var key = els[i].getAttribute('data-i18n');
      if (strings && typeof strings[key] === 'string') els[i].textContent = strings[key];
    }
    document.documentElement.setAttribute('lang', lang);
  }

  // The token travels in the fragment, which no browser sends to a server or in a Referer; it is
  // read once and removed from the address bar and the history entry before anything else runs.
  function takeToken() {
    var raw = String(window.location.hash || '').replace(/^#/, '');
    if (raw && window.history && typeof window.history.replaceState === 'function') {
      window.history.replaceState(null, '', window.location.pathname + window.location.search);
    }
    return TOKEN_RE.test(raw) ? raw : '';
  }

  // Shows the message with this id and hides the others.
  function show(id) {
    var msgs = document.querySelectorAll('.msg');
    for (var i = 0; i < msgs.length; i++) msgs[i].hidden = msgs[i].getAttribute('id') !== id;
  }

  function post(path, body) {
    return window.fetch(path, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
      credentials: 'omit',
      cache: 'no-store',
      referrerPolicy: 'no-referrer',
    }).then(function (r) {
      return r.json().then(function (d) { return { status: r.status, data: d || {} }; },
        function () { return { status: r.status, data: {} }; });
    });
  }

  var ready = lang === 'en' ? Promise.resolve() : window.fetch('/lang/' + lang + '.json', { credentials: 'omit', cache: 'no-cache' })
    .then(function (r) { return r.json(); })
    .then(apply, function () {});

  window.UsurperAccount = { lang: lang, ready: ready, takeToken: takeToken, show: show, post: post, MIN_PASSWORD: 8, MAX_PASSWORD: 128 };
})();
