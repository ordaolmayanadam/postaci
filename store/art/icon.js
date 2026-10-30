// The app icon (assets/icon/postaci.svg) and the texts per listing language, for the Store image pages.
const ICON = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="12 12 232 232">
  <defs>
    <linearGradient id="bg" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#2B9AF3"/><stop offset="1" stop-color="#0B5CAD"/></linearGradient>
    <linearGradient id="paper" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#FFFFFF"/><stop offset="1" stop-color="#E6F0FA"/></linearGradient>
  </defs>
  <rect x="12" y="12" width="232" height="232" rx="56" fill="url(#bg)"/>
  <rect x="12.5" y="12.5" width="231" height="231" rx="55.5" fill="none" stroke="#FFFFFF" stroke-opacity=".35"/>
  <g stroke="#FFFFFF" stroke-linecap="round" stroke-width="11" opacity="0.92">
    <line x1="34" y1="104" x2="58" y2="104"/><line x1="26" y1="130" x2="58" y2="130"/><line x1="38" y1="156" x2="58" y2="156"/>
  </g>
  <rect x="76" y="80" width="140" height="100" rx="14" fill="url(#paper)"/>
  <path d="M84,90 L146.0,135.0 L208,90" fill="none" stroke="#0B5CAD" stroke-width="10" stroke-linecap="round" stroke-linejoin="round"/>
</svg>`;

const LANG = new URLSearchParams(location.search).get('lang') === 'en' ? 'en' : 'tr';
const TEXT = {
  tr: { tagline: 'Şirket içi Exchange için posta, takvim ve kişiler' },
  en: { tagline: 'Mail, calendar and contacts for on-premises Exchange' },
}[LANG];

/** Screenshots are 2854×1785 with a 66 px title bar, which is cut off. */
const SHOT = { width: 2854, height: 1785, titleBar: 66 };

function placeIcon(id, size) {
  const el = document.getElementById(id);
  el.innerHTML = ICON;
  el.firstElementChild.setAttribute('width', size);
  el.firstElementChild.setAttribute('height', size);
}

/** A window showing ../screenshots/<lang>/<name>.png, <width> px wide, at (left, top). */
function placeWindow(name, left, top, width, extraStyle = '') {
  const scale = width / SHOT.width;
  const div = document.createElement('div');
  div.className = 'window';
  div.style.cssText = `left:${left}px;top:${top}px;width:${width}px;height:${Math.round((SHOT.height - SHOT.titleBar) * scale)}px;${extraStyle}`;
  div.innerHTML = `<img src="../screenshots/${LANG}/${name}.png" style="top:${-Math.round(SHOT.titleBar * scale)}px">`;
  document.body.appendChild(div);
}
