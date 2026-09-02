namespace Kcc.Recorder;

/// <summary>Eingebettetes Ein-Datei-Dashboard, das die Lese-API unter <c>/</c> ausliefert.</summary>
public static class Dashboard
{
    public const string Html = """
        <!doctype html>
        <html lang="de">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>kcc Dashboard</title>
        <style>
          :root { color-scheme: dark; }
          * { box-sizing: border-box; }
          body { margin: 0; font: 14px/1.4 system-ui, sans-serif; background: #14171c; color: #e6e6e6; }
          header { padding: 16px 20px; border-bottom: 1px solid #2a2f37; display: flex; gap: 16px; align-items: baseline; flex-wrap: wrap; }
          header h1 { font-size: 16px; margin: 0; font-weight: 600; }
          header .meta { color: #9aa4b2; font-size: 12px; }
          main { padding: 20px; max-width: 1100px; margin: 0 auto; }
          .tiles { display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: 12px; }
          .tile { background: #1c2128; border: 1px solid #2a2f37; border-radius: 8px; padding: 14px 16px; }
          .tile .label { color: #9aa4b2; font-size: 12px; text-transform: uppercase; letter-spacing: .04em; }
          .tile .value { font-size: 26px; font-weight: 600; margin-top: 6px; }
          .tile.warn .value { color: #ffb454; }
          .tile.bad .value { color: #ff6b6b; }
          .grids { display: grid; grid-template-columns: repeat(auto-fit, minmax(260px, 1fr)); gap: 16px; margin-top: 20px; }
          .grids section { background: #1c2128; border: 1px solid #2a2f37; border-radius: 8px; padding: 14px 16px; }
          .grids h2 { font-size: 13px; margin: 0 0 10px; color: #9aa4b2; text-transform: uppercase; letter-spacing: .04em; }
          table { width: 100%; border-collapse: collapse; }
          td { padding: 4px 0; border-bottom: 1px solid #262b33; }
          td.n { text-align: right; font-variant-numeric: tabular-nums; color: #cdd6e0; }
          tr:last-child td { border-bottom: 0; }
          .err { color: #ff6b6b; }
          #status { color: #9aa4b2; font-size: 12px; }
        </style>
        </head>
        <body>
        <header>
          <h1>kcc Dashboard</h1>
          <span class="meta" id="window"></span>
          <span class="meta" id="status">lade …</span>
        </header>
        <main>
          <div class="tiles" id="tiles"></div>
          <div class="grids">
            <section><h2>Richtung</h2><table id="byDirection"></table></section>
            <section><h2>Verbindungen</h2><table id="byConnection"></table></section>
            <section><h2>MessageCode</h2><table id="byMessageCode"></table></section>
          </div>
        </main>
        <script>
          const MINUTES = 60;
          const REFRESH_MS = 60_000;

          const tile = (label, value, cls = "") =>
            `<div class="tile ${cls}"><div class="label">${label}</div><div class="value">${value}</div></div>`;

          const rows = (obj) => {
            const entries = Object.entries(obj || {});
            if (!entries.length) return `<tr><td>–</td><td class="n">0</td></tr>`;
            return entries.map(([k, v]) => `<tr><td>${k}</td><td class="n">${v}</td></tr>`).join("");
          };

          async function refresh() {
            try {
              const r = await fetch(`/api/kpis?minutes=${MINUTES}`, { cache: "no-store" });
              if (!r.ok) throw new Error(`HTTP ${r.status}`);
              const k = await r.json();

              document.getElementById("window").textContent =
                `Fenster: letzte ${k.windowMinutes} min`;
              document.getElementById("status").textContent =
                `aktualisiert ${new Date().toLocaleTimeString()}`;

              const lag = k.lagSeconds == null ? "–" : `${k.lagSeconds}s`;
              const lagCls = k.lagSeconds == null ? "" : k.lagSeconds > 120 ? "bad" : k.lagSeconds > 30 ? "warn" : "";
              document.getElementById("tiles").innerHTML = [
                tile("Telegramme", k.count),
                tile("pro Minute", k.perMinute),
                tile("Fehler", k.errors, k.errors > 0 ? "bad" : ""),
                tile("Lag", lag, lagCls),
                tile("Verbindungen", k.distinctConnections),
                tile("letzte Id", k.latestId ?? "–"),
              ].join("");

              document.getElementById("byDirection").innerHTML = rows(k.byDirection);
              document.getElementById("byConnection").innerHTML = rows(k.byConnection);
              document.getElementById("byMessageCode").innerHTML = rows(k.byMessageCode);
            } catch (e) {
              document.getElementById("status").innerHTML =
                `<span class="err">Fehler: ${e.message}</span>`;
            }
          }

          refresh();
          setInterval(refresh, REFRESH_MS);
        </script>
        </body>
        </html>
        """;
}
