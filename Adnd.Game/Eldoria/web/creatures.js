// Procedural creatures for outdoor encounters. Every creature faces +z, stands on y = 0 and returns a Group
// whose userData.tick(seconds) plays a small idle animation.
export function createCreature(THREE, name) {
  const spec = classify(name);
  if (spec.weaponChoices) spec.weapon = spec.weaponChoices[Math.floor(Math.random() * spec.weaponChoices.length)];
  const ctx = makeContext(THREE, spec);
  const group = ({
    dragon: buildDragon, quad: buildQuadruped, bird: buildQuadruped, humanoid: buildHumanoid, undead: buildHumanoid,
    insect: buildInsect, spider: buildInsect, serpent: buildSerpent, beholder: buildBeholder, blob: buildBlob,
  }[spec.kind])(ctx);
  // Individual variation so creatures of one kind are not identical: size, build and colour shift.
  const r = () => Math.random() * 2 - 1;
  const k = 1 + r() * 0.1;
  group.scale.set(spec.size * k * (1 + r() * 0.07), spec.size * k * (1 + r() * 0.05), spec.size * k * (1 + r() * 0.07));
  const dh = r() * 0.025, ds = r() * 0.08, dl = r() * 0.07;
  const cloned = new Map();
  const hsl = {};
  group.traverse(o => {
    if (!o.isMesh || Array.isArray(o.material) || !o.material.color) return;
    let m = cloned.get(o.material);
    if (!m) {
      m = o.material.clone();
      m.color.getHSL(hsl);
      if (hsl.s > 0.05 || hsl.l > 0.08) m.color.setHSL((hsl.h + dh + 1) % 1, Math.min(1, Math.max(0, hsl.s + ds)), Math.min(1, Math.max(0, hsl.l + dl)));
      cloned.set(o.material, m);
    }
    o.material = m;
  });
  group.traverse(o => { if (o.isMesh) { o.castShadow = true; o.receiveShadow = true; } });
  return group;
}

const has = (n, ...words) => words.some(w => n.includes(w));

function classify(rawName) {
  const n = (rawName ?? '').toLowerCase();
  const dragonColors = { red: 0xa82820, blue: 0x2a58b0, green: 0x2f8a3a, black: 0x23232b, white: 0xe6ecf0, gold: 0xc9a227, silver: 0xbfc6cc, brass: 0xb5883a, bronze: 0x8c6a30, copper: 0xa8603a };
  if (has(n, 'dragon', 'wyvern', 'wyrm', 'drake')) {
    const key = Object.keys(dragonColors).find(c => n.includes(c));
    return { kind: 'dragon', color: dragonColors[key] ?? 0x4d7a3a, size: has(n, 'young', 'small', 'baby') ? 0.8 : has(n, 'ancient', 'old') ? 1.6 : 1.25, tex: 'scale' };
  }
  if (has(n, 'beholder')) return { kind: 'beholder', color: 0x7a4a8a, size: 1.2, tex: 'skin' };
  if (has(n, 'griffon', 'gryphon', 'hippogriff', 'pegasus')) return { kind: 'bird', color: 0xb08a4a, size: 1.2, tex: 'fur', wings: true, beak: true, mane: false, tail: 'lion' };
  if (has(n, 'eagle', 'hawk', 'harpy', 'vulture', 'bird') || /\b(owls?|rocs?)\b/.test(n)) return { kind: 'bird', color: 0x6a5238, size: 0.8, tex: 'fur', wings: true, beak: true, bipedBird: true };
  if (has(n, 'spider', 'tarantula')) return { kind: 'spider', color: 0x2a2224, size: 0.9, tex: 'skin', legs: 8 };
  if (/\bants?\b/.test(n) || has(n, 'beetle', 'scorpion', 'centipede', 'wasp', 'insect', 'mantis') || /\b(bees?|fly|flies)\b/.test(n)) {
    return { kind: 'insect', color: /\bants?\b/.test(n) ? 0x3a1c12 : 0x2f3a22, size: 0.9, tex: 'skin', legs: 6 };
  }
  if (has(n, 'snake', 'serpent', 'worm', 'eel', 'naga', 'asp', 'viper', 'cobra', 'python', 'hydra')) return { kind: 'serpent', color: has(n, 'green') ? 0x3a7a3a : 0x6a5a2a, size: 1.1, tex: 'scale' };
  if (has(n, 'skeleton', 'zombie', 'ghoul', 'wight', 'wraith', 'ghost', 'mummy', 'undead', 'vampire', 'lich', 'shadow', 'spectre', 'ghast')) {
    const bone = has(n, 'skeleton', 'lich');
    return { kind: 'undead', color: bone ? 0xd8d2bc : 0x7d8a72, cloth: 0x2a2a30, size: 1, tex: bone ? 'bone' : 'skin', bone, eyes: 0x60ff90, h: 1.8, bulk: bone ? 0.55 : 0.9, weapon: bone ? 'sword' : 'none', hunch: !bone, helm: bone ? 'nasal' : null, shield: bone ? 'round' : null, shieldColor: 0x5a4a3a, metal: 0x7a756a, armor: bone ? null : 'leather' };
  }
  if (has(n, 'wolf', 'worg', 'jackal', 'hyena') || /\b(dogs?|foxe?s?)\b/.test(n)) return { kind: 'quad', color: has(n, 'worg') ? 0x3a3530 : has(n, 'dire') ? 0x4a4440 : 0x80796f, size: has(n, 'worg', 'dire') ? 1.3 : 1, tex: 'fur', len: 1.3, h: 0.7, r: 0.38, headR: 0.27, snout: 0.45, ears: 'point', tail: 'bush', fangs: true };
  if (/\bbears?\b/.test(n)) return { kind: 'quad', color: has(n, 'black') ? 0x24201c : has(n, 'polar', 'white', 'cave') ? 0xe0dcd0 : 0x5e3d24, size: has(n, 'cave') ? 1.5 : 1.25, tex: 'fur', len: 1.5, h: 0.75, r: 0.62, headR: 0.36, snout: 0.3, ears: 'round', tail: 'stub', fangs: true, hump: true };
  if (has(n, 'boar', 'pig', 'warthog')) return { kind: 'quad', color: 0x3c2a20, size: 1, tex: 'fur', len: 1.3, h: 0.55, r: 0.5, headR: 0.3, snout: 0.4, ears: 'point', tail: 'stub', tusks: true, hump: true };
  if (has(n, 'lion', 'tiger', 'panther', 'leopard', 'lynx', 'cougar', 'puma') || /\bcats?\b/.test(n)) return { kind: 'quad', color: has(n, 'panther') ? 0x1c1c22 : has(n, 'tiger') ? 0xc8782a : 0xc19a55, size: 1.1, tex: 'fur', len: 1.5, h: 0.65, r: 0.38, headR: 0.3, snout: 0.2, ears: 'round', tail: 'long', fangs: true, mane: has(n, 'lion'), stripes: has(n, 'tiger') };
  if (/\b(ox|oxen)\b/.test(n) || has(n, 'horse', 'mule', 'pony', 'bull', 'cow', 'stag', 'deer', 'elk', 'moose', 'antelope', 'goat', 'sheep', 'herd', 'camel', 'buffalo', 'bison', 'unicorn')) return { kind: 'quad', color: has(n, 'deer', 'stag', 'elk') ? 0x8a5a32 : 0x6b4a2e, size: 1.2, tex: 'fur', len: 1.7, h: 1.05, r: 0.46, headR: 0.3, snout: 0.5, ears: 'point', tail: 'long', horns: has(n, 'stag', 'elk', 'moose', 'bull', 'ox', 'goat', 'buffalo', 'bison', 'deer') ? 'antlers' : null, neck: 0.8 };
  if (/\brats?\b/.test(n) || has(n, 'badger', 'weasel', 'wolverine', 'otter', 'beaver', 'rodent', 'ferret', 'rabbit', 'hare')) return { kind: 'quad', color: 0x5a4c40, size: 0.8, tex: 'fur', len: 1, h: 0.35, r: 0.36, headR: 0.22, snout: 0.35, ears: 'round', tail: 'stub', fangs: true };
  if (has(n, 'lizard', 'crocodile', 'alligator', 'basilisk', 'salamander', 'dinosaur', 'saurus', 'rex', 'turtle')) return { kind: 'quad', color: 0x5a7a3a, size: 1.3, tex: 'scale', len: 1.9, h: 0.35, r: 0.4, headR: 0.3, snout: 0.8, ears: null, tail: 'croc', fangs: true, sprawl: true };
  if (has(n, 'blink dog', 'dog')) return { kind: 'quad', color: 0x9a7a4a, size: 1, tex: 'fur', len: 1.2, h: 0.6, r: 0.33, headR: 0.25, snout: 0.4, ears: 'point', tail: 'bush' };
  if (has(n, 'ogre', 'troll', 'giant', 'cyclops', 'ettin')) {
    const troll = has(n, 'troll'), giant = has(n, 'giant');
    return { kind: 'humanoid', color: troll ? 0x4f7a3a : giant ? 0xc89a78 : 0xa08a52, cloth: 0x5a4028, size: giant ? 1.8 : 1.4, tex: 'skin', h: 2.1, bulk: troll ? 0.95 : 1.35, weapon: 'none', weaponChoices: troll ? ['bigclub', 'bigclub', 'stone', 'bone', 'none', 'none'] : giant ? ['bigclub', 'stone', 'bigclub', 'none'] : ['club', 'bigclub', 'stone', 'bone', 'club', 'none'], tusks: !giant && !troll, brow: true, hunch: true, eyes: 0xffd040, hair: giant ? 0x4a2e18 : null, warts: troll, longArms: troll, claws: troll, bareLegs: true, belly: !troll, armor: giant ? 'fur' : null, smallEyes: true, longNose: troll, pointyEars: troll, earSize: 0.8 };
  }
  if (has(n, 'bugbear')) return { kind: 'humanoid', color: 0x8a5a30, cloth: 0x5a3e28, size: 1.25, tex: 'fur', h: 1.9, bulk: 1.15, weapon: 'sword', shield: null, armor: 'studded', armorColor: 0x6a4a2a, pointyEars: true, earSize: 1.5, fangs: true, longNose: true, brow: true, eyes: 0xf0e8c0, hunch: true, claws: true, bareLegs: true, longArms: true };
  if (/\bkobolds?\b/.test(n) || has(n, 'lizard man', 'lizardman', 'lizard folk')) return { kind: 'humanoid', color: 0x8a4a30, cloth: 0x4a3a26, size: 0.8, tex: 'scale', h: 1.3, bulk: 0.85, weapon: 'spear', snout: 'reptile', eyes: 0xffd040, hunch: true, claws: true, bareLegs: true, armor: 'leather', shield: 'small' };
  if (has(n, 'goblin') && !has(n, 'hobgoblin')) return { kind: 'humanoid', color: 0x6c8a3a, cloth: 0x4a3a26, size: 0.85, tex: 'skin', h: 1.3, bulk: 0.85, weapon: 'sword', pointyEars: true, earSize: 1.6, longNose: true, fangs: true, eyes: 0xffe040, hunch: true, armor: 'leather', helm: 'leather', shield: 'small', shieldColor: 0x6a4a2a, claws: true };
  if (has(n, 'hobgoblin')) return { kind: 'humanoid', color: 0xa06a3a, cloth: 0x7a2a22, size: 1.1, tex: 'skin', h: 1.8, bulk: 1.1, weapon: 'sword', pointyEars: true, brow: true, fangs: true, eyes: 0xff5030, armor: 'chain', helm: 'nasal', shield: 'round', shieldColor: 0x8a2a22 };
  if (/\borcs?\b/.test(n) || has(n, 'orcish')) return { kind: 'humanoid', color: 0x5a7a40, cloth: 0x3a2e24, size: 1.1, tex: 'skin', h: 1.8, bulk: 1.15, weapon: 'spear', snout: 'pig', tusks: true, pointyEars: true, brow: true, eyes: 0xff5030, hunch: true, armor: 'studded', armorColor: 0x4a3a2a, helm: 'spiked', shield: 'round', shieldColor: 0x7a5a30, claws: true };
  if (has(n, 'gnoll')) return { kind: 'humanoid', color: 0x9a7a4a, cloth: 0x4a3a26, size: 1.15, tex: 'fur', h: 1.9, bulk: 1.05, weapon: 'spear', snout: 'canine', canineEars: true, mane: 0x4a3a28, eyes: 0xffd040, hunch: true, armor: 'leather', armorColor: 0x5a4a30, shield: 'round', shieldColor: 0x6a4a2a, claws: true, bareLegs: true, fangs: true };
  if (has(n, 'minotaur')) return { kind: 'humanoid', color: 0x6a4a30, cloth: 0x3a2a1a, size: 1.35, tex: 'fur', h: 2.0, bulk: 1.3, weapon: 'axe', snout: 'bull', horns: true, canineEars: false, pointyEars: true, earSize: 0.8, eyes: 0xff4020, hunch: true, bareLegs: true, claws: true, armor: 'leather', armorColor: 0x3a2a1a };
  if (has(n, 'humanoid', 'ghast')) return { kind: 'humanoid', color: 0x8a6a4a, cloth: 0x3a2e24, size: 1.1, tex: 'skin', h: 1.8, bulk: 1.15, weapon: 'club', brow: true, eyes: 0xff5030, hunch: true, armor: 'leather' };
  if (has(n, 'dwarf')) return { kind: 'humanoid', color: 0xd09a78, cloth: 0x5a3a28, size: 0.85, tex: 'skin', h: 1.4, bulk: 1.3, weapon: 'axe', hair: 0x8a4a22, brow: true, armor: 'plate', helm: 'nasal', shield: 'round', shieldColor: 0x3a5a8a };
  if (has(n, 'elf')) return { kind: 'humanoid', color: 0xe0b898, cloth: 0x2a5a3a, size: 1, tex: 'skin', h: 1.8, bulk: 0.85, weapon: 'scimitar', hair: 0xd8c070, pointyEars: true, earSize: 1.1, armor: 'chain', eyes: 0x30a050 };
  if (has(n, 'bandit', 'brigand', 'thief', 'nomad', 'dervish', 'tribesman', 'berserker', 'cavem')) return { kind: 'humanoid', color: 0xc89870, cloth: 0x5a4a3a, size: 1, tex: 'skin', h: 1.75, bulk: 1.05, weapon: has(n, 'dervish') ? 'scimitar' : 'sword', weaponChoices: has(n, 'cavem') ? ['bigclub', 'club', 'stone', 'none'] : null, hair: 0x2a1a10, armor: 'leather', helm: has(n, 'berserker') ? 'spiked' : null, shield: has(n, 'berserker') ? null : 'small', eyes: 0x202020, brow: has(n, 'cavem') };
  if (/\b(man|men|human|merchant|mage|cleric|fighter|pilgrim|knight|soldier|guard|wizard|priest|character)s?\b/.test(n) || has(n, 'lvl')) return { kind: 'humanoid', color: 0xd8a888, cloth: 0x6a3030, size: 1, tex: 'skin', h: 1.75, bulk: 1, weapon: 'sword', hair: 0x3a2a1a, eyes: 0x202020, armor: 'chain', helm: 'nasal', shield: 'round', shieldColor: 0x3a4a7a };  return { kind: 'blob', color: hashColor(n), size: 1, tex: 'skin' };
}

