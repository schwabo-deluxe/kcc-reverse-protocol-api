'use strict';

const $ = id => document.getElementById(id);
const fmt = n => n.toLocaleString('de-DE', { maximumFractionDigits: 1 });

// Erklärtexte der RBG-Kennzahlen (glossary.js setzt window.RBG_HELP).
const H = window.RBG_HELP || {};
const help = k => (H[k] ? ` title="${String(H[k]).replace(/"/g, '&quot;')}"` : '');

// Wird die Seite über die API selbst ausgeliefert (http/https), zählt die eigene Herkunft.
// Als lose Datei (file://) sonst nichts erreichbar — dann fest auf den lokalen Standard.
// Mit "?api=http://host:port" überschreibbar.
const API_BASE = (new URLSearchParams(location.search).get('api')
  || (/^https?:$/.test(location.protocol) ? location.origin : 'http://localhost:8082'))
  .replace(/\/+$/, '');

function color(pct) {
  if (pct >= 95) return '#ff6b6b';
  if (pct >= 80) return '#ffb454';
  return '#5ccb7e';
}

// Halbkreis-Tacho (nur Bogen + Zeiger, Zahl steht daneben): farbiger Wertbogen,
// Markierung bei 100 % vom Richtwert.
//
// Der Zielwert steht als data-Attribut am SVG; gezeichnet wird erst von paintGauge, das
// beim Aktualisieren vom zuletzt gezeigten Wert zum neuen überblendet (tweenGauges).
// Sonst würde der Zeiger bei jedem Abruf springen.
const GPT = (frac, rad) => {
  const t = Math.PI * (1 - frac);
  return [100 + rad * Math.cos(t), 92 - rad * Math.sin(t)];
};
const gArc = (frac, r) => {
  const [x1, y1] = GPT(0, r), [x2, y2] = GPT(Math.max(0.0001, frac), r);
  return `M${x1.toFixed(1)} ${y1.toFixed(1)} A${r} ${r} 0 0 1 ${x2.toFixed(1)} ${y2.toFixed(1)}`;
};

// Zuletzt gezeigter Wert je Tacho — Ausgangspunkt der nächsten Überblendung.
const gPrev = new Map();

function gauge(value, max, stroke, cls, key) {
  const [t1x, t1y] = GPT(Math.min(1, 100 / max), 88);
  const [t2x, t2y] = GPT(Math.min(1, 100 / max), 72);
  return `<svg class="gauge ${cls || ''}" viewBox="0 0 200 104"
               data-g-key="${key || ''}" data-g-value="${value}" data-g-max="${max}">
    <path class="track" d="${gArc(1, 80)}" fill="none" stroke-width="13" stroke-linecap="round"/>
    <path class="val" d="" fill="none" stroke="${stroke}" stroke-width="6" stroke-linecap="round"/>
    <line class="tick" x1="${t1x.toFixed(1)}" y1="${t1y.toFixed(1)}" x2="${t2x.toFixed(1)}" y2="${t2y.toFixed(1)}" stroke-width="2.5"/>
    <circle class="needle" r="7" fill="${stroke}" stroke="#14171c" stroke-width="2.5"/>
  </svg>`;
}

function paintGauge(svg, value, max) {
  const f = Math.max(0, Math.min(value / max, 1));
  const tone = color(value);
  const val = svg.querySelector('.val'), needle = svg.querySelector('.needle');
  const [mx, my] = GPT(f, 80);
  val.setAttribute('d', f > 0 ? gArc(f, 80) : '');
  val.setAttribute('stroke', tone);
  needle.setAttribute('cx', mx.toFixed(1));
  needle.setAttribute('cy', my.toFixed(1));
  needle.setAttribute('fill', tone);
}

