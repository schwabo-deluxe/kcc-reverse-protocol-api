namespace Kcc.Recorder;

/// <summary>
/// Wandansicht unter <c>/wand</c>: zieht dieselben Daten wie <c>/auslastung</c>
/// (<c>/api/utilization</c>), erkennt per <c>orientation: landscape</c> das Querformat und legt
/// dann jede Gruppe (RBG, Fördertechnik …) als eigene, klar getrennte Spalte formatfüllend ab.
/// Im Hochformat stapeln sich die Spalten und die Seite darf scrollen.
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
          body { margin: 0; font: 14px/1.4 system-ui, sans-serif; background: #14171c; color: #e6e6e6; }
          #app { display: flex; flex-direction: column; height: 100%; }
          header { padding: 8px 16px; border-bottom: 1px solid #2a2f37; display: flex; gap: 14px; align-items: center; flex-wrap: wrap; flex: 0 0 auto; }
          header h1 { font-size: 15px; margin: 0; font-weight: 600; }
          header .meta { margin-left: auto; color: #9aa4b2; font-size: 12px; }
          header .meta.err { color: #ff6b6b; }
          header button { background: #1c2128; color: #e6e6e6; border: 1px solid #2a2f37; border-radius: 6px; padding: 5px 10px; font: inherit; cursor: pointer; }
          header button:hover { background: #242b34; }

          /* Querformat: Spalten nebeneinander, alles ohne Seiten-Scroll. */
          .wall { flex: 1 1 auto; min-height: 0; display: flex; gap: 10px; padding: 10px; }
          .panel { flex: 1 1 0; min-width: 0; display: flex; flex-direction: column;
                   background: #171b21; border: 1px solid #2a2f37; border-radius: 10px; overflow: hidden; }
          .panel > .p-head { flex: 0 0 auto; display: flex; align-items: baseline; gap: 10px; flex-wrap: wrap;
                             padding: 10px 14px; border-bottom: 1px solid #2a2f37; background: #1b2029; }
          .panel > .p-head .p-name { font-size: 15px; font-weight: 700; text-transform: uppercase; letter-spacing: .05em; }
          .panel > .p-head .p-sum { font-size: 12px; color: #9aa4b2; font-variant-numeric: tabular-nums; }
          .panel > .p-head .p-pct { font-size: 15px; font-weight: 700; font-variant-numeric: tabular-nums; }
          .panel > .p-body { flex: 1 1 auto; min-height: 0; overflow: auto; padding: 10px;
                             display: grid; align-content: start; gap: 10px;
                             grid-template-columns: repeat(auto-fill, minmax(260px, 1fr)); }

          .tile { background: #1c2128; border: 1px solid #2a2f37; border-radius: 8px; padding: 10px 12px; }
          .tile .t-head { display: flex; justify-content: space-between; align-items: baseline; gap: 8px; }
          .tile .t-name { font-size: 12px; text-transform: uppercase; letter-spacing: .04em; color: #cdd6e0; font-weight: 600; }
          .tile .t-name .code { color: #7a8494; font-weight: 400; }
          .tile .t-sub { color: #9aa4b2; font-size: 11px; font-variant-numeric: tabular-nums; }
          .t-body { display: flex; gap: 10px; align-items: center; margin-top: 6px; }
          .gauge-col { flex: 0 0 auto; text-align: center; }
          .gauge { display: block; width: 96px; height: 50px; overflow: visible; }
          .gauge .track { stroke: #2a2f37; }
          .gauge .tick { stroke: #cdd6e0; }
          .pct { font-weight: 800; font-size: 16px; line-height: 1; margin-top: 2px; }
          .spark-col { flex: 1 1 0; min-width: 0; }
          .spark { display: block; width: 100%; height: 48px; overflow: visible; }
          .spark .grid { stroke: #2a2f37; stroke-width: 1; }
          .spark .target { stroke: #7a8494; stroke-width: 1; stroke-dasharray: 3 3; }
          .spark .line { fill: none; stroke-width: 2; stroke-linejoin: round; stroke-linecap: round; }
          .rbg { margin-top: 6px; padding-top: 6px; border-top: 1px solid #232830; font-size: 11px;
                 color: #9aa4b2; display: flex; flex-wrap: wrap; gap: 2px 10px; font-variant-numeric: tabular-nums; }
          .rbg b { color: #e6e6e6; font-weight: 600; }
          [title] { cursor: help; }

          /* Hochformat: Spalten untereinander, Seite scrollt. */
          @media (orientation: portrait) {
            #app { height: auto; min-height: 100%; }
            .wall { flex-direction: column; }
            .panel { flex: 0 0 auto; }
            .panel > .p-body { overflow: visible; }
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
            <span class="meta" id="meta">lädt …</span>
            <button id="fs" type="button" title="Vollbild">⛶ Vollbild</button>
          </header>
          <div class="wall" id="wall"></div>
        </div>
        <script>
        const $ = id => document.getElementById(id);
        const fmt = n => (n ?? 0).toLocaleString('de-DE', { maximumFractionDigits: 1 });
        const API_BASE = (new URLSearchParams(location.search).get('api')
          || (/^https?:$/.test(location.protocol) ? location.origin : 'http://localhost:8082'))
          .replace(/\/+$/, '');

        // Erklärtexte der RBG-Kennzahlen (serverseitig eingesetzt, siehe RbgGlossary).
        const H = window.RBG_HELP || {};
        const help = k => H[k] ? ` title="${String(H[k]).replace(/"/g, '&quot;')}"` : '';

        function color(pct) {
          if (pct >= 95) return '#ff6b6b';
          if (pct >= 80) return '#ffb454';
          return '#5ccb7e';
        }

        function gauge(value, max, stroke) {
          const cx = 100, cy = 92, r = 80;
          const f = Math.max(0, Math.min(value / max, 1));
          const pt = (frac, rad) => {
            const t = Math.PI * (1 - frac);
            return [cx + rad * Math.cos(t), cy - rad * Math.sin(t)];
          };
          const [x1, y1] = pt(0, r), [x2, y2] = pt(f, r);
          const [mx, my] = pt(f, r);
          const [t1x, t1y] = pt(Math.min(1, 100 / max), r + 8);
          const [t2x, t2y] = pt(Math.min(1, 100 / max), r - 8);
          const track = `<path class="track" d="M20 92 A80 80 0 0 1 180 92" fill="none" stroke-width="13" stroke-linecap="round"/>`;
          const val = f > 0
            ? `<path d="M${x1.toFixed(1)} ${y1.toFixed(1)} A${r} ${r} 0 0 1 ${x2.toFixed(1)} ${y2.toFixed(1)}" fill="none" stroke="${stroke}" stroke-width="6" stroke-linecap="round"/>`
            : '';
          return `<svg class="gauge" viewBox="0 0 200 104">
            ${track}${val}
            <line class="tick" x1="${t1x.toFixed(1)}" y1="${t1y.toFixed(1)}" x2="${t2x.toFixed(1)}" y2="${t2y.toFixed(1)}" stroke-width="2.5"/>
            <circle cx="${mx.toFixed(1)}" cy="${my.toFixed(1)}" r="7" fill="${stroke}" stroke="#14171c" stroke-width="2.5"/>
          </svg>`;
        }

        function spark(point, scaleMax, target) {
          const w = 240, h = 48, s = point.rbg ? point.rbg.series : point.series;
          if (!s || s.length < 2) return '<svg class="spark" viewBox="0 0 240 48"></svg>';
          const x = i => (i / (s.length - 1)) * w;
          const y = v => h - (Math.min(v, scaleMax) / scaleMax) * h;
          const line = s.map((b, i) => `${i ? 'L' : 'M'}${x(i).toFixed(1)},${y(b.uph).toFixed(1)}`).join(' ');
          const stroke = color(point.percent);
          const targetY = y(target);
          return `<svg class="spark" viewBox="0 0 ${w} ${h}" preserveAspectRatio="none">
            <line class="grid" x1="0" y1="${h}" x2="${w}" y2="${h}"></line>
            ${targetY >= 0 ? `<line class="target" x1="0" y1="${targetY.toFixed(1)}" x2="${w}" y2="${targetY.toFixed(1)}"></line>` : ''}
            <path class="line" d="${line}" stroke="${stroke}"></path>
          </svg>`;
        }

        const dur = s => s < 60 ? `${Math.round(s)} s`
          : `${Math.floor(s / 60)}:${String(Math.round(s % 60)).padStart(2, '0')} min`;

        function tile(p, data, peak) {
          const heading = (p.label && p.label !== p.resourcePoint)
            ? `${p.label} <span class="code">${p.resourcePoint}</span>`
            : p.resourcePoint;
          const sMax = p.rbg
            ? Math.max(p.rbg.maxCyclesPerHour * 1.3, 1, ...p.rbg.series.map(b => b.uph))
            : peak;
          const sTarget = p.rbg ? p.rbg.maxCyclesPerHour : data.targetUph;
          const sub = p.rbg
            ? `${fmt(p.rbg.cyclesPerHour)} / ${p.rbg.maxCyclesPerHour} Spiele/h · ${p.count} TSPORD`
            : `${fmt(p.uph)} / ${fmt(p.targetUph)} UPH · ${p.count} ges.`;
          const r = p.rbg;
          const rateHours = Math.max(1e-9, data.rateMinutes / 60);
          const perH = n => fmt(n / rateHours);
          const rbgRow = r ? `<div class="rbg">
              <span${help('busy')}>Ausl <b style="color:${color(r.busyPercent)}">${fmt(r.busyPercent)} %</b></span>
              <span${help('load')}>Leist <b style="color:${color(r.percent)}">${fmt(r.percent)} %</b></span>
              <span${help('double')}>DS <b>${perH(r.doubleCycles)}/h</b></span>
              <span${help('single')}>ES <b>${perH(r.singleCycles)}/h</b></span>
              <span${help('inout')}>Ein/Aus <b>${perH(r.stores)}/${perH(r.retrievals)}</b> /h</span>
              <span${help('idle')}>Leerlauf <b>${dur(r.idleSeconds)}</b></span>
            </div>` : '';
          return `<div class="tile">
            <div class="t-head">
              <span class="t-name">${heading}</span>
              <span class="t-sub">${sub}</span>
            </div>
            <div class="t-body">
              <div class="gauge-col">
                ${gauge(p.percent, 150, color(p.percent))}
                <div class="pct" style="color:${color(p.percent)}">${fmt(p.percent)} %</div>
              </div>
              <div class="spark-col">${spark(p, sMax, sTarget)}</div>
            </div>
            ${rbgRow}
          </div>`;
        }

        function render(data) {
          const peak = Math.max(
            data.targetUph,
            ...data.points.filter(p => !p.rbg).flatMap(p => p.series.map(b => b.uph)));
          const byName = n => data.points.find(p => p.resourcePoint === n);

          $('wall').innerHTML = data.groups.map(g => `
            <section class="panel">
              <div class="p-head">
                <span class="p-name">${g.name}</span>
                <span class="p-pct" style="color:${color(g.percent)}">Ø ${fmt(g.percent)} %</span>
                <span class="p-sum">${fmt(g.uph)} / ${fmt(g.targetUph)} UPH · ${g.count} ges.${g.errors ? ` · ${g.errors} Fehler` : ''}</span>
              </div>
              <div class="p-body">
                ${g.points.map(n => byName(n)).filter(Boolean).map(p => tile(p, data, peak)).join('')}
              </div>
            </section>`).join('');

          $('meta').classList.remove('err');
          $('meta').textContent =
            `${data.totalOrders} TSPORD / ${data.windowMinutes} min · Stand ${new Date().toLocaleTimeString('de-DE')}`;
        }

        function showOrient() {
          const land = window.matchMedia('(orientation: landscape)').matches;
          $('orient').textContent = land ? 'Querformat' : 'Hochformat — für die Wandansicht Bildschirm drehen';
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