function hashColor(n) {
  let h = 0;
  for (const c of n) h = (h * 31 + c.charCodeAt(0)) >>> 0;
  return `hsl(${h % 360}, 45%, 38%)`;
}

function makeContext(THREE, spec) {
  const textures = {};
  const cache = {};
  const rnd = (() => { let s = 7; return () => (s = (s * 16807) % 2147483647) / 2147483647; })();

  const tinted = (hex, kind) => {
    const key = `${kind}:${hex}`;
    if (textures[key]) return textures[key];
    const base = new THREE.Color(hex);
    const c = document.createElement('canvas');
    c.width = c.height = 256;
    const g = c.getContext('2d');
    g.fillStyle = `#${base.getHexString()}`;
    g.fillRect(0, 0, 256, 256);
    const shade = (f, a) => {
      const col = base.clone().multiplyScalar(f);
      return `rgba(${(col.r * 255) | 0},${(col.g * 255) | 0},${(col.b * 255) | 0},${a})`;
    };
    if (kind === 'fur') {
      for (let i = 0; i < 5000; i++) {
        g.strokeStyle = shade(0.6 + rnd() * 0.8, 0.5);
        g.lineWidth = 1;
        const x = rnd() * 256, y = rnd() * 256;
        g.beginPath(); g.moveTo(x, y); g.lineTo(x + (rnd() - 0.5) * 4, y + 6 + rnd() * 8); g.stroke();
      }
    } else if (kind === 'scale') {
      for (let y = 0; y < 256; y += 14) {
        for (let x = (y / 14) % 2 ? 0 : 7; x < 256; x += 14) {
          g.fillStyle = shade(0.8 + rnd() * 0.4, 1);
          g.beginPath(); g.arc(x, y, 8, 0, Math.PI); g.fill();
          g.strokeStyle = shade(0.5, 0.7); g.lineWidth = 1.5; g.stroke();
        }
      }
    } else if (kind === 'bone') {
      for (let i = 0; i < 900; i++) { g.fillStyle = shade(0.7 + rnd() * 0.5, 0.3); g.fillRect(rnd() * 256, rnd() * 256, 2 + rnd() * 6, 1); }
    } else {
      for (let i = 0; i < 2500; i++) { g.fillStyle = shade(0.75 + rnd() * 0.5, 0.25); g.fillRect(rnd() * 256, rnd() * 256, 2, 2); }
    }
    const t = new THREE.CanvasTexture(c);
    t.colorSpace = THREE.SRGBColorSpace;
    t.wrapS = t.wrapT = THREE.RepeatWrapping;
    t.anisotropy = 4;
    textures[key] = t;
    return t;
  };

  const mat = (hex, kind = spec.tex, extra = {}) => {
    const key = `${hex}:${kind}:${JSON.stringify(extra)}`;
    if (cache[key]) return cache[key];
    const material = new THREE.MeshStandardMaterial({
      map: tinted(hex, kind), roughness: kind === 'scale' ? 0.5 : 0.85, metalness: kind === 'scale' ? 0.2 : 0, ...extra,
    });
    // Limbs are plain tubes whose UVs would stretch the pattern, so they use the same colour without the map.
    material.userData.plain = new THREE.MeshStandardMaterial({ color: hex, roughness: material.roughness, metalness: material.metalness });
    return (cache[key] = material);
  };
  const flat = (hex, extra = {}) => new THREE.MeshStandardMaterial({ color: hex, roughness: 0.7, ...extra });

  const V = (x, y, z) => new THREE.Vector3(x, y, z);

  const ellipsoid = (rx, ry, rz, m, pos) => {
    const mesh = new THREE.Mesh(new THREE.SphereGeometry(1, 28, 20), m);
    mesh.scale.set(rx, ry, rz);
    if (pos) mesh.position.copy(pos);
    return mesh;
  };

  // Tapered limb between two points (radius r1 at a, r2 at b).
  const limb = (a, b, r1, r2, m) => {
    const dir = b.clone().sub(a);
    const mesh = new THREE.Mesh(new THREE.CylinderGeometry(r2, r1, dir.length(), 14, 1), m.userData.plain ?? m);
    mesh.position.copy(a).addScaledVector(dir, 0.5);
    mesh.quaternion.setFromUnitVectors(V(0, 1, 0), dir.normalize());
    return mesh;
  };

  const cone = (r, h, m, pos, rot) => {
    const mesh = new THREE.Mesh(new THREE.ConeGeometry(r, h, 12), m);
    mesh.position.copy(pos);
    if (rot) mesh.rotation.set(rot.x, rot.y, rot.z);
    return mesh;
  };

  const eyes = (parent, x, y, z, r, hex) => {
    for (const s of [-1, 1]) {
      const white = new THREE.Mesh(new THREE.SphereGeometry(r, 12, 10), flat(hex, { emissive: hex, emissiveIntensity: 0.6 }));
      white.position.set(s * x, y, z);
      const pupil = new THREE.Mesh(new THREE.SphereGeometry(r * 0.45, 8, 6), flat(0x050505));
      pupil.position.set(s * x, y, z + r * 0.75);
      parent.add(white, pupil);
    }
  };

  return { THREE, spec, mat, flat, V, ellipsoid, limb, cone, eyes, rnd };
}

