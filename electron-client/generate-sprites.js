#!/usr/bin/env node
// Usurper Reborn — Sprite Generator
// Generates all combat sprites via PixelLab Pixflux API at 256x256 with transparent backgrounds.
// Run: node generate-sprites.js
// Requires: API key in PIXELLAB_API_KEY env var or hardcoded below.

const fs = require('fs');
const path = require('path');
const https = require('https');

const API_KEY = process.env.PIXELLAB_API_KEY || 'd2042722-a791-4ae6-9c31-745a74c49e6f';
const BASE = 'https://api.pixellab.ai/v2';
const ASSETS = path.join(__dirname, 'assets');
// 192px is the sweet spot: transparent backgrounds work reliably, good detail
const SIZE = 192;

// ─── Sprite Definitions ───────────────────

const PLAYER_CLASSES = [
  { name: 'warrior', desc: 'Heavily armored human warrior knight with great sword, red cape, plate armor, battle-scarred, heroic stance facing right. Dark fantasy Darkest Dungeon style.' },
  { name: 'assassin', desc: 'Hooded assassin in dark leather armor, dual daggers drawn, shadowy cloak, lithe build, masked face, facing right. Dark fantasy Darkest Dungeon style.' },
  { name: 'magician', desc: 'Powerful wizard in flowing dark blue robes, glowing arcane staff, long white beard, pointed hat, magical energy, facing right. Dark fantasy Darkest Dungeon style.' },
  { name: 'cleric', desc: 'Female holy cleric in white and gold ornate robes, glowing mace raised, divine aura, holy symbol, facing right. Dark fantasy Darkest Dungeon style.' },
  { name: 'paladin', desc: 'Noble paladin in shining plate armor, longsword and tower shield, golden holy aura, blue tabard, facing right. Dark fantasy Darkest Dungeon style.' },
  { name: 'ranger', desc: 'Forest ranger in green leather and hooded cloak, longbow drawn, quiver of arrows, keen eyes, facing right. Dark fantasy Darkest Dungeon style.' },
  { name: 'barbarian', desc: 'Massive barbarian warrior in fur and bone armor, enormous two-handed battle axe, war paint, wild hair, facing right. Dark fantasy Darkest Dungeon style.' },
  { name: 'bard', desc: 'Charming bard with ornate lute, colorful entertainer outfit, feathered hat, rapier at hip, charismatic, facing right. Dark fantasy Darkest Dungeon style.' },
  { name: 'jester', desc: 'Sinister jester in motley costume, dual trick daggers, painted white face, bell-tipped hat, unsettling grin, facing right. Dark fantasy Darkest Dungeon style.' },
  { name: 'sage', desc: 'Ancient sage scholar in ornate mystic robes, floating ancient tome, crystal-topped staff, arcane symbols, facing right. Dark fantasy Darkest Dungeon style.' },
  { name: 'alchemist', desc: 'Mad alchemist with leather apron, brass goggles, belt of bubbling potion vials, throwing bomb, chemical stains, facing right. Dark fantasy Darkest Dungeon style.' },
  { name: 'mystic-shaman', desc: 'Tribal orc shaman with bone-topped spirit staff, totem necklaces, ritual body paint, spirit wisps, feathered headdress, facing right. Dark fantasy Darkest Dungeon style.' },
  { name: 'tidesworn', desc: 'Ocean warrior in coral and shell armor, barnacle-encrusted trident, seaweed cape, blue-green ocean aura, facing right. Dark fantasy Darkest Dungeon style.' },
  { name: 'wavecaller', desc: 'Sea mage in flowing aqua robes, water magic swirling around hands, pearl-topped staff, ocean mist, facing right. Dark fantasy Darkest Dungeon style.' },
  { name: 'cyclebreaker', desc: 'Time warrior in cracked armor revealing temporal energy beneath, clockwork gauntlets, hourglass chest symbol, facing right. Dark fantasy Darkest Dungeon style.' },
  { name: 'abysswarden', desc: 'Dark prison warden in heavy spiked plate armor, wrapped chains as weapons, glowing containment sigils, ominous dark aura, facing right. Dark fantasy Darkest Dungeon style.' },
  { name: 'voidreaver', desc: 'Void mage in torn dark robes, crackling void energy hands, skull mask, reality distortion around body, facing right. Dark fantasy Darkest Dungeon style.' },
];

