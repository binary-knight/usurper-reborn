// Usurper Reborn — Pattern Matcher
// Recognizes game UI patterns in parsed ANSI spans and classifies lines
// for styled HTML rendering instead of raw terminal text.

// Line classification types
const LineType = {
  TEXT: 'text',             // Regular narrative/dialogue
  BOX_TOP: 'box_top',      // ╔═══════╗
  BOX_LINE: 'box_line',    // ║ content ║
  BOX_BOTTOM: 'box_bottom',// ╚═══════╝
  BOX_DIVIDER: 'box_div',  // ╠═══════╣
  MENU_ITEM: 'menu_item',  // [A] Attack or A. Attack
  STATUS_BAR: 'status_bar',// HP: 3291/3312 | Gold: 857,550 ...
  COMBAT_HIT: 'combat_hit',// "You hit X for Y damage!"
  COMBAT_MISS: 'combat_miss',
  COMBAT_HEAL: 'combat_heal',
  COMBAT_STATUS: 'combat_status', // Combat status box content
  SEPARATOR: 'separator',  // ════════════ or ──────────
  PROMPT: 'prompt',         // "Your choice:" or "> "
  EMPTY: 'empty',
  HEADER: 'header',         // Centered title text (usually in a box)
  MUD_PROMPT: 'mud_prompt', // [3291hp 100st] Main Street | look
  NPC_ACTIVITY: 'npc_activity', // "Kira Thornbury leans against a wall..."
  LOCATION_DESC: 'location_desc', // "You are standing on the main street..."
  SYSTEM_MSG: 'system_msg', // "[WizNet]", realm enter/leave, divine power
};

// Box-drawing characters used by UIHelper
const BOX_CHARS = {
  topLeft: '╔', topRight: '╗',
  bottomLeft: '╚', bottomRight: '╝',
  horizontal: '═', vertical: '║',
  divLeft: '╠', divRight: '╣',
  thinHoriz: '─', thinVert: '│',
};

class PatternMatcher {
  constructor() {
    this.inBox = false;
    this.inCombat = false;
    this.boxTitle = '';
  }

