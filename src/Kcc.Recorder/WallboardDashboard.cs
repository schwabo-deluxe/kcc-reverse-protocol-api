namespace Kcc.Recorder;

/// <summary>
/// Wandansicht unter <c>/wand</c>: dieselben Daten wie <c>/auslastung</c> (<c>/api/utilization</c>),
/// als bildschirmfüllendes Dashboard ohne Scrollen. Je Gruppe eine Spalte, je Ressourcenpunkt eine
/// Kachel mit zwei Tachos (RBG: Auslastung + Leistung, Fördertechnik: Belegung + Leistung) und einer
/// kleinen Verlaufskurve. Spalten- und Zeilenzahl je Gruppe werden aus der Kachelzahl berechnet,
/// alle Größen skalieren mit dem Viewport. Im Hochformat stapeln die Spalten und die Seite scrollt.
/// </summary>
public static class WallboardDashboard
{
    public const string Html = """
        <!doctype html>
        <html lang="de">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Wandansicht Auslastung</title>
        <style>
          :root { color-scheme: dark; }
          * { box-sizing: border-box; }
          html, body { height: 100%; }
          body { margin: 0; font: 14px/1.35 system-ui, sans-serif; background: #14171c; color: #e6e6e6; overflow: hidden; }
          #app { display: flex; flex-direction: column; height: 100%; }
          header { padding: 6px 14px; border-bottom: 1px solid #2a2f37; display: flex; gap: 12px; align-items: center; flex: 0 0 auto; }
          header h1 { font-size: 14px; margin: 0; font-weight: 600; }
          header .meta { color: #9aa4b2; font-size: 12px; font-variant-numeric: tabular-nums; }
          header .meta.err { color: #ff6b6b; }
          header .sp { margin-left: auto; }
          header button { background: #1c2128; color: #e6e6e6; border: 1px solid #2a2f37; border-radius: 6px; padding: 4px 10px; font: inherit; cursor: pointer; }
          header button:hover { background: #242b34; }

          /* Je Gruppe eine horizontale Bahn, Bahnen untereinander gestapelt. */
          .wall { flex: 1 1 auto; min-height: 0; display: flex; flex-direction: column; gap: 8px; padding: 8px; }
          .panel { flex: 1 1 0; min-height: 0; display: flex; flex-direction: column;
                   background: #171b21; border: 1px solid #2a2f37; border-radius: 10px; overflow: hidden; }
          .p-head { flex: 0 0 auto; display: flex; align-items: baseline; gap: 10px;
                    padding: 7px 12px; border-bottom: 1px solid #2a2f37; background: #1b2029; }
          .p-head .p-name { font-size: clamp(13px, 1.3vh, 17px); font-weight: 700; text-transform: uppercase; letter-spacing: .05em; }
          .p-head .p-pct { font-size: clamp(13px, 1.3vh, 17px); font-weight: 700; font-variant-numeric: tabular-nums; }
          .p-head .p-sum { font-size: 12px; color: #9aa4b2; font-variant-numeric: tabular-nums; margin-left: auto; }
          .p-body { flex: 1 1 auto; min-height: 0; overflow: hidden; padding: 10px;
                    display: flex; gap: 10px; align-items: stretch; }
          .p-body > .tile { flex: 1 1 0; min-width: 0; }

          .tile { background: #1c2128; border: 1px solid #2a2f37; border-radius: 8px;
                  padding: 8px 12px; display: flex; flex-direction: column; gap: 4px; min-height: 0; overflow: hidden; }
          .t-name { font-size: clamp(11px, 1.25vh, 15px); font-weight: 700; text-transform: uppercase; letter-spacing: .04em;
                    color: #cdd6e0; flex: 0 0 auto; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
          .t-name .code { color: #7a8494; font-weight: 400; }

          /* Die Tachos (Wert im Bogen) füllen die freie Kachelhöhe, die Kurve ist klein und fix. */
          .duo { display: flex; gap: 8px; justify-content: center; flex: 1 1 auto; min-height: 0; }
          .gcell { flex: 1 1 0; min-width: 0; min-height: 0; display: flex; align-items: center; justify-content: center; }
          .gauge { height: 100%; width: auto; max-width: 100%; margin: 0 auto; overflow: visible; }
          .gauge .track { stroke: #2a2f37; }
          .gauge .tick { stroke: #cdd6e0; }

          .sub { flex: 0 0 auto; font-size: clamp(9px, 1.35vh, 12px); color: #9aa4b2; text-align: center;
                 font-variant-numeric: tabular-nums; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
          .sub b { color: #e6e6e6; font-weight: 600; }

          .spark { flex: 0 0 auto; display: block; width: 100%; height: clamp(22px, 5.5vh, 68px); overflow: visible; }
          /* preserveAspectRatio="none" streckt den Pfad ungleich — Strich sonst dick/verzerrt. */
          .spark .grid { stroke: #2a2f37; stroke-width: 1; vector-effect: non-scaling-stroke; }
          .spark .target { stroke: #7a8494; stroke-width: 1; stroke-dasharray: 3 3; vector-effect: non-scaling-stroke; }
          .spark .line { fill: none; stroke-width: 1.75; stroke-linejoin: round; stroke-linecap: round; vector-effect: non-scaling-stroke; }
          [title] { cursor: help; }

          @media (orientation: portrait) {
            body { overflow: auto; }
            #app { height: auto; min-height: 100%; }
            .panel { flex: 0 0 auto; }
            .p-body { flex-wrap: wrap; overflow: visible; }
            .p-body > .tile { flex: 1 1 240px; min-height: 220px; }
          }
        </style>
        </head>
        <body>
        <!--nav-->
        <!--rbghelp-->
        <div id="app">
          <header>
            <h1>Wandansicht</h1>
            <span class="meta" id="orient"></span>
            <span class="meta sp" id="meta">lädt …</span>
            <button id="fs" type="button" title="Vollbild">⛶</button>
          </header>
          <div class="wall" id="wall"></div>
        </div>
        <script>
        const $ = id => document.getElementById(id);
        const fmt = n => (n ?? 0).toLocaleString('de-DE', { maximumFractionDigits: 1 });
        const API_BASE = (new URLSearchParams(location.search).get('api')
          || (/^https?:$/.test(location.protocol) ? location.origin : 'http://localhost:8082'))
          .replace(/\/+$/, '');

        const H = window.RBG_HELP || {};
        const help = k => H[k] ? ` title="${String(H[k]).replace(/"/g, '&quot;')}"` : '';

        function color(pct) {
          if (pct >= 95) return '#ff6b6b';
          if (pct >= 80) return '#ffb454';
          return '#5ccb7e';
        }
        const dur = s => s < 60 ? `${Math.round(s)} s`
          : `${Math.floor(s / 60)}:${String(Math.round(s % 60)).padStart(2, '0')} min`;

        // Halbkreis-Tacho mit Wert und Kurzlabel IM Bogen — spart die separate Beschriftungszeile.
        function gauge(value, max, label) {
          const cx = 100, cy = 96, r = 82;
          const stroke = color(value);
          const f = Math.max(0, Math.min(value / max, 1));
          const pt = (frac, rad) => {
            const t = Math.PI * (1 - frac);
            return [cx + rad * Math.cos(t), cy - rad * Math.sin(t)];
          };
          const [x1, y1] = pt(0, r), [x2, y2] = pt(Math.max(0.0001, f), r);
          const [mx, my] = pt(f, r);
          const [t1x, t1y] = pt(Math.min(1, 100 / max), r + 8);
          const [t2x, t2y] = pt(Math.min(1, 100 / max), r - 8);
          const val = f > 0
            ? `<path d="M${x1.toFixed(1)} ${y1.toFixed(1)} A${r} ${r} 0 0 1 ${x2.toFixed(1)} ${y2.toFixed(1)}" fill="none" stroke="${stroke}" stroke-width="8" stroke-linecap="round"/>`
            : '';
          return `<svg class="gauge" viewBox="0 0 200 120" preserveAspectRatio="xMidYMid meet">
            <path class="track" d="M18 96 A82 82 0 0 1 182 96" fill="none" stroke-width="13" stroke-linecap="round"/>
            ${val}
            <line class="tick" x1="${t1x.toFixed(1)}" y1="${t1y.toFixed(1)}" x2="${t2x.toFixed(1)}" y2="${t2y.toFixed(1)}" stroke-width="2.5"/>
            <circle cx="${mx.toFixed(1)}" cy="${my.toFixed(1)}" r="7.5" fill="${stroke}" stroke="#14171c" stroke-width="2.5"/>
            <text x="100" y="86" text-anchor="middle" font-size="36" font-weight="800" fill="${stroke}"
                  style="font-variant-numeric:tabular-nums">${fmt(value)} %</text>
            <text x="100" y="112" text-anchor="middle" font-size="13" letter-spacing="1" fill="#9aa4b2">${label.toUpperCase()}</text>
          </svg>`;
        }

        const dial = (value, label, titleKey) =>
          `<div class="gcell"${help(titleKey)}>${gauge(value, 150, label)}</div>`;

        function spark(series, scaleMax, target, stroke) {
          const w = 240, h = 40, s = series || [];
          if (s.length < 2) return `<svg class="spark" viewBox="0 0 ${w} ${h}"></svg>`;
          const x = i => (i / (s.length - 1)) * w;
          const y = v => h - (Math.min(v, scaleMax) / scaleMax) * h;
          const line = s.map((b, i) => `${i ? 'L' : 'M'}${x(i).toFixed(1)},${y(b.uph).toFixed(1)}`).join(' ');
          const ty = target > 0 ? y(target) : -1;
          return `<svg class="spark" viewBox="0 0 ${w} ${h}" preserveAspectRatio="none">
            <line class="grid" x1="0" y1="${h}" x2="${w}" y2="${h}"></line>
            ${ty >= 0 ? `<line class="target" x1="0" y1="${ty.toFixed(1)}" x2="${w}" y2="${ty.toFixed(1)}"></line>` : ''}
            <path class="line" d="${line}" stroke="${stroke}"></path>
          </svg>`;
        }

        function tile(p, data) {
          const r = p.rbg, cv = p.conveyor;
          const name = (p.label && p.label !== p.resourcePoint)
            ? `${p.label} <span class="code">${p.resourcePoint}</span>` : p.resourcePoint;
          const srmH = Math.max(1e-9, data.rateMinutes / 60);
          const ftH = Math.max(1e-9, (data.conveyorRateMinutes || data.rateMinutes) / 60);
          const rh = n => fmt(n / srmH), fh = n => fmt(n / ftH);

          let gauges, sub, series, sMax, sTarget, stroke;
          if (r) {
            gauges = dial(r.busyPercent, 'Auslastung', 'busy') + dial(r.percent, 'Leistung', 'load');
            sub = `DS/ES <b>${rh(r.doubleCycles)}</b>/<b>${rh(r.singleCycles)}</b>·h · Ein/Aus <b>${rh(r.stores)}</b>/<b>${rh(r.retrievals)}</b> · Leerlauf <b>${dur(r.idleSeconds)}</b>`;
            series = r.series;
            sMax = Math.max(r.maxCyclesPerHour * 1.15, 1, ...r.series.map(b => b.uph * 1.1));
            sTarget = r.maxCyclesPerHour;
            stroke = color(r.percent);
          } else if (cv) {
            gauges = dial(cv.busyPercent, 'Belegung', 'cbusy') + dial(p.percent, 'Leistung', 'load');
            sub = `Ø belegt <b>${dur(cv.avgOccupiedSeconds)}</b> · Ø leer <b>${dur(cv.avgIdleSeconds)}</b> · <b>${fh(cv.orders)}</b>/h`;
            series = p.series;
            sMax = Math.max(p.targetUph, 1, ...p.series.map(b => b.uph));
            sTarget = p.targetUph;
            stroke = color(p.percent);
          } else {
            gauges = dial(p.percent, 'Auslastung', null);
            sub = `<b>${fmt(p.uph)}</b> / ${fmt(p.targetUph)} UPH`;
            series = p.series;
            sMax = Math.max(p.targetUph, 1, ...p.series.map(b => b.uph));
            sTarget = p.targetUph;
            stroke = color(p.percent);
          }
          return `<div class="tile">
            <div class="t-name">${name}</div>
            <div class="duo">${gauges}</div>
            <div class="sub">${sub}</div>
            ${spark(series, sMax, sTarget, stroke)}
          </div>`;
        }

        function render(data) {
          const byName = n => data.points.find(p => p.resourcePoint === n);

          $('wall').innerHTML = data.groups.map(g => {
            const pts = g.points.map(byName).filter(Boolean);
            return `<section class="panel">
              <div class="p-head">
                <span class="p-name">${g.name}</span>
                <span class="p-pct" style="color:${color(g.percent)}">Ø ${fmt(g.percent)} %</span>
                <span class="p-sum">${fmt(g.uph)} / ${fmt(g.targetUph)} UPH · ${g.count} ges.${g.errors ? ` · ${g.errors} Fehler` : ''}</span>
              </div>
              <div class="p-body">${pts.map(p => tile(p, data)).join('')}</div>
            </section>`;
          }).join('');

          $('meta').classList.remove('err');
          $('meta').textContent =
            `${data.totalOrders} TSPORD / ${data.windowMinutes} min · ${new Date().toLocaleTimeString('de-DE')}`;
        }

        function showOrient() {
          const land = window.matchMedia('(orientation: landscape)').matches;
          $('orient').textContent = land ? '' : 'Hochformat — Bildschirm drehen';
        }

        async function load() {
          try {
            const res = await fetch(API_BASE + '/api/utilization', { cache: 'no-store' });
            if (!res.ok) throw new Error('HTTP ' + res.status);
            render(await res.json());
          } catch (e) {
            $('meta').classList.add('err');
            $('meta').textContent = 'Fehler: ' + e.message;
          }
        }

        $('fs').addEventListener('click', () => {
          if (document.fullscreenElement) document.exitFullscreen();
          else document.documentElement.requestFullscreen?.();
        });
        window.matchMedia('(orientation: landscape)').addEventListener('change', showOrient);
        showOrient();
        load();
        setInterval(load, 60000);
        </script>
        </body>
        </html>
        """;
}
