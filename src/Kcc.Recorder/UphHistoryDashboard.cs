namespace Kcc.Recorder;

/// <summary>
/// Eingebettete Historienansicht unter <c>/verlauf</c>: pollt <c>/api/uph-history</c> und zeigt
/// den UPH-Verlauf je Endziel als gestapelte Fläche, dazu das Mengenverhältnis der Ziele.
/// </summary>
public static class UphHistoryDashboard
{
    public const string Html = """
        <!doctype html>
        <html lang="de">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>UPH-Historie je Endziel</title>
        <style>
          :root { color-scheme: dark; }
          * { box-sizing: border-box; }
          body { margin: 0; font: 14px/1.45 system-ui, sans-serif; background: #14171c; color: #e6e6e6; }
          header { padding: 16px 20px; border-bottom: 1px solid #2a2f37; display: flex; gap: 14px; align-items: center; flex-wrap: wrap; }
          header h1 { font-size: 16px; margin: 0 8px 0 0; font-weight: 600; }
          label { color: #9aa4b2; font-size: 12px; display: flex; gap: 6px; align-items: center; }
          select { background: #10141a; color: #e6e6e6; border: 1px solid #2a2f37; border-radius: 6px; padding: 5px 7px; font: inherit; }
          .ranges { display: flex; gap: 4px; }
          .ranges button { background: #10141a; color: #9aa4b2; border: 1px solid #2a2f37; border-radius: 6px; padding: 5px 10px; font: inherit; cursor: pointer; }
          .ranges button.on { background: #1f6feb; border-color: #1f6feb; color: #fff; }
          .meta { margin-left: auto; color: #9aa4b2; font-size: 12px; }
          .meta.err { color: #ff6b6b; }
          main { padding: 20px; max-width: 1180px; margin: 0 auto; }
          .card { background: #1c2128; border: 1px solid #2a2f37; border-radius: 8px; padding: 16px; margin-bottom: 18px; }
          .card h2 { font-size: 13px; margin: 0 0 12px; font-weight: 600; color: #cdd6e0; text-transform: uppercase; letter-spacing: .04em; }
          .area { display: block; width: 100%; height: 300px; overflow: visible; cursor: crosshair; }
          .area .axis { stroke: #2a2f37; stroke-width: 1; }
          .area .glabel, .area .xlabel { fill: #7a8494; font-size: 11px; }
          .area .cursor { stroke: #7a8494; stroke-width: 1; visibility: hidden; }
          .area .sel { fill: #1f6feb; fill-opacity: 0.18; stroke: #1f6feb; stroke-opacity: 0.6; stroke-width: 1; pointer-events: none; }
          .area .cursor { pointer-events: none; }
          .zoomout { background: #10141a; color: #9aa4b2; border: 1px solid #2a2f37; border-radius: 6px; padding: 5px 10px; font: inherit; cursor: pointer; }
          .zoomout[hidden] { display: none; }
          .hint { color: #7a8494; font-size: 11px; margin: -4px 0 10px; }
          .ratio { display: flex; width: 100%; height: 26px; border-radius: 6px; overflow: hidden; border: 1px solid #2a2f37; }
          .ratio span { display: block; height: 100%; }
          .legend { display: flex; flex-wrap: wrap; gap: 10px 18px; margin-top: 12px; }
          .legend div { display: flex; gap: 6px; align-items: center; font-size: 12px; color: #cdd6e0; }
          .legend i { width: 10px; height: 10px; border-radius: 2px; flex: 0 0 auto; }
          table { width: 100%; border-collapse: collapse; }
          th, td { padding: 8px 12px; text-align: right; border-bottom: 1px solid #2a2f37; font-variant-numeric: tabular-nums; }
          th:first-child, td:first-child { text-align: left; }
          th { color: #9aa4b2; font-size: 12px; text-transform: uppercase; letter-spacing: .04em; font-weight: 600; }
          tbody tr:last-child td { border-bottom: 0; }
          td .sw { display: inline-block; width: 9px; height: 9px; border-radius: 2px; margin-right: 7px; }
          #tip { position: fixed; pointer-events: none; opacity: 0; transition: opacity .08s; background: #10141a; border: 1px solid #2a2f37; border-radius: 6px; padding: 7px 9px; font-size: 12px; z-index: 10; max-width: 260px; }
          #tip b { font-weight: 600; }
          #tip div { display: flex; justify-content: space-between; gap: 12px; }
        </style>
        </head>
        <body>
        <header>
          <h1>UPH-Historie</h1>
          <div class="ranges" id="ranges">
            <button data-h="24">24 h</button>
            <button data-h="168" class="on">7 T</button>
            <button data-h="336">14 T</button>
            <button data-h="672">4 W</button>
          </div>
          <label>Stapeln nach
            <select id="dim">
              <option value="destination">Endziel</option>
              <option value="resourcePoint">Ressourcenpunkt</option>
            </select>
          </label>
          <label>Ressourcenpunkt <select id="rp"><option value="">alle</option></select></label>
          <button class="zoomout" id="zoomout" hidden>⤺ Zoom zurück</button>
          <div class="meta" id="meta">lädt …</div>
        </header>
        <main>
          <div class="card">
            <h2 id="h-area">UPH je Endziel (gestapelt)</h2>
            <div class="hint">Zeitbereich mit gedrückter Maustaste aufziehen zum Zoomen · Doppelklick setzt zurück</div>
            <svg class="area" id="area" preserveAspectRatio="none"></svg>
          </div>
          <div class="card">
            <h2 id="h-ratio">Mengenverhältnis der Ziele</h2>
            <div class="ratio" id="ratio"></div>
            <div class="legend" id="legend"></div>
          </div>
          <div class="card">
            <h2 id="h-table">Ziele im Zeitraum</h2>
            <table>
              <thead><tr><th id="h-key">Ziel</th><th>Ø UPH</th><th>Aufträge</th><th>Anteil</th></tr></thead>
              <tbody id="rows"></tbody>
            </table>
          </div>
        </main>
        <div id="tip" role="status"></div>
        <script>
        const $ = id => document.getElementById(id);
        const fmt = n => n.toLocaleString('de-DE', { maximumFractionDigits: 1 });
        const PALETTE = ['#1f6feb', '#3fb950', '#e3b341', '#db61a2', '#a371f7', '#f0883e',
                         '#2dd4bf', '#f85149', '#8b949e', '#58a6ff', '#d29922', '#7ee787'];
        const colorFor = i => PALETTE[i % PALETTE.length];

        const API_BASE = (new URLSearchParams(location.search).get('api')
          || (/^https?:$/.test(location.protocol) ? location.origin : 'http://localhost:8080'))
          .replace(/\/+$/, '');

        let hours = 168;
        let current = null;
        let zoom = null;      // { from: 'YYYY-MM-DDTHH:MM:SS', to: '…' } — Zoombereich, sonst null
        let brush = null;     // { x0 } während des Aufziehens (viewBox-x)

        function bucketFor(h) {
          if (h <= 24) return 15;
          if (h <= 72) return 30;
          if (h <= 168) return 60;
          if (h <= 336) return 120;
          return 240;
        }

        // Zeitstempel zeitzonenfrei behandeln: lokal parsen, lokal formatieren — der Roundtrip
        // zur API bleibt so stabil, egal in welcher Zone der Browser läuft.
        const parseLocal = s => {
          const m = String(s).match(/(\d+)-(\d+)-(\d+)[T ](\d+):(\d+)(?::(\d+))?/);
          return m ? new Date(+m[1], +m[2] - 1, +m[3], +m[4], +m[5], +(m[6] || 0)).getTime() : Date.parse(s);
        };
        const fmtLocal = ms => {
          const d = new Date(ms), p = n => String(n).padStart(2, '0');
          return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`;
        };

        // Gestapelte Fläche: je Reihe (Ziel oder Ressourcenpunkt) ein Band, aufwärts akkumuliert.
        function drawArea(data) {
          const svg = $('area');
          const W = 960, H = 300, padL = 44, padB = 22, padT = 8, padR = 8;
          const b = data.buckets, keys = data.keys;
          svg.setAttribute('viewBox', `0 0 ${W} ${H}`);
          if (!b.length) { svg.innerHTML = '<text x="12" y="20" class="xlabel">keine Daten im Zeitraum</text>'; return; }

          const x = i => padL + (b.length === 1 ? 0 : i / (b.length - 1) * (W - padL - padR));
          const yMax = Math.max(1, ...b.map(k => k.uph));
          const y = v => H - padB - v / yMax * (H - padB - padT);

          let below = b.map(() => 0);
          let bands = '';
          keys.forEach((d, di) => {
            const above = b.map((k, i) => below[i] + (k.series[d] || 0));
            const top = b.map((k, i) => `${x(i).toFixed(1)},${y(above[i]).toFixed(1)}`);
            const bot = b.map((k, i) => `${x(i).toFixed(1)},${y(below[i]).toFixed(1)}`).reverse();
            bands += `<polygon points="${top.concat(bot).join(' ')}" fill="${colorFor(di)}" fill-opacity="0.85"></polygon>`;
            below = above;
          });

          // Y-Gitter/Beschriftung (0, Mitte, Max).
          let grid = '';
          for (const frac of [0, 0.5, 1]) {
            const v = yMax * frac, yy = y(v);
            grid += `<line class="axis" x1="${padL}" y1="${yy.toFixed(1)}" x2="${W - padR}" y2="${yy.toFixed(1)}"></line>`;
            grid += `<text class="glabel" x="${padL - 6}" y="${(yy + 3).toFixed(1)}" text-anchor="end">${fmt(v)}</text>`;
          }

          // X-Beschriftung: bis zu 6 Zeitmarken. Format nach Spannweite (Stunden vs. Tage).
          const spanH = (parseLocal(data.to) - parseLocal(data.from)) / 3600000;
          let xlab = '';
          const step = Math.max(1, Math.floor(b.length / 6));
          for (let i = 0; i < b.length; i += step) {
            const t = new Date(b[i].at);
            const s = spanH <= 36
              ? t.toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' })
              : spanH <= 24 * 5
                ? t.toLocaleString('de-DE', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' })
                : t.toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit' });
            xlab += `<text class="xlabel" x="${x(i).toFixed(1)}" y="${H - 6}" text-anchor="middle">${s}</text>`;
          }

          svg.innerHTML = grid + bands +
            `<rect class="sel" id="sel" y="${padT}" height="${H - padB - padT}" width="0" hidden></rect>` +
            `<line class="cursor" id="cursor" y1="${padT}" y2="${H - padB}"></line>` + xlab +
            `<rect x="${padL}" y="${padT}" width="${W - padL - padR}" height="${H - padB - padT}" fill="transparent" id="hit"></rect>`;
          svg._x = x; svg._n = b.length; svg._padL = padL; svg._padR = padR; svg._padT = padT; svg._W = W;
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
          const from = new Date(data.from), to = new Date(data.to);
          $('meta').classList.remove('err');
          $('meta').textContent =
            `${data.totalOrders.toLocaleString('de-DE')} Aufträge · Raster ${data.bucketMinutes} min · `
            + `${from.toLocaleString('de-DE')} – ${to.toLocaleString('de-DE')}`;
        }

        // clientX -> Bruchteil 0..1 der Plotbreite.
        function fracAtClientX(svg, clientX) {
          const box = svg.getBoundingClientRect();
          const vx = (clientX - box.left) / box.width * svg._W;
          return Math.min(1, Math.max(0, (vx - svg._padL) / (svg._W - svg._padL - svg._padR)));
        }
        function timeAtFrac(frac) {
          const a = parseLocal(current.from), b = parseLocal(current.to);
          return a + frac * (b - a);
        }

        $('area').addEventListener('mousedown', e => {
          if (!current || !current.buckets.length) return;
          e.preventDefault();
          brush = { x0: fracAtClientX($('area'), e.clientX) };
          $('tip').style.opacity = 0;
        });

        $('area').addEventListener('dblclick', () => {
          if (zoom) { zoom = null; load(); }
        });

        document.addEventListener('mouseup', e => {
          if (!brush) return;
          const svg = $('area');
          const f0 = brush.x0, f1 = fracAtClientX(svg, e.clientX);
          brush = null;
          const sel = $('sel'); if (sel) sel.hidden = true;
          if (Math.abs(f1 - f0) < 0.01) return;          // reiner Klick — kein Zoom
          const t0 = timeAtFrac(Math.min(f0, f1)), t1 = timeAtFrac(Math.max(f0, f1));
          zoom = { from: fmtLocal(t0), to: fmtLocal(t1) };
          load();
        });

        document.addEventListener('mousemove', e => {
          const svg = $('area');

          if (brush) {                                   // Bereich aufziehen
            const plotL = svg._padL, plotR = svg._W - svg._padR;
            const f1 = fracAtClientX(svg, e.clientX);
            const a = plotL + Math.min(brush.x0, f1) * (plotR - plotL);
            const w = Math.abs(f1 - brush.x0) * (plotR - plotL);
            const sel = $('sel');
            if (sel) { sel.setAttribute('x', a.toFixed(1)); sel.setAttribute('width', w.toFixed(1)); sel.hidden = false; }
            const cursor = $('cursor'); if (cursor) cursor.style.visibility = 'hidden';
            $('tip').style.opacity = 0;
            return;
          }

          const hit = e.target.id === 'hit' ? svg : null;
          const cursor = $('cursor');
          if (!hit || !current || !current.buckets.length) {
            if (cursor) cursor.style.visibility = 'hidden';
            $('tip').style.opacity = 0;
            return;
          }
          const frac = fracAtClientX(svg, e.clientX);
          const i = Math.round(frac * (svg._n - 1));
          const k = current.buckets[i];
          const cx = svg._x(i);
          cursor.setAttribute('x1', cx); cursor.setAttribute('x2', cx);
          cursor.style.visibility = 'visible';

          const parts = current.keys
            .map((d, di) => ({ d, di, uph: k.series[d] || 0 }))
            .filter(p => p.uph > 0)
            .sort((a, b) => b.uph - a.uph);
          const at = new Date(k.at);
          $('tip').innerHTML = `<b>${at.toLocaleString('de-DE')}</b><div><span>gesamt</span><span>${fmt(k.uph)} UPH</span></div>`
            + parts.map(p => `<div><span><i class="sw" style="display:inline-block;width:8px;height:8px;background:${colorFor(p.di)}"></i> ${current.totals.find(t => t.key === p.d)?.label || p.d}</span><span>${fmt(p.uph)}</span></div>`).join('');
          $('tip').style.opacity = 1;
          $('tip').style.left = Math.min(window.innerWidth - 270, e.clientX + 14) + 'px';
          $('tip').style.top = (e.clientY + 14) + 'px';
        });

        async function load() {
          const q = new URLSearchParams();
          q.set('groupBy', $('dim').value);
          if ($('rp').value) q.set('rp', $('rp').value);
          if (zoom) {
            const spanH = (parseLocal(zoom.to) - parseLocal(zoom.from)) / 3600000;
            q.set('from', zoom.from);
            q.set('to', zoom.to);
            q.set('bucket', bucketFor(spanH));
          } else {
            q.set('hours', hours);
            q.set('bucket', bucketFor(hours));
          }
          $('zoomout').hidden = !zoom;
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
          zoom = null;
          for (const b of $('ranges').children) b.classList.toggle('on', b === btn);
          load();
        });
        $('zoomout').addEventListener('click', () => { zoom = null; load(); });
        $('rp').addEventListener('change', load);   // Zoombereich bleibt erhalten
        $('dim').addEventListener('change', load);
        load();
        setInterval(load, 60000);
        </script>
        </body>
        </html>
        """;
}
