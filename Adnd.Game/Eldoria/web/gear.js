import { texture, emblem } from './textures.js';

// Shared material factory: a tinted, textured, bump-mapped standard material.
export function makeKit(T) {
  const cacheM = new Map();
  const mat = (kind, color, o = {}) => {
    const key = `${kind}|${color}|${JSON.stringify(o)}`;
    if (cacheM.has(key)) return cacheM.get(key);
    const map = kind ? texture(T, kind, o.repeat ?? 3) : null;
    const m = new T.MeshStandardMaterial({
      color, map, bumpMap: map, bumpScale: o.bump ?? 0.6,
      roughness: o.roughness ?? 0.8, metalness: o.metalness ?? 0,
      emissive: o.emissive ?? 0x000000, emissiveIntensity: o.emissiveIntensity ?? 0,
      side: o.double ? T.DoubleSide : T.FrontSide
    });
    cacheM.set(key, m);
    return m;
  };
  const steel = (tint = 0xd7dce4) => mat('metal', tint, { metalness: 0.85, roughness: 0.32, bump: 0.3 });
  const gold = () => mat('metal', 0xd4af37, { metalness: 0.85, roughness: 0.3, bump: 0.2 });
  const wood = (tint = 0x7a5230) => mat('wood', tint, { roughness: 0.85, repeat: 1 });
  const leather = (tint = 0x6a4526) => mat('leather', tint, { roughness: 0.85 });
  const cloth = (tint, double = false) => mat('cloth', tint, { roughness: 0.95, double, repeat: 4 });

  const add = (parent, geo, material, x = 0, y = 0, z = 0) => {
    const m = new T.Mesh(geo, material);
    m.position.set(x, y, z);
    m.castShadow = true;
    parent.add(m);
    return m;
  };
  return { mat, steel, gold, wood, leather, cloth, add };
}

const extrude = (T, shape, depth) => new T.ExtrudeGeometry(shape, { depth, bevelEnabled: true, bevelThickness: depth * 0.25, bevelSize: depth * 0.25, bevelSegments: 1 }).translate(0, 0, -depth / 2);

export function buildWeapon(T, kit, kind) {
  const g = new T.Group();
  const { add, steel, gold, wood, leather } = kit;
  const grip = (len, y) => {
    add(g, new T.CylinderGeometry(0.019, 0.019, len, 14), leather(0x3a2616), 0, y);
    for (let i = 0; i < 5; i++) add(g, new T.TorusGeometry(0.02, 0.004, 6, 14), leather(0x24170d), 0, y - len / 2 + (i + 0.5) * (len / 5)).rotation.x = Math.PI / 2;
  };

  switch (kind) {
    case 'sword': {
      const blade = new T.Shape();
      blade.moveTo(-0.033, 0); blade.lineTo(-0.031, 0.66); blade.lineTo(0, 0.76); blade.lineTo(0.031, 0.66); blade.lineTo(0.033, 0); blade.closePath();
      add(g, extrude(T, blade, 0.01), steel(), 0, 0.03);
      add(g, new T.BoxGeometry(0.012, 0.52, 0.016), steel(0x9aa2ad), 0, 0.34);              // fuller
      add(g, new T.BoxGeometry(0.30, 0.03, 0.045), gold(), 0, 0.02);                          // cross-guard
      for (const s of [-1, 1]) add(g, new T.SphereGeometry(0.022, 12, 10), gold(), s * 0.15, 0.02);
      grip(0.17, -0.075);
      add(g, new T.SphereGeometry(0.03, 14, 12), gold(), 0, -0.175);
      break;
    }
    case 'dagger': {
      const blade = new T.Shape();
      blade.moveTo(-0.022, 0); blade.lineTo(-0.02, 0.24); blade.lineTo(0, 0.32); blade.lineTo(0.02, 0.24); blade.lineTo(0.022, 0); blade.closePath();
      add(g, extrude(T, blade, 0.008), steel(), 0, 0.03);
      add(g, new T.BoxGeometry(0.1, 0.02, 0.03), gold(), 0, 0.02);
      grip(0.1, -0.035);
      add(g, new T.SphereGeometry(0.02, 10, 8), gold(), 0, -0.095);
      break;
    }
    case 'axe': {
      add(g, new T.CylinderGeometry(0.022, 0.026, 0.95, 14), wood(), 0, 0.3);
      const head = new T.Shape();
      head.moveTo(0, 0); head.quadraticCurveTo(0.2, -0.04, 0.2, 0.16); head.quadraticCurveTo(0.2, 0.34, 0, 0.3); head.lineTo(0, 0);
      add(g, extrude(T, head, 0.025), steel(), 0.015, 0.5);
      add(g, new T.BoxGeometry(0.07, 0.14, 0.05), steel(), -0.04, 0.65);                     // poll
      add(g, new T.ConeGeometry(0.02, 0.08, 8), steel(), 0, 0.79);                            // spike
      break;
    }
    case 'mace': {
      add(g, new T.CylinderGeometry(0.02, 0.024, 0.6, 14), wood(0x5a3a1e), 0, 0.2);
      add(g, new T.SphereGeometry(0.075, 18, 14), steel(0xb9bec6), 0, 0.56);
      for (let i = 0; i < 6; i++) {
        const flange = add(g, new T.BoxGeometry(0.012, 0.11, 0.05), steel(0xb9bec6), 0, 0.56);
        flange.rotation.y = (i / 6) * Math.PI;
      }
      add(g, new T.ConeGeometry(0.018, 0.06, 8), steel(), 0, 0.65);
      break;
    }
    case 'staff': {
      add(g, new T.CylinderGeometry(0.022, 0.032, 1.75, 14), wood(0x6a4526), 0, 0.5);
      const knot = add(g, new T.TorusKnotGeometry(0.05, 0.012, 48, 8, 2, 3), wood(0x5a3a1e), 0, 1.4);
      knot.scale.setScalar(0.9);
      add(g, new T.IcosahedronGeometry(0.045, 1), kit.mat(null, 0x8fd0ff, { emissive: 0x3a7acc, emissiveIntensity: 1.1, roughness: 0.2 }), 0, 1.4);
      for (let i = 0; i < 3; i++) add(g, new T.TorusGeometry(0.03, 0.006, 6, 14), leather(0x24170d), 0, 0.1 + i * 0.03).rotation.x = Math.PI / 2;
      break;
    }
    case 'spear': {
      add(g, new T.CylinderGeometry(0.02, 0.024, 1.9, 12), wood(), 0, 0.5);
      const leaf = new T.Shape();
      leaf.moveTo(0, 0); leaf.quadraticCurveTo(0.055, 0.1, 0, 0.28); leaf.quadraticCurveTo(-0.055, 0.1, 0, 0);
      add(g, extrude(T, leaf, 0.012), steel(), 0, 1.45);
      add(g, new T.CylinderGeometry(0.03, 0.026, 0.07, 12), steel(0x8a9096), 0, 1.45);
      add(g, new T.ConeGeometry(0.02, 0.05, 8), steel(), 0, -0.47).rotation.x = Math.PI;
      break;
    }
    case 'bow': {
      const pts = [[0, -0.55, 0], [0, -0.3, 0.07], [0, 0, 0.13], [0, 0.3, 0.07], [0, 0.55, 0]].map(p => new T.Vector3(...p));
      add(g, new T.TubeGeometry(new T.CatmullRomCurve3(pts), 24, 0.014, 8), wood(0x7a4a22), 0, 0.2);
      add(g, new T.CylinderGeometry(0.004, 0.004, 1.1, 4), kit.mat(null, 0xe8e0c8), 0, 0.2);   // string
      add(g, new T.CylinderGeometry(0.02, 0.02, 0.12, 10), leather(0x2a1a0e), 0, 0.2, 0.13);   // grip wrap
      break;
    }
  }
  return g;
}