function legPair(ctx, g, m, x, zHip, hipY, kneeBack, len, r, clawMat) {
  const { V, limb, ellipsoid } = ctx;
  const out = [];
  for (const s of [-1, 1]) {
    const hip = V(s * x, hipY, zHip);
    const knee = V(s * x, hipY - len * 0.5, zHip - kneeBack);
    const paw = V(s * x, 0.08, zHip + kneeBack * 0.4);
    g.add(limb(hip, knee, r, r * 0.75, m), limb(knee, paw, r * 0.75, r * 0.55, m));
    g.add(ellipsoid(r * 0.8, r * 0.45, r * 1.25, clawMat ?? m, V(s * x, 0.1, paw.z + r * 0.4)));
    if (ctx.spec.fangs) {
      for (let c = -1; c <= 1; c++) g.add(ctx.cone(r * 0.12, r * 0.55, ctx.flat(0xe8e2d0), V(s * x + c * r * 0.4, 0.07, paw.z + r * 1.6), { x: Math.PI / 2, y: 0, z: 0 }));
    }
    out.push(hip);
  }
  return out;
}

function buildQuadruped(ctx) {
  const { THREE, spec, mat, flat, V, ellipsoid, limb, cone, eyes } = ctx;
  const g = new THREE.Group();
  const body = new THREE.Group();
  g.add(body);
  const p = { len: 1.4, h: 0.6, r: 0.4, headR: 0.28, snout: 0.35, neck: 0.5, ...spec };
  const m = mat(p.color);
  const dark = flat(0x161212);
  const belly = mat(new THREE.Color(p.color).multiplyScalar(1.25).getHex());
  const cy = p.h + p.r * 0.9;

  body.add(ellipsoid(p.r, p.r * 0.95, p.len * 0.55, m, V(0, cy, 0)));
  body.add(ellipsoid(p.r * 1.05, p.r * 1.05, p.len * 0.3, m, V(0, cy + (p.hump ? 0.1 : 0), p.len * 0.28)));
  body.add(ellipsoid(p.r * 0.95, p.r * 0.9, p.len * 0.28, m, V(0, cy, -p.len * 0.3)));
  body.add(ellipsoid(p.r * 0.7, p.r * 0.5, p.len * 0.4, belly, V(0, cy - p.r * 0.45, 0)));

  const sp = p.sprawl ? 1.5 : 1;
  const hips = legPair(ctx, g, m, p.r * 0.7 * sp, -p.len * 0.34, cy - p.r * 0.2, 0.18, p.h * 1.1, p.r * 0.36, dark);
  legPair(ctx, g, m, p.r * 0.7 * sp, p.len * 0.34, cy - p.r * 0.2, -0.08, p.h * 1.1, p.r * 0.32, dark);
  void hips;

  // Neck and head.
  const neckBase = V(0, cy + p.r * 0.25, p.len * 0.42);
  const headC = V(0, cy + p.r * 0.35 + p.neck * 0.45, p.len * 0.42 + p.neck * 0.8 + p.headR * 0.8);
  body.add(limb(neckBase, headC, p.r * 0.75, p.headR * 0.8, m));
  const head = new THREE.Group();
  head.position.copy(headC);
  head.rotation.x = 0.1;
  head.add(ellipsoid(p.headR, p.headR * 0.9, p.headR * 1.1, m));
  const snoutLen = p.snout;
  if (p.beak) {
    const beakMat = flat(0xd8b040, { roughness: 0.4 });
    const upper = cone(p.headR * 0.45, snoutLen * 0.9, beakMat, V(0, -0.02, p.headR + snoutLen * 0.4), { x: Math.PI / 2 + 0.25, y: 0, z: 0 });
    head.add(upper);
  } else {
    const sn = limb(V(0, -p.headR * 0.1, p.headR * 0.5), V(0, -p.headR * 0.28, p.headR + snoutLen), p.headR * 0.62, p.headR * 0.4, m);
    head.add(sn);
    head.add(ellipsoid(p.headR * 0.28, p.headR * 0.2, p.headR * 0.25, dark, V(0, -p.headR * 0.2, p.headR + snoutLen + 0.02)));
    head.add(ellipsoid(p.headR * 0.38, p.headR * 0.18, snoutLen * 0.55, belly, V(0, -p.headR * 0.5, p.headR + snoutLen * 0.45)));
    if (p.fangs) {
      for (const s of [-1, 1]) {
        head.add(cone(0.025, 0.14, flat(0xf2ecd8), V(s * p.headR * 0.28, -p.headR * 0.72, p.headR + snoutLen * 0.75), { x: Math.PI, y: 0, z: 0 }));
      }
    }
    if (p.tusks) {
      for (const s of [-1, 1]) {
        head.add(cone(0.05, 0.3, flat(0xece4c8), V(s * p.headR * 0.5, -p.headR * 0.45, p.headR + snoutLen * 0.6), { x: -0.5, y: 0, z: -s * 0.35 }));
      }
    }
  }
  eyes(head, p.headR * 0.55, p.headR * 0.28, p.headR * 0.72, 0.045, p.beak ? 0xffd040 : 0xf0c040);
  for (const s of [-1, 1]) {
    if (p.ears === 'point') head.add(cone(p.headR * 0.28, p.headR * 0.8, m, V(s * p.headR * 0.6, p.headR * 0.95, -p.headR * 0.2), { x: -0.15, y: 0, z: -s * 0.3 }));
    else if (p.ears === 'round') head.add(ellipsoid(p.headR * 0.28, p.headR * 0.3, p.headR * 0.12, m, V(s * p.headR * 0.65, p.headR * 0.8, -p.headR * 0.2)));
    if (p.horns === 'antlers') {
      let prev = V(s * p.headR * 0.4, p.headR * 0.8, -0.05);
      for (let i = 1; i <= 4; i++) {
        const next = V(s * (p.headR * 0.4 + i * 0.12), p.headR * 0.8 + i * 0.2, -0.05 - i * 0.07);
        head.add(limb(prev, next, 0.035, 0.025, flat(0xcbb98a)));
        if (i > 1) head.add(limb(next, next.clone().add(V(s * 0.1, 0.2, 0.12)), 0.025, 0.012, flat(0xcbb98a)));
        prev = next;
      }
    }
  }
  body.add(head);

  if (p.mane) body.add(ellipsoid(p.r * 0.95, p.r * 0.95, p.r * 0.7, mat(0x6a4a22), V(0, cy + p.r * 0.55, p.len * 0.5)));
  if (p.stripes) {
    for (let i = 0; i < 6; i++) {
      const z = -p.len * 0.4 + i * p.len * 0.17;
      const stripe = ellipsoid(p.r * 1.02, p.r * 0.98, 0.04, flat(0x1a1410), V(0, cy + 0.01, z));
      body.add(stripe);
    }
  }

  // Tail.
  const tailBase = V(0, cy + p.r * 0.1, -p.len * 0.55);
  let tailShape = { stub: [V(0, cy + 0.15, -p.len * 0.75)], bush: [V(0, cy + 0.05, -p.len * 0.85), V(0, cy - 0.15, -p.len * 1.15)], long: [V(0, cy - 0.1, -p.len * 0.9), V(0, cy + 0.1, -p.len * 1.3), V(0, cy + 0.35, -p.len * 1.5)], lion: [V(0, cy - 0.1, -p.len * 0.9), V(0, cy + 0.1, -p.len * 1.3)], croc: [V(0, cy - 0.1, -p.len * 0.8), V(0, cy - 0.25, -p.len * 1.2), V(0.12, cy - 0.3, -p.len * 1.6)] }[p.tail] ?? [];
  let prev = tailBase, r = p.r * 0.3;
  for (const pt of tailShape) {
    body.add(limb(prev, pt, r, r * (p.tail === 'bush' ? 1.3 : 0.7), m));
    prev = pt; r *= p.tail === 'bush' ? 1 : 0.7;
  }

  if (p.wings) {
    body.userData.wings = buildWings(ctx, cy + p.r * 0.5, p.len * 0.25, 1.5 + p.len * 0.4, p.color, true);
    body.add(...body.userData.wings);
  }
  if (p.hump) body.add(ellipsoid(p.r * 0.9, p.r * 0.5, p.len * 0.25, m, V(0, cy + p.r * 0.75, p.len * 0.2)));

  g.userData.tick = t => {
    body.position.y = Math.sin(t * 2) * 0.012;
    head.rotation.y = Math.sin(t * 0.9) * 0.25;
    head.rotation.x = 0.1 + Math.sin(t * 1.7) * 0.05;
    if (body.userData.wings) body.userData.wings.forEach((w, i) => { w.rotation.z = (i ? -1 : 1) * (0.2 + Math.sin(t * 2.2) * 0.12); });
  };
  return g;
}

