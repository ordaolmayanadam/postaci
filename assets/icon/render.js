// Renders each SVG at the requested pixel sizes with a transparent background.
const { chromium } = require('playwright');
const fs = require('fs');
(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage();
  const jobs = [
    ['postaci.svg', [48, 64, 128, 256]], ['postaci-small.svg', [16, 20, 24, 32]],
    ['postaci-unread.svg', [48, 64, 128, 256]], ['postaci-unread-small.svg', [16, 20, 24, 32]],
  ];
  for (const [file, sizes] of jobs) {
    const svg = fs.readFileSync(file, 'utf8');
    for (const size of sizes) {
      await page.setViewportSize({ width: size, height: size });
      await page.setContent(`<html><body style="margin:0;background:transparent">${svg.replace('width="256" height="256"', `width="${size}" height="${size}"`)}</body></html>`);
      const base = file.replace('.svg', '').replace('-small', '');
      await page.screenshot({ path: `${base}-${size}.png`, omitBackground: true, clip: { x: 0, y: 0, width: size, height: size } });
    }
  }
  await browser.close();
})();