  // Classify a parsed line (array of spans) into a LineType with metadata
  classify(spans) {
    if (!spans || spans.length === 0) return { type: LineType.EMPTY, spans };

    const fullText = spans.map(s => s.text).join('');
    const trimmed = fullText.trim();

    if (trimmed.length === 0) return { type: LineType.EMPTY, spans };

    // Box drawing patterns
    if (trimmed.startsWith('╔') && trimmed.includes('═')) {
      this.inBox = true;
      this.boxTitle = '';
      return { type: LineType.BOX_TOP, spans, raw: trimmed };
    }

    if (trimmed.startsWith('╚') && trimmed.includes('═')) {
      this.inBox = false;
      const wasTitle = this.boxTitle;
      this.boxTitle = '';
      return { type: LineType.BOX_BOTTOM, spans, raw: trimmed, boxTitle: wasTitle };
    }

    if (trimmed.startsWith('╠') && trimmed.includes('═')) {
      return { type: LineType.BOX_DIVIDER, spans, raw: trimmed };
    }

    if (trimmed.startsWith('║') && trimmed.endsWith('║')) {
      const inner = trimmed.slice(1, -1).trim();

      // Detect box title (centered text, usually in CAPS or mixed case)
      if (this.inBox && !this.boxTitle && inner.length > 0 && !inner.startsWith('[')) {
        this.boxTitle = inner;
        if (inner.includes('COMBAT') || inner.includes('BATTLE')) {
          this.inCombat = true;
        }
      }

      return { type: LineType.BOX_LINE, spans, inner, boxTitle: this.boxTitle, raw: trimmed };
    }

    // Thin separators
    if (/^[═─]{10,}$/.test(trimmed)) {
      return { type: LineType.SEPARATOR, spans, raw: trimmed };
    }

    // Multi-menu lines: [D]Dungeons  [I]Inn  [T]Temple  [O]Old Church
    // Match lines with 2+ [X]Label patterns
    const multiMenuRegex = /\[([A-Z0-9<>\/!@#$%^&*~]+)\]\s*([^\[]*)/g;
    const multiItems = [];
    let mm;
    while ((mm = multiMenuRegex.exec(trimmed)) !== null) {
      const key = mm[1];
      const label = mm[2].trim();
      if (label.length > 0) {
        multiItems.push({ key, label });
      }
    }

    if (multiItems.length >= 2) {
      return {
        type: LineType.MENU_ITEM, spans,
        items: multiItems,
        multi: true,
        raw: trimmed
      };
    }

    // Single menu item: [X] Label
    if (multiItems.length === 1) {
      return {
        type: LineType.MENU_ITEM, spans,
        key: multiItems[0].key,
        label: multiItems[0].label,
        raw: trimmed
      };
    }

    // Screen reader menu: "X. Label"
    const srMenuMatch = trimmed.match(/^([A-Z0-9])\.\s+(.+)/);
    if (srMenuMatch && srMenuMatch[2].length > 2) {
      return {
        type: LineType.MENU_ITEM, spans,
        key: srMenuMatch[1],
        label: srMenuMatch[2],
        raw: trimmed
      };
    }

    // Status bar: HP: X/Y | Gold: Z | ...
    if (/HP:\s*[\d,]+\/[\d,]+/.test(trimmed) && /Gold:\s*[\d,]+/.test(trimmed)) {
      return { type: LineType.STATUS_BAR, spans, raw: trimmed, data: this._parseStatusBar(trimmed) };
    }

    // MUD prompt: [3291hp 100st] Main Street | look
    const mudMatch = trimmed.match(/^\[(\d+)hp\s+(\d+)st\]\s+(.+)/);
    if (mudMatch) {
      return {
        type: LineType.MUD_PROMPT, spans,
        hp: parseInt(mudMatch[1]),
        stamina: parseInt(mudMatch[2]),
        location: mudMatch[3].split('|')[0].trim(),
        raw: trimmed
      };
    }

    // Combat damage: "X damage gets through" or "You hit X for Y damage"
    if (/damage gets through/.test(trimmed) || /\d+ damage vs \d+ defense/.test(trimmed)) {
      return { type: LineType.COMBAT_HIT, spans, raw: trimmed };
    }

    if (/hits? .+ for \d+ damage/.test(trimmed) || /deals? \d+ damage/.test(trimmed)) {
      return { type: LineType.COMBAT_HIT, spans, raw: trimmed };
    }

    if (/dodges?|misses?|grazes?/.test(trimmed) && /attack/.test(trimmed)) {
      return { type: LineType.COMBAT_MISS, spans, raw: trimmed };
    }

    // Healing
    if (/recovers? \d+ HP|heals? for \d+|casts? Cure/.test(trimmed)) {
      return { type: LineType.COMBAT_HEAL, spans, raw: trimmed };
    }

    // Prompt detection
    if (trimmed === 'Your choice:' || trimmed === '>' || trimmed.endsWith(':') && trimmed.length < 30) {
      return { type: LineType.PROMPT, spans, raw: trimmed };
    }

    if (/Press any key/.test(trimmed)) {
      return { type: LineType.PROMPT, spans, raw: trimmed, pressAnyKey: true };
    }

    // NPC activity — names followed by verbs like "leans", "strolls", "watches", "goes about"
    if (/\b(leans|strolls|watches|paces|goes about|spotted|carries|sports|bellows|haggling|arguing|flirting|singing|praying)\b/.test(trimmed)
        && !trimmed.startsWith('You ')) {
      return { type: LineType.NPC_ACTIVITY, spans, raw: trimmed };
    }

    // System messages — realm enter/leave, WizNet, divine power
    if (/has entered the realm|has left the realm|\[WizNet\]|Divine power|entered the realm/.test(trimmed)) {
      return { type: LineType.SYSTEM_MSG, spans, raw: trimmed };
    }

    // Location description — "You are standing", "The evening air", "The morning air"
    if (/^You are standing|^The (evening|morning|afternoon|night|midday) air|^You notice:/.test(trimmed)) {
      return { type: LineType.LOCATION_DESC, spans, raw: trimmed };
    }

    // "and X others going about" — NPC summary line
    if (/and \d+ others? going about/.test(trimmed)) {
      return { type: LineType.NPC_ACTIVITY, spans, raw: trimmed };
    }

    // Default: regular text
    return { type: LineType.TEXT, spans, raw: trimmed };
  }

  _parseStatusBar(text) {
    const data = {};
    const hpMatch = text.match(/HP:\s*([\d,]+)\/([\d,]+)/);
    if (hpMatch) {
      data.hp = parseInt(hpMatch[1].replace(/,/g, ''));
      data.maxHp = parseInt(hpMatch[2].replace(/,/g, ''));
    }
    const goldMatch = text.match(/Gold:\s*([\d,]+)/);
    if (goldMatch) data.gold = parseInt(goldMatch[1].replace(/,/g, ''));
    const levelMatch = text.match(/Level\s+(\d+)/);
    if (levelMatch) data.level = parseInt(levelMatch[1]);
    const staminaMatch = text.match(/Stamina:\s*([\d,]+)\/([\d,]+)/);
    if (staminaMatch) {
      data.stamina = parseInt(staminaMatch[1].replace(/,/g, ''));
      data.maxStamina = parseInt(staminaMatch[2].replace(/,/g, ''));
    }
    const manaMatch = text.match(/Mana:\s*([\d,]+)\/([\d,]+)/);
    if (manaMatch) {
      data.mana = parseInt(manaMatch[1].replace(/,/g, ''));
      data.maxMana = parseInt(manaMatch[2].replace(/,/g, ''));
    }
    return data;
  }

  // Check if we're currently inside a combat context
  get isCombat() { return this.inCombat; }

  // Reset combat state (call on screen clear)
  resetCombat() { this.inCombat = false; }
}

window.PatternMatcher = PatternMatcher;
window.LineType = LineType;
