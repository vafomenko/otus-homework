const path = require('node:path');
const fs = require('node:fs/promises');
const { chromium } = require('../.tools/browser/node_modules/playwright');
(async () => {
  const browser = await chromium.launch({headless:true,channel:process.env.PLAYWRIGHT_CHANNEL || 'msedge'});
  try {
    const page = await browser.newPage({deviceScaleFactor:1});
    for (const name of ['architecture','http-only','http-kafka','event-collaboration']) {
      const filename=path.join(__dirname,'../docs/streaming',name);
      await page.setContent(await fs.readFile(filename+'.svg','utf8'));
      await page.locator('svg').screenshot({path:filename+'.png'});
      console.log(name+'.png');
    }
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode=1; });
