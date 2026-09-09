// Baut die gemeinsame Navigationsleiste clientseitig und markiert die aktive Seite.
// Löst die frühere serverseitige Injektion (DashboardNav) für aufgetrennte Seiten ab.
(function () {
  const links = [
    ['/', 'KPIs'],
    ['/auslastung', 'Auslastung'],
    ['/verlauf', 'Verlauf'],
    ['/kontur', 'Kontur'],
    ['/rbg', 'RBG'],
    ['/wand', 'Wand'],
  ];

  // Saubere URLs (/auslastung) wie auch die losen Dateien (/auslastung.html) treffen.
  const path = (location.pathname.replace(/\.html$/, '') || '/');

  const nav = document.createElement('nav');
  nav.className = 'kcc-nav';
  nav.innerHTML = links
    .map(([href, label]) => `<a href="${href}"${href === path ? ' class="on"' : ''}>${label}</a>`)
    .join('');

  document.body.insertBefore(nav, document.body.firstChild);
})();