// Blendet alle Tachos (und ihre Prozentzahl) vom letzten auf den neuen Wert über.
// Kleine Sprünge kriechen langsam (SLOW), große laufen zügig durch (FAST) — dazwischen linear.
const TWEEN_SLOW = 10000, TWEEN_FAST = 1500;
const TWEEN_SMALL = 3, TWEEN_BIG = 30;   // Prozentpunkte Wertänderung
function tweenDurationFor(maxDelta) {
  const t = Math.min(1, Math.max(0, (maxDelta - TWEEN_SMALL) / (TWEEN_BIG - TWEEN_SMALL)));
  return Math.round(TWEEN_SLOW + (TWEEN_FAST - TWEEN_SLOW) * t);
}

// Zusätzliche Glättung: der rohe Wert je Abruf springt (kurzes Trailing-Fenster, wenige
// Ereignisse). Ein exponentieller gleitender Mittelwert dämpft den Sprung, bevor überblendet
// wird — die Folge der Ziele wird eine weiche Kurve statt einer Treppe. Kleines Alpha = glatter,
// aber träger; der Zeiger läuft dem echten Wert dann etwas hinterher.
const TWEEN_ALPHA = 0.45;
const gSmooth = new Map();

function tweenGauges() {
  const items = [...document.querySelectorAll('svg.gauge[data-g-value]')].map(el => {
    const key = el.dataset.gKey;
    const raw = parseFloat(el.dataset.gValue) || 0;
    const to = key
      ? (gSmooth.has(key) ? gSmooth.get(key) + (raw - gSmooth.get(key)) * TWEEN_ALPHA : raw)
      : raw;
    if (key) gSmooth.set(key, to);
    return {
      el, key, to,
      max: parseFloat(el.dataset.gMax) || 100,
      from: key && gPrev.has(key) ? gPrev.get(key) : to,
      txt: key ? document.querySelector(`[data-g-txt="${key}"]`) : null,
    };
  });
  if (!items.length) return;

  // Linear: gleichmäßiges Wandern des Zeigers über die ganze Dauer, kein Auslaufen.
  const paint = k => {
    for (const it of items) {
      const v = it.from + (it.to - it.from) * k;
      paintGauge(it.el, v, it.max);
      if (it.txt) {
        it.txt.textContent = fmt(v) + ' %';
        it.txt.style.color = color(v);
      }
    }
  };

  // Endwert merken — er ist der Startpunkt der nächsten Überblendung. Muss auch dann
  // passieren, wenn nicht animiert wird, sonst fehlt beim nächsten Mal der Ausgangswert.
  const remember = () => { for (const it of items) if (it.key) gPrev.set(it.key, it.to); };

  // Beim ersten Aufbau (oder ohne Bewegung) direkt zeichnen statt zu animieren.
  const maxDelta = Math.max(...items.map(it => Math.abs(it.to - it.from)));
  if (maxDelta <= 0.05) { paint(1); remember(); return; }

  // Dauer nach der größten Wertänderung aller Tachos: kleine Sprünge langsam, große zügig.
  const ms = tweenDurationFor(maxDelta);

  // Sofort den Ausgangszustand zeichnen: das frische SVG hat noch keinen Bogen.
  paint(0);

  // Im Hintergrund-Tab liefert requestAnimationFrame keine Frames. Ohne diesen
  // Rückfall bliebe der Tacho dann auf dem alten Wert stehen.
  let done = false;
  const finish = () => { if (!done) { done = true; paint(1); remember(); } };
  const fallback = setTimeout(finish, ms + 250);

  const t0 = performance.now();
  const step = now => {
    if (done) return;
    const k = Math.min(1, (now - t0) / ms);
    paint(k);
    if (k < 1) { requestAnimationFrame(step); return; }
    clearTimeout(fallback);
    finish();
  };
  requestAnimationFrame(step);
}