const MONSTERS = [
  { name: 'goblin', family: 'Goblinoid', desc: 'Small vicious goblin warrior with rusty jagged dagger, leather scraps, pointy ears, menacing sneer, facing left. Dark fantasy Darkest Dungeon style.' },
  { name: 'skeleton', family: 'Undead', desc: 'Undead skeleton warrior with ancient corroded sword and battered shield, glowing blue eye sockets, tattered armor, facing left. Dark fantasy Darkest Dungeon style.' },
  { name: 'orc', family: 'Orcish', desc: 'Hulking orc berserker with massive battle axe, war paint, tusks, leather and bone armor, rage-filled, facing left. Dark fantasy Darkest Dungeon style.' },
  { name: 'dragon', family: 'Draconic', desc: 'Bipedal dragon warrior with red scales, horns, clawed hands, dark armor plates, fire breath glow, tail, facing left. Dark fantasy Darkest Dungeon style.' },
  { name: 'demon', family: 'Demonic', desc: 'Red-skinned demon with curving horns, folded bat wings, clawed hands, glowing yellow eyes, infernal flames, facing left. Dark fantasy Darkest Dungeon style.' },
  { name: 'giant', family: 'Giant', desc: 'Towering stone giant with rocky gray skin, moss patches, glowing crystal eyes, enormous boulder fists, facing left. Dark fantasy Darkest Dungeon style.' },
  { name: 'wolf-beast', family: 'Beast', desc: 'Bipedal werewolf beast with dark fur, snarling fangs, muscular clawed arms, tattered pants, feral, facing left. Dark fantasy Darkest Dungeon style.' },
  { name: 'elemental', family: 'Elemental', desc: 'Humanoid fire elemental made of living flame and magma, molten rock core, ember eyes, trailing fire, facing left. Dark fantasy Darkest Dungeon style.' },
  { name: 'spider-queen', family: 'Insectoid', desc: 'Spider humanoid with four arms, dark chitin armor, multiple red eyes, venom-dripping fangs, facing left. Dark fantasy Darkest Dungeon style.' },
  { name: 'golem', family: 'Construct', desc: 'Heavy iron golem with glowing blue rune plates, massive metal fists, steam venting from joints, facing left. Dark fantasy Darkest Dungeon style.' },
  { name: 'dark-fairy', family: 'Fey', desc: 'Corrupted dark fey with tattered moth wings, thorny vine crown, sharp claws, glowing violet eyes, facing left. Dark fantasy Darkest Dungeon style.' },
  { name: 'sea-creature', family: 'Aquatic', desc: 'Deep one fish humanoid with blue-green scales, webbed claws, fin head crest, barnacle armor, trident, facing left. Dark fantasy Darkest Dungeon style.' },
  { name: 'fallen-angel', family: 'Celestial', desc: 'Fallen angel in cracked golden armor, two broken white feathered wings, shattered halo, corrupted glowing sword, facing left. Dark fantasy Darkest Dungeon style.' },
  { name: 'shadow', family: 'Shadow', desc: 'Living shadow entity in hooded dark robes, void-black body, glowing purple eyes, wisps of darkness trailing, facing left. Dark fantasy Darkest Dungeon style.' },
  { name: 'wraith', family: 'Aberration', desc: 'Ghostly aberration wraith with tentacle-like tendrils, distorted form, glowing eldritch symbols, cosmic horror, facing left. Dark fantasy Darkest Dungeon style.' },
];

// ─── API Helper ───────────────────────────

function apiCall(endpoint, body) {
  return new Promise((resolve, reject) => {
    const data = JSON.stringify(body);
    const url = new URL(BASE + endpoint);
    const req = https.request({
      hostname: url.hostname,
      path: url.pathname,
      method: 'POST',
      headers: {
        'Authorization': `Bearer ${API_KEY}`,
        'Content-Type': 'application/json',
        'Content-Length': Buffer.byteLength(data),
      },
    }, (res) => {
      let body = '';
      res.on('data', chunk => body += chunk);
      res.on('end', () => {
        try { resolve(JSON.parse(body)); }
        catch (e) { reject(new Error(`Parse error: ${body.substring(0, 200)}`)); }
      });
    });
    req.on('error', reject);
    req.write(data);
    req.end();
  });
}

