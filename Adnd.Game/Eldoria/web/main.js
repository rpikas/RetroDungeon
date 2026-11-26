import * as THREE from './lib/three.module.js';
import { createWorld } from './world.js';
import { createParty } from './party.js';
import { bridge } from './bridge.js';
import { createCreature } from './creatures.js';
import { createCombatFx } from './combatfx.js';

const renderer = new THREE.WebGLRenderer({ antialias: true });
renderer.setPixelRatio(Math.min(Math.max(window.devicePixelRatio, 1.5), 2));
renderer.setSize(window.innerWidth, window.innerHeight);
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFSoftShadowMap;
renderer.toneMapping = THREE.ACESFilmicToneMapping;
renderer.toneMappingExposure = 0.9;
document.body.prepend(renderer.domElement);

const scene = new THREE.Scene();
const camera = new THREE.PerspectiveCamera(60, window.innerWidth / window.innerHeight, 0.1, 2500);

let world = null;
let party = null;

// Camera orbit around the party leader.
const view = { yaw: 0, pitch: 0.45, distance: 9 };
const keys = new Map();
// NumLock toggles walking forward on its own; it lasts through encounters until another key is pressed or 10 minutes pass.
let autoWalk = false;
let autoWalkSince = 0;
const AUTOWALK_MS = 10 * 60 * 1000;

// Simple stand-in creatures shown in front of the party when an encounter starts.
let monsterGroup = null;
let fx = null;
let showSim = localStorage.getItem('eldoria.showSim') !== '0';
const fxBar = document.getElementById('fxbar');
const fxSpeedLabel = document.getElementById('fxSpeed');
const fxToggle = document.getElementById('fxToggle');
function refreshFxBar() {
  fxSpeedLabel.textContent = (fx ? fx.speed : 1) + 'x';
  fxToggle.textContent = 'Show: ' + (showSim ? 'on' : 'off');
}
function changeSpeed(factor) { if (fx) { fx.setSpeed(fx.speed * factor); refreshFxBar(); } }
function toggleSim() { showSim = !showSim; localStorage.setItem('eldoria.showSim', showSim ? '1' : '0'); if (!showSim) fx?.skip(); refreshFxBar(); }
function hideDeadMonsters(alive) {
  alive?.forEach((a, i) => { if (!a && monsterGroup?.children.length) monsterGroup.children[i % monsterGroup.children.length].visible = false; });
}
document.getElementById('fxSlow').onclick = () => changeSpeed(0.5);
document.getElementById('fxFast').onclick = () => changeSpeed(2);
document.getElementById('fxSkip').onclick = () => fx?.skip();
fxToggle.onclick = toggleSim;
function clearMonsters() {
  if (monsterGroup) { scene.remove(monsterGroup); monsterGroup = null; }
}
function showMonsters(name, count) {
  clearMonsters();
  if (!party) return;
  monsterGroup = new THREE.Group();
  const origin = party.leader.position;
  const fx = -Math.sin(view.yaw), fz = -Math.cos(view.yaw);
  const shown = Math.min(count, 8);
  const proto = createCreature(THREE, name);
  const big = proto.scale.x > 1.4;
  for (let i = 0; i < shown; i++) {
    const side = (i % 4) - (Math.min(shown, 4) - 1) / 2;
    const row = Math.floor(i / 4);
    const spread = big ? 5 : 2.8;
    const x = origin.x + fx * (9 + row * 3.5) - fz * side * spread;
    const z = origin.z + fz * (9 + row * 3.5) + fx * side * spread;
    const m = i === 0 ? proto : createCreature(THREE, name);
    m.position.set(x, world.heightAt(x, z), z);
    m.rotation.y = Math.atan2(origin.x - x, origin.z - z);
    m.userData.phase = i * 0.9;
    monsterGroup.add(m);
  }
  scene.add(monsterGroup);
  addLog(`${count} x ${name} blocks the way!`);
}

