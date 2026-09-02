namespace Kcc.Recorder;

/// <summary>
/// Eingebettete Auslastungsansicht unter <c>/auslastung</c>: pollt <c>/api/utilization</c> und
/// zeigt je Ressourcenpunkt UPH/h und den Anteil am Richtwert.
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
          .tiles { display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 12px; }
          .tile { background: #1c2128; border: 1px solid #2a2f37; border-radius: 8px; padding: 14px 16px; }
          .tile .label { color: #9aa4b2; font-size: 12px; text-transform: uppercase; letter-spacing: .04em; }
          .tile .value { font-size: 26px; font-weight: 600; margin-top: 6px; }
          .tile .sub { color: #9aa4b2; font-size: 12px; margin-top: 4px; }
          .bar { height: 6px; background: #10141a; border-radius: 99px; margin-top: 10px; overflow: hidden; }
          .bar > i { display: block; height: 100%; border-radius: 99px; }
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
          <label>Fenster (min) <input type="number" id="minutes" value="60" min="1" max="1440"></label>
          <label>Richtwert (UPH) <input type="number" id="target" value="200" min="1"></label>
          <div class="meta" id="meta">lädt …</div>
        </header>
        <main>
          <div class="tiles" id="tiles"></div>
          <table>
            <thead><tr>
              <th>Ressourcenpunkt</th><th>TSPORD</th><th>UPH/h</th>
              <th>% vom Richtwert</th><th>Fehler</th><th>Letztes Telegramm</th>
            </tr></thead>
            <tbody id="rows"></tbody>
          </table>
        </main>
        <script>
        const $ = id => document.getElementById(id);
        const fmt = n => n.toLocaleString('de-DE', { maximumFractionDigits: 1 });

        function color(pct) {
          if (pct >= 95) return '#ff6b6b';
          if (pct >= 80) return '#ffb454';
          return '#5ccb7e';
        }

        function render(data) {
          $('target').value = data.targetUph;
          $('tiles').innerHTML = data.points.map(p => `
            <div class="tile">
              <div class="label">${p.resourcePoint}</div>
              <div class="value" style="color:${color(p.percent)}">${fmt(p.percent)} %</div>
              <div class="sub">${fmt(p.uph)} UPH/h · ${p.count} Telegramme</div>
              <div class="bar"><i style="width:${Math.min(100, p.percent)}%;background:${color(p.percent)}"></i></div>
            </div>`).join('');

          $('rows').innerHTML = data.points.map(p => `
            <tr>
              <td>${p.resourcePoint}</td>
              <td>${p.count}</td>
              <td>${fmt(p.uph)}</td>
              <td style="color:${color(p.percent)}">${fmt(p.percent)} %</td>
              <td class="${p.errors ? 'err' : ''}">${p.errors}</td>
              <td>${p.latestAt ? new Date(p.latestAt).toLocaleTimeString('de-DE') : '–'}</td>
            </tr>`).join('');

          $('meta').classList.remove('err');
          $('meta').textContent =
            `${data.totalOrders} TSPORD in ${data.windowMinutes} min · Stand ${new Date().toLocaleTimeString('de-DE')}`;
        }

        async function load() {
          const minutes = Math.min(1440, Math.max(1, parseInt($('minutes').value, 10) || 60));
          const target = Math.max(1, parseFloat($('target').value) || 200);
          try {
            const res = await fetch(`api/utilization?minutes=${minutes}&target=${target}`, { cache: 'no-store' });
            if (!res.ok) throw new Error('HTTP ' + res.status);
            render(await res.json());
          } catch (e) {
            $('meta').classList.add('err');
            $('meta').textContent = 'Fehler: ' + e.message;
          }
        }

        for (const id of ['minutes', 'target']) $(id).addEventListener('change', load);
        load();
        setInterval(load, 60000);
        </script>
        </body>
        </html>
        """;
}
