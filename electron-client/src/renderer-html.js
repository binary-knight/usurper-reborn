// Usurper Reborn — HTML Renderer
// Renders classified game output as styled HTML panels
// instead of raw ANSI terminal text.

class HtmlRenderer {
  constructor(container) {
    this.container = container;
    this.maxLines = 500;
    this.matcher = new PatternMatcher();

    // Persistent state extracted from stream
    this.statusData = null;
    this.currentLocation = '';

    // Build UI structure
    this._buildUI();
  }

  _buildUI() {
    this.container.innerHTML = `
      <div id="gr-status-bar"></div>
      <div id="gr-main">
        <div id="gr-output"></div>
      </div>
      <div id="gr-input-area">
        <div id="gr-menu-buttons"></div>
        <div id="gr-input-row">
          <input type="text" id="gr-input" placeholder="Type command..." autocomplete="off" spellcheck="false">
        </div>
      </div>
    `;

    this.outputEl = this.container.querySelector('#gr-output');
    this.statusBarEl = this.container.querySelector('#gr-status-bar');
    this.menuButtonsEl = this.container.querySelector('#gr-menu-buttons');
    this.inputEl = this.container.querySelector('#gr-input');

    // Pending menu items collected between screen clears
    this.pendingMenuItems = [];
  }

  // Render a classified line
  renderLine(classified) {
    switch (classified.type) {
      case LineType.EMPTY:
        this._appendDiv('gr-line gr-empty', '');
        break;

      case LineType.BOX_TOP:
        this._appendDiv('gr-box-top', '');
        break;

      case LineType.BOX_BOTTOM:
        this._appendDiv('gr-box-bottom', '');
        // Flush collected menu items into button bar
        if (this.pendingMenuItems.length > 0) {
          this._renderMenuButtons(this.pendingMenuItems);
          this.pendingMenuItems = [];
        }
        break;

      case LineType.BOX_DIVIDER:
        this._appendDiv('gr-box-divider', '');
        break;

      case LineType.BOX_LINE:
        this._appendSpans('gr-box-line', classified.spans);
        break;

      case LineType.SEPARATOR:
        this._appendDiv('gr-separator', '');
        break;

      case LineType.MENU_ITEM:
        if (classified.multi) {
          // Multiple items on one line
          for (const item of classified.items) {
            this.pendingMenuItems.push(item);
          }
          this._appendMultiMenuLine(classified.items);
        } else {
          this.pendingMenuItems.push({ key: classified.key, label: classified.label });
          this._appendMenuItemLine(classified);
        }
        break;

      case LineType.STATUS_BAR:
        this.statusData = classified.data;
        this._updateStatusBar();
        // Status bar means new screen — flush any pending menu items
        if (this.pendingMenuItems.length > 0) {
          this._renderMenuButtons(this.pendingMenuItems);
          this.pendingMenuItems = [];
        }
        break;

      case LineType.MUD_PROMPT:
        this.currentLocation = classified.location;
        // Don't render MUD prompt in output — it's in the status bar
        break;

      case LineType.COMBAT_HIT:
        this._appendSpans('gr-line gr-combat-hit', classified.spans);
        break;

      case LineType.COMBAT_MISS:
        this._appendSpans('gr-line gr-combat-miss', classified.spans);
        break;

      case LineType.COMBAT_HEAL:
        this._appendSpans('gr-line gr-combat-heal', classified.spans);
        break;

      case LineType.PROMPT:
        if (classified.pressAnyKey) {
          this._appendDiv('gr-line gr-prompt', classified.raw);
        }
        break;

      case LineType.TEXT:
      default:
        this._appendSpans('gr-line', classified.spans);
        break;
    }

    this._trimOutput();
    this.outputEl.scrollTop = this.outputEl.scrollHeight;
  }

  clearScreen() {
    this.outputEl.innerHTML = '';
    this.pendingMenuItems = [];
    this.menuButtonsEl.innerHTML = '';
    this.matcher.resetCombat();
  }

  // Set callback for when user clicks a menu button or submits input
  set onInput(fn) { this._onInput = fn; }

  _appendDiv(className, text) {
    const div = document.createElement('div');
    div.className = className;
    if (text) div.textContent = text;
    this.outputEl.appendChild(div);
  }