// Returns two wing groups (right/left). Membrane is a triangle fan between finger bones.
function buildWings(ctx, y, z, span, color, feathered) {
  const { THREE, flat, V, limb } = ctx;
  const wings = [];
  for (const s of [-1, 1]) {
    const w = new THREE.Group();
    w.position.set(s * 0.25, y, z);
    const shoulder = V(0, 0, 0);
    const elbow = V(s * span * 0.35, span * 0.2, -0.1);
    const tips = [V(s * span, span * 0.15, -span * 0.15), V(s * span * 0.9, span * 0.05, -span * 0.5), V(s * span * 0.6, -0.1, -span * 0.75), V(s * span * 0.2, -0.15, -span * 0.55)];
    const boneM = flat(feathered ? 0x8a6a3a : new THREE.Color(color).multiplyScalar(0.6).getHex());
    const membrane = flat(feathered ? new THREE.Color(color).multiplyScalar(0.9).getHex() : new THREE.Color(color).multiplyScalar(0.75).getHex(), { side: THREE.DoubleSide, roughness: 0.7 });
    w.add(limb(shoulder, elbow, 0.07, 0.05, boneM));
    const verts = [];
    const fan = [elbow, ...tips, V(0, -0.1, -0.35)];
    for (const tip of tips) w.add(limb(elbow, tip, 0.04, 0.015, boneM));
    for (let i = 0; i < fan.length - 1; i++) verts.push(...fan[0].toArray(), ...fan[i + 1].toArray(), ...(fan[i + 2] ?? fan[1]).toArray());
    for (let i = 1; i < fan.length - 1; i++) verts.push(...shoulder.toArray(), ...fan[i].toArray(), ...fan[i + 1].toArray());
    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.Float32BufferAttribute(verts, 3));
    geo.computeVertexNormals();
    w.add(new THREE.Mesh(geo, membrane));
    w.userData.side = s;
    wings.push(w);
  }
  return wings;

}

function buildDragon(ctx) {
  const { THREE, spec, mat, flat, V, ellipsoid, limb, cone, eyes } = ctx;
  const g = new THREE.Group();
  const body = new THREE.Group();
  g.add(body);
  const m = mat(spec.color);
  const belly = mat(new THREE.Color(spec.color).lerp(new THREE.Color(0xd8c890), 0.55).getHex());
  const horn = flat(0xd8d0b8, { roughness: 0.4 });
  const dark = flat(0x141010);
  const cy = 1.5;

  body.add(ellipsoid(0.85, 0.8, 1.6, m, V(0, cy, 0)));
  body.add(ellipsoid(0.7, 0.5, 1.4, belly, V(0, cy - 0.4, 0)));
  body.add(ellipsoid(0.8, 0.75, 0.7, m, V(0, cy + 0.05, 1.0)));

  // Legs with claws.
  for (const [zz, size] of [[0.95, 0.24], [-0.95, 0.3]]) {
    for (const s of [-1, 1]) {
      const hip = V(s * 0.75, cy - 0.2, zz);
      const knee = V(s * 0.95, 0.75, zz - 0.25);
      const foot = V(s * 0.95, 0.08, zz + 0.2);
      g.add(limb(hip, knee, size, size * 0.7, m), limb(knee, foot, size * 0.7, size * 0.5, m));
      g.add(ellipsoid(size * 0.75, 0.1, size * 1.4, m, V(s * 0.95, 0.1, zz + 0.35)));
      for (let c = -1; c <= 1; c++) g.add(cone(0.035, 0.18, horn, V(s * 0.95 + c * 0.1, 0.1, zz + 0.62), { x: Math.PI / 2, y: 0, z: 0 }));
    }
  }

  // Neck rising to the head.
  const neck = [V(0, cy + 0.3, 1.4), V(0, cy + 0.75, 1.85), V(0, cy + 1.25, 2.1), V(0, cy + 1.65, 2.3)];
  let prev = neck[0], r = 0.5;
  for (let i = 1; i < neck.length; i++) {
    body.add(ellipsoid(r, r, r, m, prev));
    body.add(limb(prev, neck[i], r, r * 0.78, m));
    prev = neck[i]; r *= 0.78;
  }
  const head = new THREE.Group();
  head.position.copy(prev).add(V(0, 0.1, 0.1));
  head.add(ellipsoid(0.3, 0.27, 0.42, m));
  head.add(limb(V(0, -0.02, 0.2), V(0, -0.1, 0.85), 0.24, 0.15, m));
  head.add(limb(V(0, -0.2, 0.1), V(0, -0.22, 0.8), 0.14, 0.1, belly));
  head.add(ellipsoid(0.1, 0.07, 0.07, dark, V(0.07, -0.05, 0.88)));
  head.add(ellipsoid(0.1, 0.07, 0.07, dark, V(-0.07, -0.05, 0.88)));
  for (const s of [-1, 1]) {
    for (let i = 0; i < 4; i++) head.add(cone(0.018, 0.1, flat(0xf4eedc), V(s * 0.1, -0.15, 0.3 + i * 0.13), { x: Math.PI, y: 0, z: 0 }));
    head.add(cone(0.06, 0.55, horn, V(s * 0.17, 0.3, -0.25), { x: -1.1, y: 0, z: -s * 0.25 }));
    head.add(cone(0.035, 0.25, horn, V(s * 0.32, 0.05, -0.1), { x: 0, y: 0, z: -s * 1.2 }));
  }
  eyes(head, 0.2, 0.15, 0.25, 0.055, 0xffb020);
  body.add(head);

  // Back spines and tail.
  for (let i = 0; i < 9; i++) {
    const z = 1.5 - i * 0.35;
    body.add(cone(0.07, 0.25 + (i < 4 ? 0.1 : 0), horn, V(0, cy + 0.85 - (i > 5 ? (i - 5) * 0.12 : 0), z), { x: -0.2, y: 0, z: 0 }));
  }
  let tp = V(0, cy, -1.4), tr = 0.55;
  for (let i = 1; i <= 8; i++) {
    const next = V(Math.sin(i * 0.6) * 0.35, cy - 0.15 * i + (i > 5 ? 0.1 * (i - 5) : 0), -1.4 - i * 0.55);
    body.add(ellipsoid(tr, tr, tr, m, tp));
    body.add(limb(tp, next, tr, tr * 0.8, m));
    if (i % 2 === 0) body.add(cone(tr * 0.35, tr * 1.2, horn, tp.clone().add(V(0, tr * 0.9, 0))));
    tp = next; tr *= 0.8;
  }

  const wings = buildWings(ctx, cy + 0.6, 0.6, 3.6, new THREE.Color(spec.color).multiplyScalar(0.9).getHex(), false);
  wings.forEach(w => body.add(w));

  g.userData.tick = t => {
    body.position.y = Math.sin(t * 1.6) * 0.03;
    head.rotation.y = Math.sin(t * 0.7) * 0.3;
    head.rotation.x = Math.sin(t * 1.3) * 0.07;
    wings.forEach((w, i) => { w.rotation.z = (i ? -1 : 1) * (0.25 + Math.sin(t * 1.8) * 0.25); });
  };
  return g;
}