function start(info) {
  if (world) {
    scene.remove(world.group);
    scene.remove(party.group);
  }

  world = createWorld(THREE, scene, info.seed ?? 1, renderer);
  party = createParty(THREE, world, info.members ?? []);
  party.setDead((info.members ?? []).map(m => !!m.dead));
  scene.add(party.group);
  fx = createCombatFx(THREE, scene, {
    party: i => party.figures[i] ?? null,
    partyCount: () => party.figures.length,
    monster: i => (monsterGroup && monsterGroup.children.length ? monsterGroup.children[i % monsterGroup.children.length] : null),
    monsterCount: () => (monsterGroup ? monsterGroup.children.length : 0),
    ground: (x, z) => world.heightAt(x, z)
  });

  refreshFxBar();
  const list = document.getElementById('party');
  list.innerHTML = (info.members ?? []).map(m => `${m.dead ? '&#10013; ' : ''}${m.name} <span style="opacity:.7">${m.class ?? ''}</span>`).join('<br>');
}

bridge.onMessage(msg => {
  if (msg.type === 'party') start(msg);
  else if (msg.type === 'status') party?.setDead(msg.dead ?? []);
  else if (msg.type === 'log') addLog(msg.text);
  else if (msg.type === 'round') {
    const done = () => bridge.post({ type: 'roundDone' });
    if (fx && showSim) fx.play(msg.steps, msg.monstersAlive, done); else { fx?.skip(); hideDeadMonsters(msg.monstersAlive); done(); }
  }
  else if (msg.type === 'linger') fx?.setLingering(msg.list);
  else if (msg.type === 'monsters') showMonsters(msg.name, msg.count);
  else if (msg.type === 'resume') { fx?.clearLingering(); clearMonsters(); paused = false; keys.clear(); }
});

// Held keys are remembered with the time of their last keydown. Auto-repeat refreshes it, so a key whose
// release was never delivered (focus changes inside WebView2) stops counting after a short while.
const HELD_MS = 700;
const normalize = e => (e.code?.startsWith('Key') ? e.code.slice(3) : e.code?.replace('Shift', 'shift')).toLowerCase();
const isHeld = k => keys.has(k) && performance.now() - keys.get(k) < HELD_MS;

window.addEventListener('keydown', e => {
  if (e.code === 'NumLock') {
    e.preventDefault();
    if (!e.repeat) { autoWalk = !autoWalk; autoWalkSince = performance.now(); }
    return;
  }
  if (paused && fx?.busy) {
    if (e.key === '[' || e.key === '-') { changeSpeed(0.5); e.preventDefault(); return; }
    if (e.key === ']' || e.key === '+' || e.key === '=') { changeSpeed(2); e.preventDefault(); return; }
    if (e.code === 'Space' || e.code === 'Enter') { fx.skip(); e.preventDefault(); return; }
  }
  if (e.code === 'KeyV' && paused) { toggleSim(); e.preventDefault(); return; }
  if (!/^(Shift|Control|Alt|Meta|CapsLock|ScrollLock)/.test(e.code)) autoWalk = false;
  if (e.key === 'Escape') {
    bridge.post({ type: 'exit' });
    return;
  }
  e.preventDefault();
  // Only the last-pressed key auto-repeats, so a repeat of it keeps the other held keys alive too.
  const now = performance.now();
  for (const k of keys.keys()) keys.set(k, now);
  keys.set(normalize(e), now);
}, true);
window.addEventListener('keyup', e => { keys.delete(normalize(e)); }, true);
window.addEventListener('blur', () => keys.clear());
document.addEventListener('visibilitychange', () => keys.clear());
let dragging = false;
renderer.domElement.addEventListener('pointerdown', e => { dragging = true; renderer.domElement.setPointerCapture(e.pointerId); });
renderer.domElement.addEventListener('pointerup', () => { dragging = false; });
renderer.domElement.addEventListener('pointermove', e => {
  if (!dragging) return;
  view.yaw -= e.movementX * 0.005;
  view.pitch = Math.min(1.35, Math.max(0.08, view.pitch + e.movementY * 0.005));
});
renderer.domElement.addEventListener('wheel', e => {
  view.distance = Math.min(40, Math.max(3, view.distance * (1 + Math.sign(e.deltaY) * 0.1)));
});
window.addEventListener('resize', () => {
  camera.aspect = window.innerWidth / window.innerHeight;
  camera.updateProjectionMatrix();
  renderer.setSize(window.innerWidth, window.innerHeight);
});

