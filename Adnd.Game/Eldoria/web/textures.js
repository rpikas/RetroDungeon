// Procedural surface textures (drawn on canvases, so no image files are needed). They are grey-scale detail
// that is tinted by each material's colour, and the same canvas doubles as a bump map for relief.
const cache = new Map();

function rng(seed) {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6D2B79F5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

const painters = {
  cloth(ctx, s, r) {
    ctx.fillStyle = '#d8d8d8'; ctx.fillRect(0, 0, s, s);
    for (let i = 0; i < s; i += 3) {
      ctx.fillStyle = `rgba(0,0,0,${0.10 + r() * 0.10})`; ctx.fillRect(i, 0, 1, s);
      ctx.fillStyle = `rgba(255,255,255,${0.10 + r() * 0.10})`; ctx.fillRect(0, i + 1, s, 1);
    }
    for (let i = 0; i < 1500; i++) { ctx.fillStyle = `rgba(0,0,0,${r() * 0.08})`; ctx.fillRect(r() * s, r() * s, 2, 2); }
  },
  leather(ctx, s, r) {
    ctx.fillStyle = '#cfcfcf'; ctx.fillRect(0, 0, s, s);
    for (let i = 0; i < 700; i++) {
      const x = r() * s, y = r() * s, rad = 3 + r() * 10;
      const g = ctx.createRadialGradient(x, y, 0, x, y, rad);
      const dark = r() < 0.5;
      g.addColorStop(0, dark ? 'rgba(0,0,0,0.16)' : 'rgba(255,255,255,0.14)');
      g.addColorStop(1, 'rgba(0,0,0,0)');
      ctx.fillStyle = g; ctx.fillRect(x - rad, y - rad, rad * 2, rad * 2);
    }
    ctx.strokeStyle = 'rgba(0,0,0,0.25)'; ctx.lineWidth = 1;
    for (let i = 0; i < 60; i++) { ctx.beginPath(); const x = r() * s, y = r() * s; ctx.moveTo(x, y); ctx.lineTo(x + (r() - 0.5) * 14, y + (r() - 0.5) * 14); ctx.stroke(); }
  },
  metal(ctx, s, r) {
    ctx.fillStyle = '#d6d6d6'; ctx.fillRect(0, 0, s, s);
    for (let i = 0; i < 700; i++) { ctx.fillStyle = `rgba(${r() < 0.5 ? '0,0,0' : '255,255,255'},${r() * 0.10})`; ctx.fillRect(0, r() * s, s, 1); }
    ctx.strokeStyle = 'rgba(0,0,0,0.30)'; ctx.lineWidth = 1;
    for (let i = 0; i < 50; i++) { ctx.beginPath(); const x = r() * s, y = r() * s; ctx.moveTo(x, y); ctx.lineTo(x + (r() - 0.5) * 50, y + (r() - 0.5) * 50); ctx.stroke(); }
  },
  mail(ctx, s, r) {
    ctx.fillStyle = '#7a7a7a'; ctx.fillRect(0, 0, s, s);
    const d = 10;
    for (let y = 0; y < s + d; y += d / 2) {
      for (let x = (Math.round(y / (d / 2)) % 2) * (d / 2); x < s + d; x += d) {
        const g = ctx.createRadialGradient(x, y, 1, x, y, d / 2);
        g.addColorStop(0, 'rgba(0,0,0,0.0)'); g.addColorStop(0.7, 'rgba(255,255,255,0.55)'); g.addColorStop(1, 'rgba(0,0,0,0.6)');
        ctx.fillStyle = g; ctx.beginPath(); ctx.arc(x, y, d / 2, 0, Math.PI * 2); ctx.fill();
      }
    }
  },
  wood(ctx, s, r) {
    ctx.fillStyle = '#d0d0d0'; ctx.fillRect(0, 0, s, s);
    for (let i = 0; i < 160; i++) {
      const x = r() * s;
      ctx.strokeStyle = `rgba(0,0,0,${0.06 + r() * 0.14})`; ctx.lineWidth = 1 + r() * 2;
      ctx.beginPath(); ctx.moveTo(x, 0);
      for (let y = 0; y <= s; y += 16) ctx.lineTo(x + Math.sin(y * 0.05 + x) * 3, y);
      ctx.stroke();
    }
  },
  skin(ctx, s, r) {
    ctx.fillStyle = '#e8e8e8'; ctx.fillRect(0, 0, s, s);
    for (let i = 0; i < 2500; i++) { ctx.fillStyle = `rgba(${r() < 0.5 ? '0,0,0' : '255,255,255'},${r() * 0.05})`; ctx.fillRect(r() * s, r() * s, 2, 2); }
  },
  planks(ctx, s, r) {
    ctx.fillStyle = '#d4d4d4'; ctx.fillRect(0, 0, s, s);
    const n = 6;
    for (let i = 0; i < n; i++) {
      const x = (i * s) / n;
      ctx.fillStyle = `rgba(${r() < 0.5 ? '0,0,0' : '255,255,255'},${0.06 + r() * 0.08})`; ctx.fillRect(x, 0, s / n, s);
      ctx.fillStyle = 'rgba(0,0,0,0.5)'; ctx.fillRect(x, 0, 2, s);
      for (let k = 0; k < 14; k++) { ctx.fillStyle = 'rgba(0,0,0,0.08)'; ctx.fillRect(x + r() * (s / n), r() * s, 1, 12 + r() * 30); }
    }
  }
};

export function texture(T, kind, repeat = 3, seed = 1) {
  const key = `${kind}|${repeat}|${seed}`;
  if (cache.has(key)) return cache.get(key);
  const size = 256;
  const canvas = document.createElement('canvas');
  canvas.width = canvas.height = size;
  painters[kind](canvas.getContext('2d'), size, rng(seed * 977 + kind.length));
  const tex = new T.CanvasTexture(canvas);
  tex.wrapS = tex.wrapT = T.RepeatWrapping;
  tex.repeat.set(repeat, repeat);
  tex.colorSpace = T.SRGBColorSpace;
  tex.anisotropy = 8;
  cache.set(key, tex);
  return tex;
}

// A heraldic tabard/shield emblem: a cross on a plain field.
export function emblem(T, color, field, shape = 'cross') {
  const size = 128;
  const c = document.createElement('canvas');
  c.width = c.height = size;
  const ctx = c.getContext('2d');
  ctx.fillStyle = field; ctx.fillRect(0, 0, size, size);
  ctx.fillStyle = color;
  if (shape === 'cross') { ctx.fillRect(size * 0.43, size * 0.12, size * 0.14, size * 0.76); ctx.fillRect(size * 0.22, size * 0.34, size * 0.56, size * 0.14); }
  else { ctx.beginPath(); ctx.arc(size / 2, size / 2, size * 0.3, 0, Math.PI * 2); ctx.fill(); }
  const tex = new T.CanvasTexture(c);
  tex.colorSpace = T.SRGBColorSpace;
  tex.anisotropy = 8;
  return tex;
}