function buildHumanoid(ctx) {
  const { THREE, spec, mat, flat, V, ellipsoid, limb, cone, eyes } = ctx;
  const g = new THREE.Group();
  const body = new THREE.Group();
  g.add(body);
  const H = spec.h, b = spec.bulk;
  const skin = mat(spec.color);
  const cloth = mat(spec.cloth ?? 0x4a3a2a, 'skin');
  const leather = mat(spec.armorColor ?? 0x5e3e24, 'skin');
  const metal = flat(spec.metal ?? 0x9aa2aa, { metalness: 0.85, roughness: 0.35 });
  const dark = flat(0x2a2018);
  const wood = flat(0x6a4a2a, { roughness: 0.9 });
  const legLen = H * 0.46, torsoH = H * 0.34;
  const hipY = legLen;
  const lean = spec.hunch ? 0.18 : 0;
  const armK = spec.longArms ? 1.3 : 1;
  const clawMat = flat(0x1c1612);

  // Legs with boots or bare clawed feet, and knee guards for armoured creatures.
  const legPivots = [];
  for (const s of [-1, 1]) {
    const hip = V(s * 0.13 * b, hipY, 0), knee = V(s * 0.14 * b, hipY * 0.5, 0.06), foot = V(s * 0.14 * b, 0.07, 0.02);
    const legMat = spec.bone ? skin : spec.bareLegs ? skin : cloth;
    // The leg hangs from a hip pivot and the shin from a knee pivot, so fighters can kick.
    const hipPivot = new THREE.Group(); hipPivot.position.copy(hip);
    const hipInner = new THREE.Group(); hipInner.position.copy(hip).negate();
    const kneePivot = new THREE.Group(); kneePivot.position.copy(knee);
    const kneeInner = new THREE.Group(); kneeInner.position.copy(knee).negate();
    body.add(hipPivot); hipPivot.add(hipInner); hipInner.add(kneePivot); kneePivot.add(kneeInner);
    hipInner.add(limb(hip, knee, 0.1 * b, 0.075 * b, legMat));
    kneeInner.add(limb(knee, foot, 0.075 * b, 0.055 * b, spec.bareLegs ? skin : leather));
    hipInner.add(ellipsoid(0.085 * b, 0.085 * b, 0.085 * b, spec.armor === 'plate' ? metal : legMat, knee));
    kneeInner.add(ellipsoid(0.075 * b, 0.06, 0.15 * b, spec.bareLegs ? skin : dark, V(s * 0.14 * b, 0.06, 0.09)));
    if (spec.claws || spec.bareLegs) {
      for (let c = -1; c <= 1; c++) kneeInner.add(cone(0.014, 0.07, clawMat, V(s * 0.14 * b + c * 0.035, 0.05, 0.25 * b), { x: Math.PI / 2, y: 0, z: 0 }));
    }
    legPivots.push({ hip: hipPivot, knee: kneePivot });
  }

  const torso = new THREE.Group();
  torso.position.set(0, hipY, 0);
  torso.rotation.x = lean;
  body.add(torso);

  const front = (x, y, rx, ry, rz, cy) => rz * Math.sqrt(Math.max(0, 1 - (x / rx) ** 2 - ((y - cy) / ry) ** 2));

  if (spec.bone) {
    torso.add(limb(V(0, 0, 0), V(0, torsoH, 0), 0.04, 0.04, skin));
    for (let i = 0; i < 5; i++) {
      const rib = new THREE.Mesh(new THREE.TorusGeometry(0.17 - Math.abs(i - 1.5) * 0.015, 0.012, 6, 16, Math.PI * 1.6), skin);
      rib.position.set(0, torsoH * (0.4 + i * 0.11), 0);
      rib.rotation.set(Math.PI / 2, 0, Math.PI * 0.2);
      torso.add(rib);
    }
    torso.add(ellipsoid(0.17, 0.1, 0.09, skin, V(0, 0.04, 0)));
  } else {
    torso.add(ellipsoid(0.26 * b, torsoH * 0.62, 0.16 * b, skin, V(0, torsoH * 0.5, 0)));
    if (spec.belly) torso.add(ellipsoid(0.27 * b, 0.2 * b, 0.22 * b, skin, V(0, torsoH * 0.18, 0.08)));
    const cy = torsoH * 0.42, rx = 0.285 * b, ry = torsoH * 0.5, rz = 0.19 * b;
    if (spec.armor === 'studded' || spec.armor === 'leather') {
      torso.add(ellipsoid(rx, ry, rz, leather, V(0, cy, 0.01)));
      if (spec.armor === 'studded') {
        // Quilted squares like the old monster illustrations.
        for (let row = 0; row < 3; row++) {
          for (let col = -2; col <= 2; col++) {
            const x = col * 0.095 * b, y = torsoH * (0.2 + row * 0.2);
            const z = front(x, y, rx, ry, rz, cy) + 0.012;
            const plate = new THREE.Mesh(new THREE.BoxGeometry(0.075 * b, 0.1, 0.025), darkened(leather, THREE));
            plate.position.set(x, y, z);
            plate.rotation.y = -x * 1.6;
            torso.add(plate);
            torso.add(ellipsoid(0.012, 0.012, 0.012, metal, V(x, y, z + 0.015)));
          }
        }
      }
    } else if (spec.armor === 'chain') {
      torso.add(ellipsoid(rx, ry, rz, mat(0x8c939a, 'scale', { metalness: 0.75, roughness: 0.4 }), V(0, cy, 0.01)));
      torso.add(ellipsoid(rx * 0.98, 0.07, rz * 1.02, metal, V(0, torsoH * 0.04, 0)));
    } else if (spec.armor === 'plate') {
      torso.add(ellipsoid(rx, ry, rz, metal, V(0, cy, 0.01)));
      for (let i = 0; i < 3; i++) torso.add(ellipsoid(rx * 0.96, 0.025, rz * 1.02, darkMetal(THREE), V(0, torsoH * (0.1 + i * 0.1), 0.0)));
    } else if (spec.armor === 'fur') {
      torso.add(ellipsoid(rx, ry, rz, mat(0x6a5238, 'fur'), V(0, cy + 0.03, 0.01)));
    } else {
      torso.add(ellipsoid(0.27 * b, torsoH * 0.5, 0.2 * b, cloth, V(0, cy - 0.02, 0.015)));
    }

    // Belt, buckle and a hanging loincloth or tassets.
    torso.add(ellipsoid(0.28 * b, 0.05, 0.19 * b, flat(0x2a1c10), V(0, 0.03, 0)));
    torso.add(new THREE.Mesh(new THREE.BoxGeometry(0.07, 0.06, 0.02), flat(0xc9a227, { metalness: 0.7 })).translateX(0).translateY(0.03).translateZ(0.19 * b));
    const skirt = new THREE.Mesh(new THREE.CylinderGeometry(0.22 * b, 0.3 * b, 0.36, 18, 1, true), (() => { const c = (spec.armor ? leather : cloth).clone(); c.side = THREE.DoubleSide; return c; })());
    skirt.position.set(0, -0.06, 0);
    torso.add(skirt);
  }

  // Head with creature-specific face.
  const head = new THREE.Group();
  head.position.set(0, torsoH + 0.17, 0.05 + lean * 0.4);
  const hr = 0.16 * (spec.size < 0.9 ? 1.25 : 1);
  head.add(ellipsoid(hr, hr * 1.1, hr * 1.05, skin));
  if (spec.bone) {
    for (const s of [-1, 1]) head.add(ellipsoid(hr * 0.28, hr * 0.32, hr * 0.2, flat(0x050505), V(s * hr * 0.4, hr * 0.15, hr * 0.8)));
    head.add(ellipsoid(hr * 0.5, hr * 0.3, hr * 0.5, skin, V(0, -hr * 0.8, hr * 0.35)));
    for (let i = -2; i <= 2; i++) head.add(ellipsoid(hr * 0.06, hr * 0.12, hr * 0.06, flat(0xf2ecd8), V(i * hr * 0.12, -hr * 0.65, hr * 0.82)));
  } else {
    const snout = spec.snout;
    if (snout === 'canine' || snout === 'reptile') {
      const len = snout === 'canine' ? hr * 1.4 : hr * 1.6;
      head.add(limb(V(0, -hr * 0.15, hr * 0.5), V(0, -hr * 0.3, hr + len), hr * 0.62, hr * 0.38, skin));
      head.add(ellipsoid(hr * 0.26, hr * 0.2, hr * 0.22, dark, V(0, -hr * 0.22, hr + len + 0.01)));
      head.add(ellipsoid(hr * 0.4, hr * 0.15, len * 0.5, skin, V(0, -hr * 0.55, hr + len * 0.5)));
      for (const s of [-1, 1]) head.add(cone(0.012, 0.05, flat(0xf2ecd8), V(s * hr * 0.28, -hr * 0.68, hr + len * 0.75), { x: Math.PI, y: 0, z: 0 }));
    } else if (snout === 'pig') {
      head.add(limb(V(0, -hr * 0.2, hr * 0.6), V(0, -hr * 0.25, hr * 1.5), hr * 0.5, hr * 0.42, skin));
      const disc = ellipsoid(hr * 0.42, hr * 0.34, hr * 0.14, flat(0xb07a78), V(0, -hr * 0.2, hr * 1.55));
      head.add(disc);
      for (const s of [-1, 1]) head.add(ellipsoid(hr * 0.07, hr * 0.09, hr * 0.05, dark, V(s * hr * 0.17, -hr * 0.2, hr * 1.66)));
      head.add(ellipsoid(hr * 0.55, hr * 0.2, hr * 0.5, skin, V(0, -hr * 0.65, hr * 0.5)));
    } else if (snout === 'bull') {
      head.add(limb(V(0, -hr * 0.1, hr * 0.5), V(0, -hr * 0.4, hr * 1.5), hr * 0.65, hr * 0.5, skin));
      head.add(ellipsoid(hr * 0.5, hr * 0.38, hr * 0.2, flat(0xb08a80), V(0, -hr * 0.4, hr * 1.55)));
      for (const s of [-1, 1]) head.add(ellipsoid(hr * 0.08, hr * 0.1, hr * 0.05, dark, V(s * hr * 0.2, -hr * 0.4, hr * 1.72)));
      const ring = new THREE.Mesh(new THREE.TorusGeometry(hr * 0.2, hr * 0.03, 8, 14), metal);
      ring.position.set(0, -hr * 0.7, hr * 1.6);
      head.add(ring);
    } else {
      head.add(ellipsoid(hr * 0.6, hr * 0.42, hr * 0.5, skin, V(0, -hr * 0.55, hr * 0.4)));
      head.add(ellipsoid(hr * 0.14, hr * 0.14, hr * 0.2, skin, V(0, -hr * 0.05, hr * 1.0)));
      if (spec.longNose) head.add(cone(hr * 0.18, hr * 0.6, skin, V(0, -hr * 0.08, hr * 1.2), { x: Math.PI / 2, y: 0, z: 0 }));
    }
    eyes(head, hr * 0.38, hr * 0.2, hr * 0.82, hr * (spec.smallEyes ? 0.1 : 0.14), spec.eyes ?? 0xf0f0e0);
    if (spec.brow) head.add(ellipsoid(hr * 0.9, hr * 0.12, hr * 0.28, skin, V(0, hr * 0.4, hr * 0.8)));
    if (spec.hair) head.add(ellipsoid(hr * 1.05, hr * 0.75, hr * 1.05, flat(spec.hair, { roughness: 1 }), V(0, hr * 0.45, -hr * 0.12)));
    if (spec.mane) {
      for (let i = 0; i < 7; i++) head.add(cone(hr * 0.16, hr * 0.55, flat(spec.mane, { roughness: 1 }), V(0, hr * 0.95 - i * hr * 0.2, -hr * (0.2 + i * 0.35)), { x: -0.9, y: 0, z: 0 }));
    }
    for (const s of [-1, 1]) {
      if (spec.pointyEars) {
        const size = spec.earSize ?? 1;
        head.add(cone(hr * 0.22 * size, hr * 1.2 * size, skin, V(s * hr * 1.1, hr * 0.25, -hr * 0.05), { x: 0, y: 0, z: -s * 1.35 }));
      }
      if (spec.canineEars) head.add(cone(hr * 0.3, hr * 0.9, skin, V(s * hr * 0.6, hr * 1.1, -hr * 0.2), { x: -0.1, y: 0, z: -s * 0.2 }));
      if (spec.tusks) head.add(cone(hr * 0.1, hr * 0.55, flat(0xece4c8), V(s * hr * 0.4, -hr * 0.7, hr * (snout === 'pig' ? 1.2 : 0.75)), { x: 0.1, y: 0, z: 0 }));
      if (spec.fangs && !spec.tusks) head.add(cone(hr * 0.06, hr * 0.3, flat(0xf2ecd8), V(s * hr * 0.25, -hr * 0.75, hr * 0.82), { x: Math.PI, y: 0, z: 0 }));
      if (spec.horns) {
        let prev = V(s * hr * 0.7, hr * 0.8, 0);
        const pts = [V(s * hr * 1.3, hr * 1.1, 0.02), V(s * hr * 1.5, hr * 1.7, 0.08), V(s * hr * 1.35, hr * 2.2, 0.2)];
        let r = hr * 0.18;
        for (const pt of pts) { head.add(limb(prev, pt, r, r * 0.65, flat(0xe0d8c0, { roughness: 0.5 }))); prev = pt; r *= 0.65; }
      }
    }
    if (spec.warts) for (let i = 0; i < 6; i++) head.add(ellipsoid(0.02, 0.02, 0.02, flat(0x3a5a2a), V((ctx.rnd() - 0.5) * hr * 1.4, (ctx.rnd() - 0.5) * hr, hr * (0.8 + ctx.rnd() * 0.2))));
  }

  // Helmets.
  if (spec.helm) {
    const cap = new THREE.Mesh(new THREE.SphereGeometry(hr * 1.12, 24, 14, 0, Math.PI * 2, 0, Math.PI * 0.55), spec.helm === 'leather' ? leather.clone() : metal);
    cap.material.side = THREE.DoubleSide;
    cap.position.set(0, hr * 0.18, -hr * 0.05);
    head.add(cap);
    const rim = new THREE.Mesh(new THREE.TorusGeometry(hr * 1.02, hr * 0.06, 8, 24), metal);
    rim.rotation.x = Math.PI / 2; rim.position.set(0, hr * 0.42, -hr * 0.05);
    head.add(rim);
    if (spec.helm === 'spiked') head.add(cone(hr * 0.12, hr * 0.7, metal, V(0, hr * 1.4, -hr * 0.05)));
    if (spec.helm === 'spiked' || spec.helm === 'nasal') head.add(new THREE.Mesh(new THREE.BoxGeometry(hr * 0.12, hr * 0.7, hr * 0.06), metal).translateY(hr * 0.1).translateZ(hr * 1.08));
  }
  torso.add(head);

  // Arms with fingered hands, shoulder pads, weapon in the right hand and shield on the left arm.
  const arms = [];
  for (const s of [-1, 1]) {
    const shoulder = V(s * 0.3 * b, torsoH * 0.88, 0);
    const elbow = V(s * 0.36 * b, torsoH * (0.5 - (armK - 1) * 0.3), 0.08);
    const hand = V(s * 0.34 * b, torsoH * (0.12 - (armK - 1) * 0.7), 0.2);
    const armMat = spec.armor === 'plate' || spec.armor === 'chain' ? metal : skin;
    torso.add(ellipsoid(0.11 * b, 0.1 * b, 0.1 * b, spec.armor ? (spec.armor === 'plate' ? metal : leather) : skin, shoulder));
    if (spec.armor === 'plate' || spec.armor === 'studded') torso.add(ellipsoid(0.14 * b, 0.07, 0.12 * b, spec.armor === 'plate' ? metal : leather, shoulder.clone().add(V(s * 0.03, 0.03, 0))));
    const swing = new THREE.Group();
    swing.position.copy(shoulder);
    const arm = new THREE.Group();
    arm.position.copy(shoulder).negate();
    swing.add(arm);
    arm.add(limb(shoulder, elbow, 0.08 * b, 0.065 * b, skin), limb(elbow, hand, 0.065 * b, 0.05 * b, skin));
    if (spec.armor) arm.add(limb(elbow, elbow.clone().lerp(hand, 0.8), 0.07 * b, 0.058 * b, armMat === metal ? metal : leather));
    arm.add(ellipsoid(0.06 * b, 0.07 * b, 0.05 * b, skin, hand));
    for (let f = 0; f < 4; f++) {
      const fx = hand.x + (f - 1.5) * 0.022 * b;
      arm.add(limb(V(fx, hand.y - 0.03, hand.z + 0.03), V(fx, hand.y - 0.1, hand.z + 0.06), 0.014, 0.01, spec.claws ? skin : skin));
      if (spec.claws) arm.add(cone(0.008, 0.04, clawMat, V(fx, hand.y - 0.12, hand.z + 0.07), { x: Math.PI, y: 0, z: 0 }));
    }
    if (s === -1 && spec.weapon && spec.weapon !== 'none') {
      const w = new THREE.Group();
      w.position.copy(hand).add(V(0, -0.04, 0.05));
      buildWeapon(THREE, ctx, w, spec.weapon, H / 1.8, metal, wood);
      w.rotation.x = -0.35;
      arm.add(w);
    }
    if (s === 1 && spec.shield) {
      const sh = buildShield(THREE, ctx, spec.shield, spec.shieldColor ?? 0x7a5a30, metal, H / 1.8);
      sh.position.set(hand.x + 0.1 * b, hand.y + 0.2, hand.z - 0.02);
      arm.add(sh);
    }
    torso.add(swing);
    arms.push(swing);
  }
  g.userData.weaponArm = spec.weapon && spec.weapon !== 'none' ? { shoulder: arms[0], elbow: null } : null;
  g.userData.weaponKind = spec.weapon ?? 'none';
  g.userData.armR = arms[0];
  g.userData.armL = arms[1];
  g.userData.legs = legPivots;
  g.userData.torso = torso;

  g.userData.tick = t => {
    torso.rotation.x = lean + Math.sin(t * 1.8) * 0.02;
    head.rotation.y = Math.sin(t * 0.8) * 0.2;
    arms.forEach((a, i) => { a.rotation.x = Math.sin(t * 1.5 + i * Math.PI) * 0.06; });
  };
  return g;
}

