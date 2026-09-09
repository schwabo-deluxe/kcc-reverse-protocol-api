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
addEventListener('resize', () => chart.resize());

// Mit der Maus einen Zeitbereich aufziehen (X-Zoom), wie in der alten HTML-Version.
// Dauerhaft aktiv; Doppelklick setzt zurück und schaltet es wieder scharf.
const DRAG_ZOOM = { show: false, feature: { dataZoom: { yAxisIndex: 'none', filterMode: 'none' } } };
const armDragZoom = () => chart.dispatchAction({ type: 'takeGlobalCursor', key: 'dataZoomSelect', dataZoomSelectActive: true });
$('area').addEventListener('dblclick', () => { chart.dispatchAction({ type: 'dataZoom', start: 0, end: 100 }); armDragZoom(); });

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
  armDragZoom();
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

function render(data) {
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
