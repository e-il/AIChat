const { chromium } = require('playwright');

(async () => {
  const browser = await chromium.launch();
  const context = await browser.newContext({ viewport: { width: 1600, height: 1000 } });
  const page = await context.newPage();
  
  let hasError = false;
  page.on('console', msg => {
    if (msg.type() === 'error') {
      console.log('ERROR:', msg.text());
      hasError = true;
    }
  });
  
  // Set auth and load
  await page.goto('http://localhost:5173', { waitUntil: 'domcontentloaded' });
  await page.evaluate(() => localStorage.setItem('aichat_auth_code', 'demo123'));
  await page.reload({ waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(2000);
  
  // Send a message
  const textarea = await page.$('textarea');
  if (textarea) {
    await textarea.fill('Hello');
    await textarea.press('Enter');
    console.log('Message sent...');
    await page.waitForTimeout(6000);
  }
  
  await page.screenshot({ path: 'screenshot-fluent.png' });
  console.log(hasError ? 'FAILED: Console errors found' : 'SUCCESS: No console errors');
  await browser.close();
})();
