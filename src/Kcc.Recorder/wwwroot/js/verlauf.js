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

// Der 60-s-Refresh baut den Chart mit notMerge neu — dabei ginge ein vom Nutzer gesetzter
// Zoom verloren. Vorher merken, nachher wiederherstellen, sofern nicht auf Vollbereich.
function keepZoom(c, redraw) {
  const prev = ((c.getOption() || {}).dataZoom || []).map(d => ({ start: d.start, end: d.end }));
  redraw();
  if (prev.some(d => (d.start ?? 0) > 0.01 || (d.end ?? 100) < 99.99))
    c.setOption({ dataZoom: prev });
}

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

  keepZoom(chart, () => chart.setOption({
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
  }, { notMerge: true }));
  bindDragZoom(chart, $('area'));
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

  if (!rpChart) rpChart = echarts.init($('rpChart'), null, { renderer: 'canvas' });

  const busy = b.map(pt => [pt.at, (pt.busyPercent && pt.busyPercent[key]) ?? 0]);
  const load = b.map(pt => [pt.at, (pt.loadPercent && pt.loadPercent[key]) ?? 0]);

  keepZoom(rpChart, () => rpChart.setOption({
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
  }, { notMerge: true }));
  bindDragZoom(rpChart, $('rpChart'));
}

function drawRatio(data) {
  const total = data.totalOrders || 1;
  $('ratio').innerHTML = data.totals.map((t, i) =>
    `<span style="width:${(t.orders / total * 100).toFixed(2)}%;background:${colorFor(i)}" title="${t.label}: ${fmt(t.share)} %"></span>`).join('');
  $('legend').innerHTML = data.totals.map((t, i) =>
    `<div><i style="background:${colorFor(i)}"></i>${t.label} · ${fmt(t.share)} %</div>`).join('');
}

function drawTable(data) {
  $('rows').innerHTML = data.totals.map((t, i) => `
    <tr>
      <td><span class="sw" style="background:${colorFor(i)}"></span>${t.label}</td>
      <td>${fmt(t.avgUph)}</td>
      <td>${t.orders.toLocaleString('de-DE')}</td>
      <td>${fmt(t.share)} %</td>
    </tr>`).join('')
    + `<tr><td><b>Summe</b></td><td></td><td><b>${data.totalOrders.toLocaleString('de-DE')}</b></td><td></td></tr>`;
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
  drawTable(data);
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
