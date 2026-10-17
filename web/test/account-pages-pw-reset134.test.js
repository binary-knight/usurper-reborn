'use strict';
// 1.3.4: the account pages web/account/forgot, reset and verify (each index.html with its own script).
// Each page runs in a vm context on the harness's fake DOM with a fake location, history and fetch:
//  - no inline script, no handler attribute, nothing from another site, <meta name="referrer" content="no-referrer">;
//  - the token is read from the fragment and cleared from the address bar before anything is sent, and
//    it is posted in the body only, with no cookie and no Referer;
//  - the reset page checks the game's rule (8 characters, no ':') before it posts;
//  - the pages' text is in the five web/lang files;
//  - the deploy archive carries the pages and nginx serves a folder's index.html.
// Nothing touches the network.
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const vm = require('node:vm');
const { execFileSync } = require('node:child_process');
const { WEB, FakeDocument, elements, pageScripts, textOf, parseHtml } = require('./harness');
const { slice, proxySource } = require('./balance-api-harness');

const ROOT = path.join(WEB, '..');
const PAGES = ['account/forgot/index.html', 'account/reset/index.html', 'account/verify/index.html'];
const TOKEN = 'AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdE';
const LANGS = ['en', 'es', 'fr', 'hu', 'it'];
const LANG = {};
for (const l of LANGS) LANG[l] = JSON.parse(fs.readFileSync(path.join(WEB, 'lang', l + '.json'), 'utf8'));