// ─── Generate Sprite ──────────────────────

async function generateSprite(name, description, outDir, forceRegen) {
  const outPath = path.join(outDir, `${name}.png`);

  // Skip if already exists (unless --force flag is set)
  if (!forceRegen && fs.existsSync(outPath) && fs.statSync(outPath).size > 5000) {
    console.log(`  SKIP ${name} (already exists, ${fs.statSync(outPath).size} bytes)`);
    return true;
  }

  console.log(`  GEN  ${name}...`);
  try {
    const result = await apiCall('/create-image-pixflux', {
      description,
      image_size: { width: SIZE, height: SIZE },
      text_guidance_scale: 12.0,
      detail: 'highly detailed',
      view: 'side',
      no_background: true,
    });

    if (result.image && result.image.base64) {
      const buf = Buffer.from(result.image.base64, 'base64');
      fs.writeFileSync(outPath, buf);
      console.log(`  OK   ${name} (${buf.length} bytes)`);
      return true;
    } else {
      const err = JSON.stringify(result.detail || result).substring(0, 100);
      console.log(`  FAIL ${name}: ${err}`);
      return false;
    }
  } catch (e) {
    console.log(`  ERR  ${name}: ${e.message}`);
    return false;
  }
}

// ─── Main ─────────────────────────────────

async function main() {
  const forceRegen = process.argv.includes('--force');
  const classesOnly = process.argv.includes('--classes');
  const monstersOnly = process.argv.includes('--monsters');
  const singleName = process.argv.find(a => a.startsWith('--name='))?.split('=')[1];

  const classDir = path.join(ASSETS, 'classes-hd');
  const monsterDir = path.join(ASSETS, 'monsters-hd');
  fs.mkdirSync(classDir, { recursive: true });
  fs.mkdirSync(monsterDir, { recursive: true });

  console.log('=== Usurper Reborn Sprite Generator ===');
  console.log(`Size: ${SIZE}x${SIZE}, Model: Pixflux, Transparent BG`);
  if (forceRegen) console.log('MODE: Force regeneration (overwriting existing)');
  if (singleName) console.log(`MODE: Single sprite: ${singleName}`);
  console.log('');

  if (!monstersOnly) {
    const classes = singleName
      ? PLAYER_CLASSES.filter(c => c.name === singleName)
      : PLAYER_CLASSES;

    console.log(`--- Player Classes (${classes.length}) ---`);
    let ok = 0, fail = 0;
    for (const cls of classes) {
      const success = await generateSprite(cls.name, cls.desc, classDir, forceRegen);
      if (success) ok++; else fail++;
      // Small delay to avoid rate limits
      await new Promise(r => setTimeout(r, 1500));
    }
    console.log(`Classes: ${ok} OK, ${fail} failed\n`);
  }

  if (!classesOnly) {
    const monsters = singleName
      ? MONSTERS.filter(m => m.name === singleName)
      : MONSTERS;

    let ok = 0, fail = 0;
    console.log(`--- Monsters (${monsters.length}) ---`);
    for (const mon of monsters) {
      const success = await generateSprite(mon.name, mon.desc, monsterDir, forceRegen);
      if (success) ok++; else fail++;
      await new Promise(r => setTimeout(r, 1500));
    }
    console.log(`Monsters: ${ok} OK, ${fail} failed\n`);
  }

  console.log('Done! HD sprites saved to:');
  console.log(`  Classes: ${classDir}`);
  console.log(`  Monsters: ${monsterDir}`);
  console.log('');
  console.log('Usage:');
  console.log('  node generate-sprites.js                  # Generate missing sprites');
  console.log('  node generate-sprites.js --force          # Regenerate all sprites');
  console.log('  node generate-sprites.js --classes        # Only player classes');
  console.log('  node generate-sprites.js --monsters       # Only monsters');
  console.log('  node generate-sprites.js --name=warrior   # Single sprite by name');
}

main().catch(console.error);