// Verlauf als Sparkline. Bei RBG-Kacheln zählt der Spiele/h-Verlauf, sonst die UPH-Reihe.
function spark(point, scaleMax, target) {
  const w = 240, h = 88, s = point.rbg ? point.rbg.series : point.series;
  if (s.length < 2) return `<svg class="spark" viewBox="0 0 ${w} ${h}"></svg>`;

  const x = i => (i / (s.length - 1)) * w;
  const y = v => h - (Math.min(v, scaleMax) / scaleMax) * h;
  const line = s.map((b, i) => `${i ? 'L' : 'M'}${x(i).toFixed(1)},${y(b.uph).toFixed(1)}`).join(' ');
  const stroke = color(point.percent);
  const targetY = y(target);

  const dots = s.map((b, i) =>
    `<circle class="dot" cx="${x(i).toFixed(1)}" cy="${y(b.uph).toFixed(1)}" fill="${stroke}" ` +
    `opacity="0" data-i="${i}"></circle>`).join('');

  return `<svg class="spark" viewBox="0 0 ${w} ${h}" preserveAspectRatio="none"
               data-point="${point.resourcePoint}">
    <line class="grid" x1="0" y1="${h}" x2="${w}" y2="${h}"></line>
    ${targetY >= 0 ? `<line class="target" x1="0" y1="${targetY.toFixed(1)}" x2="${w}" y2="${targetY.toFixed(1)}"></line>` : ''}
    <path class="line" d="${line}" stroke="${stroke}"></path>
    ${dots}
    <line class="cursor" y1="0" y2="${h}"></line>
    <rect class="hit" x="0" y="0" width="${w}" height="${h}"></rect>
  </svg>`;
}

let current = null;

