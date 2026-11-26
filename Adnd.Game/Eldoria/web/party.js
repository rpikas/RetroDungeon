import { buildFigure } from './figure.js';

// The party in formation: three in the front row, up to three in a second row behind. The dead are dragged along.
export function createParty(THREE, world, members) {
  const group = new THREE.Group();
  const infos = (members.length ? members : [{ name: 'Hero', class: 'Fighter' }]).slice(0, 6);
  const figures = infos.map((m, i) => {
    const fig = buildFigure(THREE, m, i);
    group.add(fig);
    return fig;
  });

  // The anchor is the centre of the front row; the camera follows it.
  const leader = new THREE.Object3D();
  group.add(leader);
  let heading = 0;
  let walk = 0;
  const dead = figures.map(() => false);
  figures.forEach(f => { f.rotation.order = 'YXZ'; });

  const ground = (x, z) => Math.max(world.heightAt(x, z), world.waterLevel - 0.4);

  // Spawn on open, gently sloping dry land: every spot of the formation (and a margin) must be dry and level.
  const goodSpot = (x, z) => {
    const h0 = world.heightAt(x, z);
    if (h0 < world.waterLevel + 2.5 || world.moistureAt(x, z) > 0.5) return false;
    for (let a = 0; a < 8; a++) {
      const px = x + Math.cos(a * 0.785) * 7, pz = z + Math.sin(a * 0.785) * 7;
      const h = world.heightAt(px, pz);
      if (h < world.waterLevel + 1.8 || Math.abs(h - h0) > 2.2) return false;
    }
    return true;
  };
  let sx = 0, sz = 0;
  for (let i = 0; i < 4000 && !goodSpot(sx, sz); i++) {
    const a = i * 2.399963, r = 6 * Math.sqrt(i);
    sx = Math.cos(a) * r;
    sz = Math.sin(a) * r;
  }
  world.setClearing(sx, sz, 12);
  leader.position.set(sx, ground(sx, sz), sz);

  // Rotation about x: negative swings a hanging limb forward, positive swings it back.
  function pose(fig, phase, amount) {
    const { legs, arms, body, armed } = fig.userData;
    const armSwing = armed ? 0.25 : 0.55;
    legs.forEach((leg, i) => {
      const p = phase + i * Math.PI;
      leg.hip.rotation.x = -Math.sin(p) * 0.55 * amount + (1 - amount) * leg.hip.rotation.x * 0.8 * (amount === 0 ? 1 : 0);
      leg.knee.rotation.x = Math.max(0, Math.cos(p)) * 0.85 * amount + (amount === 0 ? leg.knee.rotation.x * 0.8 : 0);
    });
    arms.forEach((arm, i) => {
      const p = phase + (1 - i) * Math.PI;   // arm opposes the leg on its own side
      arm.shoulder.rotation.x = -Math.sin(p) * armSwing * amount + (amount === 0 ? arm.shoulder.rotation.x * 0.8 : 0);
      arm.elbow.rotation.x = -0.15 - Math.max(0, Math.sin(p)) * 0.5 * amount;
    });
    body.position.y = amount === 0 ? body.position.y * 0.8 : Math.abs(Math.cos(phase)) * 0.035;
    body.rotation.y = Math.sin(phase) * 0.05 * amount;
  }

  // Formation offsets in the party's own frame: x across the row, z rows behind the front.
  // Only the living stand in formation.
  const rowSpacing = 1.7;
  const rowGap = 2.3;
  const BODY_LENGTH = 1.8;

  function layout(moving) {
    const sin = Math.sin(heading), cos = Math.cos(heading);
    const alive = figures.map((_, i) => i).filter(i => !dead[i]);
    const slot = new Map();
    alive.forEach((fi, k) => {
      const row = Math.floor(k / 3);
      const inRow = row === 0 ? Math.min(3, alive.length) : alive.length - 3;
      slot.set(fi, { x: ((k % 3) - (inRow - 1) / 2) * rowSpacing, z: row * rowGap });
    });
    const world3 = o => ({
      x: leader.position.x + cos * o.x - sin * o.z,
      z: leader.position.z - sin * o.x - cos * o.z
    });

    alive.forEach((fi, k) => {
      const fig = figures[fi];
      const w = world3(slot.get(fi));
      fig.position.set(w.x, ground(w.x, w.z), w.z);
      fig.rotation.set(0, heading, 0);
      fig.userData.arms.forEach(arm => { arm.shoulder.rotation.z = 0; });
      pose(fig, walk + k * 0.9, moving ? 1 : 0);
    });

    // The dead lie on their backs, head first, dragged by the living from the rear.
    const taken = new Map();
    let n = 0;
    figures.forEach((fig, i) => {
      if (!dead[i]) return;
      let wx, wz, side = 0;
      if (alive.length) {
        const puller = alive[alive.length - 1 - (n % alive.length)];
        const times = taken.get(puller) ?? 0;
        taken.set(puller, times + 1);
        side = times === 0 ? 0 : (times % 2 ? 1 : -1) * 0.55 * Math.ceil(times / 2);
        const o = slot.get(puller);
        const w = world3({ x: o.x + side, z: o.z + BODY_LENGTH + 0.5 + Math.abs(side) * 0.3 });
        wx = w.x;
        wz = w.z;
        const pf = figures[puller];
        pf.userData.arms.forEach(arm => {
          arm.shoulder.rotation.x = 0.7;
          arm.elbow.rotation.x = -0.3;
        });
      } else {
        const w = world3({ x: (n - (figures.length - 1) / 2) * 0.9, z: 0 });
        wx = w.x;
        wz = w.z;
      }
      n++;
      fig.position.set(wx, ground(wx, wz) + 0.18, wz);
      fig.rotation.set(-Math.PI / 2, heading + Math.PI, 0);
      const { legs, arms, body } = fig.userData;
      legs.forEach(l => { l.hip.rotation.x = 0; l.knee.rotation.x = 0; });
      arms.forEach((arm, j) => { arm.shoulder.rotation.x = 0; arm.shoulder.rotation.z = (j === 0 ? 1 : -1) * 0.25; arm.elbow.rotation.x = 0; });
      body.position.y = 0;
      body.rotation.y = 0;
    });
  }
  function move(dx, dz, dt) {
    const nx = leader.position.x + dx, nz = leader.position.z + dz;
    // Deep water blocks walking; shallows are wadeable.
    if (world.heightAt(nx, nz) > world.waterLevel - 1.2) {
      leader.position.x = nx;
      leader.position.z = nz;
    }
    leader.position.y = ground(leader.position.x, leader.position.z);

    // Turn smoothly toward the direction of travel so the rows swing round like a column.
    let diff = Math.atan2(dx, dz) - heading;
    while (diff > Math.PI) diff -= Math.PI * 2;
    while (diff < -Math.PI) diff += Math.PI * 2;
    heading += diff * Math.min(1, dt * 6);

    walk += dt * 8;
    layout(true);
  }

  function idle() {
    layout(false);
  }

  layout(false);
  function setDead(flags) {
    figures.forEach((_, i) => { dead[i] = !!flags[i]; });
  }

  return { group, leader, figures, move, idle, setDead };
}
