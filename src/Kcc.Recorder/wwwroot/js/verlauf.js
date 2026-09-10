'use strict';

const $ = id => document.getElementById(id);
const fmt = n => (n ?? 0).toLocaleString('de-DE', { maximumFractionDigits: 1 });
const PALETTE = ['#1f6feb', '#3fb950', '#e3b341', '#db61a2', '#a371f7', '#f0883e',
                 '#2dd4bf', '#f85149', '#8b949e', '#58a6ff', '#d29922', '#7ee787'];
const colorFor = i => PALETTE[i % PALETTE.length];

const API_BASE = (new URLSearchParams(location.search).get('api')
  || (/^https?:$/.test(location.protocol) ? location.origin : 'http://localhost:8082'))
  .replace(/\/+$/, '');

let hours = 8;
let rollingWin = 5;   // > 0: gleitendes Kurzzeit-Fenster (Minuten), sonst feste Eimer

// Anzeigeraster je Zeitraum. Feinstmöglich ist die Rasterweite der Aufzeichnung
// (UphHistoryIntervalMinutes, Standard 5 min); darüber wird zusammengefasst.
function bucketFor(h) {
  if (h <= 24) return 5;
  if (h <= 72) return 15;
  if (h <= 168) return 30;
  if (h <= 336) return 60;
  return 240;
}

const chart = echarts.init($('area'), null, { renderer: 'canvas' });
let rpChart = null;   // zweiter Chart (Belegung & Leistung), erst bei Auswahl eines Ressourcenpunkts
addEventListener('resize', () => { chart.resize(); rpChart && rpChart.resize(); });

// Ausgewähltes Zeitfenster [startMs, endMs] oder null (voller Bereich). Ein Zoom in einem
// Chart wird auf den anderen gespiegelt, die Tabelle zeigt dann nur diesen Ausschnitt.
let sel = null;
let syncing = false;

// Absolutes Zeitfenster aus dem aktuellen Zoom eines Charts (null = voller Bereich).
function zoomWindow(c) {
  const dz = ((c.getOption() || {}).dataZoom || [])[0] || {};
  const s = dz.start ?? 0, e = dz.end ?? 100;
  if (s <= 0.05 && e >= 99.95) return null;
  if (dz.startValue != null && dz.endValue != null) return [+dz.startValue, +dz.endValue];
  const ax = c.getModel().getComponent('xAxis', 0).axis.scale.getExtent();
  return [ax[0] + (ax[1] - ax[0]) * s / 100, ax[0] + (ax[1] - ax[0]) * e / 100];
}

// Zoom auf einen Chart anwenden, ohne dessen dataZoom-Event als neue Nutzeraktion zu werten.
function applyZoom(c) {
  if (!c) return;
  c.dispatchAction(sel
    ? { type: 'dataZoom', startValue: sel[0], endValue: sel[1] }
    : { type: 'dataZoom', start: 0, end: 100 });
}

// Zoom in einem Chart → auf den anderen spiegeln und die Tabelle neu filtern.
function onZoom(src) {
  if (syncing) return;
  sel = zoomWindow(src);
  syncing = true;
  applyZoom(src === chart ? rpChart : chart);
  syncing = false;
  renderTable();
}
chart.on('dataZoom', () => onZoom(chart));

// Mit der Maus einen Zeitbereich aufziehen (X-Zoom), wie in der alten HTML-Version.
// Dauerhaft aktiv; Doppelklick setzt zurück und schaltet es wieder scharf.
const DRAG_ZOOM = { show: false, feature: { dataZoom: { yAxisIndex: 'none', filterMode: 'none' } } };
const armDragZoom = c => c.dispatchAction({ type: 'takeGlobalCursor', key: 'dataZoomSelect', dataZoomSelectActive: true });
function bindDragZoom(c, el) {
  armDragZoom(c);
  if (el.dataset.zoomBound) return;
  el.dataset.zoomBound = '1';
  el.addEventListener('dblclick', () => { c.dispatchAction({ type: 'dataZoom', start: 0, end: 100 }); armDragZoom(c); });
}

