import { makeKit, buildWeapon, buildShield, buildHelm } from './gear.js';
import { emblem } from './textures.js';

const CLASS_LOOK = {
  Fighter:     { cloth: 0x7a2a2a, trim: 0x8a8a8a, robe: false },
  Paladin:     { cloth: 0xe8e8f0, trim: 0xd4af37, robe: false, tabard: true },
  Ranger:      { cloth: 0x3d6a32, trim: 0x5a3b21, robe: false, hood: true, quiver: true },
  Cleric:      { cloth: 0xe6e2d6, trim: 0x2e6db0, robe: true, symbol: true },
  Druid:       { cloth: 0x6a5a34, trim: 0x3aa05a, robe: true, leaves: true },
  MagicUser:   { cloth: 0x2a3f9a, trim: 0xd4af37, robe: true, hat: 'wizard', book: true },
  Illusionist: { cloth: 0x6a2a9a, trim: 0xe0c0ff, robe: true, hat: 'wizard', book: true },
  Thief:       { cloth: 0x3a3a40, trim: 0x111111, robe: false, hood: true, sheath: true },
  Assassin:    { cloth: 0x18181a, trim: 0x550000, robe: false, hood: true, mask: true, sheath: true },
  Monk:        { cloth: 0xd9822b, trim: 0x7a4a14, robe: false, band: true },
  Bard:        { cloth: 0x1f7a7a, trim: 0xb03a2e, robe: false, hat: 'feather', lute: true }
};

const RACE_LOOK = {
  Human:    { skin: 0xe0b894, height: 1.0,  width: 1.0 },
  Elf:      { skin: 0xf0d8c0, height: 1.06, width: 0.88, ears: true },
  HalfElf:  { skin: 0xe6c4a4, height: 1.03, width: 0.95, ears: true },
  Dwarf:    { skin: 0xd09a78, height: 0.8,  width: 1.22, beard: true },
  Gnome:    { skin: 0xd8a888, height: 0.72, width: 1.0,  beard: true, bigNose: true },
  Halfling: { skin: 0xe2b48c, height: 0.66, width: 0.95, ears: true },
  HalfOrc:  { skin: 0x7f9a6a, height: 1.1,  width: 1.2,  tusks: true }
};

const HAIR = [0x2b1b10, 0x6b4a2a, 0xc9a227, 0x9a9a9a, 0x151515, 0x8a3a1a];

export function armorKind(name) {
  const n = (name ?? '').toLowerCase();
  if (/plate|full harness|field/.test(n)) return 'plate';
  if (/chain|banded|splint|scale|ring/.test(n)) return 'chain';
  if (/leather|studded|hide|padded/.test(n)) return 'leather';
  return n ? 'cloth' : 'none';
}

export function weaponKind(name) {
  const n = (name ?? '').toLowerCase();
  if (/bow|crossbow|sling/.test(n)) return 'bow';
  if (/dagger|knife|dirk/.test(n)) return 'dagger';
  if (/axe/.test(n)) return 'axe';
  if (/mace|hammer|flail|morning|club|star/.test(n)) return 'mace';
  if (/staff|quarterstaff|rod|wand/.test(n)) return 'staff';
  if (/spear|pike|halberd|polearm|lance|trident|glaive/.test(n)) return 'spear';
  return n ? 'sword' : 'none';
}

const hash = s => [...(s ?? '')].reduce((a, c) => (a * 31 + c.charCodeAt(0)) >>> 0, 7);

