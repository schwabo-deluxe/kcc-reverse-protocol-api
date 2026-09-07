namespace Kcc.Recorder;

/// <summary>
/// Eingebettete Ansicht unter <c>/kontur</c>: pollt <c>/api/kontur</c> und zeigt, welche
/// Konturfehler an welchen Konturkontrollen auflaufen (aus dem <c>Status</c>-Feld <c>Kxyz</c>).
/// Auf schmale Hochformat-Bildschirme ausgelegt: alles untereinander, keine breite Kreuztabelle.
/// </summary>
public static class ContourDashboard
{
    public const string Html = """
        <!doctype html>
        <html lang="de">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Auswertung der Konturkontrollen</title>
        <style>
          :root { color-scheme: dark; }
          * { box-sizing: border-box; }
          body { margin: 0; font: 14px/1.45 system-ui, sans-serif; background: #14171c; color: #e6e6e6; }
          header { padding: 14px 16px; border-bottom: 1px solid #2a2f37; display: flex; gap: 10px 14px; align-items: center; flex-wrap: wrap; }
          header h1 { font-size: 16px; margin: 0 4px 0 0; font-weight: 600; }
          .ranges { display: flex; gap: 4px; flex-wrap: wrap; }
          .ranges button { background: #10141a; color: #9aa4b2; border: 1px solid #2a2f37; border-radius: 6px; padding: 5px 10px; font: inherit; cursor: pointer; }
          .ranges button.on { background: #1f6feb; border-color: #1f6feb; color: #fff; }
          .meta { margin-left: auto; color: #9aa4b2; font-size: 12px; }
          .meta.err { color: #ff6b6b; }
          main { padding: 16px; max-width: 900px; margin: 0 auto; }
          .kpis { display: grid; grid-template-columns: repeat(auto-fit, minmax(140px, 1fr)); gap: 10px; margin-bottom: 16px; }
          .kpi { background: #1c2128; border: 1px solid #2a2f37; border-radius: 8px; padding: 12px 14px; }
          .kpi .cap { color: #9aa4b2; font-size: 11px; text-transform: uppercase; letter-spacing: .04em; }
          .kpi .val { font-size: clamp(20px, 6vw, 24px); font-weight: 700; margin-top: 4px; font-variant-numeric: tabular-nums; }
          .card { background: #1c2128; border: 1px solid #2a2f37; border-radius: 8px; padding: 14px; margin-bottom: 16px; }
          .card h2 { font-size: 13px; margin: 0 0 12px; font-weight: 600; color: #cdd6e0; text-transform: uppercase; letter-spacing: .04em; }
          .bars { display: flex; flex-direction: column; gap: 7px; }
          .bar { display: grid; grid-template-columns: minmax(96px, 34%) 1fr auto; gap: 8px; align-items: center; font-size: 13px; }
          .bar .name { color: #cdd6e0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
          .bar .track { background: #10141a; border-radius: 4px; height: 15px; overflow: hidden; }
          .bar .fill { height: 100%; background: #f0883e; }
          .bar .num { text-align: right; color: #9aa4b2; font-variant-numeric: tabular-nums; font-size: 12px; white-space: nowrap; }
          .scroll { overflow-x: auto; }
          table { border-collapse: collapse; font-size: 12px; width: 100%; }
          th, td { padding: 6px 9px; border-bottom: 1px solid #2a2f37; text-align: right; font-variant-numeric: tabular-nums; white-space: nowrap; }
          th:first-child, td:first-child { text-align: left; }
          th { color: #9aa4b2; text-transform: uppercase; letter-spacing: .03em; font-weight: 600; }
          tbody tr:last-child td { border-bottom: 0; }
          tr.sum td { font-weight: 700; background: #171b21; }
          td.hit { color: #fff; font-weight: 600; }
          .muted { color: #4a515c; }
          .q-ok { color: #5ccb7e; } .q-warn { color: #ffb454; } .q-bad { color: #ff6b6b; }
          .cp-block { margin-bottom: 14px; }
          .cp-block:last-child { margin-bottom: 0; }
          .cp-block h3 { font-size: 12px; margin: 0 0 6px; font-weight: 600; color: #cdd6e0; }
          table.recent td.flags { white-space: normal; color: #ffb454; }
          table.recent td.le { color: #cdd6e0; }
          @media (max-width: 560px) {
            main { padding: 12px; }
            .card { padding: 12px; }
            .bar { grid-template-columns: minmax(84px, 40%) 1fr auto; font-size: 12px; }
            th, td { padding: 6px 7px; }
          }
        </style>
        </head>
        <body>
        <header>
          <h1>Konturkontrollen</h1>
          <div class="ranges" id="ranges">
            <button data-m="60">1 h</button>
            <button data-m="480" class="on">8 h</button>
            <button data-m="1440">24 h</button>
            <button data-m="4320">3 T</button>
            <button data-m="10080">7 T</button>
          </div>
          <div class="meta" id="meta">lädt …</div>
        </header>
        <main>
          <div class="kpis" id="kpis"></div>
          <div class="card">
            <h2>Je Kontrollpunkt</h2>
            <div class="scroll">
              <table>
                <thead><tr><th>Kontrollpunkt</th><th>Geprüft</th><th>Fehler</th><th>Quote</th></tr></thead>
                <tbody id="cprows"></tbody>
              </table>
            </div>
          </div>
          <div class="card">
            <h2>Konturfehler nach Art</h2>
            <div class="bars" id="bars"></div>
          </div>
          <div class="card">
            <h2>Fehlerart je Kontrollpunkt</h2>
            <div class="scroll">
              <table>
                <thead><tr id="head"></tr></thead>
                <tbody id="rows"></tbody>
              </table>
            </div>
          </div>
          <div class="card">
            <h2>Letzte Fehler je Kontrollpunkt</h2>
            <div id="recent"></div>
          </div>
        </main>
        <script>
        const $ = id => document.getElementById(id);
        const fmt = n => n.toLocaleString('de-DE', { maximumFractionDigits: 1 });
        const num = n => n.toLocaleString('de-DE');
        const esc = s => String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

        const API_BASE = (new URLSearchParams(location.search).get('api')
          || (/^https?:$/.test(location.protocol) ? location.origin : 'http://localhost:8080'))
          .replace(/\/+$/, '');

        let minutes = 480;

        const qClass = r => r >= 5 ? 'q-bad' : r >= 1 ? 'q-warn' : 'q-ok';

        function render(d) {
          $('kpis').innerHTML = `
            <div class="kpi"><div class="cap">Geprüft</div><div class="val">${num(d.total)}</div></div>
            <div class="kpi"><div class="cap">Mit Konturfehler</div><div class="val">${num(d.errors)}</div></div>
            <div class="kpi"><div class="cap">Fehlerquote</div><div class="val ${qClass(d.errorRate)}">${fmt(d.errorRate)} %</div></div>`
            + (d.unreadable ? `<div class="kpi"><div class="cap">Status unlesbar</div><div class="val">${num(d.unreadable)}</div></div>` : '');

          // Je Kontrollpunkt: kurze Tabelle (passt auf jede Breite).
          $('cprows').innerHTML = d.checkpoints.map(c => `
            <tr>
              <td title="${esc(c.resourcePoint)} ${esc(c.messageCode)}">${esc(c.label)}</td>
              <td>${num(c.total)}</td>
              <td>${num(c.errors)}</td>
              <td class="${qClass(c.errorRate)}">${fmt(c.errorRate)} %</td>
            </tr>`).join('')
            + `<tr class="sum"><td>Summe</td><td>${num(d.total)}</td><td>${num(d.errors)}</td>`
            + `<td class="${qClass(d.errorRate)}">${fmt(d.errorRate)} %</td></tr>`;

          // Balken je Fehlerart (bereits absteigend sortiert).
          const peak = Math.max(1, ...d.flags.map(f => f.count));
          $('bars').innerHTML = d.errors
            ? d.flags.map(f => `
                <div class="bar">
                  <span class="name" title="${esc(f.label)}">${esc(f.label)}</span>
                  <span class="track"><span class="fill" style="width:${(f.count / peak * 100).toFixed(1)}%"></span></span>
                  <span class="num">${num(f.count)} · ${fmt(f.percent)} %</span>
                </div>`).join('')
            : '<div class="muted">keine Konturfehler im Zeitraum</div>';

          // Kreuztabelle hochkant: Zeilen = Fehlerart, Spalten = Kontrollpunkt (Kürzel), plus Summe.
          $('head').innerHTML = ['Fehlerart', ...d.checkpoints.map(c =>
            `<abbr title="${esc(c.label)}">${esc(c.resourcePoint)}</abbr>`), 'Σ']
            .map(h => `<th>${h}</th>`).join('');

          const cellPeak = Math.max(1, ...d.flags.flatMap(f =>
            d.checkpoints.map(c => c.flags[f.label] || 0)));
          $('rows').innerHTML = d.flags.map(f => {
            const cells = d.checkpoints.map(c => {
              const v = c.flags[f.label] || 0;
              if (!v) return '<td class="muted">·</td>';
              const a = 0.12 + 0.5 * (v / cellPeak);
              return `<td class="hit" style="background:rgba(240,136,62,${a.toFixed(2)})">${v}</td>`;
            }).join('');
            return `<tr><td title="${esc(f.label)}">${esc(f.label)}</td>${cells}<td>${num(f.count)}</td></tr>`;
          }).join('')
            + `<tr class="sum"><td>Σ mit Fehler</td>`
            + d.checkpoints.map(c => `<td>${num(c.errors)}</td>`).join('')
            + `<td>${num(d.errors)}</td></tr>`;

          // Detailliste: die letzten 10 Fehler je Kontrollpunkt.
          const ts = s => new Date(s).toLocaleString('de-DE',
            { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit' });
          $('recent').innerHTML = d.checkpoints.map(c => `
            <div class="cp-block">
              <h3>${esc(c.label)}</h3>
              ${c.recent && c.recent.length ? `
              <div class="scroll"><table class="recent">
                <thead><tr><th>Zeit</th><th>LE / ID</th><th>Aufgelöste Fehler</th></tr></thead>
                <tbody>${c.recent.map(e => `
                  <tr>
                    <td>${ts(e.at)}</td>
                    <td class="le">${esc(e.loadUnit) || '–'}</td>
                    <td class="flags">${e.flags.map(esc).join(', ')}</td>
                  </tr>`).join('')}</tbody>
              </table></div>` : '<div class="muted">keine Fehler im Zeitraum</div>'}
            </div>`).join('');

          const from = new Date(d.from), to = new Date(d.to);
          $('meta').classList.remove('err');
          $('meta').textContent = `${from.toLocaleString('de-DE')} – ${to.toLocaleString('de-DE')} · ${d.windowMinutes} min`;
        }

        async function load() {
          try {
            const res = await fetch(API_BASE + '/api/kontur?minutes=' + minutes, { cache: 'no-store' });
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
          minutes = parseInt(btn.dataset.m, 10);
          for (const b of $('ranges').children) b.classList.toggle('on', b === btn);
          load();
        });
        load();
        setInterval(load, 60000);
        </script>
        </body>
        </html>
        """;
}
