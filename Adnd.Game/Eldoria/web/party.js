import { buildFigure } from './figure.js';

// The party in formation: three in the front row, up to three in a second row behind.
export function createParty(THREE, world, members) {
  const group = new THREE.Group();
  const infos = (members.length ? members : [{ name: 'Hero', class: 'Fighter' }]).slice(0, 6);
  const figures = infos.map((m, i) => {
    const fig = buildFigure(THREE, m, i);
    group.add(fig);
    return fig;
  });

  // Formation offsets in the party's own frame: x across the row, z rows behind the front.
  const rowSpacing = 1.7;
  const rowGap = 2.3;
  const offsets = figures.map((_, i) => {
    const row = Math.floor(i / 3);
    const inRow = row === 0 ? Math.min(3, figures.length) : figures.length - 3;
    return { x: ((i % 3) - (inRow - 1) / 2) * rowSpacing, z: row * rowGap };
  });

  // The anchor is the centre of the front row; the camera follows it.
  const leader = new THREE.Object3D();
  group.add(leader);
  let heading = 0;
  let walk = 0;

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

  function layout(moving) {
    const sin = Math.sin(heading), cos = Math.cos(heading);
    figures.forEach((fig, i) => {
      const o = offsets[i];
      const wx = leader.position.x + cos * o.x - sin * o.z;
      const wz = leader.position.z - sin * o.x - cos * o.z;
      fig.position.set(wx, ground(wx, wz), wz);
      fig.rotation.y = heading;
      pose(fig, walk + i * 0.9, moving ? 1 : 0);
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
  return { group, leader, move, idle };
}