function render(data) {
  current = data;
  $('minutes').value = data.windowMinutes;
  $('target').value = data.targetUph;
  $('bucket').value = data.bucketMinutes;
  $('rate').value = data.rateMinutes;
  $('rateFt').value = data.conveyorRateMinutes;

  // Eine UPH-Skala für alle Nicht-RBG-Kacheln — mindestens bis zum Richtwert.
  const peak = Math.max(
    data.targetUph,
    ...data.points.filter(p => !p.rbg).flatMap(p => p.series.map(b => b.uph)));

  const byName = n => data.points.find(p => p.resourcePoint === n);

  // Label ersetzt den Punkt nicht, es ergänzt ihn — der Code bleibt immer sichtbar.
  const heading = p => (p.label && p.label !== p.resourcePoint)
    ? `${p.label} <span class="code">${p.resourcePoint}</span>`
    : p.resourcePoint;
  const rowName = p => (p.label && p.label !== p.resourcePoint)
    ? `${p.label} · ${p.resourcePoint}`
    : p.resourcePoint;

  // Endziele: LINKS(RECHTS(Datenfeld;33);4). Anteil, welches Ziel den Punkt überfährt.
  const destTable = p => {
    const d = p.destinations || [];
    if (!d.length) return '';
    const rows = d.slice(0, 8).map(x =>
      `<tr><td>${x.label || x.target}</td><td>${fmt(x.percent)} %</td><td>${x.count}</td></tr>`).join('');
    const rest = d.length > 8
      ? `<tr><td>… ${d.length - 8} weitere</td><td>${fmt(d.slice(8).reduce((a, x) => a + x.percent, 0))} %</td><td>${d.slice(8).reduce((a, x) => a + x.count, 0)}</td></tr>`
      : '';
    return `<table class="dest"><thead><tr><th>Ziel</th><th>Anteil</th><th>n</th></tr></thead><tbody>${rows}${rest}</tbody></table>`;
  };

  // Dauer lesbar: Sekunden bzw. m:ss.
  const dur = s => s < 60 ? `${Math.round(s)} s`
    : `${Math.floor(s / 60)}:${String(Math.round(s % 60)).padStart(2, '0')} min`;

  // Mengen als Rate pro Stunde, hochgerechnet aus dem Trailing-Fenster der Tachos
  // ("Tacho RBG" bzw. "Tacho FT" in der Kopfzeile) — dieselbe Basis wie Auslastung und Leistung.
  const rbgHours = Math.max(1e-9, data.rateMinutes / 60);
  const ftHours = Math.max(1e-9, data.conveyorRateMinutes / 60);
  const perHrbg = n => fmt(n / rbgHours);
  const perHft = n => fmt(n / ftHours);

  // Fördertechnik: Durchsatz je Meldung pro Stunde + Transport- und Wartezeiten.
  const convRow = p => {
    const c = p.conveyor;
    if (!c) return '';
    // Belegung und Ø Verweildauer stehen am Tacho darüber.
    return `<div class="rbg">
      <span${help('ccount')}>Ankunft/Auftrag/Frei <b>${perHft(c.completed)}/${perHft(c.orders)}/${perHft(c.freeSignals)}</b> /h</span>
      <span${help('corderwait')}>Ø bis Auftrag <b>${dur(c.avgOrderWaitSeconds)}</b></span>
      <span${help('cdepart')}>Ø Abtransport <b>${dur(c.avgDepartSeconds)}</b></span>
      <span${help('cwait')}>Ø leer <b>${dur(c.avgIdleSeconds)}</b></span>
      <span${help('cidle')}>Leer gesamt <b>${dur(c.idleSeconds)}</b></span>
    </div>`;
  };

  const rbgRow = p => {
    const r = p.rbg;
    if (!r) return '';
    // Auslastung, Leistung und Leerlauf stehen an den Tachos darüber — hier nur, was
    // dort nicht hinpasst. Spiele und Ein/Aus als Rate pro Stunde.
    return `<div class="rbg">
      <span${help('double')}>Doppelspiele <b>${perHrbg(r.doubleCycles)}/h</b></span>
      <span${help('single')}>Einzelspiele <b>${perHrbg(r.singleCycles)}/h</b></span>
      <span${help('inout')}>Ein/Aus <b>${perHrbg(r.stores)}/${perHrbg(r.retrievals)}</b> /h</span>
      <span${help('avgdur')}>Ø Transport ein <b>${dur(r.avgStoreSeconds)}</b> / aus <b>${dur(r.avgRetrieveSeconds)}</b></span>
    </div>`;
  };

  // Ein Tacho mit Beschriftung darunter. 'key' bindet ihn über die Aktualisierung
  // hinweg an denselben Punkt, damit der Zeiger überblenden kann statt zu springen.
  const dial = (value, label, sub, titleKey, key) => `
    <div class="gauge-col"${help(titleKey)}>
      ${gauge(value, 150, color(value), '', key)}
      <div class="pct" data-g-txt="${key}" style="color:${color(value)}">${fmt(value)} %</div>
      <div class="gcap">${label}</div>
      ${sub ? `<div class="gsub">${sub}</div>` : ''}
    </div>`;

  // Tacho(s) links, Verlauf rechts auf gleicher Höhe. RBG-Kacheln zeigen beide Seiten:
  // Auslastung (Zeit mit Auftrag) und Leistung (Spiele/h gegen die Kapazität) — letztere
  // ausdrücklich aus Doppel-/Einzelspielen, nicht aus den TSPORD des Ressourcenpunkts.
  const tile = p => {
    const r = p.rbg;
    // Skala knapp über Kapazität bzw. Spitzenwert — bei 1,3× Kapazität nutzte die Kurve
    // nur einen Bruchteil der Höhe und wirkte platt.
    const sMax = r
      ? Math.max(r.maxCyclesPerHour * 1.1, 1, ...r.series.map(b => b.uph * 1.1))
      : peak;
    const sTarget = r ? r.maxCyclesPerHour : data.targetUph;
    // Der Tacho rechnet die letzten 'rateMinutes' auf eine Stunde hoch; die Kurve
    // zeigt denselben Wert gleitend über 'bucketMinutes'. Beide Fenster stehen dabei.
    // Die Leistungszahl steht am Tacho — hier nur noch die Mengen, die er nicht zeigt.
    const head = r
      ? `${p.count} TSPORD`
      : `${p.rateCount}/${data.conveyorRateMinutes}m · ${p.count} ges.`;
    const c = p.conveyor;
    const dials = r
      ? `<div class="duo">
           ${dial(r.busyPercent, 'Auslastung', `Leerlauf ${dur(r.idleSeconds)}`, 'busy', `${p.resourcePoint}:busy`)}
           ${dial(r.percent, 'Leistung', `${fmt(r.cyclesPerHour)} / ${r.maxCyclesPerHour} Spiele/h`, 'load', `${p.resourcePoint}:load`)}
         </div>`
      : c
      ? `<div class="duo">
           ${dial(c.busyPercent, 'Belegung', `Ø belegt ${dur(c.avgOccupiedSeconds)}`, 'cbusy', `${p.resourcePoint}:busy`)}
           ${dial(p.percent, 'Leistung', `${fmt(p.uph)} / ${fmt(p.targetUph)} UPH`, 'load', `${p.resourcePoint}:uph`)}
         </div>`
      : dial(p.percent, '% vom Richtwert', '', null, `${p.resourcePoint}:uph`);
    return `
    <div class="tile" data-rp="${p.resourcePoint}">
      <div class="tile-head">
        <span class="label">${heading(p)}</span>
        <span class="sub">${head}</span>
      </div>
      <div class="tile-body">
        ${dials}
        <div class="spark-col">
          ${spark(p, sMax, sTarget)}
          <div class="axis">
            <span>vor ${data.windowMinutes} min</span>
            <span class="unit">${r ? 'Spiele/h' : 'UPH'} ⌀${data.bucketMinutes} min</span>
            <span>jetzt</span>
          </div>
        </div>
      </div>
      ${rbgRow(p)}
      ${convRow(p)}
      ${destTable(p)}
      <div class="grip" aria-hidden="true"></div>
    </div>`;
  };

  // Kacheln nach Gruppe gebündelt. Reine RBG-Gruppen werden in Spielen summiert,
  // nicht in UPH — sonst stünde neben dem Spiele-Tacho eine TSPORD-Zahl.
  const grpSum = g => {
    const members = g.points.map(n => byName(n)).filter(Boolean);
    // Der Ø-Wert steht im Gruppen-Tacho rechts daneben.
    if (members.length && members.every(p => p.rbg)) {
      const cph = members.reduce((a, p) => a + p.rbg.cyclesPerHour, 0);
      const max = members.reduce((a, p) => a + p.rbg.maxCyclesPerHour, 0);
      return `${fmt(cph)} / ${max} Spiele/h · ${g.count} TSPORD`;
    }
    const rm = members.length && members.every(p => p.conveyor)
      ? data.conveyorRateMinutes : data.rateMinutes;
    return `${fmt(g.uph)} / ${fmt(g.targetUph)} UPH · ` +
      `${g.rateCount}/${rm}m · ${g.count} ges.`;
  };

  $('tiles').innerHTML = data.groups.map(g => `
    <section class="grp">
      <div class="grp-h">
        <span class="grp-name">${g.name}</span>
        <span class="grp-sum" style="color:${color(g.percent)}">${grpSum(g)}</span>
        ${gauge(g.percent, 150, color(g.percent), 'sm', `grp:${g.name}`)}
      </div>
      <div class="tiles">${g.points.map(n => tile(byName(n))).join('')}</div>
    </section>`).join('');

  // Tabelle: je Gruppe eine Summenzeile, darunter die Punkte.
  $('rows').innerHTML = data.groups.map(g => `
    <tr class="grp">
      <td>${g.name}</td><td>${g.count}</td><td>${fmt(g.uph)}</td>
      <td style="color:${color(g.percent)}">Ø ${fmt(g.percent)} %</td>
      <td class="${g.errors ? 'err' : ''}">${g.errors}</td><td></td>
    </tr>
    ${g.points.map(n => byName(n)).map(p => `
      <tr>
        <td class="sub-row">${rowName(p)}</td>
        <td>${p.count}</td><td>${fmt(p.uph)}</td>
        <td style="color:${color(p.percent)}">${fmt(p.percent)} %</td>
        <td class="${p.errors ? 'err' : ''}">${p.errors}</td>
        <td>${p.latestAt ? new Date(p.latestAt).toLocaleTimeString('de-DE') : '–'}</td>
      </tr>`).join('')}`).join('');

  tweenGauges();
  layoutTiles();

  $('meta').classList.remove('err');
  $('meta').textContent =
    `${data.totalOrders} TSPORD in ${data.windowMinutes} min · Verlauf gleitend ${data.bucketMinutes} min · Tacho RBG ${data.rateMinutes} min / FT ${data.conveyorRateMinutes} min` +
    ` · Stand ${new Date().toLocaleTimeString('de-DE')}`;
}

