// UI contract smoke test, NOT evidence of backend/SQL Server execution.
const assert = require('node:assert/strict');
const puppeteer = require('/tmp/uitest/node_modules/puppeteer');
const base = process.env.SMOKE_BASE || 'http://127.0.0.1:5173';
(async () => {
  const browser = await puppeteer.launch({headless: true, args: ['--no-sandbox', '--disable-dev-shm-usage', '--disable-gpu', '--no-zygote', '--single-process']});
  try {
    for (const [lang, width] of [['ar', 390], ['en', 1280]]) {
      const page = await browser.newPage();
      const errors = [];
      page.on('pageerror', e => errors.push(e.message));
      page.on('console', m => { if (m.type() === 'error') console.error(m.text()); });
      page.on('requestfailed', r => console.error('REQUEST FAILED', r.url(), r.failure()));
      await page.setViewport({width, height: 900});
      await page.evaluateOnNewDocument((lang) => {
        localStorage.setItem('dyehouse_token', 'ui-only-stub');
        localStorage.setItem('dyehouse_user', JSON.stringify({displayName:'Tester', roles:'admin'}));
        localStorage.setItem('dyehouse_lang', lang);
      }, lang);
      await page.setRequestInterception(true);
      page.on('request', req => {
        if (!new URL(req.url()).pathname.startsWith('/api/')) return req.continue();
        let body = [];
        if (/production-orders\/test-order(?:\?|$)/.test(req.url())) body = {
          id:'test-order',orderNumber:'JOB-PRINT',customerId:'customer',customerName:'Customer',customerCode:'C',itemId:'item',itemCode:'I',itemName:'Fabric',
          status:'InProduction',priority:'Normal',jobOrderType:'ClosedLine',orderDate:'2026-10-11',requestedQuantityKg:100,rawAllocations:[],
          stageExecutions:[{id:'exec',stageDefinitionId:'print',stageName:'Printing',stageCode:'PRINTING',status:'InProgress',requiresDrawingNumber:true,drawingNumber:'DR-UI-123',baselineKg:100}]
        };
        if (/production-stages/.test(req.url())) body = [{id:'finish',name:'Finishing',isActive:true}];
        if (/profitability/.test(req.url())) body = {servicePrices:[],revenue:0,actualCost:0,profit:0,legacyProcessingValue:0,marginPercent:null,finalQuantityKg:0,finalQuantityMeter:0};
        if (/\/cost(?:\?|$)/.test(req.url())) body = {totalCost:0,materialCost:0,processingValue:0,profit:0};
        if (/settings/.test(req.url())) body = {companyName:'DyeHouse ERP'};
        req.respond({status:200,contentType:'application/json',body:JSON.stringify(body)});
      });
      await page.goto(base + '/production-orders/test-order', {waitUntil:'networkidle0'});
      await page.waitForFunction(() => document.body.innerText.includes('DR-UI-123'), {timeout: 15000});
      const text = await page.evaluate(() => document.body.innerText);
      const direction = await page.evaluate(() => document.documentElement.dir);
      assert.equal(direction, lang === 'ar' ? 'rtl' : 'ltr');
      assert(text.includes(lang === 'ar' ? 'رقم الرسمة' : 'Drawing number'), 'drawing translation missing');
      assert(text.includes('DR-UI-123'), 'persisted drawing missing from order screen: ' + text.slice(0, 700) + ' errors=' + errors.join(';'));
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 2);
      assert(!overflow, `horizontal overflow at ${width}px`);
      assert.equal(errors.length, 0, errors.join('\n'));
      console.log(`PASS printing screen ${lang} ${width}px, stored drawing visible, no runtime errors/overflow`);
      await page.close();
    }
  } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode=1; });