// Gemeinsames dunkles Grundgerüst — sparam die Serien/Achsen dazu.
function baseOption() {
  return {
    backgroundColor: 'transparent',
    textStyle: { color: '#9aa4b2', fontFamily: 'system-ui, sans-serif' },
    grid: { left: 52, right: 16, top: 16, bottom: 64 },
    xAxis: {
      type: 'time',
      axisLine: { lineStyle: { color: '#2a2f37' } },
      axisLabel: { color: '#7a8494', hideOverlap: true },
      splitLine: { show: false },
    },
    yAxis: {
      type: 'value',
      name: 'UPH',
      nameTextStyle: { color: '#7a8494' },
      axisLabel: { color: '#7a8494' },
      splitLine: { lineStyle: { color: '#232830' } },
    },
    dataZoom: [
      { type: 'inside', filterMode: 'none' },
      { type: 'slider', filterMode: 'none', height: 20, bottom: 24,
        borderColor: '#2a2f37', backgroundColor: '#10141a',
        fillerColor: 'rgba(31,111,235,.18)', handleStyle: { color: '#1f6feb' },
        dataBackground: { lineStyle: { color: '#3a4453' }, areaStyle: { color: '#1c2128' } },
        textStyle: { color: '#7a8494' } },
    ],
    tooltip: {
      trigger: 'axis',
      confine: true,           // im Fenster halten, nicht oben/rechts rausrutschen
      backgroundColor: '#10141a',
      borderColor: '#2a2f37',
      textStyle: { color: '#e6e6e6', fontSize: 12 },
      extraCssText: 'max-height: 70vh; overflow: auto;',
      valueFormatter: v => fmt(v) + ' UPH',
      order: 'valueDesc',
    },
  };
}

function drawArea(data) {
  const keys = data.keys || [];
  const labelOf = k => (data.totals.find(t => t.key === k)?.label) || k;
  const empty = !data.buckets.length;

  const series = keys.map((k, i) => ({
    name: labelOf(k),
    type: 'line',
    stack: 'uph',
    showSymbol: false,
    lineStyle: { width: 1 },
    areaStyle: { opacity: 0.85 },
    color: colorFor(i),
    emphasis: { focus: 'series' },
    data: data.buckets.map(b => [b.at, b.series[k] || 0]),
  }));

  chart.setOption({
    ...baseOption(),
    toolbox: DRAG_ZOOM,
    legend: {
      type: 'scroll', top: 0, right: 8, left: 52,
      textStyle: { color: '#cdd6e0', fontSize: 11 },
      inactiveColor: '#5a6373',
    },
    graphic: empty ? [{
      type: 'text', left: 'center', top: 'middle',
      style: { text: 'keine Daten im Zeitraum', fill: '#7a8494', fontSize: 13 },
    }] : [],
    series,
  }, { notMerge: true });
  bindDragZoom(chart, $('area'));
  syncing = true; applyZoom(chart); syncing = false;   // Auswahl nach dem Neuaufbau wiederherstellen
}

// Zweiter Chart: Belegung (Fläche) und Leistung (Linie) des gewählten Ressourcenpunkts über
// denselben Zeitraum. Ist dem Punkt ein RBG zugeordnet, kommt die Reihe aus /api/rbg-history
// (Leistung gegen die Spielkapazität); sonst — auch für Fördertechnikpunkte — aus der
// Ressourcenpunkt-Langzeitreihe /api/point-history (Leistung = Aufträge/h gegen den Richtwert).
// Beide reichen unabhängig von den Rohtelegrammen zurück.
const C_BUSY = '#4fa3ff', C_LOAD = '#ffb454', C_IDLE = '#3a4150';
const rbgBucketFor = h => h <= 24 ? 5 : h <= 72 ? 15 : h <= 168 ? 30 : h <= 672 ? 240 : 1440;