/* ------------------------------------------------------------------ *
 * Freie Kachel-Anordnung. Position/Größe je Kachel liegen in
 * localStorage und werden bei jedem Render neu angewandt.
 * ------------------------------------------------------------------ */

const LAYOUT_KEY = 'kcc.auslastung.layout.v1';
const SNAP = 8, MIN_W = 260, MIN_H = 140, DEFAULT_W = 360;
const snap = v => Math.round(v / SNAP) * SNAP;

let layout = loadLayout();   // { <resourcePoint>: { x, y, w, h } } je Gruppen-Container
let arrange = false;

function loadLayout() {
  try { return JSON.parse(localStorage.getItem(LAYOUT_KEY)) || {}; }
  catch { return {}; }
}
function saveLayout() {
  try { localStorage.setItem(LAYOUT_KEY, JSON.stringify(layout)); } catch { /* privater Modus o. ä. */ }
}
const hasLayout = () => Object.keys(layout).length > 0;

// Wendet die gespeicherte Anordnung an bzw. stellt (ohne Anordnung) das Raster wieder her.
// Kacheln ohne gespeicherte Geometrie werden unter die gesetzten gepackt.
function layoutTiles() {
  const boxes = [...document.querySelectorAll('.grp > .tiles')];
  const custom = arrange || hasLayout();

  if (!custom) {
    document.body.classList.remove('custom-layout');
    for (const box of boxes) {
      box.style.height = '';
      for (const el of box.children) el.removeAttribute('style');
    }
    return;
  }

  // Höhe der noch nicht gesetzten Kacheln bei Zielbreite messen, solange sie im Fluss stehen.
  // (Die Container sind hier noch schmale Rasterspalten — deshalb Breite fest, nur Höhe messen.)
  const natural = new Map();
  for (const box of boxes) {
    for (const el of box.children) {
      const rp = el.dataset.rp;
      if (!rp || layout[rp]) continue;
      const prev = el.style.width;
      el.style.width = DEFAULT_W + 'px';
      natural.set(rp, { w: DEFAULT_W, h: Math.round(el.getBoundingClientRect().height) });
      el.style.width = prev;
    }
  }

  document.body.classList.add('custom-layout');   // ab hier: Gruppen als volle Bahn, Kacheln absolut

  for (const box of boxes) {
    const contW = box.clientWidth || 1080;
    let bottom = 0;
    for (const el of box.children) {
      const g = layout[el.dataset.rp];
      if (g) bottom = Math.max(bottom, g.y + g.h);
    }
    let cx = 0, cy = bottom, rowH = 0;
    for (const el of box.children) {
      const rp = el.dataset.rp;
      if (!rp) continue;
      let g = layout[rp];
      if (!g) {
        const nat = natural.get(rp) || { w: DEFAULT_W, h: 300 };
        const w = Math.min(nat.w, contW);
        if (cx > 0 && cx + w > contW) { cx = 0; cy += rowH + SNAP; rowH = 0; }
        g = { x: cx, y: cy, w, h: nat.h };
        cx += w + SNAP; rowH = Math.max(rowH, g.h);
      }
      el.style.left = g.x + 'px';
      el.style.top = g.y + 'px';
      el.style.width = g.w + 'px';
      el.style.height = g.h + 'px';
      bottom = Math.max(bottom, g.y + g.h);
    }
    box.style.height = bottom + 'px';
  }
}

