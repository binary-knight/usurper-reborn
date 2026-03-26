// Usurper Reborn — Darkest Dungeon-style Game UI
// Full graphical estate screen with clickable buildings,
// toast notifications, and atmospheric scene rendering.

class GameUI {
  constructor(container, sendInput) {
    this.container = container;
    this.sendInput = sendInput;
    this.matcher = new PatternMatcher();

    // Game state
    this.state = {
      hp: 0, maxHp: 0,
      mana: 0, maxMana: 0,
      stamina: 0, maxStamina: 0,
      gold: 0, level: 0,
      location: '',
      currentScene: null,
    };

    // Location → scene image
    this.sceneMap = [
      { keywords: ['main street'],                    image: 'main-street.png', filter: '' },
      { keywords: ['dungeon', 'floor'],               image: 'dungeon.png',     filter: '' },
      { keywords: ['inn', 'dormitory'],               image: 'inn.png',         filter: '' },
      { keywords: ['weapon', 'armor', 'magic shop'],  image: 'inn.png',         filter: 'hue-rotate(20deg) saturate(0.8)' },
      { keywords: ['dark alley'],                      image: 'main-street.png', filter: 'brightness(0.35) saturate(0.4)' },
    ];

    // Building definitions for the estate dock
    this.buildings = {
      explore: [
        { key: 'D', name: 'Dungeons',   icon: '⚔' },
        { key: 'E', name: 'Wilderness',  icon: '🌲' },
        { key: '>', name: 'Outskirts',   icon: '🏕' },
      ],
      services: [
        { key: 'I', name: 'Inn',         icon: '🍺' },
        { key: 'W', name: 'Weapons',     icon: '🗡' },
        { key: 'A', name: 'Armor',       icon: '🛡' },
        { key: 'M', name: 'Magic',       icon: '✨' },
        { key: 'U', name: 'Music',       icon: '🎵' },
        { key: 'B', name: 'Bank',        icon: '🏦' },
        { key: '1', name: 'Healer',      icon: '💊' },
        { key: 'T', name: 'Temple',      icon: '⛪' },
      ],
      progress: [
        { key: 'V', name: 'Training',    icon: '📖' },
        { key: '2', name: 'Quests',      icon: '📜' },
        { key: 'H', name: 'Home',        icon: '🏠' },
      ],
    };

    // Pending menu items from server (used to detect which buildings are available)
    this.serverMenuItems = [];

    this._build();
  }

