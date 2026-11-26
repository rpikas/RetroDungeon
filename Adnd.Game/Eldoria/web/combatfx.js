// Plays one combat round on the overland: attackers lunge, blows land or miss, spells show their effect.
const STEP_HIT = 1.7;
const STEP_SPELL = 2.8;

export function createCombatFx(THREE, scene, lookup) {
  // lookup.party(i) / lookup.monster(i) return the Object3D standing for that combatant, or null.
  const effects = [];     // short-lived visuals: { obj, update(p), life, age }

  const tmp = new THREE.Vector3();

  function textSprite(text, color) {
    const canvas = document.createElement('canvas');
    const font = 'bold 64px Georgia, serif';
    const ctx = canvas.getContext('2d');
    ctx.font = font;
    const cw = Math.max(256, Math.ceil(ctx.measureText(text).width) + 40);
    canvas.width = cw; canvas.height = 96;
    ctx.font = font;
    ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
    ctx.lineWidth = 8; ctx.strokeStyle = '#000';
    ctx.strokeText(text, cw / 2, 50);
    ctx.fillStyle = color;
    ctx.fillText(text, cw / 2, 50);
    const tex = new THREE.CanvasTexture(canvas);
    const sprite = new THREE.Sprite(new THREE.SpriteMaterial({ map: tex, transparent: true, depthTest: false }));
    sprite.scale.set(2.6 * cw / 256, 0.98, 1);
    sprite.renderOrder = 10;
    return sprite;
  }

  function add(obj, life, update) {
    scene.add(obj);
    effects.push({ obj, life, age: 0, update });
  }

  function headOf(o) {
    const p = o.getWorldPosition(new THREE.Vector3());
    const box = new THREE.Box3().setFromObject(o);
    p.y = Math.max(p.y + 1, box.max.y + 0.3);
    return p;
  }

  function floatText(o, text, color) {
    const s = textSprite(text, color);
    s.position.copy(headOf(o));
    const y0 = s.position.y;
    add(s, 1.3, p => { s.position.y = y0 + p * 1.6; s.material.opacity = p < 0.7 ? 1 : (1 - p) / 0.3; });
  }

  function sparks(pos, color, count = 14) {
    const geo = new THREE.SphereGeometry(0.07, 6, 6);
    const mat = new THREE.MeshBasicMaterial({ color, transparent: true });
    const group = new THREE.Group();
    const vel = [];
    for (let i = 0; i < count; i++) {
      const m = new THREE.Mesh(geo, mat);
      group.add(m);
      vel.push(new THREE.Vector3((Math.random() - 0.5) * 3, Math.random() * 3 + 0.5, (Math.random() - 0.5) * 3));
    }
    group.position.copy(pos);
    add(group, 0.6, p => {
      group.children.forEach((m, i) => m.position.set(vel[i].x * p, vel[i].y * p - 4 * p * p, vel[i].z * p));
      mat.opacity = 1 - p;
    });
  }

  function centerOf(side, count) {
    const list = [];
    for (let i = 0; i < count; i++) {
      const o = side === 'p' ? lookup.party(i) : lookup.monster(i);
      if (o && o.visible) list.push(o);
    }
    const c = new THREE.Vector3();
    if (!list.length) return c;
    list.forEach(o => c.add(o.getWorldPosition(tmp)));
    return c.multiplyScalar(1 / list.length);
  }

  function groupInfo(side) {
    const n = side === 'p' ? lookup.partyCount() : lookup.monsterCount();
    const c = centerOf(side, n);
    const other = centerOf(side === 'p' ? 'm' : 'p', side === 'p' ? lookup.monsterCount() : lookup.partyCount());
    const toward = other.clone().sub(c); toward.y = 0;
    if (toward.lengthSq() < 0.01) toward.set(0, 0, 1);
    toward.normalize();
    let width = 3;
    for (let i = 0; i < n; i++) {
      const o = side === 'p' ? lookup.party(i) : lookup.monster(i);
      if (o) width = Math.max(width, o.position.distanceTo(c) * 2 + 2);
    }
    return { center: c, toward, width };
  }

  const flameMat = (hex) => new THREE.MeshBasicMaterial({ color: hex, transparent: true, opacity: 0.9, blending: THREE.AdditiveBlending, depthWrite: false });

  // Spells that keep burning for several rounds live here instead of in the short-lived effects list.
  const lingers = new Map();
  function emit(kind, obj, fn) {
    if (!kind) { add(obj, STEP_SPELL, fn); return; }
    scene.add(obj);
    lingers.set(kind, { obj, fn, age: 0, fade: null });
  }

  const spellFx = {
    firewall(info, linger) {
      const g = new THREE.Group();
      const side = new THREE.Vector3(-info.toward.z, 0, info.toward.x);
      const w = info.width + 2, d = info.width * 0.8 + 3;
      const flames = [];
      const count = Math.min(130, Math.round(w * d * 1.6));
      for (let i = 0; i < count; i++) {
        const u = (Math.random() - 0.5) * w, v = (Math.random() - 0.5) * d;
        const h = 3.2 + Math.random() * 2.8;
        for (const [hex, s] of [[0xe02800, 1], [0xff7a10, 0.72], [0xffd040, 0.42]]) {
          const f = new THREE.Mesh(new THREE.ConeGeometry(0.55 * s, h * s, 7, 1, true), flameMat(hex));
          f.position.copy(side).multiplyScalar(u).addScaledVector(info.toward, v);
          f.position.y = h * s / 2;
          f.userData.seed = Math.random() * 10; f.userData.h = h * s;
          g.add(f); flames.push(f);
        }
      }
      const lights = [];
      for (let i = 0; i < 3; i++) {
        const l = new THREE.PointLight(0xff6a10, 12, 26);
        l.position.copy(side).multiplyScalar((i - 1) * w * 0.3); l.position.y = 3;
        g.add(l); lights.push(l);
      }
      g.position.copy(info.center);
      emit(linger, g, p => {
        const grow = Math.min(1, p * 4) * (p > 0.85 ? (1 - p) / 0.15 : 1);
        const now = performance.now();
        flames.forEach(f => {
          const s = f.userData.seed;
          const k = 0.8 + 0.4 * Math.sin(now / 60 + s);
          const hh = grow * (0.75 + 0.5 * Math.abs(Math.sin(now / 85 + s * 2)));
          f.scale.set(grow * k, hh, grow * k);
          f.position.y = f.userData.h * hh / 2;
          f.rotation.z = Math.sin(now / 140 + s) * 0.12;
        });
        lights.forEach(l => { l.intensity = 12 * grow * (0.85 + 0.15 * Math.sin(now / 50)); });
      });
    },

    blades(info) {
      const g = new THREE.Group();
      const side = new THREE.Vector3(-info.toward.z, 0, info.toward.x);
      const steel = new THREE.MeshStandardMaterial({ color: 0xeef3f8, metalness: 0.95, roughness: 0.15, emissive: 0x88aaff, emissiveIntensity: 0.6 });
      const w = info.width + 3, depth = info.width * 0.8 + 3;
      const n = Math.max(6, Math.round(w / 1.3));
      const spinners = [];
      for (let i = 0; i < n; i++) {
        const s = new THREE.Group();
        for (let k = 0; k < 4; k++) {
          const blade = new THREE.Mesh(new THREE.ConeGeometry(0.2, 1.1, 4), steel);
          blade.scale.z = 0.18;
          blade.rotation.z = -Math.PI / 2;
          blade.position.x = 0.55;
          const arm = new THREE.Group();
          arm.rotation.y = k * Math.PI / 2;
          arm.add(blade); s.add(arm);
        }
        s.userData.u = (i / (n - 1) - 0.5) * w;
        s.userData.y = 0.7 + (i % 2) * 0.9;
        s.userData.ph = Math.random() * 6;
        g.add(s); spinners.push(s);
      }
      const glow = new THREE.PointLight(0xaaccff, 6, 14);
      glow.position.y = 1.5; g.add(glow);
      g.position.copy(info.center);
      add(g, STEP_SPELL, p => {
        const fade = Math.min(1, p * 6) * (p > 0.9 ? (1 - p) / 0.1 : 1);
        // the row sweeps back and forth through the monsters
        const v = Math.sin(p * Math.PI * 3) * depth * 0.5;
        spinners.forEach(s => {
          s.position.copy(side).multiplyScalar(s.userData.u).addScaledVector(info.toward, v);
          s.position.y = s.userData.y + Math.sin(p * 20 + s.userData.ph) * 0.15;
          s.rotation.y = p * 90 + s.userData.ph;
          s.rotation.x = 0.25 * Math.sin(p * 12 + s.userData.ph);
          s.scale.setScalar(fade);
        });
        glow.intensity = 6 * fade;
        if (Math.random() < 0.5) {
          const pos = info.center.clone().addScaledVector(side, (Math.random() - 0.5) * w).addScaledVector(info.toward, v);
          pos.y = 1.2; sparks(pos, 0xff3030, 6);
        }
      });
    },
    insects(info, linger) {
      const count = 260;
      const pos = new Float32Array(count * 3);
      const seed = [];
      for (let i = 0; i < count; i++) seed.push({ a: Math.random() * 6.28, r: Math.random(), y: Math.random(), s: 2 + Math.random() * 5 });
      const geo = new THREE.BufferGeometry();
      geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
      const pts = new THREE.Points(geo, new THREE.PointsMaterial({ color: 0x1a1208, size: 0.13, sizeAttenuation: true, depthWrite: false }));
      pts.frustumCulled = false;
      const radius = Math.max(2.5, info.width * 0.55);
      pts.position.copy(info.center);
      emit(linger, pts, p => {
        const t = performance.now() / 1000;
        for (let i = 0; i < count; i++) {
          const s = seed[i];
          const a = s.a + t * s.s * 0.5;
          const r = s.r * radius * Math.min(1, p * 4);
          pos[i * 3] = Math.cos(a) * r + Math.sin(t * s.s * 3) * 0.25;
          pos[i * 3 + 1] = s.y * 3.2 + Math.sin(t * s.s * 4 + i) * 0.3;
          pos[i * 3 + 2] = Math.sin(a) * r + Math.cos(t * s.s * 3) * 0.25;
        }
        geo.attributes.position.needsUpdate = true;
        pts.material.opacity = 1;
        pts.visible = p < 0.95;
      });
    },

    cloud(info, hex, linger) {
      const m = new THREE.Mesh(new THREE.SphereGeometry(1, 20, 14), flameMat(hex));
      m.position.copy(info.center).add(new THREE.Vector3(0, 1.4, 0));
      const light = new THREE.PointLight(hex, 5, 18);
      m.add(light);
      const r = Math.max(2.5, info.width * 0.65);
      emit(linger, m, p => {
        const k = Math.min(1, p * 4) * (p > 0.85 ? (1 - p) / 0.15 : 1);
        m.scale.set(r * k * 1.1, r * k * 0.7, r * k * 1.1);
        m.material.opacity = 0.35 + 0.1 * Math.sin(performance.now() / 300);
        light.intensity = 5 * k;
      });
    },

    burst(info, hex) {
      const k = info.k ?? 1;
      const m = new THREE.Mesh(new THREE.SphereGeometry(1, 20, 14), flameMat(hex));
      m.position.copy(info.center).add(new THREE.Vector3(0, 1.2, 0));
      const light = new THREE.PointLight(hex, 8 * k, 20 + 8 * k);
      m.add(light);
      const max = Math.max(2.5, info.width * 0.6) * (0.7 + k * 0.6);
      add(m, 1.4, p => { m.scale.setScalar(0.3 + p * max); m.material.opacity = 0.9 * (1 - p); light.intensity = 8 * k * (1 - p); });
      sparks(m.position, hex, Math.round(24 * k));
    },

    bolt(info, hex) {
      const k = info.k ?? 1;
      const n = Math.max(1, Math.round(k * 1.6));
      for (let b = 0; b < n; b++) {
        const pts = [];
        for (let i = 0; i <= 8; i++) pts.push(new THREE.Vector3((Math.random() - 0.5) * 0.8, 14 - i * 1.7, (Math.random() - 0.5) * 0.8));
        const line = new THREE.Line(new THREE.BufferGeometry().setFromPoints(pts), new THREE.LineBasicMaterial({ color: hex }));
        line.position.copy(info.center);
        if (b > 0) line.position.add(new THREE.Vector3((Math.random() - 0.5) * info.width, 0, (Math.random() - 0.5) * info.width));
        const light = new THREE.PointLight(hex, 10 * k, 25);
        light.position.y = 3; line.add(light);
        const delay = b * 0.08;
        add(line, 0.7 + delay, p => { const t = p * (0.7 + delay) - delay; line.visible = t > 0 && Math.floor(t * 20) % 2 === 0; light.intensity = 10 * k * Math.max(0, 1 - t / 0.7); });
        sparks(line.position.clone().add(new THREE.Vector3(0, 0.5, 0)), hex, 16);
      }
    },

    magic(info, hex) {
      const k = info.k ?? 1;
      const rings = Math.max(1, Math.round(k));
      const max = Math.max(2, info.width * 0.6) * (0.7 + k * 0.5);
      for (let r = 0; r < rings; r++) {
        const ring = new THREE.Mesh(new THREE.TorusGeometry(1, 0.06 + 0.02 * k, 8, 40), flameMat(hex));
        ring.rotation.x = Math.PI / 2;
        const y0 = info.center.y + 0.3;
        ring.position.copy(info.center).setY(y0);
        const delay = r * 0.18;
        add(ring, 1.4 + delay, p => { const t = clamp01((p * (1.4 + delay) - delay) / 1.4); ring.visible = t > 0; ring.scale.setScalar(0.3 + t * max); ring.position.y = y0 + t * 2.5; ring.material.opacity = 0.9 * (1 - t); });
      }
      sparks(info.center.clone().add(new THREE.Vector3(0, 0.6, 0)), hex, Math.round(20 * k));
    },

    // Tall tangled briars with long thorns burst out of the ground across the whole area.
    thorns(info) {
      const k = info.k ?? 1;
      const g = new THREE.Group();
      g.position.copy(info.center);
      const side = new THREE.Vector3(-info.toward.z, 0, info.toward.x);
      const w = info.width + 3, d = info.width * 0.8 + 3;
      const vineMat = new THREE.MeshStandardMaterial({ color: 0x2c5a1c, roughness: 0.8, emissive: 0x0c2a08, emissiveIntensity: 0.6 });
      const thornMat = new THREE.MeshStandardMaterial({ color: 0xd8c8a0, roughness: 0.5, emissive: 0x5a4020, emissiveIntensity: 0.5 });
      const thornGeo = new THREE.ConeGeometry(0.07, 0.5, 5);
      const count = Math.min(70, Math.round(w * d * 0.9));
      const vines = [];
      for (let i = 0; i < count; i++) {
        const u = (Math.random() - 0.5) * w, v = (Math.random() - 0.5) * d;
        const h = 2.6 + Math.random() * 2.4 + k * 0.3;
        const pts = [];
        const turns = 1.2 + Math.random(), r0 = 0.25 + Math.random() * 0.25, ph = Math.random() * 6.28;
        for (let s = 0; s <= 10; s++) {
          const f = s / 10;
          pts.push(new THREE.Vector3(Math.cos(ph + f * turns * 6.28) * r0 * (1 - f * 0.5), f * h, Math.sin(ph + f * turns * 6.28) * r0 * (1 - f * 0.5)));
        }
        const curve = new THREE.CatmullRomCurve3(pts);
        const vine = new THREE.Group();
        vine.add(new THREE.Mesh(new THREE.TubeGeometry(curve, 24, 0.09, 6, false), vineMat));
        for (let s = 0; s < 14; s++) {
          const f = 0.05 + (s / 14) * 0.95;
          const p = curve.getPoint(f), tan = curve.getTangent(f);
          const th = new THREE.Mesh(thornGeo, thornMat);
          th.position.copy(p);
          const out = new THREE.Vector3(Math.cos(s * 2.4), 0.2, Math.sin(s * 2.4)).normalize();
          th.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), out);
          th.position.addScaledVector(out, 0.2);
          vine.add(th);
        }
        vine.position.copy(side).multiplyScalar(u).addScaledVector(info.toward, v);
        vine.userData.seed = Math.random() * 6; vine.userData.delay = Math.random() * 0.25;
        g.add(vine); vines.push(vine);
      }
      const light = new THREE.PointLight(0x7aff50, 4 * k, 20);
      light.position.y = 2; g.add(light);
      add(g, STEP_SPELL, p => {
        vines.forEach(vn => {
          const grow = ease(clamp01((p - vn.userData.delay) / 0.2)) * (p > 0.88 ? (1 - p) / 0.12 : 1);
          vn.scale.set(grow, Math.max(0.001, grow), grow);
          vn.rotation.z = Math.sin(performance.now() / 400 + vn.userData.seed) * 0.05;
        });
        light.intensity = 4 * k * Math.min(1, p * 5);
      });
      sparks(info.center.clone().add(new THREE.Vector3(0, 0.3, 0)), 0x6aff40, 20);
    },

    // A shower of fireballs over the whole area; bigger spells rain more of them.
    meteors(info) {
      const k = info.k ?? 1;
      const n = Math.round(5 + 7 * k);
      const side = new THREE.Vector3(-info.toward.z, 0, info.toward.x);
      for (let i = 0; i < n; i++) {
        const delay = Math.random() * 1.9;
        const u = (Math.random() - 0.5) * (info.width + 2), v = (Math.random() - 0.5) * (info.width * 0.8 + 2);
        const ground = info.center.clone().addScaledVector(side, u).addScaledVector(info.toward, v);
        const rock = new THREE.Group();
        const core = new THREE.Mesh(new THREE.SphereGeometry(0.45, 10, 8), flameMat(0xffa020));
        const tail = new THREE.Mesh(new THREE.ConeGeometry(0.4, 3.2, 8, 1, true), flameMat(0xff4a10));
        tail.position.y = 1.7;
        rock.add(core, tail);
        const life = delay + 0.55;
        let boom = false;
        add(rock, life, p => {
          const t = p * life - delay;
          rock.visible = t > 0 && t < 0.55;
          const f = clamp01(t / 0.55);
          rock.position.set(ground.x + (1 - f) * 5, ground.y + 0.3 + (1 - f) * 20, ground.z);
          rock.lookAt(ground.x + 5, ground.y + 20, ground.z);
          rock.rotateX(Math.PI / 2);
          if (t >= 0.55 && !boom) { boom = true; spellFx.burst({ center: ground, width: 2.5, k: 0.8 }, 0xff6a1a); }
        });
      }
    },

    // Glowing missiles fly from the caster to the target.
    missiles(info, hex) {
      const k = info.k ?? 1;
      const n = Math.max(1, Math.round(k * 2.2));
      const from = (info.from ?? info.center.clone().add(new THREE.Vector3(0, 0, -6))).clone();
      from.y += 1.6;
      for (let i = 0; i < n; i++) {
        const delay = i * 0.12;
        const orb = new THREE.Mesh(new THREE.SphereGeometry(0.16 + 0.03 * k, 10, 8), flameMat(hex));
        const to = info.center.clone().add(new THREE.Vector3((Math.random() - 0.5) * 0.8, 1.1 + Math.random() * 0.5, (Math.random() - 0.5) * 0.8));
        const bend = new THREE.Vector3((Math.random() - 0.5) * 2, 1 + Math.random(), (Math.random() - 0.5) * 2);
        const life = delay + 0.55;
        let hit = false;
        add(orb, life, p => {
          const t = p * life - delay;
          orb.visible = t > 0 && t < 0.55;
          const f = clamp01(t / 0.55);
          orb.position.lerpVectors(from, to, f).addScaledVector(bend, Math.sin(f * Math.PI));
          if (t >= 0.55 && !hit) { hit = true; sparks(to, hex, 8); }
        });
      }
    },

    // Healing light gathers around the patient; stronger heals give a taller, brighter glow.
    heal(info, hex = 0x7dff9a) {
      const k = info.k ?? 1;
      const g = new THREE.Group();
      g.position.copy(info.center);
      const radius = 0.8 + 0.3 * k;
      const column = new THREE.Mesh(new THREE.CylinderGeometry(radius, radius * 0.9, 3 + 1.2 * k, 20, 1, true), flameMat(hex));
      column.position.y = (3 + 1.2 * k) / 2;
      g.add(column);
      const core = new THREE.Mesh(new THREE.CylinderGeometry(radius * 0.45, radius * 0.45, 3 + 1.2 * k, 12, 1, true), flameMat(0xffffff));
      core.position.y = column.position.y;
      g.add(core);
      const rings = [];
      const nr = 2 + Math.round(k);
      for (let i = 0; i < nr; i++) {
        const ring = new THREE.Mesh(new THREE.TorusGeometry(radius * 1.15, 0.05 + 0.015 * k, 8, 32), flameMat(hex));
        ring.rotation.x = Math.PI / 2;
        g.add(ring); rings.push(ring);
      }
      const motes = [];
      const mg = new THREE.SphereGeometry(0.07 + 0.02 * k, 6, 6);
      const mm = flameMat(0xeaffd0);
      const nm = Math.round(16 + 14 * k);
      for (let i = 0; i < nm; i++) {
        const m = new THREE.Mesh(mg, mm);
        m.userData = { a: Math.random() * 6.28, r: radius * (0.4 + Math.random() * 0.9), s: 0.6 + Math.random(), o: Math.random() };
        g.add(m); motes.push(m);
      }
      const light = new THREE.PointLight(hex, 7 * k, 12 + 4 * k);
      light.position.y = 1.5; g.add(light);
      const life = 2.2;
      add(g, life, p => {
        const env = Math.min(1, p * 5) * (p > 0.75 ? (1 - p) / 0.25 : 1);
        column.material.opacity = 0.55 * env;
        core.material.opacity = 0.7 * env;
        mm.opacity = env;
        column.scale.set(1 + 0.08 * Math.sin(p * 40), 1, 1 + 0.08 * Math.sin(p * 40));
        light.intensity = 7 * k * env;
        rings.forEach((r, i) => { const t = (p * 1.6 + i / rings.length) % 1; r.position.y = t * (2.6 + k); r.scale.setScalar(1 - 0.35 * t); r.material.opacity = 0.9 * env * (1 - t); });
        motes.forEach(m => {
          const t = (p * m.userData.s * 1.6 + m.userData.o) % 1;
          const a = m.userData.a + p * 5;
          m.position.set(Math.cos(a) * m.userData.r, t * (2.8 + k), Math.sin(a) * m.userData.r);
        });
      });
    }
  };
  const isHealing = name => /cure|heal|restor|regenerat|raise dead|resurrect|revive|refresh/.test(name);

  function playSpell(step) {
    const name = (step.spell || '').toLowerCase();
    const caster = step.fs === 'p' ? lookup.party(step.fi) : lookup.monster(step.fi);
    if (caster) {
      const glow = new THREE.PointLight(0x9fb8ff, 5, 8);
      glow.position.copy(headOf(caster));
      add(glow, 1.0, p => { glow.intensity = 5 * Math.sin(p * Math.PI); });
      floatText(caster, step.spell, '#bcd0ff');
    }
    const info = groupInfo(step.ts);
    const power = step.power ?? 0;
    info.k = power > 0 ? Math.min(2.8, Math.max(0.7, 0.6 + Math.sqrt(power) * 0.25)) : 1;
    if (caster) info.from = caster.getWorldPosition(new THREE.Vector3());
    if (step.ti >= 0) {
      const t = step.ts === 'p' ? lookup.party(step.ti) : lookup.monster(step.ti);
      if (t && !/plague|insect|wall|barrier|storm|cloud/.test(name)) { info.center = t.getWorldPosition(new THREE.Vector3()); info.width = 2.5; }
    }
    if (step.ti >= 0 && step.ts === step.fs) {
      // Spells on an ally: the caster touches the target while the light plays.
      if (isHealing(name)) spellFx.heal(info);
      else spellFx.heal(info, 0xffe58a);
      if (step.heal > 0) { const t = step.ts === 'p' ? lookup.party(step.ti) : lookup.monster(step.ti); if (t) floatText(t, '+' + step.heal, '#8dff9a'); }
      return;
    }
    const lk = lingerKindOf(name);
    if (lk && wanted.has(lk)) { startLinger(lk); return; }
    if (/insect/.test(name)) spellFx.insects(info);
    else if (/wall of fire|fire wall/.test(name)) spellFx.firewall(info);
    else if (/blade barrier/.test(name)) spellFx.blades(info);
    else if (/thorn/.test(name)) spellFx.thorns(info);
    else if (/meteor/.test(name)) spellFx.meteors(info);
    else if (/fire|flame|burning|pyro|incendiary/.test(name)) spellFx.burst(info, 0xff6a1a);
    else if (/lightning|shock/.test(name)) spellFx.bolt(info, 0xcfe6ff);
    else if (/ice|cold|frost/.test(name)) spellFx.burst(info, 0x9fe8ff);
    else if (/acid|cloud|death fog|stinking|poison/.test(name)) spellFx.burst(info, 0x8fcf40);
    else if (/missile|orb|arrow/.test(name)) spellFx.missiles(info, 0xc080ff);
    else spellFx.magic(info, 0xc8a0ff);
  }

  const wanted = new Map();       // kind -> monster indices, from the game state
  const pendingKinds = new Set(); // wanted but not yet shown
  let pendingAge = 0;

  function lingerKindOf(name) {
    if (/wall of fire|fire wall/.test(name)) return 'fire';
    if (/summon insect/.test(name)) return 'insects';
    if (/death fog|stinking cloud/.test(name)) return 'fog';
    if (/incendiary cloud/.test(name)) return 'inferno';
    return null;
  }

  function infoForMonsters(indices) {
    const objs = indices.map(i => lookup.monster(i)).filter(o => o && o.visible);
    const c = new THREE.Vector3();
    objs.forEach(o => c.add(o.getWorldPosition(tmp)));
    if (objs.length) c.multiplyScalar(1 / objs.length);
    const party = centerOf('p', lookup.partyCount());
    const toward = party.sub(c); toward.y = 0;
    if (toward.lengthSq() < 0.01) toward.set(0, 0, 1);
    toward.normalize();
    let width = 3;
    objs.forEach(o => { width = Math.max(width, o.position.distanceTo(c) * 2 + 2); });
    return { center: c, toward, width };
  }

  function startLinger(kind) {
    pendingKinds.delete(kind);
    if (lingers.has(kind) || !wanted.has(kind)) return;
    const info = infoForMonsters(wanted.get(kind));
    if (kind === 'fire') spellFx.firewall(info, kind);
    else if (kind === 'insects') spellFx.insects(info, kind);
    else if (kind === 'fog') spellFx.cloud(info, 0x7fcf40, kind);
    else if (kind === 'inferno') spellFx.cloud(info, 0xff6a1a, kind);
  }

  // list: [{ kind, monsters: [indices] }] of effects that are still active in the game.
  function setLingering(list) {
    wanted.clear();
    list.forEach(l => wanted.set(l.kind, l.monsters));
    for (const [kind, l] of lingers) if (!wanted.has(kind) && l.fade === null) l.fade = 0;
    for (const kind of [...pendingKinds]) if (!wanted.has(kind)) pendingKinds.delete(kind);
    for (const kind of wanted.keys()) if (!lingers.has(kind)) pendingKinds.add(kind);
    pendingAge = 0;
  }

  function clearLingering() {
    wanted.clear(); pendingKinds.clear();
    for (const l of lingers.values()) { scene.remove(l.obj); l.obj.traverse?.(o => { o.geometry?.dispose?.(); }); }
    lingers.clear();
  }

  function tickLingering(rawDt, showNow) {
    if (showNow && pendingKinds.size) {
      pendingAge += rawDt;
      if (pendingAge > 0.3) [...pendingKinds].forEach(startLinger);
    }
    for (const [kind, l] of lingers) {
      l.age += rawDt;
      let p;
      if (l.fade === null) p = Math.min(0.5, l.age * 0.4);
      else { l.fade += rawDt / 0.8; p = 0.85 + 0.15 * Math.min(1, l.fade); }
      l.fn(p);
      if (l.fade !== null && l.fade >= 1) {
        scene.remove(l.obj);
        l.obj.traverse?.(o => { o.geometry?.dispose?.(); });
        lingers.delete(kind);
      }
    }
  }

  // ---- fighters: every combatant keeps a rest pose and is moved by summing contributions each frame ------
  const actors = new Map();
  const ease = x => { x = Math.min(1, Math.max(0, x)); return x * x * (3 - 2 * x); };
  const clamp01 = x => Math.min(1, Math.max(0, x));
  const lerp = (a, b, t) => a + (b - a) * t;
  const pick = list => list[Math.floor(Math.random() * list.length)];
  const angleLerp = (a, b, t) => { let d = b - a; while (d > Math.PI) d -= Math.PI * 2; while (d < -Math.PI) d += Math.PI * 2; return a + d * t; };

  function actorOf(o, side) {
    if (!o) return null;
    let a = actors.get(o);
    if (a) return a;
    const ud = o.userData ?? {};
    const arm = ud.weaponArm ?? null;
    const twistObj = ud.torso ?? ud.body ?? null;
    a = {
      o, side,
      home: o.position.clone(), baseYaw: o.rotation.y,
      wander: new THREE.Vector3(), wanderGoal: new THREE.Vector3(), wanderT: Math.random() * 1.5,
      radius: side === 'm' ? 0.9 : 0.6,
      atk: new THREE.Vector3(), rec: new THREE.Vector3(),
      yawGoal: null, roll: 0, pitch: 0, hop: 0, twist: 0, lean: 0,
      arm, armRest: arm ? { x: arm.shoulder.rotation.x, y: arm.shoulder.rotation.y, z: arm.shoulder.rotation.z, ex: arm.elbow ? arm.elbow.rotation.x : 0 } : null,
      armPose: null,
      limbs: arm ? [] : [[ud.armR, 1], [ud.armL, -1]].filter(l => l[0]).map(([sh, mirror]) => ({ sh, mirror, pose: null, rest: { x: sh.rotation.x, y: sh.rotation.y, z: sh.rotation.z } })),
      kickLeg: null,
      twistObj, twistRest: twistObj ? { x: twistObj.rotation.x, y: twistObj.rotation.y } : null,
      legs: ud.legs ?? null,
      lastPos: o.position.clone(), stride: Math.random() * 6, speed: 0,
      attacking: false, casting: false
    };
    actors.set(o, a);
    return a;
  }

  function restoreActor(a) {
    a.o.position.copy(a.home);
    a.o.rotation.set(0, a.baseYaw, 0);
    if (a.arm) {
      a.arm.shoulder.rotation.set(a.armRest.x, a.armRest.y, a.armRest.z);
      if (a.arm.elbow) a.arm.elbow.rotation.x = a.armRest.ex;
    }
    if (a.twistObj) { a.twistObj.rotation.x = a.twistRest.x; a.twistObj.rotation.y = a.twistRest.y; }
    a.limbs.forEach(l => l.sh.rotation.set(l.rest.x, l.rest.y, l.rest.z));
    if (a.legs) a.legs.forEach(l => { l.hip.rotation.x = 0; l.knee.rotation.x = 0; });
  }

  // Poses of the weapon arm over a swing; q runs 0..1 through wind-up, strike and recovery.
  const SWINGS = {
    overhead:   { wind: { x: -2.7, z: -0.15, y: 0, e: -0.9, tw: 0, lean: -0.1 }, hit: { x: -0.35, z: 0.05, y: 0, e: -0.15, tw: 0.1, lean: 0.3 } },
    diagonal:   { wind: { x: -2.1, z: -1.0, y: 0, e: -0.9, tw: -0.5, lean: 0 }, hit: { x: -0.9, z: 0.7, y: 0, e: -0.2, tw: 0.55, lean: 0.15 } },
    horizontal: { wind: { x: -1.35, z: -1.5, y: 0, e: -0.8, tw: -0.7, lean: 0 }, hit: { x: -1.4, z: 0.9, y: 0, e: -0.2, tw: 0.8, lean: 0.05 } },
    thrust:     { wind: { x: 0.5, z: -0.1, y: 0, e: -1.7, tw: -0.35, lean: -0.1 }, hit: { x: -1.5, z: 0.05, y: 0, e: -0.05, tw: 0.25, lean: 0.25 } },
    low:        { wind: { x: 0.3, z: -0.4, y: 0, e: -1.5, tw: -0.3, lean: 0 }, hit: { x: -1.1, z: 0.1, y: 0, e: -0.1, tw: 0.3, lean: 0.3 } }
  };

  // Unarmed blows reuse the weapon swing poses, mirrored for the left arm.
  const FISTS = { punch: 'thrust', hook: 'horizontal', smash: 'overhead' };
  function fistPose(limb, key, q) {
    const s = SWINGS[key];
    const ph = swingPhase(q);
    const m = limb.mirror;
    const blend = (w, h, r) => (ph < 0 ? lerp(r, w, -ph) : lerp(w, h, ph));
    return {
      x: blend(s.wind.x, s.hit.x, limb.rest.x), y: blend(s.wind.y, s.hit.y, limb.rest.y), z: blend(s.wind.z * m, s.hit.z * m, limb.rest.z),
      tw: blend(s.wind.tw * m, s.hit.tw * m, 0), lean: blend(s.wind.lean, s.hit.lean, 0)
    };
  }

  function swingPhase(q) {
    // Slow wind-up, fast strike, then a longer recovery. Returns blend between wind (0) and hit (1) poses.
    if (q < 0.4) return -ease(q / 0.4);          // -1..0: coming up to the wind-up pose
    if (q < 0.6) return ease((q - 0.4) / 0.2);   // 0..1: the strike
    return 1 - ease((q - 0.6) / 0.4) * 2;         // 1..-1: back to rest
  }

  function begin(step) {
    const fromO = step.fs === 'p' ? lookup.party(step.fi) : lookup.monster(step.fi);
    const toO = step.ts === 'p' ? lookup.party(step.ti) : lookup.monster(step.ti);
    const from = actorOf(fromO, step.fs);
    const to = actorOf(toO, step.ts);
    const state = { step, from, to, age: 0, duration: step.t === 'spell' ? STEP_SPELL : STEP_HIT + Math.random() * 0.3, triggered: false };
    if (step.t === 'spell') {
      if (from) from.casting = true;
      state.touch = !!(from && to && from !== to && step.ts === step.fs);
      if (state.touch) {
        const now = x => x.home.clone().add(x.wander);
        const tp = now(to), fp = now(from);
        const dir = tp.clone().sub(fp); dir.y = 0;
        const gap = dir.length();
        dir.normalize();
        state.dir = dir;
        state.goal = dir.clone().multiplyScalar(Math.max(0, gap - 1.15));
      } else {
        playSpell(step);
      }
      return state;
    }
    if (from) from.attacking = true;
    if (from && to) {
      const weapon = String(from.o.userData.weaponKind ?? 'none').toLowerCase();
      const pierce = /spear|dagger|staff|lance|pike|rapier|dart/.test(weapon);
      const canKick = !!(from.legs && from.legs.length === 2);
      if (from.arm) {
        state.style = pierce ? pick(['thrust', 'thrust', 'low', 'overhead']) : pick(['overhead', 'diagonal', 'horizontal', 'diagonal', 'thrust']);
        if (canKick && Math.random() < 0.15) state.style = 'kick';
      } else if (from.limbs.length) {
        state.style = pick(canKick ? ['punch', 'punch', 'hook', 'hook', 'smash', 'kick'] : ['punch', 'hook', 'smash']);
        if (state.style === 'smash') state.limbs = from.limbs;
        else if (state.style !== 'kick') state.limbs = [pick(from.limbs)];
      } else {
        state.style = 'pounce';
      }
      state.kickIdx = Math.random() < 0.5 ? 0 : 1;
      // Strike from a random side: the attacker circles to a point around the target.
      const now = (x) => x.home.clone().add(x.wander);
      const tp = now(to), fp = now(from);
      const dir = tp.clone().sub(fp); dir.y = 0;
      const gap = Math.max(0.01, dir.length());
      dir.normalize();
      const angle = (Math.random() - 0.5) * 2.4;
      const ca = Math.cos(angle), sa = Math.sin(angle);
      const approach = new THREE.Vector3(dir.x * ca - dir.z * sa, 0, dir.x * sa + dir.z * ca);
      const reach = ({ thrust: 1.9, low: 1.9, punch: 1.7, kick: 1.8, smash: 1.6 }[state.style] ?? 1.5) + Math.random() * 0.3;
      const goal = tp.clone().addScaledVector(approach, -reach).sub(fp);
      goal.y = 0;
      if (gap < reach) goal.multiplyScalar(0.3);
      state.goal = goal;
      state.dir = dir;
    }
    return state;
  }

  function impact(state) {
    const { step, from, to } = state;
    if (!to) return;
    const o = to.o;
    const chest = o.getWorldPosition(new THREE.Vector3()).add(new THREE.Vector3(0, 1.1 * Math.max(1, o.scale.y), 0));
    const dir = state.dir ?? new THREE.Vector3(0, 0, 1);
    if (step.t === 'hit') {
      floatText(o, step.dmg > 0 ? `-${step.dmg}` : 'HIT', '#ff4a3a');
      sparks(chest, 0xff3020, 12 + Math.min(14, step.dmg));
    } else {
      floatText(o, 'MISS', '#e8e8e8');
    }
    // A streak shows the path of the weapon through the air.
    const hitMat = flameMat(step.t === 'hit' ? 0xfff0c0 : 0xb8c8e0);
    let trail;
    const yaw = Math.atan2(dir.x, dir.z);
    const off = step.t === 'hit' ? 0 : (Math.random() < 0.5 ? -0.7 : 0.7);
    const straight = ['thrust', 'low', 'punch', 'kick'].includes(state.style);
    if (straight) {
      trail = new THREE.Mesh(new THREE.CylinderGeometry(0.015, 0.05, 1.8, 6), hitMat);
      trail.rotation.set(Math.PI / 2, 0, 0);
      const holder = new THREE.Group();
      holder.add(trail); trail.position.z = -0.8;
      holder.rotation.y = yaw;
      holder.position.copy(chest).add(new THREE.Vector3(dir.z * off, (state.style === 'low' || state.style === 'kick') ? -0.35 : 0, -dir.x * off));
      add(holder, 0.25, p => { hitMat.opacity = 0.9 * (1 - p); trail.scale.set(1, 1 + p, 1); });
    } else {
      trail = new THREE.Mesh(new THREE.TorusGeometry(0.95, 0.03, 6, 24, Math.PI * 0.75), hitMat);
      const holder = new THREE.Group();
      holder.add(trail);
      trail.rotation.z = state.style === 'overhead' ? Math.PI * 0.62 : Math.PI * 0.12;
      if (state.style === 'horizontal') holder.rotation.set(Math.PI / 2, 0, 0);
      const outer = new THREE.Group();
      outer.add(holder);
      outer.rotation.y = yaw + Math.PI / 2;
      if (state.style === 'diagonal') holder.rotation.z = (Math.random() < 0.5 ? 1 : -1) * 0.7;
      outer.position.copy(chest).add(new THREE.Vector3(dir.z * off, 0, -dir.x * off));
      add(outer, 0.3, p => { hitMat.opacity = 0.9 * (1 - p); outer.scale.setScalar(0.85 + p * 0.3); });
    }
  }

  // Adds this step's contribution to its fighters for the frame.
  function update(state, dt) {
    state.age += dt;
    const p = Math.min(1, state.age / state.duration);
    const { step, from, to } = state;

    if (step.t === 'spell' && state.touch) {
      // Walk up to the patient, reach out with the casting arm and touch while the light plays.
      const near = p < 0.22 ? ease(p / 0.22) : p < 0.85 ? 1 : 1 - ease((p - 0.85) / 0.15);
      from.atk.addScaledVector(state.goal, near);
      from.yawGoal = Math.atan2(state.dir.x, state.dir.z);
      const reach = clamp01((p - 0.15) / 0.15) * (p > 0.85 ? 1 - ease((p - 0.85) / 0.15) : 1);
      if (from.arm) from.armPose = { x: lerp(from.armRest.x, -1.45, reach), y: from.armRest.y, z: lerp(from.armRest.z, -0.1, reach), e: lerp(from.armRest.ex, -0.15, reach), tw: 0, lean: 0.1 * reach };
      if (!state.triggered && p >= 0.3) { state.triggered = true; playSpell(step); }
      return p >= 1;
    }
    if (step.t === 'spell') {
      if (from) {
        const raise = Math.sin(Math.min(1, p / 0.6) * Math.PI);
        if (from.arm) from.armPose = { x: lerp(from.armRest.x, -2.7, raise), y: from.armRest.y, z: lerp(from.armRest.z, -0.35, raise), e: lerp(from.armRest.ex, -0.4, raise), tw: 0, lean: -0.05 * raise };
        from.hop += Math.max(0, Math.sin(Math.min(1, p / 0.6) * Math.PI)) * 0.08;
      }
      return p >= 1;
    }
    if (!from || !to) return p >= 1;

    // 0..0.3 approach, 0.3..0.62 swing, 0.62..1 withdraw.
    const approach = p < 0.3 ? ease(p / 0.3) : p < 0.62 ? 1 : 1 - ease((p - 0.62) / 0.38);
    from.atk.addScaledVector(state.goal, approach);
    from.yawGoal = Math.atan2(state.dir.x, state.dir.z);

    const q = clamp01((p - 0.22) / 0.4);
    if (state.style === 'kick') {
      if (q > 0 && q < 1) {
        const ph = swingPhase(q);
        const bl = (w, h) => (ph < 0 ? lerp(0, w, -ph) : lerp(w, h, ph));
        from.kickLeg = { idx: state.kickIdx, hip: bl(0.5, -1.4), knee: bl(1.1, 0.12) };
        from.lean += bl(-0.2, 0.12);
        from.twist += bl(-0.2, 0.25) * (state.kickIdx ? 1 : -1);
      }
    } else if (state.limbs) {
      if (q > 0 && q < 1) {
        const key = FISTS[state.style];
        state.limbs.forEach((l, i) => {
          const pose = fistPose(l, key, q);
          l.pose = pose;
          if (i === 0) { from.twist += pose.tw; from.lean += pose.lean; }
        });
      }
    } else if (from.arm) {
      if (q > 0 && q < 1) {
        const pose = swingPose(from, state.style, q);
        from.armPose = pose;
        from.twist += pose.tw; from.lean += pose.lean;
      }
    } else {
      // Animals pounce: rear up, then dip the head and hop through the target.
      if (q > 0 && q < 1) {
        const ph = swingPhase(q);
        from.pitch += ph < 0 ? ph * 0.3 : -ph * 0.4;
        from.hop += Math.max(0, Math.sin(q * Math.PI)) * 0.25;
      }
    }

    if (!state.triggered && p >= 0.42) {
      state.triggered = true;
      impact(state);
    }
    if (state.triggered) {
      const r = clamp01((p - 0.42) / 0.3);
      const env = Math.sin(r * Math.PI);
      if (step.t === 'hit') {
        const force = 0.45 + Math.min(1.1, (step.dmg ?? 0) * 0.05);
        to.rec.addScaledVector(state.dir, force * env);
        to.roll += env * 0.18 * (state.goal.x > 0 ? 1 : -1);
        to.pitch -= env * 0.12;
      } else {
        // Dodge: step sideways, away from the swing.
        const sgn = state.goal.x * state.dir.z - state.goal.z * state.dir.x >= 0 ? 1 : -1;
        to.rec.add(new THREE.Vector3(state.dir.z * sgn, 0, -state.dir.x * sgn).multiplyScalar(0.9 * env));
        to.hop += env * 0.12;
      }
    }
    return p >= 1;
  }

  // Pose of the weapon arm through one swing (q 0..1).
  function swingPose(a, style, q) {
    const s = SWINGS[style];
    const rest = a.armRest;
    const ph = swingPhase(q);
    const blend = (w, h, r) => (ph < 0 ? lerp(r, w, -ph) : lerp(w, h, ph));
    return {
      x: blend(s.wind.x, s.hit.x, rest.x), y: blend(s.wind.y, s.hit.y, rest.y), z: blend(s.wind.z, s.hit.z, rest.z),
      e: blend(s.wind.e, s.hit.e, rest.ex), tw: blend(s.wind.tw, s.hit.tw, 0), lean: blend(s.wind.lean, s.hit.lean, 0)
    };
  }

  const active = [];
  let queue = [];
  let onDone = null;
  let finalState = null;
  let sinceStart = 0;
  let settling = 0;
  let playing = false;
  let speed = 1;
  const MAX_PARALLEL = 4;

  function play(steps, monstersAlive, done) {
    actors.clear();
    for (let i = 0; i < lookup.partyCount(); i++) actorOf(lookup.party(i), 'p');
    for (let i = 0; i < lookup.monsterCount(); i++) { const m = lookup.monster(i); if (m && m.visible) actorOf(m, 'm'); }
    queue = steps.slice();
    active.length = 0;
    finalState = monstersAlive;
    onDone = done;
    sinceStart = 1;
    settling = 0;
    playing = true;
  }

  function canStart(step) {
    const fromO = step.fs === 'p' ? lookup.party(step.fi) : lookup.monster(step.fi);
    const a = actors.get(fromO);
    if (!a) return true;
    if (a.attacking || a.casting) return false;
    if (step.t === 'spell' && active.some(s => s.step.t === 'spell')) return false;
    return true;
  }

  function resetFrame() {
    for (const a of actors.values()) {
      a.atk.set(0, 0, 0); a.rec.set(0, 0, 0);
      a.roll = 0; a.pitch = 0; a.hop = 0; a.twist = 0; a.lean = 0; a.armPose = null; a.yawGoal = null; a.kickLeg = null; a.limbs.forEach(l => { l.pose = null; });
    }
  }

  function applyActors(dt, wander) {
    for (const a of actors.values()) {
      // Fighters shuffle about their spot, picking a new place now and then.
      a.wanderT -= dt;
      if (!wander) a.wanderGoal.set(0, 0, 0);
      else if (a.wanderT <= 0) {
        a.wanderT = 0.9 + Math.random() * 1.6;
        a.wanderGoal.set((Math.random() - 0.5) * 2 * a.radius, 0, (Math.random() - 0.5) * 2 * a.radius);
      }
      const toGoal = a.wanderGoal.clone().sub(a.wander);
      const step = Math.min(toGoal.length(), 1.1 * dt);
      if (step > 0) a.wander.addScaledVector(toGoal.normalize(), step);

      const x = a.home.x + a.wander.x + a.atk.x + a.rec.x;
      const z = a.home.z + a.wander.z + a.atk.z + a.rec.z;
      const gy = lookup.ground ? lookup.ground(x, z) - lookup.ground(a.home.x, a.home.z) : 0;
      const o = a.o;
      o.position.set(x, a.home.y + gy + a.hop, z);

      const moved = Math.hypot(x - a.lastPos.x, z - a.lastPos.z) / Math.max(dt, 1e-4);
      a.speed = lerp(a.speed, moved, 0.25);
      a.lastPos.set(x, o.position.y, z);
      const amp = Math.min(1, a.speed / 1.8);

      // Face the foe; otherwise turn slowly toward the direction of travel, then back to the rest heading.
      let yaw = a.baseYaw;
      if (a.yawGoal !== null) yaw = a.yawGoal;
      o.rotation.y = angleLerp(o.rotation.y, yaw, 1 - Math.exp(-dt * 9));
      o.rotation.x = a.pitch;
      o.rotation.z = a.roll;

      if (a.legs) {
        a.stride += dt * (4 + a.speed * 3);
        a.legs.forEach((leg, i) => {
          const ph = a.stride + i * Math.PI;
          leg.hip.rotation.x = -Math.sin(ph) * 0.55 * amp;
          leg.knee.rotation.x = Math.max(0, Math.cos(ph)) * 0.8 * amp;
        });
        if (a.kickLeg) {
          const kl = a.legs[a.kickLeg.idx], other = a.legs[1 - a.kickLeg.idx];
          kl.hip.rotation.x = a.kickLeg.hip; kl.knee.rotation.x = a.kickLeg.knee;
          if (other) { other.hip.rotation.x = 0.05; other.knee.rotation.x = 0.1; }
        }
      } else {
        o.position.y += Math.abs(Math.sin(a.stride)) * 0.05 * amp;
        a.stride += dt * (4 + a.speed * 3);
      }

      if (a.arm) {
        const pose = a.armPose;
        const sh = a.arm.shoulder;
        sh.rotation.x = angleLerp(sh.rotation.x, pose ? pose.x : a.armRest.x, 1 - Math.exp(-dt * (pose ? 30 : 8)));
        sh.rotation.y = angleLerp(sh.rotation.y, pose ? pose.y : a.armRest.y, 1 - Math.exp(-dt * 20));
        sh.rotation.z = angleLerp(sh.rotation.z, pose ? pose.z : a.armRest.z, 1 - Math.exp(-dt * (pose ? 30 : 8)));
        if (a.arm.elbow) a.arm.elbow.rotation.x = angleLerp(a.arm.elbow.rotation.x, pose ? pose.e : a.armRest.ex, 1 - Math.exp(-dt * 25));
      }
      a.limbs.forEach(l => {
        const pose = l.pose;
        l.sh.rotation.x = angleLerp(l.sh.rotation.x, pose ? pose.x : l.rest.x, 1 - Math.exp(-dt * (pose ? 30 : 8)));
        l.sh.rotation.y = angleLerp(l.sh.rotation.y, pose ? pose.y : l.rest.y, 1 - Math.exp(-dt * 20));
        l.sh.rotation.z = angleLerp(l.sh.rotation.z, pose ? pose.z : l.rest.z, 1 - Math.exp(-dt * (pose ? 30 : 8)));
      });
      if (a.twistObj) {
        a.twistObj.rotation.y = angleLerp(a.twistObj.rotation.y, a.twistRest.y + a.twist, 1 - Math.exp(-dt * 20));
        a.twistObj.rotation.x = angleLerp(a.twistObj.rotation.x, a.twistRest.x + a.lean, 1 - Math.exp(-dt * 20));
      }
    }
  }

  function setSpeed(s) { speed = Math.min(8, Math.max(0.25, s)); return speed; }

  function endRound() {
    if (finalState) {
      finalState.forEach((alive, i) => {
        const o = lookup.monster(i);
        if (o && !alive && o.visible) {
          const y = o.position.y;
          add(new THREE.Object3D(), 0.8, p => {
            o.scale.setScalar(Math.max(0.001, 1 - p));
            o.position.y = y - p * 0.6;
            if (p >= 0.99) o.visible = false;
          });
        }
      });
    }
  }

  function complete() {
    for (const a of actors.values()) restoreActor(a);
    actors.clear();
    playing = false;
    [...pendingKinds].forEach(startLinger);
    const cb = onDone;
    onDone = null;
    if (cb) cb();
  }

  // Finishes the round at once: every blow is dropped, positions are restored and the dead are removed.
  function skip() {
    if (!playing) return;
    queue = [];
    active.length = 0;
    for (const e of effects) scene.remove(e.obj);
    effects.length = 0;
    if (finalState) finalState.forEach((alive, i) => {
      const o = lookup.monster(i);
      if (o && !alive) o.visible = false;
    });
    complete();
  }

  function tick(rawDt) {
    const dt = rawDt * speed;
    tickLingering(rawDt, !playing);
    for (let i = effects.length - 1; i >= 0; i--) {
      const e = effects[i];
      e.age += dt;
      const p = Math.min(1, e.age / e.life);
      e.update?.(p);
      if (p >= 1) {
        scene.remove(e.obj);
        e.obj.traverse?.(o => { o.geometry?.dispose?.(); });
        effects.splice(i, 1);
      }
    }
    if (!playing) return;

    resetFrame();
    sinceStart += dt;
    // Start further blows while others are still in flight, so several exchanges overlap.
    while (queue.length && active.length < MAX_PARALLEL && sinceStart >= 0.35) {
      const idx = queue.findIndex(canStart);
      if (idx < 0) break;
      const state = begin(queue.splice(idx, 1)[0]);
      active.push(state);
      sinceStart = 0;
    }
    for (let i = active.length - 1; i >= 0; i--) {
      if (update(active[i], dt)) {
        const s = active.splice(i, 1)[0];
        if (s.from) { s.from.attacking = false; s.from.casting = false; }
      }
    }

    const fighting = queue.length > 0 || active.length > 0;
    if (!fighting && settling === 0) { endRound(); settling = 0.0001; }
    if (settling > 0) settling += dt;
    applyActors(dt, fighting);
    if (settling > 1.0) complete();
  }

  return { play, tick, setLingering, clearLingering, skip, setSpeed, get speed() { return speed; }, get busy() { return playing; } };
}
