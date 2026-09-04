namespace Kcc.Recorder;

/// <summary>
/// Eingebettete Auslastungsansicht unter <c>/auslastung</c>: pollt <c>/api/utilization</c> und
/// zeigt je Ressourcenpunkt UPH und den Anteil am Richtwert.
/// </summary>
public static class UtilizationDashboard
{
    public const string Html = """
        <!doctype html>
        <html lang="de">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Auslastung der Ressourcenpunkte</title>
        <style>
          :root { color-scheme: dark; }
          * { box-sizing: border-box; }
          body { margin: 0; font: 14px/1.45 system-ui, sans-serif; background: #14171c; color: #e6e6e6; }
          header { padding: 16px 20px; border-bottom: 1px solid #2a2f37; display: flex; gap: 14px; align-items: center; flex-wrap: wrap; }
          header h1 { font-size: 16px; margin: 0 8px 0 0; font-weight: 600; }
          label { color: #9aa4b2; font-size: 12px; display: flex; gap: 6px; align-items: center; }
          input { background: #10141a; color: #e6e6e6; border: 1px solid #2a2f37; border-radius: 6px; padding: 5px 7px; font: inherit; width: 84px; }
          .meta { margin-left: auto; color: #9aa4b2; font-size: 12px; }
          .meta.err { color: #ff6b6b; }
          main { padding: 20px; max-width: 1100px; margin: 0 auto; }
          .tiles { display: grid; grid-template-columns: repeat(auto-fit, minmax(320px, 1fr)); gap: 12px; }
          .tile { background: #1c2128; border: 1px solid #2a2f37; border-radius: 8px; padding: 14px 16px; }
          .tile .label { color: #9aa4b2; font-size: 12px; text-transform: uppercase; letter-spacing: .04em; }
          .tile .label .code { color: #7a8494; font-weight: 400; }
          .tile .sub { color: #9aa4b2; font-size: 12px; }
          .tile-head { display: flex; justify-content: space-between; align-items: baseline; gap: 8px; }
          /* Tacho links, Verlauf rechts, auf gleicher Höhe. */
          .tile-body { display: flex; gap: 12px; align-items: center; margin-top: 8px; }
          .gauge-col { flex: 0 0 auto; text-align: center; }
          .spark-col { flex: 1 1 0; min-width: 0; }
          .gauge { display: block; width: 128px; height: 66px; overflow: visible; }
          .gauge .track { stroke: #2a2f37; }
          .gauge .tick { stroke: #cdd6e0; }
          .gauge.sm { width: 80px; height: 42px; }
          .pct { font-weight: 700; font-size: 17px; line-height: 1; margin-top: 2px; }
          .spark { display: block; width: 100%; height: 66px; overflow: visible; }
          .spark .grid { stroke: #2a2f37; stroke-width: 1; }
          .spark .target { stroke: #7a8494; stroke-width: 1; stroke-dasharray: 3 3; }
          .spark .line { fill: none; stroke-width: 2; stroke-linejoin: round; stroke-linecap: round; }
          .spark .dot { r: 2.5; }
          .spark .hit { fill: transparent; }
          .spark .cursor { stroke: #7a8494; stroke-width: 1; visibility: hidden; }
          .axis { display: flex; justify-content: space-between; color: #7a8494; font-size: 11px; margin-top: 4px; }
          table.dest { margin: 10px 0 0; border: 0; border-radius: 0; background: none; }
          table.dest th, table.dest td { padding: 3px 8px; font-size: 12px; border-bottom: 1px solid #232830; }
          table.dest tbody tr:last-child td { border-bottom: 0; }
          .grp { margin-bottom: 24px; }
          .grp-h { display: flex; justify-content: space-between; align-items: center; gap: 12px; flex-wrap: wrap; margin: 0 2px 12px; }
          .grp-name { font-size: 13px; font-weight: 600; color: #cdd6e0; text-transform: uppercase; letter-spacing: .04em; }
          .grp-sum { font-size: 12px; font-variant-numeric: tabular-nums; }
          tr.grp td { background: #171b21; font-weight: 600; }
          td.sub-row { padding-left: 24px; color: #cdd6e0; }
          #tip { position: fixed; pointer-events: none; opacity: 0; transition: opacity .08s; background: #10141a; border: 1px solid #2a2f37; border-radius: 6px; padding: 6px 8px; font-size: 12px; white-space: nowrap; z-index: 10; }
          #tip b { font-weight: 600; }
          table { width: 100%; border-collapse: collapse; margin-top: 22px; background: #1c2128; border: 1px solid #2a2f37; border-radius: 8px; overflow: hidden; }
          th, td { padding: 8px 12px; text-align: right; border-bottom: 1px solid #2a2f37; font-variant-numeric: tabular-nums; }
          th:first-child, td:first-child { text-align: left; }
          th { color: #9aa4b2; font-size: 12px; text-transform: uppercase; letter-spacing: .04em; font-weight: 600; }
          tbody tr:last-child td { border-bottom: 0; }
          td.err { color: #ff6b6b; }
        </style>
        </head>
        <body>
        <header>
          <h1>Auslastung (TSPORD)</h1>
          <label>Fenster (min) <input type="number" id="minutes" min="1" max="1440"></label>
          <label>Richtwert (UPH) <input type="number" id="target" value="200" min="1" title="Vorgabe für Punkte ohne eigenen Richtwert (TargetUph in appsettings.json)"></label>
          <label>Glättung (min) <input type="number" id="bucket" value="5" min="1" max="120" title="Breite des gleitenden Fensters der Verlaufskurve"></label>
          <label>UPH aus (min) <input type="number" id="rate" value="15" min="1" max="240"></label>
          <div class="meta" id="meta">lädt …</div>
        </header>
        <main>
          <div class="tiles" id="tiles"></div>
          <div id="tip" role="status"></div>
          <table>
            <thead><tr>
              <th>Ressourcenpunkt</th><th>TSPORD</th><th>UPH</th>
              <th>% vom Richtwert</th><th>Fehler</th><th>Letztes Telegramm</th>
            </tr></thead>
            <tbody id="rows"></tbody>
          </table>
        </main>
        <script>
        const $ = id => document.getElementById(id);
        const fmt = n => n.toLocaleString('de-DE', { maximumFractionDigits: 1 });

        // Wird die Seite über die API selbst ausgeliefert (http/https), zählt die eigene Herkunft.
        // Als lose Datei (file://) sonst nichts erreichbar — dann fest auf den lokalen Standard.
        // Mit "?api=http://host:port" überschreibbar.
        const API_BASE = (new URLSearchParams(location.search).get('api')
          || (/^https?:$/.test(location.protocol) ? location.origin : 'http://localhost:8080'))
          .replace(/\/+$/, '');

        function color(pct) {
          if (pct >= 95) return '#ff6b6b';
          if (pct >= 80) return '#ffb454';
          return '#5ccb7e';
        }

        // Halbkreis-Tacho (nur Bogen + Zeiger, Zahl steht daneben): farbiger Wertbogen,
        // Markierung bei 100 % vom Richtwert.
        function gauge(value, max, stroke, cls) {
          const cx = 100, cy = 92, r = 80;
          const f = Math.max(0, Math.min(value / max, 1));
          const pt = (frac, rad) => {
            const t = Math.PI * (1 - frac);
            return [cx + rad * Math.cos(t), cy - rad * Math.sin(t)];
          };
          const arc = (frac, cssClass, w, extra) => {
            if (frac <= 0) return '';
            const [x1, y1] = pt(0, r), [x2, y2] = pt(frac, r);
            return `<path class="${cssClass}" d="M${x1.toFixed(1)} ${y1.toFixed(1)} ` +
              `A${r} ${r} 0 0 1 ${x2.toFixed(1)} ${y2.toFixed(1)}" fill="none" ` +
              `stroke-width="${w}" stroke-linecap="round" ${extra || ''}/>`;
          };
          const [mx, my] = pt(f, r);
          const [t1x, t1y] = pt(Math.min(1, 100 / max), r + 8);
          const [t2x, t2y] = pt(Math.min(1, 100 / max), r - 8);
          return `<svg class="gauge ${cls || ''}" viewBox="0 0 200 104">
            ${arc(1, 'track', 13)}
            ${arc(f, 'val', 6, `stroke="${stroke}"`)}
            <line class="tick" x1="${t1x.toFixed(1)}" y1="${t1y.toFixed(1)}" x2="${t2x.toFixed(1)}" y2="${t2y.toFixed(1)}" stroke-width="2.5"/>
            <circle cx="${mx.toFixed(1)}" cy="${my.toFixed(1)}" r="7" fill="${stroke}" stroke="#14171c" stroke-width="2.5"/>
          </svg>`;
        }

        // Verlauf als Sparkline: gemeinsame Y-Skala über alle Punkte, damit die Kacheln
        // untereinander vergleichbar bleiben. Gestrichelt: der Richtwert.
        function spark(point, scaleMax, target) {
          const w = 240, h = 54, s = point.series;
          if (s.length < 2) return '<svg class="spark" viewBox="0 0 240 54"></svg>';

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

          // Eine Skala für alle Kacheln — mindestens bis zum Richtwert.
          const peak = Math.max(
            data.targetUph,
            ...data.points.flatMap(p => p.series.map(b => b.uph)));

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

          // Tacho links, Verlauf rechts auf gleicher Höhe.
          const tile = p => `
            <div class="tile">
              <div class="tile-head">
                <span class="label">${heading(p)}</span>
                <span class="sub">${fmt(p.uph)} / ${fmt(p.targetUph)} UPH · ${p.rateCount}/${data.rateMinutes}m · ${p.count} ges.</span>
              </div>
              <div class="tile-body">
                <div class="gauge-col">
                  ${gauge(p.percent, 150, color(p.percent))}
                  <div class="pct" style="color:${color(p.percent)}">${fmt(p.percent)} %</div>
                </div>
                <div class="spark-col">
                  ${spark(p, peak, data.targetUph)}
                  <div class="axis"><span>vor ${data.windowMinutes} min</span><span>jetzt</span></div>
                </div>
              </div>
              ${destTable(p)}
            </div>`;

          // Kacheln nach Gruppe gebündelt.
          $('tiles').innerHTML = data.groups.map(g => `
            <section class="grp">
              <div class="grp-h">
                <span class="grp-name">${g.name}</span>
                <span class="grp-sum" style="color:${color(g.percent)}">
                  Ø ${fmt(g.percent)} % · ${fmt(g.uph)} / ${fmt(g.targetUph)} UPH · ${g.rateCount}/${data.rateMinutes}m · ${g.count} ges.</span>
                ${gauge(g.percent, 150, color(g.percent), 'sm')}
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

          $('meta').classList.remove('err');
          $('meta').textContent =
            `${data.totalOrders} TSPORD in ${data.windowMinutes} min · Verlauf gleitend ${data.bucketMinutes} min · UPH aus ${data.rateMinutes} min` +
            ` · Stand ${new Date().toLocaleTimeString('de-DE')}`;
        }

        // Ein Hover-Handler für alle Sparklines: nächstliegender Stützpunkt, Fadenkreuz, Tooltip.
        function nearest(svg, clientX) {
          const box = svg.getBoundingClientRect();
          const point = current?.points.find(p => p.resourcePoint === svg.dataset.point);
          if (!point || point.series.length < 2 || box.width === 0) return null;

          const ratio = Math.min(1, Math.max(0, (clientX - box.left) / box.width));
          const i = Math.round(ratio * (point.series.length - 1));
          return { point, i, bucket: point.series[i] };
        }

        document.addEventListener('mousemove', e => {
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
          tip.innerHTML =
            `<b>${hit.point.resourcePoint}</b> · ${at.toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' })}` +
            `<br>${fmt(hit.bucket.uph)} UPH · ${hit.bucket.count} Telegramme`;
          tip.style.opacity = 1;
          tip.style.left = Math.min(window.innerWidth - 180, e.clientX + 12) + 'px';
          tip.style.top = (e.clientY + 14) + 'px';
        });

        async function load() {
          // Beim ersten Aufruf ohne Vorgabe: Fenster, Richtwert und Raster kommen vom Server
          // (appsettings.json), damit die Seite sofort die konfigurierte Historie zeigt.
          const query = new URLSearchParams();
          const minutes = parseInt($('minutes').value, 10);
          const target = parseFloat($('target').value);
          const bucket = parseInt($('bucket').value, 10);
          const rate = parseInt($('rate').value, 10);
          if (minutes > 0) query.set('minutes', Math.min(1440, minutes));
          if (target > 0) query.set('target', target);
          if (bucket > 0) query.set('bucket', Math.min(120, bucket));
          if (rate > 0) query.set('rate', Math.min(240, rate));
          try {
            const res = await fetch(API_BASE + '/api/utilization?' + query, { cache: 'no-store' });
            if (!res.ok) throw new Error('HTTP ' + res.status);
            render(await res.json());
          } catch (e) {
            $('meta').classList.add('err');
            $('meta').textContent = 'Fehler: ' + e.message;
          }
        }

        for (const id of ['minutes', 'target', 'bucket', 'rate']) $(id).addEventListener('change', load);
        load();
        setInterval(load, 60000);
        </script>
        </body>
        </html>
        """;
}