export function buildShield(T, kit, color, plain) {
  const g = new T.Group();
  const { add, steel, wood } = kit;
  const face = new T.Mesh(
    new T.CylinderGeometry(0.27, 0.27, 0.03, 40),
    [kit.mat('planks', 0x8a6a40, { roughness: 0.85, repeat: 1 }), kit.mat('planks', 0x8a6a40, { repeat: 1 }), new T.MeshStandardMaterial({ map: emblem(T, '#e8d28a', color), roughness: 0.7 })]);
  face.castShadow = true;
  face.rotation.z = Math.PI / 2;
  g.add(face);
  add(g, new T.TorusGeometry(0.27, 0.014, 8, 40), steel(0x9aa2ad), 0.016).rotation.y = Math.PI / 2;
  add(g, new T.SphereGeometry(0.06, 18, 12, 0, Math.PI * 2, 0, Math.PI / 2), steel(), 0.016).rotation.z = -Math.PI / 2;
  if (!plain) for (let i = 0; i < 8; i++) {
    const a = (i / 8) * Math.PI * 2;
    add(g, new T.SphereGeometry(0.012, 8, 6), steel(), 0.018, Math.cos(a) * 0.22, Math.sin(a) * 0.22);
  }
  return g;
}

export function buildHelm(T, kit, style) {
  const g = new T.Group();
  const { add, steel, gold } = kit;
  const dome = add(g, new T.SphereGeometry(0.122, 32, 20, 0, Math.PI * 2, 0, Math.PI * 0.58), steel(), 0, 0.01);
  dome.scale.set(1, 1.08, 1.06);
  add(g, new T.TorusGeometry(0.12, 0.011, 8, 32), gold(), 0, -0.02).rotation.x = Math.PI / 2;
  add(g, new T.BoxGeometry(0.022, 0.1, 0.012), steel(), 0, -0.04, 0.128);                  // nasal
  for (const s of [-1, 1]) add(g, new T.BoxGeometry(0.012, 0.09, 0.07), steel(), s * 0.12, -0.07, 0.02); // cheek plates
  if (style === 'crest') {
    const crest = add(g, new T.CylinderGeometry(0.01, 0.02, 0.2, 10), kit.mat('cloth', 0xb03a2e), 0, 0.15, -0.02);
    crest.rotation.x = -0.3;
  }
  return g;
}
