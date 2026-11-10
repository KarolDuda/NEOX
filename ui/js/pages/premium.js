import { call } from '../api.js';
import { h, page, toast, busy, fmtTime } from '../ui.js';
import { S, refreshLicense } from '../state.js';

const FEATURES = [
  ['All 260 optimizations', 'The free plan has 20. Premium unlocks every performance, input, network and cleanup item, including the high-impact ones.'],
  ['Auto-Tune', 'Scans your hardware, applies the right set, and measures before and after.'],
  ['Internet Auto-Tune', 'Detects Ethernet and Wi-Fi and applies the network items that fit each, then measures latency, jitter and loss.'],
  ['Profiles', 'Maximum performance, Esports, Network boost, Streaming and Laptop. Plus your own profiles with import and export.'],
  ['Game Boost', 'Switches to a performance power plan only while your game runs, then restores your plan.'],
  ['Game tools', 'Fortnite low-latency config, DNS switcher, and baseline snapshots with before/after comparison.'],
];
const ROWS = [
  ['Optimizations', '20', 'All 260'], ['Auto-Tune', '—', '✔'], ['Internet Auto-Tune (Wi-Fi + Ethernet)', '—', '✔'],
  ['Profiles (load, create, import, export)', '—', '✔'], ['Game Boost', '—', '✔'], ['Fortnite optimizer', '—', '✔'], ['DNS switcher', '—', '✔'],
  ['Baselines & comparison', '—', '✔'], ['Undo / revert everything', '✔', '✔'], ['Restore point, history, disk analyzer', '✔', '✔'],
];

export default async function premium({ go }) {
  const lic = await refreshLicense();
  const st = lic.status;
  const buy = h('button', { class: 'btn gold lg' }, 'Buy Premium version');
  buy.onclick = () => call('open_buy').catch(() => toast('Open https://discord.gg/2h9FUFb99 in your browser.', 'info'));

  const hero = h('div', { class: 'prem-hero' },
    h('div', { class: 'eyebrow gold-text' }, 'NEOX PREMIUM'),
    h('h1', {}, lic.premium ? 'Premium is active' : 'Everything NEOX can do. Unlocked.'),
    h('p', { class: 'muted', style: { maxWidth: '640px' } }, lic.premium
      ? `Licensed to ${st.name}. Bound to this PC.` + (st.exp ? ` Valid until ${fmtTime(st.exp * 1000)}.` : ' Lifetime license.')
      : 'Free gives you 20 safe optimizations. Premium adds every optimization, Auto-Tune, Internet Auto-Tune for Wi-Fi and Ethernet, profiles and game tools.'),
    lic.premium ? null : h('div', { class: 'row wrap', style: { marginTop: '16px' } }, buy, h('span', { class: 'small muted' }, 'Opens our Discord. Open a ticket in the Purchase channel.')));

  const hw = h('div', { class: 'hwid' }, lic.hwid || '…');
  const copy = h('button', { class: 'btn sm', onclick: async () => { try { await navigator.clipboard.writeText(lic.hwid); toast('Hardware ID copied.', 'ok'); } catch { toast('Select the ID and copy it manually.', 'info'); } } }, 'Copy');
  const key = h('textarea', { class: 'key', placeholder: 'Paste your license key here (starts with NEOX1.)' });
  const act = h('button', { class: 'btn gold' }, 'Activate');
  act.onclick = () => busy(act, async () => {
    const r = await call('license_activate', { token: key.value.trim() });
    if (r.state === 'active') { toast('Premium activated. Thank you!', 'ok'); S.nav.updateNav && S.nav.updateNav(); go('premium'); }
    else toast(r.reason || 'That key could not be activated.', 'err', 7000);
  });

  const activate = h('div', { class: 'card col', style: { gap: '12px' } },
    h('div', { class: 'h2' }, lic.premium ? 'License' : 'Activate'),
    h('div', { class: 'small muted' }, 'Your hardware ID identifies this PC (from the motherboard, not your IP address). A license works on one PC only.'),
    h('div', { class: 'row' }, hw, copy),
    lic.premium ? h('div', { class: 'row' }, h('span', { class: 'grow muted' }, `License ${st.id}`),
        h('button', { class: 'btn sm danger', onclick: async () => { await call('license_remove'); toast('License removed from this PC.', 'info'); go('premium'); } }, 'Remove from this PC'))
      : [key, h('div', { class: 'row' }, act)]);

  const how = lic.premium ? null : h('div', { class: 'card' }, h('div', { class: 'h2' }, 'How to get Premium'),
    h('ol', { class: 'steps', style: { padding: 0, margin: '12px 0 0' } },
      h('li', {}, h('span', {}, 'Press “Buy Premium version” and join our Discord.')),
      h('li', {}, h('span', {}, 'Open a ticket in the Purchase channel and send your hardware ID (shown below).')),
      h('li', {}, h('span', {}, 'After payment you get a license key made for your PC.')),
      h('li', {}, h('span', {}, 'Paste it here and press Activate. Moving to a new PC? Open a ticket in Help.'))));

  const feats = h('div', { class: 'prem-grid' }, FEATURES.map(([t, d]) => h('div', { class: 'prem-feat' }, h('b', {}, t), h('span', { class: 'small muted' }, d))));
  const table = h('div', { class: 'card' }, h('table', { class: 'cmp' },
    h('thead', {}, h('tr', {}, h('th', {}, ''), h('th', {}, 'Free'), h('th', { class: 'gold-text' }, 'Premium'))),
    h('tbody', {}, ROWS.map(r => h('tr', {}, h('td', {}, r[0]), h('td', { class: r[1] === '—' ? 'n' : '' }, r[1]), h('td', { class: 'y' }, r[2]))))));
  return page('Premium', null, hero, how, activate, feats, table);
}
