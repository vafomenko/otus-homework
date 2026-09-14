const { chromium } = require('../.tools/browser/node_modules/playwright');
const fs = require('node:fs/promises');
const path = require('node:path');
const root = path.resolve(__dirname, '..');
const base = process.env.KIBANA_URL || 'http://localhost:5601';
const output = path.join(root, 'docs/screenshots/logging');
(async () => {
  await fs.mkdir(output, { recursive: true });
  const checks = JSON.parse((await fs.readFile(path.join(root, 'docs/results/logging/http-checks.json'), 'utf8')).replace(/^\uFEFF/, ''));
  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  const errors = [];
  const httpErrors = new Set();
  try {
    const context = await browser.newContext({ viewport: { width: 1920, height: 1200 }, deviceScaleFactor: 1 });
    const page = await context.newPage();
    page.on('pageerror', error => errors.push(error.message));
    page.on('response', response => {
      if (response.status() >= 400) httpErrors.add(`${response.status()} ${response.url()}`);
    });
    async function capture(name, route, readyText) {
      await page.goto('about:blank');
      await page.goto(base + route, { waitUntil: 'domcontentloaded', timeout: 120000 });
      await page.getByText(readyText, { exact: false }).first().waitFor({ timeout: 120000 });
      if (name === 'log-levels') await page.locator('canvas').first().waitFor({ timeout: 60000 });
      await page.waitForTimeout(5000);
      const dismiss = page.getByRole('button', { name: 'Dismiss', exact: true });
      if (await dismiss.isVisible()) await dismiss.click();
      await page.screenshot({ path: path.join(output, name + '.png'), fullPage: true });
      console.log('Captured ' + name);
    }
    await capture('data-view', '/app/management/kibana/dataViews/dataView/homework-logs', 'fluent-bit-logs-*');
    const globalState = "?_g=(time:(from:now-1h,to:now))";
    await capture('discover-events', '/app/discover#/view/user-service-events' + globalState, 'user_created');
    await capture('discover-errors', '/app/discover#/view/user-service-errors' + globalState, 'ERROR');
    await capture('discover-migrations', '/app/discover#/view/user-service-migrations' + globalState, 'migration_completed');
    const state = "(columns:!(level,event,user_id,message,kubernetes.pod_name),index:homework-logs,query:(language:kuery,query:'service:%20%22user-service%22%20and%20user_id:%20" + checks.user_id + "'),sort:!(!('@timestamp',desc)))";
    await capture('discover-user', '/app/discover#/' + globalState + '&_a=' + state, 'user_created');
    await capture('log-levels', '/app/dashboards#/view/user-service-logs' + globalState, 'User service: log levels over time');
    await fs.writeFile(path.join(root, 'docs/results/logging/capture.json'), JSON.stringify({
      capturedAt: new Date().toISOString(), kibana: base, user_id: checks.user_id,
      browserErrors: errors, httpErrors: [...httpErrors]
    }, null, 2));
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
