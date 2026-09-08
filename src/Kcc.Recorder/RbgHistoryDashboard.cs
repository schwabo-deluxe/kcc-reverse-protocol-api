namespace Kcc.Recorder;

/// <summary>
/// Langzeitansicht unter <c>/rbg</c>: pollt <c>/api/rbg-history</c> und stellt die RBG über
/// Wochen bis Monate gegenüber — Verlauf der Spiele/h bzw. des Auslastungsgrads je Gerät,
/// Anteil an der Gesamtlast und die Spreizung zwischen dem am stärksten und am schwächsten
/// belasteten RBG.
/// </summary>
public static class RbgHistoryDashboard
{
    public const string Html = """
        <!doctype html>
        <html lang="de">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>RBG-Belastung im Langzeitvergleich</title>
        <style>
          :root { color-scheme: dark; }
          * { box-sizing: border-box; }
          body { margin: 0; font: 14px/1.45 system-ui, sans-serif; background: #14171c; color: #e6e6e6; }
          header { padding: 14px 20px; border-bottom: 1px solid #2a2f37; display: flex; gap: 12px; align-items: center; flex-wrap: wrap; }
          header h1 { font-size: 16px; margin: 0 6px 0 0; font-weight: 600; }
          .seg { display: flex; gap: 2px; background: #10141a; border: 1px solid #2a2f37; border-radius: 7px; padding: 2px; }
          .seg button { background: none; border: 0; color: #9aa4b2; font: inherit; font-size: 12px; padding: 4px 10px; border-radius: 5px; cursor: pointer; }
          .seg button:hover { color: #e6e6e6; }
          .seg button.on { background: #1f6feb; color: #fff; }
          .seg-label { color: #7a8494; font-size: 11px; text-transform: uppercase; letter-spacing: .04em; }
          .meta { margin-left: auto; color: #9aa4b2; font-size: 12px; }
          .meta.err { color: #ff6b6b; }
          main { padding: 18px 20px 40px; max-width: 1400px; margin: 0 auto; }
          .card { background: #1c2128; border: 1px solid #2a2f37; border-radius: 10px; padding: 16px 18px; margin-bottom: 16px; }
          .card h2 { font-size: 13px; margin: 0 0 12px; color: #9aa4b2; text-transform: uppercase; letter-spacing: .05em; font-weight: 600; }

          .verdict { display: flex; gap: 26px; align-items: baseline; flex-wrap: wrap; margin-bottom: 14px; }
          .verdict .big { font-size: 30px; font-weight: 800; line-height: 1; font-variant-numeric: tabular-nums; }
          .verdict .cap { color: #9aa4b2; font-size: 12px; text-transform: uppercase; letter-spacing: .04em; }
          .verdict .say { color: #cdd6e0; font-size: 13px; max-width: 52ch; }

          .bars { display: grid; grid-template-columns: minmax(90px, auto) 1fr minmax(150px, auto); gap: 6px 12px; align-items: center; }
          .bars .nm { font-size: 12px; font-variant-numeric: tabular-nums; }
          .bars .nm small { color: #7a8494; }
          .bars .track { position: relative; height: 18px; background: #10141a; border-radius: 4px; overflow: hidden; }
          .bars .fill { position: absolute; inset: 0 auto 0 0; border-radius: 4px; }
          .bars .avg { position: absolute; top: -2px; bottom: -2px; width: 2px; background: #cdd6e0; opacity: .55; }
          .bars .val { font-size: 12px; color: #9aa4b2; font-variant-numeric: tabular-nums; text-align: right; }
          .bars .val b { color: #e6e6e6; }

          .legend { display: flex; flex-wrap: wrap; gap: 4px 14px; margin-bottom: 8px; }
          .legend button { background: none; border: 0; color: #cdd6e0; font: inherit; font-size: 12px; cursor: pointer;
                           display: flex; align-items: center; gap: 6px; padding: 2px 4px; border-radius: 4px; }
          .legend button:hover { background: #232830; }
          .legend button.off { color: #5a6373; }
          .legend .sw { width: 11px; height: 11px; border-radius: 3px; flex: 0 0 auto; }
          .legend button.off .sw { opacity: .25; }

          #chart { width: 100%; height: 340px; display: block; overflow: visible; }
          #chart .grid { stroke: #262c34; stroke-width: 1; }
          #chart .axis { fill: #7a8494; font-size: 11px; }
          #chart .cap { stroke: #7a8494; stroke-width: 1; stroke-dasharray: 4 4; }
          #chart .ln { fill: none; stroke-width: 1.8; stroke-linejoin: round; stroke-linecap: round; }
          #chart .cursor { stroke: #7a8494; stroke-width: 1; visibility: hidden; }

          table { width: 100%; border-collapse: collapse; }
          th, td { padding: 7px 10px; text-align: right; border-bottom: 1px solid #262c34; font-variant-numeric: tabular-nums; white-space: nowrap; }
          th:first-child, td:first-child { text-align: left; }
          th { color: #9aa4b2; font-size: 11px; text-transform: uppercase; letter-spacing: .04em; font-weight: 600; }
          tbody tr:last-child td { border-bottom: 0; }
          td .sw { display: inline-block; width: 9px; height: 9px; border-radius: 2px; margin-right: 7px; }
          .wrap { overflow-x: auto; }
          #tip { position: fixed; pointer-events: none; opacity: 0; transition: opacity .08s; background: #10141a;
                 border: 1px solid #2a2f37; border-radius: 6px; padding: 7px 9px; font-size: 12px; z-index: 10;
                 font-variant-numeric: tabular-nums; }
          #tip .r { display: flex; gap: 10px; justify-content: space-between; }
          #tip .h { color: #9aa4b2; margin-bottom: 4px; }
          .hint { color: #7a8494; font-size: 12px; margin-top: 10px; }
        </style>
        </head>
        <body>
        <!--nav-->
        <header>
          <h1>RBG-Belastung</h1>
          <span class="seg-label">Zeitraum</span>
          <div class="seg" id="range">
            <button data-h="24">24 h</button>
            <button data-h="168" class="on">7 T</button>
            <button data-h="336">14 T</button>
            <button data-h="672">4 W</button>
            <button data-h="2160">3 M</button>
            <button data-h="8760">1 J</button>
          </div>
          <span class="seg-label">Kurve</span>
          <div class="seg" id="metric">
            <button data-m="cyclesPerHour" class="on">Spiele/h</button>
            <button data-m="busyPercent">Auslastung %</button>
          </div>
          <div class="meta" id="meta">lädt …</div>
        </header>
        <main>
          <div class="card">
            <h2>Belastungsvergleich</h2>
            <div class="verdict" id="verdict"></div>
            <div class="bars" id="bars"></div>
            <div class="hint">Balken = Anteil an allen Spielen im Zeitraum. Die helle Linie markiert die Gleichverteilung.</div>
          </div>
          <div class="card">
            <h2 id="chartTitle">Verlauf</h2>
            <div class="legend" id="legend"></div>
            <svg id="chart" viewBox="0 0 1000 340" preserveAspectRatio="none"></svg>
          </div>
          <div class="card">
            <h2>Kennzahlen je RBG</h2>
            <div class="wrap">
              <table>
                <thead><tr>
                  <th>RBG</th><th>Ein</th><th>Aus</th><th>Doppelspiele</th><th>Einzelspiele</th>
                  <th>Spiele</th><th>Anteil</th><th>Ø Spiele/h</th><th>Spitze</th>
                  <th>Ø Leistung</th><th>Ø Auslastung</th><th>Aktive Std.</th>
                </tr></thead>
                <tbody id="rows"></tbody>
              </table>
            </div>
            <div class="hint">Spiele = Doppelspiel-Äquivalent (Doppel + Einzel/2), im Aufzeichnungsraster
              bestimmt. Ø Spiele/h und Ø Auslastung beziehen sich auf den gesamten Zeitraum,
              Stillstände eingerechnet — „Aktive Std." zeigt, wie lange überhaupt gefahren wurde.</div>
          </div>
        </main>
        <div id="tip" role="status"></div>
        <script>
        const $ = id => document.getElementById(id);
        const fmt = (n, d) => (n ?? 0).toLocaleString('de-DE', { maximumFractionDigits: d ?? 1 });
        const API_BASE = (new URLSearchParams(location.search).get('api')
          || (/^https?:$/.test(location.protocol) ? location.origin : 'http://localhost:8082'))
          .replace(/\/+$/, '');

        const PALETTE = ['#4fa3ff', '#5ccb7e', '#ffb454', '#e06c9f', '#9d7bff', '#4fd8d0', '#ff6b6b', '#c3cb5c'];

        let hours = 168;
        let metric = 'cyclesPerHour';
        let data = null;
        const hidden = new Set();
        const colorOf = c => PALETTE[(data ? data.connections.indexOf(c) : 0) % PALETTE.length];
        const nameOf = c => {
          const t = data && data.totals.find(x => x.connection === c);
          return t && t.label && t.label !== c ? `${t.label} · ${c}` : c;
        };
        const unit = () => metric === 'busyPercent' ? '%' : '/h';

        function shown() {
          return data ? data.connections.filter(c => !hidden.has(c)) : [];
        }

        function verdict() {
          const t = data.totals.filter(x => x.cycles > 0);
          const spread = data.spreadPercent;
          const tone = spread >= 30 ? '#ff6b6b' : spread >= 15 ? '#ffb454' : '#5ccb7e';
          const say = t.length < 2
            ? 'Zu wenig Bewegung im Zeitraum für einen Vergleich.'
            : `<b>${nameOf(data.busiest)}</b> fährt am meisten (${fmt(t[0].cycles)} Spiele), ` +
              `<b>${nameOf(data.quietest)}</b> am wenigsten (${fmt(t[t.length - 1].cycles)} Spiele) — ` +
              `also ${fmt(spread)} % weniger.`;
          $('verdict').innerHTML = `
            <div>
              <div class="big" style="color:${tone}">${fmt(spread)} %</div>
              <div class="cap">Spreizung</div>
            </div>
            <div>
              <div class="big">${fmt(data.totalCycles)}</div>
              <div class="cap">Spiele gesamt</div>
            </div>
            <div>
              <div class="big">${t.length}</div>
              <div class="cap">RBG mit Bewegung</div>
            </div>
            <div class="say">${say}</div>`;
        }

        function bars() {
          const t = data.totals;
          const even = t.length ? 100 / t.length : 0;
          const max = Math.max(1, ...t.map(x => x.share));
          $('bars').innerHTML = t.map(x => `
            <div class="nm">${x.label && x.label !== x.connection ? `${x.label}<br><small>${x.connection}</small>` : x.connection}</div>
            <div class="track">
              <div class="fill" style="width:${(x.share / max * 100).toFixed(1)}%;background:${colorOf(x.connection)}"></div>
              <div class="avg" style="left:${(even / max * 100).toFixed(1)}%"></div>
            </div>
            <div class="val"><b>${fmt(x.share)} %</b> · ${fmt(x.avgCyclesPerHour)} Spiele/h · Ø ${fmt(x.avgBusyPercent)} % ausgelastet</div>`).join('');
        }

        function legend() {
          $('legend').innerHTML = data.connections.map(c => `
            <button data-c="${c}" class="${hidden.has(c) ? 'off' : ''}">
              <span class="sw" style="background:${colorOf(c)}"></span>${nameOf(c)}
            </button>`).join('');
        }

        // Mehrlinien-Verlauf: eine Linie je RBG, gemeinsame Skala — nur so ist ablesbar,
        // ob ein Gerät dauerhaft über den anderen liegt.
        function chart() {
          const W = 1000, H = 340, L = 52, R = 14, T = 12, B = 28;
          const w = W - L - R, h = H - T - B;
          const b = data.buckets, keys = shown();
          $('chartTitle').textContent = metric === 'busyPercent'
            ? 'Verlauf Auslastungsgrad' : 'Verlauf Spiele/h';

          if (!b.length || !keys.length) { $('chart').innerHTML = ''; return; }

          const caps = data.totals.filter(t => keys.includes(t.connection)).map(t => t.maxCyclesPerHour);
          const cap = metric === 'cyclesPerHour' && caps.length ? Math.max(...caps) : 0;
          let peak = 0;
          for (const pt of b) for (const k of keys) peak = Math.max(peak, pt[metric][k] ?? 0);
          // Etwas Luft nach oben; sobald es Richtung Kapazität geht, muss deren Linie ins Bild.
          let top = Math.max(1, peak * 1.08);
          if (metric === 'busyPercent') top = Math.min(100, Math.max(top, 10));
          else if (cap > 0 && peak > cap * 0.6) top = Math.max(top, cap * 1.05);

          const x = i => L + (b.length === 1 ? w / 2 : (i / (b.length - 1)) * w);
          const y = v => T + h - Math.min(v / top, 1) * h;

          const ticks = 4;
          let g = '';
          for (let i = 0; i <= ticks; i++) {
            const v = top * i / ticks, yy = y(v);
            g += `<line class="grid" x1="${L}" y1="${yy.toFixed(1)}" x2="${W - R}" y2="${yy.toFixed(1)}"></line>` +
                 `<text class="axis" x="${L - 8}" y="${(yy + 4).toFixed(1)}" text-anchor="end">${fmt(v, 0)}</text>`;
          }
          if (cap > 0 && cap <= top)
            g += `<line class="cap" x1="${L}" y1="${y(cap).toFixed(1)}" x2="${W - R}" y2="${y(cap).toFixed(1)}"></line>` +
                 `<text class="axis" x="${W - R}" y="${(y(cap) - 5).toFixed(1)}" text-anchor="end">Kapazität ${cap}/h</text>`;

          const day = hours > 48;
          const opts = day ? { day: '2-digit', month: '2-digit' } : { hour: '2-digit', minute: '2-digit' };
          const steps = Math.min(7, b.length);
          for (let i = 0; i < steps; i++) {
            const idx = Math.round(i * (b.length - 1) / Math.max(1, steps - 1));
            g += `<text class="axis" x="${x(idx).toFixed(1)}" y="${H - 8}" text-anchor="middle">` +
                 `${new Date(b[idx].at).toLocaleString('de-DE', opts)}</text>`;
          }

          const lines = keys.map(k => {
            const d = b.map((pt, i) => `${i ? 'L' : 'M'}${x(i).toFixed(1)},${y(pt[metric][k] ?? 0).toFixed(1)}`).join(' ');
            return `<path class="ln" d="${d}" stroke="${colorOf(k)}"></path>`;
          }).join('');

          $('chart').innerHTML = g + lines +
            `<line class="cursor" y1="${T}" y2="${T + h}"></line>` +
            `<rect x="${L}" y="${T}" width="${w}" height="${h}" fill="transparent" id="hit"></rect>`;
        }

        function render(d) {
          data = d;
          for (const c of [...hidden]) if (!d.connections.includes(c)) hidden.delete(c);
          verdict();
          bars();
          legend();
          chart();

          $('rows').innerHTML = d.totals.map(t => `
            <tr>
              <td><span class="sw" style="background:${colorOf(t.connection)}"></span>${nameOf(t.connection)}</td>
              <td>${t.puts}</td><td>${t.fetches}</td>
              <td>${t.doubleCycles}</td><td>${t.singleCycles}</td>
              <td><b>${fmt(t.cycles)}</b></td>
              <td>${fmt(t.share)} %</td>
              <td>${fmt(t.avgCyclesPerHour)}</td>
              <td>${fmt(t.peakCyclesPerHour)}</td>
              <td>${fmt(t.avgLoadPercent)} %</td>
              <td>${fmt(t.avgBusyPercent)} %</td>
              <td>${fmt(t.activeHours)}</td>
            </tr>`).join('');

          const from = new Date(d.from), to = new Date(d.to);
          $('meta').classList.remove('err');
          $('meta').textContent =
            `${from.toLocaleDateString('de-DE')} – ${to.toLocaleDateString('de-DE')} · ` +
            `Raster ${d.bucketMinutes} min · Stand ${new Date().toLocaleTimeString('de-DE')}`;
        }

        // Fadenkreuz mit allen RBG-Werten an dieser Stelle — der eigentliche Vergleichsmoment.
        $('chart').addEventListener('mousemove', e => {
          if (!data || !data.buckets.length) return;
          const svg = $('chart'), box = svg.getBoundingClientRect();
          const L = 52, R = 14, W = 1000;
          const px = (e.clientX - box.left) / box.width * W;
          const ratio = Math.min(1, Math.max(0, (px - L) / (W - L - R)));
          const i = Math.round(ratio * (data.buckets.length - 1));
          const pt = data.buckets[i];
          const keys = shown();
          if (!pt || !keys.length) return;

          const cursor = svg.querySelector('.cursor');
          const cx = L + ratio * (W - L - R);
          cursor.setAttribute('x1', cx); cursor.setAttribute('x2', cx);
          cursor.style.visibility = 'visible';

          const rows = keys
            .map(k => ({ k, v: pt[metric][k] ?? 0 }))
            .sort((a, b2) => b2.v - a.v)
            .map(r => `<div class="r"><span><span class="sw" style="display:inline-block;width:9px;height:9px;border-radius:2px;background:${colorOf(r.k)};margin-right:6px"></span>${r.k}</span><b>${fmt(r.v)} ${unit()}</b></div>`)
            .join('');
          const tip = $('tip');
          tip.innerHTML = `<div class="h">${new Date(pt.at).toLocaleString('de-DE')}</div>${rows}`;
          tip.style.opacity = 1;
          tip.style.left = Math.min(window.innerWidth - 230, e.clientX + 14) + 'px';
          tip.style.top = Math.min(window.innerHeight - 40 - keys.length * 20, e.clientY + 14) + 'px';
        });
        $('chart').addEventListener('mouseleave', () => {
          $('tip').style.opacity = 0;
          const c = $('chart').querySelector('.cursor');
          if (c) c.style.visibility = 'hidden';
        });

        $('legend').addEventListener('click', e => {
          const btn = e.target.closest('button');
          if (!btn) return;
          const c = btn.dataset.c;
          if (hidden.has(c)) hidden.delete(c); else hidden.add(c);
          legend();
          chart();
        });

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
          chart();
        });

        // Raster: fein genug zum Erkennen, grob genug für lange Zeiträume.
        const bucketFor = h => h <= 24 ? 15 : h <= 168 ? 60 : h <= 672 ? 240 : 1440;

        async function load() {
          try {
            const res = await fetch(`${API_BASE}/api/rbg-history?hours=${hours}&bucket=${bucketFor(hours)}`,
              { cache: 'no-store' });
            if (!res.ok) throw new Error('HTTP ' + res.status);
            render(await res.json());
          } catch (e) {
            $('meta').classList.add('err');
            $('meta').textContent = 'Fehler: ' + e.message;
          }
        }

        window.addEventListener('resize', () => { if (data) chart(); });
        load();
        setInterval(load, 300000);
        </script>
        </body>
        </html>
        """;
}