  _build() {
    this.container.innerHTML = `
      <div class="gui-layout">
        <!-- Top HUD -->
        <div class="gui-hud">
          <div class="gui-hud-left">
            <div class="gui-hud-stat">
              <span class="gui-hud-label">HP</span>
              <div class="gui-bar-track">
                <div class="gui-bar-fill gui-bar-hp" id="gui-hp-bar"></div>
              </div>
              <span class="gui-hud-value" id="gui-hp-text">0/0</span>
            </div>
            <div class="gui-hud-stat">
              <span class="gui-hud-label" id="gui-resource-label">ST</span>
              <div class="gui-bar-track">
                <div class="gui-bar-fill gui-bar-stamina" id="gui-resource-bar"></div>
              </div>
              <span class="gui-hud-value" id="gui-resource-text">0/0</span>
            </div>
          </div>
          <div class="gui-hud-center">
            <span class="gui-location-name" id="gui-location">&mdash;</span>
          </div>
          <div class="gui-hud-right">
            <div class="gui-hud-stat">
              <span class="gui-hud-label">Gold</span>
              <span class="gui-hud-value gui-gold" id="gui-gold">0</span>
            </div>
            <div class="gui-hud-stat">
              <span class="gui-hud-label">Level</span>
              <span class="gui-hud-value" id="gui-level">1</span>
            </div>
          </div>
        </div>

        <!-- Main scene area -->
        <div class="gui-main">
          <div class="gui-scene">
            <div class="gui-scene-bg" id="gui-scene-bg">
              <div class="gui-npc-area" id="gui-npc-area"></div>
              <div class="gui-scene-title" id="gui-scene-title"></div>
            </div>
          </div>
          <!-- Toast notifications -->
          <div class="gui-toast-area" id="gui-toast-area"></div>
        </div>

        <!-- Building dock -->
        <div class="gui-dock">
          <div class="gui-dock-scroll" id="gui-dock"></div>
        </div>

        <!-- Input -->
        <div class="gui-input-row">
          <input type="text" class="gui-input" id="gui-input"
            placeholder="Type command..." autocomplete="off" spellcheck="false">
        </div>
      </div>
    `;

    // Cache DOM
    this.hpBar = this.container.querySelector('#gui-hp-bar');
    this.hpText = this.container.querySelector('#gui-hp-text');
    this.resourceBar = this.container.querySelector('#gui-resource-bar');
    this.resourceText = this.container.querySelector('#gui-resource-text');
    this.resourceLabel = this.container.querySelector('#gui-resource-label');
    this.goldText = this.container.querySelector('#gui-gold');
    this.levelText = this.container.querySelector('#gui-level');
    this.locationText = this.container.querySelector('#gui-location');
    this.sceneBg = this.container.querySelector('#gui-scene-bg');
    this.sceneTitle = this.container.querySelector('#gui-scene-title');
    this.npcArea = this.container.querySelector('#gui-npc-area');
    this.toastArea = this.container.querySelector('#gui-toast-area');
    this.dock = this.container.querySelector('#gui-dock');
    this.inputEl = this.container.querySelector('#gui-input');

    // Input handling
    this.inputEl.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') {
        this.sendInput(this.inputEl.value + '\n');
        this.inputEl.value = '';
        e.preventDefault();
      }
    });

    // Keyboard shortcuts for buildings
    window.addEventListener('keydown', (e) => {
      if (e.target === this.inputEl) return; // don't intercept when typing
      if (e.key === 'F10' || e.key === 'F9') return; // let view toggle through
      // Single key presses map to building commands
      const key = e.key.toUpperCase();
      if (/^[A-Z0-9><!]$/.test(key) && !e.ctrlKey && !e.altKey) {
        this.sendInput(key + '\n');
      }
    });

    // Render default dock
    this._renderDock();
  }

  // ─── Line Processing ─────────────────────

  processLine(classified) {
    switch (classified.type) {
      case LineType.STATUS_BAR:
        this._updateHUD(classified.data);
        break;

      case LineType.MUD_PROMPT:
        this._setLocation(classified.location);
        break;

      case LineType.MENU_ITEM:
        // Collect menu items from server to know what's available
        if (classified.multi) {
          for (const item of classified.items) {
            this.serverMenuItems.push(item);
          }
        } else {
          this.serverMenuItems.push({ key: classified.key, label: classified.label });
        }
        break;

      case LineType.BOX_TOP:
        break;

      case LineType.BOX_BOTTOM:
        // Flush collected menu to dock
        if (this.serverMenuItems.length > 0) {
          this._updateDockFromServer();
        }
        // Detect location from box title
        if (classified.boxTitle) {
          this._detectLocationFromTitle(classified.boxTitle);
        }
        break;

      case LineType.BOX_LINE:
        if (classified.inner && classified.boxTitle === classified.inner) {
          this._detectLocationFromTitle(classified.inner);
        }
        break;

      case LineType.NPC_ACTIVITY:
        this._addNPCTag(classified.spans);
        this._toast(classified.spans, 'npc');
        break;

      case LineType.LOCATION_DESC:
        this._setSceneDescription(classified.spans);
        break;

      case LineType.SYSTEM_MSG:
        this._toast(classified.spans, 'system');
        break;

      case LineType.COMBAT_HIT:
        this._toast(classified.spans, 'combat');
        break;

      case LineType.COMBAT_HEAL:
        this._toast(classified.spans, 'heal');
        break;

      case LineType.PROMPT:
        if (classified.pressAnyKey) {
          this._toast([{ text: classified.raw, fg: '#c0a050', bold: false }], 'system');
        }
        break;

      case LineType.EMPTY:
      case LineType.BOX_DIVIDER:
      case LineType.SEPARATOR:
        break;

      default:
        // Regular text — show as toast if it's meaningful
        if (classified.raw && classified.raw.length > 3) {
          this._toast(classified.spans, '');
        }
        break;
    }
  }

  clearScreen() {
    this.serverMenuItems = [];
    this.npcArea.innerHTML = '';
    this.sceneTitle.textContent = '';
    this.matcher.resetCombat();
    // Reset location so it re-detects
    this.state.location = '';
  }

  // ─── HUD ────────────────────────────────

  _updateHUD(data) {
    if (data.hp !== undefined) {
      this.state.hp = data.hp;
      this.state.maxHp = data.maxHp;
      const pct = data.maxHp ? (data.hp / data.maxHp * 100) : 100;
      this.hpBar.style.width = pct + '%';
      this.hpBar.className = 'gui-bar-fill gui-bar-hp' + (pct < 30 ? ' critical' : pct < 60 ? ' warning' : '');
      this.hpText.textContent = `${data.hp.toLocaleString()}/${data.maxHp.toLocaleString()}`;
    }

    if (data.stamina !== undefined) {
      this.resourceLabel.textContent = 'ST';
      this.resourceBar.className = 'gui-bar-fill gui-bar-stamina';
      const pct = data.maxStamina ? (data.stamina / data.maxStamina * 100) : 100;
      this.resourceBar.style.width = pct + '%';
      this.resourceText.textContent = `${data.stamina}/${data.maxStamina}`;
    } else if (data.mana !== undefined) {
      this.resourceLabel.textContent = 'MP';
      this.resourceBar.className = 'gui-bar-fill gui-bar-mana';
      const pct = data.maxMana ? (data.mana / data.maxMana * 100) : 100;
      this.resourceBar.style.width = pct + '%';
      this.resourceText.textContent = `${data.mana}/${data.maxMana}`;
    }

    if (data.gold !== undefined) {
      this.state.gold = data.gold;
      this.goldText.textContent = data.gold.toLocaleString();
    }
    if (data.level !== undefined) {
      this.state.level = data.level;
      this.levelText.textContent = data.level;
    }
  }

  // ─── Location & Scene ───────────────────

  _detectLocationFromTitle(title) {
    const locationNames = [
      'Main Street', 'The Inn', 'The Dungeon', 'Weapon Shop', 'Armor Shop',
      'Magic Shop', 'The Bank', 'The Healer', 'The Temple', 'The Castle',
      'Dark Alley', 'Love Street', 'Quest Hall', 'Level Master', 'Home',
      'Music Shop', 'The Arena', 'The Wilderness', 'The Outskirts',
      'Team Corner', 'The Settlement', 'The Prison', 'The Dormitory',
    ];
    const clean = title.replace(/[^\w\s]/g, '').trim();
    for (const name of locationNames) {
      if (clean.toLowerCase().includes(name.toLowerCase())) {
        this._setLocation(name);
        return;
      }
    }
  }

  _setLocation(name) {
    if (this.state.location === name) return;
    this.state.location = name;
    this.locationText.textContent = name;
    this._applyScene(name);
  }

  _applyScene(name) {
    const loc = name.toLowerCase();
    const scene = this.sceneMap.find(s => s.keywords.some(k => loc.includes(k)));

    if (scene && this.state.currentScene !== scene.image) {
      this.state.currentScene = scene.image;
      this.sceneBg.style.backgroundImage =
        `linear-gradient(to top, rgba(2,2,4,0.5) 0%, transparent 50%),
         url('./assets/scenes/${scene.image}')`;
      this.sceneBg.style.backgroundSize = 'cover, cover';
      this.sceneBg.style.backgroundPosition = 'center, center';
      this.sceneBg.style.backgroundRepeat = 'no-repeat, no-repeat';
      this.sceneBg.style.imageRendering = 'pixelated';
      this.sceneBg.style.filter = scene.filter || '';
    } else if (!scene) {
      this.state.currentScene = null;
      this.sceneBg.style.backgroundImage = '';
      this.sceneBg.style.filter = '';
    }
  }

  _setSceneDescription(spans) {
    const text = spans.map(s => s.text).join('');
    this.sceneTitle.textContent = text;
  }

  // ─── NPC Tags (floating in scene) ───────

  _addNPCTag(spans) {
    // Extract NPC name (usually the first bold/colored span)
    const text = spans.map(s => s.text).join('').trim();
    if (!text) return;

    // Short version for the tag
    const short = text.length > 50 ? text.substring(0, 50) + '…' : text;

    const tag = document.createElement('div');
    tag.className = 'gui-npc-tag';
    tag.textContent = short;
    this.npcArea.appendChild(tag);

    // Max 8 NPC tags
    while (this.npcArea.children.length > 8) {
      this.npcArea.removeChild(this.npcArea.firstChild);
    }
  }

  // ─── Toast Notifications ────────────────

  _toast(spans, type) {
    const div = document.createElement('div');
    div.className = 'gui-toast' + (type ? ` toast-${type}` : '');

    for (const span of spans) {
      if (!span.text) continue;
      const el = document.createElement('span');
      el.textContent = span.text;
      el.style.color = span.fg;
      if (span.bold) el.style.fontWeight = 'bold';
      div.appendChild(el);
    }

    this.toastArea.appendChild(div);

    // Max 6 visible toasts
    while (this.toastArea.children.length > 6) {
      this.toastArea.removeChild(this.toastArea.firstChild);
    }

    // Auto-remove after animation completes
    setTimeout(() => {
      if (div.parentNode) div.remove();
    }, 6000);
  }

  // ─── Building Dock ──────────────────────

  _renderDock() {
    this.dock.innerHTML = '';

    const sections = [
      { name: 'explore', items: this.buildings.explore },
      { name: 'services', items: this.buildings.services },
      { name: 'progress', items: this.buildings.progress },
    ];

    sections.forEach((section, i) => {
      if (i > 0) {
        const divider = document.createElement('div');
        divider.className = 'gui-dock-divider';
        this.dock.appendChild(divider);
      }

      const sectionEl = document.createElement('div');
      sectionEl.className = 'gui-dock-section';

      for (const bld of section.items) {
        const btn = document.createElement('div');
        btn.className = 'gui-building';
        btn.innerHTML = `
          <span class="gui-building-key">${bld.key}</span>
          <span class="gui-building-icon">${bld.icon}</span>
          <span class="gui-building-name">${bld.name}</span>
        `;
        btn.addEventListener('click', () => {
          this.sendInput(bld.key + '\n');
        });
        btn.title = `${bld.name} [${bld.key}]`;
        sectionEl.appendChild(btn);
      }

      this.dock.appendChild(sectionEl);
    });
  }

  _updateDockFromServer() {
    // Future: match server menu items with buildings to show/hide availability
    // For now just clear the pending items
    this.serverMenuItems = [];
  }
}

window.GameUI = GameUI;
