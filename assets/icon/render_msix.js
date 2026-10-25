// Renders the MSIX logo assets (tiles, taskbar/Start icons, Store logo) into src/ExchangeMail.App/Assets/.
// The icon is drawn from the SVGs at each pixel size, centered on a transparent canvas; 16–32 px use the
// simplified drawing. File names carry the MRT qualifiers (scale-*, targetsize-*) Windows picks from.
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const out = path.join(__dirname, '..', '..', 'src', 'ExchangeMail.App', 'Assets');
const large = fs.readFileSync(path.join(__dirname, 'postaci.svg'), 'utf8');
const small = fs.readFileSync(path.join(__dirname, 'postaci-small.svg'), 'utf8');

// [file, canvas width, canvas height, icon size]
const jobs = [];
for (const [q, s] of [['scale-100', 44], ['scale-125', 55], ['scale-150', 66], ['scale-200', 88], ['scale-400', 176]])
  jobs.push([`Square44x44Logo.${q}.png`, s, s, s]);
for (const s of [16, 24, 32, 48, 256])
  for (const alt of ['', '_altform-unplated', '_altform-lightunplated'])
    jobs.push([`Square44x44Logo.targetsize-${s}${alt}.png`, s, s, s]);
for (const [q, k] of [['scale-100', 1], ['scale-200', 2]]) {
  jobs.push([`Square150x150Logo.${q}.png`, 150 * k, 150 * k, 100 * k]);
  jobs.push([`Wide310x150Logo.${q}.png`, 310 * k, 150 * k, 100 * k]);
  jobs.push([`StoreLogo.${q}.png`, 50 * k, 50 * k, 50 * k]);
  jobs.push([`SplashScreen.${q}.png`, 620 * k, 300 * k, 160 * k]);
}

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage();
  for (const [file, w, h, icon] of jobs) {
    const svg = (icon <= 32 ? small : large).replace('width="256" height="256"', `width="${icon}" height="${icon}"`);
    await page.setViewportSize({ width: w, height: h });
    await page.setContent(`<html><body style="margin:0;background:transparent;width:${w}px;height:${h}px;display:flex;align-items:center;justify-content:center">${svg}</body></html>`);
    await page.screenshot({ path: path.join(out, file), omitBackground: true, clip: { x: 0, y: 0, width: w, height: h } });
  }
  await browser.close();
  console.log(`${jobs.length} files in ${out}`);
})();