  _appendSpans(className, spans) {
    const div = document.createElement('div');
    div.className = className;
    const isBoxLine = className.includes('gr-box-line');
    for (const span of spans) {
      const el = document.createElement('span');
      let text = span.text;
      // Strip box-drawing chars from box lines for cleaner rendering
      if (isBoxLine) {
        text = text.replace(/[╔╗╚╝║╠╣═─│]/g, '').trim();
        if (!text) continue;
      }
      el.textContent = text;
      el.style.color = span.fg;
      if (span.bold) el.style.fontWeight = 'bold';
      if (span.dim) el.style.opacity = '0.6';
      div.appendChild(el);
    }
    this.outputEl.appendChild(div);
  }

  _appendMenuItemLine(classified) {
    const div = document.createElement('div');
    div.className = 'gr-line gr-menu-item-line';
    div.dataset.key = classified.key;
    div.innerHTML = `<span class="gr-menu-key">[${classified.key}]</span> <span class="gr-menu-label">${classified.label}</span>`;
    div.addEventListener('click', () => {
      if (this._onInput) this._onInput(classified.key + '\n');
    });
    this.outputEl.appendChild(div);
  }

  _appendMultiMenuLine(items) {
    const div = document.createElement('div');
    div.className = 'gr-line gr-multi-menu-line';
    for (const item of items) {
      const btn = document.createElement('span');
      btn.className = 'gr-inline-menu-item';
      btn.innerHTML = `<span class="gr-menu-key">[${item.key}]</span>${item.label}`;
      btn.addEventListener('click', () => {
        if (this._onInput) this._onInput(item.key + '\n');
      });
      div.appendChild(btn);
    }
    this.outputEl.appendChild(div);
  }

  _renderMenuButtons(items) {
    this.menuButtonsEl.innerHTML = '';
    for (const item of items) {
      const btn = document.createElement('button');
      btn.className = 'gr-menu-btn';
      btn.innerHTML = `<span class="gr-btn-key">${item.key}</span> ${item.label}`;
      btn.addEventListener('click', () => {
        if (this._onInput) this._onInput(item.key + '\n');
      });
      this.menuButtonsEl.appendChild(btn);
    }
  }

  _updateStatusBar() {
    if (!this.statusData) return;
    const d = this.statusData;
    const hpPct = d.maxHp ? Math.round((d.hp / d.maxHp) * 100) : 100;
    const hpColor = hpPct > 60 ? '#55ff55' : hpPct > 30 ? '#ffff55' : '#ff5555';

    let bars = `<div class="gr-stat"><span class="gr-stat-label">HP</span> <span style="color:${hpColor}">${(d.hp || 0).toLocaleString()}/${(d.maxHp || 0).toLocaleString()}</span></div>`;

    if (d.mana !== undefined) {
      bars += `<div class="gr-stat"><span class="gr-stat-label">MP</span> <span style="color:#5599ff">${d.mana.toLocaleString()}/${d.maxMana.toLocaleString()}</span></div>`;
    }
    if (d.stamina !== undefined) {
      bars += `<div class="gr-stat"><span class="gr-stat-label">ST</span> <span style="color:#ffff55">${d.stamina.toLocaleString()}/${d.maxStamina.toLocaleString()}</span></div>`;
    }
    if (d.gold !== undefined) {
      bars += `<div class="gr-stat"><span class="gr-stat-label">Gold</span> <span style="color:#ffaa00">${d.gold.toLocaleString()}</span></div>`;
    }
    if (d.level !== undefined) {
      bars += `<div class="gr-stat"><span class="gr-stat-label">Level</span> <span style="color:#c0c0c0">${d.level}</span></div>`;
    }

    this.statusBarEl.innerHTML = bars;
  }

  _trimOutput() {
    while (this.outputEl.children.length > this.maxLines) {
      this.outputEl.removeChild(this.outputEl.firstChild);
    }
  }

  // Setup input handling
  setupInput(sendFn) {
    this._onInput = sendFn;

    this.inputEl.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') {
        const val = this.inputEl.value;
        sendFn(val + '\n');
        this.inputEl.value = '';
        e.preventDefault();
      }
    });

    // Focus input on click anywhere in output
    this.outputEl.addEventListener('click', () => {
      this.inputEl.focus();
    });
  }
}

window.HtmlRenderer = HtmlRenderer;