const clock = new THREE.Clock();
let sinceReport = 0;
// An encounter check is made every CHECK_DISTANCE world units walked; the game decides if one happens.
const CHECK_DISTANCE = 20;
let walked = 0;
let paused = false;
const logBox = document.getElementById('log');
function addLog(text) {
  const line = document.createElement('div');
  line.textContent = text;
  logBox.appendChild(line);
  while (logBox.childElementCount > 80) logBox.firstChild.remove();
  logBox.scrollTop = logBox.scrollHeight;
}
const posLabel = document.getElementById('pos');

function frame() {
  const dt = Math.min(clock.getDelta(), 0.1);

  if (world && party && !paused) {
    // Movement is relative to the camera heading.
    if (autoWalk && performance.now() - autoWalkSince > AUTOWALK_MS) autoWalk = false;
    let forward = autoWalk ? 1 : 0;
    let strafe = 0;
    if (isHeld('w') || isHeld('arrowup')) forward = 1;
    if (isHeld('s') || isHeld('arrowdown')) forward -= 1;
    if (isHeld('d') || isHeld('arrowright')) strafe += 1;
    if (isHeld('a') || isHeld('arrowleft')) strafe -= 1;

    if (forward !== 0 || strafe !== 0) {
      const speed = (isHeld('shiftleft') || isHeld('shiftright')) ? 9 : 4.5;
      const length = Math.hypot(forward, strafe);
      const sin = Math.sin(view.yaw);
      const cos = Math.cos(view.yaw);
      const dx = (-sin * forward + cos * strafe) / length;
      const dz = (-cos * forward - sin * strafe) / length;
      const before = party.leader.position.clone();
      party.move(dx * speed * dt, dz * speed * dt, dt);
      walked += before.distanceTo(party.leader.position);
      if (walked >= CHECK_DISTANCE) {
        walked = 0;
        const p = party.leader.position;
        const terrain = world.terrainAt(p.x, p.z);
        if (terrain !== 'Water' && bridge.attached) {
          paused = true;
          bridge.post({ type: 'encounterCheck', terrain, x: p.x, z: p.z });
        }
      }
    } else {
      party.idle(dt);
    }

    if (monsterGroup) for (const m of monsterGroup.children) m.userData.tick?.(performance.now() / 1000 + m.userData.phase);
    world.update(dt, party.leader.position);

    const target = party.leader.position;
    sinceReport += dt;
    posLabel.textContent = `Position ${target.x.toFixed(0)}, ${target.z.toFixed(0)} · ${world.biomeAt(target.x, target.z)} · ${world.terrainAt(target.x, target.z)}`;
    if (sinceReport > 1) {
      sinceReport = 0;
      bridge.post({ type: 'position', x: target.x, z: target.z });
    }
  }

  fx?.tick(dt);
  fxBar.classList.toggle('idle', !(fx && fx.busy));
  // The camera works during battle too, so the fight can be spun around with the mouse or Q / E.
  if (world && party) {
    if (isHeld('q')) view.yaw += dt * 1.6;
    if (isHeld('e')) view.yaw -= dt * 1.6;
    const target = party.leader.position.clone();
    if (paused && monsterGroup?.children.length) {
      const mid = new THREE.Vector3();
      monsterGroup.children.forEach(c => mid.add(c.position));
      mid.divideScalar(monsterGroup.children.length);
      target.lerp(mid, 0.5);
    }
    const horizontal = Math.cos(view.pitch) * view.distance;
    camera.position.set(
      target.x + Math.sin(view.yaw) * horizontal,
      target.y + 1.6 + Math.sin(view.pitch) * view.distance,
      target.z + Math.cos(view.yaw) * horizontal);
    const minHeight = world.heightAt(camera.position.x, camera.position.z) + 0.8;
    if (camera.position.y < minHeight) camera.position.y = minHeight;
    camera.lookAt(target.x, target.y + 1.4, target.z);
  }
  renderer.render(scene, camera);
  requestAnimationFrame(frame);
}

bridge.post({ type: 'ready' });
// Opened in a plain browser (no game attached): a demo party so the world can still be looked at.
if (!bridge.attached) {
  start({ seed: 1, members: [{ name: 'Aragorn', class: 'Ranger' }, { name: 'Gandalf', class: 'Magic-User' }, { name: 'Gimli', class: 'Fighter' }] });
}
requestAnimationFrame(frame);