// Loads a page: hash is the URL fragment, reply(url, init) the fetch answer ({ status, body }).
async function loadAccountPage(rel, options = {}) {
  const html = fs.readFileSync(path.join(WEB, rel), 'utf8');
  const doc = new FakeDocument(html.slice(html.search(/<body[\s>]/i)));
  const calls = { fetch: [], replace: [], order: [] };
  const route = '/' + path.dirname(rel);
  const location = { hash: options.hash === undefined ? '' : options.hash, pathname: route, search: '' };
  const storage = Object.assign({}, options.storage || {});
  const ctx = {
    document: doc,
    location,
    history: { replaceState: (s, t, url) => { calls.replace.push(url); calls.order.push('replace'); location.hash = ''; } },
    localStorage: { getItem: (k) => (k in storage ? storage[k] : null), setItem: (k, v) => { storage[k] = String(v); } },
    navigator: { language: options.navLang || 'en-US' },
    fetch: async (url, init) => {
      calls.fetch.push({ url: String(url), init });
      calls.order.push('fetch');
      const r = options.reply ? options.reply(String(url), init) : { status: 200, body: { ok: true } };
      return { status: r.status, ok: r.status >= 200 && r.status < 300, json: async () => JSON.parse(JSON.stringify(r.body)) };
    },
    console: { log() {}, warn() {}, info() {}, error() {} },
    Promise, JSON, String,
  };
  ctx.window = ctx;
  vm.createContext(ctx);
  for (const s of pageScripts(html)) {
    vm.runInContext(fs.readFileSync(path.join(WEB, s.src.replace(/^\//, '')), 'utf8'), ctx, { filename: s.src });
  }
  const settle = async () => { for (let k = 0; k < 6; k++) await new Promise((r) => setImmediate(r)); };
  await settle();
  const el = (id) => doc.getElementById(id);
  const shown = () => doc.querySelectorAll('.msg').filter((m) => !m.hidden).map((m) => m.getAttribute('id'));
  const submit = async (formId) => { doc.dispatch(el(formId), 'submit'); await settle(); };
  const click = async (id) => { doc.dispatch(el(id), 'click'); await settle(); };
  return { doc, ctx, calls, el, shown, submit, click, settle, location };
}

// ---------------------------------------------------------------- structure

for (const rel of PAGES) {
  test(`${rel}: no-referrer meta, only local external scripts, no inline script or handler`, () => {
    const html = fs.readFileSync(path.join(WEB, rel), 'utf8');
    const els = elements(html);
    const meta = els.filter((e) => e.tag === 'meta' && e.attrs.name === 'referrer');
    assert.deepStrictEqual(meta.map((e) => e.attrs.content), ['no-referrer']);
    const scripts = pageScripts(html);
    assert.ok(scripts.length >= 2);
    for (const s of scripts) {
      assert.ok(s.src !== undefined, 'no inline script');
      assert.match(s.src, /^\/account\/[a-z/]+\.js$/);
      assert.ok(fs.existsSync(path.join(WEB, s.src)), s.src);
    }
    assert.strictEqual(scripts[0].src, '/account/account.js');
    assert.strictEqual(scripts[1].src, '/' + path.dirname(rel) + '/' + path.basename(path.dirname(rel)) + '.js', 'its own script');
    for (const e of els) for (const [name] of e.attrList) assert.ok(!/^on/.test(name), `${e.tag} ${name}`);
    assert.doesNotMatch(html, /https?:\/\/|\/\/[a-z]/i, 'nothing from another site');
    assert.doesNotMatch(html, /http-equiv/i);
    const css = els.filter((e) => e.tag === 'link' && e.attrs.rel === 'stylesheet').map((e) => e.attrs.href);
    assert.deepStrictEqual(css, ['/account/account.css']);
    assert.ok(fs.existsSync(path.join(WEB, 'account', 'account.css')));
  });

  test(`${rel}: every text has a web/lang key in five languages, the markup equal to English`, () => {
    const tree = parseHtml(fs.readFileSync(path.join(WEB, rel), 'utf8'));
    const keyed = elements(tree).filter((e) => e.attrs['data-i18n']);
    assert.ok(keyed.length >= 5);
    for (const e of keyed) {
      const key = e.attrs['data-i18n'];
      assert.strictEqual(textOf(e), LANG.en[key], key);
      for (const l of LANGS) assert.ok(typeof LANG[l][key] === 'string' && LANG[l][key].length > 0, `${l} ${key}`);
    }
  });
}

test('the account scripts build no markup, use no eval, and name no other host', () => {
  for (const f of ['account/account.js', 'account/forgot/forgot.js', 'account/reset/reset.js', 'account/verify/verify.js']) {
    const code = fs.readFileSync(path.join(WEB, f), 'utf8');
    assert.doesNotMatch(code, /innerHTML|outerHTML|insertAdjacentHTML|document\.write|eval\(|new Function/, f);
    assert.doesNotMatch(code, /https?:\/\//, f);
    assert.doesNotMatch(code, /localStorage\.setItem|sessionStorage|document\.cookie/, f);
  }
});

test('account.js checks the same minimum as the server', () => {
  const server = Number(proxySource().match(/\nconst ACCOUNT_MIN_PASSWORD_LENGTH = (\d+);/)[1]);
  const page = Number(fs.readFileSync(path.join(WEB, 'account', 'account.js'), 'utf8').match(/MIN_PASSWORD: (\d+)/)[1]);
  assert.strictEqual(page, server);
  assert.strictEqual(server, 8);
});

test('the page strings are translated in four languages', () => {
  const keys = Object.keys(LANG.en).filter((k) => k.startsWith('account.'));
  assert.ok(keys.length >= 25);
  for (const l of LANGS.slice(1)) {
    const same = keys.filter((k) => LANG[l][k] === LANG.en[k]);
    assert.deepStrictEqual(same, [], `${l}: untranslated`);
  }
});

// ---------------------------------------------------------------- the reset page

test('reset page: the token comes from the fragment, is cleared first, and is posted in the body only', async () => {
  const p = await loadAccountPage('account/reset/index.html', { hash: '#' + TOKEN });
  assert.deepStrictEqual(p.calls.replace, ['/account/reset'], 'cleared from the address bar');
  assert.strictEqual(p.location.hash, '');
  p.el('password').value = 'brand-new-pass';
  p.el('password2').value = 'brand-new-pass';
  await p.submit('reset-form');
  assert.deepStrictEqual(p.calls.order, ['replace', 'fetch']);
  const f = p.calls.fetch[0];
  assert.strictEqual(f.url, '/api/account/reset');
  assert.ok(!f.url.includes(TOKEN));
  assert.strictEqual(f.init.method, 'POST');
  assert.strictEqual(f.init.credentials, 'omit');
  assert.strictEqual(f.init.referrerPolicy, 'no-referrer');
  assert.strictEqual(f.init.cache, 'no-store');
  assert.strictEqual(f.init.headers['Content-Type'], 'application/json');
  assert.deepStrictEqual(JSON.parse(f.init.body), { token: TOKEN, password: 'brand-new-pass', lang: 'en' });
  assert.deepStrictEqual(p.shown(), ['msg-done']);
  assert.strictEqual(p.el('reset-form').hidden, true);
  assert.strictEqual(p.el('msg-bbs').hidden, false, 'the BBS door line is shown');
});

test('reset page: under 8 characters, a colon or a mismatch are refused before anything is sent', async () => {
  const p = await loadAccountPage('account/reset/index.html', { hash: '#' + TOKEN });
  const tryPw = async (a, b) => { p.el('password').value = a; p.el('password2').value = b; await p.submit('reset-form'); return p.shown(); };
  assert.deepStrictEqual(await tryPw('1234567', '1234567'), ['msg-short']);
  assert.deepStrictEqual(await tryPw('colon:inside', 'colon:inside'), ['msg-colon']);
  assert.deepStrictEqual(await tryPw('x'.repeat(129), 'x'.repeat(129)), ['msg-long']);
  assert.deepStrictEqual(await tryPw('12345678', '12345679'), ['msg-mismatch']);
  assert.strictEqual(p.calls.fetch.length, 0, 'nothing sent');
  assert.deepStrictEqual(await tryPw('12345678', '12345678'), ['msg-done']);
  assert.strictEqual(p.calls.fetch.length, 1);
});

test('reset page: no token, a bad token, or the server refusing the link shows the one invalid message', async () => {
  for (const hash of ['', '#', '#short', '#' + TOKEN + 'x', '#<script>']) {
    const p = await loadAccountPage('account/reset/index.html', { hash });
    assert.deepStrictEqual(p.shown(), ['msg-invalid'], hash);
    assert.strictEqual(p.el('reset-form').hidden, true);
    assert.strictEqual(p.el('msg-again').hidden, false);
    assert.strictEqual(p.calls.fetch.length, 0);
  }
  const p = await loadAccountPage('account/reset/index.html', { hash: '#' + TOKEN, reply: () => ({ status: 400, body: { error: 'invalid_link' } }) });
  p.el('password').value = '12345678'; p.el('password2').value = '12345678';
  await p.submit('reset-form');
  assert.deepStrictEqual(p.shown(), ['msg-invalid']);
  const q = await loadAccountPage('account/reset/index.html', { hash: '#' + TOKEN, reply: () => ({ status: 400, body: { error: 'password_colon' } }) });
  q.el('password').value = '12345678'; q.el('password2').value = '12345678';
  await q.submit('reset-form');
  assert.deepStrictEqual(q.shown(), ['msg-colon']);
  const e = await loadAccountPage('account/reset/index.html', { hash: '#' + TOKEN, reply: () => ({ status: 500, body: { error: '<img src=x onerror=alert(1)>' } }) });
  e.el('password').value = '12345678'; e.el('password2').value = '12345678';
  await e.submit('reset-form');
  assert.deepStrictEqual(e.shown(), ['msg-error']);
  assert.strictEqual(e.el('msg-error').textContent, LANG.en['account.error'], 'a server text is never shown');
});

// ---------------------------------------------------------------- the verify page

test('verify page: nothing is sent on load; the button posts the token from the fragment', async () => {
  const p = await loadAccountPage('account/verify/index.html', { hash: '#' + TOKEN });
  assert.deepStrictEqual(p.calls.replace, ['/account/verify']);
  assert.strictEqual(p.calls.fetch.length, 0, 'a mail scanner that opens the link changes nothing');
  await p.click('confirm');
  const f = p.calls.fetch[0];
  assert.strictEqual(f.url, '/api/account/verify');
  assert.strictEqual(f.init.credentials, 'omit');
  assert.strictEqual(f.init.referrerPolicy, 'no-referrer');
  assert.deepStrictEqual(JSON.parse(f.init.body), { token: TOKEN, lang: 'en' });
  assert.deepStrictEqual(p.shown(), ['msg-done']);
  const bad = await loadAccountPage('account/verify/index.html', { hash: '' });
  assert.deepStrictEqual(bad.shown(), ['msg-invalid']);
  assert.strictEqual(bad.el('confirm').hidden, true);
});

// ---------------------------------------------------------------- the forgot page

test('forgot page: posts the name and language and shows the one reply', async () => {
  const p = await loadAccountPage('account/forgot/index.html', { storage: { lang: 'it' } });
  assert.strictEqual(p.calls.fetch[0].url, '/lang/it.json', 'the visitor language');
  p.el('name').value = '  Someone@Example.com ';
  await p.submit('forgot-form');
  const f = p.calls.fetch[1];
  assert.strictEqual(f.url, '/api/account/forgot');
  assert.strictEqual(f.init.credentials, 'omit');
  assert.deepStrictEqual(JSON.parse(f.init.body), { name: 'Someone@Example.com', lang: 'it' });
  assert.deepStrictEqual(p.shown(), ['msg-sent']);
  assert.strictEqual(p.el('name').value, '');
  p.el('name').value = '   ';
  await p.submit('forgot-form');
  assert.deepStrictEqual(p.shown(), ['msg-empty']);
  assert.strictEqual(p.calls.fetch.length, 2, 'an empty name is not sent');
});

test('forgot page: the language file replaces the English text', async () => {
  const p = await loadAccountPage('account/forgot/index.html', {
    storage: { lang: 'hu' },
    reply: (url) => (url === '/lang/hu.json' ? { status: 200, body: LANG.hu } : { status: 200, body: { ok: true } }),
  });
  assert.strictEqual(p.doc.querySelector('h1').textContent, LANG.hu['account.forgot.title']);
  assert.strictEqual(p.doc.documentElement.getAttribute('lang'), 'hu');
  const n = await loadAccountPage('account/forgot/index.html', { navLang: 'fr-FR', reply: (url) => ({ status: 200, body: url === '/lang/fr.json' ? LANG.fr : {} }) });
  assert.strictEqual(n.doc.querySelector('h1').textContent, LANG.fr['account.forgot.title']);
});

test('without script, the forms post to the API (nothing typed goes into a URL)', () => {
  for (const [rel, id, action] of [['account/forgot/index.html', 'forgot-form', '/api/account/forgot'], ['account/reset/index.html', 'reset-form', '/api/account/reset']]) {
    const form = elements(fs.readFileSync(path.join(WEB, rel), 'utf8')).find((e) => e.tag === 'form');
    assert.strictEqual(form.attrs.id, id);
    assert.strictEqual(form.attrs.method, 'post');
    assert.strictEqual(form.attrs.action, action);
  }
});

// ---------------------------------------------------------------- deploy and nginx

test('the deploy archive carries the account pages, scripts and stylesheet', () => {
  const yml = fs.readFileSync(path.join(ROOT, '.github', 'workflows', 'ci-cd.yml'), 'utf8');
  const cmd = yml.match(/run: (tar -czf web\.tar\.gz -C web [^\n]+)/)[1];
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'webtar-pw-reset134-'));
  try {
    const files = ['account/account.js', 'account/account.css', ...PAGES, 'account/forgot/forgot.js', 'account/reset/reset.js', 'account/verify/verify.js'];
    for (const f of files) {
      fs.mkdirSync(path.dirname(path.join(root, 'web', f)), { recursive: true });
      fs.copyFileSync(path.join(WEB, f), path.join(root, 'web', f));
    }
    execFileSync('bash', ['-c', cmd], { cwd: root });
    const listed = execFileSync('tar', ['-tzf', path.join(root, 'web.tar.gz')], { encoding: 'utf8' }).split('\n').map((l) => l.replace(/^\.\//, ''));
    for (const f of files) assert.ok(listed.includes(f), f);
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});

test('nginx serves a folder index (location / with $uri/ and index index.html) and proxies /api/', () => {
  const conf = fs.readFileSync(path.join(ROOT, 'scripts-server', 'nginx-usurper.conf'), 'utf8');
  assert.match(conf, /\n    index index\.html;\n/);
  assert.match(conf, /location \/ \{\n\s+try_files \$uri \$uri\/ =404;/);
  assert.match(conf, /location \/api\/ \{[\s\S]*?proxy_pass http:\/\/127\.0\.0\.1:3000;[\s\S]*?proxy_set_header X-Real-IP \$remote_addr;/);
});

test('the server API is under /api/account/ and the pages are the paths the game prints', () => {
  const src = slice(proxySource(), '// --- Account Mail (1.3.4) ---', '// --- End Account Mail ---');
  for (const r of ['/api/account/forgot', '/api/account/reset', '/api/account/verify']) assert.ok(src.includes(`'${r}'`), r);
  assert.ok(src.includes("'/account/reset#'") && src.includes("'/account/verify#'"));
  const en = JSON.parse(fs.readFileSync(path.join(ROOT, 'Localization', 'en.json'), 'utf8'));
  // 1.3.4 self-host: the game fills in the operator's site (world_state mail_site) + '/account/forgot'
  assert.match(en['auth.forgot_pointer'], /\s\{0\}$/);
  assert.ok(fs.readFileSync(path.join(ROOT, 'Scripts', 'Systems', 'AccountMail.cs'), 'utf8').includes('public static string ForgotUrl(string site) => site + "/account/forgot";'));
  assert.ok(fs.existsSync(path.join(WEB, 'account', 'forgot', 'index.html')));
});

test('the privacy text and the accounts guide describe the address and the website reset', () => {
  const privacy = fs.readFileSync(path.join(ROOT, 'PRIVACY.md'), 'utf8');
  const section = slice(privacy, '## Online Account Email (Optional)', '## Third-Party Services');
  assert.match(section, /stored only with that account on the game server/);
  assert.match(section, /used only to send password reset links and account notices/);
  assert.match(section, /remove it at any time with E on the character screen/);
  const guide = fs.readFileSync(path.join(ROOT, 'DOCS', 'wiki', 'getting-started', 'accounts.md'), 'utf8');
  assert.match(guide, /\nchecked: 1\.3\.[34]\n/);
  assert.ok(guide.includes('https://usurper-reborn.net/account/forgot asks for the account name or the email address'));
  assert.ok(guide.includes('A game played as a BBS door runs on that BBS, not on the game server, and is not closed.'));
  assert.ok(guide.includes('at least 8 characters, no colon'));
});

// ---------------------------------------------------------------- 1.3.4 self-host: a server that does not send mail

const MAIL_OFF = () => ({ status: 503, body: { error: 'mail_off' } });

test('mail off: the three pages carry the one line in five languages', () => {
  assert.strictEqual(LANG.en['account.mail_off'], "This server does not send mail. Ask the server's operator to reset your password.");
  for (const l of LANGS.slice(1)) assert.notStrictEqual(LANG[l]['account.mail_off'], LANG.en['account.mail_off'], l);
  for (const rel of PAGES) {
    const p = elements(fs.readFileSync(path.join(WEB, rel), 'utf8')).filter((e) => e.attrs.id === 'msg-mail-off');
    assert.strictEqual(p.length, 1, rel);
    assert.strictEqual(p[0].attrs['data-i18n'], 'account.mail_off', rel);
  }
});

test('mail off: the forgot page shows the line from the API answer, not the sent line', async () => {
  const p = await loadAccountPage('account/forgot/index.html', { reply: MAIL_OFF });
  p.el('name').value = 'Keeper';
  await p.submit('forgot-form');
  assert.strictEqual(p.calls.fetch.filter((f) => f.url === '/api/account/forgot').length, 1, 'decided from the one API answer');
  assert.deepStrictEqual(p.shown(), ['msg-mail-off']);
  const on = await loadAccountPage('account/forgot/index.html');
  on.el('name').value = 'Keeper';
  await on.submit('forgot-form');
  assert.deepStrictEqual(on.shown(), ['msg-sent'], 'with mail on the line is not shown');
});

test('mail off: the reset page shows the line and hides the form', async () => {
  const p = await loadAccountPage('account/reset/index.html', { hash: '#' + TOKEN, reply: MAIL_OFF });
  p.el('password').value = '12345678'; p.el('password2').value = '12345678';
  await p.submit('reset-form');
  assert.deepStrictEqual(p.shown(), ['msg-mail-off']);
  assert.strictEqual(p.el('reset-form').hidden, true);
  assert.strictEqual(p.el('msg-again').hidden, true, 'no new link to ask for');
});

test('mail off: the verify page shows the line and hides the button', async () => {
  const p = await loadAccountPage('account/verify/index.html', { hash: '#' + TOKEN, reply: MAIL_OFF });
  await p.click('confirm');
  assert.deepStrictEqual(p.shown(), ['msg-mail-off']);
  assert.strictEqual(p.el('confirm').hidden, true);
  assert.strictEqual(p.el('intro').hidden, true);
});
