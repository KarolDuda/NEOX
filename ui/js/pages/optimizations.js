import { call } from '../api.js';
import { h, page, badge, switchEl, toast, confirmBox, skeleton, CAT_NAMES, EFFECT } from '../ui.js';
import { getCatalog, refreshInfo } from '../state.js';

export default async function optimizations() {
  const list = h('div', { class: 'stack' }, skeleton(6));
  const search = h('input', { type: 'search', placeholder: 'Search optimizations…', class: 'btn', style: { width: '280px', textAlign: 'left' } });
  const catBar = h('div', { class: 'row wrap' }), riskBar = h('div', { class: 'row wrap' });
  const wrap = page('Optimizations', 'Every change is verified after applying and can be reverted. Effects shown are the catalog author\'s claim, not a measurement.', h('div', { class: 'row wrap' }, search), catBar, riskBar, list);
  const catalog = await getCatalog();
  let cat = 'all', risk = 'all', statuses = {};
  const rows = new Map();

  ['all', ...Object.keys(CAT_NAMES)].forEach(c => catBar.append(pill2(catBar, c === 'all' ? 'All' : CAT_NAMES[c], c, () => cat, v => { cat = v; })));
  ['all', 'recommended', 'caution', 'experimental', 'highRisk'].forEach(r => riskBar.append(pill2(riskBar, r === 'all' ? 'Any risk' : { recommended: 'Recommended', caution: 'Caution', experimental: 'Experimental', highRisk: 'High risk' }[r], r, () => risk, v => { risk = v; })));
  function pill2(bar, label, val, cur, set) { const p = h('button', { class: 'pill' + (val === cur() ? ' on' : ''), onclick: () => { set(val); [...bar.children].forEach(c => c.classList.remove('on')); p.classList.add('on'); draw(); } }, label); return p; }
  search.addEventListener('input', () => draw());

  function row(t) {
    const dot = h('i', { class: 'dot' });
    const isAction = t.kind === 'action';
    let ctl;
    if (isAction) ctl = h('button', { class: 'btn sm', onclick: async e => {
      if (t.confirm && !(await confirmBox(t.title, t.confirm, t.button || 'Run'))) return;
      e.target.disabled = true; try { const r = await call('action_run', { id: t.id }); toast(r.message, r.success ? 'ok' : 'err'); } catch (x) { toast(String(x), 'err'); } e.target.disabled = false; } }, t.button || 'Run');
    else ctl = switchEl(false, async s => {
      const on = !s.classList.contains('on'); s.disabled = true; dot.className = 'dot busy';
      try {
        if (on && t.risk !== 'recommended' && !(await confirmBox(t.title, `${t.description}\n\n${t.consequences || ''}\n\nRisk: ${t.risk}. You can revert this later.`, 'Apply'))) return;
        const r = await call(on ? 'tweak_apply' : 'tweak_revert', { id: t.id, gameExe: null });
        toast(r.message, r.success ? 'ok' : 'err'); if (r.success) { statuses[t.id] = on ? 'applied' : 'notApplied'; s.set(on); }
        refreshInfo();
      } catch (x) { toast(String(x), 'err'); } finally { s.disabled = false; dot.className = 'dot ' + (statuses[t.id] === 'applied' ? 'on' : 'off'); }
    });
    const el = h('div', { class: 'tw' }, dot, h('div', { class: 'grow' },
      h('div', { class: 'row wrap', style: { gap: '8px' } }, h('span', { class: 'title' }, t.title), badge(t.risk), t.requiresAdmin ? h('span', { class: 'badge b-info' }, 'Admin') : null, t.restart !== 'none' ? h('span', { class: 'badge b-info' }, 'Restart') : null),
      h('div', { class: 'desc' }, t.description),
      h('details', {}, h('summary', {}, 'Details'), h('div', { class: 'more' }, (t.consequences || 'No side effects documented.') + ' · ' + (EFFECT[t.claimedEffect] || '')))), ctl);
    rows.set(t.id, { dot, ctl, isAction });
    return el;
  }
  const els = new Map(catalog.map(t => [t.id, row(t)]));
  function draw() {
    const q = search.value.trim().toLowerCase();
    const shown = catalog.filter(t => (cat === 'all' || t.category === cat) && (risk === 'all' || t.risk === risk) && (!q || (t.title + ' ' + t.description).toLowerCase().includes(q)));
    list.replaceChildren(...(shown.length ? shown.map(t => els.get(t.id)) : [h('div', { class: 'empty' }, 'Nothing matches.')]));
  }
  draw();
  call('statuses', { gameExe: null }).then(st => { statuses = st; for (const [id, r] of rows) { if (r.isAction) continue; const on = st[id] === 'applied'; r.ctl.set(on); r.dot.className = 'dot ' + (on ? 'on' : st[id] === 'unknown' ? '' : 'off'); } });
  return wrap;
}