async function loadRpChart() {
  const rp = $('rp').value;
  if (!rp) { $('rpCard').hidden = true; return; }
  const conn = current && current.resourcePointConnections
    ? current.resourcePointConnections[rp] : null;

  const q = new URLSearchParams({ hours: hours, bucket: rbgBucketFor(hours), rp });
  const path = conn ? '/api/rbg-history?' : '/api/point-history?';
  let d;
  try {
    const res = await fetch(API_BASE + path + q, { cache: 'no-store' });
    if (!res.ok) throw new Error('HTTP ' + res.status);
    d = await res.json();
  } catch { $('rpCard').hidden = true; return; }

  const b = d.buckets || [];
  const key = conn || rp;
  const idKey = conn ? 'connection' : 'resourcePoint';
  const list = conn ? d.connections : d.resourcePoints;
  const has = list && list.includes(key) && b.length;
  const total = d.totals && d.totals.find(t => t[idKey] === key);
  const label = total && total.label && total.label !== key ? `${total.label} · ${key}` : key;
  // Betriebsstunden nur nennen, wenn eine Nutzungszeit greift (weicht von der Kalenderdauer ab).
  const calH = (new Date(d.to) - new Date(d.from)) / 3.6e6;
  const opTxt = d.operatingHours && Math.abs(d.operatingHours - calH) > 0.05
    ? ` · Ø über ${fmt(d.operatingHours)} h Betrieb` : '';
  $('h-rp').textContent = (conn
    ? `Belegung & Leistung — ${label} (RBG)`
    : `Belegung & Leistung — ${label}`) + opTxt;
  $('rpCard').hidden = false;

  if (!rpChart) {
    rpChart = echarts.init($('rpChart'), null, { renderer: 'canvas' });
    rpChart.on('dataZoom', () => onZoom(rpChart));
  }

  const busy = b.map(pt => [pt.at, (pt.busyPercent && pt.busyPercent[key]) ?? 0]);
  const load = b.map(pt => [pt.at, (pt.loadPercent && pt.loadPercent[key]) ?? 0]);

  rpChart.setOption({
    ...baseOption(),
    toolbox: DRAG_ZOOM,
    yAxis: { type: 'value', name: '%', min: 0, max: 105,
      nameTextStyle: { color: '#7a8494' }, axisLabel: { color: '#7a8494' },
      splitLine: { lineStyle: { color: '#232830' } } },
    legend: { top: 0, right: 8, left: 52, textStyle: { color: '#cdd6e0', fontSize: 11 }, inactiveColor: '#5a6373' },
    tooltip: { trigger: 'axis', confine: true, backgroundColor: '#10141a', borderColor: '#2a2f37',
      textStyle: { color: '#e6e6e6', fontSize: 12 }, valueFormatter: v => fmt(v) + ' %' },
    graphic: has ? [] : [{ type: 'text', left: 'center', top: 'middle',
      style: { text: 'keine Historie im Zeitraum', fill: '#7a8494', fontSize: 13 } }],
    series: [
      { name: 'Leerlauf', type: 'line', showSymbol: false, lineStyle: { opacity: 0 }, color: C_IDLE,
        areaStyle: { origin: 100, color: C_IDLE, opacity: 0.4 },
        tooltip: { valueFormatter: v => fmt(100 - v) + ' % frei' }, data: busy },
      { name: 'Belegung', type: 'line', showSymbol: false, color: C_BUSY,
        lineStyle: { width: 1.6 }, areaStyle: { color: C_BUSY, opacity: 0.12 }, data: busy },
      { name: 'Leistung', type: 'line', showSymbol: false, color: C_LOAD,
        lineStyle: { width: 1.6 }, data: load },
    ],
  }, { notMerge: true });
  bindDragZoom(rpChart, $('rpChart'));
  syncing = true; applyZoom(rpChart); syncing = false;   // gespiegelte Auswahl übernehmen
}

function drawRatio(data) {
  const total = data.totalOrders || 1;
  $('ratio').innerHTML = data.totals.map((t, i) =>
    `<span style="width:${(t.orders / total * 100).toFixed(2)}%;background:${colorFor(i)}" title="${t.label}: ${fmt(t.share)} %"></span>`).join('');
  $('legend').innerHTML = data.totals.map((t, i) =>
    `<div><i style="background:${colorFor(i)}"></i>${t.label} · ${fmt(t.share)} %</div>`).join('');
}

