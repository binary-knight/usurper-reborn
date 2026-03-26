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
      { keywords: ['weapon shop'],                     image: 'weapon-shop.png', filter: '' },
      { keywords: ['armor shop'],                      image: 'weapon-shop.png', filter: 'hue-rotate(10deg)' },
      { keywords: ['magic shop'],                      image: 'inn.png',         filter: 'hue-rotate(200deg) saturate(0.8)' },
      { keywords: ['temple', 'church'],                image: 'temple.png',      filter: '' },
      { keywords: ['bank'],                            image: 'bank.png',        filter: '' },
      { keywords: ['healer'],                          image: 'healer.png',      filter: '' },
      { keywords: ['castle'],                          image: 'castle.png',      filter: '' },
      { keywords: ['wilderness'],                      image: 'wilderness.png',  filter: '' },
      { keywords: ['dark alley'],                      image: 'main-street.png', filter: 'brightness(0.35) saturate(0.4)' },
      { keywords: ['home'],                            image: 'inn.png',         filter: 'brightness(1.1) saturate(0.7)' },
    ];

    // Dungeon theme → scene image
    this.dungeonThemeMap = {
      'Catacombs':    'catacombs.png',
      'Sewers':       'sewers.png',
      'Caverns':      'caverns.png',
      'AncientRuins': 'ancient-ruins.png',
      'DemonLair':    'demon-lair.png',
      'FrozenDepths': 'frozen-depths.png',
      'VolcanicPit':  'volcanic-pit.png',
      'AbyssalVoid':  'abyssal-void.png',
    };

    // Monster family → sprite image
    this.monsterSpriteMap = {
      'Goblinoid': 'goblin.png',
      'Undead':    'skeleton.png',
      'Orcish':    'orc.png',
      'Draconic':  'dragon.png',
      'Demonic':   'demon.png',
      'Giant':     'giant.png',
      'Beast':     'wolf-beast.png',
      'Elemental': 'elemental.png',
      'Insectoid': 'spider-queen.png',
      'Construct': 'golem.png',
      'Fey':       'dark-fairy.png',
      'Aquatic':   'sea-creature.png',
      'Celestial': 'fallen-angel.png',
      'Shadow':    'shadow.png',
      'Aberration':'wraith.png',
    };

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
              <!-- Compass navigation overlay (dungeon) -->
              <div class="gui-compass" id="gui-compass">
                <button class="gui-compass-btn gui-compass-n" data-dir="N" title="North">&#9650;</button>
                <button class="gui-compass-btn gui-compass-w" data-dir="W" title="West">&#9664;</button>
                <button class="gui-compass-btn gui-compass-e" data-dir="E" title="East">&#9654;</button>
                <button class="gui-compass-btn gui-compass-s" data-dir="S" title="South">&#9660;</button>
              </div>
              <!-- Room action buttons (Fight, Treasure, etc.) -->
              <div class="gui-room-actions" id="gui-room-actions"></div>
              <!-- Click-anywhere overlay for "Press any key" -->
              <div class="gui-press-any" id="gui-press-any" style="display:none">
                <span>Click or press any key to continue...</span>
              </div>
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
    this.compass = this.container.querySelector('#gui-compass');
    this.roomActions = this.container.querySelector('#gui-room-actions');
    this.pressAny = this.container.querySelector('#gui-press-any');

    // Track game screen state
    this.screen = 'town'; // town | dungeon | combat | loot

    // Input handling
    this.inputEl.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') {
        this.sendInput(this.inputEl.value + '\n');
        this.inputEl.value = '';
        e.preventDefault();
      }
    });

    // Compass direction buttons
    this.compass.querySelectorAll('.gui-compass-btn').forEach(btn => {
      btn.addEventListener('click', () => {
        this.sendInput(btn.dataset.dir + '\n');
      });
    });

    // Press-any-key overlay
    this.pressAny.addEventListener('click', () => {
      this.sendInput('\n');
      this.pressAny.style.display = 'none';
    });

    // Keyboard shortcuts — single key sends command immediately in graphical mode
    window.addEventListener('keydown', (e) => {
      if (e.target === this.inputEl) return; // don't intercept when typing
      if (e.key === 'F10' || e.key === 'F9') return; // let view toggle through

      // Dismiss press-any-key on any keypress
      if (this.pressAny.style.display !== 'none') {
        this.sendInput('\n');
        this.pressAny.style.display = 'none';
        e.preventDefault();
        return;
      }

      // Enter key sends newline (for prompts)
      if (e.key === 'Enter') {
        this.sendInput('\n');
        e.preventDefault();
        return;
      }

      // Single key presses map to game commands
      const key = e.key.toUpperCase();
      if (/^[A-Z0-9><!]$/.test(key) && !e.ctrlKey && !e.altKey) {
        this.sendInput(key + '\n');
        e.preventDefault();
      }
    });

    // Render default dock
    this._renderDock();
  }

  // ─── Structured Game Events (from OSC JSON) ──

  handleGameEvent(event) {
    const { e: type, d: data } = event;

    switch (type) {
      case 'location':
        this.screen = 'town';
        this._setLocation(data.name);
        if (data.description) this.sceneTitle.textContent = data.description;
        this.compass.style.display = 'none';
        this.roomActions.innerHTML = '';
        break;

      case 'stats':
        this._updateHUD(data);
        if (data.className) this.state.playerClass = data.className.toLowerCase().replace(/\s+/g, '-');
        if (data.raceName) this.state.playerRace = data.raceName;
        if (data.playerName) this.state.playerName = data.playerName;
        break;

      case 'menu':
        if (data.items) this._renderDockFromServer(data.items);
        break;

      case 'npcs':
        this.npcArea.innerHTML = '';
        if (data.npcs) {
          for (const npc of data.npcs) {
            this._addNPCTagFromEvent(npc);
          }
        }
        break;

      case 'dungeon_room':
        this._setLocation(`Floor ${data.floor} — ${data.roomName}`);
        // Use theme-specific background
        const themeImage = this.dungeonThemeMap[data.theme] || 'dungeon.png';
        this._applySceneDirect(themeImage);
        this._renderDungeonRoom(data);
        break;

      case 'combat_start':
        this.screen = 'combat';
        this.state.inCombat = true;
        this.state.combat = data;
        this.state.combatFamily = data.family;
        this.compass.style.display = 'none';
        this.roomActions.innerHTML = '';
        this._renderCombatStart(data);
        this._renderCombatDock();
        break;

      case 'combat_status':
        this._renderCombatStatus(data);
        // Combat dock is now rendered by combat_menu event
        break;

      case 'combat_menu':
        // Store teammates for rendering in combat_status
        if (data.teammates) this.state.combatTeammates = data.teammates;
        this._renderFullCombatMenu(data);
        break;

      case 'combat_end':
        this.screen = 'dungeon';
        this.state.inCombat = false;
        this._renderCombatEnd(data);
        break;

      case 'narration':
        this._toast([{ text: data.text, fg: '#c0a050', bold: false }], 'system');
        break;

      case 'choice':
        // Render choice buttons overlaid on the scene
        this._renderChoiceButtons(data.context, data.title, data.options);
        break;

      case 'loot_item':
        this._renderLootItem(data);
        break;

      case 'press_any_key':
        this.pressAny.style.display = 'flex';
        break;

      case 'input_prompt':
        // Generic input prompt — focus the input field
        if (data.prompt) {
          this.inputEl.placeholder = data.prompt;
        }
        break;

      case 'confirm':
        this._renderChoiceButtons('confirm', data.question, [
          { key: 'Y', label: 'Yes', style: 'info' },
          { key: 'N', label: 'No', style: 'info' },
        ]);
        break;

      default:
        console.log('Unknown game event:', type, data);
    }
  }

  _addNPCTagFromEvent(npc) {
    const tag = document.createElement('div');
    tag.className = 'gui-npc-tag';
    tag.textContent = `${npc.name}`;
    if (npc.class) tag.title = `Lv.${npc.level || '?'} ${npc.class}`;
    this.npcArea.appendChild(tag);
  }

  _renderDockFromServer(items) {
    this.dock.innerHTML = '';

    // Group by category
    const categories = {};
    for (const item of items) {
      const cat = item.category || 'other';
      if (!categories[cat]) categories[cat] = [];
      categories[cat].push(item);
    }

    // Icon map for building types
    const iconMap = {
      dungeon: '⚔', inn: '🍺', weapons: '🗡', armor: '🛡', magic: '✨',
      music: '🎵', bank: '🏦', healer: '💊', temple: '⛪', wilderness: '🌲',
      outskirts: '🏕', training: '📖', quests: '📜', home: '🏠',
      status: '📊', quit: '🚪',
    };

    const catOrder = ['explore', 'services', 'progress', 'info'];
    let first = true;

    for (const cat of catOrder) {
      const catItems = categories[cat];
      if (!catItems || catItems.length === 0) continue;

      if (!first) {
        const divider = document.createElement('div');
        divider.className = 'gui-dock-divider';
        this.dock.appendChild(divider);
      }
      first = false;

      const sectionEl = document.createElement('div');
      sectionEl.className = 'gui-dock-section';

      for (const item of catItems) {
        const icon = iconMap[item.icon] || '📍';
        const btn = document.createElement('div');
        btn.className = 'gui-building';
        btn.innerHTML = `
          <span class="gui-building-key">${item.key}</span>
          <span class="gui-building-icon">${icon}</span>
          <span class="gui-building-name">${item.label}</span>
        `;
        btn.addEventListener('click', () => this.sendInput(item.key + '\n'));
        btn.title = `${item.label} [${item.key}]`;
        sectionEl.appendChild(btn);
      }
      this.dock.appendChild(sectionEl);
    }
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
          // Show clickable overlay
          this.pressAny.style.display = 'flex';
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
    this.sceneTitle.textContent = '';
    this.matcher.resetCombat();
    // Reset location so it re-detects
    this.state.location = '';
    // Don't clear npcArea or roomActions during combat — they hold monster cards and action buttons
    if (this.screen !== 'combat') {
      this.npcArea.innerHTML = '';
      this.roomActions.innerHTML = '';
    }
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

  _applySceneDirect(imageFile, filter = '') {
    if (this.state.currentScene === imageFile) return;
    this.state.currentScene = imageFile;
    this.sceneBg.style.backgroundImage =
      `linear-gradient(to top, rgba(2,2,4,0.5) 0%, transparent 50%),
       url('./assets/scenes/${imageFile}')`;
    this.sceneBg.style.backgroundSize = 'cover, cover';
    this.sceneBg.style.backgroundPosition = 'center, center';
    this.sceneBg.style.backgroundRepeat = 'no-repeat, no-repeat';
    this.sceneBg.style.imageRendering = 'pixelated';
    this.sceneBg.style.filter = filter;
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
    this.serverMenuItems = [];
  }

  // ─── Choice/Event Buttons ───────────────

  _renderChoiceButtons(context, title, options) {
    // Show choice buttons in the room actions area
    this.roomActions.innerHTML = '';

    if (title) {
      const titleEl = document.createElement('div');
      titleEl.className = 'gui-choice-title';
      titleEl.textContent = title;
      this.roomActions.appendChild(titleEl);
    }

    const btnRow = document.createElement('div');
    btnRow.className = 'gui-choice-row';

    for (const opt of options) {
      const btn = document.createElement('button');
      btn.className = `gui-room-action-btn ${opt.style || 'info'}`;
      btn.innerHTML = `<span class="gui-room-action-key">${opt.key}</span>${opt.label}`;
      btn.addEventListener('click', () => {
        this.sendInput(opt.key + '\n');
        this.roomActions.innerHTML = ''; // Clear after choice
      });
      btnRow.appendChild(btn);
    }
    this.roomActions.appendChild(btnRow);
  }

  _renderLootItem(data) {
    // Show loot item card with equip/take/pass buttons
    this.npcArea.innerHTML = '';
    this.roomActions.innerHTML = '';

    const loot = document.createElement('div');
    loot.className = 'gui-loot-card';

    let statsHtml = '';
    if (data.attack > 0) statsHtml += `<span class="gui-loot-stat atk">ATK: ${data.attack}</span>`;
    if (data.armor > 0) statsHtml += `<span class="gui-loot-stat def">AC: ${data.armor}</span>`;
    if (data.bonusStats) {
      for (const [stat, val] of Object.entries(data.bonusStats)) {
        if (val > 0) statsHtml += `<span class="gui-loot-stat bonus">+${val} ${stat}</span>`;
        else if (val < 0) statsHtml += `<span class="gui-loot-stat penalty">${val} ${stat}</span>`;
      }
    }

    loot.innerHTML = `
      <div class="gui-loot-rarity ${data.rarity.toLowerCase()}">${data.rarity}</div>
      <div class="gui-loot-name">${data.isIdentified ? data.itemName : '??? Unidentified ???'}</div>
      <div class="gui-loot-type">${data.itemType}</div>
      <div class="gui-loot-stats">${statsHtml}</div>
    `;
    this.npcArea.appendChild(loot);

    // Choice buttons
    const btnRow = document.createElement('div');
    btnRow.className = 'gui-choice-row';
    for (const opt of data.options) {
      const btn = document.createElement('button');
      btn.className = `gui-room-action-btn ${opt.style || 'info'}`;
      btn.innerHTML = `<span class="gui-room-action-key">${opt.key}</span>${opt.label}`;
      btn.addEventListener('click', () => {
        this.sendInput(opt.key + '\n');
        this.roomActions.innerHTML = '';
      });
      btnRow.appendChild(btn);
    }
    this.roomActions.appendChild(btnRow);
  }

  // ─── Dungeon Room Rendering ─────────────

  _renderDungeonRoom(data) {
    this.screen = 'dungeon';
    this.npcArea.innerHTML = '';
    this.toastArea.innerHTML = '';
    this.pressAny.style.display = 'none';

    // Room info overlay
    const roomInfo = document.createElement('div');
    roomInfo.className = 'gui-room-info';
    roomInfo.innerHTML = `
      <div class="gui-room-name">${data.roomName}</div>
      <div class="gui-room-desc">${data.description || ''}</div>
      ${data.atmosphere ? `<div class="gui-room-atmo">${data.atmosphere}</div>` : ''}
      <div class="gui-room-floor">Floor ${data.floor} — ${data.theme}</div>
    `;
    this.npcArea.appendChild(roomInfo);

    // Update compass — show/hide direction buttons based on available exits
    this.compass.style.display = 'flex';
    const exits = data.exits || [];
    this.compass.querySelector('.gui-compass-n').style.display = exits.includes('N') ? 'flex' : 'none';
    this.compass.querySelector('.gui-compass-s').style.display = exits.includes('S') ? 'flex' : 'none';
    this.compass.querySelector('.gui-compass-e').style.display = exits.includes('E') ? 'flex' : 'none';
    this.compass.querySelector('.gui-compass-w').style.display = exits.includes('W') ? 'flex' : 'none';

    // Room action buttons overlaid on the scene
    this.roomActions.innerHTML = '';
    const actions = [];
    if (data.hasMonsters && !data.isCleared)
      actions.push({ key: 'F', label: 'Fight', cls: 'danger' });
    if (data.hasTreasure)
      actions.push({ key: 'T', label: 'Treasure', cls: 'treasure' });
    if (data.hasEvent)
      actions.push({ key: 'V', label: 'Event', cls: 'event' });
    if (data.hasFeatures)
      actions.push({ key: 'X', label: 'Examine', cls: 'feature' });

    for (const action of actions) {
      const btn = document.createElement('button');
      btn.className = `gui-room-action-btn ${action.cls}`;
      btn.innerHTML = `<span class="gui-room-action-key">${action.key}</span>${action.label}`;
      btn.addEventListener('click', () => this.sendInput(action.key + '\n'));
      this.roomActions.appendChild(btn);
    }

    this.sceneTitle.textContent = data.atmosphere || '';
  }

  // ─── Combat Rendering (DD-style) ────────

  _renderFullCombatMenu(data) {
    // Build dock with all combat actions + quickbar skills
    this.dock.innerHTML = '';

    // Section 1: Core combat actions
    const coreSection = document.createElement('div');
    coreSection.className = 'gui-dock-section';
    for (const act of data.actions) {
      if (!act.available && act.key !== 'I') continue; // hide unavailable except potions
      const btn = this._createDockButton(act.key, act.label, act.available);
      coreSection.appendChild(btn);
    }
    this.dock.appendChild(coreSection);

    // Divider
    if (data.quickbar && data.quickbar.length > 0) {
      const div = document.createElement('div');
      div.className = 'gui-dock-divider';
      this.dock.appendChild(div);
    }

    // Section 2: Quickbar skills
    if (data.quickbar && data.quickbar.length > 0) {
      const skillSection = document.createElement('div');
      skillSection.className = 'gui-dock-section';
      for (const skill of data.quickbar) {
        const btn = this._createDockButton(skill.key, skill.label, skill.available);
        if (!skill.available) btn.classList.add('gui-building-disabled');
        skillSection.appendChild(btn);
      }
      this.dock.appendChild(skillSection);
    }

    // Scene overlay: main action buttons + target selection
    this.roomActions.innerHTML = '';

    const needsTarget = !data.singleTarget && data.targets && data.targets.length > 1;

    // Row 1: Core combat actions
    const actionRow = document.createElement('div');
    actionRow.className = 'gui-choice-row';

    const coreActions = [
      { key: 'A', label: 'Attack', style: 'danger', target: true },
      { key: 'D', label: 'Defend', style: 'info', target: false },
      { key: 'P', label: 'Power', style: 'danger', target: true },
      { key: 'E', label: 'Precise', style: 'danger', target: true },
    ];
    if (data.potions > 0) coreActions.push({ key: 'I', label: `Heal (${data.potions})`, style: 'feature', target: false });
    coreActions.push({ key: 'R', label: 'Retreat', style: 'info', target: false });

    for (const act of coreActions) {
      const btn = document.createElement('button');
      btn.className = `gui-room-action-btn ${act.style}`;
      btn.innerHTML = `<span class="gui-room-action-key">${act.key}</span>${act.label}`;
      if (needsTarget && act.target) {
        btn.addEventListener('click', () => this._showTargetPicker(data.targets, act.key));
      } else {
        btn.addEventListener('click', () => this.sendInput(act.key + '\n'));
      }
      actionRow.appendChild(btn);
    }
    this.roomActions.appendChild(actionRow);

    // Row 2: Quickbar skills (if any)
    if (data.quickbar && data.quickbar.length > 0) {
      const skillRow = document.createElement('div');
      skillRow.className = 'gui-choice-row gui-skill-row';

      for (const skill of data.quickbar) {
        const btn = document.createElement('button');
        btn.className = `gui-room-action-btn ${skill.available ? 'event' : 'info'} gui-skill-btn`;
        if (!skill.available) btn.classList.add('gui-btn-disabled');
        btn.innerHTML = `<span class="gui-room-action-key">${skill.key}</span>${skill.label}`;
        if (skill.available) {
          if (needsTarget) {
            btn.addEventListener('click', () => this._showTargetPicker(data.targets, skill.key));
          } else {
            btn.addEventListener('click', () => this.sendInput(skill.key + '\n'));
          }
        }
        skillRow.appendChild(btn);
      }
      this.roomActions.appendChild(skillRow);
    }
  }

  _createDockButton(key, label, available) {
    const btn = document.createElement('div');
    btn.className = 'gui-building' + (available ? '' : ' gui-building-disabled');
    btn.innerHTML = `
      <span class="gui-building-key">${key}</span>
      <span class="gui-building-name">${label}</span>
    `;
    if (available) {
      btn.addEventListener('click', () => this.sendInput(key + '\n'));
    }
    btn.title = `${label} [${key}]`;
    return btn;
  }

  _showTargetPicker(targets, actionKey) {
    // Replace room actions with clickable monster targets
    this.roomActions.innerHTML = '';
    const title = document.createElement('div');
    title.className = 'gui-choice-title';
    title.textContent = 'Select Target';
    this.roomActions.appendChild(title);

    const row = document.createElement('div');
    row.className = 'gui-choice-row';

    for (const target of targets) {
      const hpPct = target.maxHp > 0 ? Math.round(target.hp / target.maxHp * 100) : 0;
      const btn = document.createElement('button');
      btn.className = 'gui-room-action-btn danger';
      btn.innerHTML = `<span class="gui-room-action-key">${target.index}</span>${target.name} (${hpPct}%)`;
      btn.addEventListener('click', () => {
        // Send action key + target number
        this.sendInput(actionKey + '\n');
        // Small delay then send target
        setTimeout(() => this.sendInput(target.index + '\n'), 100);
      });
      row.appendChild(btn);
    }

    // Random target — send 0 for random
    const rndBtn = document.createElement('button');
    rndBtn.className = 'gui-room-action-btn info';
    rndBtn.innerHTML = '<span class="gui-room-action-key">0</span>Random';
    rndBtn.addEventListener('click', () => {
      this.sendInput(actionKey + '\n');
      setTimeout(() => this.sendInput('0\n'), 150);
    });
    row.appendChild(rndBtn);

    this.roomActions.appendChild(row);
  }

  _renderCombatDock() {
    const combatActions = [
      { key: 'A', label: 'Attack', category: 'combat', icon: 'fight' },
      { key: 'D', label: 'Defend', category: 'combat', icon: 'armor' },
      { key: 'P', label: 'Power', category: 'combat', icon: 'weapons' },
      { key: 'E', label: 'Precise', category: 'combat', icon: 'examine' },
      { key: 'H', label: 'Potions', category: 'items', icon: 'healer' },
      { key: 'T', label: 'Taunt', category: 'tactics', icon: 'temple' },
      { key: 'I', label: 'Disarm', category: 'tactics', icon: 'examine' },
      { key: 'W', label: 'Hide', category: 'tactics', icon: 'wilderness' },
      { key: 'R', label: 'Retreat', category: 'escape', icon: 'leave' },
    ];
    this._renderDockFromServer(combatActions);

    // Also render main combat buttons in the roomActions area (overlaid on scene)
    this.roomActions.innerHTML = '';
    const mainActions = [
      { key: 'A', label: 'Attack', style: 'danger' },
      { key: 'D', label: 'Defend', style: 'info' },
      { key: 'P', label: 'Power', style: 'danger' },
      { key: 'H', label: 'Heal', style: 'feature' },
      { key: 'R', label: 'Retreat', style: 'info' },
    ];
    for (const action of mainActions) {
      const btn = document.createElement('button');
      btn.className = `gui-room-action-btn ${action.style}`;
      btn.innerHTML = `<span class="gui-room-action-key">${action.key}</span>${action.label}`;
      btn.addEventListener('click', () => this.sendInput(action.key + '\n'));
      this.roomActions.appendChild(btn);
    }
  }

  _renderCombatStart(data) {
    this.npcArea.innerHTML = '';
    this.toastArea.innerHTML = '';

    // DD-style layout: party on left, monsters on right
    const battlefield = document.createElement('div');
    battlefield.className = 'gui-battlefield';

    // Left: Player party
    const partyDiv = document.createElement('div');
    partyDiv.className = 'gui-party-side';
    partyDiv.id = 'gui-party-side';
    // Player character (class sprite facing right)
    const playerClass = this.state.playerClass || 'warrior';
    partyDiv.innerHTML = `
      <div class="gui-combatant gui-player-char">
        <img class="gui-combatant-sprite" src="./assets/classes/${playerClass}-east.png"
             alt="${playerClass}" onerror="this.src='./assets/classes/${playerClass}.png'">
        <div class="gui-combatant-name player-name">${this.state.playerName || 'You'}</div>
        <div class="gui-combatant-hp-bar">
          <div class="gui-combatant-hp-fill hp-player" id="gui-player-combat-hp" style="width:100%"></div>
        </div>
      </div>
    `;
    battlefield.appendChild(partyDiv);

    // Center: VS divider
    const vs = document.createElement('div');
    vs.className = 'gui-vs-divider';
    vs.textContent = 'VS';
    battlefield.appendChild(vs);

    // Right: Monsters
    const monsterDiv = document.createElement('div');
    monsterDiv.className = 'gui-monster-side';
    monsterDiv.id = 'gui-monster-side';
    const spriteFile = this.monsterSpriteMap[data.family] || '';
    // For now single monster — will update with combat_status for multi
    monsterDiv.innerHTML = this._renderMonsterCombatant(data.monsterName, data.monsterLevel,
      data.monsterHp, data.monsterMaxHp, data.isBoss, spriteFile, data.family);
    battlefield.appendChild(monsterDiv);

    this.npcArea.appendChild(battlefield);
  }

  _renderMonsterCombatant(name, level, hp, maxHp, isBoss, spriteFile, family) {
    const hpPct = maxHp > 0 ? (hp / maxHp * 100) : 0;
    const hpClass = hpPct < 25 ? 'critical' : hpPct < 50 ? 'warning' : '';
    const sprite = spriteFile
      ? `<img class="gui-combatant-sprite monster-sprite" src="./assets/monsters/${spriteFile}" alt="${name}" onerror="this.style.display='none'">`
      : `<div class="gui-combatant-sprite gui-monster-placeholder">${name[0]}</div>`;
    return `
      <div class="gui-combatant gui-monster-combatant${isBoss ? ' boss' : ''}">
        ${sprite}
        <div class="gui-combatant-name monster-name">${name}${isBoss ? ' [BOSS]' : ''}</div>
        <div class="gui-combatant-level">Lv.${level}</div>
        <div class="gui-combatant-hp-bar">
          <div class="gui-combatant-hp-fill hp-monster ${hpClass}" style="width:${hpPct}%"></div>
        </div>
        <div class="gui-combatant-hp-text">${hp.toLocaleString()} / ${maxHp.toLocaleString()}</div>
      </div>
    `;
  }

  _renderCombatStatus(data) {
    if (!data.monsters || data.monsters.length === 0) return;

    // Re-render the battlefield with updated HP values
    this.npcArea.innerHTML = '';

    const battlefield = document.createElement('div');
    battlefield.className = 'gui-battlefield';

    // Left: Player party
    const partyDiv = document.createElement('div');
    partyDiv.className = 'gui-party-side';
    const playerClass = this.state.playerClass || 'warrior';
    const playerHpPct = data.playerMaxHp > 0 ? (data.playerHp / data.playerMaxHp * 100) : 100;
    const playerHpClass = playerHpPct < 25 ? 'critical' : playerHpPct < 50 ? 'warning' : '';

    let partyHtml = `
      <div class="gui-combatant gui-player-char">
        <img class="gui-combatant-sprite" src="./assets/classes/${playerClass}-east.png"
             alt="${playerClass}" onerror="this.src='./assets/classes/${playerClass}.png'">
        <div class="gui-combatant-name player-name">${this.state.playerName || 'You'}</div>
        <div class="gui-combatant-hp-bar">
          <div class="gui-combatant-hp-fill hp-player ${playerHpClass}" style="width:${playerHpPct}%"></div>
        </div>
      </div>
    `;

    // Add teammates if present (from combat_status or stored from combat_menu)
    const teammates = data.teammates || this.state.combatTeammates || [];
    for (const tm of teammates) {
      const tmHpPct = tm.maxHp > 0 ? (tm.hp / tm.maxHp * 100) : 100;
      const tmClass = (tm.className || '').toLowerCase().replace(/\s+/g, '-');
      const tmSprite = tmClass
        ? `<img class="gui-combatant-sprite" src="./assets/classes/${tmClass}-east.png" alt="${tm.name}" onerror="this.className='gui-combatant-sprite gui-teammate-placeholder'; this.textContent='${tm.name[0]}';">`
        : `<div class="gui-combatant-sprite gui-teammate-placeholder">${tm.name[0]}</div>`;
      partyHtml += `
        <div class="gui-combatant gui-teammate">
          ${tmSprite}
          <div class="gui-combatant-name">${tm.name}</div>
          <div class="gui-combatant-hp-bar">
            <div class="gui-combatant-hp-fill hp-ally" style="width:${tmHpPct}%"></div>
          </div>
        </div>
      `;
    }
    partyDiv.innerHTML = partyHtml;
    battlefield.appendChild(partyDiv);

    // Center
    const vs = document.createElement('div');
    vs.className = 'gui-vs-divider';
    battlefield.appendChild(vs);

    // Right: Monsters
    const monsterDiv = document.createElement('div');
    monsterDiv.className = 'gui-monster-side';
    let monstersHtml = '';
    for (const m of data.monsters) {
      const spriteFile = this.monsterSpriteMap[m.family] || '';
      monstersHtml += this._renderMonsterCombatant(m.name, m.level, m.hp, m.maxHp, m.isBoss, spriteFile, m.family);
    }
    monsterDiv.innerHTML = monstersHtml;
    battlefield.appendChild(monsterDiv);

    this.npcArea.appendChild(battlefield);

    // Update player HUD
    if (data.playerHp !== undefined) {
      this._updateHUD({
        hp: data.playerHp, maxHp: data.playerMaxHp,
        mana: data.playerMana, maxMana: data.playerMaxMana
      });
    }
  }

  _renderCombatEnd(data) {
    this.npcArea.innerHTML = '';
    const result = document.createElement('div');
    result.className = 'gui-combat-result';
    result.innerHTML = `
      <div class="gui-combat-result-title">${data.outcome}</div>
      <div class="gui-combat-rewards">
        ${data.xpGained ? `<span class="gui-reward-xp">+${data.xpGained.toLocaleString()} XP</span>` : ''}
        ${data.goldGained ? `<span class="gui-reward-gold">+${data.goldGained.toLocaleString()} Gold</span>` : ''}
        ${data.lootName ? `<span class="gui-reward-loot">${data.lootName}</span>` : ''}
      </div>
    `;
    this.npcArea.appendChild(result);

    // Auto-clear after a few seconds
    setTimeout(() => {
      if (result.parentNode) result.remove();
    }, 5000);
  }
}

window.GameUI = GameUI;
