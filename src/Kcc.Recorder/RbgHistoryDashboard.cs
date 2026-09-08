namespace Kcc.Recorder;

/// <summary>
/// Langzeitansicht unter <c>/rbg</c>: pollt <c>/api/rbg-history</c> und stellt die RBG über
/// Wochen bis Monate gegenüber — kombinierter Verlauf aller Geräte, je Gerät ein eigener Chart
/// mit Auslastung, Leistung und Leerlauf, dazu Anteil und Spreizung. Zeitbereich per Maus
/// aufziehbar wie in <c>/verlauf</c>.
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
          .zoomout { background: #10141a; color: #9aa4b2; border: 1px solid #2a2f37; border-radius: 6px; padding: 5px 10px; font: inherit; font-size: 12px; cursor: pointer; }
          .zoomout[hidden] { display: none; }
          .meta { margin-left: auto; color: #9aa4b2; font-size: 12px; }
          .meta.err { color: #ff6b6b; }
          main { padding: 18px 20px 40px; max-width: 1400px; margin: 0 auto; }
          .card { background: #1c2128; border: 1px solid #2a2f37; border-radius: 10px; padding: 16px 18px; margin-bottom: 16px; }
          .card h2 { font-size: 13px; margin: 0 0 12px; color: #9aa4b2; text-transform: uppercase; letter-spacing: .05em; font-weight: 600; }
          [title] { cursor: help; }

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
          .sw { width: 11px; height: 11px; border-radius: 3px; flex: 0 0 auto; }
          .legend button.off .sw { opacity: .25; }

          svg.plot { width: 100%; display: block; overflow: visible; touch-action: none; }
          svg.plot .grid { stroke: #262c34; stroke-width: 1; }
          svg.plot .axis { fill: #7a8494; font-size: 11px; }
          svg.plot .cap { stroke: #7a8494; stroke-width: 1; stroke-dasharray: 4 4; }
          svg.plot .ln { fill: none; stroke-width: 1.8; stroke-linejoin: round; stroke-linecap: round; }
          svg.plot .cursor { stroke: #7a8494; stroke-width: 1; visibility: hidden; }
          /* Auswahlrechteck beim Aufziehen. Sichtbarkeit über das SVG-Attribut 'visibility',
             nicht über 'hidden' — letzteres gibt es auf SVGElement nicht. */
          svg.plot .sel { fill: #1f6feb; fill-opacity: .18; stroke: #1f6feb; stroke-opacity: .6;
                          stroke-width: 1; pointer-events: none; }
          #chart { height: 340px; }

          .minis { display: grid; grid-template-columns: repeat(auto-fit, minmax(420px, 1fr)); gap: 14px; }
          .mini { background: #171b21; border: 1px solid #262c34; border-radius: 8px; padding: 12px 14px; }
          .mini .mh { display: flex; justify-content: space-between; align-items: baseline; gap: 10px; flex-wrap: wrap; margin-bottom: 6px; }
          .mini .mn { font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: .04em; display: flex; align-items: center; gap: 7px; }
          .mini .ms { font-size: 12px; color: #9aa4b2; font-variant-numeric: tabular-nums; display: flex; gap: 12px; flex-wrap: wrap; }
          .mini .ms b { color: #e6e6e6; }
          .mini svg.plot { height: 150px; }
          .mkey { display: flex; gap: 14px; flex-wrap: wrap; font-size: 11px; color: #9aa4b2; margin-top: 6px; }
          .mkey span { display: flex; align-items: center; gap: 5px; }
          .mkey i { width: 14px; height: 3px; border-radius: 2px; display: inline-block; }

          table { width: 100%; border-collapse: collapse; }
          th, td { padding: 7px 10px; text-align: right; border-bottom: 1px solid #262c34; font-variant-numeric: tabular-nums; white-space: nowrap; }
          th:first-child, td:first-child { text-align: left; }
          th { color: #9aa4b2; font-size: 11px; text-transform: uppercase; letter-spacing: .04em; font-weight: 600; }
          tbody tr:last-child td { border-bottom: 0; }
          td .sw { display: inline-block; margin-right: 7px; }
          .wrap { overflow-x: auto; }
          #tip { position: fixed; pointer-events: none; opacity: 0; transition: opacity .08s; background: #10141a;
                 border: 1px solid #2a2f37; border-radius: 6px; padding: 7px 9px; font-size: 12px; z-index: 10;
                 font-variant-numeric: tabular-nums; }
          #tip .r { display: flex; gap: 12px; justify-content: space-between; }
          #tip .h { color: #9aa4b2; margin-bottom: 4px; }
          .hint { color: #7a8494; font-size: 12px; margin-top: 10px; }
        </style>
        </head>
        <body>
        <!--nav-->
        <!--rbghelp-->
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
            <button data-m="loadPercent">Leistung %</button>
            <button data-m="busyPercent">Auslastung %</button>
          </div>
          <button class="zoomout" id="zoomout" hidden>⤺ Zoom zurück</button>
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
            <svg id="chart" class="plot" viewBox="0 0 1000 340" preserveAspectRatio="none"></svg>
            <div class="hint">Zeitbereich mit der Maus aufziehen zoomt hinein — Doppelklick oder „Zoom zurück“ setzt zurück.</div>
          </div>
          <div class="card">
            <h2>Je Gerät: Auslastung, Leistung, Leerlauf</h2>
            <div class="minis" id="minis"></div>
            <div class="mkey">
              <span><i style="background:#4fa3ff"></i>Auslastung (Zeit mit Auftrag)</span>
              <span><i style="background:#ffb454"></i>Leistung (Durchsatz gegen Kapazität)</span>
              <span><i style="background:#3a4150;height:9px"></i>Leerlauf (Fläche über der Auslastung)</span>
            </div>
          </div>
          <div class="card">
            <h2>Kennzahlen je RBG</h2>
            <div class="wrap">
              <table>
                <thead><tr id="head"></tr></thead>
                <tbody id="rows"></tbody>
              </table>
            </div>
          </div>
        </main>
        <div id="tip" role="status"></div>
        <script>
        const $ = id => document.getElementById(id);
        const fmt = (n, d) => (n ?? 0).toLocaleString('de-DE', { maximumFractionDigits: d ?? 1 });
        const API_BASE = (new URLSearchParams(location.search).get('api')
          || (/^https?:$/.test(location.protocol) ? location.origin : 'http://localhost:8082'))
          .replace(/\/+$/, '');
        const H = window.RBG_HELP || {};
        const esc = s => String(s).replace(/"/g, '&quot;');
        const help = k => H[k] ? ` title="${esc(H[k])}"` : '';

        const PALETTE = ['#4fa3ff', '#5ccb7e', '#ffb454', '#e06c9f', '#9d7bff', '#4fd8d0', '#ff6b6b', '#c3cb5c'];
        const C_BUSY = '#4fa3ff', C_LOAD = '#ffb454', C_IDLE = '#3a4150';

        let hours = 168;
        let metric = 'cyclesPerHour';
        let data = null;
        let zoom = null;    // { from: 'YYYY-MM-DDTHH:MM:SS', to: '…' }
        let brush = null;   // { svg, f0 } während des Aufziehens
        const hidden = new Set();

        const colorOf = c => PALETTE[(data ? data.connections.indexOf(c) : 0) % PALETTE.length];
        const nameOf = c => {
          const t = data && data.totals.find(x => x.connection === c);
          return t && t.label && t.label !== c ? `${t.label} · ${c}` : c;
        };
        const unitOf = m => m === 'cyclesPerHour' ? '/h' : '%';
        const shown = () => data ? data.connections.filter(c => !hidden.has(c)) : [];

        // Zeitstempel zeitzonenfrei behandeln — der Roundtrip zur API bleibt so stabil,
        // egal in welcher Zone der Browser läuft (wie in /verlauf).
        const parseLocal = s => {
          const m = String(s).match(/(\d+)-(\d+)-(\d+)[T ](\d+):(\d+)(?::(\d+))?/);
          return m ? new Date(+m[1], +m[2] - 1, +m[3], +m[4], +m[5], +(m[6] || 0)).getTime() : Date.parse(s);
        };
        const fmtLocal = ms => {
          const d = new Date(ms), p = n => String(n).padStart(2, '0');
          return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`;
        };
        const dur = h => h >= 1 ? `${fmt(h)} h` : `${fmt(h * 60, 0)} min`;

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
            <div${help('spread')}>
              <div class="big" style="color:${tone}">${fmt(spread)} %</div>
              <div class="cap">Spreizung</div>
            </div>
            <div${help('cycles')}>
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
            <div class="track"${help('share')}>
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

        // ---- Zeichenwerkzeug: gemeinsame Geometrie für alle Charts --------------------------------
        // Merkt sich Ränder am SVG, damit Hover und Aufziehen dieselbe Skala benutzen.
        function frame(svg, W, HGT, L, R, T, B) {
          svg._W = W; svg._L = L; svg._R = R;
          svg.setAttribute('viewBox', `0 0 ${W} ${HGT}`);
          return { w: W - L - R, h: HGT - T - B };
        }

        function xLabels(b, x, W, HGT, span) {
          const day = span > 48;
          const opts = day ? { day: '2-digit', month: '2-digit' } : { hour: '2-digit', minute: '2-digit' };
          const steps = Math.min(6, b.length);
          let out = '';
          for (let i = 0; i < steps; i++) {
            const idx = Math.round(i * (b.length - 1) / Math.max(1, steps - 1));
            const anchor = i === 0 ? 'start' : i === steps - 1 ? 'end' : 'middle';
            out += `<text class="axis" x="${x(idx).toFixed(1)}" y="${HGT - 8}" text-anchor="${anchor}">` +
                   `${new Date(b[idx].at).toLocaleString('de-DE', opts)}</text>`;
          }
          return out;
        }

        const path = (b, x, y, get) =>
          b.map((pt, i) => `${i ? 'L' : 'M'}${x(i).toFixed(1)},${y(get(pt)).toFixed(1)}`).join(' ');

        const showSel = (sel, on) => sel && sel.setAttribute('visibility', on ? 'visible' : 'hidden');

        const overlay = (L, T, w, h) =>
          `<rect class="sel" x="${L}" y="${T}" width="0" height="${h}" visibility="hidden"></rect>` +
          `<line class="cursor" y1="${T}" y2="${T + h}"></line>` +
          `<rect class="hit" x="${L}" y="${T}" width="${w}" height="${h}" fill="transparent"></rect>`;

        // ---- Kombinierter Verlauf ----------------------------------------------------------------
        function chart() {
          const svg = $('chart'), W = 1000, HGT = 340, L = 52, R = 14, T = 12, B = 28;
          const { w, h } = frame(svg, W, HGT, L, R, T, B);
          const b = data.buckets, keys = shown();
          $('chartTitle').textContent = metric === 'busyPercent' ? 'Verlauf Auslastungsgrad'
            : metric === 'loadPercent' ? 'Verlauf Leistungsgrad' : 'Verlauf Spiele/h';

          if (!b.length || !keys.length) { svg.innerHTML = ''; return; }

          const caps = data.totals.filter(t => keys.includes(t.connection)).map(t => t.maxCyclesPerHour);
          const cap = metric === 'cyclesPerHour' && caps.length ? Math.max(...caps) : 0;
          let peak = 0;
          for (const pt of b) for (const k of keys) peak = Math.max(peak, pt[metric][k] ?? 0);

          let top = Math.max(1, peak * 1.08);
          if (metric !== 'cyclesPerHour') top = Math.max(Math.min(105, top), 20);
          else if (cap > 0 && peak > cap * 0.6) top = Math.max(top, cap * 1.05);

          const x = i => L + (b.length === 1 ? w / 2 : (i / (b.length - 1)) * w);
          const y = v => T + h - Math.min(v / top, 1) * h;

          let g = '';
          for (let i = 0; i <= 4; i++) {
            const v = top * i / 4, yy = y(v);
            g += `<line class="grid" x1="${L}" y1="${yy.toFixed(1)}" x2="${W - R}" y2="${yy.toFixed(1)}"></line>` +
                 `<text class="axis" x="${L - 8}" y="${(yy + 4).toFixed(1)}" text-anchor="end">${fmt(v, 0)}</text>`;
          }
          const ref = metric === 'cyclesPerHour' ? cap : 100;
          const refLabel = metric === 'cyclesPerHour' ? `Kapazität ${cap}/h` : '100 %';
          if (ref > 0 && ref <= top)
            g += `<line class="cap" x1="${L}" y1="${y(ref).toFixed(1)}" x2="${W - R}" y2="${y(ref).toFixed(1)}"></line>` +
                 `<text class="axis" x="${W - R}" y="${(y(ref) - 5).toFixed(1)}" text-anchor="end">${refLabel}</text>`;

          const span = (parseLocal(data.to) - parseLocal(data.from)) / 3600000;
          g += xLabels(b, x, W, HGT, span);

          const lines = keys.map(k =>
            `<path class="ln" d="${path(b, x, y, pt => pt[metric][k] ?? 0)}" stroke="${colorOf(k)}"></path>`).join('');

          svg.innerHTML = g + lines + overlay(L, T, w, h);
        }

        // ---- Ein Chart je Gerät: Auslastung + Leistung + Leerlauf ---------------------------------
        function minis() {
          const b = data.buckets;
          const span = (parseLocal(data.to) - parseLocal(data.from)) / 3600000;

          $('minis').innerHTML = data.totals.map(t => {
            const c = t.connection;
            const W = 500, HGT = 170, L = 34, R = 10, T = 10, B = 24;
            const w = W - L - R, h = HGT - T - B;
            const x = i => L + (b.length === 1 ? w / 2 : (i / (b.length - 1)) * w);
            const y = v => T + h - Math.min(v / 100, 1) * h;   // feste 0–100-%-Skala

            let g = '';
            for (let i = 0; i <= 2; i++) {
              const v = i * 50, yy = y(v);
              g += `<line class="grid" x1="${L}" y1="${yy.toFixed(1)}" x2="${W - R}" y2="${yy.toFixed(1)}"></line>` +
                   `<text class="axis" x="${L - 6}" y="${(yy + 4).toFixed(1)}" text-anchor="end">${v}</text>`;
            }

            let body = '';
            if (b.length > 1) {
              // Leerlauf = die Fläche zwischen Auslastungskurve und 100 % — was ungenutzt blieb.
              const idleArea = `M${x(0).toFixed(1)},${y(100).toFixed(1)} ` +
                b.map((pt, i) => `L${x(i).toFixed(1)},${y(pt.busyPercent[c] ?? 0).toFixed(1)}`).join(' ') +
                ` L${x(b.length - 1).toFixed(1)},${y(100).toFixed(1)} Z`;
              body =
                `<path d="${idleArea}" fill="${C_IDLE}" opacity=".45"></path>` +
                `<path class="ln" d="${path(b, x, y, pt => pt.busyPercent[c] ?? 0)}" stroke="${C_BUSY}"></path>` +
                `<path class="ln" d="${path(b, x, y, pt => pt.loadPercent[c] ?? 0)}" stroke="${C_LOAD}"></path>`;
            }

            return `
              <div class="mini">
                <div class="mh">
                  <span class="mn"><span class="sw" style="background:${colorOf(c)}"></span>${nameOf(c)}</span>
                  <span class="ms">
                    <span${help('busy')}>Auslastung <b style="color:${C_BUSY}">${fmt(t.avgBusyPercent)} %</b></span>
                    <span${help('load')}>Leistung <b style="color:${C_LOAD}">${fmt(t.avgLoadPercent)} %</b></span>
                    <span${help('idle')}>Leerlauf <b>${dur(t.idleHours)}</b></span>
                  </span>
                </div>
                <svg class="plot" data-c="${c}" viewBox="0 0 ${W} ${HGT}" preserveAspectRatio="none">
                  ${g}${xLabels(b, x, W, HGT, span)}${body}${overlay(L, T, w, h)}
                </svg>
              </div>`;
          }).join('');

          // Ränder nachtragen, damit Hover und Aufziehen auch hier rechnen können.
          for (const svg of $('minis').querySelectorAll('svg.plot')) {
            svg._W = 500; svg._L = 34; svg._R = 10;
          }
        }

        function table() {
          $('head').innerHTML =
            `<th>RBG</th><th${help('inout')}>Ein</th><th${help('inout')}>Aus</th>` +
            `<th${help('double')}>Doppelspiele</th><th${help('single')}>Einzelspiele</th>` +
            `<th${help('cycles')}>Spiele</th><th${help('share')}>Anteil</th>` +
            `<th${help('avgcycles')}>Ø Spiele/h</th><th${help('peak')}>Spitze</th>` +
            `<th${help('load')}>Ø Leistung</th><th${help('busy')}>Ø Auslastung</th>` +
            `<th${help('idle')}>Leerlauf</th><th${help('active')}>Aktive Std.</th>`;

          $('rows').innerHTML = data.totals.map(t => `
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
              <td>${dur(t.idleHours)}</td>
              <td>${fmt(t.activeHours)}</td>
            </tr>`).join('');
        }

        function render(d) {
          data = d;
          for (const c of [...hidden]) if (!d.connections.includes(c)) hidden.delete(c);
          verdict();
          bars();
          legend();
          chart();
          minis();
          table();

          $('zoomout').hidden = !zoom;
          const from = new Date(d.from), to = new Date(d.to);
          const sameDay = from.toDateString() === to.toDateString();
          const stamp = x => sameDay
            ? x.toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' })
            : x.toLocaleDateString('de-DE');
          $('meta').classList.remove('err');
          $('meta').textContent =
            `${from.toLocaleDateString('de-DE')} ${stamp(from)} – ${stamp(to)} · ` +
            `Raster ${d.bucketMinutes} min${zoom ? ' · gezoomt' : ''} · ` +
            `Stand ${new Date().toLocaleTimeString('de-DE')}`;
        }

        // ---- Hover und Zoom, gemeinsam für alle Charts --------------------------------------------
        function fracAtClientX(svg, clientX) {
          const box = svg.getBoundingClientRect();
          if (!box.width || !svg._W) return null;      // eingeklappt/unsichtbar — kein Bezug
          const vx = (clientX - box.left) / box.width * svg._W;
          return Math.min(1, Math.max(0, (vx - svg._L) / (svg._W - svg._L - svg._R)));
        }
        const timeAtFrac = f => {
          const a = parseLocal(data.from), b = parseLocal(data.to);
          return a + f * (b - a);
        };
        const clearCursors = () => {
          for (const c of document.querySelectorAll('svg.plot .cursor')) c.style.visibility = 'hidden';
        };

        document.addEventListener('mousedown', e => {
          const svg = e.target.closest?.('svg.plot');
          if (!svg || !data || !data.buckets.length) return;
          const f0 = fracAtClientX(svg, e.clientX);
          if (f0 === null) return;
          e.preventDefault();
          brush = { svg, f0 };
          $('tip').style.opacity = 0;
        });

        document.addEventListener('mouseup', e => {
          if (!brush) return;
          const { svg, f0 } = brush;
          const f1 = fracAtClientX(svg, e.clientX);
          brush = null;
          showSel(svg.querySelector('.sel'), false);
          if (f1 === null || Math.abs(f1 - f0) < 0.01) return;   // reiner Klick — kein Zoom
          zoom = {
            from: fmtLocal(timeAtFrac(Math.min(f0, f1))),
            to: fmtLocal(timeAtFrac(Math.max(f0, f1))),
          };
          load();
        });

        document.addEventListener('dblclick', e => {
          if (e.target.closest?.('svg.plot') && zoom) { zoom = null; load(); }
        });

        document.addEventListener('mousemove', e => {
          if (brush) {                                        // Bereich aufziehen
            const { svg, f0 } = brush;
            const plotL = svg._L, plotR = svg._W - svg._R;
            const f1 = fracAtClientX(svg, e.clientX);
            if (f1 === null) return;
            const sel = svg.querySelector('.sel');
            if (sel) {
              sel.setAttribute('x', (plotL + Math.min(f0, f1) * (plotR - plotL)).toFixed(1));
              sel.setAttribute('width', (Math.abs(f1 - f0) * (plotR - plotL)).toFixed(1));
              showSel(sel, true);
            }
            clearCursors();
            $('tip').style.opacity = 0;
            return;
          }

          const svg = e.target.classList?.contains('hit') ? e.target.closest('svg.plot') : null;
          clearCursors();
          if (!svg || !data || !data.buckets.length) { $('tip').style.opacity = 0; return; }

          const f = fracAtClientX(svg, e.clientX);
          if (f === null) { $('tip').style.opacity = 0; return; }
          const i = Math.round(f * (data.buckets.length - 1));
          const pt = data.buckets[i];
          if (!pt) return;

          const cursor = svg.querySelector('.cursor');
          if (cursor) {
            const cx = svg._L + f * (svg._W - svg._L - svg._R);
            cursor.setAttribute('x1', cx); cursor.setAttribute('x2', cx);
            cursor.style.visibility = 'visible';
          }

          const one = svg.dataset.c;
          const row = (label, value, col) =>
            `<div class="r"><span><span class="sw" style="display:inline-block;width:9px;height:9px;` +
            `border-radius:2px;background:${col};margin-right:6px"></span>${label}</span><b>${value}</b></div>`;

          let body;
          if (one) {
            body = row('Auslastung', `${fmt(pt.busyPercent[one])} %`, C_BUSY) +
                   row('Leistung', `${fmt(pt.loadPercent[one])} %`, C_LOAD) +
                   row('Leerlauf', `${fmt(pt.idleMinutes[one])} min`, C_IDLE) +
                   row('Spiele/h', fmt(pt.cyclesPerHour[one]), colorOf(one));
          } else {
            const keys = shown();
            if (!keys.length) return;
            body = keys.map(k => ({ k, v: pt[metric][k] ?? 0 }))
              .sort((a, b2) => b2.v - a.v)
              .map(r => row(r.k, `${fmt(r.v)} ${unitOf(metric)}`, colorOf(r.k)))
              .join('');
          }

          const tip = $('tip');
          tip.innerHTML = `<div class="h">${new Date(pt.at).toLocaleString('de-DE')}` +
            `${one ? ` · ${one}` : ''}</div>${body}`;
          tip.style.opacity = 1;
          tip.style.left = Math.min(window.innerWidth - 240, e.clientX + 14) + 'px';
          tip.style.top = Math.min(window.innerHeight - 120, e.clientY + 14) + 'px';
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
          zoom = null;
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

        $('zoomout').addEventListener('click', () => { zoom = null; load(); });

        // Raster: fein genug zum Erkennen, grob genug für lange Zeiträume. Feinstmöglich ist
        // die Rasterweite der Aufzeichnung (UphHistoryIntervalMinutes, Standard 5 min).
        const bucketFor = h => h <= 24 ? 5 : h <= 72 ? 15 : h <= 168 ? 30 : h <= 672 ? 240 : 1440;

        async function load() {
          const q = new URLSearchParams();
          if (zoom) {
            const span = (parseLocal(zoom.to) - parseLocal(zoom.from)) / 3600000;
            q.set('from', zoom.from);
            q.set('to', zoom.to);
            q.set('bucket', bucketFor(span));
          } else {
            q.set('hours', hours);
            q.set('bucket', bucketFor(hours));
          }
          try {
            const res = await fetch(`${API_BASE}/api/rbg-history?${q}`, { cache: 'no-store' });
            if (!res.ok) throw new Error('HTTP ' + res.status);
            render(await res.json());
          } catch (e) {
            $('meta').classList.add('err');
            $('meta').textContent = 'Fehler: ' + e.message;
          }
        }

        window.addEventListener('resize', () => { if (data) { chart(); minis(); } });
        load();
        setInterval(() => { if (!zoom) load(); }, 300000);
        </script>
        </body>
        </html>
        """;
}