// Ziehen (Kopf) bzw. Größe ändern (Griff), delegiert vom Kachel-Container.
$('tiles').addEventListener('pointerdown', e => {
  if (!arrange) return;
  const el = e.target.closest('.tile');
  if (!el || !el.dataset.rp) return;
  const box = el.closest('.grp > .tiles');
  if (!box) return;

  const resize = !!e.target.closest('.grip');
  const move = !resize && !!e.target.closest('.tile-head');
  if (!resize && !move) return;
  e.preventDefault();

  const rp = el.dataset.rp;
  const rect = el.getBoundingClientRect(), brect = box.getBoundingClientRect();
  const g0 = layout[rp] || {
    x: Math.round(rect.left - brect.left),
    y: Math.round(rect.top - brect.top),
    w: Math.round(rect.width),
    h: Math.round(rect.height),
  };
  const sx = e.clientX, sy = e.clientY;

  document.body.classList.add('dragging');
  el.classList.add('drag');

  const onMove = ev => {
    const dx = ev.clientX - sx, dy = ev.clientY - sy;
    const g = { ...g0 };
    if (resize) {
      g.w = Math.max(MIN_W, snap(g0.w + dx));
      g.h = Math.max(MIN_H, snap(g0.h + dy));
    } else {
      g.x = Math.max(0, snap(g0.x + dx));
      g.y = Math.max(0, snap(g0.y + dy));
    }
    g.x = Math.min(g.x, Math.max(0, box.clientWidth - g.w));
    layout[rp] = g;
    el.style.left = g.x + 'px';
    el.style.top = g.y + 'px';
    el.style.width = g.w + 'px';
    el.style.height = g.h + 'px';
    box.style.height = Math.max(parseFloat(box.style.height) || 0, g.y + g.h) + 'px';
  };
  const onUp = () => {
    document.removeEventListener('pointermove', onMove);
    document.removeEventListener('pointerup', onUp);
    document.body.classList.remove('dragging');
    el.classList.remove('drag');
    saveLayout();
    $('resetLayout').hidden = false;
    layoutTiles();
  };
  document.addEventListener('pointermove', onMove);
  document.addEventListener('pointerup', onUp);
});