const darkened = (material, THREE) => {
  const m = material.clone();
  m.color.multiplyScalar(0.7);
  return m;
};

const darkMetal = THREE => new THREE.MeshStandardMaterial({ color: 0x5a5f66, metalness: 0.8, roughness: 0.5 });

function buildWeapon(THREE, ctx, w, kind, k, metal, wood) {
  const { V, ellipsoid, limb, cone, flat } = ctx;
  const blade = new THREE.MeshStandardMaterial({ color: 0xc5ccd4, metalness: 0.9, roughness: 0.3 });
  const grip = new THREE.MeshStandardMaterial({ color: 0x3a2a1a, roughness: 0.9 });
  if (kind === 'spear') {
    w.add(limb(V(0, -0.5, 0), V(0, 1.5, 0), 0.02, 0.02, wood));
    w.add(cone(0.05, 0.3, blade, V(0, 1.65, 0)));
    w.add(new THREE.Mesh(new THREE.TorusGeometry(0.03, 0.01, 6, 12), grip).translateY(1.48));
  } else if (kind === 'axe') {
    w.add(limb(V(0, -0.3, 0), V(0, 0.8, 0), 0.025, 0.022, wood));
    const head = new THREE.Mesh(new THREE.CylinderGeometry(0.28, 0.28, 0.03, 3), blade);
    head.rotation.set(0, 0, Math.PI / 2); head.scale.set(1, 1, 0.8);
    head.position.set(0.14, 0.7, 0);
    w.add(head);
  } else if (kind === 'mace') {
    w.add(limb(V(0, -0.25, 0), V(0, 0.55, 0), 0.025, 0.025, wood));
    w.add(ellipsoid(0.1, 0.1, 0.1, metal, V(0, 0.65, 0)));
    for (let i = 0; i < 8; i++) {
      const a = (i / 8) * Math.PI * 2;
      w.add(cone(0.025, 0.08, metal, V(Math.cos(a) * 0.1, 0.65, Math.sin(a) * 0.1), { x: Math.sin(a) * 1.3, y: 0, z: -Math.cos(a) * 1.3 }));
    }
    w.add(cone(0.03, 0.1, metal, V(0, 0.77, 0)));
  } else if (kind === 'bigclub') {
    // A rough tree trunk with knots.
    w.add(limb(V(0, -0.3, 0), V(0, 1.25, 0.1), 0.05, 0.19, wood));
    w.add(ellipsoid(0.19, 0.12, 0.19, wood, V(0, 1.27, 0.1)));
    for (let i = 0; i < 7; i++) w.add(cone(0.035, 0.12, wood, V(Math.cos(i * 2.1) * 0.16, 0.6 + i * 0.1, 0.07 + Math.sin(i * 2.1) * 0.16), { x: Math.sin(i * 2.1) * 1.4, y: 0, z: -Math.cos(i * 2.1) * 1.4 }));
  } else if (kind === 'stone') {
    // A boulder clutched in the fist.
    const rock = flat(0x77736b, { roughness: 1 });
    w.add(ellipsoid(0.32, 0.27, 0.3, rock, V(0, 0.3, 0.08)));
    w.add(ellipsoid(0.2, 0.18, 0.2, flat(0x66625b, { roughness: 1 }), V(0.15, 0.45, 0.12)));
    w.add(ellipsoid(0.16, 0.14, 0.16, flat(0x86827a, { roughness: 1 }), V(-0.14, 0.2, 0.2)));
  } else if (kind === 'bone') {
    const bone = flat(0xe4dcc2, { roughness: 0.7 });
    w.add(limb(V(0, -0.25, 0), V(0, 1.0, 0.05), 0.04, 0.045, bone));
    for (const [y, z] of [[-0.27, 0], [1.03, 0.05]]) for (const x of [-0.05, 0.05]) w.add(ellipsoid(0.07, 0.07, 0.07, bone, V(x, y, z)));
  } else if (kind === 'club') {
    w.add(limb(V(0, -0.2, 0), V(0, 0.65, 0.05), 0.04, 0.09, wood));
    w.add(ellipsoid(0.1, 0.12, 0.1, wood, V(0, 0.7, 0.06)));
    for (let i = 0; i < 6; i++) w.add(cone(0.015, 0.06, metal, V(Math.cos(i) * 0.09, 0.5 + (i % 3) * 0.1, 0.05 + Math.sin(i) * 0.09), { x: Math.sin(i) * 1.5, y: 0, z: -Math.cos(i) * 1.5 }));
  } else {
    // Sword; "scimitar" has a curved blade.
    w.add(limb(V(0, -0.12, 0), V(0, 0.08, 0), 0.022, 0.022, grip));
    w.add(ellipsoid(0.03, 0.03, 0.03, metal, V(0, -0.14, 0)));
    w.add(new THREE.Mesh(new THREE.BoxGeometry(0.22, 0.03, 0.045), metal).translateY(0.1));
    const curved = kind === 'scimitar';
    let prev = V(0, 0.12, 0);
    for (let i = 1; i <= 6; i++) {
      const next = V(0, 0.12 + i * 0.1, curved ? Math.sin(i / 6 * 1.2) * 0.1 : 0);
      const seg = limb(prev, next, 0.03 * (1 - (i - 1) * 0.1) * (curved ? 1.2 : 1), 0.03 * (1 - i * 0.1), blade);
      seg.scale.z = 0.3;
      w.add(seg);
      prev = next;
    }
  }
  w.scale.setScalar(Math.max(0.8, k));
}

