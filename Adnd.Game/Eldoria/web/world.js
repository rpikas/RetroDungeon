// Procedural Eldoria: seeded fBm terrain, rivers/lakes at a water level, biomes by height and moisture.
function mulberry(seed) {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6D2B79F5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

function makeNoise(seed) {
  const rnd = mulberry(seed);
  const perm = new Uint8Array(512);
  const p = Array.from({ length: 256 }, (_, i) => i);
  for (let i = 255; i > 0; i--) {
    const j = Math.floor(rnd() * (i + 1));
    [p[i], p[j]] = [p[j], p[i]];
  }
  for (let i = 0; i < 512; i++) perm[i] = p[i & 255];
  const grad = (h, x, y) => {
    const a = (h & 7) * Math.PI / 4;
    return Math.cos(a) * x + Math.sin(a) * y;
  };
  const fade = t => t * t * t * (t * (t * 6 - 15) + 10);
  return (x, y) => {
    const xi = Math.floor(x), yi = Math.floor(y);
    const xf = x - xi, yf = y - yi;
    const X = xi & 255, Y = yi & 255;
    const u = fade(xf), v = fade(yf);
    const aa = perm[perm[X] + Y], ab = perm[perm[X] + Y + 1];
    const ba = perm[perm[X + 1] + Y], bb = perm[perm[X + 1] + Y + 1];
    const l1 = grad(aa, xf, yf) + u * (grad(ba, xf - 1, yf) - grad(aa, xf, yf));
    const l2 = grad(ab, xf, yf - 1) + u * (grad(bb, xf - 1, yf - 1) - grad(ab, xf, yf - 1));
    return (l1 + v * (l2 - l1)) * 1.4;
  };
}

const WATER_LEVEL = 0;
const CHUNK = 64;
const CELLS = 32;
const VIEW_CHUNKS = 5;

// A simple sky/ground gradient, prefiltered, so metal has something to reflect instead of rendering black.
function makeEnvironment(THREE, renderer, scene) {
  const envScene = new THREE.Scene();
  const geo = new THREE.SphereGeometry(50, 32, 16);
  const pos = geo.attributes.position;
  const colors = new Float32Array(pos.count * 3);
  const top = new THREE.Color(0x7fa8d8), horizon = new THREE.Color(0xf4f0e4), bottom = new THREE.Color(0x4a5a2a), c = new THREE.Color();
  for (let i = 0; i < pos.count; i++) {
    const t = pos.getY(i) / 50;
    if (t > 0) c.copy(horizon).lerp(top, Math.pow(t, 0.6)); else c.copy(horizon).lerp(bottom, Math.min(1, -t * 2));
    colors.set([c.r, c.g, c.b], i * 3);
  }
  geo.setAttribute('color', new THREE.BufferAttribute(colors, 3));
  envScene.add(new THREE.Mesh(geo, new THREE.MeshBasicMaterial({ vertexColors: true, side: THREE.BackSide })));
  const sunBlob = new THREE.Mesh(new THREE.SphereGeometry(6, 12, 8), new THREE.MeshBasicMaterial({ color: 0xfff1d0 }));
  sunBlob.position.set(30, 35, 20);
  envScene.add(sunBlob);
  const pmrem = new THREE.PMREMGenerator(renderer);
  scene.environment = pmrem.fromScene(envScene, 0.03).texture;
  scene.environmentIntensity = 0.75;
  pmrem.dispose();
}

export function createWorld(THREE, scene, seed, renderer = null) {
  if (renderer) makeEnvironment(THREE, renderer, scene);
  const clearing = { x: 0, z: 0, r: 0 };
  const noise = makeNoise(seed * 7919 + 13);
  const moist = makeNoise(seed * 104729 + 7);
  const warp = makeNoise(seed * 15485863 + 3);

  function heightAt(x, z) {
    const wx = x + warp(x * 0.002, z * 0.002) * 150;
    const wz = z + warp(x * 0.002 + 9.1, z * 0.002 + 4.7) * 150;
    let amp = 1, freq = 0.0035, h = 0;
    for (let o = 0; o < 6; o++) {
      h += noise(wx * freq, wz * freq) * amp;
      amp *= 0.5;
      freq *= 2;
    }
    // Rivers: carve valleys where a ridged noise is near zero.
    const river = Math.abs(noise(wx * 0.0018 + 50, wz * 0.0018 + 50));
    const carve = Math.max(0, 1 - river / 0.035);
    h = h * 55 + 4 - carve * carve * 9;
    return h;
  }

  function moistureAt(x, z) {
    return moist(x * 0.003, z * 0.003) * 0.5 + 0.5;
  }

  function biomeAt(x, z) {
    const h = heightAt(x, z);
    const m = moistureAt(x, z);
    if (h < WATER_LEVEL) return 'Water';
    if (h < WATER_LEVEL + 1.2) return 'Shore';
    if (h > 62) return 'Snowy peaks';
    if (h > 40) return 'Mountains';
    if (m > 0.58) return 'Forest';
    if (m < 0.38) return 'Dry heath';
    return 'Meadow';
  }

  // The terrain classes of the outdoor encounter table (Plain, Scrub, Forest, Rough, Desert, Hills, Mountains, Marsh).
  function terrainAt(x, z) {
    const h = heightAt(x, z);
    const m = moistureAt(x, z);
    if (h < WATER_LEVEL) return 'Water';
    if (h < WATER_LEVEL + 1.2) return 'Marsh';
    if (h > 40) return 'Mountains';
    if (h > 24) return 'Hills';
    if (m > 0.58) return 'Forest';
    if (m < 0.25) return 'Desert';
    if (m < 0.38) return 'Scrub';
    if (h > 14) return 'Rough';
    return 'Plain';
  }

  const colorFor = (h, m, slope) => {
    const c = new THREE.Color();
    if (h < WATER_LEVEL + 1.2) c.set(0xc9b983);
    else if (h > 62) c.set(0xf2f4f7);
    else if (slope > 0.7 || h > 40) c.set(0x7b7468);
    else if (m > 0.58) c.set(0x2f5a2a);
    else if (m < 0.38) c.set(0x8c8a4a);
    else c.set(0x4f7f35);
    c.offsetHSL(0, 0, (Math.sin(h * 3.1) * 0.02));
    return c;
  };

  const group = new THREE.Group();
  scene.add(group);

  scene.background = new THREE.Color(0x9ec3e6);
  scene.fog = new THREE.Fog(0xb8d2ea, 150, 520);
  const sun = new THREE.DirectionalLight(0xfff1d0, 2.4);
  sun.position.set(120, 180, 80);
  sun.castShadow = true;
  sun.shadow.mapSize.set(4096, 4096);
  Object.assign(sun.shadow.camera, { left: -40, right: 40, top: 40, bottom: -40, near: 10, far: 500 });
  scene.add(sun, sun.target);
  scene.add(new THREE.HemisphereLight(0xbcd6ff, 0x4a5a2a, 0.9));

  const water = new THREE.Mesh(
    new THREE.PlaneGeometry(4000, 4000).rotateX(-Math.PI / 2),
    new THREE.MeshStandardMaterial({ color: 0x2a6f9e, transparent: true, opacity: 0.82, roughness: 0.15, metalness: 0.2 }));
  water.position.y = WATER_LEVEL;
  scene.add(water);

  const terrainMaterial = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.95, flatShading: false });
  const treeGeometry = new THREE.ConeGeometry(1.6, 6, 7).translate(0, 4.5, 0);
  const trunkGeometry = new THREE.CylinderGeometry(0.25, 0.35, 2, 6).translate(0, 1, 0);
  const treeMaterial = new THREE.MeshStandardMaterial({ color: 0x1f4a22, roughness: 1 });
  const trunkMaterial = new THREE.MeshStandardMaterial({ color: 0x5a3b21, roughness: 1 });

  const chunks = new Map();

  function buildChunk(cx, cz) {
    const geo = new THREE.PlaneGeometry(CHUNK, CHUNK, CELLS, CELLS).rotateX(-Math.PI / 2);
    const pos = geo.attributes.position;
    const colors = new Float32Array(pos.count * 3);
    const ox = cx * CHUNK + CHUNK / 2, oz = cz * CHUNK + CHUNK / 2;
    for (let i = 0; i < pos.count; i++) {
      const x = pos.getX(i) + ox, z = pos.getZ(i) + oz;
      const h = heightAt(x, z);
      pos.setY(i, h);
      const slope = Math.abs(heightAt(x + 2, z) - h) / 2 + Math.abs(heightAt(x, z + 2) - h) / 2;
      const c = colorFor(h, moistureAt(x, z), slope);
      colors.set([c.r, c.g, c.b], i * 3);
    }
    geo.setAttribute('color', new THREE.BufferAttribute(colors, 3));
    geo.computeVertexNormals();
    const mesh = new THREE.Mesh(geo, terrainMaterial);
    mesh.position.set(ox, 0, oz);
    mesh.receiveShadow = true;

    const holder = new THREE.Group();
    holder.add(mesh);

    // Trees scattered by a chunk-seeded generator so they are stable.
    const rnd = mulberry((cx * 73856093) ^ (cz * 19349663) ^ seed);
    const spots = [];
    for (let i = 0; i < 160; i++) {
      const x = ox + (rnd() - 0.5) * CHUNK, z = oz + (rnd() - 0.5) * CHUNK;
      const h = heightAt(x, z);
      const m = moistureAt(x, z);
      if (h < WATER_LEVEL + 1.5 || h > 45) continue;
      if (Math.hypot(x - clearing.x, z - clearing.z) < clearing.r) continue;
      if (rnd() > (m > 0.58 ? 0.9 : m > 0.42 ? 0.2 : 0.05)) continue;
      spots.push([x, h, z, 0.7 + rnd() * 0.9]);
    }
    if (spots.length) {
      const crowns = new THREE.InstancedMesh(treeGeometry, treeMaterial, spots.length);
      const trunks = new THREE.InstancedMesh(trunkGeometry, trunkMaterial, spots.length);
      const m4 = new THREE.Matrix4();
      spots.forEach(([x, h, z, s], i) => {
        m4.makeScale(s, s, s).setPosition(x, h, z);
        crowns.setMatrixAt(i, m4);
        trunks.setMatrixAt(i, m4);
      });
      crowns.castShadow = true;
      holder.add(crowns, trunks);
    }
    group.add(holder);
    return holder;
  }

  let lastCx = null, lastCz = null;

  function update(dt, p) {
    const cx = Math.floor(p.x / CHUNK), cz = Math.floor(p.z / CHUNK);
    water.position.x = p.x;
    water.position.z = p.z;
    sun.position.set(p.x + 120, 180, p.z + 80);
    sun.target.position.set(p.x, 0, p.z);
    if (cx === lastCx && cz === lastCz) return;
    lastCx = cx; lastCz = cz;

    const wanted = new Set();
    for (let dx = -VIEW_CHUNKS; dx <= VIEW_CHUNKS; dx++) {
      for (let dz = -VIEW_CHUNKS; dz <= VIEW_CHUNKS; dz++) {
        const key = `${cx + dx},${cz + dz}`;
        wanted.add(key);
        if (!chunks.has(key)) chunks.set(key, buildChunk(cx + dx, cz + dz));
      }
    }
    for (const [key, holder] of chunks) {
      if (wanted.has(key)) continue;
      group.remove(holder);
      holder.traverse(o => { if (o.geometry && o.geometry !== treeGeometry && o.geometry !== trunkGeometry) o.geometry.dispose(); });
      chunks.delete(key);
    }
  }

  // Keeps trees off the spot where the party appears (set before the first chunks are built).
  const setClearing = (x, z, r) => { clearing.x = x; clearing.z = z; clearing.r = r; };

  return { group, heightAt, biomeAt, terrainAt, moistureAt, update, setClearing, waterLevel: WATER_LEVEL, seed };
}
