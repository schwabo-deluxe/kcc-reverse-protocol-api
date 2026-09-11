'use strict';

const $ = id => document.getElementById(id);
const fmt = (n, d) => (n ?? 0).toLocaleString('de-DE', { maximumFractionDigits: d ?? 1 });
const API_BASE = (new URLSearchParams(location.search).get('api')
  || (/^https?:$/.test(location.protocol) ? location.origin : 'http://localhost:8082'))
  .replace(/\/+$/, '');
const H = window.RBG_HELP || {};
const esc = s => String(s).replace(/"/g, '&quot;');
const help = k => (H[k] ? ` title="${esc(H[k])}"` : '');

const PALETTE = ['#4fa3ff', '#5ccb7e', '#ffb454', '#e06c9f', '#9d7bff', '#4fd8d0', '#ff6b6b', '#c3cb5c'];
const C_BUSY = '#4fa3ff', C_LOAD = '#ffb454', C_IDLE = '#3a4150';

let hours = 168;
let metric = 'cyclesPerHour';
let data = null;

const colorOf = c => PALETTE[(data ? data.connections.indexOf(c) : 0) % PALETTE.length];
const nameOf = c => {
  const t = data && data.totals.find(x => x.connection === c);
  return t && t.label && t.label !== c ? `${t.label} · ${c}` : c;
};
const unitOf = m => (m === 'cyclesPerHour' ? '/h' : '%');
const metricTitle = m => m === 'busyPercent' ? 'Verlauf Auslastungsgrad'
  : m === 'loadPercent' ? 'Verlauf Leistungsgrad' : 'Verlauf Spiele/h';
const dur = h => (h >= 1 ? `${fmt(h)} h` : `${fmt(h * 60, 0)} min`);

// Raster: fein genug zum Erkennen, grob genug für lange Zeiträume.
const bucketFor = h => 5;

const mainChart = echarts.init($('chart'), null, { renderer: 'canvas' });
const minis = new Map();   // connection -> ECharts-Instanz
addEventListener('resize', () => { mainChart.resize(); minis.forEach(c => c.resize()); });

// Zeitfenster aus dem großen Chart auf alle kleinen Geräte-Charts spiegeln.
let syncingZoom = false;
function mainZoomWindow() {
  const dz = ((mainChart.getOption() || {}).dataZoom || [])[0] || {};
  const s = dz.start ?? 0, e = dz.end ?? 100;
  if (s <= 0.05 && e >= 99.95) return null;
  if (dz.startValue != null && dz.endValue != null) return [+dz.startValue, +dz.endValue];
  return null;
}
function applyMiniZoom() {
  if (syncingZoom) return;
  syncingZoom = true;
  const w = mainZoomWindow();
  for (const inst of minis.values())
    inst.dispatchAction(w ? { type: 'dataZoom', startValue: w[0], endValue: w[1] } : { type: 'dataZoom', start: 0, end: 100 });
  syncingZoom = false;
}

// Balken und Tabelle werten bei gesetztem Zoom nur den ausgewählten Zeitraum aus — eigene
// Serverabfrage, da die Balken/Kennzahlen (Doppelspiele, Leerlauf, …) nicht aus den geladenen
// Buckets zurückgerechnet werden können.
let selReq = 0;
async function applySelection() {
  applyMiniZoom();
  const w = mainZoomWindow();
  const my = ++selReq;
  if (!w) { verdict(); bars(); table(); return; }
  const q = new URLSearchParams();
  q.set('from', new Date(w[0]).toISOString());
  q.set('to', new Date(w[1]).toISOString());
  q.set('bucket', bucketFor(hours));
  try {
    const res = await fetch(`${API_BASE}/api/rbg-history?${q}`, { cache: 'no-store' });
    if (!res.ok || my !== selReq) return;
    const sel = await res.json();
    if (my !== selReq) return;
    verdict(sel); bars(sel); table(sel);
  } catch { /* Auswahl-Abfrage fehlgeschlagen — unverändert lassen */ }
}
mainChart.on('datazoom', applySelection);

// Aufziehen mit der Maus wählt einen Zeitbereich (X-Zoom), wie in der alten HTML-Version.
// ECharts kann das dauerhaft aktiv halten (sonst bräuchte es erst einen Toolbox-Klick).
// Doppelklick setzt zurück und schaltet das Aufziehen wieder scharf.
const dragZoom = { show: false, feature: { dataZoom: { yAxisIndex: 'none', filterMode: 'none' } } };
function armDragZoom(inst, el) {
  const arm = () => inst.dispatchAction({ type: 'takeGlobalCursor', key: 'dataZoomSelect', dataZoomSelectActive: true });
  arm();
  if (el.dataset.zoomBound) return;
  el.dataset.zoomBound = '1';
  el.addEventListener('dblclick', () => { inst.dispatchAction({ type: 'dataZoom', start: 0, end: 100 }); arm(); });
}

// Der 5-min-Refresh baut die Charts mit notMerge neu — ohne das hier ginge ein gesetzter
// Zoom dabei verloren. Vorher merken, nachher wiederherstellen (außer bei Vollbereich).
function keepZoom(inst, redraw) {
  const prev = ((inst.getOption() || {}).dataZoom || []).map(d => ({ start: d.start, end: d.end }));
  redraw();
  if (prev.some(d => (d.start ?? 0) > 0.01 || (d.end ?? 100) < 99.99))
    inst.setOption({ dataZoom: prev });
}

function darkBase(extra) {
  return Object.assign({
    backgroundColor: 'transparent',
    textStyle: { color: '#9aa4b2', fontFamily: 'system-ui, sans-serif' },
    tooltip: {
      trigger: 'axis',
      confine: true,
      extraCssText: 'max-height: 70vh; overflow: auto;',
      backgroundColor: '#10141a', borderColor: '#2a2f37',
      textStyle: { color: '#e6e6e6', fontSize: 12 },
    },
    xAxis: {
      type: 'time',
      axisLine: { lineStyle: { color: '#2a2f37' } },
      axisLabel: { color: '#7a8494', hideOverlap: true },
      splitLine: { show: false },
    },
    yAxis: {
      type: 'value',
      axisLabel: { color: '#7a8494' },
      splitLine: { lineStyle: { color: '#232830' } },
    },
  }, extra || {});
}

function drawMain() {
  const b = data.buckets;
  const empty = !b.length || !data.connections.length;

  const series = data.connections.map(c => ({
    name: nameOf(c),
    type: 'line',
    showSymbol: false,
    lineStyle: { width: 1.6 },
    color: colorOf(c),
    emphasis: { focus: 'series' },
    data: b.map(pt => [pt.at, pt[metric][c] ?? 0]),
  }));

  // Kapazitätslinie nur bei Spiele/h.
  if (metric === 'cyclesPerHour' && series.length) {
    const cap = Math.max(...data.totals.map(t => t.maxCyclesPerHour || 0));
    if (cap > 0) {
      series[0].markLine = {
        symbol: 'none', silent: true,
        lineStyle: { color: '#7a8494', type: 'dashed' },
        label: { color: '#7a8494', formatter: `Kapazität ${cap}/h`, position: 'insideEndTop' },
        data: [{ yAxis: cap }],
      };
    }
  }

  keepZoom(mainChart, () => mainChart.setOption(darkBase({
    toolbox: dragZoom,
    grid: { left: 52, right: 16, top: 34, bottom: 60 },
    legend: {
      type: 'scroll', top: 0, left: 52, right: 8,
      textStyle: { color: '#cdd6e0', fontSize: 11 }, inactiveColor: '#5a6373',
    },
    yAxis: {
      type: 'value', name: unitOf(metric),
      nameTextStyle: { color: '#7a8494' },
      max: metric === 'cyclesPerHour' ? null : 105,
      axisLabel: { color: '#7a8494' },
      splitLine: { lineStyle: { color: '#232830' } },
    },
    dataZoom: [
      { type: 'inside', filterMode: 'none' },
      { type: 'slider', filterMode: 'none', height: 18, bottom: 24,
        borderColor: '#2a2f37', backgroundColor: '#10141a',
        fillerColor: 'rgba(31,111,235,.18)', handleStyle: { color: '#1f6feb' },
        dataBackground: { lineStyle: { color: '#3a4453' }, areaStyle: { color: '#1c2128' } },
        textStyle: { color: '#7a8494' } },
    ],
    tooltip: {
      trigger: 'axis', order: 'valueDesc',
      confine: true,
      extraCssText: 'max-height: 70vh; overflow: auto;',
      backgroundColor: '#10141a', borderColor: '#2a2f37',
      textStyle: { color: '#e6e6e6', fontSize: 12 },
      valueFormatter: v => `${fmt(v)} ${unitOf(metric)}`,
    },
    graphic: empty ? [{ type: 'text', left: 'center', top: 'middle',
      style: { text: 'keine Daten im Zeitraum', fill: '#7a8494', fontSize: 13 } }] : [],
    series,
  }), { notMerge: true }));
  armDragZoom(mainChart, $('chart'));

  $('chartTitle').textContent = metricTitle(metric);
}

function drawMinis() {
  const b = data.buckets;
  // Container aufbauen und je RBG eine ECharts-Instanz binden.
  $('minis').innerHTML = data.totals.map(t => `
    <div class="mini" data-c="${t.connection}">
      <div class="mh">
        <span class="mn"><span class="sw" style="background:${colorOf(t.connection)}"></span>${nameOf(t.connection)}</span>
        <span class="ms">
          <span${help('busy')}>Auslastung <b style="color:${C_BUSY}">${fmt(t.avgBusyPercent)} %</b></span>
          <span${help('load')}>Leistung <b style="color:${C_LOAD}">${fmt(t.avgLoadPercent)} %</b></span>
          <span${help('inout')}>Ein/Aus <b>${fmt(t.stores)}</b> / <b>${fmt(t.retrievals)}</b></span>
          <span${help('idle')}>Leerlauf <b>${dur(t.idleHours)}</b></span>
        </span>
      </div>
      <div class="plot"></div>
    </div>`).join('');

  for (const el of minis.values()) el.dispose();
  minis.clear();

  for (const box of $('minis').querySelectorAll('.mini')) {
    const c = box.dataset.c;
    const plot = box.querySelector('.plot');
    const inst = echarts.init(plot, null, { renderer: 'canvas' });
    minis.set(c, inst);
    inst.setOption(darkBase({
      toolbox: dragZoom,
      dataZoom: [{ type: 'inside', filterMode: 'none' }],
      grid: { left: 30, right: 10, top: 10, bottom: 22 },
      yAxis: { type: 'value', min: 0, max: 100, interval: 50,
        axisLabel: { color: '#7a8494' }, splitLine: { lineStyle: { color: '#232830' } } },
      xAxis: { type: 'time', axisLabel: { color: '#7a8494', hideOverlap: true },
        axisLine: { lineStyle: { color: '#2a2f37' } }, splitLine: { show: false } },
      tooltip: {
        trigger: 'axis', confine: true,
        backgroundColor: '#10141a', borderColor: '#2a2f37',
        textStyle: { color: '#e6e6e6', fontSize: 12 },
      },
      series: [
        // Leerlauf: von der Auslastungskurve bis 100 % gefüllt.
        { name: 'Leerlauf', type: 'line', showSymbol: false, symbol: 'none',
          lineStyle: { opacity: 0 }, color: C_IDLE,
          areaStyle: { origin: 100, color: C_IDLE, opacity: 0.45 },
          tooltip: { valueFormatter: v => `${fmt(100 - v)} % frei` },
          data: b.map(pt => [pt.at, pt.busyPercent[c] ?? 0]) },
        { name: 'Auslastung', type: 'line', showSymbol: false, color: C_BUSY,
          lineStyle: { width: 1.6 }, areaStyle: { color: C_BUSY, opacity: 0.12 },
          tooltip: { valueFormatter: v => `${fmt(v)} %` },
          data: b.map(pt => [pt.at, pt.busyPercent[c] ?? 0]) },
        { name: 'Leistung', type: 'line', showSymbol: false, color: C_LOAD,
          lineStyle: { width: 1.6 },
          tooltip: { valueFormatter: v => `${fmt(v)} %` },
          data: b.map(pt => [pt.at, pt.loadPercent[c] ?? 0]) },
      ],
    }), { notMerge: true });
    armDragZoom(inst, plot);
  }
  applyMiniZoom();
}

function verdict(d) {
  d = d || data;
  const t = d.totals.filter(x => x.cycles > 0);
  const spread = d.spreadPercent;
  const tone = spread >= 30 ? '#ff6b6b' : spread >= 15 ? '#ffb454' : '#5ccb7e';
  const say = t.length < 2
    ? 'Zu wenig Bewegung im Zeitraum für einen Vergleich.'
    : `<b>${nameOf(d.busiest)}</b> fährt am meisten (${fmt(t[0].cycles)} Spiele), ` +
      `<b>${nameOf(d.quietest)}</b> am wenigsten (${fmt(t[t.length - 1].cycles)} Spiele) — ` +
      `also ${fmt(spread)} % weniger.`;
  $('verdict').innerHTML = `
    <div${help('spread')}>
      <div class="big" style="color:${tone}">${fmt(spread)} %</div>
      <div class="cap">Spreizung</div>
    </div>
    <div${help('cycles')}>
      <div class="big">${fmt(d.totalCycles)}</div>
      <div class="cap">Spiele gesamt</div>
    </div>
    <div>
      <div class="big">${t.length}</div>
      <div class="cap">RBG mit Bewegung</div>
    </div>
    <div class="say">${say}</div>`;
}

function bars(d) {
  d = d || data;
  const t = d.totals;
  const even = t.length ? 100 / t.length : 0;
  const max = Math.max(1, ...t.map(x => x.share));
  // Der Balken zeigt den Anteil am Gesamtdurchsatz, in sich aufgeteilt nach Ein- und
  // Auslagerung — so ist je RBG sofort sichtbar, ob eine Richtung überwiegt.
  $('bars').innerHTML = t.map(x => {
    const w = (x.share / max * 100).toFixed(1);
    const io = x.stores + x.retrievals;
    const sw = io > 0 ? (x.stores / io * 100).toFixed(1) : '50';
    const col = colorOf(x.connection);
    return `
    <div class="nm">${x.label && x.label !== x.connection ? `${x.label}<br><small>${x.connection}</small>` : x.connection}</div>
    <div class="track"${help('inout')}>
      <div class="split" style="width:${w}%">
        <div class="seg" style="width:${sw}%;background:${col}" title="${fmt(x.stores)} Einlagerungen"></div>
        <div class="seg" style="width:${(100 - sw).toFixed(1)}%;background:${col};opacity:.45" title="${fmt(x.retrievals)} Auslagerungen"></div>
      </div>
      <div class="avg" style="left:${(even / max * 100).toFixed(1)}%"></div>
    </div>
    <div class="val"><b>${fmt(x.share)} %</b> · ${fmt(x.stores)} Ein / ${fmt(x.retrievals)} Aus · ${fmt(x.avgCyclesPerHour)} Spiele/h</div>`;
  }).join('');
}

function table(d) {
  d = d || data;
  $('head').innerHTML =
    `<th>RBG</th><th${help('inout')}>Ein</th><th${help('inout')}>Aus</th>` +
    `<th${help('double')}>Doppelspiele</th><th${help('single')}>Einzelspiele</th>` +
    `<th${help('cycles')}>Spiele</th><th${help('share')}>Anteil</th>` +
    `<th${help('avgcycles')}>Ø Spiele/h</th><th${help('peak')}>Spitze</th>` +
    `<th${help('load')}>Ø Leistung</th><th${help('busy')}>Ø Auslastung</th>` +
    `<th${help('idle')}>Leerlauf</th><th${help('active')}>Aktive Std.</th>`;
  $('rows').innerHTML = d.totals.map(t => `
    <tr>
      <td><span class="sw" style="background:${colorOf(t.connection)}"></span>${nameOf(t.connection)}</td>
      <td>${t.stores}</td><td>${t.retrievals}</td>
      <td>${t.doubleCycles}</td><td>${t.singleCycles}</td>
      <td><b>${fmt(t.cycles)}</b></td>
      <td>${fmt(t.share)} %</td>
      <td>${fmt(t.avgCyclesPerHour)}</td>
      <td>${fmt(t.peakCyclesPerHour)}</td>
      <td>${fmt(t.avgLoadPercent)} %</td>
      <td>${fmt(t.avgBusyPercent)} %</td>
      <td>${dur(t.idleHours)}</td>
      <td>${fmt(t.activeHours)}</td>
    </tr>`).join('');
}

function render(d) {
  data = d;
  drawMain();
  drawMinis();
  applySelection();

  const from = new Date(d.from), to = new Date(d.to);
  const sameDay = from.toDateString() === to.toDateString();
  const stamp = x => sameDay
    ? x.toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' })
    : x.toLocaleDateString('de-DE');
  // Betriebsstunden nur nennen, wenn sie von der Kalenderdauer abweichen (Nutzungszeit aktiv).
  const calHours = (to - from) / 3.6e6;
  const opTxt = d.operatingHours && Math.abs(d.operatingHours - calHours) > 0.05
    ? ` · Betrieb ${fmt(d.operatingHours)} h (Totzeiten raus)` : '';
  $('meta').classList.remove('err');
  $('meta').textContent =
    `${from.toLocaleDateString('de-DE')} ${stamp(from)} – ${stamp(to)} · ` +
    `Raster ${d.bucketMinutes} min${opTxt} · Stand ${new Date().toLocaleTimeString('de-DE')}`;
}

async function load() {
  const q = new URLSearchParams();
  q.set('hours', hours);
  q.set('bucket', bucketFor(hours));
  try {
    const res = await fetch(`${API_BASE}/api/rbg-history?${q}`, { cache: 'no-store' });
    if (!res.ok) throw new Error('HTTP ' + res.status);
    render(await res.json());
  } catch (e) {
    $('meta').classList.add('err');
    $('meta').textContent = 'Fehler: ' + e.message;
  }
}

$('range').addEventListener('click', e => {
  const btn = e.target.closest('button');
  if (!btn) return;
  hours = parseInt(btn.dataset.h, 10);
  for (const b of $('range').children) b.classList.toggle('on', b === btn);
  load();
});
$('metric').addEventListener('click', e => {
  const btn = e.target.closest('button');
  if (!btn) return;
  metric = btn.dataset.m;
  for (const b of $('metric').children) b.classList.toggle('on', b === btn);
  if (data) drawMain();
});

load();
setInterval(load, 300000);
