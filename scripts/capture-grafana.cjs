// Prerequisite: npm install --prefix .tools/browser playwright@1.55.0
// Uses installed Google Chrome; captures actual Grafana without editing pixels.
const { chromium } = require('../.tools/browser/node_modules/playwright');
const fs = require('node:fs/promises');
const path = require('node:path');
const root = path.resolve(__dirname, '..');

(async () => {
  const label = process.argv[2] || 'stress';
  if (!/^[a-z0-9-]+$/.test(label)) throw new Error('Invalid screenshot label');
  const metadata = JSON.parse((await fs.readFile(path.join(root, 'docs/results/k6-run.json'), 'utf8')).replace(/^\uFEFF/, ''));
  const from = new Date(metadata.startedAt).getTime();
  const to = metadata.finishedAt ? new Date(metadata.finishedAt).getTime() : Date.now();
  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  try {
    const context = await browser.newContext({ viewport: { width: 1920, height: 2500 }, deviceScaleFactor: 1 });
    const base = process.env.GRAFANA_URL || 'http://localhost:3005';
    const login = await context.request.post(`${base}/login`, { data: {
      user: process.env.GRAFANA_USER || 'admin', password: process.env.GRAFANA_PASSWORD || 'homework5' } });
    if (!login.ok()) throw new Error(`Grafana login failed: ${login.status()}`);
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.goto(`${base}/d/blt-users-overview?orgId=1&from=${from}&to=${to}&timezone=Europe%2FMoscow&refresh=`, { waitUntil: 'networkidle' });
    await page.getByText('DB · deadlocks / 1m', { exact: true }).waitFor({ timeout: 30000 });
    await page.waitForTimeout(5000);
    await fs.mkdir(path.join(root, 'docs/screenshots'), { recursive: true });
    await page.screenshot({ path: path.join(root, `docs/screenshots/dashboard-${label}.png`), fullPage: true });
    const rules = await context.request.get(`${base}/api/prometheus/grafana/api/v1/rules`);
    if (!rules.ok()) throw new Error(`Cannot fetch alert rules: ${rules.status()}`);
    await fs.writeFile(path.join(root, `docs/results/alerts-${label}.json`), JSON.stringify(await rules.json(), null, 2));
    await page.setViewportSize({ width: 1920, height: 1080 });
    await page.goto(`${base}/alerting/list?view=state`, { waitUntil: 'networkidle' });
    await page.waitForTimeout(3000);
    await page.screenshot({ path: path.join(root, `docs/screenshots/alerts-${label}.png`), fullPage: true });
    if (label === 'recovery') {
      // Enlarge ingress latency so all four legend entries, including max,
      // are readable without scrolling the compact dashboard legend.
      await page.goto(`${base}/d-solo/blt-users-overview?orgId=1&panelId=14&from=${from}&to=${to}&timezone=Europe%2FMoscow`, { waitUntil: 'networkidle' });
      await page.getByText('max (histogram estimate)', { exact: true }).waitFor({ timeout: 30000 });
      await page.waitForTimeout(2000);
      await page.screenshot({ path: path.join(root, 'docs/screenshots/ingress-latency.png'), fullPage: true });
    }
    await fs.writeFile(path.join(root, `docs/results/capture-${label}.json`), JSON.stringify({
      from, to, capturedAt: new Date().toISOString(), grafana: base, browserErrors: errors,
      dashboard: 'blt-users-overview' }, null, 2));
    console.log(`Saved actual dashboard and alert screenshots: ${label}`);
  } finally {
    await browser.close();
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
