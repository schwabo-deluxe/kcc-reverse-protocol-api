// Baut die gemeinsame Navigationsleiste clientseitig und markiert die aktive Seite.
// Löst die frühere serverseitige Injektion (DashboardNav) für aufgetrennte Seiten ab.
(function () {
  const links = [
    ['/', 'KPIs'],
    ['/auslastung', 'Auslastung'],
    ['/verlauf', 'Verlauf'],
    ['/kontur', 'Kontur'],
    ['/rbg', 'RBG'],
    ['/dashboard', 'Dashboard'],
  ];

  // Saubere URLs (/auslastung) wie auch die losen Dateien (/auslastung.html) treffen.
  let path = (location.pathname.replace(/\.html$/, '') || '/');
  if (path === '/wand') path = '/dashboard';   // alter Pfad, weiterhin ausgeliefert

  const nav = document.createElement('nav');
  nav.className = 'kcc-nav';
  nav.innerHTML = links
    .map(([href, label]) => `<a href="${href}"${href === path ? ' class="on"' : ''}>${label}</a>`)
    .join('') + '<span class="kcc-ver" id="kcc-ver"></span>';

  document.body.insertBefore(nav, document.body.firstChild);

  // Version rechts in der Leiste — vom Server, damit die losen Dateien sie nicht mitschleppen.
  fetch('/api/version', { cache: 'no-store' })
    .then(r => r.ok ? r.json() : null)
    .then(d => {
      const v = d && d.version;
      if (!v) return;
      const el = document.getElementById('kcc-ver');
      el.textContent = /^\d/.test(v) ? 'v' + v : v;
      const built = d.buildDate
        ? ' · gebaut ' + new Date(d.buildDate).toLocaleString('de-DE', { dateStyle: 'medium', timeStyle: 'short' })
        : '';
      el.title = 'kcc ' + v + built;
    })
    .catch(() => {});
})();
