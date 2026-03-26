// Usurper Reborn — Electron Renderer
// Supports two modes:
//   local  — spawns UsurperReborn.exe as child process, pipes stdio
//   online — connects via WebSocket to game server

const Terminal = window.Terminal;
const FitAddon = window.FitAddon?.FitAddon || window.FitAddon;
const WebLinksAddon = window.WebLinksAddon?.WebLinksAddon || window.WebLinksAddon;

// Terminal setup
const term = new Terminal({
  fontFamily: "'JetBrains Mono', 'Cascadia Code', 'Consolas', monospace",
  fontSize: 14,
  theme: {
    background: '#000000',
    foreground: '#c0c0c0',
    cursor: '#c0a050',
    cursorAccent: '#000000',
    selectionBackground: '#3a3a5a',
    selectionForeground: '#ffffff',
    black: '#000000',
    red: '#aa0000',
    green: '#00aa00',
    yellow: '#aa5500',
    blue: '#5555ff',
    magenta: '#aa00aa',
    cyan: '#00aaaa',
    white: '#c0c0c0',
    brightBlack: '#555555',
    brightRed: '#ff5555',
    brightGreen: '#55ff55',
    brightYellow: '#ffff55',
    brightBlue: '#5555ff',
    brightMagenta: '#ff55ff',
    brightCyan: '#55ffff',
    brightWhite: '#ffffff'
  },
  cursorBlink: true,
  scrollback: 5000,
  allowProposedApi: true
});

const fitAddon = new FitAddon();
term.loadAddon(fitAddon);
term.loadAddon(new WebLinksAddon());

const container = document.getElementById('terminal');
term.open(container);
fitAddon.fit();

const resizeObserver = new ResizeObserver(() => fitAddon.fit());
resizeObserver.observe(container);

// Connection state
let ws = null;
let reconnectTimer = null;
let connectionMode = 'local';
const statusEl = document.getElementById('connection-status');

function setStatus(text, cls) {
  statusEl.textContent = text;
  statusEl.className = cls;
}

// ─── Send function (works for both modes) ──

function sendToServer(data) {
  if (connectionMode === 'local') {
    window.usurper.sendInput(data);
  } else if (ws && ws.readyState === WebSocket.OPEN) {
    ws.send(data);
  }
}

// ─── Local Mode — child process ────────────

async function connectLocal() {
  setStatus('Starting game...', 'connecting');

  const result = await window.usurper.spawnGame();
  if (!result.ok) {
    setStatus('Failed: ' + result.message, 'disconnected');
    return;
  }

  setStatus('Playing (Local)', 'connected');
  term.focus();

  // Receive game output
  window.usurper.onGameData((data) => {
    term.write(data);
    parser.feed(data);
  });

  window.usurper.onGameExit((code) => {
    setStatus(`Game exited (${code})`, 'disconnected');
    term.write(`\r\n\x1b[33m--- Game process exited (code ${code}) ---\x1b[0m\r\n`);
  });

  window.usurper.onGameError((msg) => {
    setStatus('Error: ' + msg, 'disconnected');
    term.write(`\r\n\x1b[31m--- Error: ${msg} ---\x1b[0m\r\n`);
  });
}

// ─── Online Mode — WebSocket ───────────────

async function connectOnline(wsUrl) {
  if (ws) { ws.close(); ws = null; }
  setStatus('Connecting...', 'connecting');

  ws = new WebSocket(wsUrl);
  ws.binaryType = 'arraybuffer';

  ws.onopen = () => {
    setStatus('Connected (Online)', 'connected');
    term.focus();
    const dims = fitAddon.proposeDimensions();
    if (dims) {
      ws.send(JSON.stringify({ type: 'resize', cols: dims.cols, rows: dims.rows }));
    }
  };

  ws.onmessage = (event) => {
    if (event.data instanceof ArrayBuffer) {
      const bytes = new Uint8Array(event.data);
      term.write(bytes);
      parser.feed(bytes);
    } else {
      term.write(event.data);
      parser.feed(event.data);
    }
  };

  ws.onclose = () => {
    setStatus('Disconnected', 'disconnected');
    if (!reconnectTimer) {
      reconnectTimer = setTimeout(() => { reconnectTimer = null; connectOnline(wsUrl); }, 3000);
    }
  };

  ws.onerror = () => setStatus('Connection error', 'disconnected');
}

// ─── Terminal input → game ─────────────────

term.onData((data) => sendToServer(data));

// ─── ANSI Parser ───────────────────────────

const parser = new AnsiParser();
const parsedOutput = document.getElementById('parsed-output');
const parsedPanel = document.getElementById('parsed-panel');
let showParsed = false;

// ─── Graphical UI ──────────────────────────

const graphicalView = document.getElementById('graphical-view');
const terminalContainer = document.getElementById('terminal-container');
const viewToggle = document.getElementById('view-toggle');

const gameUI = new GameUI(graphicalView, sendToServer);
let graphicalMode = false;

function setViewMode(graphical) {
  graphicalMode = graphical;
  terminalContainer.style.display = graphical ? 'none' : 'block';
  graphicalView.classList.toggle('active', graphical);
  viewToggle.textContent = graphical ? 'Graphical' : 'Terminal';
  if (!graphical) {
    fitAddon.fit();
    term.focus();
  } else {
    gameUI.inputEl.focus();
  }
}

window.addEventListener('keydown', (e) => {
  if (e.key === 'F10') {
    setViewMode(!graphicalMode);
    e.preventDefault();
    e.stopPropagation();
  }
  if (e.key === 'F9') {
    showParsed = !showParsed;
    parsedPanel.classList.toggle('visible', showParsed);
    if (!graphicalMode) fitAddon.fit();
    e.preventDefault();
    e.stopPropagation();
  }
}, true);

viewToggle.addEventListener('click', () => setViewMode(!graphicalMode));

// Feed parsed lines to graphical UI and debug panel
parser.onLine = (spans) => {
  const classified = gameUI.matcher.classify(spans);
  gameUI.processLine(classified);

  if (!showParsed) return;
  const lineEl = document.createElement('div');
  lineEl.className = 'parsed-line';
  const tagEl = document.createElement('span');
  tagEl.style.color = '#555';
  tagEl.style.fontSize = '9px';
  tagEl.textContent = `[${classified.type}] `;
  lineEl.appendChild(tagEl);
  for (const span of spans) {
    const spanEl = document.createElement('span');
    spanEl.textContent = span.text;
    spanEl.style.color = span.fg;
    if (span.bold) spanEl.style.fontWeight = 'bold';
    if (span.dim) spanEl.style.opacity = '0.6';
    lineEl.appendChild(spanEl);
  }
  parsedOutput.appendChild(lineEl);
  while (parsedOutput.children.length > 200) parsedOutput.removeChild(parsedOutput.firstChild);
  parsedOutput.scrollTop = parsedOutput.scrollHeight;
};

parser.onClearScreen = () => {
  gameUI.clearScreen();
  if (showParsed) parsedOutput.innerHTML = '';
};

// ─── Startup ───────────────────────────────

async function start() {
  const config = await window.usurper.getConfig();
  connectionMode = config.mode;

  if (connectionMode === 'local') {
    connectLocal();
  } else {
    connectOnline(config.wsUrl);
  }
}

start();

window.addEventListener('focus', () => {
  if (!graphicalMode) term.focus();
});
