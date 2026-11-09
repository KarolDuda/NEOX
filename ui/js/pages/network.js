import { call, on } from '../api.js';
import { h, page, toast, busy, confirmBox } from '../ui.js';

export default async function network() {
  const pings = h('div', { class: 'stack' }), dns = h('div', { class: 'stack' }), info = h('div', { class: 'card' }, h('div', { class: 'muted' }, 'Loading…'));
  const status = h('div', { class: 'small muted mono' });
  const wrap = page('Network', 'Measure first, change second: latency to common servers, DNS speed, and a path MTU probe.', info,
    h('div', { class: 'row' }, h('h3', { class: 'h2 grow' }, 'Latency'), runBtn('Run ping test', 'net_ping_all', pings)), pings,
    h('div', { class: 'row' }, h('h3', { class: 'h2 grow' }, 'DNS benchmark'), runBtn('Run DNS benchmark', 'net_dns_bench', dns),
      h('button', { class: 'btn', onclick: async () => { const r = await call('net_dns_restore'); toast(r.message, r.success ? 'ok' : 'err'); } }, 'Restore my DNS')), dns, status);

  function runBtn(label, cmd, box) { const b = h('button', { class: 'btn primary' }, label); b.onclick = () => busy(b, async () => { box.replaceChildren(); b.textContent = 'Running…'; try { await call(cmd); } catch (e) { toast(String(e), 'err'); } }); return b; }
  await on('progress', m => { if (document.body.contains(status)) status.textContent = m; });
  await on('ping-row', r => { if (!document.body.contains(pings)) return; const s = r.stats;
    pings.append(h('div', { class: 'delta' }, h('b', { class: 'grow' }, r.name), h('span', { class: 'mono muted' }, s.host), h('span', {}, s.received ? s.avgMs.toFixed(1) + ' ms' : 'no reply'), h('span', { class: 'small muted' }, `jitter ${s.jitterMs.toFixed(1)} ms · loss ${s.lossPercent.toFixed(0)}%`))); });
  const dnsRows = [];
  await on('dns-row', r => { if (!document.body.contains(dns)) return; dnsRows.push(r); dnsRows.sort((a, b) => (a.medianMs ?? 1e9) - (b.medianMs ?? 1e9));
    dns.replaceChildren(...dnsRows.map((x, i) => h('div', { class: 'delta' }, h('b', { class: 'grow' }, (i === 0 ? '★ ' : '') + x.name), h('span', { class: 'mono muted' }, x.primary + ' / ' + x.secondary), h('span', {}, x.medianMs == null ? 'failed' : x.medianMs.toFixed(1) + ' ms'),
      h('button', { class: 'btn sm', onclick: async () => { if (!(await confirmBox('Set DNS to ' + x.name + '?', 'Applied to active adapters. Your current DNS is saved and can be restored with "Restore my DNS".', 'Apply'))) return; const r = await call('net_dns_set', { servers: [x.primary, x.secondary] }); toast(r.message, r.success ? 'ok' : 'err'); } }, 'Use')))); });

  Promise.all([call('net_adapters'), call('net_mtu')]).then(([ad, mtu]) => info.replaceChildren(h('div', { class: 'eyebrow' }, 'Active adapters'),
    ...ad.map(a => h('div', { class: 'row', style: { marginTop: '8px' } }, h('b', { class: 'grow' }, a.name + ' · ' + a.description), h('span', { class: 'muted small' }, `${a.speedMbps} Mbps · DNS ${a.dns.join(', ') || 'auto'}`))),
    h('div', { class: 'small muted', style: { marginTop: '8px' } }, mtu ? `Largest working payload without fragmentation: ${mtu} bytes (MTU ${mtu + 28}).` : 'MTU probe unavailable.'))).catch(() => {});
  return wrap;
}