export function buildFigure(T, info, index) {
  const kit = makeKit(T);
  const { mat, steel, gold, wood, leather, cloth, add } = kit;
  const cls = (info.class || 'Fighter').split('/')[0];
  const look = CLASS_LOOK[cls] ?? CLASS_LOOK.Fighter;
  const race = RACE_LOOK[info.race] ?? RACE_LOOK.Human;
  const female = info.gender === 'Female';
  const eq = info.equipment ?? {};
  const armor = armorKind(eq.body);
  const wKind = weaponKind(eq.mainHand);
  const w = race.width * (female ? 0.92 : 1);
  const longRobe = look.robe && (armor === 'none' || armor === 'cloth');
  const seed = hash(info.name) + index;

  const fig = new T.Group();
  const body = new T.Group();
  fig.add(body);

  const skin = mat('skin', race.skin, { roughness: 0.7, bump: 0.25, repeat: 2 });
  const hairColor = HAIR[seed % HAIR.length];
  const hairMat = mat('wood', hairColor, { roughness: 0.6, repeat: 2, bump: 0.8 });
  const clothMat = cloth(look.cloth);
  const clothDouble = cloth(look.cloth, true);
  const trimMat = look.trim === 0xd4af37 ? gold() : cloth(look.trim);
  const bootMat = leather(0x35241a);
  const pants = cloth(armor === 'plate' || armor === 'chain' ? 0x3a3a44 : (look.robe ? look.cloth : 0x46403a));

  const torsoMat = armor === 'plate' ? steel(0xbfc6d0)
    : armor === 'chain' ? mat('mail', 0xb0b6be, { metalness: 0.8, roughness: 0.45, repeat: 6, bump: 1.2 })
    : armor === 'leather' ? leather(0x6a4526)
    : clothMat;
  const sleeveMat = armor === 'plate' || armor === 'chain' ? torsoMat : armor === 'leather' ? leather(0x5a3a1e) : clothMat;
  const handMat = armor === 'plate' ? steel(0xa9b0ba) : (armor === 'leather' ? leather(0x3a2616) : skin);

  // ---- legs: thigh, knee, shin, boot --------------------------------------------------------------
  const legs = [];
  for (const side of [-1, 1]) {
    const hip = new T.Group();
    hip.position.set(side * 0.095 * w, 0.93, 0);
    add(hip, new T.CylinderGeometry(0.082 * w, 0.058 * w, 0.46, 20), pants, 0, -0.23);
    const knee = new T.Group();
    knee.position.y = -0.46;
    hip.add(knee);
    add(knee, new T.SphereGeometry(0.056 * w, 14, 10), pants);
    add(knee, new T.CylinderGeometry(0.056 * w, 0.04 * w, 0.43, 20), pants, 0, -0.215);
    const boot = new T.Group();
    boot.position.y = -0.43;
    knee.add(boot);
    add(knee, new T.CylinderGeometry(0.052 * w, 0.06 * w, 0.24, 20), bootMat, 0, -0.31);
    add(knee, new T.TorusGeometry(0.056 * w, 0.01, 8, 20), leather(0x4a3220), 0, -0.19).rotation.x = Math.PI / 2;
    add(boot, new T.BoxGeometry(0.095 * w, 0.075, 0.2), bootMat, 0, -0.03, 0.045);
    add(boot, new T.SphereGeometry(0.047 * w, 14, 10), bootMat, 0, -0.03, 0.135).scale.set(1, 0.8, 1);
    add(boot, new T.BoxGeometry(0.1 * w, 0.018, 0.22), leather(0x1f140c), 0, -0.07, 0.05);

    if (armor === 'plate') {
      add(hip, new T.CylinderGeometry(0.088 * w, 0.064 * w, 0.28, 20), steel(), 0, -0.17);          // cuisse
      add(knee, new T.SphereGeometry(0.06 * w, 16, 12), steel(), 0, 0.0, 0.015);                      // knee cop
      add(knee, new T.CylinderGeometry(0.058 * w, 0.044 * w, 0.28, 20), steel(), 0, -0.2);            // greave
    }
    body.add(hip);
    legs.push({ hip, knee });
  }

  // ---- torso ---------------------------------------------------------------------------------------
  const prof = female
    ? [[0.001, 0], [0.16, 0], [0.205, 0.07], [0.145, 0.22], [0.19, 0.38], [0.2, 0.47], [0.16, 0.56], [0.07, 0.61], [0.001, 0.62]]
    : [[0.001, 0], [0.165, 0], [0.185, 0.06], [0.165, 0.22], [0.195, 0.4], [0.21, 0.5], [0.165, 0.58], [0.07, 0.62], [0.001, 0.63]];
  const lathe = (pr, k = 1) => new T.LatheGeometry(pr.map(([r, y]) => new T.Vector2(r * w * k, y)), 40);
  const torso = add(body, lathe(prof), torsoMat, 0, 0.93);
  torso.scale.z = 0.72;
  const waistR = (female ? 0.145 : 0.165) * w;

  if (armor === 'plate') {
    const cuirass = add(body, lathe(prof.slice(3), 1.07), steel(0xc6ccd6), 0, 0.93);
    cuirass.scale.z = 0.76;
    add(body, new T.BoxGeometry(0.014, 0.3, 0.02), steel(0x9aa2ad), 0, 1.3, 0.17 * w);
    add(body, new T.CylinderGeometry(0.075, 0.09, 0.06, 20), steel(), 0, 1.56);                       // gorget
    const tassets = add(body, new T.CylinderGeometry(0.2 * w, 0.255 * w, 0.2, 28, 1, true), steel(), 0, 0.88);
    tassets.material = tassets.material.clone(); tassets.material.side = T.DoubleSide;
  }
  if (armor === 'chain') {
    const skirt = add(body, new T.CylinderGeometry(0.2 * w, 0.25 * w, 0.3, 28, 4, true), torsoMat, 0, 0.82);
    skirt.material = skirt.material.clone(); skirt.material.side = T.DoubleSide;
  }
  if (armor === 'leather') {
    for (let i = 0; i < 10; i++) {
      const a = -0.6 + (i % 5) * 0.3, y = 1.22 + Math.floor(i / 5) * 0.12;
      add(body, new T.SphereGeometry(0.012, 8, 6), steel(0x9aa2ad), Math.sin(a) * 0.2 * w, y, Math.cos(a) * 0.2 * w * 0.72 + 0.01);
    }
    add(body, new T.BoxGeometry(0.06, 0.45, 0.02), leather(0x3a2616), 0.08, 1.28, 0.15).rotation.z = 0.55;
  }
  if (armor === 'none' || armor === 'cloth') {
    if (longRobe) {
      const robe = add(body, new T.CylinderGeometry(0.2 * w, 0.38 * w, 0.86, 36, 4, true), clothDouble, 0, 0.5);
      robe.scale.z = 0.9;
      add(body, new T.TorusGeometry(0.378 * w, 0.014, 8, 36), trimMat, 0, 0.075).rotation.x = Math.PI / 2;
      add(body, new T.TorusGeometry(0.21 * w, 0.03, 10, 28), trimMat, 0, 1.15).rotation.x = Math.PI / 2;     // sash
      add(body, new T.BoxGeometry(0.045, 0.28, 0.02), trimMat, 0.08 * w, 0.95, 0.17 * w);                      // sash tail
    } else {
      const len = female ? 0.4 : 0.22;
      const skirt = add(body, new T.CylinderGeometry(0.2 * w, (female ? 0.28 : 0.235) * w, len, 28, 2, true), clothDouble, 0, 0.93 - len / 2 + 0.06);
      skirt.scale.z = 0.85;
      add(body, new T.TorusGeometry(0.215 * w, 0.01, 8, 28), trimMat, 0, 0.93 - len + 0.06).rotation.x = Math.PI / 2;
    }
    add(body, new T.TorusGeometry(0.075 * w, 0.014, 8, 20), trimMat, 0, 1.55).rotation.x = Math.PI / 2;        // collar
  }

  // belt with buckle, pouch
  const belt = add(body, new T.CylinderGeometry(waistR + 0.012, waistR + 0.012, 0.05, 32), leather(0x2a1a0e), 0, 1.15);
  belt.scale.z = 0.76;
  add(body, new T.BoxGeometry(0.045, 0.045, 0.014), gold(), 0, 1.15, (waistR + 0.014) * 0.76 + 0.006);
  add(body, new T.BoxGeometry(0.08, 0.09, 0.05), leather(0x4a3220), -(waistR + 0.02), 1.06, 0.03);
  add(body, new T.BoxGeometry(0.084, 0.02, 0.054), leather(0x2a1a0e), -(waistR + 0.02), 1.1, 0.03);

  const neck = add(body, new T.CylinderGeometry(0.048 * w, 0.056 * w, 0.1, 16), skin, 0, 1.58);
  void neck;

  // ---- arms: upper arm, elbow, forearm, hand ----------------------------------------------------------
  const handBuilder = () => {
    const h = new T.Group();
    add(h, new T.BoxGeometry(0.07, 0.08, 0.03), handMat, 0, -0.045).scale.set(1, 1, 1);
    for (let i = 0; i < 4; i++) {
      const f = add(h, new T.CylinderGeometry(0.0085, 0.0075, 0.06, 8), handMat, -0.026 + i * 0.0175, -0.115, 0.004);
      f.rotation.x = 0.35;
    }
    add(h, new T.CylinderGeometry(0.01, 0.009, 0.05, 8), handMat, 0.04, -0.06, 0.015).rotation.z = 0.5;
    return h;
  };
  const arms = [];
  for (const side of [-1, 1]) {
    const shoulder = new T.Group();
    shoulder.position.set(side * (0.215 * w + 0.045), 1.46, 0);
    add(shoulder, new T.SphereGeometry(0.052 * w, 16, 12), sleeveMat);
    add(shoulder, new T.CylinderGeometry(0.05 * w, 0.042 * w, 0.3, 20), sleeveMat, 0, -0.15);
    const elbow = new T.Group();
    elbow.position.y = -0.3;
    shoulder.add(elbow);
    add(elbow, new T.SphereGeometry(0.042 * w, 14, 10), sleeveMat);
    if (longRobe) {
      const sleeve = add(elbow, new T.CylinderGeometry(0.044 * w, 0.1 * w, 0.28, 20, 1, true), clothDouble, 0, -0.14);
      void sleeve;
      add(elbow, new T.TorusGeometry(0.1 * w, 0.01, 8, 20), trimMat, 0, -0.28).rotation.x = Math.PI / 2;
    } else {
      add(elbow, new T.CylinderGeometry(0.04 * w, 0.032 * w, 0.27, 18), sleeveMat, 0, -0.135);
    }
    if (armor === 'plate') {
      add(elbow, new T.SphereGeometry(0.05 * w, 14, 10), steel());
      add(elbow, new T.CylinderGeometry(0.046 * w, 0.037 * w, 0.2, 18), steel(), 0, -0.14);              // vambrace
      const pauldron = add(shoulder, new T.SphereGeometry(0.095 * w, 20, 14, 0, Math.PI * 2, 0, Math.PI * 0.55), steel(0xc6ccd6), 0, 0.02);
      pauldron.scale.set(1.15, 0.85, 1.15);
      add(shoulder, new T.SphereGeometry(0.1 * w, 20, 14, 0, Math.PI * 2, 0, Math.PI * 0.4), steel(0xaab2bd), 0, -0.06).scale.set(1.1, 0.8, 1.1);
    } else if (armor === 'chain') {
      const pauldron = add(shoulder, new T.SphereGeometry(0.075 * w, 16, 12, 0, Math.PI * 2, 0, Math.PI * 0.55), leather(0x4a3220), 0, 0.02);
      pauldron.scale.set(1.1, 0.8, 1.1);
    } else if (armor === 'leather') {
      add(elbow, new T.CylinderGeometry(0.043 * w, 0.036 * w, 0.14, 16), leather(0x3a2616), 0, -0.2);      // bracer
    } else if (!longRobe) {
      add(elbow, new T.TorusGeometry(0.036 * w, 0.008, 8, 16), trimMat, 0, -0.27).rotation.x = Math.PI / 2;
    }
    const hand = handBuilder();
    hand.position.y = -0.27;
    elbow.add(hand);
    shoulder.rotation.z = side * 0.06;
    body.add(shoulder);
    arms.push({ shoulder, elbow, hand });
  }
  const rightArm = arms[0], leftArm = arms[1];

  // ---- head ---------------------------------------------------------------------------------------------
  const head = new T.Group();
  head.position.y = 1.72;
  body.add(head);
  const skull = add(head, new T.SphereGeometry(0.105, 40, 28), skin);
  skull.scale.set(0.92 * (race.tusks ? 1.1 : 1), 1.12, 1.0);
  const jaw = add(head, new T.SphereGeometry(0.075, 28, 20), skin, 0, -0.065, 0.025);
  jaw.scale.set(0.95 * (race.tusks ? 1.25 : 1), 0.85, 0.95);
  for (const side of [-1, 1]) {
    add(head, new T.SphereGeometry(0.018, 14, 10), mat(null, 0xf4f4f0, { roughness: 0.2 }), side * 0.04, 0.02, 0.092).scale.set(1, 0.8, 0.6);
    add(head, new T.SphereGeometry(0.009, 10, 8), mat(null, [0x3a6a9a, 0x3a7a4a, 0x5a3a1a, 0x2a2a2a][seed % 4], { roughness: 0.2 }), side * 0.04, 0.02, 0.101);
    const brow = add(head, new T.BoxGeometry(0.036, race.tusks ? 0.014 : 0.007, 0.012), hairMat, side * 0.042, 0.05, 0.095);
    brow.rotation.z = side * (race.tusks ? -0.25 : -0.1);
    if (race.ears) {
      add(head, new T.ConeGeometry(0.02, 0.1, 8), skin, side * 0.1, 0.03, -0.005).rotation.z = -side * 1.15;
    } else {
      add(head, new T.SphereGeometry(0.02, 10, 8), skin, side * 0.098, 0, 0).scale.set(0.5, 1, 0.8);
    }
    if (race.tusks) add(head, new T.ConeGeometry(0.009, 0.045, 8), mat(null, 0xeeeedd), side * 0.032, -0.085, 0.1);
  }
  if (race.bigNose) add(head, new T.SphereGeometry(0.03, 14, 10), skin, 0, -0.012, 0.11);
  else add(head, new T.ConeGeometry(0.014, 0.04, 10), skin, 0, -0.012, 0.106).rotation.x = Math.PI / 2;
  add(head, new T.BoxGeometry(0.04, 0.006, 0.01), mat(null, 0x8a3a3a), 0, -0.062, 0.1);

  const helmed = !!eq.head && armorKind(eq.head) !== 'none';
  const covered = helmed || look.hat || look.hood;
  if (helmed) {
    head.add(buildHelm(T, kit, cls === 'Paladin' ? 'crest' : ''));
  } else if (look.hat === 'wizard') {
    add(head, new T.CylinderGeometry(0.26, 0.27, 0.012, 32), clothMat, 0, 0.085).rotation.z = 0.05;
    const cone = add(head, new T.ConeGeometry(0.135, 0.45, 28, 4), clothMat, 0.02, 0.31);
    cone.rotation.z = -0.16;
    add(head, new T.CylinderGeometry(0.137, 0.14, 0.035, 28), gold(), 0.0, 0.11);
  } else if (look.hat === 'feather') {
    add(head, new T.CylinderGeometry(0.085, 0.115, 0.08, 24), clothMat, 0, 0.1);
    add(head, new T.TorusGeometry(0.13, 0.012, 8, 28), clothMat, 0, 0.065).rotation.x = Math.PI / 2;
    const feather = add(head, new T.ConeGeometry(0.014, 0.3, 8), mat(null, 0xb03a2e), 0.09, 0.2, -0.02);
    feather.rotation.z = -0.7;
  } else if (look.hood) {
    const hood = add(head, new T.SphereGeometry(0.125, 32, 20, 0, Math.PI * 2, 0, Math.PI * 0.68), clothDouble, 0, 0.015);
    hood.scale.set(1, 1.1, 1.12);
    add(head, new T.CylinderGeometry(0.13, 0.15, 0.1, 24, 1, true), clothDouble, 0, -0.1, -0.02);
    if (look.mask) {
      const mask = add(head, new T.CylinderGeometry(0.1, 0.095, 0.075, 20, 1, true, -1.15, 2.3), cloth(0x101012, true), 0, -0.06, 0);
      mask.scale.set(1, 1, 1.02);
    }
  } else {
    const cap = add(head, new T.SphereGeometry(0.113, 32, 20, 0, Math.PI * 2, 0, Math.PI * (female ? 0.62 : 0.52)), hairMat, 0, 0.012);
    cap.scale.set(0.96, 1.15, 1.08);
    cap.rotation.x = -0.22;
    add(head, new T.BoxGeometry(0.15, 0.02, 0.02), hairMat, 0, 0.07, 0.1);                               // fringe
    if (look.band) add(head, new T.TorusGeometry(0.1, 0.01, 8, 28), mat('cloth', 0xb03a2e), 0, 0.05).rotation.x = Math.PI / 2;
  }
  if (look.leaves) {
    for (let i = 0; i < 12; i++) {
      const a = (i / 12) * Math.PI * 2;
      const leaf = add(head, new T.SphereGeometry(0.022, 8, 6), mat(null, i % 3 ? 0x3aa05a : 0x2a7a3a), Math.cos(a) * 0.112, 0.07, Math.sin(a) * 0.112);
      leaf.scale.set(1, 0.4, 1.8); leaf.rotation.y = -a;
    }
  }
  if (!covered) {
    if (female) {
      const back = add(head, new T.CylinderGeometry(0.1, 0.05, 0.42, 20, 1, true), cloth(hairColor, true), 0, -0.16, -0.075);
      back.material = hairMat.clone(); back.material.side = T.DoubleSide;
    } else if (seed % 3 === 0) {
      add(head, new T.CylinderGeometry(0.02, 0.012, 0.2, 10), hairMat, 0, -0.09, -0.12).rotation.x = -0.4;
    }
  }
  if (race.beard && !female) {
    const beard = add(head, new T.ConeGeometry(0.09, 0.3, 20, 1), hairMat, 0, -0.18, 0.06);
    beard.rotation.x = Math.PI;
    add(beard.parent, new T.TorusGeometry(0.04, 0.012, 8, 14), hairMat, 0, -0.065, 0.1).rotation.z = Math.PI / 2;
    add(head, new T.CylinderGeometry(0.012, 0.012, 0.06, 8), gold(), 0, -0.3, 0.06);
  }

  // ---- back/belt gear and class extras -----------------------------------------------------------------
  const backZ = -0.2;
  if (eq.back) {
    const color = cls === 'Ranger' ? 0x2f4a2a : (cls === 'Paladin' ? 0xa83232 : [0x4a2a6a, 0x2a4a6a, 0x6a2a2a][seed % 3]);
    const cloak = add(body, new T.CylinderGeometry(0.235 * w, 0.37 * w, 0.95, 28, 6, true, Math.PI - 1.15, 2.3), cloth(color, true), 0, 1.03, -0.03);
    cloak.scale.z = 0.95;
    add(body, new T.SphereGeometry(0.02, 10, 8), gold(), 0, 1.52, 0.13);
    add(body, new T.TorusGeometry(0.095 * w, 0.02, 8, 20, Math.PI * 1.6), cloth(color, true), 0, 1.55, -0.01).rotation.x = Math.PI / 2;
  }
  const hasPack = !longRobe || cls === 'Cleric';
  if (hasPack) {
    const z = backZ - (eq.back ? 0.05 : 0);
    add(body, new T.BoxGeometry(0.26 * w, 0.3, 0.13), leather(0x5a3a1e), 0, 1.25, z);
    add(body, new T.BoxGeometry(0.27 * w, 0.06, 0.14), leather(0x3a2616), 0, 1.36, z);
    add(body, new T.CylinderGeometry(0.05, 0.05, 0.3 * w, 16), cloth(0x6a5a3a), 0, 1.45, z - 0.01).rotation.z = Math.PI / 2;
    for (const side of [-1, 1]) add(body, new T.BoxGeometry(0.035, 0.4, 0.012), leather(0x2a1a0e), side * 0.1 * w, 1.3, z + 0.07);
  }
  if (look.quiver || wKind === 'bow') {
    const q = new T.Group();
    q.position.set(0.1, 1.38, backZ - 0.09);
    q.rotation.z = -0.3;
    add(q, new T.CylinderGeometry(0.045, 0.04, 0.46, 16), leather(0x4a3220), 0, 0);
    add(q, new T.TorusGeometry(0.045, 0.008, 8, 16), gold(), 0, 0.22).rotation.x = Math.PI / 2;
    for (let i = 0; i < 6; i++) {
      const a = (i / 6) * Math.PI * 2;
      add(q, new T.CylinderGeometry(0.004, 0.004, 0.3, 5), wood(0xc9a56a), Math.cos(a) * 0.02, 0.32, Math.sin(a) * 0.02);
      add(q, new T.ConeGeometry(0.01, 0.04, 6), mat(null, i % 2 ? 0xb03a2e : 0xe8e0c8), Math.cos(a) * 0.02, 0.5, Math.sin(a) * 0.02);
    }
    body.add(q);
  }
  if (look.lute && wKind !== 'bow') {
    const lute = new T.Group();
    lute.position.set(-0.05, 1.2, backZ - 0.12);
    lute.rotation.z = 0.35;
    add(lute, new T.SphereGeometry(0.15, 20, 14), wood(0x9a6a3a), 0, -0.1).scale.set(0.9, 1.1, 0.5);
    add(lute, new T.CylinderGeometry(0.02, 0.02, 0.45, 10), wood(0x4a2a12), 0, 0.28);
    add(lute, new T.BoxGeometry(0.06, 0.1, 0.03), wood(0x4a2a12), 0, 0.54);
    body.add(lute);
  }
  if (look.symbol) {
    const sym = new T.Group();
    sym.position.set(0, 1.38, 0.16);
    add(sym, new T.BoxGeometry(0.014, 0.07, 0.01), gold());
    add(sym, new T.BoxGeometry(0.05, 0.014, 0.01), gold(), 0, 0.012);
    add(sym, new T.CylinderGeometry(0.002, 0.002, 0.12, 4), gold(), 0, 0.07);
    body.add(sym);
  }
  if (look.tabard) {
    const tex = emblem(T, '#d4af37', '#f2f2f6');
    const t = new T.MeshStandardMaterial({ map: tex, roughness: 0.9, side: T.DoubleSide });
    const panel = add(body, new T.BoxGeometry(0.3 * w, 0.62, 0.012), t, 0, 1.0, 0.17 * w);
    void panel;
    add(body, new T.BoxGeometry(0.3 * w, 0.62, 0.012), t, 0, 1.0, -0.15 * w);
  }
  if (look.book) {
    const book = new T.Group();
    book.position.set(waistR + 0.07, 1.04, 0.02);
    add(book, new T.BoxGeometry(0.05, 0.17, 0.12), leather(0x4a2a6a));
    add(book, new T.BoxGeometry(0.052, 0.04, 0.02), gold(), 0, 0, 0.06);
    body.add(book);
  }
  if (look.sheath && wKind !== 'dagger') {
    const sheath = new T.Group();
    sheath.position.set(waistR + 0.05, 1.05, 0.08);
    sheath.rotation.x = 0.2;
    add(sheath, new T.CylinderGeometry(0.02, 0.014, 0.22, 10), leather(0x2a1a0e), 0, -0.05);
    add(sheath, new T.CylinderGeometry(0.014, 0.014, 0.1, 8), leather(0x3a2616), 0, 0.1);
    body.add(sheath);
  }

  // ---- held items --------------------------------------------------------------------------------------
  if (wKind !== 'none') {
    const weapon = buildWeapon(T, kit, wKind);
    if (wKind === 'bow') weapon.position.y = -0.2;
    else if (wKind === 'staff' || wKind === 'spear') { weapon.position.set(0, -0.04, 0.02); weapon.rotation.x = 0.12; }
    else if (wKind === 'sword' || wKind === 'dagger') { weapon.position.y = -0.04; weapon.rotation.x = 1.85; }
    else { weapon.position.y = -0.04; weapon.rotation.x = 0.6; }
    rightArm.hand.add(weapon);
  }
  if (eq.shield) {
    const shield = buildShield(T, kit, cls === 'Paladin' ? '#f2f2f6' : '#7a2a2a', false);
    shield.position.set(0.1, -0.12, 0.02);
    leftArm.elbow.add(shield);
  }

  fig.scale.setScalar(race.height);
  fig.userData = { legs, arms, body, armed: wKind === 'staff' || wKind === 'spear' || wKind === 'bow', weaponArm: rightArm, weaponKind: wKind };
  return fig;
}
