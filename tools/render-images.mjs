// Renders the UI artboards in docs/ui-proposal/*.dc.html to PNG images for the
// user manual (docs/manual/images/) and the README (docs/assets/).
//
// Dev-only tool, not used in CI. Usage (from the repository root):
//   npm install --prefix tools
//   node tools/render-images.mjs
//
// Every image is 1720 px wide so it stays sharp when shown at 860 px.
// The artboards contain placeholder data only (SHOP, OPS, Musterlab, Max Muster,
// azdo.example.local). Real screenshots replace the images later under the same names.

import { chromium } from 'playwright';
import { mkdir, readFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const artboardDir = join(repoRoot, 'docs', 'ui-proposal');
const manualDir = join(repoRoot, 'docs', 'manual', 'images');
const assetsDir = join(repoRoot, 'docs', 'assets');

const TARGET_WIDTH = 1720;

// Root element of an artboard: the first element child of <x-dc>.
const ARTBOARD_SELECTOR = 'x-dc > div';

/** @type {{ artboard: string, out: string, overlayToast?: boolean }[]} */
const jobs = [
  { artboard: 'Main.dc.html', out: join(manualDir, 'main.png') },
  { artboard: 'Verbindung.dc.html', out: join(manualDir, 'verbindung.png') },
  { artboard: 'Erkennung.dc.html', out: join(manualDir, 'erkennung.png') },
  { artboard: 'NeuEinlesen.dc.html', out: join(manualDir, 'neu-einlesen.png') },
  { artboard: 'Tools.dc.html', out: join(manualDir, 'tools.png') },
  { artboard: 'Clients.dc.html', out: join(manualDir, 'clients.png') },
  { artboard: 'Protokoll.dc.html', out: join(manualDir, 'protokoll.png') },
  { artboard: 'Tray.dc.html', out: join(manualDir, 'tray.png') },

  { artboard: 'Main.dc.html', out: join(assetsDir, 'hero.png') },
  { artboard: 'Erkennung.dc.html', out: join(assetsDir, 'feature-1-erkennung.png') },
  { artboard: 'Tools.dc.html', out: join(assetsDir, 'feature-2-rechte.png'), overlayToast: true },
  { artboard: 'Clients.dc.html', out: join(assetsDir, 'feature-3-clients.png') },
  { artboard: 'Protokoll.dc.html', out: join(assetsDir, 'feature-4-protokoll.png') },
];

// Extracts the approval toast (role="alertdialog") from the tray artboard.
async function loadToastHtml() {
  const html = await readFile(join(artboardDir, 'Tray.dc.html'), 'utf8');
  const start = html.indexOf('<div role="alertdialog"');
  if (start < 0) throw new Error('Approval toast not found in Tray.dc.html');
  // The toast has no nested <div role=...>; find its closing tag by counting divs.
  let depth = 0;
  const re = /<\/?div\b[^>]*>/g;
  re.lastIndex = start;
  let m;
  while ((m = re.exec(html))) {
    depth += m[0].startsWith('</') ? -1 : 1;
    if (depth === 0) return html.slice(start, re.lastIndex);
  }
  throw new Error('Approval toast in Tray.dc.html is not closed');
}

async function overlayToast(page, toastHtml) {
  await page.evaluate(
    ({ selector, toastHtml }) => {
      const root = document.querySelector(selector);
      root.style.position = 'relative';
      const style = document.createElement('style');
      // Button styles of the tray artboard, scoped so they do not clash with the page's .btn.
      style.textContent =
        '.toast-overlay .btn{flex:1 1 0;height:32px;border-radius:4px;border:1px solid #D1D1D1;' +
        "background:#FBFBFB;font:400 14px 'Segoe UI Variable','Segoe UI',sans-serif;color:#1B1B1B;padding:0}" +
        '.toast-overlay .btnp{background:#005FB8;border-color:#005FB8;color:#FFFFFF}';
      document.head.appendChild(style);
      const wrap = document.createElement('div');
      wrap.className = 'toast-overlay';
      wrap.innerHTML = toastHtml;
      const toast = wrap.firstElementChild;
      toast.style.right = '24px';
      toast.style.bottom = '56px';
      toast.style.zIndex = '10';
      root.appendChild(wrap);
    },
    { selector: ARTBOARD_SELECTOR, toastHtml },
  );
}

async function main() {
  await mkdir(manualDir, { recursive: true });
  await mkdir(assetsDir, { recursive: true });
  const toastHtml = await loadToastHtml();

  const browser = await chromium.launch();
  const failed = [];
  try {
    for (const job of jobs) {
      const file = join(artboardDir, job.artboard);
      try {
        // Measure the artboard at scale 1, then render at the scale that gives TARGET_WIDTH.
        const probe = await browser.newPage({ viewport: { width: 1400, height: 1000 } });
        await probe.goto(pathToFileURL(file).href);
        const box = await probe.locator(ARTBOARD_SELECTOR).first().boundingBox();
        await probe.close();
        if (!box) throw new Error(`no element for ${ARTBOARD_SELECTOR}`);

        const scale = TARGET_WIDTH / box.width;
        const page = await browser.newPage({
          viewport: { width: Math.ceil(box.width) + 40, height: Math.ceil(box.height) + 40 },
          deviceScaleFactor: scale,
        });
        // support.js (canvas runtime) is not in the repo; the artboards are static without it.
        await page.route('**/support.js', (route) => route.fulfill({ status: 200, body: '' }));
        await page.goto(pathToFileURL(file).href);
        await page.evaluate(() => document.fonts.ready);
        if (job.overlayToast) await overlayToast(page, toastHtml);
        await page.locator(ARTBOARD_SELECTOR).first().screenshot({ path: job.out, omitBackground: true });
        await page.close();
        console.log(`${job.artboard} -> ${job.out.slice(repoRoot.length + 1)}`);
      } catch (err) {
        failed.push(job.out);
        console.error(`FAILED ${job.artboard} -> ${job.out}: ${err.message}`);
      }
    }
  } finally {
    await browser.close();
  }
  if (failed.length > 0) {
    console.error(`${failed.length} image(s) could not be rendered.`);
    process.exit(1);
  }
}

await main();
