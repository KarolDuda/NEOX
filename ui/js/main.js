import { call, on, win } from './api.js';
import { h, toast, confirmBox, attachSpotlight } from './ui.js';
import { S, refreshInfo } from './state.js';

const ICONS = {
  dashboard: '<path d="M3 13h8V3H3zM13 21h8V11h-8zM3 21h8v-6H3zM13 3v6h8V3z"/>',
  autotune: '<path d="M13 2L4 14h7l-1 8 9-12h-7z"/>',
  gaming: '<rect x="2" y="7" width="20" height="11" rx="4"/><path d="M7 10v5M4.5 12.5h5M16 11.5h.01M18.5 14h.01"/>',
  optimizations: '<path d="M4 6h10M18 6h2M4 12h4M12 12h8M4 18h12M20 18h0"/><circle cx="16" cy="6" r="2"/><circle cx="10" cy="12" r="2"/><circle cx="18" cy="18" r="2"/>',
  network: '<circle cx="12" cy="12" r="9"/><path d="M3 12h18M12 3c3 3 3 15 0 18M12 3c-3 3-3 15 0 18"/>',
  system: '<rect x="3" y="4" width="18" height="12" rx="2"/><path d="M8 20h8M12 16v4"/>',
  startup: '<path d="M12 3v9M6.3 6.3a8 8 0 1 0 11.4 0"/>',
  disk: '<ellipse cx="12" cy="6" rx="8" ry="3"/><path d="M4 6v12c0 1.7 3.6 3 8 3s8-1.3 8-3V6M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3"/>',
  history: '<path d="M3 12a9 9 0 1 0 3-6.7L3 8M3 3v5h5M12 7v5l3 2"/>',
  settings: '<circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3h0a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5h0a1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8v0a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z"/>',
};
const PAGES = [
  ['dashboard', 'Dashboard'], ['autotune', 'Auto-Tune'], ['gaming', 'Gaming'], ['optimizations', 'Optimizations'], ['network', 'Network'],
  ['system', 'System'], ['startup', 'Startup & services'], ['disk', 'Disk'], ['history', 'History'], ['settings', 'Settings'],
];
const loaders = Object.fromEntries(PAGES.map(([id]) => [id, () => import(`./pages/${id}.js`)]));
let current = null, token = 0;

export async function go(id) {
  if (!loaders[id]) id = 'dashboard';
  const my = ++token; current = id;
  document.querySelectorAll('.nav-item').forEach((b, i) => { const on = b.dataset.id === id; b.classList.toggle('active', on); if (on) { const ind = document.getElementById('nav-ind'); ind.style.opacity = 1; ind.style.transform = `translateY(${b.offsetTop}px)`; } });
  const content = document.getElementById('content');
  try {
    const mod = await loaders[id]();
    const el = await mod.default({ go });
    if (my !== token) return;
    content.replaceChildren(el); content.scrollTop = 0;
  } catch (e) { console.error(e); if (my === token) content.replaceChildren(h('div', { class: 'empty' }, 'Something went wrong: ' + e)); }
}

function updateBars() {
  const bars = document.getElementById('bars'); bars.replaceChildren();
  const i = S.info; if (!i) return;
  if (!i.elevated) bars.append(h('div', { class: 'bar warn' }, h('span', { class: 'grow' }, h('b', {}, 'Limited mode. '), 'Run as administrator to apply most optimizations.'),
    h('button', { class: 'btn sm', onclick: async () => { if (!(await call('restart_as_admin'))) toast('Elevation was cancelled.', 'info'); } }, 'Restart as admin')));
  if (i.pendingRestart && i.pendingRestart.length) bars.append(h('div', { class: 'bar info' }, h('span', { class: 'grow' }, h('b', {}, 'Restart pending. '), `${i.pendingRestart.length} change(s) take effect after you restart Windows.`),
    h('button', { class: 'btn sm', onclick: async () => { await call('clear_pending_restart'); await refreshInfo(); } }, 'Dismiss')));
}
S.nav = { updateBars };

async function boot() {
  const fill = document.getElementById('splash-fill'); fill.style.width = '25%';
  document.getElementById('tb-min').onclick = win.min; document.getElementById('tb-max').onclick = win.max; document.getElementById('tb-close').onclick = win.close;
  const nav = document.getElementById('nav');
  PAGES.forEach(([id, label]) => nav.append(h('button', { class: 'nav-item', 'data-id': id, onclick: () => go(id), html: `<svg viewBox="0 0 24 24">${ICONS[id]}</svg><span>${label}</span>` })));
  attachSpotlight(document.body);
  fill.style.width = '60%';
  try { await refreshInfo(); } catch (e) { console.error(e); }
  document.getElementById('version').textContent = 'v' + (S.info?.version || '');
  fill.style.width = '100%';
  await go('dashboard');
  setTimeout(() => { const sp = document.getElementById('splash'); sp.classList.add('hide'); setTimeout(() => sp.remove(), 700); }, 450);

  if (S.info && !S.info.firstRunDone) setTimeout(async () => {
    const yes = await confirmBox('Welcome to NEOX', 'Run Auto-Tune now? It scans your hardware and applies only safe, reversible tweaks — you review the plan first.', 'Open Auto-Tune');
    await call('settings_set', { patch: { firstRunDone: true } }); await refreshInfo(); if (yes) go('autotune');
  }, 900);
  if (S.info?.settings?.autoUpdate) call('update_check').then(r => { if (r?.update) toast(`NEOX ${r.update.version} is available — see Settings.`, 'info', 8000); }).catch(() => {});
}
boot();
