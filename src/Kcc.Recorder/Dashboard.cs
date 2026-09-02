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
          .hero { display: flex; justify-content: center; margin-bottom: 16px; }
          .hero .card { background: #1c2128; border: 1px solid #2a2f37; border-radius: 8px; padding: 12px 24px 6px; text-align: center; }
          .hero .cap { color: #9aa4b2; font-size: 12px; text-transform: uppercase; letter-spacing: .04em; }
          .hero .pct { font-weight: 700; font-size: 22px; line-height: 1; margin-top: 2px; }
          .gauge { display: block; width: 176px; height: 94px; margin: 6px auto 0; overflow: visible; }
          .gauge .track { stroke: #2a2f37; }
          .gauge .tick { stroke: #cdd6e0; }
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
          <div class="hero" id="hero"></div>
          <div class="tiles" id="tiles"></div>
          <div class="grids">
            <section><h2>Richtung</h2><table id="byDirection"></table></section>
            <section><h2>Verbindungen</h2><table id="byConnection"></table></section>
            <section><h2>MessageCode</h2><table id="byMessageCode"></table></section>
          </div>
        </main>
        <script>
          const REFRESH_MS = 60_000;

          // Über die API ausgeliefert: eigene Herkunft. Als lose Datei (file://): fest auf den
          // lokalen Standard. Mit "?api=http://host:port" überschreibbar.
          const API_BASE = (new URLSearchParams(location.search).get("api")
            || (/^https?:$/.test(location.protocol) ? location.origin : "http://localhost:8080"))
            .replace(/\/+$/, "");

          const tile = (label, value, cls = "") =>
            `<div class="tile ${cls}"><div class="label">${label}</div><div class="value">${value}</div></div>`;

          // Halbkreis-Tacho (Bogen + Zeiger, Zahl steht darunter): farbiger Wertbogen,
          // optionale Markierung bei tickValue.
          function gauge(value, max, stroke, tickValue) {
            const cx = 110, cy = 100, r = 92;
            const f = Math.max(0, Math.min(value / max, 1));
            const pt = (frac, rad) => {
              const t = Math.PI * (1 - frac);
              return [cx + rad * Math.cos(t), cy - rad * Math.sin(t)];
            };
            const arc = (frac, cls, w, extra) => {
              if (frac <= 0) return "";
              const [x1, y1] = pt(0, r), [x2, y2] = pt(frac, r);
              return `<path class="${cls}" d="M${x1.toFixed(1)} ${y1.toFixed(1)} ` +
                `A${r} ${r} 0 0 1 ${x2.toFixed(1)} ${y2.toFixed(1)}" fill="none" ` +
                `stroke-width="${w}" stroke-linecap="round" ${extra || ""}/>`;
            };
            const [mx, my] = pt(f, r);
            let mark = "";
            if (tickValue != null && tickValue > 0 && tickValue < max) {
              const [a1, b1] = pt(tickValue / max, r + 9), [a2, b2] = pt(tickValue / max, r - 9);
              mark = `<line class="tick" x1="${a1.toFixed(1)}" y1="${b1.toFixed(1)}" x2="${a2.toFixed(1)}" y2="${b2.toFixed(1)}" stroke-width="3"/>`;
            }
            return `<svg class="gauge" viewBox="0 0 220 116">
              ${arc(1, "track", 16)}
              ${arc(f, "val", 7, `stroke="${stroke}"`)}
              ${mark}
              <circle cx="${mx.toFixed(1)}" cy="${my.toFixed(1)}" r="8" fill="${stroke}" stroke="#14171c" stroke-width="3"/>
            </svg>`;
          }

          const rows = (obj) => {
            const entries = Object.entries(obj || {});
            if (!entries.length) return `<tr><td>–</td><td class="n">0</td></tr>`;
            return entries.map(([k, v]) => `<tr><td>${k}</td><td class="n">${v}</td></tr>`).join("");
          };

          async function refresh() {
            try {
              const r = await fetch(API_BASE + "/api/kpis", { cache: "no-store" });
              if (!r.ok) throw new Error(`HTTP ${r.status}`);
              const k = await r.json();

              document.getElementById("window").textContent =
                `Fenster: letzte ${k.windowMinutes} min`;
              document.getElementById("status").textContent =
                `aktualisiert ${new Date().toLocaleTimeString()}`;

              const lag = k.lagSeconds == null ? "–" : `${k.lagSeconds}s`;
              const lagCol = k.lagSeconds == null ? "#7a8494" : k.lagSeconds > 120 ? "#ff6b6b" : k.lagSeconds > 30 ? "#ffb454" : "#5ccb7e";
              document.getElementById("hero").innerHTML =
                `<div class="card"><div class="cap">Lag — Sekunden seit letztem Schreibvorgang</div>` +
                gauge(k.lagSeconds ?? 0, 180, lagCol, 30) +
                `<div class="pct" style="color:${lagCol}">${lag}</div></div>`;

              document.getElementById("tiles").innerHTML = [
                tile("Telegramme", k.count),
                tile("pro Minute", k.perMinute),
                tile("Fehler", k.errors, k.errors > 0 ? "bad" : ""),
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
