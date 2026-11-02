'use strict';
// 2.0: the web folder holds the web service itself (its .env, .bak copies, package manifests, node_modules and the
// server scripts), and both nginx (nginx-private.conf) and node (staticFilePath in ssh-proxy.js) refuse those paths
// while every page and asset is still served. The tests read files only; nothing touches the network.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { slice, proxySource } = require('./balance-api-harness');

const WEB = path.join(__dirname, '..');
const ROOT = path.join(WEB, '..');
const read = (...p) => fs.readFileSync(path.join(ROOT, ...p), 'utf8');
const TEMPLATE = read('scripts-server', 'nginx-usurper.conf');
const SNIPPET = read('scripts-server', 'nginx-private.conf');
const code = (text) => text.split('\n').filter((l) => !/^\s*#/.test(l) && l.trim() !== '').join('\n');

const proxyCode = slice(proxySource(), '// --- Static path (1.3.4) ---', '// --- End static path ---');
const ctx = { path, String, decodeURIComponent, Set };
vm.createContext(ctx);
const [staticFilePath, PRIVATE_FILES] = vm.runInContext(proxyCode + '\n[staticFilePath, PRIVATE_FILES]', ctx);
const at = (url) => staticFilePath(WEB, url, fs);

// nginx's four locations, as JavaScript tests on a normalised path (the patterns use nothing PCRE and JS read apart)
const RULES = code(SNIPPET).split('\n');
const regexOf = (line) => {
  const m = /^location ~ (\S+) \{ return 404; \}$/.exec(line);
  return m ? m[1] : null;
};
// the same three patterns as literals; a test below checks each against the snippet's text
const DENY = [
  /\/\.(?!well-known\/)/,
  /\.bak$/,
  /^\/(?:package(?:-lock)?\.json|ssh-proxy\.js|gmcp-split\.js|wiki-bot\.js|wiki-search\.js|dash-npcs\.js|dash-monster-names\.js|dash-observatory\.js)$/,
];
const nginxRefuses = (uri) => DENY.some((r) => r.test(uri)) || uri.startsWith('/node_modules/');

// every page and asset in the web folder: everything but node_modules, the tests, dot files and the private names
function walk(dir, out = []) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    if (e.name === 'node_modules' || e.name.startsWith('.')) continue;
    const f = path.join(dir, e.name);
    if (e.isDirectory()) { if (f !== path.join(WEB, 'test')) walk(f, out); } else out.push(f);
  }
  return out;
}
const PUBLIC = walk(WEB).filter((f) => !(path.dirname(f) === WEB && PRIVATE_FILES.has(path.basename(f))) && !f.endsWith('.bak'));

test('the snippet is exactly four 404 rules: dot names but .well-known, .bak, the manifests and server scripts, node_modules', () => {
  assert.deepEqual(RULES, [
    'location ~ /\\.(?!well-known/) { return 404; }',
    'location ~ \\.bak$ { return 404; }',
    'location ~ ^/(?:package(?:-lock)?\\.json|ssh-proxy\\.js|gmcp-split\\.js|wiki-bot\\.js|wiki-search\\.js|dash-npcs\\.js|dash-monster-names\\.js|dash-observatory\\.js)$ { return 404; }',
    'location ^~ /node_modules/ { return 404; }',
  ]);
});

