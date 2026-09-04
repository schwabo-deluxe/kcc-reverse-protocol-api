namespace Kcc.Recorder;

/// <summary>
/// Eingebettete Ansicht unter <c>/kontur</c>: pollt <c>/api/kontur</c> und zeigt, welche
/// Konturfehler an welchen Konturkontrollen auflaufen (aus dem <c>Status</c>-Feld <c>Kxyz</c>).
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
          header { padding: 16px 20px; border-bottom: 1px solid #2a2f37; display: flex; gap: 14px; align-items: center; flex-wrap: wrap; }
          header h1 { font-size: 16px; margin: 0 8px 0 0; font-weight: 600; }
          .ranges { display: flex; gap: 4px; }
          .ranges button { background: #10141a; color: #9aa4b2; border: 1px solid #2a2f37; border-radius: 6px; padding: 5px 10px; font: inherit; cursor: pointer; }
          .ranges button.on { background: #1f6feb; border-color: #1f6feb; color: #fff; }
          .meta { margin-left: auto; color: #9aa4b2; font-size: 12px; }
          .meta.err { color: #ff6b6b; }
          main { padding: 20px; max-width: 1180px; margin: 0 auto; }
          .kpis { display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 12px; margin-bottom: 18px; }
          .kpi { background: #1c2128; border: 1px solid #2a2f37; border-radius: 8px; padding: 14px 16px; }
          .kpi .cap { color: #9aa4b2; font-size: 12px; text-transform: uppercase; letter-spacing: .04em; }
          .kpi .val { font-size: 24px; font-weight: 700; margin-top: 4px; font-variant-numeric: tabular-nums; }
          .card { background: #1c2128; border: 1px solid #2a2f37; border-radius: 8px; padding: 16px; margin-bottom: 18px; }
          .card h2 { font-size: 13px; margin: 0 0 14px; font-weight: 600; color: #cdd6e0; text-transform: uppercase; letter-spacing: .04em; }
          .bars { display: flex; flex-direction: column; gap: 8px; }
          .bar { display: grid; grid-template-columns: 190px 1fr 96px; gap: 10px; align-items: center; font-size: 13px; }
          .bar .name { color: #cdd6e0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
          .bar .track { background: #10141a; border-radius: 4px; height: 16px; overflow: hidden; }
          .bar .fill { height: 100%; background: #f0883e; }
          .bar .num { text-align: right; color: #9aa4b2; font-variant-numeric: tabular-nums; }
          .scroll { overflow-x: auto; }
          table { border-collapse: collapse; font-size: 12px; min-width: 100%; }
          th, td { padding: 7px 10px; border-bottom: 1px solid #2a2f37; text-align: right; font-variant-numeric: tabular-nums; white-space: nowrap; }
          th:first-child, td:first-child { text-align: left; position: sticky; left: 0; background: #1c2128; }
          th { color: #9aa4b2; text-transform: uppercase; letter-spacing: .03em; font-weight: 600; }
          tbody tr:last-child td { border-bottom: 0; }
          tr.sum td { font-weight: 700; background: #171b21; }
          tr.sum td:first-child { background: #171b21; }
          td.hit { color: #fff; font-weight: 600; }
          .muted { color: #4a515c; }
          .q-ok { color: #5ccb7e; } .q-warn { color: #ffb454; } .q-bad { color: #ff6b6b; }
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
            <h2>Konturfehler nach Art</h2>
            <div class="bars" id="bars"></div>
          </div>
          <div class="card">
            <h2>Fehler je Kontrollpunkt</h2>
            <div class="scroll">
              <table>
                <thead><tr id="head"></tr></thead>
                <tbody id="rows"></tbody>
              </table>
            </div>
          </div>
        </main>
        <script>
        const $ = id => document.getElementById(id);
        const fmt = n => n.toLocaleString('de-DE', { maximumFractionDigits: 1 });
        const esc = s => String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

        const API_BASE = (new URLSearchParams(location.search).get('api')
          || (/^https?:$/.test(location.protocol) ? location.origin : 'http://localhost:8080'))
          .replace(/\/+$/, '');

        let minutes = 480;

        const qClass = r => r >= 5 ? 'q-bad' : r >= 1 ? 'q-warn' : 'q-ok';

        function render(d) {
          $('kpis').innerHTML = `
            <div class="kpi"><div class="cap">Geprüft</div><div class="val">${d.total.toLocaleString('de-DE')}</div></div>
            <div class="kpi"><div class="cap">Mit Konturfehler</div><div class="val">${d.errors.toLocaleString('de-DE')}</div></div>
            <div class="kpi"><div class="cap">Fehlerquote</div><div class="val ${qClass(d.errorRate)}">${fmt(d.errorRate)} %</div></div>`
            + (d.unreadable ? `<div class="kpi"><div class="cap">Status unlesbar</div><div class="val">${d.unreadable.toLocaleString('de-DE')}</div></div>` : '');

          const peak = Math.max(1, ...d.flags.map(f => f.count));
          $('bars').innerHTML = d.flags.length
            ? d.flags.map(f => `
                <div class="bar">
                  <span class="name" title="${esc(f.label)}">${esc(f.label)}</span>
                  <span class="track"><span class="fill" style="width:${(f.count / peak * 100).toFixed(1)}%"></span></span>
                  <span class="num">${f.count.toLocaleString('de-DE')} · ${fmt(f.percent)} %</span>
                </div>`).join('')
            : '<div class="muted">keine Konturfehler im Zeitraum</div>';

          $('head').innerHTML = ['Kontrollpunkt', ...d.flagLabels.map(esc), 'Geprüft', 'Fehler', 'Quote']
            .map(h => `<th>${h}</th>`).join('');

          const cellPeak = Math.max(1, ...d.checkpoints.flatMap(c => d.flagLabels.map(l => c.flags[l] || 0)));
          const row = c => {
            const cells = d.flagLabels.map(l => {
              const v = c.flags[l] || 0;
              if (!v) return '<td class="muted">·</td>';
              const a = 0.12 + 0.5 * (v / cellPeak);
              return `<td class="hit" style="background:rgba(240,136,62,${a.toFixed(2)})">${v}</td>`;
            }).join('');
            return `<tr>
              <td>${esc(c.label)}</td>${cells}
              <td>${c.total.toLocaleString('de-DE')}</td>
              <td>${c.errors.toLocaleString('de-DE')}</td>
              <td class="${qClass(c.errorRate)}">${fmt(c.errorRate)} %</td>
            </tr>`;
          };
          const sums = d.flagLabels.map(l =>
            `<td>${d.checkpoints.reduce((a, c) => a + (c.flags[l] || 0), 0).toLocaleString('de-DE')}</td>`).join('');
          $('rows').innerHTML = d.checkpoints.map(row).join('')
            + `<tr class="sum"><td>Summe</td>${sums}<td>${d.total.toLocaleString('de-DE')}</td>`
            + `<td>${d.errors.toLocaleString('de-DE')}</td><td class="${qClass(d.errorRate)}">${fmt(d.errorRate)} %</td></tr>`;

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
