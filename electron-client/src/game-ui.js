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

      case 'dungeon_map':
        this._renderDungeonMap(data);
        break;

      case 'inventory':
        this._renderInventory(data);
        break;

      case 'inventory_result':
        this._showInventoryResult(data);
        break;

      case 'inventory_slot_pick':
        this._showSlotPicker(data);
        break;

      case 'character_status':
        this._renderCharacterStatus(data);
        break;

      case 'party_status':
        this._renderPartyStatus(data);
        break;

      case 'potions_menu':
        this._renderPotionsMenu(data);
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

      case 'combat_action':
        this._handleCombatAction(data);
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

    // Danger stars
    const dangerStars = '★'.repeat(Math.min(data.dangerRating || 0, 5));
    const clearedBadge = data.isCleared ? '<span class="gui-room-cleared-badge">CLEARED</span>' : '';

    // Features list
    const features = data.features || [];
    const featuresHtml = features.length > 0
      ? `<div class="gui-room-features">${features.map(f => `<span class="gui-room-feature">· ${f}</span>`).join('')}</div>`
      : '';

    // Exits with cleared status
    const exits = data.exits || [];
    const exitsHtml = exits.length > 0
      ? `<div class="gui-room-exits">${exits.map(e => {
          const ex = typeof e === 'object' ? e : { dir: e, label: e, cleared: false };
          return `<span class="gui-room-exit${ex.cleared ? ' cleared' : ''}">
            <span class="gui-room-exit-dir">[${ex.dir}]</span> ${ex.label}${ex.cleared ? ' ✓' : ''}
          </span>`;
        }).join('')}</div>`
      : '';

    // Room info panel — left side overlay
    const roomInfo = document.createElement('div');
    roomInfo.className = 'gui-room-info';
    roomInfo.innerHTML = `
      <div class="gui-room-header">
        <div class="gui-room-name">${data.roomName}</div>
        <div class="gui-room-meta">
          <span class="gui-room-floor">Floor ${data.floor}</span>
          <span class="gui-room-theme">${data.theme}</span>
          <span class="gui-room-danger">${dangerStars}</span>
          ${clearedBadge}
        </div>
      </div>
      <div class="gui-room-desc">${data.description || ''}</div>
      ${data.atmosphere ? `<div class="gui-room-atmo">${data.atmosphere}</div>` : ''}
      ${featuresHtml}
      <div class="gui-room-divider"></div>
      <div class="gui-room-section-label">Exits</div>
      ${exitsHtml}
      ${data.potions !== undefined ? `<div class="gui-room-status">Potions: ${data.potions}/${data.maxPotions}</div>` : ''}
    `;
    this.npcArea.appendChild(roomInfo);

    // Update compass — show/hide direction buttons based on available exits
    this.compass.style.display = 'flex';
    const exitDirs = exits.map(e => typeof e === 'object' ? e.dir : e);
    this.compass.querySelector('.gui-compass-n').style.display = exitDirs.includes('N') ? 'flex' : 'none';
    this.compass.querySelector('.gui-compass-s').style.display = exitDirs.includes('S') ? 'flex' : 'none';
    this.compass.querySelector('.gui-compass-e').style.display = exitDirs.includes('E') ? 'flex' : 'none';
    this.compass.querySelector('.gui-compass-w').style.display = exitDirs.includes('W') ? 'flex' : 'none';

    // Room action buttons — all available actions
    this.roomActions.innerHTML = '';

    // Primary actions row (context-sensitive)
    const primaryActions = [];
    if (data.hasMonsters && !data.isCleared)
      primaryActions.push({ key: 'F', label: 'Fight', cls: 'danger' });
    if (data.hasTreasure)
      primaryActions.push({ key: 'T', label: 'Treasure', cls: 'treasure' });
    if (data.hasEvent)
      primaryActions.push({ key: 'V', label: 'Event', cls: 'event' });
    if (data.hasFeatures)
      primaryActions.push({ key: 'X', label: 'Examine', cls: 'feature' });

    if (primaryActions.length > 0) {
      const primaryRow = document.createElement('div');
      primaryRow.className = 'gui-choice-row';
      for (const action of primaryActions) {
        const btn = document.createElement('button');
        btn.className = `gui-room-action-btn ${action.cls}`;
        btn.innerHTML = `<span class="gui-room-action-key">${action.key}</span>${action.label}`;
        btn.addEventListener('click', () => this.sendInput(action.key + '\n'));
        primaryRow.appendChild(btn);
      }
      this.roomActions.appendChild(primaryRow);
    }

    // Utility actions row
    const utilRow = document.createElement('div');
    utilRow.className = 'gui-choice-row gui-utility-row';
    const utilActions = [
      { key: 'R', label: 'Camp' },
      { key: 'M', label: 'Map' },
      { key: 'G', label: 'Guide' },
      { key: 'I', label: 'Inventory' },
      { key: 'P', label: 'Potions' },
      { key: 'Y', label: 'Party' },
      { key: '=', label: 'Status' },
      { key: 'Q', label: 'Leave' },
    ];
    for (const action of utilActions) {
      const btn = document.createElement('button');
      btn.className = 'gui-room-action-btn info gui-util-btn';
      btn.innerHTML = `<span class="gui-room-action-key">${action.key}</span>${action.label}`;
      btn.addEventListener('click', () => this.sendInput(action.key + '\n'));
      utilRow.appendChild(btn);
    }
    this.roomActions.appendChild(utilRow);

    this.sceneTitle.textContent = '';
  }

  // ─── Dungeon Map Rendering ──────────────

  _renderDungeonMap(data) {
    this.npcArea.innerHTML = '';
    this.roomActions.innerHTML = '';
    this.compass.style.display = 'none';

    const overlay = document.createElement('div');
    overlay.className = 'gui-map-overlay';

    // Header
    overlay.innerHTML = `
      <div class="gui-map-header">
        <span class="gui-map-title">Dungeon Map — Floor ${data.floor} (${data.theme})</span>
        <span class="gui-map-stats">${data.explored}/${data.total} explored, ${data.cleared}/${data.total} cleared</span>
      </div>
    `;

    const rooms = data.rooms || [];
    if (rooms.length === 0) {
      overlay.innerHTML += '<div style="text-align:center;color:var(--text-dim);padding:40px;">Map unavailable</div>';
      this.npcArea.appendChild(overlay);
      return;
    }

    // Find bounds
    let minX = Infinity, maxX = -Infinity, minY = Infinity, maxY = -Infinity;
    for (const r of rooms) {
      if (r.x < minX) minX = r.x;
      if (r.x > maxX) maxX = r.x;
      if (r.y < minY) minY = r.y;
      if (r.y > maxY) maxY = r.y;
    }

    // Position lookup
    const posMap = {};
    for (const r of rooms) posMap[`${r.x},${r.y}`] = r;

    // Canvas-based map with corridors
    const CELL = 44;   // cell size in px
    const CORR = 16;   // corridor length between cells
    const STEP = CELL + CORR; // total step per grid unit
    const cols = maxX - minX + 1;
    const rows = maxY - minY + 1;
    const cw = cols * STEP - CORR + 20; // +padding
    const ch = rows * STEP - CORR + 20;

    const canvas = document.createElement('canvas');
    canvas.className = 'gui-map-canvas';
    canvas.width = cw;
    canvas.height = ch;
    const ctx = canvas.getContext('2d');

    const ox = 10; // offset/padding
    const oy = 10;

    // Color map
    const typeColors = {
      player: '#ffdd44', cleared: '#44aa44', monsters: '#cc3333',
      boss: '#ff4444', stairs: '#4477dd', safe: '#5599aa', unknown: '#444444'
    };

    // Draw corridors first
    for (const room of rooms) {
      const rx = (room.x - minX) * STEP + ox;
      const ry = (room.y - minY) * STEP + oy;
      const exits = room.exits || [];

      ctx.strokeStyle = 'rgba(120, 100, 60, 0.5)';
      ctx.lineWidth = 3;

      for (const dir of exits) {
        const d = dir.toUpperCase();
        let tx, ty;
        if (d === 'E') { tx = rx + CELL; ty = ry + CELL/2; ctx.beginPath(); ctx.moveTo(tx, ty); ctx.lineTo(tx + CORR, ty); ctx.stroke(); }
        if (d === 'S') { tx = rx + CELL/2; ty = ry + CELL; ctx.beginPath(); ctx.moveTo(tx, ty); ctx.lineTo(tx, ty + CORR); ctx.stroke(); }
        // Only draw E and S to avoid double-drawing
      }
    }

    // Draw rooms
    for (const room of rooms) {
      const rx = (room.x - minX) * STEP + ox;
      const ry = (room.y - minY) * STEP + oy;
      const color = typeColors[room.type] || '#555';

      // Room background
      ctx.fillStyle = room.type === 'player' ? 'rgba(200, 180, 50, 0.2)' :
                      room.type === 'monsters' ? 'rgba(180, 30, 30, 0.15)' :
                      room.type === 'cleared' ? 'rgba(40, 120, 40, 0.1)' :
                      room.type === 'boss' ? 'rgba(200, 30, 30, 0.25)' :
                      room.type === 'stairs' ? 'rgba(40, 60, 160, 0.15)' :
                      'rgba(30, 30, 30, 0.3)';
      ctx.fillRect(rx + 1, ry + 1, CELL - 2, CELL - 2);

      // Room border
      ctx.strokeStyle = color;
      ctx.lineWidth = room.type === 'player' ? 2 : 1;
      ctx.strokeRect(rx + 0.5, ry + 0.5, CELL - 1, CELL - 1);

      // Player glow
      if (room.type === 'player') {
        ctx.shadowColor = 'rgba(200, 180, 50, 0.6)';
        ctx.shadowBlur = 8;
        ctx.strokeRect(rx + 0.5, ry + 0.5, CELL - 1, CELL - 1);
        ctx.shadowBlur = 0;
      }

      // Room symbol
      ctx.fillStyle = color;
      ctx.font = 'bold 18px monospace';
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';
      ctx.fillText(room.symbol, rx + CELL/2, ry + CELL/2);
    }

    overlay.appendChild(canvas);

    // Legend + footer row
    const bottomRow = document.createElement('div');
    bottomRow.className = 'gui-map-bottom';
    bottomRow.innerHTML = `
      <div class="gui-map-legend">
        <span class="gui-map-legend-item"><span class="gui-map-sym-player">@</span> You</span>
        <span class="gui-map-legend-item"><span class="gui-map-sym-cleared">#</span> Cleared</span>
        <span class="gui-map-legend-item"><span class="gui-map-sym-monsters">█</span> Monsters</span>
        <span class="gui-map-legend-item"><span class="gui-map-sym-stairs">></span> Stairs</span>
        <span class="gui-map-legend-item"><span class="gui-map-sym-boss">B</span> Boss</span>
        <span class="gui-map-legend-item"><span class="gui-map-sym-safe">·</span> Safe</span>
        <span class="gui-map-legend-item"><span class="gui-map-sym-unknown">?</span> Unknown</span>
      </div>
      <div class="gui-map-footer">
        <span>Location: ${data.currentRoomName}</span>
        ${data.bossDefeated ? '<span class="gui-map-boss-defeated">★ BOSS DEFEATED</span>' : ''}
      </div>
    `;
    overlay.appendChild(bottomRow);

    this.npcArea.appendChild(overlay);
  }

  // ─── Inventory Rendering ────────────────

  _renderInventory(data) {
    this.npcArea.innerHTML = '';

    const overlay = document.createElement('div');
    overlay.className = 'gui-map-overlay gui-inv-overlay';

    // Header
    overlay.innerHTML = `
      <div class="gui-map-header">
        <span class="gui-map-title">Equipment — ${data.playerName}</span>
        <span class="gui-map-stats">Lv.${data.level} ${data.className} | Gold: ${(data.gold || 0).toLocaleString()}</span>
      </div>
    `;

    const body = document.createElement('div');
    body.className = 'gui-inv-body';

    // -- Left: Paperdoll with equipment slots --
    const paperdoll = document.createElement('div');
    paperdoll.className = 'gui-inv-paperdoll';

    // Character portrait in center
    const sprites = this._getClassSprite(data.className);
    paperdoll.innerHTML = `<img class="gui-inv-portrait" src="${sprites.hdSrc}"
      onerror="this.src='${sprites.regularSrc}'; this.onerror=function(){this.src='${sprites.fallbackSrc}';}">`;

    // Equipment slots in display order (flows into 2-column grid)
    const slotLayout = {
      MainHand: { icon: '⚔' },
      OffHand:  { icon: '🛡' },
      Head:     { icon: '🎩' },
      Body:     { icon: '👕' },
      Arms:     { icon: '💪' },
      Hands:    { icon: '🧤' },
      Legs:     { icon: '👖' },
      Feet:     { icon: '👢' },
      Waist:    { icon: '🩳' },
      Cloak:    { icon: '🧣' },
      Face:     { icon: '👤' },
      Neck:     { icon: '📿' },
      LFinger:  { icon: '💍' },
      RFinger:  { icon: '💍' },
    };

    const slotsGrid = document.createElement('div');
    slotsGrid.className = 'gui-inv-slots-grid';

    const eqMap = {};
    for (const eq of (data.equipment || [])) eqMap[eq.slot] = eq;

    for (const [slotName, layout] of Object.entries(slotLayout)) {
      const eq = eqMap[slotName];
      const isEmpty = !eq || eq.name === '(empty)';
      const slot = document.createElement('div');
      slot.className = `gui-inv-slot ${isEmpty ? 'empty' : (eq?.rarity || '')}`;
      slot.title = isEmpty ? slotName : `${eq.name}\n${eq.attack ? 'ATK: ' + eq.attack : ''} ${eq.defense ? 'DEF: ' + eq.defense : ''}`;

      slot.innerHTML = `
        <span class="gui-inv-slot-icon">${isEmpty ? layout.icon : (eq.attack ? '⚔' : '🛡')}</span>
        <span class="gui-inv-slot-name">${isEmpty ? slotName : eq.name}</span>
      `;

      // Click equipped slot — send UNEQUIP command (stays in graphical overlay)
      if (!isEmpty) {
        const sn = slotName;
        slot.addEventListener('click', () => {
          this.sendInput(`UNEQUIP:${sn}\n`);
        });
        slot.style.cursor = 'pointer';
        slot.title += '\n\nClick to unequip';
      }

      slotsGrid.appendChild(slot);
    }

    const leftPanel = document.createElement('div');
    leftPanel.className = 'gui-inv-left';
    leftPanel.appendChild(paperdoll);
    leftPanel.appendChild(slotsGrid);
    body.appendChild(leftPanel);

    // -- Right: Backpack --
    const rightPanel = document.createElement('div');
    rightPanel.className = 'gui-inv-right';
    rightPanel.innerHTML = `<div class="gui-inv-section-label">Backpack (${(data.backpack || []).length} items)</div>`;

    const bpGrid = document.createElement('div');
    bpGrid.className = 'gui-inv-bp-grid';

    const backpack = data.backpack || [];
    for (let i = 0; i < backpack.length; i++) {
      const item = backpack[i];
      const cell = document.createElement('div');
      cell.className = 'gui-inv-bp-cell';
      cell.title = `${item.name}\nType: ${item.type}\n${item.attack ? 'ATK: ' + item.attack : ''} ${item.defense ? 'DEF: ' + item.defense : ''}\n\nClick to equip`;
      const typeIcon = item.type === 'Weapon' ? '⚔' : item.type === 'Shield' ? '🛡' :
                       item.type === 'Head' ? '🎩' : item.type === 'Body' ? '👕' :
                       item.type === 'Fingers' ? '💍' : item.type === 'Neck' ? '📿' : '🛡';
      cell.innerHTML = `
        <span class="gui-inv-bp-cell-icon">${typeIcon}</span>
        <span class="gui-inv-bp-cell-name">${item.name}</span>
        <span class="gui-inv-bp-cell-stats">${item.attack ? `ATK:${item.attack}` : ''} ${item.defense ? `DEF:${item.defense}` : ''}</span>
      `;
      // Click to equip (or drop if drop mode active)
      const bpIdx = i;
      cell.addEventListener('click', () => {
        if (this._inventoryDropMode) {
          this.sendInput(`DROP:${bpIdx}\n`);
          this._inventoryDropMode = false;
        } else {
          this.sendInput(`EQUIP:${bpIdx}\n`);
        }
      });
      cell.style.cursor = 'pointer';
      bpGrid.appendChild(cell);
    }

    if ((data.backpack || []).length === 0) {
      bpGrid.innerHTML = '<div style="color:var(--text-faint);padding:20px;text-align:center;">Backpack empty</div>';
    }

    rightPanel.appendChild(bpGrid);

    // Action buttons
    const actions = document.createElement('div');
    actions.className = 'gui-inv-actions';
    const dropBtn = document.createElement('button');
    dropBtn.className = 'gui-room-action-btn info gui-util-btn';
    dropBtn.innerHTML = '<span class="gui-room-action-key">D</span>Drop Mode';
    dropBtn.addEventListener('click', () => {
      // Toggle drop mode — next backpack click drops instead of equips
      this._inventoryDropMode = !this._inventoryDropMode;
      dropBtn.classList.toggle('active-mode', this._inventoryDropMode);
      dropBtn.innerHTML = this._inventoryDropMode
        ? '<span class="gui-room-action-key">D</span>Drop Mode ON'
        : '<span class="gui-room-action-key">D</span>Drop Mode';
    });
    actions.appendChild(dropBtn);

    const closeBtn = document.createElement('button');
    closeBtn.className = 'gui-room-action-btn info gui-util-btn';
    closeBtn.innerHTML = '<span class="gui-room-action-key">Q</span>Close';
    closeBtn.addEventListener('click', () => {
      this._inventoryDropMode = false;
      this.npcArea.innerHTML = '';
      this.sendInput('Q\n');
    });
    actions.appendChild(closeBtn);
    rightPanel.appendChild(actions);

    body.appendChild(rightPanel);
    overlay.appendChild(body);
    this.npcArea.appendChild(overlay);
  }

  /** Show equip/unequip result as a toast in the inventory overlay */
  _showInventoryResult(data) {
    const existing = document.querySelector('.gui-inv-toast');
    if (existing) existing.remove();

    const toast = document.createElement('div');
    toast.className = `gui-inv-toast gui-inv-toast-${data.type || 'info'}`;
    toast.textContent = data.message;
    // Insert at top of the overlay
    const overlay = this.npcArea.querySelector('.gui-inv-overlay');
    if (overlay) {
      overlay.insertBefore(toast, overlay.children[1]); // After header
    }
    setTimeout(() => toast.remove(), 3000);
  }

  /** Show slot picker for rings or 1H weapons */
  _showSlotPicker(data) {
    const existing = document.querySelector('.gui-inv-slot-picker');
    if (existing) existing.remove();

    const picker = document.createElement('div');
    picker.className = 'gui-inv-slot-picker';
    picker.innerHTML = `<div class="gui-inv-picker-title">Where to equip ${data.itemName}?</div>`;

    for (const opt of (data.options || [])) {
      const btn = document.createElement('button');
      btn.className = 'gui-room-action-btn feature';
      btn.innerHTML = `${opt.label}<br><span style="font-size:8px;color:var(--text-dim)">${opt.current}</span>`;
      btn.addEventListener('click', () => {
        picker.remove();
        this.sendInput(`EQUIP:${data.itemIndex}:${opt.slot}\n`);
      });
      picker.appendChild(btn);
    }

    const cancelBtn = document.createElement('button');
    cancelBtn.className = 'gui-room-action-btn info';
    cancelBtn.textContent = 'Cancel';
    cancelBtn.addEventListener('click', () => picker.remove());
    picker.appendChild(cancelBtn);

    const overlay = this.npcArea.querySelector('.gui-inv-overlay');
    if (overlay) overlay.appendChild(picker);
  }

  // ─── Character Status Rendering ───────────

  _renderCharacterStatus(data) {
    this.npcArea.innerHTML = '';
    this.roomActions.innerHTML = '';

    const overlay = document.createElement('div');
    overlay.className = 'gui-map-overlay';

    // Use class sprite as portrait
    const sprites = this._getClassSprite(data.className);

    overlay.innerHTML = `
      <div class="gui-map-header">
        <span class="gui-map-title">Character Status</span>
        <span class="gui-map-stats">${data.isKnighted ? '⚔ ' : ''}${data.name}</span>
      </div>
      <div class="gui-status-body">
        <div class="gui-status-portrait">
          <img class="gui-status-portrait-img" src="${sprites.hdSrc}"
               onerror="this.src='${sprites.regularSrc}'; this.onerror=function(){this.src='${sprites.fallbackSrc}';}">
          <div class="gui-status-name">${data.name}</div>
          <div class="gui-status-classrace">Lv.${data.level} ${data.className} ${data.race}</div>
        </div>
        <div class="gui-status-stats">
          <div class="gui-status-bars">
            <div class="gui-status-bar-row">
              <span class="gui-status-bar-label">HP</span>
              <div class="gui-status-bar"><div class="gui-status-bar-fill hp" style="width:${data.maxHp > 0 ? (data.hp/data.maxHp*100) : 0}%"></div></div>
              <span class="gui-status-bar-val">${data.hp}/${data.maxHp}</span>
            </div>
            ${data.isManaClass ? `
            <div class="gui-status-bar-row">
              <span class="gui-status-bar-label">MP</span>
              <div class="gui-status-bar"><div class="gui-status-bar-fill mp" style="width:${data.maxMana > 0 ? (data.mana/data.maxMana*100) : 0}%"></div></div>
              <span class="gui-status-bar-val">${data.mana}/${data.maxMana}</span>
            </div>` : `
            <div class="gui-status-bar-row">
              <span class="gui-status-bar-label">ST</span>
              <div class="gui-status-bar"><div class="gui-status-bar-fill sta" style="width:${data.maxStamina > 0 ? (data.stamina/data.maxStamina*100) : 0}%"></div></div>
              <span class="gui-status-bar-val">${data.stamina}/${data.maxStamina}</span>
            </div>`}
          </div>
          <div class="gui-status-attributes">
            ${['str','dex','agi','con','intel','wis','cha','def'].map(s => {
              const label = s === 'intel' ? 'INT' : s.toUpperCase();
              const val = data[s] || 0;
              return `<div class="gui-status-attr"><span class="gui-status-attr-label">${label}</span><span class="gui-status-attr-val">${val}</span></div>`;
            }).join('')}
          </div>
          <div class="gui-status-info">
            <span>Gold: ${(data.gold || 0).toLocaleString()}</span>
            <span>Potions: ${data.potions}/${data.maxPotions}</span>
            <span>XP: ${(data.experience || 0).toLocaleString()}</span>
          </div>
        </div>
      </div>
    `;

    this.npcArea.appendChild(overlay);
  }

  // ─── Party Status Rendering ───────────────

  _renderPartyStatus(data) {
    this.npcArea.innerHTML = '';
    this.roomActions.innerHTML = '';

    const overlay = document.createElement('div');
    overlay.className = 'gui-map-overlay';
    overlay.innerHTML = `<div class="gui-map-header"><span class="gui-map-title">Party Management</span></div>`;

    const list = document.createElement('div');
    list.className = 'gui-party-list';

    for (const m of (data.members || [])) {
      const hpPct = m.maxHp > 0 ? (m.hp / m.maxHp * 100) : 0;
      const sprites = this._getClassSprite(m.className);
      const card = document.createElement('div');
      card.className = 'gui-party-card';
      card.innerHTML = `
        <img class="gui-party-portrait" src="${sprites.hdSrc}"
             onerror="this.src='${sprites.regularSrc}'; this.onerror=function(){this.src='${sprites.fallbackSrc}';}">
        <div class="gui-party-info">
          <div class="gui-party-member-name">${m.name}</div>
          <div class="gui-party-member-class">Lv.${m.level} ${m.className}</div>
          <div class="gui-party-hp-row">
            <span class="gui-party-hp-label">HP</span>
            <div class="gui-party-hp-bar"><div class="gui-party-hp-fill" style="width:${hpPct}%"></div></div>
            <span class="gui-party-hp-val">${m.hp}/${m.maxHp}</span>
          </div>
          ${m.isManaClass ? `
          <div class="gui-party-hp-row">
            <span class="gui-party-hp-label">MP</span>
            <div class="gui-party-hp-bar"><div class="gui-party-hp-fill mp" style="width:${m.maxMana > 0 ? (m.mana/m.maxMana*100) : 0}%"></div></div>
            <span class="gui-party-hp-val">${m.mana}/${m.maxMana}</span>
          </div>` : ''}
          <div class="gui-party-equip">⚔ ${m.weapon} | 🛡 ${m.armor}</div>
        </div>
      `;
      list.appendChild(card);
    }

    if ((data.members || []).length === 0) {
      list.innerHTML = '<div style="text-align:center;color:var(--text-dim);padding:40px;">No party members</div>';
    }

    overlay.appendChild(list);
    this.npcArea.appendChild(overlay);
  }

  // ─── Potions Menu Rendering ───────────────

  _renderPotionsMenu(data) {
    this.npcArea.innerHTML = '';
    this.roomActions.innerHTML = '';

    const overlay = document.createElement('div');
    overlay.className = 'gui-map-overlay';
    const hpPct = data.playerMaxHp > 0 ? (data.playerHp / data.playerMaxHp * 100) : 0;

    overlay.innerHTML = `
      <div class="gui-map-header">
        <span class="gui-map-title">Potions & Healing</span>
        <span class="gui-map-stats">Gold: ${(data.gold || 0).toLocaleString()}</span>
      </div>
      <div class="gui-potions-player">
        <div class="gui-potions-hp-row">
          <span>HP</span>
          <div class="gui-potions-bar"><div class="gui-potions-fill hp" style="width:${hpPct}%"></div></div>
          <span>${data.playerHp}/${data.playerMaxHp}</span>
        </div>
        <div class="gui-potions-count">Potions: ${data.potions}/${data.maxPotions} | Heals: ${(data.healAmount || 0).toLocaleString()} HP each</div>
      </div>
    `;

    // Action buttons
    const actions = document.createElement('div');
    actions.className = 'gui-potions-actions';
    const btns = [
      { key: 'U', label: 'Use Potion (Self)', enabled: data.potions > 0 && data.playerHp < data.playerMaxHp },
      { key: 'H', label: 'Heal to Full', enabled: data.potions > 0 && data.playerHp < data.playerMaxHp },
      { key: 'B', label: `Buy Potion (${data.potionCost}g)`, enabled: data.gold >= data.potionCost && data.potions < data.maxPotions },
      { key: 'T', label: 'Heal Teammate', enabled: data.potions > 0 && (data.teammates || []).length > 0 },
      { key: 'A', label: 'Heal All', enabled: data.potions > 0 },
      { key: 'Q', label: 'Back', enabled: true },
    ];
    for (const b of btns) {
      const btn = document.createElement('button');
      btn.className = `gui-room-action-btn ${b.enabled ? 'feature' : 'info'}${b.enabled ? '' : ' gui-btn-disabled'}`;
      btn.innerHTML = `<span class="gui-room-action-key">${b.key}</span>${b.label}`;
      if (b.enabled) btn.addEventListener('click', () => this.sendInput(b.key + '\n'));
      actions.appendChild(btn);
    }
    overlay.appendChild(actions);

    // Teammate HP bars
    if ((data.teammates || []).length > 0) {
      const tmSection = document.createElement('div');
      tmSection.className = 'gui-potions-team';
      tmSection.innerHTML = '<div class="gui-inv-section-label">Team Status</div>';
      for (const tm of data.teammates) {
        const tmHpPct = tm.maxHp > 0 ? (tm.hp / tm.maxHp * 100) : 0;
        const row = document.createElement('div');
        row.className = 'gui-potions-tm-row';
        row.innerHTML = `
          <span class="gui-potions-tm-name">${tm.name}</span>
          <div class="gui-potions-bar"><div class="gui-potions-fill hp" style="width:${tmHpPct}%"></div></div>
          <span class="gui-potions-tm-val">${tm.hp}/${tm.maxHp}</span>
        `;
        tmSection.appendChild(row);
      }
      overlay.appendChild(tmSection);
    }

    this.npcArea.appendChild(overlay);
  }

  // ─── Combat Rendering (DD-style) ────────

  _renderFullCombatMenu(data) {
    // Hide the dock during combat — ability bar goes in the scene
    this.dock.innerHTML = '';

    // Build the DD-style ability bar at the bottom of the scene
    this.roomActions.innerHTML = '';

    const needsTarget = !data.singleTarget && data.targets && data.targets.length > 1;

    const abilityBar = document.createElement('div');
    abilityBar.className = 'gui-combat-ability-bar';

    // -- Character portrait --
    const playerClass = this.state.playerClass || 'warrior';
    const sprites = this._getClassSprite(playerClass);
    const portrait = document.createElement('div');
    portrait.className = 'gui-combat-portrait hd';
    portrait.innerHTML = `<img src="${sprites.hdSrc}" alt="${playerClass}"
      onerror="this.parentElement.classList.remove('hd'); this.src='${sprites.regularSrc}'; this.onerror=function(){this.src='${sprites.fallbackSrc}';}">`;
    abilityBar.appendChild(portrait);

    // -- Mini stats panel --
    const hpPct = this.state.maxHp > 0 ? (this.state.hp / this.state.maxHp * 100) : 100;
    const mpPct = this.state.maxMana > 0 ? (this.state.mana / this.state.maxMana * 100) : 0;
    const staPct = this.state.maxStamina > 0 ? (this.state.stamina / this.state.maxStamina * 100) : 0;
    const statsPanel = document.createElement('div');
    statsPanel.className = 'gui-combat-stats-mini';
    statsPanel.innerHTML = `
      <div class="stat-row">
        <span class="stat-label">HP</span>
        <div class="stat-bar"><div class="stat-fill hp" style="width:${hpPct}%"></div></div>
        <span class="stat-value">${this.state.hp}/${this.state.maxHp}</span>
      </div>
      <div class="stat-row">
        <span class="stat-label">${this.state.maxMana > 0 ? 'MP' : 'ST'}</span>
        <div class="stat-bar"><div class="stat-fill ${this.state.maxMana > 0 ? 'mp' : 'sta'}" style="width:${this.state.maxMana > 0 ? mpPct : staPct}%"></div></div>
        <span class="stat-value">${this.state.maxMana > 0 ? `${this.state.mana}/${this.state.maxMana}` : `${this.state.stamina}/${this.state.maxStamina}`}</span>
      </div>
      ${data.potions !== undefined ? `<div class="stat-row"><span class="stat-label" style="color:#cc5555">⚗</span><span class="stat-value" style="flex:1">${data.potions} potions</span></div>` : ''}
    `;
    abilityBar.appendChild(statsPanel);

    // -- Skill slots container --
    const skillsContainer = document.createElement('div');
    skillsContainer.className = 'gui-combat-skills';

    // Skill icon map for core actions
    const skillIcons = {
      'A': { icon: '⚔', label: 'Attack', cat: 'combat', target: true },
      'D': { icon: '🛡', label: 'Defend', cat: 'defense', target: false },
      'P': { icon: '💥', label: 'Power', cat: 'combat', target: true },
      'E': { icon: '🎯', label: 'Precise', cat: 'combat', target: true },
      'T': { icon: '📢', label: 'Taunt', cat: 'tactics', target: true },
      'I': { icon: '🗡', label: 'Disarm', cat: 'tactics', target: true },
      'W': { icon: '👤', label: 'Hide', cat: 'tactics', target: false },
      'H': { icon: '❤', label: 'Heal', cat: 'heal', target: false },
      'R': { icon: '🏃', label: 'Flee', cat: 'escape', target: false },
    };

    // Core combat actions as skill slots
    const coreKeys = ['A', 'P', 'E', 'D', 'T', 'W', 'H', 'R'];
    for (const key of coreKeys) {
      const info = skillIcons[key];
      if (!info) continue;
      // Check if this action is available from server data
      const serverAction = data.actions ? data.actions.find(a => a.key === key) : null;
      const isAvailable = serverAction ? serverAction.available : true;
      // Special: hide potions button if no potions
      if (key === 'H' && data.potions !== undefined && data.potions <= 0) continue;

      const slot = this._createSkillSlot(key, info.icon, info.label, info.cat, isAvailable,
        needsTarget && info.target ? data.targets : null);
      skillsContainer.appendChild(slot);
    }

    // Separator before quickbar
    if (data.quickbar && data.quickbar.length > 0) {
      const sep = document.createElement('div');
      sep.style.cssText = 'width:1px; height:40px; background:rgba(140,100,40,0.3); margin:0 4px; flex-shrink:0;';
      skillsContainer.appendChild(sep);

      // Quickbar skills
      for (const skill of data.quickbar) {
        const slot = this._createSkillSlot(
          skill.key, '✦', skill.label, 'spell', skill.available,
          needsTarget ? data.targets : null
        );
        skillsContainer.appendChild(slot);
      }
    }

    abilityBar.appendChild(skillsContainer);
    this.roomActions.appendChild(abilityBar);
  }

  /** Create a single DD-style skill slot button */
  _createSkillSlot(key, icon, label, category, available, targets) {
    const slot = document.createElement('div');
    slot.className = `gui-skill-slot ${category}${available ? '' : ' disabled'}`;
    slot.innerHTML = `
      <span class="gui-skill-key">${key}</span>
      <span class="gui-skill-icon">${icon}</span>
      <span class="gui-skill-label">${label}</span>
    `;
    slot.title = `${label} [${key}]`;

    if (available) {
      if (targets && targets.length > 1) {
        slot.addEventListener('click', () => this._showTargetPicker(targets, key));
      } else {
        slot.addEventListener('click', () => this.sendInput(key + '\n'));
      }
    }
    return slot;
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
    // In DD-style, the dock is hidden during combat — the ability bar is rendered inside the scene
    this.dock.innerHTML = '';
  }

  _renderCombatStart(data) {
    this.npcArea.innerHTML = '';
    this.toastArea.innerHTML = '';
    this.roomActions.innerHTML = '';
    this.state.combatLog = [];

    this._renderBattlefield(data);
  }

  /** Get the best available sprite for a player/ally class */
  _getClassSprite(className) {
    const cls = (className || 'warrior').toLowerCase().replace(/\s+/g, '-');
    // Check if HD sprite exists (we'll try HD first, fallback to regular)
    return {
      hdSrc: `./assets/classes-hd/${cls}.png`,
      regularSrc: `./assets/classes/${cls}-east.png`,
      fallbackSrc: `./assets/classes/${cls}.png`,
      className: cls
    };
  }

  _renderPlayerCombatant(name, hpPct, hpClass, isPlayer) {
    const playerClass = this.state.playerClass || 'warrior';
    const sprites = this._getClassSprite(playerClass);
    return `
      <div class="gui-combatant gui-player-char${isPlayer ? ' active' : ''}">
        <img class="gui-combatant-sprite hd-sprite"
             src="${sprites.hdSrc}"
             alt="${playerClass}"
             onerror="this.className='gui-combatant-sprite'; this.src='${sprites.regularSrc}'; this.onerror=function(){this.src='${sprites.fallbackSrc}';}">
        <div class="gui-combatant-info">
          <div class="gui-combatant-name player-name">${name || 'You'}</div>
          <div class="gui-combatant-hp-bar">
            <div class="gui-combatant-hp-fill hp-player ${hpClass}" style="width:${hpPct}%"></div>
          </div>
        </div>
      </div>
    `;
  }

  _renderTeammateCombatant(tm) {
    const tmHpPct = tm.maxHp > 0 ? (tm.hp / tm.maxHp * 100) : 100;
    const tmHpClass = tmHpPct < 25 ? 'critical' : tmHpPct < 50 ? 'warning' : '';
    const tmClass = (tm.className || '').toLowerCase().replace(/\s+/g, '-');
    const sprites = this._getClassSprite(tmClass);
    const isDead = tm.hp <= 0;

    const spriteHtml = tmClass
      ? `<img class="gui-combatant-sprite hd-sprite"
             src="${sprites.hdSrc}" alt="${tm.name}"
             onerror="this.className='gui-combatant-sprite'; this.src='${sprites.regularSrc}'; this.onerror=function(){this.src='${sprites.fallbackSrc}';};">`
      : `<div class="gui-combatant-sprite gui-teammate-placeholder">${(tm.name || '?')[0]}</div>`;

    return `
      <div class="gui-combatant gui-teammate${isDead ? ' defeated' : ''}">
        ${spriteHtml}
        <div class="gui-combatant-info">
          <div class="gui-combatant-name ally-name">${tm.name}</div>
          <div class="gui-combatant-hp-bar">
            <div class="gui-combatant-hp-fill hp-ally ${tmHpClass}" style="width:${tmHpPct}%"></div>
          </div>
        </div>
      </div>
    `;
  }

  _renderMonsterCombatant(name, level, hp, maxHp, isBoss, spriteFile, family) {
    const hpPct = maxHp > 0 ? (hp / maxHp * 100) : 0;
    const hpClass = hpPct < 25 ? 'critical' : hpPct < 50 ? 'warning' : '';
    const isDead = hp <= 0;
    // Try HD monster sprite first, fallback to regular
    const baseName = spriteFile ? spriteFile.replace('.png', '') : '';
    const sprite = spriteFile
      ? `<img class="gui-combatant-sprite hd-sprite monster-sprite"
             src="./assets/monsters-hd/${spriteFile}" alt="${name}"
             onerror="this.className='gui-combatant-sprite monster-sprite'; this.src='./assets/monsters/${spriteFile}'; this.onerror=function(){this.style.display='none';};">`
      : `<div class="gui-combatant-sprite gui-monster-placeholder">${(name || '?')[0]}</div>`;
    return `
      <div class="gui-combatant gui-monster-combatant${isBoss ? ' boss' : ''}${isDead ? ' defeated' : ''}">
        ${sprite}
        <div class="gui-combatant-info">
          <div class="gui-combatant-name monster-name">${name}${isBoss ? ' ★' : ''}</div>
          <div class="gui-combatant-level">Lv.${level}</div>
          <div class="gui-combatant-hp-bar">
            <div class="gui-combatant-hp-fill hp-monster ${hpClass}" style="width:${hpPct}%"></div>
          </div>
          <div class="gui-combatant-hp-text">${hp.toLocaleString()} / ${maxHp.toLocaleString()}</div>
        </div>
      </div>
    `;
  }

  /** Render the full battlefield with party left, monsters right */
  _renderBattlefield(data) {
    this.npcArea.innerHTML = '';

    const battlefield = document.createElement('div');
    battlefield.className = 'gui-battlefield';

    // -- Left: Player party --
    const partyDiv = document.createElement('div');
    partyDiv.className = 'gui-party-side';

    const playerHpPct = data.playerMaxHp > 0 ? (data.playerHp / data.playerMaxHp * 100) : 100;
    const playerHpClass = playerHpPct < 25 ? 'critical' : playerHpPct < 50 ? 'warning' : '';

    let partyHtml = this._renderPlayerCombatant(this.state.playerName, playerHpPct, playerHpClass, true);

    // Add teammates
    const teammates = data.teammates || this.state.combatTeammates || [];
    for (const tm of teammates) {
      partyHtml += this._renderTeammateCombatant(tm);
    }
    partyDiv.innerHTML = partyHtml;
    battlefield.appendChild(partyDiv);

    // -- Center: Divider line --
    const vs = document.createElement('div');
    vs.className = 'gui-vs-divider';
    battlefield.appendChild(vs);

    // -- Right: Monsters --
    const monsterDiv = document.createElement('div');
    monsterDiv.className = 'gui-monster-side';

    if (data.monsters && data.monsters.length > 0) {
      let monstersHtml = '';
      for (const m of data.monsters) {
        const spriteFile = this.monsterSpriteMap[m.family] || '';
        monstersHtml += this._renderMonsterCombatant(m.name, m.level, m.hp, m.maxHp, m.isBoss, spriteFile, m.family);
      }
      monsterDiv.innerHTML = monstersHtml;
    } else if (data.monsterName) {
      // Single monster from combat_start event
      const spriteFile = this.monsterSpriteMap[data.family] || '';
      monsterDiv.innerHTML = this._renderMonsterCombatant(
        data.monsterName, data.monsterLevel,
        data.monsterHp, data.monsterMaxHp,
        data.isBoss, spriteFile, data.family
      );
    }
    battlefield.appendChild(monsterDiv);

    // -- Combat log overlay --
    const logDiv = document.createElement('div');
    logDiv.className = 'gui-combat-log';
    logDiv.id = 'gui-combat-log';
    battlefield.appendChild(logDiv);

    // -- Turn indicator --
    const turnDiv = document.createElement('div');
    turnDiv.className = 'gui-turn-indicator';
    turnDiv.id = 'gui-turn-indicator';
    turnDiv.textContent = 'Combat';
    battlefield.appendChild(turnDiv);

    // Add density class based on max combatants per side
    const partyCount = partyDiv.querySelectorAll('.gui-combatant').length;
    const monsterCount = monsterDiv.querySelectorAll('.gui-combatant').length;
    const maxPerSide = Math.max(partyCount, monsterCount);
    if (maxPerSide >= 5) battlefield.classList.add('dense-5');
    else if (maxPerSide >= 4) battlefield.classList.add('dense-4');
    else if (maxPerSide >= 3) battlefield.classList.add('dense-3');

    this.npcArea.appendChild(battlefield);
  }

  _renderCombatStatus(data) {
    if (!data.monsters || data.monsters.length === 0) return;
    this._renderBattlefield(data);

    // Update player HUD
    if (data.playerHp !== undefined) {
      this._updateHUD({
        hp: data.playerHp, maxHp: data.playerMaxHp,
        mana: data.playerMana, maxMana: data.playerMaxMana
      });
    }
  }

  /** Handle a combat_action event — feed the combat log and show damage numbers */
  _handleCombatAction(data) {
    const { actor, action, target, damage, targetHp, targetMaxHp } = data;
    // Determine log type
    let type = 'info';
    let text = '';
    if (damage > 0) {
      const isHeal = action && (action.toLowerCase().includes('heal') || action.toLowerCase().includes('potion'));
      if (isHeal) {
        type = 'heal';
        text = `${actor} heals ${target} for ${damage.toLocaleString()} HP`;
      } else {
        type = 'damage';
        text = `${actor} ${action || 'attacks'} ${target} for ${damage.toLocaleString()} damage`;
      }
    } else if (action) {
      type = action.toLowerCase().includes('defend') ? 'buff' : 'info';
      text = `${actor} uses ${action}${target ? ` on ${target}` : ''}`;
    } else {
      text = `${actor} attacks ${target}`;
    }
    this._addCombatLog(text, type);
  }

  /** Add entry to combat log */
  _addCombatLog(text, type) {
    const log = document.getElementById('gui-combat-log');
    if (!log) return;
    const entry = document.createElement('div');
    entry.className = `gui-combat-log-entry ${type || 'info'}`;
    entry.textContent = text;
    log.appendChild(entry);
    // Keep scrolled to bottom
    log.scrollTop = log.scrollHeight;
    // Limit entries
    while (log.children.length > 30) {
      log.removeChild(log.firstChild);
    }
  }

  _renderCombatEnd(data) {
    // Keep battlefield visible, overlay the result
    const result = document.createElement('div');
    result.className = 'gui-combat-result';
    const isDefeat = data.outcome && data.outcome.toLowerCase().includes('defeat');
    result.innerHTML = `
      <div class="gui-combat-result-title${isDefeat ? ' defeat' : ''}">${data.outcome}</div>
      <div class="gui-combat-rewards">
        ${data.xpGained ? `<span class="gui-reward-xp">+${data.xpGained.toLocaleString()} XP</span>` : ''}
        ${data.goldGained ? `<span class="gui-reward-gold">+${data.goldGained.toLocaleString()} Gold</span>` : ''}
        ${data.lootName ? `<span class="gui-reward-loot">${data.lootName}</span>` : ''}
      </div>
    `;
    this.npcArea.appendChild(result);

    // Clear ability bar
    this.roomActions.innerHTML = '';

    // Auto-clear after a few seconds
    setTimeout(() => {
      if (result.parentNode) result.remove();
    }, 5000);
  }
}

window.GameUI = GameUI;