function buildShield(THREE, ctx, kind, color, metal, k) {
  const { V, ellipsoid } = ctx;
  const g = new THREE.Group();
  const face = new THREE.MeshStandardMaterial({ color, roughness: 0.85 });
  const band = new THREE.MeshStandardMaterial({ color: 0x2a1c10, roughness: 0.9 });
  if (kind === 'tower') {
    const plank = new THREE.Mesh(new THREE.BoxGeometry(0.05, 0.9, 0.42), face);
    g.add(plank);
    for (let i = -2; i <= 2; i++) g.add(new THREE.Mesh(new THREE.BoxGeometry(0.056, 0.9, 0.012), band).translateZ(i * 0.08));
    g.add(new THREE.Mesh(new THREE.BoxGeometry(0.06, 0.04, 0.44), metal).translateY(0.43));
    g.add(new THREE.Mesh(new THREE.BoxGeometry(0.06, 0.04, 0.44), metal).translateY(-0.43));
  } else {
    const r = kind === 'small' ? 0.22 : 0.32;
    const disc = new THREE.Mesh(new THREE.CylinderGeometry(r, r, 0.04, 28), face);
    disc.rotation.z = Math.PI / 2;
    g.add(disc);
    const rim = new THREE.Mesh(new THREE.TorusGeometry(r, 0.02, 8, 28), metal);
    rim.rotation.y = Math.PI / 2;
    g.add(rim);
    for (let i = 0; i < 6; i++) {
      const a = (i / 6) * Math.PI * 2;
      const bar = new THREE.Mesh(new THREE.BoxGeometry(0.046, 0.018, r * 0.95), band);
      bar.rotation.x = a;
      g.add(bar);
    }
  }
  g.add(ellipsoid(0.04, 0.09, 0.09, metal, V(0.03, 0, 0)));
  g.scale.setScalar(Math.max(0.8, k));
  return g;
}