// Tabelle: ohne Auswahl die Fenstersummen vom Server; mit Chart-Auswahl aus den Rastern des
// gewählten Zeitraums neu gerechnet. Im gleitenden Modus (8-h-Bereich) sind die Raster-Mengen
// überlappende Trailing-Summen — dort bleibt die Tabelle bei den Fenstersummen.
function renderTable() {
  const d = current;
  if (!d) return;
  const rowsHtml = (list, sum, tag) =>
    list.map(r => `
      <tr>
        <td><span class="sw" style="background:${colorFor(r.i)}"></span>${r.label}</td>
        <td>${fmt(r.avgUph)}</td>
        <td>${r.orders.toLocaleString('de-DE')}</td>
        <td>${fmt(r.share)} %</td>
      </tr>`).join('')
    + `<tr><td><b>Summe${tag}</b></td><td></td><td><b>${sum.toLocaleString('de-DE')}</b></td><td></td></tr>`;

  if (!sel || d.rollingMinutes > 0) {
    $('rows').innerHTML = rowsHtml(
      d.totals.map((t, i) => ({ ...t, i })), d.totalOrders,
      (sel && d.rollingMinutes > 0) ? ' (Auswahl nicht möglich im gleitenden Modus)' : '');
    return;
  }

  const [a, z] = sel;
  const inSel = (d.buckets || []).filter(b => { const t = +new Date(b.at); return t >= a && t <= z; });
  const spanH = Math.max(1e-9, (z - a) / 3.6e6);
  const ord = {};
  let total = 0;
  for (const b of inSel)
    for (const k in (b.orders || {})) { ord[k] = (ord[k] || 0) + b.orders[k]; total += b.orders[k]; }

  const rows = (d.keys || [])
    .map((k, i) => ({
      i,
      label: (d.totals.find(t => t.key === k)?.label) || k,
      orders: ord[k] || 0,
      avgUph: (ord[k] || 0) / spanH,
      share: total ? (ord[k] || 0) / total * 100 : 0,
    }))
    .filter(r => r.orders > 0)
    .sort((x, y) => y.orders - x.orders);

  $('rows').innerHTML = rowsHtml(rows, total, ' (Auswahl)');
}

let current = null;

function render(data) {
  current = data;
  const rp = $('rp');
  if (rp.options.length <= 1 && data.resourcePoints.length) {
    for (const p of data.resourcePoints) rp.add(new Option(p, p));
  }
  const byRp = data.groupBy === 'resourcePoint';
  const noun = byRp ? 'Ressourcenpunkt' : 'Endziel';
  const nounPl = byRp ? 'Ressourcenpunkte' : 'Ziele';
  $('h-area').textContent = `UPH je ${noun} (gestapelt)`;
  $('h-ratio').textContent = `Mengenverhältnis der ${nounPl}`;
  $('h-table').textContent = `${nounPl} im Zeitraum`;
  $('h-key').textContent = noun;

  drawArea(data);
  drawRatio(data);
  renderTable();
  loadRpChart();

  const from = new Date(data.from), to = new Date(data.to);
  $('meta').classList.remove('err');
  const grid = data.rollingMinutes > 0 ? `gleitend ${data.rollingMinutes} min` : `Raster ${data.bucketMinutes} min`;
  $('meta').textContent =
    `${data.totalOrders.toLocaleString('de-DE')} Aufträge · ${grid} · ` +
    `${from.toLocaleString('de-DE')} – ${to.toLocaleString('de-DE')}`;
}

async function load() {
  const q = new URLSearchParams();
  q.set('groupBy', $('dim').value);
  if ($('rp').value) q.set('rp', $('rp').value);
  q.set('hours', hours);
  if (rollingWin > 0) { q.set('rolling', rollingWin); q.set('bucket', 1); }
  else q.set('bucket', bucketFor(hours));
  try {
    const res = await fetch(API_BASE + '/api/uph-history?' + q, { cache: 'no-store' });
    if (!res.ok) throw new Error('HTTP ' + res.status);
    render(await res.json());
  } catch (e) {
    $('meta').classList.add('err');
    $('meta').textContent = 'Fehler: ' + e.message;
  }
}