$('arrange').addEventListener('click', () => {
  arrange = !arrange;
  document.body.classList.toggle('arrange', arrange);
  $('arrange').classList.toggle('on', arrange);
  if (arrange || hasLayout()) $('resetLayout').hidden = false;
  layoutTiles();
});

$('resetLayout').addEventListener('click', () => {
  layout = {};
  try { localStorage.removeItem(LAYOUT_KEY); } catch { /* egal */ }
  arrange = false;
  document.body.classList.remove('arrange');
  $('arrange').classList.remove('on');
  $('resetLayout').hidden = true;
  layoutTiles();
});

if (hasLayout()) $('resetLayout').hidden = false;

// Ein Hover-Handler für alle Sparklines: nächstliegender Stützpunkt, Fadenkreuz, Tooltip.
function nearest(svg, clientX) {
  const box = svg.getBoundingClientRect();
  const point = current?.points.find(p => p.resourcePoint === svg.dataset.point);
  const s = point && point.rbg ? point.rbg.series : point && point.series;
  if (!s || s.length < 2 || box.width === 0) return null;

  const ratio = Math.min(1, Math.max(0, (clientX - box.left) / box.width));
  const i = Math.round(ratio * (s.length - 1));
  return { point, i, bucket: s[i], rbg: !!point.rbg };
}