test('the literal patterns the tests use are the snippet\'s own', () => {
  // a literal escapes each slash; nginx writes it bare
  assert.deepEqual(DENY.map((r) => r.source.replace(/\\\//g, '/')), RULES.map(regexOf).filter(Boolean));
});

test('the main server block includes the snippet after the cache snippet and before its first location', () => {
  const lines = TEMPLATE.split('\n');
  const start = lines.findIndex((l, i) => /^server \{/.test(l) && /default_server/.test(lines[i + 1] || ''));
  const end = lines.findIndex((l, i) => i > start && l === '}');
  const inc = lines.findIndex((l) => /^\s*include\s+\/etc\/nginx\/snippets\/usurper-private\.conf\s*;$/.test(l));
  const cache = lines.findIndex((l) => /^\s*include\s+\/etc\/nginx\/snippets\/usurper-cache\.conf\s*;$/.test(l));
  const firstLocation = lines.findIndex((l, i) => i > start && /^\s*location\b/.test(l));
  assert.ok(start >= 0 && inc > start && inc < end, 'in the main server block');
  assert.ok(inc > cache && inc < firstLocation, 'after the cache include, before the first location');
  assert.equal(lines.filter((l) => /usurper-private\.conf\s*;/.test(l) && !/^\s*#/.test(l)).length, 1);
});

test('the install comments and the deployment guide copy the snippet where the include expects it', () => {
  assert.match(TEMPLATE, /^#\s+sudo cp nginx-private\.conf \/etc\/nginx\/snippets\/usurper-private\.conf$/m);
  assert.match(SNIPPET, /^# Install: sudo cp nginx-private\.conf \/etc\/nginx\/snippets\/usurper-private\.conf$/m);
  assert.match(read('DOCS', 'SERVER_DEPLOYMENT.md'), /^sudo cp scripts-server\/nginx-private\.conf \/etc\/nginx\/snippets\/usurper-private\.conf$/m);
});

test('nginx and node name the same server files', () => {
  const m = /\^\/\(\?:(.*)\)\$/.exec(RULES[2]);
  const names = m[1].split('|').map((n) => n.replace(/\\\./g, '.')).flatMap((n) => n === 'package(?:-lock)?.json' ? ['package.json', 'package-lock.json'] : [n]);
  assert.deepEqual([...names].sort(), [...PRIVATE_FILES].sort());
});

test('every script and json file in the web folder is either loaded by a page or private, never both', () => {
  const pages = walk(WEB).filter((f) => f.endsWith('.html'));
  const srcs = new Set();   // the files the pages load by a script tag, resolved against the page
  for (const page of pages) {
    for (const m of fs.readFileSync(page, 'utf8').matchAll(/<script\b[^>]*\bsrc=["']([^"':]+)["']/g)) {
      const src = m[1].split('?')[0];
      srcs.add(src.startsWith('/') ? path.join(WEB, src) : path.resolve(path.dirname(page), src));
    }
  }
  // the language files are fetched by the pages ("/lang/" plus the language code plus ".json")
  const fetchesLang = pages.some((f) => /['"`]\/?lang\/['"`]?\s*\+|\/lang\/\$\{/.test(fs.readFileSync(f, 'utf8')));
  assert.ok(fetchesLang, 'the pages fetch the language files');
  const loaded = (f) => srcs.has(f) || (fetchesLang && path.dirname(f) === path.join(WEB, 'lang') && f.endsWith('.json'));
  const files = [];
  (function all(dir) {
    for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
      const f = path.join(dir, e.name);
      // web/wiki is the generated public wiki (tools/wiki-build, gitignored): pages, its scripts and its page list
      if (e.name === 'node_modules' || f === path.join(WEB, 'test') || f === path.join(WEB, 'wiki')) continue;
      if (e.isDirectory()) all(f); else if (/\.(js|json)$/.test(e.name)) files.push(f);
    }
  })(WEB);
  assert.ok(files.length >= 19, 'the walk found the scripts and json files');
  for (const f of files) {
    const top = path.dirname(f) === WEB;
    const priv = top && PRIVATE_FILES.has(path.basename(f));
    assert.ok(loaded(f) !== priv, path.relative(WEB, f) + (priv ? ' is private but a page loads it' : ' is neither loaded by a page nor private'));
  }
  for (const name of PRIVATE_FILES) assert.ok(fs.existsSync(path.join(WEB, name)), name + ' exists');
});

const PRIVATE_URLS = ['/.env', '/.env.bak', '/.git/config', '/account/.env', '/lang/.hidden.json', '/ssh-proxy.js',
  '/ssh-proxy.js.bak', '/index.html.bak', '/package.json', '/package-lock.json', '/gmcp-split.js', '/wiki-bot.js',
  '/wiki-search.js', '/dash-npcs.js', '/dash-monster-names.js', '/dash-observatory.js', '/node_modules/ws/package.json',
  '/node_modules/ws/index.js'];

test('nginx refuses every private path and serves every page and asset', () => {
  for (const u of PRIVATE_URLS) assert.ok(nginxRefuses(u), u);
  assert.ok(!nginxRefuses('/.well-known/acme-challenge/token'), 'certificate renewal still reaches the folder');
  assert.ok(PUBLIC.length > 50, 'the walk found the site');
  for (const f of PUBLIC) {
    const uri = '/' + path.relative(WEB, f).split(path.sep).join('/');
    assert.ok(!nginxRefuses(uri), uri);
  }
});

test('node refuses every private path, plain or percent encoded, and an extensionless one inside node_modules', () => {
  for (const u of PRIVATE_URLS) assert.equal(at(u), null, u);
  for (const u of ['/%2eenv', '/%2Eenv', '/ssh%2Dproxy.js', '/node%5Fmodules/ws/package.json', '/package.json?x=1',
    '/node_modules/ws', '/account/../.env', '/lang/../package.json']) {
    assert.equal(at(u), null, u);
  }
  // the bare folder name falls to the page rule (node_modules.html), a file that does not exist: the caller answers 404
  assert.equal(at('/node_modules'), path.join(WEB, 'node_modules.html'));
  assert.ok(!fs.existsSync(at('/node_modules')));
});

test('node still serves every page and asset', () => {
  for (const f of PUBLIC) {
    const uri = '/' + path.relative(WEB, f).split(path.sep).join('/');
    assert.equal(at(uri), f, uri);
  }
  assert.equal(at('/'), path.join(WEB, 'index.html'));
  assert.equal(at('/account/forgot'), path.join(WEB, 'account', 'forgot', 'index.html'));
  assert.equal(at('/escape.js'), path.join(WEB, 'escape.js'), 'a script the pages load');
});