function buildInsect(ctx) {
  const { THREE, spec, mat, flat, V, ellipsoid, limb, cone } = ctx;
  const g = new THREE.Group();
  const m = mat(spec.color, 'skin', { roughness: 0.35, metalness: 0.25 });
  const spider = spec.kind === 'spider';
  const cy = spider ? 0.75 : 0.55;
  const abd = spider ? 0.62 : 0.5;
  g.add(ellipsoid(abd, abd * 0.85, abd * 1.15, m, V(0, cy + 0.1, -0.75)));
  g.add(ellipsoid(0.34, 0.28, 0.45, m, V(0, cy, 0)));
  const head = ellipsoid(0.28, 0.24, 0.3, m, V(0, cy, 0.5));
  g.add(head);
  for (const s of [-1, 1]) {
    g.add(ellipsoid(0.07, 0.07, 0.07, flat(spider ? 0xc02020 : 0x101010, { emissive: spider ? 0xc02020 : 0, emissiveIntensity: 0.7 }), V(s * 0.12, cy + 0.12, 0.72)));
    g.add(limb(V(s * 0.08, cy - 0.1, 0.72), V(s * 0.16, cy - 0.3, 0.95), 0.05, 0.015, flat(0x201010)));
    if (!spider) {
      g.add(limb(V(s * 0.08, cy + 0.15, 0.7), V(s * 0.2, cy + 0.6, 1.05), 0.015, 0.01, flat(0x201010)));
    }
  }
  const n = spec.legs;
  const per = n / 2;
  const legs = [];
  for (let i = 0; i < per; i++) {
    const z = 0.3 - i * (0.6 / Math.max(1, per - 1));
    for (const s of [-1, 1]) {
      const root = V(s * 0.25, cy, z);
      const knee = V(s * (0.85 + (spider ? 0.2 : 0)), cy + (spider ? 0.55 : 0.35), z + 0.25 - i * 0.18);
      const foot = V(s * (1.2 + (spider ? 0.35 : 0)), 0.03, z + 0.4 - i * 0.3);
      g.add(limb(root, knee, 0.06, 0.045, m), limb(knee, foot, 0.045, 0.02, m));
      legs.push({ s, i });
    }
  }
  void cone; void THREE;
  g.userData.tick = t => { g.position.y = Math.abs(Math.sin(t * 3)) * 0.02; head.rotation.y = Math.sin(t * 2) * 0.15; };
  return g;
}

function buildSerpent(ctx) {
  const { THREE, spec, mat, flat, V, ellipsoid, limb, cone, eyes } = ctx;
  const g = new THREE.Group();
  const m = mat(spec.color);
  const belly = mat(0xd8c890);
  const pts = [];
  const N = 22;
  for (let i = 0; i < N; i++) {
    const t = i / (N - 1);
    pts.push(V(Math.sin(t * 9) * 0.6 * (1 - t * 0.2), 0.2 + (i > N - 6 ? (i - (N - 6)) * 0.14 : 0), 1.2 - t * 3.8));
  }
  pts.reverse();
  const radius = i => 0.2 * Math.min(1, 0.35 + (1 - i / (N - 1)) * 1.2) + (i > N - 4 ? 0.04 : 0);
  pts.forEach((p, i) => {
    const rr = 0.07 + 0.18 * Math.sin(Math.min(1, (i + 3) / (N * 0.45)) * Math.PI / 2) * (1 - (i / N) * 0.15);
    g.add(ellipsoid(rr, rr, rr, m, p));
    if (i > 0) g.add(limb(pts[i - 1], p, Math.max(0.07, rr * 0.95), rr, m));
  });
  void radius;
  const headPos = pts[pts.length - 1].clone();
  const head = new THREE.Group();
  head.position.copy(headPos).add(V(0, 0.12, 0.2));
  head.add(ellipsoid(0.2, 0.14, 0.3, m));
  head.add(ellipsoid(0.17, 0.05, 0.25, belly, V(0, -0.08, 0.04)));
  eyes(head, 0.13, 0.08, 0.12, 0.04, 0xf0c030);
  head.add(limb(V(0, -0.05, 0.28), V(0, -0.05, 0.5), 0.012, 0.012, flat(0xb02030)));
  for (const s of [-1, 1]) head.add(cone(0.012, 0.1, flat(0xf2ecd8), V(s * 0.07, -0.1, 0.2), { x: Math.PI, y: 0, z: 0 }));
  g.add(head);
  g.userData.tick = t => { head.rotation.y = Math.sin(t * 1.2) * 0.35; head.position.y = headPos.y + 0.12 + Math.sin(t * 2) * 0.03; };
  return g;
}

function buildBeholder(ctx) {
  const { THREE, spec, mat, flat, V, ellipsoid, limb, cone } = ctx;
  const g = new THREE.Group();
  const m = mat(spec.color, 'skin');
  const root = new THREE.Group();
  root.position.y = 1.6;
  g.add(root);
  root.add(ellipsoid(1, 1, 1, m));
  root.add(ellipsoid(0.42, 0.42, 0.1, flat(0xf2f0e0), V(0, 0.2, 0.93)));
  root.add(ellipsoid(0.2, 0.2, 0.08, flat(0x20a040, { emissive: 0x20a040, emissiveIntensity: 0.8 }), V(0, 0.2, 1.0)));
  root.add(ellipsoid(0.08, 0.14, 0.05, flat(0x050505), V(0, 0.2, 1.06)));
  const mouth = ellipsoid(0.6, 0.18, 0.1, flat(0x3a0c10), V(0, -0.5, 0.82));
  root.add(mouth);
  for (let i = -4; i <= 4; i++) root.add(cone(0.035, 0.14, flat(0xf2ecd8), V(i * 0.12, -0.38, 0.9), { x: Math.PI, y: 0, z: 0 }));
  const stalks = [];
  for (let i = 0; i < 8; i++) {
    const a = (i / 8) * Math.PI * 2;
    const s = new THREE.Group();
    s.position.set(Math.cos(a) * 0.55, 0.65, Math.sin(a) * 0.35 - 0.2);
    const tip = V(Math.cos(a) * 0.5, 0.9, Math.sin(a) * 0.3);
    s.add(limb(V(0, 0, 0), tip, 0.08, 0.05, m));
    s.add(ellipsoid(0.1, 0.1, 0.1, flat(0xf2f0e0), tip));
    s.add(ellipsoid(0.05, 0.05, 0.05, flat(0xc02020, { emissive: 0xc02020, emissiveIntensity: 0.8 }), tip.clone().add(V(0, 0, 0.07))));
    root.add(s);
    stalks.push(s);
  }
  g.userData.tick = t => {
    root.position.y = 1.6 + Math.sin(t * 1.4) * 0.12;
    stalks.forEach((s, i) => { s.rotation.z = Math.sin(t * 1.6 + i) * 0.2; s.rotation.x = Math.cos(t * 1.3 + i) * 0.2; });
  };
  return g;
}

function buildBlob(ctx) {
  const { THREE, spec, flat, V, ellipsoid, eyes } = ctx;
  const g = new THREE.Group();
  const m = flat(new THREE.Color(spec.color).getHex(), { roughness: 0.4 });
  const body = ellipsoid(0.8, 0.9, 0.8, m, V(0, 0.9, 0));
  g.add(body);
  eyes(g, 0.28, 1.2, 0.65, 0.1, 0xffe040);
  g.userData.tick = t => { body.scale.set(0.8 + Math.sin(t * 2) * 0.04, 0.9 - Math.sin(t * 2) * 0.05, 0.8); };
  return g;
}