document.addEventListener('mousemove', e => {
  if (arrange) { $('tip').style.opacity = 0; return; }   // im Anordnen-Modus kein Hover
  const svg = e.target.closest?.('.spark');
  const hit = svg ? nearest(svg, e.clientX) : null;
  document.querySelectorAll('.spark .dot').forEach(d => d.setAttribute('opacity', '0'));
  document.querySelectorAll('.spark .cursor').forEach(c => c.style.visibility = 'hidden');

  if (!hit) { $('tip').style.opacity = 0; return; }

  const dot = svg.querySelector(`.dot[data-i="${hit.i}"]`);
  if (dot) dot.setAttribute('opacity', '1');
  const cursor = svg.querySelector('.cursor');
  if (cursor && dot) {
    cursor.setAttribute('x1', dot.getAttribute('cx'));
    cursor.setAttribute('x2', dot.getAttribute('cx'));
    cursor.style.visibility = 'visible';
  }

  const at = new Date(hit.bucket.at);
  const tip = $('tip');
  // Die Kurve einer RBG-Kachel zeigt die Spiele der Verbindung, nicht die TSPORD des
  // Ressourcenpunkts — der Kopf nennt deshalb die Verbindung als Quelle.
  const who = hit.rbg
    ? `<b>${hit.point.rbg.connection}</b> · ${hit.point.resourcePoint}`
    : `<b>${hit.point.resourcePoint}</b>`;
  // Der Kurvenwert ist der gleitende Kurzzeitwert über 'bucketMinutes' — nicht der
  // Fenstermittelwert, den Tacho und Kopfzeile zeigen. Das Fenster gehört dazugesagt.
  tip.innerHTML =
    `${who} · ${at.toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' })}` +
    (hit.rbg
      ? `<br>${fmt(hit.bucket.uph)} Spiele/h · ${hit.bucket.count} Fahrten`
      : `<br>${fmt(hit.bucket.uph)} UPH · ${hit.bucket.count} Telegramme`) +
    `<br><span class="win">gleitend ${current.bucketMinutes} min</span>`;
  tip.style.opacity = 1;
  tip.style.left = Math.min(window.innerWidth - 180, e.clientX + 12) + 'px';
  tip.style.top = (e.clientY + 14) + 'px';
});

async function load() {
  if (document.body.classList.contains('dragging')) return;   // während des Ziehens nicht neu bauen

  // Beim ersten Aufruf ohne Vorgabe: Fenster, Richtwert und Raster kommen vom Server
  // (appsettings.json), damit die Seite sofort die konfigurierte Historie zeigt.
  const query = new URLSearchParams();
  const minutes = parseInt($('minutes').value, 10);
  const target = parseFloat($('target').value);
  const bucket = parseInt($('bucket').value, 10);
  const rate = parseInt($('rate').value, 10);
  const rateFt = parseInt($('rateFt').value, 10);
  if (minutes > 0) query.set('minutes', Math.min(1440, minutes));
  if (target > 0) query.set('target', target);
  if (bucket > 0) query.set('bucket', Math.min(120, bucket));
  if (rate > 0) query.set('rate', Math.min(240, rate));
  if (rateFt > 0) query.set('rateFt', Math.min(240, rateFt));
  try {
    const res = await fetch(API_BASE + '/api/utilization?' + query, { cache: 'no-store' });
    if (!res.ok) throw new Error('HTTP ' + res.status);
    render(await res.json());
  } catch (e) {
    $('meta').classList.add('err');
    $('meta').textContent = 'Fehler: ' + e.message;
  }
}

for (const id of ['minutes', 'target', 'bucket', 'rate', 'rateFt']) $(id).addEventListener('change', load);
load();
// Häufiger abrufen: kleinere Schritte je Aktualisierung, die Überblendung macht daraus
// eine fortlaufende Bewegung statt eines Sprungs pro Minute.
setInterval(load, 20000);
