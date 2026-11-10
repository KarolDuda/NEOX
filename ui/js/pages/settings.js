import { call, on } from '../api.js';
import { h, page, switchEl, toast, busy, confirmBox, meter } from '../ui.js';
import { S, refreshInfo, refreshLicense } from '../state.js';
import { activateCard } from './premium.js';

export default async function settings({ go }) {
  const lic = await refreshLicense();
  const i = S.info, st = i.settings;
  const toggle = (title, desc, key) => h('div', { class: 'card row' }, h('div', { class: 'grow' }, h('div', { class: 'h2' }, title), h('div', { class: 'muted small' }, desc)),
    switchEl(st[key], async s => { const v = !s.classList.contains('on'); await call('settings_set', { patch: { [key]: v } }); s.set(v); await refreshInfo(); }));
  const upd = h('div', { class: 'card' }, h('div', { class: 'h2' }, 'Updates'), h('div', { class: 'muted small', id: 'upd-msg' }, 'Version ' + i.version));
  const bar = h('div', { style: { display: 'none', marginTop: '10px' } }, meter(0));
  const check = h('button', { class: 'btn primary', style: { marginTop: '12px' } }, 'Check for updates');
  check.onclick = () => busy(check, async () => {
    check.textContent = 'Checking…'; const r = await call('update_check'); upd.querySelector('#upd-msg').textContent = r.message;
    if (r.update) { check.textContent = 'Install ' + r.update.version; check.disabled = false; check.onclick = async () => {
      if (!(await confirmBox('Install ' + r.update.version + '?', (r.update.notes || '').slice(0, 600) + '\n\nNEOX will close and the installer will run.', 'Install'))) return;
      bar.style.display = 'block'; await on('update-progress', p => bar.replaceChildren(meter(p * 100)));
      const x = await call('update_install'); if (!x.success) toast(x.message, 'err'); }; }
  });
  upd.append(check, bar);
  const admin = h('div', { class: 'card row' }, h('div', { class: 'grow' }, h('div', { class: 'h2' }, 'Administrator rights'), h('div', { class: 'muted small' }, i.elevated ? 'NEOX is running as administrator.' : 'Many optimizations need administrator rights.')),
    i.elevated ? h('span', { class: 'badge b-recommended' }, 'Active') : h('button', { class: 'btn', onclick: async () => { if (!(await call('restart_as_admin'))) toast('Elevation was cancelled.', 'info'); } }, 'Restart as admin'));
  return page('Settings', 'NEOX by spinprint · spinprint.pl', admin, activateCard(lic, go), toggle('Restore point before profiles', 'Creates a Windows restore point before applying a profile (when Windows allows it).', 'createRestorePointBeforeProfiles'),
    toggle('Check for updates automatically', 'Looks for new versions on GitHub at startup. Downloads are verified before they run.', 'autoUpdate'), upd,
    h('div', { class: 'card' }, h('div', { class: 'h2' }, 'About'), h('p', { class: 'muted' }, 'NEOX applies documented, reversible Windows settings and tells you honestly what each one does. It does not promise FPS numbers. Everything is stored locally.')));
}