// Druckreport: Auswahl + Zeitraum oben, Charts als PNG, Tabelle zum Schluss. Öffnet ein
// eigenständiges Fenster und ruft print() — als PDF speicherbar.
function openReport() {
  if (!current) return;
  const d = current;
  const rp = $('rp').value;
  const from = new Date(d.from), to = new Date(d.to);
  const grid = d.rollingMinutes > 0 ? `gleitend ${d.rollingMinutes} min` : `Raster ${d.bucketMinutes} min`;
  const png = c => c.getDataURL({ type: 'png', pixelRatio: 2, backgroundColor: '#0f1216' });
  const mainPng = png(chart);
  const rpPng = (rp && rpChart && !$('rpCard').hidden) ? png(rpChart) : null;

  const rows = [...$('rows').querySelectorAll('tr')].map(tr =>
    `<tr>${[...tr.children].map((td, i) => `<td${i ? ' class="n"' : ''}>${td.textContent.trim()}</td>`).join('')}</tr>`).join('');
  const esc = s => s.replace(/[&<>]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[c]));

  const w = window.open('', '_blank');
  w.document.write(`<!doctype html><html lang="de"><head><meta charset="utf-8">
    <title>UPH-Verlauf – Report</title><style>
      body { font: 13px/1.5 system-ui, sans-serif; color: #14171c; margin: 24px; }
      h1 { font-size: 19px; margin: 0 0 4px; }
      h2 { font-size: 14px; margin: 22px 0 6px; }
      .meta { color: #555; font-size: 12px; margin-bottom: 16px; }
      .meta b { color: #14171c; }
      img { width: 100%; border: 1px solid #d0d4da; border-radius: 6px; }
      table { border-collapse: collapse; width: 100%; margin-top: 4px; }
      th, td { border-bottom: 1px solid #d0d4da; padding: 5px 8px; text-align: left; }
      td.n, th.n { text-align: right; font-variant-numeric: tabular-nums; }
      thead th { border-bottom: 2px solid #14171c; }
      @page { margin: 16mm; }
    </style></head><body>
    <h1>UPH-Verlauf – Report</h1>
    <div class="meta">
      <b>${esc(rp || 'alle Ressourcenpunkte')}</b> · Stapelung: ${d.groupBy === 'resourcePoint' ? 'Ressourcenpunkt' : 'Endziel'}<br>
      Zeitraum <b>${from.toLocaleString('de-DE')}</b> – <b>${to.toLocaleString('de-DE')}</b> · ${grid}<br>
      ${d.totalOrders.toLocaleString('de-DE')} Aufträge · erstellt ${new Date().toLocaleString('de-DE')}
    </div>
    <h2>${esc($('h-area').textContent)}</h2>
    <img src="${mainPng}" alt="Verlauf">
    ${rpPng ? `<h2>${esc($('h-rp').textContent)}</h2><img src="${rpPng}" alt="Belegung & Leistung">` : ''}
    <h2>${esc($('h-table').textContent)}</h2>
    <table>
      <thead><tr><th>${esc($('h-key').textContent)}</th><th class="n">Ø UPH</th><th class="n">Aufträge</th><th class="n">Anteil</th></tr></thead>
      <tbody>${rows}</tbody>
    </table>
    <script>onload = () => { print(); }<\/script>
  </body></html>`);
  w.document.close();
}
$('report').addEventListener('click', openReport);

$('ranges').addEventListener('click', e => {
  const btn = e.target.closest('button');
  if (!btn) return;
  hours = parseInt(btn.dataset.h, 10);
  rollingWin = parseInt(btn.dataset.rolling, 10) || 0;
  for (const b of $('ranges').children) b.classList.toggle('on', b === btn);
  load();
});
$('rp').addEventListener('change', load);
$('dim').addEventListener('change', load);
load();
setInterval(load, 60000);
