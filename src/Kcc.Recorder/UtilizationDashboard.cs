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
          .tiles { display: grid; grid-template-columns: repeat(auto-fit, minmax(360px, 1fr)); gap: 12px; }
          .tile { background: #1c2128; border: 1px solid #2a2f37; border-radius: 8px; padding: 14px 16px; }
          .tile .label { color: #9aa4b2; font-size: 12px; text-transform: uppercase; letter-spacing: .04em; }
          .tile .label .code { color: #7a8494; font-weight: 400; }
          .tile .sub { color: #9aa4b2; font-size: 12px; }
          .tile-head { display: flex; justify-content: space-between; align-items: baseline; gap: 8px; }
          /* Tacho links, Verlauf rechts, auf gleicher Höhe. */
          .tile-body { display: flex; gap: 12px; align-items: center; margin-top: 8px; }
          .rbg [title], [title] { cursor: help; }
          .rbg { margin-top: 8px; padding-top: 8px; border-top: 1px solid #232830; font-size: 12px; color: #9aa4b2; display: flex; flex-wrap: wrap; gap: 3px 12px; }
          .rbg b { color: #e6e6e6; font-weight: 600; }
          .gauge-col { flex: 0 0 auto; text-align: center; }
          /* RBG-Kacheln: zwei Tachos nebeneinander — Zeitseite und Mengenseite. */
          .duo { display: flex; gap: 10px; flex: 0 0 auto; }
          .duo .gauge { width: 104px; height: 54px; }
          .duo .pct { font-size: 15px; }
          .gcap { font-size: 12px; color: #e6e6e6; text-transform: uppercase; letter-spacing: .05em; margin-top: 4px; }
          .gsub { font-size: 12px; color: #e6e6e6; font-variant-numeric: tabular-nums; white-space: nowrap; }
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
          /* Einheit der Kurve — bei RBG-Kacheln samt Verbindung, damit klar ist, woher sie stammt. */
          .axis .unit { color: #9aa4b2; }
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
          #tip .win { color: #7a8494; }
          table { width: 100%; border-collapse: collapse; margin-top: 22px; background: #1c2128; border: 1px solid #2a2f37; border-radius: 8px; overflow: hidden; }
          th, td { padding: 8px 12px; text-align: right; border-bottom: 1px solid #2a2f37; font-variant-numeric: tabular-nums; }
          th:first-child, td:first-child { text-align: left; }
          th { color: #9aa4b2; font-size: 12px; text-transform: uppercase; letter-spacing: .04em; font-weight: 600; }
          tbody tr:last-child td { border-bottom: 0; }
          td.err { color: #ff6b6b; }
        </style>
        </head>
        <body>
        <!--nav-->
        <!--rbghelp-->
        <header>
          <h1>Auslastung (TSPORD)</h1>
          <label>Fenster (min) <input type="number" id="minutes" min="1" max="1440"></label>
          <label>Richtwert (UPH) <input type="number" id="target" value="200" min="1" title="Vorgabe für Punkte ohne eigenen Richtwert (TargetUph in appsettings.json)"></label>
          <label>Glättung (min) <input type="number" id="bucket" value="10" min="1" max="120" title="Breite des gleitenden Fensters der Verlaufskurve"></label>
          <label>UPH aus (min) <input type="number" id="rate" value="5" min="1" max="240"></label>
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

        // Erklärtexte der RBG-Kennzahlen (serverseitig eingesetzt, siehe RbgGlossary).
        const H = window.RBG_HELP || {};
        const help = k => H[k] ? ` title="${String(H[k]).replace(/"/g, '&quot;')}"` : '';

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
        function tweenGauges(ms) {
          const items = [...document.querySelectorAll('svg.gauge[data-g-value]')].map(el => {
            const key = el.dataset.gKey;
            const to = parseFloat(el.dataset.gValue) || 0;
            return {
              el, key, to,
              max: parseFloat(el.dataset.gMax) || 100,
              from: key && gPrev.has(key) ? gPrev.get(key) : to,
              txt: key ? document.querySelector(`[data-g-txt="${key}"]`) : null,
            };
          });
          if (!items.length) return;

          const paint = k => {
            const e = 1 - Math.pow(1 - k, 3);            // sanft auslaufen
            for (const it of items) {
              const v = it.from + (it.to - it.from) * e;
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
          if (!items.some(it => Math.abs(it.to - it.from) > 0.05)) { paint(1); remember(); return; }

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
          const w = 240, h = 54, s = point.rbg ? point.rbg.series : point.series;
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

          // Fördertechnik: Belegung, Transport- und Wartezeit aus TSPORD/ENDTSP/RPFREE.
          const convRow = p => {
            const c = p.conveyor;
            if (!c) return '';
            return `<div class="rbg">
              <span${help('cbusy')}>Belegung <b style="color:${color(c.busyPercent)}">${fmt(c.busyPercent)} %</b></span>
              <span${help('ccount')}>Ankunft/Auftrag/Frei <b>${c.completed}/${c.orders}/${c.freeSignals}</b></span>
              <span${help('coccupied')}>Ø belegt <b>${dur(c.avgOccupiedSeconds)}</b></span>
              <span${help('corderwait')}>Ø bis Auftrag <b>${dur(c.avgOrderWaitSeconds)}</b></span>
              <span${help('cdepart')}>Ø Abtransport <b>${dur(c.avgDepartSeconds)}</b></span>
              <span${help('cwait')}>Ø leer <b>${dur(c.avgIdleSeconds)}</b></span>
              <span${help('cidle')}>Leer gesamt <b>${dur(c.idleSeconds)}</b></span>
            </div>`;
          };

          const rbgRow = p => {
            const r = p.rbg;
            if (!r) return '';
            return `<div class="rbg">
              <span${help('busy')}>Auslastung <b style="color:${color(r.busyPercent)}">${fmt(r.busyPercent)} %</b></span>
              <span${help('load')}>Leistung <b style="color:${color(r.percent)}">${fmt(r.percent)} %</b> von ${r.maxCyclesPerHour}/h</span>
              <span${help('double')}>Doppelspiele <b>${r.doubleCycles}</b></span>
              <span${help('single')}>Einzelspiele <b>${r.singleCycles}</b></span>
              <span${help('inout')}>Ein/Aus <b>${r.puts}/${r.fetches}</b></span>
              <span${help('idle')}>Leerlauf <b>${dur(r.idleSeconds)}</b></span>
              <span${help('avgdur')}>Ø Dauer ein <b>${dur(r.avgPutSeconds)}</b> / aus <b>${dur(r.avgFetchSeconds)}</b></span>
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
            const sMax = r
              ? Math.max(r.maxCyclesPerHour * 1.3, 1, ...r.series.map(b => b.uph))
              : peak;
            const sTarget = r ? r.maxCyclesPerHour : data.targetUph;
            // Ø: Mittel über das ganze Fenster — die Kurve zeigt dagegen den gleitenden
            // Kurzzeitwert, der deutlich darüber liegen kann.
            const head = r
              ? `Ø ${fmt(r.cyclesPerHour)} / ${r.maxCyclesPerHour} Spiele/h · ${p.count} TSPORD`
              : `${fmt(p.uph)} / ${fmt(p.targetUph)} UPH · ${p.rateCount}/${data.rateMinutes}m · ${p.count} ges.`;
            const c = p.conveyor;
            const dials = r
              ? `<div class="duo">
                   ${dial(r.busyPercent, 'Auslastung', `Leerlauf ${dur(r.idleSeconds)}`, 'busy', `${p.resourcePoint}:busy`)}
                   ${dial(r.percent, 'Leistung', `Ø ${fmt(r.cyclesPerHour)} / ${r.maxCyclesPerHour} Spiele/h`, 'load', `${p.resourcePoint}:load`)}
                 </div>`
              : c
              ? `<div class="duo">
                   ${dial(c.busyPercent, 'Belegung', `Ø belegt ${dur(c.avgOccupiedSeconds)}`, 'cbusy', `${p.resourcePoint}:busy`)}
                   ${dial(p.percent, 'Leistung', `${fmt(p.uph)} / ${fmt(p.targetUph)} UPH`, 'load', `${p.resourcePoint}:uph`)}
                 </div>`
              : dial(p.percent, '% vom Richtwert', '', null, `${p.resourcePoint}:uph`);
            return `
            <div class="tile">
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
            </div>`;
          };

          // Kacheln nach Gruppe gebündelt. Reine RBG-Gruppen werden in Spielen summiert,
          // nicht in UPH — sonst stünde neben dem Spiele-Tacho eine TSPORD-Zahl.
          const grpSum = g => {
            const members = g.points.map(n => byName(n)).filter(Boolean);
            if (members.length && members.every(p => p.rbg)) {
              const cph = members.reduce((a, p) => a + p.rbg.cyclesPerHour, 0);
              const max = members.reduce((a, p) => a + p.rbg.maxCyclesPerHour, 0);
              return `Ø ${fmt(g.percent)} % · ${fmt(cph)} / ${max} Spiele/h · ${g.count} TSPORD`;
            }
            return `Ø ${fmt(g.percent)} % · ${fmt(g.uph)} / ${fmt(g.targetUph)} UPH · ` +
              `${g.rateCount}/${data.rateMinutes}m · ${g.count} ges.`;
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

          tweenGauges(3000);

          $('meta').classList.remove('err');
          $('meta').textContent =
            `${data.totalOrders} TSPORD in ${data.windowMinutes} min · Verlauf gleitend ${data.bucketMinutes} min · UPH aus ${data.rateMinutes} min` +
            ` · Stand ${new Date().toLocaleTimeString('de-DE')}`;
        }

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
        // Häufiger abrufen: kleinere Schritte je Aktualisierung, die Überblendung macht daraus
        // eine fortlaufende Bewegung statt eines Sprungs pro Minute.
        setInterval(load, 20000);
        </script>
        </body>
        </html>
        """;
}
